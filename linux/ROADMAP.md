# Ping Watchdog Linux roadmap

Windows is the feature reference. Work in this roadmap is confined to the Linux application and Linux workflows. Linux uses native desktop conventions and a CLI trace of equivalent Linux commands; it does not emulate CMD or PowerShell.

## Phase 1: correctness

- Complete stored-history queries independent of the bounded dashboard snapshot.
- Apply range, suspect, and site filters consistently to history and CSV.
- Show matching and stored event counts; retain access to events from removed sites.
- Preserve host drafts through timer refreshes, save on focus loss, and bind drafts to their original site in Main and Wallboard.
- Route Main, Settings, and Wallboard update actions through one confirmation and save/stop/apply path; resume monitoring if applying an update fails.
- Detach Settings, Organization, and Main event subscriptions on close.
- Gate releases on core regressions and real Avalonia desktop regressions under Xvfb.

Completed and validated on October 3, 2026. Core and real desktop regression suites pass in Linux CI. The real AppImage updater round trip remains part of Phase 5.

## Phase 2: main application parity

Completed and validated on October 3, 2026: host label edit/clear, native configuration import/export, Linux tray menu and minimize preference with a safe taskbar fallback, individually colored CLI entries in Main and Wallboard, and persistent site/host/trace collections with keyed selection. Invalid imports leave the current setup intact; timing edits survive refreshes. Core and desktop regressions pass under Xvfb/Openbox with a Linux session bus. The desktop-specific tray rendering and AppImage updater round trip remain Phase 5 verification.

## Phase 3: Wallboard parity

Implemented: native site add/rename/delete and organization, a complete stable live host list with label edit/clear, timing/threshold controls that preserve drafts and lock while monitoring, shared native config actions, active outages with durations, recent stored-history queries and filters, Watchdog dog branding, monitor cycling, and shortcuts that respect editors. The topology follows site scope, scrolls for larger configurations, and explicitly counts hosts beyond its twelve nodes per site.

Validation pending: core and real desktop regressions, then Linux beta release. Physical monitor moves and desktop-specific behavior remain Phase 4/5 verification.

## Phase 4: desktop hardening

Expand the Phase 1 regression runner into full smoke/layout tests across sizes and scaling levels. Cover every window, branding, topology, monitoring transitions with injected probes, and all operations. Verify X11 and Wayland behavior on supported desktops.

## Phase 5: packaging/runtime verification

Launch the produced AppImage in clean environments, verify notifications and tray behavior on common desktops, test an updater round trip, and replace CI source rewriting with an explicit Linux update-channel setting.

## Phase 6: leave beta

Only after parity and package verification pass: consider ARM64 and additional package formats.
