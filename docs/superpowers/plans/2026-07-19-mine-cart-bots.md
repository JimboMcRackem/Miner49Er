# Mine-Cart Phase 4 — Bots Using Carts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give AI bot miners deterministic, teammate-safe cart tactics in Expedition — Miner+ push a cart to squash a monster, Foreman also detach & carry a lantern, DynamiteDan also arm & launch rolling cart-bombs.

**Architecture:** A new pure static module `CartTactics` (in `Miner49er.Core/AI`) holds a roll-predictor plus three tactic evaluators, each returning `BotAction?`. `BotBrain.Think` calls them in its existing priority chain. Two tiny read-only additions to `Simulation` expose rail/monster info the predictor needs. All logic reads only sim state + the bot's seeded `Random`, so it stays deterministic like the rest of the AI.

**Tech Stack:** C# / .NET 10, xUnit tests in `Miner49er.Core.Tests`. Engine-free `Miner49er.Core`.

## Global Constraints

- **Determinism:** tactics read only `sim` state (`Carts`, `Monsters`, `Miners`, `Grid`, `IsTrack`) and the bot's seeded `Random`. No wall-clock, no float-as-logic. `PredictRoll` is a pure integer walk.
- **Teammate-safety is an invariant:** any predicted roll path containing a living miner disqualifies a push (squash and bomb alike). A bot must never push a cart that crushes a teammate.
- **Expedition-only feature:** carts spawn only in Expedition; there are no rival miners. Cart-riding and rival-crushing are out of scope.
- **Tier gates:** Greenhorn — no cart tactics (route around, unchanged). Miner — squash. Foreman — squash + lantern-grab. DynamiteDan — squash + lantern-grab + cart-bomb.
- **Priority in `Think`:** explosive-avoidance (unchanged, highest) → squash → cart-bomb → monster-flee (existing fallback) → … → lantern-grab (near the cosmetic "listen" block).
- Build/test with `dotnet`; run `godot` only via PowerShell (not needed for this plan — Core tests only).
- Never `git add -A`; stage explicit paths. Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## Reference facts (verified against current code)

- `Direction { North, East, South, West }`; `ToOffset()`: N=(0,-1), E=(1,0), S=(0,1), W=(-1,0).
- `Simulation.IsTrack(GridPos)` is already **public** (`Simulation.cs:211`). No change needed.
- `Simulation.IsSquashable(MonsterKind)` is currently **private static** (`Simulation.cs:236-238`), squashable = Slime, ZombieMiner, SkeletonHuman, SkeletonDino, Goat. Task 1 promotes it to public.
- `Simulation.Grid` (public), `Simulation.Monsters` → `IReadOnlyList<Monster>` (`.Alive`, `.Pos`, `.Kind`), `Simulation.Miners` → `IReadOnlyCollection<Miner>` (`.Alive`, `.Pos`, `.Id`, `.Held` is `ItemKind?`), `Simulation.Carts` → `IReadOnlyList<CartReadModel>` (`.Id`, `.Pos`, `.Dir`, `.Cargo`, `.FuseRemaining`; already excludes destroyed carts).
- `CartCargo { None, Lantern, Charge }`.
- `RollCart` semantics mirrored by the predictor (`Simulation.cs:244-287`): step from cart along `dir`; stop when `!IsTrack(next)`; derail (destroyed) on a lethal tile; squashable monster → killed, roll continues; miner → shoved/crushed. A cart ahead chain-pushes (NOT modeled by the predictor — such options are simply not taken).
- Walking into a cart shoves it only if `IsTrack(cart.Pos + dir)` (`Simulation.cs:1050-1056`); otherwise the cart blocks the move like a wall. `m.Facing = dir` is set at the top of `TryMove` (`:1042`) **before** any early `return false`, so a move blocked by a non-rolling cart still updates facing.
- Arming a cart (`TryStartPlanting`, `Simulation.cs:1218-1227`): if the faced tile is **not** blastable and an orthogonally-adjacent empty cart exists, that cart is armed with a Charge. This is why a bot arms from a tile **perpendicular** to the rail: moving into the cart there is blocked (no perpendicular track), so the cart doesn't roll, but facing is set and the plant arms it.
- Detaching a lantern (`Simulation.cs:1699-1705`): empty-handed beside a laden cart + Use verb → cargo moves into hand.
- The host applies a `BotAction` as move-then-plant-then-use within a tick (`MatchHost.StepOnce`), so a plant/use meant to fire without moving must use `dir = -1`.
- `BotAction(int dir, bool mine=false, bool plant=false, bool use=false, bool throwStone=false, bool whistle=false, bool listen=false)`; `BotAction.Idle == new(-1)`.
- `BotPathfinder.NextDir(TileGrid grid, GridPos from, GridPos to, bool passRock, bool avoidHazards, IReadOnlySet<GridPos>? blocked = null)` returns a direction int or -1.
- `GridPos`: `new GridPos(x,y)`, `.X`, `.Y`, `operator+`, `.ManhattanTo`, `.ChebyshevTo`.
- Test run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo` (append `--filter "FullyQualifiedName~CartTacticsTests"` to scope).

---

## File Structure

- **Create** `src/Miner49er.Core/AI/CartTactics.cs` — the whole feature's logic: `RollPrediction`, `PredictRoll`, `PushOption`, `FindBestPush`, `TrySquash`, `TryBomb`, `TryLantern`, plus private helpers.
- **Modify** `src/Miner49er.Core/Sim/Simulation.cs:236` — `IsSquashable` private→public static.
- **Modify** `src/Miner49er.Core/AI/BotBrain.cs` — call the three tactics in the priority chain.
- **Create** `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs` — predictor + tactic unit tests.
- **Modify** `src/Miner49er.Core.Tests/AI/BotBrainTests.cs` — tier-wiring tests.

---

## Task 1: Roll predictor + `IsSquashable` accessor

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs:236`
- Create: `src/Miner49er.Core/AI/CartTactics.cs`
- Test: `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs`

