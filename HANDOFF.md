# Handoff

## The project

Ping Watchdog watches IP addresses and hostnames with ICMP ping, grouped by site, and shows outages and recoveries on a Map, a List and a full-screen Wallboard. Windows, .NET 8 WinForms, installed and updated through Velopack. Feature-complete; since 2026-11-11 it's the test project for Joseph's new standards (Quiet Precision look plus everything else the standards recommend, functionality unchanged).

## Rules box

- Keep building until done or blocked on Joseph (standards SOP Part 1).
- Ask before any visual change beyond Quiet Precision as recorded in AGENTS.md → Identity.
- Every push bumps the version; passing pushes to `main` publish a release Joseph updates to from the app.
- Proof before claims: tests plus captures you've looked at. The validation workflow prints captures to its log (`REVIEW_IMAGE_BEGIN…`); artifacts aren't reachable from the agent sandbox.
- Proprietary license stays. Windows and Linux stay separate.

## Where we are (2026-11-11)

Standards pass, released as **1.19.29**:

- **Theme:** `AppTheme.cs` (was `AuroraTheme.cs`). `ThemePalette.Create` holds the Quiet Precision values; `Nav` is the canvas; `Teal` maps to `Success`. Fonts `Title1Font`…`MonoFont`, radii `RadiusXs`…`RadiusDialog`. `ThemeButton.OnPaint` draws 8px rounded rectangles (pills only when `Outline`), ink primary, `Focus` ring. Types renamed: `ThemeButton`, `ScrollWell`, `WellListBox`, `ThemeMenuColors`.
- **Status dots:** `HostResultsGrid.OnCellPainting` paints dot + word for `StatusColumn`; `StatusDot()` maps state to color.
- **#41 whole addresses:** `AdaptiveTopology.DrawFittedTopology` prefers layouts where `addressNeeds` fit; addresses are drawn untrimmed with `NoClip`; `DrawnAddresses` exposes what was drawn. Tests: "#41" block and "The accepted map look" in `RunSelfTests` (Program.cs).
- **#40 paste:** `GridPaste.cs` (Ctrl+V / Shift+Insert fill down and across; editor paste via `ClipboardText`). Wired in `QuickAddForm`.
- **#40 edit address:** `HostEditDialog` (Program.cs), `MainForm.EditHost` / `SaveHostEdit` (HostListEditing.cs), same-site address change in `SaveManagedHosts` (HostManagement.cs). Menu item "Edit host…", F2.
- **#40 startup:** `StartupRegistration.cs` (HKCU Run value "Ping Watchdog", `--startup`), Settings → General → Startup checkboxes, `UserPreferences.OpenWallboardOnStartup` / `WallboardScreen`, `MainForm.WallboardTargetScreen`, `RecordWallboardScreen`. Uninstall removes the Run value (`OnBeforeUninstallFastCallback`).
- **#40 loading:** `ResponsivenessGuard.cs` (watchdog thread, 400 ms cover, log over 1 s to `StorageLayout.LogDirectory\responsiveness.log`), `UiActivity.Begin(...)` around StartMonitoring, RefreshGrid, OpenWallboard.
- **#35 quiet update:** `ApplyPendingUpdateAndRestart` calls `WaitExitThenApplyUpdates(..., silent: true, restart: true)`; `PendingUpdateVersion` preference; `CheckAppliedUpdate()` on start with `UpdatePolicy.IsInstalled`.
- **Version overlay:** `VersionOverlay.cs`, Settings → Advanced → Testing.
- **Copy:** Settings descriptions only where needed (`Section` skips empty descriptions); History shows sentence-case event kinds (`EventHistoryForm.KindLabel`).
- **Docs:** new DECISIONS.md, NO_LIST.md, DESIGN.md (project), CONVENTIONS.md, CREDITS.md, TESTING.md; AGENTS.md rewritten; old Aurora design archived in `archive/`.

## What's next

**Claude:** split `Program.cs` (models, dialogs, self-tests and UI smoke test into their own files; behavior unchanged); landing page and README screenshots in the new look; measured design checks in the smoke test. See ROADMAP.md.

**Joseph:** the checks in TESTING.md, and the questions in ROADMAP.md → Joseph's list.
