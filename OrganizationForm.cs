namespace PingWatchdog;

internal sealed class OrganizationForm : Form
{
    // FOCAL: sites and folders. Compact native tree with a wrapping action row.
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
        Font = AuroraTheme.LabelFont, AccessibleName = "Sites and folders",
        ShowNodeToolTips = true, TabIndex = 1
    };

    private readonly Button _newSiteButton = Button("Add site");
    private readonly Button _newFolderButton = Button("Add folder");
    private readonly Button _renameButton = Button("Rename");
    private readonly Button _moveButton = Button("Move…");
    private readonly Button _deleteButton = Button("Delete");
    private readonly Button _openButton = Button("Open site");
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
        Font = AuroraTheme.LabelFont;
        BackColor = Color.FromArgb(10, 14, 20);
        ForeColor = Color.FromArgb(234, 240, 246);

        BuildLayout();
        AuroraTheme.Register(this);
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, AuroraTheme.Space3)
        };
        header.Controls.Add(new Label
        {
            Text = "Site organization", Tag = "title",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold),
            ForeColor = Color.White
        });
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabIndex = 0,
            Margin = new Padding(0, 0, 0, AuroraTheme.Space3)
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
        return new AuroraButton
        {
            Text = text, AutoSize = true, MinimumSize = new Size(0, 32),
            Margin = new Padding(0, 0, 8, 4)
        };
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    internal void CheckLayout()
    {
        PerformLayout();
        foreach (var button in new[] { _newSiteButton, _newFolderButton, _renameButton, _moveButton, _deleteButton, _openButton })
            if (!button.Visible || !button.Parent!.ClientRectangle.Contains(button.Bounds) ||
                !ClientRectangle.Contains(RectangleToClient(button.RectangleToScreen(button.ClientRectangle))))
                throw new InvalidOperationException($"Organization action is clipped: {button.Text}; bounds={button.Bounds}, parent={button.Parent?.ClientRectangle}.");
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
        DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Destination folder"
    };
    public string SelectedFolder => _combo.SelectedIndex <= 0 ? string.Empty : _combo.SelectedItem?.ToString() ?? string.Empty;

    public FolderPickerDialog(string title, IReadOnlyList<string> folders, string currentFolder)
    {
        _combo.Items.Add("(Root)");
        foreach (string folder in folders) _combo.Items.Add(folder);
        int selected = 0;
        for (int i = 1; i < _combo.Items.Count; i++)
            if (string.Equals(_combo.Items[i]?.ToString(), currentFolder, StringComparison.OrdinalIgnoreCase)) { selected = i; break; }
        _combo.SelectedIndex = selected;
        CompactDialog.StylePicker(_combo);
        CompactDialog.Build(this, title, new Label { Text = "Destination folder" }, _combo, "Move", 480);
    }
}
