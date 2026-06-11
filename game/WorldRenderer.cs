using Godot;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Draws the tile grid, charges, and explosion flashes with simple
/// colored rectangles. Placeholder art for Phase 1.</summary>
public partial class WorldRenderer : Node2D
{
	private MatchClient _client = null!;
	private readonly System.Collections.Generic.List<(GridPos pos, float life)> _flashes = new();

	private static readonly Color FloorColor = new("2b2b33");
	private static readonly Color RockColor = new("5a4a3a");
	private static readonly Color GoldColor = new("c9a227");
	private static readonly Color ImpermeableColor = new("20242b");
	private static readonly Color ShallowWaterColor = new("2f6f8f");
	private static readonly Color DeepWaterColor = new("16384f");
	private static readonly Color ChargeColor = new("ff5530");
	private static readonly Color FlashColor = new("ffd27f");
	private static readonly Color SpeedItemColor = new("4ad06a");  // green
	private static readonly Color VisionItemColor = new("4ad0d0"); // cyan
	private static readonly Color BlastItemColor = new("e08a2f");  // orange
	private static readonly Color ToolboxColor = new("9a7b4f");  // muted box outline behind a visible item
	private static readonly Color ShimmerColor = new("f5f0c0");  // neutral pale glow — NO kind tell
	private const int ListenItemRevealRadius = 6;                // tiles; Chebyshev radius for sensing through rock

	public void Init(MatchClient client) => _client = client;

	public void AddExplosionFlash(GridPos pos) => _flashes.Add((pos, 0.4f));

	public override void _Process(double delta)
	{
		for (int i = _flashes.Count - 1; i >= 0; i--)
		{
			var f = _flashes[i];
			f.life -= (float)delta;
			if (f.life <= 0) _flashes.RemoveAt(i);
			else _flashes[i] = f;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		int ts = MatchClient.TileSize;

		foreach (var p in grid.Positions())
		{
			var color = grid.Get(p) switch
			{
				TileType.Floor => FloorColor,
				TileType.Rock => RockColor,
				TileType.GoldRock => GoldColor,
				TileType.ImpermeableRock => ImpermeableColor,
				TileType.ShallowWater => ShallowWaterColor,
				TileType.DeepWater => DeepWaterColor,
				_ => FloorColor,
			};
			DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
		}

		foreach (var c in _client.Charges)
		{
			var center = new Vector2(c.X * ts + ts / 2f, c.Y * ts + ts / 2f);
			DrawCircle(center, ts * 0.25f, ChargeColor);
		}

		foreach (var it in _client.Items)
		{
			if (it.Placement == ItemPlacement.Buried) continue; // buried items only show under Listen (below)
			var ip = new GridPos(it.X, it.Y);
			if (!_client.Fog.IsVisible(ip)) continue; // hidden in the dark, like tiles
			var icol = it.Kind switch
			{
				ItemKind.SpeedPotion => SpeedItemColor,
				ItemKind.LongerVision => VisionItemColor,
				ItemKind.BiggerBlast => BlastItemColor,
				_ => SpeedItemColor,
			};
			var icenter = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);
			if (it.Placement == ItemPlacement.Toolbox)
			{
				float bs = ts * 0.5f; // a small box behind the dot for visible toolbox items
				DrawRect(new Rect2(icenter.X - bs / 2f, icenter.Y - bs / 2f, bs, bs), ToolboxColor, false, 2f);
			}
			DrawCircle(icenter, ts * 0.22f, icol);
		}

		// Listen reveal: buried items and decoys shimmer IDENTICALLY (sensed through rock) while the
		// local miner holds Listen, within ListenItemRevealRadius. Neutral color, drawn regardless of fog.
		if (_client.Listening && TryLocalTile(out var lt))
		{
			float t = (float)Time.GetTicksMsec() / 1000f;
			float a = 0.18f + 0.22f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 0.8f)); // ~0.8s pulse
			var shimmer = ShimmerColor with { A = a };
			foreach (var it in _client.Items)
				if (it.Placement == ItemPlacement.Buried && WithinReveal(lt, it.X, it.Y))
					DrawShimmer(it.X, it.Y, shimmer, ts);
			foreach (var d in _client.Decoys)
				if (_client.Grid.Get(d) == TileType.Rock && WithinReveal(lt, d.X, d.Y))
					DrawShimmer(d.X, d.Y, shimmer, ts);
		}

		foreach (var (pos, life) in _flashes)
		{
			var col = FlashColor with { A = Mathf.Clamp(life / 0.4f, 0f, 1f) };
			DrawRect(new Rect2(pos.X * ts, pos.Y * ts, ts, ts), col);
		}
	}

	private bool TryLocalTile(out GridPos tile)
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) { tile = new GridPos(m.X, m.Y); return true; }
		tile = default;
		return false;
	}

	private static bool WithinReveal(GridPos local, int x, int y) =>
		Mathf.Max(Mathf.Abs(local.X - x), Mathf.Abs(local.Y - y)) <= ListenItemRevealRadius;

	private void DrawShimmer(int x, int y, Color col, int ts)
	{
		var c = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
		DrawCircle(c, ts * 0.42f, col); // soft diffuse glow; identical for items and decoys
	}
}
