// Parts shared by the main window and the Wallboard: Ping activity, the priority bar, the update popup,
// the short confirmation that offers Undo, and the optional version overlay.
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal static class BrandAssets
{
    internal static readonly Avalonia.Media.Imaging.Bitmap Logo = LoadLogo();

    private static Avalonia.Media.Imaging.Bitmap LoadLogo()
    {
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("PingWatchdog.Brand.Logo")
            ?? throw new InvalidOperationException("Embedded Watchdog logo is missing.");
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}

internal sealed record PingLine(string Text, bool Success);

// The live ping log. Lines stay where they are while new ones arrive; following stops when you scroll up.
internal sealed class PingActivityView : DockPanel
{
    private readonly ObservableCollection<PingLine> _lines = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _empty = Ui.Text("No ping activity", Tokens.Label, "Pw.Text2", mono: true);
    private DateTime _lastStamp = DateTime.MinValue;
    private int _lastCount;

    public PingActivityView(Action clear, bool showHeader = true)
    {
        _list.ItemsSource = _lines;
        _list.ItemTemplate = new FuncDataTemplate<PingLine>((line, _) =>
        {
            var text = Ui.Text(line?.Text ?? "", Tokens.Label, line?.Success == false ? "Pw.Danger" : "Pw.Text", mono: true);
            text.TextTrimming = TextTrimming.None;
            text.TextWrapping = TextWrapping.NoWrap;
            text.Margin = new Thickness(Tokens.Space2, 1);
            return text;
        });
        _list.SelectionMode = SelectionMode.Single;
        Avalonia.Automation.AutomationProperties.SetName(_list, "Ping activity");
        if (showHeader)
        {
            var title = Ui.Text("Ping activity", Tokens.Body, "Pw.Text2");
            var clearButton = Ui.Button("Clear", "ghost", "Clear ping activity");
            clearButton.Click += (_, _) => clear();
            var header = Ui.Row(Tokens.Space4, title, clearButton);
            header.Margin = new Thickness(Tokens.Space2, 0, 0, Tokens.Space2);
            SetDock(header, Dock.Top);
            Children.Add(header);
        }
        var body = new Panel();
        body.Children.Add(_list);
        _empty.Margin = new Thickness(Tokens.Space3, Tokens.Space2);
        _empty.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(_empty);
        var well = new Border
        {
            CornerRadius = new CornerRadius(Tokens.RadiusMedium),
            BorderThickness = new Thickness(1),
            Child = body,
            ClipToBounds = true
        }.Res(Border.BackgroundProperty, "Pw.Surface").Res(Border.BorderBrushProperty, "Pw.Border");
        Children.Add(well);
    }

    public int LineCount => _lines.Count;
    public string EmptyText { get => _empty.Text ?? ""; set => _empty.Text = value; }

    public void Refresh(IReadOnlyList<CommandLogEntry> commands, string emptyText = "No ping activity")
    {
        _empty.Text = emptyText;
        _empty.IsVisible = commands.Count == 0;
        var newest = commands.Count == 0 ? DateTime.MinValue : commands[^1].Timestamp;
        if (commands.Count == _lastCount && newest == _lastStamp) return;
        var scroll = _list.FindDescendantOfType<ScrollViewer>();
        bool follow = scroll is null || scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 4;
        var tail = commands.TakeLast(200).Select(c => new PingLine(c.Text, c.Success)).ToList();
        _lines.Clear();
        foreach (var line in tail) _lines.Add(line);
        _lastCount = commands.Count;
        _lastStamp = newest;
        if (follow && _lines.Count > 0) Dispatcher.UIThread.Post(() => _list.ScrollIntoView(_lines.Count - 1), DispatcherPriority.Background);
    }
}

internal static class VisualHelpers
{
    public static T? FindDescendantOfType<T>(this Visual visual) where T : Visual
    {
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(visual))
            if (child is T match) return match;
        return null;
    }
}

// Shown while priority alerts are unacknowledged: a stripe plus words, never color alone.
internal sealed class PriorityBanner : Border
{
    private readonly PriorityAlarm _alarm;
    private readonly TextBlock _message = Ui.Text("");
    private readonly Button _acknowledge = Ui.Button("Acknowledge", "primary");
    private readonly Button _acknowledgeAll = Ui.Button("Acknowledge all", "ghost");
    private readonly Border _stripe = new() { Width = 6, CornerRadius = new CornerRadius(3) };

