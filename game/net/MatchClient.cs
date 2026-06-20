using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Render-side world replica. Holds the grid (regenerated from seed),
/// applies tile changes + entity snapshots, smooths miner visuals toward their
/// authoritative positions, and computes local fog from the local miner.</summary>
public partial class MatchClient : Node2D
{
	public const int TileSize = 32;

	public TileGrid Grid { get; private set; } = null!;
	public FogState Fog { get; } = new();
	public IReadOnlyList<MinerSnapshot> Miners => _miners;
	public IReadOnlyList<ChargeSnapshot> Charges => _charges;
	public IReadOnlyList<ItemSnapshot> Items => _items;
	public IReadOnlyList<MoldSnapshot> Molds => _molds;
	public IReadOnlyList<MonsterSnapshot> Monsters => _monsters;
	public bool EscapeOpen { get; private set; }
	public GridPos? EscapeTile { get; private set; }
	public GridPos? ShopPos { get; private set; }
	public int GoldRemaining { get; private set; }
	public Vector2 MonsterVisualPos(int id, int x, int y) =>
		_monsterVisualPos.TryGetValue(id, out var v)
			? v : new Vector2(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);
	public int LocalMinerId { get; private set; }
	public bool Listening; // set by Main each frame; gates the buried-item shimmer
	public IReadOnlyList<GridPos> Decoys { get; private set; } = System.Array.Empty<GridPos>();
	public float SecondsRemaining { get; private set; } = -1f;
	public int Lives { get; private set; } = 3;
	public event System.Action<Vector2>? Exploded; // world position of a detonation

	private List<MinerSnapshot> _miners = new();
	private List<ChargeSnapshot> _charges = new();
	private List<ItemSnapshot> _items = new();
	private List<MoldSnapshot> _molds = new();
	private List<MonsterSnapshot> _monsters = new();
	private readonly Dictionary<int, Vector2> _monsterVisualPos = new(); // monsterId -> smoothed pixels
	private readonly Dictionary<int, Vector2> _visualPos = new(); // minerId -> smoothed pixels

	private Node2D _sceneRoot = null!;
	public int StartingGoldCount { get; private set; }
	public OctopusSnapshot? Octopus { get; private set; }

