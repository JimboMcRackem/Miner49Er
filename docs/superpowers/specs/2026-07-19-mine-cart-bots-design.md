# Mine-Cart Phase 4 — Bots Using Carts (Design)

**Date:** 2026-07-19
**Status:** Approved (design)
**Feature:** AI bot miners weaponize mine carts in Expedition — push a cart to squash monsters, arm & launch rolling cart-bombs, and carry a lantern from a cart.

## Context

Mine carts (Phase 1 Core sim, Phase 2 render/enable, Phase 3 cargo) are complete and live on `main`. Carts are pushable rail obstacles: walking into a cart shoves it along the rail (momentum), rolling until blocked. A rolling cart **squashes squashable monsters and rolls through them**, **shoves/crushes a miner** in its path, **derails** (destroyed) on a lethal tile, and **chain-pushes** a cart ahead (train). An empty cart beside a miner can be **armed** with a charge (rolling bomb) via the Plant verb; a lantern/charge can be **attached/detached** as cargo via the Use verb.

Carts spawn **Expedition-only** (`MapConfig.MineCarts = mode == GameMode.Expedition`). Expedition is co-op / solo-with-bots, so **there are no rivals** — every miner is a teammate. Therefore cart use is framed as "clever teammates who weaponize the cart against monsters," with **teammate-safety as a hard constraint** (a bot must never crush a teammate). There is no cart-*riding* mechanic in the sim (a cart blocks a miner's tile; you push it, you don't board it), so "ride" is out of scope.

Today bots treat carts purely as walls and route around them (`BotBrain.cs:257-259`). This feature makes Miner+ bots use them.

## Behavior by skill tier

| Skill        | Cart behavior                                             |
|--------------|----------------------------------------------------------|
| Greenhorn    | Route around carts (unchanged); flee monsters (unchanged) |
| Miner        | + Squash a monster with a handy cart                     |
| Foreman      | + Squash + detach & carry a lantern from a cart          |
| DynamiteDan  | + Squash + lantern + arm & launch rolling cart-bombs     |

## Architecture

**New module: `src/Miner49er.Core/AI/CartTactics.cs`** — a pure, static helper called from `BotBrain.Think`'s existing priority chain. `BotBrain.cs` is already ~555 lines; folding this inline would bloat it. `CartTactics` holds the shared roll-predictor and the three tactic evaluators, each individually unit-testable.

**Rejected alternatives:**
- *Inline in `BotBrain`* — smallest diff but worsens an already-large file.
- *Model cart use as pathfinding goals* — doesn't fit the two-step "arm then push," nor the "walk **into** the cart" action, which isn't a normal move-to-goal.

**Interface additions to `Simulation`** (the bot must reason about rails, currently private):
- `public bool IsTrack(GridPos p)` — promote the existing private helper to public.
- `public static bool IsSquashable(MonsterKind k)` — promote/expose so the predictor knows which monsters a cart kills vs. bounces off.

Both are read-only projections of existing internal logic; no behavior change to the sim.

### Shared predictor

```
CartTactics.PredictRoll(sim, cartPos, dir) -> RollPrediction
```

`RollPrediction` fields:
- `IReadOnlyList<GridPos> Tiles` — the tiles the cart would occupy in order (excluding its start).
- `int MonstersSquashed` — count of squashable monsters on the path.
- `bool MinerInPath` — a living miner sits on any rolled tile (teammate-safety trip).
- `bool Derails` — the roll ends on a lethal tile (cart destroyed; a charge cart detonates there).

`PredictRoll` mirrors `RollCart` exactly, as a pure integer walk from `cartPos` stepping `dir`:
1. `next = pos + dir`. If `!IsTrack(next)` → stop.
2. If `Grid.Get(next).IsLethal()` → append `next`, set `Derails = true`, stop.
3. If a squashable monster is at `next` → `MonstersSquashed++` (roll continues through it).
4. If a living miner is at `next` → `MinerInPath = true` (roll continues in prediction; consumers treat this as disqualifying).
5. Append `next`, advance, loop.

(Chain-push of a cart ahead is **not** modeled in the predictor — an opportunity requiring a train is simply not taken. YAGNI; keeps the predictor simple and safe.)

## Tactic 1 — Squash (Miner+)

Evaluated in `Think` **just before the existing monster-flee block**, so a bot that can squash does so instead of fleeing; otherwise it falls through to fleeing. Only considered when a living monster is within 5 tiles of the bot (so it never fires on a monster-free floor).

