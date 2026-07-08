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
	public IReadOnlyList<ReelChargeSnapshot> ReelCharges => _reelChargeSnaps;
	public bool EscapeOpen { get; private set; }
	public GridPos? EscapeTile { get; private set; }
	public GridPos? ShopPos { get; private set; }
	public GridPos? CenterTile { get; private set; }
	public GridPos? ExpeditionTreasurePos { get; private set; }
	public int GoldRemaining { get; private set; }
	public Vector2 MonsterVisualPos(int id, int x, int y) =>
		_monsterVisualPos.TryGetValue(id, out var v)
			? v : new Vector2(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);
	public Vector2 MinerVisualPos(int id, int x, int y) =>
		_visualPos.TryGetValue(id, out var v)
			? v : new Vector2(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);
	public int LocalMinerId { get; private set; }
	public bool Listening;  // set by Main each frame; gates the buried-item shimmer
	public float ListenTime; // seconds held this press; reset to 0 when not listening
	public IReadOnlyList<GridPos> Decoys { get; private set; } = System.Array.Empty<GridPos>();
	public float SecondsRemaining { get; private set; } = -1f;
	public int Lives { get; private set; } = 3;
	public event System.Action<Vector2>? Exploded; // world position of a detonation

	private List<MinerSnapshot> _miners = new();
	private List<ChargeSnapshot> _charges = new();
	private List<ItemSnapshot> _items = new();
	private List<MoldSnapshot> _molds = new();
	private List<MonsterSnapshot> _monsters = new();
	private List<ReelChargeSnapshot> _reelChargeSnaps = new();
	private readonly Dictionary<int, Vector2> _monsterVisualPos = new(); // monsterId -> smoothed pixels
	private readonly Dictionary<int, Vector2> _visualPos = new(); // minerId -> smoothed pixels

	private Node2D _sceneRoot = null!;
	public int StartingGoldCount { get; private set; }
	public OctopusSnapshot? Octopus { get; private set; }
	public IReadOnlyList<TreasureProgressSnapshot>? TreasureProgress { get; private set; }
	public IReadOnlyList<PlacedChestSnapshot>?      PlacedChests     { get; private set; }
	public IReadOnlyList<TripChargeSnapshot>?        TripCharges      { get; private set; }
	public IReadOnlyList<PendingFallSnapshot>?       PendingFalls     { get; private set; }
	public FogRenderer? FogRenderer { get; private set; }

	private TerrainMap _terrainMap = null!;
	private WorldRenderer _world = null!;
	private FogRenderer _fogRenderer = null!;
	private Node2D _camera = null!;
	private Camera2D _cam = null!;
	private Texture2D[,]?  _minerTex;       // [colorIndex 0-7, facing 0=N 1=E 2=S 3=W]
	private Texture2D[,]?  _minerListenTex; // [colorIndex, facing] — null if sprites not yet placed
	private Texture2D[,]?  _minerIdleTex;   // [colorIndex, idleIdx] south-facing only; null until art placed
	private Texture2D[,,]? _minerWalkTex;  // [colorIndex, facing, frame 0-3]
	private Texture2D[,,]? _minerMineTex;  // [colorIndex, facing, frame 0-6]
	private Texture2D[,,]? _minerPlantTex; // [colorIndex, facing, frame 0-4]
	private readonly Dictionary<int, (int X, int Y)> _lastMinerPos = new();
	private readonly Dictionary<int, double> _walkUntil = new();

	private HashSet<GridPos>? _crystalPositions;
	private const int CrystalWallRadius       = 5;
	private const int CrystalShardFloorRadius = 2;
	private const int CrystalShardHeldRadius  = 3;

	// Random idle fidget state (local miner only)
	private const int   IdleVariantCount  = 3;
	private const float IdleMinWait       = 5f;
	private const float IdleMaxWait       = 12f;
	private const float IdleShowDuration  = 2.0f;
	private float _idleTime;       // seconds of continuous true-idle
	private float _idleCountdown  = 6f;
	private float _idleShowTimer;  // counts down while pose is displayed
	private int   _currentIdleIdx = -1;
	private readonly System.Random _idleRng = new();

	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot, GridPos? escapeTile = null, GridPos? shopPos = null, GridPos? centerTile = null, GridPos? expeditionTreasurePos = null)
	{
		_sceneRoot = sceneRoot;
		Grid = grid;
		LocalMinerId = localMinerId;
		Decoys = decoys;
		EscapeTile = escapeTile;
		ShopPos = shopPos;
		CenterTile = centerTile;
		ExpeditionTreasurePos = expeditionTreasurePos;
		GoldRemaining = CountGold(grid);
		StartingGoldCount = GoldRemaining;

		_terrainMap = new TerrainMap { Name = "TerrainMap", ZIndex = -10 };
		sceneRoot.AddChild(_terrainMap);
		_terrainMap.Init(this);
		_terrainMap.SetFloor(NetworkManager.Instance.MatchFloor);

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -9 };
		sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);
		FogRenderer = _fogRenderer;

		_minerTex       = BuildMinerTextures();
		_minerListenTex = BuildMinerListenTextures();
		_minerIdleTex   = BuildMinerIdleTextures();
		_minerWalkTex   = BuildMinerWalkTextures();
		_minerMineTex   = BuildMinerActivityTextures("mine",  7);
		_minerPlantTex  = BuildMinerActivityTextures("plant", 5);

		_camera = new Node2D { Name = "CameraRig" };
		sceneRoot.AddChild(_camera);
		_cam = new Camera2D { Zoom = new Vector2(2.5f, 2.5f) };
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
			if (Grid.InBounds(p))
			{
				Grid.Set(p, t.NewType);
				if (t.NewType == TileType.Floor && _crystalPositions != null)
					_crystalPositions.Remove(p);
			}
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
			float maxR = 0f;
			foreach (var t in update.TileChanges)
				if (t.FromBlast)
				{
					float d = c.DistanceTo(new Vector2(t.X * TileSize + TileSize / 2f, t.Y * TileSize + TileSize / 2f));
					if (d > maxR) maxR = d;
				}
			_world?.AddExplosionRing(c, maxR + TileSize * 0.7f);
			Exploded?.Invoke(c);
		}

		_terrainMap?.UpdateTiles(update.TileChanges);
		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		_items = new List<ItemSnapshot>(update.Snapshot.Items);
		_molds = new List<MoldSnapshot>(update.Snapshot.Molds);
		_monsters = new List<MonsterSnapshot>(update.Snapshot.Monsters);
		bool wasOpen = EscapeOpen;
		EscapeOpen = update.Snapshot.EscapeOpen;
		if (!wasOpen && EscapeOpen) ExpeditionTreasurePos = null;
		GoldRemaining = CountGold(Grid);
		SecondsRemaining = update.Snapshot.SecondsRemaining;
		Octopus          = update.Snapshot.Octopus;
		Lives            = update.Snapshot.Lives;
		TreasureProgress = update.Snapshot.TreasureProgress;
		PlacedChests     = update.Snapshot.PlacedChests;
		TripCharges      = update.Snapshot.TripCharges;
		PendingFalls     = update.Snapshot.PendingFalls;
		_reelChargeSnaps = new List<ReelChargeSnapshot>(update.Snapshot.ReelCharges ?? System.Array.Empty<ReelChargeSnapshot>());
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
		if (floor == 51)
		{
			newMap = MapGenerator.GenerateBossFloor(floorSeed);
		}
		else
		{
			var mapCfg = MapConfig.FloorConfig(floor, floorSeed, nm.MatchPlayerCount);
			FloorModifiers.Apply(FloorModifiers.Pick(nm.MatchSeed, floor), mapCfg, new SimConfig());
			newMap = MapGenerator.Generate(mapCfg);
		}
		EscapeTile            = newMap.EscapeTile;
		ShopPos               = newMap.ShopPos;
		ExpeditionTreasurePos = newMap.ExpeditionTreasurePos;

		Grid              = newMap.Grid;
		Decoys            = newMap.Decoys;
		GoldRemaining     = CountGold(newMap.Grid);
		StartingGoldCount = GoldRemaining;
		EscapeOpen        = false;
		Octopus           = null;

		Fog.Reset();
		_crystalPositions = null;
		_visualPos.Clear();
		_monsterVisualPos.Clear();
		_miners.Clear();
		_monsters.Clear();

		_terrainMap = new TerrainMap { Name = "TerrainMap", ZIndex = -10 };
		_sceneRoot.AddChild(_terrainMap);
		_terrainMap.Init(this);
		_terrainMap.SetFloor(floor);

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -9 };
		_sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		_sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);
		FogRenderer = _fogRenderer;
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
		bool foundLocal = false;
		bool localAlive = false;
		Vector2 localVisualPos = Vector2.Zero;
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
			{
				foundLocal = true;
				localAlive = m.Alive;
				localVisualPos = _visualPos[m.Id];

				// Random idle fidgets: only when truly standing still
				bool trulyIdle = m.Alive && m.Activity == 0
					&& !(_walkUntil.TryGetValue(m.Id, out double wu) && now < wu)
					&& !Listening && _minerIdleTex != null;

				if (_idleShowTimer > 0)
				{
					_idleShowTimer -= (float)delta;
					if (_idleShowTimer <= 0) _currentIdleIdx = -1;
				}

				if (trulyIdle)
				{
					_idleTime += (float)delta;
					if (_idleTime >= _idleCountdown && _currentIdleIdx < 0)
					{
						_currentIdleIdx = _idleRng.Next(IdleVariantCount);
						_idleShowTimer  = IdleShowDuration;
						_idleTime       = 0f;
						_idleCountdown  = IdleMinWait + (float)_idleRng.NextDouble() * (IdleMaxWait - IdleMinWait);
					}
				}
				else
				{
					_idleTime = 0f;
					if (_idleShowTimer <= 0) _currentIdleIdx = -1;
				}
			}
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

		// Camera and fog: follow local miner when alive; reveal full map when dead.
		if (!foundLocal)
		{
			// No snapshot yet — leave camera where it is.
		}
		else if (localAlive && _cam != null)
		{
			_camera.Position = localVisualPos;
			_cam.Zoom = _cam.Zoom.Lerp(new Vector2(2.5f, 2.5f), Mathf.Min(1f, (float)delta * 4f));
			if (_fogRenderer != null) _fogRenderer.SpectatorMode = false;
		}
		else if (_cam != null && NetworkManager.Instance.MatchMode != GameMode.Expedition)
		{
			// Dead — ease to a browsable zoom; camera stays near death position.
			// Expedition uses its own black-fade overlay and respawns the miner, so we leave it alone.
			_cam.Zoom = _cam.Zoom.Lerp(new Vector2(2.0f, 2.0f), Mathf.Min(1f, (float)delta * 2.5f));
			if (_fogRenderer != null) _fogRenderer.SpectatorMode = true;

			// WASD/arrow keys scroll the camera; clamp to map bounds.
			float scrollSpeed = TileSize * 10f;
			var scroll = Vector2.Zero;
			if (Input.IsActionPressed(InputBindings.MoveUp))    scroll.Y -= 1f;
			if (Input.IsActionPressed(InputBindings.MoveDown))  scroll.Y += 1f;
			if (Input.IsActionPressed(InputBindings.MoveLeft))  scroll.X -= 1f;
			if (Input.IsActionPressed(InputBindings.MoveRight)) scroll.X += 1f;
			if (scroll != Vector2.Zero)
			{
				_camera.Position += scroll.Normalized() * scrollSpeed * (float)delta;
				float mapW = Grid.Width  * TileSize;
				float mapH = Grid.Height * TileSize;
				_camera.Position = new Vector2(
					Mathf.Clamp(_camera.Position.X, 0f, mapW),
					Mathf.Clamp(_camera.Position.Y, 0f, mapH));
			}
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		bool spectating = _fogRenderer?.SpectatorMode == true;
		foreach (var m in _miners)
		{
			if (!m.Alive) continue;
			// Hide rivals outside the local vision circle unless dead and spectating.
			if (m.Id != LocalMinerId && !spectating && !Fog.IsVisible(new GridPos(m.X, m.Y))) continue;
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
			else if ((m.Activity == 2 || m.Activity == 3) && m.ActivityRemaining > 0 && _minerPlantTex != null)
			{
				// Planting / PlantingDetonator: loop 4 animated frames (1-4) at 4 fps
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
				else if (Listening && m.Id == LocalMinerId && _minerListenTex != null)
				{
					tex = _minerListenTex[colorIdx, facing];
				}
				else if (_currentIdleIdx >= 0 && m.Id == LocalMinerId && _minerIdleTex != null)
				{
					tex = _minerIdleTex[colorIdx, _currentIdleIdx]; // south-facing fidget pose
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
			var img = GD.Load<CompressedTexture2D>(paths[d])?.GetImage();
			if (img != null) { img.Convert(Image.Format.Rgba8); srcs[d] = img; }
		}
		var tex = new Texture2D[PlayerColors.Palette.Length, 4];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int d = 0; d < 4; d++)
				if (srcs[d] != null)
					tex[c, d] = ImageTexture.CreateFromImage(TintMiner(srcs[d]!, PlayerColors.At(c)));
		return tex;
	}

	private static Texture2D[,]? BuildMinerListenTextures()
	{
		var dirLetter = new[] { "n", "e", "s", "w" };
		var srcs = new Image?[4];
		bool anyLoaded = false;
		for (int d = 0; d < 4; d++)
		{
			string path = $"res://assets/miners/listen/{dirLetter[d]}.png";
			var img = GD.Load<CompressedTexture2D>(path)?.GetImage();
			if (img != null) { img.Convert(Image.Format.Rgba8); srcs[d] = img; anyLoaded = true; }
		}
		if (!anyLoaded) return null;
		var tex = new Texture2D[PlayerColors.Palette.Length, 4];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int d = 0; d < 4; d++)
				if (srcs[d] != null)
					tex[c, d] = ImageTexture.CreateFromImage(TintMiner(srcs[d]!, PlayerColors.At(c)));
		return tex;
	}

	private static Texture2D[,]? BuildMinerIdleTextures()
	{
		var srcs = new Image?[IdleVariantCount];
		bool anyLoaded = false;
		for (int i = 0; i < IdleVariantCount; i++)
		{
			string path = $"res://assets/miners/idle/idle{i}.png";
			var img = GD.Load<CompressedTexture2D>(path)?.GetImage();
			if (img != null) { img.Convert(Image.Format.Rgba8); srcs[i] = img; anyLoaded = true; }
		}
		if (!anyLoaded) return null;
		var tex = new Texture2D[PlayerColors.Palette.Length, IdleVariantCount];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int i = 0; i < IdleVariantCount; i++)
				if (srcs[i] != null)
					tex[c, i] = ImageTexture.CreateFromImage(TintMiner(srcs[i]!, PlayerColors.At(c)));
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
				string path = $"res://assets/miners/walk/{dirLetter[d]}{f}.png";
				var img = GD.Load<CompressedTexture2D>(path)?.GetImage();
				if (img != null) { img.Convert(Image.Format.Rgba8); srcs[d, f] = img; }
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
				string path = $"res://assets/miners/{folder}/{dirLetter[d]}{f}.png";
				var img = GD.Load<CompressedTexture2D>(path)?.GetImage();
				if (img != null) { img.Convert(Image.Format.Rgba8); srcs[d, f] = img; }
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
				if (px.A < 0.05f) continue;
				float lum = 0.299f * px.R + 0.587f * px.G + 0.114f * px.B;
				// Preserve skin (warm peach) and yellow hard hat; tint dark clothing only.
				if (px.S > 0.3f && px.H < 0.25f && lum > 0.25f) continue;
				// Lift luminance floor so dark overalls show a vivid team colour, not near-black.
				float l = lum * 0.6f + 0.4f;
				img.SetPixel(x, y, new Color(tint.R * l, tint.G * l, tint.B * l, px.A));
			}
		return img;
	}

	private void UpdateFog()
	{
		foreach (var m in _miners)
		{
			if (m.Id != LocalMinerId || !m.Alive) continue;

			var visible = Visibility.Compute(Grid, new GridPos(m.X, m.Y), m.VisionRadius);

			if (_crystalPositions == null) BuildCrystalCache();
			foreach (var cp in _crystalPositions!)
				visible.UnionWith(Visibility.Compute(Grid, cp, CrystalWallRadius));

			foreach (var it in _items)
				if (it.Kind == ItemKind.CrystalShard && it.Placement == ItemPlacement.Loose)
					visible.UnionWith(Visibility.Compute(Grid, new GridPos(it.X, it.Y), CrystalShardFloorRadius));

			foreach (var mn in _miners)
				if (mn.Alive && mn.Held == (int)ItemKind.CrystalShard)
					visible.UnionWith(Visibility.Compute(Grid, new GridPos(mn.X, mn.Y), CrystalShardHeldRadius));

			Fog.Update(visible);
		}
	}

	private void BuildCrystalCache()
	{
		_crystalPositions = new HashSet<GridPos>();
		foreach (var p in Grid.Positions())
			if (Grid.Get(p) == TileType.CrystalRock)
				_crystalPositions.Add(p);
	}

	private static int CountGold(TileGrid grid)
	{
		int n = 0;
		foreach (var p in grid.Positions())
			if (grid.Get(p) == TileType.GoldRock) n++;
		return n;
	}
}
