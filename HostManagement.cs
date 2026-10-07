namespace PingWatchdog;

public sealed partial class MainForm
{
    private float _inventoryHeight = 210;
    private Control BuildResizableInventory(Control inventory)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Tag = "window" };
        var grip = new Label { Dock = DockStyle.Bottom, Height = 18, Cursor = Cursors.HSplit,
            Text = "⋯  Drag to resize hosts  ⋯", TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(96, 210, 220), BackColor = Color.FromArgb(25, 34, 45),
            AccessibleName = "Resize host inventory" };
        int startY = 0; float startHeight = 0;
        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || _rightLayout == null) return;
            startY = Cursor.Position.Y; startHeight = _rightLayout.RowStyles[1].Height;
            grip.Capture = true;
        };
        grip.MouseMove += (_, _) =>
        {
            if (!grip.Capture || _rightLayout == null) return;
            _inventoryHeight = (startHeight + Cursor.Position.Y - startY) * 96f / DeviceDpi;
            ApplyInventoryHeight();
        };
        grip.MouseUp += (_, _) => grip.Capture = false;
        grip.DoubleClick += (_, _) => { _inventoryHeight = 210; ApplyInventoryHeight(); };
        panel.Controls.Add(inventory); panel.Controls.Add(grip);
        return panel;
    }
    private void ApplyInventoryHeight()
    {
        if (_rightLayout == null) return;
        float scale = DeviceDpi / 96f;
        float remaining = _rightLayout.ClientSize.Height - _rightLayout.RowStyles[0].Height -
            _rightLayout.RowStyles[2].Height - 150 * scale;
        float minimum = 130 * scale;
        _rightLayout.RowStyles[1].Height = Math.Clamp(_inventoryHeight * scale, minimum, Math.Max(minimum, remaining));
    }
    private DataGridView? _hostInventory;
    private HostManagerForm? _hostManager;
    private readonly ContextMenuStrip _hostQuickMenu = new();
    private static HostOptions GetHostOptions(SiteDefinition? site, string address)
    {
        if (site == null) return new HostOptions();
        site.HostDetails ??= new(StringComparer.OrdinalIgnoreCase);
        var existing = site.HostDetails.FirstOrDefault(p => p.Key.Equals(address, StringComparison.OrdinalIgnoreCase)).Value;
        if (existing != null) return existing;
        var options = new HostOptions(); site.HostDetails[address] = options; return options;
    }
    private static Dictionary<string, HostOptions> NormalizeHostDetails(Dictionary<string, HostOptions>? details, IEnumerable<string> hosts)
    {
        var result = new Dictionary<string, HostOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (string address in hosts)
        {
            var value = details?.FirstOrDefault(p => p.Key.Equals(address, StringComparison.OrdinalIgnoreCase)).Value?.Copy() ?? new HostOptions();
            if (string.IsNullOrWhiteSpace(value.Id)) value.Id = Guid.NewGuid().ToString("N");
            value.IntervalSeconds = Math.Clamp(value.IntervalSeconds, 0, 3600);
            value.TimeoutMs = Math.Clamp(value.TimeoutMs, 0, 60000);
            value.FailureThreshold = Math.Clamp(value.FailureThreshold, 0, 100);
            result[address] = value;
        }
        return result;
    }
    private FlowLayoutPanel BuildHostActions()
    {
        var actions = HostUi.Bar(); actions.WrapContents = false;
        actions.Controls.Add(HostUi.Button("Hosts", OpenHostManager));
        actions.Controls.Add(HostUi.Button("Quick Add", () => OpenQuickAdd(false)));
        actions.Controls.Add(HostUi.Button("Quick Monitor", () => OpenQuickAdd(true)));
        return actions;
    }
    private Control BuildHostInventory()
    {
        _hostInventory = HostUi.Grid(); _hostInventory.ReadOnly = true;
        foreach (var name in new[] { "Name", "Address", "Site", "Group", "Monitoring" }) _hostInventory.Columns.Add(name, name);
        _hostInventory.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            var row = _hostInventory.Rows[e.RowIndex];
            ShowHostQuickActions(_hostInventory, _hostInventory.PointToClient(Cursor.Position),
                row.Cells["Site"].Value?.ToString() ?? "", row.Cells["Address"].Value?.ToString() ?? "");
        };
        _hostInventory.CellDoubleClick += (_, _) => OpenHostManager();
        return _hostInventory;
    }
    private void ShowHostQuickActions(Control surface, Point location, string site, string address)
    {
        var host = HostSnapshot().FirstOrDefault(h =>
            h.Site.Equals(site, StringComparison.OrdinalIgnoreCase) &&
            h.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
        if (host == null) return;
        // WinForms still uses the drop-down after raising Closed. Keep one menu
        // alive for the owning form instead of disposing it inside that event.
        var menu = _hostQuickMenu;
        menu.Close();
        foreach (var item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Renderer = new ToolStripProfessionalRenderer(new HostMenuColors());
        menu.BackColor = Color.FromArgb(22, 27, 34);
        menu.ForeColor = Color.White;
        void Add(string title, Action action) => menu.Items.Add(title, null, (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(surface.FindForm(), ex.Message, "Host action failed"); }
        });
        menu.Items.Add(new ToolStripMenuItem(string.IsNullOrWhiteSpace(host.Label) ? address : host.Label + " • " + address) { Enabled = false });
        Add("Copy address", () => Clipboard.SetText(address));
        Add("Set label / nickname…", () =>
        {
            using var dialog = new NicknameDialog(address, host.Label);
            if (dialog.ShowDialog(surface.FindForm()) == DialogResult.OK)
                SaveManagedHosts(new() { host with { Label = dialog.Nickname } });
        });
        Add("Clear label", () => SaveManagedHosts(new() { host with { Label = "" } }));
        Add("Set category…", () =>
        {
            using var dialog = new CategoryDialog(address, host.Group);
            if (dialog.ShowDialog(surface.FindForm()) == DialogResult.OK)
                SaveManagedHosts(new() { host with { Group = dialog.Category } });
        });
        Add("Clear category", () => SaveManagedHosts(new() { host with { Group = "" } }));
        Add("Ping in command window", () =>
        {
            var start = new System.Diagnostics.ProcessStartInfo("ping.exe") { UseShellExecute = true };
            start.ArgumentList.Add("-t"); start.ArgumentList.Add(address);
            System.Diagnostics.Process.Start(start);
        });
        menu.Items.Add(new ToolStripSeparator());
        if (host.Options.IsSnoozed(DateTimeOffset.UtcNow))
            menu.Items.Add(new ToolStripMenuItem("Alerts snoozed until " +
                DisplayTime.Clock(host.Options.SnoozedUntilUtc!.Value.LocalDateTime)) { Enabled = false });
        Add("Snooze alerts for 1 hour", () => SetHostSnooze(host.Options.Id, DateTimeOffset.UtcNow.AddHours(1)));
        if (host.Options.IsSnoozed(DateTimeOffset.UtcNow))
            Add("Resume alerts now", () => SetHostSnooze(host.Options.Id, null));
        Add(host.Options.Enabled ? "Disable monitoring" : "Enable monitoring", () =>
        {
            var current = HostSnapshot().FirstOrDefault(h => h.Options.Id == host.Options.Id);
            if (current == null) return;
            var options = current.Options.Copy(); options.Enabled = !options.Enabled;
            SaveManagedHosts(new() { current with { Options = options } });
        });
        menu.Items.Add(new ToolStripSeparator());
        Add("Remove host…", () =>
        {
            if (MessageBox.Show(surface.FindForm(), $"Remove {address} from {site}?", "Remove host",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                DeleteManagedHosts(new() { host });
        });
        menu.Show(surface, location);
    }

    private void SetHostSnooze(string id, DateTimeOffset? until)
    {
        var current = HostSnapshot().FirstOrDefault(h => h.Options.Id == id);
        if (current == null) return;
        var options = current.Options.Copy(); options.SnoozedUntilUtc = until;
        SaveManagedHosts(new() { current with { Options = options } });
    }

    private List<ManagedHost> HostSnapshot() => _sites.SelectMany(site => site.Hosts.Select(address =>
        new ManagedHost(site.Name, address, GetNickname(site.Name, address), GetCategory(site.Name, address),
            GetHostOptions(site, address).Copy(), _hosts.TryGetValue(BuildHostKey(site.Name, address), out var host) ? host.State.ToString() : "Unknown"))).ToList();
    private void RefreshHostInventory()
    {
        if (_hostInventory == null || _hostInventory.IsDisposed) return;
        _hostInventory.Rows.Clear();
        foreach (var h in HostSnapshot().Where(h => _selectedSiteName == null || h.Site == _selectedSiteName))
            _hostInventory.Rows.Add(h.Label, h.Address, h.Site, h.Group, h.Options.IsSnoozed(DateTimeOffset.UtcNow) ? "Alerts snoozed" : h.Options.Enabled ? "Enabled" : "Disabled");
    }
    private void OpenHostManager()
    {
        if (_hostManager is { IsDisposed: false }) { _hostManager.RefreshNow(); _hostManager.Activate(); return; }
        _hostManager = new HostManagerForm(HostSnapshot, SaveManagedHosts, DeleteManagedHosts, OpenQuickAdd, SaveTemporaryHosts, () => _sites.Select(s => s.Name).ToList());
        _hostManager.FormClosed += (_, _) => _hostManager = null;
        _hostManager.Show(this);
    }
    private void OpenQuickAdd(bool temporary)
    {
        using var form = new QuickAddForm(_sites.Where(s => !s.Temporary).Select(s => s.Name), _selectedSiteName,
            HostSnapshot, ImportManagedHosts, temporary);
        form.ShowDialog(_hostManager is { IsDisposed: false } ? _hostManager : this);
    }
    private void ImportManagedHosts(List<ManagedHost> hosts, bool temporary)
    {
        PersistCurrentEditor();
        SiteDefinition? transient = null;
        if (temporary)
        {
            string name = "Quick Monitor"; int suffix = 1;
            while (FindSite(name) != null) name = $"Quick Monitor {++suffix}";
            transient = new SiteDefinition { Name = name, Temporary = true }; _sites.Add(transient);
        }
        foreach (var host in hosts)
        {
            var site = transient ?? FindSite(host.Site); if (site == null) continue;
            if (site.Hosts.Contains(host.Address, StringComparer.OrdinalIgnoreCase)) continue;
            site.Hosts.Add(host.Address); site.HostDetails[host.Address] = host.Options.Copy();
            SetNicknameValue(site.Name, host.Address, host.Label); SetCategoryValue(site.Name, host.Address, host.Group);
        }
        if (transient != null) _selectedSiteName = transient.Name;
        CommitHostChanges();
        if (temporary && _cts == null) StartMonitoring();
    }
    private void SaveManagedHosts(List<ManagedHost> hosts)
    {
        // Validate the complete operation first so bulk moves cannot partially apply.
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hosts)
        {
            if (!requested.Add(BuildHostKey(host.Site, host.Address)))
                throw new InvalidOperationException("The selection would create duplicate hosts in the destination site.");
            var original = HostSnapshot().FirstOrDefault(h => h.Options.Id == host.Options.Id);
            var site = FindSite(host.Site) ?? throw new InvalidOperationException("Select an existing destination site.");
            if (original != null && original.Site != host.Site && site.Hosts.Contains(host.Address, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{host.Address} already exists in {site.Name}.");
        }
        foreach (var host in hosts)
        {
            var original = HostSnapshot().FirstOrDefault(h => h.Options.Id == host.Options.Id); if (original == null) continue;
            var site = FindSite(host.Site)!;
            if (original.Site != host.Site)
            {
                RemoveManagedHost(original); site.Hosts.Add(host.Address);
            }
            site.HostDetails[host.Address] = host.Options.Copy();

            if (!host.Options.Enabled)
            {
                string workerKey = BuildHostKey(site.Name, host.Address);
                StopHostWorker(workerKey);
                _hosts.TryRemove(workerKey, out _);
            }

            SetNicknameValue(site.Name, host.Address, host.Label); SetCategoryValue(site.Name, host.Address, host.Group);
        }
        CommitHostChanges();
    }
    private void RemoveManagedHost(ManagedHost host)
    {
        var site = FindSite(host.Site); if (site == null) return;
        site.Hosts.RemoveAll(a => a.Equals(host.Address, StringComparison.OrdinalIgnoreCase));
        foreach (var key in site.Labels.Keys.Where(a => a.Equals(host.Address, StringComparison.OrdinalIgnoreCase)).ToList()) site.Labels.Remove(key);
        foreach (var key in site.Categories.Keys.Where(a => a.Equals(host.Address, StringComparison.OrdinalIgnoreCase)).ToList()) site.Categories.Remove(key);
        site.HostDetails.Remove(host.Address);
    }
    private void DeleteManagedHosts(List<ManagedHost> hosts)
    {
        foreach (var host in hosts) RemoveManagedHost(host);
        CommitHostChanges();
    }
    private void SaveTemporaryHosts(List<ManagedHost> hosts)
    {
        var temporary = hosts.Where(h => FindSite(h.Site)?.Temporary == true).ToList();
        if (temporary.Count == 0) return;
        using var picker = new SiteNameDialog("Save temporary hosts as a new site", "Save Hosts");
        if (picker.ShowDialog(_hostManager) != DialogResult.OK) return;
        if (!ValidateNewSiteName(picker.SiteName, null)) return;
        var site = new SiteDefinition { Name = picker.SiteName }; _sites.Add(site);
        foreach (var host in temporary)
        {
            RemoveManagedHost(host); site.Hosts.Add(host.Address); site.HostDetails[host.Address] = host.Options.Copy();
            SetNicknameValue(site.Name, host.Address, host.Label); SetCategoryValue(site.Name, host.Address, host.Group);
        }
        _selectedSiteName = site.Name; CommitHostChanges();
    }
    private void CommitHostChanges()
    {
        // Reload the compatibility editor before any path captures it back into configuration.
        LoadHostEditor(); SaveSites(); RefreshSiteList(_selectedSiteName);
        ReconcileMonitoringWithConfig(); RefreshGrid(); RebuildCommandView(); UpdateActionState();
    }
    internal static void RunHostManagementIntegrationTests()
    {
        using var form = new MainForm(false, true, false);
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Host Manager: " + message); }
        string siteName = form._sites[0].Name;
        form.ImportManagedHosts(new() { new(siteName, "192.0.2.10", "Printer", "Printers", new HostOptions(), "Unknown") }, false);
        var imported = form.HostSnapshot().First(h => h.Address == "192.0.2.10");
        Check(imported.Label == "Printer" && imported.Group == "Printers", "import metadata");

        form.StartMonitoring();
        string liveKey = BuildHostKey(siteName, imported.Address);
        Check(form._hosts.ContainsKey(liveKey), "enabled host starts a live worker");

        var options = imported.Options.Copy(); options.Enabled = false; options.TimeoutMs = 2345;
        form.SaveManagedHosts(new() { imported with { Label = "Accounting", Options = options } });
        Check(form.GetNickname(siteName, imported.Address) == "Accounting", "rename canonical source");
        Check(!form.GetConfiguredTargets().Any(h => h.Address == imported.Address), "disabled hosts excluded");
        Check(!form._hosts.ContainsKey(liveKey), "disable removes active host immediately");
        Check(!form._hostTokens.ContainsKey(liveKey), "disable cancels active worker immediately");

        int eventCountBeforeDisabledResult = form._stateEvents.Count;
        var disabledRuntime = new HostMonitor(siteName, imported.Address);
        for (int i = 0; i < 3; i++)
            form.ProcessResult(disabledRuntime, false, null);
        Check(disabledRuntime.State == HostState.Unknown, "disabled host result ignored");
        Check(form._stateEvents.Count == eventCountBeforeDisabledResult, "disabled host cannot create outage events");
        form.StopMonitoring();
        var wallboard = form.BuildWallboardSnapshot();
        Check(!wallboard.Sites.SelectMany(site => site.Hosts).Any(h => h.Address == imported.Address),
            "disabled hosts excluded from Wallboard topology");
        var controls = form.BuildWallboardControlSnapshot();
        Check(!controls.Hosts.Any(h => h.Address == imported.Address),
            "disabled hosts excluded from Wallboard live hosts");
        Check(controls.ConfiguredHosts.Any(h => h.Address == imported.Address),
            "disabled hosts remain available to configuration editors");
        form.AppendCommandLog(siteName, imported.Address, true, "Reply from test: time=1ms");
        form.RebuildCommandView();
        Check(!form._commandBox.Text.Contains(imported.Address, StringComparison.OrdinalIgnoreCase),
            "disabled hosts excluded from CLI monitoring trace");
        var config = form.BuildConfig();
        var saved = config.Sites.First(s => s.Name == siteName).HostDetails[imported.Address];
        Check(saved.TimeoutMs == 2345 && saved.Id == imported.Options.Id, "policy and identity export");
        form._sites.Add(new SiteDefinition { Name = "Temporary test", Temporary = true, Hosts = new() { "192.0.2.20" } });
        Check(!form.BuildConfig().Sites.Any(s => s.Temporary || s.Name == "Temporary test"), "temporary excluded from persistence");
        form.ApplyConfig(config);
        Check(form.HostSnapshot().First(h => h.Address == imported.Address).Options.Id == imported.Options.Id, "identity import round trip");
        form.DeleteManagedHosts(new() { form.HostSnapshot().First(h => h.Address == imported.Address) });
        Check(!form.HostSnapshot().Any(h => h.Address == imported.Address), "deletion");
    }

}

internal sealed class HostMenuColors : ProfessionalColorTable
{
    public HostMenuColors() { UseSystemColors = false; }
    public override Color ToolStripDropDownBackground => Color.FromArgb(22, 27, 34);
    public override Color MenuBorder => Color.FromArgb(60, 76, 90);
    public override Color MenuItemSelected => Color.FromArgb(32, 65, 80);
    public override Color MenuItemBorder => Color.FromArgb(60, 110, 130);
    public override Color SeparatorDark => Color.FromArgb(60, 76, 90);
    public override Color SeparatorLight => Color.FromArgb(22, 27, 34);
}
