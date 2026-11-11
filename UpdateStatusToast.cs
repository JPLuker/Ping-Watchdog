using System.Drawing.Drawing2D;

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
        _updateToast = new UpdateToastPanel { Visible = false, Tag = "overlay", Padding = new Padding(16),
            Size = new Size(360, 160), Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AccessibleName = "Update status", AccessibleRole = AccessibleRole.Alert };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Tag = "overlay" };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        content.Controls.Add(new Label { Text = "Updates", AutoSize = true, Font = AppTheme.LabelFont }, 0, 0);
        _updateToastBody = new Label { Dock = DockStyle.Fill, Font = AppTheme.CaptionFont, AutoEllipsis = true };
        content.Controls.Add(_updateToastBody, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty, Tag = "overlay" };
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
        AppTheme.Register(owner);
        AppTheme.Apply(owner);
        _updateToast.BringToFront();
        _updateToast.PerformLayout();
        _updateToast.Invalidate(true);
        _updateToastTimer!.Stop();
        if (!persistent) _updateToastTimer.Start();
    }
}

// FOCAL: update status. Opaque elevated surface with a visible edge over a dense map.
internal sealed class UpdateToastPanel : Panel
{
    internal UpdateToastPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    private int CornerRadius => Math.Max(1, LogicalToDeviceUnits(AppTheme.RadiusCard));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ClipCorners();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ClipCorners();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ClipCorners();
    }

    private void ClipCorners()
    {
        if (Width <= 0 || Height <= 0) return;
        using var shape = WallboardStyle.Rounded(ClientRectangle, CornerRadius);
        Region = new Region(shape);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = ClientRectangle;
        bounds.Inflate(-1, -1);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var edge = WallboardStyle.Rounded(bounds, Math.Max(1, CornerRadius - 1));
        var palette = AppTheme.Palette;
        var edgeColor = SystemInformation.HighContrast ? SystemColors.WindowText :
            Color.FromArgb((3 * BackColor.R + palette.Text.R) / 4,
                (3 * BackColor.G + palette.Text.G) / 4, (3 * BackColor.B + palette.Text.B) / 4);
        using var pen = new Pen(edgeColor, Math.Max(1, LogicalToDeviceUnits(1)));
        e.Graphics.DrawPath(pen, edge);
    }
}
