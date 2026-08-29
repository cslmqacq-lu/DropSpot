# DropSpot Configurable Global Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add configurable global shortcuts for opening and copying the latest active folder while removing the floating-window shortcut.

**Architecture:** Persist normalized hotkey bindings in `AppSettings`, edit them in a dedicated settings tab, and transactionally re-register both bindings through `GlobalHotKeyManager`. The implementation uses native `RegisterHotKey` messages and adds no polling or background thread.

**Tech Stack:** .NET 8, Windows Forms, Win32 `RegisterHotKey`, JSON settings.

## Global Constraints

- Actions are open latest active folder and copy latest active folder path.
- Defaults are `Ctrl+Alt+F` and `Ctrl+Alt+D`.
- Modifiers are `Ctrl`, `Alt`, and `Shift`; keys are `A-Z` only.
- Each shortcut requires at least one modifier and the two combinations must differ.

---

### Task 1: Persisted hotkey model

**Files:**
- Modify: `AppSettings.cs`
- Test: `SmokeTest.cs`

- [ ] Add `SavedHotKey` defaults, clone, normalize, equality, display, and validation helpers.
- [ ] Add both bindings to `AppSettings` with backward-compatible defaults.
- [ ] Test JSON save, backup recovery, and legacy missing-property defaults.

### Task 2: Settings editor

**Files:**
- Modify: `SettingsForm.cs`
- Test: `SmokeTest.cs`

- [ ] Add a `快捷键` tab with two modifier-and-letter editor rows.
- [ ] Validate modifier presence, letter range, and duplicate combinations before returning `OK`.
- [ ] Expose normalized binding copies to `MainForm`.

### Task 3: Runtime registration and actions

**Files:**
- Modify: `GlobalHotKeyManager.cs`
- Modify: `MainForm.cs`
- Test: `SmokeTest.cs`

- [ ] Remove the floating-window hotkey and add copy-latest routing.
- [ ] Implement copying the latest folder path with status and empty-state feedback.
- [ ] Re-register bindings transactionally after settings changes and restore old bindings on Windows conflicts.
- [ ] Log registration failures and keep settings unchanged when registration fails.

### Task 4: Documentation and verification

**Files:**
- Modify: `README.md`
- Modify: `packaging/README-portable.txt`

- [ ] Document the configurable shortcuts and defaults.
- [ ] Run release build and complete smoke tests.
- [ ] Launch the 1.0.6 development executable and confirm both bindings register without errors.
