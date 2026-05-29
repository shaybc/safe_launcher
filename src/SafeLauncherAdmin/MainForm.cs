using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

public sealed class MainForm : Form
{
    private static readonly System.Drawing.Color BackgroundColor = System.Drawing.Color.FromArgb(249, 250, 252);
    private static readonly System.Drawing.Color CardColor = System.Drawing.Color.White;
    private static readonly System.Drawing.Color SidebarColor = System.Drawing.Color.FromArgb(244, 246, 250);
    private static readonly System.Drawing.Color SidebarBorderColor = System.Drawing.Color.FromArgb(220, 226, 236);
    private static readonly System.Drawing.Color ForegroundColor = System.Drawing.Color.FromArgb(15, 23, 42);
    private static readonly System.Drawing.Color MutedForegroundColor = System.Drawing.Color.FromArgb(100, 116, 139);
    private static readonly System.Drawing.Color LabelColor = System.Drawing.Color.FromArgb(71, 85, 105);
    private static readonly System.Drawing.Color BorderColor = System.Drawing.Color.FromArgb(220, 228, 240);
    private static readonly System.Drawing.Color SectionHeaderColor = System.Drawing.Color.FromArgb(100, 116, 139);
    private static readonly System.Drawing.Color AccentColor = System.Drawing.Color.FromArgb(37, 99, 235);
    private static readonly System.Drawing.Color AccentHoverColor = System.Drawing.Color.FromArgb(59, 130, 246);
    private static readonly System.Drawing.Color AccentLightColor = System.Drawing.Color.FromArgb(239, 246, 255);
    private static readonly System.Drawing.Font UiFont = new("Segoe UI", 10.25F);
    private static readonly System.Drawing.Font LabelFont = new("Segoe UI", 9F);
    private static readonly System.Drawing.Font SectionFont = new("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
    private static readonly System.Drawing.Font MonoFont = new("Consolas", 10.5F);

    private readonly string repositoryRoot;
    private readonly string packagesRoot;
    private readonly string rootConfigPath;
    private readonly string buildScriptPath;
    private readonly string provisionScriptPath;

    private readonly ListBox packageList = new();
    private readonly TextBox packageNameText = new();
    private readonly TextBox packageNotesText = new();
    private readonly TextBox launcherExeText = new();
    private readonly TextBox launcherIconFileText = new();
    private readonly TextBox domainText = new();
    private readonly TextBox userNameText = new();
    private readonly TextBox credentialTargetText = new();
    private readonly TextBox destinationExeText = new();
    private readonly TextBox workingDirectoryText = new();
    private readonly TextBox argumentsText = new();
    private readonly TextBox preLaunchFileText = new();
    private readonly NumericUpDown timeoutSecondsInput = new();
    private readonly ModernCheckBox splashEnabledCheck = new();
    private readonly TextBox splashImageFileText = new();
    private readonly NumericUpDown splashMinimumSecondsInput = new();
    private readonly RichTextBox preLaunchContentText = new();
    private readonly FindReplaceBar findReplaceBar = new();
    private readonly LineNumberGutter lineNumberGutter = new();
    private readonly TextBox logText = new();
    private readonly RoundedButton saveButton = new();
    private readonly RoundedButton buildButton = new();
    private readonly TableLayoutPanel buildStatusPanel = new();
    private readonly Label buildStatusIcon = new();
    private readonly Label buildStatusLabel = new();
    private readonly RoundedButton provisionButton = new();
    private readonly RoundedButton newButton = new();
    private readonly RoundedButton refreshButton = new();
    private readonly RoundedButton clearOutputButton = new();
    private readonly PickerButton browseIconButton = new();
    private readonly PickerButton browseSplashButton = new();
    private readonly ContextMenuStrip packageContextMenu = new();
    private readonly ToolTip helpToolTip = new();
    private readonly System.Windows.Forms.Timer batchHighlightTimer = new() { Interval = 250 };

    private string? selectedPackageFolder;
    private bool hasUnsavedChanges;
    private bool isLoadingPackage;
    private bool isBuildRunning;
    private bool showBuildWarning;
    private bool isHighlightingBatch;

    private const int WM_SETREDRAW = 0x000B;
    private const int WM_USER = 0x0400;
    private const int EM_GETSCROLLPOS = WM_USER + 221;
    private const int EM_SETSCROLLPOS = WM_USER + 222;
    private const int EM_SETUNDOLIMIT = WM_USER + 146;
    private const int UndoStackDepth  = 1000;

    public MainForm()
    {
        repositoryRoot = FindRepositoryRoot();
        packagesRoot = Path.Combine(repositoryRoot, "config-packages");
        rootConfigPath = Path.Combine(repositoryRoot, "src", "SafeLauncher", "launcher-config.json");
        buildScriptPath = Path.Combine(repositoryRoot, "src", "SafeLauncher", "tools", "build.bat");
        provisionScriptPath = Path.Combine(repositoryRoot, "src", "SafeLauncher", "tools", "provision-credential.bat");

        Text = "SafeLauncher Admin";
        MinimumSize = new System.Drawing.Size(1120, 820);
        Size = new System.Drawing.Size(1160, 840);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackgroundColor;
        Font = UiFont;

        ConfigureHelpToolTip();
        BuildLayout();
        WireEvents();
        LoadPackages();
    }

    private void BuildLayout()
    {
        SplitContainer mainSplit = new()
        {
            Dock = DockStyle.Fill,
            Panel1MinSize = 260,
            FixedPanel = FixedPanel.Panel1
        };
        Shown += (_, _) => ApplySidebarWidth(mainSplit);
        mainSplit.BackColor = BackgroundColor;

        // Sidebar with right border
        SidebarPanel left = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(14, 16, 14, 14),
            BackColor = SidebarColor
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        TableLayoutPanel packageHeader = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = SidebarColor
        };
        packageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        packageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

        Label packageLabel = new()
        {
            Text = "PACKAGES",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = AccentColor,
            Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold)
        };

        ConfigureIconButton(refreshButton, "↻");
        SetHelp(refreshButton, "Reload the config-packages list. If there are unsaved edits, you will be asked before they are discarded.");
        packageHeader.Controls.Add(packageLabel, 0, 0);
        packageHeader.Controls.Add(refreshButton, 1, 0);

        packageList.Dock = DockStyle.Fill;
        packageList.BorderStyle = BorderStyle.None;
        packageList.BackColor = SidebarColor;
        packageList.ForeColor = ForegroundColor;
        packageList.Font = new System.Drawing.Font("Segoe UI", 10F);
        packageList.DrawMode = DrawMode.OwnerDrawFixed;
        packageList.ItemHeight = 52;
        packageList.DrawItem += DrawPackageListItem;
        packageList.MouseDown += SelectPackageListItemUnderMouse;
        ConfigurePackageContextMenu();
        packageList.ContextMenuStrip = packageContextMenu;
        SetHelp(packageList, "Select a config package. Right-click a package to open, duplicate, or delete it.");

