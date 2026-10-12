# Wallboard design review

Reference: [Watchdog Toolkit style guide](https://github.com/JPLuker/Watchdog-Toolkit/blob/main/STYLE-GUIDE.md).
Target: standalone Windows Ping Watchdog. Toolkit and Linux remain separate.

The fullscreen presentation now uses the guide's navy background and panel surfaces, purple emphasis, semantic green/yellow/red health colors, Segoe UI headings, Consolas technical output, rounded summary panels and keyboard-focus outlines on rounded buttons. The grid, radar sweep, traveling connection pulses and offline site glow were removed so observed health and host captions carry the emphasis.

The existing topology placement and collision rules, host and outage hit testing, monitoring commands, shared Hosts editor, saved time format, history filters, site organization, and ping output remain in place. Summary labels use sentence case. The header identifies the current view with one product subtitle.

Validation: the Windows pipeline runs the existing alert/configuration tests, caption visibility and collision regressions at multiple canvas sizes with the ping log on/off, host and outage hit testing, published UI/Settings/Hosts checks, startup and recovery checks. Check the workflow result for actual pass/fail status.

Limitations: fullscreen topology captions still use compact text and scale for dense site inventories. Native advanced dialogs retain their existing presentation. Generated Windows screenshots require human visual review; automated layout assertions do not establish visual quality or cover every user's display scaling.
