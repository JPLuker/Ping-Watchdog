using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace PingWatchdog.Linux;

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
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
    public bool AutoCheckUpdates { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool WallboardShowCli { get; set; } = true;
    public bool ShowUpdateControlOnHome { get; set; }
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

internal sealed record HostSnapshot(
    string Site,
    string FolderPath,
    string Address,
    string Label,
    HostState State,
    long? LatencyMs,
    int Failures,
    DateTime? LastReply,
    DateTime? OutageStarted);

internal sealed record SiteSnapshot(
    string Name,
    string FolderPath,
    IReadOnlyList<HostSnapshot> Hosts);

internal sealed record WatchdogSnapshot(
    bool Monitoring,
    string? SelectedSite,
    IReadOnlyList<SiteSnapshot> Sites,
    IReadOnlyList<HostSnapshot> Hosts,
    IReadOnlyList<StateEventRecord> Events,
    IReadOnlyList<CommandLogEntry> Commands,
    WatchdogConfig Settings);

internal sealed class HostRuntime
{
    public HostRuntime(string site, string folderPath, string address, string label)
    {
        Site = site;
        FolderPath = folderPath;
        Address = address;
        Label = label;
    }

    public string Site { get; }
    public string FolderPath { get; }
    public string Address { get; }
    public string Label { get; set; }
    public HostState State { get; set; } = HostState.Unknown;
    public long? LastRoundTripMs { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public DateTime? LastReply { get; set; }
    public DateTime? OutageStarted { get; set; }
}

internal sealed class WatchdogEngine : IDisposable
{
    private const int MaxCommandEntries = 2500;
    private readonly object _gate = new();
    private readonly List<SiteDefinition> _sites = new();
    private readonly List<string> _folders = new();
    private readonly List<StateEventRecord> _events = new();
    private readonly List<CommandLogEntry> _commands = new();
    private readonly ConcurrentDictionary<string, HostRuntime> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _hostTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _configDirectory;
    private readonly string _configPath;
    private readonly string _historyPath;
    private CancellationTokenSource? _sessionToken;
    private bool _disposed;

    public WatchdogEngine(string? storageRoot = null)
    {
        _configDirectory = storageRoot ?? ResolveConfigDirectory();
        _configPath = Path.Combine(_configDirectory, "autosave.pingwatch.json");
        _historyPath = Path.Combine(ResolveStateDirectory(storageRoot), "event-history.json");
        Config = new WatchdogConfig();
        Load();
    }

    public event EventHandler? Changed;
    public WatchdogConfig Config { get; private set; }
    public bool Monitoring => _sessionToken is not null && !_sessionToken.IsCancellationRequested;
    public string? SelectedSite
    {
        get => Config.SelectedSite;
        set
        {
            lock (_gate)
            {
                Config.SelectedSite = value is not null && FindSiteUnsafe(value) is null ? null : value;
                SaveUnsafe();
            }
            RaiseChanged();
        }
    }

    public string ConfigPath => _configPath;

    private static string ResolveConfigDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string root = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "PingWatchdog");
    }

    private static string ResolveStateDirectory(string? storageRoot)
    {
        if (!string.IsNullOrWhiteSpace(storageRoot))
            return Path.Combine(storageRoot, "state");

        string? xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        string root = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(root, "PingWatchdog");
    }

    private void Load()
    {
        lock (_gate)
        {
            WatchdogConfig? loaded = null;

            try
            {
                if (File.Exists(_configPath))
                    loaded = JsonSerializer.Deserialize<WatchdogConfig>(File.ReadAllText(_configPath));
            }
            catch
            {
                loaded = null;
            }

            if (loaded is null)
            {
                string bundled = Path.Combine(AppContext.BaseDirectory, "default.pingwatch.json");
                try
                {
                    if (File.Exists(bundled))
                        loaded = JsonSerializer.Deserialize<WatchdogConfig>(File.ReadAllText(bundled));
                }
                catch
                {
                    loaded = null;
                }
            }

            Config = SanitizeConfig(loaded ?? new WatchdogConfig());
            _sites.Clear();
            _sites.AddRange(Config.Sites);
            _folders.Clear();
            foreach (var folder in Config.SiteFolders)
                EnsureFolderUnsafe(folder);
            foreach (var site in _sites)
                EnsureFolderUnsafe(site.FolderPath);

            if (_sites.Count == 0)
                _sites.Add(new SiteDefinition { Name = "Default Site" });

            Config.Sites = _sites;
            Config.SiteFolders = _folders;

            if (Config.SelectedSite is not null && FindSiteUnsafe(Config.SelectedSite) is null)
                Config.SelectedSite = null;

            LoadHistoryUnsafe();
            SaveUnsafe();
        }
    }

    private static WatchdogConfig SanitizeConfig(WatchdogConfig source)
    {
        var config = new WatchdogConfig
        {
            FormatVersion = Math.Max(1, source.FormatVersion),
            PingIntervalSeconds = Math.Clamp(source.PingIntervalSeconds, 1, 300),
            PingTimeoutMs = Math.Clamp(source.PingTimeoutMs, 250, 10000),
            FailureThreshold = Math.Clamp(source.FailureThreshold, 2, 20),
            RecoveryThreshold = Math.Clamp(source.RecoveryThreshold, 1, 20),
            ShowCommandView = source.ShowCommandView,
            SelectedSite = source.SelectedSite,
            EventHistoryHours = NormalizeHistoryHours(source.EventHistoryHours),
            HideSuspectEvents = source.HideSuspectEvents,
            AutoCheckUpdates = source.AutoCheckUpdates,
            MinimizeToTray = source.MinimizeToTray,
            NotificationsEnabled = source.NotificationsEnabled,
            WallboardShowCli = source.WallboardShowCli,
            ShowUpdateControlOnHome = source.ShowUpdateControlOnHome
        };

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in source.Sites ?? new List<SiteDefinition>())
        {
            string name = raw.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                continue;

            var hosts = NormalizeHosts(raw.Hosts ?? new List<string>());
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var host in hosts)
            {
                if (raw.Labels is not null && raw.Labels.TryGetValue(host, out var label) && !string.IsNullOrWhiteSpace(label))
                    labels[host] = label.Trim();
            }

            config.Sites.Add(new SiteDefinition
            {
                Name = name,
                FolderPath = NormalizeFolderPath(raw.FolderPath),
                Hosts = hosts,
                Labels = labels
            });
        }

        config.SiteFolders = (source.SiteFolders ?? new List<string>())
            .Select(NormalizeFolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return config;
    }

    public static List<string> NormalizeHosts(IEnumerable<string> hosts)
    {
        return hosts
            .SelectMany(host => host.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(host => host.Trim())
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return string.Join(
            "/",
            path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static int NormalizeHistoryHours(int hours) => hours switch
    {
        0 or 24 or 168 or 720 => hours,
        _ => 24
    };

    public IReadOnlyList<string> FolderSnapshot()
    {
        lock (_gate)
            return _folders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<SiteDefinition> SiteDefinitions()
    {
        lock (_gate)
        {
            return _sites.Select(CloneSite).ToList();
        }
    }

    // History/export must query the stored events, not the bounded live dashboard snapshot.
    public EventHistorySnapshot HistorySnapshot(int hours, bool hideSuspects, string? site = null, DateTime? now = null)
    {
        lock (_gate)
        {
            IEnumerable<StateEventRecord> events = _events;
            hours = NormalizeHistoryHours(hours);
            if (hours > 0)
            {
                DateTime cutoff = (now ?? DateTime.Now).AddHours(-hours);
                events = events.Where(e => e.Timestamp >= cutoff);
            }
            if (hideSuspects)
                events = events.Where(e => !e.Kind.Equals("SUSPECT", StringComparison.OrdinalIgnoreCase));
            if (site is not null)
                events = events.Where(e => e.Site.Equals(site, StringComparison.OrdinalIgnoreCase));
            return new EventHistorySnapshot(_events.Count, events.OrderByDescending(e => e.Timestamp).ToList());
        }
    }

    public IReadOnlyList<string> HistorySites()
    {
        lock (_gate)
            return _sites.Select(s => s.Name).Concat(_events.Select(e => e.Site))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public WatchdogSnapshot Snapshot()
    {
        lock (_gate)
        {
            var runtime = new Dictionary<string, HostSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _hosts)
            {
                lock (pair.Value)
                {
                    var host = pair.Value;
                    runtime[pair.Key] = new HostSnapshot(
                        host.Site,
                        host.FolderPath,
                        host.Address,
                        host.Label,
                        host.State,
                        host.LastRoundTripMs,
                        host.ConsecutiveFailures,
                        host.LastReply,
                        host.OutageStarted);
                }
            }

            var sites = new List<SiteSnapshot>();
            foreach (var site in _sites)
            {
                var hosts = new List<HostSnapshot>();
                foreach (var address in site.Hosts)
                {
                    string key = BuildHostKey(site.Name, address);
                    if (runtime.TryGetValue(key, out var active))
                    {
                        hosts.Add(active);
                    }
                    else
                    {
                        site.Labels.TryGetValue(address, out var label);
                        hosts.Add(new HostSnapshot(
                            site.Name,
                            site.FolderPath,
                            address,
                            label ?? string.Empty,
                            HostState.Unknown,
                            null,
                            0,
                            null,
                            null));
                    }
                }
                sites.Add(new SiteSnapshot(site.Name, site.FolderPath, hosts));
            }

            IEnumerable<StateEventRecord> filteredEvents = _events;
            if (Config.EventHistoryHours > 0)
            {
                DateTime cutoff = DateTime.Now.AddHours(-Config.EventHistoryHours);
                filteredEvents = filteredEvents.Where(e => e.Timestamp >= cutoff);
            }
            if (Config.HideSuspectEvents)
                filteredEvents = filteredEvents.Where(e => !e.Kind.Equals("SUSPECT", StringComparison.OrdinalIgnoreCase));

            var selectedHosts = Config.SelectedSite is null
                ? sites.SelectMany(site => site.Hosts).ToList()
                : sites.Where(site => site.Name.Equals(Config.SelectedSite, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(site => site.Hosts)
                    .ToList();

            return new WatchdogSnapshot(
                Monitoring,
                Config.SelectedSite,
                sites,
                selectedHosts,
                filteredEvents.OrderBy(e => e.Timestamp).TakeLast(250).ToList(),
                _commands.TakeLast(400).ToList(),
                CloneConfigUnsafe());
        }
    }

    public void StartMonitoring()
    {
        lock (_gate)
        {
            if (Monitoring)
                return;
            if (_sites.Sum(site => site.Hosts.Count) == 0)
                return;

            _sessionToken = new CancellationTokenSource();
            _hosts.Clear();
            foreach (var old in _hostTokens.Values)
            {
                old.Cancel();
                old.Dispose();
            }
            _hostTokens.Clear();
            ReconcileUnsafe();
        }
        RaiseChanged();
    }

    public void StopMonitoring()
    {
        CancellationTokenSource? session;
        lock (_gate)
        {
            session = _sessionToken;
            _sessionToken = null;
            session?.Cancel();
            foreach (var token in _hostTokens.Values)
            {
                token.Cancel();
                token.Dispose();
            }
            _hostTokens.Clear();
        }
        session?.Dispose();
        RaiseChanged();
    }

    private void ReconcileUnsafe()
    {
        if (!Monitoring || _sessionToken is null)
            return;

        var desired = _sites
            .SelectMany(site => site.Hosts.Select(address => new
            {
                Site = site,
                Address = address,
                Key = BuildHostKey(site.Name, address)
            }))
            .ToDictionary(item => item.Key, item => item, StringComparer.OrdinalIgnoreCase);

        foreach (string key in _hosts.Keys.ToArray())
        {
            if (desired.ContainsKey(key))
                continue;
            if (_hostTokens.TryRemove(key, out var token))
            {
                token.Cancel();
                token.Dispose();
            }
            _hosts.TryRemove(key, out _);
        }

        foreach (var item in desired.Values)
        {
            if (_hosts.TryGetValue(item.Key, out var existing))
            {
                lock (existing)
                {
                    item.Site.Labels.TryGetValue(item.Address, out var label);
                    existing.Label = label ?? string.Empty;
                }
                continue;
            }

            item.Site.Labels.TryGetValue(item.Address, out var nickname);
            var runtime = new HostRuntime(
                item.Site.Name,
                item.Site.FolderPath,
                item.Address,
                nickname ?? string.Empty);
            _hosts[item.Key] = runtime;

            var token = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken.Token);
            _hostTokens[item.Key] = token;
            _ = MonitorHostAsync(item.Key, runtime, token.Token);
        }
    }

    private async Task MonitorHostAsync(string key, HostRuntime host, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool success = false;
            long? latency = null;
            string result;

            try
            {
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(host.Address, Config.PingTimeoutMs);
                success = reply.Status == IPStatus.Success;
                latency = success ? reply.RoundtripTime : null;
                result = success
                    ? $"Reply from {reply.Address}: time={reply.RoundtripTime}ms"
                    : $"FAILED ({reply.Status})";
            }
            catch (Exception ex) when (ex is PingException or InvalidOperationException or ArgumentException)
            {
                result = $"FAILED ({ex.GetType().Name})";
            }

            ProcessProbe(host, success, latency);
            AppendCommand(host, success, result);
            RaiseChanged();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Config.PingIntervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _hostTokens.TryRemove(key, out _);
    }

    private void ProcessProbe(HostRuntime host, bool success, long? latency)
    {
        string? eventKind = null;
        string? eventMessage = null;
        bool notify = false;

        lock (_gate)
        {
            lock (host)
            {
                if (success)
                {
                    host.LastRoundTripMs = latency;
                    host.LastReply = DateTime.Now;
                    host.ConsecutiveFailures = 0;

                    if (host.State == HostState.Offline)
                    {
                        host.ConsecutiveSuccesses++;
                        if (host.ConsecutiveSuccesses >= Config.RecoveryThreshold)
                        {
                            TimeSpan? outage = host.OutageStarted is null
                                ? null
                                : DateTime.Now - host.OutageStarted.Value;
                            host.State = HostState.Online;
                            host.OutageStarted = null;
                            host.ConsecutiveSuccesses = 0;
                            eventKind = "RECOVERED";
                            eventMessage = outage is null
                                ? $"{host.Address} recovered."
                                : $"{host.Address} recovered after {FormatDuration(outage.Value)}.";
                            notify = true;
                        }
                    }
                    else
                    {
                        host.State = HostState.Online;
                        host.ConsecutiveSuccesses = 0;
                    }
                }
                else
                {
                    host.LastRoundTripMs = null;
                    host.ConsecutiveSuccesses = 0;
                    host.ConsecutiveFailures++;

                    if (host.State != HostState.Offline && host.ConsecutiveFailures >= Config.FailureThreshold)
                    {
                        host.State = HostState.Offline;
                        host.OutageStarted ??= DateTime.Now;
                        eventKind = "DOWN";
                        eventMessage = $"{host.Address} declared offline after {host.ConsecutiveFailures} consecutive failures.";
                        notify = true;
                    }
                    else if (host.State is HostState.Unknown or HostState.Online)
                    {
                        host.State = HostState.Suspect;
                        eventKind = "SUSPECT";
                        eventMessage = $"{host.Address} missed a ping ({host.ConsecutiveFailures}/{Config.FailureThreshold}).";
                    }
                }

                if (eventKind is not null)
                {
                    string display = string.IsNullOrWhiteSpace(host.Label)
                        ? host.Address
                        : $"{host.Label} ({host.Address})";
                    _events.Add(new StateEventRecord(
                        DateTime.Now,
                        host.Site,
                        host.Address,
                        display,
                        eventKind,
                        eventMessage ?? eventKind));
                    PersistHistoryUnsafe();

                    if (notify && Config.NotificationsEnabled)
                        NotifyLinux($"Ping Watchdog • {eventKind}", $"{host.Site}: {display}");
                }
            }
        }
    }

    private void AppendCommand(HostRuntime host, bool success, string result)
    {
        lock (_gate)
        {
            string display = string.IsNullOrWhiteSpace(host.Label) ? string.Empty : $" [{host.Label}]";
            int timeoutSeconds = Math.Max(1, (int)Math.Ceiling(Config.PingTimeoutMs / 1000d));
            string text = $"{host.Address}: [{host.Site}]{display} [{DateTime.Now:HH:mm:ss}] ping -c 1 -W {timeoutSeconds} {host.Address} -> {result}";
            _commands.Add(new CommandLogEntry(DateTime.Now, host.Site, host.Address, host.Label, success, text));
            if (_commands.Count > MaxCommandEntries)
                _commands.RemoveRange(0, _commands.Count - MaxCommandEntries);
        }
    }

    private static void NotifyLinux(string title, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                ArgumentList = { title, message },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Desktop notifications are optional; monitoring must never depend on notify-send.
        }
    }

    public string? AddSite(string name, string folderPath = "")
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return "Enter a site name.";

        lock (_gate)
        {
            if (_sites.Any(site => site.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return "A site with that name already exists.";

            folderPath = NormalizeFolderPath(folderPath);
            EnsureFolderUnsafe(folderPath);
            _sites.Add(new SiteDefinition { Name = name, FolderPath = folderPath });
            Config.SelectedSite = name;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? RenameSite(string oldName, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName)) return "Enter a site name.";

        lock (_gate)
        {
            var site = FindSiteUnsafe(oldName);
            if (site is null) return "That site no longer exists.";
            if (_sites.Any(other => !ReferenceEquals(other, site) && other.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return "A site with that name already exists.";

            site.Name = newName;
            if (Config.SelectedSite?.Equals(oldName, StringComparison.OrdinalIgnoreCase) == true)
                Config.SelectedSite = newName;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? DeleteSite(string name)
    {
        lock (_gate)
        {
            var site = FindSiteUnsafe(name);
            if (site is null) return "That site no longer exists.";
            _sites.Remove(site);
            if (_sites.Count == 0)
                _sites.Add(new SiteDefinition { Name = "Default Site" });
            if (Config.SelectedSite?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                Config.SelectedSite = _sites[0].Name;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? SaveHosts(string siteName, string text)
    {
        lock (_gate)
        {
            var site = FindSiteUnsafe(siteName);
            if (site is null) return "That site no longer exists.";
            site.Hosts = NormalizeHosts(new[] { text });
            site.Labels = site.Labels
                .Where(pair => site.Hosts.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? SetLabel(string siteName, string host, string label)
    {
        lock (_gate)
        {
            var site = FindSiteUnsafe(siteName);
            if (site is null) return "That site no longer exists.";
            if (!site.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase)) return "That host no longer exists.";

            if (string.IsNullOrWhiteSpace(label))
                site.Labels.Remove(host);
            else
                site.Labels[host] = label.Trim();

            string key = BuildHostKey(siteName, host);
            if (_hosts.TryGetValue(key, out var runtime))
            {
                lock (runtime)
                    runtime.Label = label.Trim();
            }
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? AddFolder(string parent, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return "Enter a folder name.";
        if (name.Contains('/') || name.Contains('\\')) return "Folder names cannot contain / or \\.";

        lock (_gate)
        {
            string path = CombineFolder(parent, name);
            if (_folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                return "A folder with that name already exists here.";
            EnsureFolderUnsafe(path);
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? MoveSite(string siteName, string folderPath)
    {
        lock (_gate)
        {
            var site = FindSiteUnsafe(siteName);
            if (site is null) return "That site no longer exists.";
            folderPath = NormalizeFolderPath(folderPath);
            EnsureFolderUnsafe(folderPath);
            site.FolderPath = folderPath;
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? RenameFolder(string path, string newName)
    {
        path = NormalizeFolderPath(path);
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName)) return "Enter a folder name.";
        if (newName.Contains('/') || newName.Contains('\\')) return "Folder names cannot contain / or \\.";

        lock (_gate)
        {
            if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase)) return "That folder no longer exists.";
            string parent = ParentFolder(path);
            string replacement = CombineFolder(parent, newName);
            if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) && _folders.Contains(replacement, StringComparer.OrdinalIgnoreCase))
                return "A folder with that name already exists here.";
            RewriteFolderUnsafe(path, replacement);
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? MoveFolder(string path, string newParent)
    {
        path = NormalizeFolderPath(path);
        newParent = NormalizeFolderPath(newParent);

        lock (_gate)
        {
            if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase)) return "That folder no longer exists.";
            if (!string.IsNullOrWhiteSpace(newParent) && !_folders.Contains(newParent, StringComparer.OrdinalIgnoreCase)) return "Destination folder not found.";
            if (newParent.Equals(path, StringComparison.OrdinalIgnoreCase) || newParent.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) return "A folder cannot be moved inside itself.";

            string replacement = CombineFolder(newParent, LeafFolder(path));
            if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) && _folders.Contains(replacement, StringComparer.OrdinalIgnoreCase))
                return "A folder with that name already exists in the destination.";
            RewriteFolderUnsafe(path, replacement);
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? DeleteFolder(string path)
    {
        path = NormalizeFolderPath(path);
        lock (_gate)
        {
            if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase)) return "That folder no longer exists.";
            RewriteFolderUnsafe(path, ParentFolder(path));
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    private void RewriteFolderUnsafe(string oldPath, string replacement)
    {
        oldPath = NormalizeFolderPath(oldPath);
        replacement = NormalizeFolderPath(replacement);
        var original = _folders.ToList();
        _folders.Clear();

        foreach (var folder in original)
        {
            string rewritten = folder;
            if (folder.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                rewritten = replacement;
            else if (folder.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                rewritten = CombineFolder(replacement, folder[(oldPath.Length + 1)..]);
            EnsureFolderUnsafe(rewritten);
        }

        foreach (var site in _sites)
        {
            if (site.FolderPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                site.FolderPath = replacement;
            else if (site.FolderPath.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                site.FolderPath = CombineFolder(replacement, site.FolderPath[(oldPath.Length + 1)..]);
        }
    }

    private static string ParentFolder(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string LeafFolder(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string CombineFolder(string parent, string child)
    {
        parent = NormalizeFolderPath(parent);
        child = NormalizeFolderPath(child);
        return string.IsNullOrWhiteSpace(parent) ? child : NormalizeFolderPath($"{parent}/{child}");
    }

    private void EnsureFolderUnsafe(string? path)
    {
        path = NormalizeFolderPath(path);
        if (string.IsNullOrWhiteSpace(path)) return;

        string current = string.Empty;
        foreach (string part in path.Split('/'))
        {
            current = CombineFolder(current, part);
            if (!_folders.Contains(current, StringComparer.OrdinalIgnoreCase))
                _folders.Add(current);
        }
    }

    public string? ApplySettings(WatchdogConfig settings)
    {
        lock (_gate)
        {
            if (Monitoring &&
                (settings.PingIntervalSeconds != Config.PingIntervalSeconds ||
                 settings.PingTimeoutMs != Config.PingTimeoutMs ||
                 settings.FailureThreshold != Config.FailureThreshold ||
                 settings.RecoveryThreshold != Config.RecoveryThreshold))
            {
                return "Stop monitoring before changing ping timing or outage thresholds.";
            }

            Config.PingIntervalSeconds = Math.Clamp(settings.PingIntervalSeconds, 1, 300);
            Config.PingTimeoutMs = Math.Clamp(settings.PingTimeoutMs, 250, 10000);
            Config.FailureThreshold = Math.Clamp(settings.FailureThreshold, 2, 20);
            Config.RecoveryThreshold = Math.Clamp(settings.RecoveryThreshold, 1, 20);
            Config.ShowCommandView = settings.ShowCommandView;
            Config.EventHistoryHours = NormalizeHistoryHours(settings.EventHistoryHours);
            Config.HideSuspectEvents = settings.HideSuspectEvents;
            Config.AutoCheckUpdates = settings.AutoCheckUpdates;
            Config.MinimizeToTray = settings.MinimizeToTray;
            Config.NotificationsEnabled = settings.NotificationsEnabled;
            Config.WallboardShowCli = settings.WallboardShowCli;
            Config.ShowUpdateControlOnHome = settings.ShowUpdateControlOnHome;
            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public void ClearCommandLog()
    {
        lock (_gate)
            _commands.Clear();
        RaiseChanged();
    }

    public string ExportConfigJson()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(CloneConfigUnsafe(), new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public string? ImportConfigJson(string json)
    {
        WatchdogConfig candidate;
        try
        {
            var loaded = JsonSerializer.Deserialize<WatchdogConfig>(json);
            if (loaded is null) return "The configuration file was empty.";
            candidate = SanitizeConfig(loaded);
            if (candidate.Sites.Count == 0) return "The configuration contains no valid sites.";
            if (candidate.SelectedSite is not null && !candidate.Sites.Any(s => s.Name.Equals(candidate.SelectedSite, StringComparison.OrdinalIgnoreCase)))
                candidate.SelectedSite = null;
        }
        catch (Exception ex)
        {
            return $"Could not read configuration: {ex.Message}";
        }

        bool restart;
        lock (_gate)
        {
            restart = Monitoring;
            if (restart)
                StopMonitoring();

            Config = candidate;
            _hosts.Clear();
            _sites.Clear();
            _sites.AddRange(Config.Sites);
            _folders.Clear();
            foreach (var folder in Config.SiteFolders)
                EnsureFolderUnsafe(folder);
            foreach (var site in _sites)
                EnsureFolderUnsafe(site.FolderPath);
            Config.Sites = _sites;
            Config.SiteFolders = _folders;
            SaveUnsafe();
        }

        if (restart)
            StartMonitoring();
        RaiseChanged();
        return null;
    }

    public void Save()
    {
        lock (_gate)
            SaveUnsafe();
    }

    private void SaveUnsafe()
    {
        Directory.CreateDirectory(_configDirectory);
        Config.Sites = _sites;
        Config.SiteFolders = _folders
            .Select(NormalizeFolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string temp = _configPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(CloneConfigUnsafe(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _configPath, true);
    }

    private WatchdogConfig CloneConfigUnsafe()
    {
        return new WatchdogConfig
        {
            FormatVersion = Config.FormatVersion,
            Sites = _sites.Select(CloneSite).ToList(),
            SiteFolders = _folders.ToList(),
            PingIntervalSeconds = Config.PingIntervalSeconds,
            PingTimeoutMs = Config.PingTimeoutMs,
            FailureThreshold = Config.FailureThreshold,
            RecoveryThreshold = Config.RecoveryThreshold,
            ShowCommandView = Config.ShowCommandView,
            SelectedSite = Config.SelectedSite,
            EventHistoryHours = Config.EventHistoryHours,
            HideSuspectEvents = Config.HideSuspectEvents,
            AutoCheckUpdates = Config.AutoCheckUpdates,
            MinimizeToTray = Config.MinimizeToTray,
            NotificationsEnabled = Config.NotificationsEnabled,
            WallboardShowCli = Config.WallboardShowCli,
            ShowUpdateControlOnHome = Config.ShowUpdateControlOnHome
        };
    }

    private static SiteDefinition CloneSite(SiteDefinition site) => new()
    {
        Name = site.Name,
        FolderPath = site.FolderPath,
        Hosts = site.Hosts.ToList(),
        Labels = new Dictionary<string, string>(site.Labels, StringComparer.OrdinalIgnoreCase)
    };

    private SiteDefinition? FindSiteUnsafe(string name) =>
        _sites.FirstOrDefault(site => site.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void LoadHistoryUnsafe()
    {
        _events.Clear();
        try
        {
            if (!File.Exists(_historyPath)) return;
            var loaded = JsonSerializer.Deserialize<List<StateEventRecord>>(File.ReadAllText(_historyPath));
            if (loaded is not null)
                _events.AddRange(loaded.OrderBy(e => e.Timestamp));
        }
        catch
        {
            // A bad history file must never stop monitoring.
        }
    }

    private void PersistHistoryUnsafe()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string temp = _historyPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_events));
            File.Move(temp, _historyPath, true);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static string BuildHostKey(string site, string address) => $"{site}\u001f{address}";

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        return $"{Math.Max(1, (int)span.TotalSeconds)}s";
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}

internal sealed class LinuxUpdateService : IDisposable
{
    private const string RepositoryUrl = "https://github.com/JPLuker/Ping-Watchdog";
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateManager? _pendingManager;
    private UpdateInfo? _pendingUpdate;
    private bool _checking;

    public event EventHandler? Changed;
    public event EventHandler<string>? UpdateReady;
    public string Status { get; private set; } = "Ready";
    public string ActionText => _pendingUpdate is null ? "Check for Updates" : "Restart to Update";
    public bool HasPendingUpdate => _pendingUpdate is not null;
    public string Version => Assembly.GetExecutingAssembly().GetName().Version is { } v
        ? $"v{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}"
        : "Linux";

    public async Task CheckAsync(bool userInitiated)
    {
        if (_checking || _pendingUpdate is not null)
            return;

        _checking = true;
        Status = "Checking GitHub Releases...";
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            var source = new GithubSource(RepositoryUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                Status = "Unmanaged AppImage / development build";
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Status = "Up to date";
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            Status = $"Downloading {version}...";
            Changed?.Invoke(this, EventArgs.Empty);

            await manager.DownloadUpdatesAsync(update, progress =>
            {
                Status = $"Downloading {progress}%";
                Changed?.Invoke(this, EventArgs.Empty);
            });

            _pendingManager = manager;
            _pendingUpdate = update;
            Status = $"{version} downloaded • restart ready";
            Changed?.Invoke(this, EventArgs.Empty);
            UpdateReady?.Invoke(this, version);
        }
        catch (Exception ex)
        {
            Status = userInitiated ? $"Update check failed: {ex.Message}" : "Background update check failed";
        }
        finally
        {
            _checking = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RunBackgroundAsync(Func<bool> enabled)
    {
        try
        {
            if (enabled())
                await CheckAsync(false);

            while (await Task.Delay(TimeSpan.FromHours(6), _lifetime.Token).ContinueWith(
                task => !task.IsCanceled,
                CancellationToken.None))
            {
                if (enabled())
                    await CheckAsync(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void ApplyAndRestart(WatchdogEngine engine)
    {
        if (_pendingManager is null || _pendingUpdate is null)
            return;
        LinuxUpdateRestart.Apply(engine, () => _pendingManager.ApplyUpdatesAndRestart(_pendingUpdate));
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

internal static class LinuxUpdateRestart
{
    public static void Apply(WatchdogEngine engine, Action applyUpdate)
    {
        engine.Save();
        bool wasMonitoring = engine.Monitoring;
        engine.StopMonitoring();
        try
        {
            applyUpdate();
        }
        catch
        {
            if (wasMonitoring) engine.StartMonitoring();
            throw;
        }
    }
}

internal static class LinuxSelfTest
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ping-watchdog-linux-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var engine = new WatchdogEngine(root))
            {
                Check(WatchdogEngine.NormalizeFolderPath(@" Indiana\\North / Branch ") == "Indiana/North/Branch");
                Check(WatchdogEngine.NormalizeHosts(new[] { "10.0.0.1, 10.0.0.1\n8.8.8.8" }).Count == 2);
                Check(engine.AddFolder("", "Indiana") is null);
                Check(engine.AddFolder("Indiana", "North") is null);
                Check(engine.AddSite("Test Site", "Indiana/North") is null);
                Check(engine.SaveHosts("Test Site", "127.0.0.1\n8.8.8.8") is null);
                Check(engine.SetLabel("Test Site", "127.0.0.1", "Loopback") is null);
                var snapshot = engine.Snapshot();
                Check(snapshot.Sites.Any(site => site.Name == "Test Site" && site.FolderPath == "Indiana/North"));
                Check(snapshot.Hosts.Any(host => host.Address == "127.0.0.1" && host.Label == "Loopback"));
                Check(engine.ExportConfigJson().Contains("Indiana/North", StringComparison.Ordinal));
                Check(File.Exists(engine.ConfigPath));
            }

            using (var reloaded = new WatchdogEngine(root))
            {
                var snapshot = reloaded.Snapshot();
                Check(snapshot.Sites.Any(site => site.Name == "Test Site"));
                Check(snapshot.Sites.SelectMany(site => site.Hosts).Any(host => host.Label == "Loopback"));
                Check(reloaded.MoveSite("Test Site", "") is null);
                Check(reloaded.RenameFolder("Indiana/North", "Northwest") is null);
                Check(reloaded.DeleteFolder("Indiana/Northwest") is null);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Linux self-test assertion failed.");
    }
}
