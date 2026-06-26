using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness as a smooth radial gradient centred on the local miner.
/// Unexplored = opaque black, explored-but-not-visible = flat dim, currently visible =
/// a pixel-smooth circular falloff from clear at the centre to dim at the radius edge.
/// Wall occlusion is preserved: tiles excluded from fog.IsVisible() keep their dim/unexplored
/// background; the gradient adds darkness, never brightness.</summary>
public partial class FogRenderer : Node2D
{
	private MatchClient _client = null!;
	private ImageTexture _fogGradientTex = null!;

	private static readonly Color Unexplored = new(0, 0, 0, 1f);
	private const float DimAlpha = 0.6f;
	private static readonly Color Dim = new(0, 0, 0, DimAlpha);

	public void Init(MatchClient client)
	{
		_client = client;
		_fogGradientTex = BuildFogGradientTex();
	}

	// Black circular gradient: alpha 0 at centre → DimAlpha at radius, 0 outside circle.
	// Drawn once at Init; scaled each frame to match the miner's vision radius.
	private static ImageTexture BuildFogGradientTex(int size = 256)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float half = size / 2f;
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			float t = new Vector2(x - half, y - half).Length() / half;
			float alpha = t < 1f ? DimAlpha * Mathf.Pow(t, 3f) : 0f;
			img.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
		}
		return ImageTexture.CreateFromImage(img);
	}

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		var fog = _client.Fog;
		int ts = MatchClient.TileSize;

		// Background pass: dim explored tiles and black out unexplored ones.
		// Visible tiles are skipped here — the gradient handles them below.
		foreach (var p in grid.Positions())
		{
			if (fog.IsVisible(p)) continue;
			DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts),
					 fog.IsExplored(p) ? Dim : Unexplored);
		}

		// Smooth radial gradient centred on the local miner.
		// Sized so t=1 in texture space lands exactly at vision_radius tiles,
		// where gradient alpha (DimAlpha) matches the Dim background seamlessly.
		var (centre, radius) = LocalMinerView();
		if (radius > 0)
		{
			int gradPx = (radius + 1) * 2 * ts;
			DrawTextureRect(_fogGradientTex,
				new Rect2(centre.X - gradPx / 2f, centre.Y - gradPx / 2f, gradPx, gradPx),
				false);
		}
	}

	// Local miner's interpolated pixel position and vision radius, for the radial gradient.
	// Uses MinerVisualPos so the gradient follows smooth movement rather than snapping per tile.
	private (Vector2 centre, int radius) LocalMinerView()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
				return (_client.MinerVisualPos(m.Id, m.X, m.Y), m.VisionRadius);
		return (Vector2.Zero, 0);
	}
}
