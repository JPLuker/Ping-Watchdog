// Monitoring engine: sites, hosts and their options, the ping loops, outage history and Undo.
// Every edit goes through here so the main window, Wallboard and tray always agree.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace PingWatchdog.Linux;

internal sealed class HostRuntime
{
    public HostRuntime(string site, string folderPath, string address, string label, HostOptions options)
    {
        Site = site;
        FolderPath = folderPath;
        Address = address;
        Label = label;
        Options = options;
    }

    public string Site { get; }
    public string FolderPath { get; }
    public string Address { get; }
    public string Label { get; set; }
    public HostOptions Options { get; set; }
    public HostState State { get; set; } = HostState.Unknown;
    public long? LastRoundTripMs { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public DateTime? LastReply { get; set; }
    public DateTime? OutageStarted { get; set; }
}

internal sealed class WatchdogEngine : IDisposable
{
    internal const string QuickMonitorSite = "Quick Monitor";
    private const int MaxCommandEntries = 2500;
    private const int MaxUndo = 30;
    private readonly object _gate = new();
    private readonly List<SiteDefinition> _sites = new();
    private readonly List<string> _folders = new();
    private readonly List<StateEventRecord> _events = new();
    private readonly List<CommandLogEntry> _commands = new();
    private readonly List<(string Description, List<SiteDefinition> Sites)> _undo = new();
    private readonly ConcurrentDictionary<string, HostRuntime> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _hostTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _configDirectory;
    private readonly string _stateDirectory;
    private readonly string _configPath;
    private readonly string _historyPath;
    private readonly string _preferencesPath;
    private CancellationTokenSource? _sessionToken;
    private Timer? _snoozeTimer;
    private bool _disposed;
    private readonly Func<string, int, CancellationToken, Task<ProbeResult>> _probe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public WatchdogEngine(string? storageRoot = null, Func<string, int, CancellationToken, Task<ProbeResult>>? probe = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _probe = probe ?? NativeProbeAsync;
        _delay = delay ?? Task.Delay;
        _configDirectory = storageRoot ?? ResolveConfigDirectory();
        _stateDirectory = ResolveStateDirectory(storageRoot);
        _configPath = Path.Combine(_configDirectory, "autosave.pingwatch.json");
        _historyPath = Path.Combine(_stateDirectory, "event-history.json");
        _preferencesPath = Path.Combine(_configDirectory, "preferences.json");
        Config = new WatchdogConfig();
        Preferences = LinuxPreferences.Load(_preferencesPath);
        Load();
    }

    public event EventHandler? Changed;
    public event Action<PriorityStateChange>? PriorityStateChanged;
    // Desktop notifications go through this so tests and headless runs can replace notify-send.
    public Action<string, string, bool> Notify { get; set; } = NotifyLinux;
    public WatchdogConfig Config { get; private set; }
    public LinuxPreferences Preferences { get; private set; }
    public bool Monitoring => _sessionToken is not null && !_sessionToken.IsCancellationRequested;
    public string ConfigPath => _configPath;
    public string HistoryPath => _historyPath;
    public string PreferencesPath => _preferencesPath;
    public string ConfigDirectory => _configDirectory;
    public string StateDirectory => _stateDirectory;
    public string? UndoDescription { get { lock (_gate) return _undo.Count == 0 ? null : _undo[^1].Description; } }

    public string? SelectedSite
    {
        get => Config.SelectedSite;
        set
        {
            lock (_gate)
            {
                Config.SelectedSite = value is not null && FindSiteUnsafe(value) is null ? null : FindSiteUnsafe(value ?? "")?.Name;
                SaveUnsafe();
            }
            RaiseChanged();
        }
    }

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

    public void SavePreferences(LinuxPreferences preferences)
    {
        lock (_gate)
        {
            preferences.Save(_preferencesPath);
            Preferences = preferences.Copy();
        }
        RaiseChanged();
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Keep the unreadable file for recovery instead of overwriting it with defaults.
                try { File.Copy(_configPath, _configPath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), false); }
                catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { }
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
                catch (Exception ex) when (ex is IOException or JsonException)
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
            DisplayTime.Use12HourTime = Config.Use12HourTime;

