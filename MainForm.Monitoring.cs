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

// Starting and stopping monitoring, per-host ping workers and state changes.
public sealed partial class MainForm
{
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
        using var activity = UiActivity.Begin("Starting monitoring");
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

        // Priority hosts ring until acknowledged and replace the ordinary notification.
        bool priorityAlert = notificationTitle is not null && host.Options.Priority &&
            !host.Options.IsSnoozed(DateTimeOffset.UtcNow) && !IsDisposed && !Disposing;
        if (priorityAlert)
            RaisePriorityAlert(host, eventKind == "DOWN");

        if (!priorityAlert &&
            !_suppressNotifications &&
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

    private bool IsHostEnabled(string siteName, string address)
    {
        var site = FindSite(siteName);
        return site is not null &&
            site.Hosts.Any(host => host.Equals(address, StringComparison.OrdinalIgnoreCase)) &&
            GetHostOptions(site, address).Enabled;
    }

    private bool IsHostProbeAllowed(string siteName, string address) =>
        IsHostEnabled(siteName, address) && !GetHostOptions(FindSite(siteName), address).IsSnoozed(DateTimeOffset.UtcNow);
}
