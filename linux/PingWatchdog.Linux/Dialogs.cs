// Small one-task dialogs: messages, confirmations, a name, Edit host, choose a site, per-host monitoring.
// Every dialog is owned by the window that raised it; Escape cancels and Enter accepts.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PingWatchdog.Linux;

internal class DialogWindow : Window
{
    protected readonly StackPanel Body = new() { Spacing = Tokens.Space3 };
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Space2, HorizontalAlignment = HorizontalAlignment.Right };
    private Button? _accept;

    public DialogWindow(string title, double width = 420)
    {
        Title = title + " • Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new DockPanel { Margin = new Thickness(Tokens.Space6, Tokens.Space5, Tokens.Space6, Tokens.Space5) };
        Actions.Margin = new Thickness(0, Tokens.Space5, 0, 0);
        DockPanel.SetDock(Actions, Dock.Bottom);
        root.Children.Add(Actions);
        root.Children.Add(Body);
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(null); }
            else if (e.Key == Key.Enter && _accept is { IsEnabled: true } && e.Source is not TextBox { AcceptsReturn: true })
            {
                e.Handled = true;
                _accept.Command?.Execute(null);
                _accept.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
        };
    }

    protected Button AddAction(string text, Action onClick, string? kind = null, bool accept = false)
    {
        var button = Ui.Button(text, kind);
        button.Click += (_, _) => onClick();
        Actions.Children.Add(button);
        if (accept) _accept = button;
        return button;
    }

    protected static TextBlock FieldLabel(string text) => Ui.Text(text, Tokens.Label, "Pw.Text2");

    protected static TextBlock Wrapped(string text, string color = "Pw.Text")
    {
        var block = Ui.Text(text, Tokens.Body, color);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }
}

internal static class Dialogs
{
    public static async Task Message(Window owner, string title, string text)
    {
        var dialog = new MessageDialog(title, text, "OK", null, false);
        await dialog.ShowDialog<bool?>(owner);
    }

    public static async Task<bool> Confirm(Window owner, string title, string text, string confirm, bool destructive = false)
    {
        var dialog = new MessageDialog(title, text, confirm, "Cancel", destructive);
        return await dialog.ShowDialog<bool?>(owner) == true;
    }

    // Save / Don't save / Cancel. Null means cancel.
    public static async Task<bool?> SaveDiscard(Window owner, string text)
    {
        var dialog = new SaveDiscardDialog(text);
        return await dialog.ShowDialog<bool?>(owner);
    }

    public static Task<string?> Prompt(Window owner, string title, string label, string initial = "", string accept = "Save", Func<string, string?>? validate = null)
        => new PromptDialog(title, label, initial, accept, validate).ShowDialog<string?>(owner);

    public static Task<(string Address, string Label)?> EditHost(Window owner, HostSnapshot host, Func<string, string, string?> save)
        => new EditHostDialog(host, save).ShowDialog<(string, string)?>(owner);

    public static Task<(string Site, bool Create)?> ChooseSite(Window owner, string title, string accept, IEnumerable<string> sites, string? current = null)
        => new ChooseSiteDialog(title, accept, sites, current).ShowDialog<(string, bool)?>(owner);

    public static Task<(int Interval, int Timeout, int Failures)?> Monitoring(Window owner, string subject, HostOptions? current, WatchdogConfig defaults)
        => new MonitoringDialog(subject, current, defaults).ShowDialog<(int, int, int)?>(owner);

    public static Task ShowText(Window owner, string title, string text, bool copy)
        => new TextReviewDialog(title, text, copy).ShowDialog<object?>(owner);
}

internal sealed class MessageDialog : DialogWindow
{
    public MessageDialog(string title, string text, string confirm, string? cancel, bool destructive) : base(title)
    {
        Body.Children.Add(Ui.Title(title, Tokens.Title3));
        Body.Children.Add(Wrapped(text));
        if (cancel is not null) AddAction(cancel, () => Close(false));
        var accept = AddAction(confirm, () => Close(true), destructive ? "destructive" : cancel is null ? null : "primary", accept: true);
        Opened += (_, _) => (cancel is null || !destructive ? accept : (Control)Actions.Children[0]).Focus();
    }
}

