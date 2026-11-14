// Add hosts: paste addresses, spreadsheet rows, CIDR blocks or ranges, preview them, then save them to
// a site (Quick Add) or watch them for this session only (Quick Monitor).
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

// One preview row. Address, label and group can be edited in place, as in the Windows preview grid.
internal sealed class PreviewRow : INotifyPropertyChanged
{
    private bool _use;
    private string _address = "", _label = "", _group = "", _site = "", _status = "", _statusKey = "";
    private bool _valid;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
    public string Address { get => _address; set => Set(ref _address, value ?? "", nameof(Address)); }
    public string Label { get => _label; set => Set(ref _label, value ?? "", nameof(Label)); }
    public string Group { get => _group; set => Set(ref _group, value ?? "", nameof(Group)); }
    public string Site { get => _site; set => Set(ref _site, value, nameof(Site)); }
    public string Status { get => _status; set => Set(ref _status, value, nameof(Status)); }
    public string StatusKey { get => _statusKey; set => Set(ref _statusKey, value, nameof(StatusKey)); }
    public bool Valid { get => _valid; set => Set(ref _valid, value, nameof(Valid)); }
    public bool Use { get => _use; set => Set(ref _use, value && Valid, nameof(Use)); }
    // Status colors come from the palette; a light/dark switch repaints them.
    public void Repaint() => PropertyChanged?.Invoke(this, new(nameof(StatusKey)));
}

