# Ping Watchdog roadmap to maintenance

Current published Windows build: **v1.19.5**.
Proposed final maintenance release: **v1.20.0**, subject to acceptance and intervening version bumps.
Current stage: config/preference protection, Settings save feedback, host keyboard/accessibility, snooze correction and About/support information implemented. Complete backup/restore and data-folder access are implemented. Portable storage and confirmed deletion are implemented. The map handles many sites without overlap and the History font floor is met. Remaining supporting UI, release work and manual acceptance remain open.

The accepted dashboard and connected topology remain the visual baseline. Wallboard now omits the visible top toolbar at Joseph's request; keyboard and host context actions remain. Features are frozen. Work is limited to finishing existing functionality, correctness, accessibility, data safety, release security and documentation.

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

## 2. Data protection and essential access — implemented; acceptance pending

- [x] Atomic config saves, three rolling backups, visible write errors and damaged config/site-list protection (v1.17.4). Preferences/history and Settings save feedback remain separate.
- [x] Reject newer unsupported configs before applying/rewriting; test original preservation and backup retention (v1.17.4). No schema migration was introduced; future migrations still need their own acceptance.
- [x] Keyboard and accessible topology/outage host actions; full identity/state in accessible names and keyboard details, with distinct marker shapes (v1.18.2). Real screen-reader acceptance remains pending.
- [x] Readable font floor, long addresses/IPv6 and constrained-height behavior (v1.18.14). Many-site map layout, full site names on hover, History text at the floor; physical DPI/large-text acceptance remains pending.

## 3. Supporting UI and data controls

- [ ] Complete Settings/Hosts/History/Organization/dialog/About/updater/recovery review in both themes.
- [x] Preserve staged Settings save/discard/cancel and Ctrl+S with visible persistence failure feedback (v1.18.4).
- [x] Add Settings > About, bundled license review, report-issue access and reviewable diagnostic copying (v1.18.6).
- [ ] Complete dialog ownership, focus, keyboard and independent recovery acceptance across remaining supporting windows.
- [x] Complete backup/restore including preferences and history, explicit replacement policy and journaled interruption recovery (v1.18.9; see DATA-RECOVERY.md).
- [x] Open the current data folder from Settings (v1.18.9).
- [x] Implement explicit portable folder-local storage and confirmed active-storage deletion, including interruption retry (v1.18.13; physical/package-update acceptance remains pending).

## 4. Release and security hardening

- [x] Immutable Windows Actions (commit-SHA pins), version-checked releases, dependency governance (Dependabot, vulnerable-package gate) (v1.19.4). Tag-driven publication is a recorded deviation; see RELEASE-AUDIT.md.
- [x] Checksums (SHA256SUMS.txt), complete asset checks, delta retry with visible warning, unsigned notice (v1.19.4). Landing-page downloads remain under public readiness.
- [x] Daily checks and managed controls (v1.19.5). Package-specific behavior and download verification are recorded deviations/evidence in RELEASE-AUDIT.md; a corrupt-download test remains open.
- [x] Dependency vulnerability gate (v1.19.4) and owner-managed security settings (Dependabot alerts and security updates, secret scanning, push protection; confirmed by Joseph). Third-party license review remains under public readiness.

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


## Complete backup and restore — 2026-10-20

v1.18.9 adds Settings > Data and storage, complete-backup creation, explicit replacement restore and Open data folder. DATA-RECOVERY.md defines included/excluded data, replacement and retention behavior. A complete backup covers all saved sites/folders and host metadata, enabled state and snoozes, saved preferences and outage history. Temporary monitoring sessions, command output, binaries and startup reports are excluded. Settings drafts are not exported.

Backups are validated before file replacement. Restore requires stopped monitoring, defaults its confirmation to No, respects Settings save/discard/cancel, replaces rather than merges, and restarts the application. Four destinations are updated: authoritative config, preferences, history and the legacy inventory. Closing after a successful restore suppresses autosave of the previous in-memory setup.

A flushed recovery journal stores exact original bytes and missing-file state before writes begin. Renaming that journal is the commit point. Failures attempt rollback; an interrupted transaction is rolled back at the very start of the next MainForm construction, before UI handlers, data loading or autosave. Failure to finish recovery retains the pending record and prevents normal startup. Successful originals and rollback records are retained for recovery, without automatic cleanup yet. This is journaled recovery across multiple files, not a claim that the filesystem atomically replaces four files together or that physical power-loss behavior was tested.

Tests exercise round-trip labels/categories/snooze/preferences/history, original byte preservation including damaged bytes, missing-original deletion on rollback, a commit failure after two writes, simulated interrupted-restore recovery, locked-file refusal, malformed/incomplete/newer backup refusal and live-data-folder export protection. A MainForm regression ensures a protected startup configuration cannot be exported as a fallback complete backup.

