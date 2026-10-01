using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace PingWatchdog;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            ApplicationConfiguration.Initialize();
            try { MainForm.RunSelfTests(); Environment.ExitCode = 0; }
            catch { Environment.ExitCode = 1; }
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

internal sealed class HostMonitor
{
    public string Address { get; }
    public HostState State { get; set; } = HostState.Unknown;
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public long? LastRoundTripMs { get; set; }
    public DateTime? LastReply { get; set; }
    public DateTime? OutageStarted { get; set; }
    public bool AlertedForCurrentOutage { get; set; }

    public HostMonitor(string address) => Address = address;
}

public sealed class MainForm : Form
{
    private readonly TextBox _ipBox = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10),
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

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BorderStyle = BorderStyle.None
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

    private readonly SplitContainer _mainSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        Panel2Collapsed = true,
        SplitterWidth = 6,
        Panel1MinSize = 140,
        Panel2MinSize = 120
    };

    private readonly StatusStrip _statusStrip = new() { SizingGrip = false };
    private readonly ToolStripStatusLabel _statusLabel = new("Idle");
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 500 };
    private readonly ConcurrentDictionary<string, HostMonitor> _hosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayStopItem;
    private readonly Icon _appIcon;

    private CancellationTokenSource? _cts;
    private bool _appNotificationsAvailable;
    private readonly bool _suppressNotifications;
    private int _commandLineCount;

    private int _monitorIntervalSeconds = 2;
    private int _pingTimeoutMs = 1000;
    private int _failureThresholdValue = 3;
    private int _recoveryThresholdValue = 2;

    public MainForm(bool appNotificationsAvailable, bool suppressNotifications = false)
    {
        _appNotificationsAvailable = appNotificationsAvailable;
        _suppressNotifications = suppressNotifications;

        Text = "Ping Watchdog";
        Width = 1040;
        Height = 760;
        MinimumSize = new Size(860, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        Icon = _appIcon;

        BuildGrid();
        BuildLayout();
        _statusStrip.Items.Add(_statusLabel);

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

        _startButton.Click += (_, _) => StartMonitoring();
        _stopButton.Click += (_, _) => StopMonitoring();
        _showCommandView.CheckedChanged += (_, _) => ToggleCommandView();
        _clearLogButton.Click += (_, _) => ClearCommandLog();
        _uiTimer.Tick += (_, _) => RefreshGrid();

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _trayIcon.ShowBalloonTip(
                    2500,
                    "Ping Watchdog",
                    _cts is null ? "Ping Watchdog is minimized." : "Monitoring continues in the background.",
                    ToolTipIcon.Info);
            }
        };

        FormClosing += (_, _) => StopMonitoring();
        FormClosed += (_, _) =>
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _appIcon.Dispose();
        };

        ApplyDarkTheme();
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
        using var form = new MainForm(false, suppressNotifications: true);
        var host = new HostMonitor("127.0.0.1");
        void Check(bool condition) { if (!condition) throw new InvalidOperationException("Alert state test failed."); }
        form.ProcessResult(host, false, null);
        Check(host.State == HostState.Suspect && !host.AlertedForCurrentOutage);
        form.ProcessResult(host, true, 1);
        Check(host.State == HostState.Online && host.ConsecutiveFailures == 0);
        for (int i = 0; i < 3; i++) form.ProcessResult(host, false, null);
        Check(host.State == HostState.Offline && host.AlertedForCurrentOutage);
        var outage = host.OutageStarted;
        form.ProcessResult(host, true, 1);
        form.ProcessResult(host, false, null);
        Check(host.State == HostState.Offline && host.OutageStarted == outage);
        form.ProcessResult(host, true, 1);
        form.ProcessResult(host, true, 1);
        Check(host.State == HostState.Online && !host.AlertedForCurrentOutage);
        for (int i = 0; i < 3; i++) form.ProcessResult(host, false, null);
        Check(host.State == HostState.Offline && host.AlertedForCurrentOutage);
        using var ping = new Ping();
        Check(ping.Send("127.0.0.1", 1000).Status == IPStatus.Success);
        form._trayIcon.Visible = false;
        form._trayIcon.Dispose();
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Host",
            DataPropertyName = "Host",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 35
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Status",
            DataPropertyName = "Status",
            Width = 95
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Latency",
            DataPropertyName = "Latency",
            Width = 85
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Failures",
            DataPropertyName = "Failures",
            Width = 75
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Last Reply",
            DataPropertyName = "LastReply",
            Width = 150
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Outage Since",
            DataPropertyName = "OutageSince",
            Width = 150
        });
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var inputGroup = new GroupBox
        {
            Text = " MONITORED HOSTS ",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        inputGroup.Controls.Add(_ipBox);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(2, 10, 2, 4)
        };

        controls.Controls.Add(new Label { Text = "Ping every:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        controls.Controls.Add(_intervalSeconds);
        controls.Controls.Add(new Label { Text = "sec", AutoSize = true, Padding = new Padding(0, 6, 10, 0) });

        controls.Controls.Add(new Label { Text = "Timeout:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        controls.Controls.Add(_timeoutMs);
        controls.Controls.Add(new Label { Text = "ms", AutoSize = true, Padding = new Padding(0, 6, 10, 0) });

        controls.Controls.Add(new Label { Text = "Declare down after:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        controls.Controls.Add(_failureThreshold);
        controls.Controls.Add(new Label { Text = "fails", AutoSize = true, Padding = new Padding(0, 6, 10, 0) });

        controls.Controls.Add(new Label { Text = "Recover after:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        controls.Controls.Add(_recoveryThreshold);
        controls.Controls.Add(new Label { Text = "successes", AutoSize = true, Padding = new Padding(0, 6, 10, 0) });

        controls.Controls.Add(_startButton);
        controls.Controls.Add(_stopButton);
        controls.Controls.Add(_showCommandView);

        _mainSplit.Panel1.Padding = new Padding(0, 4, 0, 4);
        _mainSplit.Panel1.Controls.Add(_grid);

        var commandPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 5, 0, 0)
        };
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var commandHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        commandHeader.Controls.Add(new Label
        {
            Text = "LIVE CMD VIEW  •  every ping attempt",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Padding = new Padding(0, 7, 12, 0)
        });
        commandHeader.Controls.Add(_clearLogButton);

        commandPanel.Controls.Add(commandHeader, 0, 0);
        commandPanel.Controls.Add(_commandBox, 0, 1);
        _mainSplit.Panel2.Controls.Add(commandPanel);

        root.Controls.Add(inputGroup, 0, 0);
        root.Controls.Add(controls, 0, 1);
        root.Controls.Add(_mainSplit, 0, 2);
        root.Controls.Add(_statusStrip, 0, 3);

        Controls.Add(root);
    }

    private void StartMonitoring()
    {
        var targets = _ipBox.Text.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (targets.Length == 0)
        {
            MessageBox.Show(
                "Enter at least one IP address or hostname.",
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
        ClearCommandLog();
        foreach (var target in targets)
            _hosts[target] = new HostMonitor(target);

        _ipBox.Enabled = false;
        _intervalSeconds.Enabled = false;
        _failureThreshold.Enabled = false;
        _recoveryThreshold.Enabled = false;
        _timeoutMs.Enabled = false;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _trayStopItem.Enabled = true;

        _cts = new CancellationTokenSource();
        _uiTimer.Start();
        _statusLabel.Text = $"Monitoring {targets.Length} host(s)...";

        foreach (var target in targets)
            _ = MonitorHostAsync(_hosts[target], _cts.Token);
    }

    private void StopMonitoring()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _uiTimer.Stop();

        if (!IsDisposed)
        {
            _ipBox.Enabled = true;
            _intervalSeconds.Enabled = true;
            _failureThreshold.Enabled = true;
            _recoveryThreshold.Enabled = true;
            _timeoutMs.Enabled = true;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _trayStopItem.Enabled = false;
            _statusLabel.Text = "Stopped";
        }
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
                    var ttl = reply.Options is null ? string.Empty : $" TTL={reply.Options.Ttl}";
                    resultText = $"Reply from {reply.Address}: time={reply.RoundtripTime}ms{ttl}";
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

            AppendCommandLog(host.Address, success, resultText);
            ProcessResult(host, success, latency);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_monitorIntervalSeconds), token);
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

                        notificationTitle = "Host Recovered";
                        notificationBody = duration.HasValue
                            ? $"{host.Address} is responding again. Outage duration: {FormatDuration(duration.Value)}."
                            : $"{host.Address} is responding again.";
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
                            notificationTitle = "Host Down";
                            notificationBody =
                                $"{host.Address} failed {host.ConsecutiveFailures} consecutive ping attempts and is now OFFLINE.";
                            fallbackIcon = ToolTipIcon.Warning;
                        }
                    }
                }
                else
                {
                    if (host.State != HostState.Offline)
                        host.State = HostState.Suspect;
                }
            }
        }

        if (!_suppressNotifications && notificationTitle is not null && notificationBody is not null)
        {
            // Async ping continuations resume on the UI thread; deliver immediately so Stop cannot leave stale queued alerts.
            if (!IsDisposed && !Disposing)
                ShowNotification(notificationTitle, notificationBody, fallbackIcon);
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
                    _mainSplit.SplitterDistance = Math.Max(160, (int)(height * 0.60));

                _commandBox.SelectionStart = _commandBox.TextLength;
                _commandBox.ScrollToCaret();
            });
        }
    }

    private void ClearCommandLog()
    {
        _commandBox.Clear();
        _commandLineCount = 0;
    }

    private void AppendCommandLog(string host, bool success, string resultText)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendCommandLog(host, success, resultText));
            return;
        }

        if (_commandLineCount >= 2000)
        {
            int cutAt = _commandBox.GetFirstCharIndexFromLine(500);
            if (cutAt > 0)
            {
                _commandBox.Select(0, cutAt);
                _commandBox.SelectedText = string.Empty;
                _commandLineCount -= 500;
            }
        }

        string command = $"{host}: [{DateTime.Now:HH:mm:ss}] ping {host} -n 1 -w {_pingTimeoutMs}  ->  {resultText}";

        _commandBox.SelectionStart = _commandBox.TextLength;
        _commandBox.SelectionLength = 0;
        _commandBox.SelectionColor = success
            ? Color.FromArgb(126, 231, 135)
            : Color.FromArgb(255, 123, 114);
        _commandBox.AppendText(command + Environment.NewLine);
        _commandBox.SelectionColor = _commandBox.ForeColor;
        _commandLineCount++;

        if (_showCommandView.Checked)
        {
            _commandBox.SelectionStart = _commandBox.TextLength;
            _commandBox.ScrollToCaret();
        }
    }

    private void ApplyDarkTheme()
    {
        var window = Color.FromArgb(13, 17, 23);
        var panel = Color.FromArgb(22, 27, 34);
        var input = Color.FromArgb(13, 17, 23);
        var border = Color.FromArgb(48, 54, 61);
        var text = Color.FromArgb(230, 237, 243);
        var muted = Color.FromArgb(139, 148, 158);

        BackColor = window;
        ForeColor = text;

        void Theme(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                control.ForeColor = text;

                switch (control)
                {
                    case TextBox box:
                        box.BackColor = input;
                        box.ForeColor = text;
                        box.BorderStyle = BorderStyle.FixedSingle;
                        break;

                    case RichTextBox rich:
                        rich.BackColor = Color.FromArgb(8, 12, 18);
                        rich.ForeColor = Color.FromArgb(201, 209, 217);
                        break;

                    case NumericUpDown number:
                        number.BackColor = input;
                        number.ForeColor = text;
                        break;

                    case Button button:
                        button.FlatStyle = FlatStyle.Flat;
                        button.FlatAppearance.BorderColor = border;
                        button.FlatAppearance.BorderSize = 1;
                        button.BackColor = panel;
                        button.ForeColor = text;
                        button.Padding = new Padding(5, 1, 5, 1);
                        break;

                    case GroupBox group:
                        group.BackColor = window;
                        group.ForeColor = muted;
                        break;

                    case Label label:
                        label.BackColor = Color.Transparent;
                        label.ForeColor = muted;
                        break;

                    case CheckBox check:
                        check.BackColor = Color.Transparent;
                        check.ForeColor = text;
                        break;

                    case FlowLayoutPanel flow:
                        flow.BackColor = window;
                        break;

                    case TableLayoutPanel table:
                        table.BackColor = window;
                        break;

                    case SplitContainer split:
                        split.BackColor = border;
                        split.Panel1.BackColor = window;
                        split.Panel2.BackColor = window;
                        break;
                }

                if (control.HasChildren)
                    Theme(control);
            }
        }

        Theme(this);

        _startButton.BackColor = Color.FromArgb(35, 134, 96);
        _startButton.FlatAppearance.BorderColor = Color.FromArgb(46, 160, 107);
        _stopButton.BackColor = Color.FromArgb(92, 35, 39);
        _stopButton.FlatAppearance.BorderColor = Color.FromArgb(139, 55, 62);

        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = panel;
        _grid.GridColor = border;
        _grid.DefaultCellStyle.BackColor = panel;
        _grid.DefaultCellStyle.ForeColor = text;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(38, 79, 120);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 36, 44);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 36, 44);
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

        _statusStrip.BackColor = Color.FromArgb(17, 22, 29);
        _statusStrip.ForeColor = muted;
        _statusLabel.ForeColor = muted;

        _commandBox.BackColor = Color.FromArgb(6, 10, 15);
        _commandBox.ForeColor = Color.FromArgb(201, 209, 217);
    }

    private void ShowNotification(string title, string body, ToolTipIcon fallbackIcon)
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

        _trayIcon.ShowBalloonTip(5000, title, body, fallbackIcon);
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
            .Select(h =>
            {
                lock (h)
                {
                    return new
                    {
                        Host = h.Address,
                        Status = h.State switch
                        {
                            HostState.Online => "ONLINE",
                            HostState.Suspect => "SUSPECT",
                            HostState.Offline => "OFFLINE",
                            _ => "UNKNOWN"
                        },
                        Latency = h.LastRoundTripMs.HasValue ? $"{h.LastRoundTripMs} ms" : "—",
                        Failures = h.ConsecutiveFailures,
                        LastReply = h.LastReply?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                        OutageSince = h.OutageStarted?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—"
                    };
                }
            })
            .OrderBy(h => h.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _grid.DataSource = rows;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var status = row.Cells[1].Value?.ToString();

            row.DefaultCellStyle.BackColor = status switch
            {
                "OFFLINE" => Color.FromArgb(72, 29, 34),
                "SUSPECT" => Color.FromArgb(82, 64, 22),
                "ONLINE" => Color.FromArgb(24, 61, 45),
                _ => Color.FromArgb(22, 27, 34)
            };
            row.DefaultCellStyle.ForeColor = Color.FromArgb(230, 237, 243);
        }

        int online = rows.Count(h => h.Status == "ONLINE");
        int suspect = rows.Count(h => h.Status == "SUSPECT");
        int offline = rows.Count(h => h.Status == "OFFLINE");
        _statusLabel.Text = $"Online: {online}   Suspect: {suspect}   Offline: {offline}";
    }
}
