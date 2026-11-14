// Engine and desktop-service regressions for the Windows-parity features. Run by --self-test.
using System.Text.Json;
using System.Threading.Channels;

namespace PingWatchdog.Linux;

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
                Check(WatchdogEngine.NormalizeFolderPath(@" Indiana\\North / Branch ") == "Indiana/North/Branch", "Folder paths normalize");
                Check(WatchdogEngine.NormalizeHosts(new[] { "10.0.0.1, 10.0.0.1\n8.8.8.8" }).Count == 2, "Host lists de-duplicate");
                Check(engine.AddFolder("", "Indiana") is null, "Add folder");
                Check(engine.AddFolder("Indiana", "North") is null, "Add nested folder");
                Check(engine.AddSite("Test Site", "Indiana/North") is null, "Add site in folder");
                Check(engine.SaveHosts("Test Site", "127.0.0.1\n8.8.8.8") is null, "Save hosts");
                Check(engine.SetLabel("Test Site", "127.0.0.1", "Loopback") is null, "Set label");
                var snapshot = engine.Snapshot();
                Check(snapshot.Sites.Any(site => site.Name == "Test Site" && site.FolderPath == "Indiana/North"), "Site keeps its folder");
                Check(snapshot.Hosts.Any(host => host.Address == "127.0.0.1" && host.Label == "Loopback"), "Snapshot carries labels");
                Check(engine.ExportConfigJson().Contains("Indiana/North", StringComparison.Ordinal), "Export includes folders");
                Check(File.Exists(engine.ConfigPath), "Configuration is written");
            }
            using (var reloaded = new WatchdogEngine(root))
            {
                var snapshot = reloaded.Snapshot();
                Check(snapshot.Sites.Any(site => site.Name == "Test Site"), "Sites survive restart");
                Check(snapshot.Sites.SelectMany(site => site.Hosts).Any(host => host.Label == "Loopback"), "Labels survive restart");
                Check(reloaded.MoveSite("Test Site", "") is null, "Move site to top level");
                Check(reloaded.RenameFolder("Indiana/North", "Northwest") is null, "Rename folder");
                Check(reloaded.DeleteFolder("Indiana/Northwest") is null, "Delete folder");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static void Check(bool condition, string name) => LinuxRegressionTests.Check(condition, name);
}

internal static class FeatureRegressionTests
{
    private static void Check(bool condition, string name) => LinuxRegressionTests.Check(condition, name);

    private static string Root() => Path.Combine(Path.GetTempPath(), "ping-watchdog-features-" + Guid.NewGuid().ToString("N"));

    public static void Run()
    {
        DisplayTime.RunTests();
        Check(true, "Shared 12/24-hour clock formatting");
        WindowsConfigCompatibility();
        HostEditing();
        QuickMonitor();
        ProbeRulesAsync().GetAwaiter().GetResult();
        Backups();
        DesktopServices();
        Contrast();
        Topology();
        Alarm();
    }

    private const string WindowsConfig = """
    {
      "FormatVersion": 1,
      "Sites": [
        {
          "Name": "Office & Warehouse",
          "FolderPath": "Indiana",
          "Hosts": [ "192.0.2.10", "192.0.2.20", "printer.local" ],
          "Labels": { "192.0.2.10": "Router", "192.0.2.20": "Printer & scanner" },
          "Categories": { "192.0.2.20": "Printers" },
          "HostDetails": {
            "192.0.2.10": { "Id": "abc", "Tags": "", "Enabled": true, "Priority": true, "IntervalSeconds": 5, "TimeoutMs": 2000, "FailureThreshold": 4 },
            "192.0.2.20": { "Id": "def", "Tags": "", "Enabled": false, "Priority": false },
            "printer.local": { "Id": "ghi", "Tags": "", "Enabled": true, "SnoozedUntilUtc": "2099-01-01T00:00:00+00:00" }
          }
        }
      ],
      "SiteFolders": [ "Indiana" ],
      "PingIntervalSeconds": 3,
      "PingTimeoutMs": 1500,
      "FailureThreshold": 4,
      "RecoveryThreshold": 2,
      "Use12HourTime": true,
      "PriorityAlarmSound": "",
      "ShowCommandView": true
    }
    """;

