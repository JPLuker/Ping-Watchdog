using System.Diagnostics;

namespace PingWatchdog;

internal static class RecoveryRegressionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Run()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "ui-smoke-test", "recovery");
        Directory.CreateDirectory(root);
        try
        {
            foreach (string mode in new[] { "constructor", "ui", "background", "abrupt", "normal", "ready", "update-exit", "deletion-retry" })
            {
                string directory = Path.Combine(root, mode);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                Directory.CreateDirectory(directory);
                using var process = StartupRecovery.StartProcess("--startup-failure-test", mode, directory);
                Check(process.WaitForExit(15000), mode + ": failing process did not exit.");
                bool shouldRecover = mode is not ("normal" or "ready" or "update-exit");
                Check(process.ExitCode == (shouldRecover ? (mode == "abrupt" ? 72 : 70) : 0), mode + ": unexpected exit code.");
                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(15))
                {
                    if (File.Exists(Path.Combine(directory, "recovery-failed.txt")))
                        throw new InvalidOperationException(File.ReadAllText(Path.Combine(directory, "recovery-failed.txt")));
                    if (File.Exists(Path.Combine(directory, "watcher-complete.txt")) &&
                        (!shouldRecover || File.Exists(Path.Combine(directory, "recovery-passed.txt")))) break;
                    Thread.Sleep(100);
                }
                Check(File.Exists(Path.Combine(directory, "watcher-complete.txt")), mode + ": observer did not finish.");
                Check(File.Exists(Path.Combine(directory, "recovery-passed.txt")) == shouldRecover,
                    mode + ": recovery opened unexpectedly or did not open.");
                Check(File.Exists(Path.Combine(directory, "recovery-claimed.txt")) == shouldRecover,
                    mode + ": wrong recovery claim state.");
                if (shouldRecover)
                    Check(File.ReadAllText(Path.Combine(directory, "report.txt")).Contains("Ping Watchdog"),
                        mode + ": error report did not retain app details.");
            }
            foreach (string hook in new[] { "--veloapp-install", "--veloapp-obsolete", "--veloapp-updated", "--veloapp-uninstall" })
            {
                using var process = StartupRecovery.StartProcess(hook, "1.0.0");
                Check(process.WaitForExit(10000) && process.ExitCode == 0,
                    hook + ": Velopack lifecycle hook did not exit normally.");
            }
            File.WriteAllText(Path.Combine(root, "passed.txt"), "Startup failure and recovery update tests passed.");
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(root, "failed.txt"), ex.ToString());
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
    }

    internal static void InjectFailure(string mode)
    {
        if (mode == "deletion-retry")
        {
            string attempt = Path.Combine(Environment.CurrentDirectory, "ui-smoke-test", "recovery", mode);
            Directory.Delete(attempt, recursive: true); // Reproduce cleanup of the guard's current attempt.
            StartupRecovery.ResumeAfterDeletion();
            throw new InvalidOperationException("Injected failure after interrupted deletion recovery.");
        }
        if (mode == "constructor") throw new InvalidOperationException("Injected startup constructor failure.");
        if (mode == "abrupt") Environment.Exit(72);
        if (mode == "ready") { StartupRecovery.MarkReady(); Environment.Exit(0); }
        if (mode == "update-exit") { StartupRecovery.ExpectUpdateExit(); Environment.Exit(0); }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "Startup recovery test", ClientSize = new Size(200, 100) };
        form.Shown += (_, _) =>
        {
            if (mode == "ui")
                form.BeginInvoke(() => throw new InvalidOperationException("Injected first-frame UI failure."));
            else if (mode == "background")
                new Thread(() => throw new InvalidOperationException("Injected background startup failure.")) { IsBackground = true }.Start();
            else
                form.BeginInvoke(form.Close);
        };
        Application.Run(form);
    }

    internal static void ExerciseRecovery(RecoveryForm form, string directory)
    {
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(100);
                Check(form.Visible && form.IsHandleCreated, "Recovery window did not appear.");
                Check(form.CancelButton is Button { Text: "Close", DialogResult: DialogResult.Cancel },
                    "Escape must close the independent recovery window.");
                Check(form.UpdateButton.Visible && form.UpdateButton.Enabled, "Recovery has no usable update action.");
                Check(!string.IsNullOrWhiteSpace(form.Details.Text), "Recovery error details are empty.");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(directory, "recovery.png"));
                }

                // The real updater must gracefully explain why an unpackaged CI EXE cannot self-update.
                await form.UpdateClickedAsync();
                Check(form.UpdateButton.Enabled && !form.UpdateReady && form.StatusLabel.Text.StartsWith("Update failed:"),
                    "Unmanaged recovery copy did not retain usable controls.");

                foreach (string scenario in new[] { "available", "none", "network", "download", "apply" })
                {
                    var updates = new FakeUpdates(scenario);
                    using var test = new RecoveryForm("Original startup exception", "test-report.txt", updates);
                    test.Show(form);
                    await test.UpdateClickedAsync();
                    Check(test.UpdateButton.Enabled, scenario + ": update button remained disabled.");
                    Check(test.Details.Text == "Original startup exception", "Update attempt overwrote crash details.");
                    if (scenario is "available" or "apply")
                    {
                        Check(test.UpdateReady && test.UpdateButton.Text == "Install update and restart",
                            "Downloaded recovery update was not offered.");
                        Check(updates.ApplyCalls == 0, "Recovery installed an update without a second click.");
                        await test.UpdateClickedAsync();
                        Check(updates.ApplyCalls == 1 && test.UpdateButton.Enabled, "Recovery restart action failed.");
                        if (scenario == "apply")
                            Check(test.UpdateReady && test.StatusLabel.Text.StartsWith("Update failed:"),
                                "Failed apply lost the downloaded update or retry action.");
                    }
                    else
                    {
                        Check(!test.UpdateReady, scenario + ": failed/no update was marked ready.");
                        Check(test.StatusLabel.Text.StartsWith(scenario == "none" ? "No newer" : "Update failed:"),
                            scenario + ": missing updater explanation.");
                        await test.UpdateClickedAsync();
                        Check(updates.CheckCalls == 2, scenario + ": user could not retry an update check.");
                    }
                    test.Close();
                }
                // Exercise the recovery adapter and UI with the real pinned verifier, not FakeUpdates.
                using (var fixture = new UpdateIntegrityFixture { Fault = DownloadFault.SameSizeCorruption })
                using (var test = new RecoveryForm("Original startup exception", "test-report.txt",
                    new RecoveryUpdates(() => fixture.Manager)))
                {
                    test.Show(form);
                    await test.UpdateClickedAsync();
                    Check(!test.UpdateReady && test.UpdateButton.Enabled && test.StatusLabel.Text.StartsWith("Update failed:"),
                        "Corrupt recovery download offered installation or lost its retry action.");
                    fixture.CheckNotStaged();
                    Check(test.Details.Text == "Original startup exception", "Corrupt update overwrote crash details.");
                    fixture.Fault = DownloadFault.None;
                    await test.UpdateClickedAsync();
                    await Task.Delay(25); // Flush queued progress; it must not overwrite the ready state.
                    Check(test.UpdateReady && test.UpdateButton.Enabled &&
                        test.StatusLabel.Text.Contains("is ready"),
                        "Valid recovery retry did not retain its install action and ready status.");
                    fixture.CheckValidStaged();
                    Check(fixture.DownloadCalls == 2, "Recovery retry reused the corrupt package.");
                    test.Close();
                }

                // Simulate a transport that ignores cancellation and completes after the window closes.
                foreach (bool delayCheck in new[] { true, false })
                {
                    var delayed = new DelayedUpdates(delayCheck);
                    using var closing = new RecoveryForm("Original startup exception", "test-report.txt", delayed);
                    closing.Show(form);
                    Task pending = closing.UpdateClickedAsync();
                    closing.Close();
                    delayed.DownloadCompletion.TrySetResult(true);
                    delayed.CheckCompletion.TrySetResult("9.9.9");
                    await pending;
                    Check(!closing.UpdateReady, "Closed recovery window accepted a late download.");
                    Check(delayed.DownloadCalls == (delayCheck ? 0 : 1),
                        "Closed recovery window started another download after a late check.");
                }
                File.WriteAllText(Path.Combine(directory, "recovery-passed.txt"),
                    "Recovery success/failure/retry, real checksum rejection, and late completion after close passed.");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(directory, "recovery-failed.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            finally { form.Close(); }
        };
    }

    private sealed class DelayedUpdates(bool delayCheck) : IRecoveryUpdates
    {
        internal readonly TaskCompletionSource<string?> CheckCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> DownloadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DownloadCalls;
        public Task<string?> CheckAsync(CancellationToken cancellation) =>
            delayCheck ? CheckCompletion.Task : Task.FromResult<string?>("9.9.9");
        public Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
        {
            DownloadCalls++;
            return DownloadCompletion.Task;
        }
        public void Apply() => throw new InvalidOperationException("Late-completion test must never apply an update.");
    }

    private sealed class FakeUpdates(string scenario) : IRecoveryUpdates
    {
        internal int CheckCalls;
        internal int ApplyCalls;
        public Task<string?> CheckAsync(CancellationToken cancellation)
        {
            CheckCalls++;
            if (scenario == "network") throw new IOException("Injected unavailable network.");
            return Task.FromResult<string?>(scenario == "none" ? null : "9.9.9");
        }
        public Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
        {
            if (scenario == "download") throw new IOException("Injected download failure.");
            progress(50);
            progress(100);
            return Task.CompletedTask;
        }
        public void Apply()
        {
            ApplyCalls++;
            if (scenario == "apply") throw new IOException("Injected update apply failure.");
        }
    }
}
