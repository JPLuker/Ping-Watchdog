using PingWatchdog.Shared;

namespace PingWatchdog;

// Topology drawing for the Wallboard: site and host nodes, connections and captions.
internal sealed partial class WallboardCanvas
{
    private void DrawTopology(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        _topologyViewport = rect;
        _topologyVirtualSize = rect.Size;
        _panOffset = Point.Empty;
        var state = g.Save();
        g.SetClip(rect, System.Drawing.Drawing2D.CombineMode.Intersect);
        g.TranslateTransform(rect.X, rect.Y);
        DrawFittedTopology(g, snapshot, new Rectangle(Point.Empty, rect.Size));
        g.Restore(state);
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
        _captionObstacles.Clear();
        _hiddenCaptions = 0;
        if (snapshot.Sites.Count == 0)
        {
            using var empty = new SolidBrush(WallboardStyle.Muted);
            g.DrawString("No hosts", _sectionFont, empty, 16, 16);
            return;
        }
        int columns = _topologyViewport.Width >= 1100 && snapshot.Sites.Count > 1 ? 2 : 1;
        int laneWidth = rect.Width / columns;
        int groupHeight = Math.Max(180, snapshot.Sites.Max(site => site.Hosts.Count) * 52 + 64);
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        for (int index = 0; index < snapshot.Sites.Count; index++)
        {
            var site = snapshot.Sites[index];
            int x = (index % columns) * laneWidth;
            int y = (index / columns) * groupHeight;
            int rootY = y + 40 + Math.Max(0, site.Hosts.Count - 1) * 26;
            var root = new Point(x + 184, rootY);
            using var siteDot = new SolidBrush(StateColor(AggregateSiteState(site), 255));
            g.FillEllipse(siteDot, root.X - 6, root.Y - 6, 12, 12);
            g.DrawString(site.Name, _siteFont, text, new RectangleF(x + 16, rootY - 20, 150, 24), format);
            g.DrawString($"{site.Hosts.Count(h => h.State == HostState.Online)}/{site.Hosts.Count} online", _tinyFont, muted, x + 16, rootY + 4);
            for (int h = 0; h < site.Hosts.Count; h++)
            {
                var host = site.Hosts[h];
                var point = new Point(x + 240, y + 40 + h * 52);
                using var connector = new Pen(StateColor(host.State, 90), 1.5f);
                g.DrawBezier(connector, root, new Point(root.X + 32, root.Y), new Point(point.X - 32, point.Y), point);
                using var dot = new SolidBrush(StateColor(host.State, 255));
                g.FillEllipse(dot, point.X - 4, point.Y - 4, 8, 8);
                var lines = FormatHostNodeLines(host);
                var size = MeasureHostNodeCaption(g, lines.Primary, lines.Secondary, _hostLabelFont, _hostIpFont);
                var caption = new Rectangle(point.X + 12, point.Y - size.Height / 2, size.Width, size.Height);
                _hostHitRegions.Add((new Rectangle(point.X - 8, point.Y - 8, 16, 16), host));
                _hostHitRegions.Add((caption, host));
                _hostCaptionRegions.Add((caption, host));
                int primaryHeight = (int)Math.Ceiling(_hostLabelFont.GetHeight(g));
                g.DrawString(lines.Primary, _hostLabelFont, text, new RectangleF(caption.X + 4, caption.Y + 4, caption.Width - 8, primaryHeight + 2), format);
                if (lines.Secondary is not null)
                    g.DrawString(lines.Secondary, _hostIpFont, muted, caption.X + 4, caption.Y + 4 + primaryHeight, StringFormat.GenericTypographic);
            }
        }
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


    }

