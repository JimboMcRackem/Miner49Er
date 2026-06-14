# Phase 5 — Input Rebinding UI Design

**Date:** 2026-06-14
**Status:** Approved (brainstorm) — ready for implementation plan
**Branch:** `phase5-rebinding-ui`

## Goal

Let the local player remap their controls (keyboard and gamepad) from a
**Controls** tab folded into a combined **Settings** panel. Bindings persist to
`user://settings.cfg` and are applied on startup. Every gameplay/UI action is
rebindable **except Exit** (ESC), which stays fixed as the guaranteed back-out.

## Decisions (locked during brainstorm)

- **Devices:** keyboard *and* gamepad. Each rebindable action keeps a keyboard
  slot and (where it has one) a gamepad slot, both shown. A single **Rebind**
  button per action captures the next input and updates whichever device was
  pressed, leaving the other slot intact.
- **Rebindable actions:** everything *except* Exit — the 4 moves, Pickaxe,
  Plant, Listen, UseItem, Restart, Mute, and Settings. Exit (ESC) is never
  rebindable, preventing a soft-lock.
- **Conflict handling:** *Reject and tell.* Binding an input already assigned to
  another action on the same device is refused; the old binding is kept and a
  brief "Already used by *X*" message shows. There is never an empty slot, so no
  "Unbound" UI state is needed.
- **Placement:** folded into a combined **Settings** panel (tabs: Audio |
  Controls), reachable everywhere the Audio panel is today — the Main Menu
  "Settings" button and the in-match `O` key (menu *and* mid-match).
- **Out of scope (YAGNI):** no "reset to defaults", no add/remove of slots. Only
  rebind-in-place; every existing slot always holds a value.

## Architecture

Two layers, matching the project's split:

