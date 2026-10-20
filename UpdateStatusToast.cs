namespace PingWatchdog;

public sealed partial class MainForm
{
    private string _updateStatusText = "Ready to check for updates.";
    private Panel? _updateToast;
    private Label? _updateToastBody;
    private Button? _updateToastRestart;
    private System.Windows.Forms.Timer? _updateToastTimer;

    private void InitializeUpdateToast()
    {
        if (_updateToast is { IsDisposed: false }) return;
        _updateToast = new Panel { Visible = false, Tag = "card", Padding = new Padding(16),
            Size = new Size(360, 160), Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AccessibleName = "Update status", AccessibleRole = AccessibleRole.Alert };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Tag = "card" };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        content.Controls.Add(new Label { Text = "Updates", AutoSize = true, Font = AuroraTheme.LabelFont }, 0, 0);
        _updateToastBody = new Label { Dock = DockStyle.Fill, Font = AuroraTheme.CaptionFont, AutoEllipsis = true };
        content.Controls.Add(_updateToastBody, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty, Tag = "card" };
        var dismiss = HostUi.Button("Dismiss", () => { if (_updateToast != null) _updateToast.Visible = false; _updateToastTimer?.Stop(); });
        _updateToastRestart = HostUi.Button("Restart now", RestartToApplyPendingUpdate);
        _updateToastRestart.Visible = false;
        actions.Controls.Add(dismiss);
        actions.Controls.Add(_updateToastRestart);
        content.Controls.Add(actions, 0, 2);
        _updateToast.Controls.Add(content);
        Controls.Add(_updateToast);
        _updateToastTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        _updateToastTimer.Tick += (_, _) => { _updateToastTimer.Stop(); if (_updateToast != null) _updateToast.Visible = false; };
        _updateToast.Disposed += (_, _) => _updateToastTimer?.Dispose();
        _updateToast.Resize += (_, _) =>
        {
            if (_updateToast.Width < 1 || _updateToast.Height < 1) return;
            using var shape = WallboardStyle.Rounded(_updateToast.ClientRectangle, AuroraTheme.RadiusCard);
            var previous = _updateToast.Region;
            _updateToast.Region = new Region(shape);
            previous?.Dispose();
        };
    }

    private void SetUpdateStatus(string text, bool notify = false, bool persistent = false)
    {
        _updateStatusText = text;
        _checkUpdateButton.Text = "Check for updates";
        if (_updateToastBody != null && !_updateToastBody.IsDisposed) _updateToastBody.Text = text;
        if (!notify || IsDisposed || Disposing) return;
        InitializeUpdateToast();
        var owner = _wallboardForm is { Visible: true, IsDisposed: false } ? (Form)_wallboardForm : this;
        if (_updateToast!.Parent != owner) owner.Controls.Add(_updateToast);
        float scale = owner.DeviceDpi / 96f;
        _updateToast.Size = new Size(Math.Min((int)(360 * scale), owner.ClientSize.Width - 32), (int)(160 * scale));
        _updateToast.Location = new Point(owner.ClientSize.Width - _updateToast.Width - 16, owner.ClientSize.Height - _updateToast.Height - 16);
        _updateToastBody!.Text = text;
        _updateToastRestart!.Visible = _pendingUpdateManager != null && _pendingUpdateInfo != null;
        _updateToast.Visible = true;
        _updateToast.BringToFront();
        AuroraTheme.Register(owner);
        AuroraTheme.Apply(owner);
        _updateToastTimer!.Stop();
        if (!persistent) _updateToastTimer.Start();
    }
}
