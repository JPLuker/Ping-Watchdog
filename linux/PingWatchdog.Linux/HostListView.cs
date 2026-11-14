// The List: search, filter chips, multi-select, sortable columns, right-click actions, F2 to edit,
// Delete to remove. Rows are keyed by site and address so live refreshes never move the selection (#28).
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PingWatchdog.Linux;

internal enum HostFilter { All, Priority, Offline, Disabled, Snoozed }

internal sealed record ListColumn(string Key, string Header, GridLength Width, bool Optional, bool RightAlign = false);

internal sealed class HostRow : INotifyPropertyChanged
{
    private HostSnapshot _host;
    public HostRow(HostSnapshot host) => _host = host;
    public event PropertyChangedEventHandler? PropertyChanged;
    public HostKey Key => _host.Key;
    public HostSnapshot Host => _host;
    public string Address => _host.Address;
    public string Name => _host.Label;
    public string Site => _host.Site;
    public string Group => _host.Group;
    public string Status => _host.StatusText;
    public IBrush StatusBrush => new SolidColorBrush(Tokens.Current.StateColor(_host));
    public string Latency => _host.Monitoring && _host.LatencyMs is { } ms && !_host.Disabled && !_host.Snoozed ? $"{ms} ms" : "—";
    public string Failures => _host.Monitoring ? _host.Failures.ToString() : "—";
    public string LastReply => _host.LastReply is { } reply ? DisplayTime.Timestamp(reply) : "—";
    public string Outage => _host.OutageStarted is { } since ? DisplayTime.Timestamp(since) : "—";

    public void Update(HostSnapshot host)
    {
        bool changed = host != _host;
        _host = host;
        if (changed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void Repaint() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
}

internal sealed class HostListView : DockPanel
{
    internal static readonly ListColumn[] AllColumns =
    {
        new("Address", "Address", new GridLength(170), false),
        new("Name", "Name", new GridLength(1.4, GridUnitType.Star), true),
        new("Site", "Site", new GridLength(1, GridUnitType.Star), false),
        new("Group", "Group", new GridLength(1, GridUnitType.Star), true),
        new("Status", "Status", new GridLength(150), false),
        new("Latency", "Latency", new GridLength(90), true, true),
        new("Failures", "Failures", new GridLength(80), true, true),
        new("LastReply", "Last reply", new GridLength(170), true),
        new("Outage", "Outage since", new GridLength(170), true),
    };

    private readonly ObservableCollection<HostRow> _rows = new();
    private readonly Dictionary<HostKey, HostRow> _byKey = new();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly Grid _header = new() { Height = 36 };
    private readonly TextBlock _empty = Ui.Text("", Tokens.Body, "Pw.Text2");
    private List<ListColumn> _columns = new();
    private IReadOnlyList<HostSnapshot> _all = Array.Empty<HostSnapshot>();
    private string _sortKey = "";
    private bool _sortDescending;
    private bool _allSites = true;
    private bool _refreshing;
    private HashSet<string> _hidden = new(StringComparer.Ordinal);

    public HostListView()
    {
        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<HostRow>((row, _) => BuildRow());
        Avalonia.Automation.AutomationProperties.SetName(_list, "Hosts");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        var headerBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _header, Padding = new Thickness(Tokens.Space3, 0) }
            .Res(Border.BorderBrushProperty, "Pw.Border");
        DockPanel.SetDock(headerBorder, Dock.Top);
        Children.Add(headerBorder);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Top;
        _empty.Margin = new Thickness(0, Tokens.Space6);
        _empty.IsVisible = false;
        var stack = new Panel();
        stack.Children.Add(_list);
        stack.Children.Add(_empty);
        Children.Add(stack);
        _list.SelectionChanged += (_, _) => { if (!_refreshing) SelectionChanged?.Invoke(); };
        _list.DoubleTapped += (_, e) =>
        {
            if (RowFrom(e.Source) is { } row) HostActivated?.Invoke(row.Host);
        };
        _list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            // Right-click selects the row under the pointer unless it's already part of the selection.
            if (!e.GetCurrentPoint(_list).Properties.IsRightButtonPressed) return;
            if (RowFrom(e.Source) is { } row && !_list.SelectedItems!.Contains(row))
            {
                _list.SelectedItems.Clear();
                _list.SelectedItems.Add(row);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            var point = e.TryGetPosition(_list, out var p) ? p : new Point(Tokens.Space4, Tokens.Space4);
            ContextRequested2?.Invoke(_list, point);
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && SelectedHosts.Count == 1) { e.Handled = true; HostActivated?.Invoke(SelectedHosts[0]); }
            else if (e.Key == Key.Delete && SelectedHosts.Count > 0) { e.Handled = true; DeleteRequested?.Invoke(); }
        };
        Tokens.Changed += () => { foreach (var row in _rows) row.Repaint(); };
    }

