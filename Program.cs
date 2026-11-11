using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Velopack;
using Velopack.Sources;
using PingWatchdog.Shared;

namespace PingWatchdog;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Keep the entry point free of dashboard and notification initialization so
        // a fresh recovery process can start even when those components fail.
        if (StartupRecovery.HandleMode(args))
            return;

        if (args.Any(arg => arg is "--veloapp-install" or "--veloapp-obsolete" or "--veloapp-updated" or "--veloapp-uninstall"))
        {
            InitializeUpdater(args);
            return;
        }

        if (args.Contains("--self-test") || args.Contains("--ui-smoke-test"))
        {
            RunTests(args);
            return;
        }

        StartupRecovery.RunGuarded(() => RunApplication(args));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void InitializeUpdater(string[] args)
    {
        // Applying a staged package is an explicit action, including in recovery.
        VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false)
            // Uninstall also removes the "open at sign-in" entry so Windows isn't left pointing at a missing app.
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { StartupRegistration.Set(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            })
            .Run();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunTests(string[] args)
    {
        InitializeUpdater(args);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--ui-smoke-test"))
                MainForm.RunUiSmokeTest();
            else
            {
                UpdateIntegrityTests.RunAsync().GetAwaiter().GetResult();
                MainForm.RunSelfTests();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunApplication(string[] args)
    {
        InitializeUpdater(args);
        if (DataDeletionStore.FinishPending(StoragePaths.Current)) StartupRecovery.ResumeAfterDeletion();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        var notificationsRegistered = false;

        try
        {
            BrandAssets.EnsureShellAssets();
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register("Ping Watchdog", new Uri(BrandAssets.NotificationIconPath));
            notificationsRegistered = true;
        }
        catch
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        }

        try
        {
            WindowsBranding.Refresh();
            using var form = new MainForm(notificationsRegistered);
            using var readyTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            form.Shown += (_, _) => readyTimer.Start();
            readyTimer.Tick += (_, _) =>
            {
                readyTimer.Stop();
                StartupRecovery.MarkReady();
            };
            Application.Run(form);
        }
        finally
        {
            if (notificationsRegistered)
            {
                try
                {
                    AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                    AppNotificationManager.Default.Unregister();
                }
                catch { /* Notification cleanup must not hide the original failure. */ }
            }
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        var form = Application.OpenForms.Count > 0
            ? Application.OpenForms[0] as MainForm
            : null;
        // Copy now: the arguments belong to the notification thread.
        var arguments = new Dictionary<string, string>(args.Arguments);
        form?.BeginInvoke((Action)(() => form.HandleNotificationActivation(arguments)));
    }
}
