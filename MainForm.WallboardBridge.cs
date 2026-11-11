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

// What the Wallboard shows and the actions it can take, and opening it on the right screen.
public sealed partial class MainForm
{
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
                        host.Options.SnoozedUntilUtc,
                        host.Options.Priority);
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
                        GetHostOptions(site, address).SnoozedUntilUtc,
                        GetHostOptions(site, address).Priority);
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

    // The display it was last on when that display is still connected; otherwise a different screen than the main window.
    internal static Screen WallboardTargetScreen(IReadOnlyList<Screen> screens, Screen current, string remembered) =>
        screens.FirstOrDefault(s => remembered.Length > 0 && s.DeviceName.Equals(remembered, StringComparison.OrdinalIgnoreCase))
        ?? screens.FirstOrDefault(s => !s.DeviceName.Equals(current.DeviceName, StringComparison.OrdinalIgnoreCase))
        ?? current;

    private void OpenWallboard()
    {
        using var activity = UiActivity.Begin("Opening Wallboard");
        if (_wallboardForm is not null && !_wallboardForm.IsDisposed)
        {
            _wallboardForm.Activate();
            return;
        }

        var target = WallboardTargetScreen(Screen.AllScreens, Screen.FromControl(this), _wallboardScreen);

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
                ShowHostList),
            target,
            CycleEventHistoryWindow,
            ToggleSuspectHistory,
            _wallboardShowCli,
            ShowHostQuickActions);
        _wallboardForm.AttachPriorityBanner(new PriorityBanner(_priorityAlarm));

        var opened = _wallboardForm;
        opened.Move += (_, _) => { if (opened.IsHandleCreated && opened.WindowState != FormWindowState.Minimized) RecordWallboardScreen(Screen.FromControl(opened).DeviceName); };
        _wallboardForm.FormClosed += (_, _) =>
        {
            RecordWallboardScreen(Screen.FromRectangle(opened.RestoreBounds).DeviceName);
            _wallboardForm = null;

            if (!_closingApplication)
                RestoreAfterWallboard();
        };

        _wallboardForm.Show();
        _wallboardForm.Activate();
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
}
