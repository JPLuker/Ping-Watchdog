using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Velopack;
using Velopack.Sources;

namespace PingWatchdog;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        if (args.Contains("--self-test"))
        {
            ApplicationConfiguration.Initialize();
            try
            {
                MainForm.RunSelfTests();
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
            return;
        }

        var notificationsRegistered = false;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            notificationsRegistered = true;
        }
        catch
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(notificationsRegistered));

        if (notificationsRegistered)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
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
    public List<string> Hosts { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
}

internal sealed class WatchdogConfig
{
    public int FormatVersion { get; set; } = 1;
    public List<SiteDefinition> Sites { get; set; } = new();
    public int PingIntervalSeconds { get; set; } = 2;
    public int PingTimeoutMs { get; set; } = 1000;
    public int FailureThreshold { get; set; } = 3;
    public int RecoveryThreshold { get; set; } = 2;
    public bool ShowCommandView { get; set; }
    public string? SelectedSite { get; set; }
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
    public HostState State { get; set; } = HostState.Unknown;
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public long? LastRoundTripMs { get; set; }
    public DateTime? LastReply { get; set; }
    public DateTime? OutageStarted { get; set; }
    public bool AlertedForCurrentOutage { get; set; }

    public HostMonitor(string site, string address)
    {
        Site = site;
        Address = address;
    }
}

internal sealed record CommandLogEntry(
    string Site,
    string Host,
    DateTime Timestamp,
    bool Success,
    string ResultText,
    int TimeoutMs);

