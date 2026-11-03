namespace Miner49er.Core;

/// <summary>Picks what the terrain layer paints under water. Water is drawn separately on its own
/// tiles, so a cell mixing water with anything else paints the water corners as the dry terrain
/// around them. Pure C#; no Godot dependency.</summary>
public static class WaterCorners
{
    public static (int, int, int, int) Dry(int tl, int tr, int bl, int br, int water, int[] priority)
    {
        int best = water, bestCount = 0;
        foreach (int terrain in priority)
        {
            if (terrain == water) continue;
            int count = (tl == terrain ? 1 : 0) + (tr == terrain ? 1 : 0)
                      + (bl == terrain ? 1 : 0) + (br == terrain ? 1 : 0);
            if (count > bestCount) { best = terrain; bestCount = count; }
        }
        return (tl == water ? best : tl, tr == water ? best : tr,
                bl == water ? best : bl, br == water ? best : br);
    }
}
