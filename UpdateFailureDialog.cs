namespace PingWatchdog;

internal sealed class UpdateFailureDialog : Form
{
    // FOCAL: retry a failed restart. Keep the downloaded package and defer by default.
    internal readonly Button Retry = new AuroraButton { Text = "Retry restart", AutoSize = true, DialogResult = DialogResult.Retry };
    internal readonly Button Later = new AuroraButton { Text = "Later", AutoSize = true, DialogResult = DialogResult.Cancel };

    internal UpdateFailureDialog(string error)
    {
        Text = "Update restart failed • Ping Watchdog";
        ClientSize = new Size(560, 280);
        MinimumSize = new Size(480, 280);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = AuroraTheme.LabelFont;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(AuroraTheme.Space4)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "The update is still downloaded. Retry the restart now or apply it later.",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        root.Controls.Add(new TextBox
        {
            Text = error, ReadOnly = true, Multiline = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            AccessibleName = "Update restart error", TabIndex = 0,
            Margin = Padding.Empty
        }, 0, 1);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0), TabIndex = 1
        };
        Later.TabIndex = 0;
        Retry.TabIndex = 1;
        actions.Controls.Add(Retry);
        actions.Controls.Add(Later);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = CancelButton = Later;
        Later.Click += (_, _) => Close();
        AuroraTheme.Register(this);
        Shown += (_, _) => Later.Focus();
    }

    internal static async Task Review(Form owner, string output)
    {
        using var dialog = new UpdateFailureDialog(string.Join(Environment.NewLine,
            Enumerable.Repeat("The updater could not restart Ping Watchdog. The update file is in use.", 20)));
        dialog.Show(owner);
        dialog.Size = dialog.MinimumSize;
        await Task.Delay(50);
        CompactDialog.CheckBounds(dialog);
        if (dialog.Owner != owner || !dialog.Later.Focused || dialog.AcceptButton != dialog.Later || dialog.CancelButton != dialog.Later)
            throw new InvalidOperationException("Update failure lost its owner or safe keyboard defaults.");
        if (dialog.Retry.DialogResult != DialogResult.Retry)
            throw new InvalidOperationException("Update failure has no retry action.");
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(output, "dialog-update-failure-" + AuroraTheme.Mode + ".png"));
        DialogReviewTests.Escape(dialog);
    }
}
