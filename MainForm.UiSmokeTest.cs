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

// Drives the real window for --ui-smoke-test and saves captures to ui-smoke-test/.
public sealed partial class MainForm
{
    // Runs the published executable's real WinForms message loop with isolated settings.
    internal static void RunUiSmokeTest()
    {
        string output = Path.Combine(Environment.CurrentDirectory, "ui-smoke-test");
        Directory.CreateDirectory(output);
        using var form = new MainForm(false, suppressNotifications: true, persistSites: false);
        form._autoCheckUpdates = false;
        form._minimizeToTray = false;
        // The hosted runner's 1024px desktop otherwise clamps the requested test widths.
        form.MaximumSize = new Size(1920, 1080);
        Environment.ExitCode = 1;

        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static IEnumerable<Control> Descendants(Control parent)
        {
            yield return parent;
            foreach (Control child in parent.Controls)
                foreach (var descendant in Descendants(child)) yield return descendant;
        }

        // Gaps, unselected rows, empty list space and the well's own padding must all be the one well color.
        void CheckSiteWellBlends(MainForm window)
        {
            var list = window._siteList;
            var well = window._siteWell!;
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var origin = window.Bounds.Location;
            var expected = ScrollWell.WellColor;
            var samples = new List<(string Where, Point At)>();
            int item = list.ItemHeight, width = list.ClientSize.Width;
            for (int i = list.TopIndex; i + 1 < list.Items.Count && (i - list.TopIndex + 2) * item <= list.ClientSize.Height; i++)
            {
                if (i == list.SelectedIndex || i + 1 == list.SelectedIndex) continue;
                int y = (i - list.TopIndex + 1) * item;
                samples.Add(("gap between rows", list.PointToScreen(new Point(width / 2, y))));
                samples.Add(("unselected row", list.PointToScreen(new Point(width - 10, y + item / 2))));
                break;
            }
            int itemsBottom = (list.Items.Count - list.TopIndex) * item;
            if (itemsBottom + 8 < list.ClientSize.Height)
                samples.Add(("empty list space", list.PointToScreen(new Point(width / 2, itemsBottom + 6))));
            samples.Add(("well padding", well.PointToScreen(new Point(well.ClientSize.Width / 2, 2))));
            foreach (var (where, at) in samples)
            {
                var pixel = bitmap.GetPixel(at.X - origin.X, at.Y - origin.Y);
                bool close = Math.Abs(pixel.R - expected.R) <= 3 && Math.Abs(pixel.G - expected.G) <= 3 && Math.Abs(pixel.B - expected.B) <= 3;
                Check(close, $"Site well {where} is {ColorTranslator.ToHtml(pixel)}, expected {ColorTranslator.ToHtml(expected)}.");
            }
        }

        void Capture(Form window, string name)
        {
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        void DismissWithEscape(Form window)
        {
            var escape = typeof(Form).GetMethod("ProcessDialogKey",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Check((bool)escape.Invoke(window, new object[] { Keys.Escape })! && window.IsDisposed,
                window.Text + ": Escape did not dismiss the window.");
        }

        async Task CheckHostMenu(Control surface)
        {
            var host = form.HostSnapshot().First();
            for (int i = 0; i < 3; i++)
            {
                form.ShowHostQuickActions(surface, new Point(20, 20), host.Site, host.Address);
                Check(form._hostQuickMenu.Visible, "Host menu did not open.");
                Check(!form._hostQuickMenu.ShowImageMargin && !form._hostQuickMenu.ShowCheckMargin, "Empty menu icon gutter returned.");
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.AppClicked);
                Check(!form._hostQuickMenu.IsDisposed && form._hostQuickMenu.IsHandleCreated,
                    "Closing the host menu disposed it during WinForms close processing.");
                await Task.Delay(50);
            }
            foreach (string action in new[] { "Snooze host for 1 hour", "Resume monitoring now" })
            {
                form.ShowHostQuickActions(surface, new Point(20, 20), host.Site, host.Address);
                form._hostQuickMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == action).PerformClick();
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
                await Task.Delay(50);
                Check(!form._hostQuickMenu.IsDisposed, "A quick action destroyed the host menu.");
                Check(form.HostSnapshot().Single(h => h.Options.Id == host.Options.Id).Options.IsSnoozed(DateTimeOffset.UtcNow)
                    == (action == "Snooze host for 1 hour"), "Host snooze action did not apply.");
            }
        }

        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(200);
                // #40: a busy window is noticed and the wait is logged with what the app was doing.
                string stallLogs = Path.Combine(Path.GetTempPath(), "PingWatchdog-stall-" + Guid.NewGuid().ToString("N"));
                using (var guard = new ResponsivenessGuard(form, stallLogs))
                {
                    await Task.Delay(300);
                    using (UiActivity.Begin("Test wait"))
                        Thread.Sleep(1400);
                    await Task.Delay(400);
                    Check(File.Exists(guard.LogPath) && File.ReadAllText(guard.LogPath).Contains("not responding") &&
                        File.ReadAllText(guard.LogPath).Contains("Test wait"), "A 1.4 s wait was not logged with its activity.");
                }
                Directory.Delete(stallLogs, recursive: true);
                form.WindowState = FormWindowState.Normal;
                foreach (int width in new[] { 900, 1060, 1320 })
                {
                    form.ClientSize = new Size(width, 740);
                    form.ApplyResponsiveLayout();
                    await Task.Delay(100);
                    Check(form.Visible && form.IsHandleCreated, "Main window did not appear.");
                    Check(form.ClientSize.Width == width, "Runner clamped the requested window width.");
                    Check(form._versionLabel.Parent is null && form._monitorStateLabel.Parent is null,
                        "Duplicate header chrome returned.");
                    Check(form.Controls.Find("WatchdogBrandLogo", true).Length == 0,
                        "Repeated branding returned to the workspace.");
                    Check(form._siteHeaderLabel.Parent!.ClientRectangle.Contains(form._siteHeaderLabel.Bounds),
                        "Selected-site heading is clipped.");
                    Check(form._startButton.Parent!.ClientRectangle.Contains(form._startButton.Bounds),
                        "Monitoring action is clipped.");
                    Check(form._wallboardButton.Parent!.ClientRectangle.Contains(form._wallboardButton.Bounds),
                        "Wallboard action is clipped.");
                    Check(form._siteWell!.ClientRectangle.Contains(form._siteList.Bounds), "Site list escapes its well.");
                    Check(form._allSitesButton.Bottom <= form._siteWell.Top && form._sectionDivider!.Bottom <= form._headerActionsPanel!.Top,
                        "Sidebar sections overlap.");
                    CheckSiteWellBlends(form);
                    Check(form._siteList.ClientSize.Width == form._siteList.Width, "Site list still shows a native scrollbar.");
                    Check(form._allSitesButton.Outline, "All sites lost its outline.");
                    foreach (Control setting in form._settingsFlowPanel!.Controls.Cast<Control>().Where(control => control.Visible))
                        Check(setting.Bottom <= form._settingsFlowPanel.ClientSize.Height, "Wrapped monitoring control is clipped.");
                    Capture(form, "main-" + width);
                }

                // Larger UI text must grow the button rather than truncate its final letter.
                var buttonFont = form._wallboardButton.Font;
                foreach (float scale in new[] { 1.25f, 1.5f })
                {
                    using var largerFont = new Font(buttonFont.FontFamily, buttonFont.Size * scale, buttonFont.Style);
                    form._wallboardButton.Font = largerFont;
                    form._wallboardButton.Parent!.PerformLayout();
                    Check(form._wallboardButton.Width >= form._wallboardButton.PreferredSize.Width,
                        "Wallboard button is clipped with larger UI text.");
                    form._wallboardButton.Font = buttonFont;
                }

                // Themed scroll indicator: present only when rows overflow, follows scrolling, never overlaps the list.
                using (var scrollHost = new Form { ClientSize = new Size(240, 320), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ShowInTaskbar = false })
                {
                    var scrollWell = new ScrollWell { Dock = DockStyle.Fill, Padding = new Padding(4, 6, 14, 6) };
                    var scrollList = new WellListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, ItemHeight = 40, DrawMode = DrawMode.OwnerDrawFixed };
                    scrollWell.Controls.Add(scrollList);
                    scrollHost.Controls.Add(scrollWell);
                    scrollHost.Show();
                    for (int i = 0; i < 3; i++) scrollList.Items.Add($"Site {i}");
                    Check(scrollWell.ScrollThumb is null, "Scroll indicator shown when every row fits.");
                    for (int i = 3; i < 50; i++) scrollList.Items.Add($"Site {i}");
                    Check(scrollList.ClientSize.Width == scrollList.Width, "Themed list kept a native scrollbar.");
                    var startThumb = scrollWell.ScrollThumb;
                    Check(startThumb is Rectangle && startThumb.Value.Left >= scrollList.Right, "Scroll indicator overlaps the list.");
                    scrollList.ScrollBy(10);
                    var movedThumb = scrollWell.ScrollThumb;
                    Check(scrollList.TopIndex == 10 && movedThumb!.Value.Top > startThumb!.Value.Top, "Scroll indicator did not follow scrolling.");
                    scrollList.ScrollBy(1000);
                    Check(scrollList.TopIndex >= 50 - scrollList.VisibleRows - 1 && scrollWell.ScrollThumb!.Value.Bottom <= scrollWell.ScrollTrack!.Value.Bottom,
                        "Scrolling past the end escaped the track.");
                    scrollList.ScrollBy(-1000);
                    Check(scrollList.TopIndex == 0, "Scrolling back did not reach the first row.");
                    scrollHost.Close();
                }

