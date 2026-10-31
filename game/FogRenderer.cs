using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness from a soft mask (see <see cref="FogMask"/>). Unexplored = opaque
/// black, explored-but-not-visible = flat dim, currently visible = a smooth falloff centred on
/// the local miner that holds at InViewDimAlpha past the LOS radius.</summary>
public partial class FogRenderer : Node2D
{
	private MatchClient _client = null!;
	private ImageTexture? _maskTex;

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
		// The project default is nearest for pixel art, but the fog mask is a soft gradient.
		TextureFilter = TextureFilterEnum.Linear;
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
		FogView? view = radius > 0 ? new FogView(centre.X / ts, centre.Y / ts, radius, InViewDimAlpha) : null;
		UpdateMaskTexture(fog, vx0, vy0, tilesW, tilesH, view);
		DrawTextureRect(_maskTex!, new Rect2(vx0 * ts, vy0 * ts, tilesW * ts, tilesH * ts), false);
	}

	private void UpdateMaskTexture(FogState fog, int tileX, int tileY, int tilesW, int tilesH, FogView? view)
	{
		byte[] mask = FogMask.Build(fog, tileX, tileY, tilesW, tilesH, DimAlpha, ignoreVisible: view == null, view);
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
