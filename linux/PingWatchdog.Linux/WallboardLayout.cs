using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using PingWatchdog.Shared;
using UiTheme = PingWatchdog.Linux.Theme;

namespace PingWatchdog.Linux;

internal sealed partial class WallboardWindow
{
    private readonly TextBlock[] _statValues = Enumerable.Range(0, 4).Select(_ => Theme.Label("0", 34.7)).ToArray();
    private readonly TextBlock _historyFilter = Theme.Label("", 10, color: Theme.Muted);
    private Grid _board = null!;
    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Background = Theme.Brush(Presentation.WallboardBackground) };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Theme.Brush(Presentation.WallboardPanel), MinHeight = 42 };
        var toolbarTitle = Theme.Label("PING WATCHDOG • WALLBOARD", 13.3); toolbarTitle.Margin = new Thickness(18, 10); toolbar.Children.Add(toolbarTitle);
        var actions = new WrapPanel { Margin = new Thickness(0, 2, 10, 2) };
        _startStop.Click += (_, _) => ToggleMonitoring();
        var ops = Theme.Button("Operations", true); ops.Click += (_, _) => ToggleOperations();
        var cli = Theme.Button("Hide CLI"); cli.Click += (_, _) => { ToggleCli(); cli.Content = _showCli ? "Hide CLI" : "Show CLI"; };
        var history = Theme.Button("History"); history.Click += (_, _) => new HistoryWindow(_engine).Show(this);
        var settings = Theme.Button("Settings"); settings.Click += (_, _) => new SettingsWindow(_engine, _updates, _main).Show(this);
        var screen = Theme.Button("Screen"); screen.Click += async (_, _) => await MoveToNextScreenAsync();
        var main = Theme.Button("Main Window"); main.Click += (_, _) => Close();
        _updateButton.Click += async (_, _) => await _main.RunUpdateActionAsync(this);
        foreach (var b in new[] { _startStop, ops, cli, history, settings, _updateButton, screen, main }) { b.FontSize = 12; b.MinHeight = 30; b.Margin = new Thickness(0, 2, 6, 2); actions.Children.Add(b); }
        Grid.SetColumn(actions, 1); toolbar.Children.Add(actions); root.Children.Add(toolbar);
        var workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _board = new Grid { RowDefinitions = new RowDefinitions("116,82,*,Auto,34") };
        var background = new Grid { Children = { new WallboardBackdrop(), _board } }; workspace.Children.Add(background);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 12, 24, 10) };
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
            new Image { Source = BrandAssets.Logo, Width = 58, Height = 58, Stretch = Stretch.Uniform },
            new StackPanel { Children = { Theme.Label("PING WATCHDOG", 32, FontWeight.Normal), Theme.Label("NETWORK OPERATIONS WALLBOARD", 12, color: Theme.Cyan) } }
        } });
        _clock.FontSize = 32; _clock.FontWeight = FontWeight.Normal; _state.FontWeight = FontWeight.Normal; _date.FontSize = 12; _state.FontSize = 11;
        var clock = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Children = { _clock, _date,
            new Border { Background = Theme.Brush("#303A46"), Padding = new Thickness(12, 4), Width = 220, Child = _state } } };
        Grid.SetColumn(clock, 1); header.Children.Add(clock); _board.Children.Add(header);
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(22, 0, 22, 0) };
        string[] labels = { "TOTAL HOSTS", "ONLINE", "SUSPECT", "OFFLINE" };
        IBrush[] colors = { Theme.Brush("#BCD2E8"), Theme.Green, Theme.Yellow, Theme.Red };
        for (int i = 0; i < 4; i++) {
            _statValues[i].Foreground = colors[i];
            var card = new Border { Background = Theme.Brush(Presentation.WallboardCard), BorderBrush = Theme.Brush("#222F3D"), BorderThickness = new Thickness(1), Padding = new Thickness(15, 10),
                Margin = new Thickness(0, 0, i == 3 ? 0 : 12, 0), Child = new StackPanel { Spacing = 3, Children = { Theme.Label(labels[i], 10, color: Theme.Muted), _statValues[i] } } };
            Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        Grid.SetRow(stats, 1); _board.Children.Add(stats);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 18, 22, 0) };
        _topologyScroll.Content = _topology;
        var map = new Grid { RowDefinitions = new RowDefinitions("42,*") };
        var mapTitle = Theme.Label("LIVE SITE / HOST TOPOLOGY", 12, color: Theme.Muted); mapTitle.Margin = new Thickness(16, 12); map.Children.Add(mapTitle);
        Grid.SetRow(_topologyScroll, 1); map.Children.Add(_topologyScroll);
        _topology.Background = Theme.Brush(Presentation.WallboardPanel);
        body.Children.Add(new Border { Background = Theme.Brush(Presentation.WallboardPanel), BorderBrush = Theme.Border, BorderThickness = new Thickness(1), Child = map });
        _reportsCard = Theme.CardBorder(BuildReports(), new Thickness(16, 0, 0, 0)); _reportsCard.Width = 320; _reportsCard.Background = Theme.Brush(Presentation.WallboardPanel);
        Grid.SetColumn(_reportsCard, 1); body.Children.Add(_reportsCard); Grid.SetRow(body, 2); _board.Children.Add(body);
        _cli.Margin = new Thickness(22, 16, 22, 0); _cli.MinHeight = 150; _cli.MaxHeight = 240;
        Grid.SetRow(_cli, 3); _board.Children.Add(_cli);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 8, 22, 4) };
        var shortcuts = Theme.Label("ESC Main   O Operations   P Start/Stop   C CLI   H Range   S Suspects   Ctrl+H History   Ctrl+, Settings", 9, color: Theme.Muted); shortcuts.TextWrapping = TextWrapping.Wrap; footer.Children.Add(shortcuts);
        var copyright = Theme.Label("© 2026 Joseph Luker • All rights reserved.", 9, color: Theme.Muted); Grid.SetColumn(copyright, 1); footer.Children.Add(copyright);
        Grid.SetRow(footer, 4); _board.Children.Add(footer);
        BuildOpsPanel();
        var closeOps = Theme.Button("Close Operations"); closeOps.Click += (_, _) => ToggleOperations(); _ops.Children.Add(closeOps);
        _opsCard = Theme.CardBorder(new ScrollViewer { Content = _ops, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, new Thickness(0));
        _opsCard.Background = Theme.Brush("#0A1017"); _opsCard.IsVisible = false;
        Grid.SetColumn(_opsCard, 1); workspace.Children.Add(_opsCard); Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        _board.SizeChanged += (_, _) => _reportsCard.Width = Math.Clamp(_board.Bounds.Width * .29, 320, 440);
        SizeChanged += (_, _) => { _opsCard.Width = Math.Clamp(Bounds.Width / 3, 360, 440); _ops.Width = _opsCard.Width - 28; };
        return root;
    }

    private void DrawTopology(WatchdogSnapshot snapshot)
    {
        _topology.Children.Clear();
        var sites = snapshot.Sites.Where(s => snapshot.SelectedSite is null || s.Name.Equals(snapshot.SelectedSite, StringComparison.OrdinalIgnoreCase)).ToList();
        double width = Math.Max(300, _topologyScroll.Bounds.Width), height = Math.Max(210, _topologyScroll.Bounds.Height);
        if (sites.Any(s => s.Hosts.Count > 0)) { width = Math.Max(580, width); height = Math.Max(380, height); }
        _topology.Width = width; _topology.Height = height;
        var center = new Point(width / 2, height / 2);
        double maxRadius = Math.Min(width, height) / 2 - 18;
        for (double r = maxRadius / 4; r <= maxRadius; r += maxRadius / 4) {
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = Theme.Brush("#123E48"), StrokeThickness = 1 };
            Canvas.SetLeft(ring, center.X - r); Canvas.SetTop(ring, center.Y - r); _topology.Children.Add(ring);
        }
        double phase = DateTime.UtcNow.TimeOfDay.TotalSeconds * .45;
        AddLine(center, new Point(center.X + Math.Cos(phase) * maxRadius, center.Y + Math.Sin(phase) * maxRadius), Theme.Brush("#34A6B7"), 1);
        int coreRadius = Math.Clamp((int)Math.Min(width, height) / 11, 38, 58);
        var points = Presentation.SitePoints(sites.Count, 18, 8, width - 36, height - 16, coreRadius);
        for (int i = 0; i < sites.Count; i++) AddLine(center, new Point(points[i].X, points[i].Y), Theme.State(AggregateSite(sites[i])), 1.4);
        AddCircle(center.X - coreRadius, center.Y - coreRadius, coreRadius * 2, Theme.Cyan);
        AddText("WATCHDOG", center.X - coreRadius, center.Y - 8, 12, Theme.Text, FontWeight.Bold, coreRadius * 2, true);
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i]; var point = new Point(points[i].X, points[i].Y); int visible = Math.Min(Presentation.VisibleTopologyHosts, site.Hosts.Count);
            for (int h = 0; h < visible; h++) {
                var hp = Presentation.HostPoint(h, visible, points[i]); AddLine(point, new Point(hp.X, hp.Y), Theme.State(site.Hosts[h].State), 1);
            }
            AddCircle(point.X - 38, point.Y - 38, 76, Theme.State(AggregateSite(site)));
            AddText(site.Name, point.X - 33, point.Y - 20, 11, Theme.Text, FontWeight.Bold, 66, true);
            AddText($"{site.Hosts.Count(h => h.State == HostState.Online)}/{site.Hosts.Count} online", point.X - 33, point.Y + 10, 9, Theme.Muted, FontWeight.Normal, 66, true);
            for (int h = 0; h < visible; h++) {
                var host = site.Hosts[h]; var hp = Presentation.HostPoint(h, visible, points[i]); var node = new Ellipse { Width = 10, Height = 10, Fill = Theme.State(host.State), Stroke = Theme.Brush("#E1ECF6"), StrokeThickness = 1 }; Canvas.SetLeft(node, hp.X - 5); Canvas.SetTop(node, hp.Y - 5); _topology.Children.Add(node);
                double captionWidth = 124, captionHeight = string.IsNullOrWhiteSpace(host.Label) ? 22 : 34;
                double left = Math.Clamp(hp.X >= point.X ? hp.X + 9 : hp.X - captionWidth - 9, 4, width - captionWidth - 4);
                double top = Math.Clamp(hp.Y - captionHeight / 2, 4, height - captionHeight - 4);
                var caption = new StackPanel { Children = {
                    Theme.Label(string.IsNullOrWhiteSpace(host.Label) ? host.Address : host.Label, 10, FontWeight.Normal),
                } };
                if (!string.IsNullOrWhiteSpace(host.Label)) caption.Children.Add(Theme.Label(host.Address, 9, color: Theme.Muted));
                foreach (var text in caption.Children.OfType<TextBlock>()) text.TextTrimming = TextTrimming.CharacterEllipsis;
                var box = new Border { Width = captionWidth, Height = captionHeight, Padding = new Thickness(5, 2), Background = Theme.Brush("#070D13"), BorderBrush = Theme.State(host.State), BorderThickness = new Thickness(1), Child = caption };
                Canvas.SetLeft(box, left); Canvas.SetTop(box, top); _topology.Children.Add(box);
            }
            if (site.Hosts.Count > visible) AddText($"+{site.Hosts.Count - visible} more hosts", Math.Clamp(point.X - 55, 0, width - 140), Math.Clamp(point.Y + 86, 0, height - 20), 10, Theme.Muted, FontWeight.Normal, 140);
        }
    }
}

internal sealed class WallboardBackdrop : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(UiTheme.Brush("#121B25"), 1);
        for (int x = 0; x < Bounds.Width; x += 48) context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        for (int y = 0; y < Bounds.Height; y += 48) context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }
}
