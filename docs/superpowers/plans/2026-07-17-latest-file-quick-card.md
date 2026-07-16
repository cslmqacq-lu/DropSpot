# DropSpot Latest File Quick Card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a one-file quick card above the minimized active-folder tile so the latest usable saved file can be opened or dragged directly, while fixing the favorite info popup that can remain visible after hover ends.

**Architecture:** Select the latest usable file from the existing in-memory `FolderActivity.Files` list on a background task, then pass that immutable `ChangeRecord` into a dedicated top-level `LatestFileQuickForm`. `FloatingFolderForm` owns click timing, anchoring, visibility, hover dismissal, and delegates file operations back to `MainForm`.

**Tech Stack:** .NET 8, Windows Forms, USN Journal data already captured by DropSpot, Windows Shell file icons, standard WinForms `DataFormats.FileDrop` drag and drop.

## Global Constraints

- Do not add a directory scan, directory enumeration, file-content read, hash, thumbnail generator, or additional file-system watcher.
- Show exactly one latest usable file.
- Ignore names beginning with `~$` and extensions `.tmp`, `.temp`, `.part`, `.crdownload`, `.download`.
- File existence checks must run off the UI thread.
- Single click waits `SystemInformation.DoubleClickTime`; double click opens the folder and cancels the single-click action.
- Dragging exports the real path with `DataFormats.FileDrop` and `DragDropEffects.Copy`.
- Keep main-window card rendering stopped while minimized.
- Preserve all unrelated dirty-worktree changes; stage or commit only reviewed feature files/hunks.

---

## File Map

- Create `LatestFileSelector.cs`: pure candidate filtering plus latest-existing selection with an injected existence predicate.
- Create `LatestFileQuickForm.cs`: one-file popup, layered background, open/context-menu/drag behavior, and anchor positioning.
- Modify `FloatingFolderForm.cs`: active click timer, latest-file popup lifecycle, position sync, and hover-popup watchdog.
- Modify `MainForm.cs`: asynchronous selection generation, latest-file state, async file opening, and new floating-form callbacks.
- Modify `SmokeTest.cs`: selection, popup update, and no-residual-window tests.
- Modify `FloatingFrameAssets.cs`: expose the existing favorite frame for the one-file card; no new bitmap asset.
- Modify `README.md`: describe one-file open/drag workflow.
- Modify `DropSpot.csproj` and `installer/DropSpot.iss`: release version `1.0.4`.

### Task 1: Latest Usable File Selector

**Files:**
- Create: `LatestFileSelector.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<ChangeRecord>` and `Func<string, bool> fileExists`.
- Produces: `LatestFileSelector.SelectLatestExisting(...) -> ChangeRecord?` and `LatestFileSelector.IsTemporaryFile(string) -> bool`.

- [ ] **Step 1: Add failing smoke coverage**

Add `RunLatestFileSelectorTests()` and call it after buffer tests:

```csharp
private static int RunLatestFileSelectorTests()
{
    var now = DateTime.UtcNow;
    var records = new[]
    {
        CreateRecord(@"C:\work\render.tmp", now.AddSeconds(3)),
        CreateRecord(@"C:\work\~$brief.docx", now.AddSeconds(2)),
        CreateRecord(@"C:\work\final.png", now.AddSeconds(1)),
        CreateRecord(@"C:\work\older.jpg", now)
    };
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\work\final.png",
        @"C:\work\older.jpg"
    };
    var selected = LatestFileSelector.SelectLatestExisting(records, existing.Contains);
    return selected?.FilePath == @"C:\work\final.png" ? 0 : 71;
}
```

- [ ] **Step 2: Run the smoke test and confirm failure**

Run: `dotnet run --project DropSpot.csproj -c Release -- --smoke-test`

Expected: build fails because `LatestFileSelector` does not exist.

- [ ] **Step 3: Implement the selector**

Create:

