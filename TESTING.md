# Testing and acceptance

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
