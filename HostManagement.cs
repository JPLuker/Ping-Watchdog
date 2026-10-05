namespace PingWatchdog;

public sealed partial class MainForm
{
    private DataGridView? _hostInventory;
    private HostManagerForm? _hostManager;
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
        _hostInventory.CellDoubleClick += (_, _) => OpenHostManager();
        return _hostInventory;
    }
    private List<ManagedHost> HostSnapshot() => _sites.SelectMany(site => site.Hosts.Select(address =>
        new ManagedHost(site.Name, address, GetNickname(site.Name, address), GetCategory(site.Name, address),
            GetHostOptions(site, address).Copy(), _hosts.TryGetValue(BuildHostKey(site.Name, address), out var host) ? host.State.ToString() : "Unknown"))).ToList();
    private void RefreshHostInventory()
    {
        if (_hostInventory == null || _hostInventory.IsDisposed) return;
        _hostInventory.Rows.Clear();
        foreach (var h in HostSnapshot().Where(h => _selectedSiteName == null || h.Site == _selectedSiteName))
            _hostInventory.Rows.Add(h.Label, h.Address, h.Site, h.Group, h.Options.Enabled ? "Enabled" : "Disabled");
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
        var options = imported.Options.Copy(); options.Enabled = false; options.TimeoutMs = 2345;
        form.SaveManagedHosts(new() { imported with { Label = "Accounting", Options = options } });
        Check(form.GetNickname(siteName, imported.Address) == "Accounting", "rename canonical source");
        Check(!form.GetConfiguredTargets().Any(h => h.Address == imported.Address), "disabled hosts excluded");
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