**Interfaces:**
- Consumes: `Simulation.IsTrack`, `.Grid`, `.Monsters`, `.Miners`, `.Carts`; `Simulation.IsSquashable` (made public here).
- Produces: `CartTactics.PredictRoll(Simulation sim, GridPos cartPos, Direction dir) -> CartTactics.RollPrediction` with fields `IReadOnlyList<GridPos> Tiles`, `int MonstersSquashed`, `bool MinerInPath`, `bool Derails`.

- [ ] **Step 1: Make `IsSquashable` public**

In `src/Miner49er.Core/Sim/Simulation.cs`, change line 236 from:

```csharp
    private static bool IsSquashable(MonsterKind k) =>
```

to:

```csharp
    public static bool IsSquashable(MonsterKind k) =>
```

(Leave the body and the internal `RollCart` call unchanged.)

- [ ] **Step 2: Write the failing predictor tests**

Create `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class CartTacticsTests
{
    // Builds a sim with an all-Floor grid and a straight east-west rail across row `railY`.
    private static Simulation MakeSim(int w = 20, int h = 12, int railY = 6)
    {
        var grid = new TileGrid(w, h, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        sim.AddTrack(Enumerable.Range(0, w).Select(x => new GridPos(x, railY)));
        return sim;
    }

    // Turns cart `id` into a lantern cart via the real attach path (mirrors MineCartCargoTests):
    // an adjacent miner holding a lantern Uses it onto the cart.
    private static void MakeLanternCart(Simulation sim, int cartId, int loaderId, GridPos loaderPos)
    {
        var loader = sim.AddMiner(loaderId, loaderPos);
        loader.Held = ItemKind.Lantern;
        sim.TryUseItem(loaderId);
    }

    // Arms cart `id` with a charge via the real attach path: an adjacent miner holding a
    // Detonator Uses it onto the cart (MineCartCargoTests:102-105). Cargo becomes Charge, fuse unlit.
    private static void ArmCart(Simulation sim, int cartId, int loaderId, GridPos loaderPos)
    {
        var loader = sim.AddMiner(loaderId, loaderPos);
        loader.Held = ItemKind.Detonator;
        sim.TryUseItem(loaderId);
    }

    [Fact]
    public void PredictRoll_stops_at_track_end()
    {
        var sim = MakeSim(w: 10, railY: 5);   // rail spans x=0..9
        // Cart at x=7 pushed east: rolls x=8,9 then stops (x=10 off-grid, not track).
        var pred = CartTactics.PredictRoll(sim, new GridPos(7, 5), Direction.East);
        Assert.Equal(new[] { new GridPos(8, 5), new GridPos(9, 5) }, pred.Tiles.ToArray());
        Assert.False(pred.Derails);
        Assert.Equal(0, pred.MonstersSquashed);
        Assert.False(pred.MinerInPath);
    }

    [Fact]
    public void PredictRoll_counts_squashable_monster_and_rolls_through()
    {
        var sim = MakeSim(railY: 5);
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Slime);   // squashable, on the rail
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.Equal(1, pred.MonstersSquashed);
        Assert.Contains(new GridPos(6, 5), pred.Tiles);            // rolled past the slime
    }

    [Fact]
    public void PredictRoll_ignores_non_squashable_monster()
    {
        var sim = MakeSim(railY: 5);
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Ghost);   // NOT squashable
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.Equal(0, pred.MonstersSquashed);
    }

    [Fact]
    public void PredictRoll_flags_miner_in_path()
    {
        var sim = MakeSim(railY: 5);
        sim.AddMiner(2, new GridPos(6, 5));
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.True(pred.MinerInPath);
    }

    [Fact]
    public void PredictRoll_derails_on_lethal_tile()
    {
        var sim = MakeSim(railY: 5);
        sim.Grid.Set(new GridPos(5, 5), TileType.Lava);           // lethal on the rail
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.True(pred.Derails);
        Assert.Equal(new GridPos(5, 5), pred.Tiles.Last());       // stops AT the lethal tile
    }
}
```

