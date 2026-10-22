using System.Media;

namespace PingWatchdog;

// One unacknowledged state change on a priority host. A host has at most one: a newer change replaces it.
internal sealed record PriorityAlert(string Id, string Site, string Address, string DisplayHost, bool Offline, DateTime At)
{
    internal string Summary => $"{DisplayHost} · {Site} {(Offline ? "went offline" : "is back online")} · {DisplayTime.Clock(At)}";
}

// Rings until every priority alert is acknowledged: immediately on a new alert, then every RepeatInterval.
internal sealed class PriorityAlarm : IDisposable
{
    internal static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(15);
    private readonly List<PriorityAlert> _active = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)RepeatInterval.TotalMilliseconds };
    private readonly Action<string?> _play;
    internal event Action? Changed;
    // Empty means the Windows alarm sound.
    internal string SoundPath { get; set; } = "";
    internal IReadOnlyList<PriorityAlert> Active => _active;
    internal bool Ringing => _timer.Enabled;
    internal int RingCount { get; private set; }

    internal PriorityAlarm(Action<string?>? play = null)
    {
        _play = play ?? PlaySound;
        _timer.Tick += (_, _) => Ring();
    }

    internal PriorityAlert Raise(string site, string address, string displayHost, bool offline, DateTime at)
    {
        _active.RemoveAll(a => a.Site.Equals(site, StringComparison.OrdinalIgnoreCase) && a.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
        var alert = new PriorityAlert(Guid.NewGuid().ToString("N"), site, address, displayHost, offline, at);
        _active.Add(alert);
        Ring();
        _timer.Stop(); _timer.Start();
        Changed?.Invoke();
        return alert;
    }

    internal bool Acknowledge(string id)
    {
        if (_active.RemoveAll(a => a.Id == id) == 0) return false;
        if (_active.Count == 0) _timer.Stop();
        Changed?.Invoke();
        return true;
    }

    internal void AcknowledgeAll()
    {
        if (_active.Count == 0) return;
        _active.Clear(); _timer.Stop();
        Changed?.Invoke();
    }

    // A host that is no longer priority, or was removed, should not keep ringing.
    internal void Forget(Func<PriorityAlert, bool> stale)
    {
        if (_active.RemoveAll(a => stale(a)) == 0) return;
        if (_active.Count == 0) _timer.Stop();
        Changed?.Invoke();
    }

    internal void Ring()
    {
        if (_active.Count == 0) return;
        RingCount++;
        _play(SoundPath);
    }

    internal static string DefaultAlarmPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");

    internal static void PlaySound(string? path)
    {
        string chosen = !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : DefaultAlarmPath;
        try
        {
            if (File.Exists(chosen)) { using var player = new SoundPlayer(chosen); player.Play(); return; }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or TimeoutException) { }
        SystemSounds.Hand.Play();
    }

    public void Dispose() => _timer.Dispose();
}

// Persistent bar shown while priority alerts are unacknowledged, in the main window and on the Wallboard.
internal sealed class PriorityBanner : Panel
{
    private readonly PriorityAlarm _alarm;
    private readonly Label _message = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Tag = "primaryText", AutoEllipsis = true };
    private readonly AuroraButton _acknowledge = new() { Text = "Acknowledge", AutoSize = true };
    private readonly AuroraButton _acknowledgeAll = new() { Text = "Acknowledge all", AutoSize = true, Ghost = true };

    internal PriorityBanner(PriorityAlarm alarm)
    {
        _alarm = alarm;
        Dock = DockStyle.Top; Visible = false; Tag = "card";
        AccessibleRole = AccessibleRole.Alert;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Tag = "well" };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _acknowledge.Primary = true;
        _acknowledge.Anchor = _acknowledgeAll.Anchor = AnchorStyles.Right;
        layout.Controls.Add(_message, 0, 0);
        layout.Controls.Add(_acknowledgeAll, 1, 0);
        layout.Controls.Add(_acknowledge, 2, 0);
        Controls.Add(layout);
        _acknowledge.Click += (_, _) => { if (_alarm.Active.FirstOrDefault() is { } first) _alarm.Acknowledge(first.Id); };
        _acknowledgeAll.Click += (_, _) => _alarm.AcknowledgeAll();
        _alarm.Changed += Sync;
        Disposed += (_, _) => _alarm.Changed -= Sync;
        Sync();
    }

    internal string MessageText => _message.Text;
    internal bool AcknowledgeAllVisible => _acknowledgeAll.Visible;

    private void Sync()
    {
        if (IsDisposed) return;
        var active = _alarm.Active;
        float scale = DeviceDpi / 96f;
        Height = (int)(56 * scale);
        Padding = new Padding((int)(20 * scale), (int)(6 * scale), (int)(12 * scale), (int)(6 * scale));
        if (active.Count == 0) { Visible = false; return; }
        var first = active[0];
        _message.Text = "Priority · " + first.Summary + (active.Count > 1 ? $"  (+{active.Count - 1} more)" : "");
        AccessibleName = _message.Text;
        _acknowledgeAll.Visible = active.Count > 1;
        Visible = true;
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var p = AuroraTheme.Palette;
        e.Graphics.Clear(p.Surface2);
        var first = _alarm.Active.FirstOrDefault();
        if (first is null) return;
        // Stripe colour plus "went offline"/"is back online" text, so state never relies on colour alone.
        using var stripe = new SolidBrush(first.Offline ? p.Danger : p.Teal);
        e.Graphics.FillRectangle(stripe, 0, 0, Math.Max(4, (int)(6 * DeviceDpi / 96f)), Height);
    }
}
