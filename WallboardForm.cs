using PingWatchdog.Shared;

namespace PingWatchdog;

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
        // Wallboard is a display surface. Actions remain on the keyboard and host context menus.
        _canvas.Dock = DockStyle.Fill;
        _canvas.ShowCli = _showCli;
        BuildOperationsDrawer();
        Controls.Add(_canvas);
        Controls.Add(_drawer);
        _drawer.BringToFront();
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
        stack.Controls.Add(SectionNote("Right-click a host for quick actions. Exit Wallboard to add or edit hosts."));
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
            Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
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
            ((AuroraButton)_monitorButton).Primary = true;
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

internal sealed partial class WallboardCanvas : Control
{
    private readonly List<Rectangle> _captionObstacles = new();
    private int _hiddenCaptions;
    private readonly Bitmap _brandBitmap = BrandAssets.LoadLogo();
    private readonly Font _brandFont = new("Segoe UI Semibold", 22, FontStyle.Bold);
    private readonly Font _subtitleFont = new("Segoe UI Semibold", 11, FontStyle.Bold);
    private readonly Font _clockFont = new("Segoe UI Semibold", 22, FontStyle.Bold);
    private readonly Font _dateFont = new("Segoe UI", 9.75f);
    private readonly Font _statValueFont = new("Segoe UI Semibold", 25, FontStyle.Bold);
    private readonly Font _statLabelFont = new("Segoe UI", 10);
    private readonly Font _sectionFont = new("Segoe UI Semibold", 11, FontStyle.Bold);
    private readonly Font _siteFont = new("Segoe UI Semibold", 9.75f, FontStyle.Bold);
    private readonly Font _siteSmallFont = new("Segoe UI Semibold", 9.75f, FontStyle.Bold);
    private readonly Font _siteTinyFont = new("Segoe UI Semibold", 9.75f, FontStyle.Bold);
    private readonly Font _hostLabelFont = new("Segoe UI Semibold", 9.75f, FontStyle.Bold);
    private readonly Font _hostIpFont = new("Consolas", 9.75f);
    private readonly Font _smallFont = new("Segoe UI", 9.75f);
    private readonly Font _tinyFont = new("Segoe UI", 9.75f);
    private readonly Font _coreFont = new("Segoe UI Semibold", 11, FontStyle.Bold);
    private readonly Font _cliFont = new("Consolas", 9.75f);

    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _hostHitRegions = new();
    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _sidebarHitRegions = new();
    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _hostCaptionRegions = new();
    internal bool HasHostCaption(string site, string address) =>
        _hostCaptionRegions.Any(hit => hit.Host.Site == site && hit.Host.Address == address);
    internal bool CaptionsOverlap() => _hostCaptionRegions.SelectMany((a, i) =>
        _hostCaptionRegions.Skip(i + 1).Select(b => a.Bounds.IntersectsWith(b.Bounds))).Any(overlap => overlap);
    private Rectangle _topologyViewport;
    private Size _topologyVirtualSize;
    internal Size TopologyContentSize => _topologyVirtualSize;
    private Point _panOffset;
    private Point? _dragOrigin;
    private Point _dragPan;
    private Rectangle _resultsViewport;
    private int _resultScrollOffset;

