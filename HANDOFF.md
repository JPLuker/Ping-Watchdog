# Handoff

## The project

Ping Watchdog watches IP addresses and hostnames with ICMP ping, grouped by site, and shows outages and recoveries on a Map, a List and a full-screen Wallboard. Windows, .NET 8 WinForms, installed and updated through Velopack. Feature-complete; since 2026-11-11 it's the test project for Joseph's new standards (Quiet Precision look plus everything else the standards recommend, functionality unchanged).

## Rules box

- Keep building until done or blocked on Joseph (standards SOP Part 1).
- Ask before any visual change beyond Quiet Precision as recorded in AGENTS.md → Identity.
- Every push bumps the version; passing pushes to `main` publish a release Joseph updates to from the app.
- Proof before claims: tests plus captures you've looked at. The validation workflow prints captures to its log (`REVIEW_IMAGE_BEGIN…`); artifacts aren't reachable from the agent sandbox.
- Proprietary license stays. Windows and Linux stay separate.

## Maintenance baseline (2026-11-13)

**Windows v1.19.32 is accepted and in maintenance mode.** Joseph confirmed the crowded-map, resize/Wallboard and host-action checks, then said: "Honestly I used it all day yesterday on this build I think we are good." He authorized finalization with "Okay, let's do it". Keep the tested binaries and tag; no rebuild or version bump for this documentation-only transition. The existing proprietary license is retained.

#42/#43 fixes are in `AdaptiveTopology.cs`: measure complete captions, give busy site rows more height, then scale the map and hit regions together. Validation run 43212934329 passed; the previous renderer failed with 0/68 addresses. Light/dark captures were reviewed. Release run 43213403550 succeeded, and all seven release assets were verified present. Source: `4c0cd57ad32976fe30775571877d5100e3d9dd0a`.

Private vulnerability reporting is enabled: the public Security page exposes Report a vulnerability (verified 2026-11-13). No new monitoring features, styling passes or general refactors are planned. Handle new/reopened bugs and security reports. Linux remains separate.

`TESTING.md` records acceptance and its limits. Do not claim fresh-install, every DPI, Narrator or backup/restore device coverage from the general full-day test. Third-party notices remain bundled; no license or third-party contractual terms were changed.

## Where we are (2026-11-11)

Standards pass, released as **1.19.29** (features and look) and **1.19.30** (code split, design checks, landing page):

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

- **1.19.30:** `Program.cs` is now `Program`, `Models`, `HostDialogs`, `MainForm` and `MainForm.*.cs` feature partials (Sites, Organization, Monitoring, History, WallboardBridge, SettingsBridge, Updates, PingLog, LegacyGridMenu, SelfTests, UiSmokeTest). `WallboardCanvas.cs` split from `WallboardForm.cs`. `DesignChecks.cs` runs in the UI smoke test. The smoke test also renders `page-map-*` / `page-list-*` (example network, monitoring) used for `docs/` and README screenshots. Landing page CSS uses the Quiet Precision tokens.

**Next:** respond to new/reopened issues under SUPPORT.md.

**Acceptance record:** TESTING.md.
