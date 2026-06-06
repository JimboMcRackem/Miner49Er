# Miner49er — Phase 3: Listen Mechanic & Audio Design

**Date:** 2026-06-06
**Status:** Design approved; ready for implementation planning.
**Builds on:** `2026-06-06-miner49er-game-design.md` (§3.3 Listen, §6 Audio, §9 phased plan)
and the merged Phase 1 (core loop) + Phase 2 (multiplayer).

---

## 1. Goal

Add the **Listen** verb and the game's **audio layer**: a held-to-listen mode that
halts the player, shows an 8-point compass toward the nearest living rival, and
shifts the soundscape; plus looping music and 2D-positioned sound effects that
make the mine feel tense and alive.

**In scope:** hold-to-listen with an 8-point compass; an `AudioManager` with
Music/SFX/UI buses; positional SFX (footsteps, pickaxe, planting, explosions,
death) derived client-side from already-synced state; ambient water-drip
emitters; a drop-in music loop; procedural placeholder SFX so the game is
audible without bundled assets; an audio "duck music / lift SFX" shift while
listening; minimal default bus volumes + a master-mute key.

**Out of scope (deferred):** persisted settings UI with volume sliders (Phase 5);
a visible listen *animation* broadcast to other players (Phase 5 polish — with
placeholder rect art there is nothing to show, and "exposed" is already emergent
from standing still); real/final audio assets (the user supplies these); water
tiles themselves (Phase 4 — drip emitters here are decoupled, atmospheric only).

## 2. Key architectural fact: almost no new netcode

Phase 2 ships **naive full-state sync** — every client already receives each
miner's position, facing, `Activity`, and `Alive` each tick, plus blast
tile-changes. Therefore:

- The **compass** target (nearest living *other* miner) is computed locally from
  the snapshot the client already holds.
- **Listening** is purely client-side: while listening, the client suppresses its
  own movement input, shows the compass, and shifts audio buses. The host is not
  involved and needs no new messages.
- **SFX** are *derived* on each client from state it already has (see §5). No new
  RPCs, no `MatchHost`/`NetworkManager` changes.

This keeps Phase 3 a Core compass helper plus a client-side audio/UI layer.

## 3. Listen mechanic

- **Activation:** hold-to-listen. While the `listen` action (bound to `L`/gamepad
  B in Phase 1) is held, the local player:
  - **stands still** — movement input is suppressed at the client (`InputSender`
    sends "no direction"); the host already only moves you on your input, so you
    simply stop issuing movement;
  - shows the **compass** (§4);
  - triggers the **audio shift** (§6.3).
  Releasing the key restores normal movement, hides the compass, and restores
  audio. Mining/planting actions are also suppressed while listening (you are
  listening, not acting).
- **Exposure** is emergent: you are a stationary target for as long as you listen.
  No special vulnerability flag is needed.

## 4. Compass

- An **8-point** indicator — `CompassDirection { N, NE, E, SE, S, SW, W, NW }` —
  pointing from the local miner toward the **nearest living miner other than
  self**, by straight-line (Euclidean) distance. **No range cap** (spec §10
  default: always points to the nearest living player).
- If there is **no other living miner** (you are the last alive, or others have
  not spawned), the compass shows a **neutral state** (a centered dot, no arrow).
- **Core helper (unit-tested):** `ListenCompass.NearestDirection(GridPos self,
  IEnumerable<GridPos> others) -> CompassDirection?` returns the 8-point bucket of
  the vector to the nearest position, or `null` when `others` is empty. Direction
  bucketing snaps the angle to the nearest of 8 sectors (45° each). This pure
  function lives in `Miner49er.Core`; the Godot `Compass` UI only renders the
  returned direction.
- **Rendering:** a HUD element (`CanvasLayer`) showing 8 arrow positions with the
  active one highlighted (or a single pointer rotated to the bucket). Visible only
  while listening.

## 5. Positional SFX, derived client-side

Each client runs a per-match `MatchAudio` node that reads `MatchClient` state each
frame and emits sounds via `AudioManager.PlaySfx2D(sound, worldPos)`:

| Sound | Trigger (derived from synced state) |
|---|---|
| **Footstep** | a miner's snapshot tile position changed since last frame → one-shot at the new world position |
| **Pickaxe** (loop) | a miner's `Activity == Mining` → start a looping strike SFX at its position; stop when activity ends |
| **Planting** | a miner's `Activity` transitions to `Planting` → one-shot |
| **Explosion** | blast tile-changes arrived this tick (`TileChange.FromBlast`) → one-shot at the centroid of the blasted cells |
| **Death** | a miner transitioned `Alive: true → false` → one-shot stinger at its position |
| **Ambient drip** | a handful of looping emitters placed at random floor tiles at match start (atmosphere; not tied to water yet) |

