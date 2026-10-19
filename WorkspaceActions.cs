namespace PingWatchdog;

public sealed partial class MainForm
{
    private readonly ContextMenuStrip _siteActionsMenu = new();
    private readonly ContextMenuStrip _columnsMenu = new();

    private void PopulateSiteActions()
    {
        _siteActionsMenu.Items.Clear();
        _siteActionsMenu.Items.Add("Add site…", null, (_, _) => AddSite());
        _siteActionsMenu.Items.Add("Organize sites…", null, (_, _) => OpenOrganization());
        var rename = _siteActionsMenu.Items.Add("Rename site…", null, (_, _) => RenameSite());
        rename.Enabled = _selectedSiteName is not null;
        _siteActionsMenu.Items.Add(new ToolStripSeparator());
        var delete = _siteActionsMenu.Items.Add("Delete site…", null, (_, _) => DeleteSite());
        delete.Enabled = _selectedSiteName is not null;
        AuroraTheme.ApplyMenu(_siteActionsMenu);
    }

    private void ShowSiteActions()
    {
        PopulateSiteActions();
        _siteActionsMenu.Show(_siteList, new Point(0, _siteList.Height));
    }

    private void ShowColumnMenu()
    {
        _columnsMenu.Items.Clear();
        foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
        {
            if (column.Name == "SiteColumn") continue; // Site follows the navigation scope.
            var item = new ToolStripMenuItem(column.HeaderText) { Checked = column.Visible, CheckOnClick = true,
                Enabled = column.Name is not "HostColumn" and not "StatusColumn" };
            item.CheckedChanged += (_, _) => column.Visible = item.Checked;
            _columnsMenu.Items.Add(item);
        }
        AuroraTheme.ApplyMenu(_columnsMenu);
        _columnsMenu.Show(_settingsFlowPanel!, new Point(0, _settingsFlowPanel!.Height));
    }
}
