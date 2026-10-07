from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


root = Path('.')
program_path = root / 'Program.cs'
host_management_path = root / 'HostManagement.cs'
wallboard_path = root / 'WallboardForm.cs'
workflow_path = root / '.github/workflows/build-windows.yml'

program = program_path.read_text(encoding='utf-8')
host_management = host_management_path.read_text(encoding='utf-8')
wallboard = wallboard_path.read_text(encoding='utf-8')
workflow = workflow_path.read_text(encoding='utf-8')

# ---------------------------------------------------------------------------
# Disabled hosts: cancellation is the primary mechanism, but an in-flight ping
# can complete at the same instant a user disables the host. Gate the live
# result path against the canonical enabled state as a second line of defense.
# ---------------------------------------------------------------------------
program = replace_once(
    program,
    '''            if (token.IsCancellationRequested || IsDisposed || Disposing)\n                break;\n\n            AppendCommandLog(host.Site, host.Address, success, resultText);\n            ProcessResult(host, success, latency);''',
    '''            if (token.IsCancellationRequested || IsDisposed || Disposing ||\n                !IsHostEnabled(host.Site, host.Address))\n            {\n                break;\n            }\n\n            AppendCommandLog(host.Site, host.Address, success, resultText);\n            ProcessResult(host, success, latency);''',
    'monitor loop enabled-state gate')

program = replace_once(
    program,
    '''    private void ProcessResult(HostMonitor host, bool success, long? latency)\n    {\n        string? notificationTitle = null;''',
    '''    private void ProcessResult(HostMonitor host, bool success, long? latency)\n    {\n        // Unit/self-tests exercise the state machine without a live session. During\n        // real monitoring, however, a disabled host is quarantined even if a probe\n        // completed concurrently with the Disable action.\n        if (_cts is not null && !IsHostEnabled(host.Site, host.Address))\n            return;\n\n        string? notificationTitle = null;''',
    'process-result disabled guard')

program = replace_once(
    program,
    '''    private void RecordStateEvent(\n        string site,\n        string host,\n        string displayHost,\n        string kind,\n        string message)\n    {\n        lock (_stateEvents)''',
    '''    private void RecordStateEvent(\n        string site,\n        string host,\n        string displayHost,\n        string kind,\n        string message)\n    {\n        if (_cts is not null && !IsHostEnabled(site, host))\n            return;\n\n        lock (_stateEvents)''',
    'event disabled guard')

# Exercise the Wallboard auto-fit calculation in the normal self-test.
program = replace_once(
    program,
    '''        Check(WallboardCanvas.AggregateSiteState(tiedSite) == HostState.Offline);\n\n        form.ClientSize = new Size(960, 640);''',
    '''        Check(WallboardCanvas.AggregateSiteState(tiedSite) == HostState.Offline);\n        Check(WallboardCanvas.CalculateTopologyRenderScale(new Rectangle(0, 0, 820, 300), 3, 25) < 0.80);\n        Check(WallboardCanvas.CalculateTopologyRenderScale(new Rectangle(0, 0, 1400, 700), 2, 10) == 1.0);\n\n        form.ClientSize = new Size(960, 640);''',
    'wallboard autofit self-test')

# ---------------------------------------------------------------------------
# Host Manager: make Disable take effect immediately instead of waiting for the
# end-of-operation reconciliation pass, then verify it during an active session.
# ---------------------------------------------------------------------------
host_management = replace_once(
    host_management,
    '''            site.HostDetails[host.Address] = host.Options.Copy();\n            SetNicknameValue(site.Name, host.Address, host.Label); SetCategoryValue(site.Name, host.Address, host.Group);''',
    '''            site.HostDetails[host.Address] = host.Options.Copy();\n\n            if (!host.Options.Enabled)\n            {\n                string workerKey = BuildHostKey(site.Name, host.Address);\n                StopHostWorker(workerKey);\n                _hosts.TryRemove(workerKey, out _);\n            }\n\n            SetNicknameValue(site.Name, host.Address, host.Label); SetCategoryValue(site.Name, host.Address, host.Group);''',
    'immediate live disable')

host_management = replace_once(
    host_management,
    '''        var imported = form.HostSnapshot().First(h => h.Address == "192.0.2.10");\n        Check(imported.Label == "Printer" && imported.Group == "Printers", "import metadata");\n        var options = imported.Options.Copy(); options.Enabled = false; options.TimeoutMs = 2345;\n        form.SaveManagedHosts(new() { imported with { Label = "Accounting", Options = options } });\n        Check(form.GetNickname(siteName, imported.Address) == "Accounting", "rename canonical source");\n        Check(!form.GetConfiguredTargets().Any(h => h.Address == imported.Address), "disabled hosts excluded");''',
    '''        var imported = form.HostSnapshot().First(h => h.Address == "192.0.2.10");\n        Check(imported.Label == "Printer" && imported.Group == "Printers", "import metadata");\n\n        form.StartMonitoring();\n        string liveKey = BuildHostKey(siteName, imported.Address);\n        Check(form._hosts.ContainsKey(liveKey), "enabled host starts a live worker");\n\n        var options = imported.Options.Copy(); options.Enabled = false; options.TimeoutMs = 2345;\n        form.SaveManagedHosts(new() { imported with { Label = "Accounting", Options = options } });\n        Check(form.GetNickname(siteName, imported.Address) == "Accounting", "rename canonical source");\n        Check(!form.GetConfiguredTargets().Any(h => h.Address == imported.Address), "disabled hosts excluded");\n        Check(!form._hosts.ContainsKey(liveKey), "disable removes active host immediately");\n        Check(!form._hostTokens.ContainsKey(liveKey), "disable cancels active worker immediately");\n\n        int eventCountBeforeDisabledResult = form._stateEvents.Count;\n        var disabledRuntime = new HostMonitor(siteName, imported.Address);\n        for (int i = 0; i < 3; i++)\n            form.ProcessResult(disabledRuntime, false, null);\n        Check(disabledRuntime.State == HostState.Unknown, "disabled host result ignored");\n        Check(form._stateEvents.Count == eventCountBeforeDisabledResult, "disabled host cannot create outage events");\n        form.StopMonitoring();''',
    'live disable integration test')

