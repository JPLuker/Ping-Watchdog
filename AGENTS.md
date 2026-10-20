# Working agreement

This repository is standalone Ping Watchdog. Keep Windows work separate from Linux unless Joseph explicitly changes the scope.

## Required design review

Read DESIGN.md in full before implementing or modifying Windows UI. It is the design authority and supersedes prior visual references. Follow its token system, native-platform conventions, accessibility floors, and Part 21 review checklist. ROADMAP.md defines the remaining final-release milestones. Do not claim an unimplemented theme, uninspected rendering, or unperformed acceptance check as complete.

Keep the product, documentation, and release materials focused exclusively on Ping Watchdog. Do not add cross-promotion, external-product references, promotional cards, or upgrade prompts.

## Execution

Use the established chat scope when a request refers to a shared design or feature.

Do not let optional clarification block implementation. State a reasonable assumption and continue independent work. Use a normal, brief question only when missing information makes a safe implementation impossible; avoid a blocking question tool for optional choices. If the user stops work, stop immediately. An unanswered question is never permission for a restricted action.

Preserve unrelated changes, verify the current remote main before editing, and push authorized changes. Report actual validation and any limits honestly.

## Delivering updates

Joseph requires an updater-accessible Windows release after every implementation build, not only a source push or an Actions artifact. Keep automatic publication enabled after the existing tests pass. Before each subsequent implementation push, increment Version in PingWatchdog.csproj and refresh release notes; never reuse a published version. Verify the release includes installer, portable package, and update-feed assets. Report incomplete final-release acceptance honestly without withholding iterative updates. Documentation-only changes need no new binary release.
