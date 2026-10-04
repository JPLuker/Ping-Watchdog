using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingWatchdog.Shared;

namespace PingWatchdog.Linux;

internal sealed partial class WallboardWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
    private readonly MainWindow _main;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Canvas _topology = new() { Background = Theme.Brush("#070C12") };
    private readonly ScrollViewer _topologyScroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };
    private readonly TextBlock _clock = Theme.Label("", 24, FontWeight.Bold);
    private readonly TextBlock _date = Theme.Label("", 11, color: Theme.Muted);
    private readonly TextBlock _state = Theme.Label("IDLE", 11, FontWeight.Bold);
    private readonly TextBlock _stats = Theme.Label("", 12, FontWeight.Bold);
    private readonly TextBlock _updateStatus = Theme.Label("", 10, color: Theme.Muted);
    private readonly TextBlock _screenStatus = Theme.Label("", 10, color: Theme.Muted);
    private readonly CliTraceView _cli;
    private readonly StackPanel _ops = new() { Width = 340, Spacing = 8, IsVisible = false };
    private readonly ComboBox _site = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _hosts = new() { AcceptsReturn = true, MinHeight = 110, MaxHeight = 180, FontFamily = new FontFamily("monospace") };
    private readonly List<string?> _siteKeys = new();
    private readonly HostEditorBinding _editor;
    private readonly ListBox _hostList = new() { Height = 190, Background = Theme.Panel };
    private readonly ObservableCollection<HostRow> _hostRows = new();
    private readonly Button _editLabel = Theme.Button("Edit Label");
    private readonly Button _clearLabel = Theme.Button("Clear Label");
    private readonly Button _rename = Theme.Button("Rename");
    private readonly Button _delete = Theme.Button("Delete");
    private readonly Button _startStop = Theme.Button("Start Monitoring", true);
    private readonly Button _updateButton = Theme.Button("Check for Updates");
    private readonly NumericUpDown _interval = Number(1, 300, 2, 1);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250);
    private readonly NumericUpDown _downAfter = Number(2, 20, 3, 1);
    private readonly NumericUpDown _recoverAfter = Number(1, 20, 2, 1);
    private readonly ComboBox _range = new() { ItemsSource = new[] { "24 hours", "7 days", "30 days", "All stored history" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _hideSuspects = new() { Content = "Hide suspect history" };
    private readonly TextBlock _outageSummary = Theme.Label("", 11, FontWeight.Bold);
    private readonly TextBlock _historySummary = Theme.Label("", 10, color: Theme.Muted);
    private readonly TextBlock _activeOutages = Theme.Label("", 11);
    private readonly TextBlock _recentHistory = Theme.Label("", 10);
    private Border _opsCard = null!;
    private Border _reportsCard = null!;
    private (int, int, int, int)? _timingBaseline;
    private bool _closed;
    private bool _showCli;
    private bool _loading;
    private bool _movingScreen;

    public WallboardWindow(WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        _engine = engine;
        _updates = updates;
        _main = main;
        _cli = new CliTraceView(_engine.ClearCommandLog);
        _editor = new HostEditorBinding(engine, _hosts, error => _ = _main.AlertAsync(error, owner: this));
        Title = "Ping Watchdog Wallboard";
        Icon = new WindowIcon(BrandAssets.Logo);
        WindowState = WindowState.FullScreen;
        SystemDecorations = SystemDecorations.None;
        Background = Theme.Window;
        Foreground = Theme.Text;
        _showCli = engine.Config.WallboardShowCli;
        _hostList.ItemsSource = _hostRows;
        _hostList.ItemTemplate = new FuncDataTemplate<HostRow>((_, _) => new TextBlock
        {
            FontFamily = new FontFamily("monospace"), FontSize = 11,
            Width = 300, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3),
            [!TextBlock.TextProperty] = new Binding(nameof(HostRow.WallboardText)),
            [!TextBlock.ForegroundProperty] = new Binding(nameof(HostRow.Foreground))
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_hostList, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        Content = BuildLayout();
        _site.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            int index = _site.SelectedIndex;
            if (!_editor.Commit()) { Refresh(); return; }
            _engine.SelectedSite = index >= 0 && index < _siteKeys.Count ? _siteKeys[index] : null;
            Refresh();
        };
        _range.SelectionChanged += (_, _) => SaveHistoryFilters();
        _hideSuspects.IsCheckedChanged += (_, _) => SaveHistoryFilters();
        _ops.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            _opsCard.IsVisible = _ops.IsVisible;
            _reportsCard.IsVisible = true;
        };
        _hostList.SelectionChanged += (_, _) => UpdateLabelActions();
        _hostList.DoubleTapped += async (_, _) => await EditLabelAsync();
        _hostList.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(_hostList).Properties.IsRightButtonPressed) return;
            if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item)
                _hostList.SelectedItem = item.DataContext;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var editMenu = new MenuItem { Header = "Edit Label..." };
        var clearMenu = new MenuItem { Header = "Clear Label" };
        editMenu.Click += async (_, _) => await EditLabelAsync();
        clearMenu.Click += async (_, _) => await ClearLabelAsync();
        _hostList.ContextMenu = new ContextMenu { ItemsSource = new[] { editMenu, clearMenu } };
        _editLabel.Click += async (_, _) => await EditLabelAsync();
        _clearLabel.Click += async (_, _) => await ClearLabelAsync();
        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closing += (_, _) => _editor.Commit();
        Closed += (_, _) => { _closed = true; _timer.Stop(); _main.Show(); _main.Activate(); };
        KeyDown += OnKeyDown;
    }

    private void BuildOpsPanel()
    {
        _ops.Children.Add(Theme.Label("Operations", 17, FontWeight.Bold));
        _ops.Children.Add(Theme.Label("Site / topology scope", 10, FontWeight.Bold, Theme.Muted));
        _ops.Children.Add(_site);
        var add = Theme.Button("+ Add Site"); add.Click += async (_, _) => await AddSiteAsync();
        _rename.Click += async (_, _) => await RenameSiteAsync();
        _delete.Click += async (_, _) => await DeleteSiteAsync();
        _ops.Children.Add(new WrapPanel { Children = { add, _rename, _delete } });
        var organize = Theme.Button("Folders / Organization"); organize.Click += (_, _) => new OrganizationWindow(_engine).Show(this);
        _ops.Children.Add(organize);
        _ops.Children.Add(Theme.Label("Hosts · one address per line", 10, FontWeight.Bold, Theme.Muted));
        _ops.Children.Add(_hosts);
        var apply = Theme.Button("Apply Hosts", true); apply.Click += (_, _) => { _editor.Commit(); Refresh(); };
        _ops.Children.Add(apply);
        _ops.Children.Add(Theme.Label("Live hosts · all hosts in the selected scope", 11, FontWeight.Bold));
        _ops.Children.Add(new HostTableView(_hostList, compact: true) { Height = 190 });
        _ops.Children.Add(new WrapPanel { Children = { _editLabel, _clearLabel } });
        _ops.Children.Add(Theme.Label("Timing / thresholds · locked while monitoring", 10, FontWeight.Bold, Theme.Muted));
        var timing = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        string[] labels = { "Interval (seconds)", "Timeout (milliseconds)", "Failures before down", "Replies before recovery" };
        NumericUpDown[] values = { _interval, _timeout, _downAfter, _recoverAfter };
        for (int i = 0; i < labels.Length; i++)
        {
            var label = Theme.Label(labels[i], 11); Grid.SetRow(label, i); timing.Children.Add(label);
            Grid.SetRow(values[i], i); Grid.SetColumn(values[i], 1); timing.Children.Add(values[i]);
        }
        _ops.Children.Add(timing);
        var saveTiming = Theme.Button("Apply Timing"); saveTiming.Click += (_, _) => ApplyTiming();
        _ops.Children.Add(saveTiming);
        var import = Theme.Button("Import Config..."); import.Click += async (_, _) => { if (_editor.Commit()) await _main.ImportConfigAsync(this); Refresh(); };
        var export = Theme.Button("Export Config..."); export.Click += async (_, _) => { if (_editor.Commit()) await _main.ExportConfigAsync(this); };
        _ops.Children.Add(new WrapPanel { Children = { import, export } });
        var clear = Theme.Button("Clear CLI"); clear.Click += (_, _) => { _engine.ClearCommandLog(); Refresh(); };
        _ops.Children.Add(clear);
        _updateStatus.TextWrapping = TextWrapping.Wrap;
        _ops.Children.Add(_updateStatus);
    }

    private Control BuildReports()
    {
        _activeOutages.TextWrapping = _recentHistory.TextWrapping = TextWrapping.Wrap;
        var reports = new StackPanel { Spacing = 10, Children =
        {
            Theme.Label("ACTIVE OUTAGES", 12, FontWeight.Bold), _outageSummary, _activeOutages,
            Theme.Label("OUTAGE HISTORY", 12), _historyFilter, _historySummary, _recentHistory
        } };
        var history = Theme.Button("Full History / CSV"); history.Click += (_, _) => new HistoryWindow(_engine).Show(this);
        reports.Children.Add(history);
        return new ScrollViewer { Content = reports, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private void Refresh()
    {
        if (_loading || _closed) return;
        _loading = true;
        try
        {
            var snapshot = _engine.Snapshot();
            _clock.Text = DateTime.Now.ToString("h:mm:ss tt");
            _date.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
            _state.Text = snapshot.Monitoring ? "LIVE MONITORING" : "IDLE / CONFIG VIEW";
            _state.Foreground = snapshot.Monitoring ? Theme.Green : Theme.Cyan;
            _startStop.Content = snapshot.Monitoring ? "Stop Monitoring" : "Start Monitoring";
            var hosts = snapshot.Hosts;
            int[] counts = { hosts.Count, hosts.Count(h => h.State == HostState.Online), hosts.Count(h => h.State == HostState.Suspect), hosts.Count(h => h.State == HostState.Offline) };
            for (int i = 0; i < counts.Length; i++) _statValues[i].Text = counts[i].ToString();
            _stats.Text = $"{snapshot.SelectedSite ?? "ALL SITES"}   TOTAL {hosts.Count}   ONLINE {hosts.Count(h => h.State == HostState.Online)}   SUSPECT {hosts.Count(h => h.State == HostState.Suspect)}   OFFLINE {hosts.Count(h => h.State == HostState.Offline)}";
            _showCli = snapshot.Settings.WallboardShowCli;
            _cli.IsVisible = _showCli;
            _cli.Refresh(snapshot.Commands);
            RefreshSiteCombo(snapshot);
            _editor.Refresh(snapshot);
            RefreshHostRows(snapshot);
            _rename.IsEnabled = _delete.IsEnabled = snapshot.SelectedSite is not null;
            RefreshTiming(snapshot);
            _range.SelectedIndex = snapshot.Settings.EventHistoryHours switch { 24 => 0, 168 => 1, 720 => 2, _ => 3 };
            _hideSuspects.IsChecked = snapshot.Settings.HideSuspectEvents;
            RefreshReports(snapshot);
            _updateButton.Content = _updates.ActionText;
            _updateStatus.Text = _updates.Status;
            ToolTip.SetTip(_updateButton, _updates.Status);
            var screens = Screens.All;
            int screenIndex = screens.ToList().IndexOf(Screens.ScreenFromWindow(this)!);
            _screenStatus.Text = $"Monitor {Math.Max(0, screenIndex) + 1} of {screens.Count} · {(_ops.IsVisible ? "Operations open" : "Outages and history follow the selected site")}";
            DrawTopology(snapshot);
        }
        finally { _loading = false; }
    }

    private void RefreshSiteCombo(WatchdogSnapshot snapshot)
    {
        _siteKeys.Clear();
        var items = new List<string> { "All Sites" };
        _siteKeys.Add(null);
        foreach (var site in snapshot.Sites)
        {
            items.Add(string.IsNullOrWhiteSpace(site.FolderPath) ? site.Name : $"{site.FolderPath.Replace("/", " › ")} › {site.Name}");
            _siteKeys.Add(site.Name);
        }
        if (_site.ItemsSource is not IEnumerable<string> current || !current.SequenceEqual(items)) _site.ItemsSource = items;
        int selected = snapshot.SelectedSite is null ? 0 : _siteKeys.FindIndex(key => string.Equals(key, snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase));
        _site.SelectedIndex = Math.Max(0, selected);
    }

    private void RefreshHostRows(WatchdogSnapshot snapshot)
    {
        string? selected = (_hostList.SelectedItem as HostRow)?.Key;
        var scroll = _hostList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.Offset;
        LiveRows.Hosts(_hostRows, snapshot.Hosts);
        if (selected is not null) _hostList.SelectedItem = _hostRows.FirstOrDefault(row => row.Key.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (offset is { } saved && scroll is not null) scroll.Offset = saved;
        UpdateLabelActions();
    }

    private void UpdateLabelActions()
    {
        var host = (_hostList.SelectedItem as HostRow)?.Host;
        _editLabel.IsEnabled = host is not null;
        _clearLabel.IsEnabled = host is not null && !string.IsNullOrWhiteSpace(host.Label);
    }

    private void RefreshTiming(WatchdogSnapshot snapshot)
    {
        bool enabled = !snapshot.Monitoring;
        foreach (var control in new[] { _interval, _timeout, _downAfter, _recoverAfter }) control.IsEnabled = enabled;
        var timing = (snapshot.Settings.PingIntervalSeconds, snapshot.Settings.PingTimeoutMs, snapshot.Settings.FailureThreshold, snapshot.Settings.RecoveryThreshold);
        if (!enabled || _timingBaseline == timing) return;
        _timingBaseline = timing;
        _interval.Value = timing.PingIntervalSeconds;
        _timeout.Value = timing.PingTimeoutMs;
        _downAfter.Value = timing.FailureThreshold;
        _recoverAfter.Value = timing.RecoveryThreshold;
    }

    private bool ApplyTiming()
    {
        if (_engine.Monitoring) return true;
        var config = _engine.Snapshot().Settings;
        config.PingIntervalSeconds = (int)(_interval.Value ?? 2);
        config.PingTimeoutMs = (int)(_timeout.Value ?? 1000);
        config.FailureThreshold = (int)(_downAfter.Value ?? 3);
        config.RecoveryThreshold = (int)(_recoverAfter.Value ?? 2);
        string? error = _engine.ApplySettings(config);
        if (error is not null) { _ = _main.AlertAsync(error, owner: this); return false; }
        Refresh();
        return true;
    }

    private void RefreshReports(WatchdogSnapshot snapshot)
    {
        var active = snapshot.Hosts.Where(h => h.State is HostState.Offline or HostState.Suspect)
            .OrderBy(h => h.State == HostState.Offline ? 0 : 1).ThenBy(h => h.OutageStarted).ToList();
        _outageSummary.Text = $"{active.Count(h => h.State == HostState.Offline)} offline · {active.Count(h => h.State == HostState.Suspect)} suspect";
        _activeOutages.Text = active.Count == 0 ? "No active outages in this scope." : string.Join("\n\n", active.Take(8).Select(h =>
            $"{h.State.ToString().ToUpperInvariant()} · {h.Site}\n{(string.IsNullOrWhiteSpace(h.Label) ? h.Address : $"{h.Label} · {h.Address}")}\n{(h.OutageStarted is { } started ? $"Down for {FormatDuration(DateTime.Now - started)}" : $"{h.Failures} consecutive failure(s)")}"));
        if (active.Count > 8) _activeOutages.Text += $"\n\n+{active.Count - 8} more · see the live host list";
        _historyFilter.Text = $"{snapshot.Settings.EventHistoryHours switch { 24 => "Last 24 hours", 168 => "Last 7 days", 720 => "Last 30 days", _ => "All time" }} • suspects {(snapshot.Settings.HideSuspectEvents ? "hidden" : "shown")}";
        var history = _engine.HistorySnapshot(snapshot.Settings.EventHistoryHours, snapshot.Settings.HideSuspectEvents, snapshot.SelectedSite);
        _historySummary.Text = $"{history.Events.Count} matching / {history.StoredCount} stored · showing {Math.Min(8, history.Events.Count)}";
        _recentHistory.Text = history.Events.Count == 0 ? "No matching events." : string.Join("\n\n", history.Events.Take(8).Select(e =>
            $"{e.Timestamp:MMM d HH:mm:ss} · {e.Kind}\n{e.Site} · {e.DisplayHost}\n{e.Message}"));
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m" :
            duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m" :
            duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s" : $"{duration.Seconds}s";
    }

    private void SaveHistoryFilters()
    {
        if (_loading) return;
        var config = _engine.Snapshot().Settings;
        config.EventHistoryHours = _range.SelectedIndex switch { 0 => 24, 1 => 168, 2 => 720, _ => 0 };
        config.HideSuspectEvents = _hideSuspects.IsChecked == true;
        _engine.ApplySettings(config);
        Refresh();
    }

    private static HostState AggregateSite(SiteSnapshot site)
    {
        if (site.Hosts.Count == 0) return HostState.Unknown;
        int online = site.Hosts.Count(h => h.State == HostState.Online);
        if (site.Hosts.Any(h => h.State == HostState.Offline)) return online > site.Hosts.Count / 2d ? HostState.Suspect : HostState.Offline;
        if (site.Hosts.Any(h => h.State == HostState.Suspect)) return HostState.Suspect;
        return online > 0 ? HostState.Online : HostState.Unknown;
    }

    private void AddLine(Point start, Point end, IBrush brush, double thickness) => _topology.Children.Add(new Line { StartPoint = start, EndPoint = end, Stroke = brush, StrokeThickness = thickness, Opacity = 0.45 });
    private void AddCircle(double left, double top, double size, IBrush brush)
    {
        var circle = new Ellipse { Width = size, Height = size, Fill = Theme.Brush("#0A1119"), Stroke = brush, StrokeThickness = 2 };
        Canvas.SetLeft(circle, left); Canvas.SetTop(circle, top); _topology.Children.Add(circle);
    }
    private void AddText(string text, double left, double top, double size, IBrush brush, FontWeight weight, double width, bool centered = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, Width = width, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left };
        ToolTip.SetTip(label, text);
        Canvas.SetLeft(label, left); Canvas.SetTop(label, top); _topology.Children.Add(label);
    }

    private async Task AddSiteAsync()
    {
        if (!_editor.Commit()) return;
        string? name = await PromptAsync("Add Site", "Site name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        await ReportErrorAsync(_engine.AddSite(name));
        Refresh();
    }
    private async Task RenameSiteAsync()
    {
        string? selected = _engine.SelectedSite;
        if (selected is null || !_editor.Commit()) return;
        string? name = await PromptAsync("Rename Site", "Site name:", selected);
        if (string.IsNullOrWhiteSpace(name)) return;
        await ReportErrorAsync(_engine.RenameSite(selected, name));
        Refresh();
    }
    private async Task DeleteSiteAsync()
    {
        string? selected = _engine.SelectedSite;
        if (selected is null || !_editor.Commit()) return;
        if (!await _main.ConfirmAsync("Delete Site", $"Delete {selected} and its saved hosts?", this)) return;
        await ReportErrorAsync(_engine.DeleteSite(selected));
        Refresh();
    }
    private async Task EditLabelAsync()
    {
        var host = (_hostList.SelectedItem as HostRow)?.Host;
        if (host is null) return;
        string? label = await PromptAsync("Edit Host Label", $"Label for {host.Address} ({host.Site}):", host.Label);
        if (label is null) return;
        await ReportErrorAsync(_engine.SetLabel(host.Site, host.Address, label));
        Refresh();
    }
    private async Task ClearLabelAsync()
    {
        var host = (_hostList.SelectedItem as HostRow)?.Host;
        if (host is null) return;
        await ReportErrorAsync(_engine.SetLabel(host.Site, host.Address, ""));
        Refresh();
    }
    private async Task ReportErrorAsync(string? error) { if (error is not null) await _main.AlertAsync(error, owner: this); }
    internal bool CommitHostEdits() => _editor.Commit();
    private void ToggleMonitoring()
    {
        if (_engine.Monitoring) _engine.StopMonitoring();
        else if (_editor.Commit() && ApplyTiming()) _engine.StartMonitoring();
        Refresh();
    }
    private void ToggleOperations() { _ops.IsVisible = !_ops.IsVisible; Refresh(); }
    private void ToggleCli()
    {
        var config = _engine.Snapshot().Settings;
        config.WallboardShowCli = !config.WallboardShowCli;
        _engine.ApplySettings(config);
        Refresh();
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Dialogs and text/numeric/dropdown editors own their keystrokes, including Esc.
        if (this.GetVisualDescendants().OfType<Control>().Any(c => c.IsKeyboardFocusWithin && c is TextBox or ComboBox or NumericUpDown)) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.H)
        {
            new HistoryWindow(_engine).Show(this); e.Handled = true; return;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.OemComma)
        {
            new SettingsWindow(_engine, _updates, _main).Show(this); e.Handled = true; return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Escape: case Key.F11: Close(); break;
            case Key.O: ToggleOperations(); break;
            case Key.C: ToggleCli(); break;
            case Key.P: ToggleMonitoring(); break;
            case Key.H: _range.SelectedIndex = (_range.SelectedIndex + 1) % 4; break;
            case Key.S: _hideSuspects.IsChecked = _hideSuspects.IsChecked != true; break;
            case Key.M: _ = MoveToNextScreenAsync(); break;
            default: return;
        }
        e.Handled = true;
    }
    internal static int NextScreenIndex(int current, int count) => count <= 1 ? 0 : (Math.Max(-1, current) + 1) % count;
    private async Task MoveToNextScreenAsync()
    {
        if (_movingScreen || Screens.All.Count <= 1) { Refresh(); return; }
        _movingScreen = true;
        try
        {
            var screens = Screens.All;
            var target = screens[NextScreenIndex(screens.ToList().IndexOf(Screens.ScreenFromWindow(this)!), screens.Count)];
            WindowState = WindowState.Normal;
            await Task.Delay(80);
            if (_closed) return;
            Position = target.Bounds.Position;
            await Task.Delay(80);
            if (_closed) return;
            WindowState = WindowState.FullScreen;
            Refresh();
        }
        finally { _movingScreen = false; }
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => NativeControls.Number(min, max, value, step, 110);
    private async Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        var box = new TextBox { MinWidth = 320, Text = initial };
        var dialog = new Window { Title = title, Width = 430, Height = 170, CanResize = false, Background = Theme.Window, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = Theme.Button("OK", true); var cancel = Theme.Button("Cancel");
        ok.Click += (_, _) => dialog.Close(box.Text?.Trim()); cancel.Click += (_, _) => dialog.Close((string?)null);
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 10, Children = { Theme.Label(label, 11, color: Theme.Muted), box, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } } } };
        return await dialog.ShowDialog<string?>(this);
    }
}
