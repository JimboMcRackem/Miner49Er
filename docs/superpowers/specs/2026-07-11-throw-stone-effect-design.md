# Throw-Stone Animation & Effect — Design Spec

**Date:** 2026-07-11
**Status:** Approved (design), pending implementation plan

## Goal

Give the existing stone-throw mechanic a visible, audible presence: network the
throw to all clients, render an arcing stone with a landing effect, play a throw
pose on the thrower, and add throw SFX. Stones and `Simulation.TryThrowStone`
already exist; today the `StoneThrown` event is dropped by the host and clients
see/hear nothing.

## Global Constraints

- **Host-authoritative, deterministic Core.** The throw is decided in Core; the
  visual/audio is client-side. The only Core additions are a snapshot data
  record + codec support (data, not logic).
- **Wire safety.** Append the new snapshot list; never reorder existing snapshot
  fields or codec order. A snapshot with no throws serialises an empty/omitted
  list.
- **Run `godot` via PowerShell only** (Bash shim breaks headless import). Import
  new art with `godot --headless --import --quit`.
- **Never `git add -A`.** Stage feature files by explicit path. Do not stage
  `*.uid`, `.superpowers/`, `Temp/`, `_preview_*`, or unrelated edits.
- **PixelLab API key** must never be written to a memory file or committed.
- End every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## Part 1 — Network the throw (host → clients)

- New Core record in `src/Miner49er.Core/Net/Snapshots.cs`:
  `StoneThrowSnapshot(int ThrowerId, int FromX, int FromY, int ToX, int ToY)`,
  mirroring `PortalUseSnapshot` / `WhistleSnapshot`.
- New optional list on the snapshot type: `List<StoneThrowSnapshot>? Throws`
  (default null), added the same way `PortalUses` / `Whistles` are.
- `SnapshotCodec` (`src/Miner49er.Core/Net/SnapshotCodec.cs`) writes/reads the
  list (count-prefixed, same style as the other optional lists).
- `game/net/MatchHost.cs` `TickAndBroadcast`: add `case StoneThrown st:` in the
  event switch. Capture the thrower's current position (`_sim.GetMiner(st.MinerId).Pos`)
  as the origin; append `new StoneThrowSnapshot(st.MinerId, from.X, from.Y, st.LandingPos.X, st.LandingPos.Y)`
  to a per-tick list; attach to the snapshot via `snapshot with { Throws = … }`
  when non-empty (exactly like `_portalUses`).
- `game/net/MatchClient.cs` exposes the current tick's `Throws` so `WorldRenderer`
  and `MatchAudio` can consume them (same access path as `PortalUses`).

## Part 2 — Client visual (`game/WorldRenderer.cs`)

- Active-throws list of `(Vector2 from, Vector2 to, float life, float dur)`,
  `dur ≈ 0.35s`. A new throw is enqueued when a `StoneThrowSnapshot` first
  appears for a tick (dedup so the same throw isn't enqueued every frame the
  snapshot is held).
- `_Process` advances `life`; entries with `life >= dur` are removed after their
  landing effect is spawned.
- `_Draw` per active throw (respecting fog: draw only if flight/landing tile is
  visible or spectating):
  - `t = life / dur`; flat position `flat = from.Lerp(to, t)`.
  - **Shadow:** faint dark ellipse at `flat`, slightly shrinking as the stone
    rises.
  - **Stone:** small grey pebble at `flat + (0, -arcHeight * 4 * t * (1 - t))`
    (`arcHeight ≈ ts * 0.6`).
  - **Landing (at `t` reaching 1):** dust puff + a quick expanding faint ring
    ("noise ping") at `to`, visually distinct from explosion rings. Reuse the
    existing ring/flash timing structures where practical; the ping is a
    dedicated subtle grey-white ring so it does not read as an explosion.
- No lingering 4s noise marker (the Core noise source's lifetime is unchanged;
  the ping is a one-shot).

## Part 3 — Throw pose (new `throw/` animation category)

- New per-direction pose art `throw/{n,e,s,w}{0..3}.png` (4 frames: wind-up →
  release → follow-through) under each miner's folder.
- `game/net/MatchClient.cs`: a `_throwUntil[minerId] = drawNow + dur` map (like
  `_walkUntil`), set when a `StoneThrowSnapshot` arrives for that `ThrowerId`.
  In the sprite-selection block the throw pose takes **priority** over
  walk/mine/plant/idle/listen while active; frame indexed by elapsed fraction.
- Loaded as a new texture category alongside the existing walk/mine/plant sets
  (built against the current texture arrays now; the miner-variants refactor
  will absorb `throw` into its lazy `(variant,colour)` set cache).
- **Default miner:** recreate a matching PixelLab character, generate only the
  throw pose, place at `assets/miners/throw/…`.
- **4 variants (burly/gaunt/veteran/matron):** each gets its own `throw/` set
  generated with its other animations. **The miner-variants plan is updated** to
  add the `throw` category to `MinerSourceImages` / `MinerTextureSet` /
  `GetVariantSources` / `GetOrBuildSet` and to each variant's PixelLab art brief.

## Part 4 — Audio (`game/net/MatchAudio.cs`, `game/audio/SfxLibrary.cs`)

- Two synthesized `SfxLibrary` entries (consistent with existing placeholder
  synths): `StoneThrow` (a light swish) and `StoneLand` (a short clack). If a
  suitable real asset is dropped in later, the loader picks it up automatically.
- On a new `StoneThrowSnapshot`: play `StoneThrow` positionally at the thrower's
  origin immediately, and `StoneLand` at the landing tile deferred by ~`dur` so
  it coincides with the puff (reuse the existing deferred-schedule pattern, or a
  simple timed queue).

## Testing

- **Core (TDD):** `SnapshotCodec` round-trips the `Throws` list — a snapshot
  carrying one or more `StoneThrowSnapshot`s survives write→read with thrower id
  and all four coordinates intact; a snapshot with no throws round-trips cleanly.
- **Manual Godot:** throw a stone in-game → arcing stone + shadow, landing puff +
  noise ping, throw pose on the thrower, swish + clack SFX. Confirm a remote/bot
  throw renders and is heard on another client. Confirm default miner shows its
  recreated throw pose.

## Out of Scope (YAGNI)

- Changing the throw's gameplay (range, noise lifetime, monster reaction) — Core
  behaviour is untouched.
- A persistent visual marker for the noise source's full 4s lifetime.
- Throw poses for monsters or NPCs.