These seams are verified against `MineCartCargoTests.cs`: `new Simulation(grid, new SimConfig())`; `sim.AddTrack(IEnumerable<GridPos>)`; `sim.AddCart(new CartSpec(id, pos, dir))` places a live cart; `sim.AddMonster(id, pos, kind)`; `var m = sim.AddMiner(id, pos)` returns the `Miner` (with `m.Held` / `m.Facing` settable from tests via InternalsVisibleTo). No new production API is needed.

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: FAIL — `CartTactics` does not exist yet.

- [ ] **Step 4: Create `CartTactics` with the predictor**

Create `src/Miner49er.Core/AI/CartTactics.cs`:

```csharp
using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er.Core.AI;

/// <summary>Deterministic bot tactics for weaponizing mine carts in Expedition. Predicts where a
/// pushed cart ends up (mirroring <see cref="Simulation"/>.RollCart) and turns that into squash /
/// cart-bomb / lantern actions. Pure: reads only sim state and a seeded Random.</summary>
public static class CartTactics
{
    public readonly struct RollPrediction
    {
        public readonly IReadOnlyList<GridPos> Tiles;   // tiles the cart occupies in order (excl. start)
        public readonly int  MonstersSquashed;
        public readonly bool MinerInPath;               // a living miner sits on a rolled tile (teammate)
        public readonly bool Derails;                   // roll ends on a lethal tile (cart destroyed there)

        public RollPrediction(IReadOnlyList<GridPos> tiles, int squashed, bool minerInPath, bool derails)
        {
            Tiles = tiles; MonstersSquashed = squashed; MinerInPath = minerInPath; Derails = derails;
        }
    }

    /// <summary>Pure integer walk mirroring RollCart: rolls from <paramref name="cartPos"/> stepping
    /// <paramref name="dir"/> along contiguous track. Counts squashable monsters (rolls through them),
    /// flags any miner in the path, and derails on a lethal tile. A cart ahead stops the prediction
    /// (chain-push is intentionally not modeled — those opportunities are simply not taken).</summary>
    public static RollPrediction PredictRoll(Simulation sim, GridPos cartPos, Direction dir)
    {
        var off = dir.ToOffset();
        var tiles = new List<GridPos>();
        int squashed = 0;
        bool minerInPath = false;
        var pos = cartPos;
        int guard = 0;
        while (guard++ < 10000)
        {
            var next = new GridPos(pos.X + off.X, pos.Y + off.Y);
            if (!sim.IsTrack(next)) break;                       // track end → stop

            if (sim.Grid.Get(next).IsLethal())                  // hazard → derail at this tile
            {
                tiles.Add(next);
                return new RollPrediction(tiles, squashed, minerInPath, derails: true);
            }

            if (CartAt(sim, next) != null) break;               // cart ahead → don't model the train

            foreach (var mo in sim.Monsters)
                if (mo.Alive && mo.Pos == next && Simulation.IsSquashable(mo.Kind)) { squashed++; break; }

            foreach (var m in sim.Miners)
                if (m.Alive && m.Pos == next) { minerInPath = true; break; }

            tiles.Add(next);
            pos = next;
        }
        return new RollPrediction(tiles, squashed, minerInPath, derails: false);
    }

    // Nearest live cart on a tile, or null.
    private static CartReadModel? CartAt(Simulation sim, GridPos p)
    {
        foreach (var c in sim.Carts) if (c.Pos == p) return c;
        return null;
    }
}
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/AI/CartTactics.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/AI/CartTacticsTests.cs
git commit -m "feat(carts): cart roll-predictor + public IsSquashable

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Squash tactic (Miner+)

**Files:**
- Modify: `src/Miner49er.Core/AI/CartTactics.cs`
- Modify: `src/Miner49er.Core/AI/BotBrain.cs` (insert before the monster-flee block, ~line 93)
- Test: `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs`

**Interfaces:**
- Consumes: `PredictRoll` (Task 1); `BotPathfinder.NextDir`; `BotSkill`.
- Produces:
  - `CartTactics.TrySquash(Simulation sim, Miner miner, BotSkill skill) -> BotAction?`
  - `CartTactics.PushOption` (struct: `GridPos CartPos`, `Direction Dir`, `GridPos PushTile`, `GridPos? ArmTile`, `int Monsters`, `bool GoldAtEnd`) and `CartTactics.FindBestPush(Simulation sim, Miner miner, bool forBomb) -> PushOption?` — shared with Task 3.

- [ ] **Step 1: Write the failing squash tests**

Append to `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs` (inside the class):

```csharp
    [Fact]
    public void Squash_pushes_into_cart_when_on_the_push_tile()
    {
        var sim = MakeSim(railY: 5);
        // rail east-west on row 5. Cart at x=8, slime at x=10 (east of cart). Bot at x=7 (push tile
        // to shove the cart EAST). Expected: push east (Direction.East ordinal 1).
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        var act = CartTactics.TrySquash(sim, bot, BotSkill.Miner);
        Assert.NotNull(act);
        Assert.Equal((int)Direction.East, act!.Value.Dir);
        Assert.False(act.Value.Plant);
    }

    [Fact]
    public void Squash_navigates_toward_push_tile_when_not_on_it()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(5, 5));   // west of push tile (x=7); step east toward it
        var act = CartTactics.TrySquash(sim, bot, BotSkill.Miner);
        Assert.NotNull(act);
        Assert.Equal((int)Direction.East, act!.Value.Dir);
    }

    [Fact]
    public void Squash_skips_when_a_teammate_is_in_the_roll_path()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        sim.AddMiner(9, new GridPos(9, 5));             // teammate between cart and slime
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        Assert.Null(CartTactics.TrySquash(sim, bot, BotSkill.Miner));
    }

    [Fact]
    public void Squash_does_nothing_with_no_monster_near()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        Assert.Null(CartTactics.TrySquash(sim, bot, BotSkill.Miner));
    }

    [Fact]
    public void Squash_is_gated_off_for_greenhorn()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        Assert.Null(CartTactics.TrySquash(sim, bot, BotSkill.Greenhorn));
    }
