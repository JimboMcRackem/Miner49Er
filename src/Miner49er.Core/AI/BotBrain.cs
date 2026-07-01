using System;
using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er.Core.AI;

public sealed class BotBrain
{
    public int MinerId { get; }
    public BotSkill Skill { get; }

    private GridPos? _goal;
    private int _ticksUntilReeval;
    private readonly Random _rng;

    private static readonly Direction[] AllDirs =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    public BotBrain(int minerId, BotSkill skill, int seed)
    {
        MinerId = minerId;
        Skill   = skill;
        _rng    = new Random(seed);
        _ticksUntilReeval = 0; // force goal pick on first Think()
    }

    public BotAction Think(Simulation sim, GameMode mode)
    {
        var miner = sim.GetMiner(MinerId);
        if (!miner.Alive) return BotAction.Idle;

        // Let any in-progress activity finish — sending a direction cancels mining.
        if (miner.Activity != ActivityKind.None) return BotAction.Idle;

        _ticksUntilReeval--;
        if (_ticksUntilReeval <= 0)
        {
            _goal = PickGoal(sim, mode, miner);
            _ticksUntilReeval = RevalInterval();
        }

        // Foreman/Dan: use beneficial held items immediately (skips movement this tick)
        if (Skill >= BotSkill.Foreman && miner.Held is ItemKind.SpeedPotion or ItemKind.LongerVision)
            return new BotAction(-1, use: true);

        // Explosive avoidance — skill-gated; Greenhorn is oblivious
        if (Skill > BotSkill.Greenhorn)
        {
            var danger = NearestDangerousCharge(sim, miner);
            if (danger != null)
            {
                var fleeTarget = FleeFrom(sim.Grid, miner.Pos, danger.WallPos);
                if (fleeTarget != null)
                {
                    int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false);
                    if (fleeDir != -1)
                    {
                        _ticksUntilReeval = 0; // re-pick goal once clear
                        return new BotAction(fleeDir);
                    }
                }
            }
        }