```csharp
namespace DropSpot;

public static class LatestFileSelector
{
    private static readonly HashSet<string> TemporaryExtensions = new(
        new[] { ".tmp", ".temp", ".part", ".crdownload", ".download" },
        StringComparer.OrdinalIgnoreCase);

    public static ChangeRecord? SelectLatestExisting(
        IReadOnlyList<ChangeRecord> records,
        Func<string, bool> fileExists)
    {
        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.FilePath))
            .Where(record => !IsTemporaryFile(record.FilePath))
            .OrderByDescending(record => record.Time)
            .FirstOrDefault(record => fileExists(record.FilePath));
    }

    public static bool IsTemporaryFile(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
            || TemporaryExtensions.Contains(Path.GetExtension(name))
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".temp", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run smoke tests**

Run: `dotnet run --project DropSpot.csproj -c Release -- --smoke-test`

Expected: selector tests return `0`; all existing smoke sections pass.

- [ ] **Step 5: Review commit scope**

Run: `git diff --check -- LatestFileSelector.cs SmokeTest.cs`.

Do not commit unrelated pre-existing `SmokeTest.cs` changes without reviewing the full staged diff.

### Task 2: One-File Quick Popup

**Files:**
- Create: `LatestFileQuickForm.cs`
- Modify: `FloatingFrameAssets.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- Consumes: `Action<ChangeRecord> openFile`, `Action<string> openFolder`, `Action<string> copyPath`.
- Produces: `UpdateFile(ChangeRecord?)`, `ShowAbove(Rectangle)`, `HideCard()`, `DisplayedFilePath`, and `IsDragging`.

- [ ] **Step 1: Add failing UI-resource coverage**

Extend `RunUiResourceTests()` to instantiate the form, update a `.png` record, and assert:

```csharp
using var latestFileForm = new LatestFileQuickForm(_ => { }, _ => { }, _ => { });
latestFileForm.UpdateFile(CreateRecord(@"C:\work\final.png", DateTime.Now));
if (latestFileForm.DisplayedFilePath != @"C:\work\final.png")
{
    return 86;
}
```

- [ ] **Step 2: Implement the form shell and update API**

Use `FormBorderStyle.None`, `ShowInTaskbar = false`, `TopMost = true`, `TransparentWindowStyle.ApplyToForeground(this)`, and a click-through `LayeredImageBackdropForm` using `FloatingFrameAssets.FavoriteFrame`.

The form owns one `PictureBox`, one ellipsized `Label`, a `ToolTip`, and a `ContextMenuStrip` with these exact actions:

```csharp
_menu.Items.Add("打开文件", null, (_, _) => OpenCurrent());
_menu.Items.Add("打开所在文件夹", null, (_, _) => OpenCurrentFolder());
_menu.Items.Add("复制文件路径", null, (_, _) => CopyCurrentPath());
```

- [ ] **Step 3: Implement click-versus-drag state**

Capture `Cursor.Position` on left `MouseDown`. On `MouseMove`, once either axis exceeds half of `SystemInformation.DragSize`, snapshot the current path and call:

```csharp
var data = new DataObject(DataFormats.FileDrop, new[] { dragPath });
DoDragDrop(data, DragDropEffects.Copy);
```

On `MouseUp`, call the open callback only when no drag started. Do not open a file after a drag operation.

- [ ] **Step 4: Implement anchored positioning and cleanup**

`ShowAbove(Rectangle anchor)` places the card centered over `anchor` with a 6px gap. `HideCard()` hides both foreground and layered backdrop. `Dispose` releases the backdrop, menu, tooltip, and fonts.

- [ ] **Step 5: Build and run UI smoke coverage**

Run: `dotnet build DropSpot.csproj -c Release`

Run: `dotnet run --project DropSpot.csproj -c Release -- --ui-smoke-test`

Expected: exit code `0`; update API exposes the expected file path and all embedded images load.

### Task 3: Active Tile State Machine and Dynamic Updates

**Files:**
- Modify: `FloatingFolderForm.cs`
- Modify: `MainForm.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- `FloatingFolderForm` constructor gains `Action<ChangeRecord> openFile`.
- `FloatingFolderForm.UpdateLatest` becomes `UpdateLatest(FolderActivity? folder, ChangeRecord? latestFile, bool monitoring)`.
- `MainForm` owns `_latestFile`, `_latestFileSelectionGeneration`, and `RefreshLatestFileAsync(FolderActivity?)`.

- [ ] **Step 1: Add smoke assertions for the new update contract**

Update all smoke-test calls to pass a latest file. Assert the floating form reports one file when present and zero when `null`.

- [ ] **Step 2: Add active click timer and chevron**

Create `_activeClickTimer` with:

```csharp
_activeClickTimer.Interval = Math.Max(200, SystemInformation.DoubleClickTime);
_activeClickTimer.Tick += (_, _) =>
{
    _activeClickTimer.Stop();
    ToggleLatestFileCard();
};
```

Wire left-segment `MouseClick` to start the timer. In `HandleActiveDoubleClick`, stop the timer, hide the card, then call `_openLatestFolder()` once.

- [ ] **Step 3: Integrate popup lifecycle**

Instantiate one `LatestFileQuickForm`. Update it whenever `UpdateLatest` receives a new record. When visible, update in place and reposition without hiding. Hide it when the main floating form hides, restores the main window, opens the folder, or disposes.

- [ ] **Step 4: Select the file off the UI thread**

In `MainForm`, snapshot `folder.Files.ToArray()` and increment a generation counter. Run:

```csharp
var selected = await Task.Run(() =>
    LatestFileSelector.SelectLatestExisting(snapshot, File.Exists));
