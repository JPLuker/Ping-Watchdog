# Ping Watchdog final feature release

Status: implementation in progress; design migration and release acceptance are not complete.
Current published Windows build: v1.14.84 (appearance foundation).
Pre-migration baseline for update acceptance: v1.14.81.
Design authority: [DESIGN.md](DESIGN.md), Aurora Glass v1.2.

## Release goal

Finish the standalone Windows monitoring application, then enter maintenance mode. Preserve its working monitoring and management capabilities. No cross-promotion, external-product links, upgrade prompts, or promotional copy in the app, documentation, or release notes. The final version number will be set after the candidate passes acceptance; it is not a reason to declare unfinished work complete.

New monitoring features are frozen. The remaining product work is design consistency, usability, accessibility, correctness, documentation, and packaging. This roadmap does not authorize a new framework, a web rewrite, new tools, or changes to the Linux frontend.

## 1. Design foundation

- [x] Adopt the supplied DESIGN.md as the required visual and interaction standard.
- [x] Update contributor instructions and supersede the previous Wallboard design reference.
- [ ] Audit every Windows screen and record remaining gaps against DESIGN.md Part 21.
- [ ] Implement centralized native color, spacing, radius, typography, motion, and material tokens matching DESIGN.md. Convert CSS pixels to native logical units consistently; do not confuse point sizes with pixel sizes.
- [ ] Add persisted System / Light / Dark appearance, defaulting to the OS. Apply changes to open windows and handle an OS theme change while running.
- [ ] Build reusable buttons, inputs, search, status indicators, panels, context menus, and focus/validation states before restyling individual forms. Support rest, hover, pressed, focus, disabled, selected, loading, empty, and error states where applicable.
- [ ] Keep native window controls and standard keyboard behavior. Use system sans and one technical monospace family with documented fallbacks.
- [ ] Establish contrast-checked token pairings for both themes. Verify semantic colors on their actual backgrounds; the palette alone is not proof of contrast compliance. Record token gaps rather than scattering arbitrary values.
- [ ] Use native Mica/Acrylic only where supported and readable. Provide an opaque surface fallback for unsupported systems, reduced transparency, and expensive/large content. Dense tables, forms, and logs remain opaque.

Acceptance: one token source and shared component set; both themes render correctly; appearance persists; native fallbacks work. No screen is declared migrated by changing colors alone.

## 2. Main monitoring view and host management

- [ ] Rework the monitoring view around targets, the start/stop action, and live host results. Pick one focal element per state and remove repeated branding, redundant summaries, and explanatory filler.
- [ ] Use compact density consistently. Keep essential monitoring state visible without repeating inventory totals throughout the screen.
- [ ] Default host tables to the 3–5 columns needed for scanning. Keep additional fields accessible through details or column selection; do not drop saved host data or editing capabilities.
- [ ] Preserve full IP addresses and hostnames. Provide accessible full-value details for truncated labels. Use readable text, stable row updates, explicit status words, units, and numeric alignment.
- [ ] Migrate Hosts, Quick Add, Quick Monitor, category/label editing, and site organization to the shared components. Preserve search, bulk edits, import preview, temporary-host behavior, live editing, and clear save/discard/cancel handling.
- [ ] Keep ping output discoverable and expandable, with host/time context, failure text, copy/export where appropriate, and bounded rendering for large logs.

Acceptance: idle, empty, monitoring, suspect, offline, recovery, snoozed, disabled, and invalid-input states are usable. Editing/navigation does not interrupt unrelated monitoring or discard drafts.

## 3. Wallboard

- [ ] Rebuild its hierarchy using the same tokens and components, with topology/current outages as the task focus, restrained floating controls, and secondary information on demand.
- [ ] Remove decorative and duplicated information; retain meaningful host, site, state, clock, and outage context. No explanatory subtitles or visible shortcut legend by default.
- [ ] Replace the existing tiny-text scaling strategy. Preserve minimum readable font sizes using a larger/scrollable or navigable layout when the topology cannot fit. Do not solve density by hiding captions or shrinking all text below the design floor.
- [ ] Retain host/site identity, collision prevention, complete addresses, right-click host actions, outage-row actions, snooze, history filters, saved clock format, ping log access, multi-display behavior, and Esc to exit.
- [ ] Make host actions reachable by keyboard as well as mouse; a painted node and its color alone are not an accessible control.
- [ ] Verify long labels, IPv6, hostnames, dense sites, many sites, and empty/unknown states in both themes and with ping output shown/hidden.

Acceptance: no overlapping or vanished captions, readable minimum text, no clipped controls, correct action target from topology and outage history, no monitoring interruption when entering/leaving Wallboard.

