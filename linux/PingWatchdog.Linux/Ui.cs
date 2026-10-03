using Avalonia;
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
    private readonly TextBox _commandBox = new()
    {
        AcceptsReturn = true,
        IsReadOnly = true,
        FontFamily = new FontFamily("monospace"),
        MinHeight = 118,
        TextWrapping = TextWrapping.NoWrap
    };
    private readonly NumericUpDown _interval = Number(1, 300, 2, 1, 82);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250, 94);
    private readonly NumericUpDown _downAfter = Number(2, 20, 3, 1, 74);
    private readonly NumericUpDown _recoverAfter = Number(1, 20, 2, 1, 74);
    private readonly CheckBox _showCli = new() { Content = "Show CLI trace" };
    private readonly Button _startStop = Theme.Button("Start Monitoring", true);
    private readonly Button _updateButton = Theme.Button("Updates");
    private readonly TextBlock _status = Theme.Label("Ready", 11, color: Theme.Muted);
    private readonly List<string?> _siteKeys = new();
    private List<HostSnapshot> _hostRows = new();
    private bool _loading;
    private bool _editorDirty;
    private WallboardWindow? _wallboard;
    private SettingsWindow? _settings;
    private HistoryWindow? _history;
    private OrganizationWindow? _organization;

    public MainWindow(WatchdogEngine engine, LinuxUpdateService updates)
    {
        _engine = engine;
        _updates = updates;

        Title = "Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 1180;
        Height = 790;
        MinWidth = 960;
        MinHeight = 640;
        Background = Theme.Window;
        Foreground = Theme.Text;
        Content = BuildLayout();

        _engine.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
        _updates.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshUpdateState);
        _updates.UpdateReady += (_, version) => Dispatcher.UIThread.Post(async () => await PromptForUpdateAsync(version));
        _timer.Tick += (_, _) => RefreshAll();

        _siteList.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            int index = _siteList.SelectedIndex;
            _engine.SelectedSite = index >= 0 && index < _siteKeys.Count ? _siteKeys[index] : null;
            _editorDirty = false;
            RefreshAll();
        };
        _hostEditor.TextChanged += (_, _) => { if (!_loading) _editorDirty = true; };
        _showCli.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            var config = _engine.Snapshot().Settings;
            config.ShowCommandView = _showCli.IsChecked == true;
            _engine.ApplySettings(config);
        };
        _startStop.Click += (_, _) => ToggleMonitoring();
        _updateButton.Click += async (_, _) => await RunUpdateActionAsync();

        Opened += (_, _) =>
        {
            RefreshAll();
            _timer.Start();
            _ = _updates.RunBackgroundAsync(() => _engine.Config.AutoCheckUpdates);
        };
        Closing += (_, _) =>
        {
            _timer.Stop();
            _lifetime.Cancel();
            _engine.Save();
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
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"),
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
        workspace.Children.Add(nav);

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("86,132,84,*,Auto"),
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

        var monitorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        monitorRow.Children.Add(Setting("Interval", _interval, "sec"));
        monitorRow.Children.Add(Setting("Timeout", _timeout, "ms"));
        monitorRow.Children.Add(Setting("Down after", _downAfter, "fails"));
        monitorRow.Children.Add(Setting("Recover after", _recoverAfter, "successes"));
        monitorRow.Children.Add(_showCli);
        monitorRow.Children.Add(_startStop);
        var monitorBorder = Theme.CardBorder(monitorRow);
        Grid.SetRow(monitorBorder, 2);
        right.Children.Add(monitorBorder);

        var hostAndCli = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        _hostList.Background = Theme.Panel;
        _hostList.BorderBrush = Theme.Border;
        _hostList.BorderThickness = new Thickness(1);
        hostAndCli.Children.Add(_hostList);
        _commandBox.Background = Theme.Brush("#070C12");
        _commandBox.Foreground = Theme.Green;
        _commandBox.BorderBrush = Theme.Border;
        _commandBox.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_commandBox, 1);
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
        if (_loading) return;
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

            if (!_editorDirty || !_hostEditor.IsFocused)
            {
                _hostEditor.Text = snapshot.SelectedSite is null
                    ? string.Join(Environment.NewLine, snapshot.Hosts.Select(host => $"[{host.Site}] {host.Address}"))
                    : string.Join(Environment.NewLine, snapshot.Hosts.Select(host => host.Address));
                _editorDirty = false;
            }
            _hostEditor.IsReadOnly = snapshot.SelectedSite is null;

            bool settingsEnabled = !snapshot.Monitoring;
            _interval.IsEnabled = settingsEnabled;
            _timeout.IsEnabled = settingsEnabled;
            _downAfter.IsEnabled = settingsEnabled;
            _recoverAfter.IsEnabled = settingsEnabled;
            if (settingsEnabled)
            {
                _interval.Value = snapshot.Settings.PingIntervalSeconds;
                _timeout.Value = snapshot.Settings.PingTimeoutMs;
                _downAfter.Value = snapshot.Settings.FailureThreshold;
                _recoverAfter.Value = snapshot.Settings.RecoveryThreshold;
            }
            _showCli.IsChecked = snapshot.Settings.ShowCommandView;
            _commandBox.IsVisible = snapshot.Settings.ShowCommandView;
            _commandBox.Text = string.Join(Environment.NewLine, snapshot.Commands.TakeLast(120).Select(entry => entry.Text));

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
        string? previous = snapshot.SelectedSite;
        _siteKeys.Clear();
        var items = new List<string>();
        _siteKeys.Add(null);
        items.Add($"All Sites ({snapshot.Sites.Sum(site => site.Hosts.Count)})");
        foreach (var site in snapshot.Sites
            .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase))
        {
            _siteKeys.Add(site.Name);
            string path = string.IsNullOrWhiteSpace(site.FolderPath)
                ? site.Name
                : $"{site.FolderPath.Replace("/", " › ")} › {site.Name}";
            items.Add($"{path} ({site.Hosts.Count})");
        }
        _siteList.ItemsSource = items;
        int index = previous is null ? 0 : _siteKeys.FindIndex(key => key?.Equals(previous, StringComparison.OrdinalIgnoreCase) == true);
        _siteList.SelectedIndex = Math.Max(0, index);
    }

    private void RefreshHostRows(WatchdogSnapshot snapshot)
    {
        int previous = _hostList.SelectedIndex;
        _hostRows = snapshot.Hosts.ToList();
        _hostList.ItemsSource = _hostRows.Select(host =>
        {
            string label = string.IsNullOrWhiteSpace(host.Label) ? "" : $"  {host.Label}";
            string latency = host.LatencyMs is null ? "—" : $"{host.LatencyMs} ms";
            return $"{host.State,-8}  {host.Site,-18}  {host.Address,-24}{label,-20}  {latency,-9}  failures {host.Failures}";
        }).ToList();
        if (previous >= 0 && previous < _hostRows.Count)
            _hostList.SelectedIndex = previous;
    }

    private void RefreshUpdateState()
    {
        _updateButton.Content = _updates.ActionText;
        if (_updates.HasPendingUpdate)
            _status.Text = _updates.Status;
    }

    private void ToggleMonitoring()
    {
        if (_engine.Monitoring)
        {
            _engine.StopMonitoring();
            return;
        }
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

    private void ApplyHosts()
    {
        string? selected = _engine.SelectedSite;
        if (selected is null) return;
        string? error = _engine.SaveHosts(selected, _hostEditor.Text ?? string.Empty);
        if (error is not null) _ = AlertAsync(error);
        else _editorDirty = false;
    }

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

    private async Task RunUpdateActionAsync()
    {
        if (_updates.HasPendingUpdate)
        {
            if (await ConfirmAsync("Restart to Update", "The update is downloaded. Restart Ping Watchdog now?"))
            {
                _engine.Save();
                _engine.StopMonitoring();
                _updates.ApplyAndRestart();
            }
            return;
        }
        await _updates.CheckAsync(true);
        if (!_updates.HasPendingUpdate)
            await AlertAsync(_updates.Status, "Ping Watchdog Updates");
    }

    private async Task PromptForUpdateAsync(string version)
    {
        bool restart = await ConfirmAsync(
            "Ping Watchdog Update Ready",
            $"Version {version} downloaded in the background. Restart now to apply it?");
        if (!restart) return;
        _engine.Save();
        _engine.StopMonitoring();
        _updates.ApplyAndRestart();
    }

    private void OpenWallboard()
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
        _settings = new SettingsWindow(_engine, _updates);
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

    internal async Task<bool> ConfirmAsync(string title, string message)
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
        return await dialog.ShowDialog<bool>(this);
    }

    internal async Task AlertAsync(string message, string title = "Ping Watchdog")
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
        await dialog.ShowDialog(this);
    }
}