- **`Miner49er.Core`** (pure C#, 4-space indent) owns the only logic worth
  unit-testing: the binding model, conflict detection, and the persistence
  round-trip format.
- **`game/`** (Godot adapter, TAB indent) owns everything Godot: the
  `SettingsPanel` overlay, the Controls tab UI, input capture, and the bridge
  between `BindingSet`, Godot's `InputMap`, and `SettingsStore`.

### Component 1 — `Miner49er.Core.Input.BindingSet` (new, pure)

The editable model of all rebindable bindings. Pure C#, no Godot types — codes
are plain `int`s (the caller maps Godot `Key`/`JoyButton` enum values to/from
`int`). `-1` means "this action has no slot for this device".

```
namespace Miner49er.Core.Input;

public enum BindDevice { Keyboard, Gamepad }

public sealed class BindingSet
{
    // Per action: keyboard code and gamepad code (-1 = no slot).
    // Construct from defaults, overlay saved values, query/edit.

    public BindingSet();                                  // empty
    public void Set(string action, BindDevice device, int code);   // unchecked set (used to seed defaults/saved)
    public int Get(string action, BindDevice device);    // -1 if no slot
    public IEnumerable<string> Actions { get; }

    // Reject-and-tell: returns false and sets conflictingAction if `code` is
    // already bound to a DIFFERENT action on the same device; otherwise applies
    // and returns true. Binding to the same action's existing code is a no-op success.
    public bool TryRebind(string action, BindDevice device, int code, out string? conflictingAction);

    // Flat (key -> long) map for ConfigFile. Keys like "move_up.kb" / "move_up.pad".
    public IReadOnlyDictionary<string, long> ToConfig();
    public void FromConfig(IReadOnlyDictionary<string, long> values);  // overlay; ignores unknown/absent
}
```

**Conflict rule:** Exit's ESC code is seeded into the set as an occupied
keyboard slot (under the `exit` action, which is present in the set but not
shown in the UI), so `TryRebind` naturally refuses to let any other action steal
ESC — without a special case.

### Component 2 — persistence: `SettingsStore.LoadInput/SaveInput` (extend existing)

`game/audio/SettingsStore.cs` gains an `[input]` section in the same
`user://settings.cfg`. `SaveAudio` already calls `cfg.Load(Path)` before writing
to preserve other sections, so the two sections coexist cleanly.

```
// Returns saved (key -> long) overrides; empty dict if none/unreadable.
public static Dictionary<string, long> LoadInput();
// Persists the BindingSet.ToConfig() map under [input].
public static void SaveInput(IReadOnlyDictionary<string, long> values);
```

### Component 3 — startup wiring: `InputBindings` (extend existing)

`InputBindings.EnsureDefaults()` keeps registering the code defaults into
`InputMap` exactly as today (so actions always exist even with no save file).
A new step builds the live `BindingSet` and applies it:

1. `EnsureDefaults()` registers all default events into `InputMap` (unchanged).
2. New `InputBindings.BuildBindingSet()` reads the current `InputMap` defaults
   into a fresh `BindingSet` (keyboard physical keycode + gamepad button index
   per action, including `exit`).
3. Overlay saved overrides: `set.FromConfig(SettingsStore.LoadInput())`.
4. New `InputBindings.Apply(set)` rewrites `InputMap` for each rebindable action
   from the set (erase the action's events, re-add the keyboard event and, if
   the action has a gamepad slot, the gamepad event). Exit is left untouched.

This runs once at the same call site as `EnsureDefaults()` (Main `_Ready` /
`NetworkManager._Ready`). `InputMap` remains the runtime source of truth;
`BindingSet` is the editable model the Controls tab mutates.

### Component 4 — `SettingsPanel` (rename of `AudioSettingsPanel`)

`game/ui/AudioSettingsPanel.cs` → `SettingsPanel.cs` (class `SettingsPanel`).
Same opaque, centered `CanvasLayer` (Layer 100), same
`Open()/Close()/Toggle()/IsOpen` surface so callers are unaffected. The body's
`VBoxContainer` moves inside a `TabContainer`:

- **Audio tab** — the existing Music/SFX sliders + "Music on" checkbox, verbatim.
- **Controls tab** — one row per rebindable action (Component 5).

Callers updated for the rename only: `MainMenu.cs` (`_audioPanel` field/type,
`new AudioSettingsPanel` → `new SettingsPanel`) and `Main.cs` (`_audioPanel`
field/type at `Main.cs:19,78`; the `O`-key `_audioPanel.Toggle()` at
`Main.cs:132-133` is unchanged). No behavioral change to those callers. Note: the
`O`-toggle uses `InputBindings.Settings`, which is itself rebindable — so
remapping Settings moves the toggle key too, as intended.

### Component 5 — Controls tab UI & capture

A `GridContainer` (or per-row `HBoxContainer`s) with one row per rebindable
action, in a stable display order. Each row:

- a **label** (friendly name, e.g. "Move Up", "Pickaxe"),
- a **Key** slot showing the keyboard binding (e.g. "W"),
- a **Pad** slot showing the gamepad binding (e.g. "X"), or "—" only for
  Settings, which has no gamepad slot,
- a single **Rebind** button.

**Capture flow:**

1. Pressing **Rebind** puts that row into capture mode: the button/label shows
   "Press a key or button…", and the panel sets `_capturingAction`.
2. While capturing, the panel handles input in `_Input` and calls
   `GetViewport().SetInputAsHandled()` for the captured event — so a mid-match
   capture cannot leak into gameplay.
3. Event routing:
   - `InputEventKey` (pressed, non-echo) with physical keycode `K`:
     `K == Escape` cancels capture, no change. Otherwise route to the **Keyboard**
     slot.
   - `InputEventJoypadButton` (pressed): route to the **Gamepad** slot. Ignored
     for Settings (keyboard-only), which keeps listening.
   - Other events ignored (keep listening).
4. Call `BindingSet.TryRebind(action, device, code, out conflict)`:
   - **success** → `InputBindings.Apply(set)` (or apply just that action),
     `SettingsStore.SaveInput(set.ToConfig())`, refresh the row's slot labels,
     exit capture.
   - **conflict** → show "Already used by *<friendly name of conflict>*" on the
     row for a moment, keep the old binding, exit capture.

Only one row captures at a time. Opening/closing the panel never enters capture.

## Data flow

```
startup:   EnsureDefaults() -> InputMap defaults
           BuildBindingSet() <- InputMap
           FromConfig(LoadInput())  (overlay saved)
           Apply(set) -> InputMap          [runtime truth]

rebind:    user presses Rebind -> capture next input
           TryRebind(set) --reject--> "Already used by X", no change
                          --accept--> Apply(set)->InputMap, SaveInput(ToConfig())
```

## Error handling

- **Unreadable/missing save file:** `LoadInput()` returns an empty map; defaults
  stand. (Mirrors `LoadAudio`'s fall-back-to-defaults contract.)
- **Saved value naming an unknown action or out-of-range code:** `FromConfig`
  ignores entries whose action isn't in the set; `Apply` only writes slots the
  action actually has. A stale/garbage code still applies as an `InputMap` event
  but cannot crash; worst case the player rebinds it again.
- **Conflict during overlay (two saved actions share a code):** overlay is a
  raw `Set`, not `TryRebind`, so the file is trusted as last-written; since every
  write went through `TryRebind`, a conflicting pair can't be produced by the UI.
- **ESC during capture:** always cancels cleanly; Exit binding never changes.

## Testing

**Unit tests (xUnit, `Miner49er.Core.Tests`), targeting `BindingSet`:**

- `Get` returns seeded codes; `-1` for absent device slots.
- `TryRebind` to a free code succeeds and updates `Get`.
- `TryRebind` to a code held by another action on the same device fails, names
  that action, and leaves both bindings unchanged.
- `TryRebind` to a code held by *the same* action is a no-op success.
- Same code on the *other* device is not a conflict (keyboard vs gamepad
  independent).
- ESC/Exit code is reported as the conflicting action when another action tries
  to take it.
- `ToConfig` → `FromConfig` round-trips an edited set exactly.
- `FromConfig` ignores unknown-action and absent entries, leaving defaults.

**Manual / Godot verification (not automated):**

- Rebind a key, reopen the panel — new binding shown and effective in a match.
- Rebind a gamepad button — keyboard slot for that action unchanged.
- Attempt a conflicting bind — rejected with the right "Already used by X".
- Mid-match `O` → Controls → capture a key — gameplay does not also react to it.
- ESC during capture cancels; ESC always exits the panel/quits as before.
- Restart the app — saved bindings persist.

## Files

- **Create:** `src/Miner49er.Core/Input/BindingSet.cs`
- **Create:** `src/Miner49er.Core.Tests/BindingSetTests.cs`
- **Modify:** `game/audio/SettingsStore.cs` (add `LoadInput/SaveInput`)
- **Modify:** `game/InputBindings.cs` (add `BuildBindingSet`, `Apply`; call at
  the `EnsureDefaults` site)
- **Rename + extend:** `game/ui/AudioSettingsPanel.cs` →
  `game/ui/SettingsPanel.cs` (TabContainer; new Controls tab + capture)
- **Modify:** `game/ui/MainMenu.cs` and `game/Main.cs` (type rename only)
