# Cause-of-Death Messaging — Design

**Date:** 2026-06-13
**Status:** Approved

## Goal

When a miner dies, the victim sees a center-screen banner naming the cause of
death, and every other player sees a short-lived toast in a stacking kill-feed.
This closes the play-test gap where a player could not tell that (or how) a rival
had died — e.g. seeing "a rival drowned" the moment the flood takes them.

## Architecture & Data Flow

The cause of death is computed **authoritatively in Core**, replicated as a single
field on `MinerSnapshot`, and surfaced by a **new client-only UI node** that watches
each miner's `Alive` flag flip `true → false` (the same per-miner transition trick
`MatchAudio` already uses for splash-vs-death SFX). There is **no new network
channel** — the cause rides the existing per-tick world snapshot.

```
Core kill site → Miner.DeathCause → MinerSnapshot.Cause → SnapshotCodec → client
                                                                            │
                                       DeathFeed node sees Alive: true→false
                                       ├─ local miner  → center banner
                                       └─ other miner  → toast in kill-feed
```

Why a snapshot field rather than client-side tile inference: the feature exists to
report *why* someone died, so correctness matters. Tile inference (DeepWater =
drown, else = blast) cannot tell a disconnect apart from an explosion and would
mislabel a player who quit on dry land as "blown up". The authoritative field is
correct in every case and is future-proof for new causes.

## Core Changes

### New enum — `Sim/DeathCause.cs`

```csharp
namespace Miner49er.Core;

public enum DeathCause { None, Drowned, Exploded, Left }
```

### `Sim/Miner.cs`

Add a field, default `None`:

```csharp
public DeathCause DeathCause { get; internal set; } = DeathCause.None;
```

### Set the cause at the three kill sites in `Sim/Simulation.cs`

The cause field is **additive** — the existing `MinerDrowned` / `MinerKilled`
`SimEvent`s are left untouched so round-resolution and existing tests do not change.
At each site, set `DeathCause` alongside the existing `Alive = false`:

- **`Simulation.cs:172`** (drown on move into deep water) → `DeathCause.Drowned`
- **`Simulation.cs:424`** (flood rises under a standing miner) → `DeathCause.Drowned`
- **`Simulation.cs:458`** (caught in a blast radius) → `DeathCause.Exploded`
- **`Simulation.cs:49`** `KillMiner(int id)` (the host's disconnect path, called
  from `MatchHost` when a peer drops) → `DeathCause.Left`

### `Net/Snapshots.cs`

Append the cause to `MinerSnapshot` (default keeps existing call sites compiling):

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held,
    DeathCause Cause = DeathCause.None);
```

### `Net/SnapshotFactory.cs`

Populate `Cause` from `miner.DeathCause` when building each `MinerSnapshot`.

### `Net/SnapshotCodec.cs`

Write/read one byte for `Cause` (cast `DeathCause` ↔ `byte`), positioned
consistently with the existing per-miner field order.

## Client UI — new `DeathFeed` node (under `Main`)

A `CanvasLayer`/`Control`-based node added as a child of `Main`, given the
`MatchClient`. It keeps a `prevAlive` map per miner Id. Each frame, for any miner
whose `Alive` went `true → false` this frame, it reads `m.Cause` and:

- **Local miner** (`m.Id == _client.LocalMinerId`) → show a **center banner**:
  large text, held ~3s, then fades. The HUD's existing "Dead — spectating" status
  continues to carry the spectate state afterward.
- **Any other miner** → push a **toast** into a top-right feed. Toasts stack
  (newest on top, cap ~4 visible), each fades after ~4s; older toasts drop off.

### Wording

| Cause     | Victim banner          | Others' toast       |
|-----------|------------------------|---------------------|
| Drowned   | **YOU HAVE DROWNED**   | *{Name} drowned*    |
| Exploded  | **YOU WERE BLOWN UP**  | *{Name} was blown up* |
| Left      | *(none — they're gone)* | *{Name} left*       |

`None` is never surfaced (it means the miner is alive). A `Left` cause has no
victim banner because that peer has already disconnected.

### Name lookup

`minerId → name` via `NetworkManager.Instance`:
`Players[PeerOrder[minerId - 1]].Name`, with a safe fallback (e.g. `"Miner {id}"`)
if the lookup is out of range. (Player-color tinting of toasts via
`PlayerInfo.ColorIndex` is deferred as later polish — out of scope here.)

## MatchAudio Cleanup

Switch `MatchAudio`'s death SFX selection from its current tile-inspection
heuristic to the authoritative field. Today (`MatchAudio.cs:80-82`) it inspects the
death tile (`DeepWater → Splash` else `Death`). Replace that with
`m.Cause == DeathCause.Drowned ? Splash : Death`, unifying cause logic on the
snapshot field and removing the heuristic. Behavior is unchanged for the common
cases; a dry-land disconnect now correctly plays the non-splash death cue.

## Testing

- **Core (xUnit):**
  - Drown-on-move sets `DeathCause.Drowned`.
  - Flood-under-standing-miner sets `DeathCause.Drowned`.
  - Blast kill sets `DeathCause.Exploded`.
  - `KillMiner` sets `DeathCause.Left`.
  - A living miner's `DeathCause` is `None`.
- **Codec (xUnit):** a `WorldSnapshot` containing miners with each `Cause`
  round-trips through `SnapshotCodec` with `Cause` preserved.
- **UI (play-test):** Godot-side, verified manually — own-death center banner per
  cause; rival-death toasts including drowning a rival and seeing "{Name} drowned"
  (the exact gap that motivated this feature); toasts stack and fade; disconnect
  shows "{Name} left".

## Out of Scope

- Audio controls (music toggle, music/SFX balance) — a separate feature with its
  own spec and plan.
- Player-color tinting of toasts.
- A persistent scrollable kill-log / match history.
