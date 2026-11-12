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

// Logic, layout and regression tests run by --self-test.
public sealed partial class MainForm
{
    internal static void RunSelfTests()
    {
        using var form = new MainForm(
            appNotificationsAvailable: false,
            suppressNotifications: true,
            persistSites: false);

        if (form._selectedSiteName != null)
            throw new InvalidOperationException("All Hosts must be the default startup view.");
        form._restoreLastSiteOnStartup = true;
        form._selectedSiteName = form._sites[0].Name;
        form.ApplyStartupView();
        if (form._selectedSiteName != form._sites[0].Name)
            throw new InvalidOperationException("Startup site override was ignored.");
        form._restoreLastSiteOnStartup = false;
        form.LoadHostEditor();
        TopologyLayout.RunTests();
        UserPreferenceStore.RunTests();
        StartupRegistration.RunTests();
        VersionOverlay.RunTests();
        SafeConfigStore.RunTests();
        SettingsPersistence.RunTests();
        DataBackupStore.RunTests();
        StoragePaths.RunTests();
        DataDeletionStore.RunTests();
        DeleteDataDialog.RunTests();
        ThemeButton.RunDestructiveContrastTests();
        form._configWriteBlocked = true;
        if (form.TrySaveSites())
            throw new InvalidOperationException("Update persistence guard allowed a protected configuration.");
        bool protectedBackupRefused = false;
        try { form.CreateDataBackup(); }
        catch (InvalidOperationException) { protectedBackupRefused = true; }
        finally { form._configWriteBlocked = false; }
        if (!protectedBackupRefused)
            throw new InvalidOperationException("A protected startup configuration was exported as a fallback backup.");
        SettingsForm.RunSaveFailureTests(form.GetAppSettingsSnapshot());
        SupportInfo.RunTests();
        RunSnoozeRegressionTests();
        var snoozedSiteHosts = new[]
        {
            new WallboardHostSnapshot("Snooze health", "192.0.2.1", "", HostState.Online, 1, null),
            new WallboardHostSnapshot("Snooze health", "192.0.2.2", "", HostState.Online, 1, null),
            new WallboardHostSnapshot("Snooze health", "192.0.2.3", "", HostState.Offline, null, DateTime.Now),
            new WallboardHostSnapshot("Snooze health", "192.0.2.4", "", HostState.Offline, null, DateTime.Now, DateTimeOffset.UtcNow.AddHours(1)),
            new WallboardHostSnapshot("Snooze health", "192.0.2.5", "", HostState.Unknown, null, null, DateTimeOffset.UtcNow.AddHours(1))
        };
        if (WallboardCanvas.AggregateSiteState(new WallboardSiteSnapshot("Snooze health", snoozedSiteHosts)) != HostState.Suspect ||
            WallboardCanvas.AggregateSiteState(new WallboardSiteSnapshot("Snooze health", snoozedSiteHosts.Skip(3).ToArray())) != HostState.Unknown)
            throw new InvalidOperationException("Snoozed hosts distorted aggregate site health.");
        int originalSiteCount = form._sites.Count;
        bool newerFormatRejected = false;
        try { form.ApplyConfig(new WatchdogConfig { FormatVersion = 999 }); }
        catch (InvalidDataException) { newerFormatRejected = true; }
        if (!newerFormatRejected || form._sites.Count != originalSiteCount)
            throw new InvalidOperationException("Newer configuration mutated the active sites.");
        DisplayTime.RunTests();
        AppTheme.RunTests();
        HostManagerTests.Run();
        RunHostManagementIntegrationTests();
        var host = new HostMonitor("Test Site", "127.0.0.1");

        void Check(bool condition)
        {
            if (!condition)
                throw new InvalidOperationException("Ping Watchdog self-test failed.");
        }

        void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Ping Watchdog self-test failed: " + message);
        }

        form.ProcessResult(host, false, null);
        Check(host.State == HostState.Suspect && !host.AlertedForCurrentOutage);

        form.ProcessResult(host, true, 1);
        Check(host.State == HostState.Online && host.ConsecutiveFailures == 0);

        for (int i = 0; i < 3; i++)
            form.ProcessResult(host, false, null);

        Check(host.State == HostState.Offline && host.AlertedForCurrentOutage);

        var outage = host.OutageStarted;
        form.ProcessResult(host, true, 1);
        form.ProcessResult(host, false, null);
        Check(host.State == HostState.Offline && host.OutageStarted == outage);

        form.ProcessResult(host, true, 1);
        form.ProcessResult(host, true, 1);
        Check(host.State == HostState.Online && !host.AlertedForCurrentOutage);

        form._ipBox.Text = "127.0.0.1";
        form.PersistCurrentEditor();
        form.SetNicknameValue("Test Site", "127.0.0.1", "Loopback");
        Check(form.GetNickname("Test Site", "127.0.0.1") == "Loopback");
        form.SetCategoryValue("Test Site", "127.0.0.1", "Infrastructure");
        Check(form.GetCategory("Test Site", "127.0.0.1") == "Infrastructure");

        form.AppendCommandLog("Test Site", "127.0.0.1", true, "Reply from 127.0.0.1: time=1ms TTL=128");
        form.AppendCommandLog("Test Site", "127.0.0.1", false, "FAILED (TimedOut)");
        Check(form._commandEntries.Count == 2);

