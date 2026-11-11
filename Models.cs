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

// Saved configuration, live host state and the snapshots passed to the Wallboard, Settings and Organization windows.
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
    // Empty uses the Windows alarm sound.
    public string PriorityAlarmSound { get; set; } = "";
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
    AppearanceMode Appearance = AppearanceMode.System,
    string PriorityAlarmSound = "",
    bool UpdatesManaged = false,
    bool StartWithWindows = false,
    bool OpenWallboardOnStartup = false,
    bool ShowVersionOverlay = false);

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
    DateTimeOffset? SnoozedUntilUtc = null,
    bool Priority = false);

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
