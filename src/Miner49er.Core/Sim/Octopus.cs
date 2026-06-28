using System;
using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Mobile boss entity for the Floor 21 arena. Pathfinds toward the nearest
/// alive miner one cardinal step per cooldown tick. Speed is terrain-dependent:
/// slow on dry floor, medium in shallow water, fast in deep water. Immune to drowning.</summary>
public sealed class Octopus
{
    public GridPos Pos          { get; private set; }
    public double  MoveCooldown { get; private set; }

    public const double LandCooldown    = 2.0;   // seconds/move on Floor
    public const double ShallowCooldown = 1.0;   // seconds/move on ShallowWater
    public const double DeepCooldown    = 0.4;   // seconds/move on DeepWater

    public Octopus(GridPos pos)
    {
        Pos          = pos;
        MoveCooldown = LandCooldown;
    }

    public void Advance(double dt, TileGrid grid, IEnumerable<Miner> miners)
    {
        MoveCooldown -= dt;
        if (MoveCooldown > 0) return;

        GridPos? target = NearestMiner(miners);
        if (target is { } t && t != Pos)
        {
            int dx = t.X - Pos.X;
            int dy = t.Y - Pos.Y;
            bool primaryX = Math.Abs(dx) >= Math.Abs(dy);

            GridPos primary   = primaryX && dx != 0 ? new GridPos(Pos.X + Math.Sign(dx), Pos.Y)
                              : dy != 0             ? new GridPos(Pos.X, Pos.Y + Math.Sign(dy))
                              :                       Pos;
            GridPos secondary = primaryX && dy != 0 ? new GridPos(Pos.X, Pos.Y + Math.Sign(dy))
                              : dx != 0             ? new GridPos(Pos.X + Math.Sign(dx), Pos.Y)
                              :                       Pos;

            if (Passable(primary, grid))        Pos = primary;
            else if (Passable(secondary, grid)) Pos = secondary;
        }

        MoveCooldown = CooldownFor(grid.Get(Pos));
    }

    private GridPos? NearestMiner(IEnumerable<Miner> miners)
    {
        GridPos? best = null;
        int bestDist  = int.MaxValue;
        foreach (var m in miners)
        {
            if (!m.Alive) continue;
            int d = Math.Abs(m.Pos.X - Pos.X) + Math.Abs(m.Pos.Y - Pos.Y);
            if (d < bestDist) { bestDist = d; best = m.Pos; }
        }
        return best;
    }

    private static bool Passable(GridPos p, TileGrid grid) =>
        grid.InBounds(p) &&
        grid.Get(p) is TileType.Floor or TileType.ShallowWater
                     or TileType.DeepWater or TileType.Plank;

    public static double CooldownFor(TileType t) => t switch
    {
        TileType.DeepWater    => DeepCooldown,
        TileType.ShallowWater => ShallowCooldown,
        _                     => LandCooldown,
    };
}