## 4. Supporting windows and copy

- [ ] Migrate Settings, History, Organization, import/export prompts, label/category dialogs, confirmations, About, update progress, and recovery UI to the common system. Recovery must remain independently launchable if the main UI fails.
- [ ] Keep Settings grouped by user task; show pending/saved/error state clearly and preserve Ctrl+S and unsaved-change protection.
- [ ] Give each fact one home. Remove redundant subtitles, all-caps headings, slogans, decorative illustrations, repeated version information, and unnecessary notifications.
- [ ] Keep longer text only where it enables a decision: ambiguous settings, errors, destructive consequences, and on-demand help.
- [ ] Test dialog ownership, keyboard focus, tab order, Escape, menu dismissal/reopening, disabled-state explanations, and monitor placement.

Acceptance: shared appearance across ordinary and failure flows; clear saving; no misleading or promotional copy; no loss of existing history/filter/export behavior.

## 5. Release-candidate validation

- [ ] Run existing configuration, notification, history, updater, host-management, caption/collision, published UI, startup, and recovery tests. Extend coverage only for material new behavior such as appearance persistence and accessible host selection.
- [ ] Render and inspect actual Windows screens in both themes at narrow, normal, and wide viewports (approximately 900, 1320, and 1800 logical pixels where supported) and constrained heights. Record actual captured dimensions.
- [ ] Check Windows scaling at 100%, 125%, 150%, and 200%, large system text, keyboard-only use, screen-reader names/status, high contrast, reduced motion, reduced transparency, and mixed-DPI monitor moves. Record unavailable physical-desktop checks as pending rather than passed.
- [ ] Verify contrast on actual surface/material combinations, minimum text sizes, visible focus, control hit targets, and status meanings without color.
- [ ] Exercise fresh installer, portable extraction, update from v1.14.81, and update failure/retry. Preserve hosts, groups, labels, categories, preferences, history, per-host policies, and unexpired snoozes.
- [ ] Check controlled failure/recovery thresholds, missed-reply behavior, snooze expiration, monitoring disable/enable, all-hosts startup/default override, and stale results after editing/removing a host.
- [ ] Run a full workday trial after the last substantive UI or monitoring change. Include network interruptions, repeated menu use, opening/closing dialogs, and entering/leaving Wallboard. Investigate crashes, growing memory/CPU use, lost data, and missing notifications.

Acceptance: automated gates green; actual visual review recorded; no release-blocking crash, data loss, incorrect alerting, unreadable/clipped essential UI, broken updater, or inaccessible essential action. A version number, screenshots alone, or an empty issues list does not satisfy this gate.

## 6. Documentation, final release, and maintenance

- [ ] Rewrite README and README-FIRST instructions around the actual interface, including Hosts saving, snooze, appearance, backup/import/export, and updates. Remove outdated control names.
- [ ] Capture current screenshots after design acceptance; document supported Windows/package behavior and known limitations honestly. Do not imply a signed build if it remains unsigned.
- [ ] Publish final feature-release notes and a maintenance policy. Keep the repository and issue reporting available.
- [ ] Publish the installer, portable build, and update feed only after candidate acceptance. Verify the published assets and update discovery.
- [ ] Enter maintenance mode: address crashes, data loss, incorrect monitoring/notifications, security, compatibility, installation/updating, and material accessibility defects. No routine feature expansion or rolling redesign.

## Linux boundary

Linux remains separately labeled beta. Its existing packaging, updater, tray/notification, and real-desktop acceptance work stays in linux/ROADMAP.md and the dedicated Linux development track. A Windows final release does not certify Linux or authorize untested changes to shared presentation definitions.

## Execution order and reporting

Work in reviewable stages: foundation, main/Hosts, Wallboard, supporting windows, candidate validation, release. Preserve existing behavior at every stage. Each implementation reports the affected screens, reused components, actual checks and captures, and remaining gaps. Do not claim visual verification from source review or automated layout assertions alone.

This is a full design migration, not the previously proposed single cleanup pass. Do not promise a release date before the migration is rendered, reviewed, and tested.

## Implementation checkpoint — 2026-10-17

The appearance foundation now includes centralized light/dark palettes, native font tokens, persisted System / Light / Dark selection in Settings, live updates of registered windows, shared pill buttons and context-menu colors, and opaque dense-content surfaces. Palette text-pair contrast and preference compatibility have automated coverage. This is partial stage 1 implementation, not completion of the design migration.

Current source-review gaps (rendering acceptance remains separate):

