# Testing and acceptance

## Linux 1.16.0: automated only, not yet run by a person

Linux releases are published as separate UNTESTED pre-releases until Joseph installs and runs one (RELEASES.md Part 2). No person has run 1.16.0 yet. The device checks are in issue #46.

What CI proved (validation run [43540639276](https://github.com/JPLuker/Ping-Watchdog/actions/runs/43540639276), release run [43541098084](https://github.com/JPLuker/Ping-Watchdog/actions/runs/43541098084), published as [v1.16.0-untested-linux](https://github.com/JPLuker/Ping-Watchdog/releases/tag/v1.16.0-untested-linux)):

- Engine and desktop-service regressions (145 checks): monitoring transitions, stale probes, history and CSV, Windows-configuration compatibility, host editing and Undo, Quick Monitor, snooze/disable/priority probe rules, backups and restore, Delete saved data keeping foreign files and link targets, autostart, clipboard parsing, theme contrast, map aggregation, the priority alarm.
- The real windows under Xvfb at 100%, 150% and 200% render scale and under headless Weston (native Wayland): map fit and whole addresses (including a crowded 30-host site), List filters, search and columns, host menus, Undo, dialogs, staged Settings, appearance switching, History, Organize sites, the Wallboard, the priority bar, the update popup and tray fallback. Light and dark captures were reviewed.
- Packages: the AppImage's own self-test and desktop suite; the .deb installed with apt, run and removed; the .rpm's binary byte-identical to the tested build and run; the .tar.gz run from its folder; the Flatpak installed and run in its sandbox.

Not established by CI:

- Real desktop integration on elementary OS, GNOME or KDE: tray icon, notifications, the alarm sound, sign-in autostart, the app-menu entry and icon.
- An AppImage update from one Linux release to the next on a real machine.
- Physical multi-monitor Wallboard placement, mixed DPI, screen readers (AT-SPI) and real ICMP on a desktop network (CI uses a scripted probe for the UI suite).
- Restoring a backup across the relaunch on a real desktop.


## Accepted Windows baseline: v1.19.32

Joseph reported on 2026-11-13:

- "Yeah looks like all of that is good" after the crowded-site captions, resize/Wallboard and host-action checks.
- "Honestly I used it all day yesterday on this build I think we are good."
- "Okay, let's do it" to final release housekeeping.

This records successful daily use and the focused map checks. Exact Windows version, package type and DPI were not supplied. No additional routine test request is pending.

## Automated evidence

- Windows validation run [43212934329](https://github.com/JPLuker/Ping-Watchdog/actions/runs/43212934329): dependency checks, self-tests and UI smoke tests passed. The old renderer failed the crowded-map regression with 0 of 68 addresses; the fix passed repeated resizing and outage-sidebar checks.
- Light/dark full-caption, populated map and Wallboard captures were inspected.
- Release run [43213403550](https://github.com/JPLuker/Ping-Watchdog/actions/runs/43213403550) passed and published v1.19.32.

## Not independently established by that acceptance

- Fresh installer and portable installations, upgrade from v1.14.81, and data retention in each package mode.
- Every 100/125/150/200% DPI, mixed-DPI display, high-contrast and screen-reader combination.
- Real backup/restore interruption, foreign-file deletion protection and physical power-loss recovery.
- Startup on a second screen after sign-in, loading-cover composition during an actual freeze, and every silent-update failure path.

CI coverage is not device coverage. WinForms DrawToBitmap does not capture every overlapping native window. Retain these limits when reporting support or investigating future issues; do not infer they failed or passed.
