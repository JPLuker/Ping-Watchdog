using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Velopack;
using Velopack.Sources;
using PingWatchdog.Shared;

namespace PingWatchdog;

// The main window: sidebar, Map and List workspace, monitoring, updates and app-level actions.
public sealed partial class MainForm : Form
{
    private const string AllSitesLabel = "All Sites";
    private const string UpdateRepoUrl = "https://github.com/JPLuker/Ping-Watchdog";
    private readonly string _settingsPath = Path.Combine(StoragePaths.Current.DataDirectory, "sites.json");
    private readonly string _autoConfigPath = Path.Combine(StoragePaths.Current.DataDirectory, "autosave.pingwatch.json");
    private readonly string _bundledDefaultConfigPath = Path.Combine(
        AppContext.BaseDirectory,
        "default.pingwatch.json");
    private readonly string _eventHistoryPath = Path.Combine(StoragePaths.Current.DataDirectory, "event-history.json");

    private readonly List<SiteDefinition> _sites = new();
    private readonly List<string> _siteFolders = new();
    private readonly List<CommandLogEntry> _commandEntries = new();
    private readonly List<StateEventRecord> _stateEvents = new();
    private readonly ConcurrentDictionary<string, HostMonitor> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _hostTokens = new(StringComparer.OrdinalIgnoreCase);

