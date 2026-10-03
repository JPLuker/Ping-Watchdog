using System.Text.Json;

namespace PingWatchdog.Linux;

internal static class LinuxRegressionTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ping-watchdog-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DateTime now = DateTime.Now;
            var records = Enumerable.Range(0, 600).Select(i => new StateEventRecord(
                now.AddMinutes(-i), i < 300 ? "Alpha" : "Beta", "127.0.0.1", $"Host {i}",
                i % 3 == 0 ? "SUSPECT" : "DOWN", $"Event {i}")).ToList();
            records.Add(new StateEventRecord(now.AddDays(-60), "Removed site", "127.0.0.2", "Old host",
                "RECOVERED", "Recovered, after \"maintenance\"\nSecond line"));
            Directory.CreateDirectory(Path.Combine(root, "state"));
            File.WriteAllText(Path.Combine(root, "state", "event-history.json"), JsonSerializer.Serialize(records));

            using var engine = new WatchdogEngine(root);
            var settings = engine.Snapshot().Settings;
            settings.EventHistoryHours = 24;
            settings.HideSuspectEvents = true;
            Check(engine.ApplySettings(settings) is null, "History defaults save");
            Check(engine.Snapshot().Events.Count == 250, "Live dashboard remains bounded");
            var all = engine.HistorySnapshot(0, false, now: now);
            Check(all.StoredCount == 601 && all.Events.Count == 601, "All time includes every stored event");
            Check(all.Events.First().Timestamp == now && all.Events.Last().Site == "Removed site", "Newest-first history order");
            Check(engine.HistorySnapshot(24, false, now: now).Events.Count == 600, "24-hour range");
            Check(engine.HistorySnapshot(168, false, now: now).Events.Count == 600, "7-day range");
            Check(engine.HistorySnapshot(720, false, now: now).Events.Count == 600, "30-day range");
            Check(engine.HistorySnapshot(0, true, now: now).Events.Count == 401, "Suspect filter preserves all stored events");
            var alpha = engine.HistorySnapshot(0, false, "aLpHa", now);
            Check(alpha.Events.Count == 300 && alpha.Events.All(e => e.Site == "Alpha"), "Site filter is case insensitive and uncapped");
            Check(engine.HistorySnapshot(24, true, "Beta", now).Events.Count == 200, "Combined filters");
            Check(engine.HistorySnapshot(0, false, "Missing", now).Events.Count == 0, "Empty site filter");
            Check(engine.HistorySites().Contains("Removed site"), "Deleted sites remain discoverable in history");

            using var csv = new StringWriter();
            HistoryCsv.WriteAsync(csv, alpha.Events).GetAwaiter().GetResult();
            string text = csv.ToString();
            Check(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 301, "CSV exports all 300 selected events plus header");
            Check(!text.Contains("Beta", StringComparison.Ordinal), "CSV respects selected site");
            using var escapedCsv = new StringWriter();
            HistoryCsv.WriteAsync(escapedCsv, records.TakeLast(1)).GetAwaiter().GetResult();
            Check(escapedCsv.ToString().Contains("\"Recovered, after \"\"maintenance\"\"\nSecond line\"", StringComparison.Ordinal), "CSV quotes commas, quotes, and newlines");

            engine.Config.SelectedSite = "Default Site";
            bool invoked = false;
            LinuxUpdateRestart.Apply(engine, () =>
            {
                invoked = true;
                Check(!engine.Monitoring, "Updater receives a stopped engine");
                var saved = JsonSerializer.Deserialize<WatchdogConfig>(File.ReadAllText(engine.ConfigPath));
                Check(saved?.SelectedSite == "Default Site", "Config is saved before applying update");
            });
            Check(invoked, "Updater callback is invoked");
            Check(engine.SaveHosts("Default Site", "127.0.0.1") is null, "Loopback fixture saved");
            engine.StartMonitoring();
            Check(engine.Monitoring, "Monitoring fixture starts");
            try
            {
                LinuxUpdateRestart.Apply(engine, () =>
                {
                    Check(!engine.Monitoring, "Active monitoring stops before applying update");
                    throw new IOException("Simulated update failure");
                });
                throw new InvalidOperationException("Update failure was swallowed");
            }
            catch (IOException)
            {
                Check(engine.Monitoring, "Failed update restores monitoring");
            }
            LinuxUpdateRestart.Apply(engine, () => Check(!engine.Monitoring, "Successful update stops monitoring"));
            Check(!engine.Monitoring, "Monitoring stays stopped on successful restart");
            Check(engine.HistorySnapshot(0, false, now: now).StoredCount >= 601, "Filtering/export/update never deletes history");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Linux regression failed: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
