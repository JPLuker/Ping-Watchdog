namespace PingWatchdog;

internal sealed class OrganizationForm : Form
{
    private readonly Func<OrganizationSnapshot> _provider;
    private readonly OrganizationActions _actions;
    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        HideSelection = false,
        FullRowSelect = true,
        ShowLines = true,
        ShowPlusMinus = true,
        ShowRootLines = true,
        Font = new Font("Segoe UI", 9.5f)
    };

    private readonly Button _newSiteButton = Button("New Site");
    private readonly Button _newFolderButton = Button("New Folder");
    private readonly Button _renameButton = Button("Rename");
    private readonly Button _moveButton = Button("Move...");
    private readonly Button _deleteButton = Button("Delete");
    private readonly Button _openButton = Button("Open Site");
    private readonly Label _detail = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Padding = new Padding(12, 8, 12, 8),
        TextAlign = ContentAlignment.MiddleLeft
    };

    public OrganizationForm(
        Func<OrganizationSnapshot> provider,
        OrganizationActions actions)
    {
        _provider = provider;
        _actions = actions;

        Text = "Organization • Ping Watchdog";
        Width = 760;
        Height = 620;
        MinimumSize = new Size(620, 460);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(10, 14, 20);
        ForeColor = Color.FromArgb(234, 240, 246);

        BuildLayout();
        ApplyTheme();
        WireActions();

        Shown += (_, _) => RefreshNow();
        _tree.AfterSelect += (_, _) => UpdateActionState();
        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is OrgTreeItem { Kind: OrgKind.Site } item)
                _actions.SelectSite(item.Key);
        };
    }

    internal void RefreshNow()
    {
        if (IsDisposed || Disposing)
            return;

        string? selectedKey = (_tree.SelectedNode?.Tag as OrgTreeItem)?.StableKey;
        var expanded = GetExpandedFolderKeys();

        var snapshot = _provider();

        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();

            var root = new TreeNode($"Sites ({snapshot.Sites.Count})")
            {
                Tag = new OrgTreeItem(OrgKind.Root, string.Empty, "root")
            };
            _tree.Nodes.Add(root);

            var folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in snapshot.Folders
                .OrderBy(path => Depth(path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string parent = Parent(folder);
                var node = new TreeNode(Name(folder))
                {
                    Tag = new OrgTreeItem(OrgKind.Folder, folder, $"folder:{folder}")
                };

                if (string.IsNullOrWhiteSpace(parent))
                    root.Nodes.Add(node);
                else if (folderNodes.TryGetValue(parent, out var parentNode))
                    parentNode.Nodes.Add(node);
                else
                    root.Nodes.Add(node);

                folderNodes[folder] = node;
            }

            foreach (var site in snapshot.Sites
                .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase))
            {
                var node = new TreeNode($"{site.Name}  ({site.HostCount})")
                {
                    Tag = new OrgTreeItem(
                        OrgKind.Site,
                        site.Name,
                        $"site:{site.Name}")
                };

                if (!string.IsNullOrWhiteSpace(site.FolderPath) &&
                    folderNodes.TryGetValue(site.FolderPath, out var folderNode))
                {
                    folderNode.Nodes.Add(node);
                }
                else
                {
                    root.Nodes.Add(node);
                }
            }

            root.Expand();

            foreach (var key in expanded)
            {
                var node = FindNodeByStableKey(root, key);
                node?.Expand();
            }

            if (selectedKey is not null)
                _tree.SelectedNode = FindNodeByStableKey(root, selectedKey);

            _tree.SelectedNode ??= root;
        }
        finally
        {
            _tree.EndUpdate();
        }

        UpdateActionState();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        header.Controls.Add(new Label
        {
            Text = "Site Organization",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold),
            ForeColor = Color.White
        });
        header.Controls.Add(new Label
        {
            Text = "Folders can contain subfolders and sites. Empty folders are saved.",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 153, 169)
        });

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        foreach (var button in new[]
        {
            _newSiteButton,
            _newFolderButton,
            _renameButton,
            _moveButton,
            _deleteButton,
            _openButton
        })
        {
            toolbar.Controls.Add(button);
        }

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_tree, 0, 2);
        root.Controls.Add(_detail, 0, 3);
        Controls.Add(root);
    }

    private static Button Button(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 4, 7, 4)
        };
    }

    private void ApplyTheme()
    {
        var card = Color.FromArgb(14, 20, 28);
        var text = Color.FromArgb(234, 240, 246);
        var muted = Color.FromArgb(139, 153, 169);
        var border = Color.FromArgb(39, 49, 61);

        _tree.BackColor = card;
        _tree.ForeColor = text;
        _tree.LineColor = Color.FromArgb(70, 85, 101);
        _detail.BackColor = Color.FromArgb(12, 18, 26);
        _detail.ForeColor = muted;

        foreach (var button in new[]
        {
            _newSiteButton,
            _newFolderButton,
            _renameButton,
            _moveButton,
            _deleteButton,
            _openButton
        })
        {
            button.BackColor = Color.FromArgb(25, 34, 45);
            button.ForeColor = text;
            button.FlatAppearance.BorderColor = border;
        }
    }

    private void WireActions()
    {
        _newFolderButton.Click += (_, _) => CreateFolder();
        _newSiteButton.Click += (_, _) => CreateSite();
        _renameButton.Click += (_, _) => RenameSelected();
        _moveButton.Click += (_, _) => MoveSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _openButton.Click += (_, _) => OpenSelectedSite();
    }

    private void CreateFolder()
    {
        string parent = SelectedFolderForChildren();

        using var dialog = new SiteNameDialog(
            "New Folder",
            "Create Folder");

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string? error = _actions.AddFolder(parent, dialog.SiteName);
        ShowError(error);
        if (error is null)
            RefreshNow();
    }

    private void CreateSite()
    {
        string folder = SelectedFolderForChildren();

        using var dialog = new SiteNameDialog(
            "New Site",
            "Create Site");

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string? error = _actions.AddSite(dialog.SiteName, folder);
        ShowError(error);
        if (error is null)
            RefreshNow();
    }

    private void RenameSelected()
    {
        if (_tree.SelectedNode?.Tag is not OrgTreeItem item)
            return;

        if (item.Kind == OrgKind.Folder)
        {
            using var dialog = new SiteNameDialog(
                "Rename Folder",
                "Rename",
                Name(item.Key));

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            ShowError(_actions.RenameFolder(item.Key, dialog.SiteName));
            RefreshNow();
        }
        else if (item.Kind == OrgKind.Site)
        {
            using var dialog = new SiteNameDialog(
                "Rename Site",
                "Rename",
                item.Key);

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            ShowError(_actions.RenameSite(item.Key, dialog.SiteName));
            RefreshNow();
        }
    }

    private void MoveSelected()
    {
        if (_tree.SelectedNode?.Tag is not OrgTreeItem item ||
            item.Kind == OrgKind.Root)
        {
            return;
        }

        var snapshot = _provider();
        var folders = snapshot.Folders
            .Where(folder =>
                item.Kind != OrgKind.Folder ||
                (!folder.Equals(item.Key, StringComparison.OrdinalIgnoreCase) &&
                 !folder.StartsWith(item.Key + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string currentFolder = item.Kind == OrgKind.Folder
            ? Parent(item.Key)
            : snapshot.Sites
                .FirstOrDefault(site =>
                    site.Name.Equals(item.Key, StringComparison.OrdinalIgnoreCase))
                ?.FolderPath ?? string.Empty;

        using var dialog = new FolderPickerDialog(
            "Move to Folder",
            folders,
            currentFolder);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string? error = item.Kind == OrgKind.Folder
            ? _actions.MoveFolder(item.Key, dialog.SelectedFolder)
            : _actions.MoveSite(item.Key, dialog.SelectedFolder);

        ShowError(error);
        if (error is null)
            RefreshNow();
    }

    private void DeleteSelected()
    {
        if (_tree.SelectedNode?.Tag is not OrgTreeItem item ||
            item.Kind == OrgKind.Root)
        {
            return;
        }

        if (item.Kind == OrgKind.Folder)
        {
            var answer = MessageBox.Show(
                this,
                $"Delete folder \"{Name(item.Key)}\"?\r\n\r\nIts sites and subfolders will be moved to the parent folder; no sites are deleted.",
                "Ping Watchdog",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
                return;

            ShowError(_actions.DeleteFolder(item.Key));
            RefreshNow();
        }
        else
        {
            var answer = MessageBox.Show(
                this,
                $"Delete site \"{item.Key}\" and its saved hosts?",
                "Ping Watchdog",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
                return;

            ShowError(_actions.DeleteSite(item.Key));
            RefreshNow();
        }
    }

    private void OpenSelectedSite()
    {
        if (_tree.SelectedNode?.Tag is OrgTreeItem { Kind: OrgKind.Site } item)
            _actions.SelectSite(item.Key);
    }

    private string SelectedFolderForChildren()
    {
        if (_tree.SelectedNode?.Tag is not OrgTreeItem item)
            return string.Empty;

        return item.Kind switch
        {
            OrgKind.Folder => item.Key,
            OrgKind.Site => _provider().Sites
                .FirstOrDefault(site =>
                    site.Name.Equals(item.Key, StringComparison.OrdinalIgnoreCase))
                ?.FolderPath ?? string.Empty,
            _ => string.Empty
        };
    }

    private void UpdateActionState()
    {
        var item = _tree.SelectedNode?.Tag as OrgTreeItem;
        bool folder = item?.Kind == OrgKind.Folder;
        bool site = item?.Kind == OrgKind.Site;

        _renameButton.Enabled = folder || site;
        _moveButton.Enabled = folder || site;
        _deleteButton.Enabled = folder || site;
        _openButton.Enabled = site;

        _detail.Text = item?.Kind switch
        {
            OrgKind.Folder => $"Folder: {item.Key}",
            OrgKind.Site => $"Site: {item.Key}",
            _ => "Root • Add folders or sites here."
        };
    }

    private void ShowError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return;

        MessageBox.Show(
            this,
            error,
            "Ping Watchdog",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private HashSet<string> GetExpandedFolderKeys()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (TreeNode root in _tree.Nodes)
            CollectExpanded(root, result);

        return result;
    }

    private static void CollectExpanded(
        TreeNode node,
        HashSet<string> result)
    {
        if (node.IsExpanded &&
            node.Tag is OrgTreeItem { Kind: OrgKind.Folder } item)
        {
            result.Add(item.StableKey);
        }

        foreach (TreeNode child in node.Nodes)
            CollectExpanded(child, result);
    }

    private static TreeNode? FindNodeByStableKey(
        TreeNode node,
        string stableKey)
    {
        if (node.Tag is OrgTreeItem item &&
            item.StableKey.Equals(stableKey, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            var found = FindNodeByStableKey(child, stableKey);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static int Depth(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? 0
            : path.Count(ch => ch == '/') + 1;

    private static string Parent(string path)
    {
        path = MainForm.NormalizeFolderPath(path);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string Name(string path)
    {
        path = MainForm.NormalizeFolderPath(path);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private enum OrgKind
    {
        Root,
        Folder,
        Site
    }

    private sealed record OrgTreeItem(
        OrgKind Kind,
        string Key,
        string StableKey);
}

internal sealed class FolderPickerDialog : Form
{
    private readonly ComboBox _combo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Top
    };

    public string SelectedFolder =>
        _combo.SelectedIndex <= 0
            ? string.Empty
            : _combo.SelectedItem?.ToString() ?? string.Empty;

    public FolderPickerDialog(
        string title,
        IReadOnlyList<string> folders,
        string currentFolder)
    {
        Text = title;
        Width = 480;
        Height = 180;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(13, 17, 23);
        ForeColor = Color.FromArgb(230, 237, 243);
        Font = new Font("Segoe UI", 9.5f);

        _combo.Items.Add("(Root)");
        foreach (var folder in folders)
            _combo.Items.Add(folder);

        int index = 0;
        for (int i = 1; i < _combo.Items.Count; i++)
        {
            if (string.Equals(
                _combo.Items[i]?.ToString(),
                currentFolder,
                StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        _combo.SelectedIndex = index;
        _combo.BackColor = Color.FromArgb(22, 27, 34);
        _combo.ForeColor = ForeColor;

        var ok = new Button
        {
            Text = "Move",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 134, 96),
            ForeColor = Color.White
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.White
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 6, 10, 6)
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var label = new Label
        {
            Text = "Destination folder:",
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(12, 10, 0, 0)
        };

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 6, 12, 6)
        };
        panel.Controls.Add(_combo);

        Controls.Add(panel);
        Controls.Add(label);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
    }
}
