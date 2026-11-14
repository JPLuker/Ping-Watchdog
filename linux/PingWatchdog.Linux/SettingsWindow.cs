// Settings: General, Appearance, Monitoring, Notifications, History, Updates, Data and storage, Advanced, About.
// Changes are staged and saved with Save changes or Ctrl+S; closing with unsaved changes asks first.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace PingWatchdog.Linux;

internal sealed class SettingsWindow : Window
{
    internal static readonly string[] Pages = { "General", "Appearance", "Monitoring", "Notifications", "History", "Updates", "Data and storage", "Advanced", "About" };
    private readonly MainWindow _main;
    private readonly WatchdogEngine _engine;
    private readonly LinuxUpdateService _updates;
    private readonly ListBox _nav = new() { Width = 180 };
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Button _save = Ui.Button("Save changes", "primary");
    private readonly TextBlock _saveState = Ui.Text("Saved", Tokens.Label, "Pw.Text2");
    private readonly TextBlock _versionOverlay = VersionOverlay.Create();
    private WatchdogConfig _config;
    private LinuxPreferences _prefs;
    private bool _startAtLogin;
    private bool _loading;
    private bool _closeConfirmed;

    // Controls
    private readonly CheckBox _minimizeToTray = new() { Content = "Minimize to the system tray" };
    private readonly CheckBox _startWithDesktop = new() { Content = "Open Ping Watchdog when you sign in" };
    private readonly CheckBox _autoStart = new() { Content = "Start monitoring automatically when the app opens" };
    private readonly CheckBox _wallboardOnStartup = new() { Content = "Open the Wallboard when the app opens" };
    private readonly CheckBox _restoreLastSite = new() { Content = "Restore the last selected site on startup" };
    private readonly ComboBox _appearance = new() { Width = 180, ItemsSource = new[] { "System", "Light", "Dark" } };
    private readonly CheckBox _use12Hour = new() { Content = "Use a 12-hour clock" };
    private readonly CheckBox _mainLog = new() { Content = "Show ping log in the main window" };
    private readonly CheckBox _wallboardLog = new() { Content = "Show ping log on the Wallboard" };
    private readonly NumericUpDown _interval = NativeControls.Number(1, 300, 2, 1, 110);
    private readonly NumericUpDown _timeout = NativeControls.Number(250, 10000, 1000, 250, 110);
    private readonly NumericUpDown _downAfter = NativeControls.Number(2, 20, 3, 1, 110);
    private readonly NumericUpDown _recoverAfter = NativeControls.Number(1, 20, 2, 1, 110);
    private readonly TextBlock _monitorLock = Ui.Text("Stop monitoring to change these.", Tokens.Body, "Pw.Text2");
    private readonly CheckBox _notifications = new() { Content = "Desktop notifications" };
    private readonly TextBlock _alarmSound = Ui.Text("", Tokens.Body);
    private readonly ComboBox _historyRange = new() { Width = 180, ItemsSource = new[] { "Last 24 hours", "Last 7 days", "Last 30 days", "All time" } };
    private readonly CheckBox _hideSuspects = new() { Content = "Hide suspect events" };
    private readonly TextBlock _updateStatus = Ui.Text("", Tokens.Body);
    private readonly CheckBox _autoUpdates = new() { Content = "Check for updates once a day" };
    private readonly Button _updateButton = Ui.Button("Check for updates");
    private readonly CheckBox _versionOverlayBox = new() { Content = "Show the version in the corner of every window" };
    private readonly TextBlock _dataStatus = Ui.Text("", Tokens.Body, "Pw.Text2");
    private readonly TextBlock _aboutStatus = Ui.Text("", Tokens.Body, "Pw.Text2");

    private static readonly int[] RangeHours = { 24, 168, 720, 0 };