    private static void WindowsConfigCompatibility()
    {
        string root = Root();
        try
        {
            using (var engine = new WatchdogEngine(root))
            {
                Check(engine.ImportConfigJson(WindowsConfig) is null, "A Windows configuration imports");
                var hosts = engine.Snapshot().Sites.Single().Hosts;
                var router = hosts.Single(h => h.Address == "192.0.2.10");
                Check(router.Options.Priority && router.Options.IntervalSeconds == 5 && router.Options.TimeoutMs == 2000 && router.Options.FailureThreshold == 4,
                    "Windows priority and per-host timing are read");
                Check(hosts.Single(h => h.Address == "192.0.2.20").Disabled, "Windows disabled hosts stay disabled");
                Check(hosts.Single(h => h.Address == "printer.local").Snoozed, "Windows snoozes are kept");
                Check(hosts.Single(h => h.Address == "192.0.2.20").Group == "Printers", "Windows groups are kept");
                Check(engine.Config.Use12HourTime && DisplayTime.Use12HourTime, "Windows 12-hour clock choice applies");
                Check(router.StatusText == "Idle · Priority" && hosts.Single(h => h.Address == "192.0.2.20").StatusText == "Disabled" &&
                      hosts.Single(h => h.Address == "printer.local").StatusText == "Snoozed", "Status words match Windows");
            }
            using (var reloaded = new WatchdogEngine(root))
            {
                var saved = JsonDocument.Parse(File.ReadAllText(reloaded.ConfigPath)).RootElement;
                var details = saved.GetProperty("Sites")[0].GetProperty("HostDetails");
                Check(details.GetProperty("192.0.2.10").GetProperty("Priority").GetBoolean() &&
                      !details.GetProperty("192.0.2.20").GetProperty("Enabled").GetBoolean() &&
                      details.GetProperty("192.0.2.10").GetProperty("Id").GetString() == "abc",
                    "Host options are written back in the Windows shape, ids unchanged");
                var exported = JsonDocument.Parse(reloaded.ExportConfigJson()).RootElement;
                Check(exported.GetProperty("Sites")[0].GetProperty("HostDetails").EnumerateObject().Count() == 3, "Export keeps every host's options");
                Check(!reloaded.HasProbeTargets() == false, "Enabled, unsnoozed hosts can be probed");
            }
        }
        finally { Cleanup(root); }
        DisplayTime.Use12HourTime = false;
    }