                using (var scoped = new MainForm(false, true, false))
                {
                    scoped.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, Sites = new()
                    {
                        new SiteDefinition { Name = "Site A", Hosts = new() { "127.0.0.1" } },
                        new SiteDefinition { Name = "Site B", Hosts = new() { "127.0.0.2" } }
                    } });
                    scoped.WallboardSelectSite("Site A");
                    scoped.StartMonitoring();
                    await Task.Delay(150);
                    Check(scoped._hosts.Count == 1 && scoped._hosts.Values.Single().Site == "Site A", "Start ignored site scope.");
                    var oldToken = scoped._hostTokens.Values.Single().Token;
                    scoped._siteList.SelectedIndex = scoped._siteList.Items.Cast<SiteListItem>().ToList().FindIndex(x => x.SiteName == "Site B");
                    await Task.Delay(150);
                    Check(oldToken.IsCancellationRequested && scoped._hosts.Count == 1 && scoped._hosts.Values.Single().Site == "Site B", "Site change kept an old worker.");
                    Check(scoped.BuildWallboardSnapshot().Sites.All(x => x.Name == "Site B"), "Wallboard ignored scope.");
                    scoped.WallboardSelectSite(null);
                    Check(scoped._hosts.Count == 2, "All sites did not restore both targets.");
                    scoped.StopMonitoring();
                    Check(scoped._hostTokens.Count == 0, "Stop left workers active.");
                }

                using (var sorted = new MainForm(false, true, false))
                {
                    string longName = "Northside regional distribution warehouse loading dock " + new string('x', 60);
                    sorted.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, Sites = new()
                    {
                        new SiteDefinition { Name = "Zulu", Hosts = new() { "192.0.2.10" } },
                        new SiteDefinition { Name = "alpha", Hosts = new() { "192.0.2.11" } },
                        new SiteDefinition { Name = longName, Hosts = new() { "192.0.2.12" } },
                        new SiteDefinition { Name = "Mike", Hosts = new() { "192.0.2.13" } }
                    } });
                    var mapOrder = sorted.BuildWallboardSnapshot().Sites.Select(x => x.Name).ToArray();
                    Check(mapOrder.SequenceEqual(new[] { "alpha", "Mike", longName, "Zulu" }, StringComparer.Ordinal), "Map sites are not alphabetical.");
                    var listOrder = sorted._siteList.Items.Cast<SiteListItem>().Select(x => x.SiteName).ToArray();
                    Check(listOrder.SequenceEqual(mapOrder, StringComparer.Ordinal), "Map and site list order differ.");
                    int longIndex = sorted._siteList.Items.Cast<SiteListItem>().ToList().FindIndex(x => x.SiteName == longName);
                    Check(sorted.IsSiteNameTruncated(longIndex), "A truncated site name has no full-name tooltip.");
                    if (sorted._siteList.ClientSize.Width > 150)
                        Check(!sorted.IsSiteNameTruncated(longIndex - 1), "A short site name was treated as truncated.");

                    // All sites is pinned outside the scrolling list and tracks the scope both ways.
                    Check(sorted._siteList.Items.Cast<SiteListItem>().All(x => x.SiteName is not null), "All sites returned as a list row.");
                    Check(sorted._allSitesButton.Parent is not null && sorted._siteWell?.Contains(sorted._allSitesButton) == false,
                        "All sites is not pinned outside the site list.");
                    Check(sorted._allSitesButton.Text == "All sites · 4 hosts", "All sites does not show the host count.");
                    sorted.SelectSiteScope(null);
                    Check(sorted._selectedSiteName is null && sorted._allSitesButton.Selected && sorted._siteList.SelectedIndex == -1,
                        "All sites scope is not shown as selected.");
                    sorted._siteList.SelectedIndex = sorted._siteList.Items.Cast<SiteListItem>().ToList().FindIndex(x => x.SiteName == "Mike");
                    Check(sorted._selectedSiteName == "Mike" && !sorted._allSitesButton.Selected, "Choosing a site left All sites selected.");
                    sorted.SelectSiteScope(null);
                    Check(sorted._selectedSiteName is null && sorted._allSitesButton.Selected, "All sites did not restore the full scope.");
                    Check(sorted._sectionDivider?.Parent is not null, "Site list and actions have no divider.");
                    Check(sorted._headerActionsPanel?.Controls.Count == 4, "Sidebar actions were not reduced to four.");
                    Check(sorted._addSiteButton.Text == "+" && sorted._addSiteButton.AccessibleName == "Add site", "Sites header lost its add action.");
                    Check(!sorted._siteHeaderLabel.UseMnemonic, "Site heading would hide & in names.");

                    // Managed updates: Settings learns it, and a check reports it instead of contacting GitHub.
                    sorted.ManageUpdatesForTest(true);
                    Check(sorted.GetAppSettingsSnapshot().UpdatesManaged, "Settings is not told updates are managed.");
                    await sorted.CheckForUpdatesAsync(userInitiated: true);
                    Check(sorted._updateStatusText == UpdatePolicy.ManagedText, $"Managed update check reported '{sorted._updateStatusText}'.");
                    sorted.ManageUpdatesForTest(false);

                    // Priority host: marked from the menu path, shown on map and list, alarms on offline and recovery, acknowledged from the notification.
                    var mike = sorted.HostSnapshot().First(h => h.Site == "Mike");
                    sorted.SetHostPriority(mike.Options.Id, true);
                    Check(sorted.HostSnapshot().First(h => h.Site == "Mike").Options.Priority, "Mark as priority did not save.");
                    Check(sorted.BuildWallboardSnapshot().Sites.SelectMany(x => x.Hosts).Single(h => h.Site == "Mike").Priority, "Map does not know the host is priority.");
                    var watched = new HostMonitor("Mike", mike.Address) { Options = sorted.HostSnapshot().First(h => h.Site == "Mike").Options.Copy() };
                    for (int i = 0; i < 3; i++) sorted.ProcessResult(watched, false, null);
                    Check(sorted.ActivePriorityAlerts.Count == 1 && sorted.ActivePriorityAlerts[0].Offline, "Priority host going offline did not alarm.");
                    Check(sorted._priorityBanner!.MessageText.Contains("went offline"), $"Banner reads '{sorted._priorityBanner.MessageText}'.");
                    for (int i = 0; i < 2; i++) sorted.ProcessResult(watched, true, 1);
                    Check(sorted.ActivePriorityAlerts.Count == 1 && !sorted.ActivePriorityAlerts[0].Offline, "Priority host recovering did not alarm.");
                    sorted.HandleNotificationActivation(new Dictionary<string, string> { ["action"] = "acknowledge", ["alert"] = sorted.ActivePriorityAlerts[0].Id });
                    Check(sorted.ActivePriorityAlerts.Count == 0, "Acknowledge from the notification did not stop the alarm.");
                    var ordinary = new HostMonitor("Zulu", "192.0.2.10");
                    for (int i = 0; i < 3; i++) sorted.ProcessResult(ordinary, false, null);
                    Check(sorted.ActivePriorityAlerts.Count == 0, "An ordinary host started the priority alarm.");
                    for (int i = 0; i < 3; i++) sorted.ProcessResult(watched, false, null);
                    sorted.SetHostPriority(mike.Options.Id, false);
                    Check(sorted.ActivePriorityAlerts.Count == 0, "Removing priority left the host ringing.");

                    // Quick Add opens on the site in view, accepts typed and pasted names, and creates new sites.
                    var siteNames = sorted._sites.Select(x => x.Name).ToList();
                    using (var onMike = new QuickAddForm(siteNames, "Mike", sorted.HostSnapshot, sorted.ImportManagedHosts, false, name => SiteNameError(name, sorted._sites, null)))
                        Check(onMike.SiteTextForTest == "Mike", "Quick Add ignored the site in view.");
                    using (var onAll = new QuickAddForm(siteNames, null, sorted.HostSnapshot, sorted.ImportManagedHosts, false, name => SiteNameError(name, sorted._sites, null)))
                    {
                        Check(onAll.SiteTextForTest == "" && onAll.SiteStatusForTest == "Choose or type a site", "Quick Add guessed a site for All sites.");
                        onAll.SiteTextForTest = "mike";
                        Check(onAll.SiteStatusForTest == "", "An existing site in different case was treated as new.");
                        onAll.SiteTextForTest = "All Sites";
                        Check(onAll.SiteStatusForTest.Contains("reserved"), "Reserved site name was accepted.");
                        onAll.SiteTextForTest = "Brand\u00A0New Site\u200B";
                        Check(onAll.SiteStatusForTest == "Creates site \u201cBrand New Site\u201d", $"New site status was '{onAll.SiteStatusForTest}'.");
                        onAll.PreviewForTest("192.0.2.200");
                        onAll.ImportForTest();
                    }
                    Check(sorted.FindSite("Brand New Site")?.Hosts.Contains("192.0.2.200") == true, "Quick Add did not create the typed site with its host.");
                    Check(sorted._siteList.Items.Cast<SiteListItem>().Any(x => x.SiteName == "Brand New Site"), "New site missing from the site list.");
                }
                form.SetWorkspaceView(true);
                WindowsBranding.RunShortcutSmokeTest(output);
                form.ImportManagedHosts(new() { new(form._sites[0].Name, "192.0.2.254", "Menu test", "", new HostOptions(), "Unknown") }, false);
                Check(form._grid.Rows.Count > 0, "Saved hosts are absent while idle.");
                Check(form._grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible) <= 5,
                    "Default results expose too many columns.");
                form._hostSearch.Text = "does-not-match";
                Check(form._grid.Rows.Count == 0 && form.HostSnapshot().Count == 1, "Search changed the saved targets.");
                form._hostSearch.Clear();
                Check(form._grid.Rows.Count == 1, "Clearing search did not restore the host.");
                await CheckHostMenu(form._grid);

                // Keep a non-first host, its selected column, and both scroll positions through repeated refreshes.
                for (int i = 1; i <= 40; i++)
                {
                    var host = new HostMonitor("Test Site", "192.0.2." + i);
                    form._hosts[BuildHostKey(host.Site, host.Address)] = host;
                }
                form.RefreshGrid();
                var row = form._grid.Rows[25];
                form._grid.CurrentCell = row.Cells["LabelColumn"];
                row.Selected = true;
                form._grid.FirstDisplayedScrollingRowIndex = 20;
                var selected = form.GetSelectedHostIdentity();
                form.SetCategoryValue(selected!.Value.Site, selected.Value.Host, "AP");
                form.RefreshGrid();
                Check(form._grid.CurrentRow?.Cells["CategoryColumn"].Value?.ToString() == "AP",
                    "Category did not render for the selected host.");
                Check(form.GetSelectedHostIdentity() == selected, "Category edit changed the selected host.");
                int selectionChanges = 0;
                EventHandler countSelectionChanges = (_, _) => selectionChanges++;
                form._grid.SelectionChanged += countSelectionChanges;
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(100);
                    form.RefreshGrid();
                    Check(ReferenceEquals(form._grid.Rows[25], row), "Refresh rebuilt unchanged host rows.");
                    Check(selectionChanges == 0, "Refresh cleared and restored selection.");
                    Check(form.GetSelectedHostIdentity() == selected, "Refresh lost the selected host.");
                    Check(form._grid.CurrentCell?.OwningColumn.Name == "LabelColumn", "Refresh lost the selected column.");
                    Check(form._grid.FirstDisplayedScrollingRowIndex == 20, "Refresh lost the scroll position.");
                }
                form._grid.SelectionChanged -= countSelectionChanges;
                Capture(form, "hosts-selected");
                form._hosts.Clear();
                form.RefreshGrid();
                Check(form.GetSelectedHostIdentity() is null, "Removed host left a stale selection.");

                Check(form._rightLayout!.RowStyles[1].Height == 0, "Duplicate inventory space returned.");
                Capture(form, "single-host-table");
                // Selecting hosts must not insert a toolbar or shift the table.
                form.SetWorkspaceView(true);
                await Task.Delay(100);
                var unselectedGridBounds = form._grid.RectangleToScreen(form._grid.ClientRectangle);
                form.SelectHostsForTest(form.HostSnapshot().First().Address);
                await Task.Delay(50);
                Check(form._grid.SelectedRows.Count == 1, "Host selection was lost.");
                Check(form._grid.RectangleToScreen(form._grid.ClientRectangle) == unselectedGridBounds,
                    "Selecting a host shifted the table.");
                Check(form._listHeader!.Controls.Count == 1 && form._filterBar!.Visible,
                    "Selection added a row above the table or hid the filters.");
                Capture(form, "list-selection");
                form.SelectHostsForTest();

                // Filters, multi-select actions that save at once, and Undo.
                form._hostSearch.Text = "";
                string editSite = form._selectedSiteName ?? form._sites[0].Name;
                form.ImportManagedHosts(new() {
                    new(editSite, "192.0.2.201", "Edit A", "", new HostOptions(), "Unknown"),
                    new(editSite, "192.0.2.202", "Edit B", "", new HostOptions(), "Unknown") }, false);
                form.SetHostFilter(HostFilter.All);
                form.SelectHostsForTest("192.0.2.201", "192.0.2.202");
                var selectedRows = form._grid.SelectedRows.Cast<DataGridViewRow>().ToArray();
                form.GridCellMouseDown(form._grid, new DataGridViewCellMouseEventArgs(
                    form._grid.Columns["HostColumn"].Index, selectedRows[0].Index, 4, 4,
                    new MouseEventArgs(MouseButtons.Right, 1, 4, 4, 0)));
                Check(form._grid.SelectedRows.Count == 2, "Right-click lost the multi-selection.");
                var bulkCommands = form._hostQuickMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                Check(form._hostQuickMenu.Visible && bulkCommands.Contains("Move to site…") &&
                    !bulkCommands.Contains("Copy address"), "Host actions did not offer bulk operations for two hosts.");
                form._hostQuickMenu.Close();
                var otherRow = form._grid.Rows.Cast<DataGridViewRow>().First(r => !r.Selected);
                form.GridCellMouseDown(form._grid, new DataGridViewCellMouseEventArgs(
                    form._grid.Columns["HostColumn"].Index, otherRow.Index, 4, 4,
                    new MouseEventArgs(MouseButtons.Right, 1, 4, 4, 0)));
                Check(form._grid.SelectedRows.Count == 1 && otherRow.Selected,
                    "Right-click outside the selection did not select only the clicked host.");
                form._hostQuickMenu.Close();
                form.SelectHostsForTest("192.0.2.201", "192.0.2.202");
                Check(form.PriorityActionText == "Mark as priority", $"Action read '{form.PriorityActionText}'.");
                form.TogglePriorityForTest();
                bool BothPriority() => form.HostSnapshot().Where(h => h.Address is "192.0.2.201" or "192.0.2.202").All(h => h.Options.Priority);
                Check(BothPriority() && form.UndoMessageText == "Marked 2 hosts as priority", $"Bulk priority failed; undo reads '{form.UndoMessageText}'.");
                form.SetHostFilter(HostFilter.Priority);
                Check(form.VisibleHostRows == 2 && form.FilterChipText(HostFilter.Priority) == "Priority 2",
                    $"Priority filter shows {form.VisibleHostRows} rows; chip '{form.FilterChipText(HostFilter.Priority)}'.");
                form.SelectHostsForTest("192.0.2.201", "192.0.2.202");
                Check(form.PriorityActionText == "Remove priority", "Selected priority hosts do not offer Remove priority.");
                form.TogglePriorityForTest();
                Check(!form.HostSnapshot().Any(h => h.Options.Priority) && form.VisibleHostRows == 0, "Remove priority did not clear the filter.");
                form.UndoLastHostEdit();
                Check(BothPriority() && form.VisibleHostRows == 2 && form.UndoMessageText == "", "Undo did not restore priority.");
                form.SetHostFilter(HostFilter.All);
                form.SelectHostsForTest("192.0.2.202");
                form.ToggleEnabledForTest();
                form.SetHostFilter(HostFilter.Disabled);
                Check(form.VisibleHostRows == 1 && form.FilterChipText(HostFilter.Disabled) == "Disabled 1", "Disabled filter is wrong.");
                Capture(form, "list-filter-disabled");
                form.SetHostFilter(HostFilter.All);
                form.DeleteManagedHosts(form.HostSnapshot().Where(h => h.Address is "192.0.2.201" or "192.0.2.202").ToList());
                using (var quickAdd = new QuickAddForm(form._sites.Select(s => s.Name), form._selectedSiteName,
                    form.HostSnapshot, form.ImportManagedHosts))
                {
                    quickAdd.PreviewForTest("192.0.2.1,Printer,Printers\n192.0.2.10-12\n192.0.2.999");
                    quickAdd.Show(form);
                    await Task.Delay(100);
                    Check(quickAdd.PreviewRowCount == 5, "Quick Add preview lost input rows.");
                    // #40: pasted labels land in the Label column and fill down; Use and Site are left alone.
                    var pasted = quickAdd.PasteLabelsForTest("Front desk\r\nCopier\tIgnored\n");
                    Check(pasted[0] == "Front desk" && pasted[1] == "Copier", $"Pasting labels gave '{string.Join("|", pasted)}'.");
                    Capture(quickAdd, "quick-add");
                    quickAdd.Close();
                }
                form.OpenSettings();
                await Task.Delay(100);
                Check(form._settingsForm?.Visible == true, "Settings did not open.");
                form._settingsForm!.RunLayoutChecks();
                DesignChecks.RunTests();
                var designProblems = DesignChecks.Run(form).Concat(DesignChecks.Run(form._settingsForm!)).ToList();
                Check(designProblems.Count == 0, "Design checks: " + string.Join(" | ", designProblems.Take(8)));
                Capture(form._settingsForm!, "settings");
                form._settingsForm!.Close();

                // Landing-page and README captures: an example network with replies, shown as monitoring.
                // Every host is registered before monitoring is marked active, so no ping workers start; states are set directly.
                using (var page = new MainForm(false, true, false))
                {
                    page._minimizeToTray = false;
                    string[] kinds = { "Laptop", "Desktop", "Tablet", "Mini PC", "Server" }, trees = { "Ash", "Birch", "Cedar", "Maple", "Pine" };
                    page.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, ShowCommandView = true,
                        Sites = Enumerable.Range(1, 5).Select(site => new SiteDefinition {
                            Name = "Branch " + site,
                            Hosts = Enumerable.Range(1, 5).Select(host => $"172.20.{site}.{20 + host}").ToList(),
                            Labels = Enumerable.Range(1, 5).ToDictionary(host => $"172.20.{site}.{20 + host}", host => kinds[host - 1] + " " + trees[(site + host) % 5])
                        }).ToList() });
                    page.MaximumSize = new Size(1920, 1080);
                    page.Show(form);
                    var replyTime = DateTime.Now;
                    foreach (var site in page._sites)
                        foreach (var address in site.Hosts)
                        {
                            int ms = 2 + (address.GetHashCode() & 7);
                            page._hosts[BuildHostKey(site.Name, address)] = new HostMonitor(site.Name, address, page.GetNickname(site.Name, address))
                                { State = HostState.Online, LastRoundTripMs = ms, LastReply = replyTime, ConsecutiveSuccesses = 5 };
                            page.AppendCommandLog(site.Name, address, true, $"Reply from {address}: time={ms}ms TTL=128");
                        }
                    page._cts = new CancellationTokenSource();
                    page.UpdateActionState();
                    Check(page._hostTokens.IsEmpty, "Page captures started real ping workers.");
                    foreach (var appearance in new[] { AppearanceMode.Dark, AppearanceMode.Light })
                    {
                        AppTheme.SetMode(appearance);
                        page.ClientSize = new Size(1040, 700);
                        page.ApplyResponsiveLayout();
                        page.SetWorkspaceView(false);
                        page.RefreshGrid();
                        await Task.Delay(150);
                        Capture(page, "page-map-" + appearance);
                        page.SetWorkspaceView(true);
                        page.RefreshGrid();
                        await Task.Delay(150);
                        Capture(page, "page-list-" + appearance);
                    }
                    Check(page._hostTokens.IsEmpty, "Page captures started real ping workers.");
                    page.Close();
                }

                using (var review = new MainForm(false, true, false))
                {
                    review._minimizeToTray = false;
                    review.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, ShowCommandView = true,
                        Sites = Enumerable.Range(1, 5).Select(site => new SiteDefinition {
                            Name = "Branch " + site,
                            Hosts = Enumerable.Range(1, 5).Select(host => $"192.0.2.{site * 10 + host}").ToList(),
                            Labels = new() { [$"192.0.2.{site * 10 + 2}"] = "Printer", [$"192.0.2.{site * 10 + 3}"] = "Cloudkey" }
                        }).ToList() });
                    review.MaximumSize = new Size(1920, 1080);
                    review.Show(form);
                    review.SetWorkspaceView(true);
                    foreach (var appearance in new[] { AppearanceMode.Dark, AppearanceMode.Light })
                    {
                        AppTheme.SetMode(appearance);
                        review.ClientSize = new Size(1060, 680);
                        review.ApplyResponsiveLayout();
                        await Task.Delay(150);
                        Check(review._grid.Rows.Count == 25, "Populated workspace lost hosts.");
                        Check(review._grid.Columns["HostColumn"].DisplayIndex == 0, "Address must be the first column.");
                        Check(review._grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width) < review._grid.ClientSize.Width,
                            "Default host columns overflow beside ping output.");
                        Check(review._mainSplit.Panel2Collapsed && review._dashboardSplit.Panel2.Visible,
                            "List activity must use the shared bottom pane.");
                        review._dashboardSplit.SplitterDistance -= 20;
                        var activityBounds = review._dashboardSplit.Panel2.RectangleToScreen(review._dashboardSplit.Panel2.ClientRectangle);
                        review.SetWorkspaceView(false);
                        Check(review._dashboardSplit.Panel2.RectangleToScreen(review._dashboardSplit.Panel2.ClientRectangle) == activityBounds,
                            "Ping activity moved when switching from List to Map.");
                        review.SetWorkspaceView(true);
                        Check(review._dashboardSplit.Panel2.RectangleToScreen(review._dashboardSplit.Panel2.ClientRectangle) == activityBounds,
                            "Ping activity moved when switching back to List.");
                        Check(review._addHostsMenu.Items.Count == 2, "Saved and temporary host adding are not together.");
                        Check(!review._workspaceSplit.Panel1.Controls.Cast<Control>().SelectMany(Descendants).OfType<Label>().Any(label => label.Text == "Sites"),
                            "Redundant Sites heading returned.");
                        Capture(review, "workspace-list-" + appearance);
                        review._hostSearch.Text = "no-match";
                        review.SetHostFilter(HostFilter.Disabled);
                        review.ShowHostInList("Branch 3", "192.0.2.31");
                        Check(review._listView && review._selectedSiteName == "Branch 3" && review._grid.SelectedRows.Count == 1 &&
                            review._grid.SelectedRows[0].Cells["HostColumn"].Value?.ToString() == "192.0.2.31",
                            "Map-to-List navigation did not reveal and select its host.");
                        Check(!Descendants(review).OfType<Button>().Any(b => b.Text == "Host actions"), "Host actions toolbar button returned.");
                        review.ShowSelectionMenu(review._grid, new Point(20, 20));
                        Check(review._hostQuickMenu.Visible, "Selected host context menu did not open.");
                        review._hostQuickMenu.Close();
                        await Task.Delay(100);
                        Capture(review, "host-actions-" + appearance);
                        review.SelectSiteScope(null);
                        review._grid.ClearSelection();
                        review.SetWorkspaceView(false);
                        await Task.Delay(100);
                        Check(!review._mainSplit.Visible && review._monitorCanvas.Visible,
                            "Map and list are displayed together.");
                        Check(review._statusStrip.Parent == null, "Redundant bottom status bar returned.");
                        Capture(review, "workspace-populated-" + appearance);
                        Check(review._monitorCanvas.AllHostNodesFit && review._monitorCanvas.VisibleHostCount == 25,
                            "The selected topology does not fit every node.");
                        review.SetUpdateStatus("Up to date", true);
                        Check(review._checkUpdateButton.Text == "Check for updates", "Update action label changed into status.");
                        Check(review._updateToast?.Visible == true && review._updateToast.Right <= review.ClientSize.Width,
                            "Internal update status is missing or outside the application.");
                        await Task.Delay(200);
                        var updateButtons = Descendants(review._updateToast!).OfType<Button>().ToList();
                        Check(updateButtons.Any(b => b.Text == "Dismiss") &&
                            updateButtons.All(b => !new[] { "ok", "okay" }.Contains(b.Text.Trim(), StringComparer.OrdinalIgnoreCase)),
                            "Update status returned a generic OK action.");
                        review._updateToast!.Update();
                        var clip = review._updateToast.Region;
                        Check(clip != null && !clip.IsVisible(0, 0) &&
                            !clip.IsVisible(review._updateToast.Width - 1, review._updateToast.Height - 1) &&
                            clip.IsVisible(review._updateToast.Width / 2, review._updateToast.Height / 2),
                            "Popup did not clip its corners on first display.");
                        Check(review._updateToast.BackColor != AppTheme.Palette.Surface,
                            "Update popup uses the map card surface.");
                        Check(AppTheme.ContrastRatio(AppTheme.Palette.Text, review._updateToast.BackColor) >= 4.5,
                            "Update popup text contrast is insufficient.");
                        Check(review.Controls.GetChildIndex(review._updateToast) == 0,
                            "Update status is behind the main workspace.");
                        using (var popup = new Bitmap(review._updateToast.Width, review._updateToast.Height))
                        {
                            review._updateToast.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
                            popup.Save(Path.Combine(output, "update-popup-" + appearance + ".png"));
                        }
                        Capture(review, "update-status-" + appearance);
                        review._updateToast!.Visible = false;
                        review.SetWorkspaceView(true);
                    }
                    review.Close();
                }

                form.OpenWallboard();
                await Task.Delay(200);
                Check(form._wallboardForm?.Visible == true, "Wallboard did not open.");
                var canvas = (WallboardCanvas)form._wallboardForm!.Controls.Find("WallboardCanvas", true).Single();
                foreach (int width in new[] { 900, 1320 })
                {
                    form._wallboardForm.ClientSize = new Size(width, 900);
                    await Task.Delay(100);
                    form._wallboardForm.PerformLayout();
                    if (width == 900) Capture(form._wallboardForm, "wallboard-narrow");
                    Check(form._wallboardForm.Controls.Find("WallboardToolbar", true).Length == 0,
                        "Wallboard top controls returned.");
                    Check(canvas.Top == 0 && canvas.Height == form._wallboardForm.ClientSize.Height,
                        $"Wallboard canvas does not fill the available window: canvas={canvas.Bounds}, parent={canvas.Parent?.ClientRectangle}, window={form._wallboardForm.ClientRectangle}.");
                }
                await CheckHostMenu(canvas);
                canvas.Focus();
                canvas.HandleHostKey(Keys.Home);
                canvas.HandleHostKey(Keys.Shift | Keys.F10);
                Check(form._hostQuickMenu.Visible, "Keyboard host action did not open the menu.");
                Check(form._hostQuickMenu.Items[0].Text.Contains(canvas.SelectedHost!.Address),
                    "Keyboard host menu targeted a different host.");
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                canvas.AccessibilityObject.GetChild(0)!.DoDefaultAction();
                Check(form._hostQuickMenu.Visible, "Accessible host action did not open the menu.");
                form._hostQuickMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                Capture(form._wallboardForm!, "wallboard");
                canvas.Snapshot = new WallboardSnapshot(true, DateTime.Now,
                    Enumerable.Range(1, 4).Select(site => new WallboardSiteSnapshot("Branch " + site,
                        Enumerable.Range(1, site == 4 ? 1 : 8).Select(host => new WallboardHostSnapshot(
                            "Branch " + site, $"192.0.2.{site * 20 + host}", host == 4 ? "Cloudkey controller" : "Device " + host,
                            host == 2 ? HostState.Offline : HostState.Online, 12, host == 2 ? DateTime.Now.AddMinutes(-4) : null)).ToArray())).ToArray(),
                    Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "Last 24 hours", true);
                using (var dense = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(dense, canvas.ClientRectangle);
                    dense.Save(Path.Combine(output, "wallboard-dense.png"));
                    Check(!canvas.CaptionsOverlap(), "Dense Wallboard captions overlap.");
                }

                // #42/#43: capture uneven groups, full names and addresses in both themes.
                foreach (var appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                {
                    AppTheme.SetMode(appearance);
                    using var labels = new WallboardCanvas { Size = new Size(1000, 560), Embedded = true, ShowCli = false,
                        Snapshot = new WallboardSnapshot(true, DateTime.Now,
                            Enumerable.Range(1, 6).Select(site => new WallboardSiteSnapshot($"Regional office {site} - service desk",
                                Enumerable.Range(1, site == 1 ? 20 : 3).Select(host => new WallboardHostSnapshot(
                                    $"Regional office {site} - service desk", $"172.20.{site}.{host}", $"Desktop workstation {host}",
                                    HostState.Online, 1, null)).ToArray())).ToArray(),
                            Array.Empty<WallboardEventSnapshot>(), Array.Empty<WallboardCommandSnapshot>(), "24 hours", true) };
                    using var bitmap = new Bitmap(labels.Width, labels.Height);
                    labels.DrawToBitmap(bitmap, labels.ClientRectangle);
                    bitmap.Save(Path.Combine(output, "topology-full-labels-" + appearance + ".png"));
                    Check(labels.DrawnAddresses.Count == 35 && labels.DrawnSiteHeadings.Count == 6,
                        "#42/#43: full topology captions disappeared.");
                }

                var configBeforeCloseTest = form.BuildConfig(captureEditor: false);
                form.ApplyConfig(new WatchdogConfig { AutoCheckUpdates = false, Sites = new()
                {
                    new SiteDefinition { Name = "Close guard", Hosts = new() { "127.0.0.1" } }
                } });
                form.StartMonitoring();
                Check(form._cts is not null, "Close-guard test requires active monitoring.");
                foreach (Form owner in new Form[] { form, form._wallboardForm! })
                {
                    form.OpenSettings(owner);
                    await Task.Delay(50);
                    var settings = form._settingsForm!;
                    var monitoringBeforeClose = form._cts;
                    FormClosingEventHandler veto = (_, e) => e.Cancel = true;
                    settings.FormClosing += veto;
                    form.Close();
                    Check(!form.IsDisposed && !form._closingApplication && !settings.IsDisposed,
                        "Canceling Settings close did not preserve the main session.");
                    Check(ReferenceEquals(form._cts, monitoringBeforeClose) &&
                        form._wallboardForm is { Visible: true, IsDisposed: false },
                        "Canceled exit stopped monitoring or closed Wallboard.");
                    settings.FormClosing -= veto;
                    settings.Close();
                }
                form.StopMonitoring();
                form.ApplyConfig(configBeforeCloseTest);

                form._wallboardForm!.Close();
                await Task.Delay(100);
                Check(form.Visible, "Closing Wallboard did not restore the main window.");
                var appearanceBefore = AppTheme.Mode;
                foreach (var appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                {
                    AppTheme.SetMode(appearance);
                    form.OpenSettings();
                    await Task.Delay(100);
                    Check(form.BackColor == AppTheme.Palette.Canvas, "Main appearance did not update.");
                    Check(form._settingsForm!.BackColor == AppTheme.Palette.Canvas, "Open Settings appearance did not update.");
                    Capture(form, "appearance-main-" + appearance);
                    Capture(form._settingsForm!, "appearance-settings-" + appearance);
                    await form._settingsForm!.ReviewNavigation(output);
                    await DialogReviewTests.Run(form._settingsForm, output);
                    await UpdateFailureDialog.Review(form._settingsForm, output);
                    using (var restart = new UpdateRestartDialog("99.0.0", monitoring: true))
                    {
                        restart.Show(form._settingsForm);
                        restart.PerformLayout();
                        restart.CheckLayout();
                        Capture(restart, "update-restart-" + appearance);
                        restart.Close();
                    }
                    await SettingsForm.CaptureSaveFailureReview(form.GetAppSettingsSnapshot(), form._settingsForm!,
                        Path.Combine(output, "settings-save-error-" + appearance + ".png"));
                    await form._settingsForm!.CaptureAboutReview(output, appearance.ToString());
                    await form._settingsForm!.CaptureDataReview(output, appearance.ToString());
                    form._settingsForm!.Close();

                    IReadOnlyList<StateEventRecord> events = Enumerable.Range(0, 60).Select(i =>
                        new StateEventRecord(DateTime.Now.AddMinutes(-i), "Office & Warehouse", $"192.0.2.{i + 1}",
                            "Controller with a long name", i % 3 == 0 ? "DOWN" : i % 3 == 1 ? "RECOVERED" : "SUSPECT",
                            "Availability changed")).ToArray();
                    using (var history = new EventHistoryForm(() => events,
                        () => new EventHistoryPreferences(24, false), (_, _) => { }))
                    {
                        history.Show(form);
                        await Task.Delay(50);
                        foreach (int width in new[] { 820, 1080 })
                        {
                            history.Width = width;
                            await Task.Delay(50);
                            Capture(history, "dialog-history-" + width + "-" + appearance);
                            history.CheckLayout();
                        }
                        events = Array.Empty<StateEventRecord>();
                        history.RefreshNow();
                        history.CheckLayout();
                        Capture(history, "dialog-history-empty-" + appearance);
                        DismissWithEscape(history);
                    }
                    form.OpenOrganization();
                    await Task.Delay(50);
                    foreach (int width in new[] { 620, 900 })
                    {
                        form._organizationForm!.Width = width;
                        await Task.Delay(50);
                        Capture(form._organizationForm, "dialog-organization-" + width + "-" + appearance);
                        form._organizationForm.CheckLayout();
                    }
                    DismissWithEscape(form._organizationForm!);
                    foreach (bool temporary in new[] { false, true })
                    {
                        bool imported = false;
                        using var add = new QuickAddForm(new[] { "Office & Warehouse" }, "Office & Warehouse",
                            () => new List<ManagedHost>
                            {
                                new("Office & Warehouse", "192.0.2.10", "Existing router", "", new HostOptions(), "Unknown")
                            }, (_, _) => imported = true, temporary);
                        add.Show(form);
                        await Task.Delay(50);
                        add.CheckKeyboardNavigation();
                        add.Width = 850;
                        Capture(add, "dialog-quick-" + temporary + "-empty-" + appearance);
                        add.CheckLayout();
                        add.PreviewForTest("192.0.2.10\tExisting router\n192.0.2.20\tPrinter & scanner\n192.0.2.20\tDuplicate\n192.0.2.999\tInvalid");
                        foreach (int width in new[] { 850, 1100 })
                        {
                            add.Width = width;
                            await Task.Delay(50);
                            Capture(add, "dialog-quick-" + temporary + "-" + width + "-" + appearance);
                            add.CheckLayout();
                        }
                        Check(add.PreviewRowCount == 4 && add.PastedLineCountForTest == 4 && !imported,
                            "Preview lost pasted lines or changed the inventory before confirmation.");
                        DismissWithEscape(add);
                    }
                    using (var label = new NicknameDialog("2001:db8::1234", "Printer & scanner"))
                    {
                        label.Show(form);
                        await Task.Delay(50);
                        Capture(label, "dialog-label-" + appearance);
                        CompactDialog.CheckBounds(label);
                        Check(label.Nickname == "Printer & scanner", "Label review changed the current value.");
                        DismissWithEscape(label);
                    }
                    using (var hostEdit = new HostEditDialog("192.0.2.40", "Printer & scanner", _ => null))
                    {
                        hostEdit.Show(form);
                        await Task.Delay(50);
                        Capture(hostEdit, "dialog-edit-host-" + appearance);
                        CompactDialog.CheckBounds(hostEdit);
                        var editDesign = DesignChecks.Run(hostEdit);
                        Check(editDesign.Count == 0, "Design checks: " + string.Join(" | ", editDesign));
                        Check(hostEdit.Address == "192.0.2.40" && hostEdit.Nickname == "Printer & scanner", "Host edit review changed the current values.");
                        DismissWithEscape(hostEdit);
                    }
                    using (var name = new SiteNameDialog("Rename site", "Rename", "Office & Warehouse"))
                    {
                        name.Show(form);
                        await Task.Delay(50);
                        Capture(name, "dialog-site-name-" + appearance);
                        CompactDialog.CheckBounds(name);
                        Check(name.SiteName == "Office & Warehouse", "Site review changed the current value.");
                        DismissWithEscape(name);
                    }
                    using (var folder = new FolderPickerDialog("Move site",
                        new[] { "Region/Office & Warehouse", "Region/Remote offices" }, "Region/Office & Warehouse"))
                    {
                        folder.Show(form);
                        await Task.Delay(50);
                        Capture(folder, "dialog-folder-" + appearance);
                        CompactDialog.CheckBounds(folder);
                        Check(folder.SelectedFolder == "Region/Office & Warehouse", "Folder picker lost its current selection.");
                        DismissWithEscape(folder);
                    }

                    using (var category = new CategoryDialog("2001:db8:1234::1234", "Infrastructure"))
                    {
                        category.Show(form);
                        await Task.Delay(50);
                        Capture(category, "dialog-category-" + appearance);
                        category.CheckLayout();
                        Check(category.CancelButton is Button { DialogResult: DialogResult.Cancel } &&
                            category.AcceptButton is Button { DialogResult: DialogResult.OK },
                            "Category lost its native confirm/cancel actions.");
                        Capture(category, "dialog-category-" + appearance);
                        category.Close();
                    }
                }
                AppTheme.SetMode(appearanceBefore);
                File.WriteAllText(Path.Combine(output, "passed.txt"), "UI smoke test passed.");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "failed.txt"), ex.ToString());
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
            finally
            {
                form.Close();
            }
        };
        Application.Run(form);
    }
}
