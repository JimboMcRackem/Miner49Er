# Mine Carts — Phase 1 (Core Sim) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the authoritative, deterministic, networked core of pushable mine carts: seeded rail lines, a `Cart` sim entity, momentum-push movement (roll / stop / train / derail), monster squash, miner shove/crush, cart occupancy, and the snapshot/codec/factory wiring. Cargo (lantern/charge), rendering, and bot-use are **later phases** — not in this plan.

**Architecture:** Rails are a deterministic set of `Floor` tiles produced by map generation (adjacency within the set defines where a cart can roll — no explicit edge list, no networking of the layout). A `Cart` is an inert sim entity that only moves when a miner walks into it along the rail; a pure resolver in `Simulation` rolls it tile-by-tile until blocked. All state is integer/tile-based for determinism. Mirrors the existing `Monster`/`Portal` entity + `MonsterSnapshot` snapshot/codec/factory patterns.

**Tech Stack:** C# / .NET 10, engine-free `Miner49er.Core`; xUnit tests in `src/Miner49er.Core.Tests`.

## Global Constraints

- **Determinism:** all cart state integer/tile-based; movement resolved in `Simulation`; identical on every client. Seeding uses the map seed, not per-tick RNG.
- **Networking:** cart entity is networked (snapshot + codec, mirroring Monsters). Track layout is derived from the map seed and is NOT networked.
- **Cross-mode isolation:** carts seed only in the standard exploration floor generator. Modes that zero base map features get no carts. No cross-mode leakage.
- **Reuse the established patterns** (`Portal`/`Monster` entity, `MonsterSnapshot` codec/factory, `ScreeCollapses` nullable-list codec idiom `w.Write(snap.X?.Count ?? 0)`).
- Named constants for all tuning. Build: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`. Test: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj -v quiet --nologo`. Existing suite (825) stays green; each task adds tests.
- Stage commits by explicit path; commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Branch off main. Plans/specs under `docs/superpowers/` stay untracked.

---

### Task 1: Rail generation + `IsTrack` query

**Files:**
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`, `src/Miner49er.Core/Map/GeneratedMap.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (store the rail set + expose `IsTrack`)
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs` (new)

**Interfaces:**
- Produces: `GeneratedMap.TrackTiles` (`IReadOnlyList<GridPos>`) — the rail tiles for the floor (empty when the floor has no rail).
- Produces: `bool Simulation.IsTrack(GridPos p)` — true iff `p` is a rail tile. Consumed by Tasks 2–5.

**Design:** A rail line is a straight horizontal or vertical run of Floor tiles. Seed deterministically from the existing map RNG/seed the generator already uses (follow how scree/crystals/portals are seeded — same RNG instance, drawn at the same generation stage so existing placement is unperturbed *after* this draw, or drawn last). ~35% of floors get one rail; a rail is a run of `RailLength` (8) contiguous Floor tiles from a seeded start tile in a seeded axis; abort the rail (empty list) if a straight run of that length in only-Floor tiles can't be found within a few attempts.

- [ ] **Step 1: Write the failing test**

```csharp
using Miner49er.Core;
using Xunit;

public class MineCartTests
{
    // A fixed seed that produces a rail (discover the seed while implementing; pick one that yields a rail).
    [Fact]
    public void RailTiles_AreContiguousFloorAndStraight()
    {
        var map = TestMaps.WithRail();            // helper: generate a floor known to have a rail
        var tiles = map.TrackTiles;
        Assert.NotEmpty(tiles);
        // straight: all same row or all same column
        bool sameRow = tiles.TrueForAll(t => t.Y == tiles[0].Y);
        bool sameCol = tiles.TrueForAll(t => t.X == tiles[0].X);
        Assert.True(sameRow || sameCol);
        foreach (var t in tiles) Assert.Equal(TileType.Floor, map.Grid.Get(t));
    }

