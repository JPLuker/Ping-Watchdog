# Storage and saved-data deletion

Open **Settings → Data and storage**.

## Portable storage

Existing copies continue to use per-user AppData. There is no automatic migration based on the ZIP filename or a package update.

In a portable copy, **Enable portable storage…** copies the saved setup into a `Data` folder in the package root and restarts. Sites, organization, host options, snooze deadlines, saved preferences and history transfer together. Save Settings drafts first. The original AppData files, old backups and old startup reports are retained; temporary monitoring sessions are excluded.

The switch refuses an existing destination `Data` folder or portable marker. It does not merge or replace destination data. The `PingWatchdog.portable` marker is written only after the new setup is committed. If activation fails, the original setup remains active; keep any resulting `Data` or `Data.pending-*` folder for review rather than deleting it blindly.

For packaged portable releases, the data folder and marker sit beside `current`, outside the application directory replaced during updates. Move the whole portable package, including `Data` and `PingWatchdog.portable`. Folder-local storage is disabled in Setup-installed copies. Unpackaged executable copies may opt in too.

Preferences, full backup/restore, startup reports, notification artwork and diagnostic paths all use the active location. A read-only or unsupported portable location reports a failure; the app does not silently switch back to AppData. Use a complete backup to transfer data to another copy.

## Delete saved data

Stop monitoring, close other running copies, then select **Delete saved data…**. Review the affected folders, type **DELETE** exactly and activate **Delete and close**. Escape or Cancel changes nothing. Unsaved Settings protection still applies.

Deletion removes all files in the active data folder, including config/preferences/history, rolling backups, pending restore journals and retained restore originals. It also removes startup reports and notification artwork for the active storage mode. Linked folders/files are refused.

The application closes without saving its old in-memory setup. The next launch starts with no hosts; bundled example hosts are not reimported. A minimal empty configuration and fresh startup diagnostics may be created.

If deletion fails or is interrupted, an intent marker remains outside the data folder. The next startup retries cleanup before loading saved data. A locked file or unsupported intent record prevents normal startup until resolved. Failed deletion is reported explicitly and must not be treated as complete.

External exports/backups, AppData retained when enabling portable mode, other portable copies, installation/update packages, OS notifications and OS-managed caches are outside this operation. Deletion is ordinary filesystem removal, not secure erasure. The portable-storage marker remains so deleting portable data does not unexpectedly reactivate the old AppData setup.

## Wallboard

The top toolbar is removed. Escape/F11 exits Wallboard; existing keyboard shortcuts and host context actions remain. The canvas occupies the full window without a reserved toolbar row.
