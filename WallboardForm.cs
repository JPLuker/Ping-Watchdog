namespace PingWatchdog;

internal sealed class WallboardForm : Form
{
    private readonly Func<WallboardSnapshot> _snapshotProvider;
    private readonly WallboardCanvas _canvas = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly Action _cycleHistoryWindow;
    private readonly Action _toggleSuspectHistory;
    private int _screenIndex;
    private bool _showCli = true;

    public WallboardForm(
        Func<WallboardSnapshot> snapshotProvider,
        Screen targetScreen,
        Action cycleHistoryWindow,
        Action toggleSuspectHistory)
    {
        _snapshotProvider = snapshotProvider;
        _cycleHistoryWindow = cycleHistoryWindow;
        _toggleSuspectHistory = toggleSuspectHistory;

        Text = "Ping Watchdog Wallboard";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(5, 9, 14);
        ForeColor = Color.White;
        KeyPreview = true;
        ShowInTaskbar = true;

        var screens = Screen.AllScreens;
        _screenIndex = Array.FindIndex(
            screens,
            s => s.DeviceName.Equals(
                targetScreen.DeviceName,
                StringComparison.OrdinalIgnoreCase));

        if (_screenIndex < 0)
            _screenIndex = 0;

        Bounds = screens[_screenIndex].Bounds;

        _canvas.Dock = DockStyle.Fill;
        _canvas.ShowCli = _showCli;
        Controls.Add(_canvas);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Escape or Keys.F11)
            {
                e.Handled = true;
                Close();
            }
            else if (e.KeyCode == Keys.M)
            {
                e.Handled = true;
                MoveToNextScreen();
            }
            else if (e.KeyCode == Keys.C)
            {
                e.Handled = true;
                _showCli = !_showCli;
                _canvas.ShowCli = _showCli;
                _canvas.Invalidate();
            }
            else if (e.KeyCode == Keys.H)
            {
                e.Handled = true;
                _cycleHistoryWindow();
                RefreshSnapshot();
            }
            else if (e.KeyCode == Keys.S)
            {
                e.Handled = true;
                _toggleSuspectHistory();
                RefreshSnapshot();
            }
        };

        _timer.Tick += (_, _) => RefreshSnapshot();

        Shown += (_, _) =>
        {
            Bounds = Screen.AllScreens[_screenIndex].Bounds;
            RefreshSnapshot();
            _timer.Start();
            Activate();
        };

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _canvas.Dispose();
        };
    }

    private void RefreshSnapshot()
    {
        if (IsDisposed || Disposing)
            return;

        try
        {
            _canvas.Snapshot = _snapshotProvider();
            _canvas.Invalidate();
        }
        catch
        {
            // The monitoring UI must remain available even if one wallboard refresh fails.
        }
    }

    private void MoveToNextScreen()
    {
        var screens = Screen.AllScreens;

        if (screens.Length == 0)
            return;

        _screenIndex = (_screenIndex + 1) % screens.Length;
        Bounds = screens[_screenIndex].Bounds;
        Activate();
    }
}

internal sealed class WallboardCanvas : Control
{
    private readonly Font _brandFont = new("Segoe UI Semibold", 24, FontStyle.Bold);
    private readonly Font _subtitleFont = new("Segoe UI Semibold", 9, FontStyle.Bold);
    private readonly Font _clockFont = new("Segoe UI Semibold", 22, FontStyle.Bold);
    private readonly Font _dateFont = new("Segoe UI", 9.5f);
    private readonly Font _statValueFont = new("Segoe UI Semibold", 25, FontStyle.Bold);
    private readonly Font _statLabelFont = new("Segoe UI Semibold", 8, FontStyle.Bold);
    private readonly Font _sectionFont = new("Segoe UI Semibold", 9, FontStyle.Bold);
    private readonly Font _siteFont = new("Segoe UI Semibold", 9.5f, FontStyle.Bold);
    private readonly Font _siteSmallFont = new("Segoe UI Semibold", 8f, FontStyle.Bold);
    private readonly Font _siteTinyFont = new("Segoe UI Semibold", 6.75f, FontStyle.Bold);
    private readonly Font _smallFont = new("Segoe UI", 8.5f);
    private readonly Font _tinyFont = new("Segoe UI", 7.5f);
    private readonly Font _coreFont = new("Segoe UI Semibold", 9, FontStyle.Bold);
    private readonly Font _cliFont = new("Cascadia Mono", 8.5f);

    public WallboardSnapshot? Snapshot { get; set; }
    public bool ShowCli { get; set; } = true;

    public WallboardCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(5, 9, 14);
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

