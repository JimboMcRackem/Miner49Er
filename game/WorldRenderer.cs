using Godot;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Draws non-terrain world objects: charges, items, mold patches, explosion
/// flashes, the Listen shimmer, and special tile overlays (LavaVent, cracks).</summary>
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
	private static readonly Color ReelChargeColor   = new("ff3355");
	private static readonly Color WireColor         = new Color(0.9f, 0.55f, 0.1f, 0.75f);
	private static readonly Color DetonatorItemColor = new("ff3355");
	private const int ListenItemRevealRadius = 6;

	private Texture2D? _chargeTex;
	private Texture2D? _toolboxTex;
	private Texture2D? _moldPatchTex;
	private Texture2D? _goldRockTex;
	private Texture2D? _plankTex;
	private Texture2D? _lavaVentTex;
	private Texture2D? _crumbledTex;
	private Texture2D? _crackedTex;
	private readonly Dictionary<ItemKind, Texture2D> _itemTex = new();
	private ImageTexture _lanternGlowTex = null!;
	private Texture2D? _octopusTex;

	// PixelLab monster sprites — [dir] order: 0=N 1=E 2=S 3=W
	private Texture2D?[] _ghostTex = new Texture2D?[4];
	private Texture2D?[] _slimeTex = new Texture2D?[4];
	private Texture2D?[] _goatTex  = new Texture2D?[4];
	private Texture2D?[,] _ghostWalkTex = new Texture2D?[4, 9]; // [dir, frame 0-8]
	private Texture2D?[,] _goatWalkTex  = new Texture2D?[4, 9]; // [dir, frame 0-8]

	// Zombie miner: miner sprites fully tinted sickly green — no skin/hat preservation.
	private static readonly Color ZombieColor = new(0.42f, 0.78f, 0.38f);
	private Texture2D?[]  _zombieIdleTex = new Texture2D?[4];          // [dir]
	private Texture2D?[,] _zombieWalkTex = new Texture2D?[4, 4];       // [dir, frame]

	public void Init(MatchClient client)
	{
		_client = client;
		_chargeTex    = GD.Load<Texture2D>("res://assets/objects/charge.png");
		_toolboxTex   = GD.Load<Texture2D>("res://assets/objects/toolbox.png");
		_moldPatchTex = GD.Load<Texture2D>("res://assets/objects/mold_patch.png");
		_goldRockTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_6.png");
		_plankTex     = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_1.png");
		_lavaVentTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_5.png");
		_crumbledTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_11.png");
		_crackedTex   = GD.Load<Texture2D>("res://assets/tiles/cracked_floor.png");
		LoadItemTex(ItemKind.SpeedPotion,  "res://assets/objects/item_speed.png");
		LoadItemTex(ItemKind.LongerVision, "res://assets/objects/item_vision.png");
		LoadItemTex(ItemKind.BiggerBlast,  "res://assets/objects/item_blast.png");
		LoadItemTex(ItemKind.WaterPlank,   "res://assets/objects/item_plank.png");
		LoadItemTex(ItemKind.SlowMold,     "res://assets/objects/item_mold.png");
		LoadItemTex(ItemKind.Chest,        "res://assets/objects/chest.png");
		LoadItemTex(ItemKind.BossChest,    "res://assets/objects/chest_boss.png");
		LoadItemTex(ItemKind.LifePotion,   "res://assets/objects/item_lifepot.png");
		LoadItemTex(ItemKind.Detonator,    "res://assets/objects/item_detonator.png");
		LoadItemTex(ItemKind.Lantern,      "res://assets/objects/item_lantern.png");
		LoadMonsterTex(_ghostTex, "ghost");
		LoadMonsterTex(_slimeTex, "slime");
		LoadMonsterTex(_goatTex,  "goat");
		BuildZombieTextures();
		BuildGhostWalkTextures();
		BuildGoatWalkTextures();
		_octopusTex = GD.Load<Texture2D>("res://assets/monsters/octopus/octopus.png");
		_lanternGlowTex = BuildRadialGlowTex();
	}

	private static ImageTexture BuildRadialGlowTex(int size = 128)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float half = size / 2f;
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			float t = Mathf.Clamp(new Vector2(x - half, y - half).Length() / half, 0f, 1f);
			float a = Mathf.Pow(1f - t, 2.5f);
			img.SetPixel(x, y, new Color(1f, 0.85f, 0.4f, a * 0.55f));
		}
		return ImageTexture.CreateFromImage(img);
	}

	private void LoadItemTex(ItemKind kind, string path)
	{
		var t = GD.Load<Texture2D>(path);
		if (t != null) _itemTex[kind] = t;
	}

	// Loads n/e/s/w.png from assets/monsters/<folder>/ into a [4] array (0=N 1=E 2=S 3=W).
	private static void LoadMonsterTex(Texture2D?[] arr, string folder)
	{
		var dirs = new[] { "n", "e", "s", "w" };
		for (int i = 0; i < 4; i++)
			arr[i] = GD.Load<Texture2D>($"res://assets/monsters/{folder}/{dirs[i]}.png");
	}

	private void BuildGhostWalkTextures()
	{
		string[] dirName = { "north", "east", "south", "west" };
		for (int d = 0; d < 4; d++)
			for (int f = 0; f <= 8; f++)
			{
				string path = $"res://assets/monsters/ghost/walk/{dirName[d]}_{f}.png";
				if (ResourceLoader.Exists(path))
					_ghostWalkTex[d, f] = GD.Load<Texture2D>(path);
			}
	}

	private void BuildGoatWalkTextures()
	{
		string[] dirName = { "north", "east", "south", "west" };
		for (int d = 0; d < 4; d++)
			for (int f = 0; f <= 8; f++)
			{
				string path = $"res://assets/monsters/goat/walk/{dirName[d]}_{f}.png";
				if (ResourceLoader.Exists(path))
					_goatWalkTex[d, f] = GD.Load<Texture2D>(path);
			}
	}

	private void BuildZombieTextures()
	{
		var dirLetter = new[] { "n", "e", "s", "w" };
		for (int d = 0; d < 4; d++)
		{
			var img = GD.Load<CompressedTexture2D>($"res://assets/miners/miner_{dirLetter[d]}.png")?.GetImage();
			if (img != null)
			{
				img.Convert(Image.Format.Rgba8);
				_zombieIdleTex[d] = ImageTexture.CreateFromImage(TintFull(img, ZombieColor));
			}
			for (int f = 0; f < 4; f++)
			{
				var wImg = GD.Load<CompressedTexture2D>($"res://assets/miners/walk/{dirLetter[d]}{f}.png")?.GetImage();
				if (wImg != null)
				{
					wImg.Convert(Image.Format.Rgba8);
					_zombieWalkTex[d, f] = ImageTexture.CreateFromImage(TintFull(wImg, ZombieColor));
				}
			}
		}
	}

	// Full-replacement tint: every non-transparent pixel becomes luminance × tint.
	// Used for monsters (zombie) where we want the whole sprite recoloured.
	private static Image TintFull(Image src, Color tint)
	{
		var img = (Image)src.Duplicate();
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var px = img.GetPixel(x, y);
				if (px.A < 0.05f) continue;
				float lum = 0.299f * px.R + 0.587f * px.G + 0.114f * px.B;
				float l = lum * 0.6f + 0.4f;
				img.SetPixel(x, y, new Color(tint.R * l, tint.G * l, tint.B * l, px.A));
			}
		return img;
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

		// Procedural rough-stone floor — covers the PixelLab floor texture with per-tile brightness
		// variation and small grain marks, making the floor look like unworked natural stone.
		foreach (var p in grid.Positions())
		{
			var t = grid.Get(p);
			if (t != TileType.Floor && t != TileType.Cracked && t != TileType.Crumbling) continue;
			float x0 = p.X * ts, y0 = p.Y * ts;
			uint h = (uint)(p.X * 73856093 ^ p.Y * 19349663);
			float v = 0.88f + (h & 0x3Fu) * (0.28f / 63f); // brightness 0.88..1.16
			DrawRect(new Rect2(x0, y0, ts, ts), new Color(0.27f * v, 0.25f * v, 0.23f * v));
			for (int g = 0; g < 3; g++)
			{
				uint h2 = h ^ (uint)(g * 2654435761u);
				DrawRect(
					new Rect2(x0 + (h2 & 0x1Fu) * (ts - 3) / 31f, y0 + ((h2 >> 8) & 0x1Fu) * (ts - 2) / 31f,
							  2 + (int)((h2 >> 16) & 3u), 1 + (int)((h2 >> 20) & 2u)),
					new Color(0.15f, 0.13f, 0.12f, 0.55f));
			}
		}

		// Single-pass tile overlays on top of TerrainMap (FogRenderer at ZIndex -5 covers these naturally).
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
				case TileType.Cracked:
				{
					if (_crackedTex != null)
						DrawTextureRect(_crackedTex, r, false);
					else
					{
						float x0 = p.X * ts, y0 = p.Y * ts;
						var ca = new Vector2(x0 + ts * 0.45f, y0 + ts * 0.10f);
						var cb = new Vector2(x0 + ts * 0.52f, y0 + ts * 0.52f);
						var cc = new Vector2(x0 + ts * 0.60f, y0 + ts * 0.90f);
						DrawLine(ca, cb, CrackColor, 1.5f);
						DrawLine(cb, cc, CrackColor, 1.5f);
					}
					break;
				}
				case TileType.Crumbling:
				{
					if (_crumbledTex != null)
						DrawTextureRect(_crumbledTex, r, false);
					else
					{
						float x0 = p.X * ts, y0 = p.Y * ts;
						var cen = new Vector2(x0 + ts * 0.52f, y0 + ts * 0.48f);
						DrawLine(new Vector2(x0 + ts * 0.10f, y0 + ts * 0.10f), cen, CrackColor, 1.5f);
						DrawLine(cen, new Vector2(x0 + ts * 0.90f, y0 + ts * 0.90f), CrackColor, 1.5f);
						DrawLine(new Vector2(x0 + ts * 0.90f, y0 + ts * 0.10f), cen, CrackColor, 1.5f);
						DrawLine(cen, new Vector2(x0 + ts * 0.10f, y0 + ts * 0.90f), CrackColor, 1.5f);
					}
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

		// Reel charges: red marker on wall tile + orange wire to owner.
		foreach (var rc in _client.ReelCharges)
		{
			var wallCenter = new Vector2(rc.WallX * ts + ts / 2f, rc.WallY * ts + ts / 2f);
			DrawCircle(wallCenter, ts * 0.22f, ReelChargeColor);
			DrawCircle(wallCenter, ts * 0.14f, new Color(1f, 0.85f, 0.1f, 0.9f));

			bool foundOwner = false;
			MinerSnapshot ownerSnap = default;
			foreach (var m in _client.Miners) if (m.Id == rc.OwnerId) { ownerSnap = m; foundOwner = true; break; }
			if (foundOwner && ownerSnap.Alive)
				DrawLine(wallCenter, _client.MinerVisualPos(ownerSnap.Id, ownerSnap.X, ownerSnap.Y), WireColor, 1.5f);
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
				if (_itemTex.TryGetValue(it.Kind, out var litex))
					DrawTextureRect(litex, r, false);
				else
				{
					var font = ThemeDB.FallbackFont;
					int fontSize = ts * 2 / 3;
					DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
						"♥", HorizontalAlignment.Center, -1, fontSize, new Color(1f, 0.15f, 0.15f, 0.95f));
				}
				continue;
			}
			if (it.Kind == ItemKind.BossChest)
			{
				if (_itemTex.TryGetValue(it.Kind, out var bctex))
					DrawTextureRect(bctex, r, false);
				else
				{
					var font = ThemeDB.FallbackFont;
					int fontSize = ts * 2 / 3;
					DrawRect(r, new Color(0.9f, 0.75f, 0.1f, 0.9f));
					DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
						"★", HorizontalAlignment.Center, -1, fontSize, Colors.Black);
				}
				continue;
			}
			if (it.Kind == ItemKind.Chest)
			{
				if (_itemTex.TryGetValue(it.Kind, out var ctex))
					DrawTextureRect(ctex, r, false);
				else
				{
					var font = ThemeDB.FallbackFont;
					int fontSize = ts * 2 / 3;
					DrawRect(r, ChestColor);
					DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
						"♦", HorizontalAlignment.Center, -1, fontSize, Colors.Black);
				}
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

		// Lantern light: radial amber glow centered on each active lantern source
		int glowPx = 5 * ts * 2;
		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.Held != (int)ItemKind.Lantern) continue;
			var center = new Vector2(m.X * ts + ts / 2f, m.Y * ts + ts / 2f);
			DrawTextureRect(_lanternGlowTex,
				new Rect2(center.X - glowPx / 2f, center.Y - glowPx / 2f, glowPx, glowPx),
				false);
		}
		foreach (var it in _client.Items)
		{
			if (it.Kind != ItemKind.Lantern || it.Placement != ItemPlacement.Loose) continue;
			var center = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);
			DrawTextureRect(_lanternGlowTex,
				new Rect2(center.X - glowPx / 2f, center.Y - glowPx / 2f, glowPx, glowPx),
				false);
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
					var tex = _slimeTex[mo.Facing];
					if (tex != null)
						DrawTextureRect(tex, new Rect2(c.X - ts / 2f, c.Y - ts / 2f, ts, ts), false);
					else
					{
						DrawCircle(c, ts * 0.34f, SlimeColor);
						DrawCircle(c, ts * 0.34f, SlimeOutlineColor, false, 1.5f);
					}
					break;
				}
				case MonsterKind.Ghost:
				{
					int ghostFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
					var tex = _ghostWalkTex[mo.Facing, ghostFrame] ?? _ghostTex[mo.Facing];
					if (tex != null)
					{
						float gs = ts * 1.3f;
						DrawTextureRect(tex, new Rect2(c.X - gs / 2f, c.Y - gs / 2f, gs, gs), false);
					}
					else
					{
						var ghostCol = GhostColor with { A = 0.6f };
						var headOff  = new Vector2(0, -ts * 0.10f);
						DrawCircle(c + headOff, ts * 0.28f, ghostCol);
						DrawRect(new Rect2(c.X - ts * 0.28f, c.Y - ts * 0.10f, ts * 0.56f, ts * 0.28f), ghostCol);
					}
					break;
				}
				case MonsterKind.Goat:
				{
					int goatFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
					var tex = _goatWalkTex[mo.Facing, goatFrame] ?? _goatTex[mo.Facing];
					if (tex != null)
					{
						float gs = ts * 1.5f;
						DrawTextureRect(tex, new Rect2(c.X - gs / 2f, c.Y - gs / 2f, gs, gs), false);
					}
					else
					{
						DrawCircle(c, ts * 0.28f, GoatColor);
						var headPos = c + FacingOffset(mo.Facing, ts * 0.22f);
						DrawCircle(headPos, ts * 0.16f, GoatColor);
						var hSide = PerpendicularOffset(mo.Facing, ts * 0.10f);
						var hFwd  = FacingOffset(mo.Facing, ts * 0.14f);
						DrawLine(headPos + hSide, headPos + hSide * 1.8f + hFwd, GoatHornColor, 2.5f);
						DrawLine(headPos - hSide, headPos - hSide * 1.8f + hFwd, GoatHornColor, 2.5f);
					}
					break;
				}
				case MonsterKind.ZombieMiner:
				{
					int dir   = mo.Facing;
					int frame = (int)(Time.GetTicksMsec() / 175u) % 4;
					var tex   = _zombieWalkTex[dir, frame] ?? _zombieIdleTex[dir];
					if (tex != null)
						DrawTextureRect(tex, new Rect2(c.X - ts / 2f, c.Y - ts / 2f, ts, ts), false);
					else
						DrawCircle(c, ts * 0.28f, ZombieColor);
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

			float bs = ts * 2f;
			var br = new Rect2(octSnap.X * ts - ts / 2f, octSnap.Y * ts - ts / 2f, bs, bs);
			if (_octopusTex != null)
				DrawTextureRect(_octopusTex, br, false);
			else
			{
				var fb = new Rect2(octSnap.X * ts, octSnap.Y * ts, ts, ts);
				DrawRect(fb, OctopusColor);
				DrawString(font, new Vector2(octSnap.X * ts + ts / 2f, octSnap.Y * ts + ts * 0.65f),
					"✦", HorizontalAlignment.Center, -1, fontSize, Colors.White);
			}
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

		// Shopkeeper tile
		if (_client.ShopPos is GridPos sp && _client.Fog.IsVisible(sp))
		{
			var shopRect = new Rect2(sp.X * ts + 2, sp.Y * ts + 2, ts - 4, ts - 4);
			DrawRect(shopRect, new Color(0.78f, 0.63f, 0.13f));
			DrawString(ThemeDB.FallbackFont, new Vector2(sp.X * ts + 4, sp.Y * ts + ts - 6),
				"$", HorizontalAlignment.Left, -1, 16, new Color(0.1f, 0.05f, 0f));
		}
	}

	private static Color ItemColor(ItemKind kind) => kind switch
	{
		ItemKind.SpeedPotion  => SpeedItemColor,
		ItemKind.LongerVision => VisionItemColor,
		ItemKind.BiggerBlast  => BlastItemColor,
		ItemKind.WaterPlank   => PlankItemColor,
		ItemKind.SlowMold     => MoldItemColor,
		ItemKind.Detonator    => DetonatorItemColor,
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
