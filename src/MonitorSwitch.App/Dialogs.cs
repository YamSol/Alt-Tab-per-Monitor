namespace MonitorSwitch.App;

/// <summary>Small code-only dialogs; no designer files.</summary>
internal static class Dialogs
{
    public static void ShowText(string title, string text)
    {
        using var form = new Form
        {
            Text = title,
            Width = 900,
            Height = 600,
            StartPosition = FormStartPosition.CenterScreen,
            ShowInTaskbar = true,
        };
        form.Controls.Add(new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = text.ReplaceLineEndings("\r\n"),
        });
        form.ShowDialog();
    }

    /// <summary>One process name per line. Returns null when cancelled.</summary>
    public static IReadOnlyList<string>? EditList(string title, string hint, IEnumerable<string> items)
    {
        using var form = new Form
        {
            Text = title,
            Width = 420,
            Height = 420,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
        };
        var label = new Label { Text = hint, Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
        var box = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = string.Join("\r\n", items),
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([cancel, ok]);
        form.Controls.AddRange([box, label, buttons]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        return box.Lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
