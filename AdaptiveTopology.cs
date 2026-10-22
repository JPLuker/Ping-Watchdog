namespace PingWatchdog;

internal sealed partial class WallboardCanvas
{
    private readonly ToolTip _hostTip = new();
    private string _lastHostTip = "";
    internal bool AllHostNodesFit => _hostHitRegions.All(hit => new Rectangle(Point.Empty, _topologyViewport.Size).Contains(hit.Bounds));
    internal int VisibleHostCount => _hostCaptionRegions.Count;
    private readonly List<(Rectangle Bounds, string Site)> _siteHeadingRegions = new();
    internal IReadOnlyList<string> DrawnSiteHeadings => _siteHeadingRegions.Select(r => r.Site).ToList();
    // Headings must not collide with each other or with any host cell.
    internal bool TopologyRegionsOverlap() =>
        _siteHeadingRegions.SelectMany((a, i) => _siteHeadingRegions.Skip(i + 1).Select(b => a.Bounds.IntersectsWith(b.Bounds))).Any(x => x)
        || _siteHeadingRegions.Any(h => _hostHitRegions.Any(c => h.Bounds.IntersectsWith(c.Bounds)))
        || _hostHitRegions.SelectMany((a, i) => _hostHitRegions.Skip(i + 1).Select(b => a.Bounds.IntersectsWith(b.Bounds))).Any(x => x);

