using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia;
using Avalonia.VisualTree;

namespace PingWatchdog.Linux;

// Runs real Avalonia desktop windows under Xvfb, with isolated storage and no update checks.
internal static class LinuxUiRegressionTests
{
    public static bool Enabled { get; set; }

    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        string root = Path.Combine(Path.GetTempPath(), "ping-watchdog-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        var records = Enumerable.Range(0, 300).Select(i => new StateEventRecord(
            DateTime.Now.AddMinutes(-i), "Archived site", "127.0.0.1", "Archived host", "DOWN", $"Event {i}")).ToList();
        records.Add(new StateEventRecord(DateTime.Now, "Beta", "127.0.0.2", "Beta host", "SUSPECT", "Suspect"));
        File.WriteAllText(Path.Combine(root, "state", "event-history.json"), JsonSerializer.Serialize(records));
        var engine = new WatchdogEngine(root);
        var config = engine.Snapshot().Settings;
        config.AutoCheckUpdates = false;
        engine.ApplySettings(config);
        engine.AddSite("Alpha");
        engine.SaveHosts("Alpha", "127.0.0.1");
        engine.AddSite("Beta");
        engine.SaveHosts("Beta", "127.0.0.2");
        engine.SelectedSite = "Alpha";
        var updates = new LinuxUpdateService();
        var main = new MainWindow(engine, updates);
        desktop.MainWindow = main;
        bool started = false;
        main.Opened += (_, _) =>
        {
            if (started) return;
            started = true;
            Dispatcher.UIThread.Post(async () =>
            {
            int exitCode = 0;
            try
            {
                await RunAsync(engine, updates, main);
                Console.WriteLine("Ping Watchdog Linux UI regressions passed.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                exitCode = 1;
            }
            finally
            {
                desktop.Shutdown(exitCode);
                updates.Dispose();
                engine.Dispose();
                try { Directory.Delete(root, true); } catch { }
            }
            });
        };
    }

    private static async Task RunAsync(WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        await Task.Delay(100);
        if (double.TryParse(Environment.GetEnvironmentVariable("WATCHDOG_EXPECTED_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double expectedScale))
            Check(Math.Abs(main.RenderScaling - expectedScale) < .01, $"Desktop render scale is {expectedScale}");
        await CheckDesktopPresentationAsync(engine, updates, main);
        var editor = Field<TextBox>(main, "_hostEditor");
        await CheckHostPanelLayoutAsync(main, editor);
        editor.Text = "127.0.0.1\n127.0.0.3";
        engine.Save();
        await Task.Delay(850);
        Check(editor.Text!.Contains("127.0.0.3"), "Main draft survives timer refresh while unfocused");
        engine.SelectedSite = "Beta";
        await Task.Delay(150);
        Check(engine.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts.Contains("127.0.0.3"), "Main selection change saves original site's draft");
        Check(engine.SiteDefinitions().Single(s => s.Name == "Beta").Hosts.SequenceEqual(new[] { "127.0.0.2" }), "Main draft never leaks to new site");
        Check(editor.Focus(), "Main editor receives focus");
        editor.Text = "127.0.0.2\n127.0.0.4";
        Field<Button>(main, "_startStop").Focus();
        await Task.Delay(100);
        Check(engine.SiteDefinitions().Single(s => s.Name == "Beta").Hosts.Contains("127.0.0.4"), "Main focus loss persists edits");

        var wallboard = new WallboardWindow(engine, updates, main);
        wallboard.Show();
        await Task.Delay(150);
        var hosts = Field<TextBox>(wallboard, "_hosts");
        hosts.Text = "127.0.0.2\n127.0.0.5";
        await Task.Delay(850);
        Check(hosts.Text!.Contains("127.0.0.5"), "Wallboard draft survives timer refresh while unfocused");
        engine.SelectedSite = "Alpha";
        await Task.Delay(850);
        Check(engine.SiteDefinitions().Single(s => s.Name == "Beta").Hosts.Contains("127.0.0.5"), "Wallboard selection change saves original site's draft");
        Field<StackPanel>(wallboard, "_ops").IsVisible = true;
        await Task.Delay(100);
        Check(hosts.Focus(), "Wallboard editor receives focus");
        bool cli = Field<bool>(wallboard, "_showCli");
        hosts.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C });
        Check(Field<bool>(wallboard, "_showCli") == cli, "Typing C in Wallboard editor does not toggle CLI");
        hosts.Text = "127.0.0.1\n127.0.0.6";
        wallboard.Close();
        Check(engine.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts.Contains("127.0.0.6"), "Closing Wallboard persists edits");
        await RunWallboardParityAsync(engine, updates, main);

        var history = new HistoryWindow(engine);
        history.Show(main);
        await Task.Delay(100);
        Check(engine.Config.HideSuspectEvents, "Opening History preserves the saved suspect filter");
        Field<ComboBox>(history, "_range").SelectedIndex = 3;
        Field<CheckBox>(history, "_hideSuspects").IsChecked = false;
        var site = Field<ComboBox>(history, "_site");
        site.SelectedIndex = Field<List<string?>>(history, "_siteKeys").IndexOf("Archived site");
        await Task.Delay(100);
        Check(Field<IReadOnlyList<StateEventRecord>>(history, "_visibleEvents").Count == 300, "History UI includes all 300 filtered records from a removed site");
        Check(Field<TextBlock>(history, "_summary").Text!.Contains("301"), "History UI reports total stored count");
        history.Close();

        int updateSubscribers = SubscriberCount(updates, "Changed");
        var settings = new SettingsWindow(engine, updates, main);
        settings.Show(main);
        Check(SubscriberCount(updates, "Changed") == updateSubscribers + 1, "Settings subscribes while open");
        settings.Close();
        Check(SubscriberCount(updates, "Changed") == updateSubscribers, "Settings detaches updater subscription on close");
        int engineSubscribers = SubscriberCount(engine, "Changed");
        var organization = new OrganizationWindow(engine);
        organization.Show(main);
        Check(SubscriberCount(engine, "Changed") == engineSubscribers + 1, "Organization subscribes while open");
        engine.SaveHosts("Alpha", "127.0.0.1\n127.0.0.6");
        organization.Close();
        Check(SubscriberCount(engine, "Changed") == engineSubscribers, "Organization detaches engine subscription on close");
        await Task.Delay(100);
        engine.SelectedSite = null;
        await Task.Delay(100);
        Check(editor.IsReadOnly, "All Sites editor is read only");
        Check(engine.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts.Contains("127.0.0.6"), "Programmatic main refresh cannot overwrite a newer Wallboard edit");

        engine.SelectedSite = "Alpha";
        await Task.Delay(100);
        var hostList = Field<ListBox>(main, "_hostList");
        hostList.SelectedIndex = 0;
        var selectedRow = hostList.SelectedItem;
        var itemSource = hostList.ItemsSource;
        var sites = Field<ListBox>(main, "_siteList").ItemsSource;
        Field<NumericUpDown>(main, "_interval").Value = 30;
        await Task.Delay(850);
        Check(ReferenceEquals(hostList.ItemsSource, itemSource) && ReferenceEquals(hostList.SelectedItem, selectedRow), "Timer refresh preserves host collection and selection");
        Check(ReferenceEquals(Field<ListBox>(main, "_siteList").ItemsSource, sites), "Timer refresh preserves site collection");
        Check(Field<NumericUpDown>(main, "_interval").Value == 30, "Timer refresh preserves unsaved timing controls");
        string originalHosts = string.Join(Environment.NewLine, engine.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts);
        engine.SaveHosts("Alpha", originalHosts + Environment.NewLine + string.Join(Environment.NewLine, Enumerable.Range(10, 150).Select(i => $"127.0.0.{i}")));
        await Task.Delay(150);
        hostList.ScrollIntoView(Field<System.Collections.ObjectModel.ObservableCollection<HostRow>>(main, "_hostRows")[80]);
        await Task.Delay(150);
        var hostScroll = hostList.GetVisualDescendants().OfType<ScrollViewer>().First();
        double scrollY = hostScroll.Offset.Y;
        Check(scrollY > 0, "Large host list scroll fixture is realized");
        engine.SetLabel("Alpha", "127.0.0.6", "Scroll test");
        await Task.Delay(850);
        Check(Math.Abs(hostScroll.Offset.Y - scrollY) < 1 && ReferenceEquals(hostList.SelectedItem, selectedRow), "Live row updates preserve scroll position and keyed selection");
        engine.SetLabel("Alpha", "127.0.0.6", "");
        engine.SaveHosts("Alpha", originalHosts);
        await Task.Delay(150);
        Check(hostList.ContextMenu is not null && Field<Button>(main, "_editLabel").IsEnabled, "Host label actions are available from selection and context menu");
        var editTask = (Task)Invoke(main, "EditLabelAsync")!;
        await Task.Delay(100);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var labelDialog = lifetime.Windows.Single(w => w.Title == "Edit Host Label");
        labelDialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "Edge router";
        labelDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "OK")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await editTask;
        await Task.Delay(100);
        Check(engine.SiteDefinitions().Single(s => s.Name == "Alpha").Labels["127.0.0.1"] == "Edge router", "Host label dialog saves the selected address");
        Check(ReferenceEquals(hostList.SelectedItem, selectedRow) && ((HostRow)selectedRow!).Text.Contains("Edge router"), "Label refresh updates the selected row in place");
        Field<Button>(main, "_clearLabel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        Check(!engine.SiteDefinitions().Single(s => s.Name == "Alpha").Labels.ContainsKey("127.0.0.1"), "Clear Label UI removes the label");

        var trace = Field<CliTraceView>(main, "_commandBox");
        var success = new CommandLogEntry(DateTime.Now, "Alpha", "127.0.0.1", "", true, "127.0.0.1: ping -c 1 -W 1 127.0.0.1 -> Reply");
        var failure = success with { Timestamp = DateTime.Now.AddTicks(1), Success = false, Text = "127.0.0.1: ping -c 1 -W 1 127.0.0.1 -> FAILED" };
        trace.IsVisible = true;
        trace.Refresh(new[] { success, failure });
        var traceList = Field<ListBox>(trace, "_list");
        var successLabel = (TextBlock)traceList.ItemTemplate!.Build(success)!;
        var failureLabel = (TextBlock)traceList.ItemTemplate.Build(failure)!;
        Check(Equals(successLabel.Foreground, Theme.Green) && Equals(failureLabel.Foreground, Theme.Red), "CLI renders success green and failure red per command");
        Check(((TextBlock)traceList.ItemTemplate.Build(null)!).Text == string.Empty, "CLI recycled null content renders safely");
        var traceRows = traceList.ItemsSource;
        trace.Refresh(new[] { success, failure });
        Check(ReferenceEquals(traceRows, traceList.ItemsSource), "CLI refresh preserves its list collection");
        trace.Refresh(Array.Empty<CommandLogEntry>());
        Check(traceList.ItemCount == 0, "CLI clear removes visible rows");

        settings = new SettingsWindow(engine, updates, main);
        settings.Show(main);
        Field<CheckBox>(settings, "_minimizeToTray").IsChecked = false;
        Invoke(settings, "SaveValues");
        Check(!engine.Config.MinimizeToTray, "Settings UI saves minimize-to-tray preference");
        Check(settings.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Import Config...")
            && settings.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Export Config..."), "Settings exposes native config import/export actions");
        settings.Close();
        Check(!await LinuxTrayHost.AvailableAsync(), "Real session-bus check detects the missing tray host in CI");
        bool hostAvailable = true;
        using (var tray = new LinuxTrayService(Application.Current!, main, engine, updates, () => Task.FromResult(hostAvailable)))
        {
            main.AttachTray(tray);
            await tray.RefreshAsync();
            Check(tray.Available, "Native tray exporter is initialized on the Linux session bus");
            var menu = Field<TrayIcon>(tray, "_icon").Menu!;
            Check(menu.Items.OfType<NativeMenuItem>().Any(i => i.Header == "Open Wallboard")
                && menu.Items.OfType<NativeMenuItem>().Any(i => i.Header == "Exit"), "Tray menu exposes restore, Wallboard, and exit operations");
            var prefs = engine.Snapshot().Settings;
            prefs.MinimizeToTray = true;
            engine.ApplySettings(prefs);
            engine.StartMonitoring();
            main.WindowState = WindowState.Minimized;
            await tray.MinimizeAsync();
            await Task.Delay(150);
            Console.WriteLine($"Tray minimize state: available={tray.Available}, preference={engine.Config.MinimizeToTray}, visible={main.IsVisible}, state={main.WindowState}, monitoring={engine.Monitoring}");
            Check(!main.IsVisible && engine.Monitoring, "Minimize to tray hides the window while monitoring continues");
            hostAvailable = false;
            await tray.RefreshAsync();
            Check(main.IsVisible && main.WindowState == WindowState.Normal && engine.Monitoring, "Loss of tray host restores a reachable monitoring window");
            main.WindowState = WindowState.Minimized;
            await tray.MinimizeAsync();
            Check(main.IsVisible, "Missing tray host uses normal taskbar minimize");
            tray.Restore();
            Check(main.IsVisible && main.WindowState == WindowState.Normal, "Tray restore opens the main window");
            engine.StopMonitoring();
        }
        using var reloaded = new WatchdogEngine(Path.GetDirectoryName(engine.ConfigPath)!);
        Check(reloaded.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts.Contains("127.0.0.6"), "UI host edits survive restart");
    }

    private static async Task CheckHostPanelLayoutAsync(MainWindow main, TextBox editor)
    {
        double width = main.Width, height = main.Height;
        var heading = Field<TextBlock>(main, "_siteHeader");
        foreach (var size in new[] { new Size(width, height), new Size(1060, 740), new Size(main.MinWidth, main.MinHeight) })
        {
            main.Width = size.Width;
            main.Height = size.Height;
            await Task.Delay(150);
            var titlePoint = heading.TranslatePoint(new Point(), main)!.Value;
            var editorPoint = editor.TranslatePoint(new Point(), main)!.Value;
            var card = editor.GetVisualAncestors().OfType<Border>().First();
            var cardPoint = card.TranslatePoint(new Point(), main)!.Value;
            Check(heading.Bounds.Height >= heading.DesiredSize.Height - 1 && editorPoint.Y >= titlePoint.Y + heading.Bounds.Height,
                $"Host panel reserves the full heading and gap at {size.Width}x{size.Height}");
            Check(editorPoint.Y + editor.Bounds.Height <= cardPoint.Y + card.Bounds.Height - card.Padding.Bottom + 1,
                $"Host editor stays inside its card at {size.Width}x{size.Height}");
            Check(editor.Bounds.Height >= 60, "Host editor retains its usable height");
            SavePreview(main, $"main-{(int)size.Width}x{(int)size.Height}");
        }
        main.Width = width;
        main.Height = height;
        await Task.Delay(150);
    }

    private static async Task RunWallboardParityAsync(WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        var originalSettings = engine.Snapshot().Settings;
        string originalHosts = string.Join(Environment.NewLine, engine.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts);
        engine.SelectedSite = "Alpha";
        var wall = new WallboardWindow(engine, updates, main);
        wall.Show();
        await Task.Delay(150);
        Invoke(wall, "ToggleOperations");
        await Task.Delay(100);
        Check(Field<Border>(wall, "_opsCard").IsVisible && Field<Border>(wall, "_reportsCard").IsVisible,
            "Wallboard operations drawer occupies its own grid cell and retains the reports");
        Check(wall.GetVisualDescendants().OfType<Image>().Any(i => ReferenceEquals(i.Source, BrandAssets.Logo)), "Wallboard displays the embedded Watchdog dog");
        Check(wall.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Import Config...")
            && wall.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Export Config..."), "Wallboard offers shared native config actions");
        engine.SaveHosts("Alpha", originalHosts + "\n" + string.Join("\n", Enumerable.Range(10, 15).Select(i => $"127.0.0.{i}")));
        Invoke(wall, "Refresh");
        var list = Field<ListBox>(wall, "_hostList");
        list.SelectedIndex = 0;
        var row = list.SelectedItem;
        var source = list.ItemsSource;
        Check(list.ItemCount == 17, "Wallboard live list includes hosts beyond the twelve topology nodes");
        var compactHost = (Border)list.ItemTemplate!.Build(row)!;
        Check(compactHost.Child is Grid,
            "Wallboard host rows render as a column table");
        var topology = Field<Canvas>(wall, "_topology");
        Check(topology.Children.OfType<TextBlock>().Any(t => t.Text!.StartsWith("+5 more hosts")), "Wallboard topology reports hidden host count");
        Check(topology.Children.OfType<TextBlock>().Any(t => t.Text == "Alpha") && !topology.Children.OfType<TextBlock>().Any(t => t.Text == "Beta"), "Topology respects selected site scope");
        await Task.Delay(100);
        Check(topology.Children.OfType<TextBlock>().All(t => Canvas.GetLeft(t) >= 0 && Canvas.GetLeft(t) + t.Width <= topology.Width), "Topology labels stay within their scrollable canvas");
        EmitWallboardPreview(wall, "operations");
        Field<NumericUpDown>(wall, "_interval").Value = 23;
        await Task.Delay(850);
        Check(ReferenceEquals(source, list.ItemsSource) && ReferenceEquals(row, list.SelectedItem), "Wallboard live refresh preserves rows and keyed selection");
        Check(Field<NumericUpDown>(wall, "_interval").Value == 23, "Wallboard refresh preserves pending timing edits");
        await CompleteTextDialogAsync(wall, "EditLabelAsync", "Edit Host Label", "Wallboard router");
        Check(engine.SiteDefinitions().Single(s => s.Name == "Alpha").Labels["127.0.0.1"] == "Wallboard router", "Wallboard labels save the selected host");
        Check(ReferenceEquals(row, list.SelectedItem) && ((HostRow)row!).Text.Contains("Wallboard router"), "Wallboard label edits update selected row in place");
        await (Task)Invoke(wall, "ClearLabelAsync")!;
        Check(!engine.SiteDefinitions().Single(s => s.Name == "Alpha").Labels.ContainsKey("127.0.0.1"), "Wallboard clear label removes the label");
        Check((bool)Invoke(wall, "ApplyTiming")! && engine.Config.PingIntervalSeconds == 23, "Wallboard applies timing settings");
        Invoke(wall, "ToggleMonitoring");
        Check(engine.Monitoring && !Field<NumericUpDown>(wall, "_timeout").IsEnabled, "Wallboard locks timing while monitoring");
        Invoke(wall, "ToggleMonitoring");
        Check(!engine.Monitoring && Field<NumericUpDown>(wall, "_timeout").IsEnabled, "Wallboard stop restores timing controls");
        await CompleteTextDialogAsync(wall, "AddSiteAsync", "Add Site", "Wallboard fixture");
        Check(engine.SelectedSite == "Wallboard fixture", "Wallboard Add Site selects the new site");
        Field<TextBox>(wall, "_hosts").Text = "127.0.0.90";
        await CompleteTextDialogAsync(wall, "RenameSiteAsync", "Rename Site", "Wallboard renamed");
        Check(engine.SelectedSite == "Wallboard renamed" && engine.SiteDefinitions().Single(s => s.Name == "Wallboard renamed").Hosts.Contains("127.0.0.90"), "Wallboard rename commits pending hosts and retains site identity");
        var deleteTask = (Task)Invoke(wall, "DeleteSiteAsync")!;
        await Task.Delay(100);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var confirm = lifetime.Windows.Single(w => w.Title == "Delete Site");
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Yes")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await deleteTask;
        Check(!engine.SiteDefinitions().Any(s => s.Name == "Wallboard renamed"), "Wallboard delete removes the confirmed site");
        engine.SelectedSite = null;
        Invoke(wall, "Refresh");
        Check(!Field<Button>(wall, "_rename").IsEnabled && !Field<Button>(wall, "_delete").IsEnabled && Field<TextBox>(wall, "_hosts").IsReadOnly,
            "Wallboard All Sites disables site-specific destructive actions and host editor");
        Field<ComboBox>(wall, "_range").SelectedIndex = 3;
        Field<CheckBox>(wall, "_hideSuspects").IsChecked = false;
        Check(Field<TextBlock>(wall, "_historySummary").Text!.Contains("301 matching / 301 stored"), "Wallboard recent-history counts use all stored events");
        Field<CheckBox>(wall, "_hideSuspects").IsChecked = true;
        Check(Field<TextBlock>(wall, "_historySummary").Text!.Contains("300 matching"), "Wallboard suspect filter applies consistently");
        var snapshot = engine.Snapshot();
        var fixture = snapshot with { Hosts = new[] {
            snapshot.Hosts[0] with { State = HostState.Offline, OutageStarted = DateTime.Now.AddMinutes(-3), Label = "Offline fixture" },
            snapshot.Hosts[1] with { State = HostState.Suspect, Failures = 2, Label = "Suspect fixture" }
        } };
        wall.GetType().GetMethod("RefreshReports", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(wall, new object[] { fixture });
        Check(Field<TextBlock>(wall, "_outageSummary").Text == "1 offline · 1 suspect"
            && Field<TextBlock>(wall, "_activeOutages").Text!.Contains("Down for 3m"), "Wallboard active outages include live duration and suspect state");
        Invoke(wall, "ToggleOperations");
        await Task.Delay(100);
        EmitWallboardPreview(wall, "reports");
        Invoke(wall, "ToggleOperations");
        Check(WallboardWindow.NextScreenIndex(2, 3) == 0 && WallboardWindow.NextScreenIndex(0, 1) == 0, "Monitor cycling wraps and supports a single display");
        await (Task)Invoke(wall, "MoveToNextScreenAsync")!;
        Check(wall.WindowState == WindowState.FullScreen, "Single-display monitor action preserves fullscreen");
        // Global shortcuts must leave typed input and modifiers alone.
        var input = Field<TextBox>(wall, "_hosts");
        input.BringIntoView();
        await Task.Delay(80);
        Check(input.Focus(), "Wallboard editor receives focus for shortcut tests");
        bool cli = Field<bool>(wall, "_showCli");
        foreach (var key in new[] { Key.C, Key.P, Key.H, Key.S, Key.O, Key.M, Key.Escape })
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Check(wall.IsVisible && !engine.Monitoring && Field<bool>(wall, "_showCli") == cli, "Wallboard input owns all shortcut keystrokes including Escape");
        Field<Button>(wall, "_startStop").Focus();
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
        Check(Field<bool>(wall, "_showCli") == cli, "Wallboard Ctrl+C does not trigger CLI shortcut");
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.H, KeyModifiers = KeyModifiers.Control });
        await Task.Delay(100);
        var shortcutHistory = lifetime.Windows.Single(w => w is HistoryWindow);
        Check(shortcutHistory.IsVisible, "Wallboard Ctrl+H opens full History");
        shortcutHistory.Close();
        wall.Activate(); Field<Button>(wall, "_startStop").Focus();
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.OemComma, KeyModifiers = KeyModifiers.Control });
        await Task.Delay(100);
        var shortcutSettings = lifetime.Windows.Single(w => w is SettingsWindow);
        Check(shortcutSettings.IsVisible, "Wallboard Ctrl+comma opens Settings");
        shortcutSettings.Close();
        wall.Activate(); Field<Button>(wall, "_startStop").Focus();
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C });
        Check(engine.Config.WallboardShowCli != cli, "Wallboard CLI shortcut persists preference");
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.H });
        Check(engine.Config.EventHistoryHours == 24, "Wallboard history-range shortcut cycles filters");
        wall.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S });
        Check(!engine.Config.HideSuspectEvents, "Wallboard suspect shortcut changes history filter");
        wall.Close();
        engine.SaveHosts("Alpha", originalHosts);
        engine.ApplySettings(originalSettings);
        engine.SelectedSite = "Alpha";
    }

    private static async Task CompleteTextDialogAsync(WallboardWindow wall, string method, string title, string text)
    {
        var task = (Task)Invoke(wall, method)!;
        await Task.Delay(100);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var dialog = lifetime.Windows.Single(w => w.Title == title);
        dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = text;
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "OK")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await task;
    }

    private static void EmitWallboardPreview(WallboardWindow wall, string name) => SavePreview(wall, "wallboard-" + name);

    private static void SavePreview(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WATCHDOG_UI_PREVIEW") != "1") return;
        Directory.CreateDirectory("ui-regression-images");
        double scale = window.RenderScaling;
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        using var stream = File.Create(Path.Combine("ui-regression-images", name + ".png"));
        bitmap.Save(stream);
    }

    private static async Task CheckDesktopPresentationAsync(WatchdogEngine engine, LinuxUpdateService updates, MainWindow main)
    {
        Check(main.GetVisualDescendants().OfType<Image>().Any(i => ReferenceEquals(i.Source, BrandAssets.Logo) && i.Bounds.Width == 42), "Main uses the shared dog branding at the Windows size");
        var timeout = Field<NumericUpDown>(main, "_timeout");
        decimal? original = timeout.Value;
        timeout.Value = 10000;
        await Task.Delay(50);
        var text = timeout.GetVisualDescendants().OfType<TextBox>().Single();
        Check(text.Text == "10000" && text.Bounds.Width >= 55, "Numeric field shows its full maximum value");
        timeout.GetVisualDescendants().OfType<Avalonia.Controls.RepeatButton>().Single(b => b.Name == "PART_DecreaseButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(timeout.Value == 9750, "Numeric down button changes the value");
        text.Text = "2000";
        timeout.GetVisualDescendants().OfType<Avalonia.Controls.RepeatButton>().Single(b => b.Name == "PART_IncreaseButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(timeout.Value == 2250, "Numeric field accepts a typed value before spinning");
        timeout.Value = original;

        var settings = new SettingsWindow(engine, updates, main);
        settings.Show(main);
        await Task.Delay(100);
        var navigation = settings.GetVisualDescendants().OfType<ListBox>().Single();
        Check(navigation.ItemCount == 4 && navigation.Items.Cast<string>().SequenceEqual(PingWatchdog.Shared.Presentation.SettingsPages), "Settings uses Windows page navigation");
        foreach (var size in new[] { new Size(900, 650), new Size(760, 540) })
        {
            settings.Width = size.Width; settings.Height = size.Height;
            foreach (int page in Enumerable.Range(0, 4))
            {
                navigation.SelectedIndex = page;
                await Task.Delay(70);
                var save = settings.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Save Settings");
                var position = save.TranslatePoint(new Point(), settings)!.Value;
                Check(position.Y >= 0 && position.Y + save.Bounds.Height <= settings.Bounds.Height, $"Settings footer is reachable on page {page} at {size}");
                SavePreview(settings, $"settings-{page}-{size.Width}x{size.Height}");
            }
        }
        settings.Close();

        var history = new HistoryWindow(engine); history.Show(main);
        await Task.Delay(100);
        foreach (var size in new[] { new Size(1080, 650), new Size(820, 480) })
        {
            history.Width = size.Width; history.Height = size.Height;
            await Task.Delay(80);
            Check(Field<ListBox>(history, "_events").Bounds.Height > 100, $"History retains usable rows at {size}");
            SavePreview(history, $"history-{size.Width}x{size.Height}");
        }
        history.Close();
        engine.AddFolder("", "Organization fixture"); engine.AddFolder("Organization fixture", "Nested");
        engine.MoveSite("Beta", "Organization fixture/Nested");
        var organization = new OrganizationWindow(engine); organization.Show(main);
        await Task.Delay(100);
        var tree = Field<TreeView>(organization, "_items");
        var nested = tree.Items.OfType<TreeViewItem>().Single(i => i.Header?.ToString() == "Organization fixture").Items.OfType<TreeViewItem>().Single();
        Check(nested.Items.OfType<TreeViewItem>().Single().Header!.ToString()!.StartsWith("Beta"), "Organization uses a true folder/site hierarchy");
        tree.SelectedItem = nested.Items[0];
        engine.SetLabel("Beta", "127.0.0.2", "Organization label");
        await Task.Delay(100);
        Check((tree.SelectedItem as TreeViewItem)?.Header?.ToString()?.StartsWith("Beta") == true, "Organization refresh preserves selected site");
        foreach (var size in new[] { new Size(760, 620), new Size(620, 460) })
        {
            organization.Width = size.Width; organization.Height = size.Height;
            await Task.Delay(80); Check(tree.Bounds.Height > 150, $"Organization tree remains usable at {size}");
            SavePreview(organization, $"organization-{size.Width}x{size.Height}");
        }
        organization.Close();
        engine.MoveSite("Beta", ""); engine.DeleteFolder("Organization fixture/Nested"); engine.DeleteFolder("Organization fixture"); engine.SetLabel("Beta", "127.0.0.2", "");
        engine.SelectedSite = "Alpha";
    }

    private static object? Invoke(object target, string name) => target.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);

    private static T Field<T>(object target, string name) => (T)(target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target)
        ?? throw new InvalidOperationException("Missing regression field: " + name));

    private static int SubscriberCount(object target, string name) =>
        (target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) as Delegate)
            ?.GetInvocationList().Length ?? 0;

    private static void Check(bool condition, string name) => LinuxRegressionTests.Check(condition, name);
}
