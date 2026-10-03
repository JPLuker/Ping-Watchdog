using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace PingWatchdog;

/// <summary>
/// Final presentation/interaction pass layered over the existing WinForms shell.
/// Keeps the monitoring engine untouched while tightening the production UI.
/// </summary>
internal static class WatchdogUiPolish
{
    private static readonly ConditionalWeakTable<MainForm, UiState> States = new();
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form is not MainForm main || States.TryGetValue(main, out _))
                continue;

            var state = new UiState();
            States.Add(main, state);
            Apply(main, state);
        }
    }

    private static void Apply(MainForm form, UiState state)
    {
        var title = GetPrivate<Label>(form, "_brandTitleLabel");
        var subtitle = GetPrivate<Label>(form, "_brandSubtitleLabel");
        var version = GetPrivate<Label>(form, "_versionLabel");
        var monitorBadge = GetPrivate<Label>(form, "_monitorStateLabel");
        var headerActions = GetPrivate<FlowLayoutPanel>(form, "_headerActionsPanel");
        var siteList = GetPrivate<ListBox>(form, "_siteList");
        var siteFont = GetPrivate<Font>(form, "_siteItemFont") ?? siteList?.Font;
        var grid = GetPrivate<DataGridView>(form, "_grid");

        RemoveRedundantHeaderChrome(version, monitorBadge);
        CollapseDuplicateLiveApplyText(form);
        RebuildBrandHeader(title, subtitle, version);
        ApplyBrandAccents(form, siteList, siteFont, grid);

        if (grid is not null)
            InstallStableGridSelection(grid, state);

        if (headerActions is not null)
        {
            headerActions.Margin = new Padding(0, 6, 0, 0);
            headerActions.Padding = Padding.Empty;
        }

        form.Invalidate(true);
    }

    private static T? GetPrivate<T>(MainForm form, string fieldName)
        where T : class
    {
        return typeof(MainForm)
            .GetField(fieldName, PrivateInstance)
            ?.GetValue(form) as T;
    }

    private static void RemoveRedundantHeaderChrome(
        Label? version,
        Label? monitorBadge)
    {
        if (version?.Parent is Control versionParent)
        {
            versionParent.Controls.Remove(version);
            version.Visible = false;
        }

        if (monitorBadge?.Parent is Control monitorParent)
        {
            monitorParent.Controls.Remove(monitorBadge);
            monitorBadge.Visible = false;
        }
    }

    private static void CollapseDuplicateLiveApplyText(Control root)
    {
        foreach (var label in Descendants(root)
            .OfType<Label>()
            .Where(label => label.Text.Equals(
                "Changes apply live",
                StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            if (label.Parent is TableLayoutPanel table)
            {
                int row = table.GetRow(label);
                table.Controls.Remove(label);

                if (row >= 0 && row < table.RowStyles.Count)
                {
                    table.RowStyles[row].SizeType = SizeType.Absolute;
                    table.RowStyles[row].Height = 0;
                }
            }
            else
            {
                label.Visible = false;
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void RebuildBrandHeader(
        Label? title,
        Label? subtitle,
        Label? version)
    {
        if (title?.Parent is not TableLayoutPanel brand || subtitle is null)
            return;

        brand.SuspendLayout();
        try
        {
            brand.Controls.Clear();
            brand.ColumnStyles.Clear();
            brand.RowStyles.Clear();
            brand.ColumnCount = 2;
            brand.RowCount = 2;
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var logo = new WatchdogMarkControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0),
                AccessibleName = "Ping Watchdog logo"
            };

            title.Margin = new Padding(6, 0, 0, 0);
            title.Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(245, 249, 252);

            subtitle.Margin = new Padding(8, 0, 0, 0);
            subtitle.ForeColor = Color.FromArgb(139, 170, 190);

            if (version is not null)
                version.Visible = false;

            brand.Controls.Add(logo, 0, 0);
            brand.SetRowSpan(logo, 2);
            brand.Controls.Add(title, 1, 0);
            brand.Controls.Add(subtitle, 1, 1);
        }
        finally
        {
            brand.ResumeLayout(true);
        }
    }

    private static void ApplyBrandAccents(
        MainForm form,
        ListBox? siteList,
        Font? siteFont,
        DataGridView? grid)
    {
        var cyan = Color.FromArgb(57, 217, 238);
        var cyanDim = Color.FromArgb(42, 170, 189);
        var tealSelection = Color.FromArgb(27, 76, 94);
        var tealSelectionStrong = Color.FromArgb(34, 101, 124);

        var wallboard = GetPrivate<Button>(form, "_wallboardButton");
        var settings = GetPrivate<Button>(form, "_settingsButton");
        var more = GetPrivate<Button>(form, "_moreButton");

        if (wallboard is not null)
        {
            wallboard.BackColor = Color.FromArgb(20, 69, 82);
            wallboard.FlatAppearance.BorderColor = cyanDim;
        }

        if (settings is not null)
        {
            settings.BackColor = Color.FromArgb(23, 32, 43);
            settings.FlatAppearance.BorderColor = Color.FromArgb(48, 65, 82);
        }

        if (more is not null)
        {
            more.BackColor = Color.FromArgb(23, 32, 43);
            more.FlatAppearance.BorderColor = Color.FromArgb(48, 65, 82);
            more.ForeColor = cyan;
        }

        if (grid is not null)
        {
            grid.DefaultCellStyle.SelectionBackColor = tealSelectionStrong;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(183, 205, 220);
        }

        if (siteList is not null)
        {
            var drawFont = siteFont ?? siteList.Font;

            siteList.DrawItem += (_, e) =>
            {
                if (e.Index < 0 || e.Index >= siteList.Items.Count)
                    return;

                bool selected =
                    (e.State & DrawItemState.Selected) == DrawItemState.Selected;

                if (!selected)
                    return;

                var bounds = new Rectangle(
                    e.Bounds.X + 2,
                    e.Bounds.Y + 2,
                    e.Bounds.Width - 4,
                    e.Bounds.Height - 4);

                using var background = new SolidBrush(tealSelection);
                e.Graphics.FillRectangle(background, bounds);

                TextRenderer.DrawText(
                    e.Graphics,
                    siteList.Items[e.Index]?.ToString() ?? string.Empty,
                    drawFont,
                    new Rectangle(
                        bounds.X + 12,
                        bounds.Y,
                        bounds.Width - 18,
                        bounds.Height),
                    Color.White,
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.SingleLine);
            };
        }
    }

    private static void InstallStableGridSelection(
        DataGridView grid,
        UiState state)
    {
        grid.DataSourceChanged += (_, _) => state.Rebinding = true;

        grid.CellMouseDown += (_, e) =>
        {
            if (e.RowIndex < 0)
                return;

            RememberSelection(grid, state, e.RowIndex, e.ColumnIndex);
        };

        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0)
                return;

            RememberSelection(grid, state, e.RowIndex, e.ColumnIndex);
        };

        grid.SelectionChanged += (_, _) =>
        {
            if (state.Rebinding || state.Restoring || !grid.Focused)
                return;

            if (grid.CurrentRow is not null)
            {
                RememberSelection(
                    grid,
                    state,
                    grid.CurrentRow.Index,
                    grid.CurrentCell?.ColumnIndex ?? 0);
            }
        };

        grid.Scroll += (_, _) => RememberScrollPosition(grid, state);

        grid.DataBindingComplete += (_, _) =>
        {
            try
            {
                RestoreSelection(grid, state);
            }
            finally
            {
                state.Rebinding = false;
            }
        };
    }

    private static void RememberSelection(
        DataGridView grid,
        UiState state,
        int rowIndex,
        int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            return;

        var row = grid.Rows[rowIndex];
        string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
        string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(host))
            return;

        state.SelectedSite = site;
        state.SelectedHost = host;
        state.ColumnIndex = Math.Max(0, columnIndex);
        RememberScrollPosition(grid, state);
    }

    private static void RememberScrollPosition(
        DataGridView grid,
        UiState state)
    {
        try
        {
            if (grid.Rows.Count > 0 && grid.FirstDisplayedScrollingRowIndex >= 0)
                state.FirstDisplayedRow = grid.FirstDisplayedScrollingRowIndex;
        }
        catch (InvalidOperationException)
        {
            // Grid can be between data-source states during a live refresh.
        }
    }

    private static void RestoreSelection(
        DataGridView grid,
        UiState state)
    {
        if (string.IsNullOrWhiteSpace(state.SelectedSite) ||
            string.IsNullOrWhiteSpace(state.SelectedHost) ||
            grid.Rows.Count == 0)
        {
            return;
        }

        state.Restoring = true;
        try
        {
            DataGridViewRow? match = null;

            foreach (DataGridViewRow row in grid.Rows)
            {
                string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
                string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;

                if (site.Equals(state.SelectedSite, StringComparison.OrdinalIgnoreCase) &&
                    host.Equals(state.SelectedHost, StringComparison.OrdinalIgnoreCase))
                {
                    match = row;
                    break;
                }
            }

            grid.ClearSelection();

            if (match is null)
                return;

            match.Selected = true;

            int columnIndex = Math.Clamp(
                state.ColumnIndex,
                0,
                Math.Max(0, grid.Columns.Count - 1));

            if (!grid.Columns[columnIndex].Visible)
                columnIndex = grid.Columns["HostColumn"]?.Index ?? columnIndex;

            grid.CurrentCell = match.Cells[columnIndex];

            int first = Math.Clamp(
                state.FirstDisplayedRow,
                0,
                Math.Max(0, grid.Rows.Count - 1));

            try
            {
                grid.FirstDisplayedScrollingRowIndex = first;
            }
            catch (InvalidOperationException)
            {
                // A row can briefly be unavailable while layout settles.
            }
        }
        finally
        {
            state.Restoring = false;
        }
    }

    private sealed class UiState
    {
        public string? SelectedSite { get; set; }
        public string? SelectedHost { get; set; }
        public int ColumnIndex { get; set; }
        public int FirstDisplayedRow { get; set; }
        public bool Rebinding { get; set; }
        public bool Restoring { get; set; }
    }
}

