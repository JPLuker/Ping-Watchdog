namespace PingWatchdog;

internal sealed class DeleteDataDialog : Form
{
    private readonly TextBox _confirmation = new() { AccessibleName = "Type DELETE to confirm", Dock = DockStyle.Fill };
    private readonly AuroraButton _delete = new() { Text = "Delete and close", AutoSize = true, Enabled = false, Destructive = true, Primary = true };
    internal DeleteDataDialog(StorageLayout layout)
    {
        // FOCAL: explicitly confirmed deletion. Compact native surface with normal Escape cancellation.
        Text = "Delete saved data • Ping Watchdog";
        Width = 680; Height = 480; MinimumSize = new Size(600, 440);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        Font = AuroraTheme.LabelFont;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(AuroraTheme.Space6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "Permanently remove sites, preferences, history, backups and recovery reports from these folders. The app will close. External exports, other storage copies and the installation remain.",
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        root.Controls.Add(new TextBox
        {
            Text = string.Join(Environment.NewLine, layout.DeletionRoots), ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            AccessibleName = "Folders affected by deletion", Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        root.Controls.Add(new Label { Text = "Close other copies of Ping Watchdog, then type DELETE:", AutoSize = true, Dock = DockStyle.Fill }, 0, 2);
        _confirmation.Margin = new Padding(0, 8, 0, 12);
        root.Controls.Add(_confirmation, 0, 3);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var cancel = new AuroraButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        _delete.Click += (_, _) => { if (_confirmation.Text == "DELETE") { DialogResult = DialogResult.OK; Close(); } };
        _confirmation.TextChanged += (_, _) => _delete.Enabled = _confirmation.Text == "DELETE";
        buttons.Controls.Add(_delete); buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root); CancelButton = cancel; AcceptButton = cancel;
        AuroraTheme.Apply(this);
        Shown += (_, _) => { AuroraTheme.Register(this); _confirmation.Focus(); };
    }

    internal static void RunTests()
    {
        using var dialog = new DeleteDataDialog(StoragePaths.Current);
        dialog.Show();
        if (dialog._delete.Enabled || dialog.AcceptButton == dialog._delete)
            throw new InvalidOperationException("Deletion can be accepted without explicit confirmation.");
        dialog._confirmation.Text = "delete";
        if (dialog._delete.Enabled) throw new InvalidOperationException("Deletion confirmation was not exact.");
        dialog._confirmation.Text = "DELETE";
        if (!dialog._delete.Enabled) throw new InvalidOperationException("Explicit deletion confirmation was ignored.");
        dialog.DialogResult = DialogResult.Cancel;
        dialog.Close(); // No filesystem operation occurs in this dialog.
    }

    internal static async Task CaptureReview(Form owner, string path)
    {
        using var dialog = new DeleteDataDialog(StoragePaths.Current);
        dialog.Show(owner);
        await Task.Delay(75);
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path);
        dialog.Close();
    }
}
