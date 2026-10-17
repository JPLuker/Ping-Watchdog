# Wallboard design migration

Authority: [DESIGN.md](DESIGN.md). Scope and release gates: [ROADMAP.md](ROADMAP.md).

The v1.14.81 Windows Wallboard is the existing behavioral baseline, not a completed implementation of the new design standard. Its previous external design reference is superseded.

## Preserve

Monitoring state, site/host identity, caption collision protection, complete addresses, host and outage context actions, snooze, saved time format, history filters, site organization, ping output, display selection, and safe entry/exit.

## Remaining design work

- Adopt centralized Aurora Glass tokens and System / Light / Dark appearance.
- Use one focal point, floating pill controls, native window conventions, and restrained material layering. Keep dense content on opaque surfaces.
- Remove repeated facts, explanatory subtitles, unnecessary summaries, and visible shortcut legends.
- Replace tiny-font topology scaling with readable, navigable layouts. Keep full values available and prevent vanished or overlapping captions.
- Add keyboard-accessible host selection/actions and meaningful accessible status/name information.
- Review all interaction states, actual contrast, reduced transparency/motion, large system text, and display scaling.

## Verification

Existing Windows automation covers caption visibility/collisions at several canvas sizes with output shown/hidden, topology/outage hit testing, host actions, published UI, startup, and recovery. Passing that baseline does not certify the new appearance.

Record actual rendered dimensions and inspected captures for both themes, including dense inventories and long labels/addresses. Physical display, assistive-technology, installer/update, and full-workday acceptance checks remain release gates until performed.
