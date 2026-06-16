using System;

namespace Miner49er.Core;

/// <summary>Light, deterministic roster sizing for an Expedition: a fixed band of
/// 3-5 monsters that grows one step at a time with map area. Pure so the host can
/// pick the count before seeding <see cref="MonsterSpawner"/>.</summary>
public static class MonsterRoster
{
    public const int Min = 3;
    public const int Max = 5;

    /// <summary>One extra monster per ~512 tiles above the base 24x24 map, clamped to [3, 5].</summary>
    public static int CountFor(int width, int height)
    {
        int area = width * height;
        int extra = Math.Max(0, (area - 24 * 24) / 512);
        return Math.Clamp(Min + extra, Min, Max);
    }
}