Windows run 39497671069 passed self-tests, published startup/UI, independent recovery, packaging and publication. Actual light/dark Data and storage captures were inspected. Installer, portable, full/delta packages and both update feeds were verified. v1.18.7's fixture escaping was corrected before the working v1.18.8 build; v1.18.8 published successfully, then v1.18.9 corrected the storage category mnemonic rendering and protected legacy-config fallback export.

Actual installed/portable restart-and-restore acceptance, multi-instance usage, physical power loss and final workday/DPI/screen-reader trials remain open. The portable ZIP still uses AppData; folder-local storage and deliberate full deletion are separate tasks.


## Portable storage, deletion and display-only Wallboard — 2026-10-21

v1.18.13 adds explicit opt-in folder-local storage. Existing copies keep their per-user AppData location until enabled. The current saved setup is staged and committed to a new Data folder before a portable marker activates it; original AppData remains intact. Existing destination data is refused, not merged. For Velopack layouts, the Data folder and marker are in the package root, outside current/app-* directories replaced by updates. Setup-installed copies cannot enable the feature through Settings. Preferences, backup/restore, support diagnostics, brand assets and startup reports share the active storage layout.

Delete saved data requires stopped monitoring, an exact typed DELETE confirmation with folder scope, and normal Settings draft protection. It closes the app without writing stale memory. Active data, rolling backups, restore journals/originals, startup reports and brand assets are removed; external backups, inactive storage copies and installation packages are retained. Linked files/folders are refused. A deletion intent outside the data directory survives partial failure and is completed before saved data or restore journals can load. An empty config prevents example hosts from returning. This is ordinary filesystem deletion, not secure erasure.

The startup guard recreates only fresh current-attempt diagnostics after a deletion retry, retaining the independent recovery/updater path if startup then fails. PORTABLE-AND-DELETION.md documents scope, retained copies, migration failures and retry behavior.

Joseph clarified that the visible Wallboard top toolbar must be removed. The canvas now occupies a single full-window content cell; keyboard shortcuts and host context actions remain. AGENTS.md records this direction, superseding the earlier wrapping-toolbar deviation.

Windows run 39615057833 passed self-tests, published startup/UI, independent recovery including a new process-level deletion-retry crash case, packaging and publication. Tests cover explicit routing, package-root/current layout, preserved originals, rejected destination collisions and installed-mode activation, unsupported portable markers, interrupted/locked deletion retry, empty post-deletion inventory, retained external/installation/inactive data, exact confirmation and cancellation. Light/dark Settings and deletion-confirmation renders, plus the toolbar-free Wallboard, were inspected. Installer, portable, full/delta packages and both feeds were verified.

The v1.18.10 UI gate withheld publication after finding the direct Form child did not settle to full-window bounds. v1.18.11 returned to a one-cell layout, passed/published and was inspected; v1.18.12 additionally protected recovery after a deletion retry. v1.18.13 corrects light-mode destructive text contrast and adds 4.5:1 text-pair tests for both themes and interactive surfaces. Human installed/portable migration, actual portable package updates, read-only drives, real concurrent copies, deletion/restart acceptance and the final desktop/workday trials remain pending.


## Many-site map, long names and font floor — 2026-10-22

v1.18.14 answers issue #5. Map sites now follow the site list order (folder, then name), so both views are alphabetical. The fitted topology keeps its accepted square-ish search but widens to more site columns when captions stop fitting, and row gutters shrink past three rows. When a window cannot label every site, site headings are dropped and hosts use the whole block, so blocks never overlap. Every host stays drawn and reachable by mouse, keyboard and accessible name.

The site list shows the full name on hover when a row is truncated. History event-type and header text was 8.5pt (about 11px, under the 12px absolute minimum) and is now 9.75pt (13 logical px).

Regression checks cover 24 sites at 1600×1000 and 900×650 (all names drawn) and 60 sites at 900×650, with IPv6 and long names: no heading or host overlap, every host present. They also cover map/list order, truncation detection and the History font floor. Windows validation run 39803989941 passed self-tests and UI regressions. The first run (39803642344) failed to compile a test helper call and was corrected. Rendered captures from this run were not inspected: the session could not download artifacts. Joseph's visual check with his real site list, physical DPI and large-text acceptance remain open.


## Pinned All sites and sidebar separation — 2026-10-22

Joseph's screenshots showed the real #5 problem: the site list and the sidebar actions blended into one column. v1.18.15 pins All sites above the list as its own button with the enabled host count, places the list in a rounded inset well with an accent bar on the selected site, and adds a divider before the actions. Wallboard moved beside Start monitoring and Manage sites into More (right-click on the list still offers the same actions), leaving Outage history, Settings, Check for updates and More. Map addresses now trim from the front so the distinguishing end stays visible. The Sites/Add site header is deliberately left for the Hosts overhaul (#7).

Windows validation run 39808709209 passed self-tests and UI regressions, including pinned-scope selection both ways, the host count, four sidebar actions, non-overlapping sidebar sections and IPv4/IPv6 address trimming. Rendered captures were not inspected from the session; Joseph's visual check remains the acceptance step.

