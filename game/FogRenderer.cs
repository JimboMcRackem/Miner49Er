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
	private int _lastRadius = -1;

	private static readonly Color Unexplored = new(0, 0, 0, 1f);
	private const float DimAlpha = 0.78f;
	private static readonly Color Dim = new(0, 0, 0, DimAlpha);

	public void Init(MatchClient client) => _client = client;

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
			float alpha = t < edgeFraction ? DimAlpha * Mathf.Pow(t / edgeFraction, 3.0f)
			            : t < 1f           ? DimAlpha
			            :                    0f;
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

		var (centre, radius) = LocalMinerView();

		if (radius > 0 && radius != _lastRadius)
		{
			_fogGradientTex = BuildFogGradientTex(radius);
			_lastRadius = radius;
		}

		foreach (var p in grid.Positions())
		{
			if (fog.IsVisible(p) && radius > 0) continue;
			DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts),
			         fog.IsExplored(p) ? Dim : Unexplored);
		}

		if (radius > 0 && _fogGradientTex != null)
		{
			int gradPx = (radius + 1) * 2 * ts;
			DrawTextureRect(_fogGradientTex,
				new Rect2(centre.X - gradPx / 2f, centre.Y - gradPx / 2f, gradPx, gradPx),
				false);
		}
	}

	private (Vector2 centre, int radius) LocalMinerView()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
				return (_client.MinerVisualPos(m.Id, m.X, m.Y), m.VisionRadius);
		return (Vector2.Zero, 0);
	}
}
