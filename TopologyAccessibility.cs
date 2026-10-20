namespace PingWatchdog;

internal sealed partial class WallboardCanvas
{
    private sealed record HostTarget(string Key, WallboardHostSnapshot Host, Rectangle Bounds, bool Outage);
    private List<HostTarget> _hostTargets = new();
    private readonly Dictionary<string, HostAccessibleObject> _accessibleHosts = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedHostKey;
    private string _accessibleSignature = "";

    private int SelectedHostIndex => _hostTargets.FindIndex(item => string.Equals(item.Key, _selectedHostKey, StringComparison.OrdinalIgnoreCase));
    internal WallboardHostSnapshot? SelectedHost => SelectedHostIndex is int index && index >= 0 ? _hostTargets[index].Host : null;

    private static string HostKey(WallboardHostSnapshot host, bool outage) =>
        (outage ? "outage\u001f" : "map\u001f") + host.Site + "\u001f" + host.Address;

    private static bool IsHostSnoozed(WallboardHostSnapshot host) =>
        host.SnoozedUntilUtc is { } until && until > DateTimeOffset.UtcNow;
    private static bool IsActiveOutage(WallboardSnapshot snapshot, WallboardHostSnapshot host) =>
        snapshot.Monitoring && host.State == HostState.Offline && !IsHostSnoozed(host);
    private static string HostStatus(WallboardHostSnapshot host) => IsHostSnoozed(host)
        ? "Snoozed until " + DisplayTime.Clock(host.SnoozedUntilUtc!.Value.LocalDateTime) : host.State.ToString();

    private static string HostDescription(HostTarget item) =>
        (item.Outage ? "Active outage. " : "") + item.Host.Site + ". " +
        (string.IsNullOrWhiteSpace(item.Host.Label) ? "" : item.Host.Label + ". ") +
        item.Host.Address + ". " + HostStatus(item.Host);

