# DropSpot Configurable Global Hotkeys Design

## Goal

Replace the hard-coded floating-window shortcut with two user-configurable global shortcuts: open the latest active folder and copy the latest active folder path.

## Behavior

- Open latest folder defaults to `Ctrl+Alt+F`.
- Copy latest folder path defaults to `Ctrl+Alt+D`.
- The floating-window global shortcut is removed.
- Each shortcut has `Ctrl`, `Alt`, and `Shift` checkboxes plus an `A-Z` key selector.
- At least one modifier is required for each shortcut.
- The two shortcuts cannot use the same combination.
- Existing settings files receive the defaults automatically.
- Saving settings unregisters the previous shortcuts and attempts to register the new pair.
- If Windows rejects either shortcut because another application owns it, DropSpot restores the previous pair and reports the conflict without losing the rest of the settings.

## Data and Components

`SavedHotKey` is stored in `AppSettings` using booleans for the three modifiers and a single uppercase letter. It provides normalization, cloning, display text, and validation helpers.

`SettingsForm` adds a `快捷键` tab with two compact editor rows. Validation happens before the dialog returns `OK`.

`GlobalHotKeyManager` registers the two configured bindings and routes `WM_HOTKEY` to `OpenLatestFolder` and `CopyLatestFolderPath`. It retains no polling thread.

`MainForm` owns transactional re-registration. It restores the old bindings if registration fails and saves settings only after both new shortcuts register successfully.

## Testing

- Default and JSON round-trip tests for `Ctrl+Alt+F` and `Ctrl+Alt+D`.
- Validation tests for missing modifiers, invalid letters, and duplicate bindings.
- Message routing tests for the two hotkey IDs.
- Settings UI smoke test verifies both editor labels and defaults.
- Full release build and smoke test must pass.
