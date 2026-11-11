namespace PingWatchdog;

// Measured design checks (standards DESIGN.md A10 and SOP 7.2): run against real windows in the UI smoke
// test so the checklist is measured, not ticked. Each returns the problems found; empty means it passes.
internal static class DesignChecks
{
    internal static List<string> Run(Form window)
    {
        var problems = new List<string>();
        window.PerformLayout();
        var visible = DialogReviewTests.Descendants(window).Where(c => c != window && IsShown(c)).ToList();
        float scale = window.DeviceDpi / 96f;

        // A1.1: one focal point. At most one primary action per window.
        int primaries = visible.OfType<ThemeButton>().Count(b => b.Primary && !b.Destructive);
        if (primaries > 1) problems.Add($"{window.Text}: {primaries} primary buttons.");

        // A8: targets at least 24x24 on desktop.
        foreach (var control in visible.Where(c => c is ButtonBase))
            if (control.Width < 24 * scale - 0.5 || control.Height < 24 * scale - 0.5)
                problems.Add($"{window.Text}: '{control.Text}' is {control.Width}x{control.Height}.");

        // A8: nothing below 12px; text people read is at least 13px.
        foreach (var control in visible.Where(c => c is Label or ButtonBase or TextBoxBase or ComboBox))
            if (control.Font.SizeInPoints < 9f)
                problems.Add($"{window.Text}: '{control.Text}' uses {control.Font.SizeInPoints:0.##}pt.");

        // A8: text contrast 4.5:1 against what's actually behind it.
        foreach (var label in visible.OfType<Label>().Where(l => l.Text.Length > 0 && l.Enabled))
        {
            var background = EffectiveBackground(label);
            double ratio = AppTheme.ContrastRatio(label.ForeColor, background);
            if (ratio < 4.5) problems.Add($"{window.Text}: '{label.Text}' contrast {ratio:0.00}:1.");
        }
        return problems;
    }

    private static bool IsShown(Control control)
    {
        for (Control? c = control; c != null; c = c.Parent) if (!c.Visible) return false;
        return true;
    }

    internal static Color EffectiveBackground(Control control)
    {
        for (Control? c = control; c != null; c = c.Parent)
            if (c.BackColor.A == 255) return c.BackColor;
        return AppTheme.Palette.Canvas;
    }

    // Proves each check can fail (SOP 7.4).
    internal static void RunTests()
    {
        using var form = new Form { Text = "Design check test", ClientSize = new Size(400, 200) };
        var p = AppTheme.Palette;
        form.BackColor = p.Canvas;
        form.Controls.Add(new ThemeButton { Text = "One", Primary = true, Location = new Point(10, 10) });
        form.Controls.Add(new ThemeButton { Text = "Two", Primary = true, Location = new Point(10, 60) });
        form.Controls.Add(new Label { Text = "Faint", ForeColor = Color.FromArgb(235, 235, 235), BackColor = Color.White, Location = new Point(200, 10), AutoSize = true });
        form.Controls.Add(new Label { Text = "Tiny", Font = new Font("Segoe UI", 7f), ForeColor = p.Text, Location = new Point(200, 60), AutoSize = true });
        form.Controls.Add(new Button { Text = "", Size = new Size(12, 12), Location = new Point(200, 120) });
        form.Show();
        try
        {
            var problems = Run(form);
            void Expect(string what) { if (!problems.Any(x => x.Contains(what))) throw new InvalidOperationException("Design checks missed: " + what); }
            Expect("2 primary buttons"); Expect("'Faint' contrast"); Expect("'Tiny' uses"); Expect("is 12x12");
        }
        finally { form.Close(); }
        Console.WriteLine("Design check tests passed.");
    }
}
