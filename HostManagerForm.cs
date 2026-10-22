namespace PingWatchdog;

internal sealed record ManagedHost(string Site, string Address, string Label, string Group, HostOptions Options, string State);

internal static class HostUi
{
    internal static Button Button(string text, Action action)
    {
        var b = new AuroraButton { Text = text, AutoSize = true, MinimumSize = new Size(75, 32), FlatStyle = FlatStyle.Flat,
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
        f.Shown += (_, _) => AuroraTheme.Register(f);
        f.Font = new Font("Segoe UI", 9.5f); f.BackColor = Color.FromArgb(10, 14, 20); f.ForeColor = Color.White;
    }
    internal static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
}

internal sealed class QuickAddForm : Form
{
    private readonly PlainTextBox _paste = new() { Multiline = true, SingleLineText = false, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(22, 27, 34), ForeColor = Color.White, AcceptsReturn = true, MaxLength = 0 };
    private readonly DataGridView _grid = HostUi.Grid();
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(4, 8, 0, 4) };
    // Typeable: an unknown name creates that site when hosts are added.
    private readonly ComboBox _site = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, AccessibleName = "Site" };
    private readonly Label _siteStatus = new() { AutoSize = true, Padding = new Padding(4, 10, 8, 0) };
    private readonly Func<string, string?> _siteNameError;
    private readonly TextBox _group = new() { Width = 140, PlaceholderText = "Group / folder" };
    private readonly Func<List<ManagedHost>> _provider;
    private readonly Action<List<ManagedHost>, bool> _import;
    private readonly bool _temporary;
    private bool _updating;
    internal QuickAddForm(IEnumerable<string> sites, string? selected, Func<List<ManagedHost>> provider,
        Action<List<ManagedHost>, bool> import, bool temporary = false, Func<string, string?>? siteNameError = null)
    {
        _provider = provider; _import = import; _temporary = temporary;
        _siteNameError = siteNameError ?? (name => string.IsNullOrWhiteSpace(name) ? "Enter a site name first." : null);
        HostUi.Theme(this, temporary ? "Quick Monitor" : "Quick Add");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { AutoSize = true, Text = "Paste addresses, Address / Label / Group spreadsheet rows, IPv4 CIDR, or 192.168.1.10-25.\nEdit the preview before adding. CIDR excludes network/broadcast except /31 and /32. Maximum 4,096 rows.", Padding = new Padding(0, 0, 0, 10) }, 0, 0);
        root.Controls.Add(_paste, 0, 1);
        var toolbar = HostUi.Bar();
        toolbar.Controls.Add(HostUi.Button("Paste Clipboard", () =>
        {
            var read = ClipboardText.Read();
            if (read.Busy) _summary.Text = "Clipboard busy, try again";
            else if (read.Text is { } text) _paste.Text = ClipboardText.NormalizeMultiline(text);
        }));
        _paste.PasteBusy += () => _summary.Text = "Clipboard busy, try again";
        toolbar.Controls.Add(HostUi.Button("Import CSV", () =>
        {
            using var file = new OpenFileDialog { Filter = "CSV / text|*.csv;*.txt;*.tsv|All files|*.*" };
            if (file.ShowDialog(this) == DialogResult.OK)
                try { _paste.Text = File.ReadAllText(file.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed"); }
        }));
        toolbar.Controls.Add(HostUi.Button("Add Manually", () => { _grid.Rows.Add(true, "", "", "", _group.Text, "Enter an address"); }));
        foreach (var name in sites.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) _site.Items.Add(name);
        // Open on the site the person is looking at; All sites leaves the choice to them.
        if (selected is not null) _site.Text = _site.Items.Cast<string>().FirstOrDefault(n => n.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? "";
        ClipboardText.AttachPlainPaste(_site, () => ShowSiteStatus("Clipboard busy, try again", "statSuspect"));
        if (!temporary) { toolbar.Controls.Add(_site); toolbar.Controls.Add(_siteStatus); }
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
        _paste.TextChanged += (_, _) => Parse(); _site.SelectedIndexChanged += (_, _) => ValidateRows(); _site.TextChanged += (_, _) => ValidateRows();
        _grid.CellValueChanged += (_, _) => { if (!_updating) ValidateRows(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => e.ThrowException = false;
        ValidateRows();
        AllowDrop = true; DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                try { _paste.Text = File.ReadAllText(paths[0]); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        };
    }
    internal void PreviewForTest(string text) => _paste.Text = text;
    internal int PreviewRowCount => _grid.Rows.Count;
    internal string SiteTextForTest { get => _site.Text; set => _site.Text = value; }
    internal string SiteStatusForTest => _siteStatus.Text;
    internal void ImportForTest() => Import();

    // Existing names resolve to their saved spelling; anything else is a new site checked by the shared rules.
    private (string Name, bool IsNew, string? Error) ResolveSite()
    {
        string typed = ClipboardText.NormalizeLine(_site.Text);
        if (typed.Length == 0) return ("", false, "Choose or type a site");
        string? existing = _site.Items.Cast<string>().FirstOrDefault(n => n.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return (existing, false, null);
        return (typed, true, _siteNameError(typed));
    }

    private void ShowSiteStatus(string text, string? role)
    {
        // Tag keeps the color through later theme passes; set it now so typing doesn't re-theme the window.
        _siteStatus.Text = text; _siteStatus.Tag = role;
        var p = AuroraTheme.Palette;
        _siteStatus.ForeColor = role == "accentText" ? p.Accent : role == "statSuspect" ? p.StatusText(p.Warning) : p.Secondary;
    }
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
        var (siteName, siteIsNew, siteError) = ResolveSite();
        if (!_temporary)
            ShowSiteStatus(siteError ?? (siteIsNew ? $"Creates site \u201c{siteName}\u201d" : ""), siteError is null ? "accentText" : "statSuspect");
        int valid = 0, invalid = 0, duplicate = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string address = Cell(row, 1); string normal = HostImport.Normalize(address); string status;
            row.Cells[3].Value = _temporary ? "Quick Monitor (temporary)" : siteName;
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
        var (siteName, _, siteError) = ResolveSite();
        if (!_temporary && siteError is not null) { ShowSiteStatus(siteError, "statSuspect"); _site.Focus(); return; }
        var hosts = _grid.Rows.Cast<DataGridViewRow>().Where(r => Equals(r.Cells[0].Value, true) && Cell(r, 5) == "New")
            .Select(r => new ManagedHost(siteName, HostImport.Normalize(Cell(r, 1)), Cell(r, 2), Cell(r, 4), new HostOptions(), "Unknown")).ToList();
        if (hosts.Count == 0) { MessageBox.Show(this, "Select at least one valid new host. Duplicates and invalid rows are skipped."); return; }
        _import(hosts, _temporary); Close();
    }
}
