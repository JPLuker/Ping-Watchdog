using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

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
        main.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
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
        using var reloaded = new WatchdogEngine(Path.GetDirectoryName(engine.ConfigPath)!);
        Check(reloaded.SiteDefinitions().Single(s => s.Name == "Alpha").Hosts.Contains("127.0.0.6"), "UI host edits survive restart");
    }

    private static T Field<T>(object target, string name) => (T)(target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target)
        ?? throw new InvalidOperationException("Missing regression field: " + name));

    private static int SubscriberCount(object target, string name) =>
        (target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) as Delegate)
            ?.GetInvocationList().Length ?? 0;

    private static void Check(bool condition, string name) => LinuxRegressionTests.Check(condition, name);
}
