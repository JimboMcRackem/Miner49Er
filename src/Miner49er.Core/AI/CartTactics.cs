using System;
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

    // Nearest live cart on a tile, or null.
    private static CartReadModel? CartAt(Simulation sim, GridPos p)
    {
        foreach (var c in sim.Carts) if (c.Pos == p) return c;
        return null;
    }
}
