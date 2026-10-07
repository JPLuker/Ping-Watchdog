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


SETTINGS
Use the permanent Settings button, Ctrl+, or ••• > Settings.
The Settings window controls tray behavior, notifications, CLI/Wallboard preferences,
monitoring defaults, outage-history defaults, and automatic update checks.
A manual Check for Updates button is always available in Settings even if automatic checks are disabled.
The main app bar also keeps its Updates button permanently visible.


FULL-CONTROL WALLBOARD
Wallboard is no longer display-only.
Use the top toolbar or press O to open Operations.
From Wallboard you can start/stop monitoring, manage sites and hosts, edit labels,
change stopped-session monitoring defaults, import/export config, clear CLI,
open History/Settings, check for updates, switch monitors, and return to the main window.
All Wallboard actions use the same live session and autosaved configuration as the main window.


BACKGROUND UPDATES
Ping Watchdog checks for updates on launch and downloads them in the background.
When an update is ready, choose Restart Now or Later.
Manual update controls always remain in Settings > Updates and in the tray when an update is staged.
The main-screen/Wallboard update controls are hidden by default.
Developers can restore them from Settings > Updates > "Developer: show update control in the main UI".


SITE ORGANIZATION
Use the Organize button to open the filesystem-style site tree.
Create folders and nested folders, create/move sites, rename/move folders, and reorganize freely.
Deleting a folder does not delete its sites; the contents move to the parent folder.
Existing ungrouped sites remain at the root.

WALLBOARD HOST NODES
The topology now shows individual endpoint nodes around each site.
Labeled endpoints show the nickname first and the IP/hostname underneath.
Endpoint node colors reflect the host's real ONLINE / SUSPECT / OFFLINE state.


HOST CATEGORIES
Right-click a monitored host and choose Set category... to classify it separately from its nickname/label.
Examples: AP, Firewall, Switch, Router, Server, Printer, Camera, UPS, Workstation, IoT, or any custom category.
The live host table shows Category as its own column, and categories are saved with the site configuration.


DISABLED HOSTS
Disabling a host removes its active ping worker immediately.
A disabled host cannot create new SUSPECT/DOWN/RECOVERED events, notifications, CLI entries, or Wallboard status even if a ping was already in flight when Disable was pressed.
Existing historical outage records remain in Outage History because they describe events that occurred before the host was disabled.
Re-enable the host to resume monitoring it.


WALLBOARD AUTO-FIT
Wallboard automatically scales dense site/host topologies to the available monitor area.
It does not require the Wallboard window to be enlarged to resolve label collisions.
The no-collision rules remain local to each node; when necessary, the entire topology scales down instead of sending labels far away from their hosts.
