using PingWatchdog.Shared;

namespace PingWatchdog;

internal sealed class EventHistoryForm : Form
{
    // FOCAL: outage records. Compact opaque surface with wrapping filters.
    // Event type text is read, so it sits at the 13 logical-px floor (9.75pt), not below it.
    internal static readonly Font KindFont = new("Segoe UI Semibold", 9.75f, FontStyle.Bold);
    private readonly Func<IReadOnlyList<StateEventRecord>> _eventProvider;
    private readonly Func<EventHistoryPreferences> _preferencesProvider;
    private readonly Action<int, bool> _preferencesChanged;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };

    private readonly ComboBox _windowCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 140, AccessibleName = "History range", TabIndex = 0
    };

    private readonly ComboBox _siteCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 170, AccessibleName = "History site", TabIndex = 0
    };

    private readonly CheckBox _hideSuspects = new()
    {
        Text = "Hide suspect events",
        AutoSize = true, TabIndex = 2
    };

    private readonly Label _countLabel = new()
    {
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight
    };

    private readonly Button _exportButton = new AuroraButton()
    {
        Text = "Export CSV",
        AutoSize = true, TabIndex = 3
    };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Outage history", TabIndex = 2,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BorderStyle = BorderStyle.None
    };

    private int _lastEventCount = -1;
    private long _lastNewestTicks = -1;
    private bool _filterDirty = true;

    public EventHistoryForm(
        Func<IReadOnlyList<StateEventRecord>> eventProvider,
        Func<EventHistoryPreferences> preferencesProvider,
        Action<int, bool> preferencesChanged)
    {
        _eventProvider = eventProvider;
        _preferencesProvider = preferencesProvider;
        _preferencesChanged = preferencesChanged;

        Text = "Outage history • Ping Watchdog";
        Width = 1080;
        Height = 650;
        MinimumSize = new Size(820, 480);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = AuroraTheme.LabelFont;
        BackColor = Color.FromArgb(11, 16, 23);
        ForeColor = Color.FromArgb(232, 238, 245);

        BuildGrid();
        BuildLayout();
        AuroraTheme.Register(this);
        AuroraTheme.Changed += ApplyEventColors;
        Disposed += (_, _) => AuroraTheme.Changed -= ApplyEventColors;

        _windowCombo.Items.AddRange(Presentation.HistoryRanges.Cast<object>().ToArray());

        var preferences = _preferencesProvider();
        _windowCombo.SelectedIndex = HoursToIndex(preferences.WindowHours);
        _hideSuspects.Checked = preferences.HideSuspects;

        _windowCombo.SelectedIndexChanged += (_, _) =>
        {
            _filterDirty = true;
            SavePreferences();
            RefreshHistory(force: true);
        };

        _hideSuspects.CheckedChanged += (_, _) =>
        {
            _filterDirty = true;
            SavePreferences();
            RefreshHistory(force: true);
        };

        _siteCombo.SelectedIndexChanged += (_, _) =>
        {
            _filterDirty = true;
            RefreshHistory(force: true);
        };

        _exportButton.Click += (_, _) => ExportCsv();
        _refreshTimer.Tick += (_, _) => RefreshHistory();

        Shown += (_, _) =>
        {
            RefreshHistory(force: true);
            _refreshTimer.Start();
        };

        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        };
    }

    public void RefreshNow()
    {
        _filterDirty = true;
        RefreshHistory(force: true);
    }

    private void BuildGrid()
    {
        _grid.RowTemplate.Height = Presentation.TableRowHeight;
        _grid.ColumnHeadersHeight = Presentation.TableHeaderHeight;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        foreach (var column in Presentation.HistoryColumns)
        {
            var cell = new DataGridViewTextBoxColumn { HeaderText = column.Header, DataPropertyName = column.Key };
            if (column.Key is "Timestamp" or "Kind") cell.Name = column.Key + "Column";
            if (column.Weight > 0) { cell.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; cell.FillWeight = (float)column.Weight; }
            else cell.Width = column.Width;
            _grid.Columns.Add(cell);
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(AuroraTheme.Space4),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        titlePanel.AutoSize = true;
        titlePanel.Margin = new Padding(0, 0, 0, AuroraTheme.Space3);
        titlePanel.Controls.Add(new Label
        {
            Text = "Outage history", AutoSize = true, Tag = "title",
            Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold), Margin = Padding.Empty
        }, 0, 0);
        _countLabel.Margin = new Padding(12, 4, 0, 0);
        titlePanel.Controls.Add(_countLabel, 1, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabIndex = 1,
            Padding = Padding.Empty,
            Margin = new Padding(0, 0, 0, AuroraTheme.Space3)
        };

        filters.Controls.Add(FilterGroup("Range", _windowCombo, 0));
        filters.Controls.Add(FilterGroup("Site", _siteCombo, 1));
        _hideSuspects.Margin = new Padding(0, 8, 12, 4);
        filters.Controls.Add(_hideSuspects);
        _exportButton.Margin = new Padding(4, 2, 0, 0);
        filters.Controls.Add(_exportButton);

        root.Controls.Add(titlePanel, 0, 0);
        root.Controls.Add(filters, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        Controls.Add(root);
    }

    private static TableLayoutPanel FilterGroup(string label, ComboBox combo, int tabIndex)
    {
        var group = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 0, 12, 4), TabIndex = tabIndex
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, combo.Width + 8));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.Controls.Add(new Label
        {
            Text = label, AutoSize = true, Padding = new Padding(0, 8, 4, 0)
        }, 0, 0);
        group.Controls.Add(combo, 1, 0);
        return group;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    private void ApplyEventColors()
    {
        if (IsDisposed || Disposing) return;
        var palette = AuroraTheme.Palette;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string kind = row.Cells["KindColumn"].Value?.ToString()?.ToUpperInvariant() ?? string.Empty;
            var cell = row.Cells["KindColumn"];
            cell.Style.Font = KindFont;
            cell.Style.ForeColor = palette.StatusText(kind switch
            {
                "DOWN" => palette.Danger, "RECOVERED" => palette.Success,
                "SUSPECT" => palette.Warning, _ => palette.Secondary
            });
            cell.Style.SelectionForeColor = palette.Text;
            row.DefaultCellStyle.BackColor = palette.Surface;
        }
    }

    internal void CheckLayout()
    {
        PerformLayout();
        foreach (Control control in new Control[] { _windowCombo, _siteCombo, _hideSuspects, _exportButton })
        {
            if (!control.Visible || !control.Parent!.ClientRectangle.Contains(control.Bounds))
                throw new InvalidOperationException($"History filter/action is clipped: {control.Text}; visible={control.Visible}, bounds={control.Bounds}, parent={control.Parent?.ClientRectangle}.");
            var group = control.Parent!;
            if (group.Parent is FlowLayoutPanel && !group.Parent.ClientRectangle.Contains(group.Bounds))
                throw new InvalidOperationException("History filter group is clipped.");
        }
        if (_exportButton.Enabled != (_grid.Rows.Count > 0))
            throw new InvalidOperationException("History export must reflect whether any events are shown.");
        foreach (DataGridViewRow row in _grid.Rows)
            if (AuroraTheme.ContrastRatio(row.Cells["KindColumn"].Style.ForeColor, row.DefaultCellStyle.BackColor) < 4.5)
                throw new InvalidOperationException("History event text does not meet contrast.");
    }

    private void SavePreferences()
    {
        if (_windowCombo.SelectedIndex < 0)
            return;

        _preferencesChanged(
            IndexToHours(_windowCombo.SelectedIndex),
            _hideSuspects.Checked);
    }

    private void RefreshHistory(bool force = false)
    {
        if (IsDisposed || Disposing)
            return;

        var all = _eventProvider();
        long newestTicks = all.Count == 0 ? 0 : all.Max(e => e.Timestamp.Ticks);

        if (!force &&
            !_filterDirty &&
            all.Count == _lastEventCount &&
            newestTicks == _lastNewestTicks)
        {
            return;
        }

        _lastEventCount = all.Count;
        _lastNewestTicks = newestTicks;
        _filterDirty = false;

        RefreshSiteOptions(all);

        int hours = _windowCombo.SelectedIndex < 0
            ? _preferencesProvider().WindowHours
            : IndexToHours(_windowCombo.SelectedIndex);

        string selectedSite = _siteCombo.SelectedItem?.ToString() ?? "All sites";

        var filtered = MainForm.FilterStateEvents(
                all,
                hours,
                _hideSuspects.Checked,
                DateTime.Now)
            .Where(e =>
                selectedSite.Equals("All sites", StringComparison.OrdinalIgnoreCase) ||
                e.Site.Equals(selectedSite, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        _grid.DataSource = filtered
            .Select(e => new EventHistoryRow(
                DisplayTime.Timestamp(e.Timestamp),
                e.Kind,
                e.Site,
                e.DisplayHost,
                e.Message))
            .ToList();

        ApplyEventColors();
        _exportButton.Enabled = filtered.Count > 0;

        _countLabel.Text = $"{filtered.Count:N0} shown • {all.Count:N0} stored";
    }

    private void RefreshSiteOptions(IReadOnlyList<StateEventRecord> events)
    {
        string current = _siteCombo.SelectedItem?.ToString() ?? "All sites";

        var sites = events
            .Select(e => e.Site)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var expected = new[] { "All sites" }.Concat(sites).ToArray();

        if (_siteCombo.Items.Count == expected.Length &&
            _siteCombo.Items.Cast<object>().Select(x => x.ToString()).SequenceEqual(expected))
        {
            return;
        }

        _siteCombo.BeginUpdate();
        try
        {
            _siteCombo.Items.Clear();
            _siteCombo.Items.AddRange(expected);

            int index = Array.FindIndex(
                expected,
                s => s.Equals(current, StringComparison.OrdinalIgnoreCase));
            _siteCombo.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            _siteCombo.EndUpdate();
        }
    }

    private void ExportCsv()
    {
        var all = _eventProvider();
        int hours = _windowCombo.SelectedIndex < 0
            ? _preferencesProvider().WindowHours
            : IndexToHours(_windowCombo.SelectedIndex);
        string selectedSite = _siteCombo.SelectedItem?.ToString() ?? "All sites";

        var filtered = MainForm.FilterStateEvents(
                all,
                hours,
                _hideSuspects.Checked,
                DateTime.Now)
            .Where(e =>
                selectedSite.Equals("All sites", StringComparison.OrdinalIgnoreCase) ||
                e.Site.Equals(selectedSite, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Timestamp)
            .ToList();

        using var dialog = new SaveFileDialog
        {
            Title = "Export Ping Watchdog Outage History",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = $"ping-watchdog-outage-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true,
            DefaultExt = "csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName);
            writer.WriteLine("Timestamp,Event,Site,Host,Address,Details");

            foreach (var item in filtered)
            {
                writer.WriteLine(string.Join(",",
                    Csv(DisplayTime.Timestamp(item.Timestamp)),
                    Csv(item.Kind),
                    Csv(item.Site),
                    Csv(item.DisplayHost),
                    Csv(item.Host),
                    Csv(item.Message)));
            }

            _countLabel.Text = $"Exported {filtered.Count:N0} event(s)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not export outage history.\r\n\r\n{ex.Message}",
                "Ping Watchdog",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static int HoursToIndex(int hours)
    {
        return MainForm.NormalizeEventHistoryHours(hours) switch
        {
            24 => 0,
            168 => 1,
            720 => 2,
            _ => 3
        };
    }

    private static int IndexToHours(int index)
    {
        return index switch
        {
            0 => 24,
            1 => 168,
            2 => 720,
            _ => 0
        };
    }

    private sealed record EventHistoryRow(
        string Timestamp,
        string Kind,
        string Site,
        string Host,
        string Details);
}