    internal void PanTopology(int dx, int dy)
    {
        _panOffset = new Point(
            Math.Clamp(_panOffset.X + dx, 0, Math.Max(0, _topologyVirtualSize.Width - _topologyViewport.Width)),
            Math.Clamp(_panOffset.Y + dy, 0, Math.Max(0, _topologyVirtualSize.Height - _topologyViewport.Height)));
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Right) SelectHostAt(e.Location);
        if (e.Button == MouseButtons.Left && _topologyViewport.Contains(e.Location))
        {
            Focus(); _dragOrigin = e.Location; _dragPan = _panOffset; Capture = true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = HitTestHost(e.Location);
        string tip = hovered == null ? "" : hovered.Site + " · " + hovered.Address + (string.IsNullOrWhiteSpace(hovered.Label) ? "" : " · " + hovered.Label) + " · " + hovered.State;
        if (tip != _lastHostTip) { _lastHostTip = tip; _hostTip.SetToolTip(this, tip); }
        if (_dragOrigin is Point origin)
        {
            _panOffset = _dragPan;
            PanTopology(origin.X - e.X, origin.Y - e.Y);
        }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) _dragOrigin = null;
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_resultsViewport.Contains(e.Location))
        {
            int count = Snapshot?.Sites.Sum(site => site.Hosts.Count) ?? 0;
            _resultScrollOffset = Math.Clamp(_resultScrollOffset - Math.Sign(e.Delta) * 3, 0, Math.Max(0, count - 1));
            Invalidate();
            return;
        }
        if ((ModifierKeys & Keys.Shift) != 0) PanTopology(-e.Delta, 0);
        else PanTopology(0, -e.Delta);
    }

    internal event Action<WallboardHostSnapshot, Point>? HostRightClicked;
    internal IReadOnlyList<WallboardHostSnapshot> CurrentOutageHosts => _sidebarHitRegions.Select(hit => hit.Host).ToList();

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleHostKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    internal WallboardHostSnapshot? HitTestHost(Point location)
    {
        var sidebar = _sidebarHitRegions.LastOrDefault(hit => hit.Bounds.Contains(location)).Host;
        if (sidebar != null) return sidebar;
        if (!_topologyViewport.Contains(location) || _topologyVirtualSize.Width <= 0) return null;
        var point = new Point(
            location.X - _topologyViewport.X + _panOffset.X,
            location.Y - _topologyViewport.Y + _panOffset.Y);
        return _hostHitRegions.LastOrDefault(hit => hit.Bounds.Contains(point)).Host;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) { _dragOrigin = null; Capture = false; }
        if (e.Button == MouseButtons.Right && HitTestHost(e.Location) is { } host)
            HostRightClicked?.Invoke(host, e.Location);
    }

    public WallboardSnapshot? Snapshot { get; set; }
    public bool ShowCli { get; set; } = true;
    // The dashboard and full-screen presentation use the same data and renderer.
    public bool Embedded { get; set; }
    internal Rectangle BrandLogoBounds => new(22, 12, 58, 58);

    public WallboardCanvas()
    {
        Name = "WallboardCanvas";
        TabStop = true;
        AccessibleName = "Sites and hosts";
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WallboardStyle.Background;
        ForeColor = Color.White;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hostTip.Dispose();
            _brandBitmap.Dispose();
            _brandFont.Dispose();
            _subtitleFont.Dispose();
            _clockFont.Dispose();
            _dateFont.Dispose();
            _statValueFont.Dispose();
            _statLabelFont.Dispose();
            _sectionFont.Dispose();
            _siteFont.Dispose();
            _siteSmallFont.Dispose();
            _siteTinyFont.Dispose();
            _hostLabelFont.Dispose();
            _hostIpFont.Dispose();
            _smallFont.Dispose();
            _tinyFont.Dispose();
            _coreFont.Dispose();
            _cliFont.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var snapshot = Snapshot ?? new WallboardSnapshot(
            false,
            DateTime.Now,
            Array.Empty<WallboardSiteSnapshot>(),
            Array.Empty<WallboardEventSnapshot>(),
            Array.Empty<WallboardCommandSnapshot>(),
            "Last 24 hours",
            true);

        _hostHitRegions.Clear();
        _hostCaptionRegions.Clear();
        _sidebarHitRegions.Clear();
        DrawBackground(g);
        DrawHeader(g, snapshot);

        int headerBandHeight = Embedded ? 40 : 112;
        int statsTop = headerBandHeight;
        int statsHeight = 0;


        int bodyTop = statsTop + statsHeight + (Embedded ? 0 : 18);
        int footerHeight = Embedded ? 0 : Presentation.WallboardFooterHeight;
        bool hasOutages = snapshot.Monitoring && snapshot.Sites.SelectMany(site => site.Hosts).Any(host => IsActiveOutage(snapshot, host));
        int sidebarWidth = !hasOutages ? 0 : Embedded ? Math.Clamp((int)(ClientSize.Width * .25), 200, 260) : Math.Clamp((int)(ClientSize.Width * .25), 240, 360);
        int gap = hasOutages ? (Embedded ? 12 : 16) : 0;
        int availableBodyHeight = Math.Max(
            Embedded ? 100 : 240,
            ClientSize.Height - bodyTop - footerHeight - (Embedded ? 4 : 16));

        int cliHeight = ShowCli
            ? Math.Clamp((int)(availableBodyHeight * 0.28), 150, 240)
            : 0;

        int upperHeight = Embedded || !ShowCli ? availableBodyHeight : Math.Max(180, availableBodyHeight - cliHeight - 16);

        var mapRect = new Rectangle(
            Embedded ? 0 : 22,
            bodyTop,
            Math.Max(Embedded ? 160 : 300, ClientSize.Width - sidebarWidth - gap - (Embedded ? 0 : 44)),
            upperHeight);

        var sidebarRect = new Rectangle(
            mapRect.Right + gap,
            bodyTop,
            sidebarWidth,
            upperHeight);

        DrawPanel(g, mapRect);
        DrawTopology(g, snapshot, mapRect);
        if (hasOutages)
        {
            DrawPanel(g, sidebarRect);
            var sidebarClip = g.Save();
            g.SetClip(sidebarRect, System.Drawing.Drawing2D.CombineMode.Intersect);
            DrawSidebar(g, snapshot, sidebarRect);
            g.Restore(sidebarClip);
        }

        if (!Embedded && ShowCli)
        {
            var cliRect = new Rectangle(22, mapRect.Bottom + 16, ClientSize.Width - 44,
                Math.Max(80, ClientSize.Height - mapRect.Bottom - 16 - footerHeight - 16));
            DrawPanel(g, cliRect);
            var outputClip = g.Save();
            g.SetClip(cliRect, System.Drawing.Drawing2D.CombineMode.Intersect);
            DrawCli(g, snapshot, cliRect);
            g.Restore(outputClip);
        }

        RefreshHostTargets(g);
        DrawHostFocus(g);
        // Shortcuts remain available without a permanent legend.
    }

    private void DrawResultsTable(Graphics g, WallboardSnapshot snapshot, Rectangle rect)
    {
        _resultsViewport = rect;
        var clip = g.Save();
        g.SetClip(rect, System.Drawing.Drawing2D.CombineMode.Intersect);
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        float[] fractions = { 0, .28f, .54f, .73f, .89f, 1 };
        string[] headings = { "Address", "Name", "Site", "Status", "Latency" };
        int x = rect.X + 16, width = rect.Width - 32, y = rect.Y + 16;
        for (int c = 0; c < headings.Length; c++)
            g.DrawString(headings[c], _tinyFont, muted,
                new RectangleF(x + width * fractions[c], y, width * (fractions[c + 1] - fractions[c]), 24), format);
        y += 32;
        var hosts = snapshot.Sites.SelectMany(site => site.Hosts).Where(host => !IsHostSnoozed(host)).ToList();
        _resultScrollOffset = Math.Clamp(_resultScrollOffset, 0, Math.Max(0, hosts.Count - 1));
        int visible = 0;
        foreach (var host in hosts.Skip(_resultScrollOffset))
        {
            if (y + 32 > rect.Bottom - 36) break;
            string[] values = { host.Address, host.Label, host.Site,
                snapshot.Monitoring ? host.State.ToString() : "Stopped", host.LatencyMs is long ms ? $"{ms} ms" : "—" };
            for (int c = 0; c < values.Length; c++)
                g.DrawString(values[c], c == 0 ? _hostIpFont : _smallFont, text,
                    new RectangleF(x + width * fractions[c], y, width * (fractions[c + 1] - fractions[c]) - 8, 24), format);
            _sidebarHitRegions.Add((new Rectangle(x, y, width, 32), host));
            y += 32;
            visible++;
        }
        if (hosts.Count > visible)
            g.DrawString($"{_resultScrollOffset + 1}–{_resultScrollOffset + visible} of {hosts.Count} · Scroll for more", _tinyFont, muted, rect.X + 16, rect.Bottom - 28);
        g.Restore(clip);
    }

    private void DrawBackground(Graphics g)
    {
        g.Clear(WallboardStyle.Background);
    }

    private void DrawHeader(Graphics g, WallboardSnapshot snapshot)
    {
        using var brandBrush = new SolidBrush(WallboardStyle.Text);
        using var accentBrush = new SolidBrush(WallboardStyle.Accent);
        using var mutedBrush = new SolidBrush(WallboardStyle.Muted);

        var hosts = snapshot.Sites.SelectMany(site => site.Hosts).ToList();
        string summary = $"{hosts.Count(h => h.State == HostState.Online)} online · " +
            $"{hosts.Count(h => h.State == HostState.Suspect)} suspect · " +
            $"{hosts.Count(h => h.State == HostState.Offline)} offline";
        if (Embedded)
        {
            g.DrawString(snapshot.Monitoring ? summary : "Sites and hosts", _sectionFont, brandBrush, 0, 8);
            return;
        }
        g.DrawString(summary, _sectionFont, brandBrush, 24, 32);
        string clock = DisplayTime.Clock(snapshot.CapturedAt);
        string date = snapshot.CapturedAt.ToString("dddd, MMMM d, yyyy");

        var clockSize = g.MeasureString(clock, _clockFont);
        var dateSize = g.MeasureString(date, _dateFont);

        int rightEdge = ClientSize.Width - 24;

        g.DrawString(
            clock,
            _clockFont,
            brandBrush,
            rightEdge - clockSize.Width,
            10);

        g.DrawString(
            date,
            _dateFont,
            mutedBrush,
            rightEdge - dateSize.Width,
            48);

        var stateRect = new Rectangle(
            rightEdge - 220,
            74,
            220,
            24);

        using var stateBrush = new SolidBrush(snapshot.Monitoring
            ? WallboardStyle.Selection
            : WallboardStyle.Panel);
        using var stateText = new SolidBrush(snapshot.Monitoring
            ? WallboardStyle.Green
            : WallboardStyle.Muted);

        using (var pill = WallboardStyle.Rounded(stateRect, 10)) g.FillPath(stateBrush, pill);
        DrawCenteredText(
            g,
            snapshot.Monitoring ? "Monitoring" : "Stopped",
            _smallFont,
            stateText,
            stateRect);

        using var divider = new Pen(WallboardStyle.Line, 1);
        g.DrawLine(
            divider,
            22,
            108,
            Math.Max(22, ClientSize.Width - 22),
            108);
    }

    private void DrawStats(
        Graphics g,
        WallboardSnapshot snapshot,
        int top,
        int height)
    {
        var hosts = snapshot.Sites.SelectMany(s => s.Hosts).ToList();
        int total = hosts.Count;
        int online = hosts.Count(h => h.State == HostState.Online);
        int suspect = hosts.Count(h => h.State == HostState.Suspect);
        int offline = hosts.Count(h => h.State == HostState.Offline);

        int gap = 12;
        int left = 22;
        int width = ClientSize.Width - 44;
        int cardWidth = (width - gap * 3) / 4;

        DrawStatCard(g, new Rectangle(left, top, cardWidth, height), "Total hosts", total, WallboardStyle.Text);
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap), top, cardWidth, height), "Online", online, WallboardStyle.Green);
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 2, top, cardWidth, height), "Suspect", suspect, WallboardStyle.Yellow);
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 3, top, cardWidth, height), "Offline", offline, WallboardStyle.Red);
    }

    private void DrawStatCard(
        Graphics g,
        Rectangle rect,
        string label,
        int value,
        Color valueColor)
    {
        using var cardBrush = new SolidBrush(WallboardStyle.Panel);
        using var borderPen = new Pen(WallboardStyle.Line);
        using var valueBrush = new SolidBrush(valueColor);
        using var labelBrush = new SolidBrush(WallboardStyle.Muted);

        WallboardStyle.DrawSurface(g, rect, cardBrush, borderPen);

        g.DrawString(label, _statLabelFont, labelBrush, rect.X + 15, rect.Y + 10);
        g.DrawString(value.ToString(), _statValueFont, valueBrush, rect.X + 14, rect.Y + 28);
    }

    private static void DrawPanel(Graphics g, Rectangle rect)
    {
        using var fill = new SolidBrush(WallboardStyle.Panel);
        using var border = new Pen(WallboardStyle.Line);
        WallboardStyle.DrawSurface(g, rect, fill, border);
    }

    private void DrawTopology(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        _topologyViewport = rect;
        _topologyVirtualSize = rect.Size;
        _panOffset = Point.Empty;
        var state = g.Save();
        g.SetClip(rect, System.Drawing.Drawing2D.CombineMode.Intersect);
        g.TranslateTransform(rect.X, rect.Y);
        DrawFittedTopology(g, snapshot, new Rectangle(Point.Empty, rect.Size));
        g.Restore(state);
    }

    internal static double CalculateTopologyRenderScale(
        Rectangle rect,
        int siteCount,
        int visibleHostCount)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return 0.55;

        double viewportScale = Math.Min(
            rect.Width / 900d,
            rect.Height / 420d);
        double siteScale = siteCount <= 4
            ? 1d
            : Math.Sqrt(4d / Math.Max(1, siteCount));
        double hostScale = visibleHostCount <= 18
            ? 1d
            : Math.Sqrt(18d / Math.Max(1, visibleHostCount));

        return Math.Clamp(
            Math.Min(1d, Math.Min(viewportScale, Math.Min(siteScale, hostScale))),
            0.55d,
            1d);
    }

    private void DrawTopologyCore(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        _captionObstacles.Clear();
        _hiddenCaptions = 0;
        if (snapshot.Sites.Count == 0)
        {
            using var empty = new SolidBrush(WallboardStyle.Muted);
            g.DrawString("No hosts", _sectionFont, empty, 16, 16);
            return;
        }
        int columns = _topologyViewport.Width >= 1100 && snapshot.Sites.Count > 1 ? 2 : 1;
        int laneWidth = rect.Width / columns;
        int groupHeight = Math.Max(180, snapshot.Sites.Max(site => site.Hosts.Count) * 52 + 64);
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        for (int index = 0; index < snapshot.Sites.Count; index++)
        {
            var site = snapshot.Sites[index];
            int x = (index % columns) * laneWidth;
            int y = (index / columns) * groupHeight;
            int rootY = y + 40 + Math.Max(0, site.Hosts.Count - 1) * 26;
            var root = new Point(x + 184, rootY);
            using var siteDot = new SolidBrush(StateColor(AggregateSiteState(site), 255));
            g.FillEllipse(siteDot, root.X - 6, root.Y - 6, 12, 12);
            g.DrawString(site.Name, _siteFont, text, new RectangleF(x + 16, rootY - 20, 150, 24), format);
            g.DrawString($"{site.Hosts.Count(h => h.State == HostState.Online)}/{site.Hosts.Count} online", _tinyFont, muted, x + 16, rootY + 4);
            for (int h = 0; h < site.Hosts.Count; h++)
            {
                var host = site.Hosts[h];
                var point = new Point(x + 240, y + 40 + h * 52);
                using var connector = new Pen(StateColor(host.State, 90), 1.5f);
                g.DrawBezier(connector, root, new Point(root.X + 32, root.Y), new Point(point.X - 32, point.Y), point);
                using var dot = new SolidBrush(StateColor(host.State, 255));
                g.FillEllipse(dot, point.X - 4, point.Y - 4, 8, 8);
                var lines = FormatHostNodeLines(host);
                var size = MeasureHostNodeCaption(g, lines.Primary, lines.Secondary, _hostLabelFont, _hostIpFont);
                var caption = new Rectangle(point.X + 12, point.Y - size.Height / 2, size.Width, size.Height);
                _hostHitRegions.Add((new Rectangle(point.X - 8, point.Y - 8, 16, 16), host));
                _hostHitRegions.Add((caption, host));
                _hostCaptionRegions.Add((caption, host));
                int primaryHeight = (int)Math.Ceiling(_hostLabelFont.GetHeight(g));
                g.DrawString(lines.Primary, _hostLabelFont, text, new RectangleF(caption.X + 4, caption.Y + 4, caption.Width - 8, primaryHeight + 2), format);
                if (lines.Secondary is not null)
                    g.DrawString(lines.Secondary, _hostIpFont, muted, caption.X + 4, caption.Y + 4 + primaryHeight, StringFormat.GenericTypographic);
            }
        }
    }

    private static List<Point> CalculateSitePoints(
        int count,
        Rectangle rect,
        Point center,
        int coreRadius)
    {
        return Presentation.SitePoints(count, rect.X, rect.Y, rect.Width, rect.Height, coreRadius)
            .Select(p => new Point((int)p.X, (int)p.Y)).ToList();
    }

    private void DrawConnection(
        Graphics g,
        Point center,
        Point site,
        HostState status,
        int index)
    {
        Color color = StateColor(status, 115);
        using var linePen = new Pen(color, status == HostState.Offline ? 2.4f : 1.4f);
        g.DrawLine(linePen, center, site);


    }

    private void DrawCore(
        Graphics g,
        Point center,
        int radius,
        bool monitoring)
    {
        using var fillBrush = new SolidBrush(WallboardStyle.Selection);
        using var outlinePen = new Pen(WallboardStyle.Accent, 2);
        using var textBrush = new SolidBrush(WallboardStyle.Text);

        g.FillEllipse(fillBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(outlinePen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

        var coreRect = new Rectangle(
            center.X - radius,
            center.Y - 12,
            radius * 2,
            24);

        DrawCenteredText(g, "Monitor", _coreFont, textBrush, coreRect);

        using var dotBrush = new SolidBrush(monitoring
            ? WallboardStyle.Green
            : WallboardStyle.Muted);

        g.FillEllipse(dotBrush, center.X - 4, center.Y + radius - 15, 8, 8);
    }

    private void DrawSiteNode(
        Graphics g,
        WallboardSiteSnapshot site,
        Point center,
        Rectangle topologyBounds,
        int index)
    {
        var state = AggregateSiteState(site);
        int radius = 38;
        Color color = StateColor(state, 255);

        using var fillBrush = new SolidBrush(WallboardStyle.Panel);
        using var borderPen = new Pen(color, 2);
        using var nameBrush = new SolidBrush(WallboardStyle.Text);
        using var countBrush = new SolidBrush(WallboardStyle.Muted);

        g.FillEllipse(fillBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(borderPen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

        var nameRect = new Rectangle(
            center.X - radius + 5,
            center.Y - 24,
            (radius - 5) * 2,
            36);

        DrawSiteNodeName(g, site.Name, nameBrush, nameRect);

        int online = site.Hosts.Count(h => h.State == HostState.Online);
        var countRect = new Rectangle(
            center.X - radius + 5,
            center.Y + 11,
            (radius - 5) * 2,
            16);

        DrawCenteredText(
            g,
            $"{online}/{site.Hosts.Count} online",
            _tinyFont,
            countBrush,
            countRect);

        DrawHostNodes(g, site, center, topologyBounds, radius + 30);
    }

    private void DrawSiteNodeName(
        Graphics g,
        string name,
        Brush brush,
        Rectangle rect)
    {
        string formatted = FormatSiteNodeName(name);

        Font font = _siteFont;
        var size = g.MeasureString(formatted, font, rect.Width);

        if (size.Width > rect.Width || size.Height > rect.Height)
        {
            font = _siteSmallFont;
            size = g.MeasureString(formatted, font, rect.Width);
        }

        if (size.Width > rect.Width || size.Height > rect.Height)
            font = _siteTinyFont;

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        g.DrawString(formatted, font, brush, rect, format);
    }

    private static string FormatSiteNodeName(string name)
    {
        name = name.Trim();

        if (name.Length <= 9)
            return name;

        int midpoint = name.Length / 2;
        var candidates = new List<int>();

        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsWhiteSpace(name[i]))
                candidates.Add(i);
            else if (char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                candidates.Add(i);
        }

        if (candidates.Count == 0)
            return name;

        int split = candidates
            .OrderBy(i => Math.Abs(i - midpoint))
            .First();

        string first = name[..split].Trim();
        string second = name[split..].Trim();

        return string.IsNullOrWhiteSpace(second)
            ? name
            : $"{first}\n{second}";
    }

    private void DrawHostNodes(
        Graphics g,
        WallboardSiteSnapshot site,
        Point siteCenter,
        Rectangle topologyBounds,
        int baseOrbitRadius)
    {
        int visible = Math.Min(site.Hosts.Count, Presentation.VisibleTopologyHosts);

        if (visible == 0)
            return;

        for (int i = 0; i < visible; i++)
        {
            var host = site.Hosts[i];
            double angle = Presentation.HostAngle(i, visible);
            var sharedPoint = Presentation.HostPoint(i, visible, new UiPoint(siteCenter.X, siteCenter.Y), baseOrbitRadius);
            var point = new Point((int)sharedPoint.X, (int)sharedPoint.Y);

            Color stateColor = StateColor(host.State, 255);

            using var connectorPen = new Pen(StateColor(host.State, 75), 1);
            g.DrawLine(connectorPen, siteCenter, point);

            if (host.State == HostState.Offline)
            {
                using var alertRing = new Pen(StateColor(host.State, 100), 1.5f);
                g.DrawEllipse(alertRing, point.X - 8, point.Y - 8, 16, 16);
            }

            using var nodeBrush = new SolidBrush(stateColor);
            using var nodeBorder = new Pen(WallboardStyle.Text, 1);
            g.FillEllipse(nodeBrush, point.X - 5, point.Y - 5, 10, 10);
            g.DrawEllipse(nodeBorder, point.X - 5, point.Y - 5, 10, 10);

            _hostHitRegions.Add((new Rectangle(point.X - 10, point.Y - 10, 20, 20), host));
            DrawHostNodeCaption(g, host, point, angle, topologyBounds);
        }

        if (site.Hosts.Count > visible)
        {
            using var moreBrush = new SolidBrush(WallboardStyle.Muted);
            string more = $"+{site.Hosts.Count - visible} more";
            var size = g.MeasureString(more, _tinyFont);

            var rect = ClampToBounds(
                new Rectangle(
                    siteCenter.X - (int)size.Width / 2 - 4,
                    siteCenter.Y + baseOrbitRadius + 18,
                    (int)size.Width + 8,
                    (int)size.Height + 4),
                topologyBounds);

            using var fill = new SolidBrush(WallboardStyle.Panel);
            g.FillRectangle(fill, rect);
            g.DrawString(more, _tinyFont, moreBrush, rect.X + 4, rect.Y + 2);
        }
    }

    private void DrawHostNodeCaption(
        Graphics g,
        WallboardHostSnapshot host,
        Point point,
        double angle,
        Rectangle topologyBounds)
    {
        var lines = FormatHostNodeLines(host);
        string primary = lines.Primary;
        string? secondary = lines.Secondary;

        var captionSize = MeasureHostNodeCaption(g, primary, secondary, _hostLabelFont, _hostIpFont);
        int width = captionSize.Width;
        int height = captionSize.Height;
        int primaryHeight = (int)Math.Ceiling(_hostLabelFont.GetHeight(g));

        if (!TopologyLayout.TryPlaceHostCaption(point, captionSize, topologyBounds,
            _captionObstacles, Math.Cos(angle) >= 0, out var rect))
        {
            _hiddenCaptions++;
            return;
        }
        _captionObstacles.Add(rect);
        _hostHitRegions.Add((rect, host));
        _hostCaptionRegions.Add((rect, host));
        using (var leader = new Pen(StateColor(host.State, 120), 1))
            g.DrawLine(leader, point, new Point(Math.Clamp(point.X, rect.Left, rect.Right),
                Math.Clamp(point.Y, rect.Top, rect.Bottom)));

        using var fill = new SolidBrush(WallboardStyle.Panel);
        using var primaryBrush = new SolidBrush(WallboardStyle.Text);
        using var secondaryBrush = new SolidBrush(WallboardStyle.Muted);

        // One renderer for measurement and drawing; no GDI text over GDI+ bitmaps.
        // Quiet labels on the canvas, with no outlined boxes around every address.
        g.FillRectangle(fill, rect);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap,
            Trimming = secondary is null ? StringTrimming.None : StringTrimming.EllipsisCharacter };
        g.DrawString(primary, _hostLabelFont, primaryBrush,
            new RectangleF(rect.X + 6, rect.Y + 4, rect.Width - 12, primaryHeight + 2), format);

        if (secondary is not null)
        {
            format.Trimming = StringTrimming.None;
            g.DrawString(secondary, _hostIpFont, secondaryBrush,
                new RectangleF(rect.X + 6, rect.Y + 4 + primaryHeight, rect.Width - 12, rect.Height - primaryHeight), format);
        }
    }

    // Measure with the same GDI+ renderer used for drawing. Addresses never
    // inherit the nickname width cap, including IPv6 and larger display scaling.
    internal static Size MeasureHostNodeCaption(Graphics g, string primary, string? secondary,
        Font primaryFont, Font addressFont)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var primarySize = g.MeasureString(primary, primaryFont, int.MaxValue, format);
        var addressSize = secondary is null ? SizeF.Empty : g.MeasureString(secondary, addressFont, int.MaxValue, format);
        float primaryWidth = secondary is null ? primarySize.Width : Math.Min(primarySize.Width, 180);
        return new Size((int)Math.Ceiling(Math.Max(64, Math.Max(primaryWidth, addressSize.Width) + 16)),
            (int)Math.Ceiling(primaryFont.GetHeight(g) + (secondary is null ? 0 : addressFont.GetHeight(g)) + 12));
    }

    internal static (string Primary, string? Secondary) FormatHostNodeLines(
        WallboardHostSnapshot host)
    {
        string address = host.Address.Trim();
        string label = host.Label.Trim();

        if (host.SnoozedUntilUtc is { } until && until > DateTimeOffset.UtcNow)
            return (string.IsNullOrWhiteSpace(label) ? "Alerts snoozed" : label + " [snoozed]", address);

        return string.IsNullOrWhiteSpace(label)
            ? (address, null)
            : (label, address);
    }

    private static Rectangle ClampToBounds(Rectangle rect, Rectangle bounds)
    {
        int x = Math.Clamp(
            rect.X,
            bounds.Left + 2,
            Math.Max(bounds.Left + 2, bounds.Right - rect.Width - 2));
        int y = Math.Clamp(
            rect.Y,
            bounds.Top + 2,
            Math.Max(bounds.Top + 2, bounds.Bottom - rect.Height - 2));

        return new Rectangle(x, y, rect.Width, rect.Height);
    }

    private void DrawSidebar(Graphics g, WallboardSnapshot snapshot, Rectangle rect)
    {
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        int x = rect.X + 16, y = rect.Y + 16, width = rect.Width - 32;
        g.DrawString("Active outages", _sectionFont, text, x, y);
        y += 32;
        foreach (var host in snapshot.Sites.SelectMany(site => site.Hosts).Where(host => IsActiveOutage(snapshot, host)))
        {
            if (y + 48 > rect.Bottom - 12) break;
            _sidebarHitRegions.Add((new Rectangle(x, y, width, 48), host));
            g.DrawString(string.IsNullOrWhiteSpace(host.Label) ? host.Address : host.Label,
                _smallFont, text, new RectangleF(x, y, width, 20), format);
            g.DrawString(host.Site + (host.OutageStarted is DateTime started ? " · " + FormatElapsed(DateTime.Now - started) : ""),
                _tinyFont, muted, new RectangleF(x, y + 20, width, 20), format);
            y += 52;
        }
    }

    private void DrawCli(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        int x = rect.X + 14;
        int y = rect.Y + 11;
        int width = rect.Width - 28;
        int contentTop = y + 27;
        int contentBottom = rect.Bottom - 10;

        using var titleBrush = new SolidBrush(WallboardStyle.Muted);
        using var hintBrush = new SolidBrush(Color.FromArgb(84, 111, 128));
        using var idleBrush = new SolidBrush(Color.FromArgb(98, 116, 133));
        using var successBrush = new SolidBrush(WallboardStyle.Green);
        using var failureBrush = new SolidBrush(WallboardStyle.Red);

        g.DrawString("Ping activity", _sectionFont, titleBrush, x, y);

        string hint = "C  Hide";
        var hintSize = g.MeasureString(hint, _tinyFont);
        g.DrawString(
            hint,
            _tinyFont,
            hintBrush,
            rect.Right - hintSize.Width - 14,
            y + 1);

        if (snapshot.Commands.Count == 0)
        {
            g.DrawString(
                snapshot.Monitoring
                    ? "Waiting for ping output..."
                    : "Start monitoring to populate the live CLI trace.",
                _cliFont,
                idleBrush,
                x,
                contentTop);
            return;
        }

        float lineHeight = Math.Max(15f, _cliFont.GetHeight(g) + 2f);
        int lineCapacity = Math.Max(
            1,
            (int)((contentBottom - contentTop) / lineHeight));

        var visible = snapshot.Commands
            .TakeLast(lineCapacity)
            .ToList();

        float lineY = contentBottom - visible.Count * lineHeight;

        foreach (var command in visible)
        {
            Brush brush = command.Success
                ? successBrush
                : failureBrush;

            string text = TrimToWidth(
                g,
                command.Text,
                _cliFont,
                width);

            g.DrawString(
                text,
                _cliFont,
                brush,
                x,
                lineY);

            lineY += lineHeight;
        }
    }

    private void DrawFooter(Graphics g)
    {
        using var mutedBrush = new SolidBrush(WallboardStyle.Muted);
        string left = ShowCli
            ? "Esc Exit Wallboard   O Sites   P Start/Stop   C Hide log   H History range   S Suspects"
            : "Esc Exit Wallboard   O Sites   P Start/Stop   C Show log   H History range   S Suspects";
        g.DrawString(left, _tinyFont, mutedBrush, 23, ClientSize.Height - 25);
    }

    internal static HostState AggregateSiteState(WallboardSiteSnapshot site)
    {
        var activeHosts = site.Hosts.Where(host => !IsHostSnoozed(host)).ToList();
        int total = activeHosts.Count;

        if (total == 0)
            return HostState.Unknown;

        int online = activeHosts.Count(h => h.State == HostState.Online);
        int offline = activeHosts.Count(h => h.State == HostState.Offline);
        int suspect = activeHosts.Count(h => h.State == HostState.Suspect);

        if (offline > 0)
        {
            // A site with some failed hosts is degraded while a strict
            // majority of its configured hosts are still replying.
            return online > total / 2d
                ? HostState.Suspect
                : HostState.Offline;
        }

        if (suspect > 0)
            return HostState.Suspect;

        if (online > 0)
            return HostState.Online;

        return HostState.Unknown;
    }

    private static Color StateColor(HostState state, int alpha)
    {
        var p = AuroraTheme.Palette;
        Color value = state switch {
            HostState.Online => p.Teal, HostState.Suspect => p.Warning,
            HostState.Offline => p.Danger, _ => p.Secondary
        };
        return Color.FromArgb(alpha, value);
    }

    private static void DrawCenteredText(
        Graphics g,
        string text,
        Font font,
        Brush brush,
        Rectangle rect)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(text, font, brush, rect, format);
    }

    private static string TrimToWidth(
        Graphics g,
        string text,
        Font font,
        int width)
    {
        if (g.MeasureString(text, font).Width <= width)
            return text;

        string candidate = text;

        while (candidate.Length > 3 &&
               g.MeasureString(candidate + "…", font).Width > width)
        {
            candidate = candidate[..^1];
        }

        return candidate + "…";
    }

    private static string FormatElapsed(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";

        return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
    }
}
