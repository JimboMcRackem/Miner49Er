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

        _ticksUntilReeval--;
        if (_ticksUntilReeval <= 0)
        {
            _goal = PickGoal(sim, mode, miner);
            _ticksUntilReeval = RevalInterval();
        }

        // Foreman/Dan: use beneficial held items immediately (skips movement this tick)
        if (Skill >= BotSkill.Foreman && miner.Held is ItemKind.SpeedPotion or ItemKind.LongerVision)
            return new BotAction(-1, use: true);

        if (_goal == null) return BotAction.Idle;

        bool passRock = Skill >= BotSkill.Foreman;
        int dir = BotPathfinder.NextDir(sim.Grid, miner.Pos, _goal.Value, passRock);

        if (dir == -1) { _ticksUntilReeval = 0; return BotAction.Idle; }

        var off      = ((Direction)dir).ToOffset();
        var nextPos  = new GridPos(miner.Pos.X + off.X, miner.Pos.Y + off.Y);
        var nextTile = sim.Grid.InBounds(nextPos) ? sim.Grid.Get(nextPos) : TileType.Rock;
        bool mine = nextTile.IsMinable();

        bool derby = mode == GameMode.DemolitionDerby;
        bool plant = derby
            ? Skill >= BotSkill.Miner && nextTile.IsMinable()
            : Skill == BotSkill.DynamiteDan && GoldClusterAdjacent(sim.Grid, miner.Pos);
        bool throwStone = miner.StoneCount > 0 && NearestRivalDist(sim, miner) <= 2
                          && (derby ? Skill >= BotSkill.Miner : Skill == BotSkill.DynamiteDan);

        return new BotAction(dir, mine, plant, throwStone: throwStone);
    }

    // ── Goal selection ─────────────────────────────────────────────────────

    private GridPos? PickGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.DemolitionDerby) return DerbyGoal(sim, miner);
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
            var p = new GridPos(from.X + _rng.Next(-10, 11), from.Y + _rng.Next(-10, 11));
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

    private int RevalInterval() => Skill switch
    {
        BotSkill.Greenhorn   => 30,
        BotSkill.Miner       => 15,
        BotSkill.Foreman     => 7,
        BotSkill.DynamiteDan => 3,
        _ => 15,
    };
}
