# Item 10 — Combat in Open Space: Design Spec

## Problem

Dynamite requires an adjacent wall. In mined-out open areas — the most common late-game state — there are no walls next to rivals, so explosives become useless for direct combat.

## Chosen Direction (confirmed by playtester)

Three mechanics, all complementary:

1. **Floor traps** — plant a charge on a floor tile; it triggers when any miner steps on it
2. **Pickaxe stun** — press Use facing an adjacent rival miner or Goat to stun them; dizzy graphic needed
3. **Ceiling rock falls** — triggered when gold drops to 5%; shadow warning grows then a Rock tile drops (stays on floor, blastable)

---

## 1. Floor Traps

### Behaviour
- Player facing a floor tile presses the plant key (same as wall dynamite)
- If the target tile is **Floor** (not Wall/Water/Pit/etc.) and no existing charge is there: plant a trip charge
- Uses the same `TryStartPlanting` flow; only the target-tile eligibility check changes
- Trip charge sits on the floor tile as a `TripCharge` (separate from `Charge` to avoid fuse countdown)
- Any miner who **moves onto** that tile triggers it: explosion at that position with standard blast radius
- Triggering miner takes the kill. Owner of the trap gets kill credit.
- Friendly fire: yes in PvP modes (LMS, Derby, GoldRush); no in co-op Expedition

### Visual
- Thin glowing ring on the floor tile (drawn by WorldRenderer, similar to existing shimmer)
- Colour: red-orange, ~60% opacity, pulses gently
- Owner sees it at full brightness; rivals see it dimmer if within vision range (not hidden, just subtle)

### Core changes
- `SimConfig`: `bool TripMinesEnabled` (true in all non-Expedition modes by default)
- `Simulation`: `_tripCharges: List<TripCharge>` — new list separate from `_charges`
- `TripCharge`: `{ OwnerId, Pos, BlastBonus }`
- `TryStartPlanting` — if target tile is Floor and trip mines enabled, plant trip charge instead of wall charge
- `TryMove` — after moving, check if new position matches any `_tripCharge`; if so, detonate
- New `SimEvent`: `TripMineTriggered(ownerId, triggeredById, pos)`
- `SnapshotFactory`: include trip charges in snapshot so clients render them
- WorldRenderer: draw pulse ring for each `TripChargeSnapshot`

---

## 2. Pickaxe Stun

### Behaviour
- Player facing an **adjacent tile** occupied by a living miner **or a Goat monster** presses the mine/use key
- Applies `Stunned` status for **0.8 seconds** — target cannot move or act
- Stunning a Goat interrupts its charge and resets its direction
- Cooldown: 2 seconds between stun attempts per miner (prevent spam)
- Works against: rival miners, bots, the Goat
- Does NOT work on Slimes, Ghosts, Zombie Miners (use dynamite or lanterns for those)
- No kill — stun only, giving attacker a window to plant or flee

### Dizzy visual
- WorldRenderer draws above the stunned miner's head: three small rotating stars
- Implemented as small filled circles (radius ~2px) orbiting a central point ~8px above head
- Animated via elapsed time — orbit at 180°/s, equally spaced 120° apart
- Colour: yellow/white
- For Goat: same stars drawn above the goat sprite

### Core changes
- `Simulation.TryMine(int id)` — before mining: check if facing tile is occupied by a rival miner or Goat; if so, apply stun and return (no mining action)
- `Miner`: `double StunRemaining`, `double StunCooldown` — advance in `AdvanceMiners`; stunned miner's `TryMove`/`TryMine`/`TryUse` all return false while `StunRemaining > 0`
- `Monster` (Goat only): `double StunRemaining` — when stunned, `GoatDir` returns null (stops cold)
- `SimEvent`: `MinerStunned(attackerId, targetId)`, `MonsterStunned(attackerId, monsterId)` for audio
- WorldRenderer: stun orbit animation keyed off `MinerSnapshot.StunRemaining > 0`

---

## 3. Ceiling Rock Falls

### Trigger
- **NOT** per-tile stress. Falls begin once the map's gold drops to **≤ 5% remaining** (`GoldCollectedFraction >= 0.95`)
- Once triggered, a random floor tile is selected every few seconds and a pending fall begins
- Selection biases toward heavily-mined open areas (floor tiles with many floor neighbours)

### Behaviour
- **Warning phase**: a circular shadow grows on the target floor tile over **2–4 seconds** (random per fall)
- **Rumble SFX**: plays when the shadow first appears
- **Fall**: when timer expires, the tile becomes **`TileType.Rock`** — a real rock tile, mineable and blastable as normal
- Anyone standing on the tile when it resolves: killed instantly (`DeathCause.Crushed`)
- The fallen Rock stays on the map permanently — narrows the open area, forces re-routing, can be blasted
- Maximum **3 simultaneous pending falls** per map at once (avoid visual chaos)
- Falls continue triggering at random until match ends or floor changes

### Design constraints
- Minimum **2s warning** — no instant kills
- Falls only trigger when `RockFallsEnabled` (off in Expedition; on in LMS, Derby, GoldRush, ReachCenter, TreasureHunt)
- Bots: see `PendingFallSnapshot` and flee if within 3 tiles (reuse existing `FleeFrom` logic)

### Core changes
- `SimConfig`: `bool RockFallsEnabled`, `double RockFallMinDelay = 2.0`, `double RockFallMaxDelay = 4.0`, `double RockFallIntervalSeconds = 6.0`
- `Simulation`: `_pendingFalls: List<PendingFall>`, `_rockFallTimer: double`, `_rockFallsActive: bool`
- `PendingFall`: `{ Pos, SecondsRemaining, TotalSeconds }`
- `AdvanceFalls(dt)`: decrement `_rockFallTimer`; when it fires, pick a candidate floor tile and add `PendingFall`; also advance each fall timer — on expiry: kill occupant, set tile to `TileType.Rock`, emit `RockFell(pos)`
- `_rockFallsActive` set to true when `AllGoldCleared` fraction passes 0.95 — checked in `Advance(dt)`
- Candidate tile selection: floor tiles with ≥ 4 floor neighbours (open areas), excluding spawn tiles and active-fall tiles
- `DeathCause`: add `Crushed`
- Snapshot: `PendingFallSnapshot[] { Pos, FractionElapsed }` — clients use `FractionElapsed` to scale shadow circle
- WorldRenderer: filled dark circle on each pending-fall tile, alpha ~0.6, radius = `ts * 0.45 * FractionElapsed`
- SFX: `RockFell` event → rumble/thud sound on spawn; crunch on tile-set

---

## Confirmed Decisions

| # | Decision |
|---|---------|
| 1 | Pickaxe stun works on rival miners, bots, **and the Goat** |
| 2 | Rock falls triggered by **≤ 5% gold remaining** (not per-tile stress) |
| 3 | Fallen rocks become **`TileType.Rock`** — permanent, mineable, blastable |
| 4 | Trip mine friendly fire: yes in PvP, no in Expedition co-op |
| 5 | Rock falls disabled in Expedition (cave-ins already cover it) |

---

## Implementation Order

1. Floor traps (highest impact, self-contained)
2. Pickaxe stun + dizzy graphic (pickaxe stun on miners + Goat)
3. Ceiling rock falls (new sim subsystem; most complex)

Each ships independently.