```

Apply the result only when the generation and folder path still match. Pass `_latestFile` into every `UpdateLatest` call. Do not render main-window cards while `_floatingModeActive` is true.

- [ ] **Step 5: Make file opening non-blocking**

Convert `OpenFile(ChangeRecord)` into event-style `async void`. Validate and launch on `Task.Run`, then report errors on the captured UI context. If the file disappeared, refresh the current latest-file selection.

- [ ] **Step 6: Run full smoke coverage**

Run: `dotnet run --project DropSpot.csproj -c Release -- --smoke-test`

Expected: all monitor, buffer, selector, placement, favorites, UI-resource, and settings sections pass.

### Task 4: Reliable Favorite Info Dismissal

**Files:**
- Modify: `FloatingFolderForm.cs`
- Modify: `SmokeTest.cs`

**Interfaces:**
- Produces internal `FavoriteInfoVisible` for UI smoke inspection.
- Uses one `_infoDismissTimer` and one `_infoAnchorBounds`.

- [ ] **Step 1: Add watchdog state**

Create a `Timer` with a 120ms interval. `ShowFavoriteInfo` stores the supplied anchor rectangle, shows the popup, and starts the timer.

- [ ] **Step 2: Hide when the cursor leaves the source item**

On every tick:

```csharp
if (!_infoAnchorBounds.Contains(Cursor.Position))
{
    HideFavoriteInfo();
}
```

`HideFavoriteInfo` stops the timer before hiding the popup. The popup itself is not part of the retained hover region.

- [ ] **Step 3: Stop all hover timers during lifecycle transitions**

Stop the timer from `CollapseFavoriteMenu`, `OnVisibleChanged(false)`, and `Dispose`.

- [ ] **Step 4: Run UI smoke tests**

Run: `dotnet run --project DropSpot.csproj -c Release -- --ui-smoke-test`

Expected: exit code `0`; no visible top-level popup remains after the floating form hides.

### Task 5: Documentation and Release 1.0.4

**Files:**
- Modify: `README.md`
- Modify: `DropSpot.csproj`
- Modify: `installer/DropSpot.iss`

**Interfaces:**
- Produces version `1.0.4` binaries and installer.

- [ ] **Step 1: Update user documentation**

Add a concise section explaining:

```markdown
- 单击最小化浮窗左侧文件夹，可展开最近一个正常文件。
- 单击文件用默认程序打开；从文件卡拖动可发送或上传真实文件。
- 双击左侧文件夹仍直接打开资源管理器。
```

- [ ] **Step 2: Set version 1.0.4**

Set `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` to `1.0.4` / `1.0.4.0`. Set the installer fallback `MyAppVersion` to `1.0.4`.

- [ ] **Step 3: Run release verification**

Run:

```powershell
dotnet build DropSpot.csproj -c Release
dotnet run --project DropSpot.csproj -c Release -- --smoke-test
powershell -ExecutionPolicy Bypass -File .\packaging\build-release.ps1 -SkipTests
```

Expected: zero warnings/errors; installer at `dist\DropSpot_Setup_v1.0.4_win-x64.exe`.

- [ ] **Step 4: Install and verify**

Run installer with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`, then verify:

```powershell
(Get-Item "$env:LOCALAPPDATA\Programs\DropSpot\DropSpot.exe").VersionInfo.ProductVersion
```

Expected: `1.0.4`.

- [ ] **Step 5: Launch and measure**

Launch the installed executable, exercise open/expand/drag manually, and sample CPU, private memory, working set, threads, and handles for at least 10 seconds. Confirm no monotonic growth and idle CPU remains near zero.

### Task 6: Final Diff and Process Hygiene

**Files:**
- Review all files listed above.

- [ ] **Step 1: Check formatting and accidental edits**

Run: `git diff --check`.

Expected: no whitespace errors in feature changes.

- [ ] **Step 2: Confirm no test process remains**

List `DropSpot.exe` command lines and stop only processes containing `--smoke-test` or `--ui-smoke-test` that were launched during verification.

- [ ] **Step 3: Report installed state**

Report the installed path, product version, installer path, functional test result, and measured resource figures. Do not claim drag compatibility with applications that were not actually tested.