```

All seams here are the verified ones from Task 1 (`AddCart(new CartSpec(...))`, `AddMonster`, `AddMiner` returning the `Miner`). No new helpers needed.

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: FAIL — `TrySquash` not defined.

- [ ] **Step 3: Add `PushOption`, `FindBestPush`, helpers, and `TrySquash`**

In `src/Miner49er.Core/AI/CartTactics.cs`, add `using System;` at the top (for `Math`) and add these members to the class:

```csharp
    private const int MaxPushTileDistance = 5;   // keep cart plays local & opportunistic
    private const int MonsterNearRange    = 8;   // don't scan when no monster is anywhere close

    private static readonly Direction[] AllDirs =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    /// <summary>One evaluated "push this cart in this direction" opportunity.</summary>
    public readonly struct PushOption
    {
        public readonly GridPos   CartPos;
        public readonly Direction Dir;
        public readonly GridPos   PushTile;   // where the bot stands to shove the cart in Dir (= CartPos - Dir)
        public readonly GridPos?  ArmTile;    // a perpendicular-adjacent standing tile to arm from (bomb), or null
        public readonly int       Monsters;   // squashable monsters the roll kills
        public readonly bool      GoldAtEnd;  // rail ends at a GoldRock seam (bomb payoff)

        public PushOption(GridPos cartPos, Direction dir, GridPos pushTile, GridPos? armTile, int monsters, bool goldAtEnd)
        {
            CartPos = cartPos; Dir = dir; PushTile = pushTile; ArmTile = armTile; Monsters = monsters; GoldAtEnd = goldAtEnd;
        }
    }

    /// <summary>Scans all live carts × 4 directions for the best teammate-safe, non-derailing push.
    /// For squash (<paramref name="forBomb"/> false) only non-Charge carts with ≥1 monster count.
    /// For bomb (true) a gold-seam end also qualifies. Deterministic tie-break: most monsters, then
    /// nearest push tile, then lowest cart id, then direction ordinal.</summary>
    public static PushOption? FindBestPush(Simulation sim, Miner miner, bool forBomb)
    {
        PushOption? best = null;
        int bestMonsters = -1, bestDist = int.MaxValue, bestCart = int.MaxValue, bestDir = int.MaxValue;

        foreach (var cart in sim.Carts)
        {
            if (!forBomb && cart.Cargo == CartCargo.Charge) continue;   // squash never launches a live bomb
            foreach (var dir in AllDirs)
            {
                var off      = dir.ToOffset();
                var pushTile = new GridPos(cart.Pos.X - off.X, cart.Pos.Y - off.Y);
                if (!Walkable(sim, pushTile)) continue;
                if (miner.Pos.ManhattanTo(pushTile) > MaxPushTileDistance) continue;

                var pred = PredictRoll(sim, cart.Pos, dir);
                if (pred.Tiles.Count == 0 || pred.MinerInPath || pred.Derails) continue;

                bool goldAtEnd = GoldPastEnd(sim, pred.Tiles[pred.Tiles.Count - 1], off);
                bool payoff    = forBomb ? (pred.MonstersSquashed >= 1 || goldAtEnd)
                                         :  pred.MonstersSquashed >= 1;
                if (!payoff) continue;

                var armTile = PerpendicularArmTile(sim, cart.Pos, dir, miner);

                int dist = miner.Pos.ManhattanTo(pushTile);
                bool better = pred.MonstersSquashed > bestMonsters
                    || (pred.MonstersSquashed == bestMonsters && dist < bestDist)
                    || (pred.MonstersSquashed == bestMonsters && dist == bestDist && cart.Id < bestCart)
                    || (pred.MonstersSquashed == bestMonsters && dist == bestDist && cart.Id == bestCart && (int)dir < bestDir);
                if (better)
                {
                    best = new PushOption(cart.Pos, dir, pushTile, armTile, pred.MonstersSquashed, goldAtEnd);
                    bestMonsters = pred.MonstersSquashed; bestDist = dist; bestCart = cart.Id; bestDir = (int)dir;
                }
            }
        }
        return best;
    }

    /// <summary>Miner+ : push a handy cart to squash a monster instead of fleeing. Returns the push
    /// (or a step toward the push tile), or null when no safe opportunity is in range.</summary>
    public static BotAction? TrySquash(Simulation sim, Miner miner, BotSkill skill)
    {
        if (skill < BotSkill.Miner) return null;
        if (!MonsterNear(sim, miner)) return null;

        var opt = FindBestPush(sim, miner, forBomb: false);
        if (opt is not { } o) return null;

        if (miner.Pos == o.PushTile) return new BotAction((int)o.Dir);   // shove the cart
        int step = BotPathfinder.NextDir(sim.Grid, miner.Pos, o.PushTile,
                                         passRock: false, avoidHazards: true, blocked: CartTiles(sim));
        return step == -1 ? (BotAction?)null : new BotAction(step);
    }

    // ── shared helpers ──────────────────────────────────────────────────────

    private static bool MonsterNear(Simulation sim, Miner miner)
    {
        foreach (var mo in sim.Monsters)
            if (mo.Alive && miner.Pos.ChebyshevTo(mo.Pos) <= MonsterNearRange) return true;
        return false;
    }

    private static bool Walkable(Simulation sim, GridPos p) =>
        sim.Grid.InBounds(p) && sim.Grid.Get(p).IsWalkable() && CartAt(sim, p) == null;

    private static bool GoldPastEnd(Simulation sim, GridPos lastRolled, GridPos off)
    {
        var past = new GridPos(lastRolled.X + off.X, lastRolled.Y + off.Y);
        return sim.Grid.InBounds(past) && sim.Grid.Get(past) == TileType.GoldRock;
    }

    // A walkable tile orthogonally adjacent to the cart and PERPENDICULAR to the launch rail: moving
    // into the cart from there is blocked (no perpendicular track) so the cart won't roll, letting a
    // plant arm it. Returns the nearest such tile to the bot, or null.
    private static GridPos? PerpendicularArmTile(Simulation sim, GridPos cartPos, Direction dir, Miner miner)
    {
        var d = dir.ToOffset();
        GridPos? best = null; int bestDist = int.MaxValue;
        foreach (var pd in AllDirs)
        {
            var po = pd.ToOffset();
            if (po.X * d.X + po.Y * d.Y != 0) continue;        // keep only perpendicular directions
            var a = new GridPos(cartPos.X + po.X, cartPos.Y + po.Y);
            if (!Walkable(sim, a)) continue;
            int dist = miner.Pos.ManhattanTo(a);
            if (dist < bestDist) { bestDist = dist; best = a; }
        }
        return best;
    }

    private static IReadOnlySet<GridPos> CartTiles(Simulation sim)
    {
        var set = new HashSet<GridPos>();
        foreach (var c in sim.Carts) set.Add(c.Pos);
        return set;
    }
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: PASS (all squash tests + Task 1 tests).

