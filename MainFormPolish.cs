using System.Reflection;
using System.Runtime.CompilerServices;

namespace PingWatchdog;

/// <summary>
/// Small shell-level polish layer kept separate from the monitoring engine.
/// It removes redundant chrome and preserves the user's host-grid context
/// while the live data source refreshes every 500 ms.
/// </summary>
internal static class MainFormPolish
{
    private static readonly ConditionalWeakTable<MainForm, MainFormUiState> States = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += AttachToOpenMainForms;
    }

    private static void AttachToOpenMainForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is not MainForm main || States.TryGetValue(main, out _))
                continue;

            var state = new MainFormUiState(main);
            States.Add(main, state);
            state.Attach();
        }
    }

    private sealed class MainFormUiState : IDisposable
    {
        private readonly MainForm _form;
        private DataGridView? _grid;
        private PictureBox? _brandIcon;
        private Bitmap? _brandBitmap;
        private (string Site, string Host)? _desiredSelection;
        private int _desiredFirstDisplayedRow = -1;
        private bool _restoring;
        private bool _disposed;

        internal MainFormUiState(MainForm form)
        {
            _form = form;
        }

        internal void Attach()
        {
            RemoveRedundantHeaderChrome();
            RemoveSidebarHelper();
            AddBrandIcon();
            HookLiveGrid();

            _form.FormClosed += OnFormClosed;
            _form.PerformLayout();
        }

        private void RemoveRedundantHeaderChrome()
        {
            RemoveControl(Field<Label>("_versionLabel"));
            RemoveControl(Field<Label>("_monitorStateLabel"));
        }

        private void RemoveSidebarHelper()
        {
            Label? helper = FindControl<Label>(
                _form,
                label => label.Text.Equals(
                    "Changes apply live",
                    StringComparison.OrdinalIgnoreCase));

            if (helper?.Parent is not TableLayoutPanel layout)
                return;

            int row = layout.GetRow(helper);
            layout.Controls.Remove(helper);
            helper.Dispose();

            if (row >= 0 && row < layout.RowStyles.Count)
            {
                layout.RowStyles[row].SizeType = SizeType.Absolute;
                layout.RowStyles[row].Height = 0;
            }
        }

        private void AddBrandIcon()
        {
            var title = Field<Label>("_brandTitleLabel");
            var subtitle = Field<Label>("_brandSubtitleLabel");

            if (title?.Parent is not TableLayoutPanel brand || _form.Icon is null)
                return;

            brand.ColumnStyles.Clear();
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            brand.SetCellPosition(title, new TableLayoutPanelCellPosition(1, 0));
            title.Margin = new Padding(0, 0, 0, 0);

            if (subtitle is not null && ReferenceEquals(subtitle.Parent, brand))
            {
                brand.SetCellPosition(subtitle, new TableLayoutPanelCellPosition(1, 1));
                brand.SetColumnSpan(subtitle, 1);
                subtitle.Margin = new Padding(0, 0, 0, 0);
            }

            _brandBitmap = _form.Icon.ToBitmap();
            _brandIcon = new PictureBox
            {
                Image = _brandBitmap,
                Size = new Size(30, 30),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 1, 8, 0),
                BackColor = Color.Transparent,
                TabStop = false,
                AccessibleName = "Ping Watchdog logo"
            };

            brand.Controls.Add(_brandIcon, 0, 0);
            brand.SetRowSpan(_brandIcon, 2);
        }

        private void HookLiveGrid()
        {
            _grid = Field<DataGridView>("_grid");
            if (_grid is null)
                return;

            _grid.CellMouseDown += RememberMouseSelection;
            _grid.CellClick += RememberClickedSelection;
            _grid.KeyUp += RememberKeyboardSelection;
            _grid.Scroll += RememberScrollPosition;
            _grid.DataBindingComplete += RestoreGridContext;
        }

        private void RememberMouseSelection(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (_grid is null || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
                return;

            RememberRow(_grid.Rows[e.RowIndex]);
        }

        private void RememberClickedSelection(object? sender, DataGridViewCellEventArgs e)
        {
            if (_grid is null || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
                return;

            RememberRow(_grid.Rows[e.RowIndex]);
        }

        private void RememberKeyboardSelection(object? sender, KeyEventArgs e)
        {
            if (_grid?.CurrentRow is not null)
                RememberRow(_grid.CurrentRow);
        }

        private void RememberScrollPosition(object? sender, ScrollEventArgs e)
        {
            if (_restoring || _grid is null || !_grid.Focused)
                return;

            TryRememberFirstDisplayedRow();
        }

        private void RememberRow(DataGridViewRow row)
        {
            string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
            string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(host))
                return;

            _desiredSelection = (site, host);
            TryRememberFirstDisplayedRow();
        }

        private void TryRememberFirstDisplayedRow()
        {
            if (_grid is null)
                return;

            try
            {
                _desiredFirstDisplayedRow = _grid.FirstDisplayedScrollingRowIndex;
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void RestoreGridContext(object? sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (_grid is null || _restoring)
                return;

            _restoring = true;

            try
            {
                _grid.ClearSelection();
                _grid.CurrentCell = null;

                DataGridViewRow? selectedRow = null;

                if (_desiredSelection is { } desired)
                {
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        string site = row.Cells["SiteColumn"].Value?.ToString() ?? string.Empty;
                        string host = row.Cells["HostColumn"].Value?.ToString() ?? string.Empty;

                        if (!site.Equals(desired.Site, StringComparison.OrdinalIgnoreCase) ||
                            !host.Equals(desired.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        selectedRow = row;
                        break;
                    }
                }

                if (selectedRow is not null)
                {
                    selectedRow.Selected = true;
                    var hostCell = selectedRow.Cells["HostColumn"];
                    if (hostCell.Visible)
                        _grid.CurrentCell = hostCell;

                    if (_desiredFirstDisplayedRow >= 0 &&
                        _desiredFirstDisplayedRow < _grid.Rows.Count)
                    {
                        try
                        {
                            _grid.FirstDisplayedScrollingRowIndex = _desiredFirstDisplayedRow;
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                }
                else if (_desiredSelection is not null)
                {
                    _desiredSelection = null;
                    _desiredFirstDisplayedRow = -1;
                }
            }
            finally
            {
                _restoring = false;
            }
        }

        private T? Field<T>(string name) where T : class
        {
            return typeof(MainForm)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_form) as T;
        }

        private static void RemoveControl(Control? control)
        {
            if (control is null)
                return;

            control.Visible = false;
            control.Parent?.Controls.Remove(control);
        }

        private static T? FindControl<T>(Control root, Func<T, bool> predicate)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match && predicate(match))
                    return match;

                var nested = FindControl(child, predicate);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_grid is not null)
            {
                _grid.CellMouseDown -= RememberMouseSelection;
                _grid.CellClick -= RememberClickedSelection;
                _grid.KeyUp -= RememberKeyboardSelection;
                _grid.Scroll -= RememberScrollPosition;
                _grid.DataBindingComplete -= RestoreGridContext;
            }

            _form.FormClosed -= OnFormClosed;
            _brandIcon?.Dispose();
            _brandBitmap?.Dispose();
        }
    }
}
