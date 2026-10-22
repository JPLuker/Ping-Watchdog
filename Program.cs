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
        if (DataDeletionStore.FinishPending(StoragePaths.Current)) StartupRecovery.ResumeAfterDeletion();
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

        var ok = new AuroraButton
        {
            Text = "Save Label",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 134, 96),
            ForeColor = Color.White
        };

        var cancel = new AuroraButton
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

        var ok = new AuroraButton
        {
            Text = actionText,
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 134, 96),
            ForeColor = Color.White
        };

        var cancel = new AuroraButton
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
    string UpdateActionText,
    bool RestoreLastSiteOnStartup = false,
    bool StartMonitoringOnLaunch = false,
    AppearanceMode Appearance = AppearanceMode.System);

internal sealed record WallboardControlHost(
    string Site,
    string Address,
    string Label,
    HostState State,
    long? LatencyMs,
    int Failures,
    DateTime? LastReply,
    DateTime? OutageStarted);

internal sealed record WallboardConfiguredHost(
    string Site,
    string Address);

internal sealed record WallboardControlSnapshot(
    bool Monitoring,
    string? SelectedSite,
    IReadOnlyList<string> Sites,
    IReadOnlyList<WallboardControlHost> Hosts,
    IReadOnlyList<WallboardConfiguredHost> ConfiguredHosts,
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
    Action ClearCommandLog,
    Action? OpenHosts = null);

internal sealed record WallboardHostSnapshot(
    string Site,
    string Address,
    string Label,
    HostState State,
    long? LatencyMs,
    DateTime? OutageStarted,
    DateTimeOffset? SnoozedUntilUtc = null);

internal sealed record WallboardSiteSnapshot(
    string Name,
    IReadOnlyList<WallboardHostSnapshot> Hosts);