    private static void HostEditing()
    {
        string root = Root();
        try
        {
            using var engine = new WatchdogEngine(root);
            var rows = HostImport.Parse("192.0.2.1\tRouter\tCore\n192.0.2.2,Printer\n192.0.2.300\n192.0.2.1");
            Check(rows.Count(r => r.Error.Length > 0) == 1, "Invalid addresses are flagged in the preview");
            var (added, error) = engine.AddHosts("Default Site", rows);
            Check(error is null && added == 2, "Add hosts adds each valid address once");
            var site = engine.SiteDefinitions().Single();
            Check(site.Labels["192.0.2.1"] == "Router" && site.Categories["192.0.2.1"] == "Core", "Pasted label and group are saved");
            Check(HostImport.Parse("10.0.0.0/30").Count == 2 && HostImport.Parse("10.0.0.5-7").Count == 3, "CIDR blocks and ranges expand");

            var router = new HostKey("Default Site", "192.0.2.1");
            Check(engine.SetPriority(new[] { router }, true) is null, "Mark as priority");
            Check(engine.EditHost(router, "192.0.2.9", "Core router") is null, "Edit host changes the address");
            site = engine.SiteDefinitions().Single();
            Check(site.Hosts.Contains("192.0.2.9") && !site.Hosts.Contains("192.0.2.1") && site.Labels["192.0.2.9"] == "Core router" &&
                  site.HostDetails["192.0.2.9"].Priority && site.Categories["192.0.2.9"] == "Core", "Editing an address keeps its options and group");
            Check(engine.EditHost(new HostKey("Default Site", "192.0.2.9"), "192.0.2.2", "x") is not null, "Editing onto an existing address is refused");
            Check(engine.EditHost(new HostKey("Default Site", "192.0.2.9"), "not a host!", "x") is not null, "Invalid edited address is refused");

            Check(engine.Undo() is { } undid && undid.Contains("Changed", StringComparison.Ordinal), "Undo names what it undid");
            site = engine.SiteDefinitions().Single();
            Check(site.Hosts.Contains("192.0.2.1") && !site.Hosts.Contains("192.0.2.9"), "Undo restores the previous address");

            Check(engine.AddSite("Branch") is null, "Fixture site");
            var keys = new[] { new HostKey("Default Site", "192.0.2.1"), new HostKey("Default Site", "192.0.2.2") };
            Check(engine.SetGroup(keys, "Front desk") is null, "Set group on several hosts");
            Check(engine.MoveHosts(keys, "Branch") is null, "Move to site");
            var branch = engine.SiteDefinitions().Single(s => s.Name == "Branch");
            Check(branch.Hosts.Count == 2 && branch.Labels["192.0.2.1"] == "Router" && branch.HostDetails["192.0.2.1"].Priority &&
                  branch.Categories.Values.All(g => g == "Front desk"), "Moved hosts keep labels, groups and options");
            Check(engine.SiteDefinitions().Single(s => s.Name == "Default Site").Hosts.Count == 0, "Moved hosts leave the old site");

            var moved = keys.Select(k => k with { Site = "Branch" }).ToList();
            Check(engine.SetEnabled(moved, false) is null && engine.Snapshot().Sites.Single(s => s.Name == "Branch").Hosts.All(h => h.Disabled), "Disable monitoring");
            Check(!engine.HasProbeTargets(), "Disabled hosts are never probed");
            Check(engine.SetEnabled(moved, true) is null && engine.SetSnooze(moved, DateTimeOffset.UtcNow.AddHours(1)) is null, "Snooze for an hour");
            Check(engine.Snapshot().Sites.Single(s => s.Name == "Branch").Hosts.All(h => h.Snoozed && h.StatusWord == "Snoozed"), "Snoozed hosts say so");
            Check(engine.SetSnooze(moved, null) is null && engine.Snapshot().Hosts.All(h => !h.Snoozed), "Resume monitoring now");
            Check(engine.SetMonitoring(moved, 10, 500, 5) is null && engine.SiteDefinitions().Single(s => s.Name == "Branch").HostDetails["192.0.2.2"].TimeoutMs == 500, "Per-host monitoring saves");
            Check(engine.RemoveHosts(moved) is null && engine.SiteDefinitions().Single(s => s.Name == "Branch").Hosts.Count == 0, "Remove hosts");
            engine.Undo();
            Check(engine.SiteDefinitions().Single(s => s.Name == "Branch").Hosts.Count == 2, "Undo brings removed hosts back");
            Check(engine.DeleteSite("Branch") is null && engine.Undo() is not null && engine.SiteDefinitions().Any(s => s.Name == "Branch"), "Undo restores a deleted site");
        }
        finally { Cleanup(root); }
    }

