namespace Miner49er.Core;

public enum FloorDetailKind { Crack, Pebbles, Stain, Damp }

/// <summary>One block of a stamp, in world pixels from the stamp's anchor.</summary>
public readonly record struct DetailPixel(int X, int Y, int W, int H, bool Light);

public sealed record FloorStamp(FloorDetailKind Kind, DetailPixel[] Pixels);

/// <summary>Cosmetic floor markings chosen from a tile's position, so every client sees the same
/// floor. Stamps are anchored inside a tile but spread across its neighbours, which breaks up
/// the grid. Pure C#; no Godot dependency.</summary>
public static class FloorDetail
{
    private const int TileSize = 32;
    private const int VariantsPerKind = 6;
    private const uint Scarcity = 3; // about one tile in this many gets a stamp

    private static readonly FloorDetailKind[] Kinds = Enum.GetValues<FloorDetailKind>();
    private static readonly FloorStamp[] Stamps = BuildStamps();

    public static bool TryPick(int x, int y, out FloorStamp? stamp, out int offsetX, out int offsetY)
    {
        uint h = Mix((uint)(x * 73856093 ^ y * 19349663));
        stamp = null;
        offsetX = offsetY = 0;
        if (h % Scarcity != 0) return false;

        uint r = Mix(h);
        stamp = Stamps[r % (uint)Stamps.Length];
        offsetX = (int)((r >> 8) % TileSize);
        offsetY = (int)((r >> 16) % TileSize);
        return true;
    }

    // The raw position hash has weak low bits, and those pick both whether and which.
    private static uint Mix(uint h)
    {
        h ^= h >> 13;
        h *= 0x5bd1e995u;
        return h ^ (h >> 15);
    }

    private static FloorStamp[] BuildStamps()
    {
        var rng = new Random(4949);
        var stamps = new List<FloorStamp>();
        foreach (var kind in Kinds)
            for (int i = 0; i < VariantsPerKind; i++)
                stamps.Add(new FloorStamp(kind, kind switch
                {
                    FloorDetailKind.Crack => Crack(rng),
                    FloorDetailKind.Pebbles => Pebbles(rng),
                    _ => Blob(rng),
                }));
        return stamps.ToArray();
    }

    // A wandering hairline, drawn in short steps so it stays on the pixel grid.
    private static DetailPixel[] Crack(Random rng)
    {
        var pixels = new List<DetailPixel>();
        int x = 0, y = 0;
        int dx = rng.Next(2) * 2 - 1;
        int length = rng.Next(5, 11);
        for (int i = 0; i < length; i++)
        {
            pixels.Add(new DetailPixel(x, y, 3, 1, false));
            x += dx * 3;
            y += rng.Next(-1, 2);
        }
        return pixels.ToArray();
    }

    // Small stones, each with a lit top and a shadow beneath it.
    private static DetailPixel[] Pebbles(Random rng)
    {
        var pixels = new List<DetailPixel>();
        int count = rng.Next(2, 6);
        for (int i = 0; i < count; i++)
        {
            int x = rng.Next(-8, 9), y = rng.Next(-6, 7), w = rng.Next(2, 4);
            pixels.Add(new DetailPixel(x, y + 1, w, 1, false));
            pixels.Add(new DetailPixel(x, y, w, 1, true));
        }
        return pixels.ToArray();
    }

    // An irregular patch: solid in the middle with a broken edge, since scattered blocks read as noise.
    private static DetailPixel[] Blob(Random rng)
    {
        var pixels = new List<DetailPixel>();
        int radius = rng.Next(10, 21);
        for (int y = -radius; y <= radius; y += 4)
            for (int x = -radius; x <= radius; x += 4)
            {
                float t = MathF.Sqrt(x * x + y * y) / radius;
                if (rng.NextDouble() > 3f - 3f * t) continue;
                pixels.Add(new DetailPixel(x, y, 4, 4, false));
            }
        return pixels.ToArray();
    }
}
