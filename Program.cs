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

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Keep the entry point free of dashboard and notification initialization so
        // a fresh recovery process can start even when those components fail.
        if (StartupRecovery.HandleMode(args))
            return;

        if (args.Any(arg => arg is "--veloapp-install" or "--veloapp-obsolete" or "--veloapp-updated" or "--veloapp-uninstall"))
        {
            InitializeUpdater(args);
            return;
        }

        if (args.Contains("--self-test") || args.Contains("--ui-smoke-test"))
        {
            RunTests(args);
            return;
        }

        StartupRecovery.RunGuarded(() => RunApplication(args));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void InitializeUpdater(string[] args)
    {
        // Applying a staged package is an explicit action, including in recovery.
        VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false).Run();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunTests(string[] args)
    {
        InitializeUpdater(args);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--ui-smoke-test"))
                MainForm.RunUiSmokeTest();
            else
                MainForm.RunSelfTests();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunApplication(string[] args)
    {
        InitializeUpdater(args);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        var notificationsRegistered = false;

        try
        {
            BrandAssets.EnsureShellAssets();
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register("Ping Watchdog", new Uri(BrandAssets.NotificationIconPath));
            notificationsRegistered = true;
        }
        catch
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        }

        try
        {
            WindowsBranding.Refresh();
            using var form = new MainForm(notificationsRegistered);
            using var readyTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            form.Shown += (_, _) => readyTimer.Start();
            readyTimer.Tick += (_, _) =>
            {
                readyTimer.Stop();
                StartupRecovery.MarkReady();
            };
            Application.Run(form);
        }
        finally
        {
            if (notificationsRegistered)
            {
                try
                {
                    AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                    AppNotificationManager.Default.Unregister();
                }
                catch { /* Notification cleanup must not hide the original failure. */ }
            }
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        var form = Application.OpenForms.Count > 0
            ? Application.OpenForms[0] as MainForm
            : null;

        form?.BeginInvoke(form.RestoreFromTray);
    }
}

internal enum HostState
{
    Unknown,
    Online,
    Suspect,
    Offline
}

