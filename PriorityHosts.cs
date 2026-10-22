using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace PingWatchdog;

public sealed partial class MainForm
{
    private const string PriorityNotificationGroup = "priority";
    private PriorityAlarm _priorityAlarm = null!;
    private PriorityBanner? _priorityBanner;
    private readonly HashSet<string> _priorityToasts = new();

    private void InitializePriorityAlarm()
    {
        // Test windows suppress notifications: keep the alarm logic, skip the sound.
        _priorityAlarm = new PriorityAlarm(_suppressNotifications ? _ => { } : null);
        _priorityBanner = new PriorityBanner(_priorityAlarm);
        Controls.Add(_priorityBanner);
        // Fill must dock last, so the banner takes the top edge and the workspace keeps the rest.
        _rootLayout?.BringToFront();
        _priorityAlarm.Changed += SyncPriorityNotifications;
        Disposed += (_, _) => _priorityAlarm.Dispose();
    }

    private bool IsPriorityHost(string site, string address) =>
        GetHostOptions(FindSite(site), address).Priority;

    private void SetHostPriority(string id, bool priority)
    {
        var current = HostSnapshot().FirstOrDefault(h => h.Options.Id == id);
        if (current == null) return;
        var options = current.Options.Copy(); options.Priority = priority;
        SaveManagedHosts(new() { current with { Options = options } });
        if (!priority) _priorityAlarm.Forget(a => a.Site.Equals(current.Site, StringComparison.OrdinalIgnoreCase) && a.Address.Equals(current.Address, StringComparison.OrdinalIgnoreCase));
    }

    private void RaisePriorityAlert(HostMonitor host, bool offline)
    {
        var alert = _priorityAlarm.Raise(host.Site, host.Address, DescribeMonitoredHost(host), offline, DateTime.Now);
        if (!_suppressNotifications) ShowPriorityNotification(alert);
    }

    // Stays on screen (alarm scenario) with its own Acknowledge button; the app plays the sound.
    private void ShowPriorityNotification(PriorityAlert alert)
    {
        string title = "Priority · " + (alert.Offline ? "Offline" : "Back online");
        string body = $"{alert.DisplayHost} · {alert.Site} · {DisplayTime.Clock(alert.At)}";
        if (_appNotificationsAvailable)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddArgument("action", "open")
                    .AddText(title)
                    .AddText(body)
                    .AddButton(new AppNotificationButton("Acknowledge").AddArgument("action", "acknowledge").AddArgument("alert", alert.Id))
                    .SetScenario(AppNotificationScenario.Alarm)
                    .MuteAudio()
                    .BuildNotification();
                notification.Tag = alert.Id;
                notification.Group = PriorityNotificationGroup;
                AppNotificationManager.Default.Show(notification);
                _priorityToasts.Add(alert.Id);
                return;
            }
            catch { _appNotificationsAvailable = false; }
        }
        _trayIcon.ShowBalloonTip(10000, title, body + " · Open Ping Watchdog to acknowledge.", alert.Offline ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    // Acknowledged or replaced alerts take their Windows notification with them.
    private void SyncPriorityNotifications()
    {
        foreach (string id in _priorityToasts.Where(id => _priorityAlarm.Active.All(a => a.Id != id)).ToList())
        {
            _priorityToasts.Remove(id);
            try { _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(id, PriorityNotificationGroup); }
            catch { }
        }
    }

    // Called on the UI thread for any Windows notification activation.
    internal void HandleNotificationActivation(IReadOnlyDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue("action", out var action) && action == "acknowledge" &&
            arguments.TryGetValue("alert", out var id))
        {
            _priorityAlarm.Acknowledge(id);
            return;
        }
        RestoreFromTray();
    }

    internal IReadOnlyList<PriorityAlert> ActivePriorityAlerts => _priorityAlarm.Active;
}