v1.18.16 follow-up: Joseph's v1.18.15 photo showed two-tone rows. DrawSiteItem filled each row with the sidebar Surface color before drawing the pill in the well color. Rows, gaps, empty list space and the well now paint one token; only the selected site draws a pill, and the list paints its own erase background. Validation run 39810357378 passed, including a new UI check that samples rendered pixels (row gap, unselected row, empty space, well padding) against the well color.

v1.18.17 follow-up from Joseph's v1.18.16 photos: the native dark scrollbar is replaced by a thin, draggable indicator drawn in the well; All sites is outlined when unselected; the map weighs site-name width in its layout; host rows cap at a natural height and stack from the top. Validation run 39812671262 passed, including a standalone 50-row scroll-indicator test, lone-host stacking and twelve real-length site names fitting at 1600×1000.

v1.18.18 softens the All sites outline to the quietest per-theme color that keeps 3:1 contrast (validation 39817568151, build 39817898861). Joseph confirmed the v1.18.17 sidebar; #5 is closed. The Sites/Add site header moves to the Hosts overhaul (#7).


## Paste, Quick Add and the Hosts overhaul — 2026-10-22

Joseph chose to do the Hosts overhaul (#7) before release hardening, with Hosts as a page in the main window and edits saving instantly with Undo. v1.18.19 is the first part: names paste reliably (busy-clipboard retry, HTML/RTF-only copies, hidden characters removed, busy state reported; #6), Quick Add opens on the site in view and creates a typed new site, and the Sites header uses a + button. Validation 39819409144 and build 39819815923 passed; installer, portable, full + delta and feed published. The Hosts page itself follows in a later build.

v1.18.20 fixes "&" in site and host names (it rendered as an underline; mnemonic processing is now off for names).

## Priority hosts — 2026-10-22

Joseph added one feature by explicit request: priority hosts that keep alerting until acknowledged. v1.19.0: Mark as priority from the host menu (accent ring on the map, "Priority" in list status, announced to screen readers). Offline and recovery on a priority host start an alarm (Windows alarm or a chosen .wav) that rings immediately and every 15 seconds; snoozed hosts do not ring. A banner in the main window and Wallboard offers Acknowledge and Acknowledge all, and the Windows notification (alarm scenario) has its own Acknowledge button. The sound is set in Settings > Notifications > Priority alarm. Validation 39828890857 passed self-tests and UI regressions, including end-to-end offline/recovery alarms and notification acknowledgement; build 39831813534 published installer, portable, full + delta and feed. Not yet established: the real Windows notification button, sound playback, and behaviour while minimized to the tray, which need Joseph's check. The final maintenance target moves to v1.20.0.

Next: List view becomes the place to edit hosts (multi-select actions, instant save with Undo), replacing the separate Hosts window (#7).

## Host editing in the List — 2026-10-22

v1.19.1 makes the List the place to edit hosts (#7). Filter chips (All, Priority, Offline, Disabled, Snoozed) show counts; selecting hosts swaps the chips for an action bar (Mark/Remove priority, Enable/Disable, Move to site, Set group, Monitoring, Keep in a site, Remove). Edits save immediately with an eight-second Undo (Ctrl+Z); double-click/F2 renames, Delete removes. The separate Hosts window is retired; More > Hosts and the Wallboard action open the List. Validation 39839194817 passed self-tests and UI regressions, including bulk priority, filters, Remove priority and Undo. The first publish run (39839598754) stopped before release because the appearance review step still expected the two retired Hosts-window screenshots; the count was corrected and run 39840180803 published installer, portable, full + delta and feed. Joseph's hands-on check of the new List remains.

v1.19.2: Joseph could not find the filter. In v1.19.1 the action bar replaced the chips whenever a row was selected, which in practice was nearly always. Chips now keep their own top row, and the action bar is a second row beneath them (validation 39842073825, build 39842467718). The UI test now checks both rows are on screen together.

## Wallboard highlight and release hardening — 2026-10-22

v1.19.3 fades the map/Wallboard keyboard highlight after 8 seconds and shows it only after key use (#8). v1.19.4 hardens publication: actions pinned to commit SHAs, a guard that stops when the version is already published, vulnerable-package gates in build and validation, three-attempt delta download with a visible warning instead of a silent gap, required-asset verification, SHA256SUMS.txt, and an unsigned-build notice on the release page. Dependabot proposes weekly Actions and NuGet updates. Build 39846402344 exercised every new step; the published SHA256SUMS.txt matches the update feed's package hashes.

v1.19.5 aligns updates with DATA-AND-UPDATES 3.2/3.3: automatic checks at most once a day (persisted across restarts, rearmed if the clock moves back) and an IT policy (HKLM\Software\Policies\PingWatchdog DisableUpdates = 1, or PINGWATCHDOG_DISABLE_UPDATES=1) that stops all checks, hides the sidebar action and shows "Updates are managed by your organization" in Settings. The publish guard now lets workflow-only pushes (such as Dependabot action updates) build and test without publishing; build 39849289579 confirmed that path.

