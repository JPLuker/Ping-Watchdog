# Ping Watchdog roadmap

Windows. Linux is a separate beta track ([linux/ROADMAP.md](linux/ROADMAP.md)).

**Where it is:** feature-complete. Since 2026-11-11 Ping Watchdog is the test project for the new standards: Quiet Precision look, everything else the standards recommend, all current functionality kept. 1.20.0 (the maintenance release) is Joseph's to call.

Ordered by who can do the work: Claude's list first, then Joseph's. Ideas go here; bugs get fixed right away. Things Joseph said no to are in [NO_LIST.md](NO_LIST.md).

## Active scope: testing and final release prep (2026-11-12)

Reported GitHub issues and release blockers take priority. The remaining general standards/refactor work below is deferred unless it fixes a reported issue.
- Candidate 1.19.32: complete topology captions and scaling for #42/#43. Validate on Windows before release.

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
2. Split `WallboardCanvas.cs`, `WallboardForm.cs` and `MainForm.cs` below 800 lines as they're touched.
   - [x] Dark scrollbars on tables whose scrollbars are created after theming

## Joseph's list

Each answerable in a word. Recommendation first.

1. **Linux beta:** move it to Quiet Precision too? Recommended: yes, after Windows is accepted, so both look the same.
2. **License:** keep the current proprietary license for 1.20.0? Recommended: yes (it's your existing choice).
3. **Private vulnerability reporting:** turn it on in the repo's Security settings (only you can). Recommended: yes.
4. **1.20.0:** call it when the acceptance checks below pass on your machines.

## Acceptance before 1.20.0 (needs a person on real hardware)

- [ ] Install and run the exact candidate; fresh installer and portable tests.
- [ ] Upgrade from v1.14.81 and the latest iterative build with all data and snoozes kept; an update installs without a window and the app reopens.
- [ ] 100/125/150/200% scaling, large text, keyboard-only use, screen reader, high contrast, mixed-DPI displays.
- [ ] A full workday of monitoring after the last substantive change, including outages, recovery, snooze expiry and Wallboard on a second screen at sign-in.

## Shipped since 1.19.28

See [CHANGELOG.md](CHANGELOG.md). History before 2026-11-11 is in [IMPLEMENTATION-HISTORY.md](IMPLEMENTATION-HISTORY.md) and [RELEASE-AUDIT.md](RELEASE-AUDIT.md).
