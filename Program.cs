using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace PingWatchdog;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
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
        Minimum = 1,
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
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, HostMonitor> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public MainForm()
    {
        Text = "Ping Watchdog";
        Width = 980;
        Height = 680;
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;

        BuildGrid();
        BuildLayout();

        _statusStrip.Items.Add(_statusLabel);

        _startButton.Click += (_, _) => StartMonitoring();
        _stopButton.Click += (_, _) => StopMonitoring();
        _uiTimer.Tick += (_, _) => RefreshGrid();

        FormClosing += (_, _) => StopMonitoring();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
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
            WrapContents = false,
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
        var targets = _ipBox.Lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (targets.Length == 0)
        {
            MessageBox.Show("Enter at least one IP address or hostname.", "Ping Watchdog",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

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

        _ipBox.Enabled = true;
        _intervalSeconds.Enabled = true;
        _failureThreshold.Enabled = true;
        _recoveryThreshold.Enabled = true;
        _timeoutMs.Enabled = true;
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        _statusLabel.Text = "Stopped";
    }

    private async Task MonitorHostAsync(HostMonitor host, CancellationToken token)
    {
        using var ping = new Ping();

        while (!token.IsCancellationRequested)
        {
            bool success = false;
            long? latency = null;

            try
            {
                var reply = await ping.SendPingAsync(host.Address, (int)_timeoutMs.Value);
                success = reply.Status == IPStatus.Success;

                if (success)
                    latency = reply.RoundtripTime;
            }
            catch
            {
                success = false;
            }

            ProcessResult(host, success, latency);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds((double)_intervalSeconds.Value), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessResult(HostMonitor host, bool success, long? latency)
    {
        if (success)
        {
            host.LastRoundTripMs = latency;
            host.LastReply = DateTime.Now;
            host.ConsecutiveFailures = 0;
            host.ConsecutiveSuccesses++;

            if (host.State == HostState.Offline)
            {
                if (host.ConsecutiveSuccesses >= (int)_recoveryThreshold.Value)
                {
                    var outageStarted = host.OutageStarted;
                    host.State = HostState.Online;
                    host.OutageStarted = null;
                    host.AlertedForCurrentOutage = false;
                    ShowRecoveryNotification(host.Address, outageStarted);
                }
            }
            else
            {
                host.State = HostState.Online;
            }

            return;
        }

        host.LastRoundTripMs = null;
        host.ConsecutiveSuccesses = 0;
        host.ConsecutiveFailures++;

        if (host.ConsecutiveFailures >= (int)_failureThreshold.Value)
        {
            if (host.State != HostState.Offline)
            {
                host.State = HostState.Offline;
                host.OutageStarted ??= DateTime.Now;

                if (!host.AlertedForCurrentOutage)
                {
                    host.AlertedForCurrentOutage = true;
                    ShowOutageNotification(host.Address, host.ConsecutiveFailures);
                }
            }
        }
        else
        {
            host.State = HostState.Suspect;
        }
    }

    private void ShowOutageNotification(string host, int failures)
    {
        BeginInvoke(() =>
        {
            System.Media.SystemSounds.Exclamation.Play();
            MessageBox.Show(
                $"{host} has failed {failures} consecutive ping attempts and is being treated as DOWN.",
                "Ping Watchdog - Host Down",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        });
    }

    private void ShowRecoveryNotification(string host, DateTime? outageStarted)
    {
        BeginInvoke(() =>
        {
            var duration = outageStarted.HasValue
                ? $"\nOutage duration: {DateTime.Now - outageStarted.Value:g}"
                : string.Empty;

            MessageBox.Show(
                $"{host} is responding again.{duration}",
                "Ping Watchdog - Recovered",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private void RefreshGrid()
    {
        var rows = _hosts.Values
            .OrderBy(h => h.Address, StringComparer.OrdinalIgnoreCase)
            .Select(h => new
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
            })
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

        int online = _hosts.Values.Count(h => h.State == HostState.Online);
        int suspect = _hosts.Values.Count(h => h.State == HostState.Suspect);
        int offline = _hosts.Values.Count(h => h.State == HostState.Offline);
        _statusLabel.Text = $"Online: {online}   Suspect: {suspect}   Offline: {offline}";
    }
}
