using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

/// <summary>
/// Collapsible find / find-replace toolbar that docks above the batch editor.
/// Ctrl+F = find, Ctrl+H = find+replace, Escape = close, Enter/F3 = next match.
/// </summary>
internal sealed class FindReplaceBar : Panel
{
    private static readonly Color BarBg       = Color.FromArgb(244, 246, 250);
    private static readonly Color BarBorder   = Color.FromArgb(210, 220, 235);
    private static readonly Color InputBg     = Color.White;
    private static readonly Color InputBorder = Color.FromArgb(210, 220, 235);
    private static readonly Color TextMain    = Color.FromArgb(30, 41, 59);
    private static readonly Color TextMuted   = Color.FromArgb(100, 116, 139);
    private static readonly Color MatchBg     = Color.FromArgb(255, 235, 100);
    private static readonly Color CurrentBg   = Color.FromArgb(255, 165, 40);
    private static readonly Color NoMatchBg   = Color.FromArgb(255, 215, 215);

    private readonly TextBox       findBox        = new();
    private readonly TextBox       replaceBox     = new();
    private readonly CheckBox      matchCase      = new();
    private readonly Label         statusLabel    = new();
    private readonly RoundedButton btnPrev        = new();
    private readonly RoundedButton btnNext        = new();
    private readonly RoundedButton btnReplace     = new();
    private readonly RoundedButton btnReplaceAll  = new();
    private readonly RoundedButton btnClose       = new();
    private readonly Panel         replaceRow     = new();

    private RichTextBox? editor;
    private readonly List<int> matches = new();
    private int currentIdx = -1;

    public FindReplaceBar()
    {
        BackColor = BarBg;
        Height    = 42;
        Dock      = DockStyle.Top;
        Visible   = false;

        Paint += (_, pe) =>
        {
            using var pen = new Pen(BarBorder, 1f);
            pe.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
        };

        BuildLayout();
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void Attach(RichTextBox rtb)
    {
        editor = rtb;
        editor.KeyDown += OnEditorKeyDown;
    }

    public void ShowFind()
    {
        replaceRow.Visible = false;
        Height  = 42;
        Visible = true;
        SeedSearchText();
        findBox.Focus();
    }

    public void ShowReplace()
    {
        replaceRow.Visible = true;
        Height  = 78;
        Visible = true;
        SeedSearchText();
        findBox.Focus();
    }

    public new void Hide()
    {
        ClearHighlights();
        Visible = false;
        editor?.Focus();
    }

    // ── Layout ────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        // Find row
        Panel findRow = new() { BackColor = BarBg, Height = 30, Dock = DockStyle.Top, Padding = new Padding(0, 1, 0, 1) };

        StyleInput(findBox, "Find…");
        findBox.TextChanged += (_, _) => Search();
        findBox.KeyDown     += OnFindKeyDown;

        matchCase.Text      = "Match case";
        matchCase.Font      = new Font("Segoe UI", 8.5F);
        matchCase.ForeColor = TextMuted;
        matchCase.BackColor = BarBg;
        matchCase.AutoSize  = true;
        matchCase.Anchor    = AnchorStyles.Left | AnchorStyles.Top;
        matchCase.CheckedChanged += (_, _) => Search();

        StyleNavBtn(btnPrev, "▲"); btnPrev.Click  += (_, _) => Navigate(-1);
        StyleNavBtn(btnNext, "▼"); btnNext.Click  += (_, _) => Navigate(+1);

        statusLabel.AutoSize  = false;
        statusLabel.Width     = 72;
        statusLabel.Height    = 26;
        statusLabel.Font      = new Font("Segoe UI", 8.5F);
        statusLabel.ForeColor = TextMuted;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        StyleCloseBtn(btnClose); btnClose.Click += (_, _) => Hide();

        findRow.Controls.AddRange(new Control[] { findBox, matchCase, btnPrev, btnNext, statusLabel, btnClose });
        findRow.Resize += (_, _) => ArrangeFindRow(findRow);
        findRow.HandleCreated += (_, _) => ArrangeFindRow(findRow);

        // Replace row
        replaceRow.BackColor = BarBg;
        replaceRow.Height    = 32;
        replaceRow.Dock      = DockStyle.Top;
        replaceRow.Visible   = false;

        StyleInput(replaceBox, "Replace with…");
        replaceBox.KeyDown += OnReplaceKeyDown;

        StyleActionBtn(btnReplace,    "Replace",     90);
        StyleActionBtn(btnReplaceAll, "Replace All", 96);
        btnReplace.Click    += (_, _) => DoReplace();
        btnReplaceAll.Click += (_, _) => DoReplaceAll();

        replaceRow.Controls.AddRange(new Control[] { replaceBox, btnReplace, btnReplaceAll });
        replaceRow.Resize       += (_, _) => ArrangeReplaceRow();
        replaceRow.HandleCreated += (_, _) => ArrangeReplaceRow();

        // Top padding row
        Panel pad = new() { BackColor = BarBg, Height = 6, Dock = DockStyle.Top };

        // DockStyle.Top stacks in reverse add order — add replace last so find appears on top
        Controls.Add(replaceRow);
        Controls.Add(findRow);
        Controls.Add(pad);
    }