internal sealed class SaveDiscardDialog : DialogWindow
{
    public SaveDiscardDialog(string text) : base("Unsaved changes")
    {
        Body.Children.Add(Ui.Title("Unsaved changes", Tokens.Title3));
        Body.Children.Add(Wrapped(text));
        AddAction("Cancel", () => Close(null));
        AddAction("Don't save", () => Close(false));
        var save = AddAction("Save changes", () => Close(true), "primary", accept: true);
        Opened += (_, _) => save.Focus();
    }
}

internal sealed class PromptDialog : DialogWindow
{
    private readonly TextBox _box = new();
    private readonly TextBlock _error = Ui.Text("", Tokens.Label, "Pw.Danger");

    public PromptDialog(string title, string label, string initial, string accept, Func<string, string?>? validate) : base(title)
    {
        _box.Text = initial;
        Avalonia.Automation.AutomationProperties.SetName(_box, label);
        ClipboardText.Attach(_box);
        Body.Children.Add(FieldLabel(label));
        Body.Children.Add(_box);
        _error.IsVisible = false;
        _error.TextWrapping = TextWrapping.Wrap;
        Body.Children.Add(_error);
        AddAction("Cancel", () => Close(null));
        AddAction(accept, () =>
        {
            string value = (_box.Text ?? "").Trim();
            string? error = value.Length == 0 ? "Enter a name first." : validate?.Invoke(value);
            if (error is not null) { _error.Text = error; _error.IsVisible = true; _box.Focus(); return; }
            Close(value);
        }, "primary", accept: true);
        Opened += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}

internal sealed class EditHostDialog : DialogWindow
{
    private readonly TextBox _address = new() { FontFamily = Tokens.Mono };
    private readonly TextBox _label = new();
    private readonly TextBlock _error = Ui.Text("", Tokens.Label, "Pw.Danger");

    public EditHostDialog(HostSnapshot host, Func<string, string, string?> save) : base("Edit host")
    {
        Title = "Edit host • " + host.Address;
        _address.Text = host.Address;
        _label.Text = host.Label;
        Avalonia.Automation.AutomationProperties.SetName(_address, "Address");
        Avalonia.Automation.AutomationProperties.SetName(_label, "Label");
        ClipboardText.Attach(_address);
        ClipboardText.Attach(_label);
        Body.Children.Add(FieldLabel("Address"));
        Body.Children.Add(_address);
        Body.Children.Add(FieldLabel("Label"));
        Body.Children.Add(_label);
        _error.IsVisible = false;
        _error.TextWrapping = TextWrapping.Wrap;
        Body.Children.Add(_error);
        AddAction("Cancel", () => Close(null));
        AddAction("Save", () =>
        {
            string address = (_address.Text ?? "").Trim(), label = (_label.Text ?? "").Trim();
            string? error = save(address, label);
            if (error is not null) { _error.Text = error; _error.IsVisible = true; return; }
            Close((address, label));
        }, "primary", accept: true);
        Opened += (_, _) => { _address.Focus(); _address.SelectAll(); };
    }
}

internal sealed class ChooseSiteDialog : DialogWindow
{
    private readonly ComboBox _sites = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _newSite = new() { PlaceholderText = "New site name" };
    private readonly RadioButton _existing = new() { Content = "Existing site", GroupName = "site", IsChecked = true };
    private readonly RadioButton _create = new() { Content = "New site", GroupName = "site" };
    private readonly TextBlock _error = Ui.Text("", Tokens.Label, "Pw.Danger");