    private static void QuickMonitor()
    {
        string root = Root();
        try
        {
            using var engine = new WatchdogEngine(root, (_, _, token) => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => new ProbeResult(false, null, "x")));
            var rows = HostImport.Parse("198.51.100.1\tTemp camera\n198.51.100.2");
            var (added, error) = engine.StartQuickMonitor(rows);
            Check(error is null && added == 2 && engine.Monitoring, "Quick Monitor starts watching at once");
            var temp = engine.Snapshot().Sites.Single(s => s.Temporary);
            Check(temp.Name == WatchdogEngine.QuickMonitorSite && temp.Hosts.All(h => h.Temporary), "Quick Monitor hosts are temporary");
            Check(!File.ReadAllText(engine.ConfigPath).Contains("198.51.100.1", StringComparison.Ordinal), "Temporary hosts are never written to disk");
            Check(engine.KeepTemporaryHosts(new[] { new HostKey(WatchdogEngine.QuickMonitorSite, "198.51.100.1") }, "Cameras", createSite: true) is null, "Keep in a site");
            var cameras = engine.SiteDefinitions().Single(s => s.Name == "Cameras");
            Check(cameras.Labels["198.51.100.1"] == "Temp camera" && File.ReadAllText(engine.ConfigPath).Contains("198.51.100.1", StringComparison.Ordinal),
                "Kept hosts are saved with their labels");
            Check(engine.RemoveHosts(new[] { new HostKey(WatchdogEngine.QuickMonitorSite, "198.51.100.2") }) is null &&
                  !engine.Snapshot().Sites.Any(s => s.Temporary), "An empty Quick Monitor site goes away");
            engine.StopMonitoring();
        }
        finally { Cleanup(root); }
    }

    private static async Task ProbeRulesAsync()
    {
        string root = Root();
        var calls = Channel.CreateUnbounded<(string Address, int Timeout, TaskCompletionSource<ProbeResult> Reply)>();
        Task<ProbeResult> Probe(string address, int timeout, CancellationToken token)
        {
            var reply = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => reply.TrySetCanceled());
            calls.Writer.TryWrite((address, timeout, reply));
            return reply.Task;
        }
        var notifications = new List<(string Title, bool Critical)>();
        var priority = new List<PriorityStateChange>();
        using var engine = new WatchdogEngine(root, Probe, (_, token) => Task.CompletedTask);
        engine.Notify = (title, _, critical) => { lock (notifications) notifications.Add((title, critical)); };
        engine.PriorityStateChanged += change => { lock (priority) priority.Add(change); };
        try
        {
            var settings = engine.Snapshot().Settings;
            settings.FailureThreshold = 2;
            settings.NotificationsEnabled = true;
            engine.ApplySettings(settings);
            engine.SaveHosts("Default Site", "192.0.2.50\n192.0.2.51\n192.0.2.52");
            var vip = new HostKey("Default Site", "192.0.2.50");
            var quiet = new HostKey("Default Site", "192.0.2.52");
            engine.SetPriority(new[] { vip }, true);
            engine.SetMonitoring(new[] { vip }, 0, 4000, 0);
            engine.SetSnooze(new[] { quiet }, DateTimeOffset.UtcNow.AddHours(1));
            engine.StartMonitoring();
            var pending = new Dictionary<string, (int Timeout, TaskCompletionSource<ProbeResult> Reply)>();
            async Task Next()
            {
                var call = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                pending[call.Address] = (call.Timeout, call.Reply);
            }
            await Next(); await Next();
            Check(pending.ContainsKey("192.0.2.50") && pending.ContainsKey("192.0.2.51") && !pending.ContainsKey("192.0.2.52"), "Snoozed hosts aren't probed");
            Check(pending["192.0.2.50"].Timeout == 4000 && pending["192.0.2.51"].Timeout == settings.PingTimeoutMs, "Per-host timeout overrides the default");
            for (int round = 0; round < 2; round++)
            {
                foreach (var address in new[] { "192.0.2.50", "192.0.2.51" })
                {
                    pending[address].Reply.SetResult(new ProbeResult(false, null, "Timeout"));
                    pending.Remove(address);
                }
                await Next(); await Next();
            }
            await Until(() => engine.Snapshot().Hosts.Count(h => h.State == HostState.Offline) == 2);
            lock (priority) Check(priority.Count == 1 && priority[0].Host.Address == "192.0.2.50" && priority[0].Offline, "A priority host going down raises one priority alert");
            lock (notifications) Check(notifications.Count == 1 && notifications[0].Title.Contains("192.0.2.51", StringComparison.Ordinal) && !notifications[0].Critical,
                "Ordinary hosts notify; priority hosts don't get a second, ordinary notification");
            engine.SetSnooze(new[] { quiet }, null);
            engine.ReconcileForTest();
            await Next();
            Check(pending.ContainsKey("192.0.2.52"), "Resumed hosts are picked back up while monitoring");
            engine.SetEnabled(new[] { new HostKey("Default Site", "192.0.2.51") }, false);
            Check(engine.Snapshot().Hosts.Single(h => h.Address == "192.0.2.51").StatusWord == "Disabled", "Disabling a live host stops it at once");
        }
        finally
        {
            engine.StopMonitoring();
            Cleanup(root);
        }
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }

    private static void Backups()
    {
        string root = Root();
        try
        {
            byte[] backup;
            using (var engine = new WatchdogEngine(root))
            {
                engine.SaveHosts("Default Site", "192.0.2.77");
                engine.SetLabel("Default Site", "192.0.2.77", "Backed up");
                using var stream = new MemoryStream();
                DataStore.CreateBackup(engine, stream);
                backup = stream.ToArray();
                engine.SaveHosts("Default Site", "192.0.2.88");
                using var input = new MemoryStream(backup);
                var (files, error) = DataStore.ReadBackup(input);
                Check(error is null && files!.ContainsKey("autosave.pingwatch.json"), "A backup reads back");
                using var junk = new MemoryStream(new byte[] { 1, 2, 3 });
                Check(DataStore.ReadBackup(junk).Error is not null, "A file that isn't a backup is refused");
                Check(DataStore.Restore(engine, files!) is null, "Restore replaces the data");
                Check(Directory.GetFiles(DataStore.BackupDirectory(engine), "before-restore-*.zip").Length == 1, "Restore keeps a copy of the data it replaced");
            }
            using (var restored = new WatchdogEngine(root))
            {
                var host = restored.Snapshot().Hosts.Single();
                Check(host.Address == "192.0.2.77" && host.Label == "Backed up", "Restored data is what was backed up");
                string foreign = Path.Combine(restored.ConfigDirectory, "notes.txt");
                File.WriteAllText(foreign, "keep me");
                string outside = Path.Combine(root, "outside.json");
                File.WriteAllText(outside, "keep me too");
                File.CreateSymbolicLink(Path.Combine(restored.StateDirectory, "linked.json"), outside);
                var (removed, kept) = DataStore.DeleteSavedData(restored);
                Check(removed >= 1 && !File.Exists(restored.ConfigPath) && !File.Exists(restored.HistoryPath), "Delete saved data removes Ping Watchdog's files");
                Check(File.Exists(foreign) && File.Exists(outside) && kept.Contains(foreign), "Delete saved data keeps foreign files and link targets");
            }
        }
        finally { Cleanup(root); }
    }

    private static void DesktopServices()
    {
        string root = Root();
        try
        {
            Autostart.DirectoryOverride = root;
            Check(Autostart.Set(true) is null && File.ReadAllText(Autostart.EntryPath).Contains("--startup", StringComparison.Ordinal), "Sign-in entry is written");
            Check(Autostart.Set(false) is null && !Autostart.Enabled, "Sign-in entry is removed");
            Check(ClipboardText.FromHtml("<table><tr><td>192.0.2.1</td><td>Router &amp; AP</td></tr><tr><td>192.0.2.2</td></tr></table>") == "192.0.2.1\tRouter & AP\n192.0.2.2",
                "HTML-only clipboards become rows and tab-separated cells");
            Check(ClipboardText.Normalize("a\r\nb\rc") == "a\nb\nc", "Pasted line endings are normalized");
            var prefsPath = Path.Combine(root, "preferences.json");
            new LinuxPreferences { Appearance = AppearanceMode.Dark, ShowVersionOverlay = true, HiddenColumns = new() { "Group" } }.Save(prefsPath);
            var prefs = LinuxPreferences.Load(prefsPath);
            Check(prefs.Appearance == AppearanceMode.Dark && prefs.ShowVersionOverlay && prefs.HiddenColumns.SequenceEqual(new[] { "Group" }), "Preferences round-trip");
            File.WriteAllText(prefsPath, "{ broken");
            Check(LinuxPreferences.Load(prefsPath).Appearance == AppearanceMode.System, "A damaged preferences file falls back to defaults");
            Check(!HostActions.PingInTerminal("192.0.2.1; rm -rf /"), "Ping in terminal refuses anything that isn't an address");
            Check(HostActions.MenuText("my_host") == "my__host", "Underscores in host names show in menus");
        }
        finally
        {
            Autostart.DirectoryOverride = null;
            Cleanup(root);
        }
    }

    // Quiet Precision: every text role passes 4.5:1 on canvas, surface, surface-2 and surface-3, light and dark.
    private static void Contrast()
    {
        foreach (var p in new[] { Palette.Light, Palette.DarkPalette })
        {
            foreach (var background in new[] { p.Canvas, p.Surface, p.Surface2, p.Surface3 })
                foreach (var text in new[] { p.Text, p.Text2, p.Text3, p.Success, p.Warning, p.Danger })
                    if (Palette.Contrast(text, background) < 4.5)
                        Check(false, $"Contrast {text} on {background} is {Palette.Contrast(text, background):0.00}");
            Check(Palette.Contrast(p.OnAccent, p.Accent) >= 4.5, "Primary button text passes on the accent");
            Check(Palette.Contrast(p.Focus, p.Surface) >= 3, "Focus ring passes 3:1");
        }
        Check(true, "Every text role passes 4.5:1 in light and dark");
    }

    private static void Topology()
    {
        HostSnapshot H(HostState state) => new("S", "", Guid.NewGuid().ToString("N")[..8], "", state, null, 0, null, null) { Monitoring = true };
        SiteSnapshot S(params HostState[] states) => new("S", "", states.Select(H).ToList());
        Check(TopologyView.AggregateSiteState(S(HostState.Online, HostState.Online, HostState.Offline)) == HostState.Suspect, "A site with a minority down is degraded");
        Check(TopologyView.AggregateSiteState(S(HostState.Online, HostState.Offline)) == HostState.Offline, "A site with half or more down is offline");
        Check(TopologyView.AggregateSiteState(S()) == HostState.Unknown, "An empty site is unknown");
    }

    private static void Alarm()
    {
        int plays = 0;
        var alarm = new PriorityAlarm(_ => plays++);
        var first = alarm.Raise("S", "192.0.2.1", "Router", true, DateTime.Now);
        alarm.Raise("S", "192.0.2.1", "Router", false, DateTime.Now);
        Check(plays == 2 && alarm.Active.Count == 1 && !alarm.Active[0].Offline, "A newer change on the same host replaces its alert and rings");
        alarm.Raise("S", "192.0.2.2", "Switch", true, DateTime.Now);
        Check(!alarm.Acknowledge(first.Id) && alarm.Acknowledge(alarm.Active[0].Id) && alarm.Active.Count == 1, "Acknowledge clears one alert");
        alarm.Forget(a => a.Address == "192.0.2.2");
        Check(alarm.Active.Count == 0 && !alarm.Ringing, "Alerts for hosts that lost priority stop ringing");
        alarm.Dispose();
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
