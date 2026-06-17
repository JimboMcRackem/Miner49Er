using System;
using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Stationary boss entity. Four arms sweep ±45° arcs at 30°/sec.
/// Any miner on a danger tile is crushed each tick.</summary>
public sealed class Octopus
{
    public GridPos    Pos  { get; }
    public OctopusArm[] Arms { get; }

    public Octopus(GridPos pos)
    {
        Pos  = pos;
        Arms = new[]
        {
            new OctopusArm(  0.0,  1),   // North  — starts at -45°, sweeps to +45°
            new OctopusArm( 90.0, -1),   // East   — offset so arms interleave
            new OctopusArm(180.0,  1),   // South
            new OctopusArm(270.0, -1),   // West
        };
        // Stagger initial angles by 22.5° each so adjacent arms don't simultaneously
        // sweep the same region (quarter cycle of the 90° arc per arm).
        for (int i = 1; i < Arms.Length; i++)
        {
            double offset = i * (OctopusArm.ArcHalfWidth / 2.0);   // 22.5° per arm
            Arms[i].CurrentAngle += Arms[i].SwingDir > 0 ? offset : -offset;
        }
    }

    public void Advance(double dt)
    {
        foreach (var arm in Arms) arm.Advance(dt);
    }

    /// <summary>Up to Length tiles along each arm's current direction.
    /// Stops at the grid boundary. Never includes the octopus center itself.</summary>
    public IEnumerable<GridPos> DangerTiles(TileGrid grid)
    {
        foreach (var arm in Arms)
            foreach (var p in ArmTiles(Pos, arm.CurrentAngle, OctopusArm.Length, grid))
                yield return p;
    }

    // Walks <length> unique grid cells along the direction given by angleDeg
    // (0=North, 90=East, 180=South, 270=West — Y increases downward).
    private static IEnumerable<GridPos> ArmTiles(
        GridPos origin, double angleDeg, int length, TileGrid grid)
    {
        double rad  = angleDeg * Math.PI / 180.0;
        double dirX =  Math.Sin(rad);   // East component  (+x)
        double dirY = -Math.Cos(rad);   // South component (+y, Y increases down)

        var seen = new HashSet<GridPos>();
        // Step at sub-tile resolution; collect the first <length> unique cells.
        for (double t = 0.4; seen.Count < length; t += 0.4)
        {
            if (t > length * 3.0) break;
            var p = new GridPos(
                origin.X + (int)Math.Round(dirX * t),
                origin.Y + (int)Math.Round(dirY * t));
            if (!grid.InBounds(p)) break;
            if (p == origin) continue;
            if (seen.Add(p)) yield return p;
        }
    }
}
