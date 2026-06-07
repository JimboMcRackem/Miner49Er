# Phase 4b-2 — Flood (Rising-Water Modifier)

**Date:** 2026-06-07
**Status:** Design approved, ready for planning
**Sub-phase of:** Phase 4 (hazards & modes). 4b was split into 4b-1 (modes, timer, per-mode maps — DONE/merged) and **4b-2** (this doc — the flood). Builds on the 4a static-water substrate and the 4b-1 match timer + mode framework.

## 1. Goal

Add **flooding** as a host-selectable **modifier** that can layer on *any* mode: deep water rises inward from the map edges over the course of the match, drowning miners caught in it. The flood adds no new win condition — the base mode plus the universal last-man-standing already decide the winner; the flood just kills. It is **paced by the match clock** (fully floods as the timer reaches 0), so it reuses the 4b-1 timer rather than introducing a second pacing source.

## 2. The flood model (uniform rings, clock-paced)

### 2.1 Edge-distance rings
Each tile has an **edge-distance** `d(p) = min(p.X, p.Y, W-1-p.X, H-1-p.Y)` — the Chebyshev distance to the rectangular border. The impermeable border ring is `d=0`; the map centre has the maximum, `maxDist = min(W-1, H-1) / 2` (integer division). Uniform rectangular rings, fully determined by grid dimensions.

