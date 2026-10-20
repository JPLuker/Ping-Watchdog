namespace PingWatchdog;

internal sealed partial class WallboardCanvas
{
    private readonly ToolTip _hostTip = new();
    private string _lastHostTip = "";
    internal bool AllHostNodesFit => _hostHitRegions.All(hit => new Rectangle(Point.Empty, _topologyViewport.Size).Contains(hit.Bounds));
    internal int VisibleHostCount => _hostCaptionRegions.Count;

    private void DrawFittedTopology(Graphics g, WallboardSnapshot snapshot, Rectangle bounds)
    {
        // FOCAL: every selected host. Reflow the groups and nodes instead of creating a scrollable canvas.
        var sites = snapshot.Sites.Where(site => site.Hosts.Count > 0).ToList();
        if (sites.Count == 0)
        {
            using var empty = new SolidBrush(WallboardStyle.Muted);
            g.DrawString("No hosts", _sectionFont, empty, 16, 16);
            return;
        }
        float scale = DeviceDpi / 96f, gap = 16 * scale, padding = 12 * scale;
        float line = _hostIpFont.GetHeight(g);
        float heading = _siteFont.GetHeight(g) + 16 * scale;
        int bestGroups = 1;
        double bestScore = -1;
        int[] bestColumns = new int[sites.Count];
        for (int groupColumns = 1; groupColumns <= sites.Count; groupColumns++)
        {
            int groupRows = (int)Math.Ceiling(sites.Count / (double)groupColumns);
            float groupWidth = (bounds.Width - gap * (groupColumns - 1)) / groupColumns;
            float groupHeight = (bounds.Height - gap * (groupRows - 1)) / groupRows;
            if (groupWidth < 80 * scale || groupHeight <= heading) continue;
            double score = double.MaxValue;
            var columns = new int[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                double nodeScore = -1;
                float addressWidth = sites[i].Hosts.Select(host => g.MeasureString(host.Address, _hostIpFont, int.MaxValue, StringFormat.GenericTypographic).Width).Max() + 24 * scale;
                for (int c = 1; c <= sites[i].Hosts.Count; c++)
                {
                    int rows = (int)Math.Ceiling(sites[i].Hosts.Count / (double)c);
                    double fit = Math.Min((groupWidth - padding * 2) / c / addressWidth,
                        (groupHeight - heading - padding) / rows / (line * 2 + 8 * scale));
                    if (fit > nodeScore) { nodeScore = fit; columns[i] = c; }
                }
                score = Math.Min(score, nodeScore);
            }
            if (score > bestScore) { bestScore = score; bestGroups = groupColumns; bestColumns = columns; }
        }
        int rowsOfGroups = (int)Math.Ceiling(sites.Count / (double)bestGroups);
        float blockWidth = (bounds.Width - gap * (bestGroups - 1)) / bestGroups;
        float blockHeight = (bounds.Height - gap * (rowsOfGroups - 1)) / rowsOfGroups;
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            float x = i % bestGroups * (blockWidth + gap), y = i / bestGroups * (blockHeight + gap);
            int columns = Math.Max(1, bestColumns[i]);
            int hostRows = (int)Math.Ceiling(site.Hosts.Count / (double)columns);
            float cellWidth = Math.Max(1, (blockWidth - padding * 2) / columns);
            float cellHeight = Math.Max(1, (blockHeight - heading - padding) / hostRows);
            float rootX = x + padding + 4 * scale, rootY = y + padding;
            using var siteBrush = new SolidBrush(StateColor(AggregateSiteState(site), 255));
            g.FillEllipse(siteBrush, rootX - 4 * scale, rootY - 4 * scale, 8 * scale, 8 * scale);
            g.DrawString(site.Name, _siteFont, text,
                new RectangleF(rootX + 12 * scale, y + 2 * scale, Math.Max(1, blockWidth - padding * 3), heading - 2 * scale), format);
            for (int n = 0; n < site.Hosts.Count; n++)
            {
                var host = site.Hosts[n];
                float left = x + padding + n % columns * cellWidth;
                float top = y + heading + n / columns * cellHeight;
                float cy = top + cellHeight / 2;
                float radius = Math.Max(1.5f, Math.Min(4 * scale, cellHeight / 4));
                var point = new PointF(left + 4 * scale, cy);
                using var connection = new Pen(StateColor(host.State, 60), 1);
                g.DrawBezier(connection, new PointF(rootX, rootY + 4 * scale), new PointF(rootX, cy), new PointF(point.X - 8 * scale, cy), point);
                using var dot = new SolidBrush(StateColor(host.State, 255));
                g.FillEllipse(dot, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                var cell = Rectangle.Intersect(bounds, Rectangle.Round(new RectangleF(left, top, Math.Max(1, cellWidth - 4 * scale), Math.Max(1, cellHeight - 2 * scale))));
                _hostHitRegions.Add((cell, host));
                _hostCaptionRegions.Add((cell, host));
                float tx = left + 16 * scale, tw = Math.Max(1, cellWidth - 20 * scale);
                bool twoLines = cellHeight >= line * 2 + 8 * scale && !string.IsNullOrWhiteSpace(host.Label);
                if (twoLines)
                    g.DrawString(host.Label, _hostLabelFont, text, new RectangleF(tx, cy - line, tw, line + 2), format);
                if (cellHeight >= line)
                    g.DrawString(host.Address, _hostIpFont, twoLines ? muted : text,
                        new RectangleF(tx, twoLines ? cy : cy - line / 2, tw, line + 2), format);
            }
        }
    }
}
