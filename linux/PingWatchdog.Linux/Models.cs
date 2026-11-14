// Saved configuration, preferences and the snapshots the windows read. The JSON shape matches the
// Windows .pingwatch.json format, so files move between the two builds without losing host options.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PingWatchdog.Linux;

internal enum HostState
{
    Unknown,
    Online,
    Suspect,
    Offline
}

internal enum AppearanceMode { System, Light, Dark }

internal sealed class SiteDefinition
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public List<string> Hosts { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Same field name as Windows: enabled, snooze, priority and per-host timing.
    public Dictionary<string, HostOptions> HostDetails { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Quick Monitor hosts live only for this session.
    [JsonIgnore]
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
    // Empty uses the desktop's alarm sound.
    public string PriorityAlarmSound { get; set; } = "";
}

// Per-user display and startup choices. Kept apart from the host configuration, as on Windows,
// so importing someone else's configuration never changes how this desktop starts.
internal sealed class LinuxPreferences
{
    public int FormatVersion { get; set; } = 1;
    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;
    public bool RestoreLastSiteOnStartup { get; set; } = true;
    public bool StartMonitoringOnLaunch { get; set; }
    public bool OpenWallboardOnStartup { get; set; }
    public string WallboardScreen { get; set; } = "";
    public bool ShowVersionOverlay { get; set; }
    public bool ListView { get; set; }
    public List<string> HiddenColumns { get; set; } = new() { "Group", "Failures", "LastReply", "Outage" };

    internal static LinuxPreferences Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<LinuxPreferences>(File.ReadAllText(path)) is { } loaded)
            {
                if (!Enum.IsDefined(loaded.Appearance)) loaded.Appearance = AppearanceMode.System;
                loaded.HiddenColumns ??= new();
                loaded.WallboardScreen ??= "";
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged preferences file falls back to defaults; hosts live in a different file.
        }
        return new LinuxPreferences();
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafeFile.WriteAtomic(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal LinuxPreferences Copy()
    {
        var copy = (LinuxPreferences)MemberwiseClone();
        copy.HiddenColumns = HiddenColumns.ToList();
        return copy;
    }
}

internal sealed record StateEventRecord(
    DateTime Timestamp,
    string Site,
    string Host,
    string DisplayHost,
    string Kind,
    string Message);

internal sealed record CommandLogEntry(
    DateTime Timestamp,
    string Site,
    string Host,
    string DisplayHost,
    bool Success,
    string Text);

internal sealed record EventHistorySnapshot(
    int StoredCount,
    IReadOnlyList<StateEventRecord> Events);

internal static class HistoryCsv
{
    public static async Task WriteAsync(TextWriter writer, IEnumerable<StateEventRecord> events)
    {
        static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
        await writer.WriteLineAsync("Timestamp,Event,Site,Host,Details");
        foreach (var e in events)
            await writer.WriteLineAsync(string.Join(",", Csv(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(e.Kind), Csv(e.Site), Csv(e.DisplayHost), Csv(e.Message)));
    }
}

internal readonly record struct HostKey(string Site, string Address)
{
    public bool Matches(string site, string address) =>
        Site.Equals(site, StringComparison.OrdinalIgnoreCase) && Address.Equals(address, StringComparison.OrdinalIgnoreCase);
}

internal sealed record HostSnapshot(
    string Site,
    string FolderPath,
    string Address,
    string Label,
    HostState State,
    long? LatencyMs,
    int Failures,
    DateTime? LastReply,
    DateTime? OutageStarted)
{
    public string Group { get; init; } = "";
    public HostOptions Options { get; init; } = new();
    public bool Temporary { get; init; }
    public bool Monitoring { get; init; }
    public HostKey Key => new(Site, Address);
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Address : Label;
    public bool Disabled => !Options.Enabled;
    public bool Snoozed => Options.IsSnoozed(DateTimeOffset.UtcNow);

    // Idle when nothing is running; Disabled and Snoozed win over a live state.
    public string StatusWord => Disabled ? "Disabled" : Snoozed ? "Snoozed" : !Monitoring ? "Idle" : State switch
    {
        HostState.Online => "Online",
        HostState.Suspect => "Suspect",
        HostState.Offline => "Offline",
        _ => "Waiting"
    };
    public string StatusText => StatusWord + (Options.Priority ? " · Priority" : "");
}

internal sealed record SiteSnapshot(
    string Name,
    string FolderPath,
    IReadOnlyList<HostSnapshot> Hosts,
    bool Temporary = false);

internal sealed record WatchdogSnapshot(
    bool Monitoring,
    string? SelectedSite,
    IReadOnlyList<SiteSnapshot> Sites,
    IReadOnlyList<HostSnapshot> Hosts,
    IReadOnlyList<StateEventRecord> Events,
    IReadOnlyList<CommandLogEntry> Commands,
    WatchdogConfig Settings);

internal sealed record PriorityStateChange(HostSnapshot Host, bool Offline, DateTime At);

internal sealed record ProbeResult(bool Success, long? LatencyMs, string Text);

internal static class SafeFile
{
    // Temp file, flush, then replace: a crash mid-write leaves the old file whole.
    internal static void WriteAtomic(string path, string text)
    {
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(text);
            writer.Flush();
            stream.Flush(true);
        }
        if (File.ReadAllText(temp) != text) throw new IOException("The saved file did not read back correctly.");
        File.Move(temp, path, true);
    }
}
