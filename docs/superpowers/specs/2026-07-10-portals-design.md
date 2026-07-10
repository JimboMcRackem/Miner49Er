# Portal Gates — Design Spec

**Date:** 2026-07-10
**Feature:** Task4.txt #2 — colour-coded teleport gates set in the rock.
**Status:** Design approved; ready for implementation planning.

## Summary

Strange gates set in the rock glow with a colour that tells you what they do.
Stepping onto a live gate teleports the miner to its linked partner elsewhere on
the same floor. A gate's colour encodes risk, and a gate stays a black "void"
until its buried partner is uncovered — tying portals into the core mining loop.

This is a movement feature only. It has no effect on the escape tile, the
Expedition artifact/treasure gate, gold, or any objective.

## Gate kinds and states

There are two true kinds, plus a dormant appearance:

- **Stable** — glow **cycles blue ↔ green**. Reusable **two-way**: step on either
  end to emerge at the other. Subject to a short transit cooldown (below).
- **Unstable** — **pulsing red**. Two-way until the **first traversal by any
  miner**, after which **both ends collapse and vanish**. Exactly one trip is
  ever possible — a race/sacrifice decision in co-op.
- **Black void** is a *state*, not a kind. A gate renders as a dark void while
  **its partner end is still buried in rock**. When the partner is uncovered, the
  pair goes live and *both* ends reveal their true colour (red or blue-green). A
  partner that is never dug out stays black indefinitely, so the black state
  naturally serves as the bluff / "exit blocked" case — no separate decoy kind is
  needed.

The true kind is hidden behind the black void until the pair is active: you do
not know whether an uncovered-but-dormant gate will become a handy blue-green or
a one-shot red until you dig out its partner.

## Uncovering (mining integration)

Gate ends are placed like buried items: each end's tile may start **buried in
Rock** or **pre-exposed on Floor**, rolled deterministically at map generation.

- A **buried** gate is invisible until its tile is mined; then the archway
  appears (black if its partner is still buried).
- A pair is **active** only when **both ends are on uncovered (non-Rock) Floor**,
  neither end is collapsed, and the cooldown has elapsed.
- Until active, a revealed gate shows the black void and does not teleport.

Consequences that are intended, not bugs:
- You can mine into a surprise gate you did not know was there.
- You can find a black void, then go dig out its partner to bring the pair to
  life — gambling on which colour it turns out to be.
- A pair with a partner buried in impermeable rock (or simply never dug) stays
  black forever and acts as a permanent bluff.

## Transit cooldown

After any traversal, the pair enters a **short cooldown** (target ~2 seconds)
before it can be used again. This removes the ping-pong problem where a miner (or
bot) standing on a stable gate would teleport every tick. Unstable (red) gates
collapse after one use, so the cooldown is chiefly relevant to stable gates.

A per-miner "just arrived" guard also prevents the destination gate from
re-triggering on the same miner until they step off and back on.

## Scope

- **Expedition mode only.**
- Gates first appear on **floor ≥ 12**.
- Only *some* qualifying floors have gates: a **deterministic ~1-in-4 roll**
  (same seed-stable `FloorHash` style as the flooded-cave decision, so host and
  clients agree without communication).
- A gated floor gets **1–2 gate pairs**, with each pair's kind rolled
  (stable vs unstable) and each end's buried/exposed status rolled.
- Gate tiles are placed on Floor positions, **biased to sit against rock walls**
  so they read as "gates set in the rock." They are never placed on water, lava,
  pits, cracked/crumbling, or other hazard tiles, and never on the escape tile,
  the shop tile, or a spawn tile.

## Who travels

- **Any miner — human or AI bot** — teleports when stepping onto a live gate.
  Bots see gate tiles as passable terrain.
- **Monsters ignore gates** — they may walk over a gate tile without triggering a
  teleport.
- **Thrown stones and explosives do not teleport.**
- Optional enhancement (ties into the existing Miner+ hazard-aware pathfinding):
  Miner+ bots may treat **unstable/red** gates as a hazard to avoid. This is a
  nice-to-have, not required for the first cut.

## Data model (engine-free `Miner49er.Core`)

Follows the existing per-feature list pattern (`LavaVent`, `MoldPatch`), not a new
passable `TileType`:

