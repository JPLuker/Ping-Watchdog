namespace PingWatchdog;

internal enum HostFilter { All, Priority, Offline, Disabled, Snoozed }

// The List view is where hosts are edited: filter chips, multi-select actions that save at once, and Undo.
public sealed partial class MainForm
{
    private HostFilter _hostFilter = HostFilter.All;
    // True while hosts are selected: the action bar replaces the filter chips.
    private bool _selectionMode;
    private readonly Dictionary<HostFilter, AuroraButton> _filterChips = new();
    private Panel? _listHeader;
    private FlowLayoutPanel? _filterBar;
    private FlowLayoutPanel? _selectionBar;
    private readonly Label _selectionCount = new() { AutoSize = true, Tag = "primaryText", Font = AuroraTheme.LabelFont, Padding = new Padding(4, 10, 12, 0) };
    private readonly AuroraButton _priorityAction = new() { AutoSize = true };
    private readonly AuroraButton _enableAction = new() { AutoSize = true };
    private readonly AuroraButton _keepAction = new() { Text = "Keep in a site…", AutoSize = true };
    private Panel? _undoBar;
    private readonly Label _undoMessage = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Tag = "primaryText", AutoEllipsis = true };
    private List<SiteDefinition>? _undoSites;
    private string? _undoSelectedSite;
    private readonly System.Windows.Forms.Timer _undoTimer = new() { Interval = 8000 };

    private static string FilterName(HostFilter filter) => filter switch
    {
        HostFilter.Priority => "Priority", HostFilter.Offline => "Offline",
        HostFilter.Disabled => "Disabled", HostFilter.Snoozed => "Snoozed", _ => "All"
    };

    private void BuildHostListEditing()
    {
        _listHeader = new Panel { Dock = DockStyle.Top, Tag = "window", Margin = Padding.Empty };
        _filterBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Tag = "window", Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4) };
        foreach (var filter in Enum.GetValues<HostFilter>())
        {
            var chip = new AuroraButton { Text = FilterName(filter), AutoSize = true, Ghost = true, Outline = true, AccessibleName = FilterName(filter) + " hosts" };
            chip.Click += (_, _) => SetHostFilter(filter);
            _filterChips[filter] = chip;
            _filterBar.Controls.Add(chip);
        }
        _selectionBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Visible = false, Tag = "window", Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4) };
        _priorityAction.Click += (_, _) => TogglePriorityForSelection();
        _enableAction.Click += (_, _) => ToggleEnabledForSelection();
        _keepAction.Click += (_, _) => KeepSelectionInSite();
        var move = new AuroraButton { Text = "Move to site…", AutoSize = true };
        move.Click += (_, _) => MoveSelectionToSite();
        var group = new AuroraButton { Text = "Set group…", AutoSize = true };
        group.Click += (_, _) => SetGroupForSelection();
        var policy = new AuroraButton { Text = "Monitoring…", AutoSize = true };
        policy.Click += (_, _) => SetMonitoringForSelection();
        var remove = new AuroraButton { Text = "Remove…", AutoSize = true, Destructive = true, Ghost = true };
        remove.Click += (_, _) => RemoveSelection();
        var clear = new AuroraButton { Text = "Clear selection", AutoSize = true, Ghost = true };
        clear.Click += (_, _) => _grid.ClearSelection();
        _selectionBar.Controls.AddRange(new Control[] { _selectionCount, _priorityAction, _enableAction, move, group, policy, _keepAction, remove, clear });
        _listHeader.Controls.Add(_selectionBar);
        _listHeader.Controls.Add(_filterBar);
        _listHeader.Resize += (_, _) => SizeHostListBars();

        _undoBar = new Panel { Dock = DockStyle.Bottom, Visible = false, Tag = "card", Margin = Padding.Empty };
        var undoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Tag = "card" };
        undoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        undoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var undo = new AuroraButton { Text = "Undo", AutoSize = true, Anchor = AnchorStyles.Right };
        undo.Click += (_, _) => UndoLastHostEdit();
        undoLayout.Controls.Add(_undoMessage, 0, 0);
        undoLayout.Controls.Add(undo, 1, 0);
        _undoBar.Controls.Add(undoLayout);
        _undoTimer.Tick += (_, _) => HideUndo();
        Disposed += (_, _) => _undoTimer.Dispose();

        _mainSplit.Panel1.Controls.Add(_listHeader);
        _mainSplit.Panel1.Controls.Add(_undoBar);
        // Fill docks last: the table takes what the header and Undo bar leave.
        _grid.BringToFront();
        _grid.MultiSelect = true;
        _grid.SelectionChanged += (_, _) => UpdateSelectionBar();
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && _grid.SelectedRows.Count > 0) { e.Handled = true; RemoveSelection(); }
            else if (e.KeyCode == Keys.F2 && _grid.SelectedRows.Count == 1) { e.Handled = true; RenameSelectedHost(); }
            else if (e.Control && e.KeyCode == Keys.Z && _undoSites is not null) { e.Handled = true; UndoLastHostEdit(); }
        };
        UpdateSelectionBar();
    }

    private void SizeHostListBars()
    {
        if (_listHeader is null || _filterBar is null || _selectionBar is null || _undoBar is null) return;
        var bar = _selectionMode ? _selectionBar : _filterBar;
        int width = Math.Max(1, _listHeader.ClientSize.Width);
        int height = bar.GetPreferredSize(new Size(width, 0)).Height;
        _listHeader.Height = Math.Max((int)(48 * DeviceDpi / 96f), height);
        _undoBar.Height = (int)(48 * DeviceDpi / 96f);
        _undoBar.Padding = new Padding((int)(16 * DeviceDpi / 96f), 4, 8, 4);
    }

    internal void SetHostFilter(HostFilter filter)
    {
        _hostFilter = filter;
        foreach (var (key, chip) in _filterChips) { chip.Selected = key == filter; chip.Invalidate(); }
        _grid.ClearSelection();
        RefreshGrid();
    }

    // Matches the Status cell text, so filter and display can never disagree.
    private static bool MatchesFilter(HostFilter filter, string status) => filter switch
    {
        HostFilter.Priority => status.Contains("· Priority", StringComparison.Ordinal),
        HostFilter.Offline => status.StartsWith("Offline", StringComparison.Ordinal),
        HostFilter.Disabled => status.StartsWith("Disabled", StringComparison.Ordinal),
        HostFilter.Snoozed => status.StartsWith("Snoozed", StringComparison.Ordinal),
        _ => true
    };

    private void UpdateFilterChips(IEnumerable<string> statuses)
    {
        var list = statuses.ToList();
        foreach (var (filter, chip) in _filterChips)
        {
            string text = filter == HostFilter.All ? "All" : $"{FilterName(filter)} {list.Count(s => MatchesFilter(filter, s))}";
            if (chip.Text != text) chip.Text = text;
            chip.Selected = filter == _hostFilter;
            chip.Invalidate();
        }
    }

    private string EmptyFilterText() => _hostFilter switch
    {
        HostFilter.Priority => "No priority hosts",
        HostFilter.Offline => "No offline hosts",
        HostFilter.Disabled => "No disabled hosts",
        HostFilter.Snoozed => "No snoozed hosts",
        _ => "No hosts — add hosts to start"
    };

    private List<ManagedHost> SelectedManagedHosts()
    {
        var keys = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => BuildHostKey(r.Cells["SiteColumn"].Value?.ToString() ?? "", r.Cells["HostColumn"].Value?.ToString() ?? ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return HostSnapshot().Where(h => keys.Contains(BuildHostKey(h.Site, h.Address))).ToList();
    }

    private void UpdateSelectionBar()
    {
        if (_selectionBar is null || _filterBar is null) return;
        var selected = SelectedManagedHosts();
        bool any = selected.Count > 0;
        _selectionCount.Text = selected.Count == 1 ? "1 host selected" : $"{selected.Count} hosts selected";
        _priorityAction.Text = any && selected.All(h => h.Options.Priority) ? "Remove priority" : "Mark as priority";
        _enableAction.Text = any && selected.All(h => h.Options.Enabled) ? "Disable" : "Enable";
        _keepAction.Visible = selected.Any(h => FindSite(h.Site)?.Temporary == true);
        if (_selectionMode != any)
        {
            _selectionMode = any;
            _selectionBar.Visible = any;
            _filterBar.Visible = !any;
        }
        SizeHostListBars();
    }

    private static string Hosts(int count) => count == 1 ? "1 host" : $"{count} hosts";

    // Every List edit saves immediately; the previous state is kept for one Undo.
    private void ApplyHostEdit(Func<string?> change)
    {
        var before = CloneSites();
        string? selectedBefore = _selectedSiteName;
        string? message;
        try { message = change(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Change not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (message is null) return;
        _priorityAlarm.Forget(a => !IsPriorityHost(a.Site, a.Address));
        _undoSites = before; _undoSelectedSite = selectedBefore;
        ShowUndo(message);
    }

    private List<SiteDefinition> CloneSites() => _sites.Select(s => new SiteDefinition
    {
        Name = s.Name, FolderPath = s.FolderPath, Temporary = s.Temporary,
        Hosts = s.Hosts.ToList(),
        Labels = new Dictionary<string, string>(s.Labels ?? new(), (s.Labels ?? new()).Comparer),
        Categories = new Dictionary<string, string>(s.Categories ?? new(), (s.Categories ?? new()).Comparer),
        HostDetails = (s.HostDetails ?? new()).ToDictionary(p => p.Key, p => p.Value.Copy(), StringComparer.OrdinalIgnoreCase)
    }).ToList();

    private void ShowUndo(string message)
    {
        if (_undoBar is null) return;
        _undoMessage.Text = message;
        _undoBar.Visible = true;
        _undoBar.BringToFront(); _grid.BringToFront();
        SizeHostListBars();
        _undoTimer.Stop(); _undoTimer.Start();
    }

    private void HideUndo()
    {
        _undoTimer.Stop();
        if (_undoBar is not null) _undoBar.Visible = false;
        _undoSites = null;
    }

    internal void UndoLastHostEdit()
    {
        if (_undoSites is null) return;
        var restore = _undoSites;
        _sites.Clear();
        _sites.AddRange(restore);
        if (_undoSelectedSite is null || FindSite(_undoSelectedSite) is not null) _selectedSiteName = _undoSelectedSite;
        HideUndo();
        CommitHostChanges();
        _priorityAlarm.Forget(a => !IsPriorityHost(a.Site, a.Address));
    }

    private void TogglePriorityForSelection()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count == 0) return;
        bool mark = !hosts.All(h => h.Options.Priority);
        ApplyHostEdit(() =>
        {
            SaveManagedHosts(hosts.Select(h => h with { Options = WithOptions(h.Options, o => o.Priority = mark) }).ToList());
            return mark ? $"Marked {Hosts(hosts.Count)} as priority" : $"Removed priority from {Hosts(hosts.Count)}";
        });
    }

    private void ToggleEnabledForSelection()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count == 0) return;
        bool enable = !hosts.All(h => h.Options.Enabled);
        ApplyHostEdit(() =>
        {
            SaveManagedHosts(hosts.Select(h => h with { Options = WithOptions(h.Options, o => o.Enabled = enable) }).ToList());
            return (enable ? "Enabled " : "Disabled ") + Hosts(hosts.Count);
        });
    }

    private void MoveSelectionToSite()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count == 0) return;
        var sites = _sites.Where(s => !s.Temporary).Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        string? current = hosts.Select(h => h.Site).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? hosts[0].Site : null;
        using var dialog = HostEditDialogs.SitePicker(sites, current, Hosts(hosts.Count));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Tag is not string destination) return;
        ApplyHostEdit(() =>
        {
            var moving = hosts.Where(h => !h.Site.Equals(destination, StringComparison.OrdinalIgnoreCase)).ToList();
            if (moving.Count == 0) return null;
            SaveManagedHosts(moving.Select(h => h with { Site = destination }).ToList());
            return $"Moved {Hosts(moving.Count)} to {destination}";
        });
    }

    private void SetGroupForSelection()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count == 0) return;
        string current = hosts.Select(h => h.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? hosts[0].Group : "";
        using var dialog = new CategoryDialog(hosts.Count == 1 ? hosts[0].Address : Hosts(hosts.Count), current);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string group = dialog.Category;
        ApplyHostEdit(() =>
        {
            SaveManagedHosts(hosts.Select(h => h with { Group = group }).ToList());
            return group.Length == 0 ? $"Cleared group on {Hosts(hosts.Count)}" : $"Set group “{group}” on {Hosts(hosts.Count)}";
        });
    }

    private void SetMonitoringForSelection()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count == 0) return;
        int Common(Func<HostOptions, int> value) => hosts.Select(h => value(h.Options)).Distinct().Count() == 1 ? value(hosts[0].Options) : 0;
        using var dialog = HostEditDialogs.MonitoringPolicy(Hosts(hosts.Count), Common(o => o.IntervalSeconds), Common(o => o.TimeoutMs), Common(o => o.FailureThreshold));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Tag is not ValueTuple<int, int, int> values) return;
        var (interval, timeout, failures) = values;
        ApplyHostEdit(() =>
        {
            SaveManagedHosts(hosts.Select(h => h with { Options = WithOptions(h.Options, o =>
            { o.IntervalSeconds = interval; o.TimeoutMs = timeout; o.FailureThreshold = failures; }) }).ToList());
            return $"Updated monitoring for {Hosts(hosts.Count)}";
        });
    }

    private void KeepSelectionInSite()
    {
        var hosts = SelectedManagedHosts();
        int temporary = hosts.Count(h => FindSite(h.Site)?.Temporary == true);
        if (temporary == 0) return;
        ApplyHostEdit(() =>
        {
            string? siteBefore = _selectedSiteName;
            SaveTemporaryHosts(hosts);
            return _selectedSiteName != siteBefore ? $"Kept {Hosts(temporary)} in {_selectedSiteName}" : null;
        });
    }

    private void RemoveSelection()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count == 0) return;
        string what = hosts.Count == 1 ? (string.IsNullOrWhiteSpace(hosts[0].Label) ? hosts[0].Address : hosts[0].Label) : Hosts(hosts.Count);
        if (MessageBox.Show(this, $"Remove {what} from monitoring?", "Remove hosts", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        ApplyHostEdit(() => { DeleteManagedHosts(hosts); return $"Removed {what}"; });
    }

    private void RenameSelectedHost()
    {
        var hosts = SelectedManagedHosts();
        if (hosts.Count != 1) return;
        var host = hosts[0];
        using var dialog = new NicknameDialog(host.Address, host.Label);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Nickname == host.Label) return;
        string label = dialog.Nickname;
        ApplyHostEdit(() =>
        {
            SaveManagedHosts(new() { host with { Label = label } });
            return label.Length == 0 ? $"Cleared the name for {host.Address}" : $"Named {host.Address} “{label}”";
        });
    }

    private void ShowSelectionMenu(Point location)
    {
        var menu = _hostQuickMenu;
        menu.Close();
        foreach (var item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        menu.ShowImageMargin = false; menu.ShowCheckMargin = false;
        UpdateSelectionBar();
        menu.Items.Add(new ToolStripMenuItem(_selectionCount.Text) { Enabled = false });
        menu.Items.Add(_priorityAction.Text, null, (_, _) => TogglePriorityForSelection());
        menu.Items.Add(_enableAction.Text, null, (_, _) => ToggleEnabledForSelection());
        menu.Items.Add("Move to site…", null, (_, _) => MoveSelectionToSite());
        menu.Items.Add("Set group…", null, (_, _) => SetGroupForSelection());
        menu.Items.Add("Monitoring…", null, (_, _) => SetMonitoringForSelection());
        if (_keepAction.Visible) menu.Items.Add("Keep in a site…", null, (_, _) => KeepSelectionInSite());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove…", null, (_, _) => RemoveSelection());
        AuroraTheme.ApplyMenu(menu);
        menu.Show(_grid, location);
    }

    private static HostOptions WithOptions(HostOptions value, Action<HostOptions> edit) { var copy = value.Copy(); edit(copy); return copy; }

    // Replaces the old Hosts window entry points: bring the main window forward on the List.
    private void ShowHostList()
    {
        RestoreFromTray();
        SetWorkspaceView(true);
        _grid.Focus();
    }

    // Test hooks.
    internal void SelectHostsForTest(params string[] addresses)
    {
        _grid.ClearSelection();
        foreach (DataGridViewRow row in _grid.Rows)
            if (addresses.Contains(row.Cells["HostColumn"].Value?.ToString(), StringComparer.OrdinalIgnoreCase)) row.Selected = true;
        UpdateSelectionBar();
    }
    internal int VisibleHostRows => _grid.Rows.Count;
    internal string PriorityActionText => _priorityAction.Text;
    internal bool SelectionBarShowing => _selectionMode;
    internal string FilterChipText(HostFilter filter) => _filterChips[filter].Text;
    internal string UndoMessageText => _undoSites is null ? "" : _undoMessage.Text;
    internal void TogglePriorityForTest() => TogglePriorityForSelection();
    internal void ToggleEnabledForTest() => ToggleEnabledForSelection();
}