    public event Action? SelectionChanged;
    public event Action<HostSnapshot>? HostActivated;
    public event Action? DeleteRequested;
    // (anchor, point) for the host actions menu.
    public event Action<Control, Point>? ContextRequested2;

    public HostFilter Filter { get; private set; } = HostFilter.All;
    public string Search { get; private set; } = "";
    public int RowCount => _rows.Count;
    public IReadOnlyList<HostRow> Rows => _rows;
    public ListBox List => _list;
    public IReadOnlyList<string> VisibleColumns => _columns.Select(c => c.Key).ToList();
    public string EmptyText => _empty.IsVisible ? _empty.Text ?? "" : "";

    public IReadOnlyList<HostSnapshot> SelectedHosts =>
        _list.SelectedItems?.OfType<HostRow>().Select(r => r.Host).ToList() ?? new List<HostSnapshot>();

    public void SetHiddenColumns(IEnumerable<string> hidden)
    {
        _hidden = hidden.ToHashSet(StringComparer.Ordinal);
        Rebuild();
    }

    public void SetFilter(HostFilter filter) { Filter = filter; Apply(); }
    public void SetSearch(string search) { Search = search.Trim(); Apply(); }

    public void Select(HostKey key)
    {
        if (!_byKey.TryGetValue(key, out var row) || !_rows.Contains(row)) return;
        _list.SelectedItems!.Clear();
        _list.SelectedItems.Add(row);
        _list.ScrollIntoView(row);
    }

    public void SelectAll() => _list.SelectAll();

    public static int Count(IEnumerable<HostSnapshot> hosts, HostFilter filter) => hosts.Count(h => Matches(h, filter));

    private static bool Matches(HostSnapshot host, HostFilter filter) => filter switch
    {
        HostFilter.Priority => host.Options.Priority,
        HostFilter.Offline => host.Monitoring && !host.Disabled && !host.Snoozed && host.State == HostState.Offline,
        HostFilter.Disabled => host.Disabled,
        HostFilter.Snoozed => host.Snoozed && !host.Disabled,
        _ => true
    };

    public void Update(IReadOnlyList<HostSnapshot> hosts, bool allSites)
    {
        _all = hosts;
        if (_allSites != allSites || _columns.Count == 0)
        {
            _allSites = allSites;
            Rebuild();
            return;
        }
        Apply();
    }

    private void Rebuild()
    {
        // Site follows the navigation scope: shown only in All sites.
        _columns = AllColumns.Where(c => (c.Key != "Site" || _allSites) && !(c.Optional && _hidden.Contains(c.Key))).ToList();
        _header.ColumnDefinitions.Clear();
        _header.Children.Clear();
        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            _header.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
            string arrow = _sortKey == column.Key ? (_sortDescending ? " ↓" : " ↑") : "";
            var button = new Button
            {
                Content = Ui.Text(column.Header + arrow, Tokens.Label, "Pw.Text2"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 0, Tokens.Space2, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = column.RightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(0)
            };
            button.Classes.Add("ghost");
            Avalonia.Automation.AutomationProperties.SetName(button, "Sort by " + column.Header);
            string key = column.Key;
            button.Click += (_, _) =>
            {
                if (_sortKey == key) _sortDescending = !_sortDescending; else { _sortKey = key; _sortDescending = false; }
                Rebuild();
            };
            Grid.SetColumn(button, i);
            _header.Children.Add(button);
        }
        // Templates are rebuilt so rows pick up the new column set.
        var keep = SelectedHosts.Select(h => h.Key).ToHashSet();
        _rows.Clear();
        _byKey.Clear();
        _list.ItemTemplate = new FuncDataTemplate<HostRow>((row, _) => BuildRow());
        Apply();
        foreach (var row in _rows.Where(r => keep.Contains(r.Key))) _list.SelectedItems!.Add(row);
    }

