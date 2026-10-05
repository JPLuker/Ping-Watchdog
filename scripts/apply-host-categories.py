from pathlib import Path


def replace_or_fail(path: str, old: str, new: str, name: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8-sig")
    if old not in text:
        raise RuntimeError(f"Patch marker not found: {name} in {path}")
    file.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


# --- Windows model ---------------------------------------------------------
replace_or_fail(
    "Program.cs",
    '''    public Dictionary<string, string> Labels { get; set; } = new();
}
''',
    '''    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, string> Categories { get; set; } = new();
}
''',
    "SiteDefinition.Categories",
)

replace_or_fail(
    "Program.cs",
    '''            } else column.Width = spec.Width;
            _grid.Columns.Add(column);
        }
    }
''',
    '''            } else column.Width = spec.Width;
            _grid.Columns.Add(column);
        }

        var categoryColumn = new DataGridViewTextBoxColumn
        {
            Name = "CategoryColumn",
            HeaderText = "Category",
            DataPropertyName = "Category",
            Width = 120,
            MinimumWidth = 90
        };
        int labelIndex = _grid.Columns["LabelColumn"]?.Index ?? 1;
        _grid.Columns.Insert(Math.Min(labelIndex + 1, _grid.Columns.Count), categoryColumn);
    }
''',
    "Windows Category column",
)

replace_or_fail(
    "Program.cs",
    '''        form.SetNicknameValue("Test Site", "127.0.0.1", "Loopback");
        Check(form.GetNickname("Test Site", "127.0.0.1") == "Loopback");

        form.AppendCommandLog''',
    '''        form.SetNicknameValue("Test Site", "127.0.0.1", "Loopback");
        Check(form.GetNickname("Test Site", "127.0.0.1") == "Loopback");
        form.SetCategoryValue("Test Site", "127.0.0.1", "Infrastructure");
        Check(form.GetCategory("Test Site", "127.0.0.1") == "Infrastructure");

        form.AppendCommandLog''',
    "Windows category self-test setup",
)

replace_or_fail(
    "Program.cs",
    '''        Check(configRoundTrip?.Sites[0].Labels.Values.Contains("Loopback") == true);
        Check(configRoundTrip?.PingIntervalSeconds == 2);''',
    '''        Check(configRoundTrip?.Sites[0].Labels.Values.Contains("Loopback") == true);
        Check(configRoundTrip?.Sites[0].Categories.Values.Contains("Infrastructure") == true);
        Check(configRoundTrip?.PingIntervalSeconds == 2);''',
    "Windows category config self-test",
)

replace_or_fail(
    "Program.cs",
    '''                            Hosts = hosts,
                            Labels = NormalizeLabels(site.Labels, hosts)
                        });''',
    '''                            Hosts = hosts,
                            Labels = NormalizeLabels(site.Labels, hosts),
                            Categories = NormalizeCategories(site.Categories, hosts)
                        });''',
    "Legacy categories load",
)

replace_or_fail(
    "Program.cs",
    '''        site.Hosts = ParseHosts(_ipBox.Text);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);
    }

    private static Dictionary<string, string> NormalizeLabels''',
    '''        site.Hosts = ParseHosts(_ipBox.Text);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);
        site.Categories = NormalizeCategories(site.Categories, site.Hosts);
    }

    private static Dictionary<string, string> NormalizeLabels''',
    "Category cleanup on host edit",
)

replace_or_fail(
    "Program.cs",
    '''        return result;
    }

    internal static string NormalizeFolderPath''',
    '''        return result;
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

    internal static string NormalizeFolderPath''',
    "NormalizeCategories helper",
)

replace_or_fail(
    "Program.cs",
    '''        if (_hosts.TryGetValue(key, out var activeHost))
            activeHost.Label = nickname;
    }

    private string DescribeHost''',
    '''        if (_hosts.TryGetValue(key, out var activeHost))
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

    private string DescribeHost''',
    "Category accessors",
)

replace_or_fail(
    "Program.cs",
    '''        var setLabel = new ToolStripMenuItem("Set label / nickname");
        var clearLabel = new ToolStripMenuItem("Clear label");

        setLabel.Click += (_, _) => SetLabelForSelectedHost();
        clearLabel.Click += (_, _) => ClearLabelForSelectedHost();

        _gridMenu.Items.Add(setLabel);
        _gridMenu.Items.Add(clearLabel);
        _grid.ContextMenuStrip = _gridMenu;''',
    '''        var setLabel = new ToolStripMenuItem("Set label / nickname");
        var clearLabel = new ToolStripMenuItem("Clear label");
        var setCategory = new ToolStripMenuItem("Set category...");
        var clearCategory = new ToolStripMenuItem("Clear category");

        setLabel.Click += (_, _) => SetLabelForSelectedHost();
        clearLabel.Click += (_, _) => ClearLabelForSelectedHost();
        setCategory.Click += (_, _) => SetCategoryForSelectedHost();
        clearCategory.Click += (_, _) => ClearCategoryForSelectedHost();

        _gridMenu.Items.Add(setLabel);
        _gridMenu.Items.Add(clearLabel);
        _gridMenu.Items.Add(new ToolStripSeparator());
        _gridMenu.Items.Add(setCategory);
        _gridMenu.Items.Add(clearCategory);
        _grid.ContextMenuStrip = _gridMenu;''',
    "Category context menu",
)

replace_or_fail(
    "Program.cs",
    '''        SaveSites();
        RefreshGrid();
        RebuildCommandView();
    }

    private WatchdogConfig BuildConfig''',
    '''        SaveSites();
        RefreshGrid();
        RebuildCommandView();
    }

    private void SetCategoryForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        string current = GetCategory(identity.Value.Site, identity.Value.Host);

        using var dialog = new CategoryDialog(identity.Value.Host, current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetCategoryValue(identity.Value.Site, identity.Value.Host, dialog.Category);
        SaveSites();
        RefreshGrid();
    }

    private void ClearCategoryForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        SetCategoryValue(identity.Value.Site, identity.Value.Host, string.Empty);
        SaveSites();
        RefreshGrid();
    }

    private WatchdogConfig BuildConfig''',
    "Category editor actions",
)

replace_or_fail(
    "Program.cs",
    '''                Hosts = site.Hosts.ToList(),
                Labels = new Dictionary<string, string>(site.Labels ?? new())
            }).ToList(),''',
    '''                Hosts = site.Hosts.ToList(),
                Labels = new Dictionary<string, string>(site.Labels ?? new()),
                Categories = new Dictionary<string, string>(site.Categories ?? new(), StringComparer.OrdinalIgnoreCase)
            }).ToList(),''',
    "BuildConfig categories",
)

replace_or_fail(
    "Program.cs",
    '''                Hosts = hosts,
                Labels = NormalizeLabels(source.Labels, hosts)
            });''',
    '''                Hosts = hosts,
                Labels = NormalizeLabels(source.Labels, hosts),
                Categories = NormalizeCategories(source.Categories, hosts)
            });''',
    "ApplyConfig categories",
)

replace_or_fail(
    "Program.cs",
    '''        site.Hosts = ParseHosts(hostText);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);

        if (_selectedSiteName''',
    '''        site.Hosts = ParseHosts(hostText);
        site.Labels = NormalizeLabels(site.Labels, site.Hosts);
        site.Categories = NormalizeCategories(site.Categories, site.Hosts);

        if (_selectedSiteName''',
    "Wallboard host category cleanup",
)

replace_or_fail(
    "Program.cs",
    '''                        Host = h.Address,
                        Label = GetNickname(h.Site, h.Address),
                        Status = h.State switch''',
    '''                        Host = h.Address,
                        Label = GetNickname(h.Site, h.Address),
                        Category = GetCategory(h.Site, h.Address),
                        Status = h.State switch''',
    "RefreshGrid category value",
)

replace_or_fail(
    "Program.cs",
    '''                var selected = form.GetSelectedHostIdentity();
                for (int i = 0; i < 6; i++)''',
    '''                var selected = form.GetSelectedHostIdentity();
                form.SetCategoryValue(selected!.Value.Site, selected.Value.Host, "AP");
                form.RefreshGrid();
                Check(form._grid.CurrentRow?.Cells["CategoryColumn"].Value?.ToString() == "AP",
                    "Category did not render for the selected host.");
                Check(form.GetSelectedHostIdentity() == selected, "Category edit changed the selected host.");
                for (int i = 0; i < 6; i++)''',
    "UI category regression test",
)

# --- Linux config compatibility --------------------------------------------
replace_or_fail(
    "linux/PingWatchdog.Linux/Core.cs",
    '''    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}''',
    '''    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}''',
    "Linux SiteDefinition.Categories",
)

replace_or_fail(
    "linux/PingWatchdog.Linux/Core.cs",
    '''            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var host in hosts)
            {
                if (raw.Labels is not null && raw.Labels.TryGetValue(host, out var label) && !string.IsNullOrWhiteSpace(label))
                    labels[host] = label.Trim();
            }

            config.Sites.Add(new SiteDefinition
            {
                Name = name,
                FolderPath = NormalizeFolderPath(raw.FolderPath),
                Hosts = hosts,
                Labels = labels
            });''',
    '''            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var host in hosts)
            {
                if (raw.Labels is not null && raw.Labels.TryGetValue(host, out var label) && !string.IsNullOrWhiteSpace(label))
                    labels[host] = label.Trim();
                if (raw.Categories is not null && raw.Categories.TryGetValue(host, out var category) && !string.IsNullOrWhiteSpace(category))
                    categories[host] = category.Trim();
            }

            config.Sites.Add(new SiteDefinition
            {
                Name = name,
                FolderPath = NormalizeFolderPath(raw.FolderPath),
                Hosts = hosts,
                Labels = labels,
                Categories = categories
            });''',
    "Linux sanitize categories",
)

replace_or_fail(
    "linux/PingWatchdog.Linux/Core.cs",
    '''            site.Labels = site.Labels
                .Where(pair => site.Hosts.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            SaveUnsafe();''',
    '''            site.Labels = site.Labels
                .Where(pair => site.Hosts.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            site.Categories = site.Categories
                .Where(pair => site.Hosts.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            SaveUnsafe();''',
    "Linux category cleanup",
)

replace_or_fail(
    "linux/PingWatchdog.Linux/Core.cs",
    '''    public string? AddFolder(string parent, string name)''',
    '''    public string? SetCategory(string siteName, string host, string category)
    {
        lock (_gate)
        {
            var site = FindSiteUnsafe(siteName);
            if (site is null) return "That site no longer exists.";
            if (!site.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase)) return "That host no longer exists.";

            if (string.IsNullOrWhiteSpace(category))
                site.Categories.Remove(host);
            else
                site.Categories[host] = category.Trim();

            SaveUnsafe();
        }
        RaiseChanged();
        return null;
    }

    public string? AddFolder(string parent, string name)''',
    "Linux SetCategory API",
)

replace_or_fail(
    "linux/PingWatchdog.Linux/Core.cs",
    '''        Hosts = site.Hosts.ToList(),
        Labels = new Dictionary<string, string>(site.Labels, StringComparer.OrdinalIgnoreCase)
    };''',
    '''        Hosts = site.Hosts.ToList(),
        Labels = new Dictionary<string, string>(site.Labels, StringComparer.OrdinalIgnoreCase),
        Categories = new Dictionary<string, string>(site.Categories, StringComparer.OrdinalIgnoreCase)
    };''',
    "Linux clone categories",
)

# Docs and metadata.
readme_path = Path("README.md")
readme = readme_path.read_text(encoding="utf-8-sig")
if "## Host categories" not in readme:
    readme += '''\n\n## Host categories\n\nHosts can have both a **Label** and a separate **Category**. Use labels for a human-friendly identity such as `Front Lobby` and categories for device type or role such as `AP`, `Firewall`, `Switch`, `Router`, `Server`, `Printer`, `Camera`, `UPS`, `Workstation`, or any custom value. On Windows, right-click a monitored host and choose **Set category...**. Category metadata is saved with the site configuration and is cleaned up automatically when an address is removed. Linux preserves the same category metadata in the shared config schema.\n'''
    readme_path.write_text(readme, encoding="utf-8", newline="\n")

quick_path = Path("README-FIRST.txt")
quick = quick_path.read_text(encoding="utf-8-sig")
if "HOST CATEGORIES" not in quick:
    quick += '''\n\nHOST CATEGORIES\nRight-click a monitored host and choose Set category... to classify it separately from its nickname/label.\nExamples: AP, Firewall, Switch, Router, Server, Printer, Camera, UPS, Workstation, IoT, or any custom category.\nCategories are saved with the site configuration.\n'''
    quick_path.write_text(quick, encoding="utf-8", newline="\n")

project_path = Path("PingWatchdog.csproj")
project = project_path.read_text(encoding="utf-8-sig")
project = project.replace("<Version>1.14.55</Version>", "<Version>1.14.56</Version>")
project_path.write_text(project, encoding="utf-8", newline="\n")

print("Host category patch applied successfully.")
