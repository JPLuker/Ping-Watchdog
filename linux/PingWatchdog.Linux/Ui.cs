using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal static class BrandAssets
{
    internal static readonly Avalonia.Media.Imaging.Bitmap Logo = LoadLogo();

    private static Avalonia.Media.Imaging.Bitmap LoadLogo()
    {
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("PingWatchdog.Brand.Logo")
            ?? throw new InvalidOperationException("Embedded Watchdog logo is missing.");
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}

internal static class Theme
{
    public static readonly IBrush Window = Brush("#0A0F16");
    public static readonly IBrush Panel = Brush("#101923");
    public static readonly IBrush Card = Brush("#151F2B");
    public static readonly IBrush Border = Brush("#2A3948");
    public static readonly IBrush Text = Brush("#EDF4FA");
    public static readonly IBrush Muted = Brush("#8EA0B2");
    public static readonly IBrush Cyan = Brush("#32B6E6");
    public static readonly IBrush Green = Brush("#42D392");
    public static readonly IBrush Yellow = Brush("#F5BF47");
    public static readonly IBrush Red = Brush("#FF6570");
    public static readonly IBrush Unknown = Brush("#76899D");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static IBrush State(HostState state) => state switch
    {
        HostState.Online => Green,
        HostState.Suspect => Yellow,
        HostState.Offline => Red,
        _ => Unknown
    };

    public static Button Button(string text, bool accent = false)
    {
        return new Button
        {
            Content = text,
            MinHeight = 34,
            Padding = new Thickness(14, 6),
            Margin = new Thickness(4, 2),
            Background = accent ? Brush("#176D8D") : Brush("#192633"),
            Foreground = Text,
            BorderBrush = accent ? Brush("#35AADA") : Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }

    public static Border CardBorder(Control child, Thickness? margin = null)
    {
        return new Border
        {
            Background = Card,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Margin = margin ?? new Thickness(4),
            Child = child
        };
    }

    public static TextBlock Label(string text, double size = 12, FontWeight? weight = null, IBrush? color = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = color ?? Text,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}

// Each editor owns its draft and the site it was loaded from. A timer or another
// window changing the selection must never save that draft into a different site.
internal sealed class HostEditorBinding
{
    private readonly WatchdogEngine _engine;
    private readonly TextBox _box;
    private readonly Action<string> _reportError;
    private string? _site;
    private string _loadedText = string.Empty;
    private string? _lastError;
    private string? _lastFailedText;
    // TextChanged can be delivered after a programmatic refresh. Compare values
    // instead of trusting event timing to decide whether a user has a draft.
    public bool Dirty => _site is not null && (_box.Text ?? string.Empty) != _loadedText;

    public HostEditorBinding(WatchdogEngine engine, TextBox box, Action<string> reportError)
    {
        _engine = engine;
        _box = box;
        _reportError = reportError;
        _box.LostFocus += (_, _) => Commit();
    }

    public bool Commit()
    {
        if (!Dirty || _site is null) return true;
        string text = _box.Text ?? string.Empty;
        string? error = _engine.SaveHosts(_site, text);
        if (error is not null)
        {
            if (_lastError != error || _lastFailedText != text) _reportError(error);
            _lastError = error;
            _lastFailedText = text;
            return false;
        }
        _loadedText = text;
        _lastError = null;
        _lastFailedText = null;
        return true;
    }

    public void Refresh(WatchdogSnapshot snapshot)
    {
        if (Dirty && !string.Equals(_site, snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase))
        {
            if (!Commit()) return;
            snapshot = _engine.Snapshot();
        }
        if (Dirty) return;
        _site = snapshot.SelectedSite;
        string text = _site is null
            ? string.Join(Environment.NewLine, snapshot.Hosts.Select(h => $"[{h.Site}] {h.Address}"))
            : string.Join(Environment.NewLine, snapshot.Hosts.Select(h => h.Address));
        // Set the baseline first: losing focus during layout must not commit stale text.
        _loadedText = text;
        if (_box.Text != text) _box.Text = text;
        _box.IsReadOnly = _site is null;
    }
}

internal sealed class MainWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly CancellationTokenSource _lifetime = new();

    private readonly TextBlock _monitorState = Theme.Label("IDLE", 11, FontWeight.Bold, Theme.Cyan);
    private readonly TextBlock _total = Theme.Label("0", 24, FontWeight.Bold);
    private readonly TextBlock _online = Theme.Label("0", 24, FontWeight.Bold, Theme.Green);
    private readonly TextBlock _suspect = Theme.Label("0", 24, FontWeight.Bold, Theme.Yellow);
    private readonly TextBlock _offline = Theme.Label("0", 24, FontWeight.Bold, Theme.Red);
    private readonly ListBox _siteList = new();
    private readonly TextBox _hostEditor = new()
    {
        AcceptsReturn = true,
        MinHeight = 94,
        FontFamily = new FontFamily("monospace"),
        TextWrapping = TextWrapping.NoWrap
    };
    private readonly ListBox _hostList = new();
    private readonly CliTraceView _commandBox;
    private readonly NumericUpDown _interval = Number(1, 300, 2, 1, 82);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250, 94);
    private readonly NumericUpDown _downAfter = Number(2, 20, 3, 1, 74);
    private readonly NumericUpDown _recoverAfter = Number(1, 20, 2, 1, 74);
    private readonly CheckBox _showCli = new() { Content = "Show CLI trace" };
    private readonly Button _startStop = Theme.Button("Start Monitoring", true);
    private readonly Button _updateButton = Theme.Button("Updates");
    private readonly TextBlock _status = Theme.Label("Ready", 11, color: Theme.Muted);
    private readonly List<string?> _siteKeys = new();
    private readonly ObservableCollection<HostRow> _hostRows = new();
    private readonly ObservableCollection<SiteRow> _siteRows = new();
    private LinuxTrayService? _tray;
    private readonly Button _editLabel = Theme.Button("Edit Label");
    private readonly Button _clearLabel = Theme.Button("Clear Label");
    private bool _loading;
    private readonly HostEditorBinding _editor;
    private bool _closed;
    private bool _updatePromptOpen;
    private bool _backgroundStarted;
    private (int, int, int, int)? _timingBaseline;
    private WallboardWindow? _wallboard;
    private SettingsWindow? _settings;
    private HistoryWindow? _history;
    private OrganizationWindow? _organization;

    public MainWindow(WatchdogEngine engine, LinuxUpdateService updates)
    {
        _engine = engine;
        _updates = updates;
        _commandBox = new CliTraceView(_engine.ClearCommandLog);
        _hostList.ItemsSource = _hostRows;
        _hostList.ItemTemplate = new FuncDataTemplate<HostRow>((_, _) =>
        {
            var label = LiveRows.BoundText(nameof(HostRow.Text));
            label.Bind(TextBlock.ForegroundProperty, new Binding(nameof(HostRow.Foreground)));
            return label;
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_hostList, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        _siteList.ItemsSource = _siteRows;
        _siteList.ItemTemplate = new FuncDataTemplate<SiteRow>((_, _) => LiveRows.BoundText(nameof(SiteRow.Text)));
        _editor = new HostEditorBinding(engine, _hostEditor, error => _ = AlertAsync(error));

        Title = "Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 1180;
        Height = 790;
        MinWidth = 960;
        MinHeight = 640;
        Background = Theme.Window;
        Foreground = Theme.Text;
        Content = BuildLayout();

        _engine.Changed += EngineChanged;
        _updates.Changed += UpdatesChanged;
        _updates.UpdateReady += UpdateReady;
        _timer.Tick += (_, _) => RefreshAll();

        _siteList.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            int index = _siteList.SelectedIndex;
            if (!_editor.Commit()) { RefreshAll(); return; }
            _engine.SelectedSite = index >= 0 && index < _siteKeys.Count ? _siteKeys[index] : null;
            RefreshAll();
        };
        _showCli.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            var config = _engine.Snapshot().Settings;
            config.ShowCommandView = _showCli.IsChecked == true;
            _engine.ApplySettings(config);
        };
        _startStop.Click += (_, _) => ToggleMonitoring();
        _updateButton.Click += async (_, _) => await RunUpdateActionAsync();
        _editLabel.Click += async (_, _) => await EditLabelAsync();
        _clearLabel.Click += async (_, _) => await ClearLabelAsync();
        _hostList.SelectionChanged += (_, _) => UpdateLabelActions();
        _hostList.DoubleTapped += async (_, _) => await EditLabelAsync();
        var editMenu = new MenuItem { Header = "Edit Label..." };
        var clearMenu = new MenuItem { Header = "Clear Label" };
        editMenu.Click += async (_, _) => await EditLabelAsync();
        clearMenu.Click += async (_, _) => await ClearLabelAsync();
        _hostList.ContextMenu = new ContextMenu { ItemsSource = new[] { editMenu, clearMenu } };
        _hostList.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(_hostList).Properties.IsRightButtonPressed) return;
            if (e.Source is Control control)
            {
                var row = new[] { control }.Concat(control.GetVisualAncestors().OfType<Control>())
                    .Select(c => c.DataContext).OfType<HostRow>().FirstOrDefault();
                if (row is not null) _hostList.SelectedItem = row;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PropertyChanged += async (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && _tray is not null)
                await _tray.MinimizeAsync();
        };

        Opened += (_, _) =>
        {
            RefreshAll();
            _timer.Start();
            if (!_backgroundStarted)
            {
                _backgroundStarted = true;
                _ = _updates.RunBackgroundAsync(() => _engine.Config.AutoCheckUpdates);
            }
        };
        Closing += (_, _) =>
        {
            _timer.Stop();
            _lifetime.Cancel();
            _editor.Commit();
            _wallboard?.CommitHostEdits();
            _engine.Save();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _engine.Changed -= EngineChanged;
            _updates.Changed -= UpdatesChanged;
            _updates.UpdateReady -= UpdateReady;
            _tray?.Dispose();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11)
            {
                e.Handled = true;
                OpenWallboard();
            }
            else if (e.Key == Key.H && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                OpenHistory();
            }
            else if (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                OpenSettings();
            }
        };
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("74,*,30")
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Theme.Panel,
            Margin = new Thickness(0)
        };
        header.Children.Add(new StackPanel
        {
            Margin = new Thickness(18, 11, 0, 8),
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new Image { Source = BrandAssets.Logo, Width = 40, Height = 50, Stretch = Stretch.Uniform },
                new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        Theme.Label("PING WATCHDOG", 21, FontWeight.Bold),
                        Theme.Label("Linux availability monitor", 11, color: Theme.Muted)
                    }
                }
            }
        });

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 14, 0),
            Spacing = 4
        };
        var monitorChip = Theme.CardBorder(_monitorState, new Thickness(2));
        monitorChip.Padding = new Thickness(12, 7);
        var wallboard = Theme.Button("Wallboard", true);
        wallboard.Click += (_, _) => OpenWallboard();
        var organize = Theme.Button("Organize");
        organize.Click += (_, _) => OpenOrganization();
        var history = Theme.Button("History");
        history.Click += (_, _) => OpenHistory();
        var settings = Theme.Button("Settings");
        settings.Click += (_, _) => OpenSettings();
        headerActions.Children.Add(monitorChip);
        headerActions.Children.Add(wallboard);
        headerActions.Children.Add(organize);
        headerActions.Children.Add(history);
        headerActions.Children.Add(settings);
        headerActions.Children.Add(_updateButton);
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        root.Children.Add(header);

        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("225,*"),
            Background = Theme.Window
        };
        Grid.SetRow(workspace, 1);

        var nav = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto"),
            Margin = new Thickness(12, 12, 8, 12)
        };
        nav.Children.Add(Theme.Label("Sites", 13, FontWeight.Bold));
        _siteList.Background = Theme.Panel;
        _siteList.BorderBrush = Theme.Border;
        _siteList.BorderThickness = new Thickness(1);
        _siteList.Margin = new Thickness(0, 8, 0, 8);
        Grid.SetRow(_siteList, 1);
        nav.Children.Add(_siteList);

        var add = Theme.Button("+ Add Site", true);
        add.Click += async (_, _) => await AddSiteAsync();
        Grid.SetRow(add, 2);
        nav.Children.Add(add);
        var org = Theme.Button("Folders / Organization");
        org.Click += (_, _) => OpenOrganization();
        Grid.SetRow(org, 3);
        nav.Children.Add(org);
        var siteActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var rename = Theme.Button("Rename");
        rename.Click += async (_, _) => await RenameSiteAsync();
        var delete = Theme.Button("Delete");
        delete.Click += async (_, _) => await DeleteSiteAsync();
        siteActions.Children.Add(rename);
        siteActions.Children.Add(delete);
        Grid.SetRow(siteActions, 4);
        nav.Children.Add(siteActions);
        var import = Theme.Button("Import Config...");
        import.Click += async (_, _) => await ImportConfigAsync();
        Grid.SetRow(import, 5); nav.Children.Add(import);
        var export = Theme.Button("Export Config...");
        export.Click += async (_, _) => await ExportConfigAsync();
        Grid.SetRow(export, 6); nav.Children.Add(export);
        workspace.Children.Add(nav);

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("86,132,Auto,*,Auto"),
            Margin = new Thickness(8, 12, 12, 12)
        };
        Grid.SetColumn(right, 1);

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        stats.Children.Add(StatCard("Total Hosts", _total, 0));
        stats.Children.Add(StatCard("Online", _online, 1));
        stats.Children.Add(StatCard("Suspect", _suspect, 2));
        stats.Children.Add(StatCard("Offline", _offline, 3));
        right.Children.Add(stats);

        var editorCard = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(4)
        };
        editorCard.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Theme.Label("Selected site hosts", 12, FontWeight.Bold),
                Right(Theme.Label("Saved automatically • Apply to reconcile live", 10, color: Theme.Muted))
            }
        });
        var editorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        _hostEditor.Background = Theme.Panel;
        _hostEditor.Foreground = Theme.Text;
        _hostEditor.BorderBrush = Theme.Border;
        editorRow.Children.Add(_hostEditor);
        var applyHosts = Theme.Button("Apply Hosts", true);
        applyHosts.Margin = new Thickness(8, 0, 0, 0);
        applyHosts.Click += (_, _) => ApplyHosts();
        Grid.SetColumn(applyHosts, 1);
        editorRow.Children.Add(applyHosts);
        Grid.SetRow(editorRow, 1);
        editorCard.Children.Add(editorRow);
        var editorBorder = Theme.CardBorder(editorCard);
        Grid.SetRow(editorBorder, 1);
        right.Children.Add(editorBorder);

        var monitorRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        monitorRow.Children.Add(Setting("Interval", _interval, "sec"));
        monitorRow.Children.Add(Setting("Timeout", _timeout, "ms"));
        monitorRow.Children.Add(Setting("Down after", _downAfter, "fails"));
        monitorRow.Children.Add(Setting("Recover after", _recoverAfter, "successes"));
        monitorRow.Children.Add(_showCli);
        monitorRow.Children.Add(_startStop);
        foreach (var control in monitorRow.Children) control.Margin = new Thickness(4, 2);
        var monitorBorder = Theme.CardBorder(monitorRow);
        Grid.SetRow(monitorBorder, 2);
        right.Children.Add(monitorBorder);

        var hostAndCli = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var hostActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { Theme.Label("Live hosts", 12, FontWeight.Bold), _editLabel, _clearLabel } };
        hostAndCli.Children.Add(hostActions);
        _hostList.Background = Theme.Panel;
        _hostList.BorderBrush = Theme.Border;
        _hostList.BorderThickness = new Thickness(1);
        Grid.SetRow(_hostList, 1);
        hostAndCli.Children.Add(_hostList);
        _commandBox.Background = Theme.Brush("#070C12");
        _commandBox.BorderBrush = Theme.Border;
        _commandBox.Margin = new Thickness(0, 8, 0, 0);
        _commandBox.Height = 160;
        Grid.SetRow(_commandBox, 2);
        hostAndCli.Children.Add(_commandBox);
        Grid.SetRow(hostAndCli, 3);
        right.Children.Add(hostAndCli);

        workspace.Children.Add(right);
        root.Children.Add(workspace);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Theme.Panel,
            Margin = new Thickness(0)
        };
        _status.Margin = new Thickness(12, 4);
        footer.Children.Add(_status);
        var ownership = Theme.Label("© 2026 Joseph Luker • Linux", 10, color: Theme.Muted);
        ownership.Margin = new Thickness(0, 4, 12, 4);
        Grid.SetColumn(ownership, 1);
        footer.Children.Add(ownership);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private static Border StatCard(string title, TextBlock value, int column)
    {
        var content = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Theme.Label(title, 10, FontWeight.Bold, Theme.Muted),
                value
            }
        };
        var border = Theme.CardBorder(content);
        Grid.SetColumn(border, column);
        return border;
    }

    private static Control Setting(string title, Control input, string suffix)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Theme.Label(title, 9, FontWeight.Bold, Theme.Muted),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { input, Theme.Label(suffix, 10, color: Theme.Muted) }
                }
            }
        };
    }

    private static Control Right(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        return control;
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step, double width)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Increment = step,
            Width = width,
            Background = Theme.Panel,
            Foreground = Theme.Text
        };
    }

    private void RefreshAll()
    {
        if (_loading || _closed) return;
        _loading = true;
        try
        {
            var snapshot = _engine.Snapshot();
            _monitorState.Text = snapshot.Monitoring ? "MONITORING" : "IDLE";
            _monitorState.Foreground = snapshot.Monitoring ? Theme.Green : Theme.Cyan;
            _startStop.Content = snapshot.Monitoring ? "Stop" : "Start Monitoring";
            _startStop.Background = snapshot.Monitoring ? Theme.Brush("#7A2531") : Theme.Brush("#176D4D");

            var allHosts = snapshot.Sites.SelectMany(site => site.Hosts).ToList();
            _total.Text = allHosts.Count.ToString();
            _online.Text = allHosts.Count(host => host.State == HostState.Online).ToString();
            _suspect.Text = allHosts.Count(host => host.State == HostState.Suspect).ToString();
            _offline.Text = allHosts.Count(host => host.State == HostState.Offline).ToString();

            RefreshSites(snapshot);
            RefreshHostRows(snapshot);

            _editor.Refresh(snapshot);

            bool settingsEnabled = !snapshot.Monitoring;
            _interval.IsEnabled = settingsEnabled;
            _timeout.IsEnabled = settingsEnabled;
            _downAfter.IsEnabled = settingsEnabled;
            _recoverAfter.IsEnabled = settingsEnabled;
            var timing = (snapshot.Settings.PingIntervalSeconds, snapshot.Settings.PingTimeoutMs,
                snapshot.Settings.FailureThreshold, snapshot.Settings.RecoveryThreshold);
            if (settingsEnabled && _timingBaseline != timing)
            {
                _timingBaseline = timing;
                _interval.Value = snapshot.Settings.PingIntervalSeconds;
                _timeout.Value = snapshot.Settings.PingTimeoutMs;
                _downAfter.Value = snapshot.Settings.FailureThreshold;
                _recoverAfter.Value = snapshot.Settings.RecoveryThreshold;
            }
            _showCli.IsChecked = snapshot.Settings.ShowCommandView;
            _commandBox.IsVisible = snapshot.Settings.ShowCommandView;
            _commandBox.Refresh(snapshot.Commands);

            _updateButton.IsVisible = snapshot.Settings.ShowUpdateControlOnHome;
            RefreshUpdateState();
            _status.Text = snapshot.Monitoring
                ? $"Monitoring {allHosts.Count} host(s) across {snapshot.Sites.Count} site(s)"
                : $"Ready • {allHosts.Count} configured host(s)";
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshSites(WatchdogSnapshot snapshot)
    {
        var rows = new List<SiteRow> { new(null, $"All Sites ({snapshot.Sites.Sum(site => site.Hosts.Count)})") };
        foreach (var site in snapshot.Sites.OrderBy(s => s.FolderPath, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            string path = string.IsNullOrWhiteSpace(site.FolderPath) ? site.Name : $"{site.FolderPath.Replace("/", " › ")} › {site.Name}";
            rows.Add(new SiteRow(site.Name, $"{path} ({site.Hosts.Count})"));
        }
        LiveRows.Sites(_siteRows, rows);
        _siteKeys.Clear();
        _siteKeys.AddRange(_siteRows.Select(r => r.Key));
        int index = snapshot.SelectedSite is null ? 0 : _siteKeys.FindIndex(key => string.Equals(key, snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase));
        if (_siteList.SelectedIndex != Math.Max(0, index)) _siteList.SelectedIndex = Math.Max(0, index);
    }

    private void RefreshHostRows(WatchdogSnapshot snapshot)
    {
        string? selected = (_hostList.SelectedItem as HostRow)?.Key;
        var scroll = _hostList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.Offset;
        LiveRows.Hosts(_hostRows, snapshot.Hosts);
        if (selected is not null)
            _hostList.SelectedItem = _hostRows.FirstOrDefault(row => row.Key.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (offset is { } saved && scroll is not null) scroll.Offset = saved;
        UpdateLabelActions();
    }

    private void UpdateLabelActions()
    {
        var host = (_hostList.SelectedItem as HostRow)?.Host;
        _editLabel.IsEnabled = host is not null;
        _clearLabel.IsEnabled = host is not null && !string.IsNullOrWhiteSpace(host.Label);
    }

    private async Task EditLabelAsync()
    {
        var host = (_hostList.SelectedItem as HostRow)?.Host;
        if (host is null) return;
        string? label = await PromptAsync("Edit Host Label", $"Label for {host.Address} ({host.Site}):", host.Label);
        if (label is null) return;
        string? error = _engine.SetLabel(host.Site, host.Address, label);
        if (error is not null) await AlertAsync(error);
    }

    private async Task ClearLabelAsync()
    {
        var host = (_hostList.SelectedItem as HostRow)?.Host;
        if (host is null) return;
        string? error = _engine.SetLabel(host.Site, host.Address, string.Empty);
        if (error is not null) await AlertAsync(error);
    }

    internal bool CommitHostEdits() => _editor.Commit() && (_wallboard is null || _wallboard.CommitHostEdits());
    internal void AttachTray(LinuxTrayService tray) => _tray = tray;
    internal string TrayStatus => _tray?.Status ?? "System tray unavailable";
    internal void RestoreFromTray() { WindowState = WindowState.Normal; Show(); Activate(); }
    internal void ExitApplication()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else Close();
    }

    internal async Task ExportConfigAsync(Window? owner = null)
    {
        owner ??= this;
        if (!CommitHostEdits()) return;
        try
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Ping Watchdog Configuration", SuggestedFileName = "watchdog.pingwatch.json", DefaultExtension = "pingwatch.json",
                FileTypeChoices = new[] { new FilePickerFileType("Ping Watchdog configuration") { Patterns = new[] { "*.pingwatch.json" } } }
            });
            if (file is null || !CommitHostEdits()) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_engine.ExportConfigJson());
        }
        catch (Exception ex) { await AlertAsync($"Could not export configuration: {ex.Message}", owner: owner); }
    }

    internal async Task ImportConfigAsync(Window? owner = null)
    {
        owner ??= this;
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Ping Watchdog Configuration", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Ping Watchdog configuration") { Patterns = new[] { "*.pingwatch.json", "*.json" } } }
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            string json = await reader.ReadToEndAsync();
            if (!await ConfirmAsync("Import Configuration", "Replace the current sites, hosts, labels, and settings? Outage history is retained. Active monitoring restarts with the imported configuration.", owner)) return;
            if (!CommitHostEdits()) return;
            string? error = _engine.ImportConfigJson(json);
            if (error is not null) await AlertAsync(error, owner: owner);
            else RefreshAll();
        }
        catch (Exception ex) { await AlertAsync($"Could not import configuration: {ex.Message}", owner: owner); }
    }

    private void EngineChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshAll);
    private void UpdatesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshUpdateState);
    private void UpdateReady(object? sender, string version) => Dispatcher.UIThread.Post(async () =>
    {
        if (!_closed) await PromptForUpdateAsync(version);
    });

    private void RefreshUpdateState()
    {
        if (_closed) return;
        _updateButton.Content = _updates.ActionText;
        if (_updates.HasPendingUpdate)
            _status.Text = _updates.Status;
    }

    internal void ToggleMonitoring()
    {
        if (_engine.Monitoring)
        {
            _engine.StopMonitoring();
            return;
        }
        if (!_editor.Commit()) return;
        ApplyMonitoringSettings();
        _engine.StartMonitoring();
    }

    private void ApplyMonitoringSettings()
    {
        var config = _engine.Snapshot().Settings;
        config.PingIntervalSeconds = (int)(_interval.Value ?? 2);
        config.PingTimeoutMs = (int)(_timeout.Value ?? 1000);
        config.FailureThreshold = (int)(_downAfter.Value ?? 3);
        config.RecoveryThreshold = (int)(_recoverAfter.Value ?? 2);
        _engine.ApplySettings(config);
    }

    private void ApplyHosts() => _editor.Commit();

    private async Task AddSiteAsync()
    {
        string? name = await PromptAsync("Add Site", "Site name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        string? error = _engine.AddSite(name);
        if (error is not null) await AlertAsync(error);
    }

    private async Task RenameSiteAsync()
    {
        string? selected = _engine.SelectedSite;
        if (selected is null) return;
        string? name = await PromptAsync("Rename Site", "Site name:", selected);
        if (string.IsNullOrWhiteSpace(name)) return;
        string? error = _engine.RenameSite(selected, name);
        if (error is not null) await AlertAsync(error);
    }

    private async Task DeleteSiteAsync()
    {
        string? selected = _engine.SelectedSite;
        if (selected is null) return;
        if (!await ConfirmAsync("Delete Site", $"Delete {selected} and its saved hosts?")) return;
        string? error = _engine.DeleteSite(selected);
        if (error is not null) await AlertAsync(error);
    }

    internal async Task RunUpdateActionAsync(Window? owner = null)
    {
        if (_updates.HasPendingUpdate)
        {
            await PromptForUpdateAsync(null, owner);
            return;
        }
        await _updates.CheckAsync(true);
        if (!_updates.HasPendingUpdate)
            await AlertAsync(_updates.Status, "Ping Watchdog Updates", owner);
    }

    private async Task PromptForUpdateAsync(string? version, Window? owner = null)
    {
        if (_closed || _updatePromptOpen) return;
        _updatePromptOpen = true;
        owner ??= _wallboard is { IsVisible: true } ? _wallboard : this;
        try
        {
            string message = version is null
                ? "The update is downloaded. Restart Ping Watchdog now?"
                : $"Version {version} downloaded in the background. Restart now to apply it?";
            if (!await ConfirmAsync("Restart to Update", message, owner)) return;
            if (!_editor.Commit() || (_wallboard is not null && !_wallboard.CommitHostEdits())) return;
            try { _updates.ApplyAndRestart(_engine); }
            catch (Exception ex) { await AlertAsync($"Could not apply update: {ex.Message}", "Ping Watchdog Updates", owner); }
        }
        finally { _updatePromptOpen = false; }
    }

    internal void OpenWallboard()
    {
        if (_wallboard is not null)
        {
            _wallboard.Activate();
            return;
        }
        _wallboard = new WallboardWindow(_engine, _updates, this);
        _wallboard.Closed += (_, _) => _wallboard = null;
        _wallboard.Show();
    }

    private void OpenSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_engine, _updates, this);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(this);
    }

    private void OpenHistory()
    {
        if (_history is not null)
        {
            _history.Activate();
            return;
        }
        _history = new HistoryWindow(_engine);
        _history.Closed += (_, _) => _history = null;
        _history.Show(this);
    }

    private void OpenOrganization()
    {
        if (_organization is not null)
        {
            _organization.Activate();
            return;
        }
        _organization = new OrganizationWindow(_engine);
        _organization.Closed += (_, _) => _organization = null;
        _organization.Show(this);
    }

    internal async Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 330, Background = Theme.Panel, Foreground = Theme.Text };
        var dialog = new Window
        {
            Title = title,
            Width = 430,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Window
        };
        var ok = Theme.Button("OK", true);
        var cancel = Theme.Button("Cancel");
        ok.Click += (_, _) => dialog.Close(box.Text?.Trim());
        cancel.Click += (_, _) => dialog.Close((string?)null);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                Theme.Label(label, 11, color: Theme.Muted),
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok }
                }
            }
        };
        return await dialog.ShowDialog<string?>(this);
    }

    internal async Task<bool> ConfirmAsync(string title, string message, Window? owner = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 470,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Window
        };
        var yes = Theme.Button("Yes", true);
        var no = Theme.Button("No");
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes }
                }
            }
        };
        return await dialog.ShowDialog<bool>(owner ?? this);
    }

    internal async Task AlertAsync(string message, string title = "Ping Watchdog", Window? owner = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Window
        };
        var ok = Theme.Button("OK", true);
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text },
                new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } }
            }
        };
        await dialog.ShowDialog(owner ?? this);
    }
}

