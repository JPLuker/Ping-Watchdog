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

// Sites, the host editor model, labels and groups, and saving or loading configuration.
public sealed partial class MainForm
{
    private void DrawSiteItem(object? sender, DrawItemEventArgs e)
    {
        // Fill the whole row with the well first so gaps and unselected rows blend; only the selection gets a pill.
        using (var well = new SolidBrush(ScrollWell.WellColor))
            e.Graphics.FillRectangle(well, e.Bounds);

        if (e.Index < 0 || e.Index >= _siteList.Items.Count)
            return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = new Rectangle(4, e.Bounds.Y + 4, Math.Max(1, _siteList.ClientSize.Width - 8), Math.Max(1, e.Bounds.Height - 8));

        using var background = new SolidBrush(selected
            ? AppTheme.Palette.Surface3
            : ScrollWell.WellColor);
        using var textBrush = new SolidBrush(selected
            ? AppTheme.Palette.Text
            : AppTheme.Palette.Secondary);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var rowPath = WallboardStyle.Rounded(bounds, (int)(AppTheme.RadiusSmall * DeviceDpi / 96f));
        e.Graphics.FillPath(background, rowPath);
        if (selected)
        {
            // Accent bar marks the selected site beyond fill and text weight alone.
            int bar = Math.Max(3, (int)(3 * DeviceDpi / 96f)), barInset = bounds.Height / 4;
            using var accent = new SolidBrush(AppTheme.Palette.Accent);
            using var barPath = WallboardStyle.Rounded(new Rectangle(bounds.X + 4, bounds.Y + barInset, bar, Math.Max(1, bounds.Height - barInset * 2)), bar / 2);
            e.Graphics.FillPath(accent, barPath);
        }

        string text = _siteList.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            _siteItemFont,
            new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 18, bounds.Height),
            textBrush.Color,
            TextFormatFlags.VerticalCenter | AppTheme.NameTextFlags);

        if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues)
        {
            using var focus = new Pen(AppTheme.Palette.Focus, 2);
            e.Graphics.DrawPath(focus, rowPath);
        }
    }

    private void LoadSites()
    {
        _sites.Clear();

        if (!_persistSites)
        {
            _sites.Add(new SiteDefinition { Name = "Test Site" });
            return;
        }

        try
        {
            if (File.Exists(_autoConfigPath))
            {
                var autoConfig = SafeConfigStore.Read(_autoConfigPath);

                if (autoConfig is not null)
                {
                    ApplyConfig(autoConfig);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ReportStorageProblem("The saved configuration could not be opened. Autosave is off for this session; the original file has been kept. Update Ping Watchdog or restore a backup.\r\n\r\n" + ex.Message,
                protectOriginal: true);
            return;
        }

        try
        {
            if (File.Exists(_settingsPath))
            {
                var loaded = JsonSerializer.Deserialize<List<SiteDefinition>>(
                    File.ReadAllText(_settingsPath));

                if (loaded is not null)
                {
                    foreach (var site in loaded)
                    {
                        var name = (site.Name ?? string.Empty).Trim();

                        if (string.IsNullOrWhiteSpace(name) ||
                            name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase) ||
                            _sites.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var hosts = (site.Hosts ?? new List<string>())
                            .Select(h => h.Trim())
                            .Where(h => !string.IsNullOrWhiteSpace(h))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        string folderPath = NormalizeFolderPath(site.FolderPath);

                        _sites.Add(new SiteDefinition
                        {
                            Name = name,
                            FolderPath = folderPath,
                            Hosts = hosts,
                            Labels = NormalizeLabels(site.Labels, hosts),
                            Categories = NormalizeCategories(site.Categories, hosts),
                            HostDetails = NormalizeHostDetails(site.HostDetails, hosts)
                        });

                        EnsureFolderHierarchy(folderPath);
                    }

                    if (_sites.Count > 0)
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            ReportStorageProblem("The saved site list could not be opened. Autosave is off for this session; the original file has been kept. Restore a backup or export a new configuration to another location.\r\n\r\n" + ex.Message,
                protectOriginal: true);
            return;
        }

        try
        {
            if (!File.Exists(_bundledDefaultConfigPath))
                return;

            var defaultConfig = SafeConfigStore.Read(_bundledDefaultConfigPath);

            if (defaultConfig is null)
                return;

            ApplyConfig(defaultConfig);
            SaveSites();
        }
        catch
        {
            // A missing/corrupt starter config should never block the app.
        }
    }

    private bool TrySaveSites()
    {
        if (_configWriteBlocked || _restoreRequiresRestart) return false;
        if (!_persistSites) return true;

        try
        {
            // The full autosave is authoritative; the legacy inventory is secondary.
            SafeConfigStore.Write(_autoConfigPath, BuildConfig(captureEditor: false));
            SafeConfigStore.WriteAtomic(_settingsPath, JsonSerializer.Serialize(
                _sites.Where(site => !site.Temporary).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
            _storageWarning = null;
            _storageWarningShown = false;
            return true;
        }
        catch (Exception ex)
        {
            ReportStorageProblem("Changes could not be saved. Keep this window open and export the configuration to another location before closing.\r\n\r\n" + ex.Message);
            return false;
        }
    }

    private void RefreshSiteList(string? selectedName)
    {
        _ignoreSiteSelection = true;

        try
        {
            _siteList.Items.Clear();

            int totalHosts = _sites.Sum(site =>
                site.Hosts.Count(address => GetHostOptions(site, address).Enabled));
            _allSitesButton.Text = $"All sites · {totalHosts} {(totalHosts == 1 ? "host" : "hosts")}";

            foreach (var site in _sites
                .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase))
            {
                _siteList.Items.Add(new SiteListItem(
                    site.Name,
                    FormatSiteDisplayPath(site)));
            }

            int selectedIndex = -1;

            if (selectedName is not null)
            {
                for (int i = 0; i < _siteList.Items.Count; i++)
                {
                    if (_siteList.Items[i] is SiteListItem item &&
                        item.SiteName?.Equals(selectedName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            _siteList.SelectedIndex = selectedIndex;
            _allSitesButton.Selected = selectedIndex < 0;
            _allSitesButton.AccessibleDescription = selectedIndex < 0 ? "Selected" : null;
            _allSitesButton.Invalidate();
        }
        finally
        {
            _ignoreSiteSelection = false;
        }

        _organizationForm?.RefreshNow();
    }

    private void OnSiteSelectionChanged()
    {
        if (_ignoreSiteSelection || _siteList.SelectedIndex < 0)
            return;

        SelectSiteScope((_siteList.SelectedItem as SiteListItem)?.SiteName);
    }

    // null means every site; the pinned All sites button and the site list both route here.
    private void SelectSiteScope(string? siteName)
    {
        PersistCurrentEditor();
        SaveSites();
        _selectedSiteName = siteName;
        ReconcileMonitoringWithConfig();

        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void LoadHostEditor()
    {
        RefreshHostInventory();
        if (_selectedSiteName is null)
        {
            _siteHeaderLabel.Text = "All hosts";
            _ipBox.ReadOnly = true;
            _ipBox.Text = string.Join(
                Environment.NewLine,
                _sites.SelectMany(site =>
                    site.Hosts.Select(host => $"[{site.Name}] {host}")));
            return;
        }

        var site = FindSite(_selectedSiteName);

        _siteHeaderLabel.Text = $"{_selectedSiteName}";
        _ipBox.ReadOnly = false;
        _ipBox.Text = site is null
            ? string.Empty
            : string.Join(Environment.NewLine, site.Hosts);
    }

    private void PersistCurrentEditor()
    {
        if (_selectedSiteName is null)
            return;

        var site = FindSite(_selectedSiteName);
        if (site is null)
            return;

        site.Hosts = ParseHosts(_ipBox.Text);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);
        site.Categories = NormalizeCategories(site.Categories, site.Hosts);
        site.HostDetails = NormalizeHostDetails(site.HostDetails, site.Hosts);
    }

    private static Dictionary<string, string> NormalizeLabels(
        Dictionary<string, string>? labels,
        IEnumerable<string> hosts)
    {
        var hostList = hosts.ToList();
        var result = new Dictionary<string, string>();

        if (labels is null)
            return result;

        foreach (var pair in labels)
        {
            string? host = hostList.FirstOrDefault(h =>
                h.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            string label = pair.Value?.Trim() ?? string.Empty;

            if (host is not null && !string.IsNullOrWhiteSpace(label))
                result[host] = label;
        }

        return result;
    }

    private static Dictionary<string, string> NormalizeCategories(
        Dictionary<string, string>? categories,
        IEnumerable<string> hosts)
    {
        var hostList = hosts.ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (categories is null)
            return result;

        foreach (var pair in categories)
        {
            string? host = hostList.FirstOrDefault(h =>
                h.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            string category = pair.Value?.Trim() ?? string.Empty;

            if (host is not null && !string.IsNullOrWhiteSpace(category))
                result[host] = category;
        }

        return result;
    }

    private static List<string> ParseHosts(string text)
    {
        return text
            .Split(
                new[] { '\r', '\n', ',', ';', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private SiteDefinition? FindSite(string name)
    {
        return _sites.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetNickname(string siteName, string address)
    {
        var site = FindSite(siteName);
        if (site?.Labels is null)
            return string.Empty;

        var pair = site.Labels.FirstOrDefault(p =>
            p.Key.Equals(address, StringComparison.OrdinalIgnoreCase));

        return pair.Key is null ? string.Empty : pair.Value;
    }

    private void SetNicknameValue(string siteName, string address, string nickname)
    {
        var site = FindSite(siteName);
        if (site is null)
            return;

        site.Labels ??= new Dictionary<string, string>();

        var existingKey = site.Labels.Keys.FirstOrDefault(k =>
            k.Equals(address, StringComparison.OrdinalIgnoreCase));

        if (existingKey is not null)
            site.Labels.Remove(existingKey);

        nickname = nickname.Trim();
        if (!string.IsNullOrWhiteSpace(nickname))
            site.Labels[address] = nickname;

        string key = BuildHostKey(siteName, address);
        if (_hosts.TryGetValue(key, out var activeHost))
            activeHost.Label = nickname;
    }

    private string GetCategory(string siteName, string address)
    {
        var site = FindSite(siteName);
        if (site?.Categories is null)
            return string.Empty;

        var pair = site.Categories.FirstOrDefault(p =>
            p.Key.Equals(address, StringComparison.OrdinalIgnoreCase));

        return pair.Key is null ? string.Empty : pair.Value;
    }

    private void SetCategoryValue(string siteName, string address, string category)
    {
        var site = FindSite(siteName);
        if (site is null)
            return;

        site.Categories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var existingKey = site.Categories.Keys.FirstOrDefault(k =>
            k.Equals(address, StringComparison.OrdinalIgnoreCase));

        if (existingKey is not null)
            site.Categories.Remove(existingKey);

        category = category.Trim();
        if (!string.IsNullOrWhiteSpace(category))
            site.Categories[address] = category;
    }

    private string DescribeHost(string siteName, string address)
    {
        string nickname = GetNickname(siteName, address);
        return string.IsNullOrWhiteSpace(nickname)
            ? address
            : $"{nickname} ({address})";
    }

    private WatchdogConfig BuildConfig(bool captureEditor = true)
    {
        if (captureEditor)
            PersistCurrentEditor();

        return new WatchdogConfig
        {
            Sites = _sites.Where(site => !site.Temporary).Select(site => new SiteDefinition
            {
                Name = site.Name,
                FolderPath = NormalizeFolderPath(site.FolderPath),
                Hosts = site.Hosts.ToList(),
                Labels = new Dictionary<string, string>(site.Labels ?? new()),
                Categories = new Dictionary<string, string>(site.Categories ?? new(), StringComparer.OrdinalIgnoreCase),
                HostDetails = NormalizeHostDetails(site.HostDetails, site.Hosts)
            }).ToList(),
            SiteFolders = _siteFolders
                .Select(NormalizeFolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PingIntervalSeconds = (int)_intervalSeconds.Value,
            PingTimeoutMs = (int)_timeoutMs.Value,
            FailureThreshold = (int)_failureThreshold.Value,
            RecoveryThreshold = (int)_recoveryThreshold.Value,
            ShowCommandView = _showCommandView.Checked,
            SelectedSite = _selectedSiteName,
            EventHistoryHours = _eventHistoryHours,
            HideSuspectEvents = _hideSuspectEvents,
            Use12HourTime = DisplayTime.Use12HourTime,
            AutoCheckUpdates = _autoCheckUpdates,
            MinimizeToTray = _minimizeToTray,
            NotificationsEnabled = _notificationsEnabled,
            WallboardShowCli = _wallboardShowCli,
            ShowUpdateControlOnHome = _showUpdateControlOnHome,
            PriorityAlarmSound = _priorityAlarmSound
        };
    }

    private void SaveConfigFile(IWin32Window? owner = null)
    {
        var config = BuildConfig();

        using var dialog = new SaveFileDialog
        {
            Title = "Save Ping Watchdog Config",
            Filter = "Ping Watchdog Config (*.pingwatch.json)|*.pingwatch.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = "ping-watchdog-config.pingwatch.json",
            AddExtension = true,
            DefaultExt = "pingwatch.json"
        };

        if (dialog.ShowDialog(owner ?? this) != DialogResult.OK)
            return;

        try
        {
            SafeConfigStore.Write(dialog.FileName, config);

            _statusLabel.Text = $"Config saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save config.\r\n\r\n{ex.Message}",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void LoadConfigFile(IWin32Window? owner = null)
    {
        if (_cts is not null)
        {
            MessageBox.Show(
                "Stop monitoring before loading a config.",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Load Ping Watchdog Config",
            Filter = "Ping Watchdog Config (*.pingwatch.json)|*.pingwatch.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(owner ?? this) != DialogResult.OK)
            return;

        try
        {
            var config = SafeConfigStore.Read(dialog.FileName);

            if (config is null)
                throw new InvalidDataException("The config file was empty or invalid.");

            ApplyConfig(config);
            SaveSites();
            _statusLabel.Text = $"Config loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not load config.\r\n\r\n{ex.Message}",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyConfig(WatchdogConfig config)
    {
        SafeConfigStore.EnsureSupported(config.FormatVersion);
        var cleanedSites = new List<SiteDefinition>();

        foreach (var source in config.Sites ?? new List<SiteDefinition>())
        {
            string name = (source.Name ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase) ||
                cleanedSites.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var hosts = (source.Hosts ?? new List<string>())
                .Select(h => h.Trim())
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            cleanedSites.Add(new SiteDefinition
            {
                Name = name,
                FolderPath = NormalizeFolderPath(source.FolderPath),
                Hosts = hosts,
                Labels = NormalizeLabels(source.Labels, hosts),
                Categories = NormalizeCategories(source.Categories, hosts),
                HostDetails = NormalizeHostDetails(source.HostDetails, hosts)
            });
        }

        if (cleanedSites.Count == 0)
            cleanedSites.Add(new SiteDefinition { Name = "Default Site" });

        _sites.Clear();
        _sites.AddRange(cleanedSites);

        _siteFolders.Clear();

        foreach (var folder in config.SiteFolders ?? new List<string>())
            EnsureFolderHierarchy(folder);

        foreach (var site in _sites)
            EnsureFolderHierarchy(site.FolderPath);

        _intervalSeconds.Value = Math.Clamp(config.PingIntervalSeconds, (int)_intervalSeconds.Minimum, (int)_intervalSeconds.Maximum);
        _timeoutMs.Value = Math.Clamp(config.PingTimeoutMs, (int)_timeoutMs.Minimum, (int)_timeoutMs.Maximum);
        _failureThreshold.Value = Math.Clamp(config.FailureThreshold, (int)_failureThreshold.Minimum, (int)_failureThreshold.Maximum);
        _recoveryThreshold.Value = Math.Clamp(config.RecoveryThreshold, (int)_recoveryThreshold.Minimum, (int)_recoveryThreshold.Maximum);
        _showCommandView.Checked = config.ShowCommandView;
        _eventHistoryHours = NormalizeEventHistoryHours(config.EventHistoryHours);
        _hideSuspectEvents = config.HideSuspectEvents;
        DisplayTime.Use12HourTime = config.Use12HourTime;
        _autoCheckUpdates = config.AutoCheckUpdates;
        _minimizeToTray = config.MinimizeToTray;
        _notificationsEnabled = config.NotificationsEnabled;
        _priorityAlarmSound = config.PriorityAlarmSound ?? "";
        if (_priorityAlarm is not null) _priorityAlarm.SoundPath = _priorityAlarmSound;
        _wallboardShowCli = config.WallboardShowCli;
        _showUpdateControlOnHome = config.ShowUpdateControlOnHome;
        // UI/developer preferences are machine-user preferences, not scan-definition settings.
        // Re-apply the durable preference after any autosave/default/imported config is loaded.
        LoadUserPreferences();

        _selectedSiteName = config.SelectedSite is not null &&
            _sites.Any(s => s.Name.Equals(config.SelectedSite, StringComparison.OrdinalIgnoreCase))
            ? _sites.First(s => s.Name.Equals(config.SelectedSite, StringComparison.OrdinalIgnoreCase)).Name
            : null;

        _hosts.Clear();
        ClearCommandLog();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void SaveSelectedHosts()
    {
        if (_selectedSiteName is null || _cts is not null)
            return;

        PersistCurrentEditor();
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
    }

    private void AddSite()
    {
        using var dialog = new SiteNameDialog(
            "Add Site",
            "Add Site");

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string name = dialog.SiteName;

        if (!ValidateNewSiteName(name, existingSiteName: null))
            return;

        PersistCurrentEditor();

        _sites.Add(new SiteDefinition
        {
            Name = name,
            FolderPath = string.Empty
        });
        _selectedSiteName = name;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(name);
        LoadHostEditor();
        RefreshGrid();
        UpdateActionState();
    }

    private void RenameSite()
    {
        if (_selectedSiteName is null)
            return;

        var site = FindSite(_selectedSiteName);
        if (site is null)
            return;

        using var dialog = new SiteNameDialog(
            "Rename Site",
            "Rename",
            site.Name);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string newName = dialog.SiteName;

        if (!ValidateNewSiteName(newName, _selectedSiteName))
            return;

        PersistCurrentEditor();

        site.Name = newName;
        _selectedSiteName = newName;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(newName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void DeleteSite()
    {
        if (_selectedSiteName is null)
            return;

        var site = FindSite(_selectedSiteName);
        if (site is null)
            return;

        var result = MessageBox.Show(
            $"Delete site \"{site.Name}\" and its {site.Hosts.Count} saved host(s)?",
            "Ping Watchdog",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _sites.Remove(site);

        if (_sites.Count == 0)
            _sites.Add(new SiteDefinition { Name = "Default Site" });

        _selectedSiteName = _sites[0].Name;

        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(_selectedSiteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    // Shared rules so dialogs and Quick Add report the same problems.
    internal static string? SiteNameError(string name, IEnumerable<SiteDefinition> sites, string? existingSiteName)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a site name first.";
        if (name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase)) return "\"All Sites\" is reserved for the combined view.";
        bool duplicate = sites.Any(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            (existingSiteName is null || !s.Name.Equals(existingSiteName, StringComparison.OrdinalIgnoreCase)));
        return duplicate ? "A site with that name already exists." : null;
    }

    private bool ValidateNewSiteName(string name, string? existingSiteName)
    {
        if (SiteNameError(name, _sites, existingSiteName) is not string error) return true;
        MessageBox.Show(error, "Ping Watchdog", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }
}
