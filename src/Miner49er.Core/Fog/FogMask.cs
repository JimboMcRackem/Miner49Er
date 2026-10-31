namespace Miner49er.Core;

/// <summary>Builds a soft-edged fog opacity mask for a window of tiles. Each tile's flat opacity
/// (visible 0, explored dim, unexplored 1) is grown outward by <see cref="GrowTexels"/>, then
/// blurred by <see cref="BlurTexels"/>. Growing by more than the blur reaches means the whole
/// fade lands on the clearer side of an edge: hidden tiles stay fully covered, including after
/// the renderer's bilinear filtering, so softening never reveals anything. Pure C#; no Godot
/// dependency.</summary>
public static class FogMask
{
    public const int TexelsPerTile = 8;
    private const int BlurTexels = 2;
    private const int GrowTexels = BlurTexels + 1;

    /// <summary>Opacity (0-255) of each texel in the window, row by row. The window may extend
    /// past the map; tiles out there count as unexplored. With <paramref name="ignoreVisible"/>
    /// visible tiles are fogged like explored ones, for when the local miner has no view.</summary>
    public static byte[] Build(FogState fog, int tileX, int tileY, int tilesW, int tilesH,
        float dimAlpha, bool ignoreVisible)
    {
        int w = tilesW * TexelsPerTile, h = tilesH * TexelsPerTile;
        var alpha = new float[w * h];
        for (int ty = 0; ty < tilesH; ty++)
            for (int tx = 0; tx < tilesW; tx++)
            {
                var p = new GridPos(tileX + tx, tileY + ty);
                float a = fog.IsVisible(p) && !ignoreVisible ? 0f : fog.IsExplored(p) ? dimAlpha : 1f;
                for (int y = 0; y < TexelsPerTile; y++)
                    for (int x = 0; x < TexelsPerTile; x++)
                        alpha[(ty * TexelsPerTile + y) * w + tx * TexelsPerTile + x] = a;
            }

        var scratch = new float[w * h];
        Pass(alpha, scratch, 1, w, h, w, GrowTexels, grow: true);   // rows
        Pass(scratch, alpha, w, h, w, 1, GrowTexels, grow: true);   // columns
        Pass(alpha, scratch, 1, w, h, w, BlurTexels, grow: false);
        Pass(scratch, alpha, w, h, w, 1, BlurTexels, grow: false);

        var mask = new byte[w * h];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = (byte)MathF.Round(alpha[i] * 255f);
        return mask;
    }

    // One direction of a square filter over ±radius: `step` is the index distance along a line,
    // `length` the line's length, `lines` how many, and `lineStep` the distance between them.
    // Grows with a max filter or blurs with a box filter. Lines clamp at the window's edges.
    private static void Pass(float[] src, float[] dst, int step, int length, int lines, int lineStep,
        int radius, bool grow)
    {
        for (int line = 0; line < lines; line++)
            for (int i = 0; i < length; i++)
            {
                float best = 0f, sum = 0f;
                for (int d = -radius; d <= radius; d++)
                {
                    int j = Math.Clamp(i + d, 0, length - 1);
                    float v = src[line * lineStep + j * step];
                    if (v > best) best = v;
                    sum += v;
                }
                dst[line * lineStep + i * step] = grow ? best : sum / (radius * 2 + 1);
            }
    }
}
