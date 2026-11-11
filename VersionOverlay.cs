using System.Reflection;

namespace PingWatchdog;

// VERSIONING.md 4.1: Settings → Advanced → "Show version on all screens", for testing builds and reading
// screenshots. Off by default. A small pill in the bottom-left corner of every Ping Watchdog window,
// including dialogs and Settings. It never takes clicks (hit-test transparent) and never takes focus.
internal sealed class VersionOverlay : Control
{
    internal static bool Enabled { get; private set; }

    // "1.19.29 · ae6bc0a": version plus the short commit the SDK adds to the informational version.
    internal static string Label => Format(typeof(VersionOverlay).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(VersionOverlay).Assembly.GetName().Version?.ToString(3) ?? "");

    internal static string Format(string informational)
    {
        var parts = informational.Split('+', 2);
        string version = parts[0];
        string commit = parts.Length > 1 ? parts[1][..Math.Min(7, parts[1].Length)] : "";
        return commit.Length > 0 ? $"{version} · {commit}" : version;
    }

    internal VersionOverlay()
    {
        Name = "VersionOverlay";
        Text = Label;
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Version " + Text;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = AppTheme.CaptionFont;
    }

    internal static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray()) Attach(form);
    }

    // Adds or removes the pill to match the setting. Safe to call repeatedly.
    internal static void Attach(Form form)
    {
        if (form.IsDisposed) return;
        var existing = form.Controls.OfType<VersionOverlay>().FirstOrDefault();
        if (!Enabled)
        {
            if (existing is not null) { form.Controls.Remove(existing); existing.Dispose(); }
            return;
        }
        if (existing is null)
        {
            existing = new VersionOverlay();
            form.Controls.Add(existing);
            form.Resize += (_, _) => existing.Place();
        }
        existing.Place();
    }

    private void Place()
    {
        if (Parent is not Form form || IsDisposed) return;
        float scale = DeviceDpi / 96f;
        var text = TextRenderer.MeasureText(Text, Font);
        Size = new Size(text.Width + (int)(16 * scale), text.Height + (int)(6 * scale));
        Location = new Point((int)(8 * scale), form.ClientSize.Height - Height - (int)(8 * scale));
        BringToFront();
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        const int HitTest = 0x84, Transparent = -1;
        if (m.Msg == HitTest) { m.Result = (IntPtr)Transparent; return; }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = AppTheme.Palette;
        e.Graphics.Clear(Parent?.BackColor ?? p.Canvas);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var shape = WallboardStyle.Rounded(bounds, bounds.Height / 2);
        using (var fill = new SolidBrush(p.Surface2)) e.Graphics.FillPath(fill, shape);
        using (var edge = new Pen(p.Border)) e.Graphics.DrawPath(edge, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, p.Secondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    internal static void RunTests()
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Version overlay: " + message); }
        Check(Format("1.19.29+ae6bc0a0e09dbf9c08f6e2394553b9e5f5313e1a") == "1.19.29 · ae6bc0a", "commit hash format");
        Check(Format("1.19.29") == "1.19.29", "version without a commit");
        using var form = new Form { ClientSize = new Size(400, 300) };
        bool was = Enabled;
        try
        {
            Enabled = true; Attach(form); Attach(form);
            var overlay = form.Controls.OfType<VersionOverlay>().ToList();
            Check(overlay.Count == 1, "attached more than once");
            Check(overlay[0].Bottom <= form.ClientSize.Height && overlay[0].Left >= 0, "not in the bottom-left corner");
            Check(!overlay[0].TabStop, "takes keyboard focus");
            Enabled = false; Attach(form);
            Check(!form.Controls.OfType<VersionOverlay>().Any(), "stayed after turning off");
        }
        finally { Enabled = was; }
    }
}