    public PriorityBanner(PriorityAlarm alarm)
    {
        _alarm = alarm;
        IsVisible = false;
        CornerRadius = new CornerRadius(Tokens.RadiusCard);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(Tokens.Space3, Tokens.Space2, Tokens.Space3, Tokens.Space2);
        Margin = new Thickness(0, 0, 0, Tokens.Space3);
        this.Res(BackgroundProperty, "Pw.Surface2").Res(BorderBrushProperty, "Pw.Border");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = Tokens.Space3 };
        grid.Children.Add(_stripe);
        Grid.SetColumn(_message, 1);
        grid.Children.Add(_message);
        Grid.SetColumn(_acknowledgeAll, 2);
        grid.Children.Add(_acknowledgeAll);
        Grid.SetColumn(_acknowledge, 3);
        grid.Children.Add(_acknowledge);
        Child = grid;
        _acknowledge.Click += (_, _) => { if (_alarm.Active.FirstOrDefault() is { } first) _alarm.Acknowledge(first.Id); };
        _acknowledgeAll.Click += (_, _) => _alarm.AcknowledgeAll();
        _alarm.Changed += Sync;
        Tokens.Changed += Sync;
        DetachedFromVisualTree += (_, _) => { _alarm.Changed -= Sync; Tokens.Changed -= Sync; };
        Sync();
    }

    public string MessageText => _message.Text ?? "";

    private void Sync()
    {
        var active = _alarm.Active;
        if (active.Count == 0) { IsVisible = false; return; }
        var first = active[0];
        _message.Text = "Priority · " + first.Summary + (active.Count > 1 ? $"  (+{active.Count - 1} more)" : "");
        Avalonia.Automation.AutomationProperties.SetName(this, _message.Text);
        Avalonia.Automation.AutomationProperties.SetLiveSetting(this, Avalonia.Automation.AutomationLiveSetting.Assertive);
        _stripe.Background = new SolidColorBrush(first.Offline ? Tokens.Current.Danger : Tokens.Current.Success);
        _acknowledgeAll.IsVisible = active.Count > 1;
        IsVisible = true;
    }
}

// Bottom-right update status. It stands apart from the map with a border and a shadow (#25, #33).
internal sealed class UpdatePopup : Border
{
    private readonly TextBlock _title = Ui.Text("Updates", Tokens.Body, "Pw.Text2");
    private readonly TextBlock _status = Ui.Text("");
    private readonly Button _primary = Ui.Button("Restart now", "primary");
    private readonly Button _dismiss = Ui.Button("Dismiss", "ghost");
    private Action? _primaryAction;

    public UpdatePopup()
    {
        IsVisible = false;
        Width = 340;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(Tokens.Space4);
        Padding = new Thickness(Tokens.Space4);
        CornerRadius = new CornerRadius(Tokens.RadiusCard);
        BorderThickness = new Thickness(1);
        BoxShadow = BoxShadows.Parse("0 8 24 0 #40000000");
        this.Res(BackgroundProperty, "Pw.Surface2").Res(BorderBrushProperty, "Pw.BorderStrong");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextTrimming = TextTrimming.None;
        var buttons = Ui.Row(Tokens.Space2, _dismiss, _primary);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Child = Ui.Column(Tokens.Space2, _title, _status, buttons);
        _dismiss.Click += (_, _) => IsVisible = false;
        _primary.Click += (_, _) => _primaryAction?.Invoke();
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_status, Avalonia.Automation.AutomationLiveSetting.Polite);
    }

    public string StatusText => _status.Text ?? "";

    public void Show(string status, string? primary = null, Action? action = null)
    {
        _status.Text = status;
        _primary.IsVisible = primary is not null;
        if (primary is not null) _primary.Content = primary;
        _primaryAction = action;
        IsVisible = true;
    }
}

// One line after an edit, with Undo when it can be undone. Disappears after a few seconds.
internal sealed class StatusToast : Border
{
    private readonly TextBlock _text = Ui.Text("");
    private readonly Button _undo = Ui.Button("Undo", "ghost");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };

    public StatusToast(Action undo)
    {
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        // Bottom-left of the work area, clear of the sidebar's buttons.
        Margin = new Thickness(256, 0, 0, Tokens.Space4);
        Padding = new Thickness(Tokens.Space4, Tokens.Space1, Tokens.Space1, Tokens.Space1);
        CornerRadius = new CornerRadius(Tokens.RadiusMedium);
        BorderThickness = new Thickness(1);
        BoxShadow = BoxShadows.Parse("0 6 18 0 #30000000");
        this.Res(BackgroundProperty, "Pw.Surface2").Res(BorderBrushProperty, "Pw.BorderStrong");
        Child = Ui.Row(Tokens.Space2, _text, _undo);
        _undo.Click += (_, _) => { IsVisible = false; undo(); };
        _timer.Tick += (_, _) => { _timer.Stop(); IsVisible = false; };
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_text, Avalonia.Automation.AutomationLiveSetting.Polite);
    }

    public string Message => IsVisible ? _text.Text ?? "" : "";

    public void Show(string message, bool undo)
    {
        _text.Text = message;
        _undo.IsVisible = undo;
        IsVisible = true;
        _timer.Stop();
        _timer.Start();
    }
}

// Settings → Advanced → Testing: the version and build in the bottom-left corner of every window.
internal static class VersionOverlay
{
    public static TextBlock Create()
    {
        var text = Ui.Text("v" + LinuxUpdateService.Version + " · Linux " + LinuxUpdateService.PackageKind, Tokens.Caption, "Pw.Text3");
        text.HorizontalAlignment = HorizontalAlignment.Left;
        text.VerticalAlignment = VerticalAlignment.Bottom;
        text.Margin = new Thickness(Tokens.Space2, 0, 0, Tokens.Space1);
        text.IsHitTestVisible = false;
        return text;
    }
}