        var configJson = JsonSerializer.Serialize(form.BuildConfig());
        var configRoundTrip = JsonSerializer.Deserialize<WatchdogConfig>(configJson);
        Check(configRoundTrip?.Sites.Count == 1);
        Check(configRoundTrip?.Sites[0].Labels.Values.Contains("Loopback") == true);
        Check(configRoundTrip?.Sites[0].Categories.Values.Contains("Infrastructure") == true);
        Check(configRoundTrip?.PingIntervalSeconds == 2);
        Check(configRoundTrip?.FailureThreshold == 3);
        Check(configRoundTrip?.EventHistoryHours == 24);
        Check(configRoundTrip?.HideSuspectEvents == true);
        Check(configRoundTrip?.AutoCheckUpdates == true);
        Check(configRoundTrip?.MinimizeToTray == true);
        Check(configRoundTrip?.NotificationsEnabled == true);
        Check(configRoundTrip?.WallboardShowCli == true);
        Check(configRoundTrip?.ShowUpdateControlOnHome == false);
        Check(form.GetConfiguredTargets().Count == 1);
        var batch = BuildMonitoringBatch(new[] { ("Site", "127.0.0.1", "Router & exit", 1000) }, 2);
        Check(batch.Contains("goto monitor") && batch.Contains("ping.exe -n 1 -w 1000 \"127.0.0.1\""));
        Check(!batch.Contains("& exit") && !batch.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        bool unsafeTargetRejected = false;
        try { BuildMonitoringBatch(new[] { ("Site", "127.0.0.1 & whoami", "", 1000) }, 2); }
        catch (ArgumentException) { unsafeTargetRejected = true; }
        Check(unsafeTargetRejected);
        string batchFile = Path.Combine(Path.GetTempPath(), "PingWatchdog-" + Guid.NewGuid().ToString("N") + ".bat");
        try
        {
            File.WriteAllText(batchFile, batch, new System.Text.UTF8Encoding(false));
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = "/d /c call \"" + batchFile + "\" --once",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10000);
            if (!exited) process.Kill(true);
            Check(exited && process.ExitCode == 0 && output.GetAwaiter().GetResult().Contains("127.0.0.1"));
        }
        finally { File.Delete(batchFile); }

        Check(UpdateRepoUrl.EndsWith("/JPLuker/Ping-Watchdog", StringComparison.Ordinal));
        Check(form._stateEvents.Any(e => e.Kind == "DOWN"));
        Check(form._stateEvents.Any(e => e.Kind == "RECOVERED"));

        var historyNow = DateTime.Now;
        var historySample = new[]
        {
            new StateEventRecord(historyNow.AddHours(-1), "A", "1", "one", "DOWN", "down"),
            new StateEventRecord(historyNow.AddMinutes(-30), "A", "1", "one", "RECOVERED", "up"),
            new StateEventRecord(historyNow.AddMinutes(-10), "A", "2", "two", "Suspect", "suspect"),
            new StateEventRecord(historyNow.AddHours(-30), "B", "3", "three", "DOWN", "old")
        };
        var filtered24h = FilterStateEvents(historySample, 24, hideSuspects: true, historyNow);
        Check(filtered24h.Count == 2);
        Check(filtered24h.All(e => e.Kind != "Suspect"));
        Check(filtered24h.All(e => e.Timestamp >= historyNow.AddHours(-24)));
        Check(FilterStateEvents(historySample, 0, hideSuspects: false, historyNow).Count == 4);
        Check(GetEventHistoryWindowLabel(168) == "Last 7 days");

        using (var captionBitmap = new Bitmap(800, 300))
        using (var captionGraphics = Graphics.FromImage(captionBitmap))
        using (var labelFont = new Font("Segoe UI", 10.5f))
        using (var addressFont = new Font("Consolas", 9.75f))
        {
            foreach (string address in new[] { "10.235.80.60", "173.161.54.241", "255.255.255.255", "2001:db8:abcd:1234:5678:90ab:cdef:1234" })
            {
                var size = WallboardCanvas.MeasureHostNodeCaption(captionGraphics, "Printer", address, labelFont, addressFont);
                using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
                var required = captionGraphics.MeasureString(address, addressFont, int.MaxValue, format);
                Check(size.Width - 10 >= required.Width);
                Check(size.Height >= required.Height + 6);
            }
        }

