# Separate Minimize and Floating Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make native minimize use the Windows taskbar while a dedicated toolbar button opens DropSpot's floating window.

**Architecture:** `MainForm` owns two independent presentation transitions: native `FormWindowState.Minimized` and explicit `EnterFloatingMode()`. Monitoring and model updates continue in both states, while main-card rendering is suspended until the main window is visible again.

**Tech Stack:** .NET 8, Windows Forms, C# 12, existing DropSpot smoke-test harness

## Global Constraints

- Preserve `--startup` as direct entry into floating mode.
- Do not add disk scanning, polling, timers, watchers, or dependencies.
- Preserve existing floating-window, favorite, pinned-folder, and restore behavior.
- Keep the new command icon-only with tooltip text “打开悬浮窗”.

---

### Task 1: Split native minimize from floating mode

**Files:**
- Modify: `MainForm.cs`
- Test: `SmokeTest.cs`

**Interfaces:**
- Consumes: existing `FloatingFolderForm.ShowAt(Point?)`, `RestoreMainWindow()`, and `RenderCurrentMainView()` behavior.
- Produces: `EnterFloatingMode()`, a toolbar button named `openFloatingButton`, and a main-render suspension predicate.

- [ ] **Step 1: Add a failing source-level smoke assertion**

Add a UI contract check that verifies the main form declares the dedicated floating command name and exposes separate native-minimize and floating-mode methods.

- [ ] **Step 2: Run the UI smoke test and verify failure**

Run: `dotnet run --project .\DropSpot.csproj -- --ui-smoke-test`

Expected: non-zero exit because the dedicated command does not exist yet.

- [ ] **Step 3: Implement the separate transitions**

In `MainForm.cs`:

- Add a Segoe MDL2 floating-window icon button to the black header.
- Remove `ShowFloatingMode()` from the `SizeChanged` minimized branch.
- Rename the explicit transition to `EnterFloatingMode()` and allow it to run from the toolbar or startup path.
- Preserve `--startup` by invoking `EnterFloatingMode()` after the form is shown.
- Suspend `RenderFolders()`, `RenderFavorites()`, and time refreshes whenever the main form is minimized, hidden, or in floating mode.
- Refresh the current view once after a native taskbar restore.

- [ ] **Step 4: Run build and complete smoke tests**

Run: `dotnet build .\DropSpot.csproj -c Release --no-restore`

Expected: 0 warnings and 0 errors.

Run: `dotnet run --project .\DropSpot.csproj -- --smoke-test`

Expected: stages `monitor`, `buffer`, `latest-file-selector`, `single-instance`, `placement`, `favorites`, `pinned-folders`, `ui-resources`, and `settings` all complete with exit code 0.

- [ ] **Step 5: Launch the Release test executable**

Run: `Start-Process .\bin\Release\net8.0-windows\DropSpot.exe`

Expected: title-bar minimize leaves DropSpot on the taskbar; the new header icon opens the floating window.

