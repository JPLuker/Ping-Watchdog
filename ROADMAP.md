# Ping Watchdog roadmap to maintenance

Current published Windows build: **v1.17.4** (v1.18.0 in validation).
Proposed final feature release: **v1.19.0**, subject to acceptance and intervening version bumps.
Current stage: standards audit and first configuration-safety batch complete. Essential accessibility, remaining data/UI work and manual acceptance remain open.

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
- [ ] Keyboard and accessible topology/outage host actions; full identity/state independent of color.
- [ ] Readable font floor, long addresses/IPv6 and constrained-height behavior.

## 3. Supporting UI and data controls

- [ ] Complete Settings/Hosts/History/Organization/dialog/About/updater/recovery review in both themes.
- [ ] Preserve staged save/discard/cancel, Ctrl+S, dialog ownership, focus, keyboard and independent recovery.
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

Candidate v1.18.0 adds keyboard selection and context actions for topology/outage hosts, per-host accessible objects, visible focus and state-specific marker shapes. Native themed controls below the reading floor are normalized, including independent recovery text.

Joseph clarified snooze semantics: pause the host's probes, hide it from active outages and live command output for the hour, then resume automatically. The worker is cancelled without restarting unrelated hosts; late results are rejected, saved snoozes are honored, and an all-snoozed session stays alive for expiry. Historical events remain available. Menus now say Snooze host / Resume monitoring.

New regression coverage exercises worker cancellation, late-result isolation, expiry, manual resume, saved snoozes, all-snoozed sessions, keyboard and accessible action targets, stale host removal and snoozed outage filtering. Windows validation/publication pending. The final-release target moves to a later minor because this behavior/accessibility iteration uses 1.18.0; maintenance acceptance remains open.