internal sealed class SettingsWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly MainWindow _main;
    private readonly CheckBox _minimizeToTray = new() { Content = "Minimize to system tray (when available)" };
    private readonly TextBlock _trayStatus = Theme.Label("", 10, color: Theme.Muted);
    private readonly LinuxUpdateService _updates;
    private bool _closed;
    private readonly NumericUpDown _interval = Number(1, 300, 2, 1);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250);
    private readonly NumericUpDown _down = Number(2, 20, 3, 1);
    private readonly NumericUpDown _recover = Number(1, 20, 2, 1);
    private readonly CheckBox _showCli = new() { Content = "Show CLI trace in main window" };
    private readonly CheckBox _wallboardCli = new() { Content = "Show CLI trace when Wallboard opens" };
    private readonly CheckBox _notifications = new() { Content = "Desktop outage/recovery notifications (notify-send)" };
    private readonly CheckBox _autoUpdates = new() { Content = "Check for updates on launch and every 6 hours" };
    private readonly CheckBox _developerUpdate = new() { Content = "Developer: show update controls in primary UI" };
    private readonly CheckBox _hideSuspects = new() { Content = "Hide SUSPECT events by default" };
    private readonly ComboBox _historyRange = new() { Width = 180 };
    private readonly TextBlock _updateStatus = Theme.Label("Ready", 11, color: Theme.Muted);
    private readonly Button _updateAction = Theme.Button("Check for Updates", true);

    public SettingsWindow(WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        _engine = engine;
        _updates = updates;
        _main = main;
        Title = "Settings • Ping Watchdog";
        Width = 780;
        Height = 620;
        MinWidth = 650;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Window;
        Foreground = Theme.Text;

        _historyRange.ItemsSource = new[] { "Last 24 hours", "Last 7 days", "Last 30 days", "All time" };
        var tabs = new TabControl
        {
            Margin = new Thickness(16),
            ItemsSource = new[]
            {
                new TabItem { Header = "General", Content = GeneralPage() },
                new TabItem { Header = "Monitoring", Content = MonitoringPage() },
                new TabItem { Header = "History", Content = HistoryPage() },
                new TabItem { Header = "Updates", Content = UpdatesPage() }
            }
        };
        Content = tabs;

        _updates.Changed += UpdatesChanged;
        Closed += (_, _) => { _closed = true; _updates.Changed -= UpdatesChanged; };
        _updateAction.Click += async (_, _) => await main.RunUpdateActionAsync(this);
        Opened += (_, _) => LoadValues();
    }

    private Control GeneralPage()
    {
        var save = Theme.Button("Save Settings", true);
        save.Click += (_, _) => SaveValues();
        var import = Theme.Button("Import Config...");
        var export = Theme.Button("Export Config...");
        import.Click += async (_, _) => { await _main.ImportConfigAsync(this); LoadValues(); };
        export.Click += async (_, _) => await _main.ExportConfigAsync(this);
        return Page(
            Theme.Label("Application behavior", 16, FontWeight.Bold),
            _showCli,
            _wallboardCli,
            _notifications,
            _minimizeToTray,
            _trayStatus,
            save,
            Theme.Label("Configuration", 14, FontWeight.Bold),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { import, export } });
    }

    private Control MonitoringPage()
    {
        var save = Theme.Button("Save Monitoring Defaults", true);
        save.Click += (_, _) => SaveValues();
        return Page(
            Theme.Label("Monitoring defaults", 16, FontWeight.Bold),
            Row("Ping interval", _interval, "seconds"),
            Row("Ping timeout", _timeout, "milliseconds"),
            Row("Declare DOWN after", _down, "failures"),
            Row("Declare RECOVERED after", _recover, "successes"),
            Theme.Label("Timing controls are locked while monitoring is active.", 10, color: Theme.Muted),
            save);
    }

    private Control HistoryPage()
    {
        var save = Theme.Button("Save History Defaults", true);
        save.Click += (_, _) => SaveValues();
        return Page(
            Theme.Label("Outage history", 16, FontWeight.Bold),
            Row("Default range", _historyRange, ""),
            _hideSuspects,
            Theme.Label("Filters never delete stored events.", 10, color: Theme.Muted),
            save);
    }

    private Control UpdatesPage()
    {
        var save = Theme.Button("Save Update Preferences", true);
        save.Click += (_, _) => SaveValues();
        return Page(
            Theme.Label("Updates", 16, FontWeight.Bold),
            Theme.Label($"Installed: {_updates.Version}", 12, FontWeight.Bold),
            _updateStatus,
            _autoUpdates,
            _developerUpdate,
            _updateAction,
            Theme.Label("Linux releases use the self-updating AppImage channel.", 10, color: Theme.Muted),
            save);
    }

    private static StackPanel Page(params Control[] controls)
    {
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }

    private static Control Row(string label, Control control, string suffix)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, Width = 210, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center },
                control,
                Theme.Label(suffix, 10, color: Theme.Muted)
            }
        };
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Increment = step,
        Width = 110,
        Background = Theme.Panel,
        Foreground = Theme.Text
    };

    private void LoadValues()
    {
        var config = _engine.Snapshot().Settings;
        _interval.Value = config.PingIntervalSeconds;
        _timeout.Value = config.PingTimeoutMs;
        _down.Value = config.FailureThreshold;
        _recover.Value = config.RecoveryThreshold;
        _showCli.IsChecked = config.ShowCommandView;
        _wallboardCli.IsChecked = config.WallboardShowCli;
        _notifications.IsChecked = config.NotificationsEnabled;
        _minimizeToTray.IsChecked = config.MinimizeToTray;
        _trayStatus.Text = _main.TrayStatus;
        _autoUpdates.IsChecked = config.AutoCheckUpdates;
        _developerUpdate.IsChecked = config.ShowUpdateControlOnHome;
        _hideSuspects.IsChecked = config.HideSuspectEvents;
        _historyRange.SelectedIndex = config.EventHistoryHours switch { 24 => 0, 168 => 1, 720 => 2, _ => 3 };

        bool editable = !_engine.Monitoring;
        _interval.IsEnabled = editable;
        _timeout.IsEnabled = editable;
        _down.IsEnabled = editable;
        _recover.IsEnabled = editable;
        RefreshUpdate();
    }

    private void SaveValues()
    {
        var config = _engine.Snapshot().Settings;
        config.PingIntervalSeconds = (int)(_interval.Value ?? config.PingIntervalSeconds);
        config.PingTimeoutMs = (int)(_timeout.Value ?? config.PingTimeoutMs);
        config.FailureThreshold = (int)(_down.Value ?? config.FailureThreshold);
        config.RecoveryThreshold = (int)(_recover.Value ?? config.RecoveryThreshold);
        config.ShowCommandView = _showCli.IsChecked == true;
        config.WallboardShowCli = _wallboardCli.IsChecked == true;
        config.NotificationsEnabled = _notifications.IsChecked == true;
        config.MinimizeToTray = _minimizeToTray.IsChecked == true;
        config.AutoCheckUpdates = _autoUpdates.IsChecked == true;
        config.ShowUpdateControlOnHome = _developerUpdate.IsChecked == true;
        config.HideSuspectEvents = _hideSuspects.IsChecked == true;
        config.EventHistoryHours = _historyRange.SelectedIndex switch { 0 => 24, 1 => 168, 2 => 720, _ => 0 };
        string? error = _engine.ApplySettings(config);
        if (error is not null) { _ = _main.AlertAsync(error, owner: this); return; }
        LoadValues();
    }

    private void UpdatesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshUpdate);

    private void RefreshUpdate()
    {
        if (_closed) return;
        _updateStatus.Text = _updates.Status;
        _updateAction.Content = _updates.ActionText;
    }
}