internal sealed class SiteDefinition
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public List<string> Hosts { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, string> Categories { get; set; } = new();
    public Dictionary<string, HostOptions> HostDetails { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Temporary { get; set; }
}

internal sealed class WatchdogConfig
{
    public int FormatVersion { get; set; } = 1;
    public List<SiteDefinition> Sites { get; set; } = new();
    public List<string> SiteFolders { get; set; } = new();
    public int PingIntervalSeconds { get; set; } = 2;
    public int PingTimeoutMs { get; set; } = 1000;
    public int FailureThreshold { get; set; } = 3;
    public int RecoveryThreshold { get; set; } = 2;
    public bool ShowCommandView { get; set; }
    public string? SelectedSite { get; set; }
    public int EventHistoryHours { get; set; } = 24;
    public bool HideSuspectEvents { get; set; } = true;
    public bool Use12HourTime { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool WallboardShowCli { get; set; } = true;
    public bool ShowUpdateControlOnHome { get; set; }
}

internal sealed class NicknameDialog : Form
{
    private readonly TextBox _box = new() { Dock = DockStyle.Fill };

    public string Nickname => _box.Text.Trim();

    public NicknameDialog(string host, string current)
    {
        Text = $"Set label • {host}";
        Width = 430;
        Height = 150;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(13, 17, 23);
        ForeColor = Color.FromArgb(230, 237, 243);

        _box.Text = current;
        _box.BackColor = Color.FromArgb(22, 27, 34);
        _box.ForeColor = Color.FromArgb(230, 237, 243);
        _box.BorderStyle = BorderStyle.FixedSingle;
        _box.SelectAll();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var ok = new Button
        {
            Text = "Save Label",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 134, 96),
            ForeColor = Color.White
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.White
        };

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Nickname / label:",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 148, 158)
        }, 0, 0);
        layout.Controls.Add(_box, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

internal sealed class SiteNameDialog : Form
{
    private readonly TextBox _box = new() { Dock = DockStyle.Fill };
    public string SiteName => _box.Text.Trim();

    public SiteNameDialog(string title, string actionText, string current = "")
    {
        Text = title;
        Width = 430;
        Height = 150;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(13, 17, 23);
        ForeColor = Color.FromArgb(230, 237, 243);

        _box.Text = current;
        _box.BackColor = Color.FromArgb(22, 27, 34);
        _box.ForeColor = Color.FromArgb(230, 237, 243);
        _box.BorderStyle = BorderStyle.FixedSingle;
        _box.SelectAll();

        var ok = new Button
        {
            Text = actionText,
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 134, 96),
            ForeColor = Color.White
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.White
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Site / group name:",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 148, 158)
        }, 0, 0);
        layout.Controls.Add(_box, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }
}

internal sealed class HostMonitor
{
    public string Site { get; }
    public string Address { get; }
    public string Label { get; set; }
    public HostState State { get; set; } = HostState.Unknown;
    public HostOptions Options { get; set; } = new();
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public long? LastRoundTripMs { get; set; }
    public DateTime? LastReply { get; set; }
    public DateTime? OutageStarted { get; set; }
    public bool AlertedForCurrentOutage { get; set; }

    public HostMonitor(string site, string address, string label = "")
    {
        Site = site;
        Address = address;
        Label = label;
    }
}

internal sealed record CommandLogEntry(
    string Site,
    string Host,
    DateTime Timestamp,
    bool Success,
    string ResultText,
    int TimeoutMs);

internal sealed record StateEventRecord(
    DateTime Timestamp,
    string Site,
    string Host,
    string DisplayHost,
    string Kind,
    string Message);

internal sealed record EventHistoryPreferences(
    int WindowHours,
    bool HideSuspects);

internal sealed record SiteListItem(
    string? SiteName,
    string DisplayText)
{
    public override string ToString() => DisplayText;
}

internal sealed record OrganizationSiteSnapshot(
    string Name,
    string FolderPath,
    int HostCount);

internal sealed record OrganizationSnapshot(
    IReadOnlyList<string> Folders,
    IReadOnlyList<OrganizationSiteSnapshot> Sites);

internal sealed record OrganizationActions(
    Func<string, string, string?> AddFolder,
    Func<string, string, string?> RenameFolder,
    Func<string, string?> DeleteFolder,
    Func<string, string, string?> MoveFolder,
    Func<string, string, string?> AddSite,
    Func<string, string, string?> RenameSite,
    Func<string, string?> DeleteSite,
    Func<string, string, string?> MoveSite,
    Action<string> SelectSite);

internal sealed record AppSettingsSnapshot(
    int PingIntervalSeconds,
    int PingTimeoutMs,
    int FailureThreshold,
    int RecoveryThreshold,
    bool ShowCommandView,
    bool WallboardShowCli,
    bool MinimizeToTray,
    bool NotificationsEnabled,
    bool AutoCheckUpdates,
    bool ShowUpdateControlOnHome,
    int EventHistoryHours,
    bool HideSuspectEvents,
    bool Use12HourTime,
    bool MonitoringActive,
    string Version,
    string UpdateStatus,
    string UpdateActionText);

internal sealed record WallboardControlHost(
    string Site,
    string Address,
    string Label,
    HostState State,
    long? LatencyMs,
    int Failures,
    DateTime? LastReply,
    DateTime? OutageStarted);

internal sealed record WallboardControlSnapshot(
    bool Monitoring,
    string? SelectedSite,
    IReadOnlyList<string> Sites,
    IReadOnlyList<WallboardControlHost> Hosts,
    int PingIntervalSeconds,
    int PingTimeoutMs,
    int FailureThreshold,
    int RecoveryThreshold,
    bool ShowUpdateControl,
    string Version,
    string UpdateStatus,
    string UpdateActionText);

internal sealed record WallboardActions(
    Action StartMonitoring,
    Action StopMonitoring,
    Action<string?> SelectSite,
    Func<string, string?> AddSite,
    Func<string, string, string?> RenameSite,
    Func<string, string?> DeleteSite,
    Func<string, string, string?> SaveHosts,
    Func<string, string, string, string?> SetLabel,
    Func<int, int, int, int, string?> ApplyMonitoringSettings,
    Action OpenSettings,
    Action OpenHistory,
    Action OpenOrganization,
    Func<Task> RunUpdateAction,
    Action SaveConfig,
    Action LoadConfig,
    Action ClearCommandLog);

internal sealed record WallboardHostSnapshot(
    string Site,
    string Address,
    string Label,
    HostState State,
    long? LatencyMs,
    DateTime? OutageStarted);

internal sealed record WallboardSiteSnapshot(
    string Name,
    IReadOnlyList<WallboardHostSnapshot> Hosts);

internal sealed record WallboardEventSnapshot(
    DateTime Timestamp,
    string Site,
    string Host,
    string Kind,
    string Message);

internal sealed record WallboardCommandSnapshot(
    DateTime Timestamp,
    bool Success,
    string Text);

internal sealed record WallboardSnapshot(
    bool Monitoring,
    DateTime CapturedAt,
    IReadOnlyList<WallboardSiteSnapshot> Sites,
    IReadOnlyList<WallboardEventSnapshot> Events,
    IReadOnlyList<WallboardCommandSnapshot> Commands,
    string EventWindowLabel,
    bool HideSuspectEvents);

public sealed partial class MainForm : Form
{
    private const string AllSitesLabel = "All Sites";
    private const string UpdateRepoUrl = "https://github.com/JPLuker/Ping-Watchdog";
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PingWatchdog",
        "sites.json");
    private readonly string _autoConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PingWatchdog",
        "autosave.pingwatch.json");
    private readonly string _bundledDefaultConfigPath = Path.Combine(
        AppContext.BaseDirectory,
        "default.pingwatch.json");
    private readonly string _eventHistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PingWatchdog",
        "event-history.json");

    private readonly List<SiteDefinition> _sites = new();
    private readonly List<string> _siteFolders = new();
    private readonly List<CommandLogEntry> _commandEntries = new();
    private readonly List<StateEventRecord> _stateEvents = new();
    private readonly ConcurrentDictionary<string, HostMonitor> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _hostTokens = new(StringComparer.OrdinalIgnoreCase);

    private readonly ListBox _siteList = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Font = new Font("Segoe UI", 10)
    };

    private readonly Button _addSiteButton = new() { Text = "+ Add Site", AutoSize = true };
    private readonly Button _organizeButton = new() { Text = "Organize", AutoSize = true };
    private readonly Button _renameSiteButton = new() { Text = "Rename Site", AutoSize = true };
    private readonly Button _deleteSiteButton = new() { Text = "Delete Site", AutoSize = true };
    private readonly Label _autoSaveLabel = new()
    {
        Text = "Auto-save • live apply",
        AutoSize = true,
        Padding = new Padding(0, 7, 0, 0)
    };
    private readonly Button _saveConfigButton = new() { Text = "Save Config", AutoSize = true };
    private readonly Button _loadConfigButton = new() { Text = "Load Config", AutoSize = true };
    private readonly Button _checkUpdateButton = new() { Text = "Updates", AutoSize = false, Width = 120 };
    private readonly Button _wallboardButton = new()
    {
        Text = "Wallboard",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(88, 32)
    };
    private readonly Button _settingsButton = new() { Text = "Settings", AutoSize = false, Width = 88 };
    private readonly Button _moreButton = new()
    {
        Text = "⋯",
        AutoSize = false,
        Width = 48,
        Height = 34,
        Padding = Padding.Empty,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold),
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
        Font = new Font("Cascadia Mono", 9.5f),
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

    private readonly Button _startButton = new() { Text = "Start Monitoring", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly CheckBox _showCommandView = new()
    {
        Text = "Show CMD view",
        AutoSize = true,
        Padding = new Padding(8, 5, 0, 0)
    };
    private readonly Button _clearLogButton = new() { Text = "Clear", AutoSize = true };
    private readonly Label _totalValueLabel = CreateStatValueLabel();
    private readonly Label _onlineValueLabel = CreateStatValueLabel();
    private readonly Label _suspectValueLabel = CreateStatValueLabel();
    private readonly Label _offlineValueLabel = CreateStatValueLabel();
    private readonly Label _monitorStateLabel = new()
    {
        Text = "IDLE",
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold),
        Padding = new Padding(10, 6, 10, 6),
        Tag = "stateBadge"
    };

    private readonly DataGridView _grid = new()
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
        Font = new Font("Cascadia Mono", 9.5f),
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
    private readonly System.Windows.Forms.Timer _updateTimer = new()
    {
        Interval = 6 * 60 * 60 * 1000
    };

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayStopItem;
    private readonly ToolStripMenuItem _trayUpdateItem;
    private readonly Icon _appIcon;
    private readonly Bitmap _brandBitmap;
    private readonly Font _siteItemFont = new("Segoe UI Semibold", 9.5f);
    private readonly Font _statusCellFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);

    private TableLayoutPanel? _rootLayout;
    private TableLayoutPanel? _rightLayout;
    private FlowLayoutPanel? _headerActionsPanel;
    private FlowLayoutPanel? _settingsFlowPanel;
    private Label? _brandSubtitleLabel;
    private Label? _brandTitleLabel;

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
    private bool _wallboardShowCli = true;
    private bool _showUpdateControlOnHome;

    private static Label CreateStatValueLabel()
    {
        return new Label
        {
            Text = "0",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold),
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

        Text = "Ping Watchdog";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
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
        BuildGridContextMenu();
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_ownershipLabel);
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
        _checkUpdateButton.Click += async (_, _) => await RunUpdateActionAsync();
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

        _startButton.Click += (_, _) => StartMonitoring();
        _stopButton.Click += (_, _) => StopMonitoring();
        _showCommandView.CheckedChanged += (_, _) => ToggleCommandView();
        _clearLogButton.Click += (_, _) => ClearCommandLog();
        _uiTimer.Tick += (_, _) => RefreshGrid();
        _updateTimer.Tick += async (_, _) =>
        {
            if (_autoCheckUpdates)
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

            if (_autoCheckUpdates)
            {
                _updateTimer.Start();
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

        FormClosing += (_, _) =>
        {
            _closingApplication = true;
            _wallboardForm?.Close();
            _eventHistoryForm?.Close();
            _settingsForm?.Close();
            _organizationForm?.Close();
            _hostManager?.Close();
            PersistCurrentEditor();
            SaveSites();
            StopMonitoring();
        };

        FormClosed += (_, _) =>
        {
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _siteItemFont.Dispose();
            _statusCellFont.Dispose();
            _appIcon.Dispose();
            _brandBitmap.Dispose();
        };

        LoadSites();
        LoadEventHistory();

        if (_sites.Count == 0)
            _sites.Add(new SiteDefinition { Name = "Default Site" });

        _selectedSiteName ??= _sites[0].Name;
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        ApplyDarkTheme();
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
            int enabled = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows builds can ignore dark title-bar support.
        }
    }

    internal static void RunSelfTests()
    {
        using var form = new MainForm(
            appNotificationsAvailable: false,
            suppressNotifications: true,
            persistSites: false);

        DisplayTime.RunTests();
        HostManagerTests.Run();
        RunHostManagementIntegrationTests();
        var host = new HostMonitor("Test Site", "127.0.0.1");

        void Check(bool condition)
        {
            if (!condition)
                throw new InvalidOperationException("Ping Watchdog self-test failed.");
        }

        form.ProcessResult(host, false, null);
        Check(host.State == HostState.Suspect && !host.AlertedForCurrentOutage);

        form.ProcessResult(host, true, 1);
        Check(host.State == HostState.Online && host.ConsecutiveFailures == 0);

        for (int i = 0; i < 3; i++)
            form.ProcessResult(host, false, null);

        Check(host.State == HostState.Offline && host.AlertedForCurrentOutage);

        var outage = host.OutageStarted;
        form.ProcessResult(host, true, 1);
        form.ProcessResult(host, false, null);
        Check(host.State == HostState.Offline && host.OutageStarted == outage);

        form.ProcessResult(host, true, 1);
        form.ProcessResult(host, true, 1);
        Check(host.State == HostState.Online && !host.AlertedForCurrentOutage);

        form._ipBox.Text = "127.0.0.1";
        form.PersistCurrentEditor();
        form.SetNicknameValue("Test Site", "127.0.0.1", "Loopback");
        Check(form.GetNickname("Test Site", "127.0.0.1") == "Loopback");
        form.SetCategoryValue("Test Site", "127.0.0.1", "Infrastructure");
        Check(form.GetCategory("Test Site", "127.0.0.1") == "Infrastructure");

        form.AppendCommandLog("Test Site", "127.0.0.1", true, "Reply from 127.0.0.1: time=1ms TTL=128");
        form.AppendCommandLog("Test Site", "127.0.0.1", false, "FAILED (TimedOut)");
        Check(form._commandEntries.Count == 2);

        var configJson = JsonSerializer.Serialize(form.BuildConfig());
        var configRoundTrip = JsonSerializer.Deserialize<WatchdogConfig>(configJson);
        Check(configRoundTrip?.Sites.Count == 1);
        Check(configRoundTrip?.Sites[0].Labels.Values.Contains("Loopback") == true);
        Check(configRoundTrip?.Sites[0].Categories.Values.Contains("Infrastructure") == true);
        Check(configRoundTrip?.PingIntervalSeconds == 2);
        Check(configRoundTrip?.FailureThreshold == 3);
        Check(configRoundTrip?.EventHistoryHours == 24);
        Check(configRoundTrip?.HideSuspectEvents == true);
        Check(configRoundTrip?.AutoCheckUpdates == true);
        Check(configRoundTrip?.MinimizeToTray == true);
        Check(configRoundTrip?.NotificationsEnabled == true);
        Check(configRoundTrip?.WallboardShowCli == true);
        Check(configRoundTrip?.ShowUpdateControlOnHome == false);
        Check(form.GetConfiguredTargets().Count == 1);
        Check(UpdateRepoUrl.EndsWith("/JPLuker/Ping-Watchdog", StringComparison.Ordinal));
        Check(form._stateEvents.Any(e => e.Kind == "DOWN"));
        Check(form._stateEvents.Any(e => e.Kind == "RECOVERED"));

        var historyNow = DateTime.Now;
        var historySample = new[]
        {
            new StateEventRecord(historyNow.AddHours(-1), "A", "1", "one", "DOWN", "down"),
            new StateEventRecord(historyNow.AddMinutes(-30), "A", "1", "one", "RECOVERED", "up"),
            new StateEventRecord(historyNow.AddMinutes(-10), "A", "2", "two", "SUSPECT", "suspect"),
            new StateEventRecord(historyNow.AddHours(-30), "B", "3", "three", "DOWN", "old")
        };
        var filtered24h = FilterStateEvents(historySample, 24, hideSuspects: true, historyNow);
        Check(filtered24h.Count == 2);
        Check(filtered24h.All(e => e.Kind != "SUSPECT"));
        Check(filtered24h.All(e => e.Timestamp >= historyNow.AddHours(-24)));
        Check(FilterStateEvents(historySample, 0, hideSuspects: false, historyNow).Count == 4);
        Check(GetEventHistoryWindowLabel(168) == "Last 7 days");

        using (var captionBitmap = new Bitmap(800, 300))
        using (var captionGraphics = Graphics.FromImage(captionBitmap))
        using (var labelFont = new Font("Segoe UI", 8))
        using (var addressFont = new Font("Cascadia Mono", 6.8f))
        {
            foreach (string address in new[] { "10.235.80.60", "173.161.54.241", "255.255.255.255", "2001:db8:abcd:1234:5678:90ab:cdef:1234" })
            {
                var size = WallboardCanvas.MeasureHostNodeCaption(captionGraphics, "Printer", address, labelFont, addressFont);
                var required = TextRenderer.MeasureText(captionGraphics, address, addressFont, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Check(size.Width - 10 >= required.Width);
                Check(size.Height >= required.Height + 6);
            }
        }

        var labeledNode = WallboardCanvas.FormatHostNodeLines(
            new WallboardHostSnapshot(
                "Test Site",
                "10.0.0.1",
                "Router",
                HostState.Online,
                1,
                null));
        Check(labeledNode.Primary == "Router");
        Check(labeledNode.Secondary == "10.0.0.1");

        var unlabeledNode = WallboardCanvas.FormatHostNodeLines(
            new WallboardHostSnapshot(
                "Test Site",
                "10.0.0.2",
                "",
                HostState.Online,
                1,
                null));
        Check(unlabeledNode.Primary == "10.0.0.2");
        Check(unlabeledNode.Secondary is null);

        var wallboardSnapshot = form.BuildWallboardSnapshot();
        Check(wallboardSnapshot.Sites.Count == 1);
        Check(wallboardSnapshot.Sites[0].Hosts.Count == 1);
        Check(wallboardSnapshot.Commands.Count == 2);
        Check(wallboardSnapshot.Commands[0].Text.Contains("ping 127.0.0.1", StringComparison.Ordinal));

        var wallboardControls = form.BuildWallboardControlSnapshot();
        Check(wallboardControls.Sites.Count == 1);
        Check(wallboardControls.Hosts.Count == 1);
        Check(wallboardControls.SelectedSite == "Test Site");
        Check(form.WallboardAddSite("Branch") is null);
        Check(form.WallboardSaveHosts("Branch", "10.0.0.1\r\n10.0.0.2") is null);
        Check(form.WallboardRenameSite("Branch", "Branch Renamed") is null);
        Check(form.WallboardSetLabel("Branch Renamed", "10.0.0.1", "Router") is null);
        var editedWallboardControls = form.BuildWallboardControlSnapshot();
        Check(editedWallboardControls.SelectedSite == "Branch Renamed");
        Check(editedWallboardControls.Hosts.Count == 2);
        Check(editedWallboardControls.Hosts.Any(h => h.Label == "Router"));
        Check(form.WallboardDeleteSite("Branch Renamed") is null);
        form.WallboardSelectSite("Test Site");
        Check(form.BuildWallboardControlSnapshot().Sites.Count == 1);

        Check(form.AddOrganizationFolder("", "Indiana") is null);
        Check(form.AddOrganizationFolder("Indiana", "North") is null);
        Check(form.MoveOrganizationSite("Test Site", "Indiana/North") is null);
        Check(form.FindSite("Test Site")?.FolderPath == "Indiana/North");
        Check(form.RenameOrganizationFolder("Indiana/North", "Northwest") is null);
        Check(form.FindSite("Test Site")?.FolderPath == "Indiana/Northwest");
        Check(form.MoveOrganizationSite("Test Site", "") is null);
        Check(form.DeleteOrganizationFolder("Indiana/Northwest") is null);
        Check(NormalizeFolderPath(@" Indiana\\South / Branches ") == "Indiana/South/Branches");

        var majorityOnlineSite = new WallboardSiteSnapshot(
            "Majority Online",
            new[]
            {
                new WallboardHostSnapshot("Majority Online", "1", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Online", "2", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Online", "3", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Online", "4", "", HostState.Offline, null, DateTime.Now),
                new WallboardHostSnapshot("Majority Online", "5", "", HostState.Offline, null, DateTime.Now)
            });
        Check(WallboardCanvas.AggregateSiteState(majorityOnlineSite) == HostState.Suspect);

        var majorityOfflineSite = new WallboardSiteSnapshot(
            "Majority Offline",
            new[]
            {
                new WallboardHostSnapshot("Majority Offline", "1", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Offline", "2", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Offline", "3", "", HostState.Offline, null, DateTime.Now),
                new WallboardHostSnapshot("Majority Offline", "4", "", HostState.Offline, null, DateTime.Now),
                new WallboardHostSnapshot("Majority Offline", "5", "", HostState.Offline, null, DateTime.Now)
            });
        Check(WallboardCanvas.AggregateSiteState(majorityOfflineSite) == HostState.Offline);

        var tiedSite = new WallboardSiteSnapshot(
            "Tie",
            new[]
            {
                new WallboardHostSnapshot("Tie", "1", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Tie", "2", "", HostState.Offline, null, DateTime.Now)
            });
        Check(WallboardCanvas.AggregateSiteState(tiedSite) == HostState.Offline);

        form.ClientSize = new Size(960, 640);
        form.ApplyResponsiveLayout();
        Check(form._workspaceSplit.SplitterDistance <= 205);
        Check(form._rootLayout?.RowStyles[0].Height >= 68);
        Check(form._settingsFlowPanel?.WrapContents == true);

        form.ClientSize = new Size(1320, 840);
        form.ApplyResponsiveLayout();
        Check(form._workspaceSplit.SplitterDistance >= 210);
        Check(form._addSiteButton.Text == "+ Add Site");
        Check(form._renameSiteButton.Text == "Rename Site");
        Check(form._deleteSiteButton.Text == "Delete Site");
        Check(form._addSiteButton.MinimumSize.Height >= 32);
        Check(form._renameSiteButton.MinimumSize.Height >= 32);
        Check(form._deleteSiteButton.MinimumSize.Height >= 32);
        Check(form._checkUpdateButton.Parent is not null);
        Check(form._wallboardButton.Parent is not null);
        Check(form._settingsButton.Parent is not null);
        Check(form._moreButton.Parent is not null);
        Check(form._headerActionsPanel?.Controls.Contains(form._checkUpdateButton) == true);
        Check(form._headerActionsPanel?.Controls.Contains(form._settingsButton) == true);
        Check(form._moreButton.Text == "⋯");
        Check(form._moreButton.Width >= 48);
        Check(form._moreButton.Padding == Padding.Empty);
        Check(form._saveConfigButton.Parent is null);
        Check(form._loadConfigButton.Parent is null);
        Check(form._headerActionsPanel?.WrapContents == false);

        form._closingApplication = false;
        form.WindowState = FormWindowState.Minimized;
        form.Hide();
        form.RestoreAfterWallboard();
        Check(form.Visible);
        Check(form.WindowState != FormWindowState.Minimized);

        var settingsSnapshot = form.GetAppSettingsSnapshot();
        Check(settingsSnapshot.AutoCheckUpdates);
        Check(settingsSnapshot.MinimizeToTray);
        Check(settingsSnapshot.NotificationsEnabled);
        Check(settingsSnapshot.WallboardShowCli);
        Check(!settingsSnapshot.ShowUpdateControlOnHome);
        Check(settingsSnapshot.UpdateActionText == "Check for Updates");

        form.SetUpdateReadyUi("9.9.9");
        Check(form._checkUpdateButton.Text == "Restart to Update");
        Check(form._trayUpdateItem.Enabled);
        Check((form._trayUpdateItem.Text ?? string.Empty).Contains("9.9.9", StringComparison.Ordinal));
        form.ClearPendingUpdateUi();

        using var ping = new Ping();
        Check(ping.Send("127.0.0.1", 1000).Status == IPStatus.Success);

        form._trayIcon.Visible = false;
        form._trayIcon.Dispose();
        form._siteItemFont.Dispose();
        form._statusCellFont.Dispose();
        form._appIcon.Dispose();
        form._brandBitmap.Dispose();
    }

    // Runs the published executable's real WinForms message loop with isolated settings.
    internal static void RunUiSmokeTest()
    {
        string output = Path.Combine(Environment.CurrentDirectory, "ui-smoke-test");
        Directory.CreateDirectory(output);
        using var form = new MainForm(false, suppressNotifications: true, persistSites: false);
        form._autoCheckUpdates = false;
        form._minimizeToTray = false;
        // The hosted runner's 1024px desktop otherwise clamps the requested test widths.
        form.MaximumSize = new Size(1920, 1080);
        Environment.ExitCode = 1;

        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        void Capture(Form window, string name)
        {
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(200);
                form.WindowState = FormWindowState.Normal;
                foreach (int width in new[] { 900, 1060, 1320 })
                {
                    form.ClientSize = new Size(width, 740);
                    form.ApplyResponsiveLayout();
                    await Task.Delay(100);
                    Check(form.Visible && form.IsHandleCreated, "Main window did not appear.");
                    Check(form.ClientSize.Width == width, "Runner clamped the requested window width.");
                    Check(form._versionLabel.Parent is null && form._monitorStateLabel.Parent is null,
                        "Duplicate header chrome returned.");
                    Check(form.Controls.Find("WatchdogBrandLogo", true).Length == 1,
                        "The supplied logo is missing or duplicated.");
                    var title = form._brandTitleLabel!;
                    var actions = form._headerActionsPanel!;
                    Check(form._wallboardButton.Width >= form._wallboardButton.PreferredSize.Width,
                        "Wallboard button text is clipped.");
                    Check(actions.ClientRectangle.Contains(form._wallboardButton.Bounds),
                        "Wallboard button extends outside the header actions.");
                    Check(title.Parent!.Width >= title.Right, "Brand title is clipped.");
                    Check(title.PointToScreen(new Point(title.Width, 0)).X <= actions.PointToScreen(Point.Empty).X,
                        "Brand title overlaps header actions.");
                    var logo = form.Controls.Find("WatchdogBrandLogo", true)[0];
                    Check(logo.Parent!.ClientRectangle.Contains(logo.Bounds), "Brand logo is clipped.");
                    Check(logo.Parent.Parent!.ClientRectangle.Contains(logo.Parent.Bounds),
                        "Brand group extends outside the visible header.");
                    var subtitle = form._brandSubtitleLabel!;
                    int textTop = title.PointToScreen(Point.Empty).Y;
                    int textBottom = subtitle.Visible
                        ? subtitle.PointToScreen(new Point(0, subtitle.Height)).Y
                        : title.PointToScreen(new Point(0, title.Height)).Y;
                    int logoCenter = logo.PointToScreen(new Point(0, logo.Height / 2)).Y;
                    Check(Math.Abs(logoCenter - (textTop + textBottom) / 2) <= 2,
                        "Logo is not vertically centered beside the brand text.");
                    foreach (Control setting in form._settingsFlowPanel!.Controls)
                        Check(setting.Bottom <= form._settingsFlowPanel.ClientSize.Height, "Wrapped monitoring control is clipped.");
                    Capture(form, "main-" + width);
                }

                // Larger UI text must grow the button rather than truncate its final letter.
                var buttonFont = form._wallboardButton.Font;
                foreach (float scale in new[] { 1.25f, 1.5f })
                {
                    using var largerFont = new Font(buttonFont.FontFamily, buttonFont.Size * scale, buttonFont.Style);
                    form._wallboardButton.Font = largerFont;
                    form._headerActionsPanel!.PerformLayout();
                    Check(form._wallboardButton.Width >= form._wallboardButton.PreferredSize.Width,
                        "Wallboard button is clipped with larger UI text.");
                    form._wallboardButton.Font = buttonFont;
                }

                WindowsBranding.RunShortcutSmokeTest(output);

                // Keep a non-first host, its selected column, and both scroll positions through repeated refreshes.
                for (int i = 1; i <= 40; i++)
                {
                    var host = new HostMonitor("Test Site", "192.0.2." + i);
                    form._hosts[BuildHostKey(host.Site, host.Address)] = host;
                }
                form.RefreshGrid();
                var row = form._grid.Rows[25];
                form._grid.CurrentCell = row.Cells["LabelColumn"];
                row.Selected = true;
                form._grid.FirstDisplayedScrollingRowIndex = 20;
                var selected = form.GetSelectedHostIdentity();
                form.SetCategoryValue(selected!.Value.Site, selected.Value.Host, "AP");
                form.RefreshGrid();
                Check(form._grid.CurrentRow?.Cells["CategoryColumn"].Value?.ToString() == "AP",
                    "Category did not render for the selected host.");
                Check(form.GetSelectedHostIdentity() == selected, "Category edit changed the selected host.");
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(100);
                    form.RefreshGrid();
                    Check(form.GetSelectedHostIdentity() == selected, "Refresh lost the selected host.");
                    Check(form._grid.CurrentCell?.OwningColumn.Name == "LabelColumn", "Refresh lost the selected column.");
                    Check(form._grid.FirstDisplayedScrollingRowIndex == 20, "Refresh lost the scroll position.");
                }
                Capture(form, "hosts-selected");
                form._hosts.Clear();
                form.RefreshGrid();
                Check(form.GetSelectedHostIdentity() is null, "Removed host left a stale selection.");

                form._inventoryHeight = 260;
                form.ApplyResponsiveLayout();
                float resizedInventory = form._rightLayout!.RowStyles[1].Height;
                form.ApplyResponsiveLayout();
                Check(resizedInventory >= 200 && form._rightLayout.RowStyles[1].Height == resizedInventory,
                    "Host inventory resize was lost during responsive layout.");
                Capture(form, "resized-host-inventory");
                form.OpenHostManager();
                await Task.Delay(100);
                Check(form._hostManager?.Visible == true, "Host Manager did not open.");
                Capture(form._hostManager!, "hosts");
                form._hostManager!.Close();
                using (var quickAdd = new QuickAddForm(form._sites.Select(s => s.Name), form._selectedSiteName,
                    form.HostSnapshot, form.ImportManagedHosts))
                {
                    quickAdd.PreviewForTest("192.0.2.1,Printer,Printers\n192.0.2.10-12\n192.0.2.999");
                    quickAdd.Show(form);
                    await Task.Delay(100);
                    Check(quickAdd.PreviewRowCount == 5, "Quick Add preview lost input rows.");
                    Capture(quickAdd, "quick-add");
                    quickAdd.Close();
                }
                form.OpenSettings();
                await Task.Delay(100);
                Check(form._settingsForm?.Visible == true, "Settings did not open.");
                Capture(form._settingsForm!, "settings");
                form._settingsForm!.Close();

                form.OpenWallboard();
                await Task.Delay(200);
                Check(form._wallboardForm?.Visible == true, "Wallboard did not open.");
                var canvas = (WallboardCanvas)form._wallboardForm!.Controls.Find("WallboardCanvas", true).Single();
                Check(canvas.ClientRectangle.Contains(canvas.BrandLogoBounds), "Wallboard dog is outside the canvas.");
                using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                using (var expected = new Bitmap(canvas.BrandLogoBounds.Width, canvas.BrandLogoBounds.Height))
                using (var logo = BrandAssets.LoadLogo())
                {
                    canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                    using (var graphics = Graphics.FromImage(expected))
                    {
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        graphics.DrawImage(logo, new Rectangle(Point.Empty, expected.Size));
                    }
                    int opaque = 0, matching = 0;
                    for (int y = 0; y < expected.Height; y++)
                    for (int x = 0; x < expected.Width; x++)
                    {
                        var wanted = expected.GetPixel(x, y);
                        if (wanted.A < 250) continue;
                        opaque++;
                        var actual = rendered.GetPixel(canvas.BrandLogoBounds.X + x, canvas.BrandLogoBounds.Y + y);
                        if (Math.Abs(actual.R - wanted.R) <= 5 && Math.Abs(actual.G - wanted.G) <= 5 &&
                            Math.Abs(actual.B - wanted.B) <= 5) matching++;
                    }
                    Check(opaque > 100 && matching >= opaque * 0.9, "Wallboard did not render the supplied dog artwork.");
                }
                Capture(form._wallboardForm!, "wallboard");
                form._wallboardForm!.Close();
                await Task.Delay(100);
                Check(form.Visible, "Closing Wallboard did not restore the main window.");
                File.WriteAllText(Path.Combine(output, "passed.txt"), "UI smoke test passed.");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "failed.txt"), ex.ToString());
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
            finally
            {
                form.Close();
            }
        };
        Application.Run(form);
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

        var hostsItem = new ToolStripMenuItem("Hosts...") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.H };
        hostsItem.Click += (_, _) => OpenHostManager();
        _appMenu.Items.Add(hostsItem);
        _appMenu.Items.Add(settingsItem);
        _appMenu.Items.Add(historyItem);
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
        MessageBox.Show(
            $"Ping Watchdog {GetDisplayVersion()}\r\n\r\nMulti-site ICMP availability monitoring for Windows.\r\n\r\nUpdates are checked automatically on startup and every six hours.\r\n\r\nCopyright © 2026 Joseph Luker. All rights reserved.",
            "About Ping Watchdog",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void BuildGrid()
    {
        _grid.RowTemplate.Height = Presentation.TableRowHeight;
        _grid.ColumnHeadersHeight = Presentation.TableHeaderHeight;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        foreach (var spec in Presentation.HostColumns)
        {
            var column = new DataGridViewTextBoxColumn {
                Name = spec.Key + "Column", HeaderText = spec.Header,
                DataPropertyName = spec.Key == "Outage" ? "OutageSince" : spec.Key
            };
            if (spec.Weight > 0) {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.FillWeight = (float)spec.Weight; column.MinimumWidth = spec.Width;
            } else column.Width = spec.Width;
            _grid.Columns.Add(column);
        }

        var categoryColumn = new DataGridViewTextBoxColumn
        {
            Name = "CategoryColumn",
            HeaderText = "Category",
            DataPropertyName = "Category",
            Width = 120,
            MinimumWidth = 90
        };
        int labelIndex = _grid.Columns["LabelColumn"]?.Index ?? 1;
        _grid.Columns.Insert(Math.Min(labelIndex + 1, _grid.Columns.Count), categoryColumn);
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Tag = "window"
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Presentation.HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rootLayout = root;

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(20, 10, 16, 8),
            Margin = new Padding(0),
            Tag = "header"
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var brand = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Tag = "header"
        };

        _brandTitleLabel = new Label
        {
            Text = "PING WATCHDOG",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold),
            ForeColor = Color.White,
            Tag = "title"
        };

        _brandSubtitleLabel = new Label
        {
            Text = "Availability monitor",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25f),
            Tag = "muted"
        };

        var logo = new PictureBox
        {
            Name = "WatchdogBrandLogo",
            Image = _brandBitmap,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(12, 18, 26),
            TabStop = false,
            AccessibleName = "Ping Watchdog logo"
        };
        _brandTitleLabel.Margin = Padding.Empty;
        _brandSubtitleLabel.Margin = Padding.Empty;
        brand.Controls.Add(logo);
        brand.Controls.Add(_brandTitleLabel);
        brand.Controls.Add(_brandSubtitleLabel);
        // Place all three controls as one group; nested table rows previously displaced the logo.
        brand.Layout += (_, _) =>
        {
            int logoSize = (int)Math.Round(42 * DeviceDpi / 96d);
            int gap = (int)Math.Round(8 * DeviceDpi / 96d);
            int textHeight = _brandTitleLabel.Height +
                (_brandSubtitleLabel.Visible ? _brandSubtitleLabel.Height : 0);
            int top = Math.Max(0, (brand.ClientSize.Height - textHeight) / 2);
            _brandTitleLabel.Location = new Point(logoSize + gap, top);
            _brandSubtitleLabel.Location = new Point(logoSize + gap, top + _brandTitleLabel.Height);
            logo.Bounds = new Rectangle(0, top + (textHeight - logoSize) / 2, logoSize, logoSize);
        };

        var headerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
            Tag = "header"
        };
        _headerActionsPanel = headerActions;

        _monitorStateLabel.Margin = new Padding(0, 0, 7, 0);
        _checkUpdateButton.Margin = new Padding(0, 0, 7, 0);
        _wallboardButton.Margin = new Padding(0, 0, 7, 0);
        _settingsButton.Margin = new Padding(0, 0, 7, 0);
        _moreButton.Margin = new Padding(0);

        headerActions.Controls.Add(_checkUpdateButton);
        headerActions.Controls.Add(_wallboardButton);
        headerActions.Controls.Add(_settingsButton);
        headerActions.Controls.Add(_moreButton);

        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(headerActions, 1, 0);

        _workspaceSplit.SplitterDistance = Presentation.SidebarWidth;
        _workspaceSplit.SplitterWidth = 1;
        _workspaceSplit.Panel1MinSize = 174;
        _workspaceSplit.Panel1.Padding = new Padding(12, 14, 10, 14);
        _workspaceSplit.Panel2.Padding = new Padding(16, 14, 16, 14);
        _workspaceSplit.Panel1.Tag = "nav";
        _workspaceSplit.Panel2.Tag = "window";

        var sitePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Tag = "nav"
        };
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        sitePanel.Controls.Add(new Label
        {
            Text = "Sites",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            Padding = new Padding(4, 8, 0, 0),
            Tag = "primaryText"
        }, 0, 0);
        sitePanel.Controls.Add(_siteList, 0, 1);
        var hostsNav = HostUi.Button("Hosts", OpenHostManager);
        hostsNav.Dock = DockStyle.Fill;
        sitePanel.Controls.Add(hostsNav, 0, 2);

        _addSiteButton.Text = "+ Add Site";
        _organizeButton.Text = "Organize";
        _renameSiteButton.Text = "Rename Site";
        _deleteSiteButton.Text = "Delete Site";

        foreach (var button in new[] { _addSiteButton, _organizeButton, _renameSiteButton, _deleteSiteButton })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.MinimumSize = new Size(0, 32);
            button.Margin = new Padding(0, 3, 0, 3);
        }

        sitePanel.Controls.Add(_addSiteButton, 0, 3);
        sitePanel.Controls.Add(_organizeButton, 0, 4);
        sitePanel.Controls.Add(_renameSiteButton, 0, 5);
        sitePanel.Controls.Add(_deleteSiteButton, 0, 6);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Tag = "window"
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rightLayout = right;

        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Tag = "window"
        };

        for (int i = 0; i < 4; i++)
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        Panel StatCard(string title, Label value, string tag)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 9, 0),
                Padding = new Padding(14, 8, 14, 7),
                Tag = "card"
            };

            value.Tag = tag;
            value.Font = new Font("Segoe UI Semibold", 21, FontStyle.Bold);
            value.Location = new Point(14, 25);

            card.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(14, 8),
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                Tag = "muted"
            });
            card.Controls.Add(value);
            return card;
        }

        stats.Controls.Add(StatCard("Total hosts", _totalValueLabel, "statTotal"), 0, 0);
        stats.Controls.Add(StatCard("Online", _onlineValueLabel, "statOnline"), 1, 0);
        stats.Controls.Add(StatCard("Suspect", _suspectValueLabel, "statSuspect"), 2, 0);
        var offlineCard = StatCard("Offline", _offlineValueLabel, "statOffline");
        offlineCard.Margin = new Padding(0);
        stats.Controls.Add(offlineCard, 3, 0);

        var inputCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 10, 14, 12),
            Margin = new Padding(0, 0, 0, 10),
            Tag = "card"
        };
        inputCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        inputCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hostHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Tag = "card"
        };
        hostHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hostHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _siteHeaderLabel.Font = new Font("Segoe UI Semibold", 10.25f, FontStyle.Bold);
        _siteHeaderLabel.Padding = new Padding(0, 3, 0, 0);
        _siteHeaderLabel.Tag = "primaryText";

        _autoSaveLabel.Text = "Saved automatically • Applies live";
        _autoSaveLabel.Font = new Font("Segoe UI", 8.5f);
        _autoSaveLabel.Padding = new Padding(0, 4, 0, 0);
        _autoSaveLabel.Tag = "muted";

        hostHeader.Controls.Add(_siteHeaderLabel, 0, 0);
        hostHeader.Controls.Add(BuildHostActions(), 1, 0);
        inputCard.Controls.Add(hostHeader, 0, 0);
        _ipBox.Visible = false;
        Controls.Add(_ipBox);
        inputCard.Controls.Add(BuildHostInventory(), 0, 1);

        var settingsCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 10),
            Tag = "card"
        };
        settingsCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = false,
            Margin = new Padding(0),
            Tag = "card"
        };
        _settingsFlowPanel = settings;

        Control Setting(string title, Control input, string suffix)
        {
            var wrap = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 16, 0),
                Tag = "card"
            };

            wrap.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold),
                Tag = "muted"
            });

            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Tag = "card"
            };

            input.Margin = new Padding(0, 2, 4, 0);
            row.Controls.Add(input);
            row.Controls.Add(new Label
            {
                Text = suffix,
                AutoSize = true,
                Padding = new Padding(0, 7, 0, 0),
                Tag = "muted"
            });
            wrap.Controls.Add(row);

            return wrap;
        }

        settings.Controls.Add(Setting("Interval", _intervalSeconds, "sec"));
        settings.Controls.Add(Setting("Timeout", _timeoutMs, "ms"));
        settings.Controls.Add(Setting("Down after", _failureThreshold, "fails"));
        settings.Controls.Add(Setting("Recover after", _recoveryThreshold, "successes"));
        settings.Controls.Add(_showCommandView);
        settingsCard.Controls.Add(settings, 0, 0);

        var monitorActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(8, 9, 0, 0),
            Tag = "card"
        };

        _startButton.Text = "Start Monitoring";
        _startButton.MinimumSize = new Size(132, 34);
        _stopButton.MinimumSize = new Size(76, 34);
        monitorActions.Controls.Add(_startButton);
        monitorActions.Controls.Add(_stopButton);
        settingsCard.Controls.Add(monitorActions, 1, 0);

        _mainSplit.Panel1.Padding = new Padding(0);
        _mainSplit.Panel1.Controls.Add(_grid);

        var commandPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 8, 0, 0),
            Tag = "window"
        };
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var commandHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Tag = "window"
        };
        commandHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commandHeader.Controls.Add(new Label
        {
            Text = "CLI trace",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold),
            Padding = new Padding(2, 6, 0, 0),
            Tag = "primaryText"
        }, 0, 0);

        _clearLogButton.Margin = new Padding(0, 0, 0, 3);
        commandHeader.Controls.Add(_clearLogButton, 1, 0);
        commandPanel.Controls.Add(commandHeader, 0, 0);
        commandPanel.Controls.Add(_commandBox, 0, 1);
        _mainSplit.Panel2.Controls.Add(commandPanel);

        right.Controls.Add(stats, 0, 0);
        right.Controls.Add(BuildResizableInventory(inputCard), 0, 1);
        right.Controls.Add(settingsCard, 0, 2);
        right.Controls.Add(_mainSplit, 0, 3);

        _workspaceSplit.Panel1.Controls.Add(sitePanel);
        _workspaceSplit.Panel2.Controls.Add(right);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_workspaceSplit, 0, 1);
        root.Controls.Add(_statusStrip, 0, 2);

        Controls.Add(root);
        ResumeLayout(true);
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_rootLayout is null ||
            _rightLayout is null ||
            _headerActionsPanel is null ||
            _settingsFlowPanel is null ||
            IsDisposed)
        {
            return;
        }

        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);
        bool narrow = width < 1080;
        bool veryNarrow = width < 960;
        bool shortWindow = height < 720;

        SuspendLayout();

        try
        {
            _rootLayout.RowStyles[0].Height = Presentation.HeaderForWidth(width);

            _workspaceSplit.Panel1MinSize = veryNarrow ? 165 : 174;
            int requestedSidebar = Presentation.Sidebar(width);
            int maxSidebar = Math.Max(_workspaceSplit.Panel1MinSize, width / 3);

            try
            {
                _workspaceSplit.SplitterDistance = Math.Clamp(
                    requestedSidebar,
                    _workspaceSplit.Panel1MinSize,
                    maxSidebar);
            }
            catch (InvalidOperationException)
            {
                // A resize can briefly make the splitter reject a valid target.
            }

            _rightLayout.RowStyles[0].Height = shortWindow ? 66 : 76;

            _rightLayout.RowStyles[2].Height = narrow ? 106 : 84;

            _headerActionsPanel.WrapContents = false;
            _headerActionsPanel.MaximumSize = Size.Empty;
            _checkUpdateButton.Visible = _showUpdateControlOnHome;
            _wallboardButton.Visible = true;
            _settingsButton.Visible = true;
            _moreButton.Visible = true;

            _settingsFlowPanel.WrapContents = true;
            _settingsFlowPanel.AutoScroll = false;
            _settingsFlowPanel.PerformLayout();
            int settingsHeight = _settingsFlowPanel.Controls.Cast<Control>()
                .Where(control => control.Visible)
                .Select(control => control.Bottom + control.Margin.Bottom)
                .DefaultIfEmpty(0)
                .Max();
            var settingsCard = _settingsFlowPanel.Parent!;
            _rightLayout.RowStyles[2].Height = Math.Max(
                84,
                settingsHeight + settingsCard.Padding.Vertical + settingsCard.Margin.Vertical);
            ApplyInventoryHeight();

            if (_pendingUpdateManager is null)
            {
                if (_updateCheckInProgress)
                    _checkUpdateButton.Text = "Checking…";
                else if (_checkUpdateButton.Text.StartsWith("Updates:", StringComparison.Ordinal))
                    _checkUpdateButton.Text = "Unmanaged";
                else if (_checkUpdateButton.Text.StartsWith("Downloading", StringComparison.Ordinal))
                {
                    // Keep live progress text.
                }
                else if (_checkUpdateButton.Text != "Up to date")
                    _checkUpdateButton.Text = "Updates";
            }

            if (_brandSubtitleLabel is not null)
                _brandSubtitleLabel.Visible = !veryNarrow;

            _siteList.ItemHeight = Math.Max(
                34,
                (int)Math.Round(36 * DeviceDpi / 96d));

            if (!_mainSplit.Panel2Collapsed)
            {
                int splitHeight = _mainSplit.ClientSize.Height;

                if (splitHeight > 150)
                {
                    int desired = (int)(splitHeight * (shortWindow ? 0.56 : 0.64));
                    _mainSplit.SplitterDistance = Math.Clamp(
                        desired,
                        Math.Min(80, splitHeight - 1),
                        Math.Max(81, splitHeight - 70));
                }
            }
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void DrawSiteItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();

        if (e.Index < 0 || e.Index >= _siteList.Items.Count)
            return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);

        using var background = new SolidBrush(selected
            ? Color.FromArgb(27, 76, 94)
            : Color.FromArgb(18, 24, 32));
        using var textBrush = new SolidBrush(selected
            ? Color.White
            : Color.FromArgb(210, 218, 228));

        e.Graphics.FillRectangle(background, bounds);

        string text = _siteList.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            _siteItemFont,
            new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 18, bounds.Height),
            textBrush.Color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        e.DrawFocusRectangle();
    }

    private void LoadSites()
    {
        _sites.Clear();

        if (!_persistSites)
        {
            _sites.Add(new SiteDefinition { Name = "Test Site" });
            return;
        }

        try
        {
            if (File.Exists(_autoConfigPath))
            {
                var autoConfig = JsonSerializer.Deserialize<WatchdogConfig>(
                    File.ReadAllText(_autoConfigPath));

                if (autoConfig is not null)
                {
                    ApplyConfig(autoConfig);
                    return;
                }
            }
        }
        catch
        {
            // Fall through to legacy/default recovery.
        }

        try
        {
            if (File.Exists(_settingsPath))
            {
                var loaded = JsonSerializer.Deserialize<List<SiteDefinition>>(
                    File.ReadAllText(_settingsPath));

                if (loaded is not null)
                {
                    foreach (var site in loaded)
                    {
                        var name = (site.Name ?? string.Empty).Trim();

                        if (string.IsNullOrWhiteSpace(name) ||
                            name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase) ||
                            _sites.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var hosts = (site.Hosts ?? new List<string>())
                            .Select(h => h.Trim())
                            .Where(h => !string.IsNullOrWhiteSpace(h))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        string folderPath = NormalizeFolderPath(site.FolderPath);

                        _sites.Add(new SiteDefinition
                        {
                            Name = name,
                            FolderPath = folderPath,
                            Hosts = hosts,
                            Labels = NormalizeLabels(site.Labels, hosts),
                            Categories = NormalizeCategories(site.Categories, hosts),
                            HostDetails = NormalizeHostDetails(site.HostDetails, hosts)
                        });

                        EnsureFolderHierarchy(folderPath);
                    }

                    if (_sites.Count > 0)
                        return;
                }
            }
        }
        catch
        {
            // Fall through to bundled default.
        }

        try
        {
            if (!File.Exists(_bundledDefaultConfigPath))
                return;

            var defaultConfig = JsonSerializer.Deserialize<WatchdogConfig>(
                File.ReadAllText(_bundledDefaultConfigPath));

            if (defaultConfig is null)
                return;

            ApplyConfig(defaultConfig);
            SaveSites();
        }
        catch
        {
            // A missing/corrupt starter config should never block the app.
        }
    }

    private void SaveSites()
    {
        if (!_persistSites)
            return;

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                _settingsPath,
                JsonSerializer.Serialize(
                    _sites.Where(site => !site.Temporary).ToList(),
                    new JsonSerializerOptions { WriteIndented = true }));

            File.WriteAllText(
                _autoConfigPath,
                JsonSerializer.Serialize(
                    BuildConfig(captureEditor: false),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Monitoring should remain usable even if persistence fails.
        }
    }

    private void RefreshSiteList(string? selectedName)
    {
        _ignoreSiteSelection = true;

        try
        {
            _siteList.Items.Clear();

            int totalHosts = _sites.Sum(s => s.Hosts.Count);
            _siteList.Items.Add(new SiteListItem(
                null,
                $"{AllSitesLabel} ({totalHosts})"));

            foreach (var site in _sites
                .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase))
            {
                _siteList.Items.Add(new SiteListItem(
                    site.Name,
                    $"{FormatSiteDisplayPath(site)} ({site.Hosts.Count})"));
            }

            int selectedIndex = 0;

            if (selectedName is not null)
            {
                for (int i = 1; i < _siteList.Items.Count; i++)
                {
                    if (_siteList.Items[i] is SiteListItem item &&
                        item.SiteName?.Equals(selectedName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            _siteList.SelectedIndex = selectedIndex;
        }
        finally
        {
            _ignoreSiteSelection = false;
        }

        _organizationForm?.RefreshNow();
    }

    private void OnSiteSelectionChanged()
    {
        if (_ignoreSiteSelection || _siteList.SelectedIndex < 0)
            return;

        PersistCurrentEditor();
        SaveSites();
        ReconcileMonitoringWithConfig();

        _selectedSiteName = (_siteList.SelectedItem as SiteListItem)?.SiteName;

        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void LoadHostEditor()
    {
        RefreshHostInventory();
        if (_selectedSiteName is null)
        {
            _siteHeaderLabel.Text = "All sites • Read-only inventory";
            _ipBox.ReadOnly = true;
            _ipBox.Text = string.Join(
                Environment.NewLine,
                _sites.SelectMany(site =>
                    site.Hosts.Select(host => $"[{site.Name}] {host}")));
            return;
        }

        var site = FindSite(_selectedSiteName);

        _siteHeaderLabel.Text = $"{_selectedSiteName} • Hosts";
        _ipBox.ReadOnly = false;
        _ipBox.Text = site is null
            ? string.Empty
            : string.Join(Environment.NewLine, site.Hosts);
    }

    private void PersistCurrentEditor()
    {
        if (_selectedSiteName is null)
            return;

        var site = FindSite(_selectedSiteName);
        if (site is null)
            return;

        site.Hosts = ParseHosts(_ipBox.Text);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);
        site.Categories = NormalizeCategories(site.Categories, site.Hosts);
        site.HostDetails = NormalizeHostDetails(site.HostDetails, site.Hosts);
    }

    private static Dictionary<string, string> NormalizeLabels(
        Dictionary<string, string>? labels,
        IEnumerable<string> hosts)
    {
        var hostList = hosts.ToList();
        var result = new Dictionary<string, string>();

        if (labels is null)
            return result;

        foreach (var pair in labels)
        {
            string? host = hostList.FirstOrDefault(h =>
                h.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            string label = pair.Value?.Trim() ?? string.Empty;

            if (host is not null && !string.IsNullOrWhiteSpace(label))
                result[host] = label;
        }

        return result;
    }

    private static Dictionary<string, string> NormalizeCategories(
        Dictionary<string, string>? categories,
        IEnumerable<string> hosts)
    {
        var hostList = hosts.ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (categories is null)
            return result;

        foreach (var pair in categories)
        {
            string? host = hostList.FirstOrDefault(h =>
                h.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            string category = pair.Value?.Trim() ?? string.Empty;

            if (host is not null && !string.IsNullOrWhiteSpace(category))
                result[host] = category;
        }

        return result;
    }

    internal static string NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return string.Join(
            "/",
            path
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string FolderParent(string path)
    {
        path = NormalizeFolderPath(path);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string FolderName(string path)
    {
        path = NormalizeFolderPath(path);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string CombineFolderPath(string parent, string name)
    {
        parent = NormalizeFolderPath(parent);
        name = name.Trim();

        return string.IsNullOrWhiteSpace(parent)
            ? NormalizeFolderPath(name)
            : NormalizeFolderPath($"{parent}/{name}");
    }

    private void EnsureFolderHierarchy(string? path)
    {
        path = NormalizeFolderPath(path);

        if (string.IsNullOrWhiteSpace(path))
            return;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = string.Empty;

        foreach (var part in parts)
        {
            current = CombineFolderPath(current, part);

            if (!_siteFolders.Any(folder =>
                folder.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                _siteFolders.Add(current);
            }
        }
    }

    private string FormatSiteDisplayPath(SiteDefinition site)
    {
        string folder = NormalizeFolderPath(site.FolderPath);

        return string.IsNullOrWhiteSpace(folder)
            ? site.Name
            : $"{folder.Replace("/", " › ")} › {site.Name}";
    }

    private OrganizationSnapshot BuildOrganizationSnapshot()
    {
        return new OrganizationSnapshot(
            _siteFolders
                .Select(NormalizeFolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _sites
                .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
                .Select(site => new OrganizationSiteSnapshot(
                    site.Name,
                    NormalizeFolderPath(site.FolderPath),
                    site.Hosts.Count))
                .ToList());
    }

    private string? ValidateFolderName(string name)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return "Enter a folder name.";

        if (name.Contains('/') || name.Contains('\\'))
            return "Folder names cannot contain / or \\.";

        if (name is "." or "..")
            return "Choose a different folder name.";

        return null;
    }

    private string? AddOrganizationFolder(string parentPath, string name)
    {
        string? error = ValidateFolderName(name);
        if (error is not null)
            return error;

        parentPath = NormalizeFolderPath(parentPath);
        string path = CombineFolderPath(parentPath, name);

        if (_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "A folder with that name already exists here.";
        }

        EnsureFolderHierarchy(path);
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private string? RenameOrganizationFolder(string path, string newName)
    {
        path = NormalizeFolderPath(path);

        if (!_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder no longer exists.";
        }

        string? error = ValidateFolderName(newName);
        if (error is not null)
            return error;

        string parent = FolderParent(path);
        string replacement = CombineFolderPath(parent, newName);

        if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) &&
            _siteFolders.Any(folder =>
                folder.Equals(replacement, StringComparison.OrdinalIgnoreCase)))
        {
            return "A folder with that name already exists here.";
        }

        RewriteFolderPrefix(path, replacement);
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private string? DeleteOrganizationFolder(string path)
    {
        path = NormalizeFolderPath(path);

        if (!_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder no longer exists.";
        }

        string parent = FolderParent(path);
        RewriteFolderPrefix(path, parent);
        _siteFolders.RemoveAll(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase));

        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private string? MoveOrganizationFolder(string path, string newParent)
    {
        path = NormalizeFolderPath(path);
        newParent = NormalizeFolderPath(newParent);

        if (!_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder no longer exists.";
        }

        if (!string.IsNullOrWhiteSpace(newParent) &&
            !_siteFolders.Any(folder =>
                folder.Equals(newParent, StringComparison.OrdinalIgnoreCase)))
        {
            return "The destination folder no longer exists.";
        }

        if (newParent.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            newParent.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "A folder cannot be moved inside itself.";
        }

        string replacement = CombineFolderPath(newParent, FolderName(path));

        if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) &&
            _siteFolders.Any(folder =>
                folder.Equals(replacement, StringComparison.OrdinalIgnoreCase)))
        {
            return "A folder with that name already exists in the destination.";
        }

        RewriteFolderPrefix(path, replacement);
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private void RewriteFolderPrefix(string oldPath, string newPath)
    {
        oldPath = NormalizeFolderPath(oldPath);
        newPath = NormalizeFolderPath(newPath);

        var rewritten = new List<string>();

        foreach (var folder in _siteFolders)
        {
            string normalized = NormalizeFolderPath(folder);

            if (normalized.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(newPath))
                    rewritten.Add(newPath);
            }
            else if (normalized.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = normalized[(oldPath.Length + 1)..];
                rewritten.Add(CombineFolderPath(newPath, suffix));
            }
            else
            {
                rewritten.Add(normalized);
            }
        }

        _siteFolders.Clear();

        foreach (var folder in rewritten
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EnsureFolderHierarchy(folder);
        }

        foreach (var site in _sites)
        {
            string siteFolder = NormalizeFolderPath(site.FolderPath);

            if (siteFolder.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
            {
                site.FolderPath = newPath;
            }
            else if (siteFolder.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = siteFolder[(oldPath.Length + 1)..];
                site.FolderPath = CombineFolderPath(newPath, suffix);
            }
        }
    }

    private string? AddOrganizationSite(string name, string folderPath)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return "Enter a site name.";

        if (name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase))
            return "\"All Sites\" is reserved.";

        if (_sites.Any(site =>
            site.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return "A site with that name already exists.";
        }

        folderPath = NormalizeFolderPath(folderPath);

        if (!string.IsNullOrWhiteSpace(folderPath))
            EnsureFolderHierarchy(folderPath);

        PersistCurrentEditor();

        _sites.Add(new SiteDefinition
        {
            Name = name,
            FolderPath = folderPath
        });

        _selectedSiteName = name;
        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(name);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
        return null;
    }

    private string? RenameOrganizationSite(string siteName, string newName)
    {
        return WallboardRenameSite(siteName, newName);
    }

    private string? DeleteOrganizationSite(string siteName)
    {
        return WallboardDeleteSite(siteName);
    }

    private string? MoveOrganizationSite(string siteName, string folderPath)
    {
        var site = FindSite(siteName);

        if (site is null)
            return "That site no longer exists.";

        folderPath = NormalizeFolderPath(folderPath);

        if (!string.IsNullOrWhiteSpace(folderPath))
            EnsureFolderHierarchy(folderPath);

        site.FolderPath = folderPath;
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private void SelectOrganizationSite(string siteName)
    {
        if (FindSite(siteName) is null)
            return;

        PersistCurrentEditor();
        _selectedSiteName = siteName;
        RefreshSiteList(siteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void OpenOrganization(IWin32Window? owner = null)
    {
        if (_organizationForm is not null && !_organizationForm.IsDisposed)
        {
            _organizationForm.Show();
            _organizationForm.Activate();
            _organizationForm.RefreshNow();
            return;
        }

        _organizationForm = new OrganizationForm(
            BuildOrganizationSnapshot,
            new OrganizationActions(
                AddOrganizationFolder,
                RenameOrganizationFolder,
                DeleteOrganizationFolder,
                MoveOrganizationFolder,
                AddOrganizationSite,
                RenameOrganizationSite,
                DeleteOrganizationSite,
                MoveOrganizationSite,
                SelectOrganizationSite));

        _organizationForm.FormClosed += (_, _) => _organizationForm = null;
        _organizationForm.Show(owner ?? this);
    }

    private static List<string> ParseHosts(string text)
    {
        return text
            .Split(
                new[] { '\r', '\n', ',', ';', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private SiteDefinition? FindSite(string name)
    {
        return _sites.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetNickname(string siteName, string address)
    {
        var site = FindSite(siteName);
        if (site?.Labels is null)
            return string.Empty;

        var pair = site.Labels.FirstOrDefault(p =>
            p.Key.Equals(address, StringComparison.OrdinalIgnoreCase));

        return pair.Key is null ? string.Empty : pair.Value;
    }

    private void SetNicknameValue(string siteName, string address, string nickname)
    {
        var site = FindSite(siteName);
        if (site is null)
            return;

        site.Labels ??= new Dictionary<string, string>();

        var existingKey = site.Labels.Keys.FirstOrDefault(k =>
            k.Equals(address, StringComparison.OrdinalIgnoreCase));

        if (existingKey is not null)
            site.Labels.Remove(existingKey);

        nickname = nickname.Trim();
        if (!string.IsNullOrWhiteSpace(nickname))
            site.Labels[address] = nickname;

        string key = BuildHostKey(siteName, address);
        if (_hosts.TryGetValue(key, out var activeHost))
            activeHost.Label = nickname;
    }

    private string GetCategory(string siteName, string address)
    {
        var site = FindSite(siteName);
        if (site?.Categories is null)
            return string.Empty;

        var pair = site.Categories.FirstOrDefault(p =>
            p.Key.Equals(address, StringComparison.OrdinalIgnoreCase));

        return pair.Key is null ? string.Empty : pair.Value;
    }

    private void SetCategoryValue(string siteName, string address, string category)
    {
        var site = FindSite(siteName);
        if (site is null)
            return;

        site.Categories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var existingKey = site.Categories.Keys.FirstOrDefault(k =>
            k.Equals(address, StringComparison.OrdinalIgnoreCase));

        if (existingKey is not null)
            site.Categories.Remove(existingKey);

        category = category.Trim();
        if (!string.IsNullOrWhiteSpace(category))
            site.Categories[address] = category;
    }

    private string DescribeHost(string siteName, string address)
    {
        string nickname = GetNickname(siteName, address);
        return string.IsNullOrWhiteSpace(nickname)
            ? address
            : $"{nickname} ({address})";
    }

    private void BuildGridContextMenu()
    {
        _gridMenu.BackColor = Color.FromArgb(22, 27, 34);
        _gridMenu.ForeColor = Color.FromArgb(230, 237, 243);

        var setLabel = new ToolStripMenuItem("Set label / nickname");
        var clearLabel = new ToolStripMenuItem("Clear label");
        var setCategory = new ToolStripMenuItem("Set category...");
        var clearCategory = new ToolStripMenuItem("Clear category");

        setLabel.Click += (_, _) => SetLabelForSelectedHost();
        clearLabel.Click += (_, _) => ClearLabelForSelectedHost();
        setCategory.Click += (_, _) => SetCategoryForSelectedHost();
        clearCategory.Click += (_, _) => ClearCategoryForSelectedHost();

        _gridMenu.Items.Add(setLabel);
        _gridMenu.Items.Add(clearLabel);
        _gridMenu.Items.Add(new ToolStripSeparator());
        _gridMenu.Items.Add(setCategory);
        _gridMenu.Items.Add(clearCategory);
        _grid.ContextMenuStrip = _gridMenu;
    }

    private void GridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;

        _grid.ClearSelection();
        _grid.Rows[e.RowIndex].Selected = true;

        if (e.ColumnIndex >= 0)
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
    }

    private (string Site, string Host)? GetSelectedHostIdentity()
    {
        if (_grid.SelectedRows.Count == 0)
            return null;

        var row = _grid.SelectedRows[0];
        string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
        string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(host))
            return null;

        return (site, host);
    }

    private void SetLabelForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        string current = GetNickname(identity.Value.Site, identity.Value.Host);

        using var dialog = new NicknameDialog(identity.Value.Host, current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetNicknameValue(identity.Value.Site, identity.Value.Host, dialog.Nickname);
        SaveSites();
        RefreshGrid();
        RebuildCommandView();
    }

    private void ClearLabelForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        SetNicknameValue(identity.Value.Site, identity.Value.Host, string.Empty);
        SaveSites();
        RefreshGrid();
        RebuildCommandView();
    }

    private void SetCategoryForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        string current = GetCategory(identity.Value.Site, identity.Value.Host);

        using var dialog = new CategoryDialog(identity.Value.Host, current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetCategoryValue(identity.Value.Site, identity.Value.Host, dialog.Category);
        SaveSites();
        RefreshGrid();
    }

    private void ClearCategoryForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        SetCategoryValue(identity.Value.Site, identity.Value.Host, string.Empty);
        SaveSites();
        RefreshGrid();
    }

    private WatchdogConfig BuildConfig(bool captureEditor = true)
    {
        if (captureEditor)
            PersistCurrentEditor();

        return new WatchdogConfig
        {
            Sites = _sites.Where(site => !site.Temporary).Select(site => new SiteDefinition
            {
                Name = site.Name,
                FolderPath = NormalizeFolderPath(site.FolderPath),
                Hosts = site.Hosts.ToList(),
                Labels = new Dictionary<string, string>(site.Labels ?? new()),
                Categories = new Dictionary<string, string>(site.Categories ?? new(), StringComparer.OrdinalIgnoreCase),
                HostDetails = NormalizeHostDetails(site.HostDetails, site.Hosts)
            }).ToList(),
            SiteFolders = _siteFolders
                .Select(NormalizeFolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PingIntervalSeconds = (int)_intervalSeconds.Value,
            PingTimeoutMs = (int)_timeoutMs.Value,
            FailureThreshold = (int)_failureThreshold.Value,
            RecoveryThreshold = (int)_recoveryThreshold.Value,
            ShowCommandView = _showCommandView.Checked,
            SelectedSite = _selectedSiteName,
            EventHistoryHours = _eventHistoryHours,
            HideSuspectEvents = _hideSuspectEvents,
            Use12HourTime = DisplayTime.Use12HourTime,
            AutoCheckUpdates = _autoCheckUpdates,
            MinimizeToTray = _minimizeToTray,
            NotificationsEnabled = _notificationsEnabled,
            WallboardShowCli = _wallboardShowCli,
            ShowUpdateControlOnHome = _showUpdateControlOnHome
        };
    }

    private void SaveConfigFile(IWin32Window? owner = null)
    {
        var config = BuildConfig();

        using var dialog = new SaveFileDialog
        {
            Title = "Save Ping Watchdog Config",
            Filter = "Ping Watchdog Config (*.pingwatch.json)|*.pingwatch.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = "ping-watchdog-config.pingwatch.json",
            AddExtension = true,
            DefaultExt = "pingwatch.json"
        };

        if (dialog.ShowDialog(owner ?? this) != DialogResult.OK)
            return;

        try
        {
            File.WriteAllText(
                dialog.FileName,
                JsonSerializer.Serialize(
                    config,
                    new JsonSerializerOptions { WriteIndented = true }));

            _statusLabel.Text = $"Config saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save config.\r\n\r\n{ex.Message}",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void LoadConfigFile(IWin32Window? owner = null)
    {
        if (_cts is not null)
        {
            MessageBox.Show(
                "Stop monitoring before loading a config.",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Load Ping Watchdog Config",
            Filter = "Ping Watchdog Config (*.pingwatch.json)|*.pingwatch.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(owner ?? this) != DialogResult.OK)
            return;

        try
        {
            var config = JsonSerializer.Deserialize<WatchdogConfig>(
                File.ReadAllText(dialog.FileName));

            if (config is null)
                throw new InvalidDataException("The config file was empty or invalid.");

            ApplyConfig(config);
            SaveSites();
            _statusLabel.Text = $"Config loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not load config.\r\n\r\n{ex.Message}",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyConfig(WatchdogConfig config)
    {
        var cleanedSites = new List<SiteDefinition>();

        foreach (var source in config.Sites ?? new List<SiteDefinition>())
        {
            string name = (source.Name ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase) ||
                cleanedSites.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var hosts = (source.Hosts ?? new List<string>())
                .Select(h => h.Trim())
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            cleanedSites.Add(new SiteDefinition
            {
                Name = name,
                FolderPath = NormalizeFolderPath(source.FolderPath),
                Hosts = hosts,
                Labels = NormalizeLabels(source.Labels, hosts),
                Categories = NormalizeCategories(source.Categories, hosts),
                HostDetails = NormalizeHostDetails(source.HostDetails, hosts)
            });
        }

        if (cleanedSites.Count == 0)
            cleanedSites.Add(new SiteDefinition { Name = "Default Site" });

        _sites.Clear();
        _sites.AddRange(cleanedSites);

        _siteFolders.Clear();

        foreach (var folder in config.SiteFolders ?? new List<string>())
            EnsureFolderHierarchy(folder);

        foreach (var site in _sites)
            EnsureFolderHierarchy(site.FolderPath);

        _intervalSeconds.Value = Math.Clamp(config.PingIntervalSeconds, (int)_intervalSeconds.Minimum, (int)_intervalSeconds.Maximum);
        _timeoutMs.Value = Math.Clamp(config.PingTimeoutMs, (int)_timeoutMs.Minimum, (int)_timeoutMs.Maximum);
        _failureThreshold.Value = Math.Clamp(config.FailureThreshold, (int)_failureThreshold.Minimum, (int)_failureThreshold.Maximum);
        _recoveryThreshold.Value = Math.Clamp(config.RecoveryThreshold, (int)_recoveryThreshold.Minimum, (int)_recoveryThreshold.Maximum);
        _showCommandView.Checked = config.ShowCommandView;
        _eventHistoryHours = NormalizeEventHistoryHours(config.EventHistoryHours);
        _hideSuspectEvents = config.HideSuspectEvents;
        DisplayTime.Use12HourTime = config.Use12HourTime;
        _autoCheckUpdates = config.AutoCheckUpdates;
        _minimizeToTray = config.MinimizeToTray;
        _notificationsEnabled = config.NotificationsEnabled;
        _wallboardShowCli = config.WallboardShowCli;
        _showUpdateControlOnHome = config.ShowUpdateControlOnHome;

        _selectedSiteName = config.SelectedSite is not null &&
            _sites.Any(s => s.Name.Equals(config.SelectedSite, StringComparison.OrdinalIgnoreCase))
            ? _sites.First(s => s.Name.Equals(config.SelectedSite, StringComparison.OrdinalIgnoreCase)).Name
            : _sites[0].Name;

        _hosts.Clear();
        ClearCommandLog();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void SaveSelectedHosts()
    {
        if (_selectedSiteName is null || _cts is not null)
            return;

        PersistCurrentEditor();
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
    }

    private void AddSite()
    {
        using var dialog = new SiteNameDialog(
            "Add Site",
            "Add Site");

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string name = dialog.SiteName;

        if (!ValidateNewSiteName(name, existingSiteName: null))
            return;

        PersistCurrentEditor();

        _sites.Add(new SiteDefinition
        {
            Name = name,
            FolderPath = string.Empty
        });
        _selectedSiteName = name;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(name);
        LoadHostEditor();
        RefreshGrid();
        UpdateActionState();
    }

    private void RenameSite()
    {
        if (_selectedSiteName is null)
            return;

        var site = FindSite(_selectedSiteName);
        if (site is null)
            return;

        using var dialog = new SiteNameDialog(
            "Rename Site",
            "Rename",
            site.Name);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string newName = dialog.SiteName;

        if (!ValidateNewSiteName(newName, _selectedSiteName))
            return;

        PersistCurrentEditor();

        site.Name = newName;
        _selectedSiteName = newName;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(newName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void DeleteSite()
    {
        if (_selectedSiteName is null)
            return;

        var site = FindSite(_selectedSiteName);
        if (site is null)
            return;

        var result = MessageBox.Show(
            $"Delete site \"{site.Name}\" and its {site.Hosts.Count} saved host(s)?",
            "Ping Watchdog",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _sites.Remove(site);

        if (_sites.Count == 0)
            _sites.Add(new SiteDefinition { Name = "Default Site" });

        _selectedSiteName = _sites[0].Name;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private bool ValidateNewSiteName(string name, string? existingSiteName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "Enter a site name first.",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        if (name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "\"All Sites\" is reserved for the combined view.",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        bool duplicate = _sites.Any(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            (existingSiteName is null ||
             !s.Name.Equals(existingSiteName, StringComparison.OrdinalIgnoreCase)));

        if (duplicate)
        {
            MessageBox.Show(
                "A site with that name already exists.",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        return true;
    }

    private void UpdateActionState()
    {
        bool monitoring = _cts is not null;
        bool specificSite = _selectedSiteName is not null;

        _addSiteButton.Enabled = true;
        _organizeButton.Enabled = true;
        _renameSiteButton.Enabled = specificSite;
        _deleteSiteButton.Enabled = specificSite;
        _loadConfigButton.Enabled = !monitoring;
        _saveConfigButton.Enabled = true;

        _ipBox.Enabled = true;
        _ipBox.ReadOnly = !specificSite;
    }

    private void StartMonitoring()
    {
        PersistCurrentEditor();
        SaveSites();
        RefreshSiteList(_selectedSiteName);

        var targets = GetConfiguredTargets();

        if (targets.Count == 0)
        {
            MessageBox.Show(
                "Add at least one IP address or hostname to a site.",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _monitorIntervalSeconds = (int)_intervalSeconds.Value;
        _pingTimeoutMs = (int)_timeoutMs.Value;
        _failureThresholdValue = (int)_failureThreshold.Value;
        _recoveryThresholdValue = (int)_recoveryThreshold.Value;

        _hosts.Clear();

        foreach (var token in _hostTokens.Values)
            token.Dispose();
        _hostTokens.Clear();
        ClearCommandLog();

        _intervalSeconds.Enabled = false;
        _failureThreshold.Enabled = false;
        _recoveryThreshold.Enabled = false;
        _timeoutMs.Enabled = false;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _trayStopItem.Enabled = true;

        _cts = new CancellationTokenSource();

        foreach (var target in targets)
        {
            string key = BuildHostKey(target.Site, target.Address);
            var host = new HostMonitor(
                target.Site,
                target.Address,
                GetNickname(target.Site, target.Address));
            _hosts[key] = host;
            StartHostWorker(key, host);
        }

        UpdateActionState();
        LoadHostEditor();

        _uiTimer.Start();
        _monitorStateLabel.Text = "MONITORING";
        _statusLabel.Text = $"Monitoring {targets.Count} host(s) across {_sites.Count} site(s)";
    }

    private void StopMonitoring()
    {
        _cts?.Cancel();

        foreach (var pair in _hostTokens.ToArray())
        {
            pair.Value.Cancel();
            pair.Value.Dispose();
        }

        _hostTokens.Clear();
        _cts?.Dispose();
        _cts = null;

        _uiTimer.Stop();

        if (!IsDisposed)
        {
            _intervalSeconds.Enabled = true;
            _failureThreshold.Enabled = true;
            _recoveryThreshold.Enabled = true;
            _timeoutMs.Enabled = true;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _trayStopItem.Enabled = false;

            UpdateActionState();
            LoadHostEditor();
            _monitorStateLabel.Text = "IDLE";
            _statusLabel.Text = "Monitoring stopped";
        }
    }

    private List<(string Site, string Address)> GetConfiguredTargets()
    {
        return _sites
            .SelectMany(site =>
                site.Hosts.Where(host => GetHostOptions(site, host).Enabled).Select(host => (Site: site.Name, Address: host)))
            .ToList();
    }

    private void ReconcileMonitoringWithConfig()
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;

        var desired = GetConfiguredTargets()
            .ToDictionary(
                target => BuildHostKey(target.Site, target.Address),
                target => target,
                StringComparer.OrdinalIgnoreCase);

        foreach (var key in _hosts.Keys.ToArray())
        {
            if (desired.ContainsKey(key))
                continue;

            StopHostWorker(key);
            _hosts.TryRemove(key, out _);
        }

        foreach (var pair in desired)
        {
            if (_hosts.TryGetValue(pair.Key, out var existing))
            {
                existing.Options = GetHostOptions(FindSite(existing.Site), existing.Address).Copy();
                continue;
            }

            var host = new HostMonitor(
                pair.Value.Site,
                pair.Value.Address,
                GetNickname(pair.Value.Site, pair.Value.Address));
            _hosts[pair.Key] = host;
            StartHostWorker(pair.Key, host);
        }

        _statusLabel.Text = $"Monitoring {_hosts.Count} host(s) across {_sites.Count} site(s)...";
    }

    private void StartHostWorker(string key, HostMonitor host)
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;

        var workerToken = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        if (!_hostTokens.TryAdd(key, workerToken))
        {
            workerToken.Dispose();
            return;
        }

        host.Options = GetHostOptions(FindSite(host.Site), host.Address).Copy();
        _ = MonitorHostAsync(host, workerToken.Token);
    }

    private void StopHostWorker(string key)
    {
        if (_hostTokens.TryRemove(key, out var token))
        {
            token.Cancel();
            token.Dispose();
        }
    }

    private static string BuildHostKey(string site, string address)
    {
        return $"{site}\u001f{address}";
    }

    private async Task MonitorHostAsync(HostMonitor host, CancellationToken token)
    {
        using var ping = new Ping();

        while (!token.IsCancellationRequested)
        {
            bool success;
            long? latency = null;
            string resultText;

            try
            {
                var reply = await ping.SendPingAsync(host.Address, host.Options.TimeoutMs > 0 ? host.Options.TimeoutMs : _pingTimeoutMs);
                success = reply.Status == IPStatus.Success;

                if (success)
                {
                    latency = reply.RoundtripTime;
                    var ttl = reply.Options is null
                        ? string.Empty
                        : $" TTL={reply.Options.Ttl}";

                    resultText =
                        $"Reply from {reply.Address}: time={reply.RoundtripTime}ms{ttl}";
                }
                else
                {
                    resultText = $"FAILED ({reply.Status})";
                }
            }
            catch (Exception ex)
            {
                success = false;
                resultText = $"FAILED ({ex.GetType().Name})";
            }

            if (token.IsCancellationRequested || IsDisposed || Disposing)
                break;

            AppendCommandLog(host.Site, host.Address, success, resultText);
            ProcessResult(host, success, latency);

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(host.Options.IntervalSeconds > 0 ? host.Options.IntervalSeconds : _monitorIntervalSeconds),
                    token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string DescribeMonitoredHost(HostMonitor host)
    {
        string label = host.Label;
        return string.IsNullOrWhiteSpace(label)
            ? host.Address
            : $"{label} ({host.Address})";
    }

    private void ProcessResult(HostMonitor host, bool success, long? latency)
    {
        string? notificationTitle = null;
        string? notificationBody = null;
        string? eventKind = null;
        string? eventMessage = null;
        ToolTipIcon fallbackIcon = ToolTipIcon.None;

        lock (host)
        {
            var previousState = host.State;
            if (success)
            {
                host.LastRoundTripMs = latency;
                host.LastReply = DateTime.Now;
                host.ConsecutiveFailures = 0;
                host.ConsecutiveSuccesses++;

                if (host.State == HostState.Offline)
                {
                    if (host.ConsecutiveSuccesses >= _recoveryThresholdValue)
                    {
                        var outageStarted = host.OutageStarted;
                        var duration = outageStarted.HasValue
                            ? DateTime.Now - outageStarted.Value
                            : (TimeSpan?)null;

                        host.State = HostState.Online;
                        host.OutageStarted = null;
                        host.AlertedForCurrentOutage = false;

                        notificationTitle = $"Host Recovered • {host.Site}";
                        string displayHost = DescribeMonitoredHost(host);
                        notificationBody = duration.HasValue
                            ? $"{displayHost} is responding again. Outage duration: {FormatDuration(duration.Value)}."
                            : $"{displayHost} is responding again.";
                        eventKind = "RECOVERED";
                        eventMessage = notificationBody;
                        fallbackIcon = ToolTipIcon.Info;
                    }
                }
                else
                {
                    host.State = HostState.Online;
                }
            }
            else
            {
                host.LastRoundTripMs = null;
                host.ConsecutiveSuccesses = 0;
                host.ConsecutiveFailures++;

                if (host.ConsecutiveFailures >= (host.Options.FailureThreshold > 0 ? host.Options.FailureThreshold : _failureThresholdValue))
                {
                    if (host.State != HostState.Offline)
                    {
                        host.State = HostState.Offline;
                        host.OutageStarted ??= DateTime.Now;

                        if (!host.AlertedForCurrentOutage)
                        {
                            host.AlertedForCurrentOutage = true;
                            notificationTitle = $"Host Down • {host.Site}";
                            string displayHost = DescribeMonitoredHost(host);
                            notificationBody =
                                $"{displayHost} failed {host.ConsecutiveFailures} consecutive ping attempts and is now OFFLINE.";
                            eventKind = "DOWN";
                            eventMessage = notificationBody;
                            fallbackIcon = ToolTipIcon.Warning;
                        }
                    }
                }
                else if (host.State != HostState.Offline)
                {
                    host.State = HostState.Suspect;

                    if (previousState != HostState.Suspect)
                    {
                        eventKind = "SUSPECT";
                        eventMessage = $"{DescribeMonitoredHost(host)} has {host.ConsecutiveFailures} failed ping attempt(s).";
                    }
                }
            }
        }

        if (eventKind is not null && eventMessage is not null)
            RecordStateEvent(
                host.Site,
                host.Address,
                DescribeMonitoredHost(host),
                eventKind,
                eventMessage);

        if (!_suppressNotifications &&
            _notificationsEnabled &&
            notificationTitle is not null &&
            notificationBody is not null)
        {
            if (!IsDisposed && !Disposing)
                ShowNotification(
                    notificationTitle,
                    notificationBody,
                    fallbackIcon);
        }
    }

    private void RecordStateEvent(
        string site,
        string host,
        string displayHost,
        string kind,
        string message)
    {
        lock (_stateEvents)
        {
            _stateEvents.Add(new StateEventRecord(
                DateTime.Now,
                site,
                host,
                displayHost,
                kind,
                message));

            PersistEventHistoryUnsafe();
        }
    }

    private void LoadEventHistory()
    {
        if (!_persistSites)
            return;

        try
        {
            if (!File.Exists(_eventHistoryPath))
                return;

            var loaded = JsonSerializer.Deserialize<List<StateEventRecord>>(
                File.ReadAllText(_eventHistoryPath));

            if (loaded is null)
                return;

            lock (_stateEvents)
            {
                _stateEvents.Clear();
                _stateEvents.AddRange(
                    loaded
                        .Where(e =>
                            e.Timestamp != default &&
                            !string.IsNullOrWhiteSpace(e.Site) &&
                            !string.IsNullOrWhiteSpace(e.Host) &&
                            !string.IsNullOrWhiteSpace(e.Kind))
                        .OrderBy(e => e.Timestamp));
            }
        }
        catch
        {
            // A damaged history file must never prevent monitoring.
        }
    }

    private void PersistEventHistoryUnsafe()
    {
        if (!_persistSites)
            return;

        try
        {
            var directory = Path.GetDirectoryName(_eventHistoryPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = _eventHistoryPath + ".tmp";

            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(_stateEvents));

            File.Move(tempPath, _eventHistoryPath, overwrite: true);
        }
        catch
        {
            // History persistence is best-effort; monitoring stays authoritative.
        }
    }

    private IReadOnlyList<StateEventRecord> GetStateEventSnapshot()
    {
        lock (_stateEvents)
            return _stateEvents.ToList();
    }

    internal static IReadOnlyList<StateEventRecord> FilterStateEvents(
        IEnumerable<StateEventRecord> source,
        int windowHours,
        bool hideSuspects,
        DateTime now)
    {
        windowHours = NormalizeEventHistoryHours(windowHours);
        DateTime? cutoff = windowHours == 0
            ? null
            : now.AddHours(-windowHours);

        return source
            .Where(e => !cutoff.HasValue || e.Timestamp >= cutoff.Value)
            .Where(e =>
                !hideSuspects ||
                !e.Kind.Equals("SUSPECT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Timestamp)
            .ToList();
    }

    internal static int NormalizeEventHistoryHours(int hours)
    {
        return hours switch
        {
            0 or 24 or 168 or 720 => hours,
            _ => 24
        };
    }

    internal static string GetEventHistoryWindowLabel(int hours)
    {
        return NormalizeEventHistoryHours(hours) switch
        {
            24 => "Last 24 hours",
            168 => "Last 7 days",
            720 => "Last 30 days",
            _ => "All time"
        };
    }

    private EventHistoryPreferences GetEventHistoryPreferences()
    {
        return new EventHistoryPreferences(
            _eventHistoryHours,
            _hideSuspectEvents);
    }

    private void SetEventHistoryPreferences(int hours, bool hideSuspects)
    {
        _eventHistoryHours = NormalizeEventHistoryHours(hours);
        _hideSuspectEvents = hideSuspects;
        SaveSites();
    }

    private void CycleEventHistoryWindow()
    {
        int[] windows = { 24, 168, 720, 0 };
        int current = Array.IndexOf(windows, _eventHistoryHours);
        _eventHistoryHours = windows[(Math.Max(0, current) + 1) % windows.Length];
        SaveSites();
    }

    private void ToggleSuspectHistory()
    {
        _hideSuspectEvents = !_hideSuspectEvents;
        SaveSites();
    }

    private void OpenEventHistory(IWin32Window? owner = null)
    {
        if (_eventHistoryForm is not null && !_eventHistoryForm.IsDisposed)
        {
            _eventHistoryForm.Show();
            _eventHistoryForm.Activate();
            return;
        }

        _eventHistoryForm = new EventHistoryForm(
            GetStateEventSnapshot,
            GetEventHistoryPreferences,
            SetEventHistoryPreferences);

        _eventHistoryForm.FormClosed += (_, _) => _eventHistoryForm = null;
        _eventHistoryForm.Show(owner ?? this);
    }

    private WallboardSnapshot BuildWallboardSnapshot()
    {
        bool monitoring = _cts is not null && !_cts.IsCancellationRequested;
        var hostLookup = new Dictionary<string, WallboardHostSnapshot>(
            StringComparer.OrdinalIgnoreCase);

        if (monitoring)
        {
            foreach (var pair in _hosts)
            {
                var host = pair.Value;

                lock (host)
                {
                    hostLookup[pair.Key] = new WallboardHostSnapshot(
                        host.Site,
                        host.Address,
                        host.Label,
                        host.State,
                        host.LastRoundTripMs,
                        host.OutageStarted);
                }
            }
        }

        var sites = _sites
            .Select(site =>
            {
                var hosts = site.Hosts.Select(address =>
                {
                    string key = BuildHostKey(site.Name, address);

                    if (monitoring && hostLookup.TryGetValue(key, out var active))
                        return active;

                    return new WallboardHostSnapshot(
                        site.Name,
                        address,
                        GetNickname(site.Name, address),
                        HostState.Unknown,
                        null,
                        null);
                }).ToList();

                return new WallboardSiteSnapshot(
                    site.Name,
                    hosts);
            })
            .ToList();

        List<WallboardEventSnapshot> events;

        lock (_stateEvents)
        {
            events = FilterStateEvents(
                    _stateEvents,
                    _eventHistoryHours,
                    _hideSuspectEvents,
                    DateTime.Now)
                .TakeLast(80)
                .Select(e => new WallboardEventSnapshot(
                    e.Timestamp,
                    e.Site,
                    e.DisplayHost,
                    e.Kind,
                    e.Message))
                .ToList();
        }

        var commands = _commandEntries
            .TakeLast(120)
            .Select(entry => new WallboardCommandSnapshot(
                entry.Timestamp,
                entry.Success,
                FormatCommandEntry(entry)))
            .ToList();

        return new WallboardSnapshot(
            monitoring,
            DateTime.Now,
            sites,
            events,
            commands,
            GetEventHistoryWindowLabel(_eventHistoryHours),
            _hideSuspectEvents);
    }

    private WallboardControlSnapshot BuildWallboardControlSnapshot()
    {
        var selectedSites = _selectedSiteName is null
            ? _sites
            : _sites
                .Where(site => site.Name.Equals(_selectedSiteName, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var hosts = new List<WallboardControlHost>();

        foreach (var site in selectedSites)
        {
            foreach (var address in site.Hosts)
            {
                string key = BuildHostKey(site.Name, address);

                if (_hosts.TryGetValue(key, out var active))
                {
                    lock (active)
                    {
                        hosts.Add(new WallboardControlHost(
                            site.Name,
                            address,
                            active.Label,
                            active.State,
                            active.LastRoundTripMs,
                            active.ConsecutiveFailures,
                            active.LastReply,
                            active.OutageStarted));
                    }
                }
                else
                {
                    hosts.Add(new WallboardControlHost(
                        site.Name,
                        address,
                        GetNickname(site.Name, address),
                        HostState.Unknown,
                        null,
                        0,
                        null,
                        null));
                }
            }
        }

        return new WallboardControlSnapshot(
            _cts is not null,
            _selectedSiteName,
            _sites.Select(site => site.Name).ToList(),
            hosts,
            (int)_intervalSeconds.Value,
            (int)_timeoutMs.Value,
            (int)_failureThreshold.Value,
            (int)_recoveryThreshold.Value,
            _showUpdateControlOnHome,
            GetDisplayVersion(),
            GetUpdateStatusText(),
            GetUpdateActionText());
    }

    private void WallboardSelectSite(string? siteName)
    {
        PersistCurrentEditor();

        if (siteName is not null && FindSite(siteName) is null)
            return;

        _selectedSiteName = siteName;
        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private string? GetWallboardSiteNameError(string name, string? existingSiteName = null)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return "Enter a site name first.";

        if (name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase))
            return "\"All Sites\" is reserved for the combined view.";

        bool duplicate = _sites.Any(site =>
            site.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            (existingSiteName is null ||
             !site.Name.Equals(existingSiteName, StringComparison.OrdinalIgnoreCase)));

        return duplicate
            ? "A site with that name already exists."
            : null;
    }

    private string? WallboardAddSite(string name)
    {
        name = name.Trim();
        string? error = GetWallboardSiteNameError(name);
        if (error is not null)
            return error;

        PersistCurrentEditor();
        _sites.Add(new SiteDefinition
        {
            Name = name,
            FolderPath = string.Empty
        });
        _selectedSiteName = name;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(name);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
        return null;
    }

    private string? WallboardRenameSite(string oldName, string newName)
    {
        var site = FindSite(oldName);
        if (site is null)
            return "That site no longer exists.";

        newName = newName.Trim();
        string? error = GetWallboardSiteNameError(newName, oldName);
        if (error is not null)
            return error;

        PersistCurrentEditor();
        site.Name = newName;

        if (_selectedSiteName?.Equals(oldName, StringComparison.OrdinalIgnoreCase) == true)
            _selectedSiteName = newName;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
        return null;
    }

    private string? WallboardDeleteSite(string siteName)
    {
        var site = FindSite(siteName);
        if (site is null)
            return "That site no longer exists.";

        PersistCurrentEditor();
        _sites.Remove(site);

        if (_sites.Count == 0)
            _sites.Add(new SiteDefinition { Name = "Default Site" });

        if (_selectedSiteName?.Equals(siteName, StringComparison.OrdinalIgnoreCase) == true)
            _selectedSiteName = _sites[0].Name;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
        return null;
    }

    private string? WallboardSaveHosts(string siteName, string hostText)
    {
        var site = FindSite(siteName);
        if (site is null)
            return "That site no longer exists.";

        site.Hosts = ParseHosts(hostText);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);
        site.Categories = NormalizeCategories(site.Categories, site.Hosts);
        site.HostDetails = NormalizeHostDetails(site.HostDetails, site.Hosts);

        if (_selectedSiteName?.Equals(siteName, StringComparison.OrdinalIgnoreCase) == true)
            _ipBox.Text = string.Join(Environment.NewLine, site.Hosts);

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(_selectedSiteName);
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
        return null;
    }

    private string? WallboardSetLabel(string siteName, string host, string label)
    {
        var site = FindSite(siteName);
        if (site is null)
            return "That site no longer exists.";

        if (!site.Hosts.Any(address => address.Equals(host, StringComparison.OrdinalIgnoreCase)))
            return "That host no longer exists in the site.";

        SetNicknameValue(siteName, host, label);
        SaveSites();
        RefreshGrid();
        RebuildCommandView();
        return null;
    }

    private string? WallboardApplyMonitoringSettings(
        int intervalSeconds,
        int timeoutMs,
        int failureThreshold,
        int recoveryThreshold)
    {
        if (_cts is not null)
            return "Stop monitoring before changing timing or outage thresholds.";

        _intervalSeconds.Value = Math.Clamp(
            intervalSeconds,
            (int)_intervalSeconds.Minimum,
            (int)_intervalSeconds.Maximum);
        _timeoutMs.Value = Math.Clamp(
            timeoutMs,
            (int)_timeoutMs.Minimum,
            (int)_timeoutMs.Maximum);
        _failureThreshold.Value = Math.Clamp(
            failureThreshold,
            (int)_failureThreshold.Minimum,
            (int)_failureThreshold.Maximum);
        _recoveryThreshold.Value = Math.Clamp(
            recoveryThreshold,
            (int)_recoveryThreshold.Minimum,
            (int)_recoveryThreshold.Maximum);

        SaveSites();
        return null;
    }

    private void OpenWallboard()
    {
        if (_wallboardForm is not null && !_wallboardForm.IsDisposed)
        {
            _wallboardForm.Activate();
            return;
        }

        var currentScreen = Screen.FromControl(this);
        var screens = Screen.AllScreens;
        var target = screens.FirstOrDefault(s =>
            !s.DeviceName.Equals(currentScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
            ?? currentScreen;

        _wallboardForm = new WallboardForm(
            BuildWallboardSnapshot,
            BuildWallboardControlSnapshot,
            new WallboardActions(
                StartMonitoring,
                StopMonitoring,
                WallboardSelectSite,
                WallboardAddSite,
                WallboardRenameSite,
                WallboardDeleteSite,
                WallboardSaveHosts,
                WallboardSetLabel,
                WallboardApplyMonitoringSettings,
                () => OpenSettings(_wallboardForm),
                () => OpenEventHistory(_wallboardForm),
                () => OpenOrganization(_wallboardForm),
                RunUpdateActionAsync,
                () => SaveConfigFile(_wallboardForm),
                () => LoadConfigFile(_wallboardForm),
                ClearCommandLog),
            target,
            CycleEventHistoryWindow,
            ToggleSuspectHistory,
            _wallboardShowCli);

        _wallboardForm.FormClosed += (_, _) =>
        {
            _wallboardForm = null;

            if (!_closingApplication)
                RestoreAfterWallboard();
        };

        _wallboardForm.Show();
        _wallboardForm.Activate();
    }

    private AppSettingsSnapshot GetAppSettingsSnapshot()
    {
        return new AppSettingsSnapshot(
            (int)_intervalSeconds.Value,
            (int)_timeoutMs.Value,
            (int)_failureThreshold.Value,
            (int)_recoveryThreshold.Value,
            _showCommandView.Checked,
            _wallboardShowCli,
            _minimizeToTray,
            _notificationsEnabled,
            _autoCheckUpdates,
            _showUpdateControlOnHome,
            _eventHistoryHours,
            _hideSuspectEvents,
            DisplayTime.Use12HourTime,
            _cts is not null,
            GetDisplayVersion(),
            GetUpdateStatusText(),
            GetUpdateActionText());
    }

    private string GetUpdateActionText()
    {
        return _pendingUpdateManager is not null && _pendingUpdateInfo is not null
            ? "Restart to Update"
            : "Check for Updates";
    }

    private string GetUpdateStatusText()
    {
        if (_pendingUpdateManager is not null && _pendingUpdateInfo is not null)
            return $"Version {_pendingUpdateVersion ?? string.Empty} is downloaded and ready.";

        if (_updateCheckInProgress)
            return "Checking GitHub Releases...";

        return _checkUpdateButton.Text switch
        {
            "Up to date" => "Ping Watchdog is up to date.",
            "Unmanaged" => "This copy is not managed by the self-updater.",
            string value when value.StartsWith("Downloading", StringComparison.Ordinal) =>
                $"{value} from GitHub Releases.",
            _ when !_autoCheckUpdates =>
                "Automatic update checks are disabled. Manual checks remain available.",
            _ => "Ready to check GitHub Releases."
        };
    }

    private async Task RunUpdateActionAsync()
    {
        if (_pendingUpdateManager is not null && _pendingUpdateInfo is not null)
        {
            RestartToApplyPendingUpdate();
            return;
        }

        await CheckForUpdatesAsync(userInitiated: true);
        _settingsForm?.RefreshRuntimeState();
    }

    private void ApplyAppSettings(AppSettingsSnapshot settings)
    {
        bool wasAutoCheck = _autoCheckUpdates;

        _autoCheckUpdates = settings.AutoCheckUpdates;
        _minimizeToTray = settings.MinimizeToTray;
        _notificationsEnabled = settings.NotificationsEnabled;
        _wallboardShowCli = settings.WallboardShowCli;
        _showUpdateControlOnHome = settings.ShowUpdateControlOnHome;
        _eventHistoryHours = NormalizeEventHistoryHours(settings.EventHistoryHours);
        _hideSuspectEvents = settings.HideSuspectEvents;
        DisplayTime.Use12HourTime = settings.Use12HourTime;

        if (_cts is null)
        {
            _intervalSeconds.Value = Math.Clamp(
                settings.PingIntervalSeconds,
                (int)_intervalSeconds.Minimum,
                (int)_intervalSeconds.Maximum);
            _timeoutMs.Value = Math.Clamp(
                settings.PingTimeoutMs,
                (int)_timeoutMs.Minimum,
                (int)_timeoutMs.Maximum);
            _failureThreshold.Value = Math.Clamp(
                settings.FailureThreshold,
                (int)_failureThreshold.Minimum,
                (int)_failureThreshold.Maximum);
            _recoveryThreshold.Value = Math.Clamp(
                settings.RecoveryThreshold,
                (int)_recoveryThreshold.Minimum,
                (int)_recoveryThreshold.Maximum);
        }

        if (_showCommandView.Checked != settings.ShowCommandView)
            _showCommandView.Checked = settings.ShowCommandView;

        if (_autoCheckUpdates)
        {
            _updateTimer.Start();

            if (!wasAutoCheck)
                _ = CheckForUpdatesAsync(userInitiated: false);
        }
        else
        {
            _updateTimer.Stop();
        }

        ApplyResponsiveLayout();
        SaveSites();
        _eventHistoryForm?.RefreshNow();
        _settingsForm?.RefreshRuntimeState();
        RefreshGrid();
        RebuildCommandView();
        _statusLabel.Text = "Settings saved.";
    }

    private void OpenSettings(IWin32Window? owner = null)
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Show();
            _settingsForm.Activate();
            _settingsForm.RefreshRuntimeState();
            return;
        }

        _settingsForm = new SettingsForm(
            GetAppSettingsSnapshot,
            ApplyAppSettings,
            RunUpdateActionAsync,
            () => OpenEventHistory(owner));

        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show(owner ?? this);
    }

    private void RestoreAfterWallboard()
    {
        if (IsDisposed || Disposing)
            return;

        Show();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        ApplyResponsiveLayout();
        Activate();
        BringToFront();
        _statusLabel.Text = _cts is null
            ? "Ready"
            : $"Monitoring {_hosts.Count} host(s) across {_sites.Count} site(s)";
    }

    private void SetUpdateReadyUi(string version)
    {
        _pendingUpdateVersion = version;
        _checkUpdateButton.Text = "Restart to Update";
        _checkUpdateButton.Enabled = true;
        _checkUpdateButton.BackColor = Color.FromArgb(107, 72, 26);
        _checkUpdateButton.FlatAppearance.BorderColor = Color.FromArgb(190, 132, 47);
        _checkUpdateButton.ForeColor = Color.White;

        _trayUpdateItem.Text = $"Restart to Update ({version})";
        _trayUpdateItem.Visible = true;
        _trayUpdateItem.Enabled = true;

        _statusLabel.Text = $"Update {version} downloaded • restart when ready";
        _settingsForm?.RefreshRuntimeState();
    }

    private void ClearPendingUpdateUi()
    {
        _pendingUpdateManager = null;
        _pendingUpdateInfo = null;
        _pendingUpdateVersion = null;
        _trayUpdateItem.Visible = false;
        _trayUpdateItem.Enabled = false;
        _checkUpdateButton.BackColor = Color.FromArgb(24, 55, 82);
        _checkUpdateButton.FlatAppearance.BorderColor = Color.FromArgb(48, 103, 153);
        _checkUpdateButton.ForeColor = Color.White;
        _settingsForm?.RefreshRuntimeState();
    }

    private void PromptForDownloadedUpdate(string version)
    {
        if (IsDisposed || Disposing ||
            _pendingUpdateManager is null ||
            _pendingUpdateInfo is null)
        {
            return;
        }

        IWin32Window owner =
            _wallboardForm is not null && !_wallboardForm.IsDisposed
                ? _wallboardForm
                : this;

        bool monitoring = _cts is not null;

        var result = MessageBox.Show(
            owner,
            monitoring
                ? $"Ping Watchdog {version} has been downloaded in the background.\r\n\r\nRestarting now will stop the current monitoring session, save your configuration, install the update, and reopen Ping Watchdog.\r\n\r\nRestart now?"
                : $"Ping Watchdog {version} has been downloaded in the background.\r\n\r\nRestart now to install it and reopen Ping Watchdog?",
            "Ping Watchdog Update Ready",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
            ApplyPendingUpdateAndRestart();
    }

    private void RestartToApplyPendingUpdate()
    {
        if (_pendingUpdateManager is null || _pendingUpdateInfo is null)
            return;

        string version = _pendingUpdateVersion ?? "the downloaded version";
        bool monitoring = _cts is not null;

        IWin32Window owner =
            _wallboardForm is not null && !_wallboardForm.IsDisposed
                ? _wallboardForm
                : this;

        var result = MessageBox.Show(
            owner,
            monitoring
                ? $"Version {version} is downloaded and ready.\r\n\r\nRestarting now will stop the current monitoring session, save your sites and settings, apply the update, and reopen Ping Watchdog.\r\n\r\nRestart now?"
                : $"Version {version} is downloaded and ready.\r\n\r\nPing Watchdog will save your current setup, apply the update, and reopen automatically.\r\n\r\nRestart now?",
            "Restart to Update",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
            ApplyPendingUpdateAndRestart();
    }

    private void ApplyPendingUpdateAndRestart()
    {
        if (_pendingUpdateManager is null || _pendingUpdateInfo is null)
            return;

        string version = _pendingUpdateVersion ?? "the downloaded version";

        try
        {
            _wallboardForm?.Close();
            PersistCurrentEditor();
            SaveSites();

            if (_cts is not null)
                StopMonitoring();

            _checkUpdateButton.Enabled = false;
            _checkUpdateButton.Text = "Restarting...";
            _trayUpdateItem.Enabled = false;
            _statusLabel.Text = $"Applying update {version}...";

            StartupRecovery.ExpectUpdateExit();
            _pendingUpdateManager.ApplyUpdatesAndRestart(_pendingUpdateInfo);
        }
        catch (Exception ex)
        {
            StartupRecovery.CancelExpectedExit();
            _checkUpdateButton.Enabled = true;
            _checkUpdateButton.Text = "Restart to Update";
            _trayUpdateItem.Enabled = true;
            _statusLabel.Text = "Update restart failed.";

            MessageBox.Show(
                $"The update is still downloaded, but Ping Watchdog could not restart to apply it.\r\n\r\n{ex.Message}",
                "Ping Watchdog Update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (!userInitiated && !_autoCheckUpdates)
            return;

        if (_pendingUpdateManager is not null && _pendingUpdateInfo is not null)
        {
            if (userInitiated)
                _statusLabel.Text = $"Update {_pendingUpdateVersion ?? string.Empty} is already downloaded and ready.";

            return;
        }

        if (_updateCheckInProgress || IsDisposed || Disposing)
            return;

        _updateCheckInProgress = true;
        _checkUpdateButton.Enabled = false;
        _checkUpdateButton.Text = "Checking...";

        try
        {
            var source = new GithubSource(
                UpdateRepoUrl,
                accessToken: null,
                prerelease: false);

            var manager = new UpdateManager(source);

            if (!manager.IsInstalled)
            {
                _checkUpdateButton.Text = "Unmanaged";

                if (userInitiated)
                {
                    MessageBox.Show(
                        "This copy of Ping Watchdog is not running from the new self-updating package.\r\n\r\nInstall or use the current Velopack Portable release once; future releases will update automatically from GitHub.",
                        "Ping Watchdog Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            var update = await manager.CheckForUpdatesAsync();

            if (update is null)
            {
                ClearPendingUpdateUi();
                _checkUpdateButton.Text = "Up to date";

                if (userInitiated)
                    _statusLabel.Text = "Ping Watchdog is up to date.";

                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            _checkUpdateButton.Text = "Downloading 0%";

            await manager.DownloadUpdatesAsync(
                update,
                progress =>
                {
                    if (IsDisposed || Disposing)
                        return;

                    BeginInvoke(() =>
                    {
                        if (!IsDisposed && !Disposing)
                            _checkUpdateButton.Text = $"Downloading {progress}%";
                    });
                });

            _pendingUpdateManager = manager;
            _pendingUpdateInfo = update;
            SetUpdateReadyUi(version);

            PromptForDownloadedUpdate(version);
        }
        catch (Exception ex)
        {
            if (_pendingUpdateManager is null)
                _checkUpdateButton.Text = "Check Updates";

            if (userInitiated)
            {
                MessageBox.Show(
                    $"Could not check for updates.\r\n\r\n{ex.Message}",
                    "Ping Watchdog Updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _updateCheckInProgress = false;

            if (!IsDisposed && !Disposing)
                _checkUpdateButton.Enabled = true;

            _settingsForm?.RefreshRuntimeState();
        }
    }

    private void ToggleCommandView()
    {
        _mainSplit.Panel2Collapsed = !_showCommandView.Checked;

        if (_showCommandView.Checked)
        {
            BeginInvoke(() =>
            {
                if (_mainSplit.IsDisposed || _mainSplit.Panel2Collapsed)
                    return;

                int height = _mainSplit.ClientSize.Height;

                if (height > 150)
                {
                    int desired = (int)(height * (ClientSize.Height < 720 ? 0.52 : 0.60));
                    _mainSplit.SplitterDistance = Math.Clamp(
                        desired,
                        Math.Min(80, height - 1),
                        Math.Max(81, height - 70));
                }

                RebuildCommandView();
            });
        }
    }

    private void ClearCommandLog()
    {
        _commandEntries.Clear();
        _commandBox.Clear();
    }

    private void AppendCommandLog(
        string site,
        string host,
        bool success,
        string resultText)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() =>
                AppendCommandLog(site, host, success, resultText));
            return;
        }

        var entry = new CommandLogEntry(
            site,
            host,
            DateTime.Now,
            success,
            resultText,
            _hosts.TryGetValue(BuildHostKey(site, host), out var monitored) && monitored.Options.TimeoutMs > 0
                ? monitored.Options.TimeoutMs : _pingTimeoutMs);

        _commandEntries.Add(entry);

        bool trimmed = false;

        if (_commandEntries.Count > 2000)
        {
            _commandEntries.RemoveRange(0, 500);
            trimmed = true;
        }

        if (trimmed)
        {
            RebuildCommandView();
            return;
        }

        if (CommandEntryMatchesFilter(entry))
            AppendCommandEntryToView(entry);
    }

    private bool CommandEntryMatchesFilter(CommandLogEntry entry)
    {
        return _selectedSiteName is null ||
               entry.Site.Equals(
                   _selectedSiteName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildCommandView()
    {
        _commandBox.Clear();

        foreach (var entry in _commandEntries)
        {
            if (CommandEntryMatchesFilter(entry))
                AppendCommandEntryToView(entry, scroll: false);
        }

        _commandBox.SelectionStart = _commandBox.TextLength;
        _commandBox.ScrollToCaret();
    }

    private string FormatCommandEntry(CommandLogEntry entry)
    {
        string nickname = GetNickname(entry.Site, entry.Host);
        string labelPart = string.IsNullOrWhiteSpace(nickname)
            ? string.Empty
            : $" [{nickname}]";

        return
            $"{entry.Host}: [{entry.Site}]{labelPart} [{DisplayTime.Clock(entry.Timestamp)}] ping {entry.Host} -n 1 -w {entry.TimeoutMs}  ->  {entry.ResultText}";
    }

    private void AppendCommandEntryToView(
        CommandLogEntry entry,
        bool scroll = true)
    {
        string command = FormatCommandEntry(entry);

        _commandBox.SelectionStart = _commandBox.TextLength;
        _commandBox.SelectionLength = 0;
        _commandBox.SelectionColor = entry.Success
            ? Color.FromArgb(126, 231, 135)
            : Color.FromArgb(255, 123, 114);

        _commandBox.AppendText(command + Environment.NewLine);
        _commandBox.SelectionColor = _commandBox.ForeColor;

        if (scroll && _showCommandView.Checked)
        {
            _commandBox.SelectionStart = _commandBox.TextLength;
            _commandBox.ScrollToCaret();
        }
    }

    private void ApplyDarkTheme()
    {
        var window = ColorTranslator.FromHtml(Presentation.Window);
        var header = ColorTranslator.FromHtml(Presentation.Header);
        var nav = ColorTranslator.FromHtml(Presentation.Navigation);
        var card = ColorTranslator.FromHtml(Presentation.Card);
        var input = ColorTranslator.FromHtml(Presentation.Input);
        var border = ColorTranslator.FromHtml(Presentation.Border);
        var text = ColorTranslator.FromHtml(Presentation.Text);
        var muted = ColorTranslator.FromHtml(Presentation.Muted);
        var accent = ColorTranslator.FromHtml(Presentation.Accent);

        BackColor = window;
        ForeColor = text;

        void Theme(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                string tag = control.Tag?.ToString() ?? string.Empty;

                control.ForeColor = text;

                if (tag == "header")
                    control.BackColor = header;
                else if (tag == "nav")
                    control.BackColor = nav;
                else if (tag == "card")
                    control.BackColor = card;
                else if (tag == "window")
                    control.BackColor = window;

                switch (control)
                {
                    case TextBox box:
                        box.BackColor = input;
                        box.ForeColor = text;
                        box.BorderStyle = BorderStyle.FixedSingle;
                        box.Margin = new Padding(0);
                        break;

                    case RichTextBox rich:
                        rich.BackColor = Color.FromArgb(6, 10, 15);
                        rich.ForeColor = Color.FromArgb(199, 211, 223);
                        break;

                    case NumericUpDown number:
                        number.BackColor = input;
                        number.ForeColor = text;
                        number.BorderStyle = BorderStyle.FixedSingle;
                        break;

                    case Button button:
                        button.FlatStyle = FlatStyle.Flat;
                        button.FlatAppearance.BorderColor = border;
                        button.FlatAppearance.BorderSize = 1;
                        button.BackColor = Color.FromArgb(25, 34, 45);
                        button.ForeColor = text;

                        if (ReferenceEquals(button, _moreButton))
                        {
                            button.Padding = Padding.Empty;
                            button.Width = Math.Max(button.Width, 48);
                            button.Height = Math.Max(button.Height, 34);
                            button.Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold);
                            button.TextAlign = ContentAlignment.MiddleCenter;
                        }
                        else
                        {
                            button.Padding = new Padding(8, 2, 8, 2);
                            button.Height = Math.Max(button.Height, 32);
                        }
                        break;

                    case Label label:
                        label.BackColor = Color.Transparent;
                        label.ForeColor = tag switch
                        {
                            "title" => Color.White,
                            "primaryText" => text,
                            "accentText" => accent,
                            "statOnline" => Color.FromArgb(72, 207, 137),
                            "statSuspect" => Color.FromArgb(244, 190, 72),
                            "statOffline" => Color.FromArgb(255, 104, 112),
                            "statTotal" => Color.FromArgb(190, 210, 232),
                            "stateBadge" => Color.FromArgb(155, 220, 255),
                            _ => muted
                        };
                        break;

                    case CheckBox check:
                        check.BackColor = Color.Transparent;
                        check.ForeColor = text;
                        check.Padding = new Padding(6, 8, 0, 0);
                        break;

                    case ListBox list:
                        list.BackColor = nav;
                        list.ForeColor = text;
                        list.BorderStyle = BorderStyle.None;
                        break;

                    case ContextMenuStrip menu:
                        menu.BackColor = card;
                        menu.ForeColor = text;
                        break;

                    case FlowLayoutPanel flow when string.IsNullOrEmpty(tag):
                        flow.BackColor = window;
                        break;

                    case TableLayoutPanel table when string.IsNullOrEmpty(tag):
                        table.BackColor = window;
                        break;

                    case SplitContainer split:
                        split.BackColor = border;
                        if (split.Panel1.Tag?.ToString() != "nav")
                            split.Panel1.BackColor = window;
                        split.Panel2.BackColor = window;
                        break;
                }

                if (control.HasChildren)
                    Theme(control);
            }
        }

        Theme(this);

        _monitorStateLabel.BackColor = Color.FromArgb(20, 50, 68);
        _monitorStateLabel.ForeColor = Color.FromArgb(139, 211, 255);

        _startButton.BackColor = Color.FromArgb(31, 120, 82);
        _startButton.FlatAppearance.BorderColor = Color.FromArgb(53, 170, 116);
        _startButton.ForeColor = Color.White;

        _stopButton.BackColor = Color.FromArgb(93, 38, 45);
        _stopButton.FlatAppearance.BorderColor = Color.FromArgb(155, 64, 75);
        _stopButton.ForeColor = Color.White;

        _addSiteButton.BackColor = Color.FromArgb(20, 69, 82);
        _addSiteButton.FlatAppearance.BorderColor = Color.FromArgb(42, 170, 189);
        _deleteSiteButton.BackColor = Color.FromArgb(57, 28, 34);
        _deleteSiteButton.FlatAppearance.BorderColor = Color.FromArgb(100, 45, 55);

        _wallboardButton.BackColor = Color.FromArgb(20, 69, 82);
        _wallboardButton.FlatAppearance.BorderColor = Color.FromArgb(42, 170, 189);

        _settingsButton.BackColor = Color.FromArgb(25, 34, 45);
        _settingsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 49, 61);

        _moreButton.BackColor = Color.FromArgb(25, 34, 45);
        _moreButton.FlatAppearance.BorderColor = Color.FromArgb(39, 49, 61);
        _moreButton.ForeColor = accent;

        _appMenu.BackColor = card;
        _appMenu.ForeColor = text;

        _checkUpdateButton.BackColor = Color.FromArgb(24, 55, 82);
        _checkUpdateButton.FlatAppearance.BorderColor = Color.FromArgb(48, 103, 153);

        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = card;
        _grid.GridColor = Color.FromArgb(31, 40, 50);
        _grid.DefaultCellStyle.BackColor = card;
        _grid.DefaultCellStyle.ForeColor = text;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(34, 101, 124);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(16, 23, 31);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 31, 42);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(183, 205, 220);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(22, 31, 42);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

        _statusStrip.BackColor = Color.FromArgb(11, 16, 23);
        _statusStrip.ForeColor = muted;
        _statusLabel.ForeColor = muted;
        _ownershipLabel.ForeColor = Color.FromArgb(105, 119, 135);

        _commandBox.BackColor = Color.FromArgb(5, 9, 14);
        _commandBox.ForeColor = Color.FromArgb(199, 211, 223);
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

    private void RefreshGrid()
    {
        var selection = GetSelectedHostIdentity();
        string? selectedColumn = _grid.CurrentCell?.OwningColumn.Name;
        int firstDisplayedRow = _grid.FirstDisplayedScrollingRowIndex;
        int horizontalOffset = _grid.HorizontalScrollingOffset;
        var rows = _hosts.Values
            .Where(h =>
                _selectedSiteName is null ||
                h.Site.Equals(
                    _selectedSiteName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(h =>
            {
                lock (h)
                {
                    return new
                    {
                        Site = h.Site,
                        Host = h.Address,
                        Label = GetNickname(h.Site, h.Address),
                        Category = GetCategory(h.Site, h.Address),
                        Status = h.State switch
                        {
                            HostState.Online => "ONLINE",
                            HostState.Suspect => "SUSPECT",
                            HostState.Offline => "OFFLINE",
                            _ => "UNKNOWN"
                        },
                        Latency = h.LastRoundTripMs.HasValue
                            ? $"{h.LastRoundTripMs} ms"
                            : "—",
                        Failures = h.ConsecutiveFailures,
                        LastReply = (h.LastReply is DateTime reply ? DisplayTime.Timestamp(reply) : null) ?? "—",
                        OutageSince = (h.OutageStarted is DateTime outage ? DisplayTime.Timestamp(outage) : null) ?? "—"
                    };
                }
            })
            .OrderBy(h => h.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _grid.DataSource = rows;

        if (_grid.Columns["SiteColumn"] is DataGridViewColumn siteColumn)
            siteColumn.Visible = _selectedSiteName is null;

        // Restore after rebinding and setting column visibility, before repainting.
        _grid.ClearSelection();
        _grid.CurrentCell = null;
        if (selection is { } desired)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!string.Equals(row.Cells["SiteColumn"].Value?.ToString(), desired.Site, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(row.Cells["HostColumn"].Value?.ToString(), desired.Host, StringComparison.OrdinalIgnoreCase))
                    continue;

                var column = selectedColumn is not null ? _grid.Columns[selectedColumn] : null;
                if (column?.Visible != true)
                    column = _grid.Columns["HostColumn"];
                _grid.CurrentCell = row.Cells[column!.Index];
                row.Selected = true;
                break;
            }
        }
        if (firstDisplayedRow >= 0 && _grid.Rows.Count > 0)
            _grid.FirstDisplayedScrollingRowIndex = Math.Min(firstDisplayedRow, _grid.Rows.Count - 1);
        _grid.HorizontalScrollingOffset = horizontalOffset;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var status = row.Cells["StatusColumn"].Value?.ToString();
            var cell = row.Cells["StatusColumn"];

            cell.Style.Font = _statusCellFont;
            cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            cell.Style.ForeColor = status switch
            {
                "OFFLINE" => Color.FromArgb(255, 120, 128),
                "SUSPECT" => Color.FromArgb(244, 194, 84),
                "ONLINE" => Color.FromArgb(88, 214, 148),
                _ => Color.FromArgb(139, 153, 169)
            };

            if (status == "OFFLINE")
                row.DefaultCellStyle.BackColor = Color.FromArgb(40, 24, 29);
        }

        int online = rows.Count(h => h.Status == "ONLINE");
        int suspect = rows.Count(h => h.Status == "SUSPECT");
        int offline = rows.Count(h => h.Status == "OFFLINE");

        int configured = _selectedSiteName is null
            ? _sites.Sum(s => s.Hosts.Count)
            : FindSite(_selectedSiteName)?.Hosts.Count ?? 0;

        _totalValueLabel.Text = (_cts is null ? configured : rows.Count).ToString();
        _onlineValueLabel.Text = online.ToString();
        _suspectValueLabel.Text = suspect.ToString();
        _offlineValueLabel.Text = offline.ToString();

        string view = _selectedSiteName ?? AllSitesLabel;

        _statusLabel.Text = _cts is null
            ? $"Ready • View: {view} • {configured} configured host(s)"
            : $"Monitoring • View: {view} • {online} online • {suspect} suspect • {offline} offline";
    }
}