- [ ] **Step 5: Wire `TrySquash` into `BotBrain`**

In `src/Miner49er.Core/AI/BotBrain.cs`, immediately **before** the "Monster avoidance" block (the `if (Skill >= BotSkill.Miner)` at ~line 94 that flees monsters), insert:

```csharp
        // Cart squash: Miner+ shove a handy cart to crush a monster instead of fleeing. Checked
        // before the flee block so a bot that can squash does; otherwise it falls through and flees.
        {
            var squash = CartTactics.TrySquash(sim, miner, Skill);
            if (squash != null) { _ticksUntilReeval = 0; return squash.Value; }
        }
```

- [ ] **Step 6: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS (no regressions).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/AI/CartTactics.cs src/Miner49er.Core/AI/BotBrain.cs src/Miner49er.Core.Tests/AI/CartTacticsTests.cs
git commit -m "feat(carts): bots squash monsters with carts (Miner+)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Cart-bomb tactic (DynamiteDan)

**Files:**
- Modify: `src/Miner49er.Core/AI/CartTactics.cs`
- Modify: `src/Miner49er.Core/AI/BotBrain.cs` (insert right after the squash block)
- Test: `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs`

**Interfaces:**
- Consumes: `FindBestPush`, `PushOption`, helpers (Task 2).
- Produces: `CartTactics.TryBomb(Simulation sim, Miner miner, BotSkill skill) -> BotAction?`.

- [ ] **Step 1: Write the failing bomb tests**

Append to `CartTacticsTests.cs`:

