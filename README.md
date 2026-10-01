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
