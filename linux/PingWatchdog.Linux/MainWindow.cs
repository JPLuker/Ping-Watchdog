// The main window: sidebar (All sites, sites, Outage history, Settings, Check for updates, More) and a
// work area with Map and List over one shared Ping activity pane. One primary action: Start monitoring.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal sealed class MainWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
    private readonly PriorityAlarm _alarm;
    private readonly HostActions _actions;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Button _allSites = Ui.Button("All sites", "pill", "All sites");
    private readonly StackPanel _siteList = new() { Spacing = 2 };
    private readonly Button _checkUpdates = Ui.Button("Check for updates", "nav");
    private readonly TextBlock _title = Ui.Title("All hosts");
    private readonly Button _addHosts = Ui.Button("Add hosts");
    private readonly Button _wallboardButton = Ui.Button("Wallboard");
    private readonly Button _startStop = Ui.Button("Start monitoring", "primary");
    private readonly ToggleButton _mapToggle = new() { Content = "Map", Classes = { "segment" } };
    private readonly ToggleButton _listToggle = new() { Content = "List", Classes = { "segment" } };
    private readonly TextBlock _searchLabel = Ui.Text("Search", Tokens.Body, "Pw.Text2");
    private readonly TextBox _search = new() { Width = 240 };
    private readonly Button _columns = Ui.Button("Columns");
    private readonly CheckBox _showLog = new() { Content = "Show ping log" };
    private readonly TextBlock _summary = Ui.Text("", Tokens.Title3, weight: FontWeight.SemiBold);
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Space2 };
    private readonly Dictionary<HostFilter, ToggleButton> _chipButtons = new();
    private readonly TopologyView _map = new();
    private readonly HostListView _list = new();
    private readonly Border _mapCard;
    private readonly Border _listCard;
    private readonly PingActivityView _ping;
    private readonly PriorityBanner _banner;
    private readonly UpdatePopup _updatePopup = new();
    private readonly StatusToast _toast;
    private readonly TextBlock _versionOverlay = VersionOverlay.Create();
    private readonly Dictionary<string, Button> _siteButtons = new(StringComparer.OrdinalIgnoreCase);
    private LinuxTrayService? _tray;
    private WallboardWindow? _wallboard;
    private SettingsWindow? _settings;
    private HistoryWindow? _history;
    private OrganizationWindow? _organization;
    private bool _dirty = true;
    private bool _loading;
    private bool _closed;
    private bool _backgroundStarted;
    private string _siteSignature = "";
    private WatchdogSnapshot? _last;

    public MainWindow(WatchdogEngine engine, LinuxUpdateService updates, PriorityAlarm? alarm = null)
    {
        _engine = engine;
        _updates = updates;
        _alarm = alarm ?? new PriorityAlarm();
        _alarm.SoundPath = engine.Config.PriorityAlarmSound;
        _actions = new HostActions(engine, this, ShowStatus, ShowHostInList);
        _ping = new PingActivityView(engine.ClearCommandLog);
        _banner = new PriorityBanner(_alarm);
        _toast = new StatusToast(() => Undo());
        _mapCard = Ui.Card(_map, Tokens.Space3);
        _listCard = Ui.Card(_list, 0);
        _listCard.Padding = new Thickness(0, Tokens.Space1, 0, 0);

        Title = "Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 1240;
        Height = 780;
        MinWidth = 900;
        MinHeight = 600;
        Content = BuildLayout();

        _engine.Changed += EngineChanged;
        _engine.PriorityStateChanged += PriorityChanged;
        _updates.Changed += UpdatesChanged;
        _updates.UpdateReady += UpdateReady;
        _timer.Tick += (_, _) => { if (_dirty) RefreshAll(); };
        Tokens.Changed += ThemeChanged;
        WireEvents();

        Opened += (_, _) =>
        {
            ApplyPreferences();
            RefreshAll();
            _timer.Start();
            if (!_backgroundStarted)
            {
                _backgroundStarted = true;
                _ = _updates.RunBackgroundAsync(() => _engine.Config.AutoCheckUpdates);
                if (_engine.Preferences.StartMonitoringOnLaunch && _engine.HasProbeTargets()) _engine.StartMonitoring();
                if (_engine.Preferences.OpenWallboardOnStartup) Dispatcher.UIThread.Post(OpenWallboard, DispatcherPriority.Background);
            }
            (_listToggle.IsChecked == true ? (Control)_list.List : _map).Focus();
        };
        Closing += (_, _) =>
        {
            _timer.Stop();
            _engine.Save();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _engine.Changed -= EngineChanged;
            _engine.PriorityStateChanged -= PriorityChanged;
            _updates.Changed -= UpdatesChanged;
            _updates.UpdateReady -= UpdateReady;
            Tokens.Changed -= ThemeChanged;
            _alarm.Dispose();
            _tray?.Dispose();
            foreach (Window window in new Window?[] { _wallboard, _settings, _history, _organization }.OfType<Window>()) window.Close();
        };
    }

    // ---- For the tray and tests ----
    internal WatchdogEngine Engine => _engine;
    internal LinuxUpdateService Updates => _updates;
    internal PriorityAlarm Alarm => _alarm;
    internal HostActions Actions => _actions;
    internal TopologyView Map => _map;
    internal HostListView HostList => _list;
    internal PingActivityView Ping => _ping;
    internal PriorityBanner Banner => _banner;
    internal UpdatePopup UpdatePopupControl => _updatePopup;
    internal StatusToast Toast => _toast;
    internal bool ListVisible => _listCard.IsVisible;
    internal string HeaderText => _title.Text ?? "";
    internal string SummaryText => _summary.Text ?? "";
    internal string StartStopText => _startStop.Content as string ?? "";
    internal string AllSitesText => (_allSites.Content as TextBlock)?.Text ?? "";
    internal IReadOnlyList<string> SiteButtonNames => _siteButtons.Keys.ToList();
    internal bool VersionOverlayVisible => _versionOverlay.IsVisible;
    internal WallboardWindow? Wallboard => _wallboard;
    internal SettingsWindow? Settings => _settings;
    internal HistoryWindow? History => _history;
    internal OrganizationWindow? Organization => _organization;
    internal Control MapCard => _mapCard;
    internal Control ListCard => _listCard;
    internal ToggleButton ChipFor(HostFilter filter) => _chipButtons[filter];
    internal TextBox SearchBox => _search;
    internal CheckBox ShowLogBox => _showLog;
    internal Button CheckUpdatesButton => _checkUpdates;

    public void AttachTray(LinuxTrayService tray) => _tray = tray;
    public bool CommitHostEdits() => true;

    private Control BuildLayout()
    {
        // Sidebar
        var addSite = Ui.Button("+", "ghost", "Add site");
        addSite.Padding = new Thickness(10, 4);
        addSite.Click += async (_, _) => await AddSiteAsync();
        _allSites.HorizontalAlignment = HorizontalAlignment.Stretch;
        _allSites.HorizontalContentAlignment = HorizontalAlignment.Left;
        _allSites.Content = Ui.Text("All sites");
        _allSites.Click += (_, _) => SelectSite(null);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Tokens.Space2 };
        top.Children.Add(_allSites);
        Grid.SetColumn(addSite, 1);
        top.Children.Add(addSite);
        var siteScroll = new ScrollViewer { Content = _siteList, Margin = new Thickness(0, Tokens.Space3, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        siteScroll.ContextRequested += (_, e) => { e.Handled = true; OpenSiteMenu(siteScroll); };
        var history = Ui.Button("Outage history", "nav");
        history.Click += (_, _) => OpenHistory();
        var settings = Ui.Button("Settings", "nav");
        settings.Click += (_, _) => OpenSettings();
        _checkUpdates.Click += async (_, _) => await RunUpdateActionAsync();
        var more = Ui.Button("More", "nav", "More actions");
        more.Click += (_, _) => OpenMoreMenu(more);
        var utilities = Ui.Column(2, Ui.Divider(new Thickness(Tokens.Space2, Tokens.Space3, Tokens.Space2, Tokens.Space2)), history, settings, _checkUpdates, more);
        var sidebar = new DockPanel { Margin = new Thickness(Tokens.Space4, Tokens.Space5, Tokens.Space3, Tokens.Space4) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(utilities, Dock.Bottom);
        sidebar.Children.Add(top);
        sidebar.Children.Add(utilities);
        sidebar.Children.Add(siteScroll);

        // Header
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _addHosts.Click += (_, _) => OpenAddHostsMenu();
        _wallboardButton.Click += (_, _) => OpenWallboard();
        _startStop.Click += (_, _) => ToggleMonitoring();
        var actions = Ui.Row(Tokens.Space2, _addHosts, _wallboardButton, _startStop);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, Tokens.Space4) };
        header.Children.Add(_title);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        // Toolbar
        Avalonia.Automation.AutomationProperties.SetName(_search, "Search hosts");
        ClipboardText.Attach(_search);
        _columns.Click += (_, _) => OpenColumnsMenu();
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, Tokens.Space3) };
        foreach (var control in new Control[] { _mapToggle, _listToggle, _searchLabel, _search, _columns, _showLog })
        {
            control.Margin = new Thickness(0, 0, control == _mapToggle || control == _searchLabel ? Tokens.Space2 : Tokens.Space4, Tokens.Space1);
            control.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(control);
        }
        foreach (var filter in Enum.GetValues<HostFilter>())
        {
            var chip = new ToggleButton { Content = filter.ToString(), Classes = { "chip" } };
            HostFilter captured = filter;
            chip.Click += (_, _) => SetFilter(captured);
            _chipButtons[filter] = chip;
            _chips.Children.Add(chip);
        }
        _chips.Margin = new Thickness(0, 0, 0, Tokens.Space3);
        _summary.Margin = new Thickness(0, 0, 0, Tokens.Space3);

        _ping.Height = 150;
        _ping.Margin = new Thickness(0, Tokens.Space4, 0, 0);

        var work = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"), Margin = new Thickness(Tokens.Space5, Tokens.Space5, Tokens.Space6, Tokens.Space4) };
        void Put(Control control, int row) { Grid.SetRow(control, row); work.Children.Add(control); }
        Put(_banner, 0);
        Put(header, 1);
        Put(toolbar, 2);
        var sub = new Panel();
        sub.Children.Add(_summary);
        sub.Children.Add(_chips);
        Put(sub, 3);
        var body = new Panel();
        body.Children.Add(_mapCard);
        body.Children.Add(_listCard);
        Put(body, 4);
        Put(_ping, 5);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("236,*") };
        grid.Children.Add(sidebar);
        Grid.SetColumn(work, 1);
        grid.Children.Add(work);

        var root = new Panel();
        root.Children.Add(grid);
        root.Children.Add(_toast);
        root.Children.Add(_updatePopup);
        root.Children.Add(_versionOverlay);
        return root;
    }

    private void WireEvents()
    {
        _mapToggle.Click += (_, _) => SetListView(false);
        _listToggle.Click += (_, _) => SetListView(true);
        _search.TextChanged += (_, _) => _list.SetSearch(_search.Text ?? "");
        _showLog.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            var config = _engine.Snapshot().Settings;
            config.ShowCommandView = _showLog.IsChecked == true;
            _engine.ApplySettings(config);
        };
        _map.HostContextRequested += (host, point) => _actions.ShowMenu(_map, point, new[] { Fresh(host) }, fromList: false);
        _map.HostActivated += host => _ = _actions.EditAsync(Fresh(host));
        _list.HostActivated += host => _ = _actions.EditAsync(Fresh(host));
        _list.ContextRequested2 += (anchor, point) => _actions.ShowMenu(anchor, point, _list.SelectedHosts, fromList: true);
        _list.DeleteRequested += () => _ = _actions.RemoveAsync(_list.SelectedHosts);
        KeyDown += (_, e) =>
        {
            bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool inText = FocusManager?.GetFocusedElement() is TextBox;
            if (e.Key == Key.F11) { e.Handled = true; OpenWallboard(); }
            else if (control && e.Key == Key.Z && !inText) { e.Handled = true; Undo(); }
            else if (control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.H) { e.Handled = true; ShowList(); }
            else if (control && e.Key == Key.H) { e.Handled = true; OpenHistory(); }
            else if (control && e.Key == Key.OemComma) { e.Handled = true; OpenSettings(); }
            else if (control && e.Key == Key.F) { e.Handled = true; ShowList(); _search.Focus(); }
            else if (control && e.Key == Key.N) { e.Handled = true; _ = OpenAddHostsAsync(false); }
            else if (e.Key == Key.Escape && _search.IsFocused && (_search.Text ?? "").Length > 0) { e.Handled = true; _search.Text = ""; }
        };
        PropertyChanged += async (_, e) =>
        {
            if (e.Property == WindowStateProperty && e.NewValue is WindowState state && state == WindowState.Minimized && _tray is not null)
                await _tray.MinimizeAsync(requested: true);
        };
    }

    private HostSnapshot Fresh(HostSnapshot host) =>
        _engine.Snapshot().Sites.SelectMany(s => s.Hosts).FirstOrDefault(h => h.Key == host.Key) ?? host;

    private void EngineChanged(object? sender, EventArgs e) => _dirty = true;

    private void ThemeChanged()
    {
        _dirty = true;
        RefreshAll();
    }

    internal void RefreshAll()
    {
        if (_loading || _closed) return;
        _loading = true;
        _dirty = false;
        try
        {
            var snapshot = _engine.Snapshot();
            _last = snapshot;
            var allHosts = snapshot.Sites.SelectMany(site => site.Hosts).ToList();
            _title.Text = snapshot.SelectedSite ?? "All hosts";
            _startStop.Content = snapshot.Monitoring ? "Stop monitoring" : "Start monitoring";
            ((TextBlock)_allSites.Content!).Text = $"All sites · {WatchdogEngine.Hosts(allHosts.Count)}";
            SetSelected(_allSites, snapshot.SelectedSite is null);
            RefreshSites(snapshot);

            var scoped = snapshot.SelectedSite is null
                ? snapshot.Sites
                : snapshot.Sites.Where(s => s.Name.Equals(snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase)).ToList();
            var monitored = snapshot.Hosts.Where(h => h.Monitoring && !h.Disabled && !h.Snoozed).ToList();
            _summary.Text = $"{monitored.Count(h => h.State == HostState.Online)} online · {monitored.Count(h => h.State == HostState.Suspect)} suspect · {monitored.Count(h => h.State == HostState.Offline)} offline";
            _map.SetSites(scoped);
            _list.Update(snapshot.Hosts, snapshot.SelectedSite is null);
            foreach (var (filter, chip) in _chipButtons)
            {
                chip.Content = filter == HostFilter.All ? "All" : $"{filter} {HostListView.Count(snapshot.Hosts, filter)}";
                chip.IsChecked = _list.Filter == filter;
            }

            _showLog.IsChecked = snapshot.Settings.ShowCommandView;
            _ping.IsVisible = snapshot.Settings.ShowCommandView;
            _ping.Refresh(snapshot.Commands, snapshot.Monitoring ? "Waiting for replies" : "No ping activity");
            _checkUpdates.Content = _updates.ActionText;
            _alarm.SoundPath = snapshot.Settings.PriorityAlarmSound;
            // Hosts that lost priority, or were removed, stop ringing.
            _alarm.Forget(a => !allHosts.Any(h => h.Options.Priority && h.Key.Matches(a.Site, a.Address)));
            _wallboard?.Refresh(snapshot);
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshSites(WatchdogSnapshot snapshot)
    {
        string signature = string.Join("\u001f", snapshot.Sites.Select(s => s.Name + "\u001e" + s.Temporary)) + "|" + snapshot.SelectedSite;
        if (signature != _siteSignature)
        {
            _siteSignature = signature;
            _siteList.Children.Clear();
            _siteButtons.Clear();
            foreach (var site in snapshot.Sites)
            {
                var text = Ui.Text(site.Temporary ? site.Name + " (temporary)" : site.Name);
                var indicator = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, Tokens.Space2, 0), IsVisible = false }
                    .Res(Border.BackgroundProperty, "Pw.Text");
                var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { indicator, text } };
                var button = new Button { Content = content, Classes = { "nav" }, Tag = indicator };
                Avalonia.Automation.AutomationProperties.SetName(button, site.Name);
                ToolTip.SetTip(button, $"{site.Name} · {WatchdogEngine.Hosts(site.Hosts.Count)}");
                string name = site.Name;
                button.Click += (_, _) => SelectSite(name);
                button.ContextRequested += (_, e) => { e.Handled = true; SelectSite(name); OpenSiteMenu(button); };
                _siteButtons[site.Name] = button;
                _siteList.Children.Add(button);
            }
        }
        foreach (var (name, button) in _siteButtons)
        {
            bool selected = name.Equals(snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase);
            SetSelected(button, selected);
            if (button.Tag is Border indicator) indicator.IsVisible = selected;
        }
    }

    private static void SetSelected(Button button, bool selected)
    {
        if (selected) { if (!button.Classes.Contains("selected")) button.Classes.Add("selected"); }
        else button.Classes.Remove("selected");
        if (button.Classes.Contains("pill"))
        {
            // All sites: filled when it's the scope, outlined otherwise.
            button.Res(BackgroundProperty, selected ? "Pw.Surface3" : "Pw.Canvas");
            button.Res(BorderBrushProperty, selected ? "Pw.Surface3" : "Pw.BorderStrong");
        }
    }

    internal void SelectSite(string? name)
    {
        _engine.SelectedSite = name;
        RefreshAll();
    }

    internal void SetListView(bool list)
    {
        _mapToggle.IsChecked = !list;
        _listToggle.IsChecked = list;
        _mapCard.IsVisible = !list;
        _listCard.IsVisible = list;
        _summary.IsVisible = !list;
        _chips.IsVisible = list;
        _searchLabel.IsVisible = _search.IsVisible = _columns.IsVisible = list;
        if (_engine.Preferences.ListView != list)
        {
            var prefs = _engine.Preferences.Copy();
            prefs.ListView = list;
            _engine.SavePreferences(prefs);
        }
    }

    internal void ShowList()
    {
        SetListView(true);
        _list.List.Focus();
    }

    private void ShowHostInList(HostSnapshot host)
    {
        _wallboard?.Close();
        RestoreFromTray();
        _search.Text = "";
        SetFilter(HostFilter.All);
        if (_engine.SelectedSite is not null && !host.Site.Equals(_engine.SelectedSite, StringComparison.OrdinalIgnoreCase)) SelectSite(host.Site);
        ShowList();
        _list.Select(host.Key);
    }

    internal void SetFilter(HostFilter filter)
    {
        _list.SetFilter(filter);
        foreach (var (key, chip) in _chipButtons) chip.IsChecked = key == filter;
    }

    // ---- Preferences ----

    internal void ApplyPreferences()
    {
        var prefs = _engine.Preferences;
        Tokens.SetMode(prefs.Appearance);
        _versionOverlay.IsVisible = prefs.ShowVersionOverlay;
        _list.SetHiddenColumns(prefs.HiddenColumns);
        SetListView(prefs.ListView);
        _wallboard?.ApplyPreferences();
        _settings?.ApplyOverlay();
        _history?.ApplyOverlay();
    }

    private void OpenColumnsMenu()
    {
        var hidden = _engine.Preferences.HiddenColumns.ToHashSet();
        var items = HostListView.AllColumns.Where(c => c.Optional).Select(column =>
        {
            var item = new MenuItem { Header = column.Header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = !hidden.Contains(column.Key) };
            item.Click += (_, _) =>
            {
                var prefs = _engine.Preferences.Copy();
                if (prefs.HiddenColumns.Contains(column.Key)) prefs.HiddenColumns.Remove(column.Key);
                else prefs.HiddenColumns.Add(column.Key);
                _engine.SavePreferences(prefs);
                _list.SetHiddenColumns(prefs.HiddenColumns);
            };
            return (Control)item;
        }).ToList();
        new ContextMenu { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft }.Open(_columns);
    }

    // ---- Sites ----

    private void OpenSiteMenu(Control anchor)
    {
        bool site = _engine.SelectedSite is not null;
        var items = new List<Control>
        {
            Ui.MenuItem("Add site…", () => _ = AddSiteAsync()),
            Ui.MenuItem("Organize sites…", OpenOrganization),
            Ui.MenuItem("Rename site…", site ? () => _ = RenameSiteAsync() : null),
            new Separator(),
            Ui.MenuItem("Delete site…", site ? () => _ = DeleteSiteAsync() : null),
        };
        new ContextMenu { ItemsSource = items }.Open(anchor);
    }

    internal async Task AddSiteAsync()
    {
        var name = await Dialogs.Prompt(this, "Add site", "Site name", "", "Add", value =>
            _engine.SiteDefinitions().Any(s => s.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) ? "A site with that name already exists." : null);
        if (name is null) return;
        string? error = _engine.AddSite(name);
        if (error is not null) await Dialogs.Message(this, "Add site", error);
        else ShowStatus("Added " + name, false);
    }

    private async Task RenameSiteAsync()
    {
        string? current = _engine.SelectedSite;
        if (current is null) return;
        var name = await Dialogs.Prompt(this, "Rename site", "Site name", current, "Rename", value =>
            !value.Equals(current, StringComparison.OrdinalIgnoreCase) && _engine.SiteDefinitions().Any(s => s.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                ? "A site with that name already exists." : null);
        if (name is null) return;
        string? error = _engine.RenameSite(current, name);
        if (error is not null) await Dialogs.Message(this, "Rename site", error);
    }

    private async Task DeleteSiteAsync()
    {
        var snapshot = _engine.Snapshot();
        var site = snapshot.Sites.FirstOrDefault(s => s.Name.Equals(snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase));
        if (site is null) return;
        if (!await Dialogs.Confirm(this, "Delete site", $"Delete site \"{site.Name}\" and its {WatchdogEngine.Hosts(site.Hosts.Count)}?", "Delete", destructive: true)) return;
        string? error = _engine.DeleteSite(site.Name);
        if (error is not null) await Dialogs.Message(this, "Delete site", error);
        else ShowStatus("Deleted " + site.Name, true);
    }

    internal void OpenOrganization()
    {
        if (_organization is { IsVisible: true }) { _organization.Activate(); return; }
        _organization = new OrganizationWindow(_engine);
        _organization.Closed += (_, _) => _organization = null;
        _organization.Show(this);
    }

    // ---- Hosts ----

    private void OpenAddHostsMenu()
    {
        var items = new List<Control>
        {
            Ui.MenuItem("Save hosts to a site…", () => _ = OpenAddHostsAsync(false), gesture: "Ctrl+N"),
            Ui.MenuItem("Monitor temporarily…", () => _ = OpenAddHostsAsync(true)),
        };
        new ContextMenu { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft }.Open(_addHosts);
    }

    internal async Task OpenAddHostsAsync(bool temporary)
    {
        var dialog = new AddHostsDialog(_engine, temporary, _engine.SelectedSite);
        var added = await dialog.ShowDialog<int?>(this);
        if (added is { } count)
            ShowStatus(temporary ? $"Watching {WatchdogEngine.Hosts(count)} for this session" : $"Added {WatchdogEngine.Hosts(count)}", !temporary);
    }

    internal void Undo()
    {
        string? message = _engine.Undo();
        ShowStatus(message ?? "Nothing to undo", false);
    }

    internal void ShowStatus(string message, bool undo) => _toast.Show(message, undo && _engine.UndoDescription is not null);

    // ---- Monitoring ----

    public void ToggleMonitoring()
    {
        if (_engine.Monitoring)
        {
            _engine.StopMonitoring();
        }
        else if (!_engine.HasProbeTargets())
        {
            _ = Dialogs.Message(this, "Ping Watchdog", "Add at least one enabled IP address or hostname to a site.");
            return;
        }
        else _engine.StartMonitoring();
        RefreshAll();
    }

    private void PriorityChanged(PriorityStateChange change) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        string display = string.IsNullOrWhiteSpace(change.Host.Label) ? change.Host.Address : $"{change.Host.Label} ({change.Host.Address})";
        var alert = _alarm.Raise(change.Host.Site, change.Host.Address, display, change.Offline, change.At);
        _engine.Notify("Priority · " + (change.Offline ? $"{display} is offline" : $"{display} is back online"), change.Host.Site, true);
        _dirty = true;
    });

    // ---- Windows ----

    public void OpenWallboard()
    {
        if (_wallboard is { IsVisible: true }) { _wallboard.Activate(); return; }
        _wallboard = new WallboardWindow(this, _engine, _alarm);
        _wallboard.Closed += (_, _) => { _wallboard = null; RestoreFromTray(); };
        _wallboard.Show();
        _wallboard.Refresh(_engine.Snapshot());
    }

    public void OpenSettings(string? page = null)
    {
        if (_settings is { IsVisible: true }) { _settings.Activate(); if (page is not null) _settings.SelectPage(page); return; }
        _settings = new SettingsWindow(this, _engine, _updates);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(this);
        if (page is not null) _settings.SelectPage(page);
    }

    public void OpenHistory()
    {
        if (_history is { IsVisible: true }) { _history.Activate(); return; }
        _history = new HistoryWindow(_engine);
        _history.Closed += (_, _) => _history = null;
        _history.Show(this);
    }

    private void OpenMoreMenu(Control anchor)
    {
        var items = new List<Control>
        {
            Ui.MenuItem("Host list", ShowList, gesture: "Ctrl+Shift+H"),
            Ui.MenuItem("Settings…", () => OpenSettings(), gesture: "Ctrl+OemComma"),
            Ui.MenuItem("Outage history…", OpenHistory, gesture: "Ctrl+H"),
            Ui.MenuItem("Organize sites…", OpenOrganization),
            new Separator(),
            Ui.MenuItem("Export configuration…", () => _ = ExportConfigAsync()),
            Ui.MenuItem("Import configuration…", () => _ = ImportConfigAsync()),
            new Separator(),
            Ui.MenuItem("About Ping Watchdog", () => OpenSettings("About")),
        };
        new ContextMenu { ItemsSource = items, Placement = PlacementMode.TopEdgeAlignedLeft }.Open(anchor);
    }

    private async Task ExportConfigAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export configuration",
            SuggestedFileName = "PingWatchdog.pingwatch.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("Ping Watchdog configuration") { Patterns = new[] { "*.pingwatch.json", "*.json" } } }
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_engine.ExportConfigJson());
            ShowStatus("Exported the configuration", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, "Export configuration", "Couldn't save the file: " + ex.Message);
        }
    }

    private async Task ImportConfigAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import configuration",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Ping Watchdog configuration") { Patterns = new[] { "*.pingwatch.json", "*.json" } } }
        });
        if (files.Count == 0) return;
        string json;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, "Import configuration", "Couldn't read the file: " + ex.Message);
            return;
        }
        if (!await Dialogs.Confirm(this, "Import configuration", "Replace your sites and hosts with this configuration? You can undo it with Ctrl+Z.", "Import")) return;
        string? error = _engine.ImportConfigJson(json);
        if (error is not null) await Dialogs.Message(this, "Import configuration", error);
        else ShowStatus("Imported the configuration", true);
    }

    // ---- Updates ----

    private void UpdatesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        _checkUpdates.Content = _updates.ActionText;
        if (_updatePopup.IsVisible && !_updates.HasPendingUpdate) _updatePopup.Show(_updates.Status, Unmanaged ? "Open downloads page" : null, Unmanaged ? LinuxUpdateService.OpenDownloads : null);
        _settings?.RefreshUpdates();
    });

    private static bool Unmanaged => LinuxUpdateService.PackageKind is "deb" or "rpm" or "tar.gz" or "flatpak";

    private void UpdateReady(object? sender, string version) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        _updatePopup.Show($"Version {version} is ready.", "Restart now", ApplyUpdate);
    });

    public async Task RunUpdateActionAsync()
    {
        if (_updates.HasPendingUpdate) { ApplyUpdate(); return; }
        _updatePopup.Show("Checking for updates…");
        await _updates.CheckAsync(userInitiated: true);
        if (!_updates.HasPendingUpdate)
            _updatePopup.Show(_updates.Status, Unmanaged ? "Open downloads page" : null, Unmanaged ? LinuxUpdateService.OpenDownloads : null);
    }

    private void ApplyUpdate()
    {
        try { _updates.ApplyAndRestart(_engine); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _updatePopup.Show("Couldn't install the update: " + ex.Message + " Monitoring was resumed.");
        }
    }

    // ---- Lifetime ----

    public void RestoreFromTray()
    {
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    public void ExitApplication()
    {
        Close();
    }
}