```csharp
    [Fact]
    public void Bomb_arms_empty_cart_from_a_perpendicular_tile()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));   // empty
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        // Bot on the perpendicular arm tile (north of the cart). Perpendicular move south into the
        // cart is blocked (no N-S track) → plant arms it. Expect Plant, facing the cart (South).
        var bot = sim.AddMiner(3, new GridPos(8, 4));
        var act = CartTactics.TryBomb(sim, bot, BotSkill.DynamiteDan);
        Assert.NotNull(act);
        Assert.True(act!.Value.Plant);
        Assert.Equal((int)Direction.South, act.Value.Dir);       // faces the cart to arm it
    }

    [Fact]
    public void Bomb_pushes_an_already_armed_cart_from_the_push_tile()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        ArmCart(sim, cartId: 1, loaderId: 99, loaderPos: new GridPos(8, 4)); // Cargo = Charge, fuse unlit
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(7, 5));            // on push tile
        var act = CartTactics.TryBomb(sim, bot, BotSkill.DynamiteDan);
        Assert.NotNull(act);
        Assert.False(act!.Value.Plant);
        Assert.Equal((int)Direction.East, act.Value.Dir);        // launch east into the monster
    }

    [Fact]
    public void Bomb_skips_when_a_teammate_is_in_the_roll_path()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        sim.AddMiner(9, new GridPos(9, 5));                      // teammate in path
        var bot = sim.AddMiner(3, new GridPos(8, 4));
        Assert.Null(CartTactics.TryBomb(sim, bot, BotSkill.DynamiteDan));
    }

    [Fact]
    public void Bomb_is_gated_to_dynamite_dan_only()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(8, 4));
        Assert.Null(CartTactics.TryBomb(sim, bot, BotSkill.Foreman));
    }
```

`ArmCart` is the test helper added to the file in Task 1 (adjacent loader miner holds a `Detonator` and Uses it onto the cart — the real attach path, `MineCartCargoTests:102-105`). The loader stands off the rail row, so it is never in the roll path.

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: FAIL — `TryBomb` not defined.

- [ ] **Step 3: Add `TryBomb`**

Add to `CartTactics` in `src/Miner49er.Core/AI/CartTactics.cs`:

```csharp
    /// <summary>DynamiteDan: arm an empty cart and launch it as a rolling bomb toward a monster
    /// cluster or a gold seam. Stateless — reads the cart's Cargo each tick. Empty cart → walk to a
    /// perpendicular arm tile and Plant; armed cart → walk to the push tile and shove. Teammate-safe
    /// via FindBestPush. Returns null when no safe opportunity is actionable.</summary>
    public static BotAction? TryBomb(Simulation sim, Miner miner, BotSkill skill)
    {
        if (skill != BotSkill.DynamiteDan) return null;
        if (!MonsterNear(sim, miner)) return null;

        var opt = FindBestPush(sim, miner, forBomb: true);
        if (opt is not { } o) return null;
        var cart = CartAt(sim, o.CartPos);
        if (cart is not { } c) return null;

        if (c.Cargo == CartCargo.Charge)
        {
            if (c.FuseRemaining > 0) return null;                        // already launched/rolling
            if (miner.Pos == o.PushTile) return new BotAction((int)o.Dir); // shove the armed cart
            int step = BotPathfinder.NextDir(sim.Grid, miner.Pos, o.PushTile,
                                             passRock: false, avoidHazards: true, blocked: CartTiles(sim));
            return step == -1 ? (BotAction?)null : new BotAction(step);
        }

        if (c.Cargo == CartCargo.None)
        {
            if (o.ArmTile is not { } arm) return null;                  // no safe place to arm from
            if (miner.Pos == arm)
            {
                var face = DirToward(arm, o.CartPos);                    // face the cart (non-blastable) to arm
                return face is { } f ? new BotAction((int)f, plant: true) : (BotAction?)null;
            }
            int step = BotPathfinder.NextDir(sim.Grid, miner.Pos, arm,
                                             passRock: false, avoidHazards: true, blocked: CartTiles(sim));
            return step == -1 ? (BotAction?)null : new BotAction(step);
        }

        return null;   // lantern-laden cart etc.
    }

    // Direction from `from` to an orthogonally-adjacent `to`, or null if not orthogonally adjacent.
    private static Direction? DirToward(GridPos from, GridPos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        foreach (var d in AllDirs)
        {
            var o = d.ToOffset();
            if (o.X == dx && o.Y == dy) return d;
        }
        return null;
    }
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: PASS.

- [ ] **Step 5: Wire `TryBomb` into `BotBrain`**

In `src/Miner49er.Core/AI/BotBrain.cs`, immediately **after** the squash block inserted in Task 2, add:

```csharp
        // Cart-bomb: DynamiteDan arms & launches a rolling cart-bomb at a monster cluster / gold seam.
        {
            var bomb = CartTactics.TryBomb(sim, miner, Skill);
            if (bomb != null) { _ticksUntilReeval = 0; return bomb.Value; }
        }