### 2.2 Clock-paced front
`progress = Elapsed / TimeLimit` (clamped to `[0,1]`); `floodedMaxDist = (int)(progress * maxDist)`. A tile is *in the flood* when `1 <= d <= floodedMaxDist`. Near-border tiles (low `d`) flood first; the centre (max `d`) floods last, at `progress ≈ 1` (the moment the round's clock expires).

### 2.3 Shallow leading edge → deep behind (telegraph)
For a flooded tile at edge-distance `d`:
- **Shallow** if `d == floodedMaxDist` — the current front ring (a one-ring warning band; shallow water is enterable but 2× move cost).
- **Deep** (lethal) if `d < floodedMaxDist` — everything already passed by the front.

This reuses 4a's "deep is shallow-ringed" feel as a deliberate telegraph: players see a shallow ring creep one step ahead of the deadly deep.

### 2.4 What floods
Only **open space** converts: `Floor` → water, and any 4a `ShallowWater` the front reaches → deepens per the same rule. `Rock` / `GoldRock` / `ImpermeableRock` remain **walls** until mined. Mining a tile whose `d` is already `<= floodedMaxDist` yields water, not dry floor — the flood reasserts over open space within its current extent each time it advances (and over tiles freshly exposed by `RockMined`/`Explosion` inside the flooded zone), so no one can carve a dry tunnel through drowned ground. Tiles in the still-dry centre (`d > floodedMaxDist`) are untouched.

### 2.5 Determinism
The front is a pure function of grid dimensions and `progress`; tile *types* further depend only on the (delta-synced) open/rock state. The host is authoritative; clients never compute the flood (see §3.2).

## 3. Architecture

### 3.1 Flood lives in Core `Simulation` (deterministic, unit-tested)
`Simulation` already tracks `Elapsed`, `TimeLimit`, and `Grid`. It gains a `bool Flooding` (set at construction) and advances the flood inside `Tick(dt)`, **after** `AdvanceActivities`/`AdvanceCharges` (so it sees tiles freshly mined this tick):
1. Compute `floodedMaxDist` from `progress`. Flood is inert when `Flooding` is false or `TimeLimit` is null.
2. For every open tile (`Floor`/`ShallowWater`) whose target flood type differs from its current type, `Grid.Set` it to Shallow/Deep per §2.3 and emit a new **`TileFlooded(GridPos Pos, TileType Type)`** sim event. (Implementation may scan the affected rings on advance plus react to mining events within the extent — the plan picks the exact mechanism; the observable contract is "open tiles within the front hold the correct Shallow/Deep type, walls don't flood.")
3. Run **`DrownOccupants`**: every living miner whose current tile is now `IsLethal()` (Deep) drowns — `Alive = false`, `Activity = None`, emit `MinerDrowned`. This also fixes the general 4a gap where a tile turning lethal *under* a standing miner didn't kill them (drowning previously only fired on move).

`DrownOccupants` is a small reusable method; the move-time drown in `TryMove` stays as-is.

### 3.2 Sync via typed tile deltas (the carry-forward seam)
- **`TileChange` gains a `TileType NewType`** (`src/Miner49er.Core/Net/Snapshots.cs`); the codec writes/reads it; default keeps back-compat. This replaces the hardcoded `TileType.Floor` the client applies at `MatchClient.cs:59` — the client now applies `t.NewType`.
- `MatchHost` maps drained sim events to `TileChange`: `RockMined` → `Floor`, `Explosion` destroyed tiles → `Floor`, **`TileFlooded` → its `Type`** (with `FromBlast = false`).
- **Clients render the flood entirely from these deltas** — no client-side flood computation, so host and client cannot diverge. (Rejected alternatives: client-side flood from synced progress — duplicates logic, divergence risk; flood in the Godot `MatchHost` layer — not unit-testable, violates the Core-tested principle.)

### 3.3 Threading & the forced time limit
- A `bool Flooding` rides `BeginMatch` alongside the existing `mode`/`timeLimitSeconds`, exposed as `NetworkManager.MatchFlooding`. `Main.cs` constructs the host `Simulation` with it.
- The lobby gets a host-only **"Flooding" `CheckBox`**.
- **Flooding requires a clock, and the lobby enforces it:** `StartMatch` is the authoritative guard — `if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60;` (a flooded match is never untimed). For visible feedback, toggling the Flooding checkbox on also bumps the time dropdown off "No Time Limit" to "1 min" if it was there.

### 3.4 Folded-in carry-forward fixes
- **Reach-Center alive-recheck:** the centre can now flood, so a miner who drowns *on* the centre tile must not win. Guard the resolver's Reach-Center arm (or the `TryMove` latch) so only a *living* center-reacher wins. Concretely: `RoundResolver` returns the center winner only if that miner is still `Alive`.
- **`TileTypeExtensions.IsWater()`:** promote the private `MapGenerator.IsWater` to a public Core helper (`t is ShallowWater or DeepWater`); `MapGenerator` and the flood both use it.

## 4. Decomposition (one cycle; ≈3 tasks)

1. **Core — flood + drowning + fixes:** `IsWater()` helper; edge-distance + flood advance in `Simulation` (gated on `Flooding` + `TimeLimit`); `TileFlooded` sim event; `DrownOccupants` pass; Reach-Center alive guard in `RoundResolver`; xUnit tests (front advances border→centre with progress; shallow front ring / deep behind; rock unflooded; mining inside the zone re-floods; a standing miner drowns when the front reaches them; flooding inert with no time limit / when disabled; reach-center on a flooded tile is not a win).
2. **Netcode — typed tile deltas:** `TileChange.NewType`; `SnapshotCodec` read/write; `MatchHost` event→`TileChange` mapping (incl. `TileFlooded`); `MatchClient.ApplyUpdate` applies `t.NewType`; update `SnapshotCodecTests`.
3. **Lobby + wiring:** host-only "Flooding" checkbox; `Flooding` threaded through `StartMatch`/`BeginMatch` → `NetworkManager.MatchFlooding`; the forced-time-limit guard; `Main.cs` builds the host sim with `Flooding`.

## 5. Verification

- `Miner49er.Core` xUnit suite green (112 prior + new flood/drown/resolver tests).
- `dotnet build Miner49er.csproj` 0 errors; `godot --headless --quit-after 180` exits 0, no error lines.
- Play-test (user): enable Flooding in the lobby (time limit auto-forced off "None"); deep water creeps inward from the edges with a shallow warning ring, drowning miners who linger; the dry centre shrinks to nothing by the clock's end; flooding combines sensibly with each base mode (e.g. Reach Center becomes a race against the closing water); the existing mining/explosion tile reveals still render correctly through the new typed-delta path.
- Final opus review, then merge to main.

## 6. Out of scope / later

Bottomless pit (4d — pit is deep water's lethal twin but a hard edge against floor, not shallow-ringed). Items & §3.5 status effects (4c). No new map-generation knobs for flooding (it works on the existing maps; Reach Center keeps its 4b-1 larger map).
