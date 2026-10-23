# Windows final-release audit

Audited: 2026-10-20. Source: [`e353415`](https://github.com/JPLuker/Ping-Watchdog/tree/e353415d98cde77ff45386974951033cdb2ee3c8). Published baseline: v1.17.3.
Standards: [InternalDesignGuidelines `63eae9e`](https://github.com/JPLuker/InternalDesignGuidelines/tree/63eae9e89f3278dbd1443a3dc54c6186a877efbc) (all ten root documents read).
Execution checklist: [Maintenance mode checklist #4](https://github.com/JPLuker/Ping-Watchdog/issues/4).

## Meaning of the statuses

- **Verified:** direct source evidence or explicitly recorded automated/visual results; the evidence column limits the claim.
- **Needs work:** a concrete source/repository gap was found.
- **Needs testing:** implementation or framework behavior exists, but acceptance evidence is incomplete.
- **Decision/owner check:** confirmation or repository settings cannot be established from source.
- **Deviation:** a justified project choice, recorded in AGENTS.md. This never waives accessibility, privacy or security floors.

The baseline tables describe the initial audit; later checkpoints below record completed fixes. The initial audit was a source/documentation review, not a fresh Windows execution. No new binaries or new manual-test results were produced during the audit. The current dashboard and connected topology were accepted by Joseph after v1.17.3; cosmetic differences alone do not reopen that work.

## Verified baseline

| Area | Status | Evidence and limit |
| --- | --- | --- |
| Windows automated regression suite | Verified for v1.17.3 | [Run 39483113901](https://github.com/JPLuker/Ping-Watchdog/actions/runs/39483113901): self-tests, published startup/UI, recovery and packaging passed. This is not human installation or workday acceptance. |
| Published Windows packages/feed | Verified for v1.17.3 | Installer, portable ZIP, full/delta packages, RELEASES and releases.win.json were verified in the published release. |
| Main/Wallboard visual baseline | Verified for captured samples | Populated light/dark Map/List, dense connected topology and 900px toolbar captures inspected. Existing fit, overlap and host-hit assertions passed. Does not establish all host lengths, all DPI settings or screen-reader access. |
| Appearance foundation | Verified in source and existing tests | AuroraTheme.cs has semantic palettes, native font tokens, OS-theme change handling and high-contrast resolution. UserPreferences.cs persists System/Light/Dark. Remaining control/state acceptance is separate. |
| Site-scoped workers, stale-result protection, snooze | Verified within existing regression coverage | Program.cs / HostManagement.cs tests cover cancellation/replacement, disabled hosts and snooze behavior. Workday/real-network acceptance still required. |
| License baseline | Verified file, final confirmation pending | LICENSE is proprietary, copyright Joseph Luker 2026. It is also copied into Windows packages. Do not replace it with an open-source default. |

## First implementation batch: data safety

| ID | Status | Finding / source evidence | Required outcome |
| --- | --- | --- | --- |
| D1 | Needs work | Program.cs SaveSites writes sites.json and autosave.pingwatch.json directly with File.WriteAllText; exceptions are swallowed. | Atomic replacement, recoverable originals, and visible persistence failure without stopping monitoring. Test interrupted/failed writes and recovery. |
| D2 | Needs work | WatchdogConfig.FormatVersion defaults to 1, but no reader checks it before ApplyConfig. Import and startup can accept a newer format and later rewrite only known fields. | Validate before mutating live/saved data; refuse newer formats; define backup-before-migration and backup retention. Preserve original files on rejection. |
| D3 | Needs work | SaveConfigFile exports BuildConfig. That includes sites and monitoring settings but not event history or UserPreferences. | Keep useful config-only export and add a complete, re-importable backup with explicit inclusion/replacement semantics. |
| D4 | Needs work | Program.cs and UserPreferences.cs use fixed AppData-derived paths; StartupRecovery.cs uses LocalAppData. No portable-marker/data-root resolver found in the audited Windows sources. | Central storage resolution with intentional portable behavior; preserve existing installed paths/migration compatibility. Audit recovery paths too. |
| D5 | Needs work | Settings categories do not include Data & storage; no open-data-folder or full-delete action found. | Add deliberate data-folder access and full export/deletion with clear consequences, safe monitoring shutdown and no surprise deletion. |
| D6 | Needs work | UserPreferenceStore catches load failures then saves defaults to the same path. Startup recovery writes a new attempt directory each run without a retention pass. | Preserve corrupt originals, surface recovery information, and bound diagnostic retention. Do not delete history under a log-cleanup rule. |

## UI and essential accessibility

| ID | Status | Finding / source evidence | Required outcome |
| --- | --- | --- | --- |
| A1 | Needs work | WallboardCanvas has one generic AccessibleName. OnKeyDown only pans, while host actions are dispatched by right-click hit testing. No per-host keyboard selection/accessibility children found. | Keyboard host/outage selection and context actions, visible focus, per-host identity/state. Keep fitted rendering and current style. |
| A2 | Needs work | AdaptiveTopology draws status-colored dots and address/name text; individual status words are on mouse hover. Compact captions may omit nicknames; long addresses can ellipsize. | Non-color status access, keyboard-accessible full values, long-hostname/IPv6 coverage at the readability floor. |
| A3 | Needs work | Settings/Hosts/History/Organization/category dialogs commonly inherit 9.5pt fonts (~12.67 logical px); independent recovery uses 9pt (~12px). AuroraTheme does not normalize all inherited control fonts. | Required readable text at least 13 logical px, body at its intended token; measure actual controls before changing sizes and reflowing layouts. |
| A4 | Needs work / testing | Settings still uses Display rather than Appearance; there is no Data & storage or About category. Explanatory/repeated copy and fixed dimensions remain. Existing staged-save and Ctrl+S behavior works in source. | Consolidate task categories/copy and shared component states while retaining documented staged-save behavior. Render both themes and verify errors actually stay unsaved. |
| A5 | Needs testing | History, Organization, Quick Add/Monitor, import prompts, label/category dialogs and recovery have shared/automatic theming but incomplete individual render/state acceptance. Recovery launches independently. | Complete screen-by-screen review, ownership, Escape, focus/tab order, disabled/error and constrained-height states. Preserve independent recovery. |
| A6 | Needs testing | AuroraTheme provides high-contrast colors; primary component contrast tests exist. Whole-form DrawToBitmap omits the in-app toast despite visibility/stack assertions. | Real-desktop popup composition, actual semantic contrast, focus, screen readers, reduced preferences, large text and mixed-DPI tests. Do not infer acceptance from a palette or screenshot montage. |
| A7 | Needs work | ShowAboutDialog is a MessageBox with version/copyright; there is no Copy diagnostic info or Report an issue action. | Dedicated About home for version/build/license/support and reviewable diagnostic copy without secrets. |

## Updates, security and packaging

| ID | Status | Finding / source evidence | Required outcome |
| --- | --- | --- | --- |
| R1 | Needs work | Windows workflow builds on main and creates the tag only at publication; Actions use moving @v4 tags. | Immutable Action SHAs and a tag-driven, version-checked release build. Preserve authorized updater-accessible iterations. Do not modify Linux workflows in this track. |
| R2 | Needs work | No SHA256SUMS.txt generation/publication. Release notes lack a Downloads section and SmartScreen/unsigned notice. Asset validation accepts any four files instead of requiring each essential asset. | Hash all distributed assets, verify the mandatory set, disclose unsigned status and link downloads clearly. Preserve feed compatibility when considering names. |
| R3 | Needs testing | Normal and recovery updaters call Velopack DownloadUpdatesAsync / ApplyUpdatesAndRestart. Source does not independently demonstrate the dependency's integrity validation. | Verify the pinned dependency's actual verification path and corrupted-download behavior. Do not claim checksums are absent or unsafe merely because validation is delegated. |
| R4 | Needs work | Automatic checks occur on startup and every six hours. No persisted daily limiter or DisableUpdates policy/env handling found in audited Windows update paths. | Daily background checks and policy enforcement covering normal and recovery update entry points; manual checks remain available when allowed. |
| R5 | Needs work / decision | Portable ZIP is currently advertised as self-updating; central defaults specify notify-only for portable. Existing Velopack filenames are part of updater compatibility. | Establish installed/portable detection and either implement the default or document a justified non-security deviation. Do not rename feed-controlled assets casually. |
| R6 | Needs work / testing | No Dependabot config or Windows package lock found in repository tree; package versions are explicit in csproj. | Add appropriate dependency governance/locking, inspect transitive vulnerabilities and license notices. No vulnerability-free claim has been established. |
| R7 | Decision/owner check | SECURITY.md and issue templates are absent. Repo settings such as private reporting, secret scanning and security-update enablement are not established by source access. | Add source-managed files; verify settings through owner-capable controls. Do not claim a setting is enabled without inspecting it. |
| R8 | Needs testing | User explicitly requested ongoing iterative publication after passing automation. Standards distinguish human-run exact builds from CI-only checks. | Record real Windows tester/OS/package/version for the final candidate. Existing iteration authorization is not final acceptance. |

## Documentation and public readiness

| ID | Status | Finding / source evidence | Required outcome |
| --- | --- | --- | --- |
| P1 | Needs work | README describes removed developer update toggle, old site editor and outdated Wallboard history behavior. README-FIRST contains mutually inconsistent auto-install/consent claims, old header controls and bitmap-shrinking guidance. | Replace with the current workflow and actual update/storage behavior after implementation settles. README-FIRST is bundled, so its implementation-stage change requires a fresh package/version. |
| P2 | Needs work | No CHANGELOG.md, SECURITY.md, .github/ISSUE_TEMPLATE or docs landing page in tree. README has Issues/License but lacks the standard privacy/network table and explicit maintenance policy. | Add truthful support/security/privacy/release materials and a live accessible landing page. Don't assert unverified GitHub settings, zero network behavior or dependency licenses. |
| P3 | Decision/owner check | Existing proprietary rights were deliberately retained earlier; the final checklist requires license confirmation per project. | Confirm the existing choice for final publication and dependency compatibility. Preserve LICENSE until an explicit instruction changes it. |
| P4 | Needs work | Local DESIGN is v1.2; roadmap header says v1.17.1 although v1.17.3 is published. Old unchecked migration lists and historical panning/collapsing behavior obscure current tasks. | Sync design v1.5, document deviations, replace the live roadmap and retain historical checkpoints separately. This audit commit addresses P4. |

## Candidate acceptance still pending

- Human installation/run of exact final candidate, including installer and portable behavior.
- Updates from pre-migration v1.14.81 and latest iterative release, with all sites/groups/labels/categories/preferences/history/policies and unexpired snoozes retained.
- Download verification/failure/retry, deliberate failure/recovery thresholds, missed replies, snooze expiration and edit/remove stale-result protection.
- Actual 100/125/150/200% Windows scaling, large text, keyboard-only use, screen readers, high contrast, reduced settings and mixed-DPI monitor moves.
- Full workday after the last substantive change, including interruptions, repeated menus/dialogs, Wallboard transitions, memory/CPU, notifications and persisted data.
- Final package/feed discovery and license/support/documentation review.

No new monitoring features, framework changes, broad dashboard redesign or Linux implementation work is authorized by this audit.

## Execution order

1. D1/D2/D6 data-loss prevention, with tests that preserve originals.
2. A1/A2 essential host accessibility, then A3-A7 supporting-window consistency.
3. Remaining D3-D5 data controls, R1-R7 release/security/updater work.
4. P1-P3 docs, support, license confirmation and public page.
5. Manual candidate acceptance, defect fixes, and final v1.19.0 publication when accepted.

The release number remains tentative. If interim feature-bearing releases use 1.18.x, select a later compatible minor for the final candidate rather than reusing/downgrading a version.


## First configuration-safety batch

Implementation candidate v1.17.4 adds SafeConfigStore.cs and updates Program.cs. Full autosave, legacy inventory writes and explicit config export use atomic replacement with three rolling backups. Startup refuses damaged/newer full configs and damaged legacy site lists, protects originals by disabling session autosave, and shows a warning when the window is ready. Config imports/direct application validate format before changing live sites. Save failures produce a visible warning. Existing preference/history behavior, complete backup/restore, migration to a genuinely new schema, storage locations and Settings persistence feedback remain separate work.

New tests exercise original preservation during a locked-file replacement, three-version backup retention, future-format read/write refusal, malformed-file protection, legacy missing-version compatibility, temporary-file cleanup and refusal before active-site mutation. Windows [run 39485640920](https://github.com/JPLuker/Ping-Watchdog/actions/runs/39485640920) passed the self-tests (including all new storage regressions), published startup/UI exercises, independent recovery and package gates. v1.17.4 installer, portable ZIP and full/delta/feed assets were verified published. No schema migration or new complete-data backup feature was introduced, and no new physical-desktop acceptance is claimed.


## Host access and snooze candidate — 2026-10-20

Iterations v1.18.0–v1.18.2 implement keyboard host selection, visible focus, standard context-action keys and per-host Windows accessible children in the shared map/outage renderer. Host marker shapes distinguish normal, suspect, offline, unknown and snoozed states; full identity and state are available through accessible names and keyboard details. Fonts below 13 logical px in themed controls are raised to the reading floor; independent recovery text is raised too. This does not certify Narrator behavior, actual DPI/accessibility acceptance or every long-address layout.

Joseph requested snooze to pause monitoring for the hour rather than only muting notifications. Snoozed hosts lose their active worker, remain in inventory with a snoozed status, disappear from active outages/live output, and resume through the live-session timer when expiry passes. Disabling or stopping monitoring continues to take precedence. Historical events are preserved. Late results are rejected at the probe-result, command and event boundaries.

Windows runs 39487281012 and 39488095116 passed snooze/accessibility regressions, published startup/UI, independent recovery, packaging and publication. Tests cover cancelled workers, unrelated-worker stability, late command/event/result rejection, saved snoozes, automatic expiry/manual resume, all-paused sessions, aggregate site health, correct keyboard/accessibility action targets, stale child/removal safety and snoozed outage filtering. The intermediate v1.18.1 publication failed with GitHub HTTP 403 after all test/package steps passed; final v1.18.2 publication succeeded. Installer, portable, full/delta packages and feeds were verified; GitHub latest points to v1.18.2.

Inspected actual light/dark Main/Hosts/Settings review captures, dense topology/state markers and the corrected compact focus outline. Captured workspace image dimensions: 1040×736, 1040×736; Wallboard images: 1044×788, 900×788, 1044×735. Render assertions and accessible-object API checks do not certify actual Narrator behavior, mixed DPI, large text or other physical-desktop acceptance.


## Settings save feedback and preference preservation

v1.18.4 addresses A4's incorrect Saved state and the preference-corruption part of D6. PersistAppSettings stages the settings-only config values and preferences, validates both originals, writes them before changing runtime settings, and restores previous preferences if the subsequent config write fails. This is not a crash-atomic transaction across two files; rollback failure is reported explicitly. General inventory-save feedback and full backup/restore remain separate.

Settings keeps its baseline/draft after failure and exposes Could not save with Details; retry commits and reloads the successful baseline. New tests check that a failed callback does not change the committed provider value, retains the draft, and never reports Saved. Filesystem tests cover locked config files, a real commit failure after preference write, rollback, corrupt/newer preference protection and retry. Main preference loading reports fallback use without overwriting the original.

Category navigation, short page headings, Appearance naming and native Enter/Escape are refined without changing the accepted monitoring layout. Windows run 39490797950 passed storage and draft/retry tests, published startup/UI, independent recovery, packaging and publication. Actual normal 900×650 and constrained save-error 760×650 captures were inspected in Light and Dark; controls fit. The first v1.18.3 UI attempt was withheld because the review helper edited before Shown initialized its baseline; the corrected helper yields for initialization and retains visibility/bounds checks. v1.18.4 installer, portable and updater assets were verified published.

This completes the Settings save-feedback item and preference-corruption protection, not the full supporting-window migration, high-contrast/physical-DPI review, diagnostic-retention policy or complete-data backup/restore.


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

Implemented in v1.18.14 (A2/A3 code gaps; issue #5). Map site order matches the site list. Topology layout widens past the square bound only when captions no longer fit, shrinks gutters past three rows, and drops headings instead of overlapping when a window cannot label every site. Truncated site-list names show the full name on hover. History event-type and header text raised from 8.5pt to 9.75pt.

Evidence: Windows validation run 39803989941 passed self-tests (dense 24/60-site topology without heading or host overlap, all hosts reachable, IPv6/long names, map/list order, truncation detection, History font floor) and UI regressions. Not established: inspection of rendered captures from this run (artifacts were unreachable from the session), physical scaling/large-text/screen-reader behavior, or Joseph's confirmation with his real site list.

Publication: build run 39804394546 passed published startup/UI, independent recovery and packaging, and published v1.18.14 as latest with installer, portable ZIP, full package, RELEASES and releases.win.json (feed verified: 1.18.14 full package with SHA256). **No delta package was produced.** The previous-release download step is `continue-on-error` and evidently failed; the publish step accepts four assets, so the omission passed silently. Installed copies still update through the full package. Release hardening should fail or warn visibly when the delta or prior feed entries are missing.


## Pinned All sites and sidebar separation — 2026-10-22

v1.18.15: pinned All sites scope, inset site-list well, divider, four sidebar actions, front-trimmed map addresses. Evidence: Windows validation run 39808709209 (self-tests and UI regressions). Not established: rendered capture inspection, Joseph's visual confirmation, physical DPI/large text/screen reader.

Publication: build run 39809105132 published v1.18.15 as latest with installer, portable ZIP, full **and delta** packages, RELEASES and feed. The v1.18.14 delta omission therefore looks transient, but the silent-failure path still needs hardening.


## Release hardening — 2026-10-22 (v1.19.4)

Implemented: commit-SHA pins for checkout v4.4.0, setup-dotnet v4.3.1 and upload-artifact v4.6.2 in build-windows.yml and validate-windows.yml; a pre-build version guard; `dotnet list package --vulnerable --include-transitive` gates; three-attempt previous-release download; required-asset verification; SHA256SUMS.txt asset; an unsigned-build notice on the GitHub release page only (Velopack notes unchanged); Dependabot for github-actions and nuget at the repository root.

Decisions: (1) A missing delta warns and notes it on the release page rather than failing, because full-package updates remain valid and blocking iterative releases on a transient download error is worse. (2) Publication stays push-to-main, not tag-driven: Joseph requires an updater release after each passing implementation build (AGENTS.md), and the version guard enforces uniqueness. (3) Linux workflows are untouched (separate beta track). (4) Major action upgrades (checkout v6, setup-dotnet v6, upload-artifact v6) are left to Dependabot PRs so behaviour changes are reviewed individually.

Evidence: validation run 39846013708 (pinned actions, vulnerable-package check, self-tests, UI regressions) and build run 39846402344 (version guard, vulnerability gate, delta download, asset verification, publication). SHA256SUMS.txt for v1.19.4 matches releases.win.json hashes for the full and delta packages.

Still open in this section: updater integrity under corrupt downloads (Velopack verifies package hashes; no corrupt-download test yet), update-check cadence and managed-policy controls, and owner-managed repository security settings.

Owner-managed repository security, confirmed by Joseph on 2026-10-22: Dependabot alerts on, Dependabot security updates on, secret scanning and push protection on. The session's integration cannot read these settings, so this rests on Joseph's confirmation.


## Update cadence and managed controls — 2026-10-22 (v1.19.5)

Implemented per InternalDesignGuidelines DATA-AND-UPDATES 3.2/3.3 (commit 63eae9e): at most one automatic check a day, kept in preferences (LastUpdateCheckUtc); HKLM\Software\Policies\PingWatchdog DisableUpdates and PINGWATCHDOG_DISABLE_UPDATES disable all checks, hide the sidebar action and replace the Settings controls with "Updates are managed by your organization". Validation 39847814987 and build 39848145060.

Deviations: (1) The portable ZIP keeps Velopack in-place self-update rather than notify-only, per the Focused Windows distribution deviation in AGENTS.md; portable data lives outside the replaced app folders. (2) Download verification is Velopack's own hash check against the feed (SHA1/SHA256 in releases.win.json); SHA256SUMS.txt covers manual downloads. A test that a corrupted package is rejected and discarded is still open. (3) Updates download automatically after a check and install only when the user chooses Restart, which meets "never install without consent".

Publish guard refinement: a push that reuses a published version fails only when app files changed; workflow-only pushes run every build and test step and skip packaging and publication (build 39849289579). This lets Dependabot action updates merge without a version bump.



## Update restart safety — 2026-10-23

v1.19.7 replaces the generic Yes/No restart prompt with Restart now / Later. Enter and Escape defer by default. The prompt follows an open Settings window or Wallboard; restart failures use the same owner. Restart first invokes Settings' existing save/discard/cancel protection and stops if Settings remains open or configuration persistence fails. Monitoring and Wallboard are not stopped until that guard succeeds. Independent recovery now supports Escape to close and names its details/status for accessibility.

Windows release run 39961911594 passed dependency vulnerability checks, self-tests (including protected-config restart persistence), published startup/UI and isolated recovery tests. Light/dark update-confirmation captures were downloaded and inspected; the actions and wrapped text fit. Installer, portable, full/delta, update feeds and SHA256SUMS.txt are published; latest is v1.19.7. The first candidate v1.19.6 was not published: its new self-test exposed a guard-order bug in test mode, corrected before this passing run.

Remaining: physical keyboard/DPI/screen-reader acceptance, real installed/portable restart and upgrade, corrupt-download tests, broader supporting-window review, public docs and final maintenance acceptance. Issue #12 has no screenshot; the existing update toast says Dismiss, so this batch does not establish reproduction or resolution of the specific reported “okay” state.


## Update integrity and recovery lifetime — 2026-10-23

v1.19.8 adds tests of the actual pinned Velopack 1.2.161 UpdateManager download path. Isolated fixtures advertise a valid SHA-256/SHA-1 and size, then deliver same-length corrupted bytes, a truncated package, or an interrupted transfer after writing partial bytes. Each must throw the expected checksum/IO exception, leave no full or partial package, and report no pending restart. Retrying the same manager/update with valid bytes must download again, stage exactly the original package and report it ready. This runs in the Windows self-test gate.

The recovery UI also runs its real RecoveryUpdates adapter against the verifier: a corrupted package leaves installation unavailable, preserves the original crash details and keeps Check for updates usable; a valid retry offers installation. A test factory supplies the isolated manager without replacing verification. No executable updater is included or launched, no real feed/network is contacted, and no installed app or user data is touched by these fixtures.

Recovery now rejects check/download completions after the window closes, even if the transport ignores cancellation. Queued progress is limited to its active attempt so it cannot replace the final ready/error status. Additional recovery regressions cover closing during both the check and download phases.

Windows release run 39964182359 passed dependency checks, real integrity/self-tests, published startup/UI and isolated recovery regression gates. Installer, portable ZIP, full/delta, RELEASES, releases.win.json and SHA256SUMS.txt were verified on the published v1.19.8 latest release. This batch changes no layout. It establishes full-package verification/cleanup/retry and recovery state handling, not live GitHub transport reliability, delta application, installed/portable upgrade, physical accessibility or final maintenance acceptance.

Verifier source reviewed: [UpdateManager.cs at 1.2.161](https://github.com/velopack/velopack/blob/1.2.161/src/lib-csharp/UpdateManager.cs), including DownloadUpdatesAsync, VerifyPackageChecksumAsync and CleanPackagesExcept. The transport fixture is intentionally synthetic; verification and cache cleanup run in the library itself.

Next: remaining supporting-window focus/ownership/Escape/layout review, then release documentation and hands-on candidate acceptance. Include an owner-close cancellation check with an open Settings draft; the main FormClosing handler currently performs shutdown work without inspecting an inherited cancellation flag.


## Supporting windows and canceled exit — 2026-10-23

v1.19.13 fixes the owner-close path: Main honors a canceled owned-window close before shutdown, and explicitly resolves Settings opened from the independent Wallboard before closing windows or stopping workers. Explicit Settings discard reloads its committed baseline so owner preflight does not ask again. Tests cover retaining the draft on Cancel/failed save, resolving discard, and a native main-window close veto with Settings owned by Main or Wallboard while a localhost monitoring session is active.

History now uses wrapping paired filters, accessible filter names and readable theme-based event colors instead of hard-coded dark row colors. It disables export when empty, owns export error dialogs, supports Escape, and reserves native dropdown height. Read-only dropdown values draw with the current palette while retaining native selection/keyboard semantics. Organization uses the shared buttons, wrapping actions, a compact header, named tree and Escape. Category uses content-sized rows, DPI scaling and native confirm/cancel actions with visible bounds.

Windows run 39968819793 passed vulnerability/self-tests, published startup/UI and isolated recovery gates. Both-theme History populated/empty captures (820/1080 widths), Organization root/disabled-action captures (620/900 widths), and category captures were inspected. Geometric checks cover parent and full-window bounds; History event text contrast is checked. All installer/portable/full/delta/feed/checksum assets were verified on the v1.19.13 latest release.

Earlier v1.19.9/10 candidates were withheld by the UI gate for a native History dropdown height/clipping defect. v1.19.11/12 passed and published as authorized iterations; visual review then exposed white native fields in dark mode. The final follow-up restores category field styling and explicitly draws the read-only History values using theme colors. These iterations do not establish physical DPI, open-dropdown/high-contrast/Narrator acceptance, dense-folder behavior or every supporting window as complete.

Next: review Quick Add (including temporary Quick Monitor), label/site-name and folder-picker dialogs, then public documentation/notices and exact-candidate manual acceptance. Preserve the accepted dashboard/topology and display-only Wallboard.


## Quick Add and compact dialogs — 2026-10-23

v1.19.16 completes the tested desktop-state review for Quick Add, temporary Quick Monitor, host label, site name and destination-folder dialogs. Quick Add separates paste/import actions, destination fields, editable preview and confirmation. Empty or invalid-only previews cannot be confirmed; theme-aware preview rows retain readable status words. Pasted LF/CRLF input displays as separate native editor lines, preserving the caret when normalization changes line endings. The label/site/folder dialogs share measured native layouts, accessible input names, initial focus and confirm/cancel actions.

[Windows run 39996325322](https://github.com/JPLuker/Ping-Watchdog/actions/runs/39996325322) passed dependency vulnerability checks, self-tests, published startup/UI, isolated recovery, packaging and publication. Added UI assertions cover four displayed paste lines, mixed new/existing/duplicate/invalid previews, no inventory mutation before confirmation, Escape dismissal, preserved naming/folder values, control bounds, preview space and status-text contrast. Visual review covered both-theme mixed previews at 850px, light previews at 1100px, representative empty previews, and all three compact naming/folder dialogs in both themes. These screenshots do not establish physical DPI, open-dropdown, high-contrast, Narrator or complete tab-order acceptance.

v1.19.14 passed and published as an authorized iteration; visual review then found excessive destination spacing, light-theme row artifacts and concatenated pasted lines. v1.19.15 was withheld by the new spacing assertion: it compared coordinates from different parents. v1.19.16 fixes that assertion in form coordinates and passes the additional native-line check.

The [v1.19.16 release](https://github.com/JPLuker/Ping-Watchdog/releases/tag/v1.19.16) is verified as latest with installer, portable ZIP, full/delta packages, RELEASES, releases.win.json and SHA256SUMS.txt. No main/Wallboard redesign or Linux change was introduced.

Next: refresh public README and the packaged README-FIRST quick start, then privacy/security/support/maintenance material and download-page checks. The existing quick start contains obsolete UI and updater descriptions; public readiness remains unchecked. Final candidate installation/upgrade, physical accessibility, workday monitoring and license/notice acceptance remain open. Issue #12 has not been reproduced or closed by this dialog work.