        DrawBackground(g);
        DrawHeader(g, snapshot);

        int statsTop = 94;
        int statsHeight = 82;
        DrawStats(g, snapshot, statsTop, statsHeight);

        int bodyTop = statsTop + statsHeight + 18;
        int footerHeight = 34;
        int sidebarWidth = Math.Clamp((int)(ClientSize.Width * 0.29), 320, 440);
        int gap = 16;
        int availableBodyHeight = Math.Max(
            240,
            ClientSize.Height - bodyTop - footerHeight - 16);

        int cliHeight = ShowCli
            ? Math.Clamp((int)(availableBodyHeight * 0.28), 150, 240)
            : 0;

        int upperHeight = ShowCli
            ? Math.Max(210, availableBodyHeight - cliHeight - gap)
            : availableBodyHeight;

        var mapRect = new Rectangle(
            22,
            bodyTop,
            Math.Max(300, ClientSize.Width - sidebarWidth - gap - 44),
            upperHeight);

        var sidebarRect = new Rectangle(
            mapRect.Right + gap,
            bodyTop,
            sidebarWidth,
            upperHeight);

        DrawPanel(g, mapRect);
        DrawPanel(g, sidebarRect);
        DrawTopology(g, snapshot, mapRect);
        DrawSidebar(g, snapshot, sidebarRect);

        if (ShowCli)
        {
            var cliRect = new Rectangle(
                22,
                mapRect.Bottom + gap,
                Math.Max(300, ClientSize.Width - 44),
                cliHeight);

            DrawPanel(g, cliRect);
            DrawCli(g, snapshot, cliRect);
        }