        TableLayoutPanel packageButtons = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = SidebarColor,
            Padding = new Padding(0, 8, 0, 0)
        };
        packageButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ConfigureActionButton(newButton, "+ New Package", 0);
        SetHelp(newButton, "Create a new config package folder with a starter launcher config and pre-launch batch.");
        newButton.Dock = DockStyle.Fill;
        packageButtons.Controls.Add(newButton, 0, 0);

        left.Controls.Add(packageHeader, 0, 0);
        left.Controls.Add(packageList, 0, 1);
        left.Controls.Add(packageButtons, 0, 2);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
            Font = UiFont,
            Padding = new System.Drawing.Point(16, 6)
        };
        TabPage detailsTab = new("Package");
        TabPage batchTab = new("Pre-Launch Batch");
        TabPage outputTab = new("Output");
        Panel outputPanel = BuildOutputPanel();

        detailsTab.Controls.Add(BuildDetailsPanel());

        // ── Batch tab layout ─────────────────────────────────────────────
        // Structure (top→bottom): FindReplaceBar | EditorToolbar | [Gutter + Editor]
        Panel batchPanel = new() { Dock = DockStyle.Fill, BackColor = CardColor };

        // Editor toolbar: word wrap toggle
        Panel editorToolbar = BuildEditorToolbar();

        // Gutter + editor side by side
        Panel editorArea = new() { Dock = DockStyle.Fill, BackColor = CardColor };
        lineNumberGutter.Attach(preLaunchContentText);
        editorArea.Controls.Add(preLaunchContentText); // Fill
        editorArea.Controls.Add(lineNumberGutter);     // Left dock

        findReplaceBar.Attach(preLaunchContentText);

        // DockStyle.Top stacks in reverse add order
        batchPanel.Controls.Add(editorArea);
        batchPanel.Controls.Add(editorToolbar);
        batchPanel.Controls.Add(findReplaceBar);
        batchTab.Controls.Add(batchPanel);

        outputTab.Controls.Add(outputPanel);

        preLaunchContentText.Dock = DockStyle.Fill;
        preLaunchContentText.ScrollBars = RichTextBoxScrollBars.Both;
        preLaunchContentText.AcceptsTab = true;
        preLaunchContentText.Font = MonoFont;
        preLaunchContentText.BorderStyle = BorderStyle.None;
        preLaunchContentText.BackColor = CardColor;
        preLaunchContentText.ForeColor = ForegroundColor;
        preLaunchContentText.WordWrap = false;
        preLaunchContentText.DetectUrls = false;
        preLaunchContentText.ContextMenuStrip = BuildEditorContextMenu();
        preLaunchContentText.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { preLaunchContentText.Undo(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Y) { preLaunchContentText.Redo(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.G) { ShowGoToLine(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.D) { DuplicateLine(); e.Handled = true; }
        };
        SetHelp(preLaunchContentText, "Edit the pre-launch batch. This script is embedded at build time and runs as the restricted user before the destination app starts.");

        logText.Dock = DockStyle.Fill;
        logText.Multiline = true;
        logText.ReadOnly = true;
        logText.ScrollBars = ScrollBars.Both;
        logText.Font = MonoFont;
        logText.BorderStyle = BorderStyle.FixedSingle;
        logText.BackColor = System.Drawing.Color.Black;
        logText.ForeColor = System.Drawing.Color.White;
        SetHelp(logText, "Build and provisioning output appears here, like a command prompt log.");
        SetHelp(clearOutputButton, "Clear the output console. This only clears the visible log in the admin UI.");

        tabs.TabPages.Add(detailsTab);
        tabs.TabPages.Add(batchTab);
        tabs.TabPages.Add(outputTab);

        mainSplit.Panel1.Controls.Add(left);
        mainSplit.Panel2.Controls.Add(tabs);
        Controls.Add(mainSplit);
    }

    private void ConfigureHelpToolTip()
    {
        helpToolTip.OwnerDraw = true;
        helpToolTip.BackColor = CardColor;
        helpToolTip.ForeColor = System.Drawing.Color.Black;
        helpToolTip.InitialDelay = 450;
        helpToolTip.ReshowDelay = 120;
        helpToolTip.AutoPopDelay = 9000;
        helpToolTip.Popup += (_, eventArgs) =>
        {
            using System.Drawing.Font font = new("Segoe UI", 9.5F);
            string text = helpToolTip.GetToolTip(eventArgs.AssociatedControl) ?? string.Empty;
            System.Drawing.Size textSize = TextRenderer.MeasureText(text, font, new System.Drawing.Size(360, 0), TextFormatFlags.WordBreak);
            eventArgs.ToolTipSize = new System.Drawing.Size(textSize.Width + 24, textSize.Height + 18);
        };
        helpToolTip.Draw += (_, eventArgs) =>
        {
            eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            eventArgs.Graphics.Clear(CardColor);
            System.Drawing.Rectangle bounds = new(0, 0, eventArgs.Bounds.Width - 1, eventArgs.Bounds.Height - 1);
            using System.Drawing.Drawing2D.GraphicsPath path = CreateRoundedRectangle(bounds, 10);
            using System.Drawing.SolidBrush backgroundBrush = new(CardColor);
            using System.Drawing.Pen borderPen = new(BorderColor);
            eventArgs.Graphics.FillPath(backgroundBrush, path);
            eventArgs.Graphics.DrawPath(borderPen, path);

            System.Drawing.Rectangle textBounds = new(12, 9, eventArgs.Bounds.Width - 24, eventArgs.Bounds.Height - 18);
            using System.Drawing.Font tooltipFont = new("Segoe UI", 9.5F);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                eventArgs.ToolTipText,
                tooltipFont,
                textBounds,
                System.Drawing.Color.Black,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
    }

    private Panel BuildOutputPanel()
    {
        Panel outputPanel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.Black,
            Padding = new Padding(6)
        };

        ConfigureConsoleIconButton(clearOutputButton, "⌫");
        ConfigureConsoleIconButton(clearOutputButton, "X");
        outputPanel.Controls.Add(logText);
        outputPanel.Controls.Add(clearOutputButton);
        outputPanel.Resize += (_, _) => PositionClearOutputButton(outputPanel);
        PositionClearOutputButton(outputPanel);
        clearOutputButton.BringToFront();
        return outputPanel;
    }

    private void PositionClearOutputButton(Control outputPanel)
    {
        clearOutputButton.Location = new System.Drawing.Point(
            Math.Max(6, outputPanel.ClientSize.Width - clearOutputButton.Width - 34),
            10);
    }

    private void SetHelp(Control control, string text)
    {
        helpToolTip.SetToolTip(control, text);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(System.Drawing.Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        System.Drawing.Drawing2D.GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ApplySidebarWidth(SplitContainer splitContainer)
    {
        try
        {
            int targetWidth = 260;
            int maximumWidth = splitContainer.Width - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;
            if (maximumWidth > splitContainer.Panel1MinSize)
            {
                splitContainer.SplitterDistance = Math.Min(targetWidth, maximumWidth);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool wordWrapEnabled = false;

    private Panel BuildEditorToolbar()
    {
        Panel bar = new()
        {
            Dock      = DockStyle.Top,
            Height    = 34,
            BackColor = System.Drawing.Color.FromArgb(244, 246, 250)
        };

        bar.Paint += (_, pe) =>
        {
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 226, 236), 1f);
            pe.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width - 1, bar.Height - 1);
        };

        // Word wrap toggle button
        RoundedButton wrapBtn = new()
        {
            Text             = "↵  Word Wrap",
            Width            = 108,
            Height           = 26,
            Left             = 8,
            Top              = 4,
            BorderRadius     = 6,
            Font             = new System.Drawing.Font("Segoe UI", 8.5F),
            BackColor        = System.Drawing.Color.White,
            ForeColor        = System.Drawing.Color.FromArgb(100, 116, 139),
            BorderColor      = System.Drawing.Color.FromArgb(220, 228, 240),
            HoverBackColor   = System.Drawing.Color.FromArgb(239, 246, 255),
            PressedBackColor = System.Drawing.Color.FromArgb(219, 234, 254),
            Cursor           = Cursors.Hand
        };
        SetHelp(wrapBtn, "Toggle word wrap on/off (long lines vs. horizontal scroll).");

        wrapBtn.Click += (_, _) =>
        {
            wordWrapEnabled = !wordWrapEnabled;
            preLaunchContentText.WordWrap = wordWrapEnabled;
            preLaunchContentText.ScrollBars = wordWrapEnabled
                ? RichTextBoxScrollBars.Vertical
                : RichTextBoxScrollBars.Both;
            lineNumberGutter.WordWrapMode = wordWrapEnabled;
            wrapBtn.BackColor    = wordWrapEnabled ? System.Drawing.Color.FromArgb(219, 234, 254) : System.Drawing.Color.White;
            wrapBtn.ForeColor    = wordWrapEnabled ? System.Drawing.Color.FromArgb(37, 99, 235)   : System.Drawing.Color.FromArgb(100, 116, 139);
            wrapBtn.BorderColor  = wordWrapEnabled ? System.Drawing.Color.FromArgb(147, 197, 253) : System.Drawing.Color.FromArgb(220, 228, 240);
            lineNumberGutter.Invalidate();
        };

        // Go to line button
        RoundedButton gotoBtn = new()
        {
            Text             = "⤷  Go to Line",
            Width            = 108,
            Height           = 26,
            Left             = 122,
            Top              = 4,
            BorderRadius     = 6,
            Font             = new System.Drawing.Font("Segoe UI", 8.5F),
            BackColor        = System.Drawing.Color.White,
            ForeColor        = System.Drawing.Color.FromArgb(100, 116, 139),
            BorderColor      = System.Drawing.Color.FromArgb(220, 228, 240),
            HoverBackColor   = System.Drawing.Color.FromArgb(239, 246, 255),
            PressedBackColor = System.Drawing.Color.FromArgb(219, 234, 254),
            Cursor           = Cursors.Hand
        };
        SetHelp(gotoBtn, "Jump to a specific line number (Ctrl+G).");
        gotoBtn.Click += (_, _) => ShowGoToLine();

        bar.Controls.Add(wrapBtn);
        bar.Controls.Add(gotoBtn);
        return bar;
    }

    private void ShowGoToLine()
    {
        int lineCount   = preLaunchContentText.Lines.Length;
        int currentLine = preLaunchContentText.GetLineFromCharIndex(preLaunchContentText.SelectionStart);
        if (lineCount == 0) return;

        using GoToLineDialog dlg = new(currentLine, lineCount);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        int targetLine = Math.Clamp(dlg.SelectedLine - 1, 0, lineCount - 1);
        int charIdx    = preLaunchContentText.GetFirstCharIndexFromLine(targetLine);
        if (charIdx < 0) return;

        preLaunchContentText.Select(charIdx, 0);
        preLaunchContentText.ScrollToCaret();
        preLaunchContentText.Focus();
    }

    private void DuplicateLine()
    {
        int selStart = preLaunchContentText.SelectionStart;
        int selLen   = preLaunchContentText.SelectionLength;

        if (selLen > 0)
        {
            // Duplicate selection
            string selected = preLaunchContentText.SelectedText;
            preLaunchContentText.Select(selStart + selLen, 0);
            preLaunchContentText.SelectedText = selected;
            preLaunchContentText.Select(selStart + selLen, selLen);
        }
        else
        {
            // Duplicate current line
            int line    = preLaunchContentText.GetLineFromCharIndex(selStart);
            int lineStart = preLaunchContentText.GetFirstCharIndexFromLine(line);
            int nextLine  = line + 1;
            int lineEnd   = nextLine < preLaunchContentText.Lines.Length
                ? preLaunchContentText.GetFirstCharIndexFromLine(nextLine)
                : preLaunchContentText.TextLength;

            string lineText = preLaunchContentText.Text.Substring(lineStart, lineEnd - lineStart);
            // Ensure it ends with a newline
            if (!lineText.EndsWith("\n", StringComparison.Ordinal))
                lineText += "\n";

            preLaunchContentText.Select(lineEnd, 0);
            preLaunchContentText.SelectedText = lineText;

            // Place cursor on the duplicated line at same column
            int col = selStart - lineStart;
            preLaunchContentText.Select(lineEnd + col, 0);
            preLaunchContentText.ScrollToCaret();
        }
    }

    private ContextMenuStrip BuildEditorContextMenu()
    {
        ContextMenuStrip menu = new()
        {
            Renderer  = new ModernMenuRenderer(),
            BackColor = System.Drawing.Color.White,
            Font      = new System.Drawing.Font("Segoe UI", 9.5F),
            Padding   = new Padding(4, 4, 4, 4)
        };

        ToolStripMenuItem undo      = CreateEditorMenuItem("Undo",      CreateEditorIcon(EditorIconKind.Undo));
        ToolStripMenuItem redo      = CreateEditorMenuItem("Redo",      CreateEditorIcon(EditorIconKind.Redo));
        ToolStripMenuItem cut       = CreateEditorMenuItem("Cut",       CreateEditorIcon(EditorIconKind.Cut));
        ToolStripMenuItem copy      = CreateEditorMenuItem("Copy",      CreateEditorIcon(EditorIconKind.Copy));
        ToolStripMenuItem paste     = CreateEditorMenuItem("Paste",     CreateEditorIcon(EditorIconKind.Paste));
        ToolStripMenuItem delete    = CreateEditorMenuItem("Delete",    CreateEditorIcon(EditorIconKind.Delete));
        ToolStripMenuItem selectAll = CreateEditorMenuItem("Select All",CreateEditorIcon(EditorIconKind.SelectAll));

        undo     .Click += (_, _) => preLaunchContentText.Undo();
        redo     .Click += (_, _) => preLaunchContentText.Redo();
        cut      .Click += (_, _) => preLaunchContentText.Cut();
        copy     .Click += (_, _) => preLaunchContentText.Copy();
        paste    .Click += (_, _) => preLaunchContentText.Paste();
        delete   .Click += (_, _) => preLaunchContentText.SelectedText = string.Empty;
        selectAll.Click += (_, _) => preLaunchContentText.SelectAll();

        menu.Items.Add(undo);
        menu.Items.Add(redo);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(delete);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(selectAll);

        menu.Opening += (_, _) =>
        {
            bool hasSelection = preLaunchContentText.SelectionLength > 0;
            bool hasClipboard = Clipboard.ContainsText();
            undo     .Enabled = preLaunchContentText.CanUndo;
            redo     .Enabled = preLaunchContentText.CanRedo;
            cut      .Enabled = hasSelection;
            copy     .Enabled = hasSelection;
            delete   .Enabled = hasSelection;
            paste    .Enabled = hasClipboard;
        };

        return menu;
    }

    private static ToolStripMenuItem CreateEditorMenuItem(string text, System.Drawing.Image icon)
    {
        return new ToolStripMenuItem(text)
        {
            Image         = icon,
            ImageScaling  = ToolStripItemImageScaling.None,
            Padding       = new Padding(4, 5, 16, 5)
        };
    }

    private enum EditorIconKind { Undo, Redo, Cut, Copy, Paste, Delete, SelectAll }

    private static System.Drawing.Bitmap CreateEditorIcon(EditorIconKind kind)
    {
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(71, 85, 105));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap   = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

        switch (kind)
        {
            case EditorIconKind.Undo:
                // Counter-clockwise arc arrow
                g.DrawArc(pen, 3f, 4f, 8f, 8f, 90f, 240f);
                g.DrawLine(pen, 3f, 8f, 3f, 4f);
                g.DrawLine(pen, 3f, 4f, 7f, 4f);
                break;

            case EditorIconKind.Redo:
                // Clockwise arc arrow
                g.DrawArc(pen, 5f, 4f, 8f, 8f, 270f, 240f);
                g.DrawLine(pen, 13f, 8f, 13f, 4f);
                g.DrawLine(pen, 13f, 4f, 9f, 4f);
                break;

            case EditorIconKind.Cut:
                // Scissors: two circles + crossing lines
                g.DrawEllipse(pen, 2.5f, 9.5f, 4f, 4f);
                g.DrawEllipse(pen, 9.5f, 9.5f, 4f, 4f);
                g.DrawLine(pen, 4.5f,  2.0f, 11.5f, 9.5f);
                g.DrawLine(pen, 11.5f, 2.0f, 4.5f,  9.5f);
                break;

            case EditorIconKind.Copy:
                // Two overlapping rectangles
                using (var p2 = new System.Drawing.Pen(System.Drawing.Color.FromArgb(180, 255, 255, 255), 1.4f)
                       { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                    g.DrawRectangle(p2, 2.5f, 4.5f, 6.5f, 7.5f);
                g.DrawRectangle(pen, 5.5f, 2.5f, 6.5f, 7.5f);
                break;

            case EditorIconKind.Paste:
                // Clipboard with page
                g.DrawRectangle(pen, 3f, 4f, 9f, 9f);
                g.DrawLine(pen, 6f, 2f, 10f, 2f);
                g.DrawLine(pen, 6f, 2f, 6f,  4f);
                g.DrawLine(pen, 10f,2f, 10f, 4f);
                g.DrawLine(pen, 5f, 7f, 11f, 7f);
                g.DrawLine(pen, 5f, 9f, 9f,  9f);
                break;

            case EditorIconKind.Delete:
                // X mark
                pen.Width = 2f;
                g.DrawLine(pen, 4f, 4f, 12f, 12f);
                g.DrawLine(pen, 12f,4f, 4f,  12f);
                break;

            case EditorIconKind.SelectAll:
                // Dashed selection rectangle with arrow
                float[] dash = { 2f, 1.5f };
                using (var dp = new System.Drawing.Pen(System.Drawing.Color.White, 1.4f) { DashPattern = dash })
                    g.DrawRectangle(dp, 2.5f, 3.5f, 9f, 8f);
                // Small cursor arrow inside
                g.FillPolygon(brush, new[]
                {
                    new System.Drawing.PointF(10f, 10f),
                    new System.Drawing.PointF(13f, 13f),
                    new System.Drawing.PointF(10f, 13f)
                });
                break;
        }

        return bmp;
    }

    private void ConfigurePackageContextMenu()
    {
        packageContextMenu.Items.Clear();
        packageContextMenu.ImageScalingSize = new System.Drawing.Size(16, 16);
        packageContextMenu.Renderer = new ModernMenuRenderer();
        packageContextMenu.BackColor = System.Drawing.Color.White;
        packageContextMenu.ShowImageMargin = true;
        packageContextMenu.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        packageContextMenu.Padding = new Padding(4, 4, 4, 4);

        packageContextMenu.Items.Add(CreateMenuItem("Build", CreateBuildIcon(), () => _ = BuildSelectedPackageAsync()));
        packageContextMenu.Items.Add(CreateMenuItem("Provision Credential", CreateCredentialIcon(), () => _ = ProvisionSelectedPackageAsync()));
        packageContextMenu.Items.Add(CreateModernSeparator());
        packageContextMenu.Items.Add(CreateMenuItem("Launch EXE", CreateLaunchIcon(), LaunchBuiltExecutable));
        packageContextMenu.Items.Add(CreateMenuItem("Show in Explorer", CreateShowExeIcon(), ShowBuiltExecutableFolder));
        packageContextMenu.Items.Add(CreateModernSeparator());
        packageContextMenu.Items.Add(CreateMenuItem("Open Folder", CreateFolderIcon(), OpenSelectedPackageFolder));
        packageContextMenu.Items.Add(CreateMenuItem("Rename", CreateRenameIcon(), RenameSelectedPackage));
        packageContextMenu.Items.Add(CreateMenuItem("Duplicate", CreateDuplicateIcon(), DuplicateSelectedPackage));
        packageContextMenu.Items.Add(CreateModernSeparator());
        packageContextMenu.Items.Add(CreateMenuItem("Export Package…", CreateExportIcon(), ExportSelectedPackage));
        packageContextMenu.Items.Add(CreateMenuItem("Import Package…", CreateImportIcon(), ImportPackage));
        packageContextMenu.Items.Add(CreateModernSeparator());
        packageContextMenu.Items.Add(CreateMenuItem("Delete", CreateDeleteIcon(), DeleteSelectedPackage));
        packageContextMenu.Opening += (_, eventArgs) => eventArgs.Cancel = packageList.SelectedItem is null;
    }

    private static ToolStripSeparator CreateModernSeparator()
    {
        return new ToolStripSeparator();
    }

    private static ToolStripMenuItem CreateMenuItem(string text, System.Drawing.Image icon, Action onClick)
    {
        ToolStripMenuItem item = new(text)
        {
            Image = icon,
            ImageScaling = ToolStripItemImageScaling.None,
            Padding = new Padding(4, 5, 16, 5),
            ImageTransparentColor = System.Drawing.Color.Magenta
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    // ── Icon helpers ─────────────────────────────────────────────────────────

    private static System.Drawing.Bitmap CreateBuildIcon()
    {
        // Play triangle inside a rounded square — blue
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(37, 99, 235));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        g.FillPolygon(brush, new[]
        {
            new System.Drawing.PointF(5.5F, 4.0F),
            new System.Drawing.PointF(12.5F, 8.0F),
            new System.Drawing.PointF(5.5F, 12.0F)
        });
        return bmp;
    }

    private static System.Drawing.Bitmap CreateCredentialIcon()
    {
        // Key icon — amber
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(217, 119, 6));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        // Key ring
        g.DrawEllipse(pen, 3.0F, 4.5F, 5.5F, 5.5F);
        // Key shaft
        g.DrawLine(pen, 8.2F, 8.0F, 13.0F, 8.0F);
        g.DrawLine(pen, 11.5F, 8.0F, 11.5F, 10.0F);
        g.DrawLine(pen, 13.0F, 8.0F, 13.0F, 10.0F);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateLaunchIcon()
    {
        // Rocket/arrow up-right — blue
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(37, 99, 235));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.6F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        // Arrow shaft
        g.DrawLine(pen, 5.0F, 11.0F, 11.0F, 5.0F);
        // Arrow head
        g.DrawLine(pen, 7.0F, 5.0F, 11.0F, 5.0F);
        g.DrawLine(pen, 11.0F, 5.0F, 11.0F, 9.0F);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateShowExeIcon()
    {
        // Magnifier — slate
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(71, 85, 105));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.6F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        g.DrawEllipse(pen, 3.0F, 3.0F, 7.5F, 7.5F);
        g.DrawLine(pen, 9.8F, 9.8F, 13.0F, 13.0F);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateFolderIcon()
    {
        // Folder — slate
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(71, 85, 105));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        // Folder body
        g.DrawLines(pen, new[]
        {
            new System.Drawing.PointF(2.5F, 12.5F),
            new System.Drawing.PointF(2.5F, 7.0F),
            new System.Drawing.PointF(6.2F, 7.0F),
            new System.Drawing.PointF(7.5F, 5.2F),
            new System.Drawing.PointF(13.5F, 5.2F),
            new System.Drawing.PointF(13.5F, 12.5F),
            new System.Drawing.PointF(2.5F, 12.5F)
        });
        return bmp;
    }

    private static System.Drawing.Bitmap CreateDuplicateIcon()
    {
        // Two overlapping squares — green
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(22, 163, 74));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var penBack = new System.Drawing.Pen(System.Drawing.Color.FromArgb(180, 255, 255, 255), 1.4F)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        using var penFront = new System.Drawing.Pen(System.Drawing.Color.White, 1.4F)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        // Back square
        g.DrawRectangle(penBack, 3.0F, 5.0F, 7.0F, 7.0F);
        // Front square
        g.DrawRectangle(penFront, 6.0F, 3.0F, 7.0F, 7.0F);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateDeleteIcon()
    {
        // Trash — red
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(220, 38, 38));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        // Lid
        g.DrawLine(pen, 3.5F, 5.5F, 12.5F, 5.5F);
        g.DrawLine(pen, 6.5F, 5.5F, 6.5F, 3.5F);
        g.DrawLine(pen, 9.5F, 5.5F, 9.5F, 3.5F);
        g.DrawLine(pen, 6.5F, 3.5F, 9.5F, 3.5F);
        // Body
        g.DrawLines(pen, new[]
        {
            new System.Drawing.PointF(4.5F, 5.5F),
            new System.Drawing.PointF(5.0F, 13.5F),
            new System.Drawing.PointF(11.0F, 13.5F),
            new System.Drawing.PointF(11.5F, 5.5F)
        });
        // Lines inside
        g.DrawLine(pen, 7.0F, 7.5F, 7.0F, 11.5F);
        g.DrawLine(pen, 9.0F, 7.5F, 9.0F, 11.5F);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateTransparentIcon()
    {
        System.Drawing.Bitmap bitmap = new(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.MakeTransparent();
        return bitmap;
    }

    /// <summary>Creates a 16×16 bitmap with a rounded-square colored background.</summary>
    private static System.Drawing.Bitmap CreateIconBase(System.Drawing.Color color)
    {
        var bmp = new System.Drawing.Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.MakeTransparent();
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = RoundedIconRect(new System.Drawing.Rectangle(0, 0, 15, 15), 4);
        using var brush = new System.Drawing.SolidBrush(color);
        g.FillPath(brush, path);
        return bmp;
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedIconRect(System.Drawing.Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void DrawPackageListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using System.Drawing.SolidBrush rowBrush = new(SidebarColor);
        e.Graphics.FillRectangle(rowBrush, e.Bounds);

        System.Drawing.Rectangle pill = new(
            e.Bounds.Left + 2, e.Bounds.Top + 3,
            e.Bounds.Width - 4, e.Bounds.Height - 6);

        if (selected)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using System.Drawing.Drawing2D.GraphicsPath pillPath = CreateRoundedRectangle(pill, 8);
            using System.Drawing.SolidBrush pillBrush = new(AccentColor);
            e.Graphics.FillPath(pillBrush, pillPath);
        }

        System.Drawing.Color fg      = selected ? System.Drawing.Color.White : ForegroundColor;
        System.Drawing.Color fgMuted = selected ? System.Drawing.Color.FromArgb(200, 230, 255) : MutedForegroundColor;

        string name = packageList.Items[e.Index]?.ToString() ?? string.Empty;

        // Read notes from config (cached would be better but this is fast enough)
        string notes = string.Empty;
        try
        {
            string cfgPath = Path.Combine(packagesRoot, name, "launcher-config.json");
            if (File.Exists(cfgPath))
            {
                JsonNode cfg = JsonNode.Parse(File.ReadAllText(cfgPath)) ?? new JsonObject();
                notes = cfg["notes"]?.GetValue<string>() ?? string.Empty;
            }
        }
        catch { }

        int textLeft = pill.Left + 12;
        int textWidth = pill.Width - 16;

        if (string.IsNullOrWhiteSpace(notes))
        {
            // Vertically centre the name when there's no subtitle
            System.Drawing.Rectangle nameBounds = new(textLeft, pill.Top, textWidth, pill.Height);
            using System.Drawing.StringFormat sf = new()
            {
                LineAlignment = System.Drawing.StringAlignment.Center,
                Trimming      = System.Drawing.StringTrimming.EllipsisCharacter
            };
            using System.Drawing.Font nameFont = new("Segoe UI", 10F, selected ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
            using System.Drawing.SolidBrush nameBrush = new(fg);
            e.Graphics.DrawString(name, nameFont, nameBrush, nameBounds, sf);
        }
        else
        {
            // Name on top, notes subtitle below
            int nameTop  = pill.Top + 8;
            int notesTop = nameTop + 19;

            using System.Drawing.Font nameFont  = new("Segoe UI", 10F, selected ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
            using System.Drawing.Font notesFont = new("Segoe UI", 8F);
            using System.Drawing.SolidBrush nameBrush  = new(fg);
            using System.Drawing.SolidBrush notesBrush = new(fgMuted);
            using System.Drawing.StringFormat sf = new()
            {
                Trimming      = System.Drawing.StringTrimming.EllipsisCharacter,
                FormatFlags   = System.Drawing.StringFormatFlags.NoWrap
            };
            e.Graphics.DrawString(name,  nameFont,  nameBrush,  new System.Drawing.RectangleF(textLeft, nameTop,  textWidth, 18), sf);
            e.Graphics.DrawString(notes, notesFont, notesBrush, new System.Drawing.RectangleF(textLeft, notesTop, textWidth, 16), sf);
        }
    }

    private void SelectPackageListItemUnderMouse(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        int index = packageList.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            packageList.SelectedIndex = index;
        }
    }

    private Control BuildDetailsPanel()
    {
        TableLayoutPanel outer = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(0),
            BackColor = BackgroundColor
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));

        Panel formScrollHost = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BackgroundColor,
            Padding = new Padding(32, 24, 32, 16)
        };

        TableLayoutPanel form = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = BackgroundColor
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        argumentsText.Multiline = true;
        argumentsText.Height = 140;
        argumentsText.ScrollBars = ScrollBars.Vertical;

        timeoutSecondsInput.Minimum = 1;
        timeoutSecondsInput.Maximum = 3600;
        timeoutSecondsInput.Value = 30;

        splashMinimumSecondsInput.Minimum = 0;
        splashMinimumSecondsInput.Maximum = 3600;
        splashMinimumSecondsInput.Value = 0;

        // ── Section: Launcher ────────────────────────────────────────────
        AddSectionHeader(form, "LAUNCHER");
        AddFieldRow(form, "Package folder", packageNameText, "Launcher EXE name", launcherExeText);
        AddFieldFullRow(form, "Notes (optional description)", packageNotesText);

        // ── Section: Restricted User ─────────────────────────────────────
        AddSectionHeader(form, "RESTRICTED USER");
        AddFieldRow(form, "Domain", domainText, "User name", userNameText);
        AddFieldFullRow(form, "Credential target", credentialTargetText);

        // ── Section: Destination ─────────────────────────────────────────
        AddSectionHeader(form, "DESTINATION");
        AddFieldFullRow(form, "Executable", destinationExeText);
        AddFieldFullRow(form, "Working directory", workingDirectoryText);
        AddFieldFullRowMultiline(form, "Arguments (one per line)", argumentsText);

        // ── Section: Pre-Launch ──────────────────────────────────────────
        AddSectionHeader(form, "PRE-LAUNCH");
        AddFieldRow(form, "Batch file", preLaunchFileText, "Timeout (seconds)", timeoutSecondsInput);

        // ── Section: Splash Screen ───────────────────────────────────────
        AddSectionHeader(form, "SPLASH SCREEN");

        // Splash toggle row
        splashEnabledCheck.Text = "Show splash screen";
        splashEnabledCheck.Font = UiFont;
        splashEnabledCheck.BackColor = BackgroundColor;
        splashEnabledCheck.Height = 36;
        splashEnabledCheck.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        splashEnabledCheck.Top = 20;
        splashEnabledCheck.Left = 0;

        TableLayoutPanel splashRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = BackgroundColor,
            Margin = Padding.Empty
        };
        splashRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        splashRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        splashRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel checkCell = new() { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        checkCell.Controls.Add(splashEnabledCheck);
        checkCell.Resize += (_, _) =>
        {
            splashEnabledCheck.Width = checkCell.ClientSize.Width - 8;
        };
        splashRow.Controls.Add(checkCell, 0, 0);

        Panel minSecCell = new() { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        Label minSecLabel = new()
        {
            Text = "Minimum seconds",
            Left = 8,
            Top = 0,
            Height = 20,
            Font = LabelFont,
            ForeColor = LabelColor,
            AutoSize = false
        };
        StyleInput(splashMinimumSecondsInput);
        Control minSecHost = CreateInputHost(splashMinimumSecondsInput);
        minSecHost.Top = 22;
        minSecHost.Height = 36;
        minSecCell.Controls.Add(minSecLabel);
        minSecCell.Controls.Add(minSecHost);
        minSecCell.Resize += (_, _) =>
        {
            minSecLabel.Width = minSecCell.ClientSize.Width - 8;
            minSecHost.Left = 8;
            minSecHost.Width = minSecCell.ClientSize.Width - 8;
        };
        splashRow.Controls.Add(minSecCell, 1, 0);

        // Splice splashRow into the form as a full-width item
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        int splashToggleRow = form.RowCount;
        form.RowCount++;
        form.Controls.Add(splashRow, 0, splashToggleRow);
        form.SetColumnSpan(splashRow, 2);

        // Splash PNG with browse button
        AddFieldFullRowWithBrowse(form, "Splash PNG", splashImageFileText, browseSplashButton);

        // Launcher icon with browse button
        AddFieldFullRowWithBrowse(form, "Launcher icon (.ico)", launcherIconFileText, browseIconButton);

        SetPackageFieldHelp();

        // ── Status + action bar ──────────────────────────────────────────
        Panel actionBar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            Padding = new Padding(32, 0, 32, 0)
        };

        // Replace Paint-event with a dedicated solid 1px divider row (row index 1)
        Panel divider = new()
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(180, 195, 215),
            Height = 1
        };

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = System.Drawing.Color.Transparent,
            Padding = new Padding(0, 10, 0, 10)
        };

        ConfigureActionButton(saveButton, "Save", 88);
        ConfigureActionButton(buildButton, "Build", 88);
        ConfigureActionButton(provisionButton, "Provision Credential", 160);
        ConfigurePrimaryButton(buildButton);
        SetHelp(saveButton, "Save the current package config and pre-launch batch to disk. Build is available after saving.");
        SetHelp(buildButton, "Build the selected config package into its configured launcher EXE. Save or refresh first if there are unsaved changes.");
        SetHelp(provisionButton, "Open a console to store the restricted user's password in Windows Credential Manager for the selected package.");

        buildStatusPanel.Dock = DockStyle.Left;
        buildStatusPanel.ColumnCount = 2;
        buildStatusPanel.RowCount = 1;
        buildStatusPanel.BackColor = System.Drawing.Color.Transparent;
        buildStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        buildStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        buildStatusIcon.Dock = DockStyle.Fill;
        buildStatusIcon.Text = "⚠";
        buildStatusIcon.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        buildStatusIcon.ForeColor = System.Drawing.Color.FromArgb(217, 119, 6);
        buildStatusIcon.Font = new System.Drawing.Font("Segoe UI Symbol", 12F, System.Drawing.FontStyle.Bold);

        buildStatusLabel.Dock = DockStyle.Fill;
        buildStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        buildStatusLabel.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
        buildStatusLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
        buildStatusPanel.Controls.Add(buildStatusIcon, 0, 0);
        buildStatusPanel.Controls.Add(buildStatusLabel, 1, 0);

        actions.Controls.Add(provisionButton);
        actions.Controls.Add(buildButton);
        actions.Controls.Add(saveButton);
        actionBar.Controls.Add(actions);
        actionBar.Controls.Add(buildStatusPanel);

        formScrollHost.Controls.Add(form);

        outer.Controls.Add(formScrollHost, 0, 0);
        outer.Controls.Add(divider, 0, 1);
        outer.Controls.Add(actionBar, 0, 2);
        return outer;
    }

    private void SetPackageFieldHelp()
    {
        SetHelp(packageNameText, "Folder name for this config package under config-packages. Rename it here, then press Save.");
        SetHelp(launcherExeText, "Output EXE name copied to dist under this config package folder after build. Use a stable admin-chosen .exe name.");
        SetHelp(launcherIconFileText, "Optional .ico file used as the generated launcher's Windows EXE icon. Relative paths are resolved from the config package folder.");
        SetHelp(browseIconButton, "Choose a .ico file from disk. The selected icon is copied into this config package folder.");
        SetHelp(domainText, "Windows domain for the restricted account. Use . for a local workstation account.");
        SetHelp(userNameText, "Restricted Windows user that will run the destination app.");
        SetHelp(credentialTargetText, "Credential Manager target name where this package stores and reads the restricted user's password.");
        SetHelp(destinationExeText, "Fixed executable to launch as the restricted user. This can be a full path or a resolvable executable name.");
        SetHelp(workingDirectoryText, "Working directory used for the pre-launch batch and destination app.");
        SetHelp(argumentsText, "One fixed destination argument per line. Users cannot change these at runtime.");
        SetHelp(preLaunchFileText, "Batch or CMD file name inside this package folder. Its content is embedded into the launcher during build.");
        SetHelp(timeoutSecondsInput, "Maximum time the hidden pre-launch batch may run before the launcher fails closed.");
        SetHelp(splashEnabledCheck, "Enable this to show a build-time embedded PNG splash screen when the launcher starts on the user workstation.");
        SetHelp(splashImageFileText, "PNG image to embed into the launcher for the splash screen. Relative paths are resolved from the config package folder.");
        SetHelp(browseSplashButton, "Choose a PNG image from disk for this config package's splash screen.");
        SetHelp(splashMinimumSecondsInput, "Minimum number of seconds the splash screen remains visible before it closes.");
    }

    // ── Section header ────────────────────────────────────────────────────

    private void AddSectionHeader(TableLayoutPanel form, string title)
    {
        SectionHeaderPanel header = new(title)
        {
            Dock = DockStyle.Fill,
            Height = 38,
            BackColor = BackgroundColor,
            Margin = new Padding(0, 8, 0, 4)
        };
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        int row = form.RowCount++;
        form.Controls.Add(header, 0, row);
        form.SetColumnSpan(header, 2);
    }

    // ── Two-field side-by-side row ────────────────────────────────────────

    private void AddFieldRow(TableLayoutPanel form, string label1, Control control1, string label2, Control control2)
    {
        int rowHeight = 70;
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        int row = form.RowCount++;

        form.Controls.Add(BuildFieldCell(label1, control1, rightPad: 8), 0, row);
        form.Controls.Add(BuildFieldCell(label2, control2, leftPad: 8), 1, row);
    }

    // ── Full-width single-field row ───────────────────────────────────────

    private void AddFieldFullRow(TableLayoutPanel form, string label, Control control)
    {
        int rowHeight = 70;
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        int row = form.RowCount++;

        Panel cell = BuildFieldCell(label, control, rightPad: 0);
        form.Controls.Add(cell, 0, row);
        form.SetColumnSpan(cell, 2);
    }

    // ── Full-width multiline row ──────────────────────────────────────────

    private void AddFieldFullRowMultiline(TableLayoutPanel form, string label, Control control)
    {
        int rowHeight = 168;
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        int row = form.RowCount++;

        Panel cell = BuildFieldCellMultiline(label, control);
        form.Controls.Add(cell, 0, row);
        form.SetColumnSpan(cell, 2);
    }

    // ── Full-width row with browse button ─────────────────────────────────

    private void AddFieldFullRowWithBrowse(TableLayoutPanel form, string label, TextBox textBox, PickerButton browseButton)
    {
        const int rowHeight = 68;
        const int labelHeight = 20;
        const int inputTop = 22;
        const int inputHeight = 36;
        const int btnWidth = 46;
        const int btnGap = 6;

        form.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        int row = form.RowCount++;

        Panel outer = new()
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor
        };

        Label labelControl = new()
        {
            Text = label,
            Left = 0,
            Top = 0,
            Height = labelHeight,
            Font = LabelFont,
            ForeColor = LabelColor,
            AutoSize = false
        };

        StyleInput(textBox);
        Control inputHost = CreateInputHost(textBox);
        inputHost.Top = inputTop;
        inputHost.Height = inputHeight;

        ConfigureBrowseButton(browseButton);
        browseButton.Top = inputTop;
        browseButton.Height = inputHeight;
        browseButton.Width = btnWidth;

        outer.Controls.Add(labelControl);
        outer.Controls.Add(inputHost);
        outer.Controls.Add(browseButton);

        void Layout()
        {
            int w = outer.ClientSize.Width;
            labelControl.Width = w;
            inputHost.Left = 0;
            inputHost.Width = Math.Max(0, w - btnWidth - btnGap);
            browseButton.Left = Math.Max(0, w - btnWidth);
        }

        outer.Resize += (_, _) => Layout();
        outer.HandleCreated += (_, _) => Layout();

        form.Controls.Add(outer, 0, row);
        form.SetColumnSpan(outer, 2);
    }

    private Panel BuildFieldCell(string label, Control control, int rightPad = 0, int leftPad = 0)
    {
        const int labelTop = 0;
        const int labelHeight = 20;
        const int inputTop = 22;
        const int inputHeight = 36;

        Panel cell = new()
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor
        };

        Label labelControl = new()
        {
            Text = label,
            Left = leftPad,
            Top = labelTop,
            Height = labelHeight,
            Font = LabelFont,
            ForeColor = LabelColor,
            AutoSize = false
        };

        StyleInput(control);
        Control inputHost = CreateInputHost(control);
        inputHost.Top = inputTop;
        inputHost.Height = inputHeight;

        cell.Controls.Add(labelControl);
        cell.Controls.Add(inputHost);

        void Layout()
        {
            int w = cell.ClientSize.Width;
            labelControl.Width = Math.Max(0, w - leftPad - rightPad);
            inputHost.Left = leftPad;
            inputHost.Width = Math.Max(0, w - leftPad - rightPad);
        }

        cell.Resize += (_, _) => Layout();
        cell.HandleCreated += (_, _) => Layout();

        return cell;
    }

    private Panel BuildFieldCellMultiline(string label, Control control)
    {
        const int labelHeight = 20;
        const int inputTop = 22;

        Panel cell = new()
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor
        };

        Label labelControl = new()
        {
            Text = label,
            Left = 0,
            Top = 0,
            Height = labelHeight,
            Font = LabelFont,
            ForeColor = LabelColor,
            AutoSize = false
        };

        StyleInput(control);
        Control inputHost = CreateInputHost(control);
        inputHost.Dock = DockStyle.None;
        inputHost.Top = inputTop;

        cell.Controls.Add(labelControl);
        cell.Controls.Add(inputHost);

        void Layout()
        {
            int w = cell.ClientSize.Width;
            int h = cell.ClientSize.Height;
            labelControl.Width = w;
            inputHost.Left = 0;
            inputHost.Width = w;
            inputHost.Height = Math.Max(40, h - inputTop - 4);
        }

        cell.Resize += (_, _) => Layout();
        cell.HandleCreated += (_, _) => Layout();

        return cell;
    }

    // ── Legacy AddRow kept for any other callers ──────────────────────────

    private static void AddRow(TableLayoutPanel form, int row, string label, Control control)
    {
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, control is TextBox { Multiline: true } ? 152 : 46));

        Label labelControl = new()
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = control is TextBox { Multiline: true }
                ? System.Drawing.ContentAlignment.TopLeft
                : System.Drawing.ContentAlignment.MiddleLeft,
            Font = LabelFont,
            ForeColor = ForegroundColor,
            Padding = control is TextBox { Multiline: true }
                ? new Padding(0, 14, 0, 0)
                : Padding.Empty
        };

        StyleInput(control);
        Control inputControl = CreateInputHost(control);
        form.Controls.Add(labelControl, 0, row);
        form.Controls.Add(inputControl, 1, row);
    }

    private static void AddBrowseRow(TableLayoutPanel form, int row, string label, TextBox textBox, PickerButton browseButton)
    {
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        Label labelControl = new()
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Font = LabelFont,
            ForeColor = ForegroundColor
        };

        StyleInput(textBox);

        TableLayoutPanel browseHost = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BackgroundColor,
            Margin = Padding.Empty
        };
        browseHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        browseHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        browseHost.Controls.Add(CreateInputHost(textBox), 0, 0);
        browseHost.Controls.Add(browseButton, 1, 0);

        form.Controls.Add(labelControl, 0, row);
        form.Controls.Add(browseHost, 1, row);
    }

    private static Control CreateInputHost(Control control)
    {
        if (control is TextBox or NumericUpDown)
        {
            RoundedInputPanel host = new(control)
            {
                Dock = DockStyle.None,
                BorderColor = BorderColor,
                BackColor = CardColor
            };
            return host;
        }

        control.Dock = DockStyle.None;
        return control;
    }

    private static void ConfigureActionButton(RoundedButton button, string text, int width)
    {
        button.Text = text;
        if (width > 0)
        {
            button.Width = width;
        }

        button.Height = 36;
        button.Margin = new Padding(6, 4, 0, 4);
        button.BorderRadius = 8;
        button.BorderColor = BorderColor;
        button.HoverBackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        button.BackColor = CardColor;
        button.ForeColor = ForegroundColor;
        button.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
    }

    private static void ConfigurePrimaryButton(RoundedButton button)
    {
        button.BackColor = AccentColor;
        button.ForeColor = System.Drawing.Color.White;
        button.BorderColor = AccentColor;
        button.HoverBackColor = AccentHoverColor;
        button.PressedBackColor = System.Drawing.Color.FromArgb(29, 78, 216);
    }

    private static void ConfigureIconButton(RoundedButton button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2, 2, 0, 2);
        button.BorderRadius = 8;
        button.BorderColor = SidebarColor;
        button.BackColor = SidebarColor;
        button.HoverBackColor = System.Drawing.Color.FromArgb(220, 226, 236);
        button.PressedBackColor = System.Drawing.Color.FromArgb(203, 213, 225);
        button.ForeColor = MutedForegroundColor;
        // Use Segoe UI Symbol which reliably contains ↻ (U+21BB) on all Windows versions
        button.Font = new System.Drawing.Font("Segoe UI Symbol", 13F);
    }

    private static void ConfigureConsoleIconButton(RoundedButton button, string text)
    {
        button.Text = text;
        button.Width = 28;
        button.Height = 28;
        button.Margin = new Padding(4, 2, 0, 2);
        button.BorderRadius = 14;
        button.BorderColor = System.Drawing.Color.FromArgb(30, 41, 59);
        button.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
        button.HoverBackColor = System.Drawing.Color.FromArgb(30, 41, 59);
        button.PressedBackColor = System.Drawing.Color.FromArgb(51, 65, 85);
        button.ForeColor = System.Drawing.Color.White;
        button.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
    }

    private static void ConfigureBrowseButton(PickerButton button)
    {
        button.BorderColor = BorderColor;
        button.BackColor = CardColor;
        button.ForeColor = MutedForegroundColor;
        button.Cursor = Cursors.Hand;
    }

    private static void StyleInput(Control control)
    {
        if (control is TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = CardColor;
            textBox.ForeColor = ForegroundColor;
            textBox.Font = textBox.Multiline ? MonoFont : UiFont;
            textBox.Margin = Padding.Empty;
        }

        if (control is NumericUpDown numeric)
        {
            numeric.BorderStyle = BorderStyle.None;
            numeric.BackColor = CardColor;
            numeric.ForeColor = ForegroundColor;
            numeric.Font = UiFont;
            numeric.Margin = Padding.Empty;
        }
    }

    private void WireEvents()
    {
        packageList.SelectedIndexChanged += (_, _) => LoadSelectedPackage();
        refreshButton.Click += (_, _) => RefreshPackagesWithWarning();
        newButton.Click += (_, _) => CreatePackage();
        saveButton.Click += (_, _) => SaveSelectedPackage();
        buildButton.Click += async (_, _) => await BuildSelectedPackageAsync();
        provisionButton.Click += async (_, _) => await ProvisionSelectedPackageAsync();
        clearOutputButton.Click += (_, _) => logText.Clear();
        browseIconButton.Click += (_, _) => BrowseLauncherIcon();
        browseSplashButton.Click += (_, _) => BrowseSplashImage();
        batchHighlightTimer.Tick += (_, _) => batchHighlightTimer.Stop();
        WireDirtyTracking();
    }

    private void WireDirtyTracking()
    {
        packageNameText.TextChanged += (_, _) => MarkDirty();
        packageNotesText.TextChanged += (_, _) => MarkDirty();
        launcherExeText.TextChanged += (_, _) => MarkDirty();
        launcherIconFileText.TextChanged += (_, _) => MarkDirty();
        domainText.TextChanged += (_, _) => MarkDirty();
        userNameText.TextChanged += (_, _) => MarkDirty();
        credentialTargetText.TextChanged += (_, _) => MarkDirty();
        destinationExeText.TextChanged += (_, _) => MarkDirty();
        workingDirectoryText.TextChanged += (_, _) => MarkDirty();
        argumentsText.TextChanged += (_, _) => MarkDirty();
        preLaunchFileText.TextChanged += (_, _) => MarkDirty();
        preLaunchContentText.TextChanged += (_, _) => MarkDirty();
        // Note: syntax highlighting is intentionally NOT re-triggered on TextChanged.
        // Doing so would corrupt the undo stack (SelectionColor ops pollute EM_UNDO).
        // Highlighting runs only on load and save, preserving full Ctrl+Z / Ctrl+Y history.
        timeoutSecondsInput.ValueChanged += (_, _) => MarkDirty();
        splashEnabledCheck.CheckedChanged += (_, _) => MarkDirty();
        splashImageFileText.TextChanged += (_, _) => MarkDirty();
        splashMinimumSecondsInput.ValueChanged += (_, _) => MarkDirty();
    }

    private void LoadPackages()
    {
        isLoadingPackage = true;

        Directory.CreateDirectory(packagesRoot);

        string? selectedName = packageList.SelectedItem as string;
        packageList.Items.Clear();

        foreach (string folder in Directory.GetDirectories(packagesRoot).OrderBy(Path.GetFileName))
        {
            if (File.Exists(Path.Combine(folder, "launcher-config.json")))
            {
                packageList.Items.Add(Path.GetFileName(folder));
            }
        }

        if (selectedName is not null && packageList.Items.Contains(selectedName))
        {
            packageList.SelectedItem = selectedName;
        }
        else if (packageList.Items.Count > 0)
        {
            packageList.SelectedIndex = 0;
        }

        isLoadingPackage = false;

        if (packageList.SelectedItem is null)
        {
            SetDirty(false);
        }
    }

    private void RefreshPackagesWithWarning()
    {
        if (hasUnsavedChanges)
        {
            DialogResult result = MessageBox.Show(
                "You have unsaved changes. Refreshing the config-packages list will discard those changes. Save first if you want to keep them.",
                "Discard unsaved changes?",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (result != DialogResult.OK)
            {
                return;
            }
        }

        findReplaceBar.Hide();
        LoadPackages();
        SetDirty(false);
    }

    private void LoadSelectedPackage()
    {
        if (packageList.SelectedItem is not string packageName)
        {
            return;
        }

        findReplaceBar.Hide();
        isLoadingPackage = true;

        try
        {
            selectedPackageFolder = Path.Combine(packagesRoot, packageName);
            string configPath = Path.Combine(selectedPackageFolder, "launcher-config.json");
            JsonNode config = JsonNode.Parse(File.ReadAllText(configPath)) ?? new JsonObject();

            packageNameText.Text = packageName;
            packageNotesText.Text = config["notes"]?.GetValue<string>() ?? string.Empty;
            launcherExeText.Text = ReadString(config, "launcher", "executableName");
            launcherIconFileText.Text = ReadString(config, "launcher", "iconFile");
            domainText.Text = ReadString(config, "restrictedUser", "domain");
            userNameText.Text = ReadString(config, "restrictedUser", "userName");
            credentialTargetText.Text = ReadString(config, "credentialManager", "targetName");
            destinationExeText.Text = ReadString(config, "destination", "executable");
            workingDirectoryText.Text = ReadString(config, "destination", "workingDirectory");
            argumentsText.Text = ReadArray(config, "destination", "arguments");
            preLaunchFileText.Text = ReadString(config, "preLaunch", "batchFile");
            timeoutSecondsInput.Value = Math.Clamp(ReadInt(config, 30, "preLaunch", "timeoutSeconds"), 1, 3600);
            splashEnabledCheck.Checked = ReadBool(config, false, "splash", "enabled");
            splashImageFileText.Text = ReadString(config, "splash", "imageFile");
            splashMinimumSecondsInput.Value = Math.Clamp(ReadInt(config, 0, "splash", "minimumSeconds"), 0, 3600);

            string batchPath = Path.Combine(selectedPackageFolder, preLaunchFileText.Text);
            preLaunchContentText.Text = File.Exists(batchPath)
                ? NormalizeLineEndings(File.ReadAllText(batchPath))
                : string.Empty;
            ApplyBatchSyntaxHighlighting();
            SelectCurrentPackage(saveFirst: false);
            SetDirty(false);
        }
        finally
        {
            isLoadingPackage = false;
        }
    }

    private void CreatePackage()
    {
        string? packageName = Prompt.ShowDialog("New package folder name:", "Create Config Package");
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return;
        }

        packageName = SanitizePackageName(packageName);
        string folder = Path.Combine(packagesRoot, packageName);

        if (Directory.Exists(folder))
        {
            MessageBox.Show("That package already exists.", "SafeLauncher Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "prelaunch.cmd"), "@echo off\r\nexit /b 0\r\n");

        JsonObject config = new()
        {
            ["launcher"] = new JsonObject { ["executableName"] = $"{packageName}.exe", ["iconFile"] = "" },
            ["restrictedUser"] = new JsonObject { ["domain"] = ".", ["userName"] = "ai_agent_user" },
            ["credentialManager"] = new JsonObject { ["targetName"] = $"SafeLauncher/{packageName}" },
            ["destination"] = new JsonObject
            {
                ["executable"] = "",
                ["workingDirectory"] = @"C:\ai-workspace",
                ["arguments"] = new JsonArray()
            },
            ["preLaunch"] = new JsonObject { ["batchFile"] = "prelaunch.cmd", ["timeoutSeconds"] = 30 },
            ["splash"] = new JsonObject
            {
                ["enabled"] = false,
                ["imageFile"] = "",
                ["minimumSeconds"] = 0
            }
        };

        File.WriteAllText(Path.Combine(folder, "launcher-config.json"), config.ToJsonString(JsonOptions()));
        LoadPackages();
        packageList.SelectedItem = packageName;
    }

    private void OpenSelectedPackageFolder()
    {
        string? folder = GetSelectedPackageFolder();
        if (folder is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private void LaunchBuiltExecutable()
    {
        string? executablePath = GetBuiltExecutablePath(showWarning: true);
        if (executablePath is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? repositoryRoot,
            UseShellExecute = true
        });
    }

    private void ShowBuiltExecutableFolder()
    {
        string? executablePath = GetBuiltExecutablePath(showWarning: true);
        if (executablePath is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{executablePath}\"",
            UseShellExecute = true
        });
    }

    private void DuplicateSelectedPackage()
    {
        string? sourceFolder = GetSelectedPackageFolder();
        if (sourceFolder is null || packageList.SelectedItem is not string selectedName)
        {
            return;
        }

        string? duplicateName = Prompt.ShowDialog("Duplicate package folder name:", "Duplicate Config Package");
        if (string.IsNullOrWhiteSpace(duplicateName))
        {
            return;
        }

        duplicateName = SanitizePackageName(duplicateName);
        string targetFolder = Path.Combine(packagesRoot, duplicateName);
        if (Directory.Exists(targetFolder))
        {
            MessageBox.Show("A package with that folder name already exists.", "SafeLauncher Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CopyDirectory(sourceFolder, targetFolder);
        Log($"Duplicated package {selectedName} to {duplicateName}");
        LoadPackages();
        packageList.SelectedItem = duplicateName;
    }

    private void DeleteSelectedPackage()
    {
        string? folder = GetSelectedPackageFolder();
        if (folder is null || packageList.SelectedItem is not string packageName)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            $"Delete config package \"{packageName}\"?\n\nThis will permanently delete the package folder and its pre-launch batch.",
            "Delete Config Package?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        Directory.Delete(folder, recursive: true);
        Log($"Deleted package: {packageName}");
        selectedPackageFolder = null;
        LoadPackages();
    }

    private void RenameSelectedPackage()
    {
        string? folder = GetSelectedPackageFolder();
        if (folder is null || packageList.SelectedItem is not string oldName) return;

        string? newName = Prompt.ShowDialog("New package name:", "Rename Package");
        if (string.IsNullOrWhiteSpace(newName)) return;

        newName = SanitizePackageName(newName);
        if (string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase)) return;

        string targetFolder = Path.Combine(packagesRoot, newName);
        if (Directory.Exists(targetFolder))
        {
            MessageBox.Show("A package with that name already exists.", "Rename Package",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Move folder
        Directory.Move(folder, targetFolder);

        // Update the executableName in config if it matched the old folder name
        string configPath = Path.Combine(targetFolder, "launcher-config.json");
        if (File.Exists(configPath))
        {
            try
            {
                JsonNode config = JsonNode.Parse(File.ReadAllText(configPath)) ?? new JsonObject();
                string exeName = ReadString(config, "launcher", "executableName");
                if (string.Equals(exeName, $"{oldName}.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (config["launcher"] is JsonObject launcher)
                        launcher["executableName"] = $"{newName}.exe";
                    File.WriteAllText(configPath, config.ToJsonString(JsonOptions()));
                }
            }
            catch (JsonException) { }
        }

        Log($"Renamed package: {oldName} → {newName}");
        selectedPackageFolder = targetFolder;
        LoadPackages();
        packageList.SelectedItem = newName;
    }

    private void ExportSelectedPackage()
    {
        string? folder = GetSelectedPackageFolder();
        if (folder is null || packageList.SelectedItem is not string packageName) return;

        using SaveFileDialog dlg = new()
        {
            Title            = "Export Package",
            Filter           = "Zip files (*.zip)|*.zip",
            FileName         = $"{packageName}.zip",
            DefaultExt       = "zip",
            AddExtension     = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            System.IO.Compression.ZipFile.CreateFromDirectory(folder, dlg.FileName,
                System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
            Log($"Exported package '{packageName}' → {dlg.FileName}");
            MessageBox.Show($"Package exported successfully:\n{dlg.FileName}",
                "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportPackage()
    {
        using OpenFileDialog dlg = new()
        {
            Title  = "Import Package",
            Filter = "Zip files (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Peek inside zip to find the root folder name
        string importedName;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(dlg.FileName);
            // Find root directory entry or infer from first entry
            string? firstEntry = zip.Entries.Count > 0 ? zip.Entries[0].FullName : null;
            if (firstEntry is null)
            {
                MessageBox.Show("The zip file is empty.", "Import Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            importedName = firstEntry.Split('/')[0].Split('\\')[0];
            if (string.IsNullOrWhiteSpace(importedName))
                importedName = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read zip: {ex.Message}", "Import Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        importedName = SanitizePackageName(importedName);
        string targetFolder = Path.Combine(packagesRoot, importedName);

        // If name conflicts, ask user for a new name
        if (Directory.Exists(targetFolder))
        {
            string? newName = Prompt.ShowDialog(
                $"A package named \"{importedName}\" already exists.\nEnter a different name:",
                "Import Package");
            if (string.IsNullOrWhiteSpace(newName)) return;
            importedName = SanitizePackageName(newName);
            targetFolder = Path.Combine(packagesRoot, importedName);
            if (Directory.Exists(targetFolder))
            {
                MessageBox.Show("That name is also taken.", "Import Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(dlg.FileName, packagesRoot, overwriteFiles: false);

            // ZipFile extracts using the folder name inside the zip; rename if needed
            string extractedFolder = Path.Combine(packagesRoot,
                Path.GetFileNameWithoutExtension(dlg.FileName));
            string zipRootFolder = Path.Combine(packagesRoot, importedName);
            if (!Directory.Exists(zipRootFolder) && Directory.Exists(extractedFolder))
                Directory.Move(extractedFolder, zipRootFolder);

            Log($"Imported package '{importedName}' from {dlg.FileName}");
            LoadPackages();
            packageList.SelectedItem = importedName;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static System.Drawing.Bitmap CreateRenameIcon()
    {
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(71, 85, 105));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap   = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        // Pencil icon
        g.DrawLine(pen, 3f, 13f, 5.5f, 10.5f);
        g.DrawLine(pen, 5.5f, 10.5f, 11f, 5f);
        g.DrawLine(pen, 11f, 5f, 13f, 7f);
        g.DrawLine(pen, 13f, 7f, 7.5f, 12.5f);
        g.DrawLine(pen, 7.5f, 12.5f, 3f, 13f);
        g.DrawLine(pen, 11f, 5f, 13f, 3f);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateExportIcon()
    {
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(22, 163, 74));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap   = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        // Up arrow out of box
        g.DrawLine(pen, 8f, 2f, 8f, 10f);
        g.DrawLine(pen, 5f, 5f, 8f, 2f);
        g.DrawLine(pen, 11f, 5f, 8f, 2f);
        g.DrawLine(pen, 3f, 9f, 3f, 13f);
        g.DrawLine(pen, 13f, 9f, 13f, 13f);
        g.DrawLine(pen, 3f, 13f, 13f, 13f);
        return bmp;
    }

    private static System.Drawing.Bitmap CreateImportIcon()
    {
        var bmp = CreateIconBase(System.Drawing.Color.FromArgb(37, 99, 235));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap   = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        // Down arrow into box
        g.DrawLine(pen, 8f, 2f, 8f, 10f);
        g.DrawLine(pen, 5f, 7f, 8f, 10f);
        g.DrawLine(pen, 11f, 7f, 8f, 10f);
        g.DrawLine(pen, 3f, 9f, 3f, 13f);
        g.DrawLine(pen, 13f, 9f, 13f, 13f);
        g.DrawLine(pen, 3f, 13f, 13f, 13f);
        return bmp;
    }

    private void BrowseLauncherIcon()
    {
        string? copiedIcon = BrowseAndCopyPackageFile(
            title: "Select Launcher Icon",
            filter: "Icon files (*.ico)|*.ico",
            currentValue: launcherIconFileText.Text);

        if (copiedIcon is not null)
        {
            launcherIconFileText.Text = copiedIcon;
        }
    }

    private void BrowseSplashImage()
    {
        string? copiedImage = BrowseAndCopyPackageFile(
            title: "Select Splash PNG",
            filter: "PNG images (*.png)|*.png",
            currentValue: splashImageFileText.Text);

        if (copiedImage is null)
        {
            return;
        }

        splashImageFileText.Text = copiedImage;
        splashEnabledCheck.Checked = true;
    }

    private string? BrowseAndCopyPackageFile(string title, string filter, string currentValue)
    {
        if (selectedPackageFolder is null)
        {
            return null;
        }

        using OpenFileDialog dialog = new()
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            string currentPath = ResolvePackageRelativePath(currentValue);
            string? currentDirectory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
            {
                dialog.InitialDirectory = currentDirectory;
            }
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        string targetPath = Path.Combine(selectedPackageFolder, Path.GetFileName(dialog.FileName));
        if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(dialog.FileName, targetPath, overwrite: true);
        }

        return Path.GetFileName(targetPath);
    }

    private string? GetSelectedPackageFolder()
    {
        return packageList.SelectedItem is string packageName
            ? Path.Combine(packagesRoot, packageName)
            : null;
    }

    private string? GetBuiltExecutablePath(bool showWarning)
    {
        if (packageList.SelectedItem is not string packageName)
        {
            return null;
        }

        string? packageFolder = GetSelectedPackageFolder();
        if (packageFolder is null)
        {
            return null;
        }

        string configPath = Path.Combine(packageFolder, "launcher-config.json");
        string executableName = launcherExeText.Text.Trim();
        if (File.Exists(configPath))
        {
            try
            {
                JsonNode config = JsonNode.Parse(File.ReadAllText(configPath)) ?? new JsonObject();
                executableName = ReadString(config, "launcher", "executableName");
            }
            catch (JsonException)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(executableName))
        {
            executableName = $"{packageName}.exe";
        }

        string executablePath = Path.Combine(repositoryRoot, "dist", packageName, executableName);
        if (File.Exists(executablePath))
        {
            return executablePath;
        }

        if (showWarning)
        {
            MessageBox.Show(
                $"Built launcher EXE was not found:\n\n{executablePath}\n\nBuild this config package first.",
                "Launcher EXE Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return null;
    }

    private string ResolvePackageRelativePath(string path)
    {
        if (Path.IsPathRooted(path) || selectedPackageFolder is null)
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(selectedPackageFolder, path));
    }

    private string TryMakePackageRelativePath(string path)
    {
        if (selectedPackageFolder is null)
        {
            return path;
        }

        try
        {
            string relativePath = Path.GetRelativePath(selectedPackageFolder, path);
            return relativePath.StartsWith("..", StringComparison.Ordinal) ? path : relativePath;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
        {
            string targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, targetFile);
        }

        foreach (string sourceSubdirectory in Directory.GetDirectories(sourceDirectory))
        {
            string targetSubdirectory = Path.Combine(targetDirectory, Path.GetFileName(sourceSubdirectory));
            CopyDirectory(sourceSubdirectory, targetSubdirectory);
        }
    }

    private void SaveSelectedPackage()
    {
        if (selectedPackageFolder is null)
        {
            return;
        }

        string originalFolder = selectedPackageFolder;
        string packageName = SanitizePackageName(packageNameText.Text);
        string targetFolder = Path.Combine(packagesRoot, packageName);

        if (!string.Equals(originalFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(targetFolder))
            {
                MessageBox.Show("A package with that folder name already exists.", "SafeLauncher Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Directory.Move(originalFolder, targetFolder);
            selectedPackageFolder = targetFolder;
        }

        string batchFileName = Path.GetFileName(preLaunchFileText.Text.Trim());
        if (string.IsNullOrWhiteSpace(batchFileName))
        {
            batchFileName = "prelaunch.cmd";
        }

        JsonObject config = new()
        {
            ["notes"] = packageNotesText.Text.Trim(),
            ["launcher"] = new JsonObject
            {
                ["executableName"] = launcherExeText.Text.Trim(),
                ["iconFile"] = launcherIconFileText.Text.Trim()
            },
            ["restrictedUser"] = new JsonObject
            {
                ["domain"] = domainText.Text.Trim(),
                ["userName"] = userNameText.Text.Trim()
            },
            ["credentialManager"] = new JsonObject { ["targetName"] = credentialTargetText.Text.Trim() },
            ["destination"] = new JsonObject
            {
                ["executable"] = destinationExeText.Text.Trim(),
                ["workingDirectory"] = workingDirectoryText.Text.Trim(),
                ["arguments"] = BuildArgumentsArray(argumentsText.Text)
            },
            ["preLaunch"] = new JsonObject
            {
                ["batchFile"] = batchFileName,
                ["timeoutSeconds"] = (int)timeoutSecondsInput.Value
            },
            ["splash"] = new JsonObject
            {
                ["enabled"] = splashEnabledCheck.Checked,
                ["imageFile"] = splashImageFileText.Text.Trim(),
                ["minimumSeconds"] = (int)splashMinimumSecondsInput.Value
            }
        };

        File.WriteAllText(Path.Combine(selectedPackageFolder, "launcher-config.json"), config.ToJsonString(JsonOptions()));
        File.WriteAllText(Path.Combine(selectedPackageFolder, batchFileName), NormalizeLineEndings(preLaunchContentText.Text));
        preLaunchFileText.Text = batchFileName;

        Log($"Saved package: {packageName}");
        LoadPackages();
        packageList.SelectedItem = packageName;
        SetDirty(false);
        ApplyBatchSyntaxHighlighting();
        packageList.Invalidate();
    }

    private void SelectCurrentPackage(bool saveFirst = true)
    {
        if (saveFirst)
        {
            SaveSelectedPackage();
        }

        if (selectedPackageFolder is null)
        {
            return;
        }

        JsonObject rootConfig = new()
        {
            ["configPackageFolder"] = selectedPackageFolder
        };

        File.WriteAllText(rootConfigPath, rootConfig.ToJsonString(JsonOptions()));
        Log($"Selected package: {selectedPackageFolder}");
    }

    private async Task BuildSelectedPackageAsync()
    {
        if (hasUnsavedChanges)
        {
            showBuildWarning = true;
            UpdateBuildButtonState();
            Log("Build skipped: save changes or refresh the config-packages list first.");
            return;
        }

        SelectCurrentPackage(saveFirst: false);

        // Determine output path before build
        string packageName = packageList.SelectedItem as string ?? string.Empty;
        string executableName = launcherExeText.Text.Trim();
        if (string.IsNullOrWhiteSpace(executableName))
            executableName = $"{packageName}.exe";
        string expectedExePath = Path.Combine(repositoryRoot, "dist", packageName, executableName);

        // Show overlay
        using var cts = new System.Threading.CancellationTokenSource();
        using BuildOverlay overlay = new();
        overlay.CancelRequested += (_, _) => cts.Cancel();
        overlay.ShowOver(this);
        overlay.TrackOwnerResize(this);

        isBuildRunning = true;
        UpdateBuildButtonState();
        int exitCode = 0;
        try
        {
            // Run on a thread-pool thread so the UI message loop stays free to animate
            exitCode = await Task.Run(() => RunProcessSync(buildScriptPath, cts.Token));
        }
        catch (OperationCanceledException)
        {
            Log("Build cancelled by user.");
            exitCode = -2;
        }
        catch (Exception ex)
        {
            Log($"Build error: {ex.Message}");
            exitCode = -1;
        }
        finally
        {
            overlay.UntrackOwnerResize(this);
            isBuildRunning = false;
            UpdateBuildButtonState();
            overlay.HideOverlay();
        }

        // Don't show result dialog if cancelled
        if (exitCode == -2) return;

        // Show result dialog
        bool success = exitCode == 0 && File.Exists(expectedExePath);
        string? builtPath = success ? expectedExePath : null;
        using BuildResultDialog result = new(success, packageName, builtPath);
        if (result.ShowDialog(this) == DialogResult.Yes && builtPath is not null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{builtPath}\"",
                UseShellExecute = true
            });
        }
    }

    private async Task ProvisionSelectedPackageAsync()
    {
        if (hasUnsavedChanges)
        {
            showBuildWarning = true;
            UpdateBuildButtonState();
            Log("Provision skipped: save changes or refresh the config-packages list first.");
            return;
        }

        if (GetBuiltExecutablePath(showWarning: false) is null)
        {
            Log("Built launcher EXE was not found. Building selected package before provisioning credential...");
            await BuildSelectedPackageAsync();

            if (GetBuiltExecutablePath(showWarning: true) is null)
            {
                Log("Provision skipped: build did not produce a launcher EXE.");
                return;
            }
        }

        SelectCurrentPackage(saveFirst: false);

        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/k \"\"{provisionScriptPath}\"\"",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = true
        };

        Process.Start(startInfo);
        Log("Opened credential provisioning console.");
    }

    private int RunProcessSync(string fileName, System.Threading.CancellationToken cancellationToken = default)
    {
        string actualFileName = fileName;
        string arguments = string.Empty;

        if (string.Equals(Path.GetExtension(fileName), ".bat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(fileName), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            actualFileName = "cmd.exe";
            arguments = $"/c \"\"{fileName}\"\"";
        }

        Log($"> {fileName}");

        ProcessStartInfo startInfo = new()
        {
            FileName = actualFileName,
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start process.");

        // Kill the process tree when cancellation is requested
        using var reg = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        });

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Log(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        cancellationToken.ThrowIfCancellationRequested();

        Log($"Exit code: {process.ExitCode}");
        return process.ExitCode;
    }

    private async Task ReadStreamAsync(StreamReader reader)
    {
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync();
            if (line is not null)
            {
                Log(line);
            }
        }
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        logText.AppendText(message + Environment.NewLine);
    }

    private void MarkDirty()
    {
        if (!isLoadingPackage && !isHighlightingBatch)
        {
            SetDirty(true);
        }
    }

    private void ApplyBatchSyntaxHighlighting()
    {
        if (isHighlightingBatch || preLaunchContentText.TextLength == 0)
        {
            return;
        }

        isHighlightingBatch = true;

        int selectionStart = preLaunchContentText.SelectionStart;
        int selectionLength = preLaunchContentText.SelectionLength;
        NativePoint scrollPosition = new();
        SendMessage(preLaunchContentText.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scrollPosition);

        try
        {
            preLaunchContentText.SuspendLayout();
            SendMessage(preLaunchContentText.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            preLaunchContentText.SelectAll();
            preLaunchContentText.SelectionColor = ForegroundColor;

            HighlightBatchPattern(@"(?im)^\s*(rem\b.*|::.*)$", System.Drawing.Color.FromArgb(22, 163, 74));
            HighlightBatchPattern(@"(?im)^\s*:[A-Za-z0-9_.-]+", MutedForegroundColor);
            HighlightBatchPattern(@"""[^""]*""", System.Drawing.Color.FromArgb(180, 83, 9));
            HighlightBatchPattern(@"%[^%\r\n]+%", AccentColor);
            HighlightBatchPattern(@"(?im)\b(@?echo|setlocal|endlocal|setx?|if|not|exist|errorlevel|mkdir|md|copy|xcopy|del|erase|exit|call|goto|for|in|do|else|pushd|popd|cd|cmd)\b", System.Drawing.Color.FromArgb(0, 82, 255));
            HighlightBatchPattern(@"[<|>&]", System.Drawing.Color.FromArgb(147, 51, 234));

            preLaunchContentText.Select(selectionStart, selectionLength);
            preLaunchContentText.SelectionColor = ForegroundColor;
            SendMessage(preLaunchContentText.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scrollPosition);
        }
        finally
        {
            SendMessage(preLaunchContentText.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            preLaunchContentText.ResumeLayout();
            preLaunchContentText.Invalidate();
            isHighlightingBatch = false;
        }
    }

    private void QueueBatchSyntaxHighlighting()
    {
        if (isLoadingPackage || isHighlightingBatch)
        {
            return;
        }

        batchHighlightTimer.Stop();
        batchHighlightTimer.Start();
    }

    private void HighlightBatchPattern(string pattern, System.Drawing.Color color)
    {
        foreach (Match match in Regex.Matches(preLaunchContentText.Text, pattern))
        {
            preLaunchContentText.Select(match.Index, match.Length);
            preLaunchContentText.SelectionColor = color;
        }
    }

    private void SetDirty(bool dirty)
    {
        hasUnsavedChanges = dirty;
        if (!dirty)
        {
            showBuildWarning = false;
        }

        UpdateBuildButtonState();
    }

    private void UpdateBuildButtonState()
    {
        buildButton.Enabled = !isBuildRunning;
        buildStatusPanel.Visible = showBuildWarning;
        buildStatusLabel.Text = "Unsaved changes — save before building.";

        if (hasUnsavedChanges)
        {
            buildButton.BackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            buildButton.ForeColor = System.Drawing.Color.FromArgb(148, 163, 184);
            buildButton.BorderColor = System.Drawing.Color.FromArgb(203, 213, 225);
            buildButton.HoverBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            buildButton.PressedBackColor = System.Drawing.Color.FromArgb(203, 213, 225);
            return;
        }

        ConfigurePrimaryButton(buildButton);
    }

    private static string FindRepositoryRoot()
    {
        string current = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "src", "SafeLauncher", "launcher-config.json")) &&
                Directory.Exists(Path.Combine(current, "config-packages")))
            {
                return current;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static JsonArray BuildArgumentsArray(string text)
    {
        JsonArray array = new();
        foreach (string line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                array.Add(line.Trim());
            }
        }

        return array;
    }

    private static string ReadArray(JsonNode config, string section, string property)
    {
        JsonArray? array = config[section]?[property] as JsonArray;
        if (array is null)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, array.Select(item => item?.GetValue<string>() ?? string.Empty));
    }

    private static string ReadString(JsonNode config, string section, string property)
    {
        return config[section]?[property]?.GetValue<string>() ?? string.Empty;
    }

    private static int ReadInt(JsonNode config, int defaultValue, string section, string property)
    {
        return config[section]?[property]?.GetValue<int>() ?? defaultValue;
    }

    private static bool ReadBool(JsonNode config, bool defaultValue, string section, string property)
    {
        return config[section]?[property]?.GetValue<bool>() ?? defaultValue;
    }

    private static string SanitizePackageName(string value)
    {
        string trimmed = value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(trimmed.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "new-package" : sanitized;
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, ref NativePoint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}

// ── Sidebar panel with right border ─────────────────────────────────────────

internal sealed class SidebarPanel : TableLayoutPanel
{
    private static readonly System.Drawing.Color BorderLineColor = System.Drawing.Color.FromArgb(220, 226, 236);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using System.Drawing.Pen pen = new(BorderLineColor, 1);
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

// ── Section header with accent bar and divider ───────────────────────────────

internal sealed class SectionHeaderPanel : Panel
{
    private static readonly System.Drawing.Color AccentBarColor = System.Drawing.Color.FromArgb(37, 99, 235);
    private static readonly System.Drawing.Color TextColor = System.Drawing.Color.FromArgb(100, 116, 139);
    private static readonly System.Drawing.Color DividerColor = System.Drawing.Color.FromArgb(180, 195, 215);

    private readonly string title;

    public SectionHeaderPanel(string title)
    {
        this.title = title;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;

        // Accent bar on left
        using System.Drawing.SolidBrush accentBrush = new(AccentBarColor);
        e.Graphics.FillRectangle(accentBrush, 0, 11, 3, 14);

        // Label text
        using System.Drawing.Font font = new("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
        System.Drawing.Rectangle textBounds = new(10, 0, Width - 10, Height);
        TextRenderer.DrawText(e.Graphics, title, font, textBounds, TextColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // Divider line after label — solid 1px, clearly visible
        int textWidth = TextRenderer.MeasureText(title, font).Width + 18;
        int lineY = Height / 2;
        if (textWidth < Width - 10)
        {
            using System.Drawing.Pen dividerPen = new(DividerColor, 1f);
            e.Graphics.DrawLine(dividerPen, textWidth, lineY, Width, lineY);
        }
    }
}

// ── Modern context menu renderer ─────────────────────────────────────────────

internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly System.Drawing.Color Background   = System.Drawing.Color.White;
    private static readonly System.Drawing.Color HoverFill    = System.Drawing.Color.FromArgb(239, 246, 255);
    private static readonly System.Drawing.Color HoverBorder  = System.Drawing.Color.FromArgb(187, 210, 250);
    private static readonly System.Drawing.Color SeparatorClr = System.Drawing.Color.FromArgb(226, 232, 240);
    private static readonly System.Drawing.Color TextNormal   = System.Drawing.Color.FromArgb(30, 41, 59);
    private static readonly System.Drawing.Color TextDanger   = System.Drawing.Color.FromArgb(220, 38, 38);

    public ModernMenuRenderer() : base(new ModernColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Background);
        // Rounded border around the whole menu
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 228, 240), 1f);
        var r = new System.Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = RoundedRect(r, 8);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new System.Drawing.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = RoundedRect(r, 6);
        using var fill = new System.Drawing.SolidBrush(HoverFill);
        e.Graphics.FillPath(fill, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool isDanger = e.Item.Text == "Delete";
        e.TextColor = isDanger
            ? (e.Item.Selected ? TextDanger : System.Drawing.Color.FromArgb(185, 28, 28))
            : TextNormal;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new System.Drawing.Pen(SeparatorClr, 1f);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { /* no image gutter */ }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null) return;
        int x = (e.ImageRectangle.Width - e.Image.Width) / 2 + e.ImageRectangle.X;
        int y = (e.ImageRectangle.Height - e.Image.Height) / 2 + e.ImageRectangle.Y;
        e.Graphics.DrawImage(e.Image, x, y);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(System.Drawing.Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

internal sealed class ModernColorTable : ProfessionalColorTable
{
    public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(220, 228, 240);
    public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(239, 246, 255);
    public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(239, 246, 255);
    public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(239, 246, 255);
    public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
    public override System.Drawing.Color MenuItemPressedGradientBegin => System.Drawing.Color.FromArgb(219, 234, 254);
    public override System.Drawing.Color MenuItemPressedGradientEnd => System.Drawing.Color.FromArgb(219, 234, 254);
    public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.White;
    public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(226, 232, 240);
    public override System.Drawing.Color SeparatorLight => System.Drawing.Color.White;
}
