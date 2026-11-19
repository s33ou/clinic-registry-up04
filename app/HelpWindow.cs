namespace CareerOffice;

internal static class HelpWindow
{
    public static void Show(IWin32Window owner, string headingText, string[] lines)
    {
        using var form = new Form
        {
            Text = "Справка — регистратура",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(585, 335),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi
        };
        using var headingFont = new Font("Segoe UI", 13, FontStyle.Bold);
        var heading = new Label { Text = headingText, Font = headingFont, Dock = DockStyle.Fill };
        var body = new TextBox
        {
            Text = string.Join("\r\n\r\n", lines),
            AccessibleName = "Инструкция",
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };
        var ok = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, Padding = new Padding(20) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(body, 0, 1);
        layout.Controls.Add(ok, 0, 2);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = ok;
        form.ShowDialog(owner);
    }
}
