PING WATCHDOG - QUICK START

1. Start Ping Watchdog from the Velopack Portable package or install it with PingWatchdog-Setup.exe.
2. Click "+ Add Site" to create a location or group.
3. Paste IP addresses or hostnames into that site's host box.
4. Click "Start Monitoring".
5. Right-click a monitored host to give it a nickname.

Your working setup auto-saves under your Windows user profile.
The included default.pingwatch.json is used automatically as the first-launch starter config.

UPDATES
Ping Watchdog checks GitHub automatically on startup and every six hours.
If an update is available while Watchdog is idle, it downloads, installs, and restarts automatically.
If monitoring is active, the update downloads without interrupting the scan and applies the next time Watchdog restarts.
Use "Check Updates" in the header to force a check.

Use Save Config / Load Config when you want portable named scan files you can move between PCs.


COPYRIGHT
Copyright © 2026 Joseph Luker. All rights reserved.
See LICENSE included with this release for permitted use.


WALLBOARD / NOC MODE
Click "Wallboard" or press F11 for a fullscreen side-monitor display.
If more than one monitor is attached, Wallboard opens on the other monitor automatically.
Press M to move it to another monitor.
Press Esc or F11 to close Wallboard.


WALLBOARD CLI
The live CLI/CMD trace is shown at the bottom of Wallboard by default.
Press C to hide/show the CLI panel without leaving Wallboard.


APPLYING DOWNLOADED UPDATES
When an update is downloaded, click "Restart to Update".
Watchdog saves your current setup, stops monitoring if necessary, applies the downloaded update, and reopens automatically.


WALLBOARD EXIT
Press Esc or F11 to leave Wallboard and return to the main Ping Watchdog window.
This does not exit Ping Watchdog.


WALLBOARD SITE HEALTH
A site with some failed hosts stays yellow while more than half of its configured hosts are still replying.
The site turns red when half or fewer are replying. Individual failed host dots remain red.


MODERN APP BAR
The top bar always shows Wallboard and Updates.
Import/Export configuration are under the ••• menu so they cannot crowd the updater off-screen.
The Updates button changes state automatically as Watchdog checks, downloads, and prepares releases.


OUTAGE HISTORY
Open ••• > Outage history... or press Ctrl+H.
History is saved across restarts and updates in your Ping Watchdog AppData folder.
Choose Last 24 hours / 7 days / 30 days / All time and optionally hide SUSPECT events.
Those display preferences are saved automatically.
In Wallboard: H cycles the history range and S toggles SUSPECT events.