    private void ArrangeFindRow(Panel row)
    {
        int h    = 26;
        int top  = (row.ClientSize.Height - h) / 2;
        int right = row.ClientSize.Width - 6;

        btnClose.SetBounds(right - 26, top, 26, h);
        btnNext .SetBounds(btnClose.Left - 4 - 26, top, 26, h);
        btnPrev .SetBounds(btnNext.Left  - 2 - 26, top, 26, h);
        statusLabel.SetBounds(btnPrev.Left - 4 - statusLabel.Width, top, statusLabel.Width, h);

        int matchCaseX = 232;
        matchCase.Left = matchCaseX;
        matchCase.Top  = top + (h - matchCase.Height) / 2;

        findBox.SetBounds(6, top, 218, h);
    }

    private void ArrangeReplaceRow()
    {
        int h   = 24;
        int top = (replaceRow.ClientSize.Height - h) / 2;
        replaceBox.SetBounds(6, top, 218, h);
        btnReplace   .SetBounds(232,                          top, btnReplace.Width,    h);
        btnReplaceAll.SetBounds(232 + btnReplace.Width + 6,   top, btnReplaceAll.Width, h);
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void Search()
    {
        ClearHighlights();
        matches.Clear();
        currentIdx = -1;

        if (editor is null || findBox.Text.Length == 0)
        {
            findBox.BackColor = InputBg;
            RefreshStatus();
            return;
        }

        string text    = editor.Text;
        string pattern = findBox.Text;
        var cmp = matchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (int i = 0; i <= text.Length - pattern.Length; )
        {
            int pos = text.IndexOf(pattern, i, cmp);
            if (pos < 0) break;
            matches.Add(pos);
            i = pos + 1;
        }

        findBox.BackColor = (matches.Count == 0 && findBox.Text.Length > 0) ? NoMatchBg : InputBg;

        HighlightAll();

        if (matches.Count > 0) { currentIdx = 0; HighlightCurrent(); }
        RefreshStatus();
    }

    private void Navigate(int dir)
    {
        if (matches.Count == 0) return;
        currentIdx = (currentIdx + dir + matches.Count) % matches.Count;
        HighlightAll();
        HighlightCurrent();
        RefreshStatus();
    }

    private void HighlightAll()
    {
        if (editor is null || matches.Count == 0) return;
        int len = findBox.Text.Length;
        SetRedraw(editor, false);
        int ss = editor.SelectionStart, sl = editor.SelectionLength;
        foreach (int pos in matches)
        {
            editor.Select(pos, len);
            editor.SelectionBackColor = MatchBg;
        }
        editor.Select(ss, sl);
        SetRedraw(editor, true);
        editor.Invalidate();
    }

    private void HighlightCurrent()
    {
        if (editor is null || currentIdx < 0 || currentIdx >= matches.Count) return;
        int pos = matches[currentIdx];
        SetRedraw(editor, false);
        editor.Select(pos, findBox.Text.Length);
        editor.SelectionBackColor = CurrentBg;
        editor.ScrollToCaret();
        SetRedraw(editor, true);
        editor.Invalidate();
    }

    private void ClearHighlights()
    {
        if (editor is null || editor.TextLength == 0) return;
        SetRedraw(editor, false);
        int ss = editor.SelectionStart, sl = editor.SelectionLength;
        editor.SelectAll();
        editor.SelectionBackColor = editor.BackColor;
        editor.Select(ss, sl);
        SetRedraw(editor, true);
        editor.Invalidate();
    }

    private void RefreshStatus()
    {
        statusLabel.Text = matches.Count == 0
            ? (findBox.Text.Length > 0 ? "No results" : string.Empty)
            : $"{currentIdx + 1} / {matches.Count}";
    }

    // ── Replace ───────────────────────────────────────────────────────────

    private void DoReplace()
    {
        if (editor is null || matches.Count == 0) return;
        if (currentIdx < 0) currentIdx = 0;

        // Remember the position we just replaced so we can advance past it
        int replacedPos = matches[currentIdx];
        int replacedLen = findBox.Text.Length;
        int replaceLen  = replaceBox.Text.Length;

        editor.Select(replacedPos, replacedLen);
        editor.SelectedText = replaceBox.Text;

        // Rebuild match list after the text changed
        Search();

        // Advance to the first match that starts after the replaced region
        int nextPos = replacedPos + replaceLen;
        currentIdx = -1;
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i] >= nextPos)
            {
                currentIdx = i;
                break;
            }
        }

        // Wrap to first if nothing found after current position
        if (currentIdx < 0 && matches.Count > 0)
            currentIdx = 0;

        if (currentIdx >= 0)
        {
            HighlightAll();
            HighlightCurrent();
        }

        RefreshStatus();
    }

    private void DoReplaceAll()
    {
        if (editor is null || matches.Count == 0) return;

        int startIdx  = Math.Max(0, currentIdx);
        int findLen   = findBox.Text.Length;
        int replaceLen = replaceBox.Text.Length;

        // Collect positions to replace from currentIdx onwards,
        // skipping any position that falls inside a range we already replaced.
        // Build the list top-to-bottom first so we can track replaced ranges,
        // then reverse to replace bottom-to-top (preserving earlier positions).
        var toReplace  = new List<int>();
        var replacedEnd = new List<int>(); // tracks end of each replaced region (cumulative-shifted)

        int shift = 0;
        for (int i = startIdx; i < matches.Count; i++)
        {
            int origPos    = matches[i];
            int shiftedPos = origPos + shift;

            // Skip if this match starts inside a previously replaced region
            bool overlaps = false;
            for (int r = 0; r < replacedEnd.Count; r++)
            {
                // replacedEnd[r] is the end of a previously replaced range (shifted)
                // We stored the start too — use a parallel list
                if (origPos < replacedEnd[r]) { overlaps = true; break; }
            }
            if (!overlaps)
            {
                toReplace.Add(origPos);
                replacedEnd.Add(origPos + findLen); // original end of this match
                shift += replaceLen - findLen;
            }
        }

        if (toReplace.Count == 0) return;
        int count = toReplace.Count;

        // Replace bottom-to-top so positions stay valid
        for (int i = toReplace.Count - 1; i >= 0; i--)
        {
            editor.Select(toReplace[i], findLen);
            editor.SelectedText = replaceBox.Text;
        }

        Search();

        // Position after last replaced region
        int lastEnd = toReplace[toReplace.Count - 1] + replaceLen;
        currentIdx  = -1;
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i] >= lastEnd) { currentIdx = i; break; }
        }
        if (currentIdx < 0 && matches.Count > 0) currentIdx = 0;
        if (currentIdx >= 0) { HighlightAll(); HighlightCurrent(); }

        statusLabel.Text = $"Replaced {count}";
    }

    // ── Keyboard handling ─────────────────────────────────────────────────

    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter: case Keys.F3: Navigate(e.Shift ? -1 : +1); e.Handled = true; break;
            case Keys.Escape: Hide(); e.Handled = true; break;
        }
    }

    private void OnReplaceKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter: DoReplace(); e.Handled = true; break;
            case Keys.Escape: Hide(); e.Handled = true; break;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F) { ShowFind();    e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.H) { ShowReplace(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F3) { if (!Visible) ShowFind(); Navigate(e.Shift ? -1 : +1); e.Handled = true; return; }
        if (e.KeyCode == Keys.Escape && Visible) { Hide(); e.Handled = true; }
    }

    private void SeedSearchText()
    {
        if (editor is not null && editor.SelectionLength > 0 && editor.SelectionLength < 200)
            findBox.Text = editor.SelectedText;
        findBox.SelectAll();
        if (Visible) Search();
    }

    // ── Win32 redraw suppression ──────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static void SetRedraw(Control c, bool on) =>
        SendMessage(c.Handle, 0x000B, on ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);

    // ── Button/input styling ──────────────────────────────────────────────

    private static void StyleInput(TextBox tb, string placeholder)
    {
        tb.BorderStyle      = BorderStyle.FixedSingle;
        tb.BackColor        = InputBg;
        tb.ForeColor        = TextMain;
        tb.Font             = new Font("Segoe UI", 9.5F);
        tb.PlaceholderText  = placeholder;
        tb.Anchor           = AnchorStyles.None;
    }

    private static void StyleNavBtn(RoundedButton b, string text)
    {
        b.Text             = text;
        b.BorderRadius     = 6;
        b.BackColor        = InputBg;
        b.ForeColor        = TextMuted;
        b.BorderColor      = InputBorder;
        b.HoverBackColor   = Color.FromArgb(241, 245, 249);
        b.PressedBackColor = Color.FromArgb(226, 232, 240);
        b.Font             = new Font("Segoe UI", 8F, FontStyle.Bold);
        b.Cursor           = Cursors.Hand;
    }

    private static void StyleActionBtn(RoundedButton b, string text, int width)
    {
        b.Text             = text;
        b.Width            = width;
        b.BorderRadius     = 6;
        b.BackColor        = InputBg;
        b.ForeColor        = TextMain;
        b.BorderColor      = InputBorder;
        b.HoverBackColor   = Color.FromArgb(241, 245, 249);
        b.PressedBackColor = Color.FromArgb(226, 232, 240);
        b.Font             = new Font("Segoe UI", 9F, FontStyle.Bold);
        b.Cursor           = Cursors.Hand;
    }

    private static void StyleCloseBtn(RoundedButton b)
    {
        b.Text             = "✕";
        b.BorderRadius     = 6;
        b.BackColor        = BarBg;
        b.ForeColor        = TextMuted;
        b.BorderColor      = BarBg;
        b.HoverBackColor   = Color.FromArgb(254, 226, 226);
        b.PressedBackColor = Color.FromArgb(252, 200, 200);
        b.Font             = new Font("Segoe UI", 9F, FontStyle.Bold);
        b.Cursor           = Cursors.Hand;
    }
}