internal sealed class HistoryWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ComboBox _range = new() { Width = 150 };
    private readonly CheckBox _hideSuspects = new() { Content = "Hide suspects" };
    private readonly ComboBox _site = new() { Width = 190 };
    private readonly ListBox _events = new();
    private readonly TextBlock _summary = Theme.Label("", 11, color: Theme.Muted);
    private IReadOnlyList<StateEventRecord> _visibleEvents = Array.Empty<StateEventRecord>();
    private readonly List<string?> _siteKeys = new();
    private bool _loading = true;
    private bool _closed;

    public HistoryWindow(WatchdogEngine engine)
    {
        _engine = engine;
        Title = "Outage History • Ping Watchdog";
        Width = 980;
        Height = 650;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Window;
        Foreground = Theme.Text;

        _range.ItemsSource = new[] { "Last 24 hours", "Last 7 days", "Last 30 days", "All time" };
        _range.SelectionChanged += (_, _) => ApplyFilterPreference();
        _hideSuspects.IsCheckedChanged += (_, _) => ApplyFilterPreference();
        _site.SelectionChanged += (_, _) => Refresh();

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        filters.Children.Add(Theme.Label("Range", 10, color: Theme.Muted));
        filters.Children.Add(_range);
        filters.Children.Add(Theme.Label("Site", 10, color: Theme.Muted));
        filters.Children.Add(_site);
        filters.Children.Add(_hideSuspects);
        var export = Theme.Button("Export CSV");
        export.Click += async (_, _) => await ExportCsvAsync();
        filters.Children.Add(export);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Margin = new Thickness(16),
            Children =
            {
                Theme.Label("Outage History", 20, FontWeight.Bold),
                At(filters, 1),
                At(_summary, 2),
                At(_events, 3)
            }
        };

        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) =>
        {
            var config = _engine.Snapshot().Settings;
            _range.SelectedIndex = config.EventHistoryHours switch { 24 => 0, 168 => 1, 720 => 2, _ => 3 };
            _hideSuspects.IsChecked = config.HideSuspectEvents;
            _loading = false;
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
    }

    private int Hours => _range.SelectedIndex switch { 0 => 24, 1 => 168, 2 => 720, _ => 0 };

    private void ApplyFilterPreference()
    {
        if (_loading || _closed || _range.SelectedIndex < 0) return;
        var config = _engine.Snapshot().Settings;
        config.EventHistoryHours = Hours;
        config.HideSuspectEvents = _hideSuspects.IsChecked == true;
        _engine.ApplySettings(config);
        Refresh();
    }

    private void Refresh()
    {
        if (_loading || _closed) return;
        _loading = true;
        try
        {
            string? selected = _site.SelectedIndex >= 0 && _site.SelectedIndex < _siteKeys.Count
                ? _siteKeys[_site.SelectedIndex] : null;
            var keys = new List<string?> { null };
            keys.AddRange(_engine.HistorySites());
            if (!_siteKeys.SequenceEqual(keys))
            {
                _siteKeys.Clear();
                _siteKeys.AddRange(keys);
                _site.ItemsSource = keys.Select(key => key ?? "All Sites").ToList();
                _site.SelectedIndex = Math.Max(0, keys.FindIndex(key => string.Equals(key, selected, StringComparison.OrdinalIgnoreCase)));
            }
            selected = _site.SelectedIndex >= 0 && _site.SelectedIndex < _siteKeys.Count ? _siteKeys[_site.SelectedIndex] : null;
            var history = _engine.HistorySnapshot(Hours, _hideSuspects.IsChecked == true, selected);
            _summary.Text = $"Showing {history.Events.Count:N0} of {history.StoredCount:N0} stored events • filters do not delete history";
            if (!_visibleEvents.SequenceEqual(history.Events))
            {
                _visibleEvents = history.Events;
                _events.ItemsSource = _visibleEvents
                    .Select(e => $"{e.Timestamp:yyyy-MM-dd HH:mm:ss}  {e.Kind,-10}  {e.Site,-20}  {e.DisplayHost,-30}  {e.Message}")
                    .ToList();
            }
        }
        finally { _loading = false; }
    }

    private async Task ExportCsvAsync()
    {
        Refresh();
        // Capture the shown rows before opening the picker. Timer ticks cannot change this export.
        var events = _visibleEvents.ToList();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Ping Watchdog Outage History",
            SuggestedFileName = $"ping-watchdog-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            DefaultExtension = "csv"
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        using var writer = new StreamWriter(stream);
        await HistoryCsv.WriteAsync(writer, events);
    }

    private static Control At(Control control, int row)
    {
        Grid.SetRow(control, row);
        if (row > 0) control.Margin = new Thickness(0, 10, 0, 0);
        return control;
    }
}

