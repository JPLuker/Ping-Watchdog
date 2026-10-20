namespace PingWatchdog;

public sealed partial class MainForm
{
    private readonly TextBox _hostSearch = new() { PlaceholderText = "Search hosts", AccessibleName = "Search hosts", BorderStyle = BorderStyle.FixedSingle, Width = 220 };
    private readonly Label _emptyHosts = new() { Text = "No hosts", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = AuroraTheme.BodyFont };
    private readonly Label _emptyLog = new() { Text = "No ping activity", Dock = DockStyle.Fill, Padding = new Padding(12), Font = AuroraTheme.CaptionFont, Tag = "muted" };
    private readonly WallboardCanvas _monitorCanvas = new() { Dock = DockStyle.Fill, ShowCli = false, Embedded = true };
    private readonly SplitContainer _dashboardSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 8 };

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
        var manageSites = HostUi.Button("Manage sites", ShowSiteActions);
        manageSites.Dock = DockStyle.Fill;
        navigation.Controls.Add(manageSites, 0, 2);
        _siteList.ContextMenuStrip = _siteActionsMenu;
        _siteActionsMenu.Opening += (_, _) => PopulateSiteActions();
        _siteList.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) { int index = _siteList.IndexFromPoint(e.Location); if (index >= 0) _siteList.SelectedIndex = index; } };

        var utilities = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Tag = "nav" };
        _headerActionsPanel = utilities;
        utilities.Controls.Add(_wallboardButton);
        utilities.Controls.Add(_settingsButton);
        utilities.Controls.Add(_checkUpdateButton);
        utilities.Controls.Add(_moreButton);
        _moreButton.Text = "More";
        _moreButton.AccessibleName = "More actions";
        _moreButton.Padding = new Padding(16, 4, 16, 4);
        utilities.SizeChanged += (_, _) => SizeNavigationActions();
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
        toolbar.Controls.Add(new Label { Text = "Search", AutoSize = true, Font = AuroraTheme.LabelFont, Padding = new Padding(0, 12, 8, 0) });
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
        var output = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Tag = "card" };
        output.Controls.Add(_commandBox);
        output.Controls.Add(_emptyLog);
        _emptyLog.BringToFront();
        log.Controls.Add(output, 0, 1);
        _commandBox.TextChanged += (_, _) =>
        {
            _emptyLog.Visible = _commandBox.TextLength == 0;
            _clearLogButton.Enabled = _commandBox.TextLength > 0;
            if (_commandBox.TextLength == 0) _emptyLog.BringToFront();
        };
        _clearLogButton.Enabled = false;
        _mainSplit.Panel2.Controls.Add(log);
        _mainSplit.Orientation = Orientation.Vertical;
        _mainSplit.FixedPanel = FixedPanel.Panel2;
        _mainSplit.SizeChanged += (_, _) => SizePingLog();
        _monitorCanvas.HostRightClicked += (host, point) => ShowHostQuickActions(_monitorCanvas, point, host.Site, host.Address);
        _dashboardSplit.Panel1.Controls.Add(_monitorCanvas);
        _dashboardSplit.Panel2.Controls.Add(_mainSplit);
        _dashboardSplit.SizeChanged += (_, _) => SizeDashboard();
        right.Controls.Add(_dashboardSplit, 0, 3);
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
        SizeNavigationActions();
        _rightLayout.RowStyles[0].Height = Math.Max(64 * scale, _startButton.PreferredSize.Height + 16 * scale);
        _rightLayout.RowStyles[1].Height = 0;
        _settingsFlowPanel.PerformLayout();
        _rightLayout.RowStyles[2].Height = Math.Max(64 * scale, _settingsFlowPanel.Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max() + 8 * scale);
        SizeDashboard();
    }

    private void SizeDashboard()
    {
        if (_dashboardSplit.Height < 200) return;
        int height = _dashboardSplit.ClientSize.Height;
        _dashboardSplit.SplitterDistance = Math.Clamp((int)(height * .52), 100, Math.Max(100, height - 120));
    }

    private void SizeNavigationActions()
    {
        if (_headerActionsPanel is null) return;
        foreach (var button in _headerActionsPanel.Controls.OfType<Button>())
        {
            button.AutoSize = false;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Width = Math.Max(button.PreferredSize.Width, _headerActionsPanel.ClientSize.Width - button.Margin.Horizontal);
            if (button is AuroraButton navigation) navigation.Ghost = true;
        }
    }

    private void SizePingLog(bool reset = false)
    {
        if (_mainSplit.Panel2Collapsed || _mainSplit.Width < 300) return;
        int width = _mainSplit.ClientSize.Width;
        var orientation = width * 96f / DeviceDpi < 720 ? Orientation.Horizontal : Orientation.Vertical;
        if (_mainSplit.Orientation != orientation) _mainSplit.Orientation = orientation;
        if (orientation == Orientation.Horizontal)
        {
            int height = _mainSplit.ClientSize.Height;
            if (height < 120) return;
            int outputHeight = Math.Min((int)(96 * DeviceDpi / 96f), height / 3);
            _mainSplit.SplitterDistance = Math.Max(1, height - outputHeight - _mainSplit.SplitterWidth);
            return;
        }
        int logWidth = reset ? (int)(240 * DeviceDpi / 96f) : _mainSplit.Panel2.Width;
        logWidth = Math.Clamp(logWidth, Math.Min(160, width / 3), Math.Max(160, width / 3));
        _mainSplit.SplitterDistance = Math.Max(1, width - logWidth - _mainSplit.SplitterWidth);
    }
}
