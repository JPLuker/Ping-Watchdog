using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PingWatchdog.Shared;
using UiTheme = PingWatchdog.Linux.Theme;

namespace PingWatchdog.Linux;

internal static class NativeControls
{
    public static NumericUpDown Number(decimal min, decimal max, decimal value, decimal increment, double width)
    {
        var number = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = increment,
            Width = width, Height = 24, MinHeight = 24, FontSize = 12.7, Background = UiTheme.Brush(Presentation.Input), Foreground = UiTheme.Text };
        number.Template = new FuncControlTemplate<NumericUpDown>((owner, scope) =>
        {
            var text = new TextBox { Name = "PART_TextBox", MinHeight = 0, Padding = new Thickness(4, 0),
                FontSize = 12.7, Background = UiTheme.Brush(Presentation.Input), Foreground = UiTheme.Text,
                BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
            text.Bind(TextBox.TextProperty, new Binding(nameof(NumericUpDown.Text)) { Source = owner, Mode = BindingMode.TwoWay });
            var spinner = new ButtonSpinner { Name = "PART_Spinner", Content = text };
            scope.Register(text.Name, text); scope.Register(spinner.Name, spinner);
            spinner.Template = new FuncControlTemplate<ButtonSpinner>((spin, names) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16"), RowDefinitions = new RowDefinitions("*,*") };
                var presenter = new ContentPresenter(); presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ButtonSpinner.Content)) { Source = spin });
                Grid.SetRowSpan(presenter, 2); grid.Children.Add(presenter);
                foreach (var (name, glyph, row) in new[] { ("PART_IncreaseButton", "▴", 0), ("PART_DecreaseButton", "▾", 1) })
                {
                    var button = new RepeatButton { Name = name, Content = glyph, FontSize = 9, MinHeight = 0, MinWidth = 0,
                        Padding = new Thickness(0), Background = UiTheme.Brush("#19222D"), Foreground = UiTheme.Text, Focusable = false };
                    names.Register(name, button); Grid.SetRow(button, row); Grid.SetColumn(button, 1); grid.Children.Add(button);
                }
                return grid;
            });
            return new Border { BorderThickness = new Thickness(1), BorderBrush = UiTheme.Border, Child = spinner };
        });
        return number;
    }
}

internal sealed class HostTableView : Grid
{
    private readonly ListBox _list;
    private readonly ScrollViewer _header = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid _head = new();
    private readonly List<Grid> _realized = new();
    private readonly bool _compact;
    private bool _showSite = true;
    public HostTableView(ListBox list, bool compact = false)
    {
        _list = list; _compact = compact; _list.Height = double.NaN;
        RowDefinitions = new RowDefinitions($"{(_compact ? Presentation.CompactTableHeaderHeight : Presentation.TableHeaderHeight)},*");
        Background = UiTheme.Brush(Presentation.Card);
        _list.Background = UiTheme.Brush(Presentation.Card); _list.BorderThickness = new Thickness(0); _list.Padding = new Thickness(0);
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = {
            new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, (double)(_compact ? Presentation.CompactTableRowHeight : Presentation.TableRowHeight)),
            new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)
        } });
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _list.ItemTemplate = new FuncDataTemplate<HostRow>((host, _) =>
        {
            var row = BuildGrid(false); row.DataContext = host;
            _realized.Add(row); row.AttachedToVisualTree += (_, _) => { if (!_realized.Contains(row)) _realized.Add(row); ApplyWidths(row); }; row.DetachedFromVisualTree += (_, _) => _realized.Remove(row);
            return new Border { BorderBrush = UiTheme.Brush("#1F2832"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        });
        _header.Content = _head; _header.Background = UiTheme.Brush("#161F2A"); Children.Add(_header);
        Grid.SetRow(_list, 1); Children.Add(_list);
        SizeChanged += (_, _) => UpdateWidths();
        _list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            if (e.Source is ScrollViewer scroll) _header.Offset = new Vector(scroll.Offset.X, 0);
        });
        SetScope(true);
    }
    private TableColumn[] Columns => (_compact ? Presentation.WallboardHostColumns : Presentation.HostColumns.Where(c => _showSite || c.Key != "Site")).ToArray();
    public void SetScope(bool showSite)
    {
        if (_showSite == showSite && _head.Children.Count > 0) return;
        _showSite = showSite; _head.Children.Clear(); _head.ColumnDefinitions.Clear();
        int index = 0;
        foreach (var column in Columns)
        {
            var text = UiTheme.Label(column.Header, _compact ? 12 : 12.7, FontWeight.Normal, UiTheme.Brush("#B7CDDC"));
            text.Margin = new Thickness(_compact ? 4 : 10, 0); text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, index++); _head.Children.Add(text);
        }
        // Item templates depend on the selected scope; retain the collection and selected item.
        foreach (var row in _realized.ToArray()) RebuildRow(row);
        UpdateWidths();
    }
    private Grid BuildGrid(bool header)
    {
        var grid = new Grid { Height = _compact ? Presentation.CompactTableRowHeight : Presentation.TableRowHeight };
        RebuildRow(grid);
        return grid;
    }
    private void RebuildRow(Grid row)
    {
        row.Children.Clear(); int index = 0;
        foreach (var column in Columns)
        {
            var text = new TextBlock { FontSize = _compact ? 12 : 12.7, Margin = new Thickness(_compact ? 4 : 10, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };
            text.Bind(TextBlock.TextProperty, new Binding(column.Key == "Host" ? nameof(HostRow.HostAddress) : _compact && column.Key == "Status" ? nameof(HostRow.WallboardStatus) : _compact && column.Key == "Latency" ? nameof(HostRow.WallboardLatency) : column.Key));
            text.Bind(TextBlock.ForegroundProperty, new Binding(nameof(HostRow.Foreground)));
            text.Bind(ToolTip.TipProperty, new Binding(column.Key == "Host" ? nameof(HostRow.HostAddress) : _compact && column.Key == "Status" ? nameof(HostRow.WallboardStatus) : _compact && column.Key == "Latency" ? nameof(HostRow.WallboardLatency) : column.Key));
            Grid.SetColumn(text, index++); row.Children.Add(text);
        }
        ApplyWidths(row);
    }
    private void UpdateWidths()
    {
        ApplyWidths(_head);
        foreach (var row in _realized.ToArray()) ApplyWidths(row);
    }
    private void ApplyWidths(Grid grid)
    {
        var columns = Columns;
        double minimum = columns.Sum(c => c.Width);
        double width = Math.Max(minimum, Bounds.Width - 16);
        double extra = width - minimum, weight = columns.Sum(c => c.Weight);
        grid.Width = width; grid.ColumnDefinitions.Clear();
        foreach (var column in columns)
        {
            double baseline = column.Width;
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(baseline + (weight > 0 ? extra * column.Weight / weight : 0))));
        }
    }
}