    private void RefreshHostTargets()
    {
        var targets = new List<HostTarget>();
        foreach (var hit in _hostHitRegions)
        {
            var bounds = hit.Bounds;
            bounds.Offset(_topologyViewport.X - _panOffset.X, _topologyViewport.Y - _panOffset.Y);
            targets.Add(new(HostKey(hit.Host, false), hit.Host, bounds, false));
        }
        foreach (var hit in _sidebarHitRegions)
            targets.Add(new(HostKey(hit.Host, true), hit.Host, hit.Bounds, true));

        string signature = string.Join("\n", targets.Select(item => item.Key + "|" + HostDescription(item)));
        _hostTargets = targets;
        if (SelectedHostIndex < 0) _selectedHostKey = targets.FirstOrDefault()?.Key;
        foreach (string key in _accessibleHosts.Keys.Where(key => !targets.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))).ToArray())
            _accessibleHosts.Remove(key);
        if (signature != _accessibleSignature)
        {
            _accessibleSignature = signature;
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
                if (Focused && SelectedHostIndex >= 0)
                    AccessibilityNotifyClients(AccessibleEvents.NameChange, SelectedHostIndex);
            }
        }
    }

    private bool SelectHost(int index, bool takeFocus = true, bool showDetails = true)
    {
        if (index < 0 || index >= _hostTargets.Count) return false;
        _selectedHostKey = _hostTargets[index].Key;
        if (takeFocus) Focus();
        if (IsHandleCreated)
        {
            AccessibilityNotifyClients(AccessibleEvents.Selection, index);
            if (Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, index);
        }
        if (showDetails && Focused && Visible && IsHandleCreated)
        {
            var item = _hostTargets[index];
            _hostTip.Show(HostDescription(item), this,
                new Point(Math.Clamp(item.Bounds.Left, 0, Math.Max(0, Width - 1)),
                    Math.Clamp(item.Bounds.Bottom, 0, Math.Max(0, Height - 1))), 5000);
        }
        Invalidate();
        return true;
    }

    private void SelectHostAt(Point point)
    {
        int index = _hostTargets.FindLastIndex(item => item.Bounds.Contains(point));
        if (index >= 0) SelectHost(index, showDetails: false);
    }

    internal bool HandleHostKey(Keys keyData)
    {
        Keys code = keyData & Keys.KeyCode;
        if ((keyData & (Keys.Control | Keys.Alt)) != Keys.None) return false;
        bool menu = code == Keys.Apps || (code == Keys.F10 && (keyData & Keys.Shift) != Keys.None);
        bool action = menu || code is Keys.Enter or Keys.Space;
        if (!action && code is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End)) return false;
        if (_hostTargets.Count == 0) return true;
        int index = Math.Max(0, SelectedHostIndex);
        if (action) { SelectHost(index, showDetails: false); OpenSelectedHostActions(); return true; }
        index = code switch
        {
            Keys.Home => 0,
            Keys.End => _hostTargets.Count - 1,
            Keys.Left or Keys.Up => Math.Max(0, index - 1),
            _ => Math.Min(_hostTargets.Count - 1, index + 1)
        };
        SelectHost(index);
        return true;
    }

    private void OpenSelectedHostActions()
    {
        int index = SelectedHostIndex;
        if (index < 0) return;
        var item = _hostTargets[index];
        // A refresh/removal can happen after layout but before a key or accessibility action.
        var current = Snapshot?.Sites.SelectMany(site => site.Hosts).FirstOrDefault(host =>
            string.Equals(host.Site, item.Host.Site, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(host.Address, item.Host.Address, StringComparison.OrdinalIgnoreCase));
        if (current == null || (item.Outage && (current.State != HostState.Offline || IsHostSnoozed(current)))) return;
        _hostTip.Hide(this);
        HostRightClicked?.Invoke(current, new Point(item.Bounds.Left, item.Bounds.Bottom));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        (Focused && HandleHostKey(keyData)) || base.ProcessCmdKey(ref msg, keyData);

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_hostTargets.Count > 0) SelectHost(Math.Max(0, SelectedHostIndex), takeFocus: false);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _hostTip.Hide(this);
        base.OnLostFocus(e);
        Invalidate();
    }

    private void DrawHostFocus(Graphics g)
    {
        if (!Focused || SelectedHostIndex < 0) return;
        var bounds = Rectangle.Intersect(ClientRectangle, _hostTargets[SelectedHostIndex].Bounds);
        bounds.Inflate(-2, -2);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var pen = new Pen(WallboardStyle.Accent, Math.Max(2, DeviceDpi / 48f));
        g.DrawRectangle(pen, bounds);
    }

    private static void DrawHostMarker(Graphics g, PointF point, float radius, WallboardHostSnapshot host)
    {
        var state = host.State;
        using var fill = new SolidBrush(IsHostSnoozed(host) ? WallboardStyle.Muted : StateColor(state, 255));
        using var pen = new Pen(fill.Color, 2);
        if (IsHostSnoozed(host))
        {
            g.DrawLine(pen, point.X - radius / 2, point.Y - radius, point.X - radius / 2, point.Y + radius);
            g.DrawLine(pen, point.X + radius / 2, point.Y - radius, point.X + radius / 2, point.Y + radius);
        }
        else if (state == HostState.Offline)
        {
            g.DrawLine(pen, point.X - radius, point.Y - radius, point.X + radius, point.Y + radius);
            g.DrawLine(pen, point.X - radius, point.Y + radius, point.X + radius, point.Y - radius);
        }
        else if (state == HostState.Suspect)
            g.FillPolygon(fill, new[] { new PointF(point.X, point.Y - radius),
                new PointF(point.X + radius, point.Y + radius), new PointF(point.X - radius, point.Y + radius) });
        else if (state == HostState.Online)
            g.FillEllipse(fill, point.X - radius, point.Y - radius, radius * 2, radius * 2);
        else
            g.DrawPolygon(pen, new[] { new PointF(point.X, point.Y - radius),
                new PointF(point.X + radius, point.Y), new PointF(point.X, point.Y + radius), new PointF(point.X - radius, point.Y) });
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new TopologyAccessibleObject(this);

    private HostAccessibleObject AccessibleHost(int index)
    {
        string key = _hostTargets[index].Key;
        if (!_accessibleHosts.TryGetValue(key, out var child))
            _accessibleHosts[key] = child = new HostAccessibleObject(this, key);
        return child;
    }

    private sealed class TopologyAccessibleObject : ControlAccessibleObject
    {
        private readonly WallboardCanvas _canvas;
        internal TopologyAccessibleObject(WallboardCanvas canvas) : base(canvas) { _canvas = canvas; }
        public override AccessibleRole Role => AccessibleRole.List;
        public override string? Description => "Arrow keys select hosts. Home and End move to the first and last item. Enter, Space, Menu or Shift+F10 opens host actions.";
        public override int GetChildCount() => _canvas._hostTargets.Count;
        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < GetChildCount() ? _canvas.AccessibleHost(index) : null;
        public override AccessibleObject? GetFocused() => _canvas.Focused ? GetSelected() : null;
        public override AccessibleObject? GetSelected() => GetChild(_canvas.SelectedHostIndex);
        public override AccessibleObject? HitTest(int x, int y)
        {
            var point = _canvas.PointToClient(new Point(x, y));
            int index = _canvas._hostTargets.FindLastIndex(item => item.Bounds.Contains(point));
            return GetChild(index) ?? base.HitTest(x, y);
        }
        public override AccessibleObject? Navigate(AccessibleNavigation navdir) => navdir switch
        {
            AccessibleNavigation.FirstChild => GetChild(0),
            AccessibleNavigation.LastChild => GetChild(GetChildCount() - 1),
            _ => base.Navigate(navdir)
        };
    }

    private sealed class HostAccessibleObject : AccessibleObject
    {
        private readonly WallboardCanvas _canvas;
        private readonly string _key;
        internal HostAccessibleObject(WallboardCanvas canvas, string key) { _canvas = canvas; _key = key; }
        private int Index => _canvas._hostTargets.FindIndex(item => string.Equals(item.Key, _key, StringComparison.OrdinalIgnoreCase));
        private HostTarget? Target => Index >= 0 ? _canvas._hostTargets[Index] : null;
        public override AccessibleObject? Parent => _canvas.AccessibilityObject;
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override string? Name { get => Target is { } item ? HostDescription(item) : "Host no longer available"; set { } }
        public override string? Value { get => Target is { } item ? HostStatus(item.Host) : null; set { } }
        public override string? DefaultAction => "Open host actions";
        public override Rectangle Bounds => Target is { } item && _canvas.IsHandleCreated
            ? _canvas.RectangleToScreen(item.Bounds) : Rectangle.Empty;
        public override AccessibleStates State => Target == null
            ? AccessibleStates.Unavailable | AccessibleStates.Offscreen
            : AccessibleStates.Focusable | AccessibleStates.Selectable |
                (_canvas.SelectedHostIndex == Index ? AccessibleStates.Selected |
                    (_canvas.Focused ? AccessibleStates.Focused : AccessibleStates.None) : AccessibleStates.None);
        public override void Select(AccessibleSelection flags)
        {
            if ((flags & (AccessibleSelection.TakeFocus | AccessibleSelection.TakeSelection)) != 0)
                _canvas.SelectHost(Index);
        }
        public override void DoDefaultAction()
        {
            if (_canvas.SelectHost(Index, showDetails: false)) _canvas.OpenSelectedHostActions();
        }
        public override AccessibleObject? Navigate(AccessibleNavigation navdir)
        {
            int index = Index;
            if (index < 0) return null;
            return navdir switch
            {
                AccessibleNavigation.Next or AccessibleNavigation.Down or AccessibleNavigation.Right => _canvas.AccessibilityObject.GetChild(index + 1),
                AccessibleNavigation.Previous or AccessibleNavigation.Up or AccessibleNavigation.Left => _canvas.AccessibilityObject.GetChild(index - 1),
                _ => null
            };
        }
    }
}
