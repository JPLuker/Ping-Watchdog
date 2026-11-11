# Conventions

Code rules for Ping Watchdog (Windows, .NET 8 WinForms). The standards' SOP.md Part 6 applies; this adds the project's own.

## Layout of the code

- One feature per file where it's practical. `MainForm` is a partial class split across files (`MonitorWorkspace.cs`, `HostListEditing.cs`, `HostManagement.cs`, …). New main-window features get their own partial file rather than growing `Program.cs`.
- Every file starts with a comment saying what it's for.
- Aim for files under ~800 lines. `WallboardForm.cs` (1,900) and `MainForm.cs` (900) are still over; split them by feature as you touch them, without changing behavior.

## UI

- Colors, fonts, radii and spacing come from `AppTheme` (palette, `Space*`, `Radius*`, `*Font`). No `Color.FromArgb`, hex or `new Font` for UI elsewhere, except the theme's own tests.
- Controls get their colors from `AppTheme.Apply` through their `Tag` role: `window`, `nav`, `card`, `header`, `well`, `divider`, `overlay`, and for labels `title`, `primaryText`, `muted`, `accentText`, `statOnline`, `statSuspect`, `statOffline`. Set the tag, not the color, so theme changes keep it.
- Buttons are `ThemeButton`: `Primary` (one per view), default (secondary), `Ghost`, `Outline` + `Ghost` for chips, `Destructive`.
- Layout uses docking, tables and flow panels. Sizes scale with `DeviceDpi / 96f`.
- Labels that show user text set `UseMnemonic = false`; menu text goes through `AppTheme.MenuText` so `&` shows.
- Text input that can receive pasted text uses `PlainTextBox`, `ClipboardText.AttachPlainPaste` or `GridPaste`, so remote-session and HTML-only clipboards work.

## Data

- Saved files go through `SafeConfigStore.WriteAtomic` (temp file, read back, keep backups). Never write user data elsewhere.
- Paths come from `StoragePaths.Current` (installed vs portable). Logs go in `StorageLayout.LogDirectory`.
- New preference fields go in `UserPreferences` with a default, keeping `FormatVersion = 1` while old files still read correctly.

## Tests

- `--self-test` runs logic and layout tests; `--ui-smoke-test` drives the real window and saves captures to `ui-smoke-test/`.
- A bug gets a test that fails before the fix. A removed thing gets a guard in NO_LIST.md.
- Never catch and ignore without a reason in a comment and a narrow exception filter.
