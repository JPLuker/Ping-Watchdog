# Ping Watchdog Linux roadmap

## Maintenance mode

From 1.16.0 (2026-11-13) the Linux build matches the accepted Windows v1.19.32 baseline and joins it in maintenance mode: no planned features, but reported bugs, security problems, data-loss risks and compatibility regressions get fixed. Joseph chose full parity before maintenance: "Ping Watchdog just needs its linux build in line then it's going into maintenance mode."

Linux releases ship as separate **UNTESTED** pre-releases until a person has installed and run that build (RELEASES.md Part 2). When Joseph confirms a build on his machine, the next version is published as a tested Linux release and that confirmation is recorded in [TESTING.md](../TESTING.md).

## Fixed after release

- 1.16.1: Quick Add preview cells editable, as on Windows (first Lappy note).

## Done for 1.16.0

- Quiet Precision in light and dark, following the desktop setting by default; the connected map with whole addresses; the Windows sidebar, header and Map/List layout.
- List: search, filter chips (All, Priority, Offline, Disabled, Snoozed), multi-select, sortable columns, Columns menu, right-click actions, F2, Delete, Ctrl+Z.
- Host actions: edit address and label, group, Ping in terminal, snooze, priority, enable/disable, move to site, per-host monitoring, keep temporary hosts, remove.
- Priority alarms that repeat every 15 seconds until acknowledged, on the main window and the Wallboard.
- Quick Add and Quick Monitor with pasted rows, HTML clipboards, CSV, CIDR blocks and ranges.
- Staged Settings (nine pages), sign-in autostart, backups, restore, Delete saved data, diagnostic info, license and notices.
- Display-only Wallboard that opens on the screen it was last on.
- The Windows `HostDetails` configuration shape, so options survive moving between builds.
- AppImage (self-updating), .deb, .rpm, .tar.gz and Flatpak with standard names, notices, checksums and a required-asset check.

## Waiting on a real machine

These need a person on a Linux desktop; CI can't certify them. One pass on Lappy (elementary OS) covers most:

1. Install the .deb, open Ping Watchdog from the app menu, add a few hosts, start monitoring.
2. Let a host go offline: notification, priority alarm sound, Acknowledge.
3. Minimize to the tray and restore; open the Wallboard and press Esc.
4. Turn on **Open Ping Watchdog when you sign in**, sign out and in.
5. Run the AppImage once and let it update to the next release.

## Not planned

ARM64 builds, a Flathub listing and signed packages stay out unless a concrete need comes up.
