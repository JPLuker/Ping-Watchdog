using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PingWatchdog;

// #40: "Sometimes the screen becomes unresponsive for what I assume is background loading, can we have
// some sort of blur with a message that shows the screen is loading to not cause confusion?"
// A watchdog thread asks the window thread to answer every 100 ms. When it hasn't answered for 400 ms,
// a dimmed cover with "Loading" is shown over the active Ping Watchdog window from the watchdog's own
// window thread, so it appears and animates even while the app is busy. It hides as soon as the app answers.
// Waits longer than a second are written to Logs\responsiveness.log with what the app was doing at the
// time, so the cause can be found and fixed. No host or personal data is written.
internal sealed class ResponsivenessGuard : IDisposable
{
    internal static readonly TimeSpan ShowAfter = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan LogAfter = TimeSpan.FromSeconds(1);
    private const long LogLimitBytes = 256 * 1024;

    private readonly Control _window;
    private readonly int _windowThreadId;
    private readonly string _logDirectory;
    private readonly Thread _watchdog;
    private readonly ManualResetEventSlim _stop = new(false);
    private StallCover? _cover;
    private long _probeSentTicks;   // 0 when no probe is waiting
    private long _stallStartTicks;
    private string _stallActivity = "";
    internal int CoverShownCount { get; private set; }
    internal string LogPath => Path.Combine(_logDirectory, "responsiveness.log");

    internal ResponsivenessGuard(Control window, string logDirectory)
    {
        _window = window;
        _windowThreadId = GetCurrentThreadId();
        _logDirectory = logDirectory;
        _watchdog = new Thread(Watch) { IsBackground = true, Name = "Ping Watchdog responsiveness" };
        _watchdog.SetApartmentState(ApartmentState.STA);
        _watchdog.Start();
    }

    private void Watch()
    {
        _cover = new StallCover();
        _ = _cover.Handle;
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => Check();
        timer.Start();
        // The cover lives on this thread's own message loop, independent of the app's window thread.
        while (!_stop.IsSet)
        {
            Application.DoEvents();
            _stop.Wait(15);
        }
        timer.Stop();
        _cover.Dispose();
    }

    private void Check()
    {
        long now = Stopwatch.GetTimestamp();
        long sent = Interlocked.Read(ref _probeSentTicks);
        if (sent == 0)
        {
            if (_window.IsDisposed || !_window.IsHandleCreated) return;
            Interlocked.Exchange(ref _probeSentTicks, now);
            try { _window.BeginInvoke(Answered); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref _probeSentTicks, 0); }
            return;
        }
        var waited = Stopwatch.GetElapsedTime(sent, now);
        if (waited < ShowAfter) return;
        if (_stallStartTicks == 0) { _stallStartTicks = sent; _stallActivity = UiActivity.Current; }
        if (_cover is { Visible: false } && ForegroundAppWindow() is { } target)
        {
            _cover.ShowOver(target);
            CoverShownCount++;
        }
    }

    // Runs on the app's window thread: proof it's answering again.
    private void Answered()
    {
        long sent = Interlocked.Exchange(ref _probeSentTicks, 0);
        if (sent == 0) return;
        long start = _stallStartTicks;
        string activity = _stallActivity;
        _stallStartTicks = 0;
        try { _cover?.BeginInvoke(() => _cover.Hide()); } catch (InvalidOperationException) { }
        if (start == 0) return;
        var stall = Stopwatch.GetElapsedTime(start);
        if (stall >= LogAfter) WriteLog(stall, activity);
    }

    private void WriteLog(TimeSpan stall, string activity)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > LogLimitBytes) File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\tnot responding for {stall.TotalSeconds:0.0} s\t{(activity.Length == 0 ? "unknown" : activity)}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // The window to cover: whichever Ping Watchdog window (main window or Wallboard) is in front.
    private IntPtr? ForegroundAppWindow()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsIconic(foreground)) return null;
        return GetWindowThreadProcessId(foreground, out _) == _windowThreadId ? foreground : null;
    }

    public void Dispose()
    {
        _stop.Set();
        _watchdog.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("kernel32.dll")] private static extern int GetCurrentThreadId();

    // A borderless, click-through, never-activated cover with one word and a moving bar.
    private sealed class StallCover : Form
    {
        private readonly System.Windows.Forms.Timer _animation = new() { Interval = 30 };
        private float _phase;

        internal StallCover()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Opacity = .82;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _animation.Tick += (_, _) => { _phase = (_phase + .015f) % 1f; Invalidate(); };
            VisibleChanged += (_, _) => { if (Visible) _animation.Start(); else _animation.Stop(); };
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                const int Layered = 0x80000, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x8000000;
                var cp = base.CreateParams;
                cp.ExStyle |= Layered | Transparent | ToolWindow | NoActivate;
                return cp;
            }
        }

        internal void ShowOver(IntPtr window)
        {
            if (!GetWindowRect(window, out var rect)) return;
            BackColor = AppTheme.Palette.Canvas;
            Bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!Visible) Show();
            SetWindowPos(Handle, IntPtr.Zero, Left, Top, Width, Height, 0x0010 | 0x0040); // NOACTIVATE | SHOWWINDOW, top of z-order
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = AppTheme.Palette;
            e.Graphics.Clear(p.Canvas);
            float scale = DeviceDpi / 96f;
            using var font = new Font("Segoe UI", 11.25f, FontStyle.Regular);
            const string text = "Loading";
            var size = TextRenderer.MeasureText(text, font);
            int x = (ClientSize.Width - size.Width) / 2, y = ClientSize.Height / 2 - size.Height;
            TextRenderer.DrawText(e.Graphics, text, font, new Point(x, y), p.Text, TextFormatFlags.NoPrefix);
            int trackWidth = (int)(160 * scale), trackHeight = Math.Max(3, (int)(4 * scale));
            var track = new Rectangle((ClientSize.Width - trackWidth) / 2, y + size.Height + (int)(12 * scale), trackWidth, trackHeight);
            using (var trackBrush = new SolidBrush(p.Surface3)) e.Graphics.FillRectangle(trackBrush, track);
            int segment = trackWidth / 3;
            int start = track.X - segment + (int)((trackWidth + segment) * _phase);
            var bar = Rectangle.Intersect(track, new Rectangle(start, track.Y, segment, track.Height));
            using (var barBrush = new SolidBrush(p.Text)) if (bar.Width > 0) e.Graphics.FillRectangle(barBrush, bar);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animation.Dispose();
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}

// What the window thread is doing, for the responsiveness log. Set around work that can take a while.
internal static class UiActivity
{
    private static volatile string _current = "";
    internal static string Current => _current;

    internal static IDisposable Begin(string activity)
    {
        string previous = _current;
        _current = activity;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _previous;
        private bool _ended;
        internal Scope(string previous) => _previous = previous;
        public void Dispose() { if (_ended) return; _ended = true; _current = _previous; }
    }
}
