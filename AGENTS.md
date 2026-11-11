# Working agreement

Standalone Ping Watchdog for Windows. Linux is a separate beta track under `linux/`; keep Windows work separate unless Joseph changes the scope.

## Read first, every session

1. The standards in [InternalDesignGuidelines](https://github.com/JPLuker/InternalDesignGuidelines): README, then SOP.md Part 1 (keep building until done or truly blocked), WORKING-WITH-JOSEPH.md and WORKFLOW.md. Read the file for the kind of work you're doing (README table).
2. Open GitHub issues. They're the handoff between sessions. Close an issue once the fix is pushed and proven, with the closing message from WORKFLOW.md 1.2 (version and Eastern timestamp).
3. [HANDOFF.md](HANDOFF.md), then [DECISIONS.md](DECISIONS.md) and [NO_LIST.md](NO_LIST.md). Don't make Joseph repeat himself.
4. [DESIGN.md](DESIGN.md) before any UI work, and [CONVENTIONS.md](CONVENTIONS.md) before code.

Keep the product, docs and release materials about Ping Watchdog only: no cross-promotion, external-product references, promotional cards or upgrade prompts.

## Identity

- Visual style: Quiet Precision (DESIGN.md Part B of the standards), approved by Joseph 2026-11-11: "use the new visual style, redo literally anything that the standards recommended as long as you maintain the functionality of the app"
- Accent: ink (light `#1B1B19` / dark `#EDEDEA`). Color is reserved for host state, so no hue competes with green/amber/red.
- Neutrals: warm (default)
- Semantic shifts: none
- Display font: none (Segoe UI Semibold for titles)
- Shape: default (buttons 8px, cards 12px, dialogs 16px; pills only for filter chips)
- Density: default; tables at 36px rows
- Signature detail: the connected map (Monitor → sites → hosts)

## Every push

1. Bump `Version` in `PingWatchdog.csproj` (patch for fixes and small changes, minor for features; 1.20.0 and other milestone numbers are Joseph's to call). Never reuse a published version.
2. Update the release notes step in `.github/workflows/build-windows.yml`, `CHANGELOG.md`, `HANDOFF.md` and `ROADMAP.md` in the same commit.
3. Validate on a branch first (`gh workflow run validate-windows.yml --ref <branch>`), read the log, and look at the screenshots it prints. Then merge to `main`; the build workflow publishes an updater-accessible release with installer, portable package and update feed.
4. Confirm the push landed (`git ls-remote origin refs/heads/main`) and the release published. Documentation-only changes need no binary release.

Report actual validation and its limits honestly. CI captures are "rendered on the CI runner", not device tests.

## Deviations

From the standards' defaults, on purpose. Don't "fix" these.

- **Native Windows type and chrome:** Segoe UI and Consolas, native window chrome, opaque surfaces. Quiet Precision allows the system UI font for native apps (B7); Geist isn't bundled.
- **Permanent update action:** Joseph asked for Check for updates in the sidebar and the bottom-right update popup. Keep both rather than moving them into Settings.
- **Fitted connected topology:** every selected host is visible on the map without scrolling, in the accepted connected branch style. Fix density, long values, keyboard access and non-color state; don't bring back the radial graphics or redesign the map.
- **Display-only Wallboard:** no visible toolbar and no Show/Hide controls toggle. Full window, Escape/F11 exit, keyboard shortcuts and host context actions stay.
- **Staged Settings:** Save changes, Ctrl+S and save/discard/cancel protection stay for the grouped settings form. Persistence failures are never reported as saved.
- **Focused Windows distribution:** the Velopack installer, portable ZIP and update feed keep their current names (`PingWatchdog-win-Setup.exe`, `PingWatchdog-win-Portable.zip`, feed files), because the updater and existing installs depend on them. No MSI or extra formats without a concrete need.
- **Push-to-main publication:** passing pushes to `main` publish a release (with a version-uniqueness guard) instead of tag-triggered releases, so Joseph can update from inside the app after every build.
- **Proprietary license:** `LICENSE` is proprietary by Joseph's choice. Final confirmation is on his list; never relicense.