        DrawFooter(g);
    }

    private void DrawBackground(Graphics g)
    {
        g.Clear(Color.FromArgb(5, 9, 14));

        using var gridPen = new Pen(Color.FromArgb(18, 27, 37), 1);

        for (int x = 0; x < ClientSize.Width; x += 48)
            g.DrawLine(gridPen, x, 0, x, ClientSize.Height);

        for (int y = 0; y < ClientSize.Height; y += 48)
            g.DrawLine(gridPen, 0, y, ClientSize.Width, y);
    }

    private void DrawHeader(Graphics g, WallboardSnapshot snapshot)
    {
        using var brandBrush = new SolidBrush(Color.FromArgb(238, 244, 250));
        using var accentBrush = new SolidBrush(Color.FromArgb(82, 177, 202));
        using var mutedBrush = new SolidBrush(Color.FromArgb(126, 143, 160));

        g.DrawString("PING WATCHDOG", _brandFont, brandBrush, 22, 14);
        g.DrawString(
            "NETWORK OPERATIONS WALLBOARD",
            _subtitleFont,
            accentBrush,
            25,
            57);

        string clock = snapshot.CapturedAt.ToString("h:mm:ss tt");
        string date = snapshot.CapturedAt.ToString("dddd, MMMM d, yyyy");

        var clockSize = g.MeasureString(clock, _clockFont);
        var dateSize = g.MeasureString(date, _dateFont);

        g.DrawString(
            clock,
            _clockFont,
            brandBrush,
            ClientSize.Width - clockSize.Width - 24,
            12);

        g.DrawString(
            date,
            _dateFont,
            mutedBrush,
            ClientSize.Width - dateSize.Width - 26,
            43);

        var stateRect = new Rectangle(
            ClientSize.Width - 240,
            66,
            214,
            22);

        using var stateBrush = new SolidBrush(snapshot.Monitoring
            ? Color.FromArgb(24, 92, 64)
            : Color.FromArgb(48, 58, 70));
        using var stateText = new SolidBrush(snapshot.Monitoring
            ? Color.FromArgb(112, 229, 162)
            : Color.FromArgb(165, 179, 194));

        g.FillRectangle(stateBrush, stateRect);
        DrawCenteredText(
            g,
            snapshot.Monitoring ? "LIVE MONITORING" : "IDLE / CONFIG VIEW",
            _tinyFont,
            stateText,
            stateRect);
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

        DrawStatCard(g, new Rectangle(left, top, cardWidth, height), "TOTAL HOSTS", total, Color.FromArgb(188, 210, 232));
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap), top, cardWidth, height), "ONLINE", online, Color.FromArgb(78, 216, 143));
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 2, top, cardWidth, height), "SUSPECT", suspect, Color.FromArgb(245, 191, 71));
        DrawStatCard(g, new Rectangle(left + (cardWidth + gap) * 3, top, cardWidth, height), "OFFLINE", offline, Color.FromArgb(255, 101, 111));
    }

    private void DrawStatCard(
        Graphics g,
        Rectangle rect,
        string label,
        int value,
        Color valueColor)
    {
        using var cardBrush = new SolidBrush(Color.FromArgb(14, 21, 29));
        using var borderPen = new Pen(Color.FromArgb(34, 47, 61));
        using var valueBrush = new SolidBrush(valueColor);
        using var labelBrush = new SolidBrush(Color.FromArgb(125, 143, 161));

        g.FillRectangle(cardBrush, rect);
        g.DrawRectangle(borderPen, rect);

        g.DrawString(label, _statLabelFont, labelBrush, rect.X + 15, rect.Y + 10);
        g.DrawString(value.ToString(), _statValueFont, valueBrush, rect.X + 14, rect.Y + 28);
    }

    private static void DrawPanel(Graphics g, Rectangle rect)
    {
        using var fill = new SolidBrush(Color.FromArgb(9, 15, 22));
        using var border = new Pen(Color.FromArgb(32, 44, 57));
        g.FillRectangle(fill, rect);
        g.DrawRectangle(border, rect);
    }

    private void DrawTopology(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));
        g.DrawString("LIVE SITE TOPOLOGY", _sectionFont, titleBrush, rect.X + 16, rect.Y + 13);

        var content = new Rectangle(
            rect.X + 18,
            rect.Y + 42,
            rect.Width - 36,
            rect.Height - 58);

        if (snapshot.Sites.Count == 0)
        {
            using var emptyBrush = new SolidBrush(Color.FromArgb(100, 119, 138));
            DrawCenteredText(
                g,
                "No sites configured",
                _siteFont,
                emptyBrush,
                content);
            return;
        }

        var center = new Point(
            content.X + content.Width / 2,
            content.Y + content.Height / 2);

        DrawRadar(g, content, center);

        int coreRadius = Math.Clamp(
            Math.Min(content.Width, content.Height) / 11,
            38,
            58);

        var sitePoints = CalculateSitePoints(
            snapshot.Sites.Count,
            content,
            center,
            coreRadius);

        for (int i = 0; i < snapshot.Sites.Count; i++)
        {
            var site = snapshot.Sites[i];
            var point = sitePoints[i];
            var status = AggregateSiteState(site);

            DrawConnection(g, center, point, status, i);
        }

        DrawCore(g, center, coreRadius, snapshot.Monitoring);

        for (int i = 0; i < snapshot.Sites.Count; i++)
        {
            DrawSiteNode(
                g,
                snapshot.Sites[i],
                sitePoints[i],
                i);
        }
    }

    private void DrawRadar(Graphics g, Rectangle rect, Point center)
    {
        using var ringPen = new Pen(Color.FromArgb(18, 62, 72), 1);

        int maxRadius = Math.Max(80, Math.Min(rect.Width, rect.Height) / 2 - 12);

        for (int r = maxRadius / 4; r <= maxRadius; r += Math.Max(1, maxRadius / 4))
            g.DrawEllipse(ringPen, center.X - r, center.Y - r, r * 2, r * 2);

        double phase = DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.45;
        var end = new Point(
            center.X + (int)(Math.Cos(phase) * maxRadius),
            center.Y + (int)(Math.Sin(phase) * maxRadius));

        using var sweepPen = new Pen(Color.FromArgb(80, 52, 166, 183), 2);
        g.DrawLine(sweepPen, center, end);
    }

    private static List<Point> CalculateSitePoints(
        int count,
        Rectangle rect,
        Point center,
        int coreRadius)
    {
        var points = new List<Point>(count);
        double radiusX = Math.Max(coreRadius + 80, rect.Width * 0.37);
        double radiusY = Math.Max(coreRadius + 65, rect.Height * 0.34);

        for (int i = 0; i < count; i++)
        {
            double angle = (-Math.PI / 2) + (Math.PI * 2 * i / Math.Max(1, count));
            points.Add(new Point(
                center.X + (int)(Math.Cos(angle) * radiusX),
                center.Y + (int)(Math.Sin(angle) * radiusY)));
        }

        return points;
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

        double phase = (DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 1400d + index * 0.17) % 1d;
        var pulse = new Point(
            center.X + (int)((site.X - center.X) * phase),
            center.Y + (int)((site.Y - center.Y) * phase));

        using var pulseBrush = new SolidBrush(StateColor(status, 220));
        g.FillEllipse(pulseBrush, pulse.X - 3, pulse.Y - 3, 6, 6);
    }

    private void DrawCore(
        Graphics g,
        Point center,
        int radius,
        bool monitoring)
    {
        using var glowBrush = new SolidBrush(Color.FromArgb(24, 60, 175, 194));
        using var fillBrush = new SolidBrush(Color.FromArgb(15, 39, 48));
        using var outlinePen = new Pen(Color.FromArgb(77, 190, 208), 2);
        using var textBrush = new SolidBrush(Color.FromArgb(205, 238, 243));

        g.FillEllipse(glowBrush, center.X - radius - 8, center.Y - radius - 8, (radius + 8) * 2, (radius + 8) * 2);
        g.FillEllipse(fillBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(outlinePen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

        var coreRect = new Rectangle(
            center.X - radius,
            center.Y - 12,
            radius * 2,
            24);

        DrawCenteredText(g, "WATCHDOG", _coreFont, textBrush, coreRect);

        using var dotBrush = new SolidBrush(monitoring
            ? Color.FromArgb(87, 224, 149)
            : Color.FromArgb(118, 134, 151));

        g.FillEllipse(dotBrush, center.X - 4, center.Y + radius - 15, 8, 8);
    }

    private void DrawSiteNode(
        Graphics g,
        WallboardSiteSnapshot site,
        Point center,
        int index)
    {
        var state = AggregateSiteState(site);
        int radius = 38;
        Color color = StateColor(state, 255);

        if (state == HostState.Offline)
        {
            double pulse = (Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 280d) + 1d) / 2d;
            int pulseRadius = radius + 7 + (int)(pulse * 8);
            using var pulsePen = new Pen(StateColor(state, 85), 2);
            g.DrawEllipse(
                pulsePen,
                center.X - pulseRadius,
                center.Y - pulseRadius,
                pulseRadius * 2,
                pulseRadius * 2);
        }

        using var fillBrush = new SolidBrush(Color.FromArgb(15, 23, 31));
        using var borderPen = new Pen(color, 2);
        using var nameBrush = new SolidBrush(Color.FromArgb(232, 239, 246));
        using var countBrush = new SolidBrush(Color.FromArgb(139, 158, 178));

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

        DrawHostDots(g, site, center, radius + 14);
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

    private void DrawHostDots(
        Graphics g,
        WallboardSiteSnapshot site,
        Point center,
        int orbitRadius)
    {
        int visible = Math.Min(site.Hosts.Count, 16);

        for (int i = 0; i < visible; i++)
        {
            double angle = Math.PI * 2 * i / Math.Max(1, visible);
            int x = center.X + (int)(Math.Cos(angle) * orbitRadius);
            int y = center.Y + (int)(Math.Sin(angle) * orbitRadius);
            using var brush = new SolidBrush(StateColor(site.Hosts[i].State, 240));
            g.FillEllipse(brush, x - 3, y - 3, 6, 6);
        }

        if (site.Hosts.Count > visible)
        {
            using var moreBrush = new SolidBrush(Color.FromArgb(137, 154, 171));
            g.DrawString(
                $"+{site.Hosts.Count - visible}",
                _tinyFont,
                moreBrush,
                center.X + orbitRadius - 3,
                center.Y + orbitRadius - 3);
        }
    }

    private void DrawSidebar(
        Graphics g,
        WallboardSnapshot snapshot,
        Rectangle rect)
    {
        int x = rect.X + 16;
        int y = rect.Y + 13;
        int width = rect.Width - 32;

        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));
        using var primaryBrush = new SolidBrush(Color.FromArgb(226, 234, 242));
        using var mutedBrush = new SolidBrush(Color.FromArgb(126, 143, 160));
        using var dividerPen = new Pen(Color.FromArgb(31, 44, 57));

        g.DrawString("ACTIVE OUTAGES", _sectionFont, titleBrush, x, y);
        y += 28;

        var outages = snapshot.Sites
            .SelectMany(s => s.Hosts)
            .Where(h => h.State == HostState.Offline)
            .OrderBy(h => h.OutageStarted)
            .Take(7)
            .ToList();

        if (outages.Count == 0)
        {
            using var okBrush = new SolidBrush(Color.FromArgb(87, 207, 142));
            g.DrawString(
                snapshot.Monitoring ? "No active outages" : "Monitoring is idle",
                _smallFont,
                okBrush,
                x,
                y);
            y += 31;
        }
        else
        {
            foreach (var outage in outages)
            {
                using var redBrush = new SolidBrush(Color.FromArgb(255, 110, 119));
                string display = string.IsNullOrWhiteSpace(outage.Label)
                    ? outage.Address
                    : outage.Label;

                g.DrawString(display, _siteFont, redBrush, x, y);
                y += 18;

                string detail = outage.OutageStarted.HasValue
                    ? $"{outage.Site} • down {FormatElapsed(DateTime.Now - outage.OutageStarted.Value)}"
                    : outage.Site;

                g.DrawString(detail, _tinyFont, mutedBrush, x, y);
                y += 26;
            }
        }

        g.DrawLine(dividerPen, x, y + 3, x + width, y + 3);
        y += 20;
        g.DrawString("OUTAGE HISTORY", _sectionFont, titleBrush, x, y);

        string historyFilter = snapshot.HideSuspectEvents
            ? $"{snapshot.EventWindowLabel} • suspects hidden"
            : $"{snapshot.EventWindowLabel} • suspects shown";

        var filterSize = g.MeasureString(historyFilter, _tinyFont);
        g.DrawString(
            historyFilter,
            _tinyFont,
            mutedBrush,
            Math.Max(x, x + width - filterSize.Width),
            y + 1);

        y += 27;

        if (snapshot.Events.Count == 0)
        {
            g.DrawString(
                "No events match the saved filters.",
                _smallFont,
                mutedBrush,
                x,
                y);
        }

        foreach (var item in snapshot.Events
            .OrderByDescending(e => e.Timestamp)
            .Take(10))
        {
            Color kindColor = item.Kind switch
            {
                "DOWN" => Color.FromArgb(255, 101, 111),
                "RECOVERED" => Color.FromArgb(78, 216, 143),
                "SUSPECT" => Color.FromArgb(245, 191, 71),
                _ => Color.FromArgb(152, 170, 188)
            };

            using var kindBrush = new SolidBrush(kindColor);

            g.DrawString(
                item.Timestamp.ToString("MM/dd HH:mm"),
                _tinyFont,
                mutedBrush,
                x,
                y);

            g.DrawString(
                item.Kind,
                _tinyFont,
                kindBrush,
                x + 76,
                y);

            y += 16;

            string host = item.Host;
            g.DrawString(
                TrimToWidth(g, host, _smallFont, width),
                _smallFont,
                primaryBrush,
                x,
                y);

            y += 17;

            g.DrawString(
                TrimToWidth(g, item.Site, _tinyFont, width),
                _tinyFont,
                mutedBrush,
                x,
                y);

            y += 24;

            if (y > rect.Bottom - 35)
                break;
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

        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));
        using var hintBrush = new SolidBrush(Color.FromArgb(84, 111, 128));
        using var idleBrush = new SolidBrush(Color.FromArgb(98, 116, 133));
        using var successBrush = new SolidBrush(Color.FromArgb(100, 220, 132));
        using var failureBrush = new SolidBrush(Color.FromArgb(255, 111, 116));

        g.DrawString("LIVE CLI / CMD TRACE", _sectionFont, titleBrush, x, y);

        string hint = "C  HIDE";
        var hintSize = g.MeasureString(hint, _tinyFont);
        g.DrawString(
            hint,
            _tinyFont,
            hintBrush,
            rect.Right - hintSize.Width - 14,
            y + 1);

        if (snapshot.Commands.Count == 0)
        {
            g.DrawString(
                snapshot.Monitoring
                    ? "Waiting for ping output..."
                    : "Start monitoring to populate the live CLI trace.",
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
        using var mutedBrush = new SolidBrush(Color.FromArgb(90, 107, 125));
        string left = ShowCli
            ? "ESC Exit   M Monitor   C Hide CLI   H History range   S Suspects"
            : "ESC Exit   M Monitor   C Show CLI   H History range   S Suspects";
        string right = "© 2026 Joseph Luker • All rights reserved.";

        g.DrawString(left, _tinyFont, mutedBrush, 23, ClientSize.Height - 25);

        var size = g.MeasureString(right, _tinyFont);
        g.DrawString(
            right,
            _tinyFont,
            mutedBrush,
            ClientSize.Width - size.Width - 24,
            ClientSize.Height - 25);
    }

    internal static HostState AggregateSiteState(WallboardSiteSnapshot site)
    {
        int total = site.Hosts.Count;

        if (total == 0)
            return HostState.Unknown;

        int online = site.Hosts.Count(h => h.State == HostState.Online);
        int offline = site.Hosts.Count(h => h.State == HostState.Offline);
        int suspect = site.Hosts.Count(h => h.State == HostState.Suspect);

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
        return state switch
        {
            HostState.Online => Color.FromArgb(alpha, 77, 211, 141),
            HostState.Suspect => Color.FromArgb(alpha, 244, 189, 68),
            HostState.Offline => Color.FromArgb(alpha, 255, 98, 109),
            _ => Color.FromArgb(alpha, 102, 122, 141)
        };
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
