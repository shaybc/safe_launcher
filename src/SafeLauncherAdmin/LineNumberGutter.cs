using System;
using System.Drawing;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

/// <summary>
/// Renders logical line numbers (based on \n characters) aligned to their
/// visual Y position in the RichTextBox. Works correctly with word wrap.
/// </summary>
internal sealed class LineNumberGutter : Control
{
    private static readonly Color GutterBg     = Color.FromArgb(244, 246, 250);
    private static readonly Color GutterBorder = Color.FromArgb(220, 226, 236);
    private static readonly Color NumberColor  = Color.FromArgb(150, 165, 180);
    private static readonly Color CurrentColor = Color.FromArgb(37, 99, 235);
    private static readonly Color WrapDotColor = Color.FromArgb(210, 218, 230);

    private RichTextBox? editor;
    private Font? gutterFont;
    private bool wordWrapMode;

    public LineNumberGutter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = GutterBg;
        Width     = 44;
        Dock      = DockStyle.Left;
    }

    public bool WordWrapMode
    {
        get => wordWrapMode;
        set { wordWrapMode = value; Invalidate(); }
    }

    public void Attach(RichTextBox rtb)
    {
        editor     = rtb;
        gutterFont = new Font(rtb.Font.FontFamily, rtb.Font.Size);
        rtb.TextChanged      += (_, _) => { UpdateWidth(); Invalidate(); };
        rtb.VScroll          += (_, _) => Invalidate();
        rtb.Resize           += (_, _) => Invalidate();
        rtb.SelectionChanged += (_, _) => Invalidate();
        rtb.FontChanged      += (_, _) =>
        {
            gutterFont?.Dispose();
            gutterFont = new Font(rtb.Font.FontFamily, rtb.Font.Size);
            UpdateWidth();
            Invalidate();
        };
    }

    private void UpdateWidth()
    {
        if (editor is null || gutterFont is null) return;
        // Count logical lines from the raw text
        int lines  = Math.Max(1, CountLogicalLines(editor.Text));
        int digits = lines.ToString().Length;
        using var g = CreateGraphics();
        int charW  = (int)Math.Ceiling(g.MeasureString(new string('9', digits), gutterFont).Width);
        Width = charW + 20;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (editor is null || gutterFont is null) return;

        Graphics g = e.Graphics;
        g.Clear(GutterBg);

        using (var borderPen = new Pen(GutterBorder, 1f))
            g.DrawLine(borderPen, Width - 1, 0, Width - 1, Height);

        string text      = editor.Text;
        int lineH        = (int)Math.Ceiling(gutterFont.GetHeight(g));
        int currentLine  = GetLogicalLineFromCharIndex(text, editor.SelectionStart);

        using var numberBrush  = new SolidBrush(NumberColor);
        using var currentBrush = new SolidBrush(CurrentColor);
        using var wrapBrush    = new SolidBrush(WrapDotColor);
        using var sf = new StringFormat
        {
            Alignment     = StringAlignment.Far,
            LineAlignment = StringAlignment.Center
        };

        // Walk logical lines by scanning \n characters in the raw text.
        // For each logical line, find the char index of its first character,
        // then ask the RichTextBox for its visual Y position.
        int charPos = 0;
        int prevBottomY = -1;

        for (int logicalLine = 0; ; logicalLine++)
        {
            // Get visual Y for this logical line's first character
            Point pt = editor.GetPositionFromCharIndex(charPos);
            int y = pt.Y;

            // Once we're past the visible area, stop
            if (y > editor.ClientSize.Height) break;

            if (y + lineH >= 0) // only draw if visible
            {
                // If word wrap is on, draw wrap-continuation dots between
                // the previous number's bottom and this number's top
                if (wordWrapMode && prevBottomY >= 0 && logicalLine > 0)
                {
                    int gap  = y - prevBottomY;
                    int rows = gap / lineH;
                    for (int r = 0; r < rows; r++)
                    {
                        int dotY = prevBottomY + r * lineH + lineH / 2;
                        g.FillEllipse(wrapBrush, Width - 10, dotY - 2, 4, 4);
                    }
                }

                bool isCurrent = (logicalLine == currentLine);
                var brush = isCurrent ? currentBrush : numberBrush;
                var rect  = new Rectangle(2, y, Width - 8, lineH);
                g.DrawString((logicalLine + 1).ToString(), gutterFont, brush, rect, sf);

                prevBottomY = y + lineH;
            }

            // Advance charPos to start of next logical line
            int nlPos = text.IndexOf('\n', charPos);
            if (nlPos < 0) break; // last line (no trailing newline)
            charPos = nlPos + 1;

            // Stop if we've consumed all text
            if (charPos >= text.Length) break;
        }
    }

    /// <summary>Returns the 0-based logical line number for a character index.</summary>
    private static int GetLogicalLineFromCharIndex(string text, int charIndex)
    {
        int line = 0;
        for (int i = 0; i < charIndex && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static int CountLogicalLines(string text)
    {
        if (text.Length == 0) return 1;
        int count = 1;
        foreach (char c in text)
            if (c == '\n') count++;
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) gutterFont?.Dispose();
        base.Dispose(disposing);
    }
}
