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

	private static readonly Color CrackColor     = new Color(0.15f, 0.08f, 0.0f, 0.70f);
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
	private static readonly Color SlimeColor        = new("5fbf4f");
	private static readonly Color SlimeOutlineColor = new("3a8f2a");
	private static readonly Color GhostColor        = new("dfe8ff");
	private static readonly Color GoatColor         = new("b08050");
	private static readonly Color GoatHornColor     = new("6a4828");
	private static readonly Color ExitColor          = new("ffe24a");
	private static readonly Color LadderColor        = new Color(0.68f, 0.52f, 0.28f, 0.50f);
	private static readonly Color LadderLockedColor  = new Color(0.4f, 0.4f, 0.4f, 0.40f);
	private static readonly Color OctopusColor       = new Color(0.8f, 0.1f, 0.7f, 0.85f);
	private static readonly Color OctopusArmColor    = new Color(0.9f, 0.2f, 0.6f, 0.45f);
	private static readonly Color ChestColor         = new Color(0.9f, 0.8f, 0.1f, 0.95f);
	private static readonly Color LanternItemColor   = new("ffe090");
	private static readonly Color LanternGlowColor  = new Color(1f, 0.9f, 0.3f, 0.18f);
	private const int LanternRadius = 3;
	private const int ListenItemRevealRadius = 6;

	private Texture2D? _chargeTex;
	private Texture2D? _toolboxTex;
	private Texture2D? _moldPatchTex;
	private Texture2D? _goldRockTex;
	private Texture2D? _plankTex;
	private Texture2D? _lavaVentTex;
	private readonly Dictionary<ItemKind, Texture2D> _itemTex = new();

	public void Init(MatchClient client)
	{
		_client = client;
		_chargeTex    = GD.Load<Texture2D>("res://assets/objects/charge.png");
		_toolboxTex   = GD.Load<Texture2D>("res://assets/objects/toolbox.png");
		_moldPatchTex = GD.Load<Texture2D>("res://assets/objects/mold_patch.png");
		_goldRockTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_6.png");
		_plankTex     = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_1.png");
		_lavaVentTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_5.png");
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

		// Single-pass tile overlays on top of TerrainMap (FogRenderer at ZIndex -5 covers these naturally).
		var deepOverlay = new Color(0.0f, 0.05f, 0.35f, 0.55f);
		foreach (var p in grid.Positions())
		{
			var r = new Rect2(p.X * ts, p.Y * ts, ts, ts);
			switch (grid.Get(p))
			{
				case TileType.GoldRock:
					if (_goldRockTex != null) DrawTextureRect(_goldRockTex, r, false);
					break;
				case TileType.Plank:
					if (_plankTex != null) DrawTextureRect(_plankTex, r, false);
					break;
				case TileType.LavaVent:
					if (_lavaVentTex != null) DrawTextureRect(_lavaVentTex, r, false);
					else DrawRect(r, LavaVentColor);
					break;
				case TileType.DeepWater:
					DrawRect(r, deepOverlay);
					break;
				case TileType.Cracked:
				{
					// Single hairline crack: two segments with a jog at the midpoint
					float x0 = p.X * ts, y0 = p.Y * ts;
					var ca = new Vector2(x0 + ts * 0.45f, y0 + ts * 0.10f);
					var cb = new Vector2(x0 + ts * 0.52f, y0 + ts * 0.52f);
					var cc = new Vector2(x0 + ts * 0.60f, y0 + ts * 0.90f);
					DrawLine(ca, cb, CrackColor, 1.5f);
					DrawLine(cb, cc, CrackColor, 1.5f);
					break;
				}
				case TileType.Crumbling:
				{
					// X crack: two diagonals meeting at a shifted centre for a jagged feel
					float x0 = p.X * ts, y0 = p.Y * ts;
					var cen = new Vector2(x0 + ts * 0.52f, y0 + ts * 0.48f);
					DrawLine(new Vector2(x0 + ts * 0.10f, y0 + ts * 0.10f), cen, CrackColor, 1.5f);
					DrawLine(cen, new Vector2(x0 + ts * 0.90f, y0 + ts * 0.90f), CrackColor, 1.5f);
					DrawLine(new Vector2(x0 + ts * 0.90f, y0 + ts * 0.10f), cen, CrackColor, 1.5f);
					DrawLine(cen, new Vector2(x0 + ts * 0.10f, y0 + ts * 0.90f), CrackColor, 1.5f);
					break;
				}
			}
		}

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

			if (it.Kind == ItemKind.LifePotion)
			{
				var font = ThemeDB.FallbackFont;
				int fontSize = ts * 2 / 3;
				DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
					"♥", HorizontalAlignment.Center, -1, fontSize, new Color(1f, 0.15f, 0.15f, 0.95f));
				continue;
			}
			if (it.Kind == ItemKind.BossChest)
			{
				var font = ThemeDB.FallbackFont;
				int fontSize = ts * 2 / 3;
				DrawRect(r, new Color(0.9f, 0.75f, 0.1f, 0.9f));
				DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
					"★", HorizontalAlignment.Center, -1, fontSize, Colors.Black);
				continue;
			}
			if (it.Kind == ItemKind.Chest)
			{
				var font = ThemeDB.FallbackFont;
				int fontSize = ts * 2 / 3;
				DrawRect(r, ChestColor);
				DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
					"♦", HorizontalAlignment.Center, -1, fontSize, Colors.Black);
				continue;
			}

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
				else if (it.Kind == ItemKind.Lantern)
				{
					DrawCircle(icenter, ts * 0.15f, LanternItemColor);
					DrawCircle(icenter, ts * 0.15f, new Color(0.5f, 0.4f, 0.1f), false, 1.5f);
				}
				else
					DrawCircle(icenter, ts * 0.15f, ItemColor(it.Kind));
			}
			else
			{
				if (_itemTex.TryGetValue(it.Kind, out var itex))
					DrawTextureRect(itex, r, false);
				else if (it.Kind == ItemKind.Lantern)
				{
					DrawCircle(icenter, ts * 0.22f, LanternItemColor);
					DrawCircle(icenter, ts * 0.22f, new Color(0.5f, 0.4f, 0.1f), false, 1.5f);
				}
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

		// Lantern light: dim glow over fog-visible tiles within AOE of held or placed lanterns
		foreach (var p in grid.Positions())
		{
			if (!_client.Fog.IsVisible(p)) continue;
			if (IsInLanternLight(p))
				DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), LanternGlowColor);
		}

		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp)) continue;
			var c = _client.MonsterVisualPos(mo.Id, mo.X, mo.Y);
			var fwd  = FacingOffset(mo.Facing, ts * 0.12f);
			var side = PerpendicularOffset(mo.Facing, ts * 0.10f);
			switch (mo.Kind)
			{
				case MonsterKind.Slime:
				{
					DrawCircle(c, ts * 0.34f, SlimeColor);
					DrawCircle(c, ts * 0.34f, SlimeOutlineColor, false, 1.5f);
					var eye1 = c + fwd + side;
					var eye2 = c + fwd - side;
					DrawCircle(eye1, ts * 0.07f, Colors.White);
					DrawCircle(eye1, ts * 0.04f, Colors.Black);
					DrawCircle(eye2, ts * 0.07f, Colors.White);
					DrawCircle(eye2, ts * 0.04f, Colors.Black);
					break;
				}
				case MonsterKind.Ghost:
				{
					var ghostCol = GhostColor with { A = 0.6f };
					var headOff  = new Vector2(0, -ts * 0.10f);
					DrawCircle(c + headOff, ts * 0.28f, ghostCol);
					DrawRect(new Rect2(c.X - ts * 0.28f, c.Y - ts * 0.10f, ts * 0.56f, ts * 0.28f), ghostCol);
					for (int i = 0; i < 3; i++)
					{
						float xOff = (i - 1) * ts * 0.19f;
						DrawColoredPolygon(new Vector2[] {
							c + new Vector2(xOff - ts * 0.09f, ts * 0.18f),
							c + new Vector2(xOff + ts * 0.09f, ts * 0.18f),
							c + new Vector2(xOff,              ts * 0.36f),
						}, ghostCol);
					}
					var eFwd  = FacingOffset(mo.Facing, ts * 0.08f);
					var eSide = PerpendicularOffset(mo.Facing, ts * 0.09f);
					var eyeBase = c + headOff + eFwd;
					var eyeCol  = new Color(0.1f, 0.1f, 0.2f, 0.85f);
					DrawCircle(eyeBase + eSide, ts * 0.065f, eyeCol);
					DrawCircle(eyeBase - eSide, ts * 0.065f, eyeCol);
					break;
				}
				case MonsterKind.Goat:
				{
					DrawCircle(c, ts * 0.28f, GoatColor);
					var headPos = c + FacingOffset(mo.Facing, ts * 0.22f);
					DrawCircle(headPos, ts * 0.16f, GoatColor);
					var hSide = PerpendicularOffset(mo.Facing, ts * 0.10f);
					var hFwd  = FacingOffset(mo.Facing, ts * 0.14f);
					DrawLine(headPos + hSide, headPos + hSide * 1.8f + hFwd, GoatHornColor, 2.5f);
					DrawLine(headPos - hSide, headPos - hSide * 1.8f + hFwd, GoatHornColor, 2.5f);
					DrawCircle(headPos + PerpendicularOffset(mo.Facing, ts * 0.05f), ts * 0.04f, Colors.Black);
					DrawCircle(headPos - PerpendicularOffset(mo.Facing, ts * 0.05f), ts * 0.04f, Colors.Black);
					break;
				}
			}
		}

		if (_client.Octopus is { } octSnap)
		{
			var font = ThemeDB.FallbackFont;
			int fontSize = ts * 2 / 3;

			var snapOct = new Octopus(new GridPos(octSnap.X, octSnap.Y));
			for (int i = 0; i < snapOct.Arms.Length && i < octSnap.Arms.Length; i++)
			{
				snapOct.Arms[i].CurrentAngle   = octSnap.Arms[i].Angle;
				snapOct.Arms[i].PauseRemaining = octSnap.Arms[i].PauseRemaining;
				snapOct.Arms[i].SwingDir       = octSnap.Arms[i].SwingDir;
			}

			foreach (var p in snapOct.DangerTiles(_client.Grid))
				DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), OctopusArmColor);

			var br = new Rect2(octSnap.X * ts, octSnap.Y * ts, ts, ts);
			DrawRect(br, OctopusColor);
			DrawString(font, new Vector2(octSnap.X * ts + ts / 2f, octSnap.Y * ts + ts * 0.65f),
				"✦", HorizontalAlignment.Center, -1, fontSize, Colors.White);
		}

		if (_client.EscapeTile is { } exit)
		{
			float lx = exit.X * ts + ts * 0.31f;
			float rx = exit.X * ts + ts * 0.69f;
			float ty = exit.Y * ts + ts * 0.12f;
			float by = exit.Y * ts + ts * 0.88f;
			Color ladderCol;
			if (_client.EscapeOpen)
			{
				float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * Mathf.Pi * 2f / 0.9f);
				DrawRect(new Rect2(exit.X * ts, exit.Y * ts, ts, ts), ExitColor with { A = 0.12f + 0.18f * pulse });
				DrawRect(new Rect2(exit.X * ts, exit.Y * ts, ts, ts), ExitColor with { A = 0.55f + 0.4f * pulse }, false, 3f);
				ladderCol = ExitColor with { A = 0.7f + 0.3f * pulse };
			}
			else
			{
				ladderCol = LadderLockedColor;
			}
			DrawLine(new Vector2(lx, ty), new Vector2(lx, by), ladderCol, 2.5f);
			DrawLine(new Vector2(rx, ty), new Vector2(rx, by), ladderCol, 2.5f);
			for (int i = 0; i <= 3; i++)
			{
				float ry = ty + (by - ty) * i / 3f;
				DrawLine(new Vector2(lx, ry), new Vector2(rx, ry), ladderCol, 2f);
			}
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

	private bool IsInLanternLight(GridPos pos)
	{
		foreach (var m in _client.Miners)
			if (m.Alive && m.Held == (int)ItemKind.Lantern)
				if (Mathf.Max(Mathf.Abs(pos.X - m.X), Mathf.Abs(pos.Y - m.Y)) <= LanternRadius) return true;
		foreach (var it in _client.Items)
			if (it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose)
				if (Mathf.Max(Mathf.Abs(pos.X - it.X), Mathf.Abs(pos.Y - it.Y)) <= LanternRadius) return true;
		return false;
	}

	// Returns a unit-scaled offset in the facing direction (0=N, 1=E, 2=S, 3=W).
	private static Vector2 FacingOffset(int facing, float scale) => facing switch
	{
		0 => new Vector2(0f, -scale),
		1 => new Vector2(scale, 0f),
		2 => new Vector2(0f,  scale),
		3 => new Vector2(-scale, 0f),
		_ => Vector2.Zero,
	};

	// Returns a perpendicular offset 90° clockwise from facing.
	private static Vector2 PerpendicularOffset(int facing, float scale) => facing switch
	{
		0 => new Vector2(scale, 0f),
		1 => new Vector2(0f,  scale),
		2 => new Vector2(-scale, 0f),
		3 => new Vector2(0f, -scale),
		_ => Vector2.Zero,
	};

	private void DrawShimmer(int x, int y, Color col, int ts)
	{
		var c = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
		DrawCircle(c, ts * 0.42f, col);
	}
}
