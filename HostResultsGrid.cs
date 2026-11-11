namespace PingWatchdog;

// Cell changes paint in one buffer, without erasing the table between ping results.
// Status reads as a colored dot plus the word (DESIGN.md A6/A8: never color alone).
internal sealed class HostResultsGrid : DataGridView
{
    internal HostResultsGrid() { DoubleBuffered = true; }

    internal static Color StatusDot(AuroraPalette p, string status) => status.Split(" · ")[0] switch
    {
        "Online" => p.Success,
        "Suspect" => p.Warning,
        "Offline" => p.Danger,
        _ => p.Muted
    };

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        base.OnCellPainting(e);
        if (e.Handled || e.RowIndex < 0 || e.ColumnIndex < 0 || Columns[e.ColumnIndex].Name != "StatusColumn" || e.Graphics is null) return;
        var p = AuroraTheme.Palette;
        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        string text = e.FormattedValue?.ToString() ?? "";
        float scale = DeviceDpi / 96f;
        int dot = Math.Max(6, (int)Math.Round(8 * scale));
        int x = e.CellBounds.X + (int)(AuroraTheme.Space2 * scale) + 4;
        int y = e.CellBounds.Y + (e.CellBounds.Height - dot) / 2;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(SystemInformation.HighContrast ? SystemColors.WindowText : StatusDot(p, text)))
            e.Graphics.FillEllipse(brush, x, y, dot, dot);
        var font = e.CellStyle?.Font ?? AuroraTheme.BodyFont;
        var textBounds = new Rectangle(x + dot + (int)(8 * scale), e.CellBounds.Y, Math.Max(1, e.CellBounds.Right - x - dot - (int)(12 * scale)), e.CellBounds.Height);
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        TextRenderer.DrawText(e.Graphics, text, font, textBounds, selected ? p.SelectionText : p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | AuroraTheme.NameTextFlags);
        e.Handled = true;
    }
}