# ---------------------------------------------------------------------------
# Wallboard topology: render dense/small topologies on a larger virtual canvas
# and scale the finished result into the available panel. This scales fonts,
# captions, site/host nodes, and collision displacement together instead of
# asking the operator to resize a fullscreen wallboard.
# ---------------------------------------------------------------------------
old_topology_signature = '''    private void DrawTopology(\n        Graphics g,\n        WallboardSnapshot snapshot,\n        Rectangle rect)\n    {\n        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));'''

new_topology_signature = '''    private void DrawTopology(\n        Graphics g,\n        WallboardSnapshot snapshot,\n        Rectangle rect)\n    {\n        int visibleHosts = snapshot.Sites.Sum(site =>\n            Math.Min(site.Hosts.Count, Presentation.VisibleTopologyHosts));\n        double renderScale = CalculateTopologyRenderScale(\n            rect,\n            snapshot.Sites.Count,\n            visibleHosts);\n\n        if (renderScale >= 0.995)\n        {\n            DrawTopologyCore(g, snapshot, rect);\n            return;\n        }\n\n        int virtualWidth = Math.Max(rect.Width, (int)Math.Ceiling(rect.Width / renderScale));\n        int virtualHeight = Math.Max(rect.Height, (int)Math.Ceiling(rect.Height / renderScale));\n\n        using var bitmap = new Bitmap(virtualWidth, virtualHeight);\n        using (var virtualGraphics = Graphics.FromImage(bitmap))\n        {\n            virtualGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;\n            virtualGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;\n            virtualGraphics.Clear(Color.FromArgb(9, 15, 22));\n            DrawTopologyCore(\n                virtualGraphics,\n                snapshot,\n                new Rectangle(0, 0, virtualWidth, virtualHeight));\n        }\n\n        var previousInterpolation = g.InterpolationMode;\n        var previousPixelOffset = g.PixelOffsetMode;\n        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;\n        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;\n        g.DrawImage(bitmap, rect);\n        g.InterpolationMode = previousInterpolation;\n        g.PixelOffsetMode = previousPixelOffset;\n\n        using var border = new Pen(Color.FromArgb(32, 44, 57));\n        g.DrawRectangle(border, rect);\n    }\n\n    internal static double CalculateTopologyRenderScale(\n        Rectangle rect,\n        int siteCount,\n        int visibleHostCount)\n    {\n        if (rect.Width <= 0 || rect.Height <= 0)\n            return 0.55;\n\n        double viewportScale = Math.Min(\n            rect.Width / 900d,\n            rect.Height / 420d);\n        double siteScale = siteCount <= 4\n            ? 1d\n            : Math.Sqrt(4d / Math.Max(1, siteCount));\n        double hostScale = visibleHostCount <= 18\n            ? 1d\n            : Math.Sqrt(18d / Math.Max(1, visibleHostCount));\n\n        return Math.Clamp(\n            Math.Min(1d, Math.Min(viewportScale, Math.Min(siteScale, hostScale))),\n            0.55d,\n            1d);\n    }\n\n    private void DrawTopologyCore(\n        Graphics g,\n        WallboardSnapshot snapshot,\n        Rectangle rect)\n    {\n        using var titleBrush = new SolidBrush(Color.FromArgb(139, 158, 178));'''

wallboard = replace_once(
    wallboard,
    old_topology_signature,
    new_topology_signature,
    'wallboard virtual canvas auto-fit')

# The virtual-canvas renderer is now the fallback. Never tell the user to make a
# fullscreen wallboard larger; dots remain visible even if a final caption has to
# be omitted after auto-fit.
wallboard = replace_once(
    wallboard,
    '''        if (_hiddenCaptions > 0)\n            TextRenderer.DrawText(g, $"{_hiddenCaptions} captions need more space — enlarge Wallboard or view Hosts",\n                _tinyFont, new Rectangle(content.Left, content.Bottom - 21, content.Width, 20),\n                Color.FromArgb(180, 195, 209), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);''',
    '''        // Dense layouts are rendered on a larger virtual canvas and scaled to fit.\n        // Any caption that still cannot be placed is silently omitted; the host node\n        // itself remains visible and Operations/Hosts retains the full details.''',
    'remove resize-wallboard warning')

# Release notes for the generated Windows release.
workflow = replace_once(
    workflow,
    '''          - Includes live site/host management, autosave, nicknames, grouping, dark mode, and live CMD trace.\n          - Copyright © 2026 Joseph Luker. All rights reserved.''',
    '''          - Includes live site/host management, autosave, nicknames, grouping, dark mode, and live CMD trace.\n          - Disabled hosts are immediately quarantined from live workers, outage transitions, notifications, Wallboard, and CLI even when a ping completes concurrently with Disable.\n          - Wallboard topology auto-fits dense layouts by rendering on a larger virtual canvas and scaling nodes, labels, and collision spacing to the available monitor instead of asking the user to enlarge Wallboard.\n          - Copyright © 2026 Joseph Luker. All rights reserved.''',
    'release notes')

program_path.write_text(program, encoding='utf-8')
host_management_path.write_text(host_management, encoding='utf-8')
wallboard_path.write_text(wallboard, encoding='utf-8')
workflow_path.write_text(workflow, encoding='utf-8')

print('Applied live-disable quarantine and Wallboard topology auto-fit fixes.')
