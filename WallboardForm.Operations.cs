using PingWatchdog.Shared;

namespace PingWatchdog;

// The Wallboard's operations drawer: sites, hosts, timing and their actions.
internal sealed partial class WallboardForm
{
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
}
