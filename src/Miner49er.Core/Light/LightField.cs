using System;
using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Per-tile light intensity from a single source, respecting wall occlusion.
/// Reuses <see cref="Visibility.Compute"/> for the shadowcast — walls that
/// <see cref="TileTypeExtensions.BlocksSight"/> block light exactly as they block sight —
/// then applies a linear radial falloff: 1.0 at the origin down to 0.0 at the radius edge.
/// Pure and deterministic, like <see cref="Visibility"/>; used by the client light-map.</summary>
public static class LightField
{
    public static Dictionary<GridPos, float> Compute(TileGrid grid, GridPos origin, int radius)
    {
        var field = new Dictionary<GridPos, float>();
        if (radius <= 0) return field;

        foreach (var p in Visibility.Compute(grid, origin, radius))
        {
            int dx = p.X - origin.X, dy = p.Y - origin.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float intensity = 1f - dist / radius;   // 1 at centre, 0 at the edge
            if (intensity > 0f) field[p] = intensity;
        }
        return field;
    }
}
