namespace PingWatchdog;

public sealed partial class MainForm
{
    private readonly TextBox _hostSearch = new() { PlaceholderText = "Search hosts", AccessibleName = "Search hosts", BorderStyle = BorderStyle.FixedSingle, Width = 220 };
    private readonly Label _emptyHosts = new() { Text = "No hosts", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = AuroraTheme.BodyFont };
    private readonly Label _emptyLog = new() { Text = "No ping activity", Dock = DockStyle.Fill, Padding = new Padding(12), Font = AuroraTheme.CaptionFont, Tag = "muted" };
    private readonly WallboardCanvas _monitorCanvas = new() { Dock = DockStyle.Fill, ShowCli = false, Embedded = true };
    private readonly SplitContainer _dashboardSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 8 };

    private bool _listView;
    private readonly Label _searchLabel = new() { Text = "Search", AutoSize = true, Font = AuroraTheme.LabelFont, Padding = new Padding(0, 12, 8, 0) };
    private readonly Button _columnPickerButton = new AuroraButton { Text = "Columns", AutoSize = true };
    private readonly Button _mapTab = new AuroraButton { Text = "Map", AutoSize = true };
    private readonly Button _listTab = new AuroraButton { Text = "List", AutoSize = true };
    private AuroraWell? _siteWell;
    private Panel? _sectionDivider;
    private readonly Button _outageHistoryButton = new AuroraButton { Text = "Outage history", AutoSize = true };

    private void BuildLayout()
    {
        // FOCAL: host results. Compact, opaque workspace; one primary monitoring action.
        SuspendLayout();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Tag = "window" };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _rootLayout = root;
        _workspaceSplit.SplitterWidth = 1;
        _workspaceSplit.Panel1MinSize = 180;
        _workspaceSplit.Panel1.Padding = new Padding(12, 16, 12, 12);
        _workspaceSplit.Panel2.Padding = new Padding(24, 16, 24, 16);
        _workspaceSplit.Panel1.Tag = "nav";
        _workspaceSplit.Panel2.Tag = "window";

        // Sites heading, pinned All sites, the scrolling site well, a divider, then app actions.
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, Tag = "nav" };
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        navigation.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigation.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        navigation.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var siteHeading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Tag = "nav" };
        siteHeading.Controls.Add(new Label { Text = "Sites", AutoSize = true, Font = AuroraTheme.LabelFont, Padding = new Padding(8, 12, 8, 0) });
        _addSiteButton.Text = "Add site";
        if (_addSiteButton is AuroraButton add) add.Ghost = true;
        siteHeading.Controls.Add(_addSiteButton);
        navigation.Controls.Add(siteHeading, 0, 0);
        _allSitesButton.Dock = DockStyle.Fill;
        _allSitesButton.Margin = new Padding(0, 0, 0, 8);
        _allSitesButton.Click += (_, _) => { if (_selectedSiteName is not null) SelectSiteScope(null); };
        navigation.Controls.Add(_allSitesButton, 0, 1);
        _siteWell = new AuroraWell { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(4, 6, 4, 6), Tag = "well" };
        _siteList.Tag = "well";
        _siteWell.Controls.Add(_siteList);
        navigation.Controls.Add(_siteWell, 0, 2);
        _sectionDivider = new Panel { Height = 1, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(8, 12, 8, 8), Tag = "divider" };
        navigation.Controls.Add(_sectionDivider, 0, 3);
        _siteList.KeyDown += (_, e) =>
        {
            // Up from the first site reaches the pinned All sites scope.
            if (e.KeyCode == Keys.Up && _siteList.SelectedIndex <= 0)
            {
                e.Handled = true;
                _allSitesButton.Focus();
                if (_selectedSiteName is not null) SelectSiteScope(null);
            }
        };
        _allSitesButton.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _siteList.Items.Count > 0)
            {
                e.Handled = true;
                _siteList.Focus();
                _siteList.SelectedIndex = Math.Max(0, _siteList.SelectedIndex);
            }
        };
        _outageHistoryButton.Click += (_, _) => OpenEventHistory();
        _siteList.ContextMenuStrip = _siteActionsMenu;
        _siteActionsMenu.Opening += (_, _) => PopulateSiteActions();
        _siteList.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) { int index = _siteList.IndexFromPoint(e.Location); if (index >= 0) _siteList.SelectedIndex = index; } };
        _siteList.MouseMove += (_, e) => UpdateSiteListTip(_siteList.IndexFromPoint(e.Location));
        _siteList.MouseLeave += (_, _) => UpdateSiteListTip(-1);

        var utilities = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Tag = "nav" };
        _headerActionsPanel = utilities;
        utilities.Controls.Add(_outageHistoryButton);
        utilities.Controls.Add(_settingsButton);
        utilities.Controls.Add(_checkUpdateButton);
        utilities.Controls.Add(_moreButton);
        _moreButton.Text = "More";
        _moreButton.AccessibleName = "More actions";
        _moreButton.Padding = new Padding(16, 4, 16, 4);
        utilities.SizeChanged += (_, _) => SizeNavigationActions();
        navigation.Controls.Add(utilities, 0, 4);
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
        actions.Controls.Add(_wallboardButton);
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
        _mapTab.Click += (_, _) => SetWorkspaceView(false);
        _listTab.Click += (_, _) => SetWorkspaceView(true);
        toolbar.Controls.Add(_mapTab);
        toolbar.Controls.Add(_listTab);
        toolbar.Controls.Add(_searchLabel);
        toolbar.Controls.Add(_hostSearch);
        toolbar.Controls.Add(HostUi.Button("Edit hosts", OpenHostManager));
        _columnPickerButton.Click += (_, _) => ShowColumnMenu();
        toolbar.Controls.Add(_columnPickerButton);
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
        _statusStrip.Visible = false;
        Controls.Add(root);
        InitializeUpdateToast();
        SetWorkspaceView(false);
        ResumeLayout(true);
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_rootLayout is null || _rightLayout is null || _settingsFlowPanel is null || IsDisposed) return;
        float scale = DeviceDpi / 96f;
        _rootLayout.RowStyles[0].Height = 0;
        _checkUpdateButton.Visible = true;
        _checkUpdateButton.Text = "Check for updates";
        _workspaceSplit.Panel1MinSize = (int)(224 * scale);
        try { _workspaceSplit.SplitterDistance = (int)((ClientSize.Width / scale < 1080 ? 224 : 240) * scale); }
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
        if (_dashboardSplit.Height < 200 || _dashboardSplit.Panel1Collapsed || _dashboardSplit.Panel2Collapsed) return;
        int height = _dashboardSplit.ClientSize.Height;
        int logHeight = Math.Min((int)(144 * DeviceDpi / 96f), height / 4);
        _dashboardSplit.SplitterDistance = Math.Max(1, height - logHeight - _dashboardSplit.SplitterWidth);
    }

    private readonly ToolTip _siteListTip = new();
    private int _siteTipIndex = -1;

    // The row text area matches DrawSiteItem: 4px inset each side, 12px lead, 6px trail.
    internal bool IsSiteNameTruncated(int index)
    {
        if (index < 0 || index >= _siteList.Items.Count) return false;
        int available = Math.Max(1, _siteList.ClientSize.Width - 8 - 18);
        string text = _siteList.Items[index]?.ToString() ?? string.Empty;
        return TextRenderer.MeasureText(text, _siteItemFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Width > available;
    }

    // Full site names on hover when the row shows an ellipsis.
    private void UpdateSiteListTip(int index)
    {
        if (index == _siteTipIndex) return;
        _siteTipIndex = index;
        _siteListTip.SetToolTip(_siteList, IsSiteNameTruncated(index) ? _siteList.Items[index]?.ToString() : null);
    }

    private void SizeNavigationActions()
    {
        _allSitesButton.Height = (int)(40 * DeviceDpi / 96f);
        _allSitesButton.Padding = new Padding(12, 4, 12, 4);
        if (_headerActionsPanel is null) return;
        foreach (var button in _headerActionsPanel.Controls.OfType<Button>())
        {
            button.AutoSize = false;
            button.Margin = Padding.Empty;
            button.Height = (int)(40 * DeviceDpi / 96f);
            button.Padding = new Padding(12, 4, 12, 4);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Width = Math.Max(button.PreferredSize.Width, _headerActionsPanel.ClientSize.Width - button.Margin.Horizontal);
            if (button is AuroraButton navigation) navigation.Ghost = true;
        }
    }

    private void SizePingLog(bool reset = false)
    {
        if (!_listView || _mainSplit.Panel2Collapsed || _mainSplit.Width < 300) return;
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
    private void SetWorkspaceView(bool list)
    {
        _listView = list;
        _searchLabel.Visible = list; _hostSearch.Visible = list; _columnPickerButton.Visible = list;
        _dashboardSplit.Panel2Collapsed = false;
        _dashboardSplit.Panel1Collapsed = false;
        _mainSplit.Panel2Collapsed = false;
        _mainSplit.Panel1Collapsed = false;
        if (list)
        {
            _dashboardSplit.Panel1Collapsed = true;
            _mainSplit.Panel2Collapsed = !_showCommandView.Checked;
            SizePingLog(reset: true);
        }
        else
        {
            _mainSplit.Panel1Collapsed = true;
            _dashboardSplit.Panel2Collapsed = !_showCommandView.Checked;
            SizeDashboard();
        }
        ((AuroraButton)_mapTab).Selected = !list;
        ((AuroraButton)_listTab).Selected = list;
        _mapTab.Invalidate(); _listTab.Invalidate();
        _monitorCanvas.Invalidate();
    }

}
