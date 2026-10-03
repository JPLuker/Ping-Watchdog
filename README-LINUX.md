# Ping Watchdog for Linux

Ping Watchdog now has a native Linux desktop build. It is not a Wine wrapper around the Windows WinForms application; the Linux frontend is built with Avalonia and uses the same Ping Watchdog configuration model and monitoring philosophy.

## Current release

The Linux build is currently a **beta** and targets **64-bit x86 Linux (`linux-x64`)**.

Download `PingWatchdog.AppImage` from the latest release whose tag ends in `-linux`.

If the AppImage is not already executable:

```bash
chmod +x PingWatchdog.AppImage
./PingWatchdog.AppImage
```

The AppImage is self-contained and includes the .NET runtime.

## Feature coverage

The Linux build includes:

- Multi-site ICMP monitoring.
- ONLINE / SUSPECT / OFFLINE state handling.
- Configurable interval, timeout, failure threshold, and recovery threshold.
- Live site and host edits while monitoring.
- Host labels / nicknames.
- Persistent nested folders and site organization.
- Persistent outage history with 24-hour, 7-day, 30-day, and all-time filtering.
- Optional SUSPECT-event filtering.
- CSV history export.
- Live Linux ping-command trace.
- Fullscreen Wallboard.
- Site majority-health coloring.
- Individual topology nodes for monitored hosts, including nickname/label and IP/hostname.
- Settings UI.
- Background GitHub update checks and self-updating AppImage packages on the Linux beta channel.
- Desktop outage/recovery notifications through `notify-send` when it is available.

## Configuration and history locations

Ping Watchdog follows the XDG directory conventions when the matching environment variables are present.

Default configuration path:

```text
~/.config/PingWatchdog/autosave.pingwatch.json
```

Default persistent event-history path:

```text
~/.local/state/PingWatchdog/event-history.json
```

If `XDG_CONFIG_HOME` or `XDG_STATE_HOME` is set, Ping Watchdog uses those roots instead.

The Linux configuration schema is intentionally compatible with the Windows `.pingwatch.json` format, including sites, hosts, labels, folders, thresholds, and display/history preferences.

## Notifications

Ping Watchdog attempts to use the standard `notify-send` command for desktop outage and recovery notifications. Monitoring does not depend on it: if `notify-send` is unavailable, monitoring and persistent event history continue normally.

On Debian/Ubuntu-derived distributions, `notify-send` is commonly provided by:

```bash
sudo apt install libnotify-bin
```

## Updating

Linux releases use a separate Velopack `linux` channel and are currently published as GitHub prereleases while the native Linux frontend is in beta.

The managed AppImage checks the Linux beta release feed in the background. When a new Linux build is downloaded, Ping Watchdog can restart into the staged update. Windows releases remain on their separate Windows channel.

## Compatibility notes

- Current packaged target: `linux-x64`.
- ICMP can still be filtered by a host, firewall, network policy, VPN, or container/sandbox policy; a successful service connection does not guarantee that the target will answer ICMP.
- Desktop integration can vary by distribution and desktop environment, which is why the first native Linux releases are marked beta.

## Copyright

Copyright © 2026 Joseph Luker. All rights reserved.

See [LICENSE](LICENSE) for the repository's permissions and restrictions.
