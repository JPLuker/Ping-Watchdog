// Drives the real Avalonia windows under Xvfb or headless Weston, with isolated storage, a scripted
// probe and no update checks. Saves captures to ui-regression-images/ for review in the CI log.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PingWatchdog.Linux;

internal static class LinuxUiRegressionTests
{
    public static bool Enabled { get; set; }
    private static readonly string[] Devices = { "Laptop", "Desktop", "Tablet", "Mini PC", "Server" };
    private static readonly string[] Trees = { "Cedar", "Maple", "Pine", "Ash", "Birch" };
    // Scripted replies: .22 hosts never answer, so every capture shows a real outage.
    private static readonly HashSet<string> Down = new(StringComparer.OrdinalIgnoreCase) { "172.20.2.22", "172.20.4.22" };

    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        string root = Path.Combine(Path.GetTempPath(), "ping-watchdog-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        var now = DateTime.Now;
        var records = Enumerable.Range(0, 60).Select(i => new StateEventRecord(now.AddMinutes(-i), "Office & Warehouse", "192.0.2.40",
            "Controller with a long name (192.0.2.40)", (i % 3) switch { 0 => "DOWN", 1 => "RECOVERED", _ => "SUSPECT" }, "Availability changed")).ToList();
        File.WriteAllText(Path.Combine(root, "state", "event-history.json"), JsonSerializer.Serialize(records));
        Autostart.DirectoryOverride = Path.Combine(root, "autostart-config");
        DesktopLauncher.Launcher = _ => true;

        Task<ProbeResult> Probe(string address, int timeout, CancellationToken token) =>
            Task.FromResult(Down.Contains(address)
                ? new ProbeResult(false, null, "FAILED (TimedOut)")
                : new ProbeResult(true, 2 + Math.Abs(address.GetHashCode()) % 8, $"Reply from {address}: time=3ms"));
        var engine = new WatchdogEngine(root, Probe, (_, token) => Task.Delay(150, token));
        engine.Notify = (_, _, _) => { };
        var config = engine.Snapshot().Settings;
        config.AutoCheckUpdates = false;
        config.ShowCommandView = true;
        engine.ApplySettings(config);
        engine.DeleteSite("Default Site");
        for (int b = 1; b <= 5; b++)
        {
            var rows = Enumerable.Range(0, 5).Select(i => new ImportedHost($"172.20.{b}.{21 + i}", $"{Devices[i]} {Trees[(i + b) % 5]}", "", "")).ToList();
            engine.AddHosts("Branch " + b, rows, createSite: true);
        }
        engine.RemoveHosts(engine.Snapshot().Sites.Where(s => s.Name == "Default Site").SelectMany(s => s.Hosts).Select(h => h.Key).ToList());
        if (engine.SiteDefinitions().Any(s => s.Name == "Default Site" && s.Hosts.Count == 0)) engine.DeleteSite("Default Site");
        engine.SelectedSite = null;
        var updates = new LinuxUpdateService();
        var main = new MainWindow(engine, updates);
        desktop.MainWindow = main;
        bool started = false;
        main.Opened += (_, _) =>
        {
            if (started) return;
            started = true;
            Dispatcher.UIThread.Post(async () =>
            {
                int exitCode = 0;
                try
                {
                    await RunAsync(root, engine, updates, main);
                    Console.WriteLine("Ping Watchdog Linux UI regressions passed.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    exitCode = 1;
                }
                finally
                {
                    desktop.Shutdown(exitCode);
                    updates.Dispose();
                    engine.Dispose();
                    try { Directory.Delete(root, true); } catch (IOException) { }
                }
            });
        };
    }

    private static void Check(bool condition, string name) => LinuxRegressionTests.Check(condition, name);

    private static async Task Until(Func<bool> condition, string name, int ms = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Check(false, name + " (timed out)");
            await Task.Delay(25);
        }
        Check(true, name);
    }

    private static async Task Settle(int ms = 120) { await Task.Delay(ms); Dispatcher.UIThread.RunJobs(); }

    private static string Mode => Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? "Dark" : "Light";

    private static async Task SetModeAsync(AppearanceMode mode)
    {
        Tokens.SetMode(mode);
        await Settle(150);
    }

    private static void Capture(Window window, string name)
    {
        Directory.CreateDirectory("ui-regression-images");
        double scale = window.RenderScaling;
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        using var stream = File.Create(Path.Combine("ui-regression-images", name + ".png"));
        bitmap.Save(stream);
    }

