using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

internal sealed class RoundedInputPanel : Panel
{
    private static readonly Color FocusBorderColor = Color.FromArgb(37, 99, 235);
    private static readonly Color DefaultBorderColor = Color.FromArgb(226, 232, 240);

    private readonly bool isMultiline;

    public RoundedInputPanel(Control innerControl)
    {
        InnerControl = innerControl;
        isMultiline = innerControl is TextBox { Multiline: true };
        BackColor = Color.White;

        // Keep DockStyle.Fill for multiline; single-line is centered manually
        if (isMultiline)
        {
            Padding = new Padding(12, 8, 10, 8);
            InnerControl.Dock = DockStyle.Fill;
        }
        else
        {
            Padding = Padding.Empty;
            InnerControl.Dock = DockStyle.None;
        }

        Controls.Add(InnerControl);
        WireFocusEvents(innerControl);
    }

    public Control InnerControl { get; }

    public int BorderRadius { get; set; } = 9;

    public Color BorderColor { get; set; } = DefaultBorderColor;

    private bool isFocused;

    private void WireFocusEvents(Control control)
    {
        control.GotFocus += (_, _) => { isFocused = true; Invalidate(); };
        control.LostFocus += (_, _) => { isFocused = false; Invalidate(); };
        foreach (Control child in control.Controls)
            WireFocusEvents(child);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionInnerControl();
        Invalidate();
    }

    private void PositionInnerControl()
    {
        if (isMultiline)
            return;

        // Vertically center the inner control with 12px horizontal padding
        int innerW = Math.Max(1, Width - 24);
        int innerH = InnerControl is TextBox tb ? tb.PreferredHeight
                   : InnerControl is NumericUpDown nud ? nud.PreferredHeight
                   : 22;
        int innerTop = Math.Max(1, (Height - innerH) / 2);
        InnerControl.SetBounds(12, innerTop, innerW, innerH);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = CreateRoundRectangle(bounds, BorderRadius);
        using SolidBrush backgroundBrush = new(BackColor);

        Color activeBorder = isFocused ? FocusBorderColor : BorderColor;
        float penWidth = isFocused ? 1.8F : 1.0F;
        using Pen borderPen = new(activeBorder, penWidth);

        e.Graphics.FillPath(backgroundBrush, path);
        e.Graphics.DrawPath(borderPen, path);
    }

    private static GraphicsPath CreateRoundRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