| Surface | Remaining design work |
| --- | --- |
| Main monitoring window | Reduce repeated summaries, consolidate task hierarchy, migrate remaining fixed dimensions and fonts, and verify constrained-height layout. |
| Hosts and Quick Add | Reduce default column density without removing editing fields; unify input/focus/error states and readable row sizing. |
| Wallboard | Replace bitmap/text shrinking with readable navigation; migrate topology and outage typography, keyboard host access, and secondary information hierarchy. |
| Settings | Complete shared input styling and spacing; review explanatory copy, pending/saved feedback, and keyboard navigation. |
| Supporting and recovery windows | Audit History, Organization, import/export, editing, About, updater, and independently launched recovery against both themes. Automatic window theming alone does not establish compliance. |
| Shared foundation | Complete component states, semantic contrast on actual surfaces, typography adoption, and native material/fallback acceptance. |

Windows builds 82 and 83 were withheld by existing tests: first a clipped Wallboard button, then a compact More button shrinking below its explicit width. Shared sizing was corrected to measure labels and padding while preserving explicit sizes. Windows build 84 passed the configuration/monitoring/updater self-tests, published startup and UI smoke tests, and independent recovery tests. Its installer, portable package and update feed were published as v1.14.84.

Actual Windows captures of Main, Hosts and Settings in Light and Dark were inspected through the CI appearance review sheet. They show the shared palette and pill controls working, but also legacy selection colors, dense Hosts columns, uneven spacing, and native input borders still needing refinement. The reduced review sheet is not evidence of minimum-size typography or all DPI configurations. Wallboard captures and all remaining screen/state combinations still require the broader design review. Physical scaling, screen-reader, mixed-monitor, installation/update and workday checks remain pending.

## Screenshot cleanup — 2026-10-17

Synced DESIGN.md with InternalDesignGuidelines v1.2. The current cleanup removes redundant Wallboard branding and its permanent shortcut legend, adds a collapsible controls bar (F10), moves main timing fields to Settings → Monitoring, and corrects the main logo/action backgrounds. Existing monitoring and quick actions remain available.

The next version is 1.15.0 in PingWatchdog.csproj. On 2026-10-20 Joseph explicitly requested an updater-accessible release after every implementation build. Passing Windows builds therefore publish installer, portable package, and update feed automatically again. Each subsequent implementation push must increment the project version and update release notes. Iterative releases do not mean final design acceptance or maintenance mode; the remaining release-policy audit and final acceptance stay open.

## Workspace rebuild — 2026-10-20

Implemented a single idle/live host table with five default columns and a Columns menu, simpler site navigation, and a single Start/Stop action. Removed the duplicate inventory and repeated statistics. Wallboard controls start collapsed, topology text retains readable sizing with panning, and single-site placement and caption rendering were corrected.

Windows run 39309561449 passed configuration/monitoring/updater tests, published startup and UI smoke tests, independent recovery tests, and packaging. Main/Hosts/Settings review captures were inspected during implementation. The latest Wallboard captures still require final visual review. Physical DPI, accessibility, installation/update acceptance, and the workday trial remain pending.

## Dedicated monitoring workspace — 2026-10-20

Rebuilt the main window around a compact site sidebar, one selected-site heading and Start/Stop action, a searchable host table, and optional ping output. Removed the duplicate app-brand header and stacked toolbar cards. Settings, Wallboard and utility actions are in sidebar navigation. Search only filters displayed rows; it does not change monitoring scope.

Selecting a site now scopes actual enabled monitoring workers. Switching sites while running cancels old workers and adds the selected targets; All sites monitors all enabled hosts. Wallboard topology, history and output follow the selection. Added a published Windows regression for actual worker cancellation/replacement and search/configuration separation. Simplified topology captions and use one renderer for measurement and drawing.

This is an iterative release, not final design acceptance. Remaining supporting-window polish, physical DPI/accessibility and full-workday testing remain open.

## Unified monitoring layout — 2026-10-20

Joseph supplied a dashboard reference and requested one shared dashboard/Wallboard layout. The implementation now uses the same site-to-host map and outage renderer in the normal workspace and fullscreen, with address-first results below and optional ping output beside them. The normal workspace retains site navigation, search and editing; fullscreen removes the editing/navigation chrome and adds a clock. No extra diagnostic modules or reference-only metrics were introduced.

Replaced radial orbit layout with measured branch rows that remain readable and pan at larger sizes. Main-map and outage rows reuse existing host quick actions. Site selection still scopes actual workers. Corrected selection painting, aligned sidebar utility actions, added an explicit empty ping-output state, and requested native dark scrollbar styling where available. Added populated dark/light captures and panel-allocation/address-order regressions.

Windows build, rendered review and release verification are pending for this checkpoint. Physical DPI, screen-reader, workday and remaining supporting-window acceptance remain open.
