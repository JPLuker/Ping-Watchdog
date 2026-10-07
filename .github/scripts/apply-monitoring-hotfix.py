from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)

root = Path('.')
program_path = root / 'Program.cs'
wallboard_path = root / 'WallboardForm.cs'
host_management_path = root / 'HostManagement.cs'
topology_path = root / 'TopologyLayout.cs'
csproj_path = root / 'PingWatchdog.csproj'
workflow_path = root / '.github/workflows/build-windows.yml'

program = program_path.read_text(encoding='utf-8')
wallboard = wallboard_path.read_text(encoding='utf-8')
host_management = host_management_path.read_text(encoding='utf-8')
csproj = csproj_path.read_text(encoding='utf-8')
workflow = workflow_path.read_text(encoding='utf-8')

# Persistent UI preference that must survive application package replacement.
program = replace_once(
    program,
    '        LoadSites();\n        LoadEventHistory();',
    '        LoadSites();\n        LoadUserPreferences();\n        LoadEventHistory();',
    'load user preferences at startup')

program = replace_once(
    program,
    '        _wallboardShowCli = config.WallboardShowCli;\n        _showUpdateControlOnHome = config.ShowUpdateControlOnHome;\n\n        _selectedSiteName =',
    '        _wallboardShowCli = config.WallboardShowCli;\n        _showUpdateControlOnHome = config.ShowUpdateControlOnHome;\n        // UI/developer preferences are machine-user preferences, not scan-definition settings.\n        // Re-apply the durable preference after any autosave/default/imported config is loaded.\n        LoadUserPreferences();\n\n        _selectedSiteName =',
    'reapply user preferences after config load')

program = replace_once(
    program,
    '        _wallboardShowCli = settings.WallboardShowCli;\n        _showUpdateControlOnHome = settings.ShowUpdateControlOnHome;\n        _eventHistoryHours =',
    '        _wallboardShowCli = settings.WallboardShowCli;\n        _showUpdateControlOnHome = settings.ShowUpdateControlOnHome;\n        SaveUserPreferences();\n        _eventHistoryHours =',
    'persist developer update preference immediately')

# A monitoring count means enabled targets, not every configured inventory record.
program = replace_once(
    program,
    '            int totalHosts = _sites.Sum(s => s.Hosts.Count);',
    '            int totalHosts = _sites.Sum(site =>\n                site.Hosts.Count(address => GetHostOptions(site, address).Enabled));',
    'all-sites enabled count')

program = replace_once(
    program,
    '                    $"{FormatSiteDisplayPath(site)} ({site.Hosts.Count})"));',
    '                    $"{FormatSiteDisplayPath(site)} ({site.Hosts.Count(address => GetHostOptions(site, address).Enabled)})"));',
    'site enabled count')

# Add a configured-host record so Wallboard can keep disabled hosts in its editor while
# excluding them from every live monitoring surface.
program = replace_once(
    program,
    'internal sealed record WallboardControlSnapshot(\n    bool Monitoring,\n    string? SelectedSite,\n    IReadOnlyList<string> Sites,\n    IReadOnlyList<WallboardControlHost> Hosts,',
    'internal sealed record WallboardConfiguredHost(\n    string Site,\n    string Address);\n\ninternal sealed record WallboardControlSnapshot(\n    bool Monitoring,\n    string? SelectedSite,\n    IReadOnlyList<string> Sites,\n    IReadOnlyList<WallboardControlHost> Hosts,\n    IReadOnlyList<WallboardConfiguredHost> ConfiguredHosts,',
    'wallboard configured host record')

# Helper used by all monitoring presentation surfaces.
program = replace_once(
    program,
    '    private WallboardSnapshot BuildWallboardSnapshot()\n    {',
    '    private bool IsHostEnabled(string siteName, string address)\n    {\n        var site = FindSite(siteName);\n        return site is not null &&\n            site.Hosts.Any(host => host.Equals(address, StringComparison.OrdinalIgnoreCase)) &&\n            GetHostOptions(site, address).Enabled;\n    }\n\n    private WallboardSnapshot BuildWallboardSnapshot()\n    {',
    'enabled host helper')