    [Fact]
    public void Generation_IsDeterministic_ForSameSeed()
    {
        Assert.Equal(TestMaps.WithRail(seed: 42).TrackTiles,
                     TestMaps.WithRail(seed: 42).TrackTiles);
    }
}
```

(Add a `TestMaps` helper mirroring however existing tests construct a `GeneratedMap`/`Simulation` from a seed; if such a helper exists, reuse it.)

- [ ] **Step 2: Run to verify it fails** — `dotnet test ... --filter MineCartTests` → FAIL (no `TrackTiles`).

- [ ] **Step 3: Implement**

In `GeneratedMap`, add `public IReadOnlyList<GridPos> TrackTiles { get; init; } = System.Array.Empty<GridPos>();`. In `MapGenerator`, after the base terrain (and existing feature seeding) is placed, with probability `CartTrackFloorChance = 0.35` draw a start tile + axis and walk `RailLength = 8` tiles; if every tile is `Floor`, record them as the rail. Add `private const double CartTrackFloorChance = 0.35; private const int RailLength = 8;`. Pass `TrackTiles` into the `GeneratedMap`. In `Simulation`, store `private readonly HashSet<GridPos> _track` built from `map.TrackTiles` in the constructor, and `public bool IsTrack(GridPos p) => _track.Contains(p);`.

- [ ] **Step 4: Run tests** — PASS; full suite green.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core/Map/GeneratedMap.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/MineCartTests.cs
git commit -m "feat(carts): seeded rail generation + Simulation.IsTrack"
```

---

### Task 2: `Cart` entity + spawn on the rail

**Files:**
- Create: `src/Miner49er.Core/Sim/Cart.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs`

