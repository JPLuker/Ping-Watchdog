namespace PingWatchdog;

// Uses native Windows forms without network, clipboard or saved-data changes.
internal static class DialogReviewTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    internal static void Escape(Form form)
    {
        var method = typeof(Form).GetMethod("ProcessDialogKey",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Check((bool)method.Invoke(form, new object[] { Keys.Escape })! && form.IsDisposed,
            form.Text + ": Escape failed to close the dialog.");
    }

    private static void Capture(Form form, string output, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(output, "dialog-" + name + "-" + AuroraTheme.Mode + ".png"));
    }

    internal static async Task Run(Form owner, string output)
    {
        using (var review = SupportInfo.CreateReview("Diagnostic information",
            string.Join(Environment.NewLine, Enumerable.Repeat("Long diagnostic path: C:\\Users\\Example\\AppData\\Local\\PingWatchdog", 40)),
            true, _ => throw new IOException("Clipboard unavailable")))
        {
            review.Show(owner);
            review.Size = review.MinimumSize;
            await Task.Delay(50);
            var text = Descendants(review).OfType<TextBox>().Single();
            Check(text.Focused && review.Owner == owner, "Diagnostic review lost its initial focus or owner.");
            var copy = Descendants(review).OfType<Button>().Single(b => b.Text == "Copy diagnostic info");
            Check(review.SelectNextControl(text, true, true, true, false) && copy.Focused,
                "Diagnostic text does not tab to Copy.");
            copy.PerformClick();
            await Task.Delay(50);
            CompactDialog.CheckBounds(review);
            var status = Descendants(review).Single(c => c.AccessibleName == "Copy status");
            Check(status.Text.StartsWith("Could not copy") && status.Parent!.ClientRectangle.Contains(status.Bounds),
                "Copy error is missing or clipped.");
            Check(text.Height >= 100, "Copy error consumed the diagnostic review area.");
            Capture(review, output, "diagnostics-error");
            Escape(review);
        }
        using (var license = SupportInfo.CreateReview("License", SupportInfo.ReadLicense(), false))
        {
            license.Show(owner);
            license.Size = license.MinimumSize;
            await Task.Delay(50);
            CompactDialog.CheckBounds(license);
            Capture(license, output, "license");
            Escape(license);
        }
        using (var recovery = new RecoveryForm("Original exception details", "report.txt", new NoUpdates()))
        {
            recovery.Show(owner);
            recovery.Size = recovery.MinimumSize;
            await Task.Delay(50);
            Check(recovery.Details.Focused, "Recovery does not focus the error details.");
            Check(recovery.SelectNextControl(recovery.Details, true, true, true, false) && recovery.StatusLabel.Focused,
                "Recovery details do not tab to the status text.");
            recovery.StatusLabel.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Update failed: " + new string('x', 180), 20));
            await Task.Delay(50);
            CompactDialog.CheckBounds(recovery);
            Check(recovery.Details.Height >= 60 && recovery.StatusLabel.ScrollBars == ScrollBars.Vertical,
                "Long recovery errors hide details or cannot be scrolled.");
            Check(recovery.Details.Text == "Original exception details", "Recovery status replaced the original exception.");
            Capture(recovery, output, "recovery-long-error");
            Escape(recovery);
        }
    }

    private sealed class NoUpdates : IRecoveryUpdates
    {
        public Task<string?> CheckAsync(CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task DownloadAsync(Action<int> progress, CancellationToken cancellation) => throw new InvalidOperationException();
        public void Apply() => throw new InvalidOperationException();
    }
}
