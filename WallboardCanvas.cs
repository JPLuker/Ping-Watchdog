using PingWatchdog.Shared;

namespace PingWatchdog;

// Draws the Wallboard and the main window's Map: header, connected topology, events and ping log.
internal sealed partial class WallboardCanvas : Control
{
    private readonly List<Rectangle> _captionObstacles = new();
    private int _hiddenCaptions;
    private readonly Bitmap _brandBitmap = BrandAssets.LoadLogo();
    private readonly Font _brandFont = new("Segoe UI Semibold", 22);
    private readonly Font _subtitleFont = new("Segoe UI Semibold", 11);
    private readonly Font _clockFont = new("Segoe UI Semibold", 22);
    private readonly Font _dateFont = new("Segoe UI", 9.75f);
    private readonly Font _statValueFont = new("Segoe UI Semibold", 25);
    private readonly Font _statLabelFont = new("Segoe UI", 10);
    private readonly Font _sectionFont = new("Segoe UI Semibold", 11);
    private readonly Font _siteFont = new("Segoe UI Semibold", 9.75f);
    private readonly Font _siteSmallFont = new("Segoe UI Semibold", 9.75f);
    private readonly Font _siteTinyFont = new("Segoe UI Semibold", 9.75f);
    private readonly Font _hostLabelFont = new("Segoe UI Semibold", 9.75f);
    private readonly Font _hostIpFont = new("Consolas", 9.75f);
    private readonly Font _smallFont = new("Segoe UI", 9.75f);
    private readonly Font _tinyFont = new("Segoe UI", 9.75f);
    private readonly Font _coreFont = new("Segoe UI Semibold", 11);
    private readonly Font _cliFont = new("Consolas", 9.75f);

    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _hostHitRegions = new();
    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _sidebarHitRegions = new();
    private readonly List<(Rectangle Bounds, WallboardHostSnapshot Host)> _hostCaptionRegions = new();
    internal bool HasHostCaption(string site, string address) =>
        _hostCaptionRegions.Any(hit => hit.Host.Site == site && hit.Host.Address == address);
    internal bool CaptionsOverlap() => _hostCaptionRegions.SelectMany((a, i) =>
        _hostCaptionRegions.Skip(i + 1).Select(b => a.Bounds.IntersectsWith(b.Bounds))).Any(overlap => overlap);
    private Rectangle _topologyViewport;
    private Size _topologyVirtualSize;
    internal Size TopologyContentSize => _topologyVirtualSize;
    private Point _panOffset;
    private Point? _dragOrigin;
    private Point _dragPan;
    private Rectangle _resultsViewport;
    private int _resultScrollOffset;