    public SettingsWindow(MainWindow main, WatchdogEngine engine, LinuxUpdateService updates)
    {
        _main = main;
        _engine = engine;
        _updates = updates;
        _config = engine.Snapshot().Settings;
        _prefs = engine.Preferences.Copy();
        _startAtLogin = Autostart.Enabled;
        Title = "Settings • Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 860;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;

        _pages["General"] = Page("General",
            Section("Window behavior", "Monitoring keeps running while it's hidden.", _minimizeToTray),
            Section("Startup", "The Wallboard opens on the screen it was last on.", _startWithDesktop, _autoStart, _wallboardOnStartup, _restoreLastSite));
        _pages["Appearance"] = Page("Appearance",
            Section("Appearance", "System follows your desktop.", _appearance),
            Section("Time display", "", _use12Hour),
            Section("Ping log", "", _mainLog, _wallboardLog));
        _pages["Monitoring"] = Page("Monitoring",
            NativeControls.SettingRow("Ping interval", _interval, "seconds"),
            NativeControls.SettingRow("Ping timeout", _timeout, "milliseconds"),
            NativeControls.SettingRow("Declare offline after", _downAfter, "consecutive failures"),
            NativeControls.SettingRow("Declare recovered after", _recoverAfter, "consecutive successes"),
            _monitorLock);
        _pages["Notifications"] = Page("Notifications",
            Section("Desktop notifications", "History still records outages when alerts are off.", _notifications),
            Section("Priority alarm", "Priority hosts ring every 15 seconds when they go down or come back, until you acknowledge it.",
                _alarmSound, SoundButtons()));
        var openHistory = Ui.Button("Open outage history");
        openHistory.Click += (_, _) => _main.OpenHistory();
        _pages["History"] = Page("History",
            NativeControls.SettingRow("Default history range", _historyRange, ""),
            Section("", "Only changes what's shown. Events are never deleted.", _hideSuspects),
            openHistory);
        _updateButton.Click += async (_, _) => await _main.RunUpdateActionAsync();
        var downloads = Ui.Button("Open downloads page");
        downloads.Click += (_, _) => LinuxUpdateService.OpenDownloads();
        _pages["Updates"] = Page("Updates",
            Section("Update status", "", _updateStatus),
            _autoUpdates,
            Ui.Row(Tokens.Space2, _updateButton, downloads));
        _pages["Data and storage"] = DataPage();
        var reset = Ui.Button("Restore preferences");
        reset.Click += (_, _) => RestoreDefaults();
        _pages["Advanced"] = Page("Advanced",
            Section("Restore preferences", "Puts the defaults in place for you to review and save. Hosts and history aren't touched.", reset),
            Section("Testing", "", _versionOverlayBox));
        _pages["About"] = AboutPage();

        _nav.ItemsSource = Pages;
        _nav.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
        {
            var text = Ui.Text(name ?? "");
            text.Margin = new Thickness(Tokens.Space4, 7);
            return text;
        });
        Avalonia.Automation.AutomationProperties.SetName(_nav, "Settings pages");
        _nav.SelectionChanged += (_, _) => { if (_nav.SelectedItem is string name) _page.Content = _pages[name]; };

