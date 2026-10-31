# Global Prize Event — Design Spec

**Date:** 2026-07-12
**Goal:** Make competitive multiplayer livelier by periodically spawning an announced, minimap-pinged prize that all players rush and fight over — turning lonely fog-of-war mining into recurring flashpoints.

**Status:** Design approved; ready for implementation planning.

---

## 1. Overview & Architecture

A host-authoritative, deterministic **prize-event subsystem** owned by `Miner49er.Core.Simulation`. At most one prize event is active at a time. The host advances the event in `Tick`, resolves claims, applies rewards, and emits sim events. Clients render the event purely from a networked `PrizeEventSnapshot` (banner, minimap ping, on-map visual) — no client-side authority.

Rationale: claim timing and rewards must be identical for every peer, so the logic lives in the engine-free Core sim alongside charges/monsters/items, not in Godot.

**Scope gate:** competitive modes only — `LastManStanding`, `GoldRush`, `ReachCenter`, `TreasureHunt`, `DemolitionDerby`. **Expedition is excluded** (co-op/solo — no rivals to fight). Controlled by a lobby toggle `PrizeEventsEnabled` (default ON for competitive).

---

## 2. Event Lifecycle (one at a time)

State machine on `Simulation`:

```
Idle ──(arm timer elapses)──> Telegraph ──(telegraph timer)──> Active
Active ──(claimed)──> Idle (re-arm)
Active ──(expiry timer)──> Idle (re-arm)
```

- **Arm timer:** first event fires after `PrizeFirstDelaySeconds` (default 150s). After each event resolves (claimed or expired), re-arm after `PrizeIntervalSeconds` (default 150s) ± `PrizeJitterSeconds` (default 30s), drawn from the sim RNG (`_rng`) for determinism.
- **Telegraph (`PrizeTelegraphSeconds`, default 5s):** on entering Telegraph the sim picks the event **type** (uniform random over the enabled set) and a **spawn tile** (§4), and emits `PrizeTelegraphed`. Banner "⚡ Prize incoming!" + pulsing minimap ping at the tile. The prize is visible but **not yet claimable**.
- **Active:** prize is interactable per its type (§3).
- **Expiry (`PrizeExpirySeconds`, default 60s):** if unclaimed, despawn, emit `PrizeExpired`, re-arm.

Only competitive matches ever leave `Idle`. If `PrizeEventsEnabled` is false, the subsystem stays `Idle` forever (no-op).

---

## 3. Prize Types & Claim Mechanics

`enum PrizeType { GrabAndGo, MineOut, HoldPoint, CarryRelic }`. All four are in the rotation.

Shared state while Active: `PrizePos`, `ClaimProgress` (0..1, meaning depends on type), `HolderId` (-1 = none; used by MineOut/HoldPoint/CarryRelic).

- **GrabAndGo** — the instant any alive miner occupies `PrizePos`, they claim it. `ClaimProgress` unused.
- **MineOut** — a claimant must **channel** on `PrizePos`: `ClaimProgress` accrues by `dt / PrizeMineSeconds` (default 4s) each tick while a single alive, **un-stunned** miner stands on the tile. If the tile is vacated, the occupant dies, or is stunned, `ClaimProgress` resets to 0 and `HolderId` clears. First to reach 1.0 claims. (Rivals stun/dynamite the channeler off to steal it.)
- **HoldPoint** — `ClaimProgress` accrues by `dt / PrizeHoldSeconds` (default 5s) while **exactly one** alive miner is within `PrizeHoldRadius` (default 3, Chebyshev) of `PrizePos` and **no rival** is in the ring. If zero miners in the ring → progress **pauses**. If two+ miners (contested) → progress **resets** to 0 and `HolderId` clears. First to 1.0 claims.
- **CarryRelic** — the prize is a carried relic item at `PrizePos`. A miner stepping onto it picks it up (`HolderId` = that miner; reuse the item-carry/`Held` infra). Carrying it onto the holder's **spawn tile** banks it → claim. On the holder's death or stun the relic **drops** at their current tile and becomes grabbable again (`HolderId` = -1). `ClaimProgress` unused (position-based).

Interruption is the whole point: MineOut and HoldPoint make the claimant a stationary target; CarryRelic makes them a chased carrier.

---

## 4. Spawn-Tile Selection

Deterministic via `_rng`. Choose a tile that is:
1. `IsEnterable()` and not water/lava/pit (a safe floor-like tile),
2. reachable (skip fully walled-off pockets — reuse existing reachability/flood-fill if available; otherwise nearest-open fallback),
3. **not within `PrizeMinPlayerSpacing` (default 4) tiles of any living miner** (fairness — nobody starts on top of it),
4. biased toward the **map center / contested area**: sample several candidates, prefer the one minimizing max-distance to all living players (pulls players together).

Fallback: if no candidate satisfies (3) after N samples, relax spacing progressively.

