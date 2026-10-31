using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Accumulates light intensity from many sources into one per-tile field.
/// Overlapping lights sum; <see cref="SampleClamped"/> caps the total at 1.0. The client
/// renderer rebuilds it each frame: <see cref="Clear"/> then one <see cref="AddLight"/>
/// per source. Pure C#; no Godot dependency.</summary>
public sealed class LightMap
{
    private readonly Dictionary<GridPos, float> _acc = new();

    public void Clear() => _acc.Clear();

    public void AddLight(TileGrid grid, GridPos origin, int radius, float scale = 1f)
    {
        foreach (var (p, intensity) in LightField.Compute(grid, origin, radius))
            _acc[p] = (_acc.TryGetValue(p, out var cur) ? cur : 0f) + intensity * scale;
    }

    /// <summary>Accumulate a precomputed <see cref="LightField"/> with a flicker scale, without
    /// recomputing the shadowcast. Lets callers cache the (static) field of an unmoving source
    /// once and re-add it each frame with only the per-frame flicker varying. The parameter is
    /// the concrete <see cref="Dictionary{TKey,TValue}"/> so the foreach uses its struct
    /// enumerator — no per-frame heap allocation on the render hot path.</summary>
    public void AddField(Dictionary<GridPos, float> field, float scale = 1f)
    {
        foreach (var (p, intensity) in field)
            _acc[p] = (_acc.TryGetValue(p, out var cur) ? cur : 0f) + intensity * scale;
    }

    /// <summary>Total light at a tile, clamped to [0,1]. Unlit tiles return 0.</summary>
    public float SampleClamped(GridPos p) =>
        _acc.TryGetValue(p, out var v) ? (v > 1f ? 1f : v) : 0f;

    /// <summary>Light at the top-left corner of tile (cx, cy): the average of the visible tiles
    /// touching it, so shading blends smoothly across tiles. Tiles under fog are left out, or
    /// light they hold would leak into the view. A corner no visible tile touches returns 0.</summary>
    public float SampleCorner(int cx, int cy, FogState fog)
    {
        float sum = 0f;
        int n = 0;
        for (int y = cy - 1; y <= cy; y++)
            for (int x = cx - 1; x <= cx; x++)
            {
                var p = new GridPos(x, y);
                if (!fog.IsVisible(p)) continue;
                sum += SampleClamped(p);
                n++;
            }
        return n == 0 ? 0f : sum / n;
    }
}