Positional playback uses Godot `AudioStreamPlayer2D` at world coordinates so the
engine handles volume/pan by distance from the listener (the local miner / camera).
`MatchAudio` tracks per-miner previous position/activity/alive to detect the
transitions above. It is the single consumer of `MatchClient` for audio, so the
mapping lives in one focused file.

## 6. Audio system

### 6.1 Buses
`AudioManager` (autoload) ensures three buses exist (creating them via
`AudioServer` if absent): **Music**, **SFX**, **UI** (UI reserved for menu/click
sounds; minimal use in Phase 3). Each positional/one-shot player routes to SFX;
music routes to Music. Sensible default volumes are set on startup.

### 6.2 Music
A single looping track plays for the duration of a match (and optionally the
menu/lobby). The stream is loaded from a drop-in path (e.g.
`res://assets/audio/music_loop.ogg`); if the file is absent, music silently
no-ops (the game still runs). The user supplies the jaunty-but-spooky loop.

### 6.3 Listen audio shift
While listening, `AudioManager` **ducks the Music bus** (lower its dB) and
**lifts the SFX bus** (raise its dB), and `MatchAudio` **increases the max
audible distance** of positional emitters, so distant sounds become hearable —
making the world "open up" to the ear and reinforcing the compass. On release,
buses and ranges return to defaults (a short tween to avoid a click).

### 6.4 Assets & placeholders
- `SfxLibrary` maps logical sound names → `AudioStream`. It loads real files from
  `res://assets/audio/` when present and is **tolerant of missing files**
  (returns null → playback no-ops), so the project always builds and boots.
- For audible-out-of-the-box feedback, `SfxLibrary` **generates simple procedural
  placeholder SFX in code** (short noise/click bursts via `AudioStreamWAV` with
  generated PCM) for footstep, pickaxe, plant, explosion, and death, used
  whenever a corresponding asset file is absent.
- An **asset manifest** (`assets/audio/README.md`) lists every expected file, its
  logical name, and suggested CC0 sources, so the user can drop in real audio
  incrementally.

## 7. Settings (minimal)
Phase 3 sets default bus volumes on startup and binds a **master-mute** toggle
(reuses the input map). No persisted settings file and no in-game volume UI yet —
that is Phase 5's full settings/rebinding work. `AudioManager` exposes simple
volume setters so Phase 5 can wire a UI to them later without rework.

## 8. Components & boundaries

**Core (`src/Miner49er.Core`), unit-tested:**
- `Fog`-adjacent pure helper `ListenCompass` + `CompassDirection` enum —
  nearest-living-other → 8-point direction (and the angle-bucketing).

**Godot (`game/`):**
- `game/audio/AudioManager.cs` — autoload: bus setup, music play/loop, `PlaySfx2D`,
  duck/lift API, volume setters, master mute.
- `game/audio/SfxLibrary.cs` — logical-name → stream, file-or-procedural,
  missing-tolerant.
- `game/audio/MatchAudio.cs` — per-match node: derives SFX from `MatchClient`
  state, manages ambient drip emitters, drives match music.
- `game/ui/Compass.cs` — the 8-point HUD indicator.
- Hold-to-listen wiring in the existing input/match layer (`InputSender` /
  `Main`): suppress movement+actions while `listen` is held, toggle the compass,
  and call the audio shift.

## 9. Testing & verification
- **Core unit tests** for `ListenCompass`: cardinal and diagonal buckets, choosing
  the nearest among several, ignoring dead miners and self, and the empty/null
  case. Runs headless.
- **Build + headless boot** clean as always.
- **Audio and compass *feel* are the user's to verify by playing** — this dev
  environment is headless and cannot produce or capture sound. The agent confirms
  it compiles, boots, and that the Core compass math is correct; the player
  confirms it sounds and reads right.

## 10. Scope notes
- No networking changes; Phase 3 reads only existing synced state.
- Drip emitters are atmospheric placeholders, decoupled from the Phase 4 water
  system.
- The §3.5 movement-speed/status-effect system remains independent future work.
- Listen-animation broadcast, persisted settings UI, and final audio are Phase 5 /
  user-supplied.
