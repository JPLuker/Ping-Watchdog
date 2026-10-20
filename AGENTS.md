# Working agreement

This repository is standalone Ping Watchdog. Keep Windows work separate from Linux unless Joseph explicitly changes the scope.

## Required design review

Read DESIGN.md in full before implementing or modifying Windows UI. It tracks the Aurora Glass v1.5 defaults and supersedes prior visual references. Follow its token system and native-platform conventions, preserve accessibility floors, and apply Part 21 to unintentional gaps. Documented deviations below take precedence over visual defaults. ROADMAP.md and the open maintenance checklist define the remaining final-release milestones. Do not claim an unimplemented theme, uninspected rendering, or unperformed acceptance check as complete.

Keep the product, documentation, and release materials focused exclusively on Ping Watchdog. Do not add cross-promotion, external-product references, promotional cards, or upgrade prompts.

## Execution

Use the established chat scope when a request refers to a shared design or feature.

Do not let optional clarification block implementation. State a reasonable assumption and continue independent work. Use a normal, brief question only when missing information makes a safe implementation impossible; avoid a blocking question tool for optional choices. If the user stops work, stop immediately. An unanswered question is never permission for a restricted action.

Preserve unrelated changes, verify the current remote main before editing, and push authorized changes. Report actual validation and any limits honestly.

## Delivering updates

Joseph requires an updater-accessible Windows release after every implementation build, not only a source push or an Actions artifact. Keep automatic publication enabled after the existing tests pass. Before each subsequent implementation push, increment Version in PingWatchdog.csproj and refresh release notes; never reuse a published version. Verify the release includes installer, portable package, and update-feed assets. Report incomplete final-release acceptance honestly without withholding iterative updates. Documentation-only changes need no new binary release.

## Standards and handoff

Read the relevant documents in [InternalDesignGuidelines](https://github.com/JPLuker/InternalDesignGuidelines). The 2026-10-20 audit used commit `63eae9e89f3278dbd1443a3dc54c6186a877efbc`. Its README distinguishes flexible defaults from firm accessibility, privacy, security and untested-release rules. Explicit session instructions remain authoritative.

Read open GitHub issues before starting a session. [Maintenance mode checklist #4](https://github.com/JPLuker/Ping-Watchdog/issues/4) tracks the final-release work; RELEASE-AUDIT.md records evidence and limitations. Do not repeat the old migration backlog or infer passing human tests from CI.

The existing LICENSE is proprietary by deliberate project choice. Preserve it; the standards' suggested open-source default does not authorize relicensing. Record final license confirmation before maintenance publication.

## Deviations

- **Compact native Windows UI:** use Segoe UI and Consolas, opaque dense surfaces, native form chrome and controls where appropriate. Do not introduce expensive glass/material effects solely to match a web reference. Accessibility floors still apply.
- **Permanent update action:** Joseph explicitly requested Check for updates in the sidebar and a bottom-right in-app status popup. Keep it discoverable rather than moving it solely into Settings to satisfy P12 or moving the popup solely to match the default toast position.
- **Fitted connected topology:** Joseph requested every selected node visible without map scrolling and accepted the v1.17.3 connected branch style. Preserve that baseline; fix long values, keyboard access, non-color state and actual density defects rather than restoring old radial graphics or repeatedly redesigning the map.
- **Straightforward Wallboard controls:** the Show/Hide controls feature and F10 shortcut were removed at Joseph's request. Actions remain directly available in a wrapping toolbar; Escape/F11 exit. Do not recreate a controls drawer/toggle.
- **Staged Settings:** retain Save changes, Ctrl+S and save/discard/cancel protection for the grouped monitoring/preference form. This is intentional rather than an automatic demand to convert all settings to immediate application. Persistence failures must not be reported as saved.
- **Focused Windows distribution:** keep the existing Velopack installer/portable/update-feed integration. Additional package formats and asset renaming require a concrete distribution need and compatibility review; the generic format list is not a mandate to add every format.
- **Iterative publication:** Joseph explicitly requires an updater-accessible release after each passing implementation build. Preserve that workflow while hardening it. Iteration authorization does not mean final human acceptance is complete; record tester/version/package/OS evidence for the maintenance candidate.
