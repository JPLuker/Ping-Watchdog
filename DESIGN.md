# Ping Watchdog design

What Ping Watchdog is, how its screens work, and how it looks. The rules every project follows live in [InternalDesignGuidelines DESIGN.md](https://github.com/JPLuker/InternalDesignGuidelines/blob/main/DESIGN.md): Part A always, Part B (Quiet Precision) because Joseph chose it for this project on 2026-11-11. This file only adds what's specific to Ping Watchdog. Deviations are in [AGENTS.md](AGENTS.md).

## What it does

Watches IP addresses and hostnames with ICMP ping, grouped by site, and shows when a host stops answering, how long it was down, and when it came back. Single user, local, no account and no server.

## Screens

| Screen | Job | Notes |
|---|---|---|
| Main window | Doing: watch hosts, add hosts, start and stop monitoring | Sidebar (All sites, sites, Outage history, Settings, Check for updates, More) and a work area with **Map** and **List** over one shared Ping activity pane. One primary action: Start monitoring. |
| Map | See every host at once | Connected branches (Monitor → site → host). Every selected host fits without scrolling; addresses are always whole. Right-click a host for actions. |
| List | Find and edit hosts | Search, filter chips (All, Priority, Offline, Disabled, Snoozed), multi-select, right-click actions, F2 to edit, Delete, Ctrl+Z Undo. Edits save at once. |
| Wallboard | Show state on a big screen | Full window, display-only. Opens on the screen it was last on. Escape or F11 returns. |
| Settings | Configuring | General, Appearance, Monitoring, Notifications, History, Updates, Data and storage, Advanced, About. Staged with Save changes (deviation). |
| Outage history | Look back | Filters and range; full names and addresses. |
| Dialogs | One task each | Add hosts (Quick Add / Quick Monitor), Edit host (address and label), site name, group, monitoring policy, move to site, restore, delete data, update restart and failure. |

## Host state

Online, Suspect (missed a reply, not yet an outage), Offline, plus Idle, Disabled and Snoozed. State always shows as a word with a dot or marker shape; color is never the only signal. Online is `success`, Suspect is `warning`, Offline is `danger`, and the rest are `text-3`.

## Look (Quiet Precision, native Windows)

- **Tokens:** `AuroraTheme.cs` holds the palette, spacing, radii and type. Nothing else hard-codes a color, font or radius. The palette is the standards' B3 values exactly, light and dark.
- **Accent:** ink. Primary buttons are near-black in light mode and near-white in dark. Hue is kept for host state.
- **Surfaces:** canvas for the window and sidebar; `surface` with a 1px border for the map card, tables and the ping log; shadows only on the update popup and menus.
- **Shape:** buttons and inputs 8px, list rows 6px, cards 12px, dialogs 16px. Filter chips are pills.
- **Type:** Segoe UI (14px body, 13px labels and captions, never smaller), Segoe UI Semibold for titles (24px page title, 20px section, 16px group), Consolas for addresses, logs and timestamps.
- **Focus:** a 2px blue ring (`focus`), never the accent.
- **Motion:** none except the update popup and the loading cover's bar.

## Behavior worth knowing

- **Loading cover:** if the window doesn't answer for 0.4 s, a dimmed "Loading" cover appears over it from a separate thread and disappears when the app answers. Waits over a second go to `Logs\responsiveness.log` with what the app was doing (#40).
- **Startup:** Settings → General → Startup can open Ping Watchdog at sign-in, start monitoring and open the Wallboard on its last screen (#40).
- **Updates:** checked at most daily. Installing closes the app, applies the update without a separate installer window, and reopens it; the next start confirms the version changed (#35).
