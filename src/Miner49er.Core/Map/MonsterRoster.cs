using System;

namespace Miner49er.Core;

/// <summary>Light, deterministic roster sizing for an Expedition: a fixed band of
/// 3-5 monsters that grows one step at a time with map area. Pure so the host can
/// pick the count before seeding <see cref="MonsterSpawner"/>.</summary>
public static class MonsterRoster
{
    public const int Min      = 3;
    public const int Max      = 8;
    public const int FloorMax = 10;

    /// <summary>One extra monster per ~384 tiles above the base 24x24 map, clamped to [3, 8].</summary>
    public static int CountFor(int width, int height)
    {
        int area = width * height;
        int extra = Math.Max(0, (area - 24 * 24) / 384);
        return Math.Clamp(Min + extra, Min, Max);
    }

    /// <summary>Area-based count plus a floor difficulty bonus at floors 8 and 14,
    /// hard-capped at <see cref="FloorMax"/>.</summary>
    public static int CountFor(int width, int height, int floor)
    {
        int bonus = (floor >= 8 ? 1 : 0) + (floor >= 14 ? 1 : 0);
        return Math.Clamp(CountFor(width, height) + bonus, Min, FloorMax);
    }
}
