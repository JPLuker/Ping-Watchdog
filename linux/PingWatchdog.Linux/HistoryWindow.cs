// Outage history: range, site and suspect filters over every stored event, with CSV export.
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal sealed class HistoryWindow : Window
{
    private static readonly (string Name, int Hours)[] Ranges = { ("Last 24 hours", 24), ("Last 7 days", 168), ("Last 30 days", 720), ("All time", 0) };
    private readonly WatchdogEngine _engine;
    private readonly ComboBox _range = new() { Width = 170, ItemsSource = Ranges.Select(r => r.Name).ToList() };
    private readonly ComboBox _site = new() { Width = 200 };
    private readonly CheckBox _hideSuspects = new() { Content = "Hide suspect events" };
    private readonly TextBlock _count = Ui.Text("", Tokens.Body, "Pw.Text2");
    private readonly ObservableCollection<StateEventRecord> _rows = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _empty = Ui.Text("No outages in this range", Tokens.Body, "Pw.Text2");
    private readonly TextBlock _versionOverlay = VersionOverlay.Create();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading;
    private bool _dirty;
    private int _lastStored = -1;

    public HistoryWindow(WatchdogEngine engine)
    {
        _engine = engine;
        Title = "Outage history • Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 1040;
        Height = 650;
        MinWidth = 780;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var config = engine.Snapshot().Settings;
        _loading = true;
        _range.SelectedIndex = Math.Max(0, Array.FindIndex(Ranges, r => r.Hours == config.EventHistoryHours));
        _hideSuspects.IsChecked = config.HideSuspectEvents;
        RefreshSites();
        _site.SelectedIndex = 0;
        _loading = false;
        Avalonia.Automation.AutomationProperties.SetName(_range, "Range");
        Avalonia.Automation.AutomationProperties.SetName(_site, "Site");

        var export = Ui.Button("Export CSV");
        export.Click += async (_, _) => await ExportAsync();
        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { Ui.Text("Range", Tokens.Body, "Pw.Text2"), _range, Ui.Text("Site", Tokens.Body, "Pw.Text2"), _site, _hideSuspects, export })
        {
            control.Margin = new Thickness(0, 0, control is TextBlock ? Tokens.Space2 : Tokens.Space5, Tokens.Space2);
            control.VerticalAlignment = VerticalAlignment.Center;
            filters.Children.Add(control);
        }
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, Tokens.Space3) };
        title.Children.Add(Ui.Title("Outage history"));
        Grid.SetColumn(_count, 1);
        title.Children.Add(_count);

        var header = Columns();
        string[] titles = { "Timestamp", "Event", "Site", "Host", "Details" };
        for (int i = 0; i < titles.Length; i++)
        {
            var text = Ui.Text(titles[i], Tokens.Label, "Pw.Text2");
            Grid.SetColumn(text, i);
            header.Children.Add(text);
        }
        header.Margin = new Thickness(Tokens.Space3, 0);
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Pw.Border");
        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<StateEventRecord>((record, _) => Row(record));
        Avalonia.Automation.AutomationProperties.SetName(_list, "Outage history");
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Top;
        _empty.Margin = new Thickness(0, Tokens.Space6);
        var listPanel = new Panel();
        listPanel.Children.Add(_list);
        listPanel.Children.Add(_empty);
        var table = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        table.Children.Add(headerBorder);
        table.Children.Add(listPanel);
        var card = Ui.Card(table, 0);
        card.Padding = new Thickness(0, Tokens.Space1, 0, 0);
        card.Margin = new Thickness(0, Tokens.Space3, 0, 0);

        var layout = new DockPanel { Margin = new Thickness(Tokens.Space6, Tokens.Space5) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(filters, Dock.Top);
        layout.Children.Add(title);
        layout.Children.Add(filters);
        layout.Children.Add(card);
        var root = new Panel();
        root.Children.Add(layout);
        root.Children.Add(_versionOverlay);
        Content = root;

        _range.SelectionChanged += (_, _) => Refresh();
        _site.SelectionChanged += (_, _) => Refresh();
        _hideSuspects.IsCheckedChanged += (_, _) => Refresh();
        _engine.Changed += EngineChanged;
        _refresh.Tick += (_, _) => { if (_dirty) { _dirty = false; RefreshSites(); Refresh(); } };
        Opened += (_, _) => { ApplyOverlay(); Refresh(); _refresh.Start(); _range.Focus(); };
        Closed += (_, _) => { _refresh.Stop(); _engine.Changed -= EngineChanged; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    internal int RowCount => _rows.Count;
    internal string CountText => _count.Text ?? "";
    internal ComboBox RangeBox => _range;
    internal ComboBox SiteBox => _site;
    internal CheckBox HideSuspectsBox => _hideSuspects;

    internal void ApplyOverlay() => _versionOverlay.IsVisible = _engine.Preferences.ShowVersionOverlay;

    private void EngineChanged(object? sender, EventArgs e) => _dirty = true;

    private static Grid Columns() => new() { ColumnDefinitions = new ColumnDefinitions("180,110,170,1.2*,1.6*"), Height = 36 };

    private Control Row(StateEventRecord? record)
    {
        var grid = Columns();
        grid.Margin = new Thickness(Tokens.Space3, 0);
        if (record is null) return grid;
        var p = Tokens.Current;
        string kind = record.Kind.ToUpperInvariant() switch { "DOWN" => "Down", "RECOVERED" => "Recovered", "SUSPECT" => "Suspect", _ => record.Kind };
        var color = record.Kind.ToUpperInvariant() switch { "DOWN" => "Pw.Danger", "RECOVERED" => "Pw.Success", "SUSPECT" => "Pw.Warning", _ => "Pw.Text" };
        var cells = new Control[]
        {
            Ui.Text(DisplayTime.Timestamp(record.Timestamp), Tokens.Body, mono: true),
            Ui.Text(kind, Tokens.Body, color, FontWeight.SemiBold),
            Ui.Text(record.Site),
            Ui.Text(record.DisplayHost),
            Ui.Text(record.Message, Tokens.Body, "Pw.Text2"),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] is TextBlock text) ToolTip.SetTip(text, text.Text);
            cells[i].Margin = new Thickness(0, 0, Tokens.Space3, 0);
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
        return new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Pw.Border");
    }

    private void RefreshSites()
    {
        var sites = new[] { "All sites" }.Concat(_engine.HistorySites()).ToList();
        string? selected = _site.SelectedItem as string;
        bool wasLoading = _loading;
        _loading = true;
        _site.ItemsSource = sites;
        _site.SelectedItem = selected is not null && sites.Contains(selected) ? selected : sites[0];
        _loading = wasLoading;
    }

    private (int Hours, bool HideSuspects, string? Site) Filters() =>
        (Ranges[Math.Max(0, _range.SelectedIndex)].Hours, _hideSuspects.IsChecked == true, _site.SelectedItem as string is { } s && s != "All sites" ? s : null);

    internal void Refresh()
    {
        if (_loading) return;
        var (hours, hide, site) = Filters();
        var history = _engine.HistorySnapshot(hours, hide, site);
        if (history.StoredCount == _lastStored && history.Events.Count == _rows.Count && _rows.Count > 0 && history.Events[0] == _rows[0]) return;
        _lastStored = history.StoredCount;
        _rows.Clear();
        // The list virtualizes; even a long history stays responsive.
        foreach (var record in history.Events) _rows.Add(record);
        _count.Text = $"{history.Events.Count:N0} shown • {history.StoredCount:N0} stored";
        _empty.IsVisible = _rows.Count == 0;
    }

    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
            SuggestedFileName = $"PingWatchdog-outages-{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        if (file is null) return;
        var (hours, hide, site) = Filters();
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await HistoryCsv.WriteAsync(writer, _engine.HistorySnapshot(hours, hide, site).Events);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, "Export CSV", "Couldn't save the file: " + ex.Message);
        }
    }
}
