using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PingWatchdog.Shared;
using Avalonia.Styling;

namespace PingWatchdog.Linux;

internal sealed partial class MainWindow
{
    private readonly Button _stop = Theme.Button("Stop");
    private readonly TextBlock _siteHeader = Theme.Label("Selected site hosts", 13.7, FontWeight.SemiBold);
    private readonly ColumnDefinition _navigationColumn = new(new GridLength(Presentation.SidebarWidth));
    private HostTableView _hostTable = null!;

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions($"{Presentation.HeaderHeight},*,{Presentation.FooterHeight}") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Theme.Brush(Presentation.Header), Margin = new Thickness(0), };
        header.Children.Add(new StackPanel {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 10, 0, 8), VerticalAlignment = VerticalAlignment.Center,
            Children = { new Image { Source = BrandAssets.Logo, Width = 42, Height = 42, Stretch = Stretch.Uniform },
                new StackPanel { Children = { Theme.Label("PING WATCHDOG", 25.3, FontWeight.SemiBold), Theme.Label("Availability monitor", 12.3, color: Theme.Muted) } } }
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var wallboard = Theme.Button("Wallboard", true); wallboard.Click += (_, _) => OpenWallboard();
        var settings = Theme.Button("Settings"); settings.Click += (_, _) => OpenSettings();
        var more = Theme.Button("⋯"); more.Width = 48; more.FontSize = 20;
        var settingsMenu = new MenuItem { Header = "Settings..." }; settingsMenu.Click += (_, _) => OpenSettings();
        var historyMenu = new MenuItem { Header = "Outage history..." }; historyMenu.Click += (_, _) => OpenHistory();
        var export = new MenuItem { Header = "Export configuration" }; export.Click += async (_, _) => await ExportConfigAsync();
        var import = new MenuItem { Header = "Import configuration" }; import.Click += async (_, _) => await ImportConfigAsync();
        var about = new MenuItem { Header = "About Ping Watchdog" }; about.Click += async (_, _) => await AlertAsync($"Ping Watchdog {_updates.Version}\n\nMulti-site ICMP availability monitoring.\n\nCopyright © 2026 Joseph Luker. All rights reserved.");
        var menu = new ContextMenu { ItemsSource = new Control[] { settingsMenu, historyMenu, new Separator(), export, import, new Separator(), about } };
        more.ContextMenu = menu; more.Click += (_, _) => menu.Open(more);
        actions.Children.Add(_updateButton); actions.Children.Add(wallboard); actions.Children.Add(settings); actions.Children.Add(more);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); root.Children.Add(header);
        var workspace = new Grid { ColumnDefinitions = new ColumnDefinitions { _navigationColumn, new(GridLength.Star) } };
        Grid.SetRow(workspace, 1);
        var nav = new Grid { RowDefinitions = new RowDefinitions("38,*,38,38,38,38"), Background = Theme.Brush(Presentation.Navigation) };
        var navContent = new Grid { RowDefinitions = new RowDefinitions("38,*,38,38,38,38"), Margin = new Thickness(12, 14, 10, 14) };
        navContent.Children.Add(Theme.Label("Sites", 14, FontWeight.SemiBold));
        _siteList.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = {
            new Setter(ListBoxItem.BackgroundProperty, Theme.Brush("#121820")), new Setter(ListBoxItem.MinHeightProperty, 36d),
            new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(1)), new Setter(ListBoxItem.BorderBrushProperty, Avalonia.Media.Brushes.Transparent),
            new Setter(ListBoxItem.MarginProperty, new Thickness(0, 2))
        } });
        _siteList.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":selected")) { Setters = {
            new Setter(ListBoxItem.BackgroundProperty, Theme.Brush("#1A485B")), new Setter(ListBoxItem.BorderBrushProperty, Theme.Cyan)
        } });
        _siteList.Background = Theme.Brush(Presentation.Navigation); _siteList.BorderThickness = new Thickness(0); _siteList.FontSize = 13.3;
        Grid.SetRow(_siteList, 1); navContent.Children.Add(_siteList);
        var add = Theme.Button("+ Add Site", true); add.Click += async (_, _) => await AddSiteAsync();
        var organize = Theme.Button("Organize"); organize.Click += (_, _) => OpenOrganization();
        var rename = Theme.Button("Rename Site"); rename.Click += async (_, _) => await RenameSiteAsync();
        var delete = Theme.Button("Delete Site"); delete.Background = Theme.Brush("#391C22"); delete.Click += async (_, _) => await DeleteSiteAsync();
        int navRow = 2;
        foreach (var b in new[] { add, organize, rename, delete }) { b.Margin = new Thickness(0, 3); b.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetRow(b, navRow++); navContent.Children.Add(b); }
        Grid.SetRowSpan(navContent, 6); nav.Children.Add(navContent); workspace.Children.Add(nav);
        var right = new Grid { RowDefinitions = new RowDefinitions("76,Auto,Auto,*"), Margin = new Thickness(16, 14, 16, 14) };
        Grid.SetColumn(right, 1);
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        stats.Children.Add(StatCard("Total hosts", _total, 0)); stats.Children.Add(StatCard("Online", _online, 1)); stats.Children.Add(StatCard("Suspect", _suspect, 2)); stats.Children.Add(StatCard("Offline", _offline, 3)); right.Children.Add(stats);
        var editor = new Grid { RowDefinitions = new RowDefinitions("30,Auto") };
        var editorHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        editorHead.Children.Add(_siteHeader); editorHead.Children.Add(Right(Theme.Label("Saved automatically • Applies live", 11.3, color: Theme.Muted)));
        editor.Children.Add(editorHead);
        _hostEditor.Height = 60; _hostEditor.MinHeight = 60; _hostEditor.FontSize = 12.7;
        _hostEditor.PlaceholderText = "One IP or hostname per line\n192.168.1.1\n8.8.8.8\nserver01";
        _hostEditor.Background = Theme.Brush(Presentation.Input); _hostEditor.BorderBrush = Theme.Border;
        Grid.SetRow(_hostEditor, 1); editor.Children.Add(_hostEditor);
        var editorBorder = Theme.CardBorder(editor, new Thickness(0, 0, 0, 10)); editorBorder.BorderThickness = new Thickness(0); editorBorder.Padding = new Thickness(14, 10, 14, 12);
        Grid.SetRow(editorBorder, 1); right.Children.Add(editorBorder);
        var timing = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var inputs = new WrapPanel();
        foreach (var c in new[] { Setting("Interval", _interval, "sec"), Setting("Timeout", _timeout, "ms"), Setting("Down after", _downAfter, "fails"), Setting("Recover after", _recoverAfter, "successes"), _showCli }) { c.Margin = new Thickness(0, 0, 16, 0); inputs.Children.Add(c); }
        timing.Children.Add(inputs);
        _startStop.Content = "Start Monitoring"; _startStop.Background = Theme.Brush("#1F7852"); _stop.Background = Theme.Brush("#5D262D");
        _stop.Click += (_, _) => { if (_engine.Monitoring) _engine.StopMonitoring(); };
        var monitoring = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 9, 0, 0), Children = { _startStop, _stop } };
        Grid.SetColumn(monitoring, 1); timing.Children.Add(monitoring);
        var timingBorder = Theme.CardBorder(timing, new Thickness(0, 0, 0, 10)); timingBorder.BorderThickness = new Thickness(0); timingBorder.Padding = new Thickness(14, 10, 12, 10);
        Grid.SetRow(timingBorder, 2); right.Children.Add(timingBorder);
        var live = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        _hostTable = new HostTableView(_hostList); live.Children.Add(_hostTable);
        _commandBox.Height = 160; _commandBox.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_commandBox, 1); live.Children.Add(_commandBox);
        Grid.SetRow(live, 3); right.Children.Add(live);
        workspace.Children.Add(right); root.Children.Add(workspace);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Theme.Brush("#0B1017") };
        _status.Margin = new Thickness(4, 4); footer.Children.Add(_status);
        var copyright = Theme.Label($"{_updates.Version} · © 2026 Joseph Luker · All rights reserved.", 11, color: Theme.Muted);
        copyright.Margin = new Thickness(0, 4, 12, 4); Grid.SetColumn(copyright, 1); footer.Children.Add(copyright);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        SizeChanged += (_, _) => { _navigationColumn.Width = new GridLength(Presentation.Sidebar(Bounds.Width)); root.RowDefinitions[0].Height = new GridLength(Presentation.Header(Bounds.Width)); };
        return root;
    }
}
