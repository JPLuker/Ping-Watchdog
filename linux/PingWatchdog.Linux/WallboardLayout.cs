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
    private readonly TextBlock[] _statValues = Enumerable.Range(0, 4).Select(_ => Theme.Label("0", 33.3)).ToArray();
    private readonly TextBlock _historyFilter = Theme.Label("", 10, color: Theme.Muted);
    private Grid _board = null!;
    private Control BuildLayout()
    {
        var root = new Grid { Background = Theme.Window };
        var actions = new WrapPanel();
        _startStop.Click += (_, _) => ToggleMonitoring();
        var cli = Theme.Button("Toggle CLI"); cli.Click += (_, _) => ToggleCli();
        var history = Theme.Button("History"); history.Click += (_, _) => new HistoryWindow(_engine).Show(this);
        var settings = Theme.Button("Settings"); settings.Click += (_, _) => new SettingsWindow(_engine, _updates, _main).Show(this);
        var screen = Theme.Button("Screen"); screen.Click += async (_, _) => await MoveToNextScreenAsync();
        var main = Theme.Button("Main window"); main.Click += (_, _) => Close();
        _updateButton.Click += async (_, _) => await _main.RunUpdateActionAsync(this);
        foreach (var button in new[] { _startStop, cli, history, settings, _updateButton, screen, main })
        {
            button.Margin = new Thickness(0, 2, 6, 2);
            actions.Children.Add(button);
        }
        var operations = new MenuItem { Header = "Operations" };
        operations.Click += (_, _) => ToggleOperations();
        var exit = new MenuItem { Header = "Exit Wallboard" };
        exit.Click += (_, _) => Close();
        root.ContextMenu = new ContextMenu { ItemsSource = new[] { operations, exit } };
        var workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _board = new Grid { RowDefinitions = new RowDefinitions($"{Presentation.WallboardHeaderHeight},{Presentation.WallboardStatsHeight},*,Auto") };
        var background = new Grid { Children = { new WallboardBackdrop(), _board } }; workspace.Children.Add(background);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 12, 24, 10) };
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
            new Image { Source = BrandAssets.Logo, Width = 58, Height = 58, Stretch = Stretch.Uniform },
            new StackPanel { Children = { Theme.Label("Ping Watchdog", 32, FontWeight.SemiBold) } }
        } });
        _clock.FontSize = 29.3; _clock.FontWeight = FontWeight.Normal; _state.FontWeight = FontWeight.Normal; _date.FontSize = 13; _state.FontSize = 13;
        var clock = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Children = { _clock, _date,
            new Border { Background = Theme.Surface2, CornerRadius = new CornerRadius(9999), Padding = new Thickness(12, 4), Width = 220, Child = _state } } };
        Grid.SetColumn(clock, 1); header.Children.Add(clock); _board.Children.Add(header);
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(22, 0, 22, 0) };
        string[] labels = { "Hosts", "Online", "Suspect", "Offline" };
        IBrush[] colors = { Theme.Text, Theme.Green, Theme.Yellow, Theme.Red };
        for (int i = 0; i < 4; i++) {
            _statValues[i].Foreground = colors[i];
            var card = new Border { Background = Theme.Card, BorderBrush = Theme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(15, 10),
                Margin = new Thickness(0, 0, i == 3 ? 0 : 12, 0), Child = new StackPanel { Spacing = 3, Children = { Theme.Label(labels[i], 10, color: Theme.Muted), _statValues[i] } } };
            Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        Grid.SetRow(stats, 1); _board.Children.Add(stats);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 18, 22, 0) };
        _topologyScroll.Content = _topology;
        var map = new Grid { RowDefinitions = new RowDefinitions("42,*") };
        // No heading over the map: it is the view (A2, say less).
        Grid.SetRow(_topologyScroll, 1); map.Children.Add(_topologyScroll);
        _topology.Background = Theme.Card;
        body.Children.Add(new Border { Background = Theme.Card, BorderBrush = Theme.Border, BorderThickness = new Thickness(1), Child = map });
        _reportsCard = Theme.CardBorder(BuildReports(), new Thickness(16, 0, 0, 0)); _reportsCard.Width = 320; _reportsCard.Background = Theme.Card;
        Grid.SetColumn(_reportsCard, 1); body.Children.Add(_reportsCard); Grid.SetRow(body, 2); _board.Children.Add(body);
        _cli.Margin = new Thickness(22, 16, 22, 0); _cli.MinHeight = 150; _cli.MaxHeight = 240;
        Grid.SetRow(_cli, 3); _board.Children.Add(_cli);
        BuildOpsPanel();
        _ops.Children.Insert(1, actions);
        var closeOps = Theme.Button("Close Operations"); closeOps.Click += (_, _) => ToggleOperations(); _ops.Children.Add(closeOps);
        _opsCard = Theme.CardBorder(new ScrollViewer { Content = _ops, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, new Thickness(0));
        _opsCard.Background = Theme.Card; _opsCard.IsVisible = false;
        Grid.SetColumn(_opsCard, 1); workspace.Children.Add(_opsCard); root.Children.Add(workspace);
        _board.SizeChanged += (_, _) => {
            _reportsCard.Width = Math.Clamp(_board.Bounds.Width * .29, 320, 440);
            _cli.Height = Math.Clamp(Math.Max(240, _board.Bounds.Height - 266) * .28, 150, 240);
        };
        SizeChanged += (_, _) => { _opsCard.Width = Math.Clamp(Bounds.Width / 3, 360, 440); _ops.Width = _opsCard.Width - 28; };
        return root;
    }

    private static IBrush TopologyState(HostState state) => Theme.State(state);

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
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = Theme.Border, StrokeThickness = 1 };
            Canvas.SetLeft(ring, center.X - r); Canvas.SetTop(ring, center.Y - r); _topology.Children.Add(ring);
        }
        double phase = DateTime.UtcNow.TimeOfDay.TotalSeconds * .45;
        AddLine(center, new Point(center.X + Math.Cos(phase) * maxRadius, center.Y + Math.Sin(phase) * maxRadius), Theme.Muted, 1);
        int coreRadius = Math.Clamp((int)Math.Min(width, height) / 11, 38, 58);
        var points = Presentation.SitePoints(sites.Count, 18, 8, width - 36, height - 16, coreRadius);
        for (int i = 0; i < sites.Count; i++) AddLine(center, new Point(points[i].X, points[i].Y), TopologyState(AggregateSite(sites[i])), 1.4);
        AddCircle(center.X - coreRadius, center.Y - coreRadius, coreRadius * 2, Theme.Text, Theme.Surface2);
        AddText("Monitor", center.X - coreRadius, center.Y - 8, 13, Theme.Text, FontWeight.SemiBold, coreRadius * 2, true);
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i]; var point = new Point(points[i].X, points[i].Y); int visible = Math.Min(Presentation.VisibleTopologyHosts, site.Hosts.Count);
            for (int h = 0; h < visible; h++) {
                var hp = Presentation.HostPoint(h, visible, points[i]); AddLine(point, new Point(hp.X, hp.Y), TopologyState(site.Hosts[h].State), 1);
            }
            AddCircle(point.X - 38, point.Y - 38, 76, TopologyState(AggregateSite(site)));
            AddText(site.Name, point.X - 33, point.Y - 20, 13, Theme.Text, FontWeight.SemiBold, 66, true);
            AddText($"{site.Hosts.Count(h => h.State == HostState.Online)}/{site.Hosts.Count} online", point.X - 33, point.Y + 10, 9, Theme.Muted, FontWeight.Normal, 66, true);
            for (int h = 0; h < visible; h++) {
                var host = site.Hosts[h]; var hp = Presentation.HostPoint(h, visible, points[i]); var node = new Ellipse { Width = 10, Height = 10, Fill = TopologyState(host.State), Stroke = Theme.Card, StrokeThickness = 1 }; Canvas.SetLeft(node, hp.X - 5); Canvas.SetTop(node, hp.Y - 5); _topology.Children.Add(node);
                double captionWidth = 124, captionHeight = string.IsNullOrWhiteSpace(host.Label) ? 22 : 34;
                double left = Math.Clamp(hp.X >= point.X ? hp.X + 9 : hp.X - captionWidth - 9, 4, width - captionWidth - 4);
                double top = Math.Clamp(hp.Y - captionHeight / 2, 4, height - captionHeight - 4);
                var caption = new StackPanel { Children = {
                    Theme.Label(string.IsNullOrWhiteSpace(host.Label) ? host.Address : host.Label, 10, FontWeight.Normal),
                } };
                if (!string.IsNullOrWhiteSpace(host.Label)) caption.Children.Add(Theme.Label(host.Address, 9, color: Theme.Muted));
                foreach (var text in caption.Children.OfType<TextBlock>()) text.TextTrimming = TextTrimming.CharacterEllipsis;
                var box = new Border { Width = captionWidth, Height = captionHeight, Padding = new Thickness(5, 2), Background = Theme.Card, CornerRadius = new CornerRadius(6), BorderBrush = TopologyState(host.State), BorderThickness = new Thickness(1), Child = caption };
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
        var pen = new Pen(UiTheme.Border, 1);
        for (int x = 0; x < Bounds.Width; x += 48) context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        for (int y = 0; y < Bounds.Height; y += 48) context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }
}
