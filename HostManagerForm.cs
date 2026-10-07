namespace PingWatchdog;

internal sealed record ManagedHost(string Site, string Address, string Label, string Group, HostOptions Options, string State);

internal static class HostUi
{
    internal static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(75, 32), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(25, 34, 45), ForeColor = Color.White, Margin = new Padding(3) };
        b.Click += (_, _) => action(); return b;
    }
    internal static DataGridView Grid() => new() { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        BackgroundColor = Color.FromArgb(14, 20, 28), BorderStyle = BorderStyle.None,
        EnableHeadersVisualStyles = false,
        DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(14, 20, 28), ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(32, 65, 80), SelectionForeColor = Color.White },
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(25, 34, 45), ForeColor = Color.White } };
    internal static void Theme(Form f, string title)
    {
        f.Text = title + " • Ping Watchdog"; f.Size = new Size(1100, 720); f.MinimumSize = new Size(850, 580);
        f.StartPosition = FormStartPosition.CenterParent; f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Font = new Font("Segoe UI", 9.5f); f.BackColor = Color.FromArgb(10, 14, 20); f.ForeColor = Color.White;
    }
    internal static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
}

internal sealed class QuickAddForm : Form
{
    private readonly TextBox _paste = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(22, 27, 34), ForeColor = Color.White, AcceptsReturn = true };
    private readonly DataGridView _grid = HostUi.Grid();
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(4, 8, 0, 4) };
    private readonly ComboBox _site = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _group = new() { Width = 140, PlaceholderText = "Group / folder" };
    private readonly Func<List<ManagedHost>> _provider;
    private readonly Action<List<ManagedHost>, bool> _import;
    private readonly bool _temporary;
    private bool _updating;
    internal QuickAddForm(IEnumerable<string> sites, string? selected, Func<List<ManagedHost>> provider,
        Action<List<ManagedHost>, bool> import, bool temporary = false)
    {
        _provider = provider; _import = import; _temporary = temporary;
        HostUi.Theme(this, temporary ? "Quick Monitor" : "Quick Add");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { AutoSize = true, Text = "Paste addresses, Address / Label / Group spreadsheet rows, IPv4 CIDR, or 192.168.1.10-25.\nEdit the preview before adding. CIDR excludes network/broadcast except /31 and /32. Maximum 4,096 rows.", Padding = new Padding(0, 0, 0, 10) }, 0, 0);
        root.Controls.Add(_paste, 0, 1);
        var toolbar = HostUi.Bar();
        toolbar.Controls.Add(HostUi.Button("Paste Clipboard", () => { if (Clipboard.ContainsText()) _paste.Text = Clipboard.GetText(); }));
        toolbar.Controls.Add(HostUi.Button("Import CSV", () =>
        {
            using var file = new OpenFileDialog { Filter = "CSV / text|*.csv;*.txt;*.tsv|All files|*.*" };
            if (file.ShowDialog(this) == DialogResult.OK)
                try { _paste.Text = File.ReadAllText(file.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed"); }
        }));
        toolbar.Controls.Add(HostUi.Button("Add Manually", () => { _grid.Rows.Add(true, "", "", "", _group.Text, "Enter an address"); }));
        foreach (var name in sites) _site.Items.Add(name);
        _site.SelectedItem = selected; if (_site.SelectedIndex < 0 && _site.Items.Count > 0) _site.SelectedIndex = 0;
        if (!temporary) toolbar.Controls.Add(_site);
        toolbar.Controls.Add(_group);
        toolbar.Controls.Add(HostUi.Button("Assign Group to Selected", () =>
        {
            foreach (DataGridViewRow row in _grid.SelectedRows) row.Cells[4].Value = _group.Text.Trim();
        }));
        root.Controls.Add(toolbar, 0, 2);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", FillWeight = 35 });
        foreach (var name in new[] { "Address", "Label", "Site", "Group", "Status" }) _grid.Columns.Add(name, name);
        _grid.Columns[3].ReadOnly = true; _grid.Columns[5].ReadOnly = true;
        root.Controls.Add(_grid, 0, 3);
        var footer = HostUi.Bar(); footer.Controls.Add(_summary);
        footer.Controls.Add(HostUi.Button(temporary ? "Start Quick Monitor" : "Add Valid Hosts", Import));
        footer.Controls.Add(HostUi.Button("Cancel", Close)); root.Controls.Add(footer, 0, 4); Controls.Add(root);
        _paste.TextChanged += (_, _) => Parse(); _site.SelectedIndexChanged += (_, _) => ValidateRows();
        _grid.CellValueChanged += (_, _) => { if (!_updating) ValidateRows(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => e.ThrowException = false;
        AllowDrop = true; DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                try { _paste.Text = File.ReadAllText(paths[0]); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        };
    }
    internal void PreviewForTest(string text) => _paste.Text = text;
    internal int PreviewRowCount => _grid.Rows.Count;
    private void Parse()
    {
        _updating = true;
        _grid.Rows.Clear();
        foreach (var host in HostImport.Parse(_paste.Text))
        {
            int i = _grid.Rows.Add(host.Error.Length == 0, host.Address, host.Label, _site.Text,
                host.Group.Length == 0 ? _group.Text : host.Group, host.Error);
            _grid.Rows[i].Tag = host.Error;
        }
        _updating = false; ValidateRows();
    }
    private static string Cell(DataGridViewRow r, int i) => r.Cells[i].Value?.ToString()?.Trim() ?? "";
    private void ValidateRows()
    {
        _updating = true;
        var existing = _provider(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int valid = 0, invalid = 0, duplicate = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string address = Cell(row, 1); string normal = HostImport.Normalize(address); string status;
            row.Cells[3].Value = _temporary ? "Quick Monitor (temporary)" : _site.Text;
            if (!HostImport.Valid(address)) { status = row.Tag as string is { Length: > 0 } error ? error : "Invalid address"; invalid++; }
            else if (!seen.Add(normal)) { status = "Duplicate in preview"; duplicate++; }
            else if (!_temporary && existing.Any(h => HostImport.Normalize(h.Address) == normal))
            {
                var match = existing.First(h => HostImport.Normalize(h.Address) == normal);
                status = match.Label != Cell(row, 2) && Cell(row, 2).Length > 0 ? $"Label conflict • {match.Site}" : $"Already exists • {match.Site}";
                duplicate++;
            }
            else { status = "New"; valid++; }
            row.Cells[5].Value = status;
            row.Cells[5].Style.ForeColor = status == "New" ? Color.LightGreen : status.StartsWith("Invalid") ? Color.Salmon : Color.Gold;
        }
        _summary.Text = $"{valid} new · {duplicate} duplicates/conflicts · {invalid} invalid"; _updating = false;
    }
    private void Import()
    {
        _grid.EndEdit(); ValidateRows();
        if (!_temporary && _site.SelectedIndex < 0) return;
        var hosts = _grid.Rows.Cast<DataGridViewRow>().Where(r => Equals(r.Cells[0].Value, true) && Cell(r, 5) == "New")
            .Select(r => new ManagedHost(_site.Text, HostImport.Normalize(Cell(r, 1)), Cell(r, 2), Cell(r, 4), new HostOptions(), "Unknown")).ToList();
        if (hosts.Count == 0) { MessageBox.Show(this, "Select at least one valid new host. Duplicates and invalid rows are skipped."); return; }
        _import(hosts, _temporary); Close();
    }
}

internal sealed class HostManagerForm : Form
{
    private readonly Func<List<ManagedHost>> _provider;
    private readonly Action<List<ManagedHost>> _save;
    private readonly Action<List<ManagedHost>> _delete;
    private readonly Func<List<string>> _sites;
    private readonly DataGridView _grid = HostUi.Grid();
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 20, 28), ForeColor = Color.White, HideSelection = false };
    private readonly TextBox _search = new() { Width = 240, PlaceholderText = "Search name, address, site, group, tags" };
    private readonly ComboBox _destination = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _group = new() { Width = 130, PlaceholderText = "Group / folder" };
    private readonly TextBox _tags = new() { Width = 130, PlaceholderText = "Tags" };
    private readonly NumericUpDown _interval = new() { Maximum = 3600, Width = 65 };
    private readonly NumericUpDown _timeout = new() { Maximum = 60000, Width = 80, Increment = 100 };
    private readonly NumericUpDown _failures = new() { Maximum = 100, Width = 60 };
    private readonly Label _count = new() { AutoSize = true };
    private string? _siteFilter, _groupFilter;
    private List<ManagedHost> _draft = new();
    private readonly HashSet<string> _changedIds = new();
    private bool _loading;
    private readonly Label _saveStatus = new() { AutoSize = true, Padding = new Padding(8, 10, 0, 0) };
    private Button _saveButton = null!;
    private void UpdateSaveState()
    {
        _saveButton.Enabled = _changedIds.Count > 0;
        _saveStatus.Text = _changedIds.Count > 0 ? $"Unsaved changes • {_changedIds.Count} host(s)" : "All changes saved";
        _saveStatus.ForeColor = _changedIds.Count > 0 ? Color.Gold : Color.LightGreen;
    }
    private bool ConfirmPendingChanges()
    {
        _grid.EndEdit();
        if (_changedIds.Count == 0) return true;
        var result = MessageBox.Show(this, "Save your host changes before continuing?\n\nYes = Save changes · No = Discard changes · Cancel = Keep editing",
            "Unsaved host changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes) return SaveEdits();
        _changedIds.Clear(); Reload(); return true;
    }
    private void RunImmediate(Action action)
    {
        if (!ConfirmPendingChanges()) return;
        action(); Reload();
    }
    internal HostManagerForm(Func<List<ManagedHost>> provider, Action<List<ManagedHost>> save,
        Action<List<ManagedHost>> delete, Action<bool> quickAdd, Action<List<ManagedHost>> saveTemporary, Func<List<string>> sites)
    {
        _provider = provider; _save = save; _delete = delete; _sites = sites; HostUi.Theme(this, "Hosts");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var saveBar = HostUi.Bar();
        _saveButton = HostUi.Button("Save Changes", () => SaveEdits());
        _saveButton.BackColor = Color.FromArgb(25, 130, 100); _saveButton.Font = new Font(Font, FontStyle.Bold);
        _saveButton.MinimumSize = new Size(150, 40);
        saveBar.Controls.Add(_saveButton);
        saveBar.Controls.Add(HostUi.Button("Discard Changes", () =>
        {
            if (_changedIds.Count == 0 || MessageBox.Show(this, "Discard all unsaved host changes?", "Discard changes", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { _changedIds.Clear(); Reload(); }
        }));
        saveBar.Controls.Add(_saveStatus);
        saveBar.Controls.Add(HostUi.Button("Close", Close));
        root.Controls.Add(saveBar, 0, 0);
        root.Controls.Add(new Label { AutoSize = true, Padding = new Padding(3, 6, 3, 10),
            Text = "Edit the table or select hosts for bulk changes. Then click Save Changes above. Ctrl+S also saves.\nAdding hosts and saving temporary sessions use their own dialogs. Zero in monitoring settings uses the app default." }, 0, 1);
        var toolbar = HostUi.Bar(); toolbar.Controls.Add(_search);
        toolbar.Controls.Add(HostUi.Button("Quick Add", () => RunImmediate(() => quickAdd(false))));
        toolbar.Controls.Add(HostUi.Button("Quick Monitor", () => RunImmediate(() => quickAdd(true))));
        toolbar.Controls.Add(HostUi.Button("Reload Saved Hosts", () => { if (ConfirmPendingChanges()) Reload(); }));
        toolbar.Controls.Add(HostUi.Button("Keep Temporary Hosts…", () => RunImmediate(() => saveTemporary(Selected()))));
        root.Controls.Add(toolbar, 0, 2);
        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.Controls.Add(_tree, 0, 0);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", FillWeight = 40 });
        foreach (var name in new[] { "Name", "Address", "Site", "Group", "Tags", "Interval (s)", "Timeout (ms)", "Failures", "State" }) _grid.Columns.Add(name, name);
        foreach (int i in new[] { 2, 3, 9 }) _grid.Columns[i].ReadOnly = true;
        workspace.Controls.Add(_grid, 1, 0); root.Controls.Add(workspace, 0, 3);
        var bulk = HostUi.Bar(); bulk.Controls.Add(new Label { Text = "Bulk edit selected:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        bulk.Controls.Add(_destination); bulk.Controls.Add(HostUi.Button("Move Site", () => Bulk(h => h with { Site = _destination.Text })));
        bulk.Controls.Add(_group); bulk.Controls.Add(HostUi.Button("Set Group", () => Bulk(h => h with { Group = _group.Text.Trim() })));
        bulk.Controls.Add(_tags); bulk.Controls.Add(HostUi.Button("Set Tags", () => Bulk(h => h with { Options = Copy(h.Options, o => o.Tags = _tags.Text.Trim()) })));
        bulk.Controls.Add(HostUi.Button("Enable", () => Bulk(h => h with { Options = Copy(h.Options, o => o.Enabled = true) })));
        bulk.Controls.Add(HostUi.Button("Disable", () => Bulk(h => h with { Options = Copy(h.Options, o => o.Enabled = false) })));
        root.Controls.Add(bulk, 0, 4);
        var footer = HostUi.Bar();
        footer.Controls.Add(new Label { AutoSize = true, Text = "Interval s / Timeout ms / Failures (0 inherits defaults):", Padding = new Padding(0, 8, 0, 0) });
        footer.Controls.Add(_interval); footer.Controls.Add(_timeout); footer.Controls.Add(_failures);
        footer.Controls.Add(HostUi.Button("Apply Policy", () => Bulk(h => h with { Options = Copy(h.Options, o =>
            { o.IntervalSeconds = (int)_interval.Value; o.TimeoutMs = (int)_timeout.Value; o.FailureThreshold = (int)_failures.Value; }) })));

        footer.Controls.Add(HostUi.Button("Delete Selected", () =>
        {
            if (!ConfirmPendingChanges()) return;
            var selected = Selected(); if (selected.Count == 0) return;
            if (MessageBox.Show(this, $"Remove {selected.Count} hosts from monitoring and saved configuration?", "Remove hosts", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { _delete(selected); Reload(); }
        }));
        footer.Controls.Add(_count); root.Controls.Add(footer, 0, 5); Controls.Add(root);
        _search.TextChanged += (_, _) => FillGrid();
        _tree.AfterSelect += (_, e) => { if (!_loading && e.Node?.Tag is ValueTuple<string?, string?> filter) { (_siteFilter, _groupFilter) = filter; FillGrid(); } };
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not ManagedHost host) return;
            _changedIds.Add(host.Options.Id); UpdateSaveState();
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        { if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        FormClosing += (_, e) => { if (!ConfirmPendingChanges()) e.Cancel = true; };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; SaveEdits(); } };
        Shown += (_, _) => RefreshNow();
        UpdateSaveState();
    }
    private static HostOptions Copy(HostOptions value, Action<HostOptions> edit) { var copy = value.Copy(); edit(copy); return copy; }
    internal void RefreshNow()
    {
        // Reopening the manager must never replace an unfinished edit session.
        if (_changedIds.Count > 0) return;
        Reload();
    }
    private void Reload()
    {
        _loading = true;
        _draft = _provider();
        var hosts = _draft; _tree.BeginUpdate(); _tree.Nodes.Clear();
        var all = _tree.Nodes.Add("All Hosts"); all.Tag = ((string?)null, (string?)null);
        foreach (var site in hosts.GroupBy(h => h.Site).OrderBy(s => s.Key))
        {
            var node = all.Nodes.Add($"{site.Key} ({site.Count()})"); node.Tag = ((string?)site.Key, (string?)null);
            foreach (var group in site.GroupBy(h => h.Group).OrderBy(g => g.Key))
            {
                var child = node.Nodes.Add($"{(group.Key.Length == 0 ? "Ungrouped" : group.Key)} ({group.Count()})");
                child.Tag = ((string?)site.Key, (string?)group.Key);
            }
            node.Expand();
        }
        all.Expand(); _tree.EndUpdate(); _destination.Items.Clear();
        foreach (var site in _sites()) _destination.Items.Add(site);
        if (_destination.Items.Count > 0) _destination.SelectedIndex = 0;
        FillGrid(false); UpdateSaveState();
    }
    private void FillGrid(bool capture = true)
    {
        if (capture && !CaptureEdits()) return;
        var selectedIds = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => ((ManagedHost)r.Tag!).Options.Id).ToHashSet();
        _loading = true;
        _grid.Rows.Clear(); string search = _search.Text.Trim();
        foreach (var h in _draft.Where(h => (_siteFilter == null || h.Site == _siteFilter) && (_groupFilter == null || h.Group == _groupFilter) &&
            $"{h.Label} {h.Address} {h.Site} {h.Group} {h.Options.Tags}".Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            int i = _grid.Rows.Add(h.Options.Enabled, h.Label, h.Address, h.Site, h.Group, h.Options.Tags,
                h.Options.IntervalSeconds, h.Options.TimeoutMs, h.Options.FailureThreshold, h.State);
            _grid.Rows[i].Tag = h;
        }
        _grid.ClearSelection();
        foreach (DataGridViewRow row in _grid.Rows)
            if (selectedIds.Contains(((ManagedHost)row.Tag!).Options.Id)) row.Selected = true;
        _loading = false;
        _count.Text = $"{_grid.Rows.Count} hosts • {_grid.SelectedRows.Count} selected";
    }
    private List<ManagedHost> Selected() => _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (ManagedHost)r.Tag!).ToList();
    private void Bulk(Func<ManagedHost, ManagedHost> edit)
    {
        if (!CaptureEdits()) return;
        var selected = Selected();
        if (selected.Count == 0) { MessageBox.Show(this, "Select one or more host rows first.", "Select hosts"); return; }
        foreach (var host in selected)
        {
            var updated = edit(host);
            int index = _draft.FindIndex(h => h.Options.Id == host.Options.Id);
            if (index >= 0) _draft[index] = updated;
            _changedIds.Add(host.Options.Id);
        }
        FillGrid(false); UpdateSaveState();
    }
    private bool CaptureEdits()
    {
        _grid.EndEdit(); var updated = new List<ManagedHost>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var host = (ManagedHost)row.Tag!; var options = host.Options.Copy();
            string Cell(int n) => row.Cells[n].Value?.ToString()?.Trim() ?? "";
            if (!int.TryParse(Cell(6), out int interval) || interval is < 0 or > 3600 ||
                !int.TryParse(Cell(7), out int timeout) || timeout is < 0 or > 60000 ||
                !int.TryParse(Cell(8), out int failures) || failures is < 0 or > 100)
            { MessageBox.Show(this, "Use interval 0–3600, timeout 0–60000, and failures 0–100. Zero inherits defaults."); return false; }
            options.Enabled = Equals(row.Cells[0].Value, true); options.Tags = Cell(5);
            options.IntervalSeconds = interval; options.TimeoutMs = timeout; options.FailureThreshold = failures;
            var edited = host with { Label = Cell(1), Group = Cell(4), Options = options };
            updated.Add(edited); row.Tag = edited;
        }
        foreach (var edited in updated)
        {
            int index = _draft.FindIndex(h => h.Options.Id == edited.Options.Id);
            if (index >= 0) _draft[index] = edited;
        }
        return true;
    }
    private bool SaveEdits()
    {
        if (!CaptureEdits()) return false;
        try
        {
            _save(_draft.Where(h => _changedIds.Contains(h.Options.Id)).ToList());
            _changedIds.Clear(); Reload();
            _saveStatus.Text = "Saved successfully";
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Changes were not saved"); return false; }
    }
    internal static void RunDraftTests()
    {
        var host = new ManagedHost("Office", "192.0.2.1", "Before", "", new HostOptions(), "Unknown");
        var saved = new List<ManagedHost> { host };
        using var form = new HostManagerForm(() => saved.ToList(), edits => saved = edits.ToList(), _ => { }, _ => { }, _ => { }, () => new() { "Office", "Branch" });
        form.Reload();
        form._grid.Rows[0].Selected = true;
        form._grid.Rows[0].Cells[1].Value = "After";
        form._search.Text = "no match";
        form._search.Text = "";
        if (form._grid.Rows[0].Cells[1].Value?.ToString() != "After" || saved[0].Label != "Before")
            throw new InvalidOperationException("Host draft must survive filtering without saving.");
        form._grid.Rows[0].Selected = true;
        form.Bulk(h => h with { Site = "Branch" });
        if (saved[0].Site != "Office" || !form._saveButton.Enabled)
            throw new InvalidOperationException("Bulk edits must be staged.");
        if (!form.SaveEdits() || saved[0].Label != "After" || saved[0].Site != "Branch" || form._saveButton.Enabled)
            throw new InvalidOperationException("Save Changes must commit draft edits.");
    }
}
