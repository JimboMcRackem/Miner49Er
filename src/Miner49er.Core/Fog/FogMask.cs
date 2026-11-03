namespace Miner49er.Core;

/// <summary>A vignette over the local miner's view: centre and radius in tiles, and the
/// opacity it reaches at that radius.</summary>
public readonly record struct FogView(float X, float Y, float Radius, float Dim);

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
        float dimAlpha, bool ignoreVisible, FogView? view = null)
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

        if (view is { } v)
        {
            ApplyVignette(alpha, w, h, tileX, tileY, v);
            ApplyRoundEdge(alpha, NearUnexplored(fog, tileX, tileY, tilesW, tilesH, ignoreVisible),
                w, h, tileX, tileY, v);
        }

        var mask = new byte[w * h];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = (byte)MathF.Round(alpha[i] * 255f);
        return mask;
    }

    // Fraction of the view radius that stays fully clear before the vignette starts to darken.
    private const float VignetteClear = 0.5f;

    // Clear out to VignetteClear, smoothly darkens to the edge of the view, then holds that level
    // everywhere beyond it so the vignette has no rim. Reaching the dim level at the radius is what
    // makes the view read as a circle rather than as the tile-shaped visible set. Fog already
    // darker than the vignette is left as it is.
    private static void ApplyVignette(float[] alpha, int w, int h, int tileX, int tileY, FogView view)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = tileX + (x + 0.5f) / TexelsPerTile - view.X;
                float dy = tileY + (y + 0.5f) / TexelsPerTile - view.Y;
                float t = Math.Clamp((MathF.Sqrt(dx * dx + dy * dy) / view.Radius - VignetteClear)
                    / (1f - VignetteClear), 0f, 1f);
                float a = view.Dim * t * t * (3f - 2f * t);
                if (a > alpha[y * w + x]) alpha[y * w + x] = a;
            }
    }

    // Where the view runs into never-seen tiles, fog fades to fully opaque around the view radius,
    // hiding the tile-shaped notches of the visible set behind a circle. The first never-seen tiles
    // come as close as about R - 0.5 (their near corner), so the fade starts a tile inside the radius
    // and is opaque just past it, at the cost of dimming the outermost ring next to the unknown.
    // Offsets are from the view radius in tiles. Only ever darkens, so it can't reveal anything.
    private const float RoundEdgeStart = -1f, RoundEdgeEnd = 0.4f;
    // How far "near never-seen" reaches inward: grown by two tiles, then softened, so the round edge
    // fades out smoothly where the view's border turns from never-seen to remembered.
    private const int NearGrowTexels = 2 * TexelsPerTile, NearBlurTexels = 6;

    private static void ApplyRoundEdge(float[] alpha, float[] near, int w, int h, int tileX, int tileY,
        FogView view)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (near[i] <= 0f) continue;
                float dx = tileX + (x + 0.5f) / TexelsPerTile - view.X;
                float dy = tileY + (y + 0.5f) / TexelsPerTile - view.Y;
                float t = Math.Clamp((MathF.Sqrt(dx * dx + dy * dy) - view.Radius - RoundEdgeStart)
                    / (RoundEdgeEnd - RoundEdgeStart), 0f, 1f);
                float a = near[i] * t * t * (3f - 2f * t);
                if (a > alpha[i]) alpha[i] = a;
            }
    }

    // 1 on and within about a tile and a quarter of never-seen tiles, falling to 0 near three tiles away.
    private static float[] NearUnexplored(FogState fog, int tileX, int tileY, int tilesW, int tilesH,
        bool ignoreVisible)
    {
        int w = tilesW * TexelsPerTile, h = tilesH * TexelsPerTile;
        var near = new float[w * h];
        for (int ty = 0; ty < tilesH; ty++)
            for (int tx = 0; tx < tilesW; tx++)
            {
                var p = new GridPos(tileX + tx, tileY + ty);
                if ((fog.IsVisible(p) && !ignoreVisible) || fog.IsExplored(p)) continue;
                for (int y = 0; y < TexelsPerTile; y++)
                    for (int x = 0; x < TexelsPerTile; x++)
                        near[(ty * TexelsPerTile + y) * w + tx * TexelsPerTile + x] = 1f;
            }
        var scratch = new float[w * h];
        Pass(near, scratch, 1, w, h, w, NearGrowTexels, grow: true);
        Pass(scratch, near, w, h, w, 1, NearGrowTexels, grow: true);
        Pass(near, scratch, 1, w, h, w, NearBlurTexels, grow: false);
        Pass(scratch, near, w, h, w, 1, NearBlurTexels, grow: false);
        return near;
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
