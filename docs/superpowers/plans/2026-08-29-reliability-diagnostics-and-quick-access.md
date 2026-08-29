# DropSpot Reliability, Diagnostics, and Quick Access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver DropSpot 1.0.6 with per-volume recovery and health, local diagnostics, tray and hotkey access, bounded activity history, and pinned-folder activity indicators without adding directory scans.

**Architecture:** Keep the USN reader as the sole event source and turn each selected volume into an independent reconnecting session. Add focused controllers and stores for logging, diagnostics, tray access, hotkeys, history, and pin unread state; `MainForm` remains the coordinator.

**Tech Stack:** .NET 8, Windows Forms, Windows USN Journal APIs, `NotifyIcon`, Win32 `RegisterHotKey`, JSON settings, Inno Setup.

## Global Constraints

- No directory enumeration, polling, content reads, hashing, or per-folder watchers.
- Persistent history is limited to 50 folders and three recent files per folder.
- No continuous animation or sound.
- Preserve existing settings, favorites, pins, floating behavior, and PNG frames.
- Version target is 1.0.6.

---

### Task 1: Reconnecting per-volume monitor sessions

**Files:**
- Create: `VolumeMonitorStatus.cs`
- Modify: `FileMonitorService.cs`
- Test: `SmokeTest.cs`

**Interfaces:**
- Produces: `VolumeMonitorStatus`, `VolumeMonitorState`, `FileMonitorService.VolumeStatusChanged`, `FileMonitorService.VolumeStatuses`, and `FileMonitorService.IsRunning`.

- [ ] Add status model and smoke tests for healthy, reconnecting, and partial-volume aggregation.
- [ ] Refactor `UsnJournalVolumeWatcher` so every reconnect reopens the volume and requeries the Journal.
- [ ] Preserve requested scopes even when a drive is temporarily unavailable.
- [ ] Run `dotnet build -c Release` and monitor smoke tests.

### Task 2: Rolling logs and diagnostics

**Files:**
- Create: `AppLog.cs`
- Create: `DiagnosticsSnapshot.cs`
- Create: `DiagnosticsForm.cs`
- Modify: `Program.cs`
- Modify: `MainForm.cs`
- Modify: `SettingsForm.cs`
- Test: `SmokeTest.cs`

**Interfaces:**
- Consumes: `FileMonitorService.VolumeStatuses`.
- Produces: `AppLog.Info/Error`, `DiagnosticsSnapshot.Create`, and a diagnostics window reachable from settings and tray.

- [ ] Add bounded rolling log writer and deterministic diagnostics formatter tests.
- [ ] Register UI and AppDomain exception handlers before the main form starts.
- [ ] Log monitor transitions and errors without blocking watcher threads.
- [ ] Add diagnostics UI with refresh, copy, and open-log-folder actions.
- [ ] Run release build and smoke tests.

### Task 3: Tray and global hotkeys

**Files:**
- Create: `TrayIconController.cs`
- Create: `GlobalHotKeyManager.cs`
- Modify: `MainForm.cs`
- Modify: `FloatingFolderForm.cs`
- Test: `SmokeTest.cs`

**Interfaces:**
- Produces: tray commands for main/floating/pause/diagnostics/exit and native hotkey infrastructure. Final hotkey actions are implemented by `2026-08-29-configurable-global-hotkeys.md`.

- [ ] Add command-state and hotkey ID smoke tests.
- [ ] Build the tray menu and keep its pause label synchronized.
- [ ] Register hotkeys on the main window handle and route commands through existing window methods.
- [ ] Dispose tray and hotkey resources during application shutdown.
- [ ] Run UI resource and window presentation tests.

### Task 4: Bounded activity history and pin unread state

**Files:**
- Create: `ActivityHistoryStore.cs`
- Modify: `AppSettings.cs`
- Modify: `FolderActivity.cs`
- Modify: `PinnedFolderStore.cs`
- Modify: `PinnedFolderForm.cs`
- Modify: `MainForm.cs`
- Test: `SmokeTest.cs`

**Interfaces:**
- Produces: `SavedActivityFolder`, `SavedActivityFile`, bounded hydration/persistence, and `PinnedFolder.HasUnreadActivity`.

- [ ] Add tests for 50-folder/three-file bounds and corrupt-entry skipping.
- [ ] Hydrate active folders at startup and persist batched activity changes.
- [ ] Mark matching pins active on existing monitor events and read when opened.
- [ ] Render a static activity dot and activity time in the pin tooltip.
- [ ] Run settings, pin, and UI resource smoke tests.

### Task 5: Stable upgrades, documentation, and release verification

**Files:**
- Modify: `DropSpot.csproj`
- Modify: `StartupRegistration.cs`
- Modify: `installer/DropSpot.iss`
- Modify: `packaging/build-release.ps1`
- Modify: `README.md`
- Test: `SmokeTest.cs`

**Interfaces:**
- Produces: version 1.0.6 metadata and stable installer location `%LocalAppData%\Programs\DropSpot`.

- [ ] Update path-resolution tests for stable installed and versioned legacy paths.
- [ ] Change installer to stable in-place upgrade while retaining versioned portable output.
- [ ] Synchronize README with taskbar minimize, floating mode, tray, diagnostics, hotkeys, recovery, and history.
- [ ] Run complete smoke tests and release build.
- [ ] Launch the test executable and sample process resources after a real file event.
