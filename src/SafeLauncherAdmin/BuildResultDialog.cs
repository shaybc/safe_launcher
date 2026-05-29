using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

/// <summary>
/// Modal dialog shown after a build completes — success or failure.
/// Returns DialogResult.Yes when the user clicks "Show in Explorer".
/// </summary>
internal sealed class BuildResultDialog : Form
{
    private static readonly Color Background  = Color.FromArgb(249, 250, 252);
    private static readonly Color CardColor   = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(220, 228, 240);
    private static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
    private static readonly Color TextMuted   = Color.FromArgb(100, 116, 139);
    private static readonly Color AccentBlue  = Color.FromArgb(37, 99, 235);
    private static readonly Color SuccessGreen = Color.FromArgb(22, 163, 74);
    private static readonly Color FailRed     = Color.FromArgb(220, 38, 38);

    public BuildResultDialog(bool success, string packageName, string? exePath)
    {
        Text = success ? "Build Successful" : "Build Failed";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Background;
        Size = new Size(480, exePath is not null ? 260 : 220);
        ShowInTaskbar = false;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        // ── Icon badge ────────────────────────────────────────────────────
        IconBadge badge = new(success)
        {
            Location = new Point(32, 28),
            Size = new Size(48, 48)
        };

        // ── Title ─────────────────────────────────────────────────────────
        Label title = new()
        {
            Text = success ? "Build successful" : "Build failed",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = success ? SuccessGreen : FailRed,
            AutoSize = true,
            Location = new Point(92, 30)
        };

        // ── Package name ──────────────────────────────────────────────────
        Label pkgLabel = new()
        {
            Text = packageName,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(92, 57)
        };

        // ── Path label ────────────────────────────────────────────────────
        int buttonTop = 178;
        if (exePath is not null)
        {
            Panel pathCard = new()
            {
                Location = new Point(32, 100),
                Size = new Size(416, 52),
                BackColor = CardColor
            };
            pathCard.Paint += (_, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(BorderColor, 1f);
                using var path = RoundedRect(new Rectangle(0, 0, pathCard.Width - 1, pathCard.Height - 1), 8);
                pe.Graphics.DrawPath(pen, path);
            };

            Label pathMeta = new()
            {
                Text = "Output file",
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(12, 7)
            };
            Label pathValue = new()
            {
                Text = TruncatePath(exePath, 52),
                Font = new Font("Consolas", 9F),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(12, 26)
            };
            pathValue.ToolTip(exePath);
            pathCard.Controls.Add(pathMeta);
            pathCard.Controls.Add(pathValue);
            Controls.Add(pathCard);
            buttonTop = 172;
        }
        else if (!success)
        {
            Label hint = new()
            {
                Text = "Check the Output tab for details.",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(32, 100)
            };
            Controls.Add(hint);
        }

        // ── Buttons ───────────────────────────────────────────────────────
        RoundedButton okButton = new()
        {
            Text = "OK",
            Width = 90,
            Height = 36,
            Location = new Point(Width - 32 - 90, buttonTop),
            BackColor = AccentBlue,
            ForeColor = Color.White,
            BorderColor = AccentBlue,
            HoverBackColor = Color.FromArgb(59, 130, 246),
            PressedBackColor = Color.FromArgb(29, 78, 216),
            BorderRadius = 8,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };
        okButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        Controls.Add(badge);
        Controls.Add(title);
        Controls.Add(pkgLabel);
        Controls.Add(okButton);
        AcceptButton = okButton;
        CancelButton = okButton;

        if (exePath is not null)
        {
            RoundedButton showButton = new()
            {
                Text = "Show in Explorer",
                Width = 136,
                Height = 36,
                Location = new Point(Width - 32 - 90 - 8 - 136, buttonTop),
                BackColor = CardColor,
                ForeColor = TextPrimary,
                BorderColor = BorderColor,
                HoverBackColor = Color.FromArgb(241, 245, 249),
                PressedBackColor = Color.FromArgb(226, 232, 240),
                BorderRadius = 8,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                DialogResult = DialogResult.Yes
            };
            showButton.Click += (_, _) => { DialogResult = DialogResult.Yes; Close(); };
            Controls.Add(showButton);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(BorderColor, 1f);
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        e.Graphics.DrawPath(pen, path);
    }

    // Allow dragging the borderless form
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTCAPTION = 2;
        if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTCAPTION; return; }
        base.WndProc(ref m);
    }

    private static string TruncatePath(string path, int maxChars)
    {
        if (path.Length <= maxChars) return path;
        string file = Path.GetFileName(path);
        string dir = Path.GetDirectoryName(path) ?? "";
        int keep = maxChars - file.Length - 4;
        return keep > 0 ? dir[..keep] + "…\\" + file : "…\\" + file;
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        GraphicsPath p = new();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ── Icon badge ────────────────────────────────────────────────────────
    private sealed class IconBadge : Control
    {
        private readonly bool success;
        public IconBadge(bool success)
        {
            this.success = success;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Fill parent background to avoid artifacts
            using SolidBrush parentBrush = new(Parent?.BackColor ?? Color.White);
            g.FillRectangle(parentBrush, ClientRectangle);

            Color fill   = success ? Color.FromArgb(220, 252, 231) : Color.FromArgb(254, 226, 226);
            Color stroke = success ? Color.FromArgb(22, 163, 74)   : Color.FromArgb(220, 38, 38);

            Rectangle r = new(1, 1, Width - 2, Height - 2);
            using GraphicsPath path = RoundedRect(r, 14);
            using SolidBrush fillBrush = new(fill);
            g.FillPath(fillBrush, path);

            using Pen pen = new(stroke, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

            if (success)
            {
                // Checkmark
                g.DrawLines(pen, new[]
                {
                    new PointF(12f, 24f),
                    new PointF(20f, 33f),
                    new PointF(36f, 15f)
                });
            }
            else
            {
                // X
                g.DrawLine(pen, 14f, 14f, 34f, 34f);
                g.DrawLine(pen, 34f, 14f, 14f, 34f);
            }
        }
    }
}

// Extension helper for tooltip on a label
internal static class ControlExtensions
{
    private static readonly ToolTip SharedTip = new();
    public static void ToolTip(this Control control, string text) =>
        SharedTip.SetToolTip(control, text);
}
