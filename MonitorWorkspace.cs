namespace PingWatchdog;

public sealed partial class MainForm
{
    private readonly TextBox _hostSearch = new() { PlaceholderText = "Search hosts", AccessibleName = "Search hosts", BorderStyle = BorderStyle.FixedSingle, Width = 220 };
    private readonly Label _emptyHosts = new() { Text = "No hosts", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = AuroraTheme.BodyFont };

    private void BuildLayout()
    {
        // FOCAL: host results. Compact, opaque workspace; one primary monitoring action.
        SuspendLayout();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Tag = "window" };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rootLayout = root;
        _workspaceSplit.SplitterWidth = 1;
        _workspaceSplit.Panel1MinSize = 180;
        _workspaceSplit.Panel1.Padding = new Padding(12, 16, 12, 12);
        _workspaceSplit.Panel2.Padding = new Padding(24, 16, 24, 16);
        _workspaceSplit.Panel1.Tag = "nav";
        _workspaceSplit.Panel2.Tag = "window";

        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Tag = "nav" };
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        navigation.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var siteHeading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Tag = "nav" };
        siteHeading.Controls.Add(new Label { Text = "Sites", AutoSize = true, Font = AuroraTheme.LabelFont, Padding = new Padding(8, 12, 8, 0) });
        _addSiteButton.Text = "Add site";
        siteHeading.Controls.Add(_addSiteButton);
        navigation.Controls.Add(siteHeading, 0, 0);
        navigation.Controls.Add(_siteList, 0, 1);
        navigation.Controls.Add(HostUi.Button("Manage sites", ShowSiteActions), 0, 2);
        _siteList.ContextMenuStrip = _siteActionsMenu;
        _siteActionsMenu.Opening += (_, _) => PopulateSiteActions();
        _siteList.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) { int index = _siteList.IndexFromPoint(e.Location); if (index >= 0) _siteList.SelectedIndex = index; } };

        var utilities = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Tag = "nav" };
        _headerActionsPanel = utilities;
        utilities.Controls.Add(_wallboardButton);
        utilities.Controls.Add(_settingsButton);
        utilities.Controls.Add(_checkUpdateButton);
        utilities.Controls.Add(_moreButton);
        navigation.Controls.Add(utilities, 0, 3);
        _workspaceSplit.Panel1.Controls.Add(navigation);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Tag = "window" };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rightLayout = right;
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Tag = "window" };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _siteHeaderLabel.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
        _siteHeaderLabel.AutoSize = false;
        _siteHeaderLabel.AutoEllipsis = true;
        _siteHeaderLabel.Dock = DockStyle.Fill;
        _siteHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        _siteHeaderLabel.Tag = "primaryText";
        heading.Controls.Add(_siteHeaderLabel, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty, Tag = "window" };
        actions.Controls.Add(HostUi.Button("Add hosts", () => OpenQuickAdd(false)));
        if (_startButton is AuroraButton primary) primary.Primary = true;
        _startButton.Text = "Start monitoring";
        actions.Controls.Add(_startButton);
        heading.Controls.Add(actions, 1, 0);
        right.Controls.Add(heading, 0, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 8), Tag = "window" };
        _settingsFlowPanel = toolbar;
        _hostSearch.Font = AuroraTheme.BodyFont;
        _hostSearch.Margin = new Padding(0, 8, 8, 0);
        _hostSearch.TextChanged += (_, _) => RefreshGrid();
        toolbar.Controls.Add(_hostSearch);
        toolbar.Controls.Add(HostUi.Button("Edit hosts", OpenHostManager));
        toolbar.Controls.Add(HostUi.Button("Columns", ShowColumnMenu));
        toolbar.Controls.Add(_showCommandView);
        right.Controls.Add(toolbar, 0, 2);
        _mainSplit.Panel1.Controls.Add(_grid);
        _mainSplit.Panel1.Controls.Add(_emptyHosts);
        _emptyHosts.Visible = false;
        var log = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Tag = "window" };
        log.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        log.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logHeading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Tag = "window" };
        logHeading.Controls.Add(new Label { Text = "Ping activity", AutoSize = true, Font = AuroraTheme.LabelFont, Padding = new Padding(0, 12, 16, 0) });
        logHeading.Controls.Add(_clearLogButton);
        log.Controls.Add(logHeading, 0, 0);
        log.Controls.Add(_commandBox, 0, 1);
        _mainSplit.Panel2.Controls.Add(log);
        right.Controls.Add(_mainSplit, 0, 3);
        _workspaceSplit.Panel2.Controls.Add(right);
        _ipBox.Visible = false;
        Controls.Add(_ipBox);
        root.Controls.Add(_workspaceSplit, 0, 1);
        root.Controls.Add(_statusStrip, 0, 2);
        Controls.Add(root);
        ResumeLayout(true);
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_rootLayout is null || _rightLayout is null || _settingsFlowPanel is null || IsDisposed) return;
        float scale = DeviceDpi / 96f;
        _rootLayout.RowStyles[0].Height = 0;
        _checkUpdateButton.Visible = _showUpdateControlOnHome;
        _workspaceSplit.Panel1MinSize = (int)(180 * scale);
        try { _workspaceSplit.SplitterDistance = (int)((ClientSize.Width / scale < 1080 ? 192 : 224) * scale); }
        catch (InvalidOperationException) { }
        _siteList.ItemHeight = (int)(40 * scale);
        _rightLayout.RowStyles[0].Height = Math.Max(64 * scale, _startButton.PreferredSize.Height + 16 * scale);
        _rightLayout.RowStyles[1].Height = 0;
        _settingsFlowPanel.PerformLayout();
        _rightLayout.RowStyles[2].Height = Math.Max(64 * scale, _settingsFlowPanel.Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max() + 8 * scale);
    }
}
