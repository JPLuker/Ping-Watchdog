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
        _list = list; _compact = compact;
        RowDefinitions = new RowDefinitions($"{Presentation.TableHeaderHeight},*");
        Background = UiTheme.Brush(Presentation.Card);
        _list.Background = UiTheme.Brush(Presentation.Card); _list.BorderThickness = new Thickness(0); _list.Padding = new Thickness(0);
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = {
            new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, (double)Presentation.TableRowHeight),
            new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)
        } });
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _list.ItemTemplate = new FuncDataTemplate<HostRow>((host, _) =>
        {
            var row = BuildGrid(false); row.DataContext = host;
            _realized.Add(row); row.DetachedFromVisualTree += (_, _) => _realized.Remove(row);
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
    private TableColumn[] Columns => (_compact ? Presentation.HostColumns.Where(c => c.Key is "Host" or "Label" or "Status") : Presentation.HostColumns.Where(c => _showSite || c.Key != "Site")).ToArray();
    public void SetScope(bool showSite)
    {
        if (_showSite == showSite && _head.Children.Count > 0) return;
        _showSite = showSite; _head.Children.Clear(); _head.ColumnDefinitions.Clear();
        int index = 0;
        foreach (var column in Columns)
        {
            var text = UiTheme.Label(column.Header, 12.7, FontWeight.Normal, UiTheme.Brush("#B7CDDC"));
            text.Margin = new Thickness(10, 0); text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, index++); _head.Children.Add(text);
        }
        // Item templates depend on the selected scope; retain the collection and selected item.
        foreach (var row in _realized.ToArray()) RebuildRow(row);
        UpdateWidths();
    }
    private Grid BuildGrid(bool header)
    {
        var grid = new Grid { Height = Presentation.TableRowHeight };
        RebuildRow(grid);
        return grid;
    }
    private void RebuildRow(Grid row)
    {
        row.Children.Clear(); int index = 0;
        foreach (var column in Columns)
        {
            var text = new TextBlock { FontSize = 12.7, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };
            text.Bind(TextBlock.TextProperty, new Binding(column.Key == "Host" ? nameof(HostRow.HostAddress) : column.Key));
            text.Bind(TextBlock.ForegroundProperty, new Binding(nameof(HostRow.Foreground)));
            text.Bind(ToolTip.TipProperty, new Binding(column.Key == "Host" ? nameof(HostRow.HostAddress) : column.Key));
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
        double minimum = columns.Sum(c => _compact ? c.Key == "Status" ? 75 : 135 : c.Width);
        double width = Math.Max(minimum, Bounds.Width - 16);
        double extra = width - minimum, weight = columns.Sum(c => c.Weight);
        grid.Width = width; grid.ColumnDefinitions.Clear();
        foreach (var column in columns)
        {
            double baseline = _compact ? column.Key == "Status" ? 75 : 135 : column.Width;
            grid.ColumnDefinitions.Add(new ColumnDefinition(baseline + (weight > 0 ? extra * column.Weight / weight : 0)));
        }
    }
}