public sealed class MainForm : Form
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

    private readonly List<SiteDefinition> _sites = new();
    private readonly List<CommandLogEntry> _commandEntries = new();
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
    private readonly Button _checkUpdateButton = new() { Text = "Check Updates", AutoSize = true };
    private readonly ContextMenuStrip _gridMenu = new();
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
    private readonly Icon _appIcon;
    private readonly Font _siteItemFont = new("Segoe UI Semibold", 9.5f);
    private readonly Font _statusCellFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);

    private CancellationTokenSource? _cts;
    private bool _appNotificationsAvailable;
    private readonly bool _suppressNotifications;
    private readonly bool _persistSites;
    private bool _ignoreSiteSelection;
    private bool _updateCheckInProgress;
    private string? _selectedSiteName;

    private int _monitorIntervalSeconds = 2;
    private int _pingTimeoutMs = 1000;
    private int _failureThresholdValue = 3;
    private int _recoveryThresholdValue = 2;

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
        Width = 1320;
        Height = 840;
        MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        Icon = _appIcon;

        BuildGrid();
        BuildLayout();
        BuildGridContextMenu();
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_ownershipLabel);

        var trayMenu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(230, 237, 243)
        };
        var openItem = new ToolStripMenuItem("Open Ping Watchdog");
        _trayStopItem = new ToolStripMenuItem("Stop Monitoring") { Enabled = false };
        var exitItem = new ToolStripMenuItem("Exit");

        openItem.Click += (_, _) => RestoreFromTray();
        _trayStopItem.Click += (_, _) => StopMonitoring();
        exitItem.Click += (_, _) => Close();

        trayMenu.Items.Add(openItem);
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
        _siteList.ItemHeight = 38;
        _siteList.DrawItem += DrawSiteItem;
        _siteList.SelectedIndexChanged += (_, _) => OnSiteSelectionChanged();
        _siteList.DoubleClick += (_, _) => RenameSite();
        _addSiteButton.Click += (_, _) => AddSite();
        _renameSiteButton.Click += (_, _) => RenameSite();
        _deleteSiteButton.Click += (_, _) => DeleteSite();
        _saveConfigButton.Click += (_, _) => SaveConfigFile();
        _loadConfigButton.Click += (_, _) => LoadConfigFile();
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync(userInitiated: true);
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
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(userInitiated: false);

        Shown += async (_, _) =>
        {
            _updateTimer.Start();
            await CheckForUpdatesAsync(userInitiated: false);
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _trayIcon.ShowBalloonTip(
                    2500,
                    "Ping Watchdog",
                    _cts is null
                        ? "Ping Watchdog is minimized."
                        : "Monitoring continues in the background.",
                    ToolTipIcon.Info);
            }
        };

        FormClosing += (_, _) =>
        {
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
        };

        LoadSites();

        if (_sites.Count == 0)
            _sites.Add(new SiteDefinition { Name = "Default Site" });

        _selectedSiteName ??= _sites[0].Name;
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        ApplyDarkTheme();
        UpdateActionState();
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

        form.AppendCommandLog("Test Site", "127.0.0.1", true, "Reply from 127.0.0.1: time=1ms TTL=128");
        form.AppendCommandLog("Test Site", "127.0.0.1", false, "FAILED (TimedOut)");
        Check(form._commandEntries.Count == 2);

        var configJson = JsonSerializer.Serialize(form.BuildConfig());
        var configRoundTrip = JsonSerializer.Deserialize<WatchdogConfig>(configJson);
        Check(configRoundTrip?.Sites.Count == 1);
        Check(configRoundTrip?.Sites[0].Labels.Values.Contains("Loopback") == true);
        Check(configRoundTrip?.PingIntervalSeconds == 2);
        Check(configRoundTrip?.FailureThreshold == 3);
        Check(form.GetConfiguredTargets().Count == 1);
        Check(UpdateRepoUrl.EndsWith("/JPLuker/Ping-Watchdog", StringComparison.Ordinal));

        using var ping = new Ping();
        Check(ping.Send("127.0.0.1", 1000).Status == IPStatus.Success);

        form._trayIcon.Visible = false;
        form._trayIcon.Dispose();
        form._siteItemFont.Dispose();
        form._statusCellFont.Dispose();
        form._appIcon.Dispose();
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

    private void BuildGrid()
    {
        _grid.RowTemplate.Height = 36;
        _grid.ColumnHeadersHeight = 40;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SiteColumn",
            HeaderText = "Site",
            DataPropertyName = "Site",
            Width = 145
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "HostColumn",
            HeaderText = "Host",
            DataPropertyName = "Host",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 32
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LabelColumn",
            HeaderText = "Label",
            DataPropertyName = "Label",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "StatusColumn",
            HeaderText = "Status",
            DataPropertyName = "Status",
            Width = 95
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LatencyColumn",
            HeaderText = "Latency",
            DataPropertyName = "Latency",
            Width = 85
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FailuresColumn",
            HeaderText = "Failures",
            DataPropertyName = "Failures",
            Width = 75
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LastReplyColumn",
            HeaderText = "Last Reply",
            DataPropertyName = "LastReply",
            Width = 150
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "OutageColumn",
            HeaderText = "Outage Since",
            DataPropertyName = "OutageSince",
            Width = 150
        });
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(22, 13, 18, 10),
            Margin = new Padding(0),
            Tag = "header"
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Tag = "header"
        };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        brand.Controls.Add(new Label
        {
            Text = "PING WATCHDOG",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20, FontStyle.Bold),
            ForeColor = Color.White,
            Tag = "title"
        }, 0, 0);
        brand.Controls.Add(new Label
        {
            Text = "Multi-site availability monitoring",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Tag = "muted"
        }, 0, 1);

        var headerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
            Tag = "header"
        };
        _saveConfigButton.Text = "Export Config";
        _loadConfigButton.Text = "Import Config";
        headerActions.Controls.Add(_monitorStateLabel);
        headerActions.Controls.Add(_checkUpdateButton);
        headerActions.Controls.Add(_saveConfigButton);
        headerActions.Controls.Add(_loadConfigButton);

        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(headerActions, 1, 0);

        _workspaceSplit.SplitterDistance = 252;
        _workspaceSplit.SplitterWidth = 1;
        _workspaceSplit.Panel1MinSize = 225;
        _workspaceSplit.Panel1.Padding = new Padding(14, 16, 12, 16);
        _workspaceSplit.Panel2.Padding = new Padding(18, 16, 18, 16);
        _workspaceSplit.Panel1.Tag = "nav";
        _workspaceSplit.Panel2.Tag = "window";

        var sitePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Tag = "nav"
        };
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        sitePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        sitePanel.Controls.Add(new Label
        {
            Text = "SITES",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
            Padding = new Padding(4, 10, 0, 0),
            Tag = "sectionLabel"
        }, 0, 0);
        sitePanel.Controls.Add(_siteList, 0, 1);

        _addSiteButton.Dock = DockStyle.Fill;
        _addSiteButton.Margin = new Padding(0, 7, 0, 2);
        _renameSiteButton.Dock = DockStyle.Fill;
        _renameSiteButton.Margin = new Padding(0, 4, 0, 2);
        sitePanel.Controls.Add(_addSiteButton, 0, 2);
        sitePanel.Controls.Add(_renameSiteButton, 0, 3);

        var siteFooter = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Tag = "nav"
        };
        siteFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        siteFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        siteFooter.Controls.Add(new Label
        {
            Text = "Changes apply live",
            AutoSize = true,
            Padding = new Padding(3, 13, 0, 0),
            Tag = "muted"
        }, 0, 0);
        _deleteSiteButton.Text = "Delete";
        _deleteSiteButton.Margin = new Padding(5, 7, 0, 0);
        siteFooter.Controls.Add(_deleteSiteButton, 1, 0);
        sitePanel.Controls.Add(siteFooter, 0, 4);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Tag = "window"
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12),
            Tag = "window"
        };
        for (int i = 0; i < 4; i++)
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        Panel StatCard(string title, Label value, string tag)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 0),
                Padding = new Padding(14, 10, 14, 8),
                Tag = "card"
            };
            value.Tag = tag;
            value.Location = new Point(14, 28);
            card.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(14, 9),
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                Tag = "muted"
            });
            card.Controls.Add(value);
            return card;
        }

        stats.Controls.Add(StatCard("TOTAL HOSTS", _totalValueLabel, "statTotal"), 0, 0);
        stats.Controls.Add(StatCard("ONLINE", _onlineValueLabel, "statOnline"), 1, 0);
        stats.Controls.Add(StatCard("SUSPECT", _suspectValueLabel, "statSuspect"), 2, 0);
        var offlineCard = StatCard("OFFLINE", _offlineValueLabel, "statOffline");
        offlineCard.Margin = new Padding(0);
        stats.Controls.Add(offlineCard, 3, 0);

        var inputCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 12, 16, 14),
            Margin = new Padding(0, 0, 0, 12),
            Tag = "card"
        };
        inputCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
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
        _siteHeaderLabel.Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        _siteHeaderLabel.Padding = new Padding(0, 5, 0, 0);
        _siteHeaderLabel.Tag = "primaryText";
        _autoSaveLabel.Text = "AUTO-SAVE  •  LIVE APPLY";
        _autoSaveLabel.Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold);
        _autoSaveLabel.Padding = new Padding(0, 6, 0, 0);
        _autoSaveLabel.Tag = "accentText";
        hostHeader.Controls.Add(_siteHeaderLabel, 0, 0);
        hostHeader.Controls.Add(_autoSaveLabel, 1, 0);
        inputCard.Controls.Add(hostHeader, 0, 0);
        inputCard.Controls.Add(_ipBox, 0, 1);

        var settingsCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 13, 14, 12),
            Margin = new Padding(0, 0, 0, 12),
            Tag = "card"
        };
        settingsCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0),
            Tag = "card"
        };

        Control Setting(string title, Control input, string suffix)
        {
            var wrap = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 18, 0),
                Tag = "card"
            };
            wrap.Controls.Add(new Label
            {
                Text = title.ToUpperInvariant(),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold),
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
            Margin = new Padding(8, 12, 0, 0),
            Tag = "card"
        };
        _startButton.Text = "Start Monitoring";
        _startButton.MinimumSize = new Size(132, 34);
        _stopButton.MinimumSize = new Size(78, 34);
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
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
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
            Text = "LIVE COMMAND TRACE",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold),
            Padding = new Padding(2, 7, 0, 0),
            Tag = "sectionLabel"
        }, 0, 0);
        _clearLogButton.Margin = new Padding(0, 0, 0, 4);
        commandHeader.Controls.Add(_clearLogButton, 1, 0);

        commandPanel.Controls.Add(commandHeader, 0, 0);
        commandPanel.Controls.Add(_commandBox, 0, 1);
        _mainSplit.Panel2.Controls.Add(commandPanel);

        right.Controls.Add(stats, 0, 0);
        right.Controls.Add(inputCard, 0, 1);
        right.Controls.Add(settingsCard, 0, 2);
        right.Controls.Add(_mainSplit, 0, 3);

        _workspaceSplit.Panel1.Controls.Add(sitePanel);
        _workspaceSplit.Panel2.Controls.Add(right);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_workspaceSplit, 0, 1);
        root.Controls.Add(_statusStrip, 0, 2);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private void DrawSiteItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();

        if (e.Index < 0 || e.Index >= _siteList.Items.Count)
            return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);

        using var background = new SolidBrush(selected
            ? Color.FromArgb(36, 86, 132)
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

                        _sites.Add(new SiteDefinition
                        {
                            Name = name,
                            Hosts = hosts,
                            Labels = NormalizeLabels(site.Labels, hosts)
                        });
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
                    _sites,
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
            _siteList.Items.Add($"{AllSitesLabel} ({totalHosts})");

            foreach (var site in _sites)
                _siteList.Items.Add($"{site.Name} ({site.Hosts.Count})");

            if (selectedName is null)
            {
                _siteList.SelectedIndex = 0;
            }
            else
            {
                int index = _sites.FindIndex(s =>
                    s.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));

                _siteList.SelectedIndex = index >= 0 ? index + 1 : 0;
            }
        }
        finally
        {
            _ignoreSiteSelection = false;
        }
    }

    private void OnSiteSelectionChanged()
    {
        if (_ignoreSiteSelection || _siteList.SelectedIndex < 0)
            return;

        PersistCurrentEditor();
        SaveSites();
        ReconcileMonitoringWithConfig();

        _selectedSiteName = _siteList.SelectedIndex == 0
            ? null
            : _sites[_siteList.SelectedIndex - 1].Name;

        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void LoadHostEditor()
    {
        if (_selectedSiteName is null)
        {
            _siteHeaderLabel.Text = "ALL SITES • READ-ONLY SUMMARY";
            _ipBox.ReadOnly = true;
            _ipBox.Text = string.Join(
                Environment.NewLine,
                _sites.SelectMany(site =>
                    site.Hosts.Select(host => $"[{site.Name}] {host}")));
            return;
        }

        var site = FindSite(_selectedSiteName);

        _siteHeaderLabel.Text = $"{_selectedSiteName.ToUpperInvariant()} • HOSTS";
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

        setLabel.Click += (_, _) => SetLabelForSelectedHost();
        clearLabel.Click += (_, _) => ClearLabelForSelectedHost();

        _gridMenu.Items.Add(setLabel);
        _gridMenu.Items.Add(clearLabel);
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

    private WatchdogConfig BuildConfig(bool captureEditor = true)
    {
        if (captureEditor)
            PersistCurrentEditor();

        return new WatchdogConfig
        {
            Sites = _sites.Select(site => new SiteDefinition
            {
                Name = site.Name,
                Hosts = site.Hosts.ToList(),
                Labels = new Dictionary<string, string>(site.Labels ?? new())
            }).ToList(),
            PingIntervalSeconds = (int)_intervalSeconds.Value,
            PingTimeoutMs = (int)_timeoutMs.Value,
            FailureThreshold = (int)_failureThreshold.Value,
            RecoveryThreshold = (int)_recoveryThreshold.Value,
            ShowCommandView = _showCommandView.Checked,
            SelectedSite = _selectedSiteName
        };
    }

    private void SaveConfigFile()
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

        if (dialog.ShowDialog(this) != DialogResult.OK)
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

    private void LoadConfigFile()
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

        if (dialog.ShowDialog(this) != DialogResult.OK)
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
                Hosts = hosts,
                Labels = NormalizeLabels(source.Labels, hosts)
            });
        }

        if (cleanedSites.Count == 0)
            cleanedSites.Add(new SiteDefinition { Name = "Default Site" });

        _sites.Clear();
        _sites.AddRange(cleanedSites);

        _intervalSeconds.Value = Math.Clamp(config.PingIntervalSeconds, (int)_intervalSeconds.Minimum, (int)_intervalSeconds.Maximum);
        _timeoutMs.Value = Math.Clamp(config.PingTimeoutMs, (int)_timeoutMs.Minimum, (int)_timeoutMs.Maximum);
        _failureThreshold.Value = Math.Clamp(config.FailureThreshold, (int)_failureThreshold.Minimum, (int)_failureThreshold.Maximum);
        _recoveryThreshold.Value = Math.Clamp(config.RecoveryThreshold, (int)_recoveryThreshold.Minimum, (int)_recoveryThreshold.Maximum);
        _showCommandView.Checked = config.ShowCommandView;

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
            "Add Site / Group",
            "Add Site");

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string name = dialog.SiteName;

        if (!ValidateNewSiteName(name, existingSiteName: null))
            return;

        PersistCurrentEditor();

        _sites.Add(new SiteDefinition { Name = name });
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
            "Rename Site / Group",
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
            var host = new HostMonitor(target.Site, target.Address);
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
                site.Hosts.Select(host => (Site: site.Name, Address: host)))
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
            if (_hosts.ContainsKey(pair.Key))
                continue;

            var host = new HostMonitor(pair.Value.Site, pair.Value.Address);
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
                var reply = await ping.SendPingAsync(host.Address, _pingTimeoutMs);
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
                    TimeSpan.FromSeconds(_monitorIntervalSeconds),
                    token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessResult(HostMonitor host, bool success, long? latency)
    {
        string? notificationTitle = null;
        string? notificationBody = null;
        ToolTipIcon fallbackIcon = ToolTipIcon.None;

        lock (host)
        {
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
                        string displayHost = DescribeHost(host.Site, host.Address);
                        notificationBody = duration.HasValue
                            ? $"{displayHost} is responding again. Outage duration: {FormatDuration(duration.Value)}."
                            : $"{displayHost} is responding again.";
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

                if (host.ConsecutiveFailures >= _failureThresholdValue)
                {
                    if (host.State != HostState.Offline)
                    {
                        host.State = HostState.Offline;
                        host.OutageStarted ??= DateTime.Now;

                        if (!host.AlertedForCurrentOutage)
                        {
                            host.AlertedForCurrentOutage = true;
                            notificationTitle = $"Host Down • {host.Site}";
                            string displayHost = DescribeHost(host.Site, host.Address);
                            notificationBody =
                                $"{displayHost} failed {host.ConsecutiveFailures} consecutive ping attempts and is now OFFLINE.";
                            fallbackIcon = ToolTipIcon.Warning;
                        }
                    }
                }
                else if (host.State != HostState.Offline)
                {
                    host.State = HostState.Suspect;
                }
            }
        }

        if (!_suppressNotifications &&
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

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
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
                _checkUpdateButton.Text = "Updates: unmanaged";

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
                _checkUpdateButton.Text = "Up to date";

                if (userInitiated)
                    _statusLabel.Text = "Ping Watchdog is up to date.";

                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            _checkUpdateButton.Text = $"Downloading {version}...";

            await manager.DownloadUpdatesAsync(
                update,
                progress =>
                {
                    if (IsDisposed || Disposing)
                        return;

                    BeginInvoke(() =>
                    {
                        if (!IsDisposed && !Disposing)
                            _checkUpdateButton.Text = $"Update {progress}%";
                    });
                });

            if (_cts is null)
            {
                PersistCurrentEditor();
                SaveSites();
                _checkUpdateButton.Text = $"Restarting to {version}...";
                manager.ApplyUpdatesAndRestart(update);
                return;
            }

            _checkUpdateButton.Text = $"{version} ready";
            ShowNotification(
                "Ping Watchdog update ready",
                $"Version {version} was downloaded. It will be applied the next time Ping Watchdog restarts.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
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

                if (height > 330)
                    _mainSplit.SplitterDistance =
                        Math.Max(160, (int)(height * 0.58));

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
            _pingTimeoutMs);

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

    private void AppendCommandEntryToView(
        CommandLogEntry entry,
        bool scroll = true)
    {
        string nickname = GetNickname(entry.Site, entry.Host);
        string labelPart = string.IsNullOrWhiteSpace(nickname)
            ? string.Empty
            : $" [{nickname}]";

        string command =
            $"{entry.Host}: [{entry.Site}]{labelPart} [{entry.Timestamp:HH:mm:ss}] ping {entry.Host} -n 1 -w {entry.TimeoutMs}  ->  {entry.ResultText}";

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
        var window = Color.FromArgb(10, 14, 20);
        var header = Color.FromArgb(12, 18, 26);
        var nav = Color.FromArgb(14, 20, 28);
        var card = Color.FromArgb(18, 25, 34);
        var input = Color.FromArgb(11, 17, 24);
        var border = Color.FromArgb(39, 49, 61);
        var text = Color.FromArgb(234, 240, 246);
        var muted = Color.FromArgb(139, 153, 169);
        var accent = Color.FromArgb(74, 158, 255);

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
                        button.Padding = new Padding(8, 2, 8, 2);
                        button.Height = Math.Max(button.Height, 32);
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

        _addSiteButton.BackColor = Color.FromArgb(29, 73, 115);
        _addSiteButton.FlatAppearance.BorderColor = Color.FromArgb(59, 128, 196);
        _deleteSiteButton.BackColor = Color.FromArgb(57, 28, 34);
        _deleteSiteButton.FlatAppearance.BorderColor = Color.FromArgb(100, 45, 55);

        _checkUpdateButton.BackColor = Color.FromArgb(24, 55, 82);
        _checkUpdateButton.FlatAppearance.BorderColor = Color.FromArgb(48, 103, 153);

        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = card;
        _grid.GridColor = Color.FromArgb(31, 40, 50);
        _grid.DefaultCellStyle.BackColor = card;
        _grid.DefaultCellStyle.ForeColor = text;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(35, 73, 108);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(16, 23, 31);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 31, 42);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(170, 185, 201);
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
                        LastReply = h.LastReply?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                        OutageSince = h.OutageStarted?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—"
                    };
                }
            })
            .OrderBy(h => h.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _grid.DataSource = rows;

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
