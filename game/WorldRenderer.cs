using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Draws non-terrain world objects: charges, items, mold patches, explosion
/// flashes, the Listen shimmer, and LavaVent (no Wang tileset yet).</summary>
public partial class WorldRenderer : Node2D
{
	private MatchClient _client = null!;
	private readonly List<(GridPos pos, float life)> _flashes = new();

	private static readonly Color LavaVentColor  = new("ff7a2a");
	private static readonly Color ChargeColor    = new("ff5530");
	private static readonly Color FlashColor     = new("ffd27f");
	private static readonly Color SpeedItemColor = new("4ad06a");
	private static readonly Color VisionItemColor = new("4ad0d0");
	private static readonly Color BlastItemColor = new("e08a2f");
	private static readonly Color ToolboxColor   = new("9a7b4f");
	private static readonly Color ShimmerColor   = new("f5f0c0");
	private static readonly Color PlankItemColor = new("c8a060");
	private static readonly Color MoldItemColor  = new("8fae4f");
	private static readonly Color MoldColor      = new("6f8f3a");
	private static readonly Color SlimeColor     = new("5fbf4f");
	private static readonly Color GhostColor     = new("dfe8ff");
	private static readonly Color GoatColor      = new("b08050");
	private static readonly Color ExitColor      = new("ffe24a");
	private const int ListenItemRevealRadius = 6;

	private Texture2D? _chargeTex;
	private Texture2D? _toolboxTex;
	private Texture2D? _moldPatchTex;
	private readonly Dictionary<ItemKind, Texture2D> _itemTex = new();

	public void Init(MatchClient client)
	{
		_client = client;
		_chargeTex    = GD.Load<Texture2D>("res://assets/objects/charge.png");
		_toolboxTex   = GD.Load<Texture2D>("res://assets/objects/toolbox.png");
		_moldPatchTex = GD.Load<Texture2D>("res://assets/objects/mold_patch.png");
		LoadItemTex(ItemKind.SpeedPotion,  "res://assets/objects/item_speed.png");
		LoadItemTex(ItemKind.LongerVision, "res://assets/objects/item_vision.png");
		LoadItemTex(ItemKind.BiggerBlast,  "res://assets/objects/item_blast.png");
		LoadItemTex(ItemKind.WaterPlank,   "res://assets/objects/item_plank.png");
		LoadItemTex(ItemKind.SlowMold,     "res://assets/objects/item_mold.png");
	}

	private void LoadItemTex(ItemKind kind, string path)
	{
		var t = GD.Load<Texture2D>(path);
		if (t != null) _itemTex[kind] = t;
	}

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

		// LavaVent: no Wang tileset; TerrainMap handles everything else
		foreach (var p in grid.Positions())
			if (grid.Get(p) == TileType.LavaVent)
				DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), LavaVentColor);

		foreach (var c in _client.Charges)
		{
			var r = new Rect2(c.X * ts, c.Y * ts, ts, ts);
			if (_chargeTex != null)
				DrawTextureRect(_chargeTex, r, false);
			else
				DrawCircle(new Vector2(c.X * ts + ts / 2f, c.Y * ts + ts / 2f), ts * 0.25f, ChargeColor);
		}

		foreach (var it in _client.Items)
		{
			if (it.Placement == ItemPlacement.Buried) continue;
			var ip = new GridPos(it.X, it.Y);
			if (!_client.Fog.IsVisible(ip)) continue;

			var r = new Rect2(it.X * ts, it.Y * ts, ts, ts);
			var icenter = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);

			if (it.Placement == ItemPlacement.Toolbox)
			{
				// Draw container, then item sprite at half-size so player sees what's inside
				if (_toolboxTex != null)
					DrawTextureRect(_toolboxTex, r, false);
				else
				{
					float bs = ts * 0.5f;
					DrawRect(new Rect2(icenter.X - bs / 2f, icenter.Y - bs / 2f, bs, bs), ToolboxColor, false, 2f);
				}
				float hs = ts * 0.5f;
				var inner = new Rect2(icenter.X - hs / 2f, icenter.Y - hs / 2f, hs, hs);
				if (_itemTex.TryGetValue(it.Kind, out var itex2))
					DrawTextureRect(itex2, inner, false);
				else
					DrawCircle(icenter, ts * 0.15f, ItemColor(it.Kind));
			}
			else
			{
				if (_itemTex.TryGetValue(it.Kind, out var itex))
					DrawTextureRect(itex, r, false);
				else
					DrawCircle(icenter, ts * 0.22f, ItemColor(it.Kind));
			}
		}

		foreach (var mo in _client.Molds)
		{
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp)) continue;
			float alpha = Mathf.Clamp((float)mo.RemainingSeconds, 0f, 1f) * 0.5f + 0.25f;
			var r = new Rect2(mo.X * ts, mo.Y * ts, ts, ts);
			if (_moldPatchTex != null)
				DrawTextureRect(_moldPatchTex, r, false, new Color(1f, 1f, 1f, alpha));
			else
				DrawRect(r, MoldColor with { A = alpha });
		}

		if (_client.Listening && TryLocalTile(out var lt))
		{
			float t = (float)Time.GetTicksMsec() / 1000f;
			float a = 0.18f + 0.22f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 0.8f));
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

		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp)) continue;
			var c = _client.MonsterVisualPos(mo.Id, mo.X, mo.Y);
			switch (mo.Kind)
			{
				case MonsterKind.Slime:
					DrawCircle(c, ts * 0.34f, SlimeColor);
					break;
				case MonsterKind.Ghost:
					DrawCircle(c, ts * 0.36f, GhostColor with { A = 0.6f });
					break;
				case MonsterKind.Goat:
					DrawRect(new Rect2(c.X - ts * 0.3f, c.Y - ts * 0.3f, ts * 0.6f, ts * 0.6f), GoatColor);
					break;
			}
		}

		if (_client.EscapeOpen && _client.EscapeTile is { } exit)
		{
			float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * Mathf.Pi * 2f / 0.9f);
			var col = ExitColor with { A = 0.4f + 0.5f * pulse };
			DrawRect(new Rect2(exit.X * ts, exit.Y * ts, ts, ts), col, false, 3f);
		}
	}

	private static Color ItemColor(ItemKind kind) => kind switch
	{
		ItemKind.SpeedPotion  => SpeedItemColor,
		ItemKind.LongerVision => VisionItemColor,
		ItemKind.BiggerBlast  => BlastItemColor,
		ItemKind.WaterPlank   => PlankItemColor,
		ItemKind.SlowMold     => MoldItemColor,
		_                     => SpeedItemColor,
	};

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
		DrawCircle(c, ts * 0.42f, col);
	}
}
