# Windows implementation history

Historical checkpoints retained from the former roadmap. Some described behavior has since been replaced. Use ROADMAP.md and issue #4 for current requirements.

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

Windows run 39439444049 passed monitoring/configuration/updater, published UI/startup, recovery and packaging gates. Actual populated light/dark dashboard and fullscreen captures were inspected. That review found table overflow beside ping output and unbounded fullscreen output text; the 1.16.1 correction fits columns, adapts output placement on narrow windows, clips fullscreen output, and adds scrolling for fullscreen results. Windows run 39440342943 passed the monitoring/configuration/updater, published UI/startup, recovery and packaging gates. The 1060×680 populated dashboard captures in both themes and fullscreen captures were inspected; five default columns fit beside output. v1.16.1 installer, portable package and update-feed assets were verified published. Large maps remain pannable, and fullscreen results scroll; physical DPI/accessibility and workday checks remain pending. Physical DPI, screen-reader, workday and remaining supporting-window acceptance remain open.


## Issues and fitted monitoring view — 2026-10-20

Requested follow-up: fit every selected topology node without scrolling; make Map and List alternative views (#2); show only current outages, with resolved events retained in the history window (#3); unify sidebar actions; keep Check for updates permanent and show check/download/ready/error status inside the app; remove the redundant bottom status bar.

Implemented native topology reflow across site groups and host columns, retaining the existing font floor rather than shrinking a bitmap. Dense groups use compact address labels with full names/site/status on hover. The sidebar has a unified action section, a history action, and a permanent update check. The old developer-only update-control setting is retired from Settings while old config fields remain compatible.

Joseph clarified that the shareable export is for ongoing monitoring, not saved output. Export monitoring batch creates a self-contained .bat for the selected enabled targets, repeatedly running ordinary Windows ping commands in one command window. It requires no app install, performs checks sequentially, and writes no result files. The generator validates targets and treats labels as text. A Windows loopback integration test exercises the generated batch in its single-pass test mode.

Windows runs 39480743453 (1.17.0) and 39481434578 (1.17.1) passed the existing monitoring/configuration/updater, published UI/startup, recovery and package gates. The generated batch was executed against loopback in single-pass test mode; malformed shell targets were rejected. Populated 25-host light/dark map and List captures and the dense fullscreen capture were inspected. All test nodes fit without panning; recovered devices leave the active-outage surface. Separate popup component captures were inspected, with visibility, bounds and top-of-stack checks; whole-form DrawToBitmap does not include the overlay in these captures, so physical desktop/compositor acceptance remains pending. Installer, portable ZIP and updater-feed assets were verified in v1.17.1. Issues #2 and #3 are resolved. Final-release, physical DPI/accessibility and workday acceptance remain separate.


## Connected topology refinement — 2026-10-20

Refine the current fitted topology rather than restoring the old renderer. A shared Monitor trunk connects every selected site; each site branches into its fitted host columns. Connection gutters are reserved before packing so lines stay outside host text. Site groups use balanced rows instead of an unrestricted strip of narrow columns. Existing theme tokens, native font floor, quick actions and no-scroll fitting remain in place. Removed the Show/Hide controls feature and its F10 shortcut from Wallboard at Joseph’s request. The normal toolbar remains directly available and still wraps to fit. Windows run 39483113901 passed monitoring/configuration/updater checks, published startup and UI exercises, recovery and packaging. Populated light/dark topology and dense fullscreen captures were inspected, plus the 900px Wallboard toolbar with actions visible and no controls toggle. Existing node-fit, caption-overlap and host-hit checks passed. v1.17.3 installer, portable package and updater-feed assets were verified published. Physical DPI/accessibility and workday acceptance remain open.
