using System.Collections.Concurrent;
using System.Net.NetworkInformation;
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

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new("Idle");
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 500 };
    private readonly ConcurrentDictionary<string, HostMonitor> _hosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayStopItem;

    private CancellationTokenSource? _cts;
    private bool _appNotificationsAvailable;

    private int _monitorIntervalSeconds = 2;
    private int _pingTimeoutMs = 1000;
    private int _failureThresholdValue = 3;
    private int _recoveryThresholdValue = 2;

    public MainForm(bool appNotificationsAvailable)
    {
        _appNotificationsAvailable = appNotificationsAvailable;

        Text = "Ping Watchdog";
        Width = 980;
        Height = 680;
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;

        BuildGrid();
        BuildLayout();
        _statusStrip.Items.Add(_statusLabel);

        var trayMenu = new ContextMenuStrip();
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
            Icon = SystemIcons.Application,
            Text = "Ping Watchdog",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _startButton.Click += (_, _) => StartMonitoring();
        _stopButton.Click += (_, _) => StopMonitoring();
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

        FormClosing += (_, _) =>
        {
            StopMonitoring();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        };
    }

    internal static void RunSelfTests()
    {
        using var form = new MainForm(false);
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
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var inputGroup = new GroupBox
        {
            Text = "IPs / Hostnames",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        inputGroup.Controls.Add(_ipBox);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0, 8, 0, 0)
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

        root.Controls.Add(inputGroup, 0, 0);
        root.Controls.Add(controls, 0, 1);
        root.Controls.Add(_grid, 0, 2);
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

            try
            {
                var reply = await ping.SendPingAsync(host.Address, _pingTimeoutMs);
                success = reply.Status == IPStatus.Success;

                if (success)
                    latency = reply.RoundtripTime;
            }
            catch
            {
                success = false;
            }

            if (token.IsCancellationRequested || IsDisposed || Disposing) break;
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

        if (notificationTitle is not null && notificationBody is not null)
        {
            // Async ping continuations resume on the UI thread; deliver immediately so Stop cannot leave stale queued alerts.
            if (!IsDisposed && !Disposing)
                ShowNotification(notificationTitle, notificationBody, fallbackIcon);
        }
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
                "OFFLINE" => Color.MistyRose,
                "SUSPECT" => Color.LemonChiffon,
                "ONLINE" => Color.Honeydew,
                _ => SystemColors.Window
            };
        }

        int online = rows.Count(h => h.Status == "ONLINE");
        int suspect = rows.Count(h => h.Status == "SUSPECT");
        int offline = rows.Count(h => h.Status == "OFFLINE");
        _statusLabel.Text = $"Online: {online}   Suspect: {suspect}   Offline: {offline}";
    }
}
