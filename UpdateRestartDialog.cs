namespace PingWatchdog;

// FOCAL: explicit restart consent. Native chrome and existing compact theme tokens.
internal sealed class UpdateRestartDialog : Form
{
    private readonly Button _restart = new AuroraButton { Text = "Restart now", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button _later = new AuroraButton { Text = "Later", AutoSize = true, DialogResult = DialogResult.Cancel };

    internal UpdateRestartDialog(string version, bool monitoring)
    {
        Text = "Install update • Ping Watchdog";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = AuroraTheme.LabelFont;
        ClientSize = new Size(460, 200);
        MinimumSize = new Size(400, 220);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = $"Install version {version}?", AutoSize = true, Dock = DockStyle.Fill,
            Font = AuroraTheme.LabelFont, Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = monitoring
                ? "Restarting stops this monitoring session and reopens Ping Watchdog. Unsaved settings will be reviewed first."
                : "Ping Watchdog will save your setup and reopen. Unsaved settings will be reviewed first.",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16)
        }, 0, 1);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Margin = Padding.Empty
        };
        actions.Controls.Add(_restart);
        actions.Controls.Add(_later);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        // Enter/Escape both defer; restarting always requires explicit selection.
        AcceptButton = _later;
        CancelButton = _later;
        Shown += (_, _) => _later.Focus();
        AuroraTheme.Register(this);
        AuroraTheme.Apply(this);
    }

    internal void CheckLayout()
    {
        if (AcceptButton != _later || CancelButton != _later)
            throw new InvalidOperationException("Update confirmation must default to Later.");
        foreach (var button in new[] { _restart, _later })
            if (!button.Visible || !button.Parent!.ClientRectangle.Contains(button.Bounds))
                throw new InvalidOperationException("Update confirmation action is clipped.");
    }
}