internal sealed record WallboardEventSnapshot(
    DateTime Timestamp,
    string Site,
    string Host,
    string Kind,
    string Message,
    string Address = "");

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

    private readonly ListBox _siteList = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Font = new Font("Segoe UI", 10)
    };

    private readonly Button _addSiteButton = new AuroraButton() { Text = "+ Add Site", AutoSize = true };
    private readonly Button _organizeButton = new AuroraButton() { Text = "Organize", AutoSize = true };
    private readonly Button _renameSiteButton = new AuroraButton() { Text = "Rename Site", AutoSize = true };
    private readonly Button _deleteSiteButton = new AuroraButton() { Text = "Delete Site", AutoSize = true };
    private readonly Label _autoSaveLabel = new()
    {
        Text = "Auto-save • live apply",
        AutoSize = true,
        Padding = new Padding(0, 7, 0, 0)
    };
    private readonly Button _saveConfigButton = new AuroraButton() { Text = "Save Config", AutoSize = true };
    private readonly Button _loadConfigButton = new AuroraButton() { Text = "Load Config", AutoSize = true };
    private readonly Button _checkUpdateButton = new AuroraButton() { Text = "Updates", AutoSize = false, Width = 120 };
    private readonly Button _wallboardButton = new AuroraButton()
    {
        Text = "Wallboard",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(88, 32)
    };
    private readonly Button _settingsButton = new AuroraButton() { Text = "Settings", AutoSize = false, Width = 88 };
    private readonly Button _moreButton = new AuroraButton()
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

    private readonly Button _startButton = new AuroraButton() { Text = "Start Monitoring", AutoSize = true };
    private readonly Button _stopButton = new AuroraButton() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly CheckBox _showCommandView = new()
    {
        Text = "Show ping log",
        AutoSize = true,
        Padding = new Padding(8, 5, 0, 0)
    };
    private readonly Button _clearLogButton = new AuroraButton() { Text = "Clear", AutoSize = true };
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
    private readonly Font _siteItemFont = new("Segoe UI Semibold", 10.5f);
    private readonly Font _statusCellFont = new("Segoe UI", 10.5f, FontStyle.Bold);

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
        if (_persistSites) DataDeletionStore.FinishPending(StoragePaths.Current);
        if (_persistSites && DataBackupStore.RecoverPending(DataDirectory))
            ReportStorageProblem("An interrupted restore was rolled back. The previous saved files were recovered.");

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

            ShowStorageWarning();
            if (_startMonitoringOnLaunch && !_configWriteBlocked) StartMonitoring();

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
            _hostQuickMenu.Close();
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
            _siteActionsMenu.Dispose();
            _columnsMenu.Dispose();
            _siteItemFont.Dispose();
            _siteListTip.Dispose();
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
        AuroraTheme.Changed += RefreshAppearance;
        Disposed += (_, _) => AuroraTheme.Changed -= RefreshAppearance;
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
            int enabled = AuroraTheme.Palette.Dark ? 1 : 0;
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

        if (form._selectedSiteName != null)
            throw new InvalidOperationException("All Hosts must be the default startup view.");
        form._restoreLastSiteOnStartup = true;
        form._selectedSiteName = form._sites[0].Name;
        form.ApplyStartupView();
        if (form._selectedSiteName != form._sites[0].Name)
            throw new InvalidOperationException("Startup site override was ignored.");
        form._restoreLastSiteOnStartup = false;
        form.LoadHostEditor();
        TopologyLayout.RunTests();
        UserPreferenceStore.RunTests();
        SafeConfigStore.RunTests();
        SettingsPersistence.RunTests();
        DataBackupStore.RunTests();
        StoragePaths.RunTests();
        DataDeletionStore.RunTests();
        DeleteDataDialog.RunTests();
        AuroraButton.RunDestructiveContrastTests();
        form._configWriteBlocked = true;
        bool protectedBackupRefused = false;
        try { form.CreateDataBackup(); }
        catch (InvalidOperationException) { protectedBackupRefused = true; }
        finally { form._configWriteBlocked = false; }
        if (!protectedBackupRefused)
            throw new InvalidOperationException("A protected startup configuration was exported as a fallback backup.");
        SettingsForm.RunSaveFailureTests(form.GetAppSettingsSnapshot());
        SupportInfo.RunTests();
        RunSnoozeRegressionTests();
        var snoozedSiteHosts = new[]
        {
            new WallboardHostSnapshot("Snooze health", "192.0.2.1", "", HostState.Online, 1, null),
            new WallboardHostSnapshot("Snooze health", "192.0.2.2", "", HostState.Online, 1, null),
            new WallboardHostSnapshot("Snooze health", "192.0.2.3", "", HostState.Offline, null, DateTime.Now),
            new WallboardHostSnapshot("Snooze health", "192.0.2.4", "", HostState.Offline, null, DateTime.Now, DateTimeOffset.UtcNow.AddHours(1)),
            new WallboardHostSnapshot("Snooze health", "192.0.2.5", "", HostState.Unknown, null, null, DateTimeOffset.UtcNow.AddHours(1))
        };
        if (WallboardCanvas.AggregateSiteState(new WallboardSiteSnapshot("Snooze health", snoozedSiteHosts)) != HostState.Suspect ||
            WallboardCanvas.AggregateSiteState(new WallboardSiteSnapshot("Snooze health", snoozedSiteHosts.Skip(3).ToArray())) != HostState.Unknown)
            throw new InvalidOperationException("Snoozed hosts distorted aggregate site health.");
        int originalSiteCount = form._sites.Count;
        bool newerFormatRejected = false;
        try { form.ApplyConfig(new WatchdogConfig { FormatVersion = 999 }); }
        catch (InvalidDataException) { newerFormatRejected = true; }
        if (!newerFormatRejected || form._sites.Count != originalSiteCount)
            throw new InvalidOperationException("Newer configuration mutated the active sites.");
        DisplayTime.RunTests();
        AuroraTheme.RunTests();
        HostManagerForm.RunDraftTests();
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
        var batch = BuildMonitoringBatch(new[] { ("Site", "127.0.0.1", "Router & exit", 1000) }, 2);
        Check(batch.Contains("goto monitor") && batch.Contains("ping.exe -n 1 -w 1000 \"127.0.0.1\""));
        Check(!batch.Contains("& exit") && !batch.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        bool unsafeTargetRejected = false;
        try { BuildMonitoringBatch(new[] { ("Site", "127.0.0.1 & whoami", "", 1000) }, 2); }
        catch (ArgumentException) { unsafeTargetRejected = true; }
        Check(unsafeTargetRejected);
        string batchFile = Path.Combine(Path.GetTempPath(), "PingWatchdog-" + Guid.NewGuid().ToString("N") + ".bat");
        try
        {
            File.WriteAllText(batchFile, batch, new System.Text.UTF8Encoding(false));
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = "/d /c call \"" + batchFile + "\" --once",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10000);
            if (!exited) process.Kill(true);
            Check(exited && process.ExitCode == 0 && output.GetAwaiter().GetResult().Contains("127.0.0.1"));
        }
        finally { File.Delete(batchFile); }

        Check(UpdateRepoUrl.EndsWith("/JPLuker/Ping-Watchdog", StringComparison.Ordinal));
        Check(form._stateEvents.Any(e => e.Kind == "DOWN"));
        Check(form._stateEvents.Any(e => e.Kind == "RECOVERED"));

        var historyNow = DateTime.Now;
        var historySample = new[]
        {
            new StateEventRecord(historyNow.AddHours(-1), "A", "1", "one", "DOWN", "down"),
            new StateEventRecord(historyNow.AddMinutes(-30), "A", "1", "one", "RECOVERED", "up"),
            new StateEventRecord(historyNow.AddMinutes(-10), "A", "2", "two", "Suspect", "suspect"),
            new StateEventRecord(historyNow.AddHours(-30), "B", "3", "three", "DOWN", "old")
        };
        var filtered24h = FilterStateEvents(historySample, 24, hideSuspects: true, historyNow);
        Check(filtered24h.Count == 2);
        Check(filtered24h.All(e => e.Kind != "Suspect"));
        Check(filtered24h.All(e => e.Timestamp >= historyNow.AddHours(-24)));
        Check(FilterStateEvents(historySample, 0, hideSuspects: false, historyNow).Count == 4);
        Check(GetEventHistoryWindowLabel(168) == "Last 7 days");

        using (var captionBitmap = new Bitmap(800, 300))
        using (var captionGraphics = Graphics.FromImage(captionBitmap))
        using (var labelFont = new Font("Segoe UI", 10.5f))
        using (var addressFont = new Font("Consolas", 9.75f))
        {
            foreach (string address in new[] { "10.235.80.60", "173.161.54.241", "255.255.255.255", "2001:db8:abcd:1234:5678:90ab:cdef:1234" })
            {
                var size = WallboardCanvas.MeasureHostNodeCaption(captionGraphics, "Printer", address, labelFont, addressFont);
                using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
                var required = captionGraphics.MeasureString(address, addressFont, int.MaxValue, format);
                Check(size.Width - 10 >= required.Width);
                Check(size.Height >= required.Height + 6);
            }
        }

        // Many sites must not pile up: headings and host cells never collide, every host stays reachable.
        foreach (var (siteCount, viewport, expectHeadings) in new[]
        {
            (24, new Size(1600, 1000), true),
            (24, new Size(900, 650), true),
            (60, new Size(900, 650), false)
        })
        {
            var denseSites = Enumerable.Range(0, siteCount).Select(s => new WallboardSiteSnapshot(
                $"Site {s:00} " + (s % 5 == 0 ? "with a very long branch office name" : ""),
                Enumerable.Range(0, 2 + s % 4).Select(h => new WallboardHostSnapshot(
                    $"Site {s:00}",
                    h == 0 && s % 3 == 0 ? $"2001:db8:abcd:{s:x4}:5678:90ab:cdef:{h:x4}" : $"10.{s}.0.{h + 1}",
                    h % 2 == 0 ? "" : "Switch",
                    HostState.Online, 1, null)).ToArray())).ToArray();
            using var dense = new WallboardCanvas { Size = viewport, ShowCli = false,
                Snapshot = new WallboardSnapshot(true, DateTime.Now, denseSites,
                    Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var denseBitmap = new Bitmap(viewport.Width, viewport.Height);
            dense.DrawToBitmap(denseBitmap, new Rectangle(Point.Empty, viewport));
            int denseHosts = denseSites.Sum(site => site.Hosts.Count);
            Check(dense.AllHostNodesFit && dense.VisibleHostCount == denseHosts, $"{siteCount} sites at {viewport} lost hosts.");
            Check(!dense.TopologyRegionsOverlap(), $"{siteCount} sites at {viewport} overlap.");
            if (expectHeadings)
                Check(dense.DrawnSiteHeadings.Count == siteCount, $"{siteCount} sites at {viewport} dropped site names.");
        }

        Check(EventHistoryForm.KindFont.SizeInPoints >= AuroraTheme.CaptionFont.SizeInPoints, "History event type text is below the reading floor.");

        var snoozeNow = DateTimeOffset.UtcNow;
        var snoozeOptions = new HostOptions { SnoozedUntilUtc = snoozeNow.AddHours(1) };
        Check(snoozeOptions.IsSnoozed(snoozeNow));
        Check(!snoozeOptions.IsSnoozed(snoozeNow.AddHours(1)));
        Check(!snoozeOptions.IsSnoozed(snoozeNow.AddHours(2)));
        var snoozeRoundTrip = JsonSerializer.Deserialize<HostOptions>(JsonSerializer.Serialize(snoozeOptions))!;
        Check(snoozeRoundTrip.SnoozedUntilUtc == snoozeOptions.SnoozedUntilUtc);
        Check(snoozeOptions.Copy().IsSnoozed(snoozeNow));
        snoozeOptions.SnoozedUntilUtc = null;
        Check(!snoozeOptions.IsSnoozed(snoozeNow));

        foreach (var viewport in new[] { new Size(1600, 1000), new Size(900, 650) })
        {
            var hitHosts = Enumerable.Range(1, 8).Select(i =>
                new WallboardHostSnapshot("Hit test", $"192.0.2.{i}", $"Host {i}", HostState.Online, 1, null)).ToArray();
            using var canvas = new WallboardCanvas { Size = viewport, ShowCli = false,
                Snapshot = new WallboardSnapshot(true, DateTime.Now,
                    new[] { new WallboardSiteSnapshot("Hit test", hitHosts) },
                    Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var bitmap = new Bitmap(viewport.Width, viewport.Height);
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, viewport));
            var found = new HashSet<string>();
            // Every host must fit and remain reachable at narrow sizes.
            Check(canvas.AllHostNodesFit && canvas.VisibleHostCount == hitHosts.Length);
            for (int y = 0; y < viewport.Height; y += 4)
                for (int x = 0; x < viewport.Width; x += 4)
                    if (canvas.HitTestHost(new Point(x, y)) is { } hit) found.Add(hit.Address);
            Check(found.Count == hitHosts.Length);
            Check(canvas.HitTestHost(Point.Empty) == null);
            var accessible = canvas.AccessibilityObject;
            Check(accessible.GetChildCount() == hitHosts.Length);
            Check(accessible.GetChild(0)!.Name!.Contains("192.0.2.1") && accessible.GetChild(0)!.Name!.Contains("Online"));
            WallboardHostSnapshot? actionHost = null;
            canvas.HostRightClicked += (target, _) => actionHost = target;
            canvas.HandleHostKey(Keys.Home);
            canvas.HandleHostKey(Keys.Right);
            Check(canvas.SelectedHost?.Address == "192.0.2.2");
            canvas.HandleHostKey(Keys.Shift | Keys.F10);
            Check(actionHost?.Address == "192.0.2.2");
            accessible.GetChild(7)!.DoDefaultAction();
            Check(actionHost?.Address == "192.0.2.8");
            var removedChild = accessible.GetChild(7)!;
            actionHost = null;
            canvas.Snapshot = canvas.Snapshot! with { Sites = new[] {
                new WallboardSiteSnapshot("Hit test", hitHosts.Take(7).ToArray()) } };
            canvas.HandleHostKey(Keys.Enter);
            Check(actionHost == null); // A stale selection must not target a removed host.
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, viewport));
            Check(accessible.GetChildCount() == 7);
            removedChild.DoDefaultAction();
            Check(actionHost == null && removedChild.State.HasFlag(AccessibleStates.Unavailable));
        }

        using (var outageCanvas = new WallboardCanvas { Size = new Size(1600, 1000), ShowCli = false })
        using (var outageBitmap = new Bitmap(1600, 1000))
        {
            var active = new WallboardHostSnapshot("Outage site", "192.0.2.1", "Router", HostState.Offline, null, DateTime.Now.AddMinutes(-5));
            var recovered = new WallboardHostSnapshot("Outage site", "192.0.2.2", "Printer", HostState.Online, 1, null);
            outageCanvas.Snapshot = new WallboardSnapshot(true, DateTime.Now,
                new[] { new WallboardSiteSnapshot("Outage site", new[] { active, recovered }) },
                new[] { new WallboardEventSnapshot(DateTime.Now, recovered.Site, "Printer (192.0.2.2)", "RECOVERED", "", recovered.Address) },
                Array.Empty<WallboardCommandSnapshot>(), "24 hours", true);
            outageCanvas.DrawToBitmap(outageBitmap, new Rectangle(0, 0, 1600, 1000));
            Check(outageCanvas.CurrentOutageHosts.Select(host => host.Address).SequenceEqual(new[] { active.Address }));
            WallboardHostSnapshot? outageAction = null;
            outageCanvas.HostRightClicked += (target, _) => outageAction = target;
            outageCanvas.HandleHostKey(Keys.End);
            outageCanvas.HandleHostKey(Keys.Apps);
            Check(outageAction?.Address == active.Address);
            Check(outageCanvas.AccessibilityObject.GetSelected()!.Name!.Contains("Active outage"));
            outageCanvas.Snapshot = outageCanvas.Snapshot! with { Sites = new[] {
                new WallboardSiteSnapshot("Outage site", new[] { active with { SnoozedUntilUtc = DateTimeOffset.UtcNow.AddHours(1) }, recovered }) } };
            outageCanvas.DrawToBitmap(outageBitmap, new Rectangle(0, 0, 1600, 1000));
            Check(outageCanvas.CurrentOutageHosts.Count == 0);
            Check(outageCanvas.AccessibilityObject.GetChild(0)!.Name!.Contains("Snoozed"));
            outageCanvas.Snapshot = outageCanvas.Snapshot with { Sites = new[] {
                new WallboardSiteSnapshot("Outage site", new[] { active with { State = HostState.Online, OutageStarted = null }, recovered }) } };
            outageCanvas.DrawToBitmap(outageBitmap, new Rectangle(0, 0, 1600, 1000));
            Check(outageCanvas.CurrentOutageHosts.Count == 0);

        }

        foreach (var size in new[] { new Size(900, 650), new Size(1600, 1000), new Size(1920, 1080) })
        foreach (bool showCli in new[] { false, true })
        {
            WallboardHostSnapshot[] Branch(string site, int count) => Enumerable.Range(0, count).Select(i =>
                new WallboardHostSnapshot(site, $"192.0.2.{i + 1}", i == 3 ? "Cloudkey" : "Host " + i,
                    HostState.Online, 1, null)).ToArray();
            using var canvas = new WallboardCanvas { Size = size, ShowCli = showCli,
                Snapshot = new WallboardSnapshot(true, DateTime.Now, new[]
                {
                    new WallboardSiteSnapshot("Hammond Munster", Branch("Hammond Munster", 8)),
                    new WallboardSiteSnapshot("Gary", Branch("Gary", 6)),
                    new WallboardSiteSnapshot("East Chicago", Branch("East Chicago", 6)),
                    new WallboardSiteSnapshot("Anderson", Branch("Anderson", 1))
                }, Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var bitmap = new Bitmap(size.Width, size.Height);
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            Check(canvas.HasHostCaption("East Chicago", "192.0.2.4"));
            Check(!canvas.CaptionsOverlap());
            Check(canvas.AllHostNodesFit && canvas.VisibleHostCount == canvas.Snapshot!.Sites.Sum(site => site.Hosts.Count));
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
        Check(WallboardCanvas.CalculateTopologyRenderScale(new Rectangle(0, 0, 820, 300), 3, 25) < 0.80);
        Check(WallboardCanvas.CalculateTopologyRenderScale(new Rectangle(0, 0, 1400, 700), 2, 10) == 1.0);

        form.ClientSize = new Size(960, 640);
        form.ApplyResponsiveLayout();
        Check(form._workspaceSplit.SplitterDistance <= 240);
        Check(form._rootLayout?.RowStyles[0].Height == 0);
        Check(form._settingsFlowPanel?.WrapContents == true);

        form.ClientSize = new Size(1320, 840);
        form.ApplyResponsiveLayout();
        Check(form._workspaceSplit.SplitterDistance >= 224);






        Check(form._checkUpdateButton.Parent is not null);
        Check(form._wallboardButton.Parent is not null);
        Check(form._settingsButton.Parent is not null);
        Check(form._moreButton.Parent is not null);
        Check(form._headerActionsPanel?.Controls.Contains(form._checkUpdateButton) == true);
        Check(form._headerActionsPanel?.Controls.Contains(form._settingsButton) == true);
        Check(form._moreButton.Text == "More");
        Check(form._moreButton.Width >= 48);
        Check(form._moreButton.Padding.Left >= 12);
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
        Check(form._checkUpdateButton.Text == "Check for updates");
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

        async Task CheckHostMenu(Control surface)
        {
            var host = form.HostSnapshot().First();
            for (int i = 0; i < 3; i++)
            {
                form.ShowHostQuickActions(surface, new Point(20, 20), host.Site, host.Address);
                Check(form._hostQuickMenu.Visible, "Host menu did not open.");
                Check(!form._hostQuickMenu.ShowImageMargin && !form._hostQuickMenu.ShowCheckMargin, "Empty menu icon gutter returned.");
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.AppClicked);
                Check(!form._hostQuickMenu.IsDisposed && form._hostQuickMenu.IsHandleCreated,
                    "Closing the host menu disposed it during WinForms close processing.");
                await Task.Delay(50);
            }
            foreach (string action in new[] { "Snooze host for 1 hour", "Resume monitoring now" })
            {
                form.ShowHostQuickActions(surface, new Point(20, 20), host.Site, host.Address);
                form._hostQuickMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == action).PerformClick();
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
                await Task.Delay(50);
                Check(!form._hostQuickMenu.IsDisposed, "A quick action destroyed the host menu.");
                Check(form.HostSnapshot().Single(h => h.Options.Id == host.Options.Id).Options.IsSnoozed(DateTimeOffset.UtcNow)
                    == (action == "Snooze host for 1 hour"), "Host snooze action did not apply.");
            }
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
                    Check(form.Controls.Find("WatchdogBrandLogo", true).Length == 0,
                        "Repeated branding returned to the workspace.");
                    Check(form._siteHeaderLabel.Parent!.ClientRectangle.Contains(form._siteHeaderLabel.Bounds),
                        "Selected-site heading is clipped.");
                    Check(form._startButton.Parent!.ClientRectangle.Contains(form._startButton.Bounds),
                        "Monitoring action is clipped.");
                    var actions = form._headerActionsPanel!;
                    Check(actions.ClientRectangle.Contains(form._wallboardButton.Bounds),
                        "Wallboard navigation is clipped.");
                    foreach (Control setting in form._settingsFlowPanel!.Controls.Cast<Control>().Where(control => control.Visible))
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

                using (var scoped = new MainForm(false, true, false))
                {
                    scoped.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, Sites = new()
                    {
                        new SiteDefinition { Name = "Site A", Hosts = new() { "127.0.0.1" } },
                        new SiteDefinition { Name = "Site B", Hosts = new() { "127.0.0.2" } }
                    } });
                    scoped.WallboardSelectSite("Site A");
                    scoped.StartMonitoring();
                    await Task.Delay(150);
                    Check(scoped._hosts.Count == 1 && scoped._hosts.Values.Single().Site == "Site A", "Start ignored site scope.");
                    var oldToken = scoped._hostTokens.Values.Single().Token;
                    scoped._siteList.SelectedIndex = scoped._siteList.Items.Cast<SiteListItem>().ToList().FindIndex(x => x.SiteName == "Site B");
                    await Task.Delay(150);
                    Check(oldToken.IsCancellationRequested && scoped._hosts.Count == 1 && scoped._hosts.Values.Single().Site == "Site B", "Site change kept an old worker.");
                    Check(scoped.BuildWallboardSnapshot().Sites.All(x => x.Name == "Site B"), "Wallboard ignored scope.");
                    scoped.WallboardSelectSite(null);
                    Check(scoped._hosts.Count == 2, "All sites did not restore both targets.");
                    scoped.StopMonitoring();
                    Check(scoped._hostTokens.Count == 0, "Stop left workers active.");
                }

                using (var sorted = new MainForm(false, true, false))
                {
                    string longName = "Northside regional distribution warehouse loading dock " + new string('x', 60);
                    sorted.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, Sites = new()
                    {
                        new SiteDefinition { Name = "Zulu", Hosts = new() { "192.0.2.10" } },
                        new SiteDefinition { Name = "alpha", Hosts = new() { "192.0.2.11" } },
                        new SiteDefinition { Name = longName, Hosts = new() { "192.0.2.12" } },
                        new SiteDefinition { Name = "Mike", Hosts = new() { "192.0.2.13" } }
                    } });
                    var mapOrder = sorted.BuildWallboardSnapshot().Sites.Select(x => x.Name).ToArray();
                    Check(mapOrder.SequenceEqual(new[] { "alpha", "Mike", longName, "Zulu" }, StringComparer.Ordinal), "Map sites are not alphabetical.");
                    var listOrder = sorted._siteList.Items.Cast<SiteListItem>().Skip(1).Select(x => x.SiteName).ToArray();
                    Check(listOrder.SequenceEqual(mapOrder, StringComparer.Ordinal), "Map and site list order differ.");
                    int longIndex = sorted._siteList.Items.Cast<SiteListItem>().ToList().FindIndex(x => x.SiteName == longName);
                    Check(sorted.IsSiteNameTruncated(longIndex), "A truncated site name has no full-name tooltip.");
                    if (sorted._siteList.ClientSize.Width > 150)
                        Check(!sorted.IsSiteNameTruncated(longIndex - 1), "A short site name was treated as truncated.");
                }
                form.SetWorkspaceView(true);
                WindowsBranding.RunShortcutSmokeTest(output);
                form.ImportManagedHosts(new() { new(form._sites[0].Name, "192.0.2.254", "Menu test", "", new HostOptions(), "Unknown") }, false);
                Check(form._grid.Rows.Count > 0, "Saved hosts are absent while idle.");
                Check(form._grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible) <= 5,
                    "Default results expose too many columns.");
                form._hostSearch.Text = "does-not-match";
                Check(form._grid.Rows.Count == 0 && form.HostSnapshot().Count == 1, "Search changed the saved targets.");
                form._hostSearch.Clear();
                Check(form._grid.Rows.Count == 1, "Clearing search did not restore the host.");
                await CheckHostMenu(form._grid);

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

                Check(form._rightLayout!.RowStyles[1].Height == 0, "Duplicate inventory space returned.");
                Capture(form, "single-host-table");
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
                form._settingsForm!.RunLayoutChecks();
                Capture(form._settingsForm!, "settings");
                form._settingsForm!.Close();

                using (var review = new MainForm(false, true, false))
                {
                    review._minimizeToTray = false;
                    review.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, ShowCommandView = true,
                        Sites = Enumerable.Range(1, 5).Select(site => new SiteDefinition {
                            Name = "Branch " + site,
                            Hosts = Enumerable.Range(1, 5).Select(host => $"192.0.2.{site * 10 + host}").ToList(),
                            Labels = new() { [$"192.0.2.{site * 10 + 2}"] = "Printer", [$"192.0.2.{site * 10 + 3}"] = "Cloudkey" }
                        }).ToList() });
                    review.MaximumSize = new Size(1920, 1080);
                    review.Show(form);
                    review.SetWorkspaceView(true);
                    foreach (var appearance in new[] { AppearanceMode.Dark, AppearanceMode.Light })
                    {
                        AuroraTheme.SetMode(appearance);
                        review.ClientSize = new Size(1060, 680);
                        review.ApplyResponsiveLayout();
                        await Task.Delay(150);
                        Check(review._grid.Rows.Count == 25, "Populated workspace lost hosts.");
                        Check(review._grid.Columns["HostColumn"].DisplayIndex == 0, "Address must be the first column.");
                        Check(review._grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width) < review._grid.ClientSize.Width,
                            "Default host columns overflow beside ping output.");
                        Check(review._mainSplit.Panel2.Width <= review._mainSplit.Width / 3 + 4,
                            "Ping log consumed the host table width.");
                        Check(review._mainSplit.Panel1.Width > review._mainSplit.Panel2.Width * 1.8,
                            "Host results lost priority over the log.");
                        Capture(review, "workspace-list-" + appearance);
                        review.SetWorkspaceView(false);
                        await Task.Delay(100);
                        Check(review._mainSplit.Panel1Collapsed && !review._dashboardSplit.Panel1Collapsed,
                            "Map and list are displayed together.");
                        Check(review._statusStrip.Parent == null, "Redundant bottom status bar returned.");
                        Capture(review, "workspace-populated-" + appearance);
                        Check(review._monitorCanvas.AllHostNodesFit && review._monitorCanvas.VisibleHostCount == 25,
                            "The selected topology does not fit every node.");
                        review.SetUpdateStatus("Up to date", true);
                        Check(review._checkUpdateButton.Text == "Check for updates", "Update action label changed into status.");
                        Check(review._updateToast?.Visible == true && review._updateToast.Right <= review.ClientSize.Width,
                            "Internal update status is missing or outside the application.");
                        await Task.Delay(200);
                        review._updateToast!.Update();
                        Check(review.Controls.GetChildIndex(review._updateToast) == 0,
                            "Update status is behind the main workspace.");
                        using (var popup = new Bitmap(review._updateToast.Width, review._updateToast.Height))
                        {
                            review._updateToast.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
                            popup.Save(Path.Combine(output, "update-popup-" + appearance + ".png"));
                        }
                        Capture(review, "update-status-" + appearance);
                        review._updateToast!.Visible = false;
                        review.SetWorkspaceView(true);
                    }
                    review.Close();
                }

                form.OpenWallboard();
                await Task.Delay(200);
                Check(form._wallboardForm?.Visible == true, "Wallboard did not open.");
                var canvas = (WallboardCanvas)form._wallboardForm!.Controls.Find("WallboardCanvas", true).Single();
                foreach (int width in new[] { 900, 1320 })
                {
                    form._wallboardForm.ClientSize = new Size(width, 900);
                    await Task.Delay(100);
                    form._wallboardForm.PerformLayout();
                    if (width == 900) Capture(form._wallboardForm, "wallboard-narrow");
                    Check(form._wallboardForm.Controls.Find("WallboardToolbar", true).Length == 0,
                        "Wallboard top controls returned.");
                    Check(canvas.Top == 0 && canvas.Height == form._wallboardForm.ClientSize.Height,
                        $"Wallboard canvas does not fill the available window: canvas={canvas.Bounds}, parent={canvas.Parent?.ClientRectangle}, window={form._wallboardForm.ClientRectangle}.");
                }
                form.OpenHostManager(form._wallboardForm);
                await Task.Delay(100);
                Check(form._hostManager?.Visible == true && form._hostManager.Owner == form._wallboardForm,
                    "Wallboard must open the shared Hosts manager.");
                form._hostManager!.Close();
                await CheckHostMenu(canvas);
                canvas.Focus();
                canvas.HandleHostKey(Keys.Home);
                canvas.HandleHostKey(Keys.Shift | Keys.F10);
                Check(form._hostQuickMenu.Visible, "Keyboard host action did not open the menu.");
                Check(form._hostQuickMenu.Items[0].Text.Contains(canvas.SelectedHost!.Address),
                    "Keyboard host menu targeted a different host.");
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                canvas.AccessibilityObject.GetChild(0)!.DoDefaultAction();
                Check(form._hostQuickMenu.Visible, "Accessible host action did not open the menu.");
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                Capture(form._wallboardForm!, "wallboard");
                canvas.Snapshot = new WallboardSnapshot(true, DateTime.Now,
                    Enumerable.Range(1, 4).Select(site => new WallboardSiteSnapshot("Branch " + site,
                        Enumerable.Range(1, site == 4 ? 1 : 8).Select(host => new WallboardHostSnapshot(
                            "Branch " + site, $"192.0.2.{site * 20 + host}", host == 4 ? "Cloudkey controller" : "Device " + host,
                            host == 2 ? HostState.Offline : HostState.Online, 12, host == 2 ? DateTime.Now.AddMinutes(-4) : null)).ToArray())).ToArray(),
                    Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "Last 24 hours", true);
                using (var dense = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(dense, canvas.ClientRectangle);
                    dense.Save(Path.Combine(output, "wallboard-dense.png"));
                    Check(!canvas.CaptionsOverlap(), "Dense Wallboard captions overlap.");
                }

                form._wallboardForm!.Close();
                await Task.Delay(100);
                Check(form.Visible, "Closing Wallboard did not restore the main window.");
                var appearanceBefore = AuroraTheme.Mode;
                foreach (var appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                {
                    AuroraTheme.SetMode(appearance);
                    form.OpenSettings();
                    form.OpenHostManager();
                    await Task.Delay(100);
                    Check(form.BackColor == AuroraTheme.Palette.Canvas, "Main appearance did not update.");
                    Check(form._settingsForm!.BackColor == AuroraTheme.Palette.Canvas, "Open Settings appearance did not update.");
                    Check(form._hostManager!.BackColor == AuroraTheme.Palette.Canvas, "Open Hosts appearance did not update.");
                    Capture(form, "appearance-main-" + appearance);
                    Capture(form._settingsForm!, "appearance-settings-" + appearance);
                    Capture(form._hostManager!, "appearance-hosts-" + appearance);
                    await SettingsForm.CaptureSaveFailureReview(form.GetAppSettingsSnapshot(), form._settingsForm!,
                        Path.Combine(output, "settings-save-error-" + appearance + ".png"));
                    await form._settingsForm!.CaptureAboutReview(output, appearance.ToString());
                    await form._settingsForm!.CaptureDataReview(output, appearance.ToString());
                    form._settingsForm!.Close(); form._hostManager!.Close();
                }
                AuroraTheme.SetMode(appearanceBefore);
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
        _appMenu.Items.Add("Export monitoring batch…", null, (_, _) => ExportMonitoringBatch());
        _appMenu.Items.Add("Quick monitor…", null, (_, _) => OpenQuickAdd(true));
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
        OpenSettings();
        _settingsForm?.SelectCategory("About");
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
        foreach (DataGridViewColumn column in _grid.Columns)
            column.Visible = column.Name is "SiteColumn" or "HostColumn" or "LabelColumn" or "StatusColumn" or "LatencyColumn";
        _grid.Columns["HostColumn"].HeaderText = "Address";
        _grid.Columns["LabelColumn"].HeaderText = "Name";
        _grid.Columns["HostColumn"].DisplayIndex = 0;
        _grid.Columns["LabelColumn"].DisplayIndex = 1;
        _grid.Columns["HostColumn"].DefaultCellStyle.Font = new Font("Consolas", 11.25f);
        _grid.Columns["SiteColumn"].DisplayIndex = 2;
        _grid.Columns["LatencyColumn"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (var name in new[] { "Host", "Label", "Site", "Status", "Latency" })
        {
            var column = _grid.Columns[name + "Column"];
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.MinimumWidth = name switch { "Host" => 140, "Label" => 96, "Site" => 80, "Status" => 76, _ => 64 };
            column.FillWeight = name switch { "Host" => 30, "Label" => 24, "Site" => 19, "Status" => 16, _ => 13 };
        }
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenHostManager(); };

    }

    private void DrawSiteItem(object? sender, DrawItemEventArgs e)
    {
        using (var canvas = new SolidBrush(AuroraTheme.Palette.Surface)) e.Graphics.FillRectangle(canvas, e.Bounds);

        if (e.Index < 0 || e.Index >= _siteList.Items.Count)
            return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = new Rectangle(4, e.Bounds.Y + 4, Math.Max(1, _siteList.ClientSize.Width - 8), Math.Max(1, e.Bounds.Height - 8));

        using var background = new SolidBrush(selected
            ? AuroraTheme.Palette.Surface3
            : AuroraTheme.Palette.Surface);
        using var textBrush = new SolidBrush(selected
            ? AuroraTheme.Palette.Text
            : AuroraTheme.Palette.Secondary);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var rowPath = WallboardStyle.Rounded(bounds, AuroraTheme.RadiusMedium);
        e.Graphics.FillPath(background, rowPath);

        string text = _siteList.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            _siteItemFont,
            new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 18, bounds.Height),
            textBrush.Color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues)
        {
            using var focus = new Pen(AuroraTheme.Palette.Accent, 2);
            e.Graphics.DrawPath(focus, rowPath);
        }
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
                var autoConfig = SafeConfigStore.Read(_autoConfigPath);

                if (autoConfig is not null)
                {
                    ApplyConfig(autoConfig);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ReportStorageProblem("The saved configuration could not be opened. Autosave is off for this session; the original file has been kept. Update Ping Watchdog or restore a backup.\r\n\r\n" + ex.Message,
                protectOriginal: true);
            return;
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
        catch (Exception ex)
        {
            ReportStorageProblem("The saved site list could not be opened. Autosave is off for this session; the original file has been kept. Restore a backup or export a new configuration to another location.\r\n\r\n" + ex.Message,
                protectOriginal: true);
            return;
        }

        try
        {
            if (!File.Exists(_bundledDefaultConfigPath))
                return;

            var defaultConfig = SafeConfigStore.Read(_bundledDefaultConfigPath);

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
        if (!_persistSites || _configWriteBlocked || _restoreRequiresRestart)
            return;

        try
        {
            // The full autosave is authoritative; the legacy inventory is secondary.
            SafeConfigStore.Write(_autoConfigPath, BuildConfig(captureEditor: false));
            SafeConfigStore.WriteAtomic(_settingsPath, JsonSerializer.Serialize(
                _sites.Where(site => !site.Temporary).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
            _storageWarning = null;
            _storageWarningShown = false;
        }
        catch (Exception ex)
        {
            ReportStorageProblem("Changes could not be saved. Keep this window open and export the configuration to another location before closing.\r\n\r\n" + ex.Message);
        }
    }

    private void RefreshSiteList(string? selectedName)
    {
        _ignoreSiteSelection = true;

        try
        {
            _siteList.Items.Clear();

            int totalHosts = _sites.Sum(site =>
                site.Hosts.Count(address => GetHostOptions(site, address).Enabled));
            _siteList.Items.Add(new SiteListItem(
                null,
                "All sites"));

            foreach (var site in _sites
                .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase))
            {
                _siteList.Items.Add(new SiteListItem(
                    site.Name,
                    FormatSiteDisplayPath(site)));
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
        _selectedSiteName = (_siteList.SelectedItem as SiteListItem)?.SiteName;
        ReconcileMonitoringWithConfig();

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
            _siteHeaderLabel.Text = "All hosts";
            _ipBox.ReadOnly = true;
            _ipBox.Text = string.Join(
                Environment.NewLine,
                _sites.SelectMany(site =>
                    site.Hosts.Select(host => $"[{site.Name}] {host}")));
            return;
        }

        var site = FindSite(_selectedSiteName);

        _siteHeaderLabel.Text = $"{_selectedSiteName}";
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

        _gridMenu.Opening += (_, e) => e.Cancel = true;
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
        var identity = GetSelectedHostIdentity();
        if (identity != null)
            ShowHostQuickActions(_grid, _grid.PointToClient(Cursor.Position), identity.Value.Site, identity.Value.Host);
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
            SafeConfigStore.Write(dialog.FileName, config);

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
            var config = SafeConfigStore.Read(dialog.FileName);

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
        SafeConfigStore.EnsureSupported(config.FormatVersion);
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
        // UI/developer preferences are machine-user preferences, not scan-definition settings.
        // Re-apply the durable preference after any autosave/default/imported config is loaded.
        LoadUserPreferences();

        _selectedSiteName = config.SelectedSite is not null &&
            _sites.Any(s => s.Name.Equals(config.SelectedSite, StringComparison.OrdinalIgnoreCase))
            ? _sites.First(s => s.Name.Equals(config.SelectedSite, StringComparison.OrdinalIgnoreCase)).Name
            : null;

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

        var targets = GetConfiguredTargets(includeSnoozed: true);

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
        _startButton.Enabled = true;
        _stopButton.Enabled = true;
        _trayStopItem.Enabled = true;

        _cts = new CancellationTokenSource();

        foreach (var target in targets)
        {
            if (GetHostOptions(FindSite(target.Site), target.Address).IsSnoozed(DateTimeOffset.UtcNow)) continue;
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
        int monitoredSiteCount = targets.Select(target => target.Site)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        _statusLabel.Text = $"Monitoring {targets.Count} host(s) across {monitoredSiteCount} site(s)";
        RefreshGrid();
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
            RefreshGrid();
        }
    }

    private List<(string Site, string Address)> GetConfiguredTargets(bool includeSnoozed = false)
    {
        return _sites
            .Where(site => _selectedSiteName is null || site.Name.Equals(_selectedSiteName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(site =>
                site.Hosts.Where(host => GetHostOptions(site, host).Enabled &&
                    (includeSnoozed || !GetHostOptions(site, host).IsSnoozed(DateTimeOffset.UtcNow))).Select(host => (Site: site.Name, Address: host)))
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

        int monitoredSiteCount = _hosts.Values
            .Select(host => host.Site)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        _statusLabel.Text = $"Monitoring {_hosts.Count} host(s) across {monitoredSiteCount} site(s)...";
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
            if (!IsHostProbeAllowed(host.Site, host.Address)) break;
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

            if (token.IsCancellationRequested || IsDisposed || Disposing ||
                !IsHostProbeAllowed(host.Site, host.Address))
            {
                break;
            }

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
        // Unit/self-tests exercise the state machine without a live session. During
        // real monitoring, however, a disabled host is quarantined even if a probe
        // completed concurrently with the Disable action.
        if (_cts is not null && !IsHostProbeAllowed(host.Site, host.Address))
            return;

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
                        eventKind = "Suspect";
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
            !host.Options.IsSnoozed(DateTimeOffset.UtcNow) &&
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
        if (_cts is not null && !IsHostProbeAllowed(site, host))
            return;

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
        if (!_persistSites || _restoreRequiresRestart)
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
                !e.Kind.Equals("Suspect", StringComparison.OrdinalIgnoreCase))
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

    private bool IsHostEnabled(string siteName, string address)
    {
        var site = FindSite(siteName);
        return site is not null &&
            site.Hosts.Any(host => host.Equals(address, StringComparison.OrdinalIgnoreCase)) &&
            GetHostOptions(site, address).Enabled;
    }

    private bool IsHostProbeAllowed(string siteName, string address) =>
        IsHostEnabled(siteName, address) && !GetHostOptions(FindSite(siteName), address).IsSnoozed(DateTimeOffset.UtcNow);

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
                        host.OutageStarted,
                        host.Options.SnoozedUntilUtc);
                }
            }
        }

        // Same order as the site list: folder, then name.
        var sites = _sites
            .Where(site => _selectedSiteName is null || site.Name.Equals(_selectedSiteName, StringComparison.OrdinalIgnoreCase))
            .Where(site => site.Hosts.Any(address => GetHostOptions(site, address).Enabled))
            .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .Select(site =>
            {
                var hosts = site.Hosts
                    .Where(address => GetHostOptions(site, address).Enabled)
                    .Select(address =>
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
                        null,
                        GetHostOptions(site, address).SnoozedUntilUtc);
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
                .Where(e => IsHostEnabled(e.Site, e.Host) && (_selectedSiteName is null || e.Site.Equals(_selectedSiteName, StringComparison.OrdinalIgnoreCase)))
                .TakeLast(80)
                .Select(e => new WallboardEventSnapshot(
                    e.Timestamp,
                    e.Site,
                    e.DisplayHost,
                    e.Kind,
                    e.Message,
                    e.Host))
                .ToList();
        }

        var commands = _commandEntries
            .Where(entry => IsHostProbeAllowed(entry.Site, entry.Host) && (_selectedSiteName is null || entry.Site.Equals(_selectedSiteName, StringComparison.OrdinalIgnoreCase)))
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
        var configuredHosts = selectedSites
            .SelectMany(site => site.Hosts.Select(address =>
                new WallboardConfiguredHost(site.Name, address)))
            .ToList();

        foreach (var site in selectedSites)
        {
            foreach (var address in site.Hosts)
            {
                if (!GetHostOptions(site, address).Enabled)
                    continue;

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
            configuredHosts,
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
                ClearCommandLog,
                () => OpenHostManager(_wallboardForm)),
            target,
            CycleEventHistoryWindow,
            ToggleSuspectHistory,
            _wallboardShowCli,
            ShowHostQuickActions);

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
            GetUpdateActionText(),
            _restoreLastSiteOnStartup,
            _startMonitoringOnLaunch,
            AuroraTheme.Mode);
    }

    private string GetUpdateActionText()
    {
        return _pendingUpdateManager is not null && _pendingUpdateInfo is not null
            ? "Restart to Update"
            : "Check for Updates";
    }

    private string GetUpdateStatusText() => _updateStatusText;

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
        PersistAppSettings(settings);
        bool wasAutoCheck = _autoCheckUpdates;

        _autoCheckUpdates = settings.AutoCheckUpdates;
        _minimizeToTray = settings.MinimizeToTray;
        _notificationsEnabled = settings.NotificationsEnabled;
        _wallboardShowCli = settings.WallboardShowCli;
        _showUpdateControlOnHome = settings.ShowUpdateControlOnHome;
        _restoreLastSiteOnStartup = settings.RestoreLastSiteOnStartup;
        _startMonitoringOnLaunch = settings.StartMonitoringOnLaunch;
        AuroraTheme.SetMode(settings.Appearance);
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
            () => OpenEventHistory(owner), ExportDataBackup, RestoreDataBackup, OpenDataFolder, EnablePortableStorage, DeleteSavedData, CanEnablePortableStorage);

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
        SetUpdateStatus($"Version {_pendingUpdateVersion} is downloaded and ready.");
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
            SetUpdateStatus("Restarting…", true, true);
            _trayUpdateItem.Enabled = false;
            _statusLabel.Text = $"Applying update {version}...";

            StartupRecovery.ExpectUpdateExit();
            _pendingUpdateManager.ApplyUpdatesAndRestart(_pendingUpdateInfo);
        }
        catch (Exception ex)
        {
            StartupRecovery.CancelExpectedExit();
            _checkUpdateButton.Enabled = true;
            SetUpdateStatus("Update downloaded and ready.");
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
        if ((!userInitiated && !_autoCheckUpdates) || IsDisposed || Disposing) return;
        if (_pendingUpdateManager != null && _pendingUpdateInfo != null)
        {
            SetUpdateStatus($"Version {_pendingUpdateVersion} is downloaded and ready.", userInitiated, true);
            return;
        }
        if (_updateCheckInProgress) return;
        _updateCheckInProgress = true;
        _checkUpdateButton.Enabled = false;
        SetUpdateStatus("Checking for updates…", userInitiated, true);
        try
        {
            var manager = new UpdateManager(new GithubSource(UpdateRepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
            {
                SetUpdateStatus("Install the current Setup or Portable release to enable automatic updates.", userInitiated, true);
                return;
            }
            var update = await manager.CheckForUpdatesAsync();
            if (update == null)
            {
                ClearPendingUpdateUi();
                SetUpdateStatus("Up to date", userInitiated);
                return;
            }
            string version = update.TargetFullRelease.Version.ToString();
            SetUpdateStatus($"Downloading version {version}…", userInitiated, true);
            await manager.DownloadUpdatesAsync(update, progress =>
            {
                if (!IsHandleCreated || IsDisposed || Disposing) return;
                try { BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing && _updateCheckInProgress && _pendingUpdateManager == null) SetUpdateStatus($"Downloading {progress}%", userInitiated, true); })); }
                catch (InvalidOperationException) { }
            });
            _pendingUpdateManager = manager;
            _pendingUpdateInfo = update;
            SetUpdateReadyUi(version);
            SetUpdateStatus($"Version {version} is ready to install.", true, true);
        }
        catch (Exception ex)
        {
            SetUpdateStatus("Update check failed: " + ex.Message, userInitiated, true);
        }
        finally
        {
            _updateCheckInProgress = false;
            if (!IsDisposed && !Disposing) _checkUpdateButton.Enabled = true;
            _settingsForm?.RefreshRuntimeState();
        }
    }

    private void ToggleCommandView()
    {
        SetWorkspaceView(_listView);
        RebuildCommandView();
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

        if (_cts is not null && !IsHostProbeAllowed(site, host)) return;

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
        if (!IsHostProbeAllowed(entry.Site, entry.Host))
            return false;

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
            ? AuroraTheme.Palette.Text
            : AuroraTheme.Palette.StatusText(AuroraTheme.Palette.Danger);

        _commandBox.AppendText(command + Environment.NewLine);
        _commandBox.SelectionColor = _commandBox.ForeColor;

        if (scroll && _showCommandView.Checked)
        {
            _commandBox.SelectionStart = _commandBox.TextLength;
            _commandBox.ScrollToCaret();
        }
    }

    private void RefreshAppearance()
    {
        if (IsDisposed) return;
        AuroraTheme.ApplyMenu(_appMenu);
        AuroraTheme.ApplyMenu(_hostQuickMenu);
        RefreshGrid();
        RebuildCommandView();
        _siteList.Invalidate();
    }

    private void ApplyDarkTheme()
    {
        AuroraTheme.Initialize(this);
        AuroraTheme.Apply(this);
        ((AuroraButton)_startButton).Primary = true;
        ((AuroraButton)_stopButton).Primary = true;
        AuroraTheme.ApplyMenu(_appMenu);
        AuroraTheme.ApplyMenu(_hostQuickMenu);
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
        // The active-session UI timer also resumes expired snoozes, including when all hosts are paused.
        ReconcileMonitoringWithConfig();
        _monitorCanvas.Snapshot = BuildWallboardSnapshot();
        _monitorCanvas.Invalidate();
        var selection = GetSelectedHostIdentity();
        string? selectedColumn = _grid.CurrentCell?.OwningColumn.Name;
        int firstDisplayedRow = _grid.FirstDisplayedScrollingRowIndex;
        int horizontalOffset = _grid.HorizontalScrollingOffset;
        var configuredHosts = HostSnapshot().ToDictionary(h => BuildHostKey(h.Site, h.Address), StringComparer.OrdinalIgnoreCase);
        var monitors = _hosts.Values.ToDictionary(h => BuildHostKey(h.Site, h.Address), StringComparer.OrdinalIgnoreCase);
        foreach (var host in configuredHosts.Values)
            monitors.TryAdd(BuildHostKey(host.Site, host.Address), new HostMonitor(host.Site, host.Address));
        var rows = monitors.Values
            .Where(h =>
                _selectedSiteName is null ||
                h.Site.Equals(
                    _selectedSiteName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(h =>
            {
                lock (h)
                {
                    configuredHosts.TryGetValue(BuildHostKey(h.Site, h.Address), out var saved);
                    string health = _cts is null ? "Idle" : h.State.ToString();
                    string statusText = saved?.Options.Enabled == false ? "Disabled" :
                        saved?.Options.IsSnoozed(DateTimeOffset.UtcNow) == true ? "Snoozed" : health;
                    return new
                    {
                        Site = h.Site,
                        Host = h.Address,
                        Label = GetNickname(h.Site, h.Address),
                        Category = GetCategory(h.Site, h.Address),
                        Status = statusText,
                        Latency = _cts is not null && h.LastRoundTripMs.HasValue
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

        string query = _hostSearch.Text.Trim();
        if (query.Length > 0)
            rows = rows.Where(h => h.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                h.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                h.Site.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.DataSource = rows;
        _emptyHosts.Text = query.Length > 0 ? "No matching hosts" : "No hosts — add hosts to start";
        _emptyHosts.Visible = rows.Count == 0;
        if (_emptyHosts.Visible) _emptyHosts.BringToFront();

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
            var status = row.Cells["StatusColumn"].Value?.ToString()?.Split(" · ")[0];
            var cell = row.Cells["StatusColumn"];

            cell.Style.Font = _statusCellFont;
            cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            cell.Style.ForeColor = status switch
            {
                "Offline" => AuroraTheme.Palette.StatusText(AuroraTheme.Palette.Danger),
                "Suspect" => AuroraTheme.Palette.StatusText(AuroraTheme.Palette.Warning),
                "Online" => AuroraTheme.Palette.StatusText(AuroraTheme.Palette.Teal),
                _ => AuroraTheme.Palette.Secondary
            };

            if (status == "Offline")
                row.DefaultCellStyle.BackColor = AuroraTheme.Palette.Surface2;
        }

        int online = rows.Count(h => h.Status.StartsWith("Online", StringComparison.Ordinal));
        int suspect = rows.Count(h => h.Status.StartsWith("Suspect", StringComparison.Ordinal));
        int offline = rows.Count(h => h.Status.StartsWith("Offline", StringComparison.Ordinal));

        int configured = _selectedSiteName is null
            ? _sites.Sum(site => site.Hosts.Count(address => GetHostOptions(site, address).Enabled))
            : (FindSite(_selectedSiteName) is SiteDefinition selectedSite
                ? selectedSite.Hosts.Count(address => GetHostOptions(selectedSite, address).Enabled)
                : 0);

        _startButton.Text = _cts is null ? "Start monitoring" : "Stop monitoring";
        _startButton.Enabled = true;
        _totalValueLabel.Text = (_cts is null ? configured : rows.Count).ToString();
        _onlineValueLabel.Text = online.ToString();
        _suspectValueLabel.Text = suspect.ToString();
        _offlineValueLabel.Text = offline.ToString();

        string view = _selectedSiteName ?? AllSitesLabel;

        _statusLabel.Text = _cts is null ? "Stopped" :
            $"Monitoring {_selectedSiteName ?? "all sites"} · {_hostTokens.Count} active hosts";
    }
}
