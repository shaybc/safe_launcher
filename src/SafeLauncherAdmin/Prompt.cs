using System.Drawing;
using System.Windows.Forms;

namespace SafeLauncherAdmin;

internal static class Prompt
{
    public static string? ShowDialog(string text, string caption)
    {
        using Form prompt = new()
        {
            Width = 420,
            Height = 190,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = caption,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 10.25F)
        };

        Label label = new() { Left = 18, Top = 18, Width = 370, Text = text };
        TextBox input = new() { Left = 18, Top = 50, Width = 376, Height = 30, Font = new Font("Segoe UI", 10.25F) };
        RoundedButton ok = new()
        {
            Text = "OK",
            Left = 214,
            Width = 86,
            Height = 34,
            Top = 106,
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(0, 82, 255),
            ForeColor = Color.White,
            BorderColor = Color.FromArgb(0, 82, 255),
            HoverBackColor = Color.FromArgb(77, 124, 255)
        };
        RoundedButton cancel = new()
        {
            Text = "Cancel",
            Left = 308,
            Width = 86,
            Height = 34,
            Top = 106,
            DialogResult = DialogResult.Cancel
        };

        prompt.Controls.Add(label);
        prompt.Controls.Add(input);
        prompt.Controls.Add(ok);
        prompt.Controls.Add(cancel);
        prompt.AcceptButton = ok;
        prompt.CancelButton = cancel;

        return prompt.ShowDialog() == DialogResult.OK ? input.Text : null;
    }
}