- `enum PortalKind { Stable, Unstable }`
- `record Portal(int Id, GridPos Pos, PortalKind Kind, int LinkId, bool Collapsed, double CooldownRemaining)`
  - `LinkId` references the partner portal's `Id`.
- `Simulation` holds `List<Portal> Portals` (or equivalent), plus lookup by
  position for the movement check.

Derived predicates:
- **Revealed(portal)** — the portal's tile is not Rock (uncovered).
- **Active(portal)** — `Revealed(portal)` and `Revealed(link)` and neither
  `Collapsed` and `CooldownRemaining <= 0`.

Simulation behaviour:
- When a miner's move lands on a portal tile and that portal is Active, teleport
  the miner to the link's position, start the pair's cooldown, and set the
  miner's "just arrived" guard.
- If the pair is **Unstable**, the first traversal sets `Collapsed = true` on
  both ends (they are removed from play; their tiles remain Floor).
- If two miners land on the same unstable gate on the same tick, the
  **lowest miner id** makes the trip and the gate collapses; the other miner's
  teleport does not fire.
- Cooldown counts down each tick; the "just arrived" guard clears when the miner
  moves off the destination tile.

Determinism: all placement and kind/buried rolls derive from the seed via the
existing process-stable `FloorHash`, so host and clients generate identical
portals. `CooldownRemaining` and transient guards are host-authoritative sim
state.

## Networking

- Append-only **`Portals`** list on `WorldSnapshot`, encoded in the same
  positional-codec style as the recent whistle work (write order must match read
  order; appended after existing lists). Fields per portal: `Id, X, Y, Kind,
  Collapsed, CooldownRemaining` (cooldown may be quantised/optional if it proves
  unnecessary on the wire — decided in the plan).
- The client derives **Revealed/Active** from its own tile grid plus these
  fields, and **renders black until the pair is active**, preserving the
  red-vs-blue-green reveal in the UI. (Sending `Kind` before activation is a
  minor, accepted information leak, consistent with the earlier decision to drop
  cheat-resistant fog culling.)
- A **`PortalUsed`** client event (position + kind) drives the whoosh SFX and, for
  red, a collapse puff — same host→client event pattern as the whistle SFX.

## Presentation and art

- One base **archway sprite** (PixelLab), drawn as an overlay on the portal tile
  by `WorldRenderer` from the snapshot list.
- Three animated glow treatments applied in-renderer over the base sprite:
  - **Stable:** blue ↔ green colour cycle.
  - **Unstable:** red pulse.
  - **Dormant:** dark void (no teleport).
- Collapsed gates stop drawing (or briefly play a collapse puff, then disappear).

## Edge cases

- **Destination occupied** by another miner/monster: overlap is allowed
  transiently on arrival (miners already overlap during respawn/teleport snaps).
- **Portal on a tile that later becomes a hazard** (e.g. flooding, lava creep):
  placement excludes hazard tiles at gen; if a hazard later reaches a portal
  tile, the portal is treated as no longer revealed/active (does not teleport
  into danger). Exact handling finalised in the plan.
- **Fog:** gates are only visible when within sight, like everything else; colour
  is readable once seen.
- **Buried gate mined by a monster/explosion** rather than a player: uncovering is
  driven by the tile becoming non-Rock, regardless of cause, so any reveal
  activates the pair.

## Testing (Core, deterministic)

- Placement determinism: same seed/floor → identical portals; gates only on
  floors ≥ 12 and only on ~1-in-4 qualifying floors.
- Stable gate teleports a miner to its link, and back, both directions.
- Transit cooldown blocks immediate re-use; teleport resumes after cooldown.
- "Just arrived" guard prevents same-miner bounce-back.
- Unstable gate: first traversal teleports and collapses **both** ends; a second
  stepper is a no-op; simultaneous steppers resolved by lowest miner id.
- Dormant pair (partner buried) never teleports; uncovering the partner activates
  the pair and reveals the true kind.
- Codec round-trip for the `Portals` list (write order == read order; no
  unintended fields on the wire).
- A bot that steps onto an active stable gate teleports.

Presentation (`WorldRenderer`, `MatchClient`, `MatchAudio`) is exercised
minimally; the deterministic sim carries the behavioural coverage.

## Out of scope

- No interaction with the escape tile, artifact/treasure gate, or scoring.
- No cross-floor portals (destination is always on the same floor).
- Monster teleportation.
- Thrown-object teleportation.