    private void DrawCore(
        Graphics g,
        Point center,
        int radius,
        bool monitoring)
    {
        using var fillBrush = new SolidBrush(WallboardStyle.Selection);
        using var outlinePen = new Pen(WallboardStyle.Accent, 2);
        using var textBrush = new SolidBrush(WallboardStyle.Text);

        g.FillEllipse(fillBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(outlinePen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

        var coreRect = new Rectangle(
            center.X - radius,
            center.Y - 12,
            radius * 2,
            24);

        DrawCenteredText(g, "Monitor", _coreFont, textBrush, coreRect);

        using var dotBrush = new SolidBrush(monitoring
            ? WallboardStyle.Green
            : WallboardStyle.Muted);

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

        using var fillBrush = new SolidBrush(WallboardStyle.Panel);
        using var borderPen = new Pen(color, 2);
        using var nameBrush = new SolidBrush(WallboardStyle.Text);
        using var countBrush = new SolidBrush(WallboardStyle.Muted);

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
            using var nodeBorder = new Pen(WallboardStyle.Text, 1);
            g.FillEllipse(nodeBrush, point.X - 5, point.Y - 5, 10, 10);
            g.DrawEllipse(nodeBorder, point.X - 5, point.Y - 5, 10, 10);

            _hostHitRegions.Add((new Rectangle(point.X - 10, point.Y - 10, 20, 20), host));
            DrawHostNodeCaption(g, host, point, angle, topologyBounds);
        }

        if (site.Hosts.Count > visible)
        {
            using var moreBrush = new SolidBrush(WallboardStyle.Muted);
            string more = $"+{site.Hosts.Count - visible} more";
            var size = g.MeasureString(more, _tinyFont);

            var rect = ClampToBounds(
                new Rectangle(
                    siteCenter.X - (int)size.Width / 2 - 4,
                    siteCenter.Y + baseOrbitRadius + 18,
                    (int)size.Width + 8,
                    (int)size.Height + 4),
                topologyBounds);

            using var fill = new SolidBrush(WallboardStyle.Panel);
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
        int primaryHeight = (int)Math.Ceiling(_hostLabelFont.GetHeight(g));

        if (!TopologyLayout.TryPlaceHostCaption(point, captionSize, topologyBounds,
            _captionObstacles, Math.Cos(angle) >= 0, out var rect))
        {
            _hiddenCaptions++;
            return;
        }
        _captionObstacles.Add(rect);
        _hostHitRegions.Add((rect, host));
        _hostCaptionRegions.Add((rect, host));
        using (var leader = new Pen(StateColor(host.State, 120), 1))
            g.DrawLine(leader, point, new Point(Math.Clamp(point.X, rect.Left, rect.Right),
                Math.Clamp(point.Y, rect.Top, rect.Bottom)));

        using var fill = new SolidBrush(WallboardStyle.Panel);
        using var primaryBrush = new SolidBrush(WallboardStyle.Text);
        using var secondaryBrush = new SolidBrush(WallboardStyle.Muted);

        // One renderer for measurement and drawing; no GDI text over GDI+ bitmaps.
        // Quiet labels on the canvas, with no outlined boxes around every address.
        g.FillRectangle(fill, rect);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap,
            Trimming = secondary is null ? StringTrimming.None : StringTrimming.EllipsisCharacter };
        g.DrawString(primary, _hostLabelFont, primaryBrush,
            new RectangleF(rect.X + 6, rect.Y + 4, rect.Width - 12, primaryHeight + 2), format);

        if (secondary is not null)
        {
            format.Trimming = StringTrimming.None;
            g.DrawString(secondary, _hostIpFont, secondaryBrush,
                new RectangleF(rect.X + 6, rect.Y + 4 + primaryHeight, rect.Width - 12, rect.Height - primaryHeight), format);
        }
    }

    // Measure with the same GDI+ renderer used for drawing. Addresses never
    // inherit the nickname width cap, including IPv6 and larger display scaling.
    internal static Size MeasureHostNodeCaption(Graphics g, string primary, string? secondary,
        Font primaryFont, Font addressFont)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var primarySize = g.MeasureString(primary, primaryFont, int.MaxValue, format);
        var addressSize = secondary is null ? SizeF.Empty : g.MeasureString(secondary, addressFont, int.MaxValue, format);
        float primaryWidth = secondary is null ? primarySize.Width : Math.Min(primarySize.Width, 180);
        return new Size((int)Math.Ceiling(Math.Max(64, Math.Max(primaryWidth, addressSize.Width) + 16)),
            (int)Math.Ceiling(primaryFont.GetHeight(g) + (secondary is null ? 0 : addressFont.GetHeight(g)) + 12));
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
}