internal sealed record HistoryRow(StateEventRecord Event)
{
    public string Timestamp => Event.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public string Kind => Event.Kind;
    public string Site => Event.Site;
    public string Host => Event.DisplayHost;
    public string Details => Event.Message;
    public IBrush Foreground => Kind == "DOWN" ? UiTheme.Red : Kind == "SUSPECT" ? UiTheme.Yellow : UiTheme.Green;
}
internal sealed class HistoryTableView : Grid
{
    private readonly List<Grid> _rows = new();
    private void Fit(Grid row)
    {
        double width = Math.Max(620, Bounds.Width - 16), fill = width - Presentation.HistoryColumns.Where(c => c.Weight == 0).Sum(c => c.Width);
        row.Width = width; row.ColumnDefinitions.Clear();
        foreach (var column in Presentation.HistoryColumns) row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(column.Weight > 0 ? fill * column.Weight / 100 : column.Width)));
    }
    public HistoryTableView(ListBox list)
    {
        RowDefinitions = new RowDefinitions($"{Presentation.TableHeaderHeight},*");
        var head = new Grid();
        _rows.Add(head); Fit(head);
        SizeChanged += (_, _) => { foreach (var row in _rows.ToArray()) Fit(row); };
        int index = 0;
        foreach (var column in Presentation.HistoryColumns) {
            var text = UiTheme.Label(column.Header, 12.7, color: UiTheme.Brush("#B7CDDC")); text.Margin = new Thickness(10, 0); text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, index++); head.Children.Add(text);
        }
        var header = new ScrollViewer { Content = head, Background = UiTheme.Brush("#161F2A"), HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(header); Grid.SetRow(list, 1); Children.Add(list);
        list.Padding = new Thickness(0); list.Background = UiTheme.Card; list.BorderThickness = new Thickness(0);
        list.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, 34d) } });
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        list.ItemTemplate = new FuncDataTemplate<HistoryRow>((_, _) => {
            var row = new Grid { Height = Presentation.TableRowHeight };
            int i = 0;
            foreach (var column in Presentation.HistoryColumns) {

                var text = new TextBlock { FontSize = 12.7, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                text.Bind(TextBlock.TextProperty, new Binding(column.Key)); text.Bind(ToolTip.TipProperty, new Binding(column.Key));
                text.Bind(TextBlock.ForegroundProperty, new Binding(nameof(HistoryRow.Foreground)));
                Grid.SetColumn(text, i++); row.Children.Add(text);
            }
            Fit(row); row.AttachedToVisualTree += (_, _) => { if (!_rows.Contains(row)) _rows.Add(row); Fit(row); };
            row.DetachedFromVisualTree += (_, _) => _rows.Remove(row);
            return new Border { BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        });
        list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => { if (e.Source is ScrollViewer scroll) header.Offset = new Vector(scroll.Offset.X, 0); });
    }
}
