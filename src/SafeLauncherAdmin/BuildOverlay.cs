using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

/// <summary>
/// Two-layer overlay: a dim semi-transparent form covers the main window,
/// and a separate fully-opaque card form floats on top of it.
/// </summary>
internal sealed class BuildOverlay : IDisposable
{
    private readonly DimLayer   dimLayer;
    private readonly CardLayer  cardLayer;

    public event EventHandler? CancelRequested;

    public BuildOverlay()
    {
        dimLayer  = new DimLayer();
        cardLayer = new CardLayer();
        cardLayer.CancelClicked += (_, _) =>
        {
            cardLayer.SetCancelling();
            CancelRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void ShowOver(Form owner)
    {
        dimLayer.Owner  = owner;
        cardLayer.Owner = owner;

        Rectangle clientScreen = owner.ClientRectangleToScreen();
        dimLayer.Bounds  = clientScreen;
        cardLayer.CentreOver(clientScreen);

        dimLayer.Show(owner);
        cardLayer.Show(owner);
        owner.Activate();

        dimLayer.StartAnimation();
        cardLayer.StartAnimation();
    }

    public void HideOverlay()
    {
        dimLayer.StopAndClose();
        cardLayer.StopAndClose();
    }

    public void TrackOwnerResize(Form owner)
    {
        owner.Resize += OnOwnerResize;
        owner.Move   += OnOwnerResize;
    }

    public void UntrackOwnerResize(Form owner)
    {
        owner.Resize -= OnOwnerResize;
        owner.Move   -= OnOwnerResize;
    }

    private void OnOwnerResize(object? s, EventArgs e)
    {
        if (s is not Form owner) return;
        Rectangle clientScreen = owner.ClientRectangleToScreen();
        dimLayer.Bounds = clientScreen;
        cardLayer.CentreOver(clientScreen);
    }

    public void Dispose()
    {
        dimLayer.Dispose();
        cardLayer.Dispose();
    }
}

// ── Dim layer ────────────────────────────────────────────────────────────────

internal sealed class DimLayer : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private int tick;

    public DimLayer()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        Opacity         = 0.55;
        BackColor       = Color.FromArgb(15, 23, 42);
        Cursor          = Cursors.WaitCursor;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        // Slow pulse to indicate activity
        timer.Tick += (_, _) => { tick++; Opacity = 0.50 + 0.05 * Math.Sin(tick * 0.12); };
    }

    public void StartAnimation() => timer.Start();

    public void StopAndClose()
    {
        timer.Stop();
        if (!IsDisposed) Close();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; }
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}

// ── Card layer ───────────────────────────────────────────────────────────────

internal sealed class CardLayer : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly RoundedButton cancelButton = new();
    private int spinAngle;
    private int dotTick;
    private bool cancelling;

    public event EventHandler? CancelClicked;

    private static readonly Color CardBg     = Color.White;
    private static readonly Color CardBorder = Color.FromArgb(220, 228, 240);
    private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
    private static readonly Color TextMain   = Color.FromArgb(30, 41, 59);
    private static readonly Color TextMuted  = Color.FromArgb(100, 116, 139);

    public CardLayer()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        BackColor       = CardBg;
        Size            = new Size(280, 196);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        // Cancel button
        cancelButton.Text             = "Cancel";
        cancelButton.Width            = 90;
        cancelButton.Height           = 30;
        cancelButton.BorderRadius     = 8;
        cancelButton.BackColor        = Color.White;
        cancelButton.ForeColor        = Color.FromArgb(100, 116, 139);
        cancelButton.BorderColor      = Color.FromArgb(220, 228, 240);
        cancelButton.HoverBackColor   = Color.FromArgb(241, 245, 249);
        cancelButton.PressedBackColor = Color.FromArgb(226, 232, 240);
        cancelButton.Font             = new Font("Segoe UI", 9F, FontStyle.Bold);
        cancelButton.Cursor           = Cursors.Hand;
        cancelButton.Location         = new Point((Width - 90) / 2, Height - 30 - 14);
        cancelButton.Click           += (_, _) => CancelClicked?.Invoke(this, EventArgs.Empty);
        Controls.Add(cancelButton);

        timer.Tick += (_, _) =>
        {
            spinAngle = (spinAngle + 9) % 360;
            dotTick   = (dotTick + 1) % 30;
            Invalidate();
        };
    }

    public void CentreOver(Rectangle area)
    {
        Location = new Point(
            area.Left + (area.Width  - Width)  / 2,
            area.Top  + (area.Height - Height) / 2);
    }

    public void SetCancelling()
    {
        cancelling            = true;
        cancelButton.Text     = "Cancelling…";
        cancelButton.Enabled  = false;
        Invalidate();
    }

    public void StartAnimation() => timer.Start();

    public void StopAndClose()
    {
        timer.Stop();
        if (!IsDisposed) Close();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(CardBg);

        // Card border
        using (var pen = new Pen(CardBorder, 1f))
        using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 16))
            g.DrawPath(pen, path);

        // Spinner
        const int spinR = 22;
        int cx = Width / 2;
        var spinRect = new RectangleF(cx - spinR, 18, spinR * 2, spinR * 2);

        using (var track = new Pen(Color.FromArgb(226, 232, 240), 3.5f))
            g.DrawEllipse(track, spinRect);

        Color arcColor = cancelling ? Color.FromArgb(180, 180, 190) : AccentBlue;
        using (var arc = new Pen(arcColor, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(arc, spinRect, spinAngle, 250);

        // Labels
        int labelY = (int)(18 + spinR * 2 + 10);
        string heading = cancelling ? "Cancelling…" : "Building" + new string('.', dotTick / 10 + 1);
        string subtext = cancelling ? "Stopping the build…" : "Please wait…";

        using (var f = new Font("Segoe UI", 11F, FontStyle.Bold))
            TextRenderer.DrawText(g, heading, f,
                new Rectangle(0, labelY, Width, 26),
                TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        using (var f = new Font("Segoe UI", 9F))
            TextRenderer.DrawText(g, subtext, f,
                new Rectangle(0, labelY + 26, Width, 22),
                TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left,      r.Top,        d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top,        d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        p.AddArc(r.Left,      r.Bottom - d, d, d,  90, 90);
        p.CloseFigure();
        return p;
    }
}

internal static class FormExtensions
{
    public static Rectangle ClientRectangleToScreen(this Form form)
    {
        Point topLeft = form.PointToScreen(Point.Empty);
        return new Rectangle(topLeft, form.ClientSize);
    }
}