internal sealed class AddHostsDialog : DialogWindow
{
    private readonly WatchdogEngine _engine;
    private readonly bool _temporary;
    private readonly TextBox _input = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        Height = 120,
        PlaceholderText = "192.168.1.10\tFront desk printer\n10.0.0.0/28\n10.0.5.20-30"
    };
    private readonly ComboBox _site = new() { Width = 240, IsEditable = true };
    private readonly TextBlock _siteStatus = Ui.Text("", Tokens.Label, "Pw.Text2");
    private readonly TextBox _group = new() { Width = 180 };
    private readonly ObservableCollection<PreviewRow> _rows = new();
    private readonly ListBox _preview = new() { Height = 220 };
    private readonly TextBlock _summary = Ui.Text("", Tokens.Label, "Pw.Text2");
    private readonly TextBlock _error = Ui.Text("", Tokens.Label, "Pw.Danger");
    private readonly Button _accept;
    private readonly DispatcherTimer _parse = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public AddHostsDialog(WatchdogEngine engine, bool temporary, string? site) : base(temporary ? "Quick Monitor" : "Quick Add", 900)
    {
        _engine = engine;
        _temporary = temporary;
        CanResize = true;
        Avalonia.Automation.AutomationProperties.SetName(_input, "Hosts to add");
        Avalonia.Automation.AutomationProperties.SetName(_site, "Site");
        Avalonia.Automation.AutomationProperties.SetName(_group, "Group");
        ClipboardText.Attach(_input);
        ClipboardText.Attach(_group);
        Body.Children.Add(Ui.Text("Paste addresses, spreadsheet rows, CIDR blocks or ranges. Up to 4,096 hosts.", Tokens.Body, "Pw.Text2"));
        Body.Children.Add(_input);
        var paste = Ui.Button("Paste clipboard");
        var import = Ui.Button("Import CSV");
        paste.Click += async (_, _) =>
        {
            string? text = await ClipboardText.GetAsync(this);
            if (string.IsNullOrWhiteSpace(text)) { ShowError("The clipboard has no text."); return; }
            _input.Text = string.IsNullOrWhiteSpace(_input.Text) ? text : _input.Text.TrimEnd() + "\n" + text;
        };
        import.Click += async (_, _) => await ImportCsvAsync();
        Body.Children.Add(Ui.Row(Tokens.Space2, paste, import));

        var sites = engine.Snapshot().Sites.Where(s => !s.Temporary).Select(s => s.Name).ToList();
        _site.ItemsSource = sites;
        _site.SelectedItem = sites.FirstOrDefault(s => s.Equals(site, StringComparison.OrdinalIgnoreCase)) ?? sites.FirstOrDefault();
        var assign = Ui.Button("Assign group");
        assign.Click += (_, _) =>
        {
            string group = (_group.Text ?? "").Trim();
            foreach (var row in _rows.Where(r => r.Use)) row.Group = group;
        };
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space4 };
        if (!temporary) options.Children.Add(Ui.Column(Tokens.Space1, FieldLabel("Site"), _site, _siteStatus));
        options.Children.Add(Ui.Column(Tokens.Space1, FieldLabel("Group"), Ui.Row(Tokens.Space2, _group, assign)));
        Body.Children.Add(options);

        var header = PreviewGrid();
        string[] titles = { "Use", "Address", "Label", "Site", "Group", "Status" };
        for (int i = 0; i < titles.Length; i++)
        {
            var title = Ui.Text(titles[i], Tokens.Label, "Pw.Text2");
            Grid.SetColumn(title, i);
            header.Children.Add(title);
        }
        header.Margin = new Thickness(Tokens.Space3, 0);
        _preview.ItemsSource = _rows;
        _preview.ItemTemplate = new FuncDataTemplate<PreviewRow>((_, _) => BuildRow());
        Avalonia.Automation.AutomationProperties.SetName(_preview, "Preview");
        var previewCard = Ui.Card(new DockPanel { Children = { Dock(header), _preview } }, 0);
        previewCard.Padding = new Thickness(0, Tokens.Space2, 0, 0);
        Body.Children.Add(previewCard);
        Body.Children.Add(_summary);
        _error.IsVisible = false;
        _error.TextWrapping = TextWrapping.Wrap;
        Body.Children.Add(_error);

        AddAction("Cancel", () => Close(null));
        _accept = AddAction(temporary ? "Start quick monitor" : "Add hosts", Accept, "primary");
        _accept.IsEnabled = false;
        _parse.Tick += (_, _) => { _parse.Stop(); Parse(); };
        _input.TextChanged += (_, _) => { _parse.Stop(); _parse.Start(); };
        _site.SelectionChanged += (_, _) => Validate();
        _site.PropertyChanged += (_, e) => { if (e.Property == ComboBox.TextProperty) Validate(); };
        Opened += (_, _) => _input.Focus();
        Action repaint = () => { foreach (var row in _rows) row.Repaint(); };
        Tokens.Changed += repaint;
        Closed += (_, _) => { _parse.Stop(); Tokens.Changed -= repaint; };
    }

    internal IReadOnlyList<PreviewRow> PreviewRows => _rows;
    internal string SummaryText => _summary.Text ?? "";
    internal void SetInput(string text) { _input.Text = text; Parse(); }
    internal void AcceptForTest() => Accept();

    private static Control Dock(Control control) { DockPanel.SetDock(control, Avalonia.Controls.Dock.Top); return control; }

    private static Grid PreviewGrid() => new() { ColumnDefinitions = new ColumnDefinitions("56,150,2*,130,*,200"), Height = 38 };

    private Control BuildRow()
    {
        var grid = PreviewGrid();
        grid.Margin = new Thickness(Tokens.Space3, 0);
        var use = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        use.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(PreviewRow.Use)) { Mode = BindingMode.TwoWay });
        use.Bind(IsEnabledProperty, new Binding(nameof(PreviewRow.Valid)));
        Avalonia.Automation.AutomationProperties.SetName(use, "Use");
        grid.Children.Add(use);
        string[] keys = { nameof(PreviewRow.Address), nameof(PreviewRow.Label), nameof(PreviewRow.Site), nameof(PreviewRow.Group), nameof(PreviewRow.Status) };
        for (int i = 0; i < keys.Length; i++)
        {
            Control cell;
            if (keys[i] is nameof(PreviewRow.Address) or nameof(PreviewRow.Label) or nameof(PreviewRow.Group))
            {
                var box = new TextBox
                {
                    MinHeight = 26,
                    Padding = new Thickness(6, 2),
                    FontSize = Tokens.Body,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                if (i == 0) box.FontFamily = Tokens.Mono;
                box.Bind(TextBox.TextProperty, new Binding(keys[i]) { Mode = BindingMode.TwoWay });
                Avalonia.Automation.AutomationProperties.SetName(box, keys[i]);
                ClipboardText.Attach(box);
                cell = box;
            }
            else
            {
                var text = Ui.Text("", Tokens.Body);
                text.Bind(TextBlock.TextProperty, new Binding(keys[i]));
                text.Bind(ToolTip.TipProperty, new Binding(keys[i]));
                if (i == 4) text.Bind(TextBlock.ForegroundProperty, new Binding(nameof(PreviewRow.StatusKey)) { Converter = StatusBrush.Instance });
                cell = text;
            }
            cell.Margin = new Thickness(0, 0, Tokens.Space2, 0);
            Grid.SetColumn(cell, i + 1);
            grid.Children.Add(cell);
        }
        return new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Pw.Border");
    }

    private sealed class StatusBrush : Avalonia.Data.Converters.IValueConverter
    {
        public static readonly StatusBrush Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            var p = Tokens.Current;
            return new SolidColorBrush((value as string) switch { "new" => p.Success, "warn" => p.Warning, "error" => p.Danger, _ => p.Text });
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    private string TargetSite => _temporary ? WatchdogEngine.QuickMonitorSite : (_site.Text ?? _site.SelectedItem as string ?? "").Trim();

    // An existing site, or a new one created when the hosts are added (Windows "Creates site").
    private (string Name, bool IsNew) ResolveSite()
    {
        string typed = TargetSite;
        var existing = _engine.Snapshot().Sites.FirstOrDefault(s => s.Name.Equals(typed, StringComparison.OrdinalIgnoreCase));
        return existing is null ? (typed, typed.Length > 0) : (existing.Name, false);
    }

    private bool _validating;

    private void Parse()
    {
        _error.IsVisible = false;
        foreach (var row in _rows) row.PropertyChanged -= RowEdited;
        _rows.Clear();
        foreach (var item in HostImport.Parse(_input.Text ?? ""))
        {
            bool valid = string.IsNullOrEmpty(item.Error);
            var row = new PreviewRow { Address = item.Address, Label = item.Label, Group = item.Group, Valid = valid, Status = item.Error };
            row.Use = valid;
            row.PropertyChanged += RowEdited;
            _rows.Add(row);
        }
        Validate();
    }

    // An edited address, label or group re-checks every row, as the Windows preview does.
    private void RowEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (_validating || e.PropertyName is not (nameof(PreviewRow.Address) or nameof(PreviewRow.Label) or nameof(PreviewRow.Use))) return;
        Validate();
    }

    private void Validate()
    {
        _validating = true;
        try
        {
            var existing = _engine.Snapshot().Sites.SelectMany(s => s.Hosts).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string site = TargetSite;
            foreach (var row in _rows)
            {
                string address = row.Address.Trim();
                string normal = HostImport.Valid(address) ? HostImport.Normalize(address) : address;
                bool wasValid = row.Valid;
                row.Site = site;
                if (!HostImport.Valid(address))
                {
                    row.Valid = false;
                    row.Use = false;
                    // Keep the parser's own message (CIDR or range errors) until the address is edited.
                    if (row.Status.Length == 0 || row.StatusKey is "new" or "warn" or "plain") row.Status = "Invalid IP address or hostname.";
                    row.StatusKey = "error";
                    continue;
                }
                row.Valid = true;
                if (!wasValid) row.Use = true;
                if (!seen.Add(normal)) { row.Status = "Duplicate in preview"; row.StatusKey = "warn"; }
                else if (!_temporary && existing.FirstOrDefault(h => h.Address.Equals(normal, StringComparison.OrdinalIgnoreCase)) is { } found)
                {
                    string label = row.Label.Trim();
                    row.Status = found.Label != label && label.Length > 0 ? "Label conflict • " + found.Site : "Already exists • " + found.Site;
                    row.StatusKey = "plain";
                }
                else { row.Status = "New"; row.StatusKey = "new"; }
            }
        }
        finally { _validating = false; }
        int fresh = _rows.Count(r => r.StatusKey == "new"), conflicts = _rows.Count(r => r.StatusKey is "warn" or "plain"), invalid = _rows.Count(r => !r.Valid);
        _summary.Text = _rows.Count == 0 ? "" : $"{fresh} new · {conflicts} duplicates/conflicts · {invalid} invalid";
        var (siteName, isNew) = ResolveSite();
        _siteStatus.Text = isNew ? $"Creates site “{siteName}”" : "";
        _siteStatus.IsVisible = isNew;
        // Only new, valid rows are added, as on Windows.
        _accept.IsEnabled = _rows.Any(r => r.Use && r.StatusKey == "new") && (siteName.Length > 0 || _temporary);
    }

    private void ShowError(string message) { _error.Text = message; _error.IsVisible = true; }

    private async Task ImportCsvAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import CSV",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CSV or text") { Patterns = new[] { "*.csv", "*.txt", "*.tsv" } } }
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            _input.Text = ClipboardText.Normalize(await reader.ReadToEndAsync());
            Parse();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Couldn't read that file: " + ex.Message);
        }
    }

    private void Accept()
    {
        Validate();
        var rows = _rows.Where(r => r.Use && r.StatusKey == "new").Select(r => new ImportedHost(HostImport.Normalize(r.Address.Trim()), r.Label.Trim(), r.Group.Trim(), "")).ToList();
        if (rows.Count == 0) { ShowError("Select at least one new, valid host."); return; }
        var (siteName, isNew) = ResolveSite();
        if (!_temporary && siteName.Length == 0) { ShowError("Enter a site name first."); _site.Focus(); return; }
        var (added, error) = _temporary ? _engine.StartQuickMonitor(rows) : _engine.AddHosts(siteName, rows, createSite: isNew);
        if (error is not null) { ShowError(error); return; }
        Close(added);
    }
}
