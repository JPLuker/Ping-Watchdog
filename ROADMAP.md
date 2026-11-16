# Ping Watchdog roadmap

## Windows: maintenance mode

Accepted baseline: **v1.19.32**, 2026-11-13. Joseph accepted the map/Wallboard fixes, reported a full day of use and authorized finalization. Keep the tested release and existing proprietary license. No new version is needed for the maintenance designation.

No planned feature work. Fix reported bugs, security problems, data-loss risks and compatibility regressions. Do not restart the accepted UI redesign or pursue general refactors without a reported need. Support policy: [SUPPORT.md](SUPPORT.md).

## Linux: maintenance mode

Linux 1.16.0 (2026-11-13) established parity with Windows v1.19.32; Linux 1.16.1 followed on 2026-11-14. Joseph accepted Linux after a full day of real-device use on 2026-11-16. The Linux app includes: Quiet Precision, the connected map, the List and host actions, priority alarms, staged Settings and data tools, plus AppImage, .deb, .rpm, .tar.gz and Flatpak packages. Same maintenance policy as Windows. Older Linux UNTESTED releases retain their historical labels. Future tested release publication follows the standards; acceptance limits are documented in [TESTING.md](TESTING.md).

## Final combined release — pending

Issue [#47](https://github.com/JPLuker/Ping-Watchdog/issues/47) tracks a single Windows-and-Linux release. Do not mark it complete until verified assets, checksums, update feeds and release flags are published. The website now lists both operating systems, with Linux downloads pointing to the releases index until final links exist.

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
