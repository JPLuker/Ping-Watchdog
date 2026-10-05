using System.Diagnostics;

namespace PingWatchdog;

/// <summary>Early guard and short-lived observer, independent of MainForm and Windows App SDK.</summary>
internal static class StartupRecovery
{
    internal const string ReleasesUrl = "https://github.com/JPLuker/Ping-Watchdog/releases/latest";
    private static string? _attempt;
    private static int _reported;

    internal static bool HandleMode(string[] args)
    {
        if (args.Length == 3 && args[0] == "--startup-watch" && int.TryParse(args[1], out int pid))
        {
            Watch(pid, args[2]);
            return true;
        }
        if (args.Length == 2 && args[0] == "--startup-recovery")
        {
            RunRecovery(args[1]);
            return true;
        }
        if (args.Contains("--recovery-smoke-test"))
        {
            RecoveryRegressionTests.Run();
            return true;
        }
        if (args.Length == 3 && args[0] == "--startup-failure-test")
        {
            RunGuarded(() => RecoveryRegressionTests.InjectFailure(args[1]), args[2]);
            return true;
        }
        return false;
    }

    internal static void RunGuarded(Action run, string? testDirectory = null)
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingWatchdog", "Startup");
            _attempt = testDirectory ?? Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_attempt);
            File.WriteAllText(Path.Combine(_attempt, "startup.txt"),
                $"Ping Watchdog {typeof(Program).Assembly.GetName().Version}\r\nStarted: {DateTimeOffset.Now:O}\r\n" +
                $"Windows: {Environment.OSVersion}\r\nProcess: {Environment.ProcessId}\r\n");
            if (testDirectory is not null)
                File.WriteAllText(Path.Combine(_attempt, "test-mode.txt"), "diagnostics only");
            using var observer = StartProcess("--startup-watch", Environment.ProcessId.ToString(), _attempt);
        }
        catch
        {
            // An unavailable log directory or observer must not stop normal monitoring.
        }

        UnhandledExceptionEventHandler backgroundFailure = (_, e) =>
        {
            Report(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            // Do not leave a fatal background exception waiting behind Windows error reporting.
            Environment.Exit(70);
        };
        AppDomain.CurrentDomain.UnhandledException += backgroundFailure;
        try
        {
            run();
            WriteMarker("completed.txt");
        }
        catch (Exception ex)
        {
            Report(ex);
            Environment.ExitCode = 70;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= backgroundFailure;
        }
    }

    internal static void MarkReady() => WriteMarker("ready.txt");
    internal static void ExpectUpdateExit()
    {
        WriteMarker("expected-exit.txt");
    }
    internal static void CancelExpectedExit()
    {
        try { if (_attempt is not null) File.Delete(Path.Combine(_attempt, "expected-exit.txt")); } catch { }
    }

    private static void WriteMarker(string name)
    {
        try
        {
            if (_attempt is null) return;
            Directory.CreateDirectory(_attempt);
            File.WriteAllText(Path.Combine(_attempt, name), DateTimeOffset.Now.ToString("O"));
        }
        catch { }
    }

    private static void Report(Exception error)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;
        CancelExpectedExit();
        try
        {
            if (_attempt is null)
            {
                _attempt = Path.Combine(Path.GetTempPath(), "PingWatchdog-recovery-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_attempt);
            }
            File.WriteAllText(Path.Combine(_attempt, "error.txt"), error.ToString());
            LaunchRecoveryOnce(_attempt);
        }
        catch
        {
            MessageBox.Show($"Ping Watchdog could not continue.\r\n\r\n{error.Message}\r\n\r\n" +
                $"Download an updated installer at:\r\n{ReleasesUrl}", "Ping Watchdog startup failure",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Watch(int pid, string directory)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var timeout = Stopwatch.StartNew();
            while (!process.WaitForExit(100))
            {
                if (File.Exists(Path.Combine(directory, "ready.txt")) || timeout.Elapsed > TimeSpan.FromMinutes(2))
                    return;
            }
            if (File.Exists(Path.Combine(directory, "ready.txt")) ||
                File.Exists(Path.Combine(directory, "completed.txt")) ||
                File.Exists(Path.Combine(directory, "expected-exit.txt")))
                return;
            if (!File.Exists(Path.Combine(directory, "error.txt")))
                File.WriteAllText(Path.Combine(directory, "error.txt"),
                    $"Ping Watchdog exited before its main window became responsive. Exit code: {process.ExitCode}.\r\n" +
                    "Windows did not provide a managed exception for this exit.");
            LaunchRecoveryOnce(directory);
        }
        catch (ArgumentException)
        {
            // The application may exit before the observer obtains its process handle.
            if (!File.Exists(Path.Combine(directory, "completed.txt")) &&
                !File.Exists(Path.Combine(directory, "expected-exit.txt")) &&
                !File.Exists(Path.Combine(directory, "ready.txt")))
                LaunchRecoveryOnce(directory);
        }
        catch { /* The main exception guard remains available if observation fails. */ }
        finally
        {
            try { File.WriteAllText(Path.Combine(directory, "watcher-complete.txt"), "done"); } catch { }
        }
    }

    private static void LaunchRecoveryOnce(string directory)
    {
        // The exception guard and observer can notice the same crash simultaneously.
        try
        {
            using var claim = new FileStream(Path.Combine(directory, "recovery-claimed.txt"), FileMode.CreateNew);
        }
        catch (IOException) when (File.Exists(Path.Combine(directory, "recovery-claimed.txt"))) { return; }
        try { using var recovery = StartProcess("--startup-recovery", directory); }
        catch
        {
            File.Delete(Path.Combine(directory, "recovery-claimed.txt"));
            throw;
        }
    }

    internal static Process StartProcess(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the Ping Watchdog executable."))
        { UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Cannot start recovery.");
    }

    private static void RunRecovery(string directory)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ApplicationConfiguration.Initialize();
            string header = ReadIfPresent(Path.Combine(directory, "startup.txt"));
            string error = ReadIfPresent(Path.Combine(directory, "error.txt"));
            if (string.IsNullOrWhiteSpace(error))
                error = "Ping Watchdog stopped before startup completed. No exception details were available.";
            string report = header + "\r\n" + error;
            File.WriteAllText(Path.Combine(directory, "report.txt"), report);
            using var form = new RecoveryForm(report, Path.Combine(directory, "report.txt"));
            if (File.Exists(Path.Combine(directory, "test-mode.txt")))
                RecoveryRegressionTests.ExerciseRecovery(form, directory);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            if (File.Exists(Path.Combine(directory, "test-mode.txt")))
                File.WriteAllText(Path.Combine(directory, "recovery-failed.txt"), ex.ToString());
            else
                MessageBox.Show($"Recovery could not open: {ex.Message}\r\n\r\nDownload the installer at:\r\n{ReleasesUrl}",
                    "Ping Watchdog recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string ReadIfPresent(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