**Opportunity scan** — for each cart × each of 4 directions `D`:
- Require `IsTrack(cart + D)` (a push only rolls if there's track ahead; otherwise the cart just blocks the move).
- `pred = PredictRoll(cart, D)`. Keep the opportunity only if **all** hold:
  - `pred.MonstersSquashed >= 1`
  - `pred.MinerInPath == false` (teammate-safe, hard constraint)
  - `pred.Derails == false` (don't waste the cart / risk a chain into lava)
- Push tile `P = cart - D` (where the bot stands to shove in `D`). Require `P` in-bounds, walkable, and `miner.Pos.ManhattanTo(P) <= 5` (keeps it a local, opportunistic play — not a map-crossing detour).

**Selection:** among valid opportunities, pick the most monsters squashed; tie-break by nearest `P` (Manhattan), then by lowest cart Id then direction ordinal — fully deterministic.

**Act:**
- Bot already on `P` → return `BotAction(dir: D)` (walks into the cart; sim rolls it; squash happens).
- Else → set `_goal = P`, `_ticksUntilReeval = 0`, and route to it via the existing pathfinder (which already treats other carts as blocked).

## Tactic 2 — Cart-bomb (DynamiteDan only)

Stateless, driven off the cart's own `Cargo` (no bot memory needed). Evaluated alongside squash (Dan considers both; a valid squash with a cart already in place is cheaper than arming, so squash is checked first, then bomb).

- Dan **adjacent to an empty cart** (`Cargo == None`) with a valid, teammate-safe opportunity down a rail — where the roll's payoff is a **monster (≥1 squashed) or a gold seam at the end** (`GoldRock` on the tile past the last rolled tile) — → **arm it**: return `BotAction(dir: toward cart, plant: true)`. The sim arms an adjacent empty cart on Plant when not facing a blastable wall (`Simulation.cs:1222`).
- Dan **on the push tile `P` of an armed charge cart** (`Cargo == Charge`) with the opportunity still valid → **push it**: `BotAction(dir: D)`. If adjacent to an armed cart but not yet on `P`, route to `P`. The rolling charge cart detonates on derail / fuse-out (`RollCart` / `AdvanceCartFuses`).
- **Safety:** the same teammate-safe roll-path check as squash. Blast-radius safety at the detonation point is intentionally **not** modeled — Dan already plants dynamite recklessly, and the roll-path check prevents a direct crush. (Refinement, deferred.)

## Tactic 3 — Lantern-grab (Foreman)

Minimal, cosmetic. Placed low in the priority chain, near the cosmetic "listen" pose block, and only when nothing urgent is happening (no hazard fled this tick, escape not open).

- Foreman, **empty-handed and adjacent to a lantern-carrying cart** (`Cargo == Lantern`), with a small per-tick chance (`~0.02`, seeded RNG) → **detach & carry** the lantern: `BotAction(dir: -1, use: true)`. The sim detaches an adjacent laden cart's cargo into an empty hand on Use (`Simulation.cs:1699`).

Bots see the whole map, so this is flavor (a visibly-lit wandering bot) rather than a functional advantage; kept deliberately small.

## Safety & determinism

- **Teammate-safety is an invariant** across squash and bomb: any predicted roll path containing a living miner disqualifies the push. Covered by a dedicated test.
- **Determinism:** every tactic reads only sim state (`Carts`, `Monsters`, `Grid`, `IsTrack`) and the bot's seeded `Random`. `PredictRoll` is a pure integer walk — no wall-clock, no float logic. Host/client and replays stay in lockstep, consistent with the rest of the bot AI.
- **Priority order in `Think`:** explosive-avoidance (unchanged, highest) → **cart-squash / cart-bomb (new)** → monster-flee (existing fallback) → rockfall/mine avoidance (unchanged) → … → lantern-grab (near the cosmetic listen block). Carts are used proactively against monsters but never override fleeing a live charge.

## Testing

**New `CartTacticsTests` (Core):**
- `PredictRoll`: stops at track-end; derails into lava; squashes a squashable monster and rolls on; flags a miner in path; counts multiple monsters.
- Squash: Miner on the push tile pushes into the cart; navigates to `P` when off it; **skips when a teammate is in the roll path**; ignores a cart with no track ahead; no-op with no monster within range.
- Bomb: Dan arms an adjacent empty cart on a valid opportunity, then pushes the armed cart; teammate-safe skip; gold-seam-end also counts as a payoff.
- Lantern: Foreman detaches a lantern from an adjacent cart when empty-handed.

**`BotBrainTests` (wiring / tiering):**
- Greenhorn routes around carts unchanged (no squash/bomb/lantern).
- Miner squashes but does not arm bombs or grab lanterns.
- Foreman squashes and grabs lanterns but does not arm bombs.
- DynamiteDan does all three.
- Cart tactics do not fire when a live charge is nearby (explosive-avoidance wins priority).

## Out of scope

- Cart-riding (no sim mechanic).
- Crushing rival miners (Expedition has no rivals).
- Chain-push (train) opportunities in the predictor.
- Blast-radius safety for cart-bombs.
- Escort AI (pushing a lantern cart toward human teammates).