            if (Config.SelectedSite is not null && FindSiteUnsafe(Config.SelectedSite) is null)
                Config.SelectedSite = null;
            if (!Preferences.RestoreLastSiteOnStartup)
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
            Use12HourTime = source.Use12HourTime,
            AutoCheckUpdates = source.AutoCheckUpdates,
            MinimizeToTray = source.MinimizeToTray,
            NotificationsEnabled = source.NotificationsEnabled,
            WallboardShowCli = source.WallboardShowCli,
            ShowUpdateControlOnHome = source.ShowUpdateControlOnHome,
            PriorityAlarmSound = source.PriorityAlarmSound ?? ""
        };

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in source.Sites ?? new List<SiteDefinition>())
        {
            if (raw is null) continue;
            string name = raw.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                continue;

            var hosts = NormalizeHosts(raw.Hosts ?? new List<string>());
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var details = new Dictionary<string, HostOptions>(StringComparer.OrdinalIgnoreCase);
            var rawLabels = raw.Labels is null ? null : new Dictionary<string, string>(raw.Labels, StringComparer.OrdinalIgnoreCase);
            var rawCategories = raw.Categories is null ? null : new Dictionary<string, string>(raw.Categories, StringComparer.OrdinalIgnoreCase);
            var rawDetails = raw.HostDetails is null ? null : new Dictionary<string, HostOptions>(raw.HostDetails, StringComparer.OrdinalIgnoreCase);
            foreach (var host in hosts)
            {
                if (rawLabels is not null && rawLabels.TryGetValue(host, out var label) && !string.IsNullOrWhiteSpace(label))
                    labels[host] = label.Trim();
                if (rawCategories is not null && rawCategories.TryGetValue(host, out var category) && !string.IsNullOrWhiteSpace(category))
                    categories[host] = category.Trim();
                if (rawDetails is not null && rawDetails.TryGetValue(host, out var options) && options is not null)
                    details[host] = SanitizeOptions(options);
            }

            config.Sites.Add(new SiteDefinition
            {
                Name = name,
                FolderPath = NormalizeFolderPath(raw.FolderPath),
                Hosts = hosts,
                Labels = labels,
                Categories = categories,
                HostDetails = details
            });
        }

        config.SiteFolders = (source.SiteFolders ?? new List<string>())
            .Where(path => path is not null)
            .Select(NormalizeFolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return config;
    }

    private static HostOptions SanitizeOptions(HostOptions source)
    {
        var options = source.Copy();
        if (string.IsNullOrWhiteSpace(options.Id)) options.Id = Guid.NewGuid().ToString("N");
        options.Tags ??= "";
        options.IntervalSeconds = options.IntervalSeconds <= 0 ? 0 : Math.Clamp(options.IntervalSeconds, 1, 300);
        options.TimeoutMs = options.TimeoutMs <= 0 ? 0 : Math.Clamp(options.TimeoutMs, 250, 10000);
        options.FailureThreshold = options.FailureThreshold <= 0 ? 0 : Math.Clamp(options.FailureThreshold, 2, 20);
        return options;
    }

    public static List<string> NormalizeHosts(IEnumerable<string> hosts)
    {
        return hosts
            .Where(host => host is not null)
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
            return _sites.Select(CloneSite).ToList();
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
            return _sites.Where(s => !s.Temporary).Select(s => s.Name).Concat(_events.Select(e => e.Site))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public WatchdogSnapshot Snapshot()
    {
        lock (_gate)
        {
            bool monitoring = Monitoring;
            var runtime = new Dictionary<string, HostSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _hosts)
            {
                lock (pair.Value)
                {
                    var host = pair.Value;
                    runtime[pair.Key] = new HostSnapshot(host.Site, host.FolderPath, host.Address, host.Label, host.State,
                        host.LastRoundTripMs, host.ConsecutiveFailures, host.LastReply, host.OutageStarted);
                }
            }

            var sites = new List<SiteSnapshot>();
            foreach (var site in _sites)
            {
                var hosts = new List<HostSnapshot>();
                foreach (var address in site.Hosts)
                {
                    site.Labels.TryGetValue(address, out var label);
                    site.Categories.TryGetValue(address, out var group);
                    var options = OptionsUnsafe(site, address).Copy();
                    var live = runtime.TryGetValue(BuildHostKey(site.Name, address), out var active)
                        ? active
                        : new HostSnapshot(site.Name, site.FolderPath, address, label ?? "", HostState.Unknown, null, 0, null, null);
                    hosts.Add(live with
                    {
                        Label = label ?? "",
                        Group = group ?? "",
                        Options = options,
                        Temporary = site.Temporary,
                        Monitoring = monitoring
                    });
                }
                sites.Add(new SiteSnapshot(site.Name, site.FolderPath, hosts, site.Temporary));
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
                monitoring,
                Config.SelectedSite,
                sites,
                selectedHosts,
                filteredEvents.OrderBy(e => e.Timestamp).TakeLast(250).ToList(),
                _commands.TakeLast(400).ToList(),
                CloneConfigUnsafe());
        }
    }

    // ---- Monitoring ----

    public bool HasProbeTargets()
    {
        lock (_gate)
            return _sites.Any(site => site.Hosts.Any(address => ProbeAllowedUnsafe(site, address)));
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
            // Snoozes end on their own; pick hosts back up when they do.
            _snoozeTimer = new Timer(_ => ReconcileSnoozes(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
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
            _snoozeTimer?.Dispose();
            _snoozeTimer = null;
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

    private void ReconcileSnoozes()
    {
        bool changed;
        lock (_gate)
        {
            if (!Monitoring) return;
            int before = _hosts.Count;
            var keys = _hosts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            ReconcileUnsafe();
            changed = before != _hosts.Count || !keys.SetEquals(_hosts.Keys);
        }
        if (changed) RaiseChanged();
    }

    internal void ReconcileForTest() => ReconcileSnoozes();

    private bool ProbeAllowedUnsafe(SiteDefinition site, string address)
    {
        var options = OptionsUnsafe(site, address);
        return options.Enabled && !options.IsSnoozed(DateTimeOffset.UtcNow);
    }

    private void ReconcileUnsafe()
    {
        if (!Monitoring || _sessionToken is null)
            return;

        var desired = _sites
            .SelectMany(site => site.Hosts.Where(address => ProbeAllowedUnsafe(site, address)).Select(address => new
            {
                Site = site,
                Address = address,
                Key = BuildHostKey(site.Name, address)
            }))
            .ToDictionary(item => item.Key, item => item, StringComparer.OrdinalIgnoreCase);

        foreach (string key in _hosts.Keys.ToArray())
        {
            if (desired.TryGetValue(key, out var wanted) &&
                _hosts.TryGetValue(key, out var running) &&
                running.FolderPath.Equals(wanted.Site.FolderPath, StringComparison.Ordinal))
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
            item.Site.Labels.TryGetValue(item.Address, out var nickname);
            var options = OptionsUnsafe(item.Site, item.Address).Copy();
            if (_hosts.TryGetValue(item.Key, out var existing))
            {
                lock (existing)
                {
                    existing.Label = nickname ?? string.Empty;
                    existing.Options = options;
                }
                continue;
            }

            var runtime = new HostRuntime(item.Site.Name, item.Site.FolderPath, item.Address, nickname ?? string.Empty, options);
            _hosts[item.Key] = runtime;

            var token = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken.Token);
            _hostTokens[item.Key] = token;
            _ = MonitorHostAsync(item.Key, runtime, token);
        }
    }

    private static async Task<ProbeResult> NativeProbeAsync(string address, int timeout, CancellationToken token)
    {
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(address, timeout).WaitAsync(token);
            bool success = reply.Status == IPStatus.Success;
            return new ProbeResult(success, success ? reply.RoundtripTime : null,
                success ? $"Reply from {reply.Address}: time={reply.RoundtripTime}ms" : $"FAILED ({reply.Status})");
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or ArgumentException)
        {
            return new ProbeResult(false, null, $"FAILED ({ex.GetType().Name})");
        }
    }

    private int TimeoutFor(HostRuntime host) => host.Options.TimeoutMs > 0 ? host.Options.TimeoutMs : Config.PingTimeoutMs;
    private int IntervalFor(HostRuntime host) => host.Options.IntervalSeconds > 0 ? host.Options.IntervalSeconds : Config.PingIntervalSeconds;
    private int FailureThresholdFor(HostRuntime host) => host.Options.FailureThreshold > 0 ? host.Options.FailureThreshold : Config.FailureThreshold;

    private async Task MonitorHostAsync(string key, HostRuntime host, CancellationTokenSource source)
    {
        // Keep a token value: the owner may dispose its source while a probe is pending.
        CancellationToken token = source.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                int timeout;
                lock (host) timeout = TimeoutFor(host);
                ProbeResult result = await _probe(host.Address, timeout, token);
                lock (_gate)
                {
                    // A stopped, removed, or superseded host must never publish a late reply.
                    if (token.IsCancellationRequested || !Monitoring || !_hosts.TryGetValue(key, out var current) || !ReferenceEquals(current, host)) break;
                    ProcessProbe(host, result.Success, result.LatencyMs);
                    AppendCommand(host, result.Success, result.Text, timeout);
                }
                RaiseChanged();
                int interval;
                lock (host) interval = IntervalFor(host);
                await _delay(TimeSpan.FromSeconds(interval), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            // An old loop cannot remove the token belonging to a restarted host.
            ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_hostTokens).Remove(new(key, source));
        }
    }

    private void ProcessProbe(HostRuntime host, bool success, long? latency)
    {
        string? eventKind = null;
        string? eventMessage = null;
        bool notify = false;
        PriorityStateChange? priority = null;

        lock (_gate)
        {
            lock (host)
            {
                int threshold = FailureThresholdFor(host);
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
                            TimeSpan? outage = host.OutageStarted is null ? null : DateTime.Now - host.OutageStarted.Value;
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

                    if (host.State != HostState.Offline && host.ConsecutiveFailures >= threshold)
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
                        eventMessage = $"{host.Address} missed a ping ({host.ConsecutiveFailures}/{threshold}).";
                    }
                }

                if (eventKind is not null)
                {
                    string display = string.IsNullOrWhiteSpace(host.Label) ? host.Address : $"{host.Label} ({host.Address})";
                    var now = DateTime.Now;
                    _events.Add(new StateEventRecord(now, host.Site, host.Address, display, eventKind, eventMessage ?? eventKind));
                    PersistHistoryUnsafe();

                    if (notify && host.Options.Priority)
                    {
                        // Priority hosts ring until acknowledged and replace the ordinary notification.
                        priority = new PriorityStateChange(
                            new HostSnapshot(host.Site, host.FolderPath, host.Address, host.Label, host.State, host.LastRoundTripMs,
                                host.ConsecutiveFailures, host.LastReply, host.OutageStarted) { Options = host.Options.Copy(), Monitoring = true },
                            eventKind == "DOWN", now);
                    }
                    else if (notify && Config.NotificationsEnabled)
                    {
                        Notify(eventKind == "DOWN" ? $"{display} is offline" : $"{display} is back online", host.Site, false);
                    }
                }
            }
        }
        if (priority is not null) PriorityStateChanged?.Invoke(priority);
    }

    private void AppendCommand(HostRuntime host, bool success, string result, int timeoutMs)
    {
        lock (_gate)
        {
            string display = string.IsNullOrWhiteSpace(host.Label) ? string.Empty : $" [{host.Label}]";
            int timeoutSeconds = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000d));
            string clock = DisplayTime.Clock(DateTime.Now);
            string text = $"{host.Address}: [{host.Site}]{display} [{clock}] ping -c 1 -W {timeoutSeconds} {host.Address}  ->  {result}";
            _commands.Add(new CommandLogEntry(DateTime.Now, host.Site, host.Address, host.Label, success, text));
            if (_commands.Count > MaxCommandEntries)
                _commands.RemoveRange(0, _commands.Count - MaxCommandEntries);
        }
    }

    private static void NotifyLinux(string title, string message, bool critical)
    {
        try
        {
            var start = new ProcessStartInfo { FileName = "notify-send", UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--app-name=Ping Watchdog");
            if (critical) start.ArgumentList.Add("--urgency=critical");
            start.ArgumentList.Add(title);
            start.ArgumentList.Add(message);
            using var process = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Desktop notifications are optional; monitoring must never depend on notify-send.
        }
    }

    // ---- Sites and folders ----

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
            site.Temporary = false;
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
            PushUndoUnsafe($"Deleted {site.Name}");
            _sites.Remove(site);
            if (_sites.Count(s => !s.Temporary) == 0)
                _sites.Add(new SiteDefinition { Name = "Default Site" });
            if (Config.SelectedSite?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                Config.SelectedSite = null;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? AddFolder(string parent, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return "Enter a folder name.";
        if (name.Contains('/') || name.Contains('\\')) return "Folder names can't contain / or \\.";

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
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? RenameFolder(string path, string newName)
    {
        path = NormalizeFolderPath(path);
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName)) return "Enter a folder name.";
        if (newName.Contains('/') || newName.Contains('\\')) return "Folder names can't contain / or \\.";

        lock (_gate)
        {
            if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase)) return "That folder no longer exists.";
            string parent = ParentFolder(path);
            string replacement = CombineFolder(parent, newName);
            if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) && _folders.Contains(replacement, StringComparer.OrdinalIgnoreCase))
                return "A folder with that name already exists here.";
            RewriteFolderUnsafe(path, replacement);
            SaveUnsafe();
            ReconcileUnsafe();
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
            if (newParent.Equals(path, StringComparison.OrdinalIgnoreCase) || newParent.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) return "A folder can't be moved inside itself.";

            string replacement = CombineFolder(newParent, LeafFolder(path));
            if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) && _folders.Contains(replacement, StringComparer.OrdinalIgnoreCase))
                return "A folder with that name already exists in the destination.";
            RewriteFolderUnsafe(path, replacement);
            SaveUnsafe();
            ReconcileUnsafe();
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
            ReconcileUnsafe();
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

    // ---- Hosts ----

    public string? SaveHosts(string siteName, string text)
    {
        lock (_gate)
        {
            var site = FindSiteUnsafe(siteName);
            if (site is null) return "That site no longer exists.";
            PushUndoUnsafe($"Edited {site.Name}");
            site.Hosts = NormalizeHosts(new[] { text });
            PruneUnsafe(site);
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    // Adds imported rows to a site. Rows already in the site keep their options; a new label or group replaces the old one.
    public (int Added, string? Error) AddHosts(string siteName, IEnumerable<ImportedHost> rows, bool createSite = false)
    {
        int added = 0;
        lock (_gate)
        {
            var site = FindSiteUnsafe(siteName);
            if (site is null && createSite)
            {
                if (string.IsNullOrWhiteSpace(siteName)) return (0, "Enter a site name.");
                site = new SiteDefinition { Name = siteName.Trim() };
                _sites.Add(site);
            }
            if (site is null) return (0, "That site no longer exists.");
            var valid = rows.Where(r => string.IsNullOrEmpty(r.Error) && !string.IsNullOrWhiteSpace(r.Address)).ToList();
            if (valid.Count == 0) return (0, "There are no valid hosts to add.");
            PushUndoUnsafe($"Added {Hosts(valid.Count)} to {site.Name}");
            foreach (var row in valid)
            {
                string address = HostImport.Normalize(row.Address);
                string? existing = site.Hosts.FirstOrDefault(h => h.Equals(address, StringComparison.OrdinalIgnoreCase));
                if (existing is null) { site.Hosts.Add(address); existing = address; added++; }
                if (!string.IsNullOrWhiteSpace(row.Label)) site.Labels[existing] = row.Label.Trim();
                if (!string.IsNullOrWhiteSpace(row.Group)) site.Categories[existing] = row.Group.Trim();
            }
            if (!site.Temporary) Config.SelectedSite = site.Name;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return (added, null);
    }

    // Quick Monitor: hosts watched for this session only, then kept or forgotten.
    public (int Added, string? Error) StartQuickMonitor(IEnumerable<ImportedHost> rows)
    {
        lock (_gate)
        {
            if (FindSiteUnsafe(QuickMonitorSite) is { Temporary: false })
                return (0, $"Rename the saved site called {QuickMonitorSite} first.");
            if (FindSiteUnsafe(QuickMonitorSite) is null)
                _sites.Add(new SiteDefinition { Name = QuickMonitorSite, Temporary = true });
        }
        var result = AddHosts(QuickMonitorSite, rows);
        if (result.Error is null)
        {
            lock (_gate) Config.SelectedSite = QuickMonitorSite;
            if (!Monitoring) StartMonitoring();
            RaiseChanged();
        }
        return result;
    }

    // Moves temporary hosts into a saved site, keeping labels and options.
    public string? KeepTemporaryHosts(IReadOnlyCollection<HostKey> keys, string destination, bool createSite)
    {
        return MoveHosts(keys, destination, createSite, keep: true);
    }

    public string? EditHost(HostKey key, string newAddress, string label)
    {
        newAddress = newAddress.Trim();
        if (!HostImport.Valid(newAddress)) return "Enter a valid IP address or hostname.";
        newAddress = HostImport.Normalize(newAddress);
        lock (_gate)
        {
            var site = FindSiteUnsafe(key.Site);
            string? address = site?.Hosts.FirstOrDefault(h => h.Equals(key.Address, StringComparison.OrdinalIgnoreCase));
            if (site is null || address is null) return "That host no longer exists.";
            bool moved = !newAddress.Equals(address, StringComparison.OrdinalIgnoreCase);
            if (moved && site.Hosts.Any(h => h.Equals(newAddress, StringComparison.OrdinalIgnoreCase)))
                return $"{newAddress} is already in {site.Name}.";
            site.Labels.TryGetValue(address, out var oldLabel);
            if (!moved && (oldLabel ?? "") == label.Trim()) return null;
            PushUndoUnsafe(moved ? $"Changed {address} to {newAddress}" : $"Named {address}");
            if (moved)
            {
                int index = site.Hosts.IndexOf(address);
                site.Hosts[index] = newAddress;
                if (site.Categories.Remove(address, out var group)) site.Categories[newAddress] = group;
                if (site.HostDetails.Remove(address, out var options)) site.HostDetails[newAddress] = options;
                site.Labels.Remove(address);
            }
            if (string.IsNullOrWhiteSpace(label)) site.Labels.Remove(newAddress);
            else site.Labels[newAddress] = label.Trim();
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
            PushUndoUnsafe(string.IsNullOrWhiteSpace(label) ? $"Cleared the name for {host}" : $"Named {host}");
            if (string.IsNullOrWhiteSpace(label))
                site.Labels.Remove(host);
            else
                site.Labels[host] = label.Trim();
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? SetCategory(string siteName, string host, string category) =>
        UpdateHosts(new[] { new HostKey(siteName, host) }, string.IsNullOrWhiteSpace(category) ? "Cleared group" : "Set group",
            (site, address) =>
            {
                if (string.IsNullOrWhiteSpace(category)) site.Categories.Remove(address);
                else site.Categories[address] = category.Trim();
            });

    public string? SetGroup(IReadOnlyCollection<HostKey> keys, string group) =>
        UpdateHosts(keys, group.Trim().Length == 0 ? $"Cleared group on {Hosts(keys.Count)}" : $"Set group on {Hosts(keys.Count)}",
            (site, address) =>
            {
                if (string.IsNullOrWhiteSpace(group)) site.Categories.Remove(address);
                else site.Categories[address] = group.Trim();
            });

    public string? SetEnabled(IReadOnlyCollection<HostKey> keys, bool enabled) =>
        UpdateOptions(keys, (enabled ? "Enabled " : "Disabled ") + Hosts(keys.Count), o => o.Enabled = enabled);

    public string? SetPriority(IReadOnlyCollection<HostKey> keys, bool priority) =>
        UpdateOptions(keys, (priority ? "Marked priority on " : "Removed priority from ") + Hosts(keys.Count), o => o.Priority = priority);

    public string? SetSnooze(IReadOnlyCollection<HostKey> keys, DateTimeOffset? until) =>
        UpdateOptions(keys, until is null ? "Resumed " + Hosts(keys.Count) : "Snoozed " + Hosts(keys.Count), o => o.SnoozedUntilUtc = until);

    // Zero means "use the default" for each value, as on Windows.
    public string? SetMonitoring(IReadOnlyCollection<HostKey> keys, int intervalSeconds, int timeoutMs, int failureThreshold) =>
        UpdateOptions(keys, "Changed monitoring for " + Hosts(keys.Count), o =>
        {
            o.IntervalSeconds = intervalSeconds <= 0 ? 0 : Math.Clamp(intervalSeconds, 1, 300);
            o.TimeoutMs = timeoutMs <= 0 ? 0 : Math.Clamp(timeoutMs, 250, 10000);
            o.FailureThreshold = failureThreshold <= 0 ? 0 : Math.Clamp(failureThreshold, 2, 20);
        });

    private string? UpdateOptions(IReadOnlyCollection<HostKey> keys, string description, Action<HostOptions> edit) =>
        UpdateHosts(keys, description, (site, address) =>
        {
            var options = OptionsUnsafe(site, address).Copy();
            edit(options);
            site.HostDetails[address] = options;
        });

    private string? UpdateHosts(IReadOnlyCollection<HostKey> keys, string description, Action<SiteDefinition, string> edit)
    {
        if (keys.Count == 0) return "Select a host first.";
        lock (_gate)
        {
            var targets = ResolveUnsafe(keys);
            if (targets.Count == 0) return "Those hosts no longer exist.";
            PushUndoUnsafe(description);
            foreach (var (site, address) in targets) edit(site, address);
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? RemoveHosts(IReadOnlyCollection<HostKey> keys)
    {
        if (keys.Count == 0) return "Select a host first.";
        lock (_gate)
        {
            var targets = ResolveUnsafe(keys);
            if (targets.Count == 0) return "Those hosts no longer exist.";
            PushUndoUnsafe("Removed " + Hosts(targets.Count));
            foreach (var (site, address) in targets)
            {
                site.Hosts.Remove(address);
                PruneUnsafe(site);
            }
            _sites.RemoveAll(s => s.Temporary && s.Hosts.Count == 0);
            if (Config.SelectedSite is not null && FindSiteUnsafe(Config.SelectedSite) is null) Config.SelectedSite = null;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? MoveHosts(IReadOnlyCollection<HostKey> keys, string destination, bool createSite = false, bool keep = false)
    {
        if (keys.Count == 0) return "Select a host first.";
        destination = destination.Trim();
        if (destination.Length == 0) return "Choose a site.";
        lock (_gate)
        {
            var target = FindSiteUnsafe(destination);
            if (target is null && !createSite) return "Select an existing destination site.";
            if (target is { Temporary: true }) return "Choose a saved site.";
            var sources = ResolveUnsafe(keys);
            if (sources.Count == 0) return "Those hosts no longer exist.";
            if (target is not null && sources.All(s => ReferenceEquals(s.Site, target))) return null;
            PushUndoUnsafe((keep ? "Kept " : "Moved ") + Hosts(sources.Count) + " in " + destination);
            if (target is null)
            {
                target = new SiteDefinition { Name = destination };
                _sites.Add(target);
            }
            foreach (var (site, address) in sources)
            {
                if (ReferenceEquals(site, target)) continue;
                if (!target.Hosts.Contains(address, StringComparer.OrdinalIgnoreCase)) target.Hosts.Add(address);
                if (site.Labels.TryGetValue(address, out var label)) target.Labels[address] = label;
                if (site.Categories.TryGetValue(address, out var group)) target.Categories[address] = group;
                if (site.HostDetails.TryGetValue(address, out var options)) target.HostDetails[address] = options.Copy();
                site.Hosts.Remove(address);
                PruneUnsafe(site);
            }
            _sites.RemoveAll(s => s.Temporary && s.Hosts.Count == 0);
            if (keep || (Config.SelectedSite is not null && FindSiteUnsafe(Config.SelectedSite) is null))
                Config.SelectedSite = target.Name;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? Undo()
    {
        string description;
        lock (_gate)
        {
            if (_undo.Count == 0) return null;
            (description, var previous) = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            var temporary = _sites.Where(s => s.Temporary && !previous.Any(p => p.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            _sites.Clear();
            _sites.AddRange(previous);
            _sites.AddRange(temporary);
            foreach (var site in _sites) EnsureFolderUnsafe(site.FolderPath);
            if (Config.SelectedSite is not null && FindSiteUnsafe(Config.SelectedSite) is null) Config.SelectedSite = null;
            SaveUnsafe();
            ReconcileUnsafe();
        }
        RaiseChanged();
        return "Undid: " + description;
    }

    private void PushUndoUnsafe(string description)
    {
        _undo.Add((description, _sites.Select(CloneSite).ToList()));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    private List<(SiteDefinition Site, string Address)> ResolveUnsafe(IEnumerable<HostKey> keys)
    {
        var result = new List<(SiteDefinition, string)>();
        foreach (var key in keys.Distinct())
        {
            var site = FindSiteUnsafe(key.Site);
            string? address = site?.Hosts.FirstOrDefault(h => h.Equals(key.Address, StringComparison.OrdinalIgnoreCase));
            if (site is not null && address is not null) result.Add((site, address));
        }
        return result;
    }

    private static void PruneUnsafe(SiteDefinition site)
    {
        bool Keep(string host) => site.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase);
        foreach (var key in site.Labels.Keys.Where(k => !Keep(k)).ToList()) site.Labels.Remove(key);
        foreach (var key in site.Categories.Keys.Where(k => !Keep(k)).ToList()) site.Categories.Remove(key);
        foreach (var key in site.HostDetails.Keys.Where(k => !Keep(k)).ToList()) site.HostDetails.Remove(key);
    }

    private static HostOptions OptionsUnsafe(SiteDefinition? site, string address) =>
        site is not null && site.HostDetails.TryGetValue(address, out var options) ? options : DefaultOptions;

    private static readonly HostOptions DefaultOptions = new() { Id = "" };

    internal static string Hosts(int count) => count == 1 ? "1 host" : $"{count} hosts";

    // ---- Settings, import and export ----

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
            Config.Use12HourTime = settings.Use12HourTime;
            Config.AutoCheckUpdates = settings.AutoCheckUpdates;
            Config.MinimizeToTray = settings.MinimizeToTray;
            Config.NotificationsEnabled = settings.NotificationsEnabled;
            Config.WallboardShowCli = settings.WallboardShowCli;
            Config.ShowUpdateControlOnHome = settings.ShowUpdateControlOnHome;
            Config.PriorityAlarmSound = settings.PriorityAlarmSound ?? "";
            DisplayTime.Use12HourTime = Config.Use12HourTime;
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
            return JsonSerializer.Serialize(CloneConfigUnsafe(), new JsonSerializerOptions { WriteIndented = true });
    }

    public string? ImportConfigJson(string json)
    {
        WatchdogConfig candidate;
        try
        {
            var loaded = JsonSerializer.Deserialize<WatchdogConfig>(json);
            if (loaded is null) return "The configuration file was empty.";
            candidate = SanitizeConfig(loaded);
            if (candidate.Sites.Count == 0) return "The configuration has no valid sites.";
            if (candidate.SelectedSite is not null && !candidate.Sites.Any(s => s.Name.Equals(candidate.SelectedSite, StringComparison.OrdinalIgnoreCase)))
                candidate.SelectedSite = null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return $"Couldn't read the configuration: {ex.Message}";
        }

        bool restart = Monitoring;
        if (restart)
            StopMonitoring();
        lock (_gate)
        {
            PushUndoUnsafe("Imported a configuration");
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
            DisplayTime.Use12HourTime = Config.Use12HourTime;
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
        SafeFile.WriteAtomic(_configPath, JsonSerializer.Serialize(CloneConfigUnsafe(), new JsonSerializerOptions { WriteIndented = true }));
    }

    private WatchdogConfig CloneConfigUnsafe()
    {
        string? selected = Config.SelectedSite is not null && FindSiteUnsafe(Config.SelectedSite) is { Temporary: true } ? null : Config.SelectedSite;
        return new WatchdogConfig
        {
            FormatVersion = Config.FormatVersion,
            // Quick Monitor sites are never written to disk.
            Sites = _sites.Where(site => !site.Temporary).Select(CloneSite).ToList(),
            SiteFolders = _folders.ToList(),
            PingIntervalSeconds = Config.PingIntervalSeconds,
            PingTimeoutMs = Config.PingTimeoutMs,
            FailureThreshold = Config.FailureThreshold,
            RecoveryThreshold = Config.RecoveryThreshold,
            ShowCommandView = Config.ShowCommandView,
            SelectedSite = selected,
            EventHistoryHours = Config.EventHistoryHours,
            HideSuspectEvents = Config.HideSuspectEvents,
            Use12HourTime = Config.Use12HourTime,
            AutoCheckUpdates = Config.AutoCheckUpdates,
            MinimizeToTray = Config.MinimizeToTray,
            NotificationsEnabled = Config.NotificationsEnabled,
            WallboardShowCli = Config.WallboardShowCli,
            ShowUpdateControlOnHome = Config.ShowUpdateControlOnHome,
            PriorityAlarmSound = Config.PriorityAlarmSound
        };
    }

    private static SiteDefinition CloneSite(SiteDefinition site) => new()
    {
        Temporary = site.Temporary,
        Name = site.Name,
        FolderPath = site.FolderPath,
        Hosts = site.Hosts.ToList(),
        Labels = new Dictionary<string, string>(site.Labels, StringComparer.OrdinalIgnoreCase),
        Categories = new Dictionary<string, string>(site.Categories, StringComparer.OrdinalIgnoreCase),
        HostDetails = site.HostDetails.ToDictionary(pair => pair.Key, pair => pair.Value.Copy(), StringComparer.OrdinalIgnoreCase)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A bad history file must never stop monitoring.
        }
    }

    private void PersistHistoryUnsafe()
    {
        try
        {
            Directory.CreateDirectory(_stateDirectory);
            SafeFile.WriteAtomic(_historyPath, JsonSerializer.Serialize(_events));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is best effort; the live state and notifications still work.
        }
    }

    private static string BuildHostKey(string site, string address) => $"{site}\u001f{address}";

    internal static string FormatDuration(TimeSpan span)
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