internal sealed class WatchdogMarkControl : Control
{
    public WatchdogMarkControl()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        MinimumSize = new Size(42, 42);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        float size = Math.Min(ClientSize.Width, ClientSize.Height) - 4f;
        if (size <= 4f)
            return;

        float ox = (ClientSize.Width - size) / 2f;
        float oy = (ClientSize.Height - size) / 2f;
        float scale = size / 44f;

        e.Graphics.TranslateTransform(ox, oy);
        e.Graphics.ScaleTransform(scale, scale);

        using var outline = new Pen(Color.FromArgb(57, 217, 238), 2.1f);
        using var earLine = new Pen(Color.FromArgb(42, 170, 189), 1.7f);
        using var headBrush = new SolidBrush(Color.FromArgb(37, 47, 59));
        using var earBrush = new SolidBrush(Color.FromArgb(24, 57, 69));
        using var eyeBrush = new SolidBrush(Color.FromArgb(131, 244, 255));
        using var muzzleBrush = new SolidBrush(Color.FromArgb(74, 85, 101));
        using var noseBrush = new SolidBrush(Color.FromArgb(16, 24, 32));

        var leftEar = new[]
        {
            new PointF(9, 11),
            new PointF(3, 2),
            new PointF(5, 17)
        };
        var rightEar = new[]
        {
            new PointF(35, 11),
            new PointF(41, 2),
            new PointF(39, 17)
        };

        e.Graphics.FillPolygon(earBrush, leftEar);
        e.Graphics.DrawPolygon(earLine, leftEar);
        e.Graphics.FillPolygon(earBrush, rightEar);
        e.Graphics.DrawPolygon(earLine, rightEar);

        e.Graphics.FillEllipse(headBrush, 5, 6, 34, 34);
        e.Graphics.DrawEllipse(outline, 5, 6, 34, 34);

        e.Graphics.FillEllipse(eyeBrush, 13, 19, 4, 4);
        e.Graphics.FillEllipse(eyeBrush, 27, 19, 4, 4);

        e.Graphics.FillEllipse(muzzleBrush, 14, 24, 16, 11);
        e.Graphics.FillEllipse(noseBrush, 19, 26, 6, 4);
        using var mouthPen = new Pen(Color.FromArgb(16, 24, 32), 1.5f);
        e.Graphics.DrawLine(mouthPen, 22, 30, 22, 35);

        e.Graphics.ResetTransform();
    }
}
