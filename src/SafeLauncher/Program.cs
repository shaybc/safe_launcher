using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// SafeLauncher is a controlled Windows launcher prototype.
//
// The app launches one build-time-approved destination process as a restricted
// local Windows user. Before the destination starts, it runs an admin-authored
// pre-launch batch that was embedded into the EXE at build time. Runtime users
// cannot choose the executable, arguments, working directory, environment, or
// pre-launch actions.
internal static class Program
{
    private const int LOGON_WITH_PROFILE = 0x00000001;
    private const int CREATE_NEW_CONSOLE = 0x00000010;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_HIDE = 0;
    private const int LOGON32_LOGON_INTERACTIVE = 2;
    private const int LOGON32_PROVIDER_DEFAULT = 0;
    private const int NERR_Success = 0;
    private const int FILTER_NORMAL_ACCOUNT = 0x0002;
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;

    private const string ProvisionCredentialArgument = "--provision-credential";

    // Entry point for the launcher. Normal use accepts no runtime arguments;
    // the only supported argument is the management path for provisioning the
    // stored restricted-user credential.
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return HandleCommandLine(args);
        }

        HideConsoleWindowIfConfigured();

        using SplashScreenHandle splashScreen = SplashScreenHandle.StartIfConfigured();

        string domain = LauncherConfig.Domain;
        string userName = LauncherConfig.UserName;
        string workingDirectory = LauncherConfig.WorkingDirectory;
        string applicationName = ResolveExecutable(LauncherConfig.DestinationExecutable);
        string commandLine = BuildCommandLine(applicationName, LauncherConfig.Arguments);

        if (!RunPreflightChecks(domain, userName, workingDirectory, applicationName))
        {
            return 1;
        }

        string? password = ReadStoredPassword(LauncherConfig.CredentialTargetName);
        if (password is null)
        {
            Console.Error.WriteLine("Stored credential was not found.");
            Console.Error.WriteLine($"Provision it first with: SafeLauncher.exe {ProvisionCredentialArgument}");
            return 1;
        }

        Console.WriteLine("Starting restricted launcher...");
        Console.WriteLine($"User: {domain}\\{userName}");
        Console.WriteLine($"Working directory: {workingDirectory}");
        Console.WriteLine($"Destination: {applicationName}");
        Console.WriteLine();

        IntPtr userToken = IntPtr.Zero;
        IntPtr loadedProfile = IntPtr.Zero;

        try
        {
            if (!LogonUser(
                userName,
                domain,
                password,
                LOGON32_LOGON_INTERACTIVE,
                LOGON32_PROVIDER_DEFAULT,
                out userToken))
            {
                int error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"Failed to log on restricted user. Win32 error: {error}");
                Console.Error.WriteLine(new Win32Exception(error).Message);
                return error;
            }

            loadedProfile = LoadRestrictedUserProfile(userToken, userName);

            using PreparedEnvironment preLaunchEnvironment = CreatePreparedEnvironment(userToken, workingDirectory);
            if (!RunPreLaunchBatch(domain, userName, password, userToken, workingDirectory, preLaunchEnvironment))
            {
                return 1;
            }

            // Recreate the environment after the batch completes so persistent
            // setx changes written by the batch are visible to the destination.
            using PreparedEnvironment destinationEnvironment = CreatePreparedEnvironment(userToken, workingDirectory);
            bool launched = LaunchDestination(
                domain,
                userName,
                password,
                workingDirectory,
                applicationName,
                commandLine,
                destinationEnvironment.Pointer);

            if (!launched)
            {
                int error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"Failed to launch process. Win32 error: {error}");
                Console.Error.WriteLine(new Win32Exception(error).Message);
                return error;
            }

            Console.WriteLine("Destination launched as restricted user.");
            return 0;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"Launch failed: {exception.Message}");
            return exception.NativeErrorCode != 0 ? exception.NativeErrorCode : 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Launch failed: {exception.Message}");
            return 1;
        }
        finally
        {
            password = string.Empty;

            if (userToken != IntPtr.Zero)
            {
                if (loadedProfile != IntPtr.Zero)
                {
                    UnloadUserProfile(userToken, loadedProfile);
                }

                CloseHandle(userToken);
            }
        }
    }

    // Hides the launcher's own console window when the selected package asks
    // for quiet startup. This does not affect credential provisioning because
    // provisioning is handled before this method is called.
    private static void HideConsoleWindowIfConfigured()
    {
        if (!LauncherConfig.HideLauncherConsole)
        {
            return;
        }

        IntPtr consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, SW_HIDE);
        }
    }

    // Handles the small management surface for Phase 2. The launch path remains
    // argument-free and cannot be used to override app-specific settings.
    private static int HandleCommandLine(string[] args)
    {
        if (args.Length == 1 &&
            string.Equals(args[0], ProvisionCredentialArgument, StringComparison.OrdinalIgnoreCase))
        {
            return ProvisionStoredCredential();
        }

        Console.Error.WriteLine("Unsupported argument.");
        Console.Error.WriteLine($"Allowed management command: {ProvisionCredentialArgument}");
        return 2;
    }

    // Prompts an IT/admin operator for the restricted user's password and stores
    // it as a Windows Generic Credential under the configured target name.
    private static int ProvisionStoredCredential()
    {
        string storedUserName = $@"{LauncherConfig.Domain}\{LauncherConfig.UserName}";

        Console.WriteLine("Provisioning restricted launcher credential...");
        Console.WriteLine($"Credential target: {LauncherConfig.CredentialTargetName}");
        Console.WriteLine($"Stored user: {storedUserName}");
        Console.WriteLine();

        string password = ReadPassword($"Password for {storedUserName}: ");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Password cannot be empty.");
            return 1;
        }

        bool success = WriteStoredPassword(
            LauncherConfig.CredentialTargetName,
            storedUserName,
            password);

        password = string.Empty;

        if (!success)
        {
            return 1;
        }

        Console.WriteLine("Credential provisioned successfully.");
        return 0;
    }

    // Runs only generic startup checks. App-specific checks belong in the
    // embedded pre-launch batch or in external IT validation, not in Program.cs.
    private static bool RunPreflightChecks(
        string domain,
        string userName,
        string workingDirectory,
        string applicationName)
    {
        Console.WriteLine("Running launcher prerequisite checks...");
        Console.WriteLine();

        bool success = true;

        success &= Check(Environment.OSVersion.Platform == PlatformID.Win32NT, "Windows OS", "This launcher requires Windows 10 or Windows 11.");
        success &= Check(Environment.OSVersion.Version.Major >= 10, "Windows version", "Windows 10 or Windows 11 is required.");
        success &= Check(Directory.Exists(workingDirectory), "Destination working directory", $"{workingDirectory} does not exist.");
        success &= Check(File.Exists(applicationName), "Destination executable", $"{applicationName} was not found.");
        success &= Check(
            Path.IsPathRooted(LauncherConfig.DestinationExecutable) ||
            !string.Equals(applicationName, LauncherConfig.DestinationExecutable, StringComparison.OrdinalIgnoreCase),
            "Resolved destination executable",
            $"{LauncherConfig.DestinationExecutable} could not be resolved to a full path.");
        success &= Check(CredentialExists(LauncherConfig.CredentialTargetName), "Stored credential", $"Credential Manager target {LauncherConfig.CredentialTargetName} was not found.");

        bool userExists = LocalUserExists(userName);
        success &= Check(userExists, "Restricted local user", $@"{domain}\{userName} does not exist.");

        if (userExists)
        {
            success &= Check(
                !LocalUserIsInGroup(userName, "Administrators"),
                "Restricted user is not admin",
                $@".\{userName} is a member of the local Administrators group.");
        }

        WriteCheck(
            DotNetSdk8OrNewerInstalled(),
            "Compile prerequisite: .NET SDK 8+",
            "dotnet SDK 8.0 or newer was not found. The published EXE can still run, but compiling from source requires it.");

        Console.WriteLine();
        Console.WriteLine("Manual security validation still required:");
        Console.WriteLine("- Windows policy blocks unapproved shells, browsers, service tools, and network utilities.");
        Console.WriteLine("- Approved binaries are signed or hash-pinned.");
        Console.WriteLine("- The restricted user cannot access developer profile folders or unrelated project folders.");
        Console.WriteLine("- The destination app's own security policy is configured and tested.");
        Console.WriteLine();

        if (success)
        {
            Console.WriteLine("Prerequisite checks passed.");
            Console.WriteLine();
            return true;
        }

        Console.Error.WriteLine("Prerequisite checks failed. Fix the items above before launching.");
        return false;
    }

    // Reads the restricted-user password from Windows Credential Manager.
    // Missing credentials are treated as a setup problem, not a runtime prompt.
    private static string? ReadStoredPassword(string targetName)
    {
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out IntPtr credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ERROR_NOT_FOUND)
            {
                Console.Error.WriteLine($"Failed to read stored credential. Win32 error: {error}");
                Console.Error.WriteLine(new Win32Exception(error).Message);
            }

            return null;
        }

        try
        {
            CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                return null;
            }

            byte[] passwordBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, passwordBytes.Length);

            try
            {
                return Encoding.Unicode.GetString(passwordBytes);
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    // Checks only whether the configured Credential Manager target can be read.
    // The full password decode happens later after all preflight checks pass.
    private static bool CredentialExists(string targetName)
    {
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out IntPtr credentialPointer))
        {
            return false;
        }

        CredFree(credentialPointer);
        return true;
    }

    // Writes the restricted-user password to Windows Credential Manager as a
    // machine-persisted Generic Credential for the current Windows account.
    private static bool WriteStoredPassword(string targetName, string storedUserName, string password)
    {
        byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
        GCHandle passwordHandle = GCHandle.Alloc(passwordBytes, GCHandleType.Pinned);

        try
        {
            CREDENTIAL credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetName,
                CredentialBlobSize = passwordBytes.Length,
                CredentialBlob = passwordHandle.AddrOfPinnedObject(),
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = storedUserName
            };

            if (CredWrite(ref credential, 0))
            {
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"Failed to write stored credential. Win32 error: {error}");
            Console.Error.WriteLine(new Win32Exception(error).Message);
            return false;
        }
        finally
        {
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
            passwordHandle.Free();
        }
    }

    // Loads the restricted user's profile hive so environment creation resolves
    // USERPROFILE, APPDATA, and user-level setx values for the correct account.
    private static IntPtr LoadRestrictedUserProfile(IntPtr userToken, string userName)
    {
        PROFILEINFO profileInfo = new PROFILEINFO();
        profileInfo.dwSize = Marshal.SizeOf<PROFILEINFO>();
        profileInfo.lpUserName = userName;

        if (LoadUserProfile(userToken, ref profileInfo))
        {
            return profileInfo.hProfile;
        }

        int error = Marshal.GetLastWin32Error();
        Console.Error.WriteLine($"Failed to load restricted user profile. Win32 error: {error}");
        Console.Error.WriteLine(new Win32Exception(error).Message);
        return IntPtr.Zero;
    }

    // Creates a pinned, normalized environment block for the restricted user.
    // The caller disposes the returned wrapper after process creation.
    private static PreparedEnvironment CreatePreparedEnvironment(IntPtr userToken, string workingDirectory)
    {
        if (!CreateEnvironmentBlock(out IntPtr rawEnvironment, userToken, bInherit: false))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Failed to create restricted user environment block.");
        }

        try
        {
            Dictionary<string, string> variables = ReadEnvironmentBlock(rawEnvironment);
            NormalizeEnvironment(variables, workingDirectory);
            return new PreparedEnvironment(variables, BuildEnvironmentBytes(variables));
        }
        finally
        {
            DestroyEnvironmentBlock(rawEnvironment);
        }
    }

    // Normalizes cwd/profile/temp variables so child processes start in the
    // approved workspace and use the restricted user's profile directories.
    private static void NormalizeEnvironment(Dictionary<string, string> variables, string workingDirectory)
    {
        string userProfile = variables.TryGetValue("USERPROFILE", out string? profile)
            ? profile
            : $@"C:\Users\{LauncherConfig.UserName}";

        string localAppData = Path.Combine(userProfile, "AppData", "Local");
        string tempDirectory = Path.Combine(localAppData, "Temp");

        variables["CD"] = workingDirectory;
        variables["PWD"] = workingDirectory;
        variables["INIT_CWD"] = workingDirectory;
        variables["USERPROFILE"] = userProfile;
        variables["HOMEDRIVE"] = Path.GetPathRoot(userProfile)?.TrimEnd('\\') ?? "C:";
        variables["HOMEPATH"] = userProfile.Length > 2 ? userProfile[2..] : $@"\Users\{LauncherConfig.UserName}";
        variables["APPDATA"] = Path.Combine(userProfile, "AppData", "Roaming");
        variables["LOCALAPPDATA"] = localAppData;
        variables["TEMP"] = tempDirectory;
        variables["TMP"] = tempDirectory;
    }

    // Runs the build-time embedded pre-launch batch as the restricted user. The
    // destination app is not started unless the batch exits successfully.
    private static bool RunPreLaunchBatch(
        string domain,
        string userName,
        string password,
        IntPtr userToken,
        string workingDirectory,
        PreparedEnvironment environment)
    {
        if (LauncherConfig.PreLaunchBatchBytes.Length == 0)
        {
            Console.WriteLine("No pre-launch batch embedded.");
            return true;
        }

        string tempDirectory = GetTempDirectory(environment.Variables);
        string batchPath = Path.Combine(tempDirectory, $"SafeLauncher-prelaunch-{Guid.NewGuid():N}.cmd");

        try
        {
            WriteTemporaryBatchAsRestrictedUser(userToken, tempDirectory, batchPath, LauncherConfig.PreLaunchBatchBytes);
            Console.WriteLine("Running pre-launch actions...");

            int exitCode = RunHiddenBatchProcess(
                domain,
                userName,
                password,
                workingDirectory,
                batchPath,
                environment.Pointer,
                LauncherConfig.PreLaunchTimeoutSeconds);

            if (exitCode != 0)
            {
                Console.Error.WriteLine($"Pre-launch actions failed with exit code {exitCode}.");
                return false;
            }

            Console.WriteLine("Pre-launch actions completed.");
            return true;
        }
        catch (TimeoutException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return false;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Pre-launch actions failed: {exception.Message}");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Pre-launch actions failed: {exception.Message}");
            return false;
        }
        finally
        {
            DeleteTemporaryBatchAsRestrictedUser(userToken, batchPath);
        }
    }

    // Writes the embedded batch into the restricted user's temp folder while
    // impersonating that user so filesystem ownership and ACL checks are honest.
    private static void WriteTemporaryBatchAsRestrictedUser(IntPtr userToken, string tempDirectory, string batchPath, byte[] content)
    {
        RunAsRestrictedUser(userToken, () =>
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllBytes(batchPath, content);
            File.SetAttributes(batchPath, FileAttributes.Hidden | FileAttributes.Temporary);
        });
    }

    // Deletes the temporary batch while impersonating the restricted user. If
    // cleanup cannot impersonate, it still attempts a normal best-effort delete.
    private static void DeleteTemporaryBatchAsRestrictedUser(IntPtr userToken, string batchPath)
    {
        try
        {
            RunAsRestrictedUser(userToken, () =>
            {
                if (File.Exists(batchPath))
                {
                    File.SetAttributes(batchPath, FileAttributes.Normal);
                    File.Delete(batchPath);
                }
            });
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(batchPath))
                {
                    File.SetAttributes(batchPath, FileAttributes.Normal);
                    File.Delete(batchPath);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    // Runs a delegate under the restricted user's token for narrow file-system
    // work that needs to happen inside that user's profile.
    private static void RunAsRestrictedUser(IntPtr userToken, Action action)
    {
        if (!ImpersonateLoggedOnUser(userToken))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Failed to impersonate restricted user.");
        }

        try
        {
            action();
        }
        finally
        {
            RevertToSelf();
        }
    }

    // Starts cmd.exe hidden, waits for the embedded batch to finish, and returns
    // its exit code. A timeout terminates the batch and fails the launch.
    private static int RunHiddenBatchProcess(
        string domain,
        string userName,
        string password,
        string workingDirectory,
        string batchPath,
        IntPtr environment,
        int timeoutSeconds)
    {
        string cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string commandLine = BuildCommandLine(cmdPath, new[] { "/D", "/Q", "/C", batchPath });

        STARTUPINFO startupInfo = new STARTUPINFO();
        startupInfo.cb = Marshal.SizeOf<STARTUPINFO>();
        startupInfo.dwFlags = STARTF_USESHOWWINDOW;
        startupInfo.wShowWindow = SW_HIDE;

        bool started = CreateProcessWithLogonW(
            userName,
            domain,
            password,
            LOGON_WITH_PROFILE,
            cmdPath,
            commandLine,
            CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
            environment,
            workingDirectory,
            ref startupInfo,
            out PROCESS_INFORMATION processInfo);

        if (!started)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Failed to start pre-launch batch.");
        }

        try
        {
            uint waitMilliseconds = checked((uint)Math.Max(1, timeoutSeconds) * 1000);
            uint waitResult = WaitForSingleObject(processInfo.hProcess, waitMilliseconds);

            if (waitResult == WAIT_TIMEOUT)
            {
                TerminateProcess(processInfo.hProcess, 1);
                throw new TimeoutException($"Pre-launch actions timed out after {timeoutSeconds} seconds.");
            }

            if (waitResult != WAIT_OBJECT_0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed while waiting for pre-launch batch.");
            }

            if (!GetExitCodeProcess(processInfo.hProcess, out int exitCode))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Failed to read pre-launch batch exit code.");
            }

            return exitCode;
        }
        finally
        {
            CloseHandle(processInfo.hProcess);
            CloseHandle(processInfo.hThread);
        }
    }

    // Launches the configured destination in a visible console or window. The
    // destination command line and working directory are fixed at build time.
    private static bool LaunchDestination(
        string domain,
        string userName,
        string password,
        string workingDirectory,
        string applicationName,
        string commandLine,
        IntPtr environment)
    {
        STARTUPINFO startupInfo = new STARTUPINFO();
        startupInfo.cb = Marshal.SizeOf<STARTUPINFO>();

        bool success = CreateProcessWithLogonW(
            userName,
            domain,
            password,
            LOGON_WITH_PROFILE,
            applicationName,
            commandLine,
            CREATE_NEW_CONSOLE | CREATE_UNICODE_ENVIRONMENT,
            environment,
            workingDirectory,
            ref startupInfo,
            out PROCESS_INFORMATION processInfo);

        if (success)
        {
            CloseHandle(processInfo.hProcess);
            CloseHandle(processInfo.hThread);
        }

        return success;
    }

    // Resolves either an absolute executable path from config or an executable
    // name that should be found through the launcher account's PATH.
    private static string ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable))
        {
            return executable;
        }

        return ResolveExecutableFromPath(executable);
    }

    // Searches PATH for an executable name and returns the first full path found.
    // If no match is found, the original name is returned for clear preflight output.
    private static string ResolveExecutableFromPath(string executableName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return executableName;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return executableName;
    }

    // Builds the exact command-line string passed to CreateProcessWithLogonW.
    // Windows process creation receives one command-line string, so each token
    // from the build-time allowlist is quoted carefully before concatenation.
    private static string BuildCommandLine(string applicationName, IReadOnlyList<string> arguments)
    {
        StringBuilder commandLine = new StringBuilder();
        commandLine.Append(QuoteCommandLineArgument(applicationName));

        foreach (string argument in arguments)
        {
            commandLine.Append(' ');
            commandLine.Append(QuoteCommandLineArgument(argument));
        }

        return commandLine.ToString();
    }

    // Quotes one command-line argument using Windows command-line parsing rules.
    // This handles whitespace, quotes, and trailing backslashes safely.
    private static string QuoteCommandLineArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuotes = argument.IndexOfAny([' ', '\t', '"']) >= 0;
        if (!needsQuotes)
        {
            return argument;
        }

        StringBuilder result = new StringBuilder();
        result.Append('"');

        int backslashCount = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashCount * 2 + 1);
                result.Append('"');
                backslashCount = 0;
                continue;
            }

            result.Append('\\', backslashCount);
            backslashCount = 0;
            result.Append(character);
        }

        result.Append('\\', backslashCount * 2);
        result.Append('"');

        return result.ToString();
    }

    // Reads a Win32 double-NUL-terminated environment block into a dictionary so
    // selected variables can be overridden before process creation.
    private static Dictionary<string, string> ReadEnvironmentBlock(IntPtr environment)
    {
        Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        IntPtr current = environment;
        while (true)
        {
            string? entry = Marshal.PtrToStringUni(current);
            if (string.IsNullOrEmpty(entry))
            {
                break;
            }

            int separator = entry.IndexOf('=');
            if (separator > 0)
            {
                variables[entry[..separator]] = entry[(separator + 1)..];
            }

            current = IntPtr.Add(current, (entry.Length + 1) * sizeof(char));
        }

        return variables;
    }

    // Converts normalized environment variables back into the UTF-16, double-NUL
    // terminated form required by CreateProcessWithLogonW.
    private static byte[] BuildEnvironmentBytes(Dictionary<string, string> variables)
    {
        StringBuilder builder = new StringBuilder();
        foreach (KeyValuePair<string, string> variable in variables)
        {
            builder.Append(variable.Key);
            builder.Append('=');
            builder.Append(variable.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        return Encoding.Unicode.GetBytes(builder.ToString());
    }

    // Gets the restricted user's temp directory from the prepared environment,
    // falling back to LOCALAPPDATA\Temp if TEMP is unexpectedly missing.
    private static string GetTempDirectory(Dictionary<string, string> variables)
    {
        if (variables.TryGetValue("TEMP", out string? temp) && !string.IsNullOrWhiteSpace(temp))
        {
            return temp;
        }

        if (variables.TryGetValue("LOCALAPPDATA", out string? localAppData) && !string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "Temp");
        }

        return Path.Combine($@"C:\Users\{LauncherConfig.UserName}", "AppData", "Local", "Temp");
    }

    // Writes one required-check result and returns the condition so checks can be
    // combined with boolean accumulation.
    private static bool Check(bool condition, string name, string failureMessage)
    {
        WriteCheck(condition, name, failureMessage);
        return condition;
    }

    // Prints an OK/FAIL line for a preflight check.
    private static void WriteCheck(bool condition, string name, string failureMessage)
    {
        if (condition)
        {
            Console.WriteLine($"[OK] {name}");
            return;
        }

        Console.Error.WriteLine($"[FAIL] {name}: {failureMessage}");
    }

    // Checks whether the .NET 8+ SDK is available for developers building from
    // source. This is not required by the self-contained published EXE.
    private static bool DotNetSdk8OrNewerInstalled()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (!process.HasExited || process.ExitCode != 0)
            {
                return false;
            }

            foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                string versionText = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (Version.TryParse(versionText, out Version? version) && version.Major >= 8)
                {
                    return true;
                }
            }
        }
        catch (Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    // Uses NetUserEnum to check whether a local normal account exists.
    private static bool LocalUserExists(string userName)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            int result = NetUserEnum(
                null,
                0,
                FILTER_NORMAL_ACCOUNT,
                out buffer,
                -1,
                out int entriesRead,
                out _,
                IntPtr.Zero);

            if (result != NERR_Success || entriesRead == 0)
            {
                return false;
            }

            int structSize = Marshal.SizeOf<USER_INFO_0>();
            for (int i = 0; i < entriesRead; i++)
            {
                IntPtr item = IntPtr.Add(buffer, i * structSize);
                USER_INFO_0 userInfo = Marshal.PtrToStructure<USER_INFO_0>(item);
                if (string.Equals(userInfo.usri0_name, userName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NetApiBufferFree(buffer);
            }
        }
    }

    // Uses NetUserGetLocalGroups to check whether the restricted user belongs to
    // a specific local group such as Administrators.
    private static bool LocalUserIsInGroup(string userName, string groupName)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            int result = NetUserGetLocalGroups(
                null,
                userName,
                0,
                0,
                out buffer,
                -1,
                out int entriesRead,
                out _);

            if (result != NERR_Success || entriesRead == 0)
            {
                return false;
            }

            int structSize = Marshal.SizeOf<LOCALGROUP_USERS_INFO_0>();
            for (int i = 0; i < entriesRead; i++)
            {
                IntPtr item = IntPtr.Add(buffer, i * structSize);
                LOCALGROUP_USERS_INFO_0 groupInfo = Marshal.PtrToStructure<LOCALGROUP_USERS_INFO_0>(item);
                if (string.Equals(groupInfo.lgrui0_name, groupName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NetApiBufferFree(buffer);
            }
        }
    }

    // Reads a password from the console without echoing the characters.
    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);

        StringBuilder password = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }

        return password.ToString();
    }

    // Shows an optional build-time embedded PNG while launcher startup work is
    // happening. Disposal keeps the image visible for the configured minimum
    // duration, then closes it from the UI thread.
    private sealed class SplashScreenHandle : IDisposable
    {
        private readonly DateTime startedAt;
        private readonly int minimumMilliseconds;
        private readonly ManualResetEventSlim formReady = new ManualResetEventSlim(false);
        private readonly Thread? uiThread;
        private Form? form;
        private bool disposed;

        private SplashScreenHandle(byte[] imageBytes, int minimumSeconds)
        {
            startedAt = DateTime.UtcNow;
            minimumMilliseconds = checked(Math.Max(0, minimumSeconds) * 1000);
            uiThread = new Thread(() => RunSplashWindow(imageBytes))
            {
                IsBackground = true,
                Name = "SafeLauncher Splash"
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            formReady.Wait(TimeSpan.FromSeconds(3));
        }

        private SplashScreenHandle()
        {
            startedAt = DateTime.UtcNow;
        }

        public static SplashScreenHandle StartIfConfigured()
        {
            if (!LauncherConfig.SplashEnabled || LauncherConfig.SplashImageBytes.Length == 0)
            {
                return new SplashScreenHandle();
            }

            return new SplashScreenHandle(LauncherConfig.SplashImageBytes, LauncherConfig.SplashMinimumSeconds);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            int elapsedMilliseconds = (int)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds);
            int remainingMilliseconds = Math.Max(0, minimumMilliseconds - elapsedMilliseconds);
            if (remainingMilliseconds > 0)
            {
                Thread.Sleep(remainingMilliseconds);
            }

            if (form is not null && !form.IsDisposed)
            {
                try
                {
                    form.BeginInvoke(new Action(() => form.Close()));
                    uiThread?.Join(TimeSpan.FromSeconds(2));
                }
                catch (InvalidOperationException)
                {
                }
            }

            formReady.Dispose();
        }

        private void RunSplashWindow(byte[] imageBytes)
        {
            try
            {
                Application.EnableVisualStyles();
                using MemoryStream stream = new MemoryStream(imageBytes);
                using Image image = Image.FromStream(stream);

                Size splashSize = FitImageSize(image.Size, new Size(720, 480));
                Form splashForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.CenterScreen,
                    ShowInTaskbar = false,
                    TopMost = true,
                    BackColor = Color.Black,
                    ClientSize = splashSize
                };

                PictureBox picture = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = (Image)image.Clone(),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Black
                };

                splashForm.Controls.Add(picture);
                splashForm.Shown += (_, _) => formReady.Set();
                splashForm.FormClosed += (_, _) =>
                {
                    picture.Image?.Dispose();
                    picture.Image = null;
                };

                form = splashForm;
                Application.Run(splashForm);
            }
            catch (Exception)
            {
                formReady.Set();
            }
        }

        private static Size FitImageSize(Size imageSize, Size maximumSize)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0)
            {
                return new Size(420, 260);
            }

            double ratio = Math.Min(
                (double)maximumSize.Width / imageSize.Width,
                (double)maximumSize.Height / imageSize.Height);

            ratio = Math.Min(1.0, ratio);
            return new Size(
                Math.Max(240, (int)Math.Round(imageSize.Width * ratio)),
                Math.Max(160, (int)Math.Round(imageSize.Height * ratio)));
        }
    }

    // Owns a pinned managed environment block for one CreateProcessWithLogonW
    // call and releases the pin when disposed.
    private sealed class PreparedEnvironment : IDisposable
    {
        private GCHandle handle;

        public PreparedEnvironment(Dictionary<string, string> variables, byte[] bytes)
        {
            Variables = variables;
            handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            Pointer = handle.AddrOfPinnedObject();
        }

        public Dictionary<string, string> Variables { get; }

        public IntPtr Pointer { get; private set; }

        public void Dispose()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            Pointer = IntPtr.Zero;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithLogonW(
        string lpUsername,
        string lpDomain,
        string lpPassword,
        int dwLogonFlags,
        string? lpApplicationName,
        string lpCommandLine,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, int uExitCode);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(
        string target,
        int type,
        int reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(
        ref CREDENTIAL userCredential,
        int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername,
        string lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ImpersonateLoggedOnUser(IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertToSelf();

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr lpEnvironment,
        IntPtr hToken,
        bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LoadUserProfile(IntPtr hToken, ref PROFILEINFO lpProfileInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool UnloadUserProfile(IntPtr hToken, IntPtr hProfile);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserEnum(
        string? servername,
        int level,
        int filter,
        out IntPtr bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries,
        IntPtr resume_handle);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetLocalGroups(
        string? servername,
        string username,
        int level,
        int flags,
        out IntPtr bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_0
    {
        public string usri0_name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOCALGROUP_USERS_INFO_0
    {
        public string lgrui0_name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROFILEINFO
    {
        public int dwSize;
        public int dwFlags;
        public string? lpUserName;
        public string? lpProfilePath;
        public string? lpDefaultPath;
        public string? lpServerName;
        public string? lpPolicyPath;
        public IntPtr hProfile;
    }
}