    internal void PanTopology(int dx, int dy)
    {
        _panOffset = new Point(
            Math.Clamp(_panOffset.X + dx, 0, Math.Max(0, _topologyVirtualSize.Width - _topologyViewport.Width)),
            Math.Clamp(_panOffset.Y + dy, 0, Math.Max(0, _topologyVirtualSize.Height - _topologyViewport.Height)));
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Right) SelectHostAt(e.Location);
        if (e.Button == MouseButtons.Left && _topologyViewport.Contains(e.Location))
        {
            Focus(); _dragOrigin = e.Location; _dragPan = _panOffset; Capture = true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = HitTestHost(e.Location);
        string tip = hovered == null ? "" : hovered.Site + " · " + hovered.Address + (string.IsNullOrWhiteSpace(hovered.Label) ? "" : " · " + hovered.Label) + " · " + hovered.State;
        if (tip != _lastHostTip) { _lastHostTip = tip; _hostTip.SetToolTip(this, tip); }
        if (_dragOrigin is Point origin)
        {
            _panOffset = _dragPan;
            PanTopology(origin.X - e.X, origin.Y - e.Y);
        }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) _dragOrigin = null;
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_resultsViewport.Contains(e.Location))
        {
            int count = Snapshot?.Sites.Sum(site => site.Hosts.Count) ?? 0;
            _resultScrollOffset = Math.Clamp(_resultScrollOffset - Math.Sign(e.Delta) * 3, 0, Math.Max(0, count - 1));
            Invalidate();
            return;
        }
        if ((ModifierKeys & Keys.Shift) != 0) PanTopology(-e.Delta, 0);
        else PanTopology(0, -e.Delta);
    }

    internal event Action<WallboardHostSnapshot, Point>? HostRightClicked;
    internal IReadOnlyList<WallboardHostSnapshot> CurrentOutageHosts => _sidebarHitRegions.Select(hit => hit.Host).ToList();

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleHostKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    internal WallboardHostSnapshot? HitTestHost(Point location)
    {
        var sidebar = _sidebarHitRegions.LastOrDefault(hit => hit.Bounds.Contains(location)).Host;
        if (sidebar != null) return sidebar;
        if (!_topologyViewport.Contains(location) || _topologyVirtualSize.Width <= 0) return null;
        var point = new Point(
            location.X - _topologyViewport.X + _panOffset.X,
            location.Y - _topologyViewport.Y + _panOffset.Y);
        return _hostHitRegions.LastOrDefault(hit => hit.Bounds.Contains(point)).Host;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) { _dragOrigin = null; Capture = false; }
        if (e.Button == MouseButtons.Right && HitTestHost(e.Location) is { } host)
            HostRightClicked?.Invoke(host, e.Location);
    }

    public WallboardSnapshot? Snapshot { get; set; }
    public bool ShowCli { get; set; } = true;
    // The dashboard and full-screen presentation use the same data and renderer.
    public bool Embedded { get; set; }
    internal Rectangle BrandLogoBounds => new(22, 12, 58, 58);

    public WallboardCanvas()
    {
        Name = "WallboardCanvas";
        TabStop = true;
        AccessibleName = "Sites and hosts";
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WallboardStyle.Background;
        ForeColor = Color.White;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hostTip.Dispose();
            _brandBitmap.Dispose();
            _brandFont.Dispose();
            _subtitleFont.Dispose();
            _clockFont.Dispose();
            _dateFont.Dispose();
            _statValueFont.Dispose();
            _statLabelFont.Dispose();
            _sectionFont.Dispose();
            _siteFont.Dispose();
            _siteSmallFont.Dispose();
            _siteTinyFont.Dispose();
            _hostLabelFont.Dispose();
            _hostIpFont.Dispose();
            _smallFont.Dispose();
            _tinyFont.Dispose();
            _coreFont.Dispose();
            _cliFont.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var snapshot = Snapshot ?? new WallboardSnapshot(
            false,
            DateTime.Now,
            Array.Empty<WallboardSiteSnapshot>(),
            Array.Empty<WallboardEventSnapshot>(),
            Array.Empty<WallboardCommandSnapshot>(),
            "Last 24 hours",
            true);

        _hostHitRegions.Clear();
        _hostCaptionRegions.Clear();
        _siteHeadingRegions.Clear();
        _sidebarHitRegions.Clear();
        DrawBackground(g);
        DrawHeader(g, snapshot);

        int headerBandHeight = Embedded ? (snapshot.Monitoring ? 40 : 0) : 112;
        int statsTop = headerBandHeight;
        int statsHeight = 0;

        int bodyTop = statsTop + statsHeight + (Embedded ? 0 : 18);
        int footerHeight = Embedded ? 0 : Presentation.WallboardFooterHeight;
        bool hasOutages = snapshot.Monitoring && snapshot.Sites.SelectMany(site => site.Hosts).Any(host => IsActiveOutage(snapshot, host));
        int sidebarWidth = !hasOutages ? 0 : Embedded ? Math.Clamp((int)(ClientSize.Width * .25), 200, 260) : Math.Clamp((int)(ClientSize.Width * .25), 240, 360);
        int gap = hasOutages ? (Embedded ? 12 : 16) : 0;
        int availableBodyHeight = Math.Max(
            Embedded ? 100 : 240,
            ClientSize.Height - bodyTop - footerHeight - (Embedded ? 4 : 16));

        int cliHeight = ShowCli
            ? Math.Clamp((int)(availableBodyHeight * 0.28), 150, 240)
            : 0;

        int upperHeight = Embedded || !ShowCli ? availableBodyHeight : Math.Max(180, availableBodyHeight - cliHeight - 16);

        var mapRect = new Rectangle(
            Embedded ? 0 : 22,
            bodyTop,
            Math.Max(Embedded ? 160 : 300, ClientSize.Width - sidebarWidth - gap - (Embedded ? 0 : 44)),
            upperHeight);

        var sidebarRect = new Rectangle(
            mapRect.Right + gap,
            bodyTop,
            sidebarWidth,
            upperHeight);

        DrawPanel(g, mapRect);
        DrawTopology(g, snapshot, mapRect);
        if (hasOutages)
        {
            DrawPanel(g, sidebarRect);
            var sidebarClip = g.Save();
            g.SetClip(sidebarRect, System.Drawing.Drawing2D.CombineMode.Intersect);
            DrawSidebar(g, snapshot, sidebarRect);
            g.Restore(sidebarClip);
        }

        if (!Embedded && ShowCli)
        {
            var cliRect = new Rectangle(22, mapRect.Bottom + 16, ClientSize.Width - 44,
                Math.Max(80, ClientSize.Height - mapRect.Bottom - 16 - footerHeight - 16));
            DrawPanel(g, cliRect);
            var outputClip = g.Save();
            g.SetClip(cliRect, System.Drawing.Drawing2D.CombineMode.Intersect);
            DrawCli(g, snapshot, cliRect);
            g.Restore(outputClip);
        }

        RefreshHostTargets(g);
        DrawHostFocus(g);
        // Shortcuts remain available without a permanent legend.
    }

    private void DrawResultsTable(Graphics g, WallboardSnapshot snapshot, Rectangle rect)
    {
        _resultsViewport = rect;
        var clip = g.Save();
        g.SetClip(rect, System.Drawing.Drawing2D.CombineMode.Intersect);
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        float[] fractions = { 0, .28f, .54f, .73f, .89f, 1 };
        string[] headings = { "Address", "Name", "Site", "Status", "Latency" };
        int x = rect.X + 16, width = rect.Width - 32, y = rect.Y + 16;
        for (int c = 0; c < headings.Length; c++)
            g.DrawString(headings[c], _tinyFont, muted,
                new RectangleF(x + width * fractions[c], y, width * (fractions[c + 1] - fractions[c]), 24), format);
        y += 32;
        var hosts = snapshot.Sites.SelectMany(site => site.Hosts).Where(host => !IsHostSnoozed(host)).ToList();
        _resultScrollOffset = Math.Clamp(_resultScrollOffset, 0, Math.Max(0, hosts.Count - 1));
        int visible = 0;
        foreach (var host in hosts.Skip(_resultScrollOffset))
        {
            if (y + 32 > rect.Bottom - 36) break;
            string[] values = { host.Address, host.Label, host.Site,
                snapshot.Monitoring ? host.State.ToString() : "Stopped", host.LatencyMs is long ms ? $"{ms} ms" : "—" };
            for (int c = 0; c < values.Length; c++)
                g.DrawString(values[c], c == 0 ? _hostIpFont : _smallFont, text,
                    new RectangleF(x + width * fractions[c], y, width * (fractions[c + 1] - fractions[c]) - 8, 24), format);
            _sidebarHitRegions.Add((new Rectangle(x, y, width, 32), host));
            y += 32;
            visible++;
        }
        if (hosts.Count > visible)
            g.DrawString($"{_resultScrollOffset + 1}–{_resultScrollOffset + visible} of {hosts.Count} · Scroll for more", _tinyFont, muted, rect.X + 16, rect.Bottom - 28);
        g.Restore(clip);
    }

    private void DrawBackground(Graphics g)
    {
        g.Clear(WallboardStyle.Background);
    }

    private void DrawHeader(Graphics g, WallboardSnapshot snapshot)
    {
        using var brandBrush = new SolidBrush(WallboardStyle.Text);
        using var accentBrush = new SolidBrush(WallboardStyle.Accent);
        using var mutedBrush = new SolidBrush(WallboardStyle.Muted);

        var hosts = snapshot.Sites.SelectMany(site => site.Hosts).ToList();
        string summary = $"{hosts.Count(h => h.State == HostState.Online)} online · " +
            $"{hosts.Count(h => h.State == HostState.Suspect)} suspect · " +
            $"{hosts.Count(h => h.State == HostState.Offline)} offline";
        if (Embedded)
        {
            if (snapshot.Monitoring) g.DrawString(summary, _sectionFont, brandBrush, 0, 8);
            return;
        }
        g.DrawString(summary, _sectionFont, brandBrush, 24, 32);
        string clock = DisplayTime.Clock(snapshot.CapturedAt);
        string date = snapshot.CapturedAt.ToString("dddd, MMMM d, yyyy");

        var clockSize = g.MeasureString(clock, _clockFont);
        var dateSize = g.MeasureString(date, _dateFont);

        int rightEdge = ClientSize.Width - 24;

        g.DrawString(
            clock,
            _clockFont,
            brandBrush,
            rightEdge - clockSize.Width,
            10);

        g.DrawString(
            date,
            _dateFont,
            mutedBrush,
            rightEdge - dateSize.Width,
            48);

        var stateRect = new Rectangle(
            rightEdge - 220,
            74,
            220,
            24);

        using var stateBrush = new SolidBrush(snapshot.Monitoring
            ? WallboardStyle.Selection
            : WallboardStyle.Panel);
        using var stateText = new SolidBrush(snapshot.Monitoring
            ? WallboardStyle.Green
            : WallboardStyle.Muted);

        using (var pill = WallboardStyle.Rounded(stateRect, 10)) g.FillPath(stateBrush, pill);
        DrawCenteredText(
            g,
            snapshot.Monitoring ? "Monitoring" : "Stopped",
            _smallFont,
            stateText,
            stateRect);

        using var divider = new Pen(WallboardStyle.Line, 1);
        g.DrawLine(
            divider,
            22,
            108,
            Math.Max(22, ClientSize.Width - 22),
            108);
    }

    private void DrawStats(
        Graphics g,
        WallboardSnapshot snapshot,
        int top,
        int height)
    {
        var hosts = snapshot.Sites.SelectMany(s => s.Hosts).ToList();
        int total = hosts.Count;
        int online = hosts.Count(h => h.State == HostState.Online);
        int suspect = hosts.Count(h => h.State == HostState.Suspect);
        int offline = hosts.Count(h => h.State == HostState.Offline);

        int gap = 12;
        int left = 22;
        int width = ClientSize.Width - 44;
        int cardWidth = (width - gap * 3) / 4;

        DrawStatCard(g, new Rectangle(left, top, cardWidth, height), "Total hosts", total, WallboardStyle.Text);
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap), top, cardWidth, height), "Online", online, WallboardStyle.Green);
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 2, top, cardWidth, height), "Suspect", suspect, WallboardStyle.Yellow);
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 3, top, cardWidth, height), "Offline", offline, WallboardStyle.Red);
    }

    private void DrawStatCard(
        Graphics g,
        Rectangle rect,
        string label,
        int value,
        Color valueColor)
    {
        using var cardBrush = new SolidBrush(WallboardStyle.Panel);
        using var borderPen = new Pen(WallboardStyle.Line);
        using var valueBrush = new SolidBrush(valueColor);
        using var labelBrush = new SolidBrush(WallboardStyle.Muted);

        WallboardStyle.DrawSurface(g, rect, cardBrush, borderPen);

        g.DrawString(label, _statLabelFont, labelBrush, rect.X + 15, rect.Y + 10);
        g.DrawString(value.ToString(), _statValueFont, valueBrush, rect.X + 14, rect.Y + 28);
    }

    private static void DrawPanel(Graphics g, Rectangle rect)
    {
        using var fill = new SolidBrush(WallboardStyle.Panel);
        using var border = new Pen(WallboardStyle.Line);
        WallboardStyle.DrawSurface(g, rect, fill, border);
    }

    internal static (string Primary, string? Secondary) FormatHostNodeLines(
        WallboardHostSnapshot host)
    {
        string address = host.Address.Trim();
        string label = host.Label.Trim();

        if (host.SnoozedUntilUtc is { } until && until > DateTimeOffset.UtcNow)
            return (string.IsNullOrWhiteSpace(label) ? "Alerts snoozed" : label + " [snoozed]", address);

        return string.IsNullOrWhiteSpace(label)
            ? (address, null)
            : (label, address);
    }

    private void DrawSidebar(Graphics g, WallboardSnapshot snapshot, Rectangle rect)
    {
        using var text = new SolidBrush(WallboardStyle.Text);
        using var muted = new SolidBrush(WallboardStyle.Muted);
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        int x = rect.X + 16, y = rect.Y + 16, width = rect.Width - 32;
        g.DrawString("Active outages", _sectionFont, text, x, y);
        y += 32;
        foreach (var host in snapshot.Sites.SelectMany(site => site.Hosts).Where(host => IsActiveOutage(snapshot, host)))
        {
            if (y + 48 > rect.Bottom - 12) break;
            _sidebarHitRegions.Add((new Rectangle(x, y, width, 48), host));
            g.DrawString(string.IsNullOrWhiteSpace(host.Label) ? host.Address : host.Label,
                _smallFont, text, new RectangleF(x, y, width, 20), format);
            g.DrawString(host.Site + (host.OutageStarted is DateTime started ? " · " + FormatElapsed(DateTime.Now - started) : ""),
                _tinyFont, muted, new RectangleF(x, y + 20, width, 20), format);
            y += 52;
        }
    }

    private void DrawCli(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        int x = rect.X + 14;
        int y = rect.Y + 11;
        int width = rect.Width - 28;
        int contentTop = y + 27;
        int contentBottom = rect.Bottom - 10;

        using var titleBrush = new SolidBrush(WallboardStyle.Muted);
        using var idleBrush = new SolidBrush(WallboardStyle.Muted);
        using var successBrush = new SolidBrush(WallboardStyle.Green);
        using var failureBrush = new SolidBrush(WallboardStyle.Red);

        g.DrawString("Ping activity", _sectionFont, titleBrush, x, y);

        // The footer already lists C for the log; one home per fact (A3).

        if (snapshot.Commands.Count == 0)
        {
            g.DrawString(
                snapshot.Monitoring
                    ? "Waiting for replies"
                    : "No ping activity",
                _cliFont,
                idleBrush,
                x,
                contentTop);
            return;
        }

        float lineHeight = Math.Max(15f, _cliFont.GetHeight(g) + 2f);
        int lineCapacity = Math.Max(
            1,
            (int)((contentBottom - contentTop) / lineHeight));

        var visible = snapshot.Commands
            .TakeLast(lineCapacity)
            .ToList();

        float lineY = contentBottom - visible.Count * lineHeight;

        foreach (var command in visible)
        {
            Brush brush = command.Success
                ? successBrush
                : failureBrush;

            string text = TrimToWidth(
                g,
                command.Text,
                _cliFont,
                width);

            g.DrawString(
                text,
                _cliFont,
                brush,
                x,
                lineY);

            lineY += lineHeight;
        }
    }

    private void DrawFooter(Graphics g)
    {
        using var mutedBrush = new SolidBrush(WallboardStyle.Muted);
        string left = ShowCli
            ? "Esc Exit Wallboard   O Sites   P Start/Stop   C Hide log   H History range   S Suspects"
            : "Esc Exit Wallboard   O Sites   P Start/Stop   C Show log   H History range   S Suspects";
        g.DrawString(left, _tinyFont, mutedBrush, 23, ClientSize.Height - 25);
    }

    internal static HostState AggregateSiteState(WallboardSiteSnapshot site)
    {
        var activeHosts = site.Hosts.Where(host => !IsHostSnoozed(host)).ToList();
        int total = activeHosts.Count;

        if (total == 0)
            return HostState.Unknown;

        int online = activeHosts.Count(h => h.State == HostState.Online);
        int offline = activeHosts.Count(h => h.State == HostState.Offline);
        int suspect = activeHosts.Count(h => h.State == HostState.Suspect);

        if (offline > 0)
        {
            // A site with some failed hosts is degraded while a strict
            // majority of its configured hosts are still replying.
            return online > total / 2d
                ? HostState.Suspect
                : HostState.Offline;
        }

        if (suspect > 0)
            return HostState.Suspect;

        if (online > 0)
            return HostState.Online;

        return HostState.Unknown;
    }

    private static Color StateColor(HostState state, int alpha)
    {
        var p = AppTheme.Palette;
        Color value = state switch {
            HostState.Online => p.Teal, HostState.Suspect => p.Warning,
            HostState.Offline => p.Danger, _ => p.Secondary
        };
        return Color.FromArgb(alpha, value);
    }

    private static void DrawCenteredText(
        Graphics g,
        string text,
        Font font,
        Brush brush,
        Rectangle rect)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(text, font, brush, rect, format);
    }

    private static string TrimToWidth(
        Graphics g,
        string text,
        Font font,
        int width)
    {
        if (g.MeasureString(text, font).Width <= width)
            return text;

        string candidate = text;

        while (candidate.Length > 3 &&
               g.MeasureString(candidate + "…", font).Width > width)
        {
            candidate = candidate[..^1];
        }

        return candidate + "…";
    }

    private static string FormatElapsed(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";

        return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
    }
}