    private void Apply()
    {
        _refreshing = true;
        try
        {
            IEnumerable<HostSnapshot> visible = _all.Where(h => Matches(h, Filter));
            if (Search.Length > 0)
                visible = visible.Where(h => new[] { h.Address, h.Label, h.Site, h.Group, h.StatusText }
                    .Any(v => v.Contains(Search, StringComparison.OrdinalIgnoreCase)));
            visible = Sort(visible);
            var list = visible.ToList();
            var keys = list.Select(h => h.Key).ToList();
            var wanted = keys.ToHashSet();
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (!wanted.Contains(_rows[i].Key)) { _byKey.Remove(_rows[i].Key); _rows.RemoveAt(i); }
            for (int i = 0; i < list.Count; i++)
            {
                var host = list[i];
                if (_byKey.TryGetValue(host.Key, out var row))
                {
                    row.Update(host);
                    int current = _rows.IndexOf(row);
                    if (current != i) _rows.Move(current, i);
                }
                else
                {
                    row = new HostRow(host);
                    _byKey[host.Key] = row;
                    _rows.Insert(i, row);
                }
            }
            _empty.Text = _all.Count == 0 ? "No hosts" : Search.Length > 0 && list.Count == 0 ? "No matching hosts" : list.Count > 0 ? "" : Filter switch
            {
                HostFilter.Priority => "No priority hosts",
                HostFilter.Offline => "No offline hosts",
                HostFilter.Disabled => "No disabled hosts",
                HostFilter.Snoozed => "No snoozed hosts",
                _ => "No hosts"
            };
            _empty.IsVisible = list.Count == 0;
        }
        finally { _refreshing = false; }
    }

    private IEnumerable<HostSnapshot> Sort(IEnumerable<HostSnapshot> hosts)
    {
        Func<HostSnapshot, IComparable> key = _sortKey switch
        {
            "Address" => h => AddressKey(h.Address),
            "Name" => h => h.Label,
            "Site" => h => h.Site,
            "Group" => h => h.Group,
            "Status" => h => h.StatusText,
            "Latency" => h => h.LatencyMs ?? long.MaxValue,
            "Failures" => h => h.Failures,
            "LastReply" => h => h.LastReply ?? DateTime.MinValue,
            "Outage" => h => h.OutageStarted ?? DateTime.MinValue,
            _ => h => 0
        };
        if (_sortKey.Length == 0) return hosts;
        return _sortDescending ? hosts.OrderByDescending(key).ThenBy(h => h.Site) : hosts.OrderBy(key).ThenBy(h => h.Site);
    }

    // IPv4 sorts numerically; hostnames after addresses, alphabetically.
    private static string AddressKey(string address) =>
        IPAddress.TryParse(address, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? "0" + string.Concat(ip.GetAddressBytes().Select(b => b.ToString("D3")))
            : "1" + address.ToLowerInvariant();

    private Control BuildRow()
    {
        var grid = new Grid { Height = 36, Margin = new Thickness(Tokens.Space3, 0) };
        foreach (var column in _columns) grid.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            Control cell;
            if (column.Key == "Status")
            {
                var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
                dot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new Binding(nameof(HostRow.StatusBrush)));
                var word = Ui.Text("");
                word.Bind(TextBlock.TextProperty, new Binding(nameof(HostRow.Status)));
                cell = Ui.Row(Tokens.Space2, dot, word);
            }
            else
            {
                var text = Ui.Text("", Tokens.Body, mono: column.Key is "Address");
                text.Bind(TextBlock.TextProperty, new Binding(column.Key));
                text.Bind(ToolTip.TipProperty, new Binding(column.Key));
                if (column.RightAlign) text.HorizontalAlignment = HorizontalAlignment.Right;
                cell = text;
            }
            cell.Margin = new Thickness(0, 0, column.RightAlign ? Tokens.Space2 : Tokens.Space3, 0);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent }
            .Res(Border.BorderBrushProperty, "Pw.Border");
    }

    private static HostRow? RowFrom(object? source) =>
        source is Control control
            ? new[] { control }.Concat(control.GetVisualAncestors().OfType<Control>()).Select(c => c.DataContext).OfType<HostRow>().FirstOrDefault()
            : null;
}
