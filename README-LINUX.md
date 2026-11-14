# Ping Watchdog for Linux

The Linux build is a native Avalonia app with the same look and features as the Windows app: Map, List, Wallboard, Outage history and Settings, in light and dark. It reads and writes the same `.pingwatch.json` configuration, so you can move sites and hosts between the two.

**Status:** the Linux build is in maintenance mode alongside Windows. Each Linux release is published as a separate **UNTESTED** pre-release until it has been installed and run on a real machine ([RELEASES.md Part 2](https://github.com/JPLuker/InternalDesignGuidelines/blob/main/RELEASES.md)).

## Download

Open the newest release whose title starts with **UNTESTED — Linux builds** on the [releases page](https://github.com/JPLuker/Ping-Watchdog/releases). Every file is 64-bit x86 (`x64`) and includes the .NET runtime.

| File | Use it when | Updates |
| --- | --- | --- |
| `PingWatchdog-<version>-Linux-x64.AppImage` | You want one file that runs anywhere | Updates itself from inside the app |
| `PingWatchdog-<version>-Linux-x64.deb` | Debian, Ubuntu, elementary OS, Mint, Pop!_OS | Install the next .deb |
| `PingWatchdog-<version>-Linux-x64.rpm` | Fedora, openSUSE, RHEL and similar | Install the next .rpm |
| `PingWatchdog-<version>-Linux-x64.flatpak` | You prefer Flatpak | Install the next bundle |
| `PingWatchdog-<version>-Linux-x64.tar.gz` | You want a folder you can run from anywhere | Download the next one |

```bash
# AppImage
chmod +x PingWatchdog-*-Linux-x64.AppImage && ./PingWatchdog-*-Linux-x64.AppImage

# .deb (installs the ping-watchdog command and an app menu entry)
sudo apt install ./PingWatchdog-*-Linux-x64.deb

# .rpm
sudo dnf install ./PingWatchdog-*-Linux-x64.rpm

# Flatpak (needs the Flathub org.freedesktop.Platform 24.08 runtime)
flatpak install --user ./PingWatchdog-*-Linux-x64.flatpak
```

The builds are unsigned. Compare `sha256sum <file>` with the line for that file in the release's `SHA256SUMS.txt`.

## What's the same as Windows

Everything in the main [README](README.md) applies: Add hosts (Quick Add and Quick Monitor), the connected Map, the List with search, filter chips, multi-select, F2, Delete and Ctrl+Z, right-click host actions, priority alarms, snooze, per-host monitoring, the display-only Wallboard (Esc or F11 to return), Outage history with CSV export, and staged Settings.

Linux differs in a few places on purpose:

- **Ping in terminal** opens a continuous `ping` in your desktop's terminal, instead of a Windows command window.
- **Open Ping Watchdog when you sign in** writes `~/.config/autostart/ping-watchdog.desktop`.
- **Notifications** use `notify-send` (`sudo apt install libnotify-bin` on Debian-based systems). Priority alarms play through `pw-play`, `paplay` or `canberra-gtk-play`, with any WAV, OGG, FLAC or MP3 you choose.
- **Minimize to the system tray** needs a tray host (most desktops have one; GNOME needs the AppIndicator extension). Without one, minimizing uses the taskbar and monitoring keeps running.
- There's no portable-storage mode, no Export monitoring batch (that creates a Windows .bat file), and no Loading cover.

## Files

Ping Watchdog follows the XDG conventions.

| What | Where |
| --- | --- |
| Sites, hosts and settings | `~/.config/PingWatchdog/autosave.pingwatch.json` |
| Display and startup choices | `~/.config/PingWatchdog/preferences.json` |
| Outage history | `~/.local/state/PingWatchdog/event-history.json` |
| Copies kept before a restore | `~/.local/state/PingWatchdog/backups/` |

`XDG_CONFIG_HOME` and `XDG_STATE_HOME` move these when they're set. **Settings → Data and storage** creates and restores backups and deletes saved data; files that aren't Ping Watchdog's are left alone.

## Wayland

The app runs through X11 or XWayland by default. To try the native Wayland backend:

```bash
WATCHDOG_BACKEND=wayland ./PingWatchdog-*-Linux-x64.AppImage
```

## ICMP

ICMP can be filtered by a host, firewall, VPN or container policy. Inside the Flatpak sandbox, ping needs unprivileged ICMP sockets, which most distributions allow (`net.ipv4.ping_group_range`).

## How it's checked

Every Linux build runs the engine and desktop-service regressions, then drives the real windows under Xvfb at 100%, 150% and 200% scale and under headless Weston (native Wayland), saving light and dark captures that are reviewed. The release build installs the .deb with apt, extracts the .rpm and .tar.gz and runs each, runs the AppImage's own regressions, installs the Flatpak, and checks every asset and checksum after publishing. [TESTING.md](TESTING.md) lists what that doesn't cover.

## Copyright

Copyright © 2026 Joseph Luker. All rights reserved. See [LICENSE](LICENSE). Third-party notices ship in each package's `ThirdPartyNotices` folder.
