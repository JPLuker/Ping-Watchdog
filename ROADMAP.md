# Ping Watchdog roadmap

Windows. Linux is a separate beta track ([linux/ROADMAP.md](linux/ROADMAP.md)).

**Where it is:** feature-complete. Since 2026-11-11 Ping Watchdog is the test project for the new standards: Quiet Precision look, everything else the standards recommend, all current functionality kept. 1.20.0 (the maintenance release) is Joseph's to call.

Ordered by who can do the work: Claude's list first, then Joseph's. Ideas go here; bugs get fixed right away. Things Joseph said no to are in [NO_LIST.md](NO_LIST.md).

## Claude's list

1. Standards pass (1.19.29 and 1.19.30):
   - [x] #40 and #41 fixes (paste labels, edit address, startup and Wallboard screen, loading cover, whole addresses)
   - [x] #35 updates install without Velopack's window
   - [x] Quiet Precision tokens, buttons, tables, status dots, focus ring
   - [x] Say-less copy pass in Settings and Wallboard
   - [x] Version overlay (Settings → Advanced)
   - [ ] Quiet Precision on the remaining windows: Outage history, Organization, Quick Add, update popup, Wallboard header
   - [x] Split `Program.cs` and `WallboardForm.cs` by feature, behavior unchanged
   - [x] Rename `Aurora*` types to theme names
   - [x] Landing page in Quiet Precision with new screenshots (`docs/`)
   - [x] Release notes and README screenshots for the new look
   - [x] Measured design checks in the UI smoke test (one primary, 24px targets, 12px floor, label contrast)
   - [x] Every Windows source file under 800 lines (1.19.31)
2. Linux beta in Quiet Precision (`work/linux-qp`): palette, light/dark, font floor and copy done; layout parity with Windows (connected map, Map/List workspace) next.
   - [x] Dark scrollbars on tables whose scrollbars are created after theming

## Joseph's list

Each answerable in a word. Recommendation first.

1. **Private vulnerability reporting:** turn it on in the repo's Security settings (you said you will).
2. **1.20.0:** call it when the acceptance checks below pass on your machines.

Answered 2026-11-11: Linux moves to Quiet Precision; the proprietary license stays (DECISIONS.md).

## Acceptance before 1.20.0 (needs a person on real hardware)

- [ ] Install and run the exact candidate; fresh installer and portable tests.
- [ ] Upgrade from v1.14.81 and the latest iterative build with all data and snoozes kept; an update installs without a window and the app reopens.
- [ ] 100/125/150/200% scaling, large text, keyboard-only use, screen reader, high contrast, mixed-DPI displays.
- [ ] A full workday of monitoring after the last substantive change, including outages, recovery, snooze expiry and Wallboard on a second screen at sign-in.

## Shipped since 1.19.28

See [CHANGELOG.md](CHANGELOG.md). History before 2026-11-11 is in [IMPLEMENTATION-HISTORY.md](IMPLEMENTATION-HISTORY.md) and [RELEASE-AUDIT.md](RELEASE-AUDIT.md).