    // Rows past the third trade some gutter for host space; the bus lines still fit.
    private static float TopologyRowGutter(float height, int rows, float scale) =>
        rows <= 3 ? 24 * scale : Math.Clamp(height * 0.15f / rows, 10 * scale, 24 * scale);

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
        // Reserve the shared trunk and row gutters before packing the host captions.
        float hubHeight = 40 * scale, trunkWidth = 20 * scale, rowGutter = 24 * scale;
        var content = new RectangleF(trunkWidth, hubHeight, Math.Max(1, bounds.Width - trunkWidth), Math.Max(1, bounds.Height - hubHeight));
        float heading = _siteFont.GetHeight(g) + 16 * scale;
        // A block must hold its heading and at least one readable address row.
        float minimumBlock = heading + padding + line;
        int squareBound = (int)Math.Ceiling(Math.Sqrt(sites.Count));
        int bestGroups = 1, relaxedGroups = 1;
        double bestScore = -1, relaxedScore = -1;
        float bestGutter = rowGutter, relaxedGutter = rowGutter;
        int[] bestColumns = new int[sites.Count], relaxedColumns = new int[sites.Count];
        var addressWidths = sites.Select(site => site.Hosts.Select(host => g.MeasureString(host.Address, _hostIpFont, int.MaxValue, StringFormat.GenericTypographic).Width).Max() + 40 * scale).ToArray();
        // Keep the accepted square-ish search; widen it only when captions no longer fit.
        for (int groupColumns = 1; groupColumns <= sites.Count; groupColumns++)
        {
            if (groupColumns > squareBound && bestScore >= 1) break;
            int groupRows = (int)Math.Ceiling(sites.Count / (double)groupColumns);
            float gutter = TopologyRowGutter(content.Height, groupRows, scale);
            float groupWidth = (content.Width - gap * (groupColumns - 1)) / groupColumns;
            float groupHeight = (content.Height - gutter * groupRows) / groupRows;
            if (groupWidth <= 1 || groupHeight <= 1) continue;
            bool readable = groupWidth >= 80 * scale && groupHeight >= minimumBlock;
            double score = double.MaxValue;
            var columns = new int[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                double nodeScore = -1;
                float addressWidth = addressWidths[i];
                for (int c = 1; c <= sites[i].Hosts.Count; c++)
                {
                    int rows = (int)Math.Ceiling(sites[i].Hosts.Count / (double)c);
                    double fit = Math.Min((groupWidth - padding * 2) / c / addressWidth,
                        (groupHeight - heading - padding) / rows / (line * 2 + 8 * scale));
                    if (fit > nodeScore) { nodeScore = fit; columns[i] = c; }
                }
                score = Math.Min(score, nodeScore);
            }
            if (readable && score > bestScore) { bestScore = score; bestGroups = groupColumns; bestColumns = columns; bestGutter = gutter; }
            if (score > relaxedScore) { relaxedScore = score; relaxedGroups = groupColumns; relaxedColumns = columns; relaxedGutter = gutter; }
        }
        if (bestScore < 0)
        {
            // Too many sites for this window to label everything: keep every marker separate.
            bestGroups = relaxedGroups; bestColumns = relaxedColumns; bestGutter = relaxedGutter;
        }
        rowGutter = bestGutter;
        int rowsOfGroups = (int)Math.Ceiling(sites.Count / (double)bestGroups);
        float blockWidth = (content.Width - gap * (bestGroups - 1)) / bestGroups;
        float blockHeight = (content.Height - rowGutter * rowsOfGroups) / rowsOfGroups;
        // When a block cannot hold a heading and a readable row, give its hosts the whole block.
        bool showHeadings = blockHeight >= minimumBlock;
        float siteHeading = showHeadings ? heading : 6 * scale, blockPadding = showHeadings ? padding : 2 * scale;
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        using var trunk = new Pen(Color.FromArgb(110, WallboardStyle.Accent), Math.Max(1, scale));
        float trunkX = 8 * scale, hubY = 16 * scale;
        using var hubBrush = new SolidBrush(WallboardStyle.Accent);
        g.FillEllipse(hubBrush, trunkX - 5 * scale, hubY - 5 * scale, 10 * scale, 10 * scale);
        g.DrawString("Monitor", _siteFont, text, trunkX + 14 * scale, hubY - line / 2);
        float lastBusY = content.Y + (rowsOfGroups - 1) * (blockHeight + rowGutter) + 4 * scale;
        g.DrawLine(trunk, trunkX, hubY + 5 * scale, trunkX, lastBusY);
        // Edges occupy gutters, never the heading or address rectangles.
        for (int row = 0; row < rowsOfGroups; row++)
        {
            int count = Math.Min(bestGroups, sites.Count - row * bestGroups);
            float busY = content.Y + row * (blockHeight + rowGutter) + 4 * scale;
            float lastRootX = content.X + (count - 1) * (blockWidth + gap) + padding + 4 * scale;
            g.DrawLine(trunk, trunkX, busY, lastRootX, busY);
            for (int column = 0; column < count; column++)
            {
                float rootX = content.X + column * (blockWidth + gap) + padding + 4 * scale;
                g.DrawLine(trunk, rootX, busY, rootX, busY + rowGutter - 4 * scale + padding);
            }
        }
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            float x = content.X + i % bestGroups * (blockWidth + gap), y = content.Y + rowGutter + i / bestGroups * (blockHeight + rowGutter);
            int columns = Math.Max(1, bestColumns[i]);
            int hostRows = (int)Math.Ceiling(site.Hosts.Count / (double)columns);
            float cellWidth = Math.Max(1, (blockWidth - padding * 2) / columns);
            float cellHeight = Math.Max(1, (blockHeight - siteHeading - blockPadding) / hostRows);
            float rootX = x + padding + 4 * scale, rootY = y + blockPadding;
            using var siteBrush = new SolidBrush(StateColor(AggregateSiteState(site), 255));
            g.FillEllipse(siteBrush, rootX - 4 * scale, rootY - 4 * scale, 8 * scale, 8 * scale);
            var headingRect = new RectangleF(rootX + 12 * scale, y + 2 * scale, Math.Max(1, blockWidth - padding * 3), siteHeading - 4 * scale);
            if (showHeadings && headingRect.Width >= 24 * scale)
            {
                g.DrawString(site.Name, _siteFont, text, headingRect, format);
                _siteHeadingRegions.Add((Rectangle.Round(headingRect), site.Name));
            }
            using var branch = new Pen(StateColor(AggregateSiteState(site), 80), Math.Max(1, scale));
            float railY = y + siteHeading - 3 * scale;
            float lastColumnX = x + padding + (columns - 1) * cellWidth + 4 * scale;
            g.DrawLine(branch, rootX, rootY + 4 * scale, rootX, railY);
            g.DrawLine(branch, rootX, railY, lastColumnX, railY);
            for (int column = 0; column < columns; column++)
            {
                int count = (site.Hosts.Count - column + columns - 1) / columns;
                float branchX = x + padding + column * cellWidth + 4 * scale;
                g.DrawLine(branch, branchX, railY, branchX, y + siteHeading + (count - .5f) * cellHeight);
            }
            for (int n = 0; n < site.Hosts.Count; n++)
            {
                var host = site.Hosts[n];
                float left = x + padding + n % columns * cellWidth;
                float top = y + siteHeading + n / columns * cellHeight;
                float cy = top + cellHeight / 2;
                float radius = Math.Max(1.5f, Math.Min(4 * scale, cellHeight / 4));
                var point = new PointF(left + 12 * scale, cy);
                using var connection = new Pen(StateColor(host.State, 60), 1);
                g.DrawLine(connection, left + 4 * scale, cy, point.X, cy);
                DrawHostMarker(g, point, radius, host);
                var cell = Rectangle.Intersect(bounds, Rectangle.Round(new RectangleF(left, top, Math.Max(1, cellWidth - 4 * scale), Math.Max(1, cellHeight - 2 * scale))));
                _hostHitRegions.Add((cell, host));
                _hostCaptionRegions.Add((cell, host));
                float tx = left + 24 * scale, tw = Math.Max(1, cellWidth - 28 * scale);
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