internal sealed class SettingsWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
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

    public SettingsWindow(WatchdogEngine engine, LinuxUpdateService updates)
    {
        _engine = engine;
        _updates = updates;
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

        _updates.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshUpdate);
        _updateAction.Click += async (_, _) =>
        {
            if (_updates.HasPendingUpdate)
                _updates.ApplyAndRestart();
            else
                await _updates.CheckAsync(true);
            RefreshUpdate();
        };
        Opened += (_, _) => LoadValues();
    }

    private Control GeneralPage()
    {
        var save = Theme.Button("Save Settings", true);
        save.Click += (_, _) => SaveValues();
        return Page(
            Theme.Label("Application behavior", 16, FontWeight.Bold),
            _showCli,
            _wallboardCli,
            _notifications,
            save);
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
        config.AutoCheckUpdates = _autoUpdates.IsChecked == true;
        config.ShowUpdateControlOnHome = _developerUpdate.IsChecked == true;
        config.HideSuspectEvents = _hideSuspects.IsChecked == true;
        config.EventHistoryHours = _historyRange.SelectedIndex switch { 0 => 24, 1 => 168, 2 => 720, _ => 0 };
        _engine.ApplySettings(config);
        LoadValues();
    }

    private void RefreshUpdate()
    {
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
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(16),
            Children =
            {
                Theme.Label("Outage History", 20, FontWeight.Bold),
                At(filters, 1),
                At(_events, 2)
            }
        };

        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) =>
        {
            var config = _engine.Snapshot().Settings;
            _range.SelectedIndex = config.EventHistoryHours switch { 24 => 0, 168 => 1, 720 => 2, _ => 3 };
            _hideSuspects.IsChecked = config.HideSuspectEvents;
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void ApplyFilterPreference()
    {
        if (_range.SelectedIndex < 0) return;
        var config = _engine.Snapshot().Settings;
        config.EventHistoryHours = _range.SelectedIndex switch { 0 => 24, 1 => 168, 2 => 720, _ => 0 };
        config.HideSuspectEvents = _hideSuspects.IsChecked == true;
        _engine.ApplySettings(config);
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = _engine.Snapshot();
        string current = _site.SelectedItem?.ToString() ?? "All Sites";
        var sites = new[] { "All Sites" }.Concat(snapshot.Sites.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)).ToList();
        _site.ItemsSource = sites;
        int siteIndex = sites.FindIndex(s => s.Equals(current, StringComparison.OrdinalIgnoreCase));
        _site.SelectedIndex = Math.Max(0, siteIndex);

        IEnumerable<StateEventRecord> events = snapshot.Events;
        if (!current.Equals("All Sites", StringComparison.OrdinalIgnoreCase))
            events = events.Where(e => e.Site.Equals(current, StringComparison.OrdinalIgnoreCase));
        _events.ItemsSource = events.OrderByDescending(e => e.Timestamp)
            .Select(e => $"{e.Timestamp:yyyy-MM-dd HH:mm:ss}  {e.Kind,-10}  {e.Site,-20}  {e.DisplayHost,-30}  {e.Message}")
            .ToList();
    }

    private async Task ExportCsvAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Ping Watchdog Outage History",
            SuggestedFileName = $"ping-watchdog-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            DefaultExtension = "csv"
        });
        if (file is null) return;

        var snapshot = _engine.Snapshot();
        await using var stream = await file.OpenWriteAsync();
        using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync("Timestamp,Event,Site,Host,Details");
        foreach (var e in snapshot.Events)
        {
            static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
            await writer.WriteLineAsync(string.Join(",", Csv(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")), Csv(e.Kind), Csv(e.Site), Csv(e.DisplayHost), Csv(e.Message)));
        }
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
        _engine.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
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

internal sealed class WallboardWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
    private readonly MainWindow _main;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Canvas _topology = new() { Background = Theme.Brush("#070C12") };
    private readonly TextBlock _clock = Theme.Label("", 26, FontWeight.Bold);
    private readonly TextBlock _date = Theme.Label("", 12, color: Theme.Muted);
    private readonly TextBlock _state = Theme.Label("IDLE", 11, FontWeight.Bold);
    private readonly TextBlock _stats = Theme.Label("", 12, FontWeight.Bold);
    private readonly TextBox _cli = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("monospace"), MinHeight = 120 };
    private readonly StackPanel _ops = new() { Width = 370, Spacing = 8, IsVisible = false, Margin = new Thickness(12) };
    private readonly ComboBox _site = new() { Width = 330 };
    private readonly TextBox _hosts = new() { AcceptsReturn = true, MinHeight = 110, FontFamily = new FontFamily("monospace") };
    private readonly List<string?> _siteKeys = new();
    private bool _showCli;
    private bool _loading;

    public WallboardWindow(WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        _engine = engine;
        _updates = updates;
        _main = main;
        Title = "Ping Watchdog Wallboard";
        Icon = new WindowIcon(BrandAssets.Logo);
        WindowState = WindowState.FullScreen;
        SystemDecorations = SystemDecorations.None;
        Background = Theme.Window;
        Foreground = Theme.Text;
        _showCli = _engine.Config.WallboardShowCli;
        Content = BuildLayout();

        _site.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            int index = _site.SelectedIndex;
            _engine.SelectedSite = index >= 0 && index < _siteKeys.Count ? _siteKeys[index] : null;
            Refresh();
        };
        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _main.Show(); _main.Activate(); };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F11) { e.Handled = true; Close(); }
            else if (e.Key == Key.O) { e.Handled = true; _ops.IsVisible = !_ops.IsVisible; }
            else if (e.Key == Key.C) { e.Handled = true; _showCli = !_showCli; Refresh(); }
            else if (e.Key == Key.P) { e.Handled = true; ToggleMonitoring(); }
        };
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("46,*,Auto") };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Theme.Panel };
        toolbar.Children.Add(new TextBlock { Text = "PING WATCHDOG • LINUX WALLBOARD", FontWeight = FontWeight.Bold, Foreground = Theme.Text, Margin = new Thickness(14, 13, 0, 0) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 4, 10, 4) };
        var start = Theme.Button("Start / Stop"); start.Click += (_, _) => ToggleMonitoring();
        var ops = Theme.Button("Operations", true); ops.Click += (_, _) => _ops.IsVisible = !_ops.IsVisible;
        var cli = Theme.Button("CLI"); cli.Click += (_, _) => { _showCli = !_showCli; Refresh(); };
        var history = Theme.Button("History"); history.Click += (_, _) => new HistoryWindow(_engine).Show(this);
        var settings = Theme.Button("Settings"); settings.Click += (_, _) => new SettingsWindow(_engine, _updates).Show(this);
        var organize = Theme.Button("Organize"); organize.Click += (_, _) => new OrganizationWindow(_engine).Show(this);
        var updates = Theme.Button("Updates"); updates.Click += async (_, _) => await _updates.CheckAsync(true);
        var main = Theme.Button("Main Window"); main.Click += (_, _) => Close();
        foreach (var button in new[] { start, ops, cli, history, settings, organize, updates, main }) actions.Children.Add(button);
        Grid.SetColumn(actions, 1); toolbar.Children.Add(actions); root.Children.Add(toolbar);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 12, 18, 8) };
        header.Children.Add(_stats);
        var clockStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Spacing = 0, Children = { _clock, _date, _state } };
        Grid.SetColumn(clockStack, 1); header.Children.Add(clockStack); content.Children.Add(header);
        Grid.SetRow(_topology, 1); content.Children.Add(_topology);
        BuildOpsPanel();
        Grid.SetColumn(_ops, 1); Grid.SetRowSpan(_ops, 2); content.Children.Add(Theme.CardBorder(_ops, new Thickness(8)));
        Grid.SetRow(content, 1); root.Children.Add(content);

        _cli.Background = Theme.Brush("#060A0F"); _cli.Foreground = Theme.Green; _cli.Margin = new Thickness(10, 4, 10, 10);
        Grid.SetRow(_cli, 2); root.Children.Add(_cli);
        return root;
    }

    private void BuildOpsPanel()
    {
        _ops.Children.Add(Theme.Label("Operations", 17, FontWeight.Bold));
        _ops.Children.Add(Theme.Label("Same live session and config as the main window.", 10, color: Theme.Muted));
        _ops.Children.Add(Theme.Label("Site", 10, FontWeight.Bold, Theme.Muted));
        _ops.Children.Add(_site);
        _ops.Children.Add(Theme.Label("Hosts", 10, FontWeight.Bold, Theme.Muted));
        _ops.Children.Add(_hosts);
        var apply = Theme.Button("Apply Hosts", true); apply.Click += (_, _) => { if (_engine.SelectedSite is { } site) _engine.SaveHosts(site, _hosts.Text ?? ""); };
        _ops.Children.Add(apply);
        var add = Theme.Button("+ Add Site"); add.Click += async (_, _) => { string? name = await PromptAsync("Add Site", "Site name:"); if (!string.IsNullOrWhiteSpace(name)) _engine.AddSite(name); };
        var organize = Theme.Button("Folders / Organization"); organize.Click += (_, _) => new OrganizationWindow(_engine).Show(this);
        var settings = Theme.Button("Settings"); settings.Click += (_, _) => new SettingsWindow(_engine, _updates).Show(this);
        var history = Theme.Button("Outage History"); history.Click += (_, _) => new HistoryWindow(_engine).Show(this);
        var clear = Theme.Button("Clear CLI"); clear.Click += (_, _) => _engine.ClearCommandLog();
        foreach (var button in new[] { add, organize, settings, history, clear }) _ops.Children.Add(button);
    }

    private void Refresh()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var snapshot = _engine.Snapshot();
            _clock.Text = DateTime.Now.ToString("h:mm:ss tt");
            _date.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
            _state.Text = snapshot.Monitoring ? "LIVE MONITORING" : "IDLE / CONFIG VIEW";
            _state.Foreground = snapshot.Monitoring ? Theme.Green : Theme.Cyan;
            var hosts = snapshot.Sites.SelectMany(site => site.Hosts).ToList();
            _stats.Text = $"TOTAL {hosts.Count}     ONLINE {hosts.Count(h => h.State == HostState.Online)}     SUSPECT {hosts.Count(h => h.State == HostState.Suspect)}     OFFLINE {hosts.Count(h => h.State == HostState.Offline)}";
            _cli.IsVisible = _showCli;
            _cli.Text = string.Join(Environment.NewLine, snapshot.Commands.TakeLast(90).Select(c => c.Text));
            RefreshSiteCombo(snapshot);
            if (!_hosts.IsFocused && snapshot.SelectedSite is not null)
                _hosts.Text = string.Join(Environment.NewLine, snapshot.Hosts.Select(h => h.Address));
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
            items.Add(site.Name);
            _siteKeys.Add(site.Name);
        }
        _site.ItemsSource = items;
        int selected = snapshot.SelectedSite is null ? 0 : _siteKeys.FindIndex(key => key?.Equals(snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase) == true);
        _site.SelectedIndex = Math.Max(0, selected);
    }

    private void DrawTopology(WatchdogSnapshot snapshot)
    {
        _topology.Children.Clear();
        double width = Math.Max(700, _topology.Bounds.Width);
        double height = Math.Max(420, _topology.Bounds.Height);
        var center = new Point(width / 2, height / 2);
        AddCircle(center.X - 42, center.Y - 42, 84, Theme.Cyan);
        AddText("WATCHDOG", center.X - 34, center.Y - 9, 12, Theme.Text, FontWeight.Bold);

        int siteCount = Math.Max(1, snapshot.Sites.Count);
        double siteOrbit = Math.Min(width, height) * 0.31;
        for (int i = 0; i < snapshot.Sites.Count; i++)
        {
            var site = snapshot.Sites[i];
            double angle = -Math.PI / 2 + (Math.PI * 2 * i / siteCount);
            var sitePoint = new Point(center.X + Math.Cos(angle) * siteOrbit, center.Y + Math.Sin(angle) * siteOrbit);
            HostState aggregate = AggregateSite(site);
            AddLine(center, sitePoint, Theme.State(aggregate), 2);
            AddCircle(sitePoint.X - 34, sitePoint.Y - 34, 68, Theme.State(aggregate));
            AddText(site.Name, sitePoint.X - 50, sitePoint.Y - 8, 11, Theme.Text, FontWeight.Bold, 100);

            int visible = Math.Min(12, site.Hosts.Count);
            for (int h = 0; h < visible; h++)
            {
                var host = site.Hosts[h];
                double hostAngle = -Math.PI / 2 + (Math.PI * 2 * h / Math.Max(1, visible));
                double hostOrbit = 82 + (h >= 6 ? 42 : 0);
                var hp = new Point(sitePoint.X + Math.Cos(hostAngle) * hostOrbit, sitePoint.Y + Math.Sin(hostAngle) * hostOrbit);
                AddLine(sitePoint, hp, Theme.State(host.State), 1);
                AddCircle(hp.X - 5, hp.Y - 5, 10, Theme.State(host.State));
                string primary = string.IsNullOrWhiteSpace(host.Label) ? host.Address : host.Label;
                string secondary = string.IsNullOrWhiteSpace(host.Label) ? "" : host.Address;
                AddText(primary, hp.X + 8, hp.Y - 11, 9, Theme.Text, FontWeight.Bold, 120);
                if (!string.IsNullOrWhiteSpace(secondary))
                    AddText(secondary, hp.X + 8, hp.Y + 2, 8, Theme.Muted, FontWeight.Normal, 120);
            }
        }
    }

    private static HostState AggregateSite(SiteSnapshot site)
    {
        if (site.Hosts.Count == 0) return HostState.Unknown;
        int online = site.Hosts.Count(host => host.State == HostState.Online);
        int offline = site.Hosts.Count(host => host.State == HostState.Offline);
        int suspect = site.Hosts.Count(host => host.State == HostState.Suspect);
        if (offline > 0) return online > site.Hosts.Count / 2d ? HostState.Suspect : HostState.Offline;
        if (suspect > 0) return HostState.Suspect;
        if (online > 0) return HostState.Online;
        return HostState.Unknown;
    }

    private void AddLine(Point start, Point end, IBrush brush, double thickness)
    {
        _topology.Children.Add(new Line { StartPoint = start, EndPoint = end, Stroke = brush, StrokeThickness = thickness, Opacity = 0.65 });
    }

    private void AddCircle(double left, double top, double size, IBrush brush)
    {
        var circle = new Ellipse { Width = size, Height = size, Fill = Theme.Brush("#0A1119"), Stroke = brush, StrokeThickness = size > 20 ? 2 : 1 };
        Canvas.SetLeft(circle, left); Canvas.SetTop(circle, top); _topology.Children.Add(circle);
    }

    private void AddText(string text, double left, double top, double size, IBrush brush, FontWeight weight, double width = 90)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, Width = width, TextTrimming = TextTrimming.CharacterEllipsis };
        Canvas.SetLeft(label, left); Canvas.SetTop(label, top); _topology.Children.Add(label);
    }

    private void ToggleMonitoring()
    {
        if (_engine.Monitoring) _engine.StopMonitoring(); else _engine.StartMonitoring();
        Refresh();
    }

    private async Task<string?> PromptAsync(string title, string label)
    {
        var box = new TextBox { MinWidth = 320 };
        var dialog = new Window { Title = title, Width = 430, Height = 170, CanResize = false, Background = Theme.Window, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = Theme.Button("OK", true); var cancel = Theme.Button("Cancel");
        ok.Click += (_, _) => dialog.Close(box.Text?.Trim()); cancel.Click += (_, _) => dialog.Close((string?)null);
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 10, Children = { Theme.Label(label, 11, color: Theme.Muted), box, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } } } };
        return await dialog.ShowDialog<string?>(this);
    }
}
