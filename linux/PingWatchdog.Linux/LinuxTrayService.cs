using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal static class LinuxTrayHost
{
    public static async Task<bool> AvailableAsync()
    {
        // Native desktop IPC only. No shell is involved; a missing tool/host means normal minimize.
        foreach (string tool in new[] { "gdbus", "dbus-send" })
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            string[] args = tool == "gdbus"
                ? new[] { "call", "--session", "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus", "--method", "org.freedesktop.DBus.NameHasOwner", "org.kde.StatusNotifierWatcher" }
                : new[] { "--session", "--type=method_call", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.NameHasOwner", "string:org.kde.StatusNotifierWatcher" };
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            try
            {
                if (!process.Start()) continue;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(timeout.Token);
                string text = await output;
                await error;
                if (process.ExitCode == 0) return text.Contains("true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            }
        }
        return false;
    }
}

internal sealed class LinuxTrayService : IDisposable
{
    private readonly MainWindow _main;
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _update;
    private readonly NativeMenuItem _monitor;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Func<Task<bool>> _hostAvailable;
    private bool _disposed;
    private bool _refreshing;
    private int _restoreGeneration;
    public bool Available { get; private set; }
    public string Status => Available ? "System tray available" : "No tray host detected • normal taskbar minimization";

    public LinuxTrayService(Application app, MainWindow main, WatchdogEngine engine, LinuxUpdateService updates, Func<Task<bool>>? hostAvailable = null)
    {
        _main = main; _engine = engine; _updates = updates;
        _hostAvailable = hostAvailable ?? LinuxTrayHost.AvailableAsync;
        var menu = new NativeMenu();
        menu.Items.Add(Item("Open Ping Watchdog", Restore));
        menu.Items.Add(Item("Open Wallboard", () => { Restore(); _main.OpenWallboard(); }));
        _update = Item("Check for Updates", async () => { Restore(); await _main.RunUpdateActionAsync(); });
        menu.Items.Add(_update);
        _monitor = Item("Start Monitoring", () => _main.ToggleMonitoring());
        menu.Items.Add(_monitor);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Exit", () => _main.ExitApplication()));
        _icon = new TrayIcon { Icon = new WindowIcon(BrandAssets.Logo), ToolTipText = "Ping Watchdog", Menu = menu, IsVisible = true };
        _icon.Clicked += (_, _) => Restore();
        TrayIcon.SetIcons(app, new TrayIcons { _icon });
        _engine.Changed += StateChanged;
        _updates.Changed += StateChanged;
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private static NativeMenuItem Item(string text, Action action)
    {
        var item = new NativeMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private void StateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        UpdateMenu();
        if (!_engine.Config.MinimizeToTray && !_main.IsVisible) Restore();
    });

    private void UpdateMenu()
    {
        _update.Header = _updates.ActionText;
        _monitor.Header = _engine.Monitoring ? "Stop Monitoring" : "Start Monitoring";
        _icon.ToolTipText = _engine.Monitoring ? "Ping Watchdog • Monitoring" : "Ping Watchdog • Idle";
    }

    public async Task RefreshAsync()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            bool available = await _hostAvailable();
            if (_disposed) return;
            Available = available && _icon.NativeMenuExporter is not null;
            UpdateMenu();
            if (!Available && !_main.IsVisible) Restore();
        }
        finally { _refreshing = false; }
    }

    public async Task MinimizeAsync(bool requested = false)
    {
        // Wayland may report Normal again before the asynchronous tray-host check finishes.
        bool minimizeRequested = requested || _main.WindowState == WindowState.Minimized;
        int generation = _restoreGeneration;
        await RefreshAsync();
        if (_disposed || !minimizeRequested || generation != _restoreGeneration || !Available || !_engine.Config.MinimizeToTray) return;
        if (_main.CommitHostEdits()) _main.Hide();
    }

    public void Restore()
    {
        if (_disposed) return;
        _restoreGeneration++;
        _main.WindowState = WindowState.Normal;
        _main.Show();
        _main.Activate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _engine.Changed -= StateChanged;
        _updates.Changed -= StateChanged;
        _icon.Dispose();
    }
}
