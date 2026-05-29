using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

/// <summary>
/// Small borderless dialog for Ctrl+G "Go to line".
/// </summary>
internal sealed class GoToLineDialog : Form
{
    private static readonly Color Bg        = Color.FromArgb(249, 250, 252);
    private static readonly Color Border    = Color.FromArgb(220, 228, 240);
    private static readonly Color Blue      = Color.FromArgb(37, 99, 235);
    private static readonly Color TextColor = Color.FromArgb(30, 41, 59);

    private readonly NumericUpDown lineInput = new();
    public int SelectedLine => (int)lineInput.Value;

    public GoToLineDialog(int currentLine, int maxLine)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.CenterParent;
        ShowInTaskbar   = false;
        BackColor       = Bg;
        Size            = new Size(300, 120);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        Label titleLabel = new()
        {
            Text      = "Go to line",
            Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = TextColor,
            Location  = new Point(20, 16),
            AutoSize  = true
        };

        Label maxLabel = new()
        {
            Text      = $"(1 – {maxLine})",
            Font      = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location  = new Point(102, 19),
            AutoSize  = true
        };

        lineInput.Minimum  = 1;
        lineInput.Maximum  = maxLine;
        lineInput.Value    = Math.Max(1, Math.Min(currentLine + 1, maxLine));
        lineInput.Font     = new Font("Segoe UI", 10.5F);
        lineInput.Location = new Point(20, 44);
        lineInput.Width    = 180;
        lineInput.BorderStyle = BorderStyle.FixedSingle;

        RoundedButton ok = new()
        {
            Text             = "Go",
            Width            = 60,
            Height           = 30,
            Location         = new Point(210, 44),
            BackColor        = Blue,
            ForeColor        = Color.White,
            BorderColor      = Blue,
            HoverBackColor   = Color.FromArgb(59, 130, 246),
            PressedBackColor = Color.FromArgb(29, 78, 216),
            BorderRadius     = 8,
            Font             = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            DialogResult     = DialogResult.OK
        };

        // Hidden cancel button so Escape closes the dialog
        Button cancelBtn = new() { DialogResult = DialogResult.Cancel, Width = 0, Height = 0, TabStop = false };

        Controls.AddRange(new Control[] { titleLabel, maxLabel, lineInput, ok, cancelBtn });
        AcceptButton = ok;
        CancelButton = cancelBtn;

        // Pre-select the number in the input
        lineInput.Select();
        lineInput.Controls.OfType<TextBox>().FirstOrDefault()?.SelectAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Border, 1f);
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = (IntPtr)2; return; } // HTCAPTION — draggable
        base.WndProc(ref m);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
