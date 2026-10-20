# Ping Watchdog roadmap to maintenance

Current published Windows build: **v1.18.6**.
Proposed final feature release: **v1.19.0**, subject to acceptance and intervening version bumps.
Current stage: config/preference protection, Settings save feedback, host keyboard/accessibility, snooze correction and About/support information implemented. Remaining supporting UI/data/release work and manual acceptance remain open.

The accepted dashboard and connected topology remain the visual baseline. Features are frozen. Work is limited to finishing existing functionality, correctness, accessibility, data safety, release security and documentation.

## Source of truth

- [Maintenance checklist #4](https://github.com/JPLuker/Ping-Watchdog/issues/4): current execution and handoff.
- [RELEASE-AUDIT.md](RELEASE-AUDIT.md): verified evidence, concrete gaps, decisions and pending tests.
- [DESIGN.md](DESIGN.md): Aurora Glass v1.5 defaults; [AGENTS.md](AGENTS.md) records intentional deviations.
- [IMPLEMENTATION-HISTORY.md](IMPLEMENTATION-HISTORY.md): earlier implementation/validation checkpoints. Historical panning, controls-toggle and old layout descriptions are not current requirements.

## 1. Standards audit — complete

- [x] Read the ten current InternalDesignGuidelines documents.
- [x] Inspect Windows source, release workflow, package metadata and repository structure.
- [x] Separate existing CI/render evidence from source findings and unperformed human acceptance.
- [x] Record approved design deviations and preserve the proprietary license.
- [x] Open the maintenance checklist and establish implementation order.

## 2. Data protection and essential access — next

- [x] Atomic config saves, three rolling backups, visible write errors and damaged config/site-list protection (v1.17.4). Preferences/history and Settings save feedback remain separate.
- [x] Reject newer unsupported configs before applying/rewriting; test original preservation and backup retention (v1.17.4). No schema migration was introduced; future migrations still need their own acceptance.
- [x] Keyboard and accessible topology/outage host actions; full identity/state in accessible names and keyboard details, with distinct marker shapes (v1.18.2). Real screen-reader acceptance remains pending.
- [ ] Readable font floor, long addresses/IPv6 and constrained-height behavior.

## 3. Supporting UI and data controls

- [ ] Complete Settings/Hosts/History/Organization/dialog/About/updater/recovery review in both themes.
- [x] Preserve staged Settings save/discard/cancel and Ctrl+S with visible persistence failure feedback (v1.18.4).
- [x] Add Settings > About, bundled license review, report-issue access and reviewable diagnostic copying (v1.18.6).
- [ ] Complete dialog ownership, focus, keyboard and independent recovery acceptance across remaining supporting windows.
- [ ] Complete backup/restore, portable storage, open-data-folder and deliberate data deletion.

## 4. Release and security hardening

- [ ] Immutable Windows Actions, tag-driven version-checked releases, dependency governance.
- [ ] Checksums, complete asset checks, unsigned notice and clear downloads.
- [ ] Verify normal/recovery updater integrity, package-specific behavior, daily checks and managed controls.
- [ ] Verify dependency vulnerabilities/licenses and owner-managed repository security settings.

## 5. Public readiness

- [ ] Current README/quick start/changelog/screenshots.
- [ ] Privacy/network disclosure, SECURITY.md, issue templates, support and maintenance policy.
- [ ] Truthful accessible landing page with tested Windows downloads.
- [ ] Final confirmation of the existing proprietary license and required notices.

## 6. Candidate acceptance and final release

- [ ] Human install/run of the exact candidate; fresh installer and portable tests.
- [ ] Upgrade from v1.14.81 and latest iterative build, preserving all data and snoozes; test failure/retry.
- [ ] Physical scaling, large text, keyboard, screen reader, contrast and mixed-DPI checks.
- [ ] Full-workday monitoring trial after the last substantive change.
- [ ] Fix blocking defects; publish accepted candidate, verify packages/update discovery and announce maintenance mode.

Passing iterative builds continue to publish for Joseph to test. A source audit, screenshots, version number or empty bug list does not establish final acceptance. Documentation-only changes need no binary release.

## Scope

Windows only in this chat. Linux remains a separate beta track under linux/ROADMAP.md. Do not change shared presentation to force a Windows design migration onto Linux. No cross-promotion, upgrade prompts or new diagnostic modules. Maintenance covers crashes, data loss, incorrect monitoring/notifications, security, compatibility, installation/updating and material accessibility defects.


## Host access and snooze correction — 2026-10-20

Iterations v1.18.0–v1.18.2 add keyboard selection and context actions for topology/outage hosts, per-host accessible objects, visible focus and state-specific marker shapes. Native themed controls below the reading floor are normalized, including independent recovery text.

Joseph clarified snooze semantics: pause the host's probes, hide it from active outages and live command output for the hour, then resume automatically. The worker is cancelled without restarting unrelated hosts; late results are rejected, saved snoozes are honored, and an all-snoozed session stays alive for expiry. Historical events remain available. Menus now say Snooze host / Resume monitoring.

New regression coverage exercises worker cancellation, late-result isolation, expiry, manual resume, saved snoozes, all-snoozed sessions, keyboard and accessible action targets, stale host removal and snoozed outage filtering. Windows runs 39487281012 and 39488095116 passed storage/snooze/accessibility regressions, published startup/UI, independent recovery and package/publication gates. An intermediate v1.18.1 run passed its tests/packaging but GitHub rejected publication with HTTP 403; the final v1.18.2 release published successfully and was verified as latest with installer, portable and update feeds. Rendered light/dark Main/Hosts/Settings and dense topology were inspected; the final sparse-map focus capture was inspected. Physical screen-reader/DPI acceptance remains open. The final-release target moves to a later minor because this behavior/accessibility iteration uses 1.18.0; maintenance acceptance remains open.


## Settings persistence and navigation — 2026-10-20

v1.18.4 persists configuration and user preferences before applying Settings to the live session. Failure keeps the draft, Save changes and unsaved-close protection intact, with an inline error and an on-demand Details action. Preference reads no longer replace damaged/unsupported files with defaults; saves validate originals and use rolling atomic backups. If config commit fails after preferences were written, the previous preferences are restored where possible, with explicit failure reporting if restoration also fails.

Settings category navigation now uses shared selected/focus styling, Display is renamed Appearance, and redundant page subtitles are removed. Native Enter/Escape behavior complements Ctrl+S and staged save/discard/cancel.

Coverage includes locked destinations, real config-commit failure and preference restoration, malformed/newer preference preservation, failed-save draft retention and successful retry, plus both-theme save-error captures at a constrained width. Windows run 39490797950 passed persistence/retry regressions, published startup/UI, recovery, packaging and publication. Normal 900×650 and save-error 760×650 Settings captures were inspected in both themes; error controls fit. v1.18.3 was withheld after the new UI review ran before Shown initialized its draft; the corrected test waits for initialization and keeps the bounds checks. v1.18.4 installer, portable, full/delta packages and feeds were verified.


## About and reviewable support information — 2026-10-20

v1.18.6 makes Settings > About the single home for installed version and license access; the existing About menu opens that page. The license viewer reads the exact embedded proprietary LICENSE. Report an issue opens the repository issue chooser in the system browser, with visible failure feedback.

Diagnostic information is generated from an explicit allowlist of assembly build identity, Windows/.NET/architecture and data/recovery locations. It does not read inventory, settings contents, history or recovery reports. An owned review window shows exactly what Copy diagnostic info copies and warns that paths can include the Windows username. No automatic upload occurs. Clipboard errors remain visible, with selection/Ctrl+C available; Escape closes the review. Existing staged Settings edits are preserved when opening About.

Automated checks cover the bundled license, report fields, exact copying and clipboard failure. Both-theme About and diagnostic captures were inspected. Windows run 39492663542 passed self-tests, published startup/UI, independent recovery, packaging and publication. Installer, portable package and full/delta/feed assets were verified for v1.18.6.

The first v1.18.5 review exposed full-width support buttons from the Settings page fitter. v1.18.6 groups them inside a compact action container; both versions passed their build gates and were published, with v1.18.6 the final reviewed update.

This is the support-information batch, not acceptance of every supporting dialog, third-party notices, actual screen-reader/DPI behavior or the final maintenance release.
