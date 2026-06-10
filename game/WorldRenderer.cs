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
			DrawCircle(icenter, ts * 0.22f, icol);
		}

		foreach (var (pos, life) in _flashes)
		{
			var col = FlashColor with { A = Mathf.Clamp(life / 0.4f, 0f, 1f) };
			DrawRect(new Rect2(pos.X * ts, pos.Y * ts, ts, ts), col);
		}
	}
}
