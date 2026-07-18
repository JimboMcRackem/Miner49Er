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
	public const double StoneFlightSeconds = 0.35;

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
	public event System.Action<Vector2, int>? ScreeCollapsed; // world position + collapse radius
	public event System.Action<Vector2>? Whistled; // world position of a bot whistle
		public event System.Action<Vector2, PortalKind>? Portaled; // world position + kind of a teleport
		public event System.Action<Vector2, Vector2>? StoneTossed; // origin, landing (world coords)
	public event System.Action<PrizeType, Vector2>? PrizeTelegraphed; // type + world position of an incoming prize
	public event System.Action<int, PrizeType>? PrizeWon; // miner id + type of a claimed prize
	public TreasureSnapshot? Treasure { get; private set; }
	public IReadOnlyList<HoldTimeSnapshot> HoldTimes { get; private set; } = System.Array.Empty<HoldTimeSnapshot>();
	public event System.Action<byte, int>? TreasureToast; // kind + miner id of a treasure event

		// Portals are client-derived from the deterministic map gen (never networked);
		// only the transient teleport event rides the wire. Track collapsed ends locally.
		public IReadOnlyList<PortalSpec> Portals { get; private set; } = System.Array.Empty<PortalSpec>();
		private readonly HashSet<int> _collapsedPortals = new();
		public bool IsPortalCollapsed(int id) => _collapsedPortals.Contains(id);

	private List<MinerSnapshot> _miners = new();
	private List<ChargeSnapshot> _charges = new();
	private List<ItemSnapshot> _items = new();
	private List<MoldSnapshot> _molds = new();
	private List<MonsterSnapshot> _monsters = new();
	private List<ReelChargeSnapshot> _reelChargeSnaps = new();
	private readonly Dictionary<int, Vector2> _monsterVisualPos = new(); // monsterId -> smoothed pixels
	private readonly Dictionary<int, Vector2> _visualPos = new(); // minerId -> smoothed pixels
	// Camera shake: trauma decays each frame; screen offset scales with trauma squared.
	private float _shakeTrauma;
	private readonly System.Random _shakeRng = new();
	private bool _localWasAlive;

	private Node2D _sceneRoot = null!;
	public int StartingGoldCount { get; private set; }
	public OctopusSnapshot? Octopus { get; private set; }
	public PrizeEventSnapshot? PrizeEvent { get; private set; }
	private byte _prevPrizeState;
	public IReadOnlyList<TreasureProgressSnapshot>? TreasureProgress { get; private set; }
	public IReadOnlyList<PlacedChestSnapshot>?      PlacedChests     { get; private set; }
	public IReadOnlyList<TripChargeSnapshot>?        TripCharges      { get; private set; }
	public IReadOnlyList<PendingFallSnapshot>?       PendingFalls     { get; private set; }
	public FogRenderer? FogRenderer { get; private set; }

	private TerrainMap _terrainMap = null!;
	private WorldRenderer _world = null!;
	private FogRenderer _fogRenderer = null!;
	private LightRenderer _lightRenderer = null!;
	private GhostOverlay _ghostOverlay = null!;
	// Flood tiles whose settled tilemap paint is held back while WorldRenderer animates the
	// water creeping in; flushed to the tilemap once the animation's duration has elapsed.
	private readonly List<(GridPos pos, TileChange change, float remaining)> _pendingFloodPaints = new();
	private Node2D _camera = null!;
	private Camera2D _cam = null!;
	// Tinted miner textures, built lazily one set per (variant, colour) pair actually in the match.
	private readonly System.Collections.Generic.Dictionary<(int variant, int color), MinerTextureSet> _minerSets = new();
	// Raw source images per variant, loaded once (with base fallback) then re-tinted per colour.
	private readonly System.Collections.Generic.Dictionary<int, MinerSourceImages> _variantSources = new();
	private readonly Dictionary<int, (int X, int Y)> _lastMinerPos = new();
	private readonly Dictionary<int, double> _walkUntil = new();
	private readonly Dictionary<int, double> _throwUntil = new();
	private int _lastLocalGold  = -1;
	private int _lastLocalIdols = -1;
	private readonly Dictionary<int, bool> _prevMinerAlive = new();

	private HashSet<GridPos>? _crystalPositions;
	private Dictionary<GridPos, HashSet<GridPos>>? _crystalLitByPos;  // per-crystal precomputed lit set
	private const int CrystalWallRadius       = 5;
	private const int CrystalShardFloorRadius = 2;
	private const int CrystalShardHeldRadius  = 3;
	public bool FogDirty { get; private set; }

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

		_lightRenderer = new LightRenderer { Name = "LightRenderer", ZIndex = -8 };
		sceneRoot.AddChild(_lightRenderer);
		_lightRenderer.Init(this);

		_ghostOverlay = new GhostOverlay { Name = "GhostOverlay", ZIndex = -7 };
		sceneRoot.AddChild(_ghostOverlay);
		_ghostOverlay.Init(_world);

		// Warm the tint cache for every (variant, colour) pair present at match start.
		// Any pair not covered here (edge cases) is built lazily on first draw.
		_minerSets.Clear();
		_variantSources.Clear();
		foreach (var info in NetworkManager.Instance.Players.Values)
			GetOrBuildSet(MinerVariants.Clamp(info.VariantIndex), info.ColorIndex);

		_camera = new Node2D { Name = "CameraRig" };
		sceneRoot.AddChild(_camera);
		_cam = new Camera2D { Zoom = new Vector2(2.5f, 2.5f) };
		_camera.AddChild(_cam);
		_cam.MakeCurrent();

		AddGlowEnvironment(sceneRoot);
	}

	// 2D bloom: a WorldEnvironment whose glow blooms only pixels brighter than 1.0. With HDR-2D
	// on (project setting), ordinary art stays crisp — only the emissive draws that deliberately
	// push their colour past 1.0 (lava, crystals, lantern, explosions, gold) light up.
	private void AddGlowEnvironment(Node sceneRoot)
	{
		if (sceneRoot.GetNodeOrNull("GlowEnv") != null) return;
		var env = new Godot.Environment
		{
			BackgroundMode   = Godot.Environment.BGMode.Canvas,
			GlowEnabled      = true,
			GlowIntensity    = 0.7f,
			GlowStrength     = 1.0f,
			GlowBloom        = 0.0f,  // 0 = only >threshold pixels bloom; >0 lifts the whole scene (wash)
			GlowBlendMode    = Godot.Environment.GlowBlendModeEnum.Additive, // adds light at bright spots only
			GlowHdrThreshold = 1.05f, // just above white so ordinary UI + base art stay untouched
			GlowHdrScale     = 2.0f,
		};
		// Soft, wide spread across the mid glow levels.
		env.SetGlowLevel(2, 1f);
		env.SetGlowLevel(3, 1f);
		env.SetGlowLevel(4, 1f);
		sceneRoot.AddChild(new WorldEnvironment { Name = "GlowEnv", Environment = env });
	}

	public void ApplyUpdate(TickUpdate update)
	{
		float bx = 0f, by = 0f;
		int blastCount = 0;
		List<GridPos>? floodAdvances = null;
		foreach (var t in update.TileChanges)
		{
			var p = new GridPos(t.X, t.Y);
			bool inBounds = Grid.InBounds(p);
			// Captured before Grid.Set mutates this tile below, so FromBlast logic can still
			// see what the tile was before the blast (Grid.Get throws when out of bounds).
			var oldType = inBounds ? Grid.Get(p) : default;
			if (inBounds)
			{
				// A dry tile turning to water is the flood front conquering it: animate the
				// creep instead of an instant square (a shallow->deep upgrade is not "new water").
				if (!oldType.IsWater() && t.NewType.IsWater())
					(floodAdvances ??= new()).Add(p);
				Grid.Set(p, t.NewType);
				if (t.NewType == TileType.Floor && _crystalPositions != null)
				{
					if (_crystalPositions.Remove(p))
						_crystalLitByPos = null; // invalidate; rebuilt lazily next UpdateFog
				}
			}
			if (t.FromBlast)
			{
				_world?.AddExplosionFlash(p);
				bx += t.X; by += t.Y; blastCount++;
				// A blasted wall that was a propped, exposed-face Rock throws wood splinters over
				// the rock debris (exterior check mirrors the draw pass so splinters only fly where
				// a prop was actually shown).
				if (inBounds && oldType == TileType.Rock && WorldRenderer.HasPitProp(t.X, t.Y)
					&& WorldRenderer.PitPropExteriorFace(Grid, t.X, t.Y))
					_world?.EmitWoodSplinters(new Vector2(t.X * TileSize + TileSize / 2f,
														  t.Y * TileSize + TileSize / 2f));
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
			ShakeFromWorld(c, 0.7f);
			_world?.EmitExplosionDebris(c);
		}

		if (update.Snapshot.ScreeCollapses is { } screeCollapses)
			foreach (var sc in screeCollapses)
			{
				var wpos = new Vector2(sc.X * TileSize + TileSize / 2f, sc.Y * TileSize + TileSize / 2f);
				_world?.AddRockfallDust(new GridPos(sc.X, sc.Y), sc.Radius);
				ScreeCollapsed?.Invoke(wpos, sc.Radius);
				ShakeFromWorld(wpos, 0.4f + 0.12f * sc.Radius);
			}

		if (update.Snapshot.Whistles is { } whistles)
			foreach (var wh in whistles)
				Whistled?.Invoke(new Vector2(wh.X * TileSize + TileSize / 2f, wh.Y * TileSize + TileSize / 2f));

		if (update.Snapshot.PortalUses is { } portalUses)
			foreach (var pu in portalUses)
			{
				// Mirror the host's authoritative collapse: an unstable gate's pair is
				// gone once used, so stop rendering both ends locally.
				if (pu.Kind == PortalKind.Unstable)
					foreach (var sp in Portals)
						if (sp.Pos.X == pu.X && sp.Pos.Y == pu.Y)
						{ _collapsedPortals.Add(sp.Id); _collapsedPortals.Add(sp.LinkId); }

				Portaled?.Invoke(new Vector2(pu.X * TileSize + TileSize / 2f, pu.Y * TileSize + TileSize / 2f), pu.Kind);
			}

			if (update.Snapshot.Throws is { } throws)
			{
				double now = Time.GetTicksMsec() / 1000.0;
				foreach (var th in throws)
				{
					var from = new Vector2(th.FromX * TileSize + TileSize / 2f, th.FromY * TileSize + TileSize / 2f);
					var to   = new Vector2(th.ToX   * TileSize + TileSize / 2f, th.ToY   * TileSize + TileSize / 2f);
					_world?.AddThrownStone(from, to);
					_throwUntil[th.ThrowerId] = now + StoneFlightSeconds;
					StoneTossed?.Invoke(from, to);
				}
			}

			if (update.Snapshot.DynamiteThrows is { } dynThrows)
			{
				double now = Time.GetTicksMsec() / 1000.0;
				foreach (var dt in dynThrows)
				{
					var from = new Vector2(dt.FromX * TileSize + TileSize / 2f, dt.FromY * TileSize + TileSize / 2f);
					var to   = new Vector2(dt.ToX   * TileSize + TileSize / 2f, dt.ToY   * TileSize + TileSize / 2f);
					_world?.AddThrownDynamite(from, to);
					_throwUntil[dt.ThrowerId] = now + StoneFlightSeconds; // thrower plays the same overhand toss pose
					StoneTossed?.Invoke(from, to);
				}
			}

			// Prize events: expose the active snapshot for the renderers, and raise
			// one-shot signals for the banner/feed on telegraph entry and on a claim.
			PrizeEvent = update.Snapshot.PrizeEvent;
			byte prizeState = update.Snapshot.PrizeEvent?.State ?? 0;
			if (prizeState == 1 && _prevPrizeState != 1 && update.Snapshot.PrizeEvent is { } pev)
				PrizeTelegraphed?.Invoke((PrizeType)pev.Type,
					new Vector2(pev.X * TileSize + TileSize / 2f, pev.Y * TileSize + TileSize / 2f));
			_prevPrizeState = prizeState;
			if (update.Snapshot.PrizeClaim is { } pclaim)
				PrizeWon?.Invoke(pclaim.MinerId, (PrizeType)pclaim.Type);

			Treasure = update.Snapshot.Treasure;
			HoldTimes = update.Snapshot.HoldTimes ?? System.Array.Empty<HoldTimeSnapshot>();
			if (update.Snapshot.TreasureToast is { } tt)
				TreasureToast?.Invoke(tt.Kind, tt.MinerId);

		ApplyTilePaints(update.TileChanges, floodAdvances);
		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		// Your own death always jolts the camera, regardless of what caused it.
		bool localAliveNow = false;
		foreach (var mm in _miners) if (mm.Id == LocalMinerId && mm.Alive) { localAliveNow = true; break; }
		if (_localWasAlive && !localAliveNow) AddShake(0.6f);
		foreach (var mm2 in _miners)
		{
			bool wasAlive = _prevMinerAlive.TryGetValue(mm2.Id, out var pa) && pa;
			if (wasAlive && !mm2.Alive)
			{
				var dp = new GridPos(mm2.X, mm2.Y);
				if (FogRenderer?.SpectatorMode == true || Fog.IsVisible(dp))
					_world?.EmitMinerDeath(
						new Vector2(mm2.X * TileSize + TileSize / 2f, mm2.Y * TileSize + TileSize / 2f));
			}
			_prevMinerAlive[mm2.Id] = mm2.Alive;
		}
		_localWasAlive = localAliveNow;
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		_items = new List<ItemSnapshot>(update.Snapshot.Items);
		_molds = new List<MoldSnapshot>(update.Snapshot.Molds);
		// Monster death spark: any currently-alive monster the new snapshot no longer has alive.
		foreach (var oldMo in _monsters)
		{
			if (!oldMo.Alive) continue;
			bool stillAlive = false;
			foreach (var nmo in update.Snapshot.Monsters)
				if (nmo.Id == oldMo.Id && nmo.Alive) { stillAlive = true; break; }
			if (stillAlive) continue;
			var dp = new GridPos(oldMo.X, oldMo.Y);
			if (FogRenderer?.SpectatorMode == true || Fog.IsVisible(dp))
				_world?.EmitMonsterDeath(
					new Vector2(oldMo.X * TileSize + TileSize / 2f, oldMo.Y * TileSize + TileSize / 2f),
					oldMo.Kind);
		}
		_monsters = new List<MonsterSnapshot>(update.Snapshot.Monsters);
		bool wasOpen = EscapeOpen;
		EscapeOpen = update.Snapshot.EscapeOpen;
		if (!wasOpen && EscapeOpen) ExpeditionTreasurePos = null;
		GoldRemaining = CountGold(Grid);
		SecondsRemaining = update.Snapshot.SecondsRemaining;
		Octopus          = update.Snapshot.Octopus;
		Lives            = update.Snapshot.Lives;
		TreasureProgress = update.Snapshot.TreasureProgress;

		// Floating pickup numbers over the LOCAL miner only (client-side cosmetic).
		foreach (var lm in _miners)
		{
			if (lm.Id != LocalMinerId) continue;
			var basePos = MinerVisualPos(lm.Id, lm.X, lm.Y);

			if (_lastLocalGold >= 0 && lm.Gold > _lastLocalGold)
			{
				_world?.AddFloatingText(basePos + new Vector2(0f, -20f),
					$"+{lm.Gold - _lastLocalGold}g", new Color(1f, 0.85f, 0.3f));
				_world?.EmitPickupBurst(basePos, new Color(1f, 0.85f, 0.3f));
			}
			_lastLocalGold = lm.Gold;

			int idols = 0;
			if (TreasureProgress is { } tp)
				foreach (var e in tp) if (e.MinerId == LocalMinerId) { idols = e.Found; break; }
			if (_lastLocalIdols >= 0 && idols > _lastLocalIdols)
			{
				_world?.AddFloatingText(basePos + new Vector2(0f, -28f),
					$"+{idols - _lastLocalIdols} idol", new Color(1f, 0.8f, 0.2f));
				_world?.EmitPickupBurst(basePos, new Color(1f, 0.8f, 0.2f));
			}
			_lastLocalIdols = idols;

			if (update.Snapshot.PrizeClaim is { } pc && pc.MinerId == LocalMinerId)
			{
				Color pcol = (PrizeType)pc.Type switch
				{
					PrizeType.MineOut    => new Color(0.35f, 0.85f, 1f),
					PrizeType.HoldPoint  => new Color(1f, 0.55f, 0.15f),
					PrizeType.CarryRelic => new Color(0.85f, 0.4f, 1f),
					_                    => new Color(1f, 0.85f, 0.3f),
				};
				_world?.AddFloatingText(basePos + new Vector2(0f, -36f), "PRIZE!", pcol);
			}
			break;
		}
		TripCharges      = update.Snapshot.TripCharges;
		PendingFalls     = update.Snapshot.PendingFalls;
		_reelChargeSnaps = new List<ReelChargeSnapshot>(update.Snapshot.ReelCharges ?? System.Array.Empty<ReelChargeSnapshot>());
		UpdateFog();
	}

	// Paints tile changes to the tilemap, but holds back flood-front tiles: WorldRenderer
	// animates those creeping in, and _PhysicsProcess paints the settled tile once its
	// animation completes, so the fill hands off without a visible pop.
	private void ApplyTilePaints(IReadOnlyList<TileChange> changes, List<GridPos>? floodAdvances)
	{
		if (floodAdvances == null)
		{
			_terrainMap?.UpdateTiles(changes);
			return;
		}
		var immediate = new List<TileChange>(changes.Count);
		foreach (var t in changes)
		{
			var p = new GridPos(t.X, t.Y);
			bool isFlood = floodAdvances.Contains(p);
			// Any pending paint for this tile is stale — a newer change (flood or not) wins.
			for (int i = _pendingFloodPaints.Count - 1; i >= 0; i--)
				if (_pendingFloodPaints[i].pos.Equals(p)) _pendingFloodPaints.RemoveAt(i);
			if (isFlood)
			{
				_world?.AddFloodAdvance(p, t.NewType);
				_pendingFloodPaints.Add((p, t, WorldRenderer.FloodAnimSeconds));
			}
			else immediate.Add(t);
		}
		if (immediate.Count > 0) _terrainMap?.UpdateTiles(immediate);
	}

	public void ResetFloor(int floor)
	{
		_terrainMap?.QueueFree(); _terrainMap = null!;
		_world?.QueueFree();      _world = null!;
		_fogRenderer?.QueueFree(); _fogRenderer = null!;
		_lightRenderer?.QueueFree(); _lightRenderer = null!;
		_ghostOverlay?.QueueFree(); _ghostOverlay = null!;

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
		Portals           = newMap.Portals;
		_collapsedPortals.Clear();
		GoldRemaining     = CountGold(newMap.Grid);
		StartingGoldCount = GoldRemaining;
		EscapeOpen        = false;
		Octopus           = null;

		Fog.Reset();
		_crystalPositions = null;
		_crystalLitByPos  = null;
		FogDirty = false;
		_visualPos.Clear();
		_lastMinerPos.Clear();
		_lastLocalGold  = -1;
		_lastLocalIdols = -1;
		_prevMinerAlive.Clear();
		_walkUntil.Clear();
		_throwUntil.Clear();
		_pendingFloodPaints.Clear(); // stale flood animations don't carry to the new floor
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

		_lightRenderer = new LightRenderer { Name = "LightRenderer", ZIndex = -8 };
		_sceneRoot.AddChild(_lightRenderer);
		_lightRenderer.Init(this);

		_ghostOverlay = new GhostOverlay { Name = "GhostOverlay", ZIndex = -7 };
		_sceneRoot.AddChild(_ghostOverlay);
		_ghostOverlay.Init(_world);
	}

	public override void _PhysicsProcess(double delta)
	{
		// Re-assert our camera after a scene swap: the previous scene's camera
		// teardown can clobber this one's MakeCurrent() (a Godot _Ready ordering
		// race), leaving the rig tracking the miner while the viewport stays at
		// world origin ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the symptom is a tracked-but-unseen miner and a fixed
		// offset view. Cheap to check every frame and self-heals immediately.
		if (_cam != null && !_cam.IsCurrent())
			_cam.MakeCurrent();

		// Flush deferred flood paints whose creep animation has finished, handing the tile
		// over to the settled tilemap render.
		for (int i = _pendingFloodPaints.Count - 1; i >= 0; i--)
		{
			var pf = _pendingFloodPaints[i];
			pf.remaining -= (float)delta;
			if (pf.remaining <= 0f)
			{
				_terrainMap?.UpdateTiles(new[] { pf.change });
				_pendingFloodPaints.RemoveAt(i);
			}
			else _pendingFloodPaints[i] = pf;
		}

		// Smooth each miner visual toward its authoritative tile position.
		double now = Time.GetTicksMsec() / 1000.0;
		bool foundLocal = false;
		bool localAlive = false;
		Vector2 localVisualPos = Vector2.Zero;
		foreach (var m in _miners)
		{
			var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);

			bool hadLast = _lastMinerPos.TryGetValue(m.Id, out var last);
			// A single-tile step is a walk; a non-adjacent jump (respawn teleport, floor
			// warp) must NOT slide/animate across the map ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â snap straight to the destination.
			bool teleported = hadLast && Mathf.Max(Mathf.Abs(last.X - m.X), Mathf.Abs(last.Y - m.Y)) > 1;

			if (teleported)
			{
				_visualPos[m.Id] = target;
				_walkUntil.Remove(m.Id);
			}
			else
			{
				var cur = _visualPos.TryGetValue(m.Id, out var v) ? v : target;
				float pixelsPerSec = TileSize / (float)m.MoveSeconds;
				_visualPos[m.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);
				if (hadLast && (last.X != m.X || last.Y != m.Y))
					_walkUntil[m.Id] = now + m.MoveSeconds;
			}
			_lastMinerPos[m.Id] = (m.X, m.Y);

			if (m.Id == LocalMinerId)
			{
				foundLocal = true;
				localAlive = m.Alive;
				localVisualPos = _visualPos[m.Id];

				// Random idle fidgets: only when truly standing still
				bool trulyIdle = m.Alive && m.Activity == 0
					&& !(_walkUntil.TryGetValue(m.Id, out double wu) && now < wu)
					&& !Listening;

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
			// No snapshot yet ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â leave camera where it is.
		}
		else if (localAlive && _cam != null)
		{
			_camera.Position = localVisualPos;
			_cam.Zoom = _cam.Zoom.Lerp(new Vector2(2.5f, 2.5f), Mathf.Min(1f, (float)delta * 4f));
			if (_fogRenderer != null) _fogRenderer.SpectatorMode = false;
			if (_lightRenderer != null) _lightRenderer.SpectatorMode = false;
		}
		else if (_cam != null && NetworkManager.Instance.MatchMode != GameMode.Expedition)
		{
			// Dead ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â ease to a browsable zoom; camera stays near death position.
			// Expedition uses its own black-fade overlay and respawns the miner, so we leave it alone.
			_cam.Zoom = _cam.Zoom.Lerp(new Vector2(2.0f, 2.0f), Mathf.Min(1f, (float)delta * 2.5f));
			if (_fogRenderer != null) _fogRenderer.SpectatorMode = true;
			if (_lightRenderer != null) _lightRenderer.SpectatorMode = true;

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

		// Camera shake: trauma decays; the screen offset scales with its square so small
		// tremors fade smoothly while big hits punch hard.
		if (_cam != null)
		{
			_shakeTrauma = Mathf.Max(0f, _shakeTrauma - (float)delta * 1.6f);
			if (_shakeTrauma > 0f)
			{
				float s = _shakeTrauma * _shakeTrauma;
				const float maxOffset = 9f;
				_cam.Offset = new Vector2(
					((float)_shakeRng.NextDouble() * 2f - 1f) * maxOffset * s,
					((float)_shakeRng.NextDouble() * 2f - 1f) * maxOffset * s);
			}
			else if (_cam.Offset != Vector2.Zero)
				_cam.Offset = Vector2.Zero;
		}

		QueueRedraw();
	}

	// Adds camera-shake trauma (clamped to 1). Called on explosions, cave-ins, and local death.
	public void AddShake(float amount) => _shakeTrauma = Mathf.Min(1f, _shakeTrauma + amount);

	// Shake scaled by how close a world-space event is to the camera focus — distant booms
	// barely register, nearby ones jolt.
	private void ShakeFromWorld(Vector2 worldPos, float baseAmount)
	{
		Vector2 focus = _camera?.Position ?? worldPos;
		float near = Mathf.Clamp(1f - focus.DistanceTo(worldPos) / (TileSize * 10f), 0f, 1f);
		if (near > 0f) AddShake(baseAmount * near);
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
			int variant  = MinerVariantIndex(m.Id);
			int facing   = m.Facing;
			var set      = GetOrBuildSet(variant, colorIdx);

			double drawNow = Time.GetTicksMsec() / 1000.0;
			Texture2D? tex;
			bool throwing = _throwUntil.TryGetValue(m.Id, out double throwEnd) && drawNow < throwEnd;
			if (throwing)
			{
				double telapsed = MatchClient.StoneFlightSeconds - (throwEnd - drawNow);
				int frame = Mathf.Clamp((int)(telapsed / MatchClient.StoneFlightSeconds * 4), 0, 3);
				tex = set.Throw[facing, frame];
			}
			else if (m.Activity == 1 && m.ActivityRemaining > 0)
			{
				// Mining: loop 6 animated frames (1-6) at 6 fps
				int frame = (int)(drawNow * 6 % 6) + 1;
				tex = set.Mine[facing, frame];
			}
			else if ((m.Activity == 2 || m.Activity == 3) && m.ActivityRemaining > 0)
			{
				// Planting / PlantingDetonator: loop 4 animated frames (1-4) at 4 fps
				int frame = (int)(drawNow * 4 % 4) + 1;
				tex = set.Plant[facing, frame];
			}
			else
			{
				bool walking = _walkUntil.TryGetValue(m.Id, out double until) && drawNow < until;
				if (walking)
				{
					double elapsed = m.MoveSeconds - (until - drawNow);
					// Clamp (don't %) the frame: MoveSeconds can shrink between the update that set
					// _walkUntil and this draw (speed buff / upgrade), making `elapsed` negative — and
					// C# `%` keeps the sign, so `-1 % 4 == -1` would index Walk[facing, -1] and throw.
					int frame = m.MoveSeconds > 0.0
						? Mathf.Clamp((int)(elapsed / m.MoveSeconds * 4), 0, 3)
						: 0;
					tex = set.Walk[facing, frame];
				}
				else if ((m.Id == LocalMinerId && Listening) || (m.Id != LocalMinerId && m.Listening))
				{
					tex = set.Listen[facing];
				}
				else if (_currentIdleIdx >= 0 && m.Id == LocalMinerId)
				{
					tex = set.Idle[_currentIdleIdx]; // south-facing fidget pose
				}
				else
				{
					tex = set.Facing[facing];
				}
			}

			WorldRenderer.DrawGroundShadow(this, new Vector2(p.X, p.Y + 11f), 9f, 3.5f);
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

	private sealed class MinerTextureSet
	{
		public readonly Texture2D?[]  Facing = new Texture2D?[4];
		public readonly Texture2D?[]  Listen = new Texture2D?[4];
		public readonly Texture2D?[]  Idle   = new Texture2D?[IdleVariantCount];
		public readonly Texture2D?[,] Walk   = new Texture2D?[4, 4];
		public readonly Texture2D?[,] Mine   = new Texture2D?[4, 7];
		public readonly Texture2D?[,] Plant  = new Texture2D?[4, 5];
		public readonly Texture2D?[,] Throw  = new Texture2D?[4, 4];
	}

	private sealed class MinerSourceImages
	{
		public readonly Image?[]  Facing = new Image?[4];
		public readonly Image?[]  Listen = new Image?[4];
		public readonly Image?[]  Idle   = new Image?[IdleVariantCount];
		public readonly Image?[,] Walk   = new Image?[4, 4];
		public readonly Image?[,] Mine   = new Image?[4, 7];
		public readonly Image?[,] Plant  = new Image?[4, 5];
		public readonly Image?[,] Throw  = new Image?[4, 4];
	}

	// Loads a miner frame for a variant, falling back to the base (variant 0) art if the
	// variant's own file is missing, so a not-yet-drawn variant still renders.
	private static Image? LoadMinerImage(int variant, string relative)
	{
		string prefix = MinerVariants.Prefix(variant);
		var img = GD.Load<CompressedTexture2D>($"res://assets/miners/{prefix}{relative}")?.GetImage();
		if (img == null && prefix != "")
			img = GD.Load<CompressedTexture2D>($"res://assets/miners/{relative}")?.GetImage();
		if (img != null) img.Convert(Image.Format.Rgba8);
		return img;
	}

	private MinerSourceImages GetVariantSources(int variant)
	{
		variant = MinerVariants.Clamp(variant);
		if (_variantSources.TryGetValue(variant, out var cached)) return cached;
		var s = new MinerSourceImages();
		var dir = new[] { "n", "e", "s", "w" };
		for (int d = 0; d < 4; d++)
		{
			s.Facing[d] = LoadMinerImage(variant, $"miner_{dir[d]}.png");
			s.Listen[d] = LoadMinerImage(variant, $"listen/{dir[d]}.png");
			for (int f = 0; f < 4; f++) s.Walk[d, f]  = LoadMinerImage(variant, $"walk/{dir[d]}{f}.png");
			for (int f = 0; f < 7; f++) s.Mine[d, f]  = LoadMinerImage(variant, $"mine/{dir[d]}{f}.png");
			for (int f = 0; f < 5; f++) s.Plant[d, f] = LoadMinerImage(variant, $"plant/{dir[d]}{f}.png");
			for (int f = 0; f < 4; f++) s.Throw[d, f] = LoadMinerImage(variant, $"throw/{dir[d]}{f}.png");
		}
		for (int i = 0; i < IdleVariantCount; i++) s.Idle[i] = LoadMinerImage(variant, $"idle/idle{i}.png");
		_variantSources[variant] = s;
		return s;
	}

	private MinerTextureSet GetOrBuildSet(int variant, int color)
	{
		variant = MinerVariants.Clamp(variant);
		if (_minerSets.TryGetValue((variant, color), out var set)) return set;
		var src = GetVariantSources(variant);
		var tint = PlayerColors.At(color);
		set = new MinerTextureSet();
		for (int d = 0; d < 4; d++)
		{
			if (src.Facing[d] != null) set.Facing[d] = ImageTexture.CreateFromImage(TintMiner(src.Facing[d]!, tint));
			if (src.Listen[d] != null) set.Listen[d] = ImageTexture.CreateFromImage(TintMiner(src.Listen[d]!, tint));
			for (int f = 0; f < 4; f++) if (src.Walk[d, f]  != null) set.Walk[d, f]  = ImageTexture.CreateFromImage(TintMiner(src.Walk[d, f]!,  tint));
			for (int f = 0; f < 7; f++) if (src.Mine[d, f]  != null) set.Mine[d, f]  = ImageTexture.CreateFromImage(TintMiner(src.Mine[d, f]!,  tint));
			for (int f = 0; f < 5; f++) if (src.Plant[d, f] != null) set.Plant[d, f] = ImageTexture.CreateFromImage(TintMiner(src.Plant[d, f]!, tint));
			for (int f = 0; f < 4; f++) if (src.Throw[d, f] != null) set.Throw[d, f] = ImageTexture.CreateFromImage(TintMiner(src.Throw[d, f]!, tint));
		}
		for (int i = 0; i < IdleVariantCount; i++) if (src.Idle[i] != null) set.Idle[i] = ImageTexture.CreateFromImage(TintMiner(src.Idle[i]!, tint));
		_minerSets[(variant, color)] = set;
		return set;
	}

	private static int MinerVariantIndex(int minerId)
	{
		var nm = NetworkManager.Instance;
		int idx = minerId - 1;
		if (idx >= 0 && idx < nm.PeerOrder.Length &&
			nm.Players.TryGetValue(nm.PeerOrder[idx], out var info))
			return MinerVariants.Clamp(info.VariantIndex);
		return 0;
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
				// Preserve skin and the yellow hard hat: both are warm-hued AND bright. Grey/dark
				// clothing is either low-saturation or low-luminance and takes the team colour.
				// (Keyed on luminance rather than a tight saturation floor so the variants' lighter,
				// less-saturated skin tones — S as low as ~0.26 — are not tinted like the old S>0.3 test.)
				if (px.H < 0.14f && px.S > 0.18f && lum > 0.5f) continue;
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

			// Crystal walls light their surroundings only once the player can actually see the
			// crystal ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â i.e. it lies within their own line-of-sight vision. This makes a crystal an
			// "encountered in your FOV" light source, not a global map reveal. Per-crystal lit sets
			// are precomputed once per floor (invalidated when a crystal is mined) so only the
			// cheap Contains check runs per tick.
			if (_crystalLitByPos == null) BuildCrystalLitCache();
			foreach (var (cp, lit) in _crystalLitByPos!)
				if (visible.Contains(cp))
					visible.UnionWith(lit);

			// A loose shard on the floor lights its area only when the player can see the shard.
			foreach (var it in _items)
				if (it.Kind == ItemKind.CrystalShard && it.Placement == ItemPlacement.Loose
				    && visible.Contains(new GridPos(it.X, it.Y)))
					visible.UnionWith(Visibility.Compute(Grid, new GridPos(it.X, it.Y), CrystalShardFloorRadius));

			foreach (var mn in _miners)
				if (mn.Alive && mn.Held == (int)ItemKind.CrystalShard)
					visible.UnionWith(Visibility.Compute(Grid, new GridPos(mn.X, mn.Y), CrystalShardHeldRadius));

			Fog.Update(visible);
			FogDirty = true;
		}
	}

	public void ClearFogDirty() => FogDirty = false;

	private void BuildCrystalCache()
	{
		_crystalPositions = new HashSet<GridPos>();
		foreach (var p in Grid.Positions())
			if (Grid.Get(p) == TileType.CrystalRock)
				_crystalPositions.Add(p);
	}

	private void BuildCrystalLitCache()
	{
		if (_crystalPositions == null) BuildCrystalCache();
		_crystalLitByPos = new Dictionary<GridPos, HashSet<GridPos>>();
		foreach (var cp in _crystalPositions!)
			_crystalLitByPos[cp] = Visibility.Compute(Grid, cp, CrystalWallRadius);
	}

	private static int CountGold(TileGrid grid)
	{
		int n = 0;
		foreach (var p in grid.Positions())
			if (grid.Get(p) == TileType.GoldRock) n++;
		return n;
	}
}