// Small themed dialogs used by the List actions.
internal static class HostEditDialogs
{
    private static Form Shell(string title, Control content, int height)
    {
        var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(420, height),
            ShowInTaskbar = false
        };
        var ok = new AuroraButton { Text = "Save", AutoSize = true, Primary = true, DialogResult = DialogResult.OK };
        var cancel = new AuroraButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(12) };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        content.Dock = DockStyle.Fill;
        form.Controls.Add(content);
        form.Controls.Add(buttons);
        form.AcceptButton = ok; form.CancelButton = cancel;
        form.Shown += (_, _) => AuroraTheme.Register(form);
        return form;
    }

    // Result in Tag: the chosen site name.
    internal static Form SitePicker(IReadOnlyList<string> sites, string? current, string what)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, AccessibleName = "Destination site" };
        foreach (var site in sites) combo.Items.Add(site);
        var start = sites.FirstOrDefault(s => !s.Equals(current, StringComparison.OrdinalIgnoreCase));
        if (start is not null) combo.SelectedItem = start;
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        stack.Controls.Add(new Label { Text = $"Move {what} to", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) });
        stack.Controls.Add(combo);
        var form = Shell("Move to site", stack, 150);
        form.FormClosing += (_, e) => { if (form.DialogResult == DialogResult.OK) { if (combo.SelectedItem is string s) form.Tag = s; else e.Cancel = true; } };
        return form;
    }

    // Result in Tag: (interval s, timeout ms, failures); 0 uses the app default.
    internal static Form MonitoringPolicy(string what, int interval, int timeout, int failures)
    {
        NumericUpDown Number(int max, int value, string name) => new() { Maximum = max, Value = Math.Clamp(value, 0, max), Width = 120, AccessibleName = name };
        var intervalBox = Number(3600, interval, "Interval in seconds");
        var timeoutBox = Number(60000, timeout, "Timeout in milliseconds");
        timeoutBox.Increment = 100;
        var failuresBox = Number(100, failures, "Failures before offline");
        var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 4, Padding = new Padding(16) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(int index, string label, Control box)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, index);
            grid.Controls.Add(box, 1, index);
        }
        Row(0, "Interval (seconds)", intervalBox);
        Row(1, "Timeout (ms)", timeoutBox);
        Row(2, "Failures before offline", failuresBox);
        grid.Controls.Add(new Label { Text = $"Applies to {what}. 0 uses the default from Settings.", AutoSize = true, UseMnemonic = false, Tag = "muted", Margin = new Padding(0, 8, 0, 0) }, 0, 3);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 3)!, 2);
        var form = Shell("Monitoring", grid, 230);
        form.FormClosing += (_, _) => { if (form.DialogResult == DialogResult.OK) form.Tag = ((int)intervalBox.Value, (int)timeoutBox.Value, (int)failuresBox.Value); };
        return form;
    }
}
