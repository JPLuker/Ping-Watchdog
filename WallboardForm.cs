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
        BackColor = Color.FromArgb(10, 16, 23)
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
        Font = new Font("Cascadia Mono", 9)
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
    private readonly Button _operationsButton = ToolbarButton("Operations", 88);
    private readonly Button _cliButton = ToolbarButton("CLI", 58);
    private readonly Button _historyButton = ToolbarButton("History", 72);
    private readonly Button _settingsButton = ToolbarButton("Settings", 74);
    private readonly Button _updateButton = ToolbarButton("Check Updates", 112);
    private readonly Button _screenButton = ToolbarButton("Screen", 66);
    private readonly Button _mainButton = ToolbarButton("Main Window", 92);

    private readonly Button _applyHostsButton = DrawerButton("Apply Hosts");
    private readonly Button _addSiteButton = DrawerButton("+ Add Site");
    private readonly Button _renameSiteButton = DrawerButton("Rename");
    private readonly Button _deleteSiteButton = DrawerButton("Delete");
    private readonly Button _organizeButton = DrawerButton("Folders / Organization");
    private readonly Button _editLabelButton = DrawerButton("Edit Label");
    private readonly Button _clearLabelButton = DrawerButton("Clear Label");
    private readonly Button _applyMonitoringButton = DrawerButton("Apply Monitoring Defaults");
    private readonly Button _exportButton = DrawerButton("Export Config");
    private readonly Button _importButton = DrawerButton("Import Config");
    private readonly Button _clearCliButton = DrawerButton("Clear CLI");
    private readonly Button _closeDrawerButton = DrawerButton("Close Operations");

    private readonly Label _updateStatusLabel = new()
    {
        AutoSize = false,
        Width = 372,
        Height = 42,
        ForeColor = Color.FromArgb(139, 158, 178)
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
        BackColor = Color.FromArgb(5, 9, 14);
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
        return new Button
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
        return new Button
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(5, 9, 14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(14, 0, 10, 0),
            Margin = new Padding(0),
            BackColor = Color.FromArgb(9, 15, 22)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        toolbar.Controls.Add(new Label
        {
            Text = "PING WATCHDOG • WALLBOARD",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(191, 211, 231),
            Margin = new Padding(4, 12, 0, 0)
        }, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Name = "WallboardActions",
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };

        foreach (var button in new[]
        {
            _monitorButton,
            _operationsButton,
            _cliButton,
            _historyButton,
            _settingsButton,
            _updateButton,
            _screenButton,
            _mainButton
        })
        {
            actions.Controls.Add(button);
        }

        toolbar.Controls.Add(actions, 0, 1);
        toolbar.SizeChanged += (_, _) => actions.MaximumSize = new Size(Math.Max(1, toolbar.ClientSize.Width - toolbar.Padding.Horizontal), 0);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(5, 9, 14)
        };

        _canvas.Dock = DockStyle.Fill;
        _canvas.ShowCli = _showCli;

        BuildOperationsDrawer();

        content.Controls.Add(_canvas);
        content.Controls.Add(_drawer);
        _drawer.BringToFront();

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(content, 0, 1);
        Controls.Add(root);
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

        stack.Controls.Add(SectionTitle("Operations"));
        stack.Controls.Add(SectionNote(
            "Wallboard controls the same live Ping Watchdog session as the main window."));

        stack.Controls.Add(SectionTitle("Site"));
        stack.Controls.Add(_siteCombo);

        var siteButtons = Row(_addSiteButton, _renameSiteButton, _deleteSiteButton);
        stack.Controls.Add(siteButtons);
        stack.Controls.Add(_organizeButton);

        stack.Controls.Add(SectionTitle("Hosts"));
        stack.Controls.Add(_hostEditor);
        stack.Controls.Add(_applyHostsButton);

        stack.Controls.Add(SectionTitle("Live host status"));
        stack.Controls.Add(_hostGrid);
        stack.Controls.Add(Row(_editLabelButton, _clearLabelButton));

        stack.Controls.Add(SectionTitle("Monitoring defaults"));
        stack.Controls.Add(MonitorRow("Interval", _interval, "sec"));
        stack.Controls.Add(MonitorRow("Timeout", _timeout, "ms"));
        stack.Controls.Add(MonitorRow("Down after", _downAfter, "fails"));
        stack.Controls.Add(MonitorRow("Recover after", _recoverAfter, "successes"));
        stack.Controls.Add(_applyMonitoringButton);

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
            ForeColor = Color.FromArgb(126, 143, 160),
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
            ForeColor = Color.FromArgb(126, 143, 160)
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
        var border = Color.FromArgb(39, 53, 67);
        var button = Color.FromArgb(20, 29, 39);
        var text = Color.FromArgb(230, 238, 246);
        var input = Color.FromArgb(7, 12, 18);

        foreach (var control in new Button[]
        {
            _monitorButton,
            _operationsButton,
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

        _operationsButton.BackColor = Color.FromArgb(19, 58, 79);
        _operationsButton.FlatAppearance.BorderColor = Color.FromArgb(48, 119, 150);

        _siteCombo.BackColor = input;
        _siteCombo.ForeColor = text;
        _siteCombo.FlatStyle = FlatStyle.Flat;

        _hostEditor.BackColor = input;
        _hostEditor.ForeColor = Color.FromArgb(207, 221, 234);
        _hostEditor.BorderStyle = BorderStyle.FixedSingle;

        foreach (var numeric in new[] { _interval, _timeout, _downAfter, _recoverAfter })
        {
            numeric.BackColor = input;
            numeric.ForeColor = text;
        }

        _hostGrid.BackgroundColor = Color.FromArgb(7, 12, 18);
        _hostGrid.GridColor = Color.FromArgb(31, 43, 55);
        _hostGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(19, 28, 38);
        _hostGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(139, 158, 178);
        _hostGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(19, 28, 38);
        _hostGrid.DefaultCellStyle.BackColor = Color.FromArgb(10, 16, 23);
        _hostGrid.DefaultCellStyle.ForeColor = text;
        _hostGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(31, 74, 102);
        _hostGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _hostGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(8, 14, 20);
    }

    private void OnWallboardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Escape or Keys.F11)
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
        _cliButton.Text = _showCli ? "Hide CLI" : "Show CLI";

        _canvas.Invalidate();
    }

    private void ToggleDrawer(bool forceClosed = false)
    {
        _drawer.Visible = forceClosed ? false : !_drawer.Visible;
        _operationsButton.Text = _drawer.Visible ? "Close Ops" : "Operations";

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
            _monitorButton.Text = state.Monitoring ? "Stop Monitoring" : "Start Monitoring";
            _monitorButton.BackColor = state.Monitoring
                ? Color.FromArgb(87, 35, 43)
                : Color.FromArgb(28, 99, 68);
            _monitorButton.FlatAppearance.BorderColor = state.Monitoring
                ? Color.FromArgb(151, 59, 70)
                : Color.FromArgb(51, 157, 108);

            _cliButton.Text = _showCli ? "Hide CLI" : "Show CLI";
    

            _updateButton.Text = state.UpdateActionText;
            _updateButton.Visible = state.ShowUpdateControl;
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
                "Online" => Color.FromArgb(78, 216, 143),
                "Suspect" => Color.FromArgb(245, 191, 71),
                "Offline" => Color.FromArgb(255, 101, 111),
                _ => Color.FromArgb(139, 158, 178)
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

internal sealed class WallboardCanvas : Control
{
    private readonly List<Rectangle> _captionObstacles = new();
    private int _hiddenCaptions;
    private readonly Bitmap _brandBitmap = BrandAssets.LoadLogo();
    private readonly Font _brandFont = new("Segoe UI Semibold", 24, FontStyle.Bold);
    private readonly Font _subtitleFont = new("Segoe UI Semibold", 9, FontStyle.Bold);
    private readonly Font _clockFont = new("Segoe UI Semibold", 22, FontStyle.Bold);
    private readonly Font _dateFont = new("Segoe UI", 9.5f);
    private readonly Font _statValueFont = new("Segoe UI Semibold", 25, FontStyle.Bold);
    private readonly Font _statLabelFont = new("Segoe UI Semibold", 8, FontStyle.Bold);
    private readonly Font _sectionFont = new("Segoe UI Semibold", 9, FontStyle.Bold);
    private readonly Font _siteFont = new("Segoe UI Semibold", 9.5f, FontStyle.Bold);
    private readonly Font _siteSmallFont = new("Segoe UI Semibold", 8f, FontStyle.Bold);
    private readonly Font _siteTinyFont = new("Segoe UI Semibold", 6.75f, FontStyle.Bold);
    private readonly Font _hostLabelFont = new("Segoe UI Semibold", 7.5f, FontStyle.Bold);
    private readonly Font _hostIpFont = new("Cascadia Mono", 6.8f);
    private readonly Font _smallFont = new("Segoe UI", 8.5f);
    private readonly Font _tinyFont = new("Segoe UI", 7.5f);
    private readonly Font _coreFont = new("Segoe UI Semibold", 9, FontStyle.Bold);
    private readonly Font _cliFont = new("Cascadia Mono", 8.5f);

    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _hostHitRegions = new();
    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _sidebarHitRegions = new();
    private Rectangle _topologyViewport;
    private Size _topologyVirtualSize;
    internal event Action<WallboardHostSnapshot, Point>? HostRightClicked;

    internal WallboardHostSnapshot? HitTestHost(Point location)
    {
        var sidebar = _sidebarHitRegions.LastOrDefault(hit => hit.Bounds.Contains(location)).Host;
        if (sidebar != null) return sidebar;
        if (!_topologyViewport.Contains(location) || _topologyVirtualSize.Width <= 0) return null;
        var point = new Point(
            (int)((location.X - _topologyViewport.X) * (double)_topologyVirtualSize.Width / _topologyViewport.Width),
            (int)((location.Y - _topologyViewport.Y) * (double)_topologyVirtualSize.Height / _topologyViewport.Height));
        return _hostHitRegions.LastOrDefault(hit => hit.Bounds.Contains(point)).Host;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && HitTestHost(e.Location) is { } host)
            HostRightClicked?.Invoke(host, e.Location);
    }

    public WallboardSnapshot? Snapshot { get; set; }
    public bool ShowCli { get; set; } = true;
    internal Rectangle BrandLogoBounds => new(22, 12, 58, 58);

    public WallboardCanvas()
    {
        Name = "WallboardCanvas";
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(5, 9, 14);
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
        _sidebarHitRegions.Clear();
        DrawBackground(g);
        DrawHeader(g, snapshot);

        const int headerBandHeight = Presentation.WallboardHeaderHeight;
        int statsTop = headerBandHeight;
        int statsHeight = Presentation.WallboardStatsHeight;
        DrawStats(g, snapshot, statsTop, statsHeight);

        int bodyTop = statsTop + statsHeight + 18;
        int footerHeight = Presentation.WallboardFooterHeight;
        int sidebarWidth = Math.Clamp((int)(ClientSize.Width * 0.29), 320, 440);
        int gap = 16;
        int availableBodyHeight = Math.Max(
            240,
            ClientSize.Height - bodyTop - footerHeight - 16);

        int cliHeight = ShowCli
            ? Math.Clamp((int)(availableBodyHeight * 0.28), 150, 240)
            : 0;

        int upperHeight = ShowCli
            ? Math.Max(210, availableBodyHeight - cliHeight - gap)
            : availableBodyHeight;

        var mapRect = new Rectangle(
            22,
            bodyTop,
            Math.Max(300, ClientSize.Width - sidebarWidth - gap - 44),
            upperHeight);

        var sidebarRect = new Rectangle(
            mapRect.Right + gap,
            bodyTop,
            sidebarWidth,
            upperHeight);

        DrawPanel(g, mapRect);
        DrawPanel(g, sidebarRect);
        DrawTopology(g, snapshot, mapRect);
        DrawSidebar(g, snapshot, sidebarRect);

        if (ShowCli)
        {
            var cliRect = new Rectangle(
                22,
                mapRect.Bottom + gap,
                Math.Max(300, ClientSize.Width - 44),
                cliHeight);

            DrawPanel(g, cliRect);
            DrawCli(g, snapshot, cliRect);
        }

        DrawFooter(g);
    }

    private void DrawBackground(Graphics g)
    {
        g.Clear(Color.FromArgb(5, 9, 14));

        using var gridPen = new Pen(Color.FromArgb(18, 27, 37), 1);

        for (int x = 0; x < ClientSize.Width; x += 48)
            g.DrawLine(gridPen, x, 0, x, ClientSize.Height);

        for (int y = 0; y < ClientSize.Height; y += 48)
            g.DrawLine(gridPen, 0, y, ClientSize.Width, y);
    }

    private void DrawHeader(Graphics g, WallboardSnapshot snapshot)
    {
        using var brandBrush = new SolidBrush(Color.FromArgb(238, 244, 250));
        using var accentBrush = new SolidBrush(Color.FromArgb(82, 177, 202));
        using var mutedBrush = new SolidBrush(Color.FromArgb(126, 143, 160));

        g.DrawImage(_brandBitmap, BrandLogoBounds);
        int textLeft = BrandLogoBounds.Right + 12;
        g.DrawString("PING WATCHDOG", _brandFont, brandBrush, textLeft, 14);
        g.DrawString(
            "NETWORK OPERATIONS WALLBOARD",
            _subtitleFont,
            accentBrush,
            textLeft + 3,
            57);

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
            ? Color.FromArgb(24, 92, 64)
            : Color.FromArgb(48, 58, 70));
        using var stateText = new SolidBrush(snapshot.Monitoring
            ? Color.FromArgb(112, 229, 162)
            : Color.FromArgb(165, 179, 194));

        g.FillRectangle(stateBrush, stateRect);
        DrawCenteredText(
            g,
            snapshot.Monitoring ? "LIVE MONITORING" : "IDLE / CONFIG VIEW",
            _smallFont,
            stateText,
            stateRect);

        using var divider = new Pen(Color.FromArgb(28, 40, 52), 1);
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

        DrawStatCard(g, new Rectangle(left, top, cardWidth, height), "TOTAL HOSTS", total, Color.FromArgb(188, 210, 232));
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap), top, cardWidth, height), "ONLINE", online, Color.FromArgb(78, 216, 143));
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 2, top, cardWidth, height), "SUSPECT", suspect, Color.FromArgb(245, 191, 71));
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 3, top, cardWidth, height), "OFFLINE", offline, Color.FromArgb(255, 101, 111));
    }

    private void DrawStatCard(
        Graphics g,
        Rectangle rect,
        string label,
        int value,
        Color valueColor)
    {
        using var cardBrush = new SolidBrush(Color.FromArgb(14, 21, 29));
        using var borderPen = new Pen(Color.FromArgb(34, 47, 61));
        using var valueBrush = new SolidBrush(valueColor);
        using var labelBrush = new SolidBrush(Color.FromArgb(125, 143, 161));

        g.FillRectangle(cardBrush, rect);
        g.DrawRectangle(borderPen, rect);

        g.DrawString(label, _statLabelFont, labelBrush, rect.X + 15, rect.Y + 10);
        g.DrawString(value.ToString(), _statValueFont, valueBrush, rect.X + 14, rect.Y + 28);
    }

    private static void DrawPanel(Graphics g, Rectangle rect)
    {
        using var fill = new SolidBrush(Color.FromArgb(9, 15, 22));
        using var border = new Pen(Color.FromArgb(32, 44, 57));
        g.FillRectangle(fill, rect);
        g.DrawRectangle(border, rect);
    }

    private void DrawTopology(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        int visibleHosts = snapshot.Sites.Sum(site =>
            Math.Min(site.Hosts.Count, Presentation.VisibleTopologyHosts));
        double renderScale = CalculateTopologyRenderScale(
            rect,
            snapshot.Sites.Count,
            visibleHosts);

        _topologyViewport = rect;
        if (renderScale >= 0.995)
        {
            _topologyVirtualSize = rect.Size;
            DrawTopologyCore(g, snapshot, rect);
            for (int i = 0; i < _hostHitRegions.Count; i++)
            {
                var hit = _hostHitRegions[i]; var bounds = hit.Bounds;
                bounds.Offset(-rect.X, -rect.Y); _hostHitRegions[i] = (bounds, hit.Host);
            }
            return;
        }

        int virtualWidth = Math.Max(rect.Width, (int)Math.Ceiling(rect.Width / renderScale));
        int virtualHeight = Math.Max(rect.Height, (int)Math.Ceiling(rect.Height / renderScale));

        _topologyVirtualSize = new Size(virtualWidth, virtualHeight);
        using var bitmap = new Bitmap(virtualWidth, virtualHeight);
        using (var virtualGraphics = Graphics.FromImage(bitmap))
        {
            virtualGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            virtualGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            virtualGraphics.Clear(Color.FromArgb(9, 15, 22));
            DrawTopologyCore(
                virtualGraphics,
                snapshot,
                new Rectangle(0, 0, virtualWidth, virtualHeight));
        }

        var previousInterpolation = g.InterpolationMode;
        var previousPixelOffset = g.PixelOffsetMode;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(bitmap, rect);
        g.InterpolationMode = previousInterpolation;
        g.PixelOffsetMode = previousPixelOffset;

        using var border = new Pen(Color.FromArgb(32, 44, 57));
        g.DrawRectangle(border, rect);
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
        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));
        g.DrawString("LIVE SITE / HOST TOPOLOGY", _sectionFont, titleBrush, rect.X + 16, rect.Y + 13);

        var content = new Rectangle(
            rect.X + 18,
            rect.Y + 42,
            rect.Width - 36,
            rect.Height - 58);

        if (snapshot.Sites.Count == 0)
        {
            using var emptyBrush = new SolidBrush(Color.FromArgb(100, 119, 138));
            DrawCenteredText(
                g,
                "No sites configured",
                _siteFont,
                emptyBrush,
                content);
            return;
        }

        var center = new Point(
            content.X + content.Width / 2,
            content.Y + content.Height / 2);

        DrawRadar(g, content, center);

        int coreRadius = Math.Clamp(
            Math.Min(content.Width, content.Height) / 11,
            38,
            58);

        var sitePoints = CalculateSitePoints(
            snapshot.Sites.Count,
            content,
            center,
            coreRadius);

        _captionObstacles.Clear();
        _hiddenCaptions = 0;
        _captionObstacles.Add(new Rectangle(center.X - coreRadius - 10, center.Y - coreRadius - 10,
            (coreRadius + 10) * 2, (coreRadius + 10) * 2));
        _captionObstacles.Add(new Rectangle(content.Left, content.Bottom - 22, content.Width, 22));
        for (int i = 0; i < snapshot.Sites.Count; i++)
        {
            var sitePoint = sitePoints[i];
            _captionObstacles.Add(new Rectangle(sitePoint.X - 55, sitePoint.Y - 55, 110, 110));
            int count = Math.Min(snapshot.Sites[i].Hosts.Count, Presentation.VisibleTopologyHosts);
            for (int h = 0; h < count; h++)
            {
                var node = Presentation.HostPoint(h, count, new UiPoint(sitePoint.X, sitePoint.Y), 68);
                _captionObstacles.Add(new Rectangle((int)node.X - 8, (int)node.Y - 8, 16, 16));
            }
        }

        for (int i = 0; i < snapshot.Sites.Count; i++)
        {
            var site = snapshot.Sites[i];
            var point = sitePoints[i];
            var status = AggregateSiteState(site);

            DrawConnection(g, center, point, status, i);
        }

        DrawCore(g, center, coreRadius, snapshot.Monitoring);

        for (int i = 0; i < snapshot.Sites.Count; i++)
        {
            DrawSiteNode(
                g,
                snapshot.Sites[i],
                sitePoints[i],
                content,
                i);
        }
        // Dense layouts are rendered on a larger virtual canvas and scaled to fit.
        // Any caption that still cannot be placed is silently omitted; the host node
        // itself remains visible and Operations/Hosts retains the full details.
    }

    private void DrawRadar(Graphics g, Rectangle rect, Point center)
    {
        using var ringPen = new Pen(Color.FromArgb(18, 62, 72), 1);

        int maxRadius = Math.Max(80, Math.Min(rect.Width, rect.Height) / 2 - 12);

        for (int r = maxRadius / 4; r <= maxRadius; r += Math.Max(1, maxRadius / 4))
            g.DrawEllipse(ringPen, center.X - r, center.Y - r, r * 2, r * 2);

        double phase = DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.45;
        var end = new Point(
            center.X + (int)(Math.Cos(phase) * maxRadius),
            center.Y + (int)(Math.Sin(phase) * maxRadius));

        using var sweepPen = new Pen(Color.FromArgb(80, 52, 166, 183), 2);
        g.DrawLine(sweepPen, center, end);
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

        double phase = (DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 1400d + index * 0.17) % 1d;
        var pulse = new Point(
            center.X + (int)((site.X - center.X) * phase),
            center.Y + (int)((site.Y - center.Y) * phase));

        using var pulseBrush = new SolidBrush(StateColor(status, 220));
        g.FillEllipse(pulseBrush, pulse.X - 3, pulse.Y - 3, 6, 6);
    }

    private void DrawCore(
        Graphics g,
        Point center,
        int radius,
        bool monitoring)
    {
        using var glowBrush = new SolidBrush(Color.FromArgb(24, 60, 175, 194));
        using var fillBrush = new SolidBrush(Color.FromArgb(15, 39, 48));
        using var outlinePen = new Pen(Color.FromArgb(77, 190, 208), 2);
        using var textBrush = new SolidBrush(Color.FromArgb(205, 238, 243));

        g.FillEllipse(glowBrush, center.X - radius - 8, center.Y - radius - 8, (radius + 8) * 2, (radius + 8) * 2);
        g.FillEllipse(fillBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(outlinePen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

        var coreRect = new Rectangle(
            center.X - radius,
            center.Y - 12,
            radius * 2,
            24);

        DrawCenteredText(g, "WATCHDOG", _coreFont, textBrush, coreRect);

        using var dotBrush = new SolidBrush(monitoring
            ? Color.FromArgb(87, 224, 149)
            : Color.FromArgb(118, 134, 151));

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

        if (state == HostState.Offline)
        {
            double pulse = (Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 280d) + 1d) / 2d;
            int pulseRadius = radius + 7 + (int)(pulse * 8);
            using var pulsePen = new Pen(StateColor(state, 85), 2);
            g.DrawEllipse(
                pulsePen,
                center.X - pulseRadius,
                center.Y - pulseRadius,
                pulseRadius * 2,
                pulseRadius * 2);
        }

        using var fillBrush = new SolidBrush(Color.FromArgb(15, 23, 31));
        using var borderPen = new Pen(color, 2);
        using var nameBrush = new SolidBrush(Color.FromArgb(232, 239, 246));
        using var countBrush = new SolidBrush(Color.FromArgb(139, 158, 178));

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
            using var nodeBorder = new Pen(Color.FromArgb(225, 236, 246), 1);
            g.FillEllipse(nodeBrush, point.X - 5, point.Y - 5, 10, 10);
            g.DrawEllipse(nodeBorder, point.X - 5, point.Y - 5, 10, 10);

            _hostHitRegions.Add((new Rectangle(point.X - 10, point.Y - 10, 20, 20), host));
            DrawHostNodeCaption(g, host, point, angle, topologyBounds);
        }

        if (site.Hosts.Count > visible)
        {
            using var moreBrush = new SolidBrush(Color.FromArgb(152, 169, 186));
            string more = $"+{site.Hosts.Count - visible} more";
            var size = g.MeasureString(more, _tinyFont);

            var rect = ClampToBounds(
                new Rectangle(
                    siteCenter.X - (int)size.Width / 2 - 4,
                    siteCenter.Y + baseOrbitRadius + 18,
                    (int)size.Width + 8,
                    (int)size.Height + 4),
                topologyBounds);

            using var fill = new SolidBrush(Color.FromArgb(205, 8, 14, 20));
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
        int primaryHeight = TextRenderer.MeasureText(g, primary, _hostLabelFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;

        bool rightSide = Math.Cos(angle) >= 0;
        int x = rightSide
            ? point.X + 9
            : point.X - width - 9;
        int y = point.Y - height / 2;

        // The other side is still attached to the same node, even when the
        // caption width is larger than the local displacement limit.
        int alternateX = rightSide ? point.X - width - 9 : point.X + 9;
        if (!TopologyLayout.TryPlaceCaption(new Rectangle(x, y, width, height), topologyBounds,
            _captionObstacles, out var rect) &&
            !TopologyLayout.TryPlaceCaption(new Rectangle(alternateX, y, width, height), topologyBounds,
                _captionObstacles, out rect))
        {
            _hiddenCaptions++;
            return;
        }
        _captionObstacles.Add(rect);
        _hostHitRegions.Add((rect, host));
        using (var leader = new Pen(StateColor(host.State, 120), 1))
            g.DrawLine(leader, point, new Point(Math.Clamp(point.X, rect.Left, rect.Right),
                Math.Clamp(point.Y, rect.Top, rect.Bottom)));

        using var fill = new SolidBrush(Color.FromArgb(220, 7, 13, 19));
        using var border = new Pen(StateColor(host.State, 115), 1);
        using var primaryBrush = new SolidBrush(Color.FromArgb(232, 240, 247));
        using var secondaryBrush = new SolidBrush(Color.FromArgb(139, 158, 178));

        g.FillRectangle(fill, rect);
        g.DrawRectangle(border, rect);

        TextRenderer.DrawText(
            g,
            primary,
            _hostLabelFont,
            new Rectangle(rect.X + 5, rect.Y + 3, rect.Width - 10, primaryHeight),
            primaryBrush.Color,
            (secondary is null ? TextFormatFlags.Default : TextFormatFlags.EndEllipsis) | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        if (secondary is not null)
        {
            TextRenderer.DrawText(
                g,
                secondary,
                _hostIpFont,
                new Rectangle(rect.X + 5, rect.Y + 3 + primaryHeight, rect.Width - 10, rect.Height - primaryHeight - 6),
                secondaryBrush.Color,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    // Measure with the same GDI renderer and flags used for drawing. Addresses never
    // inherit the nickname width cap, including IPv6 and larger display scaling.
    internal static Size MeasureHostNodeCaption(Graphics g, string primary, string? secondary,
        Font primaryFont, Font addressFont)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var primarySize = TextRenderer.MeasureText(g, primary, primaryFont, Size.Empty, flags);
        var addressSize = secondary is null ? Size.Empty : TextRenderer.MeasureText(g, secondary, addressFont, Size.Empty, flags);
        int primaryWidth = secondary is null ? primarySize.Width : Math.Min(primarySize.Width, 180);
        return new Size(Math.Max(62, Math.Max(primaryWidth, addressSize.Width) + 12),
            Math.Max(22, primarySize.Height + addressSize.Height + 6));
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

    private void DrawSidebar(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        int x = rect.X + 16;
        int y = rect.Y + 13;
        int width = rect.Width - 32;

        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));
        using var primaryBrush = new SolidBrush(Color.FromArgb(226, 234, 242));
        using var mutedBrush = new SolidBrush(Color.FromArgb(126, 143, 160));
        using var dividerPen = new Pen(Color.FromArgb(31, 44, 57));

        g.DrawString("ACTIVE OUTAGES", _sectionFont, titleBrush, x, y);
        y += 28;

        var outages = snapshot.Sites
            .SelectMany(s => s.Hosts)
            .Where(h => h.State == HostState.Offline)
            .OrderBy(h => h.OutageStarted)
            .Take(7)
            .ToList();

        if (outages.Count == 0)
        {
            using var okBrush = new SolidBrush(Color.FromArgb(87, 207, 142));
            g.DrawString(
                snapshot.Monitoring ? "No active outages" : "Monitoring is idle",
                _smallFont,
                okBrush,
                x,
                y);
            y += 31;
        }
        else
        {
            foreach (var outage in outages)
            {
                _sidebarHitRegions.Add((Rectangle.Intersect(rect, new Rectangle(x, y, width, 44)), outage));
                using var redBrush = new SolidBrush(Color.FromArgb(255, 110, 119));
                string display = string.IsNullOrWhiteSpace(outage.Label)
                    ? outage.Address
                    : outage.Label;

                g.DrawString(display, _siteFont, redBrush, x, y);
                y += 18;

                string detail = outage.OutageStarted.HasValue
                    ? $"{outage.Site} • down {FormatElapsed(DateTime.Now - outage.OutageStarted.Value)}"
                    : outage.Site;

                g.DrawString(detail, _tinyFont, mutedBrush, x, y);
                y += 26;
            }
        }

        g.DrawLine(dividerPen, x, y + 3, x + width, y + 3);
        y += 20;
        g.DrawString("OUTAGE HISTORY", _sectionFont, titleBrush, x, y);

        string historyFilter = snapshot.HideSuspectEvents
            ? $"{snapshot.EventWindowLabel} • suspects hidden"
            : $"{snapshot.EventWindowLabel} • suspects shown";

        var filterSize = g.MeasureString(historyFilter, _tinyFont);
        g.DrawString(
            historyFilter,
            _tinyFont,
            mutedBrush,
            Math.Max(x, x + width - filterSize.Width),
            y + 1);

        y += 27;

        if (snapshot.Events.Count == 0)
        {
            g.DrawString(
                "No events match the saved filters.",
                _smallFont,
                mutedBrush,
                x,
                y);
        }

        foreach (var item in snapshot.Events
            .OrderByDescending(e => e.Timestamp)
            .Take(10))
        {
            var target = snapshot.Sites.SelectMany(site => site.Hosts).FirstOrDefault(host =>
                host.Site.Equals(item.Site, StringComparison.OrdinalIgnoreCase) &&
                host.Address.Equals(item.Address, StringComparison.OrdinalIgnoreCase));
            if (target != null)
                _sidebarHitRegions.Add((Rectangle.Intersect(rect, new Rectangle(x, y, width, 57)), target));
            Color kindColor = item.Kind switch
            {
                "DOWN" => Color.FromArgb(255, 101, 111),
                "RECOVERED" => Color.FromArgb(78, 216, 143),
                "SUSPECT" => Color.FromArgb(245, 191, 71),
                _ => Color.FromArgb(152, 170, 188)
            };

            using var kindBrush = new SolidBrush(kindColor);

            g.DrawString(
                DisplayTime.ShortTimestamp(item.Timestamp),
                _tinyFont,
                mutedBrush,
                x,
                y);

            g.DrawString(
                item.Kind,
                _tinyFont,
                kindBrush,
                x + 76,
                y);

            y += 16;

            string host = item.Host;
            g.DrawString(
                TrimToWidth(g, host, _smallFont, width),
                _smallFont,
                primaryBrush,
                x,
                y);

            y += 17;

            g.DrawString(
                TrimToWidth(g, item.Site, _tinyFont, width),
                _tinyFont,
                mutedBrush,
                x,
                y);

            y += 24;

            if (y > rect.Bottom - 35)
                break;
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

        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));
        using var hintBrush = new SolidBrush(Color.FromArgb(84, 111, 128));
        using var idleBrush = new SolidBrush(Color.FromArgb(98, 116, 133));
        using var successBrush = new SolidBrush(Color.FromArgb(100, 220, 132));
        using var failureBrush = new SolidBrush(Color.FromArgb(255, 111, 116));

        g.DrawString("LIVE CLI / CMD TRACE", _sectionFont, titleBrush, x, y);

        string hint = "C  HIDE";
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
        using var mutedBrush = new SolidBrush(Color.FromArgb(90, 107, 125));
        string left = ShowCli
            ? "ESC Main   O Operations   P Start/Stop   C Hide CLI   H Range   S Suspects"
            : "ESC Main   O Operations   P Start/Stop   C Show CLI   H Range   S Suspects";
        string right = "© 2026 Joseph Luker • All rights reserved.";

        g.DrawString(left, _tinyFont, mutedBrush, 23, ClientSize.Height - 25);

        var size = g.MeasureString(right, _tinyFont);
        g.DrawString(
            right,
            _tinyFont,
            mutedBrush,
            ClientSize.Width - size.Width - 24,
            ClientSize.Height - 25);
    }

    internal static HostState AggregateSiteState(WallboardSiteSnapshot site)
    {
        int total = site.Hosts.Count;

        if (total == 0)
            return HostState.Unknown;

        int online = site.Hosts.Count(h => h.State == HostState.Online);
        int offline = site.Hosts.Count(h => h.State == HostState.Offline);
        int suspect = site.Hosts.Count(h => h.State == HostState.Suspect);

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
        string value = state switch {
            HostState.Online => Presentation.WallboardOnline,
            HostState.Suspect => Presentation.WallboardSuspect,
            HostState.Offline => Presentation.WallboardOffline,
            _ => Presentation.WallboardUnknown
        };
        return Color.FromArgb(alpha, ColorTranslator.FromHtml(value));
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