        var title = Ui.Title("Settings");
        title.Margin = new Thickness(Tokens.Space2, 0, 0, Tokens.Space5);
        var close = Ui.Button("Close");
        close.Click += (_, _) => Close();
        _save.Click += (_, _) => Save();
        var footer = Ui.Row(Tokens.Space3, _saveState, _save, close);
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new Thickness(0, Tokens.Space4, 0, 0);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Tokens.Space6 };
        body.Children.Add(_nav);
        var scroll = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        var layout = new DockPanel { Margin = new Thickness(Tokens.Space6, Tokens.Space6, Tokens.Space6, Tokens.Space5) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(title);
        layout.Children.Add(footer);
        layout.Children.Add(body);
        var root = new Panel();
        root.Children.Add(layout);
        root.Children.Add(_versionOverlay);
        Content = root;

        // Status lines take no space until they have something to say.
        foreach (var status in new[] { _dataStatus, _aboutStatus })
        {
            status.IsVisible = false;
            status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) status.IsVisible = !string.IsNullOrEmpty(status.Text); };
        }
        Load();
        foreach (var check in new[] { _minimizeToTray, _startWithDesktop, _autoStart, _wallboardOnStartup, _restoreLastSite, _use12Hour, _mainLog, _wallboardLog, _notifications, _hideSuspects, _autoUpdates, _versionOverlayBox })
            check.IsCheckedChanged += (_, _) => Changed();
        foreach (var number in new[] { _interval, _timeout, _downAfter, _recoverAfter })
            number.ValueChanged += (_, _) => Changed();
        _appearance.SelectionChanged += (_, _) => Changed();
        _historyRange.SelectionChanged += (_, _) => Changed();
        _engine.Changed += EngineChanged;
        Closed += (_, _) => _engine.Changed -= EngineChanged;

        _nav.SelectedIndex = 0;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; Save(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        Closing += async (_, e) =>
        {
            if (_closeConfirmed || !Dirty) return;
            e.Cancel = true;
            var choice = await Dialogs.SaveDiscard(this, "Save your changes before closing Settings?");
            if (choice is null) return;
            if (choice == true && !Save()) return;
            _closeConfirmed = true;
            Close();
        };
        Opened += (_, _) => { ApplyOverlay(); RefreshUpdates(); _nav.Focus(); };
    }

    internal bool Dirty => _save.IsEnabled;
    internal string SaveStateText => _saveState.Text ?? "";
    internal string? CurrentPage => _nav.SelectedItem as string;
    internal Control PageControl(string name) => _pages[name];
    internal CheckBox MinimizeToTrayBox => _minimizeToTray;
    internal CheckBox Use12HourBox => _use12Hour;
    internal CheckBox VersionOverlayBox => _versionOverlayBox;
    internal ComboBox AppearanceBox => _appearance;
    internal NumericUpDown IntervalBox => _interval;
    internal string UpdateStatusText => _updateStatus.Text ?? "";
    internal bool MonitoringLocked => !_interval.IsEnabled;

    internal void SelectPage(string name) => _nav.SelectedItem = Pages.FirstOrDefault(p => p == name) ?? Pages[0];

    internal void ApplyOverlay() => _versionOverlay.IsVisible = _engine.Preferences.ShowVersionOverlay;

    private void EngineChanged(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(SyncLock);

    private void SyncLock()
    {
        bool locked = _engine.Monitoring;
        foreach (var number in new[] { _interval, _timeout, _downAfter, _recoverAfter }) number.IsEnabled = !locked;
        _monitorLock.IsVisible = locked;
    }

    internal void RefreshUpdates()
    {
        _updateStatus.Text = string.IsNullOrWhiteSpace(_updates.Status)
            ? "Version " + LinuxUpdateService.Version + (LinuxUpdateService.PackageKind == "AppImage" ? "" : " · " + LinuxUpdateService.UnmanagedText)
            : _updates.Status;
        _updateButton.Content = _updates.ActionText;
        _updateButton.IsEnabled = !_updates.Checking;
    }

    private void Load()
    {
        _loading = true;
        try
        {
            _minimizeToTray.IsChecked = _config.MinimizeToTray;
            _startWithDesktop.IsChecked = _startAtLogin;
            _autoStart.IsChecked = _prefs.StartMonitoringOnLaunch;
            _wallboardOnStartup.IsChecked = _prefs.OpenWallboardOnStartup;
            _restoreLastSite.IsChecked = _prefs.RestoreLastSiteOnStartup;
            _appearance.SelectedIndex = (int)_prefs.Appearance;
            _use12Hour.IsChecked = _config.Use12HourTime;
            _mainLog.IsChecked = _config.ShowCommandView;
            _wallboardLog.IsChecked = _config.WallboardShowCli;
            _interval.Value = _config.PingIntervalSeconds;
            _timeout.Value = _config.PingTimeoutMs;
            _downAfter.Value = _config.FailureThreshold;
            _recoverAfter.Value = _config.RecoveryThreshold;
            _notifications.IsChecked = _config.NotificationsEnabled;
            _alarmSound.Text = SoundName(_config.PriorityAlarmSound);
            _historyRange.SelectedIndex = Math.Max(0, Array.IndexOf(RangeHours, _config.EventHistoryHours));
            _hideSuspects.IsChecked = _config.HideSuspectEvents;
            _autoUpdates.IsChecked = _config.AutoCheckUpdates;
            _versionOverlayBox.IsChecked = _prefs.ShowVersionOverlay;
            SyncLock();
        }
        finally { _loading = false; }
        _save.IsEnabled = false;
        _saveState.Text = "Saved";
    }

    private void Changed()
    {
        if (_loading) return;
        _save.IsEnabled = true;
        _saveState.Text = "Unsaved changes";
    }

    private static string SoundName(string path) => string.IsNullOrWhiteSpace(path) ? "Desktop alarm" : Path.GetFileName(path);

    private Control SoundButtons()
    {
        var choose = Ui.Button("Choose sound…");
        var test = Ui.Button("Test");
        var reset = Ui.Button("Use desktop alarm", "ghost");
        choose.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Priority alarm sound",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Sound") { Patterns = new[] { "*.wav", "*.oga", "*.ogg", "*.flac", "*.mp3" } } }
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) SetSound(path);
        };
        test.Click += (_, _) => PriorityAlarm.PlaySound(_config.PriorityAlarmSound);
        reset.Click += (_, _) => SetSound("");
        return Ui.Row(Tokens.Space2, choose, test, reset);
    }

    private void SetSound(string path)
    {
        _config.PriorityAlarmSound = path;
        _alarmSound.Text = SoundName(path);
        Changed();
    }

    private void RestoreDefaults()
    {
        var defaults = new WatchdogConfig();
        _config.MinimizeToTray = defaults.MinimizeToTray;
        _config.Use12HourTime = defaults.Use12HourTime;
        _config.ShowCommandView = defaults.ShowCommandView;
        _config.WallboardShowCli = defaults.WallboardShowCli;
        _config.NotificationsEnabled = defaults.NotificationsEnabled;
        _config.PriorityAlarmSound = defaults.PriorityAlarmSound;
        _config.EventHistoryHours = defaults.EventHistoryHours;
        _config.HideSuspectEvents = defaults.HideSuspectEvents;
        _config.AutoCheckUpdates = defaults.AutoCheckUpdates;
        if (!_engine.Monitoring)
        {
            _config.PingIntervalSeconds = defaults.PingIntervalSeconds;
            _config.PingTimeoutMs = defaults.PingTimeoutMs;
            _config.FailureThreshold = defaults.FailureThreshold;
            _config.RecoveryThreshold = defaults.RecoveryThreshold;
        }
        var prefs = new LinuxPreferences { ListView = _prefs.ListView, WallboardScreen = _prefs.WallboardScreen, HiddenColumns = _prefs.HiddenColumns.ToList() };
        _prefs = prefs;
        _startAtLogin = false;
        Load();
        Changed();
    }

    // Persistence failures are never reported as saved.
    internal bool Save()
    {
        if (!Dirty) return true;
        var config = _config;
        config.MinimizeToTray = _minimizeToTray.IsChecked == true;
        config.Use12HourTime = _use12Hour.IsChecked == true;
        config.ShowCommandView = _mainLog.IsChecked == true;
        config.WallboardShowCli = _wallboardLog.IsChecked == true;
        config.NotificationsEnabled = _notifications.IsChecked == true;
        config.EventHistoryHours = RangeHours[Math.Max(0, _historyRange.SelectedIndex)];
        config.HideSuspectEvents = _hideSuspects.IsChecked == true;
        config.AutoCheckUpdates = _autoUpdates.IsChecked == true;
        var live = _engine.Snapshot().Settings;
        if (_engine.Monitoring)
        {
            config.PingIntervalSeconds = live.PingIntervalSeconds;
            config.PingTimeoutMs = live.PingTimeoutMs;
            config.FailureThreshold = live.FailureThreshold;
            config.RecoveryThreshold = live.RecoveryThreshold;
        }
        else
        {
            config.PingIntervalSeconds = (int)(_interval.Value ?? 2);
            config.PingTimeoutMs = (int)(_timeout.Value ?? 1000);
            config.FailureThreshold = (int)(_downAfter.Value ?? 3);
            config.RecoveryThreshold = (int)(_recoverAfter.Value ?? 2);
        }
        var prefs = _prefs.Copy();
        prefs.StartMonitoringOnLaunch = _autoStart.IsChecked == true;
        prefs.OpenWallboardOnStartup = _wallboardOnStartup.IsChecked == true;
        prefs.RestoreLastSiteOnStartup = _restoreLastSite.IsChecked == true;
        prefs.Appearance = (AppearanceMode)Math.Max(0, _appearance.SelectedIndex);
        prefs.ShowVersionOverlay = _versionOverlayBox.IsChecked == true;
        prefs.ListView = _engine.Preferences.ListView;
        prefs.HiddenColumns = _engine.Preferences.HiddenColumns.ToList();
        prefs.WallboardScreen = _engine.Preferences.WallboardScreen;
        try
        {
            string? error = _engine.ApplySettings(config);
            if (error is not null) { ShowSaveError(error); return false; }
            _engine.SavePreferences(prefs);
            bool wantLogin = _startWithDesktop.IsChecked == true;
            if (wantLogin != Autostart.Enabled && Autostart.Set(wantLogin) is { } loginError) { ShowSaveError(loginError); return false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowSaveError("Couldn't save settings: " + ex.Message);
            return false;
        }
        _config = _engine.Snapshot().Settings;
        _prefs = _engine.Preferences.Copy();
        _startAtLogin = Autostart.Enabled;
        _main.ApplyPreferences();
        _main.RefreshAll();
        Load();
        return true;
    }

    private void ShowSaveError(string message)
    {
        _saveState.Text = "Not saved";
        _ = Dialogs.Message(this, "Settings not saved", message);
    }

    private Control DataPage()
    {
        var open = Ui.Button("Open data folder");
        open.Click += (_, _) => _dataStatus.Text = DesktopLauncher.Open(_engine.ConfigDirectory) ? "" : "Couldn't open the folder. It's at " + _engine.ConfigDirectory;
        var backup = Ui.Button("Create backup…");
        backup.Click += async (_, _) => await BackupAsync();
        var restore = Ui.Button("Restore backup…");
        restore.Click += async (_, _) => await RestoreAsync();
        var delete = Ui.Button("Delete saved data…", "destructive");
        delete.Click += async (_, _) => await DeleteAsync();
        _dataStatus.TextWrapping = TextWrapping.Wrap;
        var paths = Ui.Text(_engine.ConfigDirectory + "\n" + _engine.StateDirectory, Tokens.Label, "Pw.Text2", mono: true);
        paths.TextTrimming = TextTrimming.None;
        return Page("Data and storage",
            Section("Storage", "", paths, open),
            Section("Backups", "Restoring replaces your current data and restarts the app.", Ui.Row(Tokens.Space2, backup, restore), _dataStatus),
            Section("Delete saved data", "Removes data, backups and recovery files, then closes the app. Stop monitoring first.", delete));
    }

    private async Task BackupAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create backup",
            SuggestedFileName = DataStore.DefaultBackupName(),
            DefaultExtension = "zip",
            FileTypeChoices = new[] { new FilePickerFileType("Ping Watchdog backup") { Patterns = new[] { "*.zip" } } }
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            DataStore.CreateBackup(_engine, stream);
            _dataStatus.Text = "Backup saved: " + file.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dataStatus.Text = "Couldn't save the backup: " + ex.Message;
        }
    }

    private async Task RestoreAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restore backup",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Ping Watchdog backup") { Patterns = new[] { "*.zip" } } }
        });
        if (files.Count == 0) return;
        Dictionary<string, byte[]>? data;
        string? error;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;
            (data, error) = DataStore.ReadBackup(memory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (data, error) = (null, "Couldn't read that file: " + ex.Message);
        }
        if (data is null) { _dataStatus.Text = error; return; }
        if (!await Dialogs.Confirm(this, "Restore backup", "Replace your current hosts, settings and history with this backup? A copy of your current data is kept first, then the app restarts.", "Restore", destructive: true)) return;
        _engine.StopMonitoring();
        string? restoreError = DataStore.Restore(_engine, data);
        if (restoreError is not null) { _dataStatus.Text = restoreError; return; }
        _closeConfirmed = true;
        if (!DataStore.Relaunch())
            await Dialogs.Message(this, "Restore backup", "The backup was restored. Open Ping Watchdog again to use it.");
        // Exit without saving over the restored files.
        Environment.Exit(0);
    }

    private async Task DeleteAsync()
    {
        if (_engine.Monitoring) { _dataStatus.Text = "Stop monitoring first."; return; }
        if (!await Dialogs.Confirm(this, "Delete saved data", "Delete your sites, hosts, settings, history and recovery files, then close Ping Watchdog? This can't be undone. Files that aren't Ping Watchdog's stay where they are.", "Delete and close", destructive: true)) return;
        var (removed, kept) = DataStore.DeleteSavedData(_engine);
        if (kept.Count > 0)
            await Dialogs.Message(this, "Delete saved data", $"Removed {removed} files. These weren't Ping Watchdog's, so they were kept:\n" + string.Join("\n", kept.Take(8)));
        _closeConfirmed = true;
        Environment.Exit(0);
    }

    private Control AboutPage()
    {
        var license = Ui.Button("View license");
        license.Click += async (_, _) => await Dialogs.ShowText(this, "License", SupportInfo.ReadLicense(), false);
        var notices = Ui.Button("Open third-party notices");
        notices.Click += (_, _) =>
        {
            string folder = SupportInfo.NoticesFolder;
            _aboutStatus.Text = Directory.Exists(folder) && DesktopLauncher.Open(folder) ? "" : "This copy has no packaged notice folder.";
        };
        var issue = Ui.Button("Report an issue…");
        issue.Click += (_, _) => _aboutStatus.Text = DesktopLauncher.Open(SupportInfo.IssueUrl) ? "Issue page opened in your browser." : "Couldn't open your browser. Visit " + SupportInfo.IssueUrl;
        var diagnostics = Ui.Button("Review diagnostic info…");
        diagnostics.Click += async (_, _) =>
        {
            string? screen = Screens.ScreenFromWindow(this) is { } s ? $"{s.Bounds.Width}×{s.Bounds.Height} at {s.Scaling:0.##}×" : null;
            await Dialogs.ShowText(this, "Diagnostic information", SupportInfo.CreateDiagnostics(_engine, _updates, screen), true);
        };
        _aboutStatus.TextWrapping = TextWrapping.Wrap;
        var version = Ui.Text("Version " + LinuxUpdateService.Version + " for Linux", Tokens.Body, "Pw.Text2");
        return Page("About",
            Ui.Column(Tokens.Space1, Ui.Title("Ping Watchdog", Tokens.Title3), version),
            Ui.Text("Copyright © 2026 Joseph Luker. All rights reserved.", Tokens.Body, "Pw.Text2"),
            Ui.Column(Tokens.Space2, license, notices, issue, diagnostics),
            _aboutStatus);
    }

    private static Control Page(string title, params Control[] content)
    {
        var stack = new StackPanel { Spacing = Tokens.Space5, Margin = new Thickness(0, 0, Tokens.Space4, Tokens.Space4) };
        stack.Children.Add(Ui.Title(title, Tokens.Title2));
        foreach (var control in content)
        {
            if (control is Button) control.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(control);
        }
        return stack;
    }

    private static Control Section(string title, string caption, params Control[] controls)
    {
        var stack = new StackPanel { Spacing = Tokens.Space2 };
        if (title.Length > 0) stack.Children.Add(Ui.Title(title, Tokens.Title3));
        foreach (var control in controls)
        {
            if (control is Button or ComboBox) control.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(control);
        }
        if (caption.Length > 0)
        {
            var text = Ui.Text(caption, Tokens.Body, "Pw.Text2");
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.MaxWidth = 560;
            text.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(text);
        }
        return stack;
    }
}
