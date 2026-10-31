using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness as a smooth radial gradient centred on the local miner.
/// Unexplored = opaque black, explored-but-not-visible = flat dim, currently visible =
/// a pixel-smooth circular falloff from clear at the centre to DimAlpha at the LOS radius,
/// then a 1-tile flat-dark overhang ring that absorbs any sub-pixel seam between the
/// smooth visual position and the integer tile visibility set.</summary>
public partial class FogRenderer : Node2D
{
	private MatchClient _client = null!;
	private ImageTexture? _fogGradientTex;
	private ImageTexture? _maskTex;
	private int _lastRadius = -1;

	// Set true when the local miner is dead — suppresses all fog so the full map is visible.
	public bool SpectatorMode { get; set; }

	private const float DimAlpha = 0.78f;
	// The visible disc is now lit by the light-map, so fog only lightly vignettes it —
	// far below the flat DimAlpha used for explored-but-not-visible tiles.
	private const float InViewDimAlpha = 0.32f;
	private const float VoidMargin = 4096f; // world px blacked out past each map edge; well beyond any view
	private const int MaskMarginTiles = 2;

	public void Init(MatchClient client)
	{
		_client = client;
		// The project default is nearest for pixel art, but both fog textures are soft gradients.
		TextureFilter = TextureFilterEnum.Linear;
	}

	// Gradient: 0 at centre, smoothly darkens to DimAlpha at the LOS radius (edgeFraction),
	// then flat DimAlpha across a 1-tile overhang, then transparent outside the circle.
	// The overhang means any visible tile that drifts slightly past the integer LOS boundary
	// lands under the flat-dark ring and blends with the fog rather than appearing bright.
	private static ImageTexture BuildFogGradientTex(int radius, int size = 256)
	{
		float edgeFraction = (float)radius / (radius + 1);
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float half = size / 2f;
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			float t = new Vector2(x - half, y - half).Length() / half;
			float alpha = t < edgeFraction ? InViewDimAlpha * Mathf.Pow(t / edgeFraction, 3.0f)
			            : t < 1f           ? InViewDimAlpha
			            :                    0f;
			img.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
		}
		return ImageTexture.CreateFromImage(img);
	}

	public override void _Process(double delta)
	{
		if (_client != null && _client.FogDirty)
		{
			_client.ClearFogDirty();
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		var fog = _client.Fog;
		int ts = MatchClient.TileSize;

		// Past the map edge there are no tiles to darken, so without this the default clear
		// colour shows there and glow sprites spill over it.
		float mapW = grid.Width * ts, mapH = grid.Height * ts;
		DrawRect(new Rect2(-VoidMargin, -VoidMargin, mapW + VoidMargin * 2f, VoidMargin), Colors.Black);
		DrawRect(new Rect2(-VoidMargin, mapH, mapW + VoidMargin * 2f, VoidMargin), Colors.Black);
		DrawRect(new Rect2(-VoidMargin, 0f, VoidMargin, mapH), Colors.Black);
		DrawRect(new Rect2(mapW, 0f, VoidMargin, mapH), Colors.Black);

		if (SpectatorMode) return;

		var (centre, radius) = LocalMinerView();

		if (radius > 0 && radius != _lastRadius)
		{
			_fogGradientTex = BuildFogGradientTex(radius);
			_lastRadius = radius;
		}

		// Only cover on-screen tiles; walking the whole grid (up to ~12k tiles) each
		// fog update was O(map) regardless of zoom. FogRenderer draws in world space
		// at ZIndex -5, so it shares the viewport's canvas transform. The window isn't
		// clamped to the map so that map-edge tiles fade out like any others, and its
		// margin holds until the next fog update as the camera scrolls.
		Rect2 vw = GetViewport().CanvasTransform.AffineInverse() * GetViewportRect();
		int vx0 = (int)Mathf.Floor(vw.Position.X / ts) - MaskMarginTiles;
		int vy0 = (int)Mathf.Floor(vw.Position.Y / ts) - MaskMarginTiles;
		int vx1 = (int)Mathf.Floor((vw.Position.X + vw.Size.X) / ts) + MaskMarginTiles;
		int vy1 = (int)Mathf.Floor((vw.Position.Y + vw.Size.Y) / ts) + MaskMarginTiles;
		int tilesW = vx1 - vx0 + 1, tilesH = vy1 - vy0 + 1;
		UpdateMaskTexture(fog, vx0, vy0, tilesW, tilesH, ignoreVisible: radius <= 0);
		DrawTextureRect(_maskTex!, new Rect2(vx0 * ts, vy0 * ts, tilesW * ts, tilesH * ts), false);

		if (radius > 0 && _fogGradientTex != null)
		{
			int gradPx = (radius + 1) * 2 * ts;
			DrawTextureRect(_fogGradientTex,
				new Rect2(centre.X - gradPx / 2f, centre.Y - gradPx / 2f, gradPx, gradPx),
				false);
		}
	}

	private void UpdateMaskTexture(FogState fog, int tileX, int tileY, int tilesW, int tilesH, bool ignoreVisible)
	{
		byte[] mask = FogMask.Build(fog, tileX, tileY, tilesW, tilesH, DimAlpha, ignoreVisible);
		var data = new byte[mask.Length * 2];
		for (int i = 0; i < mask.Length; i++)
			data[i * 2 + 1] = mask[i];   // luminance stays 0: the mask is black with varying alpha

		int w = tilesW * FogMask.TexelsPerTile, h = tilesH * FogMask.TexelsPerTile;
		var img = Image.CreateFromData(w, h, false, Image.Format.La8, data);
		if (_maskTex != null && _maskTex.GetWidth() == w && _maskTex.GetHeight() == h)
			_maskTex.Update(img);
		else
			_maskTex = ImageTexture.CreateFromImage(img);
	}

	private (Vector2 centre, int radius) LocalMinerView()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
				return (_client.MinerVisualPos(m.Id, m.X, m.Y), m.VisionRadius);
		return (Vector2.Zero, 0);
	}
}