        // Many sites must not pile up: headings and host cells never collide, every host stays reachable.
        foreach (var (siteCount, viewport, expectHeadings) in new[]
        {
            (24, new Size(1600, 1000), true),
            (24, new Size(900, 650), true),
            (60, new Size(900, 650), false)
        })
        {
            var denseSites = Enumerable.Range(0, siteCount).Select(s => new WallboardSiteSnapshot(
                $"Site {s:00} " + (s % 5 == 0 ? "with a very long branch office name" : ""),
                Enumerable.Range(0, 2 + s % 4).Select(h => new WallboardHostSnapshot(
                    $"Site {s:00}",
                    h == 0 && s % 3 == 0 ? $"2001:db8:abcd:{s:x4}:5678:90ab:cdef:{h:x4}" : $"10.{s}.0.{h + 1}",
                    h % 2 == 0 ? "" : "Switch",
                    HostState.Online, 1, null)).ToArray())).ToArray();
            using var dense = new WallboardCanvas { Size = viewport, ShowCli = false,
                Snapshot = new WallboardSnapshot(true, DateTime.Now, denseSites,
                    Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var denseBitmap = new Bitmap(viewport.Width, viewport.Height);
            dense.DrawToBitmap(denseBitmap, new Rectangle(Point.Empty, viewport));
            int denseHosts = denseSites.Sum(site => site.Hosts.Count);
            Expect(dense.AllHostNodesFit && dense.VisibleHostCount == denseHosts, $"{siteCount} sites at {viewport} lost hosts.");
            Expect(!dense.TopologyRegionsOverlap(), $"{siteCount} sites at {viewport} overlap.");
            if (expectHeadings)
                Expect(dense.DrawnSiteHeadings.Count == siteCount, $"{siteCount} sites at {viewport} dropped site names.");
        }

        Expect(EventHistoryForm.KindFont.SizeInPoints >= AppTheme.CaptionFont.SizeInPoints, "History event type text is below the reading floor.");

        // The All sites outline is quieter than muted text but still meets the 3:1 component floor.
        foreach (bool dark in new[] { false, true })
        {
            var palette = ThemePalette.Create(dark);
            var outline = AppTheme.SoftOutline(palette, palette.Surface);
            double outlineRatio = AppTheme.ContrastRatio(outline, palette.Surface);
            Expect(outlineRatio >= 3.0, $"{(dark ? "Dark" : "Light")} outline contrast is {outlineRatio:0.00}:1.");
            Expect(outlineRatio < AppTheme.ContrastRatio(palette.Muted, palette.Surface), $"{(dark ? "Dark" : "Light")} outline is not softer than muted.");
        }

        // A lone host stacks under its site at a natural row height instead of floating mid-block.
        using (var lone = new WallboardCanvas { Size = new Size(1600, 1000), ShowCli = false,
            Snapshot = new WallboardSnapshot(true, DateTime.Now,
                new[] { new WallboardSiteSnapshot("Camp Hidden Falls", new[] { new WallboardHostSnapshot("Camp Hidden Falls", "10.233.100.5", "Server", HostState.Online, 1, null) }) },
                Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) })
        using (var loneBitmap = new Bitmap(1600, 1000))
        {
            lone.DrawToBitmap(loneBitmap, new Rectangle(0, 0, 1600, 1000));
            var heading = lone.SiteHeadingBounds.Single().Bounds;
            var cell = lone.HostCellBounds.Single();
            using var g = Graphics.FromImage(loneBitmap);
            float lineHeight = lone.HostAddressFont.GetHeight(g), dpi = lone.DeviceDpi / 96f;
            Expect(cell.Height <= lineHeight * 2 + 16 * dpi + 1, $"Lone host row is {cell.Height}px tall.");
            Expect(cell.Top - heading.Bottom <= 8 * dpi, $"Lone host floats {cell.Top - heading.Bottom}px below its site.");
        }

        // With room to spare, every site name shows in full.
        using (var named = new WallboardCanvas { Size = new Size(1600, 1000), ShowCli = false,
            Snapshot = new WallboardSnapshot(true, DateTime.Now,
                new[] { "Clarksville Thrift Store", "Richmond 360 Life Center", "Eagle Creek - Booth Manor", "Carpenter Apartments", "Michigan City Corps", "New Albany Corps",
                        "Camp Hidden Falls", "Terre Haute Corps", "Shelbyville Corps", "Johnson County Center", "Indy Eagle Creek Corps", "Lafayette Corps" }
                    .Select((name, i) => new WallboardSiteSnapshot(name, new[] { new WallboardHostSnapshot(name, $"10.233.{i}.5", "", HostState.Online, 1, null) })).ToArray(),
                Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) })
        using (var namedBitmap = new Bitmap(1600, 1000))
        {
            named.DrawToBitmap(namedBitmap, new Rectangle(0, 0, 1600, 1000));
            using var g = Graphics.FromImage(namedBitmap);
            foreach (var (bounds, site) in named.SiteHeadingBounds)
            {
                float needed = g.MeasureString(site, named.SiteNameFont, int.MaxValue, StringFormat.GenericTypographic).Width;
                Expect(needed <= bounds.Width + 1, $"Site name '{site}' is cut off ({needed:0} > {bounds.Width}).");
            }
            Expect(named.SiteHeadingBounds.Count == 12, "Named sites lost headings.");
        }

        // Updates: at most one automatic check a day (clock rollback re-arms it); IT policy turns them off.
        var checkedAt = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        Expect(UpdatePolicy.IsDue(null, checkedAt), "First update check was not due.");
        Expect(UpdatePolicy.IsInstalled("v1.19.30", "1.19.30") && UpdatePolicy.IsInstalled("v1.20.0", "v1.19.30"), "An installed update read as missing.");
        Expect(!UpdatePolicy.IsInstalled("v1.19.29", "1.19.30") && !UpdatePolicy.IsInstalled("v1.19.29", "the downloaded version"), "A missing update read as installed.");
        Expect(!UpdatePolicy.IsDue(checkedAt, checkedAt.AddHours(23)), "Update check ran twice in a day.");
        Expect(UpdatePolicy.IsDue(checkedAt, checkedAt.AddHours(24)), "Daily update check was not due after a day.");
        Expect(UpdatePolicy.IsDue(checkedAt, checkedAt.AddHours(-3)), "A clock moved backwards blocked update checks.");
        Expect(UpdatePolicy.Evaluate(1, null) && UpdatePolicy.Evaluate("1", null) && UpdatePolicy.Evaluate(null, "1") && UpdatePolicy.Evaluate(null, " true "),
            "Update policy did not honour an enabled value.");
        Expect(!UpdatePolicy.Evaluate(0, null) && !UpdatePolicy.Evaluate(null, "0") && !UpdatePolicy.Evaluate(null, null) && !UpdatePolicy.Evaluate(new byte[] { 1 }, ""),
            "Update policy disabled updates without a clear setting.");
        var checkPrefs = JsonSerializer.Deserialize<UserPreferences>(JsonSerializer.Serialize(new UserPreferences { LastUpdateCheckUtc = checkedAt }))!;
        Expect(checkPrefs.LastUpdateCheckUtc == checkedAt, "Last update check time is not saved.");

        // Priority alarm: rings at once, repeats every 15 s, latest change per host wins, stops only when all are acknowledged.
        int rings = 0;
        using (var alarm = new PriorityAlarm(_ => rings++))
        {
            Expect(PriorityAlarm.RepeatInterval == TimeSpan.FromSeconds(15), "Priority alarm does not repeat every 15 seconds.");
            var down = alarm.Raise("Anderson", "10.233.48.1", "Watchguard", offline: true, DateTime.Now);
            Expect(rings == 1 && alarm.Ringing && alarm.Active.Count == 1, "A priority alert did not ring immediately.");
            alarm.Ring();
            Expect(rings == 2, "Repeat ring did not play.");
            var up = alarm.Raise("anderson", "10.233.48.1", "Watchguard", offline: false, DateTime.Now);
            Expect(alarm.Active.Count == 1 && !alarm.Active[0].Offline && alarm.Active[0].Id == up.Id, "A newer change did not replace the host's alert.");
            Expect(!alarm.Acknowledge(down.Id), "A replaced alert could still be acknowledged.");
            alarm.Raise("DHQ", "10.0.0.1", "Core switch", offline: true, DateTime.Now);
            Expect(alarm.Acknowledge(up.Id) && alarm.Ringing && alarm.Active.Count == 1, "Acknowledging one alert stopped the others.");
            alarm.AcknowledgeAll();
            int before = rings;
            alarm.Ring();
            Expect(!alarm.Ringing && alarm.Active.Count == 0 && rings == before, "The alarm kept ringing after everything was acknowledged.");
        }
        var priorityOptions = new HostOptions { Priority = true };
        Expect(JsonSerializer.Deserialize<HostOptions>(JsonSerializer.Serialize(priorityOptions))!.Priority && priorityOptions.Copy().Priority, "Priority flag is not saved.");

        // Paste: hidden characters, line breaks and HTML-only copies still become clean names; a busy clipboard is reported.
        Expect(ClipboardText.NormalizeLine("  Anderson\u00A0Corps\u200B\r\n") == "Anderson Corps", "Pasted name kept hidden characters.");
        Expect(ClipboardText.NormalizeMultiline("10.0.0.1\tPrinter\u200B\r\n10.0.0.2") == "10.0.0.1\tPrinter\r\n10.0.0.2", "Multi-line paste lost its rows or kept hidden characters.");
        var htmlOnly = new DataObject();
        htmlOnly.SetData(DataFormats.Html, "Version:0.9\r\nStartHTML:0\r\n<html><body><!--StartFragment--><b>Camp&nbsp;Hidden Falls</b><!--EndFragment--></body></html>");
        Expect(ClipboardText.NormalizeLine(ClipboardText.Extract(htmlOnly) ?? "") == "Camp Hidden Falls", "HTML-only clipboard pasted nothing.");
        var plain = new DataObject(DataFormats.UnicodeText, "Gary\r\n");
        Expect(ClipboardText.Read(() => plain).Text == "Gary\r\n", "Plain clipboard text was not read.");
        var busy = ClipboardText.Read(() => throw new System.Runtime.InteropServices.ExternalException("busy"), attempts: 2);
        Expect(busy.Busy && busy.Text is null, "A busy clipboard was not reported.");
        using (var pasteBox = new PlainTextBox())
        {
            pasteBox.PasteText("Eagle Creek -\r\nBooth Manor\u200B");
            Expect(pasteBox.Text == "Eagle Creek - Booth Manor", $"Single-line paste produced '{pasteBox.Text}'.");
        }

        // "&" in names is a character, not a keyboard-shortcut marker, in owner-drawn rows, labels and menus.
        using (var nameFont = new Font("Segoe UI Semibold", 10.5f))
        {
            const string ampersandName = "Women & Children's Center";
            int asTyped = TextRenderer.MeasureText(ampersandName, nameFont, new Size(int.MaxValue, int.MaxValue), AppTheme.NameTextFlags).Width;
            int asShortcut = TextRenderer.MeasureText(ampersandName, nameFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Width;
            Expect(asTyped > asShortcut, "Site rows still treat & as a shortcut marker.");
        }
        Expect(AppTheme.MenuText("Women & Children's Center") == "Women && Children's Center", "Menu names would hide their &.");
        using (var themedForm = new Form())
        {
            var nameLabel = new Label { Text = "Women & Children's Center" };
            themedForm.Controls.Add(nameLabel);
            AppTheme.Apply(themedForm);
            Expect(!nameLabel.UseMnemonic, "Themed labels still treat & as a shortcut marker.");
        }

        // #43: repeated resizes and an outage sidebar must never drop host addresses.
        foreach (bool embedded in new[] { true, false })
        using (var crowded = new WallboardCanvas { ShowCli = false, Embedded = embedded,
            Snapshot = new WallboardSnapshot(true, DateTime.Now,
                Enumerable.Range(1, 8).Select(site => new WallboardSiteSnapshot($"Regional office {site} - service desk",
                    Enumerable.Range(1, site == 1 ? 40 : 4).Select(host => new WallboardHostSnapshot(
                        $"Regional office {site} - service desk", $"172.20.{site}.{host}", $"Desktop workstation {host}",
                        host == 1 ? HostState.Offline : HostState.Online, 1, null)).ToArray())).ToArray(),
                Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) })
        foreach (var viewport in new[] { new Size(740, 345), new Size(1280, 720), new Size(640, 300), new Size(740, 345) })
        {
            crowded.Size = viewport;
            using var bitmap = new Bitmap(viewport.Width, viewport.Height);
            crowded.DrawToBitmap(bitmap, new Rectangle(Point.Empty, viewport));
            Expect(crowded.DrawnAddresses.Count == 68,
                $"#43: {embedded}/{viewport} drew {crowded.DrawnAddresses.Count} of 68 addresses.");
            Expect(crowded.DrawnSiteHeadings.Count == 8, "#42: a crowded map dropped site headings.");
            Expect(crowded.AllHostNodesFit && !crowded.TopologyRegionsOverlap(), "#43: scaled hit regions overlap or leave the viewport.");
        }

        // The accepted map look: five branches of five hosts sit in one row at the default window size, labels shown.
        using (var accepted = new WallboardCanvas { Size = new Size(740, 345), ShowCli = false, Embedded = true,
            Snapshot = new WallboardSnapshot(true, DateTime.Now, Enumerable.Range(1, 5).Select(site => new WallboardSiteSnapshot($"Branch {site}",
                Enumerable.Range(1, 5).Select(host => new WallboardHostSnapshot($"Branch {site}", $"192.0.2.{site * 10 + host}", host == 2 ? "Printer" : host == 3 ? "Cloudkey" : "", HostState.Online, 1, null)).ToArray())).ToArray(),
                Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) })
        using (var acceptedBitmap = new Bitmap(740, 345))
        {
            accepted.DrawToBitmap(acceptedBitmap, new Rectangle(0, 0, 740, 345));
            var tops = accepted.SiteHeadingBounds.Select(h => h.Bounds.Top).Distinct().Count();
            Expect(accepted.SiteHeadingBounds.Count == 5 && tops == 1, $"Five branches used {tops} rows instead of one.");
            Expect(accepted.DrawnAddresses.Count == 25, "The accepted map lost addresses.");
        }

        // #41: the map always shows whole addresses, and the layout leaves room for them.
        foreach (var (siteCount, hostsPerSite, viewport) in new[] { (5, 5, new Size(1000, 500)), (12, 4, new Size(1280, 720)), (24, 2, new Size(1600, 1000)) })
        {
            var fullSites = Enumerable.Range(0, siteCount).Select(s => new WallboardSiteSnapshot($"Site {s:00}",
                Enumerable.Range(0, hostsPerSite).Select(h => new WallboardHostSnapshot($"Site {s:00}", $"10.233.{100 + s}.{200 + h}", h % 2 == 0 ? "" : "Switch", HostState.Online, 1, null)).ToArray())).ToArray();
            using var full = new WallboardCanvas { Size = viewport, ShowCli = false,
                Snapshot = new WallboardSnapshot(true, DateTime.Now, fullSites, Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var fullBitmap = new Bitmap(viewport.Width, viewport.Height);
            full.DrawToBitmap(fullBitmap, new Rectangle(Point.Empty, viewport));
            var expected = fullSites.SelectMany(site => site.Hosts.Select(host => host.Address)).OrderBy(a => a).ToArray();
            var drawn = full.DrawnAddresses.OrderBy(a => a).ToArray();
            Expect(drawn.SequenceEqual(expected), $"{siteCount}x{hostsPerSite} at {viewport} drew {drawn.Length} of {expected.Length} addresses; first missing: {expected.Except(drawn).FirstOrDefault() ?? "none"}.");
            using var g = Graphics.FromImage(fullBitmap);
            foreach (var cell in full.HostCellBounds)
                Expect(g.MeasureString("10.233.123.204", full.HostAddressFont, int.MaxValue, StringFormat.GenericTypographic).Width <= cell.Width - 24 * full.DeviceDpi / 96f,
                    $"{siteCount}x{hostsPerSite} at {viewport}: a {cell.Width}px host cell is too narrow for a whole address.");
        }

        var snoozeNow = DateTimeOffset.UtcNow;
        var snoozeOptions = new HostOptions { SnoozedUntilUtc = snoozeNow.AddHours(1) };
        Check(snoozeOptions.IsSnoozed(snoozeNow));
        Check(!snoozeOptions.IsSnoozed(snoozeNow.AddHours(1)));
        Check(!snoozeOptions.IsSnoozed(snoozeNow.AddHours(2)));
        var snoozeRoundTrip = JsonSerializer.Deserialize<HostOptions>(JsonSerializer.Serialize(snoozeOptions))!;
        Check(snoozeRoundTrip.SnoozedUntilUtc == snoozeOptions.SnoozedUntilUtc);
        Check(snoozeOptions.Copy().IsSnoozed(snoozeNow));
        snoozeOptions.SnoozedUntilUtc = null;
        Check(!snoozeOptions.IsSnoozed(snoozeNow));

        foreach (var viewport in new[] { new Size(1600, 1000), new Size(900, 650) })
        {
            var hitHosts = Enumerable.Range(1, 8).Select(i =>
                new WallboardHostSnapshot("Hit test", $"192.0.2.{i}", $"Host {i}", HostState.Online, 1, null)).ToArray();
            using var canvas = new WallboardCanvas { Size = viewport, ShowCli = false,
                Snapshot = new WallboardSnapshot(true, DateTime.Now,
                    new[] { new WallboardSiteSnapshot("Hit test", hitHosts) },
                    Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var bitmap = new Bitmap(viewport.Width, viewport.Height);
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, viewport));
            var found = new HashSet<string>();
            // Every host must fit and remain reachable at narrow sizes.
            Check(canvas.AllHostNodesFit && canvas.VisibleHostCount == hitHosts.Length);
            for (int y = 0; y < viewport.Height; y += 4)
                for (int x = 0; x < viewport.Width; x += 4)
                    if (canvas.HitTestHost(new Point(x, y)) is { } hit) found.Add(hit.Address);
            Check(found.Count == hitHosts.Length);
            Check(canvas.HitTestHost(Point.Empty) == null);
            var accessible = canvas.AccessibilityObject;
            Check(accessible.GetChildCount() == hitHosts.Length);
            Check(accessible.GetChild(0)!.Name!.Contains("192.0.2.1") && accessible.GetChild(0)!.Name!.Contains("Online"));
            WallboardHostSnapshot? actionHost = null;
            canvas.HostRightClicked += (target, _) => actionHost = target;
            // No highlight until a key is used; keys show it; it fades but the selection stays.
            Expect(!canvas.KeyboardHighlightVisible, "Map highlighted a host before any key press.");
            canvas.HandleHostKey(Keys.Home);
            canvas.HandleHostKey(Keys.Right);
            Check(canvas.SelectedHost?.Address == "192.0.2.2");
            Expect(canvas.KeyboardHighlightVisible && WallboardCanvas.KeyboardHighlightDuration == TimeSpan.FromSeconds(8), "Keyboard use did not show the highlight.");
            canvas.ExpireKeyboardHighlightForTest();
            Expect(!canvas.KeyboardHighlightVisible && canvas.SelectedHost?.Address == "192.0.2.2", "Highlight timeout lost the selection or kept the box.");
            canvas.HandleHostKey(Keys.Shift | Keys.F10);
            Check(actionHost?.Address == "192.0.2.2");
            accessible.GetChild(7)!.DoDefaultAction();
            Check(actionHost?.Address == "192.0.2.8");
            var removedChild = accessible.GetChild(7)!;
            actionHost = null;
            canvas.Snapshot = canvas.Snapshot! with { Sites = new[] {
                new WallboardSiteSnapshot("Hit test", hitHosts.Take(7).ToArray()) } };
            canvas.HandleHostKey(Keys.Enter);
            Check(actionHost == null); // A stale selection must not target a removed host.
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, viewport));
            Check(accessible.GetChildCount() == 7);
            removedChild.DoDefaultAction();
            Check(actionHost == null && removedChild.State.HasFlag(AccessibleStates.Unavailable));
        }

        using (var outageCanvas = new WallboardCanvas { Size = new Size(1600, 1000), ShowCli = false })
        using (var outageBitmap = new Bitmap(1600, 1000))
        {
            var active = new WallboardHostSnapshot("Outage site", "192.0.2.1", "Router", HostState.Offline, null, DateTime.Now.AddMinutes(-5));
            var recovered = new WallboardHostSnapshot("Outage site", "192.0.2.2", "Printer", HostState.Online, 1, null);
            outageCanvas.Snapshot = new WallboardSnapshot(true, DateTime.Now,
                new[] { new WallboardSiteSnapshot("Outage site", new[] { active, recovered }) },
                new[] { new WallboardEventSnapshot(DateTime.Now, recovered.Site, "Printer (192.0.2.2)", "RECOVERED", "", recovered.Address) },
                Array.Empty<WallboardCommandSnapshot>(), "24 hours", true);
            outageCanvas.DrawToBitmap(outageBitmap, new Rectangle(0, 0, 1600, 1000));
            Check(outageCanvas.CurrentOutageHosts.Select(host => host.Address).SequenceEqual(new[] { active.Address }));
            WallboardHostSnapshot? outageAction = null;
            outageCanvas.HostRightClicked += (target, _) => outageAction = target;
            outageCanvas.HandleHostKey(Keys.End);
            outageCanvas.HandleHostKey(Keys.Apps);
            Check(outageAction?.Address == active.Address);
            Check(outageCanvas.AccessibilityObject.GetSelected()!.Name!.Contains("Active outage"));
            outageCanvas.Snapshot = outageCanvas.Snapshot! with { Sites = new[] {
                new WallboardSiteSnapshot("Outage site", new[] { active with { SnoozedUntilUtc = DateTimeOffset.UtcNow.AddHours(1) }, recovered }) } };
            outageCanvas.DrawToBitmap(outageBitmap, new Rectangle(0, 0, 1600, 1000));
            Check(outageCanvas.CurrentOutageHosts.Count == 0);
            Check(outageCanvas.AccessibilityObject.GetChild(0)!.Name!.Contains("Snoozed"));
            outageCanvas.Snapshot = outageCanvas.Snapshot with { Sites = new[] {
                new WallboardSiteSnapshot("Outage site", new[] { active with { State = HostState.Online, OutageStarted = null }, recovered }) } };
            outageCanvas.DrawToBitmap(outageBitmap, new Rectangle(0, 0, 1600, 1000));
            Check(outageCanvas.CurrentOutageHosts.Count == 0);

        }

        foreach (var size in new[] { new Size(900, 650), new Size(1600, 1000), new Size(1920, 1080) })
        foreach (bool showCli in new[] { false, true })
        {
            WallboardHostSnapshot[] Branch(string site, int count) => Enumerable.Range(0, count).Select(i =>
                new WallboardHostSnapshot(site, $"192.0.2.{i + 1}", i == 3 ? "Cloudkey" : "Host " + i,
                    HostState.Online, 1, null)).ToArray();
            using var canvas = new WallboardCanvas { Size = size, ShowCli = showCli,
                Snapshot = new WallboardSnapshot(true, DateTime.Now, new[]
                {
                    new WallboardSiteSnapshot("Hammond Munster", Branch("Hammond Munster", 8)),
                    new WallboardSiteSnapshot("Gary", Branch("Gary", 6)),
                    new WallboardSiteSnapshot("East Chicago", Branch("East Chicago", 6)),
                    new WallboardSiteSnapshot("Anderson", Branch("Anderson", 1))
                }, Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
            using var bitmap = new Bitmap(size.Width, size.Height);
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            Check(canvas.HasHostCaption("East Chicago", "192.0.2.4"));
            Check(!canvas.CaptionsOverlap());
            Check(canvas.AllHostNodesFit && canvas.VisibleHostCount == canvas.Snapshot!.Sites.Sum(site => site.Hosts.Count));
        }

        var labeledNode = WallboardCanvas.FormatHostNodeLines(
            new WallboardHostSnapshot(
                "Test Site",
                "10.0.0.1",
                "Router",
                HostState.Online,
                1,
                null));
        Check(labeledNode.Primary == "Router");
        Check(labeledNode.Secondary == "10.0.0.1");

        var unlabeledNode = WallboardCanvas.FormatHostNodeLines(
            new WallboardHostSnapshot(
                "Test Site",
                "10.0.0.2",
                "",
                HostState.Online,
                1,
                null));
        Check(unlabeledNode.Primary == "10.0.0.2");
        Check(unlabeledNode.Secondary is null);

        var wallboardSnapshot = form.BuildWallboardSnapshot();
        Check(wallboardSnapshot.Sites.Count == 1);
        Check(wallboardSnapshot.Sites[0].Hosts.Count == 1);
        Check(wallboardSnapshot.Commands.Count == 2);
        Check(wallboardSnapshot.Commands[0].Text.Contains("ping 127.0.0.1", StringComparison.Ordinal));

        var wallboardControls = form.BuildWallboardControlSnapshot();
        Check(wallboardControls.Sites.Count == 1);
        Check(wallboardControls.Hosts.Count == 1);
        Check(wallboardControls.SelectedSite == "Test Site");
        Check(form.WallboardAddSite("Branch") is null);
        Check(form.WallboardSaveHosts("Branch", "10.0.0.1\r\n10.0.0.2") is null);
        Check(form.WallboardRenameSite("Branch", "Branch Renamed") is null);
        Check(form.WallboardSetLabel("Branch Renamed", "10.0.0.1", "Router") is null);
        var editedWallboardControls = form.BuildWallboardControlSnapshot();
        Check(editedWallboardControls.SelectedSite == "Branch Renamed");
        Check(editedWallboardControls.Hosts.Count == 2);
        Check(editedWallboardControls.Hosts.Any(h => h.Label == "Router"));
        Check(form.WallboardDeleteSite("Branch Renamed") is null);
        form.WallboardSelectSite("Test Site");
        Check(form.BuildWallboardControlSnapshot().Sites.Count == 1);

        Check(form.AddOrganizationFolder("", "Indiana") is null);
        Check(form.AddOrganizationFolder("Indiana", "North") is null);
        Check(form.MoveOrganizationSite("Test Site", "Indiana/North") is null);
        Check(form.FindSite("Test Site")?.FolderPath == "Indiana/North");
        Check(form.RenameOrganizationFolder("Indiana/North", "Northwest") is null);
        Check(form.FindSite("Test Site")?.FolderPath == "Indiana/Northwest");
        Check(form.MoveOrganizationSite("Test Site", "") is null);
        Check(form.DeleteOrganizationFolder("Indiana/Northwest") is null);
        Check(NormalizeFolderPath(@" Indiana\\South / Branches ") == "Indiana/South/Branches");

        var majorityOnlineSite = new WallboardSiteSnapshot(
            "Majority Online",
            new[]
            {
                new WallboardHostSnapshot("Majority Online", "1", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Online", "2", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Online", "3", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Online", "4", "", HostState.Offline, null, DateTime.Now),
                new WallboardHostSnapshot("Majority Online", "5", "", HostState.Offline, null, DateTime.Now)
            });
        Check(WallboardCanvas.AggregateSiteState(majorityOnlineSite) == HostState.Suspect);

        var majorityOfflineSite = new WallboardSiteSnapshot(
            "Majority Offline",
            new[]
            {
                new WallboardHostSnapshot("Majority Offline", "1", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Offline", "2", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Majority Offline", "3", "", HostState.Offline, null, DateTime.Now),
                new WallboardHostSnapshot("Majority Offline", "4", "", HostState.Offline, null, DateTime.Now),
                new WallboardHostSnapshot("Majority Offline", "5", "", HostState.Offline, null, DateTime.Now)
            });
        Check(WallboardCanvas.AggregateSiteState(majorityOfflineSite) == HostState.Offline);

        var tiedSite = new WallboardSiteSnapshot(
            "Tie",
            new[]
            {
                new WallboardHostSnapshot("Tie", "1", "", HostState.Online, 1, null),
                new WallboardHostSnapshot("Tie", "2", "", HostState.Offline, null, DateTime.Now)
            });
        Check(WallboardCanvas.AggregateSiteState(tiedSite) == HostState.Offline);
        Check(WallboardCanvas.CalculateTopologyRenderScale(new Rectangle(0, 0, 820, 300), 3, 25) < 0.80);
        Check(WallboardCanvas.CalculateTopologyRenderScale(new Rectangle(0, 0, 1400, 700), 2, 10) == 1.0);

        form.ClientSize = new Size(960, 640);
        form.ApplyResponsiveLayout();
        Check(form._workspaceSplit.SplitterDistance <= 240);
        Check(form._rootLayout?.RowStyles[0].Height == 0);
        Check(form._settingsFlowPanel?.WrapContents == true);

        form.ClientSize = new Size(1320, 840);
        form.ApplyResponsiveLayout();
        Check(form._workspaceSplit.SplitterDistance >= 224);






        Check(form._checkUpdateButton.Parent is not null);
        Check(form._wallboardButton.Parent is not null);
        Check(form._settingsButton.Parent is not null);
        Check(form._moreButton.Parent is not null);
        Check(form._headerActionsPanel?.Controls.Contains(form._checkUpdateButton) == true);
        Check(form._headerActionsPanel?.Controls.Contains(form._settingsButton) == true);
        Check(form._moreButton.Text == "More");
        Check(form._moreButton.Width >= 48);
        Check(form._moreButton.Padding.Left >= 12);
        Check(form._saveConfigButton.Parent is null);
        Check(form._loadConfigButton.Parent is null);
        Check(form._headerActionsPanel?.WrapContents == false);

        form._closingApplication = false;
        form.WindowState = FormWindowState.Minimized;
        form.Hide();
        form.RestoreAfterWallboard();
        Check(form.Visible);
        Check(form.WindowState != FormWindowState.Minimized);

        var settingsSnapshot = form.GetAppSettingsSnapshot();
        Check(settingsSnapshot.AutoCheckUpdates);
        Check(settingsSnapshot.MinimizeToTray);
        Check(settingsSnapshot.NotificationsEnabled);
        Check(settingsSnapshot.WallboardShowCli);
        Check(!settingsSnapshot.ShowUpdateControlOnHome);
        Check(settingsSnapshot.UpdateActionText == "Check for Updates");

        form.SetUpdateReadyUi("9.9.9");
        Check(form._checkUpdateButton.Text == "Check for updates");
        Check(form._trayUpdateItem.Enabled);
        Check((form._trayUpdateItem.Text ?? string.Empty).Contains("9.9.9", StringComparison.Ordinal));
        form.ClearPendingUpdateUi();

        using var ping = new Ping();
        Check(ping.Send("127.0.0.1", 1000).Status == IPStatus.Success);

        form._trayIcon.Visible = false;
        form._trayIcon.Dispose();
        form._siteItemFont.Dispose();
        form._statusCellFont.Dispose();
        form._appIcon.Dispose();
        form._brandBitmap.Dispose();
    }
}
