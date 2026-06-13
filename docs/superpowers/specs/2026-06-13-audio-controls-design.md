# Audio Controls — Design

**Date:** 2026-06-13
**Status:** Approved

## Goal

Let the player turn music off independently of the master mute, and adjust the
balance between music and SFX, from a shared settings panel reachable both from
the Main Menu and during a match. Settings persist across launches.

## Architecture & Data Flow

`AudioManager` (the existing autoload) becomes the single source of truth for the
live audio settings and applies them to the audio buses. A small `SettingsStore`
persists them to disk. A reusable `AudioSettingsPanel` overlay edits them live and
is opened from two places (Main Menu, in-match).

```
AudioSettingsPanel (UI)  ──edits──►  AudioManager (live values + bus dB)
        │                                   │
        └────────── persists ──────────►  SettingsStore (user://settings.cfg)
                                            │
                          AudioManager._Ready ◄── loads on startup
```

## 1. Model & `AudioManager` Refactor

`AudioManager` holds three live settings:

- `MusicVolume` ∈ [0, 1] — linear; the Music slider's 0–100%.
- `SfxVolume` ∈ [0, 1] — linear; the SFX slider's 0–100%.
- `MusicEnabled` (bool) — the Music on/off toggle, **independent** of master mute.

### Linear → dB

Slider fractions map to bus dB via `Mathf.LinearToDb` (100% → 0 dB, 50% → −6 dB).
A fraction at or below a small epsilon (≤ 0.0005) is treated as −80 dB ("silent")
rather than `LinearToDb`'s −∞, so tweens never receive a non-finite value.

```
MusicBaseDb = (MusicVolume <= 0.0005) ? -80f : Mathf.LinearToDb(MusicVolume)
SfxBaseDb   = (SfxVolume   <= 0.0005) ? -80f : Mathf.LinearToDb(SfxVolume)
```

### Listen-duck becomes relative

Today `SetListening` tweens the Music bus to a hardcoded `MusicDuckedDb` (−18) and
the SFX bus to `SfxLiftedDb` (+4). These become **offsets** from the user's
baseline, preserving the current feel:

- `MusicDuckOffsetDb = MusicDuckedDb − MusicDefaultDb = −18 − (−6) = −12`
- `SfxLiftOffsetDb   = SfxLiftedDb   − SfxDefaultDb   = 4 − 0     = +4`

Target dB each state:

- Music bus volume → `MusicBaseDb + (listening ? MusicDuckOffsetDb : 0)`
- SFX bus volume   → `SfxBaseDb   + (listening ? SfxLiftOffsetDb   : 0)`

The existing 0.2 s tween in `SetListening` is retained, now tweening to these
computed targets. A single `ApplyBuses()` helper computes and applies both bus
volumes for the current `listening` state; `SetListening` tweens toward the same
targets `ApplyBuses` would set.

### Music on/off

`MusicEnabled` drives `AudioServer.SetBusMute(MusicBusIndex, !MusicEnabled)`. This
is orthogonal to the Music bus *volume*, so toggling music never interferes with
the duck tween, and the slider value is preserved while music is off.

### Master mute

`ToggleMute` (the `M` key, master bus) is unchanged and independent of all the
above.

### Defaults (preserve today's mix exactly)

- `MusicVolume = 0.5` → −6 dB (the current `MusicDefaultDb`)
- `SfxVolume = 1.0` → 0 dB (the current `SfxDefaultDb`)
- `MusicEnabled = true`

### Public surface (for the panel)

```csharp
public float MusicVolume { get; }
public float SfxVolume { get; }
public bool MusicEnabled { get; }
public void SetMusicVolume(float v);   // clamps 0..1, applies buses, saves
public void SetSfxVolume(float v);      // clamps 0..1, applies buses, saves
public void SetMusicEnabled(bool on);   // mutes/unmutes music bus, saves
```

Each setter clamps, applies to the buses immediately (respecting the current
listening state), and calls `SettingsStore.Save(...)`.

## 2. Persistence — `SettingsStore`

New static class `game/audio/SettingsStore.cs` wrapping Godot `ConfigFile` at
`user://settings.cfg`, section `audio`:

```csharp
public static class SettingsStore
{
    // Returns saved values or the supplied defaults if the file/keys are absent.
    public static (float music, float sfx, bool musicEnabled) LoadAudio(
        float defMusic, float defSfx, bool defMusicEnabled);

    public static void SaveAudio(float music, float sfx, bool musicEnabled);
}
```

`AudioManager._Ready` calls `LoadAudio` with the defaults above, stores the
results, then applies them to the buses. Load failures (missing/corrupt file) fall
back to the defaults — no crash.

## 3. Shared Panel & Access Points

New `game/ui/AudioSettingsPanel.cs` — a `CanvasLayer` overlay built in code
(matching the project's code-built UI), containing:

- A **Music** `HSlider` (0–1), labeled, reflecting/!setting `AudioManager.MusicVolume`.
- An **SFX** `HSlider` (0–1) for `AudioManager.SfxVolume`.
- A **Music on/off** `CheckBox` for `AudioManager.MusicEnabled`.
- A **Close** button.

The panel initializes its controls from `AudioManager`'s current values, and wires
each control's `ValueChanged` / `Toggled` / `Pressed` to the matching
`AudioManager` setter so edits are live and persisted. It exposes `Open()`,
`Close()`, and `IsOpen`.

### Access points

- **Main Menu** (`game/ui/MainMenu.cs`): a new "Settings" button instantiates/opens
  the panel over the menu.
- **In-match** (`game/Main.cs`): a new input action toggles the panel.
  - New action `InputBindings.Settings = "settings"`, default key **`O`** (added in
    `InputBindings.EnsureDefaults`; registered in code via `InputMap`, so
    `project.godot` is not touched).
  - `Main` instantiates the panel once and toggles it on the action.
  - **While the panel is open in-match, `Main` disables the local `InputSender`**
    (reusing the existing `_input.Enabled` hook that already gates input on death),
    so dragging sliders does not also move the miner. Input is restored on close.
    The match itself keeps running (real-time, host-authoritative — no pause).

## 4. Error Handling

- Slider values are clamped to [0, 1] in the `AudioManager` setters.
- `SettingsStore.LoadAudio` returns defaults on any read error; `SaveAudio`
  failures are non-fatal (best-effort write).
- Sub-epsilon volumes use −80 dB instead of −∞ to keep bus dB finite for tweens.

## 5. Testing

The feature is entirely engine-coupled (Godot `AudioServer`, `ConfigFile`, UI
nodes), so there is nothing for the pure-C# xUnit Core suite. Verification is
**play-test**:

- Music on/off silences/restores music **independently** of master mute (`M`).
- Music and SFX sliders change their levels live while dragging.
- The listen-duck still ducks/lifts **relative to** custom slider levels (hold
  Listen with music at, say, 80% — music drops ~12 dB from there, SFX rises).
- Settings **survive a relaunch** (set values, quit, relaunch → panel reflects them
  and the mix matches).
- Panel opens from the Main Menu and in-match (`O`); movement is suspended while the
  in-match panel is open and restored on close.
- Build stays 0-warning / 0-error.

## Out of Scope

- Input rebinding / a general settings screen (a future Phase-5 concern;
  `SettingsStore` is the reusable seed).
- Per-category SFX sub-mixing, music track selection, or a UI-bus slider.
- Networked/shared audio settings — these are per-client local preferences.
