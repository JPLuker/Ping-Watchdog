# Back up and restore Ping Watchdog

Open **Settings → Data and storage**.

## Create a backup

Choose **Create backup…** and save the `.pingwatchbackup.json` file outside the application data folder.

The backup includes all saved sites and folders, host addresses, labels, categories, per-host settings, disabled-host state, snooze expiry times, saved application preferences and outage history. It covers all sites regardless of the selected view. Temporary Quick Monitor sessions, live ping output, application binaries and startup error reports are excluded.

Save Settings changes first if you want them included. Unsaved Settings drafts are excluded. Backups are ordinary, unencrypted JSON files containing network inventory and history; choose an appropriate storage location.

A damaged or unsupported saved configuration, preference file or history file must be recovered before creating a complete backup. Ping Watchdog refuses to present session fallback data as a complete backup. Existing configuration-only export/import remains available for its narrower purpose.

## Restore a backup

1. Stop monitoring and close any other running copies of Ping Watchdog.
2. Choose **Restore backup…** and select a complete backup.
3. Review its creation time and site, host and history counts.
4. Confirm replacement. If Settings has a draft, save, discard or cancel it when prompted.

Restore **replaces** sites, folders, host settings, snoozes, preferences and history. It does not merge or deduplicate current and restored inventories. The app restarts to load the restored setup; startup monitoring follows the restored preference. Expired snoozes are already expired, and future expiry times retain their original deadline.

An incomplete, malformed or unsupported backup is refused before any saved data changes.

## Recovery during restore

Restore keeps byte-for-byte copies of the previous configuration, preferences, history and legacy site inventory, including missing-file state. A recovery record is written before replacements begin.

If a replacement fails, Ping Watchdog attempts to restore the previous files. If the process stops before the final commit, the next startup restores those originals before loading or autosaving the setup. If recovery cannot finish, keep the recovery files and resolve the file-access problem before retrying startup.

After a successful restore, `restore-originals-*.json` retains the original bytes in the data folder. After rollback, `restore-rolled-back-*.json` retains that recovery record. These are recovery records, not ordinary complete backups for the Restore backup chooser. They are retained until deliberately removed; no automatic cleanup policy is implemented yet. They can contain the same private inventory/history information as the original files.

Do not remove `restore-pending.json` while recovery is unresolved. It is needed to finish an interrupted restore. Rolling `.backup-1` through `.backup-3` files also remain available for individual atomic writes.

## Data location

**Open data folder** opens `%APPDATA%\PingWatchdog` in File Explorer.

The current Windows portable ZIP also uses this per-user location. Portable folder-local storage and full data deletion remain separate roadmap items. Copying the executable alone does not transfer saved data; use a complete backup.
