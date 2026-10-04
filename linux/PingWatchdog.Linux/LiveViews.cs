using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WatchdogTheme = PingWatchdog.Linux.Theme;

namespace PingWatchdog.Linux;

internal sealed class HostRow : INotifyPropertyChanged
{
    public HostRow(HostSnapshot host) => Host = host;
    public HostSnapshot Host { get; private set; }
    public string Site => Host.Site;
    public string HostAddress => Host.Address;
    public string Label => Host.Label;
    public string Status => Host.State.ToString().ToUpperInvariant();
    public string WallboardStatus => Host.State.ToString();
    public string WallboardLatency => Host.LatencyMs?.ToString() ?? "—";
    public string Latency => Host.LatencyMs is null ? "—" : $"{Host.LatencyMs} ms";
    public string Failures => Host.Failures.ToString();
    public string LastReply => Host.LastReply?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    public string Outage => Host.OutageStarted?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    public string Key => Host.Site + "\u001f" + Host.Address;
    public string Text => $"{Host.State,-8}  {Host.Site,-18}  {Host.Address,-24}  {Host.Label,-20}  {(Host.LatencyMs is null ? "—" : $"{Host.LatencyMs} ms"),-9}  failures {Host.Failures}";
    public string WallboardText => $"{Host.State} · {Host.Address} · {(Host.LatencyMs is null ? "—" : $"{Host.LatencyMs} ms")}\n{Host.Site}{(string.IsNullOrWhiteSpace(Host.Label) ? "" : $" · {Host.Label}")} · {Host.Failures} failure(s)";
    public IBrush Foreground => Theme.State(Host.State);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(HostSnapshot host)
    {
        if (Host == host) return;
        Host = host;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WallboardText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
    }
}

internal sealed class SiteRow : INotifyPropertyChanged
{
    public SiteRow(string? key, string text) { Key = key; Text = text; }
    public string? Key { get; }
    public string Text { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(string text)
    {
        if (Text == text) return;
        Text = text;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

internal static class LiveRows
{
    public static void Hosts(ObservableCollection<HostRow> rows, IReadOnlyList<HostSnapshot> hosts)
    {
        var desired = hosts.Select(h => h.Site + "\u001f" + h.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = rows.Count - 1; i >= 0; i--)
            if (!desired.Contains(rows[i].Key)) rows.RemoveAt(i);
        for (int i = 0; i < hosts.Count; i++)
        {
            string key = hosts[i].Site + "\u001f" + hosts[i].Address;
            var existing = rows.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is null) rows.Insert(i, new HostRow(hosts[i]));
            else
            {
                int index = rows.IndexOf(existing);
                if (index != i) rows.Move(index, i);
                existing.Update(hosts[i]);
            }
        }
    }

    public static void Sites(ObservableCollection<SiteRow> rows, IReadOnlyList<SiteRow> desired)
    {
        for (int i = rows.Count - 1; i >= 0; i--)
            if (!desired.Any(r => string.Equals(r.Key, rows[i].Key, StringComparison.OrdinalIgnoreCase))) rows.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            var existing = rows.FirstOrDefault(r => string.Equals(r.Key, desired[i].Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null) rows.Insert(i, desired[i]);
            else
            {
                int index = rows.IndexOf(existing);
                if (index != i) rows.Move(index, i);
                existing.Update(desired[i].Text);
            }
        }
    }

    public static TextBlock BoundText(string property) => new()
    {
        FontFamily = new FontFamily("monospace"),
        Foreground = Theme.Text,
        [!TextBlock.TextProperty] = new Binding(property)
    };
}

internal sealed class CliTraceView : Border
{
    private readonly ObservableCollection<CommandLogEntry> _rows = new();
    private readonly ListBox _list = new() { Background = WatchdogTheme.Brush("#070C12"), MinHeight = 85, MaxHeight = 170 };
    private readonly CheckBox _follow = new() { Content = "Follow live", IsChecked = true };
    private readonly TextBlock _count = WatchdogTheme.Label("0 entries", 10, color: WatchdogTheme.Muted);
    public CliTraceView(Action clear)
    {
        Background = WatchdogTheme.Brush("#070C12");
        BorderBrush = WatchdogTheme.Border;
        BorderThickness = new Thickness(1);
        MinHeight = 118;
        MaxHeight = 210;
        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<CommandLogEntry>((entry, _) => new TextBlock
        {
            // Avalonia clears content to null while recycling a virtualized container.
            Text = entry?.Text ?? string.Empty, FontFamily = new FontFamily("monospace"), FontSize = 11,
            Foreground = entry is null ? WatchdogTheme.Muted : entry.Success ? WatchdogTheme.Green : WatchdogTheme.Red,
            Margin = new Thickness(0, 1), TextWrapping = TextWrapping.NoWrap
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var clearButton = WatchdogTheme.Button("Clear");
        clearButton.Click += (_, _) => clear();
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(8, 2),
            Children = { WatchdogTheme.Label("CLI TRACE", 10, FontWeight.Bold, WatchdogTheme.Muted), _count, _follow, clearButton }
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, _list } };
        Grid.SetRow(_list, 1);
        Child = grid;
    }

    public void Refresh(IReadOnlyList<CommandLogEntry> entries)
    {
        // Remove expired rows and append only new records; do not replace the list on every ping.
        int overlap = 0;
        if (_rows.Count > 0 && entries.Count > 0)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != entries[0]) continue;
                int count = Math.Min(_rows.Count - i, entries.Count);
                if (Enumerable.Range(0, count).All(j => _rows[i + j] == entries[j]))
                {
                    for (int removed = 0; removed < i; removed++) _rows.RemoveAt(0);
                    overlap = count;
                    break;
                }
            }
        }
        if (overlap == 0 && _rows.Count > 0) _rows.Clear();
        while (_rows.Count > overlap) _rows.RemoveAt(_rows.Count - 1);
        for (int i = overlap; i < entries.Count; i++) _rows.Add(entries[i]);
        _count.Text = $"{_rows.Count} entries • equivalent Linux commands";
        if (_follow.IsChecked == true && entries.Count > overlap && _rows.Count > 0)
        {
            var last = _rows[^1];
            Dispatcher.UIThread.Post(() => { if (_follow.IsChecked == true && _rows.Contains(last)) _list.ScrollIntoView(last); });
        }
    }
}