# Wallboard topology/sites: disabled hosts and sites containing only disabled hosts vanish.
program = replace_once(
    program,
    '        var sites = _sites\n            .Select(site =>\n            {\n                var hosts = site.Hosts.Select(address =>',
    '        var sites = _sites\n            .Where(site => site.Hosts.Any(address => GetHostOptions(site, address).Enabled))\n            .Select(site =>\n            {\n                var hosts = site.Hosts\n                    .Where(address => GetHostOptions(site, address).Enabled)\n                    .Select(address =>',
    'wallboard topology enabled hosts')

program = replace_once(
    program,
    '                    _hideSuspectEvents,\n                    DateTime.Now)\n                .TakeLast(80)',
    '                    _hideSuspectEvents,\n                    DateTime.Now)\n                .Where(e => IsHostEnabled(e.Site, e.Host))\n                .TakeLast(80)',
    'wallboard event filtering')

program = replace_once(
    program,
    '        var commands = _commandEntries\n            .TakeLast(120)',
    '        var commands = _commandEntries\n            .Where(entry => IsHostEnabled(entry.Site, entry.Host))\n            .TakeLast(120)',
    'wallboard command filtering')

# Wallboard Operations keeps full configured inventory in its editor but its live table
# contains enabled monitoring targets only.
program = replace_once(
    program,
    '        var hosts = new List<WallboardControlHost>();\n\n        foreach (var site in selectedSites)\n        {\n            foreach (var address in site.Hosts)\n            {\n                string key = BuildHostKey(site.Name, address);',
    '        var hosts = new List<WallboardControlHost>();\n        var configuredHosts = selectedSites\n            .SelectMany(site => site.Hosts.Select(address =>\n                new WallboardConfiguredHost(site.Name, address)))\n            .ToList();\n\n        foreach (var site in selectedSites)\n        {\n            foreach (var address in site.Hosts)\n            {\n                if (!GetHostOptions(site, address).Enabled)\n                    continue;\n\n                string key = BuildHostKey(site.Name, address);',
    'wallboard live control enabled hosts')

program = replace_once(
    program,
    '            _sites.Select(site => site.Name).ToList(),\n            hosts,\n            (int)_intervalSeconds.Value,',
    '            _sites.Select(site => site.Name).ToList(),\n            hosts,\n            configuredHosts,\n            (int)_intervalSeconds.Value,',
    'wallboard configured host return')

# CLI is a monitoring surface: a host disabled later should disappear from the rebuilt trace.
program = replace_once(
    program,
    '    private bool CommandEntryMatchesFilter(CommandLogEntry entry)\n    {\n        return _selectedSiteName is null ||',
    '    private bool CommandEntryMatchesFilter(CommandLogEntry entry)\n    {\n        if (!IsHostEnabled(entry.Site, entry.Host))\n            return false;\n\n        return _selectedSiteName is null ||',
    'cli enabled filtering')

# Main monitoring totals count enabled targets even while stopped.
program = replace_once(
    program,
    '        int configured = _selectedSiteName is null\n            ? _sites.Sum(s => s.Hosts.Count)\n            : FindSite(_selectedSiteName)?.Hosts.Count ?? 0;',
    '        int configured = _selectedSiteName is null\n            ? _sites.Sum(site => site.Hosts.Count(address => GetHostOptions(site, address).Enabled))\n            : (FindSite(_selectedSiteName) is SiteDefinition selectedSite\n                ? selectedSite.Hosts.Count(address => GetHostOptions(selectedSite, address).Enabled)\n                : 0);',
    'main enabled configured count')

