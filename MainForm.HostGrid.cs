using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Velopack;
using Velopack.Sources;
using PingWatchdog.Shared;

namespace PingWatchdog;

// The host table under List: columns, refreshing rows in place and status styling.
public sealed partial class MainForm
{
    private void BuildGrid()
    {
        _grid.RowTemplate.Height = Presentation.TableRowHeight;
        _grid.ColumnHeadersHeight = Presentation.TableHeaderHeight;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        foreach (var spec in Presentation.HostColumns)
        {
            var column = new DataGridViewTextBoxColumn {
                Name = spec.Key + "Column", HeaderText = spec.Header,
                DataPropertyName = spec.Key == "Outage" ? "OutageSince" : spec.Key
            };
            if (spec.Weight > 0) {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.FillWeight = (float)spec.Weight; column.MinimumWidth = spec.Width;
            } else column.Width = spec.Width;
            _grid.Columns.Add(column);
        }

        var categoryColumn = new DataGridViewTextBoxColumn
        {
            Name = "CategoryColumn",
            HeaderText = "Category",
            DataPropertyName = "Category",
            Width = 120,
            MinimumWidth = 90
        };
        int labelIndex = _grid.Columns["LabelColumn"]?.Index ?? 1;
        _grid.Columns.Insert(Math.Min(labelIndex + 1, _grid.Columns.Count), categoryColumn);
        foreach (DataGridViewColumn column in _grid.Columns)
            column.Visible = column.Name is "SiteColumn" or "HostColumn" or "LabelColumn" or "StatusColumn" or "LatencyColumn";
        // Ordering follows site/address, as it did with the former read-only bound list.
        foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.Columns["HostColumn"].HeaderText = "Address";
        _grid.Columns["LabelColumn"].HeaderText = "Name";
        _grid.Columns["HostColumn"].DisplayIndex = 0;
        _grid.Columns["LabelColumn"].DisplayIndex = 1;
        _grid.Columns["HostColumn"].DefaultCellStyle.Font = new Font("Consolas", 10.5f);
        _grid.Columns["SiteColumn"].DisplayIndex = 2;
        _grid.Columns["LatencyColumn"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (var name in new[] { "Host", "Label", "Site", "Status", "Latency" })
        {
            var column = _grid.Columns[name + "Column"];
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.MinimumWidth = name switch { "Host" => 140, "Label" => 96, "Site" => 80, "Status" => 76, _ => 64 };
            column.FillWeight = name switch { "Host" => 30, "Label" => 24, "Site" => 19, "Status" => 16, _ => 13 };
        }
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) RenameSelectedHost(); };

    }

    private void RefreshGrid()
    {
        using var activity = UiActivity.Begin("Refreshing hosts");
        // The active-session UI timer also resumes expired snoozes, including when all hosts are paused.
        ReconcileMonitoringWithConfig();
        _monitorCanvas.Snapshot = BuildWallboardSnapshot();
        _monitorCanvas.Invalidate();
        var selection = GetSelectedHostIdentity();
        // Keep every selected host selected across refreshes (List editing works on multi-selections).
        var selectedKeys = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => BuildHostKey(r.Cells["SiteColumn"].Value?.ToString() ?? "", r.Cells["HostColumn"].Value?.ToString() ?? ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? selectedColumn = _grid.CurrentCell?.OwningColumn.Name;
        int firstDisplayedRow = _grid.FirstDisplayedScrollingRowIndex;
        int horizontalOffset = _grid.HorizontalScrollingOffset;
        var configuredHosts = HostSnapshot().ToDictionary(h => BuildHostKey(h.Site, h.Address), StringComparer.OrdinalIgnoreCase);
        var monitors = _hosts.Values.ToDictionary(h => BuildHostKey(h.Site, h.Address), StringComparer.OrdinalIgnoreCase);
        foreach (var host in configuredHosts.Values)
            monitors.TryAdd(BuildHostKey(host.Site, host.Address), new HostMonitor(host.Site, host.Address));
        var rows = monitors.Values
            .Where(h =>
                _selectedSiteName is null ||
                h.Site.Equals(
                    _selectedSiteName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(h =>
            {
                lock (h)
                {
                    configuredHosts.TryGetValue(BuildHostKey(h.Site, h.Address), out var saved);
                    string health = _cts is null ? "Idle" : h.State.ToString();
                    string statusText = (saved?.Options.Enabled == false ? "Disabled" :
                        saved?.Options.IsSnoozed(DateTimeOffset.UtcNow) == true ? "Snoozed" : health) +
                        (saved?.Options.Priority == true ? " · Priority" : "");
                    return new
                    {
                        Site = h.Site,
                        Host = h.Address,
                        Label = GetNickname(h.Site, h.Address),
                        Category = GetCategory(h.Site, h.Address),
                        Status = statusText,
                        Latency = _cts is not null && h.LastRoundTripMs.HasValue
                            ? $"{h.LastRoundTripMs} ms"
                            : "—",
                        Failures = h.ConsecutiveFailures,
                        LastReply = (h.LastReply is DateTime reply ? DisplayTime.Timestamp(reply) : null) ?? "—",
                        OutageSince = (h.OutageStarted is DateTime outage ? DisplayTime.Timestamp(outage) : null) ?? "—"
                    };
                }
            })
            .OrderBy(h => h.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string query = _hostSearch.Text.Trim();
        if (query.Length > 0)
            rows = rows.Where(h => h.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                h.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                h.Site.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        UpdateFilterChips(rows.Select(h => h.Status));
        rows = rows.Where(h => MatchesFilter(_hostFilter, h.Status)).ToList();
        // Preserve native rows, selection and scroll position on ordinary ping refreshes.
        bool structureChanged = _grid.Rows.Count != rows.Count || rows.Where((host, index) =>
            !string.Equals(_grid.Rows[index].Cells["SiteColumn"].Value?.ToString(), host.Site, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_grid.Rows[index].Cells["HostColumn"].Value?.ToString(), host.Host, StringComparison.OrdinalIgnoreCase)).Any();
        _refreshingHostRows = true;
        try
        {
            if (structureChanged)
            {
                _grid.Rows.Clear();
                if (rows.Count > 0) _grid.Rows.Add(rows.Count);
            }
            for (int index = 0; index < rows.Count; index++)
            {
                var host = rows[index];
                var row = _grid.Rows[index];
                void Set(string column, object value)
                {
                    if (!Equals(row.Cells[column].Value, value)) row.Cells[column].Value = value;
                }
                Set("SiteColumn", host.Site); Set("HostColumn", host.Host);
                Set("LabelColumn", host.Label); Set("CategoryColumn", host.Category);
                Set("StatusColumn", host.Status); Set("LatencyColumn", host.Latency);
                Set("FailuresColumn", host.Failures); Set("LastReplyColumn", host.LastReply);
                Set("OutageColumn", host.OutageSince);
            }
        }
        finally { _refreshingHostRows = false; }
        _emptyHosts.Text = query.Length > 0 ? "No matching hosts" : EmptyFilterText();
        _emptyHosts.Visible = rows.Count == 0;
        if (_emptyHosts.Visible) _emptyHosts.BringToFront();

        if (_grid.Columns["SiteColumn"] is DataGridViewColumn siteColumn)
            siteColumn.Visible = _selectedSiteName is null;

        if (structureChanged)
        {
            _refreshingHostRows = true;
            try
            {
                // Restore after membership changes and setting column visibility.
                _grid.ClearSelection();
                _grid.CurrentCell = null;
                if (selection is { } desired)
                {
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        if (!string.Equals(row.Cells["SiteColumn"].Value?.ToString(), desired.Site, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(row.Cells["HostColumn"].Value?.ToString(), desired.Host, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var column = selectedColumn is not null ? _grid.Columns[selectedColumn] : null;
                        if (column?.Visible != true)
                            column = _grid.Columns["HostColumn"];
                        _grid.CurrentCell = row.Cells[column!.Index];
                        row.Selected = true;
                        break;
                    }
                }
                foreach (DataGridViewRow row in _grid.Rows)
                    if (selectedKeys.Contains(BuildHostKey(row.Cells["SiteColumn"].Value?.ToString() ?? "", row.Cells["HostColumn"].Value?.ToString() ?? "")))
                        row.Selected = true;
                if (firstDisplayedRow >= 0 && _grid.Rows.Count > 0)
                    _grid.FirstDisplayedScrollingRowIndex = Math.Min(firstDisplayedRow, _grid.Rows.Count - 1);
                _grid.HorizontalScrollingOffset = horizontalOffset;

            }
            finally { _refreshingHostRows = false; }
        }
        UpdateSelectionActions();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var status = row.Cells["StatusColumn"].Value?.ToString()?.Split(" · ")[0];
            var cell = row.Cells["StatusColumn"];

            // HostResultsGrid paints the dot and word; the font only sizes the row.
            cell.Style.Font = _statusCellFont;
            cell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;

            row.DefaultCellStyle.BackColor = status == "Offline" ? AppTheme.Palette.Surface2 : Color.Empty;
        }

        int online = rows.Count(h => h.Status.StartsWith("Online", StringComparison.Ordinal));
        int suspect = rows.Count(h => h.Status.StartsWith("Suspect", StringComparison.Ordinal));
        int offline = rows.Count(h => h.Status.StartsWith("Offline", StringComparison.Ordinal));

        int configured = _selectedSiteName is null
            ? _sites.Sum(site => site.Hosts.Count(address => GetHostOptions(site, address).Enabled))
            : (FindSite(_selectedSiteName) is SiteDefinition selectedSite
                ? selectedSite.Hosts.Count(address => GetHostOptions(selectedSite, address).Enabled)
                : 0);

        _startButton.Text = _cts is null ? "Start monitoring" : "Stop monitoring";
        _startButton.Enabled = true;
        _totalValueLabel.Text = (_cts is null ? configured : rows.Count).ToString();
        _onlineValueLabel.Text = online.ToString();
        _suspectValueLabel.Text = suspect.ToString();
        _offlineValueLabel.Text = offline.ToString();

        string view = _selectedSiteName ?? AllSitesLabel;

        _statusLabel.Text = _cts is null ? "Stopped" :
            $"Monitoring {_selectedSiteName ?? "all sites"} · {_hostTokens.Count} active hosts";
    }
}