	private TerrainMap _terrainMap = null!;
	private WorldRenderer _world = null!;
	private FogRenderer _fogRenderer = null!;
	private Node2D _camera = null!;
	private Camera2D _cam = null!;
	private Texture2D[,]?  _minerTex;      // [colorIndex 0-7, facing 0=N 1=E 2=S 3=W]
	private Texture2D[,,]? _minerWalkTex;  // [colorIndex, facing, frame 0-3]
	private Texture2D[,,]? _minerMineTex;  // [colorIndex, facing, frame 0-6]
	private Texture2D[,,]? _minerPlantTex; // [colorIndex, facing, frame 0-4]
	private readonly Dictionary<int, (int X, int Y)> _lastMinerPos = new();
	private readonly Dictionary<int, double> _walkUntil = new();

	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot, GridPos? escapeTile = null, GridPos? shopPos = null)
	{
		_sceneRoot = sceneRoot;
		Grid = grid;
		LocalMinerId = localMinerId;
		Decoys = decoys;
		EscapeTile = escapeTile;
		ShopPos = shopPos;
		GoldRemaining = CountGold(grid);
		StartingGoldCount = GoldRemaining;

		_terrainMap = new TerrainMap { Name = "TerrainMap", ZIndex = -10 };
		sceneRoot.AddChild(_terrainMap);
		_terrainMap.Init(this);

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -9 };
		sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);

		_minerTex      = BuildMinerTextures();
		_minerWalkTex  = BuildMinerWalkTextures();
		_minerMineTex  = BuildMinerActivityTextures("mine",  7);
		_minerPlantTex = BuildMinerActivityTextures("plant", 5);

		_camera = new Node2D { Name = "CameraRig" };
		sceneRoot.AddChild(_camera);
		_cam = new Camera2D { Zoom = new Vector2(2.0f, 2.0f) };
		_camera.AddChild(_cam);
		_cam.MakeCurrent();
	}

	public void ApplyUpdate(TickUpdate update)
	{
		float bx = 0f, by = 0f;
		int blastCount = 0;
		foreach (var t in update.TileChanges)
		{
			var p = new GridPos(t.X, t.Y);
			if (Grid.InBounds(p)) Grid.Set(p, t.NewType);
			if (t.FromBlast)
			{
				_world?.AddExplosionFlash(p);
				bx += t.X; by += t.Y; blastCount++;
			}
		}
		if (blastCount > 0)
		{
			var c = new Vector2(bx / blastCount * TileSize + TileSize / 2f,
								 by / blastCount * TileSize + TileSize / 2f);
			Exploded?.Invoke(c);
		}

		_terrainMap?.UpdateTiles(update.TileChanges);
		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		_items = new List<ItemSnapshot>(update.Snapshot.Items);
		_molds = new List<MoldSnapshot>(update.Snapshot.Molds);
		_monsters = new List<MonsterSnapshot>(update.Snapshot.Monsters);
		EscapeOpen = update.Snapshot.EscapeOpen;
		GoldRemaining = CountGold(Grid);
		SecondsRemaining = update.Snapshot.SecondsRemaining;
		Octopus = update.Snapshot.Octopus;
		Lives = update.Snapshot.Lives;
		UpdateFog();
	}

	public void ResetFloor(int floor)
	{
		_terrainMap?.QueueFree(); _terrainMap = null!;
		_world?.QueueFree();      _world = null!;
		_fogRenderer?.QueueFree(); _fogRenderer = null!;

		var nm = NetworkManager.Instance;
		int floorSeed = nm.MatchSeed + floor * 1000;

		GeneratedMap newMap;
		if (floor == 21)
		{
			newMap = MapGenerator.GenerateBossFloor(floorSeed);
		}
		else
		{
			var mapCfg = MapConfig.FloorConfig(floor, floorSeed, nm.MatchPlayerCount);
			FloorModifiers.Apply(FloorModifiers.Pick(nm.MatchSeed, floor), mapCfg, new SimConfig());
			newMap = MapGenerator.Generate(mapCfg);
		}
		EscapeTile = newMap.EscapeTile;
		ShopPos    = newMap.ShopPos;

		Grid              = newMap.Grid;
		Decoys            = newMap.Decoys;
		GoldRemaining     = CountGold(newMap.Grid);
		StartingGoldCount = GoldRemaining;
		EscapeOpen        = false;
		Octopus           = null;

		Fog.Reset();
		_visualPos.Clear();
		_monsterVisualPos.Clear();
		_miners.Clear();
		_monsters.Clear();

		_terrainMap = new TerrainMap { Name = "TerrainMap", ZIndex = -10 };
		_sceneRoot.AddChild(_terrainMap);
		_terrainMap.Init(this);

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -9 };
		_sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		_sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);
	}

	public override void _PhysicsProcess(double delta)
	{
		// Re-assert our camera after a scene swap: the previous scene's camera
		// teardown can clobber this one's MakeCurrent() (a Godot _Ready ordering
		// race), leaving the rig tracking the miner while the viewport stays at
		// world origin — the symptom is a tracked-but-unseen miner and a fixed
		// offset view. Cheap to check every frame and self-heals immediately.
		if (_cam != null && !_cam.IsCurrent())
			_cam.MakeCurrent();

		// Smooth each miner visual toward its authoritative tile position.
		double now = Time.GetTicksMsec() / 1000.0;
		foreach (var m in _miners)
		{
			var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
			var cur = _visualPos.TryGetValue(m.Id, out var v) ? v : target;
			float pixelsPerSec = TileSize / (float)m.MoveSeconds;
			_visualPos[m.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);

			if (_lastMinerPos.TryGetValue(m.Id, out var last) && (last.X != m.X || last.Y != m.Y))
				_walkUntil[m.Id] = now + m.MoveSeconds;
			_lastMinerPos[m.Id] = (m.X, m.Y);

			if (m.Id == LocalMinerId)
				_camera.Position = _visualPos[m.Id];
		}
		foreach (var mo in _monsters)
		{
			if (!mo.Alive) { _monsterVisualPos.Remove(mo.Id); continue; }
			var target = new Vector2(mo.X * TileSize + TileSize / 2f, mo.Y * TileSize + TileSize / 2f);
			var cur = _monsterVisualPos.TryGetValue(mo.Id, out var v) ? v : target;
			// Goat cadence is the fastest (~0.15s/tile); match it so no monster visually lags.
			float pixelsPerSec = TileSize / 0.15f;
			_monsterVisualPos[mo.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		foreach (var m in _miners)
		{
			if (!m.Alive) continue;
			var p = _visualPos.TryGetValue(m.Id, out var v) ? v : Vector2.Zero;

			float alpha = 1f;
			if (m.InvulRemaining > 0f)
			{
				float fraction = 1f - (m.InvulRemaining / 3f);
				float phase    = (float)(Time.GetTicksMsec() * 0.001 * 4.0) % 1f;
				alpha = phase < fraction ? 1f : 0.2f;
			}

			int colorIdx = MinerColorIndex(m.Id);
			int facing   = m.Facing;

			double drawNow = Time.GetTicksMsec() / 1000.0;
			Texture2D? tex;
			if (m.Activity == 1 && m.ActivityRemaining > 0 && _minerMineTex != null)
			{
				// Mining: loop 6 animated frames (1-6) at 6 fps
				int frame = (int)(drawNow * 6 % 6) + 1;
				tex = _minerMineTex[colorIdx, facing, frame];
			}
			else if (m.Activity == 2 && m.ActivityRemaining > 0 && _minerPlantTex != null)
			{
				// Planting: loop 4 animated frames (1-4) at 4 fps
				int frame = (int)(drawNow * 4 % 4) + 1;
				tex = _minerPlantTex[colorIdx, facing, frame];
			}
			else
			{
				bool walking = _walkUntil.TryGetValue(m.Id, out double until) && drawNow < until;
				if (walking && _minerWalkTex != null)
				{
					double elapsed = m.MoveSeconds - (until - drawNow);
					int frame = (int)(elapsed / m.MoveSeconds * 4) % 4;
					tex = _minerWalkTex[colorIdx, facing, frame];
				}
				else
				{
					tex = _minerTex?[colorIdx, facing];
				}
			}

			if (tex != null)
				DrawTextureRect(tex, new Rect2(p.X - 16, p.Y - 16, 32, 32), false, new Color(1, 1, 1, alpha));
			else
			{
				var col = PlayerColors.At(colorIdx);
				col.A = alpha;
				DrawRect(new Rect2(p.X - 10, p.Y - 10, 20, 20), col);
			}
		}
	}

	private static int MinerColorIndex(int minerId)
	{
		var nm = NetworkManager.Instance;
		int idx = minerId - 1;
		if (idx >= 0 && idx < nm.PeerOrder.Length &&
		    nm.Players.TryGetValue(nm.PeerOrder[idx], out var info))
			return info.ColorIndex;
		return idx % PlayerColors.Palette.Length;
	}

	private static Texture2D[,] BuildMinerTextures()
	{
		var paths = new[]
		{
			"res://assets/miners/miner_n.png",
			"res://assets/miners/miner_e.png",
			"res://assets/miners/miner_s.png",
			"res://assets/miners/miner_w.png",
		};
		var srcs = new Image?[4];
		for (int d = 0; d < 4; d++)
		{
			var img = new Image();
			if (img.Load(paths[d]) == Error.Ok) { img.Convert(Image.Format.Rgba8); srcs[d] = img; }
		}
		var tex = new Texture2D[PlayerColors.Palette.Length, 4];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int d = 0; d < 4; d++)
				if (srcs[d] != null)
					tex[c, d] = ImageTexture.CreateFromImage(TintMiner(srcs[d]!, PlayerColors.At(c)));
		return tex;
	}

	private static Texture2D[,,] BuildMinerWalkTextures()
	{
		// direction suffix (N=0,E=1,S=2,W=3) → folder letter
		var dirLetter = new[] { "n", "e", "s", "w" };
		var srcs = new Image?[4, 4]; // [facing, frame]
		for (int d = 0; d < 4; d++)
			for (int f = 0; f < 4; f++)
			{
				var img = new Image();
				string path = $"res://assets/miners/walk/{dirLetter[d]}{f}.png";
				if (img.Load(path) == Error.Ok) { img.Convert(Image.Format.Rgba8); srcs[d, f] = img; }
			}
		var tex = new Texture2D[PlayerColors.Palette.Length, 4, 4];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int d = 0; d < 4; d++)
				for (int f = 0; f < 4; f++)
					if (srcs[d, f] != null)
						tex[c, d, f] = ImageTexture.CreateFromImage(TintMiner(srcs[d, f]!, PlayerColors.At(c)));
		return tex;
	}

	private static Texture2D[,,] BuildMinerActivityTextures(string folder, int frameCount)
	{
		var dirLetter = new[] { "n", "e", "s", "w" };
		var srcs = new Image?[4, frameCount];
		for (int d = 0; d < 4; d++)
			for (int f = 0; f < frameCount; f++)
			{
				var img = new Image();
				string path = $"res://assets/miners/{folder}/{dirLetter[d]}{f}.png";
				if (img.Load(path) == Error.Ok) { img.Convert(Image.Format.Rgba8); srcs[d, f] = img; }
			}
		var tex = new Texture2D[PlayerColors.Palette.Length, 4, frameCount];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int d = 0; d < 4; d++)
				for (int f = 0; f < frameCount; f++)
					if (srcs[d, f] != null)
						tex[c, d, f] = ImageTexture.CreateFromImage(TintMiner(srcs[d, f]!, PlayerColors.At(c)));
		return tex;
	}

	private static Image TintMiner(Image src, Color tint)
	{
		var img = (Image)src.Duplicate();
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var px = img.GetPixel(x, y);
				float lum = 0.299f * px.R + 0.587f * px.G + 0.114f * px.B;
				img.SetPixel(x, y, new Color(tint.R * lum, tint.G * lum, tint.B * lum, px.A));
			}
		return img;
	}

	private void UpdateFog()
	{
		foreach (var m in _miners)
			if (m.Id == LocalMinerId && m.Alive)
				Fog.Update(Visibility.Compute(Grid, new GridPos(m.X, m.Y), m.VisionRadius));
	}

	private static int CountGold(TileGrid grid)
	{
		int n = 0;
		foreach (var p in grid.Positions())
			if (grid.Get(p) == TileType.GoldRock) n++;
		return n;
	}
}
