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
        var editor = Field<TextBox>(main, "_hostEditor");
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
