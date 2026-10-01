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