---

## 5. Mode-Appropriate Reward (on claim)

`ApplyPrizeReward(minerId, mode)` switches on the match `GameMode`:

| Mode | Reward | Reuses |
|------|--------|--------|
| Gold Rush | `+PrizeGoldReward` gold (default 20) | `GoldCollected` |
| Treasure Hunt | one random idol credited to the claimer | idol-found infra |
| Last Man Standing | brief invulnerability (default 4s) + bigger-blast buff | `InvulnerableRemaining`, `ApplyEffect(BlastRadius)` |
| Reach Center | haste — move-speed buff (default 8s) | **new** `EffectChannel.Haste` applied in move cadence |
| Demolition Derby | combat loadout: `+3` stones + bigger-blast buff | `AddStones`, `ApplyEffect(BlastRadius)` |

`EffectChannel.Haste` is the one new piece of buff infra; everything else reuses existing channels/systems. Confirm the exact move-cadence hook during planning.

Emit `PrizeClaimed(minerId, type)` for the banner/feed.

---

## 6. Presentation (Godot)

- **Banner:** reuse `Main.SetAnnouncement` for "⚡ Prize incoming!" (telegraph) and "{Name} claimed the prize!" (claim); route claim/expire through `DeathFeed` too.
- **Minimap:** `MinimapOverlay` draws a pulsing marker at `PrizePos` during Telegraph + Active.
- **On-map:** `WorldRenderer` draws a glowing prize marker with a per-type icon (placeholder primitive shapes first; PixelLab art later). MineOut/HoldPoint draw a claim-progress ring (from `ClaimProgress`); CarryRelic shows the relic on the ground and a carried indicator on the holder.
- Visible through fog (like thrown-stone arcs) so the announced flashpoint is always readable; or restrict to minimap ping + banner if we want it hidden until close — **default: prize marker always visible on minimap, on-map visual only when the tile is in view/spectating.**

---

## 7. Networking

New optional field on `WorldSnapshot`:

```
PrizeEventSnapshot(byte Type, byte State, int X, int Y, float ClaimProgress, int HolderId, float SecondsRemaining)
```

- `State`: 0 Idle (omit snapshot), 1 Telegraph, 2 Active.
- Encoded/decoded in `SnapshotCodec` mirroring existing optional sections (write count/flag then fields). `SecondsRemaining` drives client countdown UI.
- Sim events (`PrizeTelegraphed`, `PrizeClaimed`, `PrizeExpired`) drained in `MatchHost.TickAndBroadcast` → snapshot attach (like `StoneThrown`/`PortalUses`).

---

## 8. Config & Lobby Toggle

`SimConfig` tunables (all defaulted above): `PrizeFirstDelaySeconds`, `PrizeIntervalSeconds`, `PrizeJitterSeconds`, `PrizeTelegraphSeconds`, `PrizeExpirySeconds`, `PrizeMineSeconds`, `PrizeHoldSeconds`, `PrizeHoldRadius`, `PrizeMinPlayerSpacing`, `PrizeGoldReward`.

Lobby: add `PrizeEventsEnabled` toggle → persisted via `SettingsStore.LoadLobby/SaveLobby` → threaded through `NetworkManager.StartMatch` → `MatchHost`/`SimConfig`. Disabled/hidden for Expedition.

---

## 9. Testing (Core, xUnit)

- Lifecycle: no event before `PrizeFirstDelaySeconds`; Idle→Telegraph→Active transitions at the right times; unclaimed → Expired → re-arm.
- Toggle off → never leaves Idle.
- GrabAndGo: stepping on the tile claims + fires `PrizeClaimed`.
- MineOut: progress accrues while standing; resets on leave/stun; claims at threshold.
- HoldPoint: progress accrues solo-in-radius; pauses when empty; resets when contested; claims.
- CarryRelic: pickup sets holder; carrying to spawn banks; death/stun drops it.
- Rewards: Gold Rush adds gold; at least one buff-reward mode applies its effect.
- Spawn selection never lands on a player or a lethal tile.

---

## 10. Phased Build Order

Each phase is an independently testable, mergeable slice.

1. **Framework + GrabAndGo** — state machine, timers, spawn selection, `PrizeEventSnapshot` + codec, lobby toggle, banner + minimap ping, GrabAndGo claim, one reward (Gold Rush gold). Complete vertical slice.
2. **MineOut + HoldPoint** — channel/hold claim mechanics + progress-ring rendering.
3. **CarryRelic** — relic pickup/carry/drop + carried visual.
4. **Per-mode rewards + polish** — full reward table incl. `EffectChannel.Haste`, DeathFeed lines, on-map per-type icons.

---

## Open tuning defaults (adjustable)
Cadence ~150s ±30s · telegraph 5s · expiry 60s · one event at a time · hold radius 3 · mine 4s · hold 5s · min player spacing 4 · gold reward 20.
