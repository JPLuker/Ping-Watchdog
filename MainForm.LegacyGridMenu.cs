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

// Older single-host label and group actions on the host table.
public sealed partial class MainForm
{
    private void BuildGridContextMenu()
    {
        _gridMenu.BackColor = Color.FromArgb(22, 27, 34);
        _gridMenu.ForeColor = Color.FromArgb(230, 237, 243);

        _gridMenu.Opening += (_, e) => e.Cancel = true;
        _grid.ContextMenuStrip = _gridMenu;
    }

    private void GridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;

        // Right-clicking inside a multi-selection acts on all of it.
        if (_grid.Rows[e.RowIndex].Selected && _grid.SelectedRows.Count > 1)
        {
            ShowSelectionMenu(_grid, _grid.PointToClient(Cursor.Position));
            return;
        }

        _grid.ClearSelection();
        _grid.Rows[e.RowIndex].Selected = true;

        if (e.ColumnIndex >= 0)
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var identity = GetSelectedHostIdentity();
        if (identity != null)
            ShowHostQuickActions(_grid, _grid.PointToClient(Cursor.Position), identity.Value.Site, identity.Value.Host);
    }

    private (string Site, string Host)? GetSelectedHostIdentity()
    {
        if (_grid.SelectedRows.Count == 0)
            return null;

        var row = _grid.SelectedRows[0];
        string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
        string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(host))
            return null;

        return (site, host);
    }

    private void SetLabelForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        string current = GetNickname(identity.Value.Site, identity.Value.Host);

        using var dialog = new NicknameDialog(identity.Value.Host, current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetNicknameValue(identity.Value.Site, identity.Value.Host, dialog.Nickname);
        SaveSites();
        RefreshGrid();
        RebuildCommandView();
    }

    private void ClearLabelForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        SetNicknameValue(identity.Value.Site, identity.Value.Host, string.Empty);
        SaveSites();
        RefreshGrid();
        RebuildCommandView();
    }

    private void SetCategoryForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        string current = GetCategory(identity.Value.Site, identity.Value.Host);

        using var dialog = new CategoryDialog(identity.Value.Host, current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetCategoryValue(identity.Value.Site, identity.Value.Host, dialog.Category);
        SaveSites();
        RefreshGrid();
    }

    private void ClearCategoryForSelectedHost()
    {
        var identity = GetSelectedHostIdentity();
        if (identity is null)
            return;

        SetCategoryValue(identity.Value.Site, identity.Value.Host, string.Empty);
        SaveSites();
        RefreshGrid();
    }
}
