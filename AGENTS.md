# Working agreement

Ping Watchdog for Windows (repo root, WinForms) and Linux (`linux/`, Avalonia). Two native apps with the same look, features and configuration format. Keep their code separate: a Linux change never touches Windows files, and the reverse. The Linux project compiles two framework-free Windows files unchanged (`HostImport.cs`, `DisplayTime.cs`); changing either rebuilds both.

## Current scope

Both platforms are in maintenance mode. Windows v1.19.32 is the accepted baseline (2026-11-13). Linux 1.16.0 (2026-11-13) matches it; Linux releases are published as separate UNTESTED pre-releases until Joseph runs one. Respond to new/reopened bugs and security issues; no planned features or general redesign/refactor work. See TESTING.md for acceptance limits.

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

1. Bump the version of the platform you changed (patch for fixes and small changes, minor for features; 1.20.0 and other milestone numbers are Joseph's to call). Windows: `Version` in `PingWatchdog.csproj`. Linux: `Version` in `linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj` (its own 1.16.x line). Never reuse a published version.
2. Update the release notes (Windows: the step in `.github/workflows/build-windows.yml`; Linux: `linux/RELEASE-NOTES.md`), `CHANGELOG.md`, `HANDOFF.md` and `ROADMAP.md` in the same commit.
3. Validate on a branch first, read the log, and look at the screenshots it prints. Windows: `gh workflow run validate-windows.yml --ref <branch>`. Linux: push a `linux/<name>` branch; `validate-linux.yml` runs the suites at three scales and on Wayland, builds and exercises every package, and prints captures (`REVIEW_IMAGE_BEGIN…`). Locally, `linux/build-packages.sh validate` does the same. Then merge to `main`; the build workflows publish (Windows: updater-accessible release; Linux: the UNTESTED Linux pre-release).
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
- **Proprietary license:** `LICENSE` is proprietary by Joseph's choice. Retained during the authorized maintenance finalization; never relicense.

### Linux deviations

- **Desktop type:** the Linux desktop's own UI font (Quiet Precision B7 allows it for native apps) and a monospace fallback list for addresses and logs (`Tokens.Mono`). Geist isn't bundled.
- **Separate version line:** Linux is 1.16.x and Windows 1.19.x. Tags never collide (`vX-untested-linux`).
- **Untested Linux releases:** every Linux build ships as `UNTESTED — Linux builds for vX`, a pre-release that is never Latest, with the RELEASES.md 2.3 warning. When Joseph confirms a build, the next version ships as a tested Linux release (RELEASES.md 2.5) and the landing page can offer Linux.
- **Updates by package:** only the AppImage updates itself (Velopack `linux` channel). The .deb, .rpm, .tar.gz and Flatpak say where updates come from. Velopack's feed files keep their names (`releases.linux.json`, `PingWatchdog-X-linux-full.nupkg`, `-delta.nupkg`) because installed AppImages look for them; every other asset follows RELEASES.md Part 5.
- **Linux equivalents:** Ping in terminal instead of a command window; an XDG autostart entry instead of the Run key; `notify-send` and `pw-play`/`paplay` for alerts. No portable-storage mode, no Export monitoring batch (a Windows .bat), and no Loading cover.