```

- [ ] **Step 6: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/AI/CartTactics.cs src/Miner49er.Core/AI/BotBrain.cs src/Miner49er.Core.Tests/AI/CartTacticsTests.cs
git commit -m "feat(carts): DynamiteDan arms & launches rolling cart-bombs

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Lantern-grab (Foreman)

**Files:**
- Modify: `src/Miner49er.Core/AI/CartTactics.cs`
- Modify: `src/Miner49er.Core/AI/BotBrain.cs` (near the cosmetic "listen" block, ~line 189)
- Test: `src/Miner49er.Core.Tests/AI/CartTacticsTests.cs`

**Interfaces:**
- Consumes: helpers (Task 2); `System.Random`.
- Produces: `CartTactics.TryLantern(Simulation sim, Miner miner, BotSkill skill, Random rng) -> BotAction?`.

- [ ] **Step 1: Write the failing lantern tests**

Append to `CartTacticsTests.cs`:

```csharp
    [Fact]
    public void Lantern_foreman_detaches_from_adjacent_lantern_cart()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        MakeLanternCart(sim, cartId: 1, loaderId: 99, loaderPos: new GridPos(8, 6));
        var bot = sim.AddMiner(3, new GridPos(8, 4));           // orthogonally adjacent, empty-handed
        var rng = new System.Random(0);
        // Roll until the ~2% chance fires within a bounded number of attempts.
        BotAction? act = null;
        for (int i = 0; i < 2000 && act == null; i++) act = CartTactics.TryLantern(sim, bot, BotSkill.Foreman, rng);
        Assert.NotNull(act);
        Assert.True(act!.Value.Use);
        Assert.Equal(-1, act.Value.Dir);
    }

    [Fact]
    public void Lantern_is_gated_off_below_foreman()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        MakeLanternCart(sim, cartId: 1, loaderId: 99, loaderPos: new GridPos(8, 6));
        var bot = sim.AddMiner(3, new GridPos(8, 4));
        var rng = new System.Random(0);
        for (int i = 0; i < 2000; i++)
            Assert.Null(CartTactics.TryLantern(sim, bot, BotSkill.Miner, rng));
    }

    [Fact]
    public void Lantern_skips_when_hands_full()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        MakeLanternCart(sim, cartId: 1, loaderId: 99, loaderPos: new GridPos(8, 6));
        var bot = sim.AddMiner(3, new GridPos(8, 4));
        bot.Held = ItemKind.Lantern;                            // already carrying
        var rng = new System.Random(0);
        for (int i = 0; i < 2000; i++)
            Assert.Null(CartTactics.TryLantern(sim, bot, BotSkill.Foreman, rng));
    }
```

`MakeLanternCart` is the Task 1 test helper (adjacent loader miner holds a `Lantern` and Uses it onto the cart). `bot.Held = ItemKind.Lantern` is settable from tests — `Miner.Held` is `internal set` and `Miner49er.Core` has `InternalsVisibleTo` the test assembly (see `MineCartCargoTests` doing the same).

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: FAIL — `TryLantern` not defined.

- [ ] **Step 3: Add `TryLantern`**

Add to `CartTactics` in `src/Miner49er.Core/AI/CartTactics.cs`:

```csharp
    private const double LanternGrabChance = 0.02;   // ~ occasional; cosmetic light for the bot

    /// <summary>Foreman+ : occasionally detach and carry a lantern from an adjacent lantern-carrying
    /// cart (empty-handed only). Cosmetic — bots see the whole map — so kept small and low-priority.</summary>
    public static BotAction? TryLantern(Simulation sim, Miner miner, BotSkill skill, System.Random rng)
    {
        if (skill < BotSkill.Foreman) return null;
        if (miner.Held != null) return null;
        foreach (var c in sim.Carts)
        {
            if (c.Cargo != CartCargo.Lantern) continue;
            if (miner.Pos.ManhattanTo(c.Pos) != 1) continue;         // orthogonally adjacent
            return rng.NextDouble() < LanternGrabChance ? new BotAction(-1, use: true) : (BotAction?)null;
        }
        return null;
    }
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~CartTacticsTests"`
Expected: PASS.

- [ ] **Step 5: Wire `TryLantern` into `BotBrain`**

In `src/Miner49er.Core/AI/BotBrain.cs`, in the cosmetic-listen area, **before** the `if (Skill >= BotSkill.Miner && !escapeUrgentNow)` listen block (~line 192), insert (it reuses the `escapeUrgentNow` local computed just above at ~line 191):

```csharp
        // Foreman+ : occasionally grab a lantern off a nearby cart when nothing urgent is happening.
        if (!escapeUrgentNow)
        {
            var lantern = CartTactics.TryLantern(sim, miner, Skill, _rng);
            if (lantern != null) return lantern.Value;
        }
```

- [ ] **Step 6: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/AI/CartTactics.cs src/Miner49er.Core/AI/BotBrain.cs src/Miner49er.Core.Tests/AI/CartTacticsTests.cs
git commit -m "feat(carts): Foreman bots grab a lantern off a cart

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Tier-wiring integration tests

**Files:**
- Test: `src/Miner49er.Core.Tests/AI/BotBrainTests.cs`

**Interfaces:**
- Consumes: `BotBrain.Think`, the three tactics via the brain, the existing `BotBrainTests` setup helpers.

- [ ] **Step 1: Write tier-wiring tests through `BotBrain.Think`**

