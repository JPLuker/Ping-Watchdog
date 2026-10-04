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
using PingWatchdog.Shared;

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
    public static readonly IBrush Window = Brush(Presentation.Window);
    public static readonly IBrush Panel = Brush(Presentation.Input);
    public static readonly IBrush Card = Brush(Presentation.Card);
    public static readonly IBrush Border = Brush(Presentation.Border);
    public static readonly IBrush Text = Brush(Presentation.Text);
    public static readonly IBrush Muted = Brush(Presentation.Muted);
    public static readonly IBrush Cyan = Brush(Presentation.Accent);
    public static readonly IBrush Green = Brush(Presentation.Online);
    public static readonly IBrush Yellow = Brush(Presentation.Suspect);
    public static readonly IBrush Red = Brush(Presentation.Offline);
    public static readonly IBrush Unknown = Brush(Presentation.Unknown);

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
            MinHeight = 32,
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0),
            Background = accent ? Brush("#144552") : Brush("#19222D"),
            Foreground = Text,
            BorderBrush = accent ? Brush("#2AAABD") : Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
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
            CornerRadius = new CornerRadius(0),
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

internal sealed partial class MainWindow : Window
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
        Height = 94,
        FontFamily = new FontFamily("Cascadia Mono, DejaVu Sans Mono"),
        TextWrapping = TextWrapping.NoWrap
    };
    private readonly ListBox _hostList = new();
    private readonly CliTraceView _commandBox;
    private readonly NumericUpDown _interval = Number(1, 300, 2, 1, 70);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250, 90);
    private readonly NumericUpDown _downAfter = Number(2, 20, 3, 1, 70);
    private readonly NumericUpDown _recoverAfter = Number(1, 20, 2, 1, 70);
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
        Width = 1320;
        Height = 790;
        MinWidth = 900;
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

    private static Border StatCard(string title, TextBlock value, int column)
    {
        var content = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Theme.Label(title, 12, FontWeight.Normal, Theme.Muted),
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
                Theme.Label(title, 12, FontWeight.Normal, Theme.Muted),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { input, Theme.Label(suffix, 12, color: Theme.Muted) }
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

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step, double width) => NativeControls.Number(min, max, value, step, width);

    private void RefreshAll()
    {
        if (_loading || _closed) return;
        _loading = true;
        try
        {
            var snapshot = _engine.Snapshot();
            _monitorState.Text = snapshot.Monitoring ? "MONITORING" : "IDLE";
            _monitorState.Foreground = snapshot.Monitoring ? Theme.Green : Theme.Cyan;
            _startStop.Content = "Start Monitoring";
            _startStop.IsEnabled = !snapshot.Monitoring; _stop.IsEnabled = snapshot.Monitoring;
            _siteHeader.Text = snapshot.SelectedSite is null ? "All Sites • Hosts" : $"{snapshot.SelectedSite} • Hosts";
            _hostTable.SetScope(snapshot.SelectedSite is null);
            _startStop.Background = Theme.Brush("#1F7852");

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
        Width = 900;
        Height = 650;
        MinWidth = 760;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Window;
        Foreground = Theme.Text;

        _historyRange.ItemsSource = Presentation.HistoryRanges;
        var root = new Grid { RowDefinitions = new RowDefinitions("80,*,68") };
        var header = new StackPanel { Margin = new Thickness(32, 18), Children = {
            Theme.Label("Settings", 26.7, FontWeight.SemiBold),
            Theme.Label("Configure Ping Watchdog without crowding the monitoring workspace.", 12.7, color: Theme.Muted)
        } };
        root.Children.Add(header);
        var workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("186,*") };
        var nav = new ListBox { ItemsSource = Presentation.SettingsPages, Margin = new Thickness(14, 20, 10, 0), Background = Theme.Brush(Presentation.Navigation), BorderThickness = new Thickness(0) };
        workspace.Children.Add(nav);
        var pages = new Control[] { GeneralPage(), MonitoringPage(), HistoryPage(), UpdatesPage() };
        var page = new ContentControl(); Grid.SetColumn(page, 1); workspace.Children.Add(page);
        nav.SelectionChanged += (_, _) => { if (nav.SelectedIndex >= 0) page.Content = pages[nav.SelectedIndex]; };
        nav.SelectedIndex = 0;
        Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        var save = Theme.Button("Save Settings", true); save.Click += (_, _) => SaveValues();
        var close = Theme.Button("Close"); close.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 32, 12), Children = { save, close } };
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;

        _updates.Changed += UpdatesChanged;
        Closed += (_, _) => { _closed = true; _updates.Changed -= UpdatesChanged; };
        _updateAction.Click += async (_, _) => await main.RunUpdateActionAsync(this);
        Opened += (_, _) => LoadValues();
    }

    private Control GeneralPage()
    {
        var import = Theme.Button("Import Config..."); var export = Theme.Button("Export Config...");
        import.Click += async (_, _) => { await _main.ImportConfigAsync(this); LoadValues(); };
        export.Click += async (_, _) => await _main.ExportConfigAsync(this);
        return Page("General", "Application behavior and display defaults.",
            Section("Window behavior", "When enabled, minimizing Ping Watchdog hides it to the notification area while monitoring continues.", _minimizeToTray, _trayStatus),
            Section("Notifications", "Controls outage and recovery notifications. Monitoring and history recording continue either way.", _notifications),
            Section("CLI displays", "", _showCli, _wallboardCli),
            Section("Configuration", "Import or export the same configuration schema used by Windows.", new WrapPanel { Children = { import, export } }));
    }
    private Control MonitoringPage() => Page("Monitoring", "Ping cadence and outage detection defaults.",
        Section("Monitoring defaults", "Timing controls are locked while monitoring is active.",
            Row("Ping interval", _interval, "seconds"), Row("Ping timeout", _timeout, "milliseconds"),
            Row("Declare DOWN after", _down, "failures"), Row("Declare RECOVERED after", _recover, "successes")));
    private Control HistoryPage()
    {
        var history = Theme.Button("Open Outage History"); history.Click += (_, _) => new HistoryWindow(_engine).Show(this);
        return Page("History", "Saved history range and event filters.", Section("Outage history", "Filters never delete stored events.", Row("Default range", _historyRange, ""), _hideSuspects, history));
    }
    private Control UpdatesPage() => Page("Updates", "Version, automatic checks, and update installation.",
        Section("Installed version", "", Theme.Label(_updates.Version, 13.3, FontWeight.SemiBold), _updateStatus, _updateAction),
        Section("Update preferences", "Checks run in the background; downloaded updates prompt for a restart.", _autoUpdates, _developerUpdate));
    private static Control Page(string title, string subtitle, params Control[] controls)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(30, 24, 26, 24) };
        root.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(6, 0, 0, 20), Children = {
            Theme.Label(title, 21.3, FontWeight.SemiBold), Theme.Label(subtitle, 12.7, color: Theme.Muted)
        } });
        var stack = new StackPanel { Spacing = 12 };
        foreach (var control in controls) stack.Children.Add(control);
        var scroll = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll); return root;
    }
    private static Control Section(string title, string description, params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 10, Children = { Theme.Label(title, 14, FontWeight.SemiBold) } };
        foreach (var control in controls) stack.Children.Add(control);
        if (!string.IsNullOrWhiteSpace(description)) stack.Children.Add(new TextBlock { Text = description, FontSize = 12.7, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap });
        return Theme.CardBorder(stack, new Thickness(0));
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
                Theme.Label(suffix, 12, color: Theme.Muted)
            }
        };
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => NativeControls.Number(min, max, value, step, 110);

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
        Width = 1080;
        Height = 650;
        MinWidth = 820;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Window;
        Foreground = Theme.Text;

        _range.ItemsSource = Presentation.HistoryRanges;
        _range.SelectionChanged += (_, _) => ApplyFilterPreference();
        _hideSuspects.IsCheckedChanged += (_, _) => ApplyFilterPreference();
        _site.SelectionChanged += (_, _) => Refresh();

        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(Theme.Label("Range", 10, color: Theme.Muted));
        filters.Children.Add(_range);
        filters.Children.Add(Theme.Label("Site", 10, color: Theme.Muted));
        filters.Children.Add(_site);
        filters.Children.Add(_hideSuspects);
        var export = Theme.Button("Export CSV");
        export.Click += async (_, _) => await ExportCsvAsync();
        filters.Children.Add(export);
        foreach (var control in filters.Children) control.Margin = new Thickness(0, 4, 8, 4);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Margin = new Thickness(16),
            Children =
            {
                Theme.Label("Outage History", 20, FontWeight.Bold),
                At(filters, 1),
                At(_summary, 2),
                At(new HistoryTableView(_events), 3)
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
                _events.ItemsSource = _visibleEvents.Select(e => new HistoryRow(e)).ToList();
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
    private readonly TreeView _items = new();
    private readonly TextBlock _details = Theme.Label("Select a site or folder to organize.", 12.7, color: Theme.Muted);
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

        var toolbar = new WrapPanel();
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
        foreach (var button in new[] { newSite, newFolder, rename, move, delete, open }) { button.Margin = new Thickness(0, 2, 6, 2); toolbar.Children.Add(button); }

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16),
            Children =
            {
                Theme.Label("Site Organization", 20, FontWeight.Bold),
                At(toolbar, 1),
                At(_items, 2),
                At(_details, 3)
            }
        };
        _items.SelectionChanged += (_, _) => _details.Text = Selected is { } selected ? selected.IsFolder ? $"Folder: {selected.Key}" : $"Site: {selected.Key} • {_engine.SiteDefinitions().FirstOrDefault(s => s.Name == selected.Key)?.Hosts.Count ?? 0} hosts" : "Select a site or folder to organize.";
        _items.DoubleTapped += (_, _) => OpenSelected();
        Opened += (_, _) => Refresh();
        _engine.Changed += EngineChanged;
        Closed += (_, _) => { _closed = true; _engine.Changed -= EngineChanged; };
    }

    private void EngineChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (_closed) return;
        string? selectedKey = Selected is { } previous ? $"{previous.IsFolder}:{previous.Key}" : null;
        var expanded = _items.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(item => item.IsExpanded && item.DataContext is OrgItem).Select(item => ((OrgItem)item.DataContext!).Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool first = _rows.Count == 0;
        _rows.Clear();
        var roots = new List<TreeViewItem>();
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        TreeViewItem? selection = null;
        foreach (var folder in _engine.FolderSnapshot().OrderBy(f => f.Count(ch => ch == '/')).ThenBy(f => f))
        {
            var model = new OrgItem(true, folder, folder.Split('/').Last()); _rows.Add(model);
            var item = new TreeViewItem { Header = model.Display, DataContext = model, IsExpanded = first || expanded.Contains(folder) };
            folders[folder] = item;
            string parent = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : "";
            if (folders.TryGetValue(parent, out var parentItem)) parentItem.Items.Add(item); else roots.Add(item);
            if (selectedKey == $"True:{folder}") selection = item;
        }
        foreach (var site in _engine.SiteDefinitions().OrderBy(s => s.FolderPath).ThenBy(s => s.Name))
        {
            var model = new OrgItem(false, site.Name, $"{site.Name} ({site.Hosts.Count})"); _rows.Add(model);
            var item = new TreeViewItem { Header = model.Display, DataContext = model };
            if (folders.TryGetValue(site.FolderPath, out var folder)) folder.Items.Add(item); else roots.Add(item);
            if (selectedKey == $"False:{site.Name}") selection = item;
        }
        _items.ItemsSource = roots;
        _items.SelectedItem = selection;
    }

    private OrgItem? Selected => (_items.SelectedItem as TreeViewItem)?.DataContext as OrgItem;

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