# Keep active-site status accurate when some sites have zero enabled hosts.
program = replace_once(
    program,
    '        _statusLabel.Text = $"Monitoring {targets.Count} host(s) across {_sites.Count} site(s)";',
    '        int monitoredSiteCount = targets.Select(target => target.Site)\n            .Distinct(StringComparer.OrdinalIgnoreCase)\n            .Count();\n        _statusLabel.Text = $"Monitoring {targets.Count} host(s) across {monitoredSiteCount} site(s)";',
    'start monitoring active site count')

program = replace_once(
    program,
    '        _statusLabel.Text = $"Monitoring {_hosts.Count} host(s) across {_sites.Count} site(s)...";',
    '        int monitoredSiteCount = _hosts.Values\n            .Select(host => host.Site)\n            .Distinct(StringComparer.OrdinalIgnoreCase)\n            .Count();\n        _statusLabel.Text = $"Monitoring {_hosts.Count} host(s) across {monitoredSiteCount} site(s)...";',
    'reconcile active site count')

# Exercise the independent user preference store in normal self-tests.
program = replace_once(
    program,
    '        TopologyLayout.RunTests();\n        DisplayTime.RunTests();',
    '        TopologyLayout.RunTests();\n        UserPreferenceStore.RunTests();\n        DisplayTime.RunTests();',
    'user preference self-test hook')

# Wallboard editor must use configured inventory, while the status grid continues to use state.Hosts.
wallboard = replace_once(
    wallboard,
    '                _hostEditor.Text = specificSite\n                    ? string.Join(Environment.NewLine, state.Hosts.Select(host => host.Address))\n                    : string.Join(\n                        Environment.NewLine,\n                        state.Hosts.Select(host => $"[{host.Site}] {host.Address}"));',
    '                _hostEditor.Text = specificSite\n                    ? string.Join(Environment.NewLine, state.ConfiguredHosts.Select(host => host.Address))\n                    : string.Join(\n                        Environment.NewLine,\n                        state.ConfiguredHosts.Select(host => $"[{host.Site}] {host.Address}"));',
    'wallboard editor configured host source')

# Regression coverage for the enabled flag across all monitoring representations.
host_management = replace_once(
    host_management,
    '        Check(!form.GetConfiguredTargets().Any(h => h.Address == imported.Address), "disabled hosts excluded");\n        var config = form.BuildConfig();',
    '        Check(!form.GetConfiguredTargets().Any(h => h.Address == imported.Address), "disabled hosts excluded");\n        var wallboard = form.BuildWallboardSnapshot();\n        Check(!wallboard.Sites.SelectMany(site => site.Hosts).Any(h => h.Address == imported.Address),\n            "disabled hosts excluded from Wallboard topology");\n        var controls = form.BuildWallboardControlSnapshot();\n        Check(!controls.Hosts.Any(h => h.Address == imported.Address),\n            "disabled hosts excluded from Wallboard live hosts");\n        Check(controls.ConfiguredHosts.Any(h => h.Address == imported.Address),\n            "disabled hosts remain available to configuration editors");\n        form.AppendCommandLog(siteName, imported.Address, true, "Reply from test: time=1ms");\n        form.RebuildCommandView();\n        Check(!form._commandBox.Text.Contains(imported.Address, StringComparison.OrdinalIgnoreCase),\n            "disabled hosts excluded from CLI monitoring trace");\n        var config = form.BuildConfig();',
    'disabled host monitoring regression tests')

