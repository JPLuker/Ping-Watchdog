// The Wallboard: full window and display-only. Counts and the clock, the connected map, active outages
// and Ping activity. No toolbar (deviation "Display-only Wallboard"). Escape or F11 returns; right-click
// a host for its actions. It opens on the screen it was last on.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal sealed class WallboardWindow : Window
{
    private readonly MainWindow _main;
    private readonly WatchdogEngine _engine;
    private readonly HostActions _actions;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _counts = Ui.Text("", Tokens.Title3, weight: FontWeight.SemiBold);
    private readonly TextBlock _clock = Ui.Text("", Tokens.DisplaySize, weight: FontWeight.SemiBold);
    private readonly TextBlock _date = Ui.Text("", Tokens.Body, "Pw.Text2");
    private readonly TextBlock _stateText = Ui.Text("Idle", Tokens.Body, "Pw.Text2");
    private readonly Border _statePill;
    private readonly TopologyView _map = new();
    private readonly StackPanel _outages = new() { Spacing = Tokens.Space4 };
    private readonly TextBlock _noOutages = Ui.Text("No active outages", Tokens.Body, "Pw.Text2");
    private readonly PingActivityView _ping;
    private readonly Border _pingCard;
    private readonly PriorityBanner _banner;
    private readonly TextBlock _versionOverlay = VersionOverlay.Create();
    private bool _closed;
    private bool _movingScreen;

    public WallboardWindow(MainWindow main, WatchdogEngine engine, PriorityAlarm alarm)
    {
        _main = main;
        _engine = engine;
        _actions = new HostActions(engine, this, (message, undo) => main.ShowStatus(message, undo), null);
        Title = "Ping Watchdog Wallboard";
        Icon = new WindowIcon(BrandAssets.Logo);
        _ping = new PingActivityView(engine.ClearCommandLog, showHeader: false);
        _banner = new PriorityBanner(alarm);
        _statePill = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(Tokens.Space4, Tokens.Space1),
            Child = _stateText,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 220
        }.Res(Border.BackgroundProperty, "Pw.Surface3");
        _stateText.HorizontalAlignment = HorizontalAlignment.Center;

        var right = Ui.Column(Tokens.Space1, _clock, _date, _statePill);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        _clock.HorizontalAlignment = _date.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, Tokens.Space4) };
        header.Children.Add(_counts);
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, Tokens.Space3), Margin = new Thickness(0, 0, 0, Tokens.Space5) }
            .Res(Border.BorderBrushProperty, "Pw.Border");

        var outageTitle = Ui.Title("Active outages", Tokens.Title3);
        var outagePanel = Ui.Column(Tokens.Space4, outageTitle, _noOutages, _outages);
        var outageCard = Ui.Card(new ScrollViewer { Content = outagePanel }, Tokens.Space5);
        var mapCard = Ui.Card(_map, Tokens.Space3);
        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("*,330"), ColumnSpacing = Tokens.Space4 };
        middle.Children.Add(mapCard);
        Grid.SetColumn(outageCard, 1);
        middle.Children.Add(outageCard);

        var pingTitle = Ui.Text("Ping activity", Tokens.Body, "Pw.Text2");
        pingTitle.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        var pingStack = new DockPanel();
        DockPanel.SetDock(pingTitle, Dock.Top);
        pingStack.Children.Add(pingTitle);
        pingStack.Children.Add(_ping);
        _pingCard = Ui.Card(pingStack, Tokens.Space4);
        _pingCard.Height = 210;
        _pingCard.Margin = new Thickness(0, Tokens.Space4, 0, 0);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(Tokens.Space6, Tokens.Space5) };
        Grid.SetRow(_banner, 0);
        Grid.SetRow(headerBorder, 1);
        Grid.SetRow(middle, 2);
        Grid.SetRow(_pingCard, 3);
        layout.Children.Add(_banner);
        layout.Children.Add(headerBorder);
        layout.Children.Add(middle);
        layout.Children.Add(_pingCard);
        var root = new Panel();
        root.Children.Add(layout);
        root.Children.Add(_versionOverlay);
        Content = root;

        _map.HostContextRequested += (host, point) =>
        {
            var fresh = _engine.Snapshot().Sites.SelectMany(s => s.Hosts).FirstOrDefault(h => h.Key == host.Key) ?? host;
            _actions.ShowMenu(_map, point, new[] { fresh }, fromList: false);
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        Tokens.Changed += ThemeChanged;
        KeyDown += OnKey;
        Opened += async (_, _) =>
        {
            ApplyPreferences();
            UpdateClock();
            _clockTimer.Start();
            await PlaceOnRememberedScreenAsync();
            _map.Focus();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _clockTimer.Stop();
            Tokens.Changed -= ThemeChanged;
        };
        // The screen must be read before the window's platform handle goes away.
        Closing += (_, _) => RememberScreen();
        WindowState = WindowState.FullScreen;
    }

    internal TopologyView Map => _map;
    internal string CountsText => _counts.Text ?? "";
    internal string StateText => _stateText.Text ?? "";
    internal int OutageCount => _outages.Children.Count;
    internal bool PingVisible => _pingCard.IsVisible;
    internal PriorityBanner Banner => _banner;

    private void ThemeChanged() => Refresh(_engine.Snapshot());

    internal void ApplyPreferences() => _versionOverlay.IsVisible = _engine.Preferences.ShowVersionOverlay;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clock.Text = DisplayTime.Clock(now);
        _date.Text = now.ToString("dddd, MMMM d, yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    public void Refresh(WatchdogSnapshot snapshot)
    {
        if (_closed) return;
        var scoped = snapshot.SelectedSite is null
            ? snapshot.Sites
            : snapshot.Sites.Where(s => s.Name.Equals(snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase)).ToList();
        var hosts = scoped.SelectMany(s => s.Hosts).Where(h => h.Monitoring && !h.Disabled && !h.Snoozed).ToList();
        _counts.Text = $"{hosts.Count(h => h.State == HostState.Online)} online · {hosts.Count(h => h.State == HostState.Suspect)} suspect · {hosts.Count(h => h.State == HostState.Offline)} offline";
        _stateText.Text = snapshot.Monitoring ? "Monitoring" : "Idle";
        _stateText.Res(TextBlock.ForegroundProperty, snapshot.Monitoring ? "Pw.Success" : "Pw.Text2");
        _map.SetSites(scoped);

        var down = hosts.Where(h => h.State == HostState.Offline).OrderBy(h => h.OutageStarted ?? DateTime.MaxValue).ToList();
        _outages.Children.Clear();
        foreach (var host in down)
        {
            string duration = host.OutageStarted is { } since ? WatchdogEngine.FormatDuration(DateTime.Now - since) : "";
            var name = Ui.Text(host.DisplayName);
            var detail = Ui.Text(host.Site + (duration.Length > 0 ? " · " + duration : ""), Tokens.Body, "Pw.Text2");
            ToolTip.SetTip(name, host.Address);
            _outages.Children.Add(Ui.Column(2, name, detail));
        }
        _noOutages.IsVisible = down.Count == 0;
        _pingCard.IsVisible = snapshot.Settings.WallboardShowCli;
        _ping.Refresh(snapshot.Commands, snapshot.Monitoring ? "Waiting for replies" : "No ping activity");
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        switch (e.Key)
        {
            case Key.Escape:
            case Key.F11:
                Close(); break;
            case Key.P:
                _main.ToggleMonitoring(); break;
            case Key.C:
                var config = _engine.Snapshot().Settings;
                config.WallboardShowCli = !config.WallboardShowCli;
                _engine.ApplySettings(config);
                Refresh(_engine.Snapshot());
                break;
            case Key.M:
                _ = MoveToNextScreenAsync(); break;
            default:
                return;
        }
        e.Handled = true;
    }

    internal static string ScreenKey(Avalonia.Platform.Screen screen) =>
        $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}";

    internal static int NextScreenIndex(int current, int count) => count <= 1 ? 0 : (Math.Max(-1, current) + 1) % count;

    private void RememberScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        string key = ScreenKey(screen);
        if (_engine.Preferences.WallboardScreen == key) return;
        var prefs = _engine.Preferences.Copy();
        prefs.WallboardScreen = key;
        try { _engine.SavePreferences(prefs); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task PlaceOnRememberedScreenAsync()
    {
        string key = _engine.Preferences.WallboardScreen;
        if (key.Length == 0 || Screens.All.Count <= 1) return;
        var target = Screens.All.FirstOrDefault(s => ScreenKey(s) == key);
        if (target is null || Screens.ScreenFromWindow(this) == target) return;
        await MoveToAsync(target);
    }

    private async Task MoveToNextScreenAsync()
    {
        if (_movingScreen || Screens.All.Count <= 1) return;
        var screens = Screens.All;
        var current = Screens.ScreenFromWindow(this);
        await MoveToAsync(screens[NextScreenIndex(current is null ? -1 : screens.ToList().IndexOf(current), screens.Count)]);
        RememberScreen();
    }

    private async Task MoveToAsync(Avalonia.Platform.Screen target)
    {
        _movingScreen = true;
        try
        {
            WindowState = WindowState.Normal;
            await Task.Delay(80);
            if (_closed) return;
            Position = target.Bounds.Position;
            await Task.Delay(80);
            if (_closed) return;
            WindowState = WindowState.FullScreen;
        }
        finally { _movingScreen = false; }
    }
}
