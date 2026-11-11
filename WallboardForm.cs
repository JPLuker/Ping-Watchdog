using PingWatchdog.Shared;

namespace PingWatchdog;

// The full-window, display-only Wallboard: hosts the canvas, keyboard shortcuts and host actions.
internal sealed class WallboardForm : Form
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

    private static NumericUpDown Number(
        decimal minimum,
        decimal maximum,
        decimal value,
        decimal increment,
        int width)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Increment = increment,
            Width = width,
            BorderStyle = BorderStyle.FixedSingle
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

    private static Button DrawerButton(string text)
    {
        return new WallboardButton
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 2, 6, 2)
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

    private void BuildOperationsDrawer()
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        stack.Controls.Add(SectionTitle("Sites"));

        stack.Controls.Add(SectionTitle("Site"));
        stack.Controls.Add(_siteCombo);

        var siteButtons = Row(_addSiteButton, _renameSiteButton, _deleteSiteButton);
        stack.Controls.Add(siteButtons);
        stack.Controls.Add(_organizeButton);

        stack.Controls.Add(SectionTitle("Hosts"));
        stack.Controls.Add(_hostGrid);
        stack.Controls.Add(SectionNote("Right-click a host for actions."));
        var monitoringSettings = DrawerButton("Monitoring settings…");
        monitoringSettings.Click += (_, _) => _actions.OpenSettings();
        stack.Controls.Add(monitoringSettings);

        stack.Controls.Add(SectionTitle("Configuration"));
        stack.Controls.Add(Row(_exportButton, _importButton, _clearCliButton));

        stack.Controls.Add(SectionTitle("Update status"));
        stack.Controls.Add(_updateStatusLabel);

        _closeDrawerButton.Width = 160;
        stack.Controls.Add(_closeDrawerButton);

        _drawer.Controls.Add(stack);
    }

    private static Label SectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Width = 372,
            Font = new Font("Segoe UI Semibold", 10),
            ForeColor = Color.FromArgb(225, 235, 245),
            Margin = new Padding(0, 12, 0, 5)
        };
    }

    private static Label SectionNote(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(372, 0),
            ForeColor = WallboardStyle.Muted,
            Margin = new Padding(0, 0, 0, 7)
        };
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            Width = 372,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 2, 0, 4)
        };
        foreach (var control in controls)
            row.Controls.Add(control);
        return row;
    }

    private static FlowLayoutPanel MonitorRow(
        string label,
        Control input,
        string suffix)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            Width = 372,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 1, 0, 2)
        };
        row.Controls.Add(new Label
        {
            Text = label,
            Width = 115,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(185, 199, 214)
        });
        input.Margin = new Padding(0, 3, 7, 0);
        row.Controls.Add(input);
        row.Controls.Add(new Label
        {
            Text = suffix,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
            ForeColor = WallboardStyle.Muted
        });
        return row;
    }

    private void BuildHostGrid()
    {
        _hostGrid.RowTemplate.Height = Presentation.CompactTableRowHeight;
        _hostGrid.ColumnHeadersHeight = Presentation.CompactTableHeaderHeight;
        _hostGrid.EnableHeadersVisualStyles = false;
        foreach (var column in Presentation.WallboardHostColumns)
            _hostGrid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = column.Key + "Column", HeaderText = column.Header, Width = column.Width,
                DataPropertyName = column.Key == "Host" ? "Address" : column.Key == "Status" ? "State" : column.Key == "Latency" ? "LatencyMs" : column.Key
            });
    }

    private void WireActions()
    {
        _monitorButton.Click += (_, _) =>
        {
            if (_controlProvider().Monitoring)
                _actions.StopMonitoring();
            else
                _actions.StartMonitoring();

            RefreshControlState(force: true);
            RefreshSnapshot();
        };

        _hostsButton.Click += (_, _) => _actions.OpenHosts?.Invoke();
        _operationsButton.Click += (_, _) => ToggleDrawer();
        _organizeButton.Click += (_, _) => _actions.OpenOrganization();
        _cliButton.Click += (_, _) => ToggleCli();
        _historyButton.Click += (_, _) => _actions.OpenHistory();
        _settingsButton.Click += (_, _) => _actions.OpenSettings();
        _updateButton.Click += async (_, _) =>
        {
            _updateButton.Enabled = false;
            try { await _actions.RunUpdateAction(); }
            finally
            {
                if (!IsDisposed && !Disposing)
                    _updateButton.Enabled = true;
                RefreshControlState(force: true);
            }
        };
        _screenButton.Click += (_, _) => MoveToNextScreen();
        _mainButton.Click += (_, _) => Close();

        _siteCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingControls || _siteCombo.SelectedIndex < 0)
                return;

            string? site = _siteCombo.SelectedIndex == 0
                ? null
                : _siteCombo.SelectedItem?.ToString();

            _hostEditorDirty = false;
            _actions.SelectSite(site);
            RefreshControlState(force: true);
            RefreshSnapshot();
        };

        _hostEditor.TextChanged += (_, _) =>
        {
            if (!_loadingControls)
                _hostEditorDirty = true;
        };

        _applyHostsButton.Click += (_, _) =>
        {
            var state = _controlProvider();
            if (state.SelectedSite is null)
                return;

            string? error = _actions.SaveHosts(state.SelectedSite, _hostEditor.Text);
            ShowOperationError(error);
            if (error is null)
            {
                _hostEditorDirty = false;
                RefreshControlState(force: true);
                RefreshSnapshot();
            }
        };

        _addSiteButton.Click += (_, _) =>
        {
            using var dialog = new SiteNameDialog("Add Site / Group", "Add Site");
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            string? error = _actions.AddSite(dialog.SiteName);
            ShowOperationError(error);
            if (error is null)
            {
                _hostEditorDirty = false;
                RefreshControlState(force: true);
                RefreshSnapshot();
            }
        };

        _renameSiteButton.Click += (_, _) =>
        {
            var state = _controlProvider();
            if (state.SelectedSite is null)
                return;

            using var dialog = new SiteNameDialog(
                "Rename Site / Group",
                "Rename",
                state.SelectedSite);

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            string? error = _actions.RenameSite(state.SelectedSite, dialog.SiteName);
            ShowOperationError(error);
            if (error is null)
            {
                _hostEditorDirty = false;
                RefreshControlState(force: true);
                RefreshSnapshot();
            }
        };

        _deleteSiteButton.Click += (_, _) =>
        {
            var state = _controlProvider();
            if (state.SelectedSite is null)
                return;

            var answer = MessageBox.Show(
                this,
                $"Delete site \"{state.SelectedSite}\" and its saved hosts?",
                "Ping Watchdog",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
                return;

            string? error = _actions.DeleteSite(state.SelectedSite);
            ShowOperationError(error);
            if (error is null)
            {
                _hostEditorDirty = false;
                RefreshControlState(force: true);
                RefreshSnapshot();
            }
        };

        _editLabelButton.Click += (_, _) => EditSelectedLabel(clear: false);
        _clearLabelButton.Click += (_, _) => EditSelectedLabel(clear: true);

        _applyMonitoringButton.Click += (_, _) =>
        {
            string? error = _actions.ApplyMonitoringSettings(
                (int)_interval.Value,
                (int)_timeout.Value,
                (int)_downAfter.Value,
                (int)_recoverAfter.Value);
            ShowOperationError(error);
            RefreshControlState(force: true);
        };

        _exportButton.Click += (_, _) => _actions.SaveConfig();
        _importButton.Click += (_, _) =>
        {
            _actions.LoadConfig();
            _hostEditorDirty = false;
            RefreshControlState(force: true);
            RefreshSnapshot();
        };
        _clearCliButton.Click += (_, _) =>
        {
            _actions.ClearCommandLog();
            RefreshSnapshot();
        };
        _closeDrawerButton.Click += (_, _) => ToggleDrawer(forceClosed: true);
    }

    private void EditSelectedLabel(bool clear)
    {
        if (_hostGrid.SelectedRows.Count == 0)
            return;

        var row = _hostGrid.SelectedRows[0];
        string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
        string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;
        string current = row.Cells["LabelColumn"].Value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(host))
            return;

        string label = string.Empty;

        if (!clear)
        {
            using var dialog = new NicknameDialog(host, current);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            label = dialog.Nickname;
        }

        string? error = _actions.SetLabel(site, host, label);
        ShowOperationError(error);
        if (error is null)
            RefreshControlState(force: true);
    }

    private void ShowOperationError(string? error)
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

    private void RefreshControlState(bool force = false)
    {
        if (IsDisposed || Disposing)
            return;

        WallboardControlSnapshot state;
        try
        {
            state = _controlProvider();
        }
        catch
        {
            return;
        }

        _loadingControls = true;
        try
        {
            ((ThemeButton)_monitorButton).Primary = true;
            _monitorButton.Text = state.Monitoring ? "Stop Monitoring" : "Start Monitoring";
            _monitorButton.BackColor = state.Monitoring
                ? WallboardStyle.Selection
                : WallboardStyle.Primary;
            _monitorButton.FlatAppearance.BorderColor = state.Monitoring
                ? WallboardStyle.Red
                : WallboardStyle.Accent;

            _cliButton.Text = _showCli ? "Hide ping log" : "Show ping log";
    

            _updateButton.Text = state.UpdateActionText;
            _updateButton.Visible = true;
            _screenButton.Visible = Screen.AllScreens.Length > 1;
            _updateStatusLabel.Text = $"{state.Version}\r\n{state.UpdateStatus}";

            RefreshSiteCombo(state);
            RefreshHostGrid(state, force);

            bool specificSite = state.SelectedSite is not null;
            _renameSiteButton.Enabled = specificSite;
            _deleteSiteButton.Enabled = specificSite;
            _applyHostsButton.Enabled = specificSite;
            _hostEditor.ReadOnly = !specificSite;

            if ((force || !_hostEditorDirty) && !_hostEditor.Focused)
            {
                _hostEditor.Text = specificSite
                    ? string.Join(Environment.NewLine, state.ConfiguredHosts.Select(host => host.Address))
                    : string.Join(
                        Environment.NewLine,
                        state.ConfiguredHosts.Select(host => $"[{host.Site}] {host.Address}"));
                _hostEditorDirty = false;
            }

            bool settingsEditable = !state.Monitoring;
            foreach (var numeric in new Control[] { _interval, _timeout, _downAfter, _recoverAfter })
                numeric.Enabled = settingsEditable;
            _applyMonitoringButton.Enabled = settingsEditable;

            if (settingsEditable)
            {
                _interval.Value = Math.Clamp(state.PingIntervalSeconds, (int)_interval.Minimum, (int)_interval.Maximum);
                _timeout.Value = Math.Clamp(state.PingTimeoutMs, (int)_timeout.Minimum, (int)_timeout.Maximum);
                _downAfter.Value = Math.Clamp(state.FailureThreshold, (int)_downAfter.Minimum, (int)_downAfter.Maximum);
                _recoverAfter.Value = Math.Clamp(state.RecoveryThreshold, (int)_recoverAfter.Minimum, (int)_recoverAfter.Maximum);
            }
        }
        finally
        {
            _loadingControls = false;
        }
    }

    private void RefreshSiteCombo(WallboardControlSnapshot state)
    {
        var expected = new[] { "All Sites" }.Concat(state.Sites).ToArray();
        bool rebuild = _siteCombo.Items.Count != expected.Length;

        if (!rebuild)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(
                    _siteCombo.Items[i]?.ToString(),
                    expected[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    rebuild = true;
                    break;
                }
            }
        }

        if (rebuild)
        {
            _siteCombo.Items.Clear();
            _siteCombo.Items.AddRange(expected);
        }

        int selectedIndex = state.SelectedSite is null
            ? 0
            : Array.FindIndex(
                expected,
                item => item.Equals(state.SelectedSite, StringComparison.OrdinalIgnoreCase));

        if (selectedIndex < 0)
            selectedIndex = 0;

        if (_siteCombo.SelectedIndex != selectedIndex)
            _siteCombo.SelectedIndex = selectedIndex;
    }

    private void RefreshHostGrid(WallboardControlSnapshot state, bool force)
    {
        string signature = string.Join(
            "|",
            state.Hosts.Select(host =>
                $"{host.Site}\u001f{host.Address}\u001f{host.Label}\u001f{host.State}\u001f{host.LatencyMs}\u001f{host.Failures}"));

        if (!force && signature == _hostGridSignature)
            return;

        string? selectedHost = _hostGrid.SelectedRows.Count > 0
            ? _hostGrid.SelectedRows[0].Cells["HostColumn"].Value?.ToString()
            : null;
        string? selectedSite = _hostGrid.SelectedRows.Count > 0
            ? _hostGrid.SelectedRows[0].Cells["SiteColumn"].Value?.ToString()
            : null;

        _hostGridSignature = signature;
        _hostGrid.DataSource = state.Hosts.ToList();

        foreach (DataGridViewRow row in _hostGrid.Rows)
        {
            string status = row.Cells["StatusColumn"].Value?.ToString() ?? string.Empty;
            row.Cells["StatusColumn"].Style.ForeColor = status switch
            {
                "Online" => WallboardStyle.Green,
                "Suspect" => WallboardStyle.Yellow,
                "Offline" => WallboardStyle.Red,
                _ => WallboardStyle.Muted
            };

            if (selectedHost is not null &&
                selectedSite is not null &&
                string.Equals(row.Cells["HostColumn"].Value?.ToString(), selectedHost, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Cells["SiteColumn"].Value?.ToString(), selectedSite, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
            }
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
