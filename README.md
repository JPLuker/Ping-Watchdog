# Ping Watchdog

<img src="Assets/WatchdogLogo.png" alt="Ping Watchdog dog logo" width="72">

A desktop ICMP monitoring tool for tracking multiple IP addresses and hostnames across sites. Configurable failure and recovery thresholds help distinguish a brief missed reply from an outage.

Available for Windows, with a native Linux beta built using Avalonia.

## Features

- **Multi-site monitoring:** Monitor all configured hosts together, filter by site, and assign host nicknames.
- **Site organization:** Group sites into folders and nested folders, and edit hosts while monitoring continues.
- **Outage alerts:** Receive outage and recovery notifications when consecutive failure or success thresholds are reached.
- **Persistent history:** Review outages across sessions, filter by site or time range, hide suspect events, and export history to CSV.
- **Live command trace:** View individual ping results alongside the host table, with failed attempts highlighted in red.
- **Fullscreen Wallboard:** Display site and host topology, current outages, history, and command output. Windows Wallboard also provides monitoring and site-management controls.
- **Saved configuration:** Automatically save your working setup and import or export `.pingwatch.json` files.
- **Background updates:** Check for releases and download updates in the background, then restart when ready to apply them.

## Download

Official builds are available on the [Releases page](https://github.com/JPLuker/Ping-Watchdog/releases).

| Platform | Package | Installation |
| --- | --- | --- |
| Windows x64 | `PingWatchdog-win-Setup.exe` | Run the installer. |
| Windows x64, portable | `PingWatchdog-win-Portable.zip` | Extract the ZIP and run `PingWatchdog.exe`. |
| Linux x64, beta | `PingWatchdog.AppImage` | Download from a release tagged `-linux`, make it executable, and run it. |

The packages include the .NET runtime; a separate .NET installation is not required to run them. Windows builds are unsigned. Linux requirements and desktop integration details are documented in [README-LINUX.md](README-LINUX.md).

```bash
chmod +x PingWatchdog.AppImage
./PingWatchdog.AppImage
```

## Getting started on Windows

1. Open Ping Watchdog and create a site with **+ Add Site**.
2. Enter the site's IP addresses or hostnames, one per line.
3. Adjust the monitoring interval and outage thresholds if needed.
4. Click **Start Monitoring**. Select a site or **All Sites** to choose what the table displays.
5. Right-click a host to set its label, enable **Show CMD view** for command output, or open **Wallboard** for a fullscreen view.

Use **Settings** to configure notifications, history filters, display preferences, and updates. On Windows, **Settings → Updates** always provides a manual update check. The developer option on that page can also expose update controls in the main window and Wallboard.

### Host status and default thresholds

| Status | Meaning |
| --- | --- |
| **ONLINE** | The host is replying normally. |
| **SUSPECT** | Recent failures have not yet reached the outage threshold. |
| **OFFLINE** | The consecutive failure threshold has been reached. |
| **UNKNOWN** | The host has not yet established a monitoring state. |

Defaults are a **2-second interval**, **1,000 ms timeout**, **3 consecutive failures** to declare an outage, and **2 consecutive successes** to declare recovery. Notifications are sent when an outage is declared and when the host recovers, rather than for every failed ping.

ICMP filtering can make a reachable device appear offline. Ping Watchdog reports ping availability, not the health of every service on a host.

### Windows keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `F11` | Open Wallboard, or return to the main window from Wallboard. |
| `Esc` in Wallboard | Return to the main window. |
| `Ctrl+H` | Open outage history. |
| `Ctrl+,` | Open Settings. |
| `O` in Wallboard | Open or close Operations. |
| `P` in Wallboard | Start or stop monitoring. |
| `C` in Wallboard | Show or hide the command trace. |
| `M` in Wallboard | Move to the next monitor. |
| `H` / `S` in Wallboard | Cycle the history range / toggle suspect-event visibility. |

## Configuration and data

### Windows startup recovery

If the Windows app fails during startup, a separate recovery window shows the error details and offers **Check for updates**, **Open downloads**, and **Retry startup**. If a newer release is available, recovery can download it and offer **Install update and restart** without opening the dashboard. If no fix has been published yet, it reports that instead of repeating the failed startup automatically.

Reports are saved under `%LOCALAPPDATA%\PingWatchdog\Startup`. Recovery does not reset your saved configuration. The startup observer detects early process exits after it starts; failures that prevent the executable or .NET runtime from launching still require an installer download.

### Saved settings

Settings, sites, host labels, and history persist locally between launches. Export configuration from the app menu to move a setup between computers. Windows and Linux use compatible `.pingwatch.json` configuration formats.

| Platform | Working configuration | Event history |
| --- | --- | --- |
| Windows | `%APPDATA%\PingWatchdog\autosave.pingwatch.json` | `%APPDATA%\PingWatchdog\event-history.json` |
| Linux | `~/.config/PingWatchdog/autosave.pingwatch.json` | `~/.local/state/PingWatchdog/event-history.json` |

Linux respects `XDG_CONFIG_HOME` and `XDG_STATE_HOME`. Fresh installations use the bundled [default.pingwatch.json](default.pingwatch.json) as their starter configuration.

## Development

The Windows application uses C# and Windows Forms. The Linux application uses C# and Avalonia. Both target .NET 8 and use Velopack for release packaging and updates. They compile `Shared/Presentation.cs` for common visual definitions; Windows is the layout reference. See [Linux layout and desktop verification](README-LINUX.md#windows-layout-alignment) for the shared maintenance contract and platform limits.

Build the Windows application on Windows with the **.NET 8 SDK**:

```powershell
dotnet build PingWatchdog.csproj -c Release
dotnet run --project PingWatchdog.csproj -c Release
```

Build and run the Linux application with the **.NET 8 SDK**:

```bash
dotnet build linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj -c Release
dotnet run --project linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj -c Release
```

Run the platform's built-in checks:

```powershell
# Windows
dotnet run --project PingWatchdog.csproj -c Release -- --self-test
dotnet run --project PingWatchdog.csproj -c Release -- --ui-smoke-test
dotnet run --project PingWatchdog.csproj -c Release -- --recovery-smoke-test
```

```bash
# Linux
dotnet run --project linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj -c Release -- --self-test
```

GitHub Actions builds and publishes Windows installer/portable packages and Linux AppImages. Windows release checks include startup and UI tests; UI screenshots are available in the workflow's `PingWatchdog-UI-*` artifact.

## Issues

Report bugs through [GitHub Issues](https://github.com/JPLuker/Ping-Watchdog/issues). Include your app version, operating system, steps to reproduce the problem, and a screenshot when relevant.

## License

Copyright © 2026 Joseph Luker. All rights reserved.

Ping Watchdog is proprietary software with publicly viewable source code. The [LICENSE](LICENSE) permits personal or internal business use of official compiled releases. Other uses, including modification and redistribution, require written permission from Joseph Luker.


## Host categories

Hosts can have both a **Label** and a separate **Category**. Use labels for a human-friendly identity such as `Front Lobby` and categories for device type or role such as `AP`, `Firewall`, `Switch`, `Router`, `Server`, `Printer`, `Camera`, `UPS`, `Workstation`, or any custom value. On Windows, right-click a monitored host and choose **Set category...**. Category metadata is saved with the site configuration and is cleaned up automatically when an address is removed. Linux preserves the same category metadata in the shared config schema.