    public ChooseSiteDialog(string title, string accept, IEnumerable<string> sites, string? current) : base(title)
    {
        var names = sites.ToList();
        _sites.ItemsSource = names;
        _sites.SelectedItem = names.FirstOrDefault(n => !n.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
        Avalonia.Automation.AutomationProperties.SetName(_sites, "Destination site");
        Avalonia.Automation.AutomationProperties.SetName(_newSite, "New site name");
        ClipboardText.Attach(_newSite);
        if (names.Count == 0) { _existing.IsEnabled = false; _create.IsChecked = true; }
        Body.Children.Add(_existing);
        Body.Children.Add(_sites);
        Body.Children.Add(_create);
        Body.Children.Add(_newSite);
        _error.IsVisible = false;
        Body.Children.Add(_error);
        void Sync() { _sites.IsEnabled = _existing.IsChecked == true; _newSite.IsEnabled = _create.IsChecked == true; }
        _existing.IsCheckedChanged += (_, _) => Sync();
        _create.IsCheckedChanged += (_, _) => Sync();
        _newSite.GotFocus += (_, _) => _create.IsChecked = true;
        Sync();
        AddAction("Cancel", () => Close(null));
        AddAction(accept, () =>
        {
            if (_create.IsChecked == true)
            {
                string name = (_newSite.Text ?? "").Trim();
                if (name.Length == 0) { _error.Text = "Enter a site name first."; _error.IsVisible = true; return; }
                if (names.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))) { Close((names.First(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)), false)); return; }
                Close((name, true));
            }
            else if (_sites.SelectedItem is string site) Close((site, false));
            else { _error.Text = "Select an existing destination site."; _error.IsVisible = true; }
        }, "primary", accept: true);
        Opened += (_, _) => (names.Count == 0 ? (Control)_newSite : _sites).Focus();
    }
}

internal sealed class MonitoringDialog : DialogWindow
{
    public MonitoringDialog(string subject, HostOptions? current, WatchdogConfig defaults) : base("Monitoring")
    {
        Body.Children.Add(Ui.Title("Monitoring for " + subject, Tokens.Title3));
        Body.Children.Add(Wrapped("0 uses the default from Settings.", "Pw.Text2"));
        var interval = NativeControls.Number(0, 300, current?.IntervalSeconds ?? 0, 1, 110);
        var timeout = NativeControls.Number(0, 10000, current?.TimeoutMs ?? 0, 250, 110);
        var failures = NativeControls.Number(0, 20, current?.FailureThreshold ?? 0, 1, 110);
        Body.Children.Add(NativeControls.SettingRow("Interval in seconds", interval, $"default {defaults.PingIntervalSeconds}"));
        Body.Children.Add(NativeControls.SettingRow("Timeout in milliseconds", timeout, $"default {defaults.PingTimeoutMs}"));
        Body.Children.Add(NativeControls.SettingRow("Failures before offline", failures, $"default {defaults.FailureThreshold}"));
        AddAction("Cancel", () => Close(null));
        AddAction("Save", () =>
        {
            int failureValue = (int)(failures.Value ?? 0);
            if (failureValue == 1) failureValue = 2;
            Close(((int)(interval.Value ?? 0), (int)(timeout.Value ?? 0), failureValue));
        }, "primary", accept: true);
        Opened += (_, _) => interval.Focus();
    }
}

internal sealed class TextReviewDialog : DialogWindow
{
    public TextReviewDialog(string title, string text, bool copy) : base(title, 640)
    {
        Body.Children.Add(Ui.Title(title, Tokens.Title3));
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Tokens.Mono,
            FontSize = Tokens.Label,
            Height = 360
        };
        Avalonia.Automation.AutomationProperties.SetName(box, title);
        Body.Children.Add(box);
        var status = Ui.Text("", Tokens.Label, "Pw.Text2");
        Body.Children.Add(status);
        if (copy)
            AddAction("Copy", async () =>
            {
                status.Text = await ClipboardText.SetAsync(this, text) ? "Copied." : "Couldn't reach the clipboard.";
            });
        AddAction("Close", () => Close(null), "primary", accept: true);
    }
}

internal static class NativeControls
{
    public static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step, double width)
    {
        var number = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Increment = step,
            Width = width,
            FormatString = "0",
            ParsingNumberStyle = System.Globalization.NumberStyles.Integer,
            ClipValueToMinMax = true
        };
        return number;
    }

    public static Control SettingRow(string title, Control input, string suffix)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,Auto,Auto"), Margin = new Thickness(0, 2) };
        var label = Ui.Text(title);
        Avalonia.Automation.AutomationProperties.SetName(input, title);
        grid.Children.Add(label);
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        if (suffix.Length > 0)
        {
            var suffixText = Ui.Text(suffix, Tokens.Label, "Pw.Text2");
            suffixText.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
            Grid.SetColumn(suffixText, 2);
            grid.Children.Add(suffixText);
        }
        return grid;
    }
}
