using PingWatchdog.Shared;

namespace PingWatchdog;

internal sealed class EventHistoryForm : Form
{
    private readonly Func<IReadOnlyList<StateEventRecord>> _eventProvider;
    private readonly Func<EventHistoryPreferences> _preferencesProvider;
    private readonly Action<int, bool> _preferencesChanged;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };

    private readonly ComboBox _windowCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 140
    };

    private readonly ComboBox _siteCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 170
    };

    private readonly CheckBox _hideSuspects = new()
    {
        Text = "Hide suspect events",
        AutoSize = true
    };

    private readonly Label _countLabel = new()
    {
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight
    };

    private readonly Button _exportButton = new()
    {
        Text = "Export CSV",
        AutoSize = true
    };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
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

        Text = "Outage History • Ping Watchdog";
        Width = 1080;
        Height = 650;
        MinimumSize = new Size(820, 480);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(11, 16, 23);
        ForeColor = Color.FromArgb(232, 238, 245);

        BuildGrid();
        BuildLayout();
        ApplyTheme();

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
            Padding = new Padding(18),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
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

        var titleStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        titleStack.Controls.Add(new Label
        {
            Text = "Outage History",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold),
            ForeColor = Color.White
        });
        titleStack.Controls.Add(new Label
        {
            Text = "Persistent availability events across monitoring sessions",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 153, 169)
        });

        _countLabel.Margin = new Padding(12, 20, 0, 0);
        titlePanel.Controls.Add(titleStack, 0, 0);
        titlePanel.Controls.Add(_countLabel, 1, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 7),
            Margin = new Padding(0)
        };

        filters.Controls.Add(FilterLabel("Range"));
        filters.Controls.Add(_windowCombo);
        filters.Controls.Add(FilterLabel("Site"));
        filters.Controls.Add(_siteCombo);
        _hideSuspects.Margin = new Padding(14, 8, 14, 0);
        filters.Controls.Add(_hideSuspects);
        _exportButton.Margin = new Padding(4, 2, 0, 0);
        filters.Controls.Add(_exportButton);

        root.Controls.Add(titlePanel, 0, 0);
        root.Controls.Add(filters, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        Controls.Add(root);
    }

    private static Label FilterLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(0, 8, 4, 0),
            ForeColor = Color.FromArgb(139, 153, 169)
        };
    }

    private void ApplyTheme()
    {
        var card = Color.FromArgb(15, 22, 30);
        var input = Color.FromArgb(20, 28, 38);
        var text = Color.FromArgb(232, 238, 245);
        var muted = Color.FromArgb(139, 153, 169);
        var border = Color.FromArgb(39, 49, 61);

        foreach (var combo in new[] { _windowCombo, _siteCombo })
        {
            combo.BackColor = input;
            combo.ForeColor = text;
            combo.FlatStyle = FlatStyle.Flat;
        }

        _hideSuspects.ForeColor = text;

        _exportButton.FlatStyle = FlatStyle.Flat;
        _exportButton.FlatAppearance.BorderColor = border;
        _exportButton.BackColor = Color.FromArgb(25, 34, 45);
        _exportButton.ForeColor = text;

        _countLabel.ForeColor = muted;

        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = card;
        _grid.GridColor = Color.FromArgb(31, 40, 50);
        _grid.DefaultCellStyle.BackColor = card;
        _grid.DefaultCellStyle.ForeColor = text;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(35, 73, 108);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(13, 20, 28);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 31, 42);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = muted;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(22, 31, 42);
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
                e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                e.Kind,
                e.Site,
                e.DisplayHost,
                e.Message))
            .ToList();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            string kind = row.Cells["KindColumn"].Value?.ToString() ?? string.Empty;
            row.Cells["KindColumn"].Style.Font =
                new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            row.Cells["KindColumn"].Style.ForeColor = kind switch
            {
                "DOWN" => Color.FromArgb(255, 110, 119),
                "RECOVERED" => Color.FromArgb(87, 207, 142),
                "SUSPECT" => Color.FromArgb(245, 191, 71),
                _ => Color.FromArgb(160, 176, 192)
            };

            if (kind == "DOWN")
                row.DefaultCellStyle.BackColor = Color.FromArgb(39, 24, 29);
        }

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
                    Csv(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
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