# Replace the global fallback with a bounded local collision search.
topology = '''namespace PingWatchdog;\n\ninternal static class TopologyLayout\n{\n    internal const int MaxCaptionDisplacement = 96;\n    private const int PlacementStep = 16;\n\n    internal static bool TryPlaceCaption(\n        Rectangle preferred,\n        Rectangle bounds,\n        IReadOnlyList<Rectangle> occupied,\n        out Rectangle placed)\n    {\n        int width = preferred.Width;\n        int height = preferred.Height;\n\n        if (width > bounds.Width || height > bounds.Height)\n        {\n            placed = Rectangle.Empty;\n            return false;\n        }\n\n        var origin = new Rectangle(\n            Math.Clamp(preferred.X, bounds.Left, bounds.Right - width),\n            Math.Clamp(preferred.Y, bounds.Top, bounds.Bottom - height),\n            width,\n            height);\n\n        bool Free(Rectangle candidate)\n        {\n            if (!bounds.Contains(candidate))\n                return false;\n\n            var padded = Rectangle.Inflate(candidate, 3, 3);\n            return !occupied.Any(obstacle => padded.IntersectsWith(obstacle));\n        }\n\n        if (Free(origin))\n        {\n            placed = origin;\n            return true;\n        }\n\n        // Captions are annotations for a specific host node. Search only nearby;\n        // a detached label on the other side of the topology is worse than hiding it.\n        var candidates = new List<Rectangle>();\n        var seen = new HashSet<(int X, int Y)>();\n\n        void AddCandidate(int dx, int dy)\n        {\n            int x = Math.Clamp(origin.X + dx, bounds.Left, bounds.Right - width);\n            int y = Math.Clamp(origin.Y + dy, bounds.Top, bounds.Bottom - height);\n\n            if (Math.Abs(x - origin.X) > MaxCaptionDisplacement ||\n                Math.Abs(y - origin.Y) > MaxCaptionDisplacement ||\n                !seen.Add((x, y)))\n            {\n                return;\n            }\n\n            candidates.Add(new Rectangle(x, y, width, height));\n        }\n\n        for (int radius = PlacementStep; radius <= MaxCaptionDisplacement; radius += PlacementStep)\n        {\n            for (int offset = -radius; offset <= radius; offset += PlacementStep)\n            {\n                AddCandidate(offset, -radius);\n                AddCandidate(offset, radius);\n            }\n\n            for (int offset = -radius + PlacementStep; offset <= radius - PlacementStep; offset += PlacementStep)\n            {\n                AddCandidate(-radius, offset);\n                AddCandidate(radius, offset);\n            }\n        }\n\n        foreach (var candidate in candidates.OrderBy(candidate =>\n            Math.Abs((long)candidate.X - origin.X) +\n            Math.Abs((long)candidate.Y - origin.Y)))\n        {\n            if (!Free(candidate))\n                continue;\n\n            placed = candidate;\n            return true;\n        }\n\n        placed = Rectangle.Empty;\n        return false;\n    }\n\n    internal static void RunTests()\n    {\n        var bounds = new Rectangle(0, 0, 1000, 700);\n\n        var preferred = new Rectangle(400, 300, 140, 36);\n        if (!TryPlaceCaption(preferred, bounds, Array.Empty<Rectangle>(), out var exact) || exact != preferred)\n            throw new InvalidOperationException("Free topology caption moved unexpectedly.");\n\n        var oneObstacle = new List<Rectangle> { new(395, 295, 150, 46) };\n        if (!TryPlaceCaption(preferred, bounds, oneObstacle, out var nearby) ||\n            Math.Abs(nearby.X - preferred.X) > MaxCaptionDisplacement ||\n            Math.Abs(nearby.Y - preferred.Y) > MaxCaptionDisplacement)\n        {\n            throw new InvalidOperationException("Topology caption escaped its local node area.");\n        }\n\n        var blockedLocalArea = new List<Rectangle>\n        {\n            new(290, 190, 360, 270)\n        };\n        if (TryPlaceCaption(preferred, bounds, blockedLocalArea, out _))\n            throw new InvalidOperationException("Crowded topology caption should hide instead of detaching from its node.");\n\n        if (TryPlaceCaption(new Rectangle(0, 0, 200, 40), new Rectangle(0, 0, 100, 30),\n            Array.Empty<Rectangle>(), out _))\n        {\n            throw new InvalidOperationException("Oversized topology caption must not overlap.");\n        }\n    }\n}\n'''

