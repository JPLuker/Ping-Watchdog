using PingWatchdog.Shared;

namespace PingWatchdog;

// The full-window, display-only Wallboard: hosts the canvas, keyboard shortcuts and host actions.
internal sealed partial class WallboardForm : Form
{
    private readonly Func<WallboardSnapshot> _snapshotProvider;
    private readonly Func<WallboardControlSnapshot> _controlProvider;
    private readonly WallboardActions _actions;
    private readonly WallboardCanvas _canvas = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly Action _cycleHistoryWindow;
    private readonly Action _toggleSuspectHistory;
    private readonly Icon _appIcon = BrandAssets.LoadIcon();

    private readonly Panel _drawer = new()
    {
        Dock = DockStyle.Right,
        Width = 420,
        Visible = false,
        Padding = new Padding(14),
        BackColor = WallboardStyle.Panel
    };

    private readonly ComboBox _siteCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 350
    };

    private readonly TextBox _hostEditor = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Width = 372,
        Height = 120,
        Font = new Font("Consolas", 9)
    };

    private readonly DataGridView _hostGrid = new()
    {
        Width = 372,
        Height = 190,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BorderStyle = BorderStyle.None
    };

    private readonly NumericUpDown _interval = Number(1, 300, 2, 1, 72);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250, 88);
    private readonly NumericUpDown _downAfter = Number(2, 20, 3, 1, 72);
    private readonly NumericUpDown _recoverAfter = Number(1, 20, 2, 1, 72);

    private readonly Button _monitorButton = ToolbarButton("Start Monitoring", 112);
    private readonly Button _operationsButton = ToolbarButton("Sites", 66);
    private readonly Button _hostsButton = ToolbarButton("Hosts", 66);
    private readonly Button _cliButton = ToolbarButton("CLI", 58);
    private readonly Button _historyButton = ToolbarButton("Outage history", 102);
    private readonly Button _settingsButton = ToolbarButton("Settings", 74);
    private readonly Button _updateButton = ToolbarButton("Check Updates", 112);
    private readonly Button _screenButton = ToolbarButton("Move to next display", 130);
    private readonly Button _mainButton = ToolbarButton("Exit Wallboard", 100);

    private readonly Button _applyHostsButton = DrawerButton("Apply Hosts");
    private readonly Button _addSiteButton = DrawerButton("Add site…");
    private readonly Button _renameSiteButton = DrawerButton("Rename");
    private readonly Button _deleteSiteButton = DrawerButton("Delete");
    private readonly Button _organizeButton = DrawerButton("Organize sites…");
    private readonly Button _editLabelButton = DrawerButton("Edit Label");
    private readonly Button _clearLabelButton = DrawerButton("Clear Label");
    private readonly Button _applyMonitoringButton = DrawerButton("Apply Monitoring Defaults");
    private readonly Button _exportButton = DrawerButton("Export configuration…");
    private readonly Button _importButton = DrawerButton("Import configuration…");
    private readonly Button _clearCliButton = DrawerButton("Clear ping log");
    private readonly Button _closeDrawerButton = DrawerButton("Close sites");

    private readonly Label _updateStatusLabel = new()
    {
        AutoSize = false,
        Width = 372,
        Height = 42,
        ForeColor = WallboardStyle.Muted
    };

    private int _screenIndex;
    private bool _showCli = true;
    private bool _loadingControls;
    private bool _hostEditorDirty;
    private string _hostGridSignature = string.Empty;

    public WallboardForm(
        Func<WallboardSnapshot> snapshotProvider,
        Func<WallboardControlSnapshot> controlProvider,
        WallboardActions actions,
        Screen targetScreen,
        Action cycleHistoryWindow,
        Action toggleSuspectHistory,
        bool showCliByDefault,
        Action<Control, Point, string, string>? hostQuickActions = null)
    {
        _snapshotProvider = snapshotProvider;
        _controlProvider = controlProvider;
        _actions = actions;
        _cycleHistoryWindow = cycleHistoryWindow;
        _toggleSuspectHistory = toggleSuspectHistory;
        _showCli = showCliByDefault;
        _canvas.HostRightClicked += (host, point) =>
            hostQuickActions?.Invoke(_canvas, point, host.Site, host.Address);
        _hostGrid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            _hostGrid.ClearSelection();
            var row = _hostGrid.Rows[e.RowIndex]; row.Selected = true;
            hostQuickActions?.Invoke(_hostGrid, _hostGrid.PointToClient(Cursor.Position),
                row.Cells["SiteColumn"].Value?.ToString() ?? "",
                row.Cells["HostColumn"].Value?.ToString() ?? "");
        };

        Text = "Ping Watchdog Wallboard";
        Icon = _appIcon;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = WallboardStyle.Background;
        ForeColor = Color.White;
        KeyPreview = true;
        ShowInTaskbar = true;

        var screens = Screen.AllScreens;
        _screenIndex = Array.FindIndex(
            screens,
            screen => screen.DeviceName.Equals(
                targetScreen.DeviceName,
                StringComparison.OrdinalIgnoreCase));

        if (_screenIndex < 0)
            _screenIndex = 0;

        Bounds = screens[_screenIndex].Bounds;

        BuildHostGrid();
        BuildShell();
        WireActions();
        ApplyControlTheme();

        KeyDown += OnWallboardKeyDown;
        _timer.Tick += (_, _) =>
        {
            RefreshSnapshot();
            RefreshControlState();
        };

        Shown += (_, _) =>
        {
            Bounds = Screen.AllScreens[_screenIndex].Bounds;
            _drawer.Width = Math.Clamp(ClientSize.Width / 3, 360, 440);
            RefreshSnapshot();
            RefreshControlState(force: true);
            _timer.Start();
            Activate();
        };

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _canvas.Dispose();
            _appIcon.Dispose();
            foreach (var button in new[] { _monitorButton, _operationsButton, _hostsButton, _cliButton,
                _historyButton, _settingsButton, _updateButton, _screenButton, _mainButton })
                button.Dispose();
        };
    }

    private static Button ToolbarButton(string text, int width)
    {
        return new WallboardButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 30),
            Padding = new Padding(10, 3, 10, 3),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(3, 5, 3, 4)
        };
    }

    private void BuildShell()
    {
        // One full-window content cell; no toolbar row or reserved toolbar height.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = WallboardStyle.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
        _canvas.Dock = DockStyle.Fill;
        _canvas.ShowCli = _showCli;
        BuildOperationsDrawer();
        content.Controls.Add(_canvas);
        content.Controls.Add(_drawer);
        _drawer.BringToFront();
        root.Controls.Add(content, 0, 0);
        Controls.Add(root);
    }

    // A priority alarm is the one thing allowed above the display-only canvas, and only while it rings.
    internal void AttachPriorityBanner(PriorityBanner banner)
    {
        var fill = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        Controls.Add(banner);
        fill?.BringToFront();
    }

    private void ApplyControlTheme()
    {
        var border = WallboardStyle.Line;
        var button = WallboardStyle.Panel;
        var text = WallboardStyle.Text;
        var input = WallboardStyle.Background;

        foreach (var control in new Button[]
        {
            _monitorButton,
            _operationsButton, _hostsButton,
            _cliButton,
            _historyButton,
            _settingsButton,
            _updateButton,
            _screenButton,
            _mainButton,
            _applyHostsButton,
            _addSiteButton,
            _renameSiteButton,
            _deleteSiteButton,
            _organizeButton,
            _editLabelButton,
            _clearLabelButton,
            _applyMonitoringButton,
            _exportButton,
            _importButton,
            _clearCliButton,
            _closeDrawerButton
        })
        {
            control.FlatStyle = FlatStyle.Flat;
            control.FlatAppearance.BorderColor = border;
            control.FlatAppearance.BorderSize = 1;
            control.BackColor = button;
            control.ForeColor = text;
        }

        _operationsButton.BackColor = WallboardStyle.Selection;
        _operationsButton.FlatAppearance.BorderColor = WallboardStyle.Accent;

        _siteCombo.BackColor = input;
        _siteCombo.ForeColor = text;
        _siteCombo.FlatStyle = FlatStyle.Flat;

        _hostEditor.BackColor = input;
        _hostEditor.ForeColor = WallboardStyle.Text;
        _hostEditor.BorderStyle = BorderStyle.FixedSingle;

        foreach (var numeric in new[] { _interval, _timeout, _downAfter, _recoverAfter })
        {
            numeric.BackColor = input;
            numeric.ForeColor = text;
        }

        _hostGrid.BackgroundColor = WallboardStyle.Background;
        _hostGrid.GridColor = WallboardStyle.Line;
        _hostGrid.ColumnHeadersDefaultCellStyle.BackColor = WallboardStyle.Panel;
        _hostGrid.ColumnHeadersDefaultCellStyle.ForeColor = WallboardStyle.Muted;
        _hostGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = WallboardStyle.Panel;
        _hostGrid.DefaultCellStyle.BackColor = WallboardStyle.Panel;
        _hostGrid.DefaultCellStyle.ForeColor = text;
        _hostGrid.DefaultCellStyle.SelectionBackColor = WallboardStyle.Selection;
        _hostGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _hostGrid.AlternatingRowsDefaultCellStyle.BackColor = WallboardStyle.Background;
    }

    private void OnWallboardKeyDown(object? sender, KeyEventArgs e)
    {
        if (_canvas.Focused && _canvas.HandleHostKey(e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode is Keys.Escape or Keys.F11)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Control && e.KeyCode == Keys.H)
        {
            e.Handled = true;
            _actions.OpenHistory();
        }
        else if (e.Control && e.KeyCode == Keys.Oemcomma)
        {
            e.Handled = true;
            _actions.OpenSettings();
        }
        else if (e.KeyCode == Keys.O)
        {
            e.Handled = true;
            ToggleDrawer();
        }
        else if (e.KeyCode == Keys.P)
        {
            e.Handled = true;
            if (_controlProvider().Monitoring)
                _actions.StopMonitoring();
            else
                _actions.StartMonitoring();
            RefreshControlState(force: true);
            RefreshSnapshot();
        }
        else if (e.KeyCode == Keys.M)
        {
            e.Handled = true;
            MoveToNextScreen();
        }
        else if (e.KeyCode == Keys.C)
        {
            e.Handled = true;
            ToggleCli();
        }
        else if (e.KeyCode == Keys.H)
        {
            e.Handled = true;
            _cycleHistoryWindow();
            RefreshSnapshot();
        }
        else if (e.KeyCode == Keys.S)
        {
            e.Handled = true;
            _toggleSuspectHistory();
            RefreshSnapshot();
        }
    }

    private void ToggleCli()
    {
        _showCli = !_showCli;
        _canvas.ShowCli = _showCli;
        _cliButton.Text = _showCli ? "Hide ping log" : "Show ping log";

        _canvas.Invalidate();
    }

    private void ToggleDrawer(bool forceClosed = false)
    {
        _drawer.Visible = forceClosed ? false : !_drawer.Visible;
        _operationsButton.Text = _drawer.Visible ? "Hide sites" : "Sites";

        if (_drawer.Visible)
        {
            _drawer.BringToFront();
            _hostEditorDirty = false;
            RefreshControlState(force: true);
        }

        _canvas.Invalidate();
    }

    private void RefreshSnapshot()
    {
        if (IsDisposed || Disposing)
            return;

        try
        {
            _canvas.Snapshot = _snapshotProvider();
            _canvas.Invalidate();
        }
        catch
        {
            // A failed refresh must not interrupt monitoring or Wallboard controls.
        }
    }

    private void MoveToNextScreen()
    {
        var screens = Screen.AllScreens;

        if (screens.Length == 0)
            return;

        _screenIndex = (_screenIndex + 1) % screens.Length;
        Bounds = screens[_screenIndex].Bounds;
        _drawer.Width = Math.Clamp(ClientSize.Width / 3, 360, 440);
        Activate();
    }
}
