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
	private readonly List<(Vector2 center, float maxR, float life)> _rings = new();
	private readonly List<(GridPos center, int radius, float life)> _rockfallDusts = new();

	private static readonly Color CrackColor     = new Color(0.15f, 0.08f, 0.0f, 0.70f);
	private static readonly Color LavaVentColor  = new("ff7a2a");
	private static readonly Color CrystalFacetA  = new("a060ff");
	private static readonly Color CrystalFacetB  = new("60c0ff");
	private static readonly Color CrystalFacetC  = new("c080ff");
	private static readonly Vector2[] _crystalPolyVerts  = new Vector2[4];
	private static readonly Color[]   _crystalPolyColors = new Color[4];
	private static readonly Color ChargeColor    = new("ff5530");
	private static readonly Color FlashColor     = new("ffd27f");
	private static readonly Color SpeedItemColor = new("4ad06a");
	private static readonly Color VisionItemColor = new("4ad0d0");
	private static readonly Color BlastItemColor = new("e08a2f");
	private static readonly Color ToolboxColor   = new("9a7b4f");
	private static readonly Color ShimmerColor   = new("f5f0c0");
	private static readonly Color ScreeColor     = new(1.0f, 0.67f, 0.0f, 1f);  // amber — probabilistic scree
	private static readonly Color UnstableColor  = new(1.0f, 0.27f, 0.07f, 1f); // light red — certain, radius 1
	private static readonly Color VolatileColor  = new(1.0f, 0.0f,  0.0f, 1f);  // bright red — certain, radius 2
	private static readonly Color RockfallDustColor = new(0.55f, 0.45f, 0.35f, 1f);
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
	private static readonly Color TreasureChestColor = new Color(0.2f, 0.8f, 0.95f, 0.90f);
	private static readonly Color IdolColor          = new Color(0.9f, 0.72f, 0.2f, 0.95f);
	private static readonly Color ReelChargeColor   = new("ff3355");
	private static readonly Color WireColor         = new Color(0.9f, 0.55f, 0.1f, 0.75f);
	private static readonly Color DetonatorItemColor = new("ff3355");
	private const int ListenItemRevealRadius = 6;

	// Cached water draw data — computed once on first draw, valid for map lifetime since water tiles never change.
	private GridPos[]? _waterTiles;
	private Vector2[][]? _waterPolys;
	// Singleton color arrays shared across all shallow / deep tiles to avoid per-frame allocations.
	private static readonly Color[] _shallowWaterCol = { new Color(0.06f, 0.17f, 0.46f) };
	private static readonly Color[] _deepWaterCol    = { new Color(0.02f, 0.07f, 0.24f) };

	private Texture2D? _shopLampTex;
	private Texture2D? _chargeTex;
	private Texture2D? _toolboxTex;
	private Texture2D? _moldPatchTex;
	private Texture2D? _goldRockTex;      // base rock texture drawn under gold veins
	private Texture2D? _rockBaseTex;      // plain dark rock, used as gold-vein base
	private Texture2D? _plankTex;
	private Texture2D? _lavaVentTex;
	private Texture2D? _crumbledTex;
	private Texture2D? _crackedTex;
	private readonly Dictionary<ItemKind, Texture2D> _itemTex = new();
	private ImageTexture _lanternGlowTex  = null!;
	private ImageTexture _crystalGlowTex  = null!;
	private Texture2D?   _crystalRockTex;
	private Texture2D?   _crystalShardTex;
	private Texture2D?[] _octopusIdleTex = new Texture2D?[9]; // idle_0..idle_8

	// PixelLab monster sprites — [dir] order: 0=N 1=E 2=S 3=W
	private Texture2D?[] _ghostTex = new Texture2D?[4];
	private Texture2D?[] _slimeTex = new Texture2D?[4];
	private Texture2D?[] _goatTex  = new Texture2D?[4];
	private Texture2D?[,] _ghostWalkTex = new Texture2D?[4, 9]; // [dir, frame 0-8]
	private Texture2D?[,] _goatWalkTex  = new Texture2D?[4, 9]; // [dir, frame 0-8]
	private Texture2D?[,] _slimeWalkTex = new Texture2D?[4, 9]; // [dir, frame 0-8]

	// Zombie miner: miner sprites fully tinted sickly green — no skin/hat preservation.
	private static readonly Color ZombieColor = new(0.42f, 0.78f, 0.38f);
	private Texture2D?[]  _zombieIdleTex = new Texture2D?[4];          // [dir]
	private Texture2D?[,] _zombieWalkTex = new Texture2D?[4, 4];       // [dir, frame]

	// Skeletons
	private Texture2D?   _bonesPileTex = null!;
	private Texture2D?[] _skeletonHumanTex     = new Texture2D?[4];
	private Texture2D?[] _skeletonDinoTex      = new Texture2D?[4];
	private Texture2D?[,] _skeletonHumanWalkTex = new Texture2D?[4, 4]; // [dir, frame 0-3]
	private Texture2D?[,] _skeletonDinoWalkTex  = new Texture2D?[4, 4]; // [dir, frame 0-3]

	// Water snake
	private static readonly Color WaterSnakeColor = new(0.15f, 0.72f, 0.62f);
	private Texture2D?[]  _waterSnakeTex     = new Texture2D?[4];
	private Texture2D?[,] _waterSnakeWalkTex = new Texture2D?[4, 9]; // [dir, frame 0-8]

	public void Init(MatchClient client)
	{
		_client = client;
		_shopLampTex  = GD.Load<Texture2D>("res://assets/objects/idol_lamp.png");
		_chargeTex    = GD.Load<Texture2D>("res://assets/objects/charge.png");
		_toolboxTex   = GD.Load<Texture2D>("res://assets/objects/toolbox.png");
		_moldPatchTex = GD.Load<Texture2D>("res://assets/objects/mold_patch.png");
		_goldRockTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_6.png");
		_rockBaseTex  = GD.Load<Texture2D>("res://assets/tiles/singletiles/tile_0.png");
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
		LoadItemTex(ItemKind.TreasureChest,   "res://assets/objects/treasure_chest.png");
		LoadItemTex(ItemKind.IdolVishnu,      "res://assets/objects/idol_vishnu.png");
		LoadItemTex(ItemKind.IdolZeus,        "res://assets/objects/idol_zeus.png");
		LoadItemTex(ItemKind.IdolAnubis,      "res://assets/objects/idol_anubis.png");
		LoadItemTex(ItemKind.IdolOdin,        "res://assets/objects/idol_odin.png");
		LoadItemTex(ItemKind.IdolShiva,       "res://assets/objects/idol_shiva.png");
		LoadItemTex(ItemKind.IdolBuddha,      "res://assets/objects/idol_buddha.png");
		LoadItemTex(ItemKind.IdolRa,          "res://assets/objects/idol_ra.png");
		LoadItemTex(ItemKind.IdolQuetzalcoatl,"res://assets/objects/idol_quetzalcoatl.png");
		LoadItemTex(ItemKind.IdolUrn,         "res://assets/objects/idol_urn.png");
		LoadItemTex(ItemKind.IdolLamp,        "res://assets/objects/idol_lamp.png");
		LoadItemTex(ItemKind.IdolMace,        "res://assets/objects/idol_mace.png");
		LoadItemTex(ItemKind.IdolSceptre,     "res://assets/objects/idol_sceptre.png");
		LoadItemTex(ItemKind.IdolGlobe,       "res://assets/objects/idol_globe.png");
		LoadItemTex(ItemKind.IdolTrophyCup,   "res://assets/objects/idol_trophycup.png");
		LoadItemTex(ItemKind.IdolChalice,     "res://assets/objects/idol_chalice.png");
		LoadItemTex(ItemKind.IdolCrown,       "res://assets/objects/idol_crown.png");
		LoadItemTex(ItemKind.IdolSkull,       "res://assets/objects/idol_skull.png");
		LoadMonsterTex(_ghostTex, "ghost");
		LoadMonsterTex(_slimeTex, "slime");
		LoadMonsterTex(_goatTex,  "goat");
		BuildZombieTextures();
		BuildGhostWalkTextures();
		BuildGoatWalkTextures();
		BuildSlimeWalkTextures();
		_bonesPileTex = GD.Load<Texture2D>("res://assets/monsters/skeleton_bones_pile.png");
		LoadMonsterTex(_skeletonHumanTex, "skeleton_human");
		LoadMonsterTex(_skeletonDinoTex,  "skeleton_dino");
		BuildSkeletonWalkTextures(_skeletonHumanWalkTex, "skeleton_human");
		BuildSkeletonWalkTextures(_skeletonDinoWalkTex,  "skeleton_dino");
		LoadMonsterTex(_waterSnakeTex, "water_snake");
		BuildWaterSnakeWalkTextures();
		for (int f = 0; f <= 8; f++)
		{
			string p = $"res://assets/monsters/octopus/idle_{f}.png";
			if (ResourceLoader.Exists(p)) _octopusIdleTex[f] = GD.Load<Texture2D>(p);
		}
		_lanternGlowTex  = BuildRadialGlowTex();
		_crystalGlowTex  = BuildRadialGlowTex(new Color(0.65f, 0.35f, 1.0f, 1f), new Color(0.30f, 0.55f, 1.0f, 1f));
		_crystalRockTex  = ResourceLoader.Exists("res://assets/tiles/singletiles/crystal_rock.png")
		                   ? GD.Load<Texture2D>("res://assets/tiles/singletiles/crystal_rock.png") : null;
		_crystalShardTex = ResourceLoader.Exists("res://assets/objects/item_crystal_shard.png")
		                   ? GD.Load<Texture2D>("res://assets/objects/item_crystal_shard.png") : null;
	}

	private static ImageTexture BuildRadialGlowTex(int size = 128) =>
		BuildRadialGlowTex(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.60f, 0.1f, 1f), size);

	private static ImageTexture BuildRadialGlowTex(Color centre, Color edge, int size = 128)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float half = size / 2f;
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			float t = Mathf.Clamp(new Vector2(x - half, y - half).Length() / half, 0f, 1f);
			float a = Mathf.Pow(1f - t, 2.5f);
			img.SetPixel(x, y, new Color(
				Mathf.Lerp(centre.R, edge.R, t),
				Mathf.Lerp(centre.G, edge.G, t),
				Mathf.Lerp(centre.B, edge.B, t),
				a * 0.55f));
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

	private void BuildSlimeWalkTextures()
	{
		string[] dirName = { "north", "east", "south", "west" };
		for (int d = 0; d < 4; d++)
			for (int f = 0; f <= 8; f++)
			{
				string path = $"res://assets/monsters/slime/walk/{dirName[d]}_{f}.png";
				if (ResourceLoader.Exists(path))
					_slimeWalkTex[d, f] = GD.Load<Texture2D>(path);
			}
	}

	private void BuildSkeletonWalkTextures(Texture2D?[,] arr, string folder)
	{
		string[] dirName = { "north", "east", "south", "west" };
		for (int d = 0; d < 4; d++)
			for (int f = 0; f < 4; f++)
			{
				string path = $"res://assets/monsters/{folder}/walk/{dirName[d]}_{f}.png";
				if (ResourceLoader.Exists(path))
					arr[d, f] = GD.Load<Texture2D>(path);
			}
	}

	private void BuildWaterSnakeWalkTextures()
	{
		string[] dirName = { "north", "east", "south", "west" };
		for (int d = 0; d < 4; d++)
			for (int f = 0; f <= 8; f++)
			{
				string path = $"res://assets/monsters/water_snake/walk/{dirName[d]}_{f}.png";
				if (ResourceLoader.Exists(path))
					_waterSnakeWalkTex[d, f] = GD.Load<Texture2D>(path);
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
	public void AddExplosionRing(Vector2 center, float maxR) => _rings.Add((center, maxR, 0.5f));
	public void AddRockfallDust(GridPos center, int radius) => _rockfallDusts.Add((center, radius, 0.6f));

	public override void _Process(double delta)
	{
		for (int i = _flashes.Count - 1; i >= 0; i--)
		{
			var f = _flashes[i];
			f.life -= (float)delta;
			if (f.life <= 0) _flashes.RemoveAt(i);
			else _flashes[i] = f;
		}
		for (int i = _rings.Count - 1; i >= 0; i--)
		{
			var r = _rings[i];
			r.life -= (float)delta;
			if (r.life <= 0) _rings.RemoveAt(i);
			else _rings[i] = r;
		}
		for (int i = _rockfallDusts.Count - 1; i >= 0; i--)
		{
			var d = _rockfallDusts[i];
			d.life -= (float)delta;
			if (d.life <= 0) _rockfallDusts.RemoveAt(i);
			else _rockfallDusts[i] = d;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		int ts = MatchClient.TileSize;

		// Water: polygon-based tiles with procedurally rounded outer corners.
		// Polygons are cached after first draw since water tiles never change mid-game.
		EnsureWaterCache();
		float wTime = (float)Time.GetTicksMsec() / 1000f;
		for (int wi = 0; wi < _waterTiles!.Length; wi++)
		{
			var p = _waterTiles[wi];
			bool deep = grid.Get(p) == TileType.DeepWater;
			DrawPolygon(_waterPolys![wi], deep ? _deepWaterCol : _shallowWaterCol);

			float wx0 = p.X * ts, wy0 = p.Y * ts;
			uint wh = (uint)(p.X * 2246822519u ^ p.Y * 3266489917u ^ 0xA71Bu);
			int sparkCount = 4 + (int)(wh & 1u);
			for (int wv = 0; wv < sparkCount; wv++)
			{
				uint wh2 = wh ^ (uint)(wv * 1013904223u);
				float px = wx0 + 2f + (wh2 & 0x1Fu) * (ts - 4) / 31f;
				float py = wy0 + 2f + ((wh2 >> 8) & 0x1Fu) * (ts - 4) / 31f;
				float phase = ((wh2 >> 16) & 0xFFu) * (Mathf.Tau / 255f);
				float t = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(wTime * 2.5f + phase)), 3f);
				if (t < 0.02f) continue;
				float alpha = t * 0.90f;
				DrawCircle(new Vector2(px, py), 1.2f,
					deep ? new Color(0.55f, 0.75f, 1f, alpha)
					     : new Color(0.70f, 0.88f, 1f, alpha));
			}
		}

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

		// Scattered bone fragments on ~5% of floor tiles — 4 variants, seeded by position.
		foreach (var p in grid.Positions())
		{
			if (grid.Get(p) != TileType.Floor) continue;
			uint h = (uint)(p.X * 3266489917u ^ p.Y * 2246822519u ^ 0xBEEFu);
			if ((h & 0x13u) != 1u) continue; // ~5% of floor tiles
			float x0 = p.X * ts, y0 = p.Y * ts;
			float cx = x0 + ts * 0.5f + ((h >> 5) & 0x7u) - 3.5f;
			float cy = y0 + ts * 0.5f + ((h >> 9) & 0x7u) - 3.5f;
			var col = new Color(0.72f, 0.66f, 0.55f, 0.38f);
			float angle = ((h >> 16) & 0xFFu) * (Mathf.Tau / 255f); // random rotation
			var rot = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			var perp = new Vector2(-rot.Y, rot.X);
			switch ((h >> 14) & 0x3u)
			{
				case 0: // Single rib — curved elongated bone
				{
					var a = new Vector2(cx, cy) - rot * 8f;
					var b = new Vector2(cx, cy) + rot * 8f;
					DrawLine(a, b, col, 2.5f);
					DrawCircle(a, 2f, col);
					DrawCircle(b, 1.5f, col);
					// Slight curve suggestion via a midpoint offset
					DrawCircle(new Vector2(cx, cy) + perp * 2f, 1.2f, col);
					break;
				}
				case 1: // Vertebrae cluster — 3–4 small ovals in a line
				{
					for (int i = -1; i <= 2; i++)
					{
						var vc = new Vector2(cx, cy) + rot * (i * 4.5f);
						DrawCircle(vc, 2.2f, col);
						DrawLine(vc - perp * 2f, vc + perp * 2f, col, 1f); // transverse process
					}
					break;
				}
				case 2: // Finger / toe bones — several short parallel bones
				{
					for (int i = -1; i <= 1; i++)
					{
						var off = perp * (i * 3.5f);
						DrawLine(new Vector2(cx, cy) + off - rot * 5f,
						         new Vector2(cx, cy) + off + rot * 5f, col, 1.5f);
						DrawCircle(new Vector2(cx, cy) + off - rot * 5f, 1.5f, col);
						DrawCircle(new Vector2(cx, cy) + off + rot * 5f, 1.2f, col);
					}
					break;
				}
				default: // Skull fragment — partial dome with eye socket
				{
					DrawArc(new Vector2(cx, cy), 5f, angle - Mathf.DegToRad(120f),
					        angle + Mathf.DegToRad(120f), 10, col, 2f);
					DrawCircle(new Vector2(cx, cy) + rot * 1.5f - perp * 1.5f, 1.8f, col);
					break;
				}
			}
		}

		// Single-pass tile overlays on top of TerrainMap (FogRenderer at ZIndex -5 covers these naturally).
		foreach (var p in grid.Positions())
		{
			var r = new Rect2(p.X * ts, p.Y * ts, ts, ts);
			switch (grid.Get(p))
			{
				case TileType.GoldRock:
					// Draw plain rock base then overlay prominent gold ore veins.
					if (_rockBaseTex != null) DrawTextureRect(_rockBaseTex, r, false);
					DrawGoldVeins(p.X, p.Y, ts);
					break;
				case TileType.Plank:
					if (_plankTex != null) DrawTextureRect(_plankTex, r, false);
					break;
				case TileType.LavaVent:
					if (_lavaVentTex != null) DrawTextureRect(_lavaVentTex, r, false);
					else DrawRect(r, LavaVentColor);
					break;
				case TileType.CrystalRock:
				{
					float cx = p.X * ts + ts * 0.5f, cy = p.Y * ts + ts * 0.5f;
					// The glow only shows when the crystal is in the local miner's current FOV —
					// an explored-but-unseen crystal would otherwise bleed its bright halo through
					// the dim fog overlay.
					if (_client.FogRenderer?.SpectatorMode == true || _client.Fog.IsVisible(p))
					{
						float crystalGlowPx = ts * 2.5f;
						float pulse = 0.70f + 0.30f * Mathf.Sin(wTime * Mathf.Pi * 2f / 1.65f);
						DrawTextureRect(_crystalGlowTex,
							new Rect2(cx - crystalGlowPx / 2f, cy - crystalGlowPx / 2f, crystalGlowPx, crystalGlowPx),
							false, new Color(1f, 1f, 1f, 0.40f * pulse));
					}
					if (_crystalRockTex != null)
					{
						DrawTextureRect(_crystalRockTex, r, false);
					}
					else
					{
						uint ch = (uint)(p.X * 2246822519u ^ p.Y * 3266489917u ^ 0xC7A1u);
						float brightness = 0.80f + 0.20f * Mathf.Sin(wTime * 3.1f + (ch & 0xFFu) * 0.025f);
						for (int fi = 0; fi < 4; fi++)
						{
							uint fh = ch ^ (uint)(fi * 1013904223u);
							float ang = ((fh >> 4) & 0xFFu) * (Mathf.Tau / 255f);
							float len = ts * (0.30f + ((fh >> 12) & 0x1Fu) * 0.007f);
							float wid = ts * 0.06f;
							var cen = new Vector2(cx, cy);
							var rot = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
							var perp = new Vector2(-rot.Y, rot.X) * wid;
							var tip1 = cen + rot * len;
							var tip2 = cen - rot * len;
							Color fc = (fi % 3) switch { 0 => CrystalFacetA, 1 => CrystalFacetB, _ => CrystalFacetC };
							fc = fc with { A = brightness * 0.85f };
							_crystalPolyVerts[0] = tip1; _crystalPolyVerts[1] = cen + perp;
							_crystalPolyVerts[2] = tip2; _crystalPolyVerts[3] = cen - perp;
							_crystalPolyColors[0] = _crystalPolyColors[1] = _crystalPolyColors[2] = _crystalPolyColors[3] = fc;
							DrawPolygon(_crystalPolyVerts, _crystalPolyColors);
						}
					}
					break;
				}
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

		// Skeletal remains embedded in ~6% of rock walls — seeded, 4 archaeological variants.
		foreach (var p in grid.Positions())
		{
			if (grid.Get(p) != TileType.Rock) continue;
			uint h = (uint)(p.X * 2246822519u ^ p.Y * 3266489917u ^ 0xCAFEu);
			if ((h & 0xFu) != 1u) continue;
			float x0 = p.X * ts, y0 = p.Y * ts;
			float cx = x0 + ts * 0.5f + ((h >> 4) & 0x7u) - 3.5f;
			float cy = y0 + ts * 0.5f + ((h >> 8) & 0x5u) - 2.5f;
			var col = new Color(0.76f, 0.70f, 0.60f, 0.42f);
			switch ((h >> 12) & 0x3u)
			{
				case 0: // Human — side profile lying horizontally, spine + ribs visible
				{
					// Skull oval at left
					DrawArc(new Vector2(cx - 9f, cy), 4f, 0f, Mathf.Tau, 14, col, 1.5f);
					DrawRect(new Rect2(cx - 11f, cy - 1.5f, 2f, 2f), col); // eye socket
					// Jaw
					DrawLine(new Vector2(cx - 12.5f, cy + 2f), new Vector2(cx - 5.5f, cy + 2f), col, 1f);
					// Vertebrae (spine going right as small rectangles)
					for (int i = 0; i < 5; i++)
						DrawRect(new Rect2(cx - 3.5f + i * 3f, cy - 0.8f, 2f, 1.6f), col);
					// Ribs angling up and down from spine
					float[] ribXa = { cx - 2f, cx + 1f, cx + 4f, cx + 7f };
					foreach (float rx in ribXa)
					{
						DrawLine(new Vector2(rx, cy - 0.5f), new Vector2(rx - 1f, cy - 5f), col, 1f);
						DrawLine(new Vector2(rx, cy + 0.5f), new Vector2(rx - 1f, cy + 5f), col, 1f);
					}
					break;
				}
				case 1: // Dinosaur skull — long snout, teeth, large eye socket, neck vertebrae
				{
					// Upper skull ridge
					DrawLine(new Vector2(cx - 11f, cy + 1f), new Vector2(cx + 8f, cy - 5f), col, 1.5f);
					// Back of skull (rounded arc)
					DrawArc(new Vector2(cx + 7f, cy - 2f), 3.5f, Mathf.DegToRad(-110f), Mathf.DegToRad(90f), 8, col, 1.5f);
					// Lower jaw
					DrawLine(new Vector2(cx - 11f, cy + 1f), new Vector2(cx + 4f, cy + 3f), col, 1.5f);
					DrawLine(new Vector2(cx + 4f, cy + 3f), new Vector2(cx + 7f, cy + 1f), col, 1f);
					// Teeth along lower jaw
					for (int i = 0; i < 5; i++)
						DrawLine(new Vector2(cx - 9f + i * 3f, cy + 1.5f),
						         new Vector2(cx - 8.5f + i * 3f, cy + 4f), col, 1f);
					// Large eye socket
					DrawArc(new Vector2(cx + 3f, cy - 1.5f), 2.5f, 0f, Mathf.Tau, 10, col, 1f);
					// Neck vertebrae leaving skull
					for (int i = 0; i < 3; i++)
						DrawRect(new Rect2(cx - 12.5f - i * 3f, cy + 1.5f + i * 2f, 2f, 1.8f), col);
					break;
				}
				case 2: // Human — top-down ribcage (burial pit view)
				{
					// Skull above spine
					DrawArc(new Vector2(cx, cy - 10f), 3f, 0f, Mathf.Tau, 12, col, 1f);
					DrawRect(new Rect2(cx - 2.5f, cy - 11f, 1.5f, 2f), col); // L eye
					DrawRect(new Rect2(cx + 1f,   cy - 11f, 1.5f, 2f), col); // R eye
					// Spine down centre
					DrawLine(new Vector2(cx, cy - 7f), new Vector2(cx, cy + 8f), col, 1.5f);
					// Rib pairs arching outward from spine
					float[] ribYb = { cy - 5f, cy - 2f, cy + 1f, cy + 4f };
					foreach (float ry in ribYb)
					{
						DrawLine(new Vector2(cx, ry), new Vector2(cx - 5f, ry - 2f), col, 1f);
						DrawLine(new Vector2(cx - 5f, ry - 2f), new Vector2(cx - 7f, ry + 2.5f), col, 1f);
						DrawLine(new Vector2(cx, ry), new Vector2(cx + 5f, ry - 2f), col, 1f);
						DrawLine(new Vector2(cx + 5f, ry - 2f), new Vector2(cx + 7f, ry + 2.5f), col, 1f);
					}
					break;
				}
				default: // Partial excavation — single large femur + scattered vertebrae
				{
					// Femur: long bone lying diagonally with rounded epiphyses
					var fA = new Vector2(cx - 9f, cy - 6f);
					var fB = new Vector2(cx + 7f, cy + 5f);
					DrawLine(fA, fB, col, 2.5f);
					DrawCircle(fA, 3f, col);   // femoral head
					DrawCircle(fB, 2.5f, col); // condyle
					// Scattered vertebrae (not crossing the femur)
					DrawCircle(new Vector2(cx + 5f, cy - 7f), 2f, col);
					DrawCircle(new Vector2(cx - 6f, cy + 7f), 1.8f, col);
					DrawCircle(new Vector2(cx + 9f, cy + 6f), 1.5f, col);
					break;
				}
			}
		}

		bool spectating = _client.FogRenderer?.SpectatorMode == true;
		foreach (var c in _client.Charges)
		{
			if (!_client.Fog.IsVisible(new GridPos(c.X, c.Y)) && !spectating) continue;
			float chargeScale = 0.60f;
			float chargePad   = ts * (1f - chargeScale) / 2f;
			var r = new Rect2(c.X * ts + chargePad, c.Y * ts + chargePad, ts * chargeScale, ts * chargeScale);
			if (_chargeTex != null)
				DrawTextureRect(_chargeTex, r, false);
			else
				DrawCircle(new Vector2(c.X * ts + ts / 2f, c.Y * ts + ts / 2f), ts * 0.25f, ChargeColor);

			// Animated fuse spark: jittering orange/yellow ember at fuse tip (top-centre of sprite).
			float tMs = (float)Time.GetTicksMsec();
			float flicker  = 0.5f + 0.5f * Mathf.Sin(tMs / 40f);
			float flicker2 = 0.5f + 0.5f * Mathf.Sin(tMs / 27f + 1.3f);
			float jx = Mathf.Sin(tMs / 33f) * 1.5f;
			float jy = Mathf.Sin(tMs / 41f) * 1.0f;
			var spark = new Vector2(c.X * ts + ts * 0.5f + jx, c.Y * ts + chargePad + ts * 0.07f + jy);
			DrawCircle(spark, 3.0f, new Color(1f, 0.35f, 0f, 0.50f + 0.38f * flicker));   // orange halo
			DrawCircle(spark, 1.8f, new Color(1f, 0.90f, 0.3f, 0.70f + 0.28f * flicker2)); // bright core
		}

		// Reel charges: red marker on wall tile + orange wire to owner.
		foreach (var rc in _client.ReelCharges)
		{
			if (!_client.Fog.IsVisible(new GridPos(rc.WallX, rc.WallY)) && !spectating) continue;
			var wallCenter = new Vector2(rc.WallX * ts + ts / 2f, rc.WallY * ts + ts / 2f);
			DrawCircle(wallCenter, ts * 0.22f, ReelChargeColor);
			DrawCircle(wallCenter, ts * 0.14f, new Color(1f, 0.85f, 0.1f, 0.9f));

			bool foundOwner = false;
			MinerSnapshot ownerSnap = default;
			foreach (var m in _client.Miners) if (m.Id == rc.OwnerId) { ownerSnap = m; foundOwner = true; break; }
			if (foundOwner && ownerSnap.Alive)
				DrawLine(wallCenter, _client.MinerVisualPos(ownerSnap.Id, ownerSnap.X, ownerSnap.Y), WireColor, 1.5f);
		}

		// Trip mines: only visible to the miner who planted them (or spectators).
		// Drawn as a small lying dynamite stick with a short fuse.
		if (_client.TripCharges != null)
		foreach (var tc in _client.TripCharges)
		{
			if (tc.OwnerId != _client.LocalMinerId && !spectating) continue;
			var tp = new GridPos(tc.X, tc.Y);
			if (!_client.Fog.IsVisible(tp) && !spectating) continue;

			float mx = tc.X * ts + ts * 0.5f;
			float my = tc.Y * ts + ts * 0.62f;
			float hw = ts * 0.18f;   // half-width of dynamite body
			float hh = ts * 0.07f;   // half-height

			// Body: red rectangle lying flat.
			DrawRect(new Rect2(mx - hw, my - hh, hw * 2f, hh * 2f), new Color(0.85f, 0.12f, 0.12f, 0.90f));
			// End cap: slightly darker band on left.
			DrawRect(new Rect2(mx - hw, my - hh, hw * 0.28f, hh * 2f), new Color(0.55f, 0.08f, 0.08f, 0.90f));
			// Fuse: thin line rising from right end.
			var fuseBase = new Vector2(mx + hw, my);
			var fuseTip  = new Vector2(mx + hw + ts * 0.10f, my - ts * 0.16f);
			DrawLine(fuseBase, fuseTip, new Color(0.75f, 0.65f, 0.35f, 0.95f), 1.5f);
			// Fuse spark: small orange dot at tip.
			DrawCircle(fuseTip, 2f, new Color(1f, 0.70f, 0.1f, 0.95f));
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
			else if (it.Kind == ItemKind.CrystalShard)
			{
				float shardGlowPx = ts * 1.4f;
				DrawTextureRect(_crystalGlowTex,
					new Rect2(icenter.X - shardGlowPx / 2f, icenter.Y - shardGlowPx / 2f, shardGlowPx, shardGlowPx),
					false, new Color(1f, 1f, 1f, 0.30f));
				if (_crystalShardTex != null)
					DrawTextureRect(_crystalShardTex, r, false);
				else
				{
					float hw = ts * 0.20f;
					DrawPolygon(
						new[] {
							new Vector2(icenter.X,       icenter.Y - hw),
							new Vector2(icenter.X + hw,  icenter.Y),
							new Vector2(icenter.X,       icenter.Y + hw),
							new Vector2(icenter.X - hw,  icenter.Y),
						},
						new[] { CrystalFacetB, CrystalFacetA, CrystalFacetC, CrystalFacetB });
					DrawCircle(icenter, hw * 0.5f, new Color(1f, 1f, 1f, 0.85f));
				}
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

		if (_client.Listening && _client.ListenTime >= 2.0f && TryLocalTile(out var lt))
		{
			float t = (float)Time.GetTicksMsec() / 1000f;
			float baseA = 0.18f + 0.22f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 0.8f));
			// Wave expands from closest tile outward over 1 second (stage 3: 2–3s).
			float wavePos = Mathf.Clamp(_client.ListenTime - 2.0f, 0f, 1.0f) * ListenItemRevealRadius;
			foreach (var it in _client.Items)
			{
				if (it.Placement != ItemPlacement.Buried) continue;
				int dist = Mathf.Max(Mathf.Abs(lt.X - it.X), Mathf.Abs(lt.Y - it.Y));
				if (dist > ListenItemRevealRadius) continue;
				float fade = Mathf.Clamp(wavePos - dist + 1f, 0f, 1f);
				if (fade <= 0f) continue;
				DrawShimmer(it.X, it.Y, ShimmerColor with { A = baseA * fade }, ts);
			}
			foreach (var d in _client.Decoys)
			{
				if (_client.Grid.Get(d) != TileType.Rock) continue;
				int dist = Mathf.Max(Mathf.Abs(lt.X - d.X), Mathf.Abs(lt.Y - d.Y));
				if (dist > ListenItemRevealRadius) continue;
				float fade = Mathf.Clamp(wavePos - dist + 1f, 0f, 1f);
				if (fade <= 0f) continue;
				DrawShimmer(d.X, d.Y, ShimmerColor with { A = baseA * fade }, ts);
			}
			// Scree tiles shimmer in their tier colour (amber / light-red / bright-red).
			for (int dy = -ListenItemRevealRadius; dy <= ListenItemRevealRadius; dy++)
			for (int dx = -ListenItemRevealRadius; dx <= ListenItemRevealRadius; dx++)
			{
				int nx = lt.X + dx, ny = lt.Y + dy;
				var gp = new GridPos(nx, ny);
				if (!_client.Grid.InBounds(gp)) continue;
				Color screeCol;
				switch (_client.Grid.Get(gp))
				{
					case TileType.ScreeRock:    screeCol = ScreeColor;    break;
					case TileType.UnstableRock: screeCol = UnstableColor; break;
					case TileType.VolatileRock: screeCol = VolatileColor; break;
					default: continue;
				}
				int dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
				float fade = Mathf.Clamp(wavePos - dist + 1f, 0f, 1f);
				if (fade <= 0f) continue;
				DrawShimmer(nx, ny, screeCol with { A = baseA * fade }, ts);
			}
		}

		foreach (var (pos, life) in _flashes)
		{
			var col = FlashColor with { A = Mathf.Clamp(life / 0.4f, 0f, 1f) };
			DrawRect(new Rect2(pos.X * ts, pos.Y * ts, ts, ts), col);
		}

		// Expanding shockwave ring — grows from 0 to maxR over 0.5 s, fading as it goes.
		const float RingDuration = 0.5f;
		foreach (var (center, maxR, life) in _rings)
		{
			float progress = 1f - life / RingDuration;           // 0 → 1
			float radius   = progress * maxR;
			float alpha    = Mathf.Pow(1f - progress, 0.6f);     // fast fade at edge
			// Outer bright ring
			DrawArc(center, radius, 0f, Mathf.Tau, 64,
				new Color(1f, 0.80f, 0.25f, alpha * 0.85f), 5f);
			// Inner softer halo — slightly smaller, whiter, thinner
			DrawArc(center, radius * 0.75f, 0f, Mathf.Tau, 48,
				new Color(1f, 1f, 0.70f, alpha * 0.40f), 3f);
		}

		// Rockfall dust burst — an earthy circle covering the collapse zone, fading over 0.6 s.
		const float DustDuration = 0.6f;
		foreach (var (dcenter, dradius, dlife) in _rockfallDusts)
		{
			float alpha = Mathf.Clamp(dlife / DustDuration, 0f, 1f) * 0.45f;
			var wc = new Vector2(dcenter.X * ts + ts / 2f, dcenter.Y * ts + ts / 2f);
			DrawCircle(wc, dradius * ts + ts * 0.5f, RockfallDustColor with { A = alpha });
		}

		// Lantern light: radial amber glow centered on each active lantern source
		int glowPx = 5 * ts * 2;
		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.Held != (int)ItemKind.Lantern) continue;
			if (!_client.Fog.IsVisible(new GridPos(m.X, m.Y)) && !spectating) continue;
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

		// Crystal shard halo: soft cyan ring around any miner holding a shard
		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.Held != (int)ItemKind.CrystalShard) continue;
			if (!_client.Fog.IsVisible(new GridPos(m.X, m.Y)) && !spectating) continue;
			var center = _client.MinerVisualPos(m.Id, m.X, m.Y);
			DrawCircle(center, ts * 0.55f, new Color(0.45f, 0.20f, 1.0f, 0.22f));
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
					int slimeFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
					var tex = _slimeWalkTex[mo.Facing, slimeFrame] ?? _slimeTex[mo.Facing];
					if (tex != null)
					{
						float ss = ts * 1.3f;
						DrawTextureRect(tex, new Rect2(c.X - ss / 2f, c.Y - ss / 2f, ss, ss), false);
					}
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
				case MonsterKind.SkeletonHuman:
				{
					if (mo.Dormant)
					{
						if (_bonesPileTex != null)
							DrawTextureRect(_bonesPileTex, new Rect2(c.X - ts / 2f, c.Y - ts / 2f, ts, ts), false);
						else
							DrawCircle(c, ts * 0.20f, Colors.White);
					}
					else
					{
						int dir   = mo.Facing;
						int frame = (int)(Time.GetTicksMsec() / 175u) % 4;
						var tex   = _skeletonHumanWalkTex[dir, frame] ?? _skeletonHumanTex[dir];
						if (tex != null)
						{
							float ss = ts * 1.2f;
							DrawTextureRect(tex, new Rect2(c.X - ss / 2f, c.Y - ss / 2f, ss, ss), false);
						}
						else
							DrawCircle(c, ts * 0.28f, Colors.White);
					}
					break;
				}
				case MonsterKind.SkeletonDino:
				{
					if (mo.Dormant)
					{
						if (_bonesPileTex != null)
							DrawTextureRect(_bonesPileTex, new Rect2(c.X - ts / 2f, c.Y - ts / 2f, ts, ts), false);
						else
							DrawCircle(c, ts * 0.24f, Colors.White);
					}
					else
					{
						int dir   = mo.Facing;
						int frame = (int)(Time.GetTicksMsec() / 175u) % 4;
						var tex   = _skeletonDinoWalkTex[dir, frame] ?? _skeletonDinoTex[dir];
						if (tex != null)
						{
							float ss = ts * 1.5f;
							DrawTextureRect(tex, new Rect2(c.X - ss / 2f, c.Y - ss / 2f, ss, ss), false);
						}
						else
							DrawCircle(c, ts * 0.32f, Colors.White);
					}
					break;
				}
				case MonsterKind.WaterSnake:
				{
					int snakeFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
					var snakeTex = _waterSnakeWalkTex[mo.Facing, snakeFrame] ?? _waterSnakeTex[mo.Facing];
					if (snakeTex != null)
					{
						// Elongate along the movement axis so the snake reads as low and horizontal.
						bool ew = mo.Facing == 1 || mo.Facing == 3;
						float sw = ew ? ts * 1.9f : ts * 0.80f;
						float sh = ew ? ts * 0.80f : ts * 1.9f;
						DrawTextureRect(snakeTex, new Rect2(c.X - sw / 2f, c.Y - sh / 2f, sw, sh), false);
					}
					else
					{
						DrawCircle(c, ts * 0.30f, WaterSnakeColor);
						DrawCircle(c + fwd * 1.2f, ts * 0.14f, WaterSnakeColor);
					}
					break;
				}
			}
		}

		if (_client.Octopus is { } octSnap)
		{
			float bs = ts * 2f;
			var br = new Rect2(octSnap.X * ts - ts / 2f, octSnap.Y * ts - ts / 2f, bs, bs);
			int octFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
			var octTex = _octopusIdleTex[octFrame] ?? _octopusIdleTex[0];
			if (octTex != null)
				DrawTextureRect(octTex, br, false);
			else
			{
				DrawRect(new Rect2(octSnap.X * ts, octSnap.Y * ts, ts, ts), OctopusColor);
				var font = ThemeDB.FallbackFont;
				DrawString(font, new Vector2(octSnap.X * ts + ts / 2f, octSnap.Y * ts + ts * 0.65f),
					"✦", HorizontalAlignment.Center, -1, ts * 2 / 3, Colors.White);
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

		// Reach Center: reveal the goal tile only once the local player has collected their 5 gold.
		if (NetworkManager.Instance.MatchMode == GameMode.ReachCenter && _client.CenterTile is { } centerTile)
		{
			bool hasGold = false;
			foreach (var m in _client.Miners)
				if (m.Id == _client.LocalMinerId) { hasGold = m.Gold >= 5; break; }

			if (hasGold)
			{
				var cp = new GridPos(centerTile.X, centerTile.Y);
				if (_client.Fog.IsExplored(cp) || spectating)
				{
					float cx = centerTile.X * ts, cy = centerTile.Y * ts;
					float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.003f);
					var railCol = new Color(0.95f, 0.80f, 0.15f, 0.60f + 0.35f * pulse);

					// Horizontal rails (top + bottom of tile).
					DrawRect(new Rect2(cx + 3, cy + 4,  ts - 6, 3), railCol);
					DrawRect(new Rect2(cx + 3, cy + ts - 7, ts - 6, 3), railCol);

					// Four vertical bars.
					for (int b = 0; b < 4; b++)
					{
						float bx = cx + 5 + b * 6;
						DrawRect(new Rect2(bx, cy + 4, 2.5f, ts - 8), railCol);
					}

					// Pulsing star — bright gold, signals "go here now".
					var font = ThemeDB.FallbackFont;
					DrawString(font, new Vector2(cx + ts / 2f, cy - 2f),
						"★", HorizontalAlignment.Center, -1, ts - 4,
						new Color(1f, 0.92f, 0.2f, 0.70f + 0.30f * pulse));
				}
			}
		}

		// Placed TreasureChests (treasure hunt mode)
		if (_client.PlacedChests != null)
			foreach (var pc in _client.PlacedChests)
			{
				if (!_client.Fog.IsVisible(new GridPos(pc.X, pc.Y)) && !spectating) continue;
				var pcRect   = new Rect2(pc.X * ts, pc.Y * ts, ts, ts);
				var pcCenter = new Vector2(pc.X * ts + ts / 2f, pc.Y * ts + ts / 2f);
				if (_itemTex.TryGetValue(ItemKind.TreasureChest, out var pctex))
					DrawTextureRect(pctex, pcRect, false);
				else
				{
					DrawRect(pcRect, TreasureChestColor);
					var font = ThemeDB.FallbackFont;
					DrawString(font, new Vector2(pcCenter.X, pc.Y * ts + ts * 0.68f),
						"⬆", HorizontalAlignment.Center, -1, ts * 2 / 3, Colors.Black);
				}
			}

		// Idol floor items: draw name label so players know what they found
		foreach (var it in _client.Items)
		{
			if (!it.Kind.IsIdol() || it.Placement == ItemPlacement.Buried) continue;
			var ip = new GridPos(it.X, it.Y);
			if (!_client.Fog.IsVisible(ip)) continue;
			var ir = new Rect2(it.X * ts, it.Y * ts, ts, ts);
			var icenter = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);
			if (_itemTex.TryGetValue(it.Kind, out var idoltex))
				DrawTextureRect(idoltex, ir, false);
			else
			{
				DrawCircle(icenter, ts * 0.30f, IdolColor);
				var font = ThemeDB.FallbackFont;
				string label = IdolShortName(it.Kind);
				DrawString(font, new Vector2(icenter.X, it.Y * ts + ts * 0.68f),
					label, HorizontalAlignment.Center, -1, 9, Colors.Black);
			}
		}

		// Shopkeeper tile — arabian oil lamp sprite
		if (_client.ShopPos is GridPos sp && _client.Fog.IsVisible(sp))
		{
			float glow = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.003f);
			DrawRect(new Rect2(sp.X * ts, sp.Y * ts, ts, ts),
				new Color(0.55f, 0.42f, 0.05f, 0.20f + 0.12f * glow));
			var shopRect = new Rect2(sp.X * ts, sp.Y * ts, ts, ts);
			if (_shopLampTex != null)
				DrawTextureRect(_shopLampTex, shopRect, false);
		}

		// Trip mines: pulsing red-orange ring on each trapped floor tile.
		if (_client.TripCharges != null)
		{
			float pulse = 0.55f + 0.45f * Mathf.Sin((float)(Time.GetTicksMsec() * 0.004));
			foreach (var tc in _client.TripCharges)
			{
				var tp = new GridPos(tc.X, tc.Y);
				if (!_client.Fog.IsVisible(tp) && _client.FogRenderer?.SpectatorMode != true) continue;
				float alpha = (tc.OwnerId == _client.LocalMinerId ? 0.85f : 0.45f) * pulse;
				var center = new Vector2(tc.X * ts + ts / 2f, tc.Y * ts + ts / 2f);
				DrawArc(center, ts * 0.38f, 0, Mathf.Tau, 32, new Color(1f, 0.30f, 0f, alpha), 2.5f);
				DrawArc(center, ts * 0.20f, 0, Mathf.Tau, 16, new Color(1f, 0.60f, 0f, alpha * 0.6f), 1.5f);
			}
		}

		// Pending rock falls: growing dark shadow circle, full opacity at impact.
		if (_client.PendingFalls != null)
			foreach (var pf in _client.PendingFalls)
			{
				var pfp = new GridPos(pf.X, pf.Y);
				if (!_client.Fog.IsVisible(pfp) && _client.FogRenderer?.SpectatorMode != true) continue;
				float radius = ts * 0.46f * pf.FractionElapsed;
				var center = new Vector2(pf.X * ts + ts / 2f, pf.Y * ts + ts / 2f);
				DrawCircle(center, radius, new Color(0f, 0f, 0f, 0.60f));
				DrawArc(center, radius, 0, Mathf.Tau, 32, new Color(0.4f, 0.2f, 0f, 0.8f), 1.5f);
			}

		// Stun stars: three yellow dots orbiting above stunned miners / Goat.
		double stunNow = Time.GetTicksMsec() / 1000.0;
		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.StunRemaining <= 0f) continue;
			var mp = new GridPos(m.X, m.Y);
			if (!_client.Fog.IsVisible(mp) && _client.FogRenderer?.SpectatorMode != true) continue;
			DrawStunStars(_client.MinerVisualPos(m.Id, m.X, m.Y), stunNow, ts);
		}
		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive || mo.Kind != MonsterKind.Goat || mo.StunRemaining <= 0f) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp) && _client.FogRenderer?.SpectatorMode != true) continue;
			DrawStunStars(_client.MonsterVisualPos(mo.Id, mo.X, mo.Y), stunNow, ts);
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
		_ when kind.IsIdol()  => IdolColor,
		_                     => SpeedItemColor,
	};

	private static bool IsWater(TileGrid grid, int x, int y)
	{
		var p = new GridPos(x, y);
		return grid.InBounds(p) && grid.Get(p).IsWater();
	}

	private static bool IsDeepWater(TileGrid grid, int x, int y)
	{
		var p = new GridPos(x, y);
		return grid.InBounds(p) && grid.Get(p) == TileType.DeepWater;
	}

	// Builds a clockwise polygon for a water tile with rounded outer corners.
	// Each outer corner (both orthogonal neighbours of the same water class absent) is replaced
	// with a circular arc, giving smooth rounded edges instead of hard right angles.
	private static Vector2[] WaterTilePoly(float wx0, float wy0, float ts,
		bool nAdj, bool sAdj, bool wAdj, bool eAdj)
	{
		float r   = ts * 0.28f;
		const int ArcN = 5;
		var pts = new List<Vector2>(32);
		bool nw = !nAdj && !wAdj, ne = !nAdj && !eAdj;
		bool se = !sAdj && !eAdj, sw = !sAdj && !wAdj;
		// Each corner either emits one sharp point or an arc of ArcN+1 points.
		// Arc centres sit at the inset corner; arcs bow toward the actual corner,
		// cutting water away to create the rounded boundary.
		if (nw) WaterArc(pts, wx0 + r,      wy0 + r,      r, Mathf.Pi,          Mathf.Pi * 1.5f, ArcN);
		else    pts.Add(new Vector2(wx0,      wy0));
		if (ne) WaterArc(pts, wx0 + ts - r, wy0 + r,      r, -Mathf.Pi * 0.5f, 0f,              ArcN);
		else    pts.Add(new Vector2(wx0 + ts, wy0));
		if (se) WaterArc(pts, wx0 + ts - r, wy0 + ts - r, r, 0f,                Mathf.Pi * 0.5f, ArcN);
		else    pts.Add(new Vector2(wx0 + ts, wy0 + ts));
		if (sw) WaterArc(pts, wx0 + r,      wy0 + ts - r, r, Mathf.Pi * 0.5f,  Mathf.Pi,        ArcN);
		else    pts.Add(new Vector2(wx0,      wy0 + ts));
		return pts.ToArray();
	}

	private static void WaterArc(List<Vector2> pts, float cx, float cy, float r,
		float fromA, float toA, int n)
	{
		for (int k = 0; k <= n; k++)
		{
			float a = fromA + (toA - fromA) * k / n;
			pts.Add(new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)));
		}
	}

	private void EnsureWaterCache()
	{
		if (_waterTiles != null) return;
		var grid = _client.Grid;
		int ts = MatchClient.TileSize;
		var tiles = new List<GridPos>();
		var polys = new List<Vector2[]>();
		foreach (var p in grid.Positions())
		{
			var wt = grid.Get(p);
			if (!wt.IsWater()) continue;
			bool deep = wt == TileType.DeepWater;
			bool nAdj = deep ? IsDeepWater(grid, p.X, p.Y - 1) : IsWater(grid, p.X, p.Y - 1);
			bool sAdj = deep ? IsDeepWater(grid, p.X, p.Y + 1) : IsWater(grid, p.X, p.Y + 1);
			bool wAdj = deep ? IsDeepWater(grid, p.X - 1, p.Y) : IsWater(grid, p.X - 1, p.Y);
			bool eAdj = deep ? IsDeepWater(grid, p.X + 1, p.Y) : IsWater(grid, p.X + 1, p.Y);
			tiles.Add(p);
			polys.Add(WaterTilePoly(p.X * ts, p.Y * ts, ts, nAdj, sAdj, wAdj, eAdj));
		}
		_waterTiles = tiles.ToArray();
		_waterPolys = polys.ToArray();
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

	private static string IdolShortName(ItemKind k) => k switch
	{
		ItemKind.IdolVishnu       => "Vis",
		ItemKind.IdolZeus         => "Zeus",
		ItemKind.IdolAnubis       => "Anu",
		ItemKind.IdolOdin         => "Odin",
		ItemKind.IdolShiva        => "Shv",
		ItemKind.IdolBuddha       => "Bud",
		ItemKind.IdolRa           => "Ra",
		ItemKind.IdolQuetzalcoatl => "Qtz",
		ItemKind.IdolUrn          => "Urn",
		ItemKind.IdolLamp         => "Lmp",
		ItemKind.IdolMace         => "Mce",
		ItemKind.IdolSceptre      => "Scp",
		ItemKind.IdolGlobe        => "Glb",
		ItemKind.IdolTrophyCup    => "Trp",
		ItemKind.IdolChalice      => "Cha",
		ItemKind.IdolCrown        => "Crn",
		ItemKind.IdolSkull        => "Skl",
		_                         => "?",
	};

	// Three yellow dots orbiting above a stunned miner's/goat's head, rotating at 180°/s.
	private void DrawStunStars(Vector2 pos, double t, int ts)
	{
		float cx = pos.X;
		float cy = pos.Y - ts * 0.60f;
		float orbitR = ts * 0.22f;
		for (int i = 0; i < 3; i++)
		{
			float angle = (float)(t * Mathf.Pi) + i * Mathf.Tau / 3f;
			var dot = new Vector2(cx + Mathf.Cos(angle) * orbitR, cy + Mathf.Sin(angle) * orbitR * 0.5f);
			DrawCircle(dot, 2.5f, new Color(1f, 0.95f, 0.15f));
		}
	}

	private void DrawShimmer(int x, int y, Color col, int ts)
	{
		var c = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
		DrawCircle(c, ts * 0.42f, col);
	}

	// Draws prominent diagonal gold ore veins on a GoldRock tile.
	// Two veins per tile — direction and offset are deterministic from tile position.
	private void DrawGoldVeins(int tx, int ty, int ts)
	{
		uint h = (uint)(tx * 73856093 ^ ty * 19349663);
		float x0 = tx * ts, y0 = ty * ts;

		// Primary vein — diagonal, direction and inset randomised per tile
		bool nw2se = (h & 1u) == 0;
		float inA = 5f + (h >> 2 & 7u);   // 5–12 px inset at start
		float inB = 5f + (h >> 6 & 7u);   // 5–12 px inset at end
		var va = nw2se ? new Vector2(x0 + inA, y0 + inA)           : new Vector2(x0 + ts - inA, y0 + inA);
		var vb = nw2se ? new Vector2(x0 + ts - inB, y0 + ts - inB) : new Vector2(x0 + inB,      y0 + ts - inB);
		DrawLine(va, vb, new Color(0.90f, 0.72f, 0.04f, 0.55f), 2.0f);

		// Secondary short vein — only drawn on ~60 % of tiles to add variety
		if ((h >> 10 & 3u) != 0)
		{
			float ox = 6f + (h >> 4 & 7u);
			float oy = 4f + (h >> 8 & 5u);
			var vc = new Vector2(x0 + ox,          y0 + ts * 0.30f + oy * 0.5f);
			var vd = new Vector2(x0 + ts - ox - 2, y0 + ts * 0.65f + oy * 0.3f);
			DrawLine(vc, vd, new Color(0.80f, 0.65f, 0.06f, 0.40f), 1.5f);
		}
	}
}