    private readonly ListBox _siteList = new WellListBox
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Font = new Font("Segoe UI", 10)
    };

    private readonly Button _addSiteButton = new ThemeButton() { Text = "+ Add Site", AutoSize = true };
    // Pinned scope: stays visible above the scrolling site list and is never one of its rows.
    private readonly ThemeButton _allSitesButton = new() { Text = "All sites", AccessibleName = "All sites", AutoSize = false, Ghost = true, Outline = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _organizeButton = new ThemeButton() { Text = "Organize", AutoSize = true };
    private readonly Button _renameSiteButton = new ThemeButton() { Text = "Rename Site", AutoSize = true };
    private readonly Button _deleteSiteButton = new ThemeButton() { Text = "Delete Site", AutoSize = true };
    private readonly Label _autoSaveLabel = new()
    {
        Text = "Auto-save • live apply",
        AutoSize = true,
        Padding = new Padding(0, 7, 0, 0)
    };
    private readonly Button _saveConfigButton = new ThemeButton() { Text = "Save Config", AutoSize = true };
    private readonly Button _loadConfigButton = new ThemeButton() { Text = "Load Config", AutoSize = true };
    private readonly Button _checkUpdateButton = new ThemeButton() { Text = "Updates", AutoSize = false, Width = 120 };
    private readonly Button _wallboardButton = new ThemeButton()
    {
        Text = "Wallboard",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(88, 32)
    };
    private readonly Button _settingsButton = new ThemeButton() { Text = "Settings", AutoSize = false, Width = 88 };
    private readonly Button _moreButton = new ThemeButton()
    {
        Text = "⋯",
        AutoSize = false,
        Width = 48,
        Height = 34,
        Padding = Padding.Empty,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Semibold", 15),
        AccessibleName = "More options"
    };
    private readonly ContextMenuStrip _appMenu = new();
    private readonly ContextMenuStrip _gridMenu = new();
    private readonly Label _versionLabel = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 8.5f),
        Tag = "muted"
    };
    private readonly Label _siteHeaderLabel = new()
    {
        UseMnemonic = false,
        Text = "HOSTS",
        AutoSize = true,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Padding = new Padding(0, 7, 12, 0)
    };

    private readonly TextBox _ipBox = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = AppTheme.MonoFont,
        PlaceholderText = "One IP or hostname per line\r\n192.168.1.1\r\n8.8.8.8\r\nserver01"
    };

    private readonly NumericUpDown _intervalSeconds = new()
    {
        Minimum = 1,
        Maximum = 300,
        Value = 2,
        Width = 70
    };

    private readonly NumericUpDown _failureThreshold = new()
    {
        Minimum = 2,
        Maximum = 20,
        Value = 3,
        Width = 70
    };

    private readonly NumericUpDown _recoveryThreshold = new()
    {
        Minimum = 1,
        Maximum = 20,
        Value = 2,
        Width = 70
    };

    private readonly NumericUpDown _timeoutMs = new()
    {
        Minimum = 250,
        Maximum = 10000,
        Increment = 250,
        Value = 1000,
        Width = 90
    };

    private readonly Button _startButton = new ThemeButton() { Text = "Start Monitoring", AutoSize = true };
    private readonly Button _stopButton = new ThemeButton() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly CheckBox _showCommandView = new()
    {
        Text = "Show ping log",
        AutoSize = true,
        Padding = new Padding(8, 5, 0, 0)
    };
    private readonly Button _clearLogButton = new ThemeButton() { Text = "Clear", AutoSize = true };
    private readonly Label _totalValueLabel = CreateStatValueLabel();
    private readonly Label _onlineValueLabel = CreateStatValueLabel();
    private readonly Label _suspectValueLabel = CreateStatValueLabel();
    private readonly Label _offlineValueLabel = CreateStatValueLabel();
    private readonly Label _monitorStateLabel = new()
    {
        Text = "IDLE",
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 9),
        Padding = new Padding(10, 6, 10, 6),
        Tag = "stateBadge"
    };

    private readonly DataGridView _grid = new HostResultsGrid()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BorderStyle = BorderStyle.None,
        MultiSelect = false
    };

    private readonly RichTextBox _commandBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        Font = AppTheme.MonoFont,
        DetectUrls = false,
        HideSelection = false
    };

    private readonly SplitContainer _workspaceSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        FixedPanel = FixedPanel.Panel1,
        SplitterWidth = 6,
        SplitterDistance = 220
    };

    private readonly SplitContainer _mainSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        Panel2Collapsed = true,
        SplitterWidth = 6
    };

    private readonly StatusStrip _statusStrip = new() { SizingGrip = false };
    private readonly ToolStripStatusLabel _statusLabel = new("Ready");
    private readonly ToolStripStatusLabel _ownershipLabel = new("© 2026 Joseph Luker • All rights reserved.")
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleRight
    };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 500 };
    private ResponsivenessGuard? _responsiveness;
    // Hourly look at whether a day has passed; the check itself runs at most once a day.
    private readonly System.Windows.Forms.Timer _updateTimer = new()
    {
        Interval = 60 * 60 * 1000
    };
    private readonly bool _updatesManaged = UpdatePolicy.DisabledByPolicy;
    private bool _updatesManagedOverride;
    private bool UpdatesManaged => _updatesManaged || _updatesManagedOverride;
    internal void ManageUpdatesForTest(bool managed) => _updatesManagedOverride = managed;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayStopItem;
    private readonly ToolStripMenuItem _trayUpdateItem;
    private readonly Icon _appIcon;
    private readonly Bitmap _brandBitmap;
    private readonly Font _siteItemFont = new("Segoe UI", 10.5f);
    private readonly Font _statusCellFont = new("Segoe UI", 10.5f);

    private TableLayoutPanel? _rootLayout;
    private TableLayoutPanel? _rightLayout;
    private FlowLayoutPanel? _headerActionsPanel;
    private FlowLayoutPanel? _settingsFlowPanel;

    private CancellationTokenSource? _cts;
    private WallboardForm? _wallboardForm;
    private EventHistoryForm? _eventHistoryForm;
    private SettingsForm? _settingsForm;
    private OrganizationForm? _organizationForm;
    private bool _appNotificationsAvailable;
    private readonly bool _suppressNotifications;
    private readonly bool _persistSites;
    private bool _ignoreSiteSelection;
    private bool _updateCheckInProgress;
    private bool _closingApplication;
    private UpdateManager? _pendingUpdateManager;
    private UpdateInfo? _pendingUpdateInfo;
    private string? _pendingUpdateVersion;
    private string? _selectedSiteName;

    private int _monitorIntervalSeconds = 2;
    private int _pingTimeoutMs = 1000;
    private int _failureThresholdValue = 3;
    private int _recoveryThresholdValue = 2;
    private int _eventHistoryHours = 24;
    private bool _hideSuspectEvents = true;
    private bool _autoCheckUpdates = true;
    private bool _minimizeToTray = true;
    private bool _notificationsEnabled = true;
    private string _priorityAlarmSound = "";
    private bool _wallboardShowCli = true;
    private bool _showUpdateControlOnHome;

    private static Label CreateStatValueLabel()
    {
        return new Label
        {
            Text = "0",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22),
            Tag = "statValue"
        };
    }

    public MainForm(
        bool appNotificationsAvailable,
        bool suppressNotifications = false,
        bool persistSites = true)
    {
        _appNotificationsAvailable = appNotificationsAvailable;
        _suppressNotifications = suppressNotifications;
        _persistSites = persistSites;
        if (_persistSites) DataDeletionStore.FinishPending(StoragePaths.Current);
        if (_persistSites && DataBackupStore.RecoverPending(DataDirectory))
            ReportStorageProblem("An interrupted restore was rolled back. The previous saved files were recovered.");

        Text = "Ping Watchdog";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Font = AppTheme.BodyFont;
        KeyPreview = true;

        var workingArea = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1366, 768);

        int initialWidth = Math.Min(1320, Math.Max(860, workingArea.Width - 32));
        int initialHeight = Math.Min(840, Math.Max(560, workingArea.Height - 32));

        Size = new Size(initialWidth, initialHeight);
        MinimumSize = new Size(
            Math.Min(900, initialWidth),
            Math.Min(560, initialHeight));

        _appIcon = BrandAssets.LoadIcon();
        _brandBitmap = BrandAssets.LoadLogo();
        Icon = _appIcon;

        BuildGrid();
        BuildLayout();
        InitializePriorityAlarm();
        BuildHostListEditing();
        BuildGridContextMenu();
        _statusStrip.Items.Add(_statusLabel);
        // Build and ownership details live in About.
        _versionLabel.Text = GetDisplayVersion();
        _ownershipLabel.Text = $"{GetDisplayVersion()} • © 2026 Joseph Luker • All rights reserved.";

        BuildAppMenu();

        var trayMenu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(230, 237, 243)
        };
        var openItem = new ToolStripMenuItem("Open Ping Watchdog");
        var wallboardItem = new ToolStripMenuItem("Open Wallboard");
        _trayUpdateItem = new ToolStripMenuItem("Restart to Update")
        {
            Visible = false,
            Enabled = false
        };
        _trayStopItem = new ToolStripMenuItem("Stop Monitoring") { Enabled = false };
        var exitItem = new ToolStripMenuItem("Exit");

        openItem.Click += (_, _) => RestoreFromTray();
        wallboardItem.Click += (_, _) => OpenWallboard();
        _trayUpdateItem.Click += (_, _) => RestartToApplyPendingUpdate();
        _trayStopItem.Click += (_, _) => StopMonitoring();
        exitItem.Click += (_, _) => Close();

        trayMenu.Items.Add(openItem);
        trayMenu.Items.Add(wallboardItem);
        trayMenu.Items.Add(_trayUpdateItem);
        trayMenu.Items.Add(_trayStopItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Ping Watchdog",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _siteList.DrawMode = DrawMode.OwnerDrawFixed;
        _siteList.ItemHeight = 36;
        _siteList.DrawItem += DrawSiteItem;
        _siteList.SelectedIndexChanged += (_, _) => OnSiteSelectionChanged();
        _siteList.DoubleClick += (_, _) => RenameSite();
        _addSiteButton.Click += (_, _) => AddSite();
        _organizeButton.Click += (_, _) => OpenOrganization();
        _renameSiteButton.Click += (_, _) => RenameSite();
        _deleteSiteButton.Click += (_, _) => DeleteSite();
        _saveConfigButton.Click += (_, _) => SaveConfigFile();
        _loadConfigButton.Click += (_, _) => LoadConfigFile();
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync(userInitiated: true);
        _wallboardButton.Click += (_, _) => OpenWallboard();
        _settingsButton.Click += (_, _) => OpenSettings();
        _moreButton.Click += (_, _) => ShowAppMenu();
        _grid.CellMouseDown += GridCellMouseDown;

        _ipBox.Leave += (_, _) =>
        {
            if (_selectedSiteName is not null)
            {
                PersistCurrentEditor();
                SaveSites();
                ReconcileMonitoringWithConfig();
                RefreshSiteList(_selectedSiteName);
                RefreshGrid();
            }
        };

        _startButton.Click += (_, _) => { if (_cts is null) StartMonitoring(); else StopMonitoring(); };
        _stopButton.Click += (_, _) => StopMonitoring();
        _showCommandView.CheckedChanged += (_, _) => ToggleCommandView();
        _clearLogButton.Click += (_, _) => ClearCommandLog();
        _uiTimer.Tick += (_, _) => RefreshGrid();
        _updateTimer.Tick += async (_, _) =>
        {
            if (_autoCheckUpdates && UpdatePolicy.IsDue(_lastUpdateCheckUtc, DateTimeOffset.UtcNow))
                await CheckForUpdatesAsync(userInitiated: false);
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11)
            {
                e.Handled = true;
                OpenWallboard();
            }
            else if (e.Control && e.KeyCode == Keys.H)
            {
                e.Handled = true;
                OpenEventHistory();
            }
            else if (e.Control && e.KeyCode == Keys.Oemcomma)
            {
                e.Handled = true;
                OpenSettings();
            }
        };

        Shown += async (_, _) =>
        {
            ApplyResponsiveLayout();

            var screen = Screen.FromControl(this).WorkingArea;
            if (screen.Width < 1220 || screen.Height < 700)
                WindowState = FormWindowState.Maximized;

            ApplyResponsiveLayout();

            ShowStorageWarning();
            if (_startMonitoringOnLaunch && !_configWriteBlocked) StartMonitoring();
            if (_persistSites)
            {
                StartupRegistration.RefreshPath();
                _responsiveness ??= new ResponsivenessGuard(this, StoragePaths.Current.LogDirectory);
            }
            CheckAppliedUpdate();
            if (_openWallboardOnStartup && _persistSites) OpenWallboard();

            if (_autoCheckUpdates && !UpdatesManaged)
            {
                _updateTimer.Start();
                if (UpdatePolicy.IsDue(_lastUpdateCheckUtc, DateTimeOffset.UtcNow))
                    await CheckForUpdatesAsync(userInitiated: false);
            }
        };

        Resize += (_, _) =>
        {
            ApplyResponsiveLayout();

            if (WindowState == FormWindowState.Minimized && _minimizeToTray)
            {
                Hide();
                _trayIcon.ShowBalloonTip(
                    2500,
                    "Ping Watchdog",
                    _cts is null
                        ? "Ping Watchdog is minimized to the notification area."
                        : "Monitoring continues in the notification area.",
                    ToolTipIcon.Info);
            }
        };

        DpiChanged += (_, _) => BeginInvoke(ApplyResponsiveLayout);

        FormClosing += (_, e) =>
        {
            // WinForms propagates an owned window's canceled close before this event.
            if (e.Cancel) return;
            var settings = _settingsForm;
            if (settings is { IsDisposed: false })
            {
                settings.Close();
                if (!settings.IsDisposed) { e.Cancel = true; return; }
            }
            _closingApplication = true;
            _hostQuickMenu.Close();
            _wallboardForm?.Close();
            _eventHistoryForm?.Close();
            _organizationForm?.Close();
            PersistCurrentEditor();
            SaveSites();
            StopMonitoring();
        };

        FormClosed += (_, _) =>
        {
            _responsiveness?.Dispose();
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _siteActionsMenu.Dispose();
            _columnsMenu.Dispose();
            _siteItemFont.Dispose();
            _siteListTip.Dispose();
            _addSiteTip.Dispose();
            _statusCellFont.Dispose();
            _appIcon.Dispose();
            _brandBitmap.Dispose();
        };

        Disposed += (_, _) => _hostQuickMenu.Dispose();

        LoadSites();
        LoadUserPreferences();
        LoadEventHistory();

        if (_sites.Count == 0)
            _sites.Add(new SiteDefinition { Name = "Default Site" });

        ApplyStartupView();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        ApplyDarkTheme();
        AppTheme.Changed += RefreshAppearance;
        Disposed += (_, _) => AppTheme.Changed -= RefreshAppearance;
        UpdateActionState();
        RefreshGrid();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            int enabled = AppTheme.Palette.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows builds can ignore dark title-bar support.
        }
    }

    public void RestoreFromTray()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RestoreFromTray);
            return;
        }

        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;

        if (version is null)
            return "Ping Watchdog";

        return $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private void BuildAppMenu()
    {
        _appMenu.Items.Clear();

        var settingsItem = new ToolStripMenuItem("Settings...");
        var historyItem = new ToolStripMenuItem("Outage history...");
        var exportItem = new ToolStripMenuItem("Export configuration");
        var importItem = new ToolStripMenuItem("Import configuration");
        var aboutItem = new ToolStripMenuItem("About Ping Watchdog");

        settingsItem.ShortcutKeys = Keys.Control | Keys.Oemcomma;
        settingsItem.Click += (_, _) => OpenSettings();
        historyItem.ShortcutKeys = Keys.Control | Keys.H;
        historyItem.Click += (_, _) => OpenEventHistory();
        exportItem.Click += (_, _) => SaveConfigFile();
        importItem.Click += (_, _) => LoadConfigFile();
        aboutItem.Click += (_, _) => ShowAboutDialog();

        var hostsItem = new ToolStripMenuItem("Host list") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.H };
        hostsItem.Click += (_, _) => ShowHostList();
        _appMenu.Items.Add("Export monitoring batch…", null, (_, _) => ExportMonitoringBatch());
        _appMenu.Items.Add(hostsItem);
        _appMenu.Items.Add(settingsItem);
        _appMenu.Items.Add(historyItem);
        _appMenu.Items.Add("Manage sites…", null, (_, _) => ShowSiteActions());
        _appMenu.Items.Add(new ToolStripSeparator());
        _appMenu.Items.Add(exportItem);
        _appMenu.Items.Add(importItem);
        _appMenu.Items.Add(new ToolStripSeparator());
        _appMenu.Items.Add(aboutItem);
    }

    private void ShowAppMenu()
    {
        if (_moreButton.IsDisposed)
            return;

        _appMenu.Show(
            _moreButton,
            new Point(
                Math.Max(0, _moreButton.Width - _appMenu.PreferredSize.Width),
                _moreButton.Height + 4));
    }

    private void ShowAboutDialog()
    {
        OpenSettings();
        _settingsForm?.SelectCategory("About");
    }

    private void SaveSites() => TrySaveSites();

    private string GetUpdateStatusText() => _updateStatusText;

    private Form UpdateDialogOwner =>
        _settingsForm is { Visible: true, IsDisposed: false } ? _settingsForm :
        _wallboardForm is { Visible: true, IsDisposed: false } ? _wallboardForm : this;

    private void RefreshAppearance()
    {
        if (IsDisposed) return;
        AppTheme.ApplyMenu(_appMenu);
        AppTheme.ApplyMenu(_hostQuickMenu);
        RefreshGrid();
        RebuildCommandView();
        _siteList.Invalidate();
    }

    private void ApplyDarkTheme()
    {
        AppTheme.Initialize(this);
        AppTheme.Apply(this);
        ((ThemeButton)_startButton).Primary = true;
        ((ThemeButton)_stopButton).Primary = true;
        AppTheme.ApplyMenu(_appMenu);
        AppTheme.ApplyMenu(_hostQuickMenu);
    }

    private void ShowNotification(
        string title,
        string body,
        ToolTipIcon fallbackIcon)
    {
        if (_appNotificationsAvailable)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddArgument("action", "open")
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();

                AppNotificationManager.Default.Show(notification);
                return;
            }
            catch
            {
                _appNotificationsAvailable = false;
            }
        }

        _trayIcon.ShowBalloonTip(
            5000,
            title,
            body,
            fallbackIcon);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";

        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";

        return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
    }

}
