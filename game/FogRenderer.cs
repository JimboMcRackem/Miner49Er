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

	// Set true when the local miner is dead — suppresses all fog so the full map is visible.
	public bool SpectatorMode { get; set; }

	private static readonly Color Unexplored = new(0, 0, 0, 1f);
	private const float DimAlpha = 0.78f;
	// The visible disc is now lit by the light-map, so fog only lightly vignettes it —
	// far below the flat DimAlpha used for explored-but-not-visible tiles.
	private const float InViewDimAlpha = 0.32f;
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
		if (SpectatorMode) return;
		var grid = _client.Grid;
		var fog = _client.Fog;
		int ts = MatchClient.TileSize;

		var (centre, radius) = LocalMinerView();

		if (radius > 0 && radius != _lastRadius)
		{
			_fogGradientTex = BuildFogGradientTex(radius);
			_lastRadius = radius;
		}

		// Only cover on-screen tiles; walking the whole grid (up to ~12k tiles) each
		// fog update was O(map) regardless of zoom. FogRenderer draws in world space
		// at ZIndex -5, so it shares the viewport's canvas transform.
		Rect2 vw = GetViewport().CanvasTransform.AffineInverse() * GetViewportRect();
		int vx0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.X / ts) - 1);
		int vy0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.Y / ts) - 1);
		int vx1 = Mathf.Min(grid.Width  - 1, (int)Mathf.Floor((vw.Position.X + vw.Size.X) / ts) + 1);
		int vy1 = Mathf.Min(grid.Height - 1, (int)Mathf.Floor((vw.Position.Y + vw.Size.Y) / ts) + 1);
		for (int y = vy0; y <= vy1; y++)
			for (int x = vx0; x <= vx1; x++)
			{
				var p = new GridPos(x, y);
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