user_preferences = '''using System.Text.Json;\n\nnamespace PingWatchdog;\n\ninternal sealed class UserPreferences\n{\n    public bool ShowUpdateControlOnHome { get; set; }\n}\n\ninternal static class UserPreferenceStore\n{\n    internal static UserPreferences Load(string path, bool fallbackShowUpdateControl)\n    {\n        try\n        {\n            if (File.Exists(path))\n            {\n                var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path));\n                if (loaded is not null)\n                    return loaded;\n            }\n        }\n        catch\n        {\n            // A damaged preference file must never prevent monitoring.\n        }\n\n        var fallback = new UserPreferences\n        {\n            ShowUpdateControlOnHome = fallbackShowUpdateControl\n        };\n        Save(path, fallback);\n        return fallback;\n    }\n\n    internal static void Save(string path, UserPreferences preferences)\n    {\n        try\n        {\n            string? directory = Path.GetDirectoryName(path);\n            if (!string.IsNullOrWhiteSpace(directory))\n                Directory.CreateDirectory(directory);\n\n            string temp = path + ".tmp";\n            File.WriteAllText(temp, JsonSerializer.Serialize(preferences, new JsonSerializerOptions\n            {\n                WriteIndented = true\n            }));\n            File.Move(temp, path, overwrite: true);\n        }\n        catch\n        {\n            // UI preferences are best-effort and must not block monitoring.\n        }\n    }\n\n    internal static void RunTests()\n    {\n        string directory = Path.Combine(Path.GetTempPath(), "PingWatchdog-pref-test-" + Guid.NewGuid().ToString("N"));\n        string path = Path.Combine(directory, "preferences.json");\n        try\n        {\n            Save(path, new UserPreferences { ShowUpdateControlOnHome = true });\n            var loaded = Load(path, fallbackShowUpdateControl: false);\n            if (!loaded.ShowUpdateControlOnHome)\n                throw new InvalidOperationException("User preference persistence regression.");\n        }\n        finally\n        {\n            try { Directory.Delete(directory, recursive: true); } catch { }\n        }\n    }\n}\n\npublic sealed partial class MainForm\n{\n    private string UserPreferencesPath => Path.Combine(\n        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),\n        "PingWatchdog",\n        "preferences.json");\n\n    private void LoadUserPreferences()\n    {\n        if (!_persistSites)\n            return;\n\n        var preferences = UserPreferenceStore.Load(\n            UserPreferencesPath,\n            _showUpdateControlOnHome);\n        _showUpdateControlOnHome = preferences.ShowUpdateControlOnHome;\n    }\n\n    private void SaveUserPreferences()\n    {\n        if (!_persistSites)\n            return;\n\n        UserPreferenceStore.Save(\n            UserPreferencesPath,\n            new UserPreferences\n            {\n                ShowUpdateControlOnHome = _showUpdateControlOnHome\n            });\n    }\n}\n'''

# Keep the source project version aligned with the hotfix series. CI will still stamp its run number.
csproj = replace_once(csproj, '<Version>1.14.62</Version>', '<Version>1.14.63</Version>', 'csproj version')

# Release notes for the generated package.
workflow = replace_once(
    workflow,
    '          Self-updating Windows release.\n\n',
    '          Self-updating Windows release.\n\n          - Keeps Wallboard host captions near their actual topology nodes; crowded labels hide instead of being packed across the canvas.\n          - Persists the developer home-screen update-control preference in a per-user preferences file that survives Velopack updates and config imports.\n          - Treats Host Manager Enabled as authoritative: disabled hosts remain configurable but disappear from monitoring workers, counts, CLI, Wallboard topology/events, and live host tables.\n\n',
    'workflow release notes')

program_path.write_text(program, encoding='utf-8')
wallboard_path.write_text(wallboard, encoding='utf-8')
host_management_path.write_text(host_management, encoding='utf-8')
topology_path.write_text(topology, encoding='utf-8')
(root / 'UserPreferences.cs').write_text(user_preferences, encoding='utf-8')
csproj_path.write_text(csproj, encoding='utf-8')
workflow_path.write_text(workflow, encoding='utf-8')

print('Monitoring hotfix applied successfully.')
