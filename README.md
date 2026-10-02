# Ping Watchdog

A lightweight Windows GUI for monitoring multiple IP addresses or hostnames and alerting only when failures become meaningful.

## Default monitoring behavior

- Ping interval: **2 seconds**
- Ping timeout: **1000 ms**
- Declare a host down after: **3 consecutive failures**
- Declare a host recovered after: **2 consecutive successes**
- A single failed ping only changes the host to **SUSPECT**
- The application alerts only once per outage, then alerts again when the host recovers

These values can be changed in the UI before monitoring starts.

## Notifications

Ping Watchdog uses native Windows app notifications for outage and recovery alerts. They are non-blocking, so monitoring continues without modal message boxes interrupting your work.

If native app notifications are unavailable, Ping Watchdog falls back to a notification-area balloon alert.

The app also has a system-tray icon:

- Double-click it to reopen Ping Watchdog.
- Right-click it to open the app, stop monitoring, or exit.
- Minimizing the window hides it to the notification area while monitoring continues.

## Usage

1. Enter one IP address or hostname per line.
2. Click **Start Monitoring**.
3. Watch the live status table:
   - **ONLINE** - responding normally
   - **SUSPECT** - one or more recent failures, but not enough to declare an outage
   - **OFFLINE** - failure threshold reached
4. Ping Watchdog sends a Windows notification when a host is declared offline and another when it recovers.

## Windows EXE

GitHub Actions builds a self-contained 64-bit Windows executable.

Open the repository **Actions** tab, run **Build Windows EXE**, then download the **PingWatchdog-win-x64** artifact.

The executable bundles the .NET runtime and Windows App SDK dependencies, so the target Windows PC does not need a separate .NET installation.


Download the latest standalone EXE from [Releases](https://github.com/JPLuker/Ping-Watchdog/releases/latest). IPs can also be separated by commas, spaces, or semicolons. The executable is unsigned. ICMP filtering can make an otherwise working device appear offline. Closing the application stops monitoring; minimizing keeps it running.


## Interface

Version 1.1 adds a dark interface and a Ping Watchdog application icon.

Enable **Show CMD view** to open a live console-style pane directly below the host table. Every ping attempt is displayed with the target first, for example:

`192.168.1.50: [14:32:08] ping 192.168.1.50 -n 1 -w 1000 -> Reply from 192.168.1.50: time=2ms TTL=128`

Failed attempts are highlighted in red. The pane keeps a rolling history and can be cleared without stopping monitoring.


## Sites / Groups

Version 1.2 reorganizes Ping Watchdog for multi-site use.

- Create named sites or groups from the left navigation panel.
- Each site stores its own IP addresses and hostnames.
- Site definitions persist between launches in the current Windows user's AppData folder.
- Monitoring starts across every saved site at once.
- Select **All Sites** for the combined fleet view or select one site to filter the host table.
- The live CMD view follows the same site filter.
- Outage and recovery notifications include the site name.

The command trace still keeps the host first on every line:

\`192.168.1.50: [Main Office] [14:32:08] ping 192.168.1.50 -n 1 -w 1000 -> Reply from 192.168.1.50: time=2ms TTL=128\`

Failed ping attempts are displayed in red.


## Host labels and reusable configs

Version 1.3 adds reusable scan definitions and host nicknames.

- Right-click any host in the live table and choose **Set label / nickname**.
- Labels persist with the site and are shown in the table, CMD trace, and outage/recovery notifications.
- **Save Config** exports sites, hosts, labels, ping interval, timeout, failure threshold, recovery threshold, selected site, and CMD-view preference.
- **Load Config** restores the complete scan definition so monitoring can be resumed immediately.
- Config files use readable JSON with the `.pingwatch.json` extension.


## Version 1.4 usability changes

- **Add Site** and **Rename Site** now use focused popup dialogs; the permanent site-name textbox is gone.
- Host-list changes auto-save when focus leaves the editor, when switching sites, when monitoring starts, and when the app closes.
- Ping Watchdog now maintains a full automatic working-state config in the current user's AppData folder.
- Fresh installs seed themselves from the bundled `default.pingwatch.json` file.
- Releases are packaged as `PingWatchdog-win-x64.zip` with the EXE, starter config, and a quick-start text file.
- Double-clicking a site is a shortcut to rename it.


## Version 1.5 live site management

Sites and host lists can be changed while monitoring is active.

- Adding a host starts a new ping worker without interrupting unchanged hosts.
- Removing a host cancels only that host's worker.
- Renaming a site restarts only the hosts whose site identity changed.
- Deleting a site stops only that site's workers.
- Adding, renaming, deleting, and editing host lists stay available while the scan is running.
- Existing hosts keep their current monitoring state when unrelated sites or hosts change.

Timing and outage-threshold controls remain locked during an active scan so one scan uses consistent monitoring rules.


## Version 1.6 automatic GitHub updates

Ping Watchdog now uses Velopack for distribution and self-updates.

- The app checks the public GitHub Releases feed automatically on startup and every six hours.
- If Watchdog is idle, an available update is downloaded and applied automatically with an app restart.
- If monitoring is active, the update downloads in the background and is applied the next time Watchdog restarts so an active monitoring session is not interrupted.
- **Check Updates** in the header can force an immediate check.
- GitHub Actions publishes both a normal Windows installer and a self-updating portable ZIP.
- Velopack delta packages are generated when a prior compatible release is available, reducing future update download size.
- Users of pre-1.6 standalone ZIP builds need to move to a 1.6 Velopack installer or portable package once. After that, manual release downloads are no longer required.

The update source is the official `JPLuker/Ping-Watchdog` GitHub Releases feed.


## Version 1.7 product UX pass

Version 1.7 rebuilds the primary interface around the intended day-to-day monitoring workflow.

- New product-style header and navigation rail.
- Dashboard cards for total, online, suspect, and offline hosts.
- Cleaner monitoring controls with stronger visual hierarchy.
- Refined host editor and live-apply messaging.
- Owner-drawn site navigation for a more consistent Windows dark UI.
- More restrained status presentation in the host grid.
- Explicit copyright/ownership presentation in the application footer and executable metadata.

## Copyright and license

Copyright © 2026 Joseph Luker. All rights reserved.

The repository is publicly viewable, but Ping Watchdog is **not licensed as open-source software**. See [LICENSE](LICENSE) for the permissions and restrictions that apply.