**Interfaces:**
- Produces: `Cart` (internal), `CartCargo` enum (`None, Lantern, Charge`), `CartReadModel(int Id, GridPos Pos, Direction Dir, CartCargo Cargo, bool Destroyed)`, and `IReadOnlyList<CartReadModel> Simulation.Carts`. Consumed by Tasks 3–7.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Carts_SpawnOnRail_DeterministicCountAndPositions()
{
    var sim = TestSims.WithRail(seed: 42);
    Assert.InRange(sim.Carts.Count, 1, 2);
    foreach (var c in sim.Carts)
    {
        Assert.True(sim.IsTrack(c.Pos));
        Assert.Equal(CartCargo.None, c.Cargo);
        Assert.False(c.Destroyed);
    }
    // deterministic
    Assert.Equal(sim.Carts.Select(c => c.Pos), TestSims.WithRail(seed: 42).Carts.Select(c => c.Pos));
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL (no `Carts`).

- [ ] **Step 3: Implement** — write `Cart.cs` mirroring `Portal.cs`:

```csharp
namespace Miner49er.Core;

public enum CartCargo { None, Lantern, Charge }

internal sealed class Cart
{
    public int Id { get; init; }
    public GridPos Pos { get; set; }
    public Direction Dir { get; set; } = Direction.East;
    public CartCargo Cargo { get; set; } = CartCargo.None;
    public double FuseRemaining { get; set; }   // >0 only for a launched Charge cart (Phase 3)
    public bool Destroyed { get; set; }
}

public readonly record struct CartReadModel(int Id, GridPos Pos, Direction Dir, CartCargo Cargo, bool Destroyed);
```

In `Simulation`: a `private readonly List<Cart> _carts = new();`, populated in the constructor by placing `CartsPerTrack` (1–2, seeded) carts at seeded rail tiles (`private const int CartsPerTrackMin = 1, CartsPerTrackMax = 2;`). Expose `public IReadOnlyList<CartReadModel> Carts => _carts.Where(c => !c.Destroyed).Select(c => new CartReadModel(c.Id, c.Pos, c.Dir, c.Cargo, c.Destroyed)).ToList();`.

- [ ] **Step 4: Run tests** — PASS; suite green.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Cart.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/MineCartTests.cs
git commit -m "feat(carts): Cart entity + deterministic spawn on rails"
```

---

### Task 3: Momentum-push movement (roll / stop; occupancy)

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs`

**Interfaces:**
- Produces: cart push resolution invoked from the miner-move path. A stationary cart blocks a miner move onto its tile (unless the move is a valid push).

**Design:** Find the miner-move resolution point in `Simulation` (where a miner's attempted step to `dest` is accepted/rejected against passability + occupancy — see how a tile held by a living miner is blocked, ~`Simulation.cs:1349`). Add: if `dest` holds a cart, call `TryPushCart(cart, moveDir)`:
- Push starts only if `IsTrack(cart.Pos + moveDir)` — else the cart is immovable and the miner's move is **rejected** (cart acts as a wall).
- If it starts, roll the cart with `RollCart(cart, moveDir)`, then allow the miner to advance into the cart's **original** tile.

```csharp
// Rolls a cart from its tile in dir, one rail tile at a time, until blocked.
// Task 3: empty rail advances; non-track / off-rail stops. (Squash/shove/train/derail: later tasks.)
private void RollCart(Cart cart, Direction dir)
{
    cart.Dir = dir;
    while (true)
    {
        var next = cart.Pos + dir.ToOffset();
        if (!IsTrack(next)) break;              // track end / wall / off-rail → stop
        if (CartAt(next) != null) break;        // another cart → stop (train in Task 5)
        // monster/miner in the way handled in Task 4; hazard in Task 5
        cart.Pos = next;
    }
}

private Cart? CartAt(GridPos p) { foreach (var c in _carts) if (!c.Destroyed && c.Pos == p) return c; return null; }
```

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Push_RollsCartAlongRail_UntilTrackEnd() { /* place cart mid-rail, miner behind it on rail;
    issue a move in the rail dir; assert cart ends at the last rail tile, miner advanced one tile. */ }

[Fact] public void Push_OffRailDirection_CartIsImmovable_MinerBlocked() { /* miner pushes cart toward a
    non-track neighbour; assert cart didn't move and miner didn't advance. */ }

[Fact] public void StationaryCart_BlocksMinerMove_WhenNotAValidPush() { /* cart with no onward rail; miner
    tries to step onto it; assert blocked. */ }
```

(Use the sim's existing test API to place a cart, position a miner, and advance one move tick. Set up carts/miners via internal test hooks or `InternalsVisibleTo` as existing tests do.)

- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** the resolver + integration above.
- [ ] **Step 4: Run → PASS; suite green.**
- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/MineCartTests.cs
git commit -m "feat(carts): momentum push movement + cart occupancy"
```

---

### Task 4: Squash monsters + shove/crush miners

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs`

**Design:** Extend `RollCart`'s loop to handle an entity on `next`:
- **Squashable monster** (`Slime, ZombieMiner, SkeletonHuman, SkeletonDino, Goat`): set `mo.Alive = false`, add `MonsterKilled(mo.Id)`; the cart advances onto `next` and continues (monsters don't stop a cart). Non-squashable monsters (Ghost/WaterSnake/Octopus) are ignored — they don't block or die.
- **Miner on `next`:** shove them one tile in `dir`. Let `beyond = next + dir`. If the miner can occupy `beyond` (in bounds, `IsEnterable`, no cart/miner there) move them there — if `beyond` is lethal they die (`DeathCause` from the tile, mirroring existing hazard death); then the cart advances onto `next` and continues. If the miner **cannot** be shoved (blocked behind), they are **crushed**: `mo.Alive = false`, `DeathCause.Crushed` (add this enum value), event, and the cart stops at the miner's now-vacated tile? No — a crushed miner's tile is now free, cart advances onto it and continues.

Add `DeathCause.Crushed` to the `DeathCause` enum. Use a `static readonly` squashable set / a `IsSquashable(MonsterKind)` helper.

```csharp
private static bool IsSquashable(MonsterKind k) =>
    k is MonsterKind.Slime or MonsterKind.ZombieMiner
      or MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino or MonsterKind.Goat;
```

- [ ] **Step 1: Failing tests** — `Push_SquashesMonster_AndRollsThrough` (squashable dies, cart continues past); `Push_IgnoresGhost` (ghost survives, cart rolls through its tile); `Push_ShovesMiner_AlongRail`; `Push_CrushesMiner_WhenPinnedAgainstWall` (assert `DeathCause.Crushed`); `Push_ShovesMinerIntoLava_HazardDeath`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS; suite green.**
- [ ] **Step 5: Commit** — `git add` the two files; `feat(carts): squash monsters + shove/crush miners`.

---

### Task 5: Chain-push trains + derail into hazards

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs`

**Design:** Extend `RollCart`:
- **Cart on `next`:** instead of stopping, recursively push the struck cart first (`RollCart(other, dir)`), then re-check `next`: if `other` moved off `next`, the pushing cart advances; if `other` is still there (it was blocked), the pushing cart stops adjacent (train halts). Guard against infinite recursion with a visited set or by resolving the lead cart fully before trailing carts (process front-to-back).
- **Hazard on the rail ahead** (`IsTrack(next)` but `Grid.Get(next).IsLethal()` — a flooded/lava-crept rail): the cart **derails** — it moves onto `next` (or falls in) and is destroyed: `cart.Destroyed = true`; add a `CartDerailed(cart.Id, next)` event (new). It stops rolling.

- [ ] **Step 1: Failing tests** — `Push_CartIntoCart_FormsTrain` (both advance); `Train_StopsWhenLeadBlocked`; `Push_IntoLavaRail_DerailsAndDestroys` (`Destroyed == true`, cart gone from `Carts`).
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS; suite green.**
- [ ] **Step 5: Commit** — `feat(carts): chain-push trains + derail into hazards`.

---

### Task 6: Cart occupancy in monster + bot passability

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`CanMonsterEnter`), `src/Miner49er.Core/AI/BotPathfinder.cs`
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs`

**Design:** Task 3 already blocks miner moves onto carts. Now:
- `CanMonsterEnter(mo, p)` (~`Simulation.cs:826`): also return false when a live cart occupies `p` (monsters don't walk onto carts).
- `BotPathfinder.Passable` (~`BotPathfinder.cs:80`): treat a cart-occupied tile as impassable so bots route around carts. The pathfinder needs the cart positions — pass them in (e.g. a `IReadOnlySet<GridPos>` of cart tiles) the same way it already receives sim state.

- [ ] **Step 1: Failing tests** — `Monster_CannotEnterCartTile`; `BotPathfinder_RoutesAroundCart` (a path that would cross a cart tile detours).
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run → PASS; suite green.**
- [ ] **Step 5: Commit** — `feat(carts): cart occupancy for monsters + bot pathfinding`.

---

### Task 7: Snapshot + codec + factory (networking)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`, `src/Miner49er.Core/Net/SnapshotCodec.cs`, `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Test: `src/Miner49er.Core.Tests/MineCartTests.cs` (wire-symmetry)

**Design:** Mirror `MonsterSnapshot` + the nullable-list codec idiom (`ScreeCollapses`).

- Snapshot: `public readonly record struct CartSnapshot(int Id, int X, int Y, Direction Dir, CartCargo Cargo, double FuseRemaining);` and add `IReadOnlyList<CartSnapshot>? Carts = null` to the `WorldSnapshot` record's optional tail.
- Codec write (mirror ScreeCollapses): `w.Write(snap.Carts?.Count ?? 0); foreach (var c in snap.Carts ?? System.Array.Empty<CartSnapshot>()) { w.Write(c.Id); w.Write(c.X); w.Write(c.Y); w.Write(c.Dir); w.Write((int)c.Cargo); w.Write(c.FuseRemaining); }`. Read symmetric, in the **same position** in the stream as the write.
- Factory: `var carts = sim.Carts.Select(c => new CartSnapshot(c.Id, c.Pos.X, c.Pos.Y, c.Dir, c.Cargo, /*fuse*/ 0)).ToList();` and pass into the `WorldSnapshot` (fuse stays 0 until Phase 3).

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void CartSnapshot_RoundTripsThroughCodec()
{
    var snap = TestSnapshots.WithCarts(new CartSnapshot(1, 4, 5, Direction.East, CartCargo.None, 0));
    var bytes = SnapshotCodec.Encode(snap);          // use the real encode/decode entry points
    var back  = SnapshotCodec.Decode(bytes);
    Assert.Equal(snap.Carts, back.Carts);
}
```

- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** the snapshot field, codec write/read, factory projection.
- [ ] **Step 4: Run → PASS; full suite green.**
- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/MineCartTests.cs
git commit -m "feat(carts): CartSnapshot + codec + factory wiring"
```

---

## Final verification

- [ ] Build clean; full Core suite green (825 + new cart tests).
- [ ] Then use **superpowers:finishing-a-development-branch** to merge Phase 1 to main.
- [ ] **Phase 2 (presentation), Phase 3 (cargo: lantern sweep + charge bomb), Phase 4 (bots)** each get their own plan, written against the interfaces this phase established (`Simulation.Carts`, `IsTrack`, `CartSnapshot`, `CartCargo`, `CartDerailed`).

## Completion / tuning knobs

`CartTrackFloorChance` (0.35), `RailLength` (8), `CartsPerTrackMin/Max` (1–2), `IsSquashable` kind set, `DeathCause.Crushed`. No floats in cart state except `FuseRemaining` (unused until Phase 3).