internal sealed class OrganizationWindow : Window
{
    private readonly WatchdogEngine _engine;
    private bool _closed;
    private readonly ListBox _items = new();
    private readonly List<OrgItem> _rows = new();

    public OrganizationWindow(WatchdogEngine engine)
    {
        _engine = engine;
        Title = "Site Organization • Ping Watchdog";
        Width = 760;
        Height = 620;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Window;
        Foreground = Theme.Text;

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var newSite = Theme.Button("New Site", true);
        var newFolder = Theme.Button("New Folder");
        var rename = Theme.Button("Rename");
        var move = Theme.Button("Move...");
        var delete = Theme.Button("Delete");
        var open = Theme.Button("Open Site");
        newSite.Click += async (_, _) => await NewSiteAsync();
        newFolder.Click += async (_, _) => await NewFolderAsync();
        rename.Click += async (_, _) => await RenameAsync();
        move.Click += async (_, _) => await MoveAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        open.Click += (_, _) => OpenSelected();
        foreach (var button in new[] { newSite, newFolder, rename, move, delete, open }) toolbar.Children.Add(button);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(16),
            Children =
            {
                Theme.Label("Site Organization", 20, FontWeight.Bold),
                At(toolbar, 1),
                At(_items, 2)
            }
        };
        _items.DoubleTapped += (_, _) => OpenSelected();
        Opened += (_, _) => Refresh();
        _engine.Changed += EngineChanged;
        Closed += (_, _) => { _closed = true; _engine.Changed -= EngineChanged; };
    }

    private void EngineChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (_closed) return;
        _rows.Clear();
        var folders = _engine.FolderSnapshot();
        foreach (var folder in folders)
        {
            int depth = folder.Count(ch => ch == '/');
            _rows.Add(new OrgItem(true, folder, $"{new string(' ', depth * 3)}▾ {folder.Split('/').Last()}"));
        }
        foreach (var site in _engine.SiteDefinitions().OrderBy(s => s.FolderPath).ThenBy(s => s.Name))
        {
            int depth = string.IsNullOrWhiteSpace(site.FolderPath) ? 0 : site.FolderPath.Count(ch => ch == '/') + 1;
            _rows.Add(new OrgItem(false, site.Name, $"{new string(' ', depth * 3)}• {site.Name}  ({site.Hosts.Count})"));
        }
        _items.ItemsSource = _rows.Select(row => row.Display).ToList();
    }

    private OrgItem? Selected => _items.SelectedIndex >= 0 && _items.SelectedIndex < _rows.Count ? _rows[_items.SelectedIndex] : null;

    private string CurrentFolder()
    {
        var selected = Selected;
        if (selected is null) return string.Empty;
        if (selected.IsFolder) return selected.Key;
        return _engine.SiteDefinitions().FirstOrDefault(s => s.Name.Equals(selected.Key, StringComparison.OrdinalIgnoreCase))?.FolderPath ?? string.Empty;
    }

    private async Task NewSiteAsync()
    {
        string? name = await PromptAsync("New Site", "Site name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        string? error = _engine.AddSite(name, CurrentFolder());
        if (error is not null) await AlertAsync(error);
        Refresh();
    }

    private async Task NewFolderAsync()
    {
        string? name = await PromptAsync("New Folder", "Folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        string? error = _engine.AddFolder(CurrentFolder(), name);
        if (error is not null) await AlertAsync(error);
        Refresh();
    }

    private async Task RenameAsync()
    {
        var selected = Selected;
        if (selected is null) return;
        string current = selected.IsFolder ? selected.Key.Split('/').Last() : selected.Key;
        string? name = await PromptAsync("Rename", "New name:", current);
        if (string.IsNullOrWhiteSpace(name)) return;
        string? error = selected.IsFolder ? _engine.RenameFolder(selected.Key, name) : _engine.RenameSite(selected.Key, name);
        if (error is not null) await AlertAsync(error);
        Refresh();
    }

    private async Task MoveAsync()
    {
        var selected = Selected;
        if (selected is null) return;
        var folders = _engine.FolderSnapshot().Where(f => !selected.IsFolder || (!f.Equals(selected.Key, StringComparison.OrdinalIgnoreCase) && !f.StartsWith(selected.Key + "/", StringComparison.OrdinalIgnoreCase))).ToList();
        string? destination = await ChoiceAsync("Move", "Destination folder:", new[] { "(Root)" }.Concat(folders).ToList());
        if (destination is null) return;
        destination = destination == "(Root)" ? string.Empty : destination;
        string? error = selected.IsFolder ? _engine.MoveFolder(selected.Key, destination) : _engine.MoveSite(selected.Key, destination);
        if (error is not null) await AlertAsync(error);
        Refresh();
    }

    private async Task DeleteAsync()
    {
        var selected = Selected;
        if (selected is null) return;
        string message = selected.IsFolder
            ? $"Delete folder {selected.Key}? Its contents move to the parent folder."
            : $"Delete site {selected.Key} and its saved hosts?";
        if (!await ConfirmAsync("Delete", message)) return;
        string? error = selected.IsFolder ? _engine.DeleteFolder(selected.Key) : _engine.DeleteSite(selected.Key);
        if (error is not null) await AlertAsync(error);
        Refresh();
    }

    private void OpenSelected()
    {
        var selected = Selected;
        if (selected is null || selected.IsFolder) return;
        _engine.SelectedSite = selected.Key;
    }

    private async Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 320 };
        var dialog = DialogBase(title, 430, 170);
        var ok = Theme.Button("OK", true);
        var cancel = Theme.Button("Cancel");
        ok.Click += (_, _) => dialog.Close(box.Text?.Trim());
        cancel.Click += (_, _) => dialog.Close((string?)null);
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 10, Children = { Theme.Label(label, 11, color: Theme.Muted), box, Buttons(cancel, ok) } };
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<string?> ChoiceAsync(string title, string label, IReadOnlyList<string> choices)
    {
        var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 330 };
        var dialog = DialogBase(title, 450, 180);
        var ok = Theme.Button("Move", true);
        var cancel = Theme.Button("Cancel");
        ok.Click += (_, _) => dialog.Close(combo.SelectedItem?.ToString());
        cancel.Click += (_, _) => dialog.Close((string?)null);
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 10, Children = { Theme.Label(label, 11, color: Theme.Muted), combo, Buttons(cancel, ok) } };
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = DialogBase(title, 470, 190);
        var yes = Theme.Button("Yes", true);
        var no = Theme.Button("No");
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text }, Buttons(no, yes) } };
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task AlertAsync(string message)
    {
        var dialog = DialogBase("Ping Watchdog", 470, 180);
        var ok = Theme.Button("OK", true);
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text }, Buttons(ok) } };
        await dialog.ShowDialog(this);
    }

    private static Window DialogBase(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        CanResize = false,
        Background = Theme.Window,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    private static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 4 };
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    private static Control At(Control control, int row)
    {
        Grid.SetRow(control, row);
        if (row > 0) control.Margin = new Thickness(0, 10, 0, 0);
        return control;
    }

    private sealed record OrgItem(bool IsFolder, string Key, string Display);
}
