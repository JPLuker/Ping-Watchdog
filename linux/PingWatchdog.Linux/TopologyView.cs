// The connected map: Monitor → sites → hosts, the same layout rules as the Windows AdaptiveTopology.
// Every selected host fits without scrolling and every address is drawn whole (#41, #42, #43):
// the layout grows a logical canvas until all captions fit, then scales the whole map to the view.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal sealed class TopologyView : Control
{
    private IReadOnlyList<SiteSnapshot> _sites = Array.Empty<SiteSnapshot>();
    private readonly List<(Rect Bounds, HostSnapshot Host)> _hostRegions = new();
    private readonly List<(Rect Bounds, string Site)> _siteRegions = new();
    private readonly List<string> _drawnAddresses = new();
    private readonly DispatcherTimer _focusFade = new() { Interval = TimeSpan.FromSeconds(8) };
    private int _focusIndex = -1;
    private bool _showFocus;
    private HostKey? _focusKey;

    public TopologyView()
    {
        Focusable = true;
        ClipToBounds = true;
        Avalonia.Automation.AutomationProperties.SetName(this, "Map");
        Tokens.Changed += InvalidateVisual;
        _focusFade.Tick += (_, _) => { _focusFade.Stop(); _showFocus = false; InvalidateVisual(); };
    }

    public event Action<HostSnapshot, Point>? HostContextRequested;
    public event Action<HostSnapshot>? HostActivated;

    public bool AllTextFits { get; private set; } = true;
    public double CaptionScale { get; private set; } = 1;
    public IReadOnlyList<string> DrawnAddresses => _drawnAddresses.ToList();
    public IReadOnlyList<string> DrawnSiteHeadings => _siteRegions.Select(r => r.Site).ToList();
    public IReadOnlyList<(Rect Bounds, HostSnapshot Host)> HostRegions => _hostRegions.ToList();
    public HostSnapshot? FocusedHost => _focusIndex >= 0 && _focusIndex < _hostRegions.Count ? _hostRegions[_focusIndex].Host : null;
    public bool FocusVisible => _showFocus;

    public bool AllHostsFit => _hostRegions.All(r => new Rect(Bounds.Size).Contains(r.Bounds));
    public bool RegionsOverlap()
    {
        for (int i = 0; i < _siteRegions.Count; i++)
            for (int j = i + 1; j < _siteRegions.Count; j++)
                if (_siteRegions[i].Bounds.Intersects(_siteRegions[j].Bounds)) return true;
        foreach (var heading in _siteRegions)
            if (_hostRegions.Any(h => heading.Bounds.Intersects(h.Bounds))) return true;
        for (int i = 0; i < _hostRegions.Count; i++)
            for (int j = i + 1; j < _hostRegions.Count; j++)
                if (_hostRegions[i].Bounds.Intersects(_hostRegions[j].Bounds)) return true;
        return false;
    }

    public void SetSites(IReadOnlyList<SiteSnapshot> sites)
    {
        _sites = sites;
        InvalidateVisual();
    }

    private static Typeface UiFace(FontWeight weight = FontWeight.Normal) => new(FontFamily.Default, FontStyle.Normal, weight);
    private static Typeface MonoFace => new(Tokens.Mono);

    private static FormattedText Format(string text, Typeface face, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush);

    private static double MeasureWidth(string text, Typeface face, double size) => Format(text, face, size, Brushes.Black).WidthIncludingTrailingWhitespace;

    // Same rule as Windows: a site is degraded while a strict majority of its hosts still reply.
    internal static HostState AggregateSiteState(SiteSnapshot site)
    {
        var active = site.Hosts.Where(h => h.Monitoring && !h.Snoozed && !h.Disabled).ToList();
        int total = active.Count;
        if (total == 0) return HostState.Unknown;
        int online = active.Count(h => h.State == HostState.Online);
        int offline = active.Count(h => h.State == HostState.Offline);
        int suspect = active.Count(h => h.State == HostState.Suspect);
        if (offline > 0) return online > total / 2d ? HostState.Suspect : HostState.Offline;
        if (suspect > 0) return HostState.Suspect;
        return online > 0 ? HostState.Online : HostState.Unknown;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        // Fit the normal layout when it fits; otherwise enlarge the logical canvas until every caption fits.
        double zoom = 1;
        var logical = bounds;
        for (int attempt = 0; attempt < 64 && !Layout(context, logical, measureOnly: true); attempt++)
        {
            zoom *= .9;
            logical = new Rect(0, 0, Math.Ceiling(bounds.Width / zoom), Math.Ceiling(bounds.Height / zoom));
        }
        CaptionScale = zoom;
        using (context.PushTransform(Matrix.CreateScale(zoom, zoom)))
            AllTextFits = Layout(context, logical, measureOnly: false);
        Rect Scale(Rect r) => new(r.X * zoom, r.Y * zoom, r.Width * zoom, r.Height * zoom);
        for (int i = 0; i < _hostRegions.Count; i++) _hostRegions[i] = (Scale(_hostRegions[i].Bounds), _hostRegions[i].Host);
        for (int i = 0; i < _siteRegions.Count; i++) _siteRegions[i] = (Scale(_siteRegions[i].Bounds), _siteRegions[i].Site);
        if (_focusKey is { } key)
            _focusIndex = _hostRegions.FindIndex(r => r.Host.Key == key);
        if (_showFocus && _focusIndex >= 0 && _focusIndex < _hostRegions.Count)
        {
            var p = Tokens.Current;
            context.DrawRectangle(null, new Pen(new SolidColorBrush(p.Focus), 2), _hostRegions[_focusIndex].Bounds.Inflate(1), 6, 6);
        }
    }

    private bool Layout(DrawingContext g, Rect bounds, bool measureOnly)
    {
        // FOCAL: every selected host. Reflow the groups and nodes instead of scrolling.
        var p = Tokens.Current;
        IBrush text = new SolidColorBrush(p.Text), muted = new SolidColorBrush(p.Text2);
        if (!measureOnly) { _hostRegions.Clear(); _siteRegions.Clear(); _drawnAddresses.Clear(); }
        var sites = _sites.Where(site => site.Hosts.Count > 0).ToList();
        if (sites.Count == 0)
        {
            if (!measureOnly) g.DrawText(Format("No hosts", UiFace(), Tokens.Body, muted), new Point(16, 16));
            return true;
        }
        const double siteSize = 14, labelSize = 13, addressSize = 13;
        var siteFace = UiFace(FontWeight.SemiBold);
        double scale = 1, gap = 16, padding = 12;
        double line = Format("0", MonoFace, addressSize, text).Height;
        double hubHeight = 40, trunkWidth = 20, rowGutter = 24;
        var content = new Rect(trunkWidth, hubHeight, Math.Max(1, bounds.Width - trunkWidth), Math.Max(1, bounds.Height - hubHeight));
        double heading = Format("Ag", siteFace, siteSize, text).Height + 16;
        double minimumBlock = heading + padding + line;
        int squareBound = (int)Math.Ceiling(Math.Sqrt(sites.Count));
        int bestGroups = 1, relaxedGroups = 1, fullGroups = 1;
        double bestScore = -1, relaxedScore = -1, fullScore = -1;
        double bestGutter = rowGutter, relaxedGutter = rowGutter, fullGutter = rowGutter;
        double[] bestHeights = Array.Empty<double>(), relaxedHeights = Array.Empty<double>(), fullHeights = Array.Empty<double>();
        int[] bestColumns = new int[sites.Count], relaxedColumns = new int[sites.Count], fullColumns = new int[sites.Count];
        var nameWidths = sites.Select(site => MeasureWidth(site.Name, siteFace, siteSize) + 4).ToArray();
        var addressWidths = sites.Select(site => site.Hosts.Max(h => MeasureWidth(h.Address, MonoFace, addressSize)) + 40).ToArray();
        // What a cell needs for a whole address: the text plus the marker lead-in.
        var addressNeeds = sites.Select((site, i) => Math.Max(addressWidths[i] - 40,
            site.Hosts.Max(h => MeasureWidth(h.Label ?? "", UiFace(), labelSize))) + 30).ToArray();

        static double Gutter(double height, int rows) => rows <= 3 ? 24 : Math.Clamp(height * 0.15 / rows, 10, 24);

        for (int groupColumns = 1; groupColumns <= sites.Count; groupColumns++)
        {
            if (groupColumns > squareBound && fullScore >= 1) break;
            int groupRows = (int)Math.Ceiling(sites.Count / (double)groupColumns);
            double gutter = Gutter(content.Height, groupRows);
            double groupWidth = (content.Width - gap * (groupColumns - 1)) / groupColumns;
            if (groupWidth <= padding * 2) continue;
            // Uneven sites need uneven rows: one busy site must not shrink every other row.
            var rowNeeds = new double[groupRows];
            for (int i = 0; i < sites.Count; i++)
            {
                int maxColumns = Math.Clamp((int)((groupWidth - padding * 2) / addressNeeds[i]), 1, sites[i].Hosts.Count);
                int hostRows = (int)Math.Ceiling(sites[i].Hosts.Count / (double)maxColumns);
                rowNeeds[i / groupColumns] = Math.Max(rowNeeds[i / groupColumns], heading + padding + hostRows * (line * 2 + 8));
            }
            double sparePerRow = (content.Height - gutter * groupRows - rowNeeds.Sum()) / groupRows;
            var rowHeights = rowNeeds.Select(h => h + sparePerRow).ToArray();
            bool readable = groupWidth >= 80 && rowHeights.All(h => h >= minimumBlock);
            double score = double.MaxValue;
            bool addressesFit = true;
            var columns = new int[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                double groupHeight = rowHeights[i / groupColumns];
                double nodeScore = -1;
                bool nodeWidthFits = false;
                for (int c = 1; c <= sites[i].Hosts.Count; c++)
                {
                    int rows = (int)Math.Ceiling(sites[i].Hosts.Count / (double)c);
                    double widthFit = (groupWidth - padding * 2) / c / addressWidths[i];
                    double fit = Math.Min(widthFit, (groupHeight - heading - padding) / rows / (line * 2 + 8));
                    bool widthFits = (groupWidth - padding * 2) / c >= addressNeeds[i]
                        && (groupHeight - heading - padding) / rows >= line * 2 + 8;
                    if ((widthFits && !nodeWidthFits) || (widthFits == nodeWidthFits && fit > nodeScore))
                    { nodeScore = fit; nodeWidthFits = widthFits; columns[i] = c; }
                }
                addressesFit &= nodeWidthFits && groupWidth - padding * 3 >= nameWidths[i];
                score = Math.Min(score, Math.Min(nodeScore, (groupWidth - padding * 3) / nameWidths[i]));
            }
            if (readable && addressesFit && score > fullScore) { fullScore = score; fullGroups = groupColumns; fullColumns = columns; fullGutter = gutter; fullHeights = rowHeights; }
            if (readable && score > bestScore) { bestScore = score; bestGroups = groupColumns; bestColumns = columns; bestGutter = gutter; bestHeights = rowHeights; }
            if (score > relaxedScore) { relaxedScore = score; relaxedGroups = groupColumns; relaxedColumns = columns; relaxedGutter = gutter; relaxedHeights = rowHeights; }
        }
        if (measureOnly) return fullScore >= 0;
        if (fullScore >= 0) { bestGroups = fullGroups; bestColumns = fullColumns; bestGutter = fullGutter; bestHeights = fullHeights; }
        else if (bestScore < 0) { bestGroups = relaxedGroups; bestColumns = relaxedColumns; bestGutter = relaxedGutter; bestHeights = relaxedHeights; }
        rowGutter = bestGutter;
        int rowsOfGroups = (int)Math.Ceiling(sites.Count / (double)bestGroups);
        double blockWidth = (content.Width - gap * (bestGroups - 1)) / bestGroups;
        var rowTops = new double[rowsOfGroups];
        for (int row = 1; row < rowsOfGroups; row++) rowTops[row] = rowTops[row - 1] + bestHeights[row - 1] + rowGutter;

        var trunk = new Pen(new SolidColorBrush(Color.FromArgb(110, p.Accent.R, p.Accent.G, p.Accent.B)), 1);
        double trunkX = 8, hubY = 16;
        g.DrawEllipse(new SolidColorBrush(p.Accent), null, new Point(trunkX, hubY), 5, 5);
        var monitor = Format("Monitor", siteFace, siteSize, text);
        g.DrawText(monitor, new Point(trunkX + 14, hubY - monitor.Height / 2));
        double lastBusY = content.Y + rowTops[^1] + 4;
        g.DrawLine(trunk, new Point(trunkX, hubY + 5), new Point(trunkX, lastBusY));
        for (int row = 0; row < rowsOfGroups; row++)
        {
            int count = Math.Min(bestGroups, sites.Count - row * bestGroups);
            double busY = content.Y + rowTops[row] + 4;
            double lastRootX = content.X + (count - 1) * (blockWidth + gap) + padding + 4;
            g.DrawLine(trunk, new Point(trunkX, busY), new Point(lastRootX, busY));
            for (int column = 0; column < count; column++)
            {
                double rootX = content.X + column * (blockWidth + gap) + padding + 4;
                g.DrawLine(trunk, new Point(rootX, busY), new Point(rootX, busY + rowGutter - 4 + padding));
            }
        }

        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            double x = content.X + i % bestGroups * (blockWidth + gap), y = content.Y + rowGutter + rowTops[i / bestGroups];
            double blockHeight = bestHeights[i / bestGroups];
            int columns = Math.Max(1, bestColumns[i]);
            int hostRows = (int)Math.Ceiling(site.Hosts.Count / (double)columns);
            double cellWidth = Math.Max(1, (blockWidth - padding * 2) / columns);
            double cellHeight = Math.Min(line * 2 + 16, Math.Max(1, (blockHeight - heading - padding) / hostRows));
            double rootX = x + padding + 4, rootY = y + padding;
            var siteColor = p.StateColor(AggregateSiteState(site));
            g.DrawEllipse(new SolidColorBrush(siteColor), null, new Point(rootX, rootY), 4, 4);
            var headingRect = new Rect(rootX + 12, y + 2, Math.Max(1, blockWidth - padding * 3), heading - 4);
            if (headingRect.Width >= 24)
            {
                var name = Format(site.Name, siteFace, siteSize, text);
                name.MaxTextWidth = headingRect.Width;
                name.MaxLineCount = 1;
                name.Trimming = TextTrimming.CharacterEllipsis;
                g.DrawText(name, new Point(headingRect.X, rootY - name.Height / 2));
                _siteRegions.Add((new Rect(headingRect.X, rootY - name.Height / 2, Math.Min(headingRect.Width, name.WidthIncludingTrailingWhitespace), name.Height), site.Name));
            }
            var branch = new Pen(new SolidColorBrush(Color.FromArgb(80, siteColor.R, siteColor.G, siteColor.B)), 1);
            double railY = y + heading - 3;
            double lastColumnX = x + padding + (columns - 1) * cellWidth + 4;
            g.DrawLine(branch, new Point(rootX, rootY + 4), new Point(rootX, railY));
            g.DrawLine(branch, new Point(rootX, railY), new Point(lastColumnX, railY));
            for (int column = 0; column < columns; column++)
            {
                int count = (site.Hosts.Count - column + columns - 1) / columns;
                double branchX = x + padding + column * cellWidth + 4;
                g.DrawLine(branch, new Point(branchX, railY), new Point(branchX, y + heading + (count - .5) * cellHeight));
            }
            for (int n = 0; n < site.Hosts.Count; n++)
            {
                var host = site.Hosts[n];
                double left = x + padding + n % columns * cellWidth;
                double top = y + heading + n / columns * cellHeight;
                double cy = top + cellHeight / 2;
                double radius = Math.Max(1.5, Math.Min(4 * scale, cellHeight / 4));
                var point = new Point(left + 12, cy);
                var hostColor = p.StateColor(host);
                g.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, hostColor.R, hostColor.G, hostColor.B)), 1), new Point(left + 4, cy), point);
                DrawMarker(g, point, radius, host, p);
                var cell = new Rect(left, top, Math.Max(1, cellWidth - 4), Math.Max(1, cellHeight - 2)).Intersect(bounds);
                _hostRegions.Add((cell, host));
                double tx = left + 24, tw = Math.Max(1, cellWidth - 28);
                bool twoLines = cellHeight >= line * 2 + 8 && !string.IsNullOrWhiteSpace(host.Label);
                if (twoLines)
                {
                    var label = Format(host.Label, UiFace(), labelSize, text);
                    label.MaxTextWidth = tw;
                    label.MaxLineCount = 1;
                    label.Trimming = TextTrimming.CharacterEllipsis;
                    g.DrawText(label, new Point(tx, cy - line));
                }
                if (cellHeight >= line)
                {
                    // Addresses are drawn whole, never trimmed or clipped (#41).
                    var address = Format(host.Address, MonoFace, addressSize, twoLines ? muted : text);
                    g.DrawText(address, new Point(tx, twoLines ? cy : cy - line / 2));
                    _drawnAddresses.Add(host.Address);
                }
            }
        }
        return fullScore >= 0;
    }

    // Shape as well as color: online dot, suspect triangle, offline cross, idle diamond, snoozed pause bars, priority ring.
    private static void DrawMarker(DrawingContext g, Point point, double radius, HostSnapshot host, Palette p)
    {
        if (host.Options.Priority)
            g.DrawEllipse(null, new Pen(new SolidColorBrush(p.Accent), Math.Max(1.5, radius / 2.5)), point, radius * 1.9 + 1, radius * 1.9 + 1);
        var color = p.StateColor(host);
        var fill = new SolidColorBrush(color);
        var pen = new Pen(fill, 2);
        HostState state = host.Monitoring && !host.Disabled ? host.State : HostState.Unknown;
        if (host.Snoozed)
        {
            g.DrawLine(pen, new Point(point.X - radius / 2, point.Y - radius), new Point(point.X - radius / 2, point.Y + radius));
            g.DrawLine(pen, new Point(point.X + radius / 2, point.Y - radius), new Point(point.X + radius / 2, point.Y + radius));
        }
        else if (state == HostState.Offline)
        {
            g.DrawLine(pen, new Point(point.X - radius, point.Y - radius), new Point(point.X + radius, point.Y + radius));
            g.DrawLine(pen, new Point(point.X - radius, point.Y + radius), new Point(point.X + radius, point.Y - radius));
        }
        else if (state == HostState.Suspect)
            g.DrawGeometry(fill, null, Polygon(new(point.X, point.Y - radius), new(point.X + radius, point.Y + radius), new(point.X - radius, point.Y + radius)));
        else if (state == HostState.Online)
            g.DrawEllipse(fill, null, point, radius, radius);
        else
            g.DrawGeometry(null, new Pen(fill, 1.5), Polygon(new(point.X, point.Y - radius), new(point.X + radius, point.Y), new(point.X, point.Y + radius), new(point.X - radius, point.Y)));
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(points[0], true);
        foreach (var point in points.Skip(1)) ctx.LineTo(point);
        ctx.EndFigure(true);
        return geometry;
    }

    public HostSnapshot? HostAt(Point point) => _hostRegions.FirstOrDefault(r => r.Bounds.Contains(point)).Host;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        int index = _hostRegions.FindIndex(r => r.Bounds.Contains(point));
        if (index < 0) return;
        SetFocusIndex(index, show: false);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            e.Handled = true;
            HostContextRequested?.Invoke(_hostRegions[index].Host, point);
        }
        else if (e.ClickCount == 2)
        {
            e.Handled = true;
            HostActivated?.Invoke(_hostRegions[index].Host);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_hostRegions.Count == 0) return;
        int index = Math.Max(0, _focusIndex);
        switch (e.Key)
        {
            case Key.Right or Key.Down:
                SetFocusIndex(_focusIndex < 0 ? 0 : Math.Min(_hostRegions.Count - 1, index + 1), true); e.Handled = true; break;
            case Key.Left or Key.Up:
                SetFocusIndex(Math.Max(0, index - 1), true); e.Handled = true; break;
            case Key.Home:
                SetFocusIndex(0, true); e.Handled = true; break;
            case Key.End:
                SetFocusIndex(_hostRegions.Count - 1, true); e.Handled = true; break;
            case Key.Apps:
            case Key.Enter:
            case Key.F10 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                if (FocusedHost is { } host) { HostContextRequested?.Invoke(host, _hostRegions[_focusIndex].Bounds.BottomLeft); e.Handled = true; }
                break;
            case Key.F2:
                if (FocusedHost is { } edit) { HostActivated?.Invoke(edit); e.Handled = true; }
                break;
        }
    }

    // The keyboard highlight fades after 8 seconds (#20).
    private void SetFocusIndex(int index, bool show)
    {
        _focusIndex = index;
        _focusKey = index >= 0 && index < _hostRegions.Count ? _hostRegions[index].Host.Key : null;
        _showFocus = show;
        if (show) { _focusFade.Stop(); _focusFade.Start(); }
        if (_focusKey is { } key)
            Avalonia.Automation.AutomationProperties.SetName(this, "Map, " + _hostRegions[index].Host.DisplayName + ", " + _hostRegions[index].Host.Address + ", " + _hostRegions[index].Host.StatusText);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Tokens.Changed -= InvalidateVisual;
        _focusFade.Stop();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tokens.Changed -= InvalidateVisual;
        Tokens.Changed += InvalidateVisual;
    }
}
