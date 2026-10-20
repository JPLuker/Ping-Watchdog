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

This is a source/documentation audit, not a fresh Windows execution. No new binaries or new manual-test results were produced during the audit. The current dashboard and connected topology were accepted by Joseph after v1.17.3; cosmetic differences alone do not reopen that work.

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
5. Manual candidate acceptance, defect fixes, and final v1.18.0 publication when accepted.

The release number remains tentative. If interim feature-bearing releases use 1.18.x, select a later compatible minor for the final candidate rather than reusing/downgrading a version.


## First configuration-safety batch

Implementation candidate v1.17.4 adds SafeConfigStore.cs and updates Program.cs. Full autosave, legacy inventory writes and explicit config export use atomic replacement with three rolling backups. Startup refuses damaged/newer full configs and damaged legacy site lists, protects originals by disabling session autosave, and shows a warning when the window is ready. Config imports/direct application validate format before changing live sites. Save failures produce a visible warning. Existing preference/history behavior, complete backup/restore, migration to a genuinely new schema, storage locations and Settings persistence feedback remain separate work.

New tests exercise original preservation during a locked-file replacement, three-version backup retention, future-format read/write refusal, malformed-file protection, legacy missing-version compatibility, temporary-file cleanup and refusal before active-site mutation. Windows validation and publication pending.
