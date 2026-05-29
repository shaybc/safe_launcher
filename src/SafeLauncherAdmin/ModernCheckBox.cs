using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

internal sealed class ModernCheckBox : Control
{
    private static readonly Color CheckedFill    = Color.FromArgb(37, 99, 235);
    private static readonly Color CheckedBorder  = Color.FromArgb(37, 99, 235);
    private static readonly Color UncheckedFill  = Color.FromArgb(239, 246, 255);
    private static readonly Color UncheckedBorder = Color.FromArgb(187, 210, 250);
    private static readonly Color HoverFill      = Color.FromArgb(219, 234, 254);
    private static readonly Color TextColor      = Color.FromArgb(30, 41, 59);

    private bool isChecked;
    private bool isHovered;

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Height = 32;
        AutoSize = false;
    }

    public bool Checked
    {
        get => isChecked;
        set { isChecked = value; Invalidate(); }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnMouseEnter(EventArgs e)
    {
        isHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        isChecked = !isChecked;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Fill parent background
        Color parentBg = FindOpaqueBackground();
        using SolidBrush bgBrush = new(parentBg);
        g.FillRectangle(bgBrush, ClientRectangle);

        const int boxSize = 22;
        const int boxLeft = 0;
        int boxTop = (Height - boxSize) / 2;

        Rectangle boxRect = new(boxLeft, boxTop, boxSize - 1, boxSize - 1);

        // Box fill
        Color fill = isChecked ? CheckedFill : isHovered ? HoverFill : UncheckedFill;
        Color border = isChecked ? CheckedBorder : UncheckedBorder;

        using GraphicsPath boxPath = RoundedRect(boxRect, 6);
        using SolidBrush fillBrush = new(fill);
        using Pen borderPen = new(border, 1.5f);
        g.FillPath(fillBrush, boxPath);
        g.DrawPath(borderPen, boxPath);

        // Checkmark
        if (isChecked)
        {
            using Pen checkPen = new(Color.White, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            float cx = boxLeft + boxSize / 2f;
            float cy = boxTop + boxSize / 2f;
            PointF[] pts =
            {
                new(cx - 5f, cy),
                new(cx - 1.5f, cy + 4f),
                new(cx + 5f, cy - 4.5f)
            };
            g.DrawLines(checkPen, pts);
        }

        // Label text
        if (!string.IsNullOrEmpty(Text))
        {
            Rectangle textRect = new(boxSize + 8, 0, Width - boxSize - 8, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    private Color FindOpaqueBackground()
    {
        Control? c = Parent;
        while (c is not null)
        {
            if (c.BackColor != Color.Transparent && c.BackColor.A == 255)
                return c.BackColor;
            c = c.Parent;
        }
        return SystemColors.Control;
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
}
