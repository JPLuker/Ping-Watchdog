# Ping Watchdog roadmap

## Windows: maintenance mode

Accepted baseline: **v1.19.32**, 2026-11-13. Joseph accepted the map/Wallboard fixes, reported a full day of use and authorized finalization. Keep the tested release and existing proprietary license. No new version is needed for the maintenance designation.

No planned feature work. Fix reported bugs, security problems, data-loss risks and compatibility regressions. Do not restart the accepted UI redesign or pursue general refactors without a reported need. Support policy: [SUPPORT.md](SUPPORT.md).

## Linux: maintenance mode

Linux 1.16.0 (2026-11-13) matches the Windows v1.19.32 baseline: Quiet Precision, the connected map, the List and host actions, priority alarms, staged Settings and data tools, plus AppImage, .deb, .rpm, .tar.gz and Flatpak packages. Same maintenance policy as Windows. Linux releases stay separate UNTESTED pre-releases until Joseph runs one; the checks that need a real desktop are listed in [linux/ROADMAP.md](linux/ROADMAP.md).

## Completed release work

- Published v1.19.32 with full topology captions and aligned host actions (#42/#43).
- Passed Windows dependency, self-test and UI validation; inspected light/dark captures.
- Joseph accepted the focused map checks and full-day use.
- Verified installer, portable ZIP, updater packages/feeds and checksum asset are present.
- Verified private vulnerability reporting is available.
- Retained the proprietary license and bundled third-party notices.

## Coverage limits

The general daily-use acceptance does not establish every fresh-install/portable, old-version upgrade, DPI, screen-reader or recovery scenario. These remain documented in [TESTING.md](TESTING.md), not silently marked passed.

## Deferred work

Further visual restyling and code-size refactors are not scheduled for either platform. Linux details: [linux/ROADMAP.md](linux/ROADMAP.md).

History: [CHANGELOG.md](CHANGELOG.md), [IMPLEMENTATION-HISTORY.md](IMPLEMENTATION-HISTORY.md), [RELEASE-AUDIT.md](RELEASE-AUDIT.md).
