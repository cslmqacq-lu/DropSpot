# DropSpot Reliability, Diagnostics, and Quick Access Design

## Goal

Prepare DropSpot for a dependable public release while preserving its low-resource design. The release must recover individual USN volume sessions automatically, expose real health information, provide lightweight system access, and retain a small amount of useful activity state without scanning folders or indexing file contents.

## Product Constraints

- Keep one USN Journal reader per selected volume.
- Do not enumerate watched folders, poll directories, hash files, read file contents, or create one watcher per folder.
- UI refresh remains batched; hidden main-window cards are not rendered.
- Persistent activity history is bounded to 50 folders and three files per folder.
- Notifications are static indicators only; no continuous animation or sound.
- Existing settings, favorites, pins, floating-window behavior, and user-created PNG frames remain compatible.

## Architecture

### Volume monitoring

`FileMonitorService` owns the requested watch scopes and one `UsnJournalVolumeWatcher` per volume. Each watcher becomes a reconnecting session: it opens the volume, queries the current Journal ID and next USN, reads until failure, disposes the stale handle, waits with bounded backoff, and opens the volume again. A Journal rebuild is therefore handled as a fresh session rather than an endless retry on an invalid ID.

Each volume publishes a `VolumeMonitorStatus` snapshot with its root, state, message, attempt count, last successful connection, last event, and last error. Status values are `Waiting`, `Connecting`, `Healthy`, `Reconnecting`, `Error`, and `Stopped`. Partial success never hides a failed volume: overall monitoring is active while at least one requested watcher is running, and every requested watcher keeps trying independently until monitoring is stopped.

### Logging and diagnostics

`AppLog` writes asynchronous-safe, size-bounded text logs under `%AppData%\DropSpot\logs`. It retains five files and is called for startup, monitor transitions, recoverable errors, and unhandled exceptions. `Program` registers WinForms and AppDomain exception handlers before creating the main form.

`DiagnosticsForm` displays current per-volume health, the latest successful event time, recent errors, application version, and log directory. It supports refresh, copy diagnostics, and opening the log folder. The form reads immutable snapshots and does not perform disk scans.

### Window access and shortcuts

`TrayIconController` owns a `NotifyIcon` menu with restore main window, open floating window, pause/continue monitoring, diagnostics, and exit. Closing the main window still exits the application; the tray icon is an additional access surface, not a hidden-lifetime change.

`GlobalHotKeyManager` uses native Windows hotkey messages without polling. The final actions and configurable defaults are defined by `2026-08-29-configurable-global-hotkeys-design.md`, which supersedes the original shortcut proposal in this document.

### Activity history and pinned indicators

`ActivityHistoryStore` persists a bounded list in the existing settings file. Each entry contains folder path, last activity, change count, and at most three recent file records. The main form hydrates its active-folder state at startup and updates the store only through the existing batched UI flush.

Pinned folders track `LastActivity` and `LastOpenedAt`. When a matching folder or descendant changes after the last open, the pinned shortcut shows a small static activity dot and updated tooltip. Opening the pin marks it read. No timer or new monitor is introduced.

### Installation and maintainability

The standard installer uses the stable `%LocalAppData%\Programs\DropSpot` directory and reuses the previous location for in-place upgrades. Versioned portable archives remain available for side-by-side testing. Startup registration resolves the running installed executable before falling back to the stable path.

New responsibilities are placed in focused files. `MainForm` coordinates them but does not own log rotation, tray construction, hotkey P/Invoke, diagnostic formatting, or history serialization rules.

## Error Handling

- Missing or temporarily unavailable selected volumes remain in `Waiting` or `Reconnecting` and retry automatically.
- Unsupported file systems become `Error` with a clear message and a slower retry interval.
- Permission and handle failures are rate-limited in the UI but always recorded in the rolling log.
- Corrupt settings continue to recover from `.bak`; invalid history entries are skipped individually.
- Tray or hotkey initialization failure does not stop monitoring.

## Testing

- Unit/smoke coverage for volume status aggregation, retry decisions, diagnostic formatting, bounded history, pin unread state, and stable installer-path resolution.
- Existing monitor, favorite, pin, settings, window presentation, and UI resource smoke tests remain green.
- Build must complete with zero errors.
- Manual verification covers tray commands, both hotkeys, taskbar/floating restoration, diagnostics copy, and a real file write.

## Release Outcome

The implementation increments DropSpot to version 1.0.6. It updates README behavior descriptions and installer metadata, but does not install or replace the user's existing release unless separately requested.
