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

Implementation is complete; the Linux CI runs must pass before this phase is shipped. The real AppImage updater round trip remains part of Phase 5.

## Phase 2: main application parity

Host label edit/clear, configuration import/export, Linux tray integration and preference, individually colored CLI entries, and stable list selection/scroll during refresh.

## Phase 3: Wallboard parity

Richer site operations, live host table, labels, timing/threshold controls, import/export, active outages and recent history, dog branding, monitor switching, complete shortcuts, and an overflow indicator for sites with more than 12 hosts.

## Phase 4: desktop hardening

Expand the Phase 1 regression runner into full smoke/layout tests across sizes and scaling levels. Cover every window, branding, topology, monitoring transitions with injected probes, and all operations. Verify X11 and Wayland behavior on supported desktops.

## Phase 5: packaging/runtime verification

Launch the produced AppImage in clean environments, verify notifications and tray behavior on common desktops, test an updater round trip, and replace CI source rewriting with an explicit Linux update-channel setting.

## Phase 6: leave beta

Only after parity and package verification pass: consider ARM64 and additional package formats.