    private static async Task CaptureBoth(Window window, string name)
    {
        foreach (var mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
        {
            await SetModeAsync(mode);
            Capture(window, name + "-" + mode);
        }
        await SetModeAsync(AppearanceMode.Light);
    }

    private static async Task RunAsync(string root, WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        await Settle(200);
        if (double.TryParse(Environment.GetEnvironmentVariable("WATCHDOG_EXPECTED_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double expectedScale))
            Check(Math.Abs(main.RenderScaling - expectedScale) < .01, $"Desktop render scale is {expectedScale}");
        main.Width = 1240; main.Height = 780;
        await Settle(200);

        // Main window, idle
        Check(main.HeaderText == "All hosts" && main.AllSitesText == "All sites · 25 hosts", "All sites scope and host count");
        Check(main.SiteButtonNames.SequenceEqual(new[] { "Branch 1", "Branch 2", "Branch 3", "Branch 4", "Branch 5" }), "Sidebar lists every site, no Sites heading");
        Check(main.StartStopText == "Start monitoring" && !main.ListVisible, "Map is the default view; one primary action");
        Check(main.GetVisualDescendants().OfType<TextBlock>().All(t => t.Text is not ("IDLE" or "MONITORING" or "PING WATCHDOG")), "No duplicate header chrome (badge, wordmark)");
        await Until(() => main.Map.DrawnAddresses.Count == 25, "Map draws all 25 hosts");
        Check(main.Map.AllHostsFit && !main.Map.RegionsOverlap(), "Every host fits the map and nothing overlaps");
        Check(main.Map.DrawnAddresses.ToHashSet().SetEquals(engine.Snapshot().Hosts.Select(h => h.Address)), "Every address is drawn whole (#41)");
        Check(main.Ping.IsVisible, "Ping activity sits under the Map");

        // Monitoring
        main.ToggleMonitoring();
        await Until(() => engine.Snapshot().Hosts.Count(h => h.State == HostState.Offline) == 2, "Scripted outages are declared offline");
        await Until(() => { main.RefreshAll(); return main.SummaryText == "23 online · 0 suspect · 2 offline"; }, "Map summary counts online, suspect and offline");
        Check(main.StartStopText == "Stop monitoring", "Primary action becomes Stop monitoring");
        await Until(() => main.Ping.LineCount > 0, "Ping activity shows live replies");
        await CaptureBoth(main, "main-map");

        // List
        main.SetListView(true);
        await Settle();
        Check(main.ListVisible && main.HostList.RowCount == 25, "List shows every host");
        Check(main.HostList.VisibleColumns.SequenceEqual(new[] { "Address", "Name", "Site", "Status", "Latency" }), "Default columns match Windows (Address, Name, Site, Status, Latency)");
        Check(main.HostList.Rows.Count(r => r.Status == "Offline") == 2 && main.HostList.Rows.Count(r => r.Status == "Online") == 23, "Status words with dots");
        main.SetFilter(HostFilter.Offline);
        await Settle();
        Check(main.HostList.RowCount == 2 && (main.ChipFor(HostFilter.Offline).Content as string) == "Offline 2", "Offline filter chip");
        main.SetFilter(HostFilter.Priority);
        await Settle();
        Check(main.HostList.RowCount == 0 && main.HostList.EmptyText == "No priority hosts", "Empty filter says why");
        main.SetFilter(HostFilter.All);
        main.SelectSite("Branch 3");
        await Settle();
        Check(main.HeaderText == "Branch 3" && main.HostList.RowCount == 5 && !main.HostList.VisibleColumns.Contains("Site"), "Selecting a site scopes the List; Site column follows the scope");
        main.SearchBox.Text = "server";
        await Settle();
        Check(main.HostList.RowCount == 1 && main.HostList.Rows[0].Address == "172.20.3.25", "Search filters hosts");
        main.SearchBox.Text = "";
        await Settle();
        await CaptureBoth(main, "main-list");

        // Host actions
        var tablet = engine.Snapshot().Hosts.Single(h => h.Address == "172.20.3.23");
        main.HostList.Select(tablet.Key);
        main.Actions.ShowMenu(main.HostList, new Point(40, 40), main.HostList.SelectedHosts, fromList: true);
        await Settle();
        var single = main.Actions.LastMenuItems.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Check(single.SequenceEqual(new string?[] { tablet.Label + " • 172.20.3.23", "Copy address", "Edit host…", "Clear label", "Set group…", "Ping in terminal",
            "Snooze host for 1 hour", "Mark as priority", "Disable monitoring", "Move to site…", "Monitoring…", "Remove host…" }), "Host actions match Windows: " + string.Join(", ", single));
        main.Actions.LastMenu?.Close();
        main.HostList.SelectAll();
        await Settle();
        main.Actions.ShowMenu(main.HostList, new Point(40, 40), main.HostList.SelectedHosts, fromList: true);
        await Settle();
        var bulk = main.Actions.LastMenuItems.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Check(bulk.Contains("5 hosts selected") && bulk.Contains("Move to site…") && !bulk.Contains("Copy address"), "Several hosts get bulk actions");
        main.Actions.LastMenu?.Close();

        // Edits save at once, with Undo
        Check(engine.SetPriority(new[] { tablet.Key }, true) is null, "Mark as priority");
        main.ShowStatus("Marked " + tablet.Label + " as priority", true);
        main.RefreshAll();
        await Settle();
        Check((main.ChipFor(HostFilter.Priority).Content as string) == "Priority 1" && main.HostList.Rows.Single(r => r.Address == tablet.Address).Status == "Online · Priority", "Priority shows in chip and status");
        Check(main.Toast.Message.Contains("priority", StringComparison.Ordinal), "Edits confirm in one line with Undo");
        main.Undo();
        main.RefreshAll();
        await Settle();
        Check(!engine.Snapshot().Hosts.Single(h => h.Key == tablet.Key).Options.Priority && main.Toast.Message.StartsWith("Undid", StringComparison.Ordinal), "Ctrl+Z undoes the last host edit");
        engine.SetEnabled(new[] { tablet.Key }, false);
        main.RefreshAll();
        await Settle();
        Check(main.HostList.Rows.Single(r => r.Address == tablet.Address).Status == "Disabled" && (main.ChipFor(HostFilter.Disabled).Content as string) == "Disabled 1", "Disabled hosts say so and are counted");
        engine.SetEnabled(new[] { tablet.Key }, true);

        // Dialogs
        var add = new AddHostsDialog(engine, false, "Branch 3");
        add.Show(main);
        add.SetInput("192.0.2.10\tExisting router\n192.0.2.20\tPrinter & scanner\n192.0.2.20\tDuplicate\n192.0.2.999\tInvalid\n172.20.3.21\tAgain");
        await Settle(200);
        Check(add.PreviewRows.Count == 5 && add.SummaryText == "2 new · 2 duplicates/conflicts · 1 invalid", "Quick Add previews new, duplicate and invalid rows: " + add.SummaryText);
        Check(add.PreviewRows.Single(r => r.Address == "172.20.3.21").Status == "Label conflict • Branch 3", "Existing hosts with a different label are flagged as conflicts");
        await CaptureBoth(add, "dialog-quick-add");
        add.AcceptForTest();
        await Settle();
        Check(engine.SiteDefinitions().Single(s => s.Name == "Branch 3").Labels["192.0.2.20"] == "Printer & scanner", "Quick Add saves labels with & intact");
        var edit = new EditHostDialog(engine.Snapshot().Hosts.Single(h => h.Address == "192.0.2.20"), (_, _) => null);
        edit.Show(main);
        await Settle();
        await CaptureBoth(edit, "dialog-edit-host");
        edit.Close();
        var confirm = new MessageDialog("Remove host", "Remove 192.0.2.20 from Branch 3?", "Remove", "Cancel", true);
        confirm.Show(main);
        await Settle();
        await CaptureBoth(confirm, "dialog-confirm");
        confirm.Close();
        var quick = new AddHostsDialog(engine, true, null);
        quick.Show(main);
        quick.SetInput("198.51.100.7\tCamera");
        await Settle();
        quick.AcceptForTest();
        await Settle();
        Check(engine.Snapshot().Sites.Any(s => s.Temporary && s.Hosts.Any(h => h.Address == "198.51.100.7")), "Quick Monitor watches hosts for this session");
        main.RefreshAll();
        await Settle();
        Check(main.SiteButtonNames.Contains(WatchdogEngine.QuickMonitorSite), "Quick Monitor appears as a temporary site");

        // Smallest window
        main.SetListView(false);
        main.SelectSite(null);
        main.Width = 900; main.Height = 600;
        await Settle(300);
        Check(main.Map.AllHostsFit && !main.Map.RegionsOverlap() && main.Map.DrawnAddresses.Count == engine.Snapshot().Hosts.Count, $"Map still fits every host at the minimum window size ({main.Map.DrawnAddresses.Count}/{engine.Snapshot().Hosts.Count}, fit={main.Map.AllHostsFit}, overlap={main.Map.RegionsOverlap()})");
        Capture(main, "main-map-900");
        main.Width = 1240; main.Height = 780;

        // A crowded site with long names (#42, #43)
        var crowded = Enumerable.Range(1, 30).Select(i => new ImportedHost($"10.255.{i}.{200 + i}", $"Workstation in the north annex {i}", "", "")).ToList();
        engine.AddHosts("Office & Warehouse with a long name", crowded, createSite: true);
        main.SelectSite(null);
        await Settle(400);
        Check(main.Map.DrawnAddresses.Count == engine.Snapshot().Hosts.Count && main.Map.AllHostsFit && !main.Map.RegionsOverlap(), $"Crowded maps keep every address and site name visible ({main.Map.DrawnAddresses.Count}/{engine.Snapshot().Hosts.Count}, fit={main.Map.AllHostsFit}, overlap={main.Map.RegionsOverlap()})");
        Check(main.Map.DrawnSiteHeadings.Contains("Office & Warehouse with a long name"), "Long site names are drawn");
        await CaptureBoth(main, "main-map-crowded");

        // Settings: staged
        main.OpenSettings();
        await Settle(200);
        var settings = main.Settings!;
        foreach (var page in SettingsWindow.Pages)
        {
            settings.SelectPage(page);
            await Settle(60);
            await CaptureBoth(settings, "settings-" + page.Replace(' ', '-'));
        }
        settings.SelectPage("General");
        Check(!settings.Dirty && settings.SaveStateText == "Saved", "Settings open saved");
        settings.MinimizeToTrayBox.IsChecked = false;
        settings.Use12HourBox.IsChecked = true;
        Check(settings.Dirty && settings.SaveStateText == "Unsaved changes", "Changes are staged");
        Check(engine.Config.MinimizeToTray && !engine.Config.Use12HourTime, "Staged changes aren't applied before saving");
        Check(settings.MonitoringLocked, "Monitoring timing is locked while monitoring");
        Check(settings.Save() && !engine.Config.MinimizeToTray && engine.Config.Use12HourTime && DisplayTime.Use12HourTime, "Save changes applies them");
        settings.AppearanceBox.SelectedIndex = 2;
        settings.VersionOverlayBox.IsChecked = true;
        Check(settings.Save(), "Save appearance and version overlay");
        await Settle(150);
        Check(Application.Current!.ActualThemeVariant == ThemeVariant.Dark && main.VersionOverlayVisible, "Dark appearance and version overlay apply at once");
        Capture(main, "main-map-overlay-Dark");
        settings.AppearanceBox.SelectedIndex = 0;
        settings.VersionOverlayBox.IsChecked = false;
        settings.Use12HourBox.IsChecked = false;
        Check(settings.Save() && !main.VersionOverlayVisible, "Back to the system appearance");
        settings.Close();
        await Settle();
        Check(main.Settings is null, "Settings closes when nothing is unsaved");

        // History and organization
        main.OpenHistory();
        await Settle(250);
        var history = main.History!;
        await Until(() => history.RowCount > 0, "Outage history lists stored events");
        Check(history.CountText.EndsWith("stored", StringComparison.Ordinal), "History shows shown and stored counts");
        history.Width = 1040; history.Height = 650;
        await Settle();
        await CaptureBoth(history, "history");
        history.SiteBox.SelectedItem = "Branch 2";
        await Settle();
        history.Close();
        main.OpenOrganization();
        await Settle(200);
        Check(main.Organization!.ItemCount == 6, "Organize sites lists every saved site, not temporary ones");
        await CaptureBoth(main.Organization!, "organization");
        main.Organization!.Close();

        // Wallboard: display-only
        engine.RemoveHosts(engine.Snapshot().Hosts.Where(h => h.Site.StartsWith("Office", StringComparison.Ordinal)).Select(h => h.Key).ToList());
        main.OpenWallboard();
        await Settle(400);
        var wall = main.Wallboard!;
        await Until(() => { wall.Refresh(engine.Snapshot()); return wall.OutageCount == 2; }, "Wallboard lists active outages");
        Check(wall.CountsText.EndsWith("2 offline", StringComparison.Ordinal) && wall.StateText == "Monitoring", "Wallboard counts and status");
        Check(!wall.GetVisualDescendants().OfType<Button>().Any(b => b.IsVisible && b.Content as string is "Operations" or "Show controls"), "Wallboard has no visible toolbar");
        Check(wall.Map.AllHostsFit && !wall.Map.RegionsOverlap(), "Wallboard map fits every host");
        await CaptureBoth(wall, "wallboard");
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, Source = wall });
        await Settle();
        Check(!wall.PingVisible && !engine.Config.WallboardShowCli, "C hides the Wallboard ping log");
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, Source = wall });
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = wall });
        await Settle(200);
        Check(main.Wallboard is null, "Escape returns from the Wallboard");

        // Priority alarm bar
        var pine = engine.Snapshot().Hosts.Single(h => h.Address == "172.20.2.22");
        engine.SetPriority(new[] { pine.Key }, true);
        main.Alarm.Raise("Branch 2", "172.20.2.22", pine.Label + " (172.20.2.22)", true, DateTime.Now);
        await Settle();
        Check(main.Banner.IsVisible && main.Banner.MessageText.StartsWith("Priority · " + pine.Label, StringComparison.Ordinal) && main.Banner.MessageText.Contains("went offline", StringComparison.Ordinal),
            "Priority alerts show a bar with words, not just color");
        await CaptureBoth(main, "main-priority");
        main.Alarm.AcknowledgeAll();
        await Settle();
        Check(!main.Banner.IsVisible, "Acknowledge clears the bar");
        main.Alarm.Raise("Branch 2", "172.20.2.22", pine.Label, true, DateTime.Now);
        engine.SetPriority(new[] { pine.Key }, false);
        main.RefreshAll();
        await Settle();
        Check(main.Alarm.Active.Count == 0 && !main.Banner.IsVisible, "Removing priority stops the alarm");

        // Update popup
        main.UpdatePopupControl.Show("Up to date");
        await Settle();
        await CaptureBoth(main, "main-update-popup");
        main.UpdatePopupControl.IsVisible = false;

        // Tray
        Check(!await LinuxTrayHost.AvailableAsync(), "Real session-bus check detects the missing tray host in CI");
        bool hostAvailable = true;
        using (var tray = new LinuxTrayService(Application.Current!, main, engine, updates, () => Task.FromResult(hostAvailable)))
        {
            main.AttachTray(tray);
            await tray.RefreshAsync();
            Check(tray.Available, "Native tray exporter is initialized on the Linux session bus");
            var prefs = engine.Snapshot().Settings;
            prefs.MinimizeToTray = true;
            engine.ApplySettings(prefs);
            main.WindowState = WindowState.Minimized;
            bool headlessWayland = Environment.GetEnvironmentVariable("WATCHDOG_BACKEND") == "wayland";
            await tray.MinimizeAsync(requested: headlessWayland);
            await Settle(150);
            Check(!main.IsVisible && engine.Monitoring, "Minimize to tray hides the window while monitoring continues");
            hostAvailable = false;
            await tray.RefreshAsync();
            Check(main.IsVisible && main.WindowState == WindowState.Normal && engine.Monitoring, "Losing the tray host brings the window back");
            main.WindowState = WindowState.Minimized;
            await tray.MinimizeAsync();
            Check(main.IsVisible, "Without a tray host, minimize uses the taskbar");
            tray.Restore();
            Check(main.IsVisible && main.WindowState == WindowState.Normal, "Tray restore opens the main window");
        }
        engine.StopMonitoring();

        // Everything above was saved as it happened.
        using var reloaded = new WatchdogEngine(root);
        Check(reloaded.SiteDefinitions().Single(s => s.Name == "Branch 3").Labels["192.0.2.20"] == "Printer & scanner", "Host edits survive restart");
        Check(!reloaded.SiteDefinitions().Any(s => s.Name == WatchdogEngine.QuickMonitorSite), "Quick Monitor hosts are gone after restart");
        Check(!reloaded.Config.MinimizeToTray == false, "Settings survive restart");
    }

}
