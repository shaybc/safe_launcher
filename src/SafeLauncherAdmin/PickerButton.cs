using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

internal sealed class PickerButton : Button
{
    private bool isHovered;
    private bool isPressed;

    public PickerButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public Color BorderColor { get; set; } = Color.FromArgb(226, 232, 240);

    protected override void OnMouseEnter(EventArgs e)
    {
        isHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHovered = false;
        isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        isPressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Fill entire control with parent background first (handles corners)
        Color parentBg = FindOpaqueBackground();
        using SolidBrush bgBrush = new(parentBg);
        g.FillRectangle(bgBrush, 0, 0, Width, Height);

        // Draw rounded button shape
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        Color fill = isPressed
            ? Color.FromArgb(226, 232, 240)
            : isHovered
                ? Color.FromArgb(241, 245, 249)
                : BackColor;

        using GraphicsPath path = CreateRoundRectangle(bounds, 8);
        using SolidBrush fillBrush = new(fill);
        using Pen borderPen = new(BorderColor, 1f);
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);

        // Draw three dots centered
        using SolidBrush dotBrush = new(ForeColor);
        float cx = Width / 2.0F;
        float cy = Height / 2.0F;
        float r = 2.5F;
        float gap = 5.5F;

        g.FillEllipse(dotBrush, cx - gap - r, cy - r, r * 2, r * 2);
        g.FillEllipse(dotBrush, cx - r, cy - r, r * 2, r * 2);
        g.FillEllipse(dotBrush, cx + gap - r, cy - r, r * 2, r * 2);
    }

    private Color FindOpaqueBackground()
    {
        Control? current = Parent;
        while (current is not null)
        {
            Color c = current.BackColor;
            if (c != Color.Transparent && c.A == 255)
                return c;
            current = current.Parent;
        }
        return SystemColors.Control;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    private static GraphicsPath CreateRoundRectangle(Rectangle bounds, int radius)
    {
        int d = Math.Max(1, radius * 2);
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
