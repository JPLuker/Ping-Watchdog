# Testing

Checks for this build only (at most 15). Each is something CI can't prove: it needs your machine.

**Build:** candidate 1.19.32. Use **Check for updates** after publication.

1. Map and Wallboard: full site names, labels and IPs stay visible while resizing, with multiple endpoints and during outages.
2. Right-click/select a host after the map scales. Confirm the menu belongs to that host.

## Earlier acceptance checks still pending

**Build:** the next release after 1.19.28 (the standards pass). Use **Check for updates** in the sidebar; it installs over your current copy.

1. The update installs with no separate window or OK button, and Ping Watchdog reopens on the new version.
2. The look: main window, List, Settings and Outage history in Light and Dark. Anything that reads worse than before?
3. Map: every IP address shows in full, at your usual window size and on the Wallboard.
4. Add hosts: paste a column of labels into the Label column with Ctrl+V.
5. Right-click a host → **Edit host…**: change its address. Label, group and priority stay; its old address is gone.
6. Settings → General → Startup: turn on all three, restart the laptop. Ping Watchdog opens, starts monitoring, and the Wallboard opens on the screen it was on.
7. If the window ever freezes: a dimmed "Loading" cover appears instead of a frozen screen. Send `Logs\responsiveness.log` from Settings → About → Copy diagnostic info (the path is listed).
8. Settings → Advanced → **Show version on all screens** puts the version in the bottom-left corner of every window, and it can't be clicked.

## Never run on real hardware yet

- Start with Windows after a real sign-in, and the Wallboard on a second screen at startup.
- The loading cover during a real freeze (CI checks the log, not the cover, because the runner has no foreground window).
- Silent update install end to end (CI tests the version check, not Velopack's install).