Append to `src/Miner49er.Core.Tests/AI/BotBrainTests.cs` (match the file's existing sim/brain setup helpers — build a sim with a rail, a cart, a slime on the rail, and a bot on the push tile, then assert `Think` output per skill):

```csharp
    // Greenhorn never uses carts (routes around; no squash/bomb/lantern).
    [Fact]
    public void Greenhorn_does_not_push_a_cart_to_squash()
    {
        var sim = MakeCartSquashScenario(out var botPos);       // cart + slime lined up, bot on push tile
        var brain = new BotBrain(minerId: 3, BotSkill.Greenhorn, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        // A Greenhorn on the push tile must not step INTO the cart (that would be the squash push).
        Assert.NotEqual((int)Direction.East, act.Dir);
    }

    // Miner squashes: on the push tile, Think returns the shove into the cart.
    [Fact]
    public void Miner_pushes_cart_to_squash_a_monster()
    {
        var sim = MakeCartSquashScenario(out _);
        var brain = new BotBrain(minerId: 3, BotSkill.Miner, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.Equal((int)Direction.East, act.Dir);
        Assert.False(act.Plant);
    }

    // Foreman does not arm bombs (bomb is Dan-only): on the empty-cart arm scenario it must not Plant.
    [Fact]
    public void Foreman_does_not_arm_a_cart_bomb()
    {
        var sim = MakeCartBombScenario();                       // empty cart, bot on the arm tile
        var brain = new BotBrain(minerId: 3, BotSkill.Foreman, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.False(act.Plant);
    }

    // DynamiteDan arms the cart bomb (Plant) on the arm scenario.
    [Fact]
    public void DynamiteDan_arms_a_cart_bomb()
    {
        var sim = MakeCartBombScenario();
        var brain = new BotBrain(minerId: 3, BotSkill.DynamiteDan, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.True(act.Plant);
    }
```

Add the two private scenario builders `MakeCartSquashScenario(out GridPos botPos)` and `MakeCartBombScenario()` to the test class, mirroring the `CartTacticsTests` setup with the verified seams: all-Floor grid, `sim.AddTrack(...)` for an east-west rail on a row, `sim.AddCart(new CartSpec(1, cartPos, Direction.East))`, `sim.AddMonster(1, slimePos, MonsterKind.Slime)` east of the cart, and `sim.AddMiner(3, botPos)` on the push tile (squash) / perpendicular arm tile (bomb). The bot's miner id (3) must match the `BotBrain` `minerId`. `MakeCartBombScenario` leaves the cart empty (Dan arms it).

- [ ] **Step 2: Run the new tests to confirm they fail, then pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo --filter "FullyQualifiedName~BotBrainTests"`
Expected: initially FAIL only if a wiring gap exists; with Tasks 2–4 merged they should PASS. If a test fails, the defect is in the brain wiring or a scenario builder — fix and re-run. (These are pure regression guards; they assert already-implemented behavior.)

- [ ] **Step 3: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS (whole suite green).

- [ ] **Step 4: Commit**

```bash
git add src/Miner49er.Core.Tests/AI/BotBrainTests.cs
git commit -m "test(carts): bot cart tactics tier-wiring integration tests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- Squash (Miner+) → Task 2 + Task 5. ✅
- Cart-bomb (Dan) → Task 3 + Task 5. ✅
- Lantern-grab (Foreman) → Task 4. ✅
- `PredictRoll` + `Simulation` accessors → Task 1. ✅
- Teammate-safety invariant → `MinerInPath` in `PredictRoll` (Task 1), asserted in Task 2 & Task 3 skip tests. ✅
- Determinism → pure integer walk + seeded `Random`; `FindBestPush` total-order tie-break. ✅
- Priority order (explosive-avoidance → squash → bomb → flee → … → lantern) → wiring in Tasks 2/3/4 places squash+bomb before the monster-flee block and lantern near the listen block. ✅
- Tier gates → skill guards in each tactic + Task 5 integration tests. ✅
- Out-of-scope (riding, rival-crush, chain-push, blast-radius, escort) → none implemented. ✅

**Type consistency:** `PushOption` fields, `RollPrediction` fields, and method signatures (`PredictRoll`, `FindBestPush`, `TrySquash`, `TryBomb`, `TryLantern`) are used identically across Tasks 1–5. `BotAction` constructor args and `Direction` ordinals match the referenced code.

**Test seams (verified, not placeholders):** all setup uses the real API confirmed against `MineCartCargoTests.cs` — `new Simulation(grid, new SimConfig())`, `sim.AddTrack(IEnumerable<GridPos>)`, `sim.AddCart(new CartSpec(id, pos, dir))` (live cart), `sim.AddMonster(id, pos, kind)`, `var m = sim.AddMiner(id, pos)` (returns `Miner`; `m.Held`/`m.Facing` settable via InternalsVisibleTo). Cart cargo is set through the real Use path via the `MakeLanternCart` / `ArmCart` test helpers (Task 1). No production API is added for testing.
