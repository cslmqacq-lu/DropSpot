# Pinned Folder Shortcuts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development or inline execution to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user pin any active folder as an independent, draggable DropSpot desktop shortcut without adding disk scans or another watcher.

**Architecture:** Persist a separate `PinnedFolders` collection in `AppSettings`; do not reuse Favorites because pinned shortcuts carry saved screen coordinates rather than activity ordering. `MainForm` owns one `PinnedFolderForm` per path and synchronizes visibility with the existing floating mode. Each tile invokes existing folder open, copy path, add favorite, and settings-save flows.

**Tech Stack:** .NET 8 WinForms, existing `ShellIconProvider`, `FloatingWindowPlacement`, `AppSettings` JSON persistence, built-in smoke test harness.

## Global Constraints

- Do not add directory enumeration, polling, content reads, hashes, or extra USN watchers.
- A pinned shortcut is a persisted navigation entry, not a Favorite and not a second monitor scope.
- Preserve existing floating window behavior and existing settings JSON compatibility.
- Clamp each restored or dragged shortcut to a visible screen working area.

---

### Task 1: Add persisted pinned-folder state

**Files:**
- Modify: `AppSettings.cs`
- Create: `PinnedFolderStore.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- Produces `SavedPinnedFolder { string Path; int? Left; int? Top; DateTime PinnedAt; }`.
- Produces `PinnedFolderStore.Load`, `TryPin`, `Remove`, `UpdatePosition`, and `ToSettings`.

- [ ] Add `List<SavedPinnedFolder> PinnedFolders { get; set; } = new();` to `AppSettings` so old settings load as an empty list.
- [ ] Implement `PinnedFolderStore` with case-insensitive normalized paths, no duplicates, and one `PinnedFolder` record per path.
- [ ] Add smoke assertions for load/save round-tripping, duplicate rejection, removal, and coordinate updates.
- [ ] Run `dotnet run -- --smoke-test` and require exit code `0`.

### Task 2: Add a draggable pinned-folder tile

**Files:**
- Create: `PinnedFolderForm.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- Consumes `PinnedFolder`, `Action<string> openFolder`, `Action<string> copyPath`, `Action<string> addFavorite`, `Action<string> unpin`, and `Action<string, Point> savePosition`.
- Produces `ShowAt(Point?)`, `UpdatePinnedFolder(PinnedFolder)`, and standard WinForms visibility/disposal behavior.

- [ ] Build a borderless, topmost 96x96 folder tile with the existing large Shell folder icon and an ellipsized folder name.
- [ ] Wire left-button drag with `FloatingWindowPlacement.Clamp`; save the final location after drag.
- [ ] Wire double-click to open the folder and a context menu with `打开文件夹`, `复制路径`, `加入收藏`, and `取消钉住`.
- [ ] Add a focused UI resource smoke assertion that creates, updates, shows, hides, and disposes a tile without leaking GUI resources.
- [ ] Run `dotnet run -- --smoke-test` and require exit code `0`.

### Task 3: Connect active-folder pinning and floating lifecycle

**Files:**
- Modify: `FolderCard.cs`
- Modify: `MainForm.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- `FolderCard` receives `Action<FolderActivity> pinFolder` and exposes a right-click `钉到浮窗` action.
- `MainForm` creates/removes/synchronizes `_pinnedFolderForms` from `PinnedFolderStore`.

- [ ] Add `钉到浮窗` to active-folder card context menus; disable it when already pinned.
- [ ] Load pinned settings before rendering; show tiles only while existing floating mode is active; hide them on main-window restoration and dispose them on app close.
- [ ] Persist pin, unpin, and drag operations through the existing atomic settings save path.
- [ ] Keep a pinned tile fixed when other active folders update; only the user drag changes its location.
- [ ] Run `dotnet build -c Release`, `dotnet run -- --smoke-test`, and launch the Release executable for a manual smoke check.