        // Monster avoidance: Miner+ flees monsters within 2 tiles
        if (Skill >= BotSkill.Miner)
        {
            var nearestMonster = NearestMonsterPos(sim, miner);
            if (nearestMonster.HasValue && miner.Pos.ChebyshevTo(nearestMonster.Value) <= 2)
            {
                var fleeTarget = FleeFrom(sim.Grid, miner.Pos, nearestMonster.Value);
                if (fleeTarget != null)
                {
                    int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false);
                    if (fleeDir != -1)
                    {
                        _ticksUntilReeval = 0;
                        return new BotAction(fleeDir);
                    }
                }
            }
        }

        // Rock fall avoidance: all skill levels flee a pending fall within 3 tiles.
        foreach (var pf in sim.PendingFalls)
        {
            if (miner.Pos.ChebyshevTo(pf.Pos) > 3) continue;
            var fleeTarget = FleeFrom(sim.Grid, miner.Pos, pf.Pos);
            if (fleeTarget != null)
            {
                int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false);
                if (fleeDir != -1)
                {
                    _ticksUntilReeval = 0;
                    return new BotAction(fleeDir);
                }
            }
            break; // only react to the nearest fall
        }

        if (_goal == null) return BotAction.Idle;

        bool passRock = Skill >= BotSkill.Foreman;
        int dir = BotPathfinder.NextDir(sim.Grid, miner.Pos, _goal.Value, passRock);

        if (dir == -1) { _ticksUntilReeval = 0; return BotAction.Idle; }

        var off      = ((Direction)dir).ToOffset();
        var nextPos  = new GridPos(miner.Pos.X + off.X, miner.Pos.Y + off.Y);
        var nextTile = sim.Grid.InBounds(nextPos) ? sim.Grid.Get(nextPos) : TileType.Rock;
        bool mine = nextTile.IsMinable();

        // Hold the current goal while actively mining so the bot doesn't drift mid-swing
        if (mine)
            _ticksUntilReeval = Math.Max(_ticksUntilReeval, 30);

        bool derby = mode == GameMode.DemolitionDerby;
        bool lms   = mode == GameMode.LastManStanding;
        bool plant = derby
            ? Skill >= BotSkill.Miner && nextTile.IsMinable()
            : lms
                ? (Skill >= BotSkill.Foreman && NearestRivalDist(sim, miner) <= 2 && nextTile.IsMinable())
                    || (Skill == BotSkill.DynamiteDan && GoldClusterAdjacent(sim.Grid, miner.Pos))
                : Skill == BotSkill.DynamiteDan && GoldClusterAdjacent(sim.Grid, miner.Pos);
        bool throwStone = miner.StoneCount > 0 && NearestRivalDist(sim, miner) <= 2
                          && (derby ? Skill >= BotSkill.Miner : Skill == BotSkill.DynamiteDan);

        return new BotAction(dir, mine, plant, throwStone: throwStone);
    }

    // ── Goal selection ─────────────────────────────────────────────────────

    private GridPos? PickGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.DemolitionDerby) return DerbyGoal(sim, miner);
        if (mode == GameMode.LastManStanding && Skill >= BotSkill.Foreman)
        {
            var nearest = NearestRivalPos(sim, miner);
            if (nearest.HasValue && miner.Pos.ChebyshevTo(nearest.Value) <= 6)
                return nearest;
        }
        return Skill switch
        {
            BotSkill.Greenhorn   => RandomFloor(sim.Grid, miner.Pos),
            BotSkill.Miner       => MinerGoal(sim, mode, miner),
            BotSkill.Foreman     => ForemanGoal(sim, mode, miner),
            BotSkill.DynamiteDan => ForemanGoal(sim, mode, miner),
            _ => null,
        };
    }

    private GridPos? DerbyGoal(Simulation sim, Miner miner)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var m in sim.Miners)
        {
            if (m.Id == miner.Id || !m.Alive) continue;
            int d = miner.Pos.ChebyshevTo(m.Pos);
            if (d < bestDist) { bestDist = d; best = m.Pos; }
        }
        return best ?? RandomFloor(sim.Grid, miner.Pos);
    }

    private GridPos? RandomFloor(TileGrid grid, GridPos from)
    {
        for (int i = 0; i < 20; i++)
        {
            var p = new GridPos(from.X + _rng.Next(-6, 7), from.Y + _rng.Next(-6, 7));
            if (grid.InBounds(p) && grid.Get(p).IsWalkable()) return p;
        }
        return null;
    }

    private GridPos? MinerGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.ReachCenter && sim.Center is { } center) return center;

        if (mode == GameMode.TreasureHunt)
        {
            var (a, b) = TreasureAssignment.For(sim.Config.Seed, MinerId);
            foreach (var item in sim.Items)
                if (item.Placement == ItemPlacement.Loose && (item.Kind == a || item.Kind == b))
                    return item.Pos;
        }

        // Nearest GoldRock by Manhattan distance
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var p in sim.Grid.Positions())
        {
            if (sim.Grid.Get(p) != TileType.GoldRock) continue;
            int d = miner.Pos.ManhattanTo(p);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best ?? RandomFloor(sim.Grid, miner.Pos);
    }

    private GridPos? ForemanGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.ReachCenter && sim.Center is { } center) return center;

        if (mode == GameMode.Expedition && sim.EscapeOpen && sim.EscapeTile is { } esc)
            return esc;

        if (mode == GameMode.TreasureHunt) return TreasureGoal(sim, miner);

        // Nearest GoldRock by BFS (plans through walls)
        var gold = new List<GridPos>();
        foreach (var p in sim.Grid.Positions())
            if (sim.Grid.Get(p) == TileType.GoldRock) gold.Add(p);
        return BotPathfinder.Nearest(sim.Grid, miner.Pos, gold, passRock: true)
               ?? RandomFloor(sim.Grid, miner.Pos);
    }

    private GridPos? TreasureGoal(Simulation sim, Miner miner)
    {
        var (a, b) = TreasureAssignment.For(sim.Config.Seed, MinerId);
        foreach (var item in sim.Items)
            if (item.Placement == ItemPlacement.Loose && (item.Kind == a || item.Kind == b))
                return item.Pos;
        foreach (var item in sim.Items)
            if (item.Placement == ItemPlacement.Buried && (item.Kind == a || item.Kind == b))
                return item.Pos;
        return RandomFloor(sim.Grid, miner.Pos);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool GoldClusterAdjacent(TileGrid grid, GridPos pos)
    {
        int count = 0;
        foreach (var d in AllDirs)
        {
            var off = d.ToOffset();
            var nb  = new GridPos(pos.X + off.X, pos.Y + off.Y);
            if (grid.InBounds(nb) && grid.Get(nb) == TileType.GoldRock) count++;
        }
        return count >= 3;
    }

    private static int NearestRivalDist(Simulation sim, Miner self)
    {
        int min = int.MaxValue;
        foreach (var m in sim.Miners)
        {
            if (m.Id == self.Id || !m.Alive) continue;
            int d = self.Pos.ChebyshevTo(m.Pos);
            if (d < min) min = d;
        }
        return min;
    }

    private static GridPos? NearestRivalPos(Simulation sim, Miner self)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var m in sim.Miners)
        {
            if (m.Id == self.Id || !m.Alive) continue;
            int d = self.Pos.ChebyshevTo(m.Pos);
            if (d < bestDist) { bestDist = d; best = m.Pos; }
        }
        return best;
    }

    private static GridPos? NearestMonsterPos(Simulation sim, Miner self)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var mo in sim.Monsters)
        {
            if (!mo.Alive) continue;
            int d = self.Pos.ChebyshevTo(mo.Pos);
            if (d < bestDist) { bestDist = d; best = mo.Pos; }
        }
        return best;
    }

    // Override the bot's next goal to the escape tile for ~4 s (120 ticks at 30 Hz).
    // The bot finishes any in-progress activity first, then heads for the exit.
    public void ForceEscape(GridPos escapeTile)
    {
        _goal = escapeTile;
        _ticksUntilReeval = 120;
    }

    private int RevalInterval() => Skill switch
    {
        BotSkill.Greenhorn   => 30,
        BotSkill.Miner       => 15,
        BotSkill.Foreman     => 7,
        BotSkill.DynamiteDan => 3,
        _ => 15,
    };

    // Returns the nearest charge worth fleeing, or null if none.
    // Miner: reacts within 1 tile, fuse ≤ 1.5 s.
    // Foreman: 2 tiles, fuse ≤ 2.5 s.
    // Dan: 3 tiles, any fuse, but ignores own charges beyond adjacent.
    private Charge? NearestDangerousCharge(Simulation sim, Miner miner)
    {
        int distThreshold = Skill switch
        {
            BotSkill.Miner       => 1,
            BotSkill.Foreman     => 2,
            BotSkill.DynamiteDan => 3,
            _ => 0,
        };
        double fuseThreshold = Skill switch
        {
            BotSkill.Miner       => 1.5,
            BotSkill.Foreman     => 2.5,
            BotSkill.DynamiteDan => double.MaxValue, // Dan reacts immediately
            _ => 0,
        };

        Charge? nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var charge in sim.Charges)
        {
            // Dan ignores charges it planted unless they're right next door
            if (Skill == BotSkill.DynamiteDan && charge.OwnerId == MinerId
                && miner.Pos.ChebyshevTo(charge.WallPos) > 1)
                continue;

            int d = miner.Pos.ChebyshevTo(charge.WallPos);
            if (d <= distThreshold && charge.FuseRemaining <= fuseThreshold && d < nearestDist)
            {
                nearestDist = d;
                nearest = charge;
            }
        }
        return nearest;
    }

    // Finds a walkable floor tile several steps away from the danger position.
    private static GridPos? FleeFrom(TileGrid grid, GridPos pos, GridPos danger)
    {
        int dx = pos.X - danger.X;
        int dy = pos.Y - danger.Y;
        int nx = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int ny = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

        for (int steps = 5; steps >= 2; steps--)
        {
            var candidates = new[]
            {
                new GridPos(pos.X + nx * steps, pos.Y + ny * steps),
                new GridPos(pos.X + nx * steps, pos.Y),
                new GridPos(pos.X,               pos.Y + ny * steps),
            };
            foreach (var c in candidates)
                if (grid.InBounds(c) && grid.Get(c).IsWalkable()) return c;
        }
        return null;
    }
}
