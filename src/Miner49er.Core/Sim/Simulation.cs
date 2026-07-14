namespace Miner49er.Core;

public sealed class Simulation
{
    public TileGrid Grid { get; }
    public SimConfig Config { get; }

    private readonly Dictionary<int, Miner> _miners = new();
    private readonly List<Charge> _charges = new();
    private readonly List<TripCharge> _tripCharges = new();
    private readonly List<Item> _items = new();
    private readonly List<MoldPatch> _molds = new();
    private readonly List<LavaVent> _lavaVents = new();
    private readonly List<Portal> _portals = new();
    private readonly List<SimEvent> _events = new();
    private readonly List<Monster> _monsters = new();
    private readonly List<ReelCharge> _reelCharges = new();
    private readonly List<NoiseSource> _noiseSources = new();
    private readonly List<PendingFall> _pendingFalls = new();
    private double _rockFallTimer;
    private bool _rockFallsActive;

    private readonly Dictionary<int, GridPos?>          _chestPos        = new();
    private readonly Dictionary<int, int>               _idolsFound      = new();
    private readonly Dictionary<int, (ItemKind A, ItemKind B)> _idolAssignments = new();

    public readonly record struct TreasureProgressData(int MinerId, int Found);
    public readonly record struct PlacedChestData(int MinerId, int X, int Y);
    private readonly Random _rng;
    private Octopus? _octopus;

    // Global prize event (competitive only). One at a time; see AdvancePrizeEvent.
    private PrizeState _prizeState = PrizeState.Idle;
    private PrizeType  _prizeType;
    private GridPos    _prizePos;
    private bool       _prizeArmed;
    private double     _prizeArmTimer;
    private double     _prizeStateTimer;
    private double     _prizeClaimProgress;
    private int        _prizeHolderId = -1;

    public PrizeState PrizeState => _prizeState;
    public PrizeType  PrizeType  => _prizeType;
    public GridPos    PrizePos   => _prizePos;
    public double     PrizeClaimProgress    => _prizeClaimProgress;
    public int        PrizeHolderId         => _prizeHolderId;
    public double     PrizeSecondsRemaining => _prizeStateTimer;

    // ── Treasure Heist state ──────────────────────────────────────────────
    private ItemKind _treasureKind = ItemKind.IdolUrn;
    private bool     _treasureUnearthed;
    private bool     _treasureFound;
    private GridPos  _treasurePos;
    private int      _treasureHolderId = -1;
    private readonly Dictionary<int, double> _holdSeconds = new();
    private double   _sneakHold;
    private double   _sneakCooldownRemaining;
    private double   _suddenDeathHold;
    private int      _suddenDeathWinner = -1;
    private readonly Dictionary<int, double> _respawnTimers = new();

    public bool    TreasureUnearthed => _treasureUnearthed;
    public bool    TreasureFoundYet  => _treasureFound;
    public GridPos TreasurePos       => _treasurePos;
    public int     TreasureHolderId  => _treasureHolderId;
    public int     SuddenDeathWinner => _suddenDeathWinner;
    public double  HoldSecondsOf(int minerId) => _holdSeconds.GetValueOrDefault(minerId);
    public bool    WinByCumulative() => Config.TreasureWinByCumulative;
    public bool    RespawnEnabled    => Config.TreasureRespawnEnabled;

    internal void ForceTreasureLooseForTest(GridPos pos)
    {
        _treasureUnearthed = true;
        _treasurePos = pos;
        _treasureHolderId = -1;
    }

    internal void SetTimeExpiredForTest() { _timeLimit = 0.0; Elapsed = 1.0; }

    private enum NoiseKind { Stone, Explosion, Pickaxe }
    private sealed class NoiseSource
    {
        public GridPos Pos;
        public double LifetimeRemaining;
        public NoiseKind Kind;
    }

    public sealed class PendingFall
    {
        public GridPos Pos { get; }
        public double SecondsRemaining { get; internal set; }
        public double TotalSeconds { get; }
        public double FractionElapsed => 1.0 - SecondsRemaining / TotalSeconds;
        internal PendingFall(GridPos pos, double totalSeconds)
        { Pos = pos; TotalSeconds = totalSeconds; SecondsRemaining = totalSeconds; }
    }
    public  Octopus? Octopus => _octopus;

    public IReadOnlyCollection<Miner> Miners => _miners.Values;
    public IReadOnlyList<Charge> Charges => _charges;
    public IReadOnlyList<TripCharge> TripCharges => _tripCharges;
    public IReadOnlyList<Item> Items => _items;
    public IReadOnlyList<MoldPatch> Molds => _molds;
    public IReadOnlyList<Monster> Monsters => _monsters;
    public IReadOnlyList<ReelCharge> ReelCharges => _reelCharges;
    public IReadOnlyList<PendingFall> PendingFalls => _pendingFalls;

    public void AddItem(Item item)
    {
        _items.Add(item);   // host seeds these from GeneratedMap.Items
        if (Config.TreasureHeistMode && !_treasureUnearthed && item.Placement == ItemPlacement.Buried)
        {
            _treasureKind = item.Kind;
            _treasurePos = item.Pos;
        }
    }

    public GridPos? Center { get; }
    public int FirstToReachCenter { get; private set; } = -1;

    public GridPos? EscapeTile { get; }
    public bool EscapeOpen { get; private set; }
    private int _goldRemaining;
    public bool AllGoldCleared => _goldRemaining == 0;
    public int StartingGoldCount { get; private set; }
    public double GoldCollectedFraction =>
        StartingGoldCount == 0 ? 1.0 : 1.0 - (double)_goldRemaining / StartingGoldCount;

    private double? _timeLimit;
    private readonly bool _flooding;
    public double Elapsed { get; private set; }
    public double SecondsRemaining => _timeLimit is { } lim ? Math.Max(0, lim - Elapsed) : -1;
    public bool TimeExpired => _timeLimit is { } lim && Elapsed >= lim;

    public GridPos? ShopPos { get; private set; }

    public Simulation(TileGrid grid, SimConfig config,
        GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false,
        GridPos? escapeTile = null, GridPos? shopPos = null)
    {
        Grid = grid;
        Config = config;
        Center = center;
        _timeLimit = timeLimitSeconds;
        _flooding = flooding;
        EscapeTile = escapeTile;
        ShopPos = shopPos;

        _rng = new Random(config.Seed);

        foreach (var p in Grid.Positions())
        {
            if (Grid.Get(p) == TileType.LavaVent)
                _lavaVents.Add(new LavaVent { Pos = p, Budget = config.LavaVentBudget });
            if (Grid.Get(p) == TileType.GoldRock) _goldRemaining++;
        }
        StartingGoldCount = _goldRemaining;
        if (EscapeTile is not null && _goldRemaining == 0 && !Config.RequireChestForEscape
            && Config.ExpeditionTreasureKind is null)
            EscapeOpen = true;   // gold-less map: open at once (unless boss/treasure floor)
    }

    public Miner AddMiner(int id, GridPos pos, double invulRemaining = 0.0)
    {
        var m = new Miner(id, pos) { InvulnerableRemaining = invulRemaining, SpawnPos = pos };
        _miners[id] = m;
        if (Config.TreasureHeistMode) m.StoneCount = Config.StartingStones;
        if (Config.TreasureHuntMode)
        {
            var (a, b)            = TreasureAssignment.For(Config.Seed, id);
            _idolAssignments[id]  = (a, b);
            _idolsFound[id]       = 0;
            _chestPos[id]         = null;
            m.Held                = ItemKind.TreasureChest;
        }
        return m;
    }

    public Monster AddMonster(int id, GridPos pos, MonsterKind kind)
    {
        var mo = new Monster(id, pos, kind);
        mo.MoveCooldownRemaining = MonsterCadenceFor(mo);
        _monsters.Add(mo);
        return mo;
    }

    public void AddOctopus(GridPos pos) => _octopus = new Octopus(pos);

    public void AddPortal(PortalSpec spec) =>
        _portals.Add(new Portal { Id = spec.Id, Pos = spec.Pos, Kind = spec.Kind, LinkId = spec.LinkId });

    public IReadOnlyList<PortalReadModel> Portals =>
        _portals.Select(p => new PortalReadModel(p.Id, p.Pos, p.Kind, p.LinkId, p.Collapsed)).ToList();

    /// <summary>Test-only: force a portal tile to Floor to simulate mining it out.</summary>
    internal void RevealTileForTest(GridPos p) => Grid.Set(p, TileType.Floor);

    /// <summary>Test-only: drop a prize of a chosen type straight into the Active state.</summary>
    internal void ForcePrizeForTest(PrizeType type, GridPos pos)
    {
        _prizeArmed = true; _prizeType = type; _prizePos = pos; _prizeState = PrizeState.Active;
        _prizeStateTimer = Config.PrizeExpirySeconds; _prizeClaimProgress = 0; _prizeHolderId = -1;
    }

    /// <summary>Test-only: teleport a miner (bypasses movement cadence).</summary>
    internal void SetMinerPositionForTest(int id, GridPos p)
    { if (_miners.TryGetValue(id, out var m)) m.Pos = p; }

    /// <summary>Test-only: force a miner's facing direction.</summary>
    internal void SetFacingForTest(int id, Direction dir)
    { if (_miners.TryGetValue(id, out var m)) m.Facing = dir; }

    // A portal is "revealed" once its tile is uncovered Floor (buried ends are Rock).
    private bool IsRevealed(Portal p) => Grid.InBounds(p.Pos) && Grid.Get(p.Pos) == TileType.Floor;

    private Portal? LinkOf(Portal p) => _portals.FirstOrDefault(q => q.Id == p.LinkId);

    private bool IsPortalActive(Portal p)
    {
        if (p.Collapsed || p.CooldownRemaining > 0 || !IsRevealed(p)) return false;
        var link = LinkOf(p);
        return link is not null && !link.Collapsed && IsRevealed(link);
    }

    private void AdvancePortals(double dt)
    {
        foreach (var p in _portals)
            if (p.CooldownRemaining > 0)
                p.CooldownRemaining = Math.Max(0, p.CooldownRemaining - dt);
    }

    internal void DropMoldAt(GridPos pos) =>
        _molds.Add(new MoldPatch(pos, Config.MoldSeconds));

    private double MonsterCadence(MonsterKind kind) => kind switch
    {
        MonsterKind.Slime          => Config.MonsterSlimeMoveSeconds,
        MonsterKind.Ghost          => Config.MonsterGhostMoveSeconds,
        MonsterKind.Goat           => Config.MonsterGoatMoveSeconds,
        MonsterKind.ZombieMiner    => Config.MonsterZombieMoveSeconds,
        MonsterKind.SkeletonHuman  => Config.MonsterSkeletonMoveSeconds,
        MonsterKind.SkeletonDino   => Config.MonsterSkeletonDinoMoveSeconds,
        MonsterKind.WaterSnake     => Config.MonsterWaterSnakeLandMoveSeconds,
        _ => Config.MonsterSlimeMoveSeconds,
    };

    private double MonsterCadenceFor(Monster mo)
    {
        if (mo.Kind != MonsterKind.WaterSnake) return MonsterCadence(mo.Kind);
        return Grid.Get(mo.Pos).IsWater()
            ? Config.MonsterWaterSnakeWaterMoveSeconds
            : Config.MonsterWaterSnakeLandMoveSeconds;
    }

    public Miner GetMiner(int id) => _miners[id];

    public void SetMinerHeld(int minerId, ItemKind? item)
    {
        if (_miners.TryGetValue(minerId, out var m))
            m.Held = item;
    }

    public void KillMiner(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = DeathCause.Left;
        _events.Add(new MinerKilled(id));
    }

    public void ReviveMiner(int id, GridPos pos, double invulRemaining = 3.0)
    {
        if (!_miners.TryGetValue(id, out var m)) return;
        m.Alive = true;
        m.DeathCause = DeathCause.None;
        m.Pos = pos;
        m.Activity = ActivityKind.None;
        m.ActivitySecondsRemaining = 0;
        m.InvulnerableRemaining = invulRemaining;
        m.MoveCooldownRemaining = 0;
        m.CrackDwell = 0;
    }

    public IReadOnlyList<SimEvent> DrainEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public void ApplyEffect(int minerId, EffectKind kind, EffectChannel channel,
        double magnitude, double durationSeconds)
    {
        var m = _miners[minerId];
        if (!m.Alive) return;
        var existing = m.EffectsInternal.FirstOrDefault(e => e.Kind == kind);
        if (existing is not null)
        {
            existing.Channel = channel;
            existing.Magnitude = magnitude;
            existing.RemainingSeconds = durationSeconds;   // refresh, never compound
        }
        else
        {
            m.EffectsInternal.Add(new StatusEffect
            {
                Kind = kind, Channel = channel,
                Magnitude = magnitude, RemainingSeconds = durationSeconds,
            });
        }
    }

    private void AdvanceEffects(double dt)
    {
        foreach (var m in _miners.Values)
        {
            var fx = m.EffectsInternal;
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                fx[i].RemainingSeconds -= dt;
                if (fx[i].RemainingSeconds <= 0) fx.RemoveAt(i);
            }
        }
    }

    private void AdvanceInvulnerability(double dt)
    {
        foreach (var m in _miners.Values)
            if (m.InvulnerableRemaining > 0)
                m.InvulnerableRemaining = Math.Max(0, m.InvulnerableRemaining - dt);
    }

    public double EffectiveMoveSeconds(int minerId) => EffectiveMoveSeconds(_miners[minerId]);

    private double EffectiveMoveSeconds(Miner m)
    {
        double mult = Math.Pow(Config.PermSpeedFactor, m.PermSpeedLevel);
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.MoveSpeed) mult *= e.Magnitude;
        double tile = Grid.Get(m.Pos).MoveCostMultiplier();   // shallow water = ×2
        double seconds = Config.BaseMoveSeconds * tile * mult;
        if (Config.TreasureHeistMode && m.Id == _treasureHolderId)
            seconds *= Config.TreasureCarrySlowFactor;
        return Math.Clamp(seconds, Config.MinMoveSeconds, Config.MaxMoveSeconds);
    }

    public int EffectiveVisionRadius(int minerId) => EffectiveVisionRadius(_miners[minerId]);

    private int EffectiveVisionRadius(Miner m)
    {
        int bonus = m.PermVisionLevel * Config.PermVisionBonus;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.VisionRadius) bonus += (int)e.Magnitude;
        if (m.Held == ItemKind.Lantern) bonus += Config.LanternVisionBonus;
        return Config.VisionRadius + bonus;
    }

    public int EffectiveBlastBonus(int minerId) => EffectiveBlastBonus(_miners[minerId]);

    private int EffectiveBlastBonus(Miner m)
    {
        int bonus = m.PermBlastLevel * Config.PermBlastBonus;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.BlastRadius) bonus += (int)e.Magnitude;
        return bonus;
    }

    public void SetPermLevels(int minerId, int speed, int vision, int blast)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        m.PermSpeedLevel  = Math.Clamp(speed,  0, Config.MaxPermSpeedLevel);
        m.PermVisionLevel = Math.Clamp(vision, 0, Config.MaxPermVisionLevel);
        m.PermBlastLevel  = Math.Clamp(blast,  0, Config.MaxPermBlastLevel);
    }

    public void AddStones(int minerId, int count)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        m.StoneCount = Math.Min(9, m.StoneCount + count);
    }

    public void DeductGold(int minerId, int amount)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        m.GoldCollected = Math.Max(0, m.GoldCollected - amount);
    }

    public void TryThrowStone(int minerId)
    {
        if (!_miners.TryGetValue(minerId, out var m) || !m.Alive) return;
        if (m.StoneCount <= 0) return;

        var offset = m.Facing.ToOffset();
        GridPos land = m.Pos;
        int hitId = -1;
        for (int i = 1; i <= 64; i++)
        {
            var next = new GridPos(m.Pos.X + offset.X * i, m.Pos.Y + offset.Y * i);
            if (!Grid.InBounds(next) || !Grid.Get(next).IsEnterable()) break;
            land = next;
            // The stone stops at the first miner in its path: it stuns them (dropping any
            // held item / the treasure) and comes to rest at their feet.
            var target = _miners.Values.FirstOrDefault(r => r.Id != minerId && r.Alive && r.Pos == next);
            if (target != null) { hitId = target.Id; break; }
        }

        m.StoneCount--;
        if (hitId >= 0 && _miners.TryGetValue(hitId, out var victim))
        {
            victim.StunRemaining = 0.8;
            _events.Add(new MinerStunned(minerId, hitId));
        }
        _noiseSources.Add(new NoiseSource { Pos = land, LifetimeRemaining = 4.0, Kind = NoiseKind.Stone });
        _events.Add(new StoneThrown(minerId, land));

        if (Config.TreasureHeistMode && Grid.Get(land) == TileType.Floor && _items.All(it => it.Pos != land))
            _items.Add(new Item(land, ItemKind.Stone, ItemPlacement.Loose));
    }

    // Lob a lit stick in the facing direction: it travels up to Config.ThrownDynamiteRange
    // tiles, stopping on the last open tile before a wall/edge, then lands as a live Charge
    // that detonates after a short fuse (reusing the plant/detonate pipeline for the blast,
    // networking, and rendering). Thrown into a wall right beside you, it lands at your feet.
    public void TryThrowDynamite(int minerId)
    {
        if (!_miners.TryGetValue(minerId, out var m) || !m.Alive) return;
        if (!Config.DynamiteEnabled) return;
        if (m.StunRemaining > 0) return;
        if (m.DynamiteThrowCooldown > 0) return;

        var offset = m.Facing.ToOffset();
        GridPos land = m.Pos;
        for (int i = 1; i <= Config.ThrownDynamiteRange; i++)
        {
            var next = new GridPos(m.Pos.X + offset.X * i, m.Pos.Y + offset.Y * i);
            if (!Grid.InBounds(next) || !Grid.Get(next).IsEnterable()) break;
            land = next;
        }

        m.DynamiteThrowCooldown = Config.ThrownDynamiteCooldownSeconds;
        _charges.Add(new Charge(minerId, land, Config.ThrownDynamiteFuseSeconds, EffectiveBlastBonus(m)));
        _events.Add(new DynamiteThrown(minerId, land));
    }

    public int TreasureWinner()
    {
        foreach (var kv in _idolsFound)
            if (kv.Value >= 2 && _miners.TryGetValue(kv.Key, out var m) && m.Alive)
                return kv.Key;
        return -1;
    }

    public IReadOnlyList<TreasureProgressData> GetTreasureProgress()
    {
        var list = new List<TreasureProgressData>(_idolsFound.Count);
        foreach (var kv in _idolsFound)
            list.Add(new TreasureProgressData(kv.Key, kv.Value));
        return list;
    }

    public IReadOnlyList<PlacedChestData> GetPlacedChests()
    {
        var list = new List<PlacedChestData>();
        foreach (var kv in _chestPos)
            if (kv.Value is { } pos)
                list.Add(new PlacedChestData(kv.Key, pos.X, pos.Y));
        return list;
    }

    public GridPos? ChestPosFor(int minerId) =>
        _chestPos.TryGetValue(minerId, out var pos) ? pos : null;

    internal void GiveItemForTest(int minerId, ItemKind kind)
    {
        if (_miners.TryGetValue(minerId, out var m)) m.Held = kind;
    }

    private void AdvanceMolds(double dt)
    {
        for (int i = _molds.Count - 1; i >= 0; i--)
        {
            _molds[i].RemainingSeconds -= dt;
            if (_molds[i].RemainingSeconds <= 0)
            {
                _events.Add(new MoldExpired(_molds[i].Pos));
                _molds.RemoveAt(i);
            }
        }
    }

    private void AdvanceNoiseSources(double dt)
    {
        for (int i = _noiseSources.Count - 1; i >= 0; i--)
        {
            _noiseSources[i].LifetimeRemaining -= dt;
            if (_noiseSources[i].LifetimeRemaining <= 0)
                _noiseSources.RemoveAt(i);
        }
    }

    private NoiseSource? NearestNoiseSourceInRange(GridPos pos, int radius)
    {
        NoiseSource? best = null;
        int bestDist = int.MaxValue;
        foreach (var ns in _noiseSources)
        {
            int d = pos.ManhattanTo(ns.Pos);
            if (d <= radius && d < bestDist) { best = ns; bestDist = d; }
        }
        return best;
    }

    private void AdvanceCooldowns(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (m.MoveCooldownRemaining > 0)
                m.MoveCooldownRemaining = Math.Max(0, m.MoveCooldownRemaining - dt);
            if (m.StunRemaining > 0)
                m.StunRemaining = Math.Max(0, m.StunRemaining - dt);
            if (m.StunCooldown > 0)
                m.StunCooldown = Math.Max(0, m.StunCooldown - dt);
            if (m.DynamiteThrowCooldown > 0)
                m.DynamiteThrowCooldown = Math.Max(0, m.DynamiteThrowCooldown - dt);
        }
        foreach (var mo in _monsters)
            if (mo.StunRemaining > 0)
                mo.StunRemaining = Math.Max(0, mo.StunRemaining - dt);
    }

    // A miner who lingers on a crack rather than crossing it loads the floor a
    // second time and falls through. Crossing miners reset CrackDwell on each move,
    // so a normal walk-through never trips this.
    private void AdvanceCracks(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (!m.Alive) continue;
            var t = Grid.Get(m.Pos);
            if (t == TileType.Cracked || t == TileType.Crumbling)
            {
                m.CrackDwell += dt;
                if (m.CrackDwell >= Config.CrackDwellSeconds)
                {
                    Grid.Set(m.Pos, TileType.Pit);
                    _events.Add(new CrackCollapsed(m.Pos));
                    CollapseKill(m);
                }
            }
            else m.CrackDwell = 0;
        }

        // Ground monsters that linger on a crack give way too (same threshold).
        foreach (var mo in _monsters)
        {
            if (!mo.Alive || !MonsterLoadsCracks(mo.Kind)) continue;
            var t = Grid.Get(mo.Pos);
            if (t == TileType.Cracked || t == TileType.Crumbling)
            {
                mo.CrackDwell += dt;
                if (mo.CrackDwell >= Config.CrackDwellSeconds)
                    CollapseMonster(mo);
            }
            else mo.CrackDwell = 0;
        }
    }

    // A breached vent advances one BFS ring per interval, converting open floor to
    // lava up to its tile budget. Floor touching water solidifies to a fragile
    // Cracked crust instead (water quenches lava) and the flow stops there.
    private void AdvanceLava(double dt)
    {
        if (_lavaVents.Count == 0) return;
        foreach (var vent in _lavaVents)
        {
            if (!vent.Active || vent.Budget <= 0) continue;
            vent.Timer += dt;
            if (vent.Timer < Config.LavaSpreadIntervalSeconds) continue;
            vent.Timer -= Config.LavaSpreadIntervalSeconds;
            SpreadOneRing(vent);
        }
        KillOccupantsOnLethalTiles();
    }

    private void SpreadOneRing(LavaVent vent)
    {
        var candidates = new List<GridPos>();
        var seen = new HashSet<GridPos>();
        foreach (var f in vent.Frontier)
            foreach (var d in Card)
            {
                var n = f + d.ToOffset();
                if (Grid.InBounds(n) && Grid.Get(n) == TileType.Floor && seen.Add(n))
                    candidates.Add(n);
            }
        var nextFrontier = new List<GridPos>();
        foreach (var p in candidates)
        {
            if (vent.Budget <= 0) break;
            if (IsWaterAdjacent(p))
            {
                Grid.Set(p, TileType.Cracked);
                _events.Add(new LavaQuenched(p));
            }
            else
            {
                Grid.Set(p, TileType.Lava);
                _events.Add(new LavaSpread(p));
                nextFrontier.Add(p);
            }
            vent.Budget--;
        }
        vent.Frontier = nextFrontier;
    }

    private void AdvanceDormantMonsters()
    {
        foreach (var mo in _monsters)
        {
            if (!mo.Alive || !mo.Dormant) continue;
            if (SkeletonWakeCheck(mo.Pos))
            {
                mo.Dormant = false;
                _events.Add(new SkeletonAroused(mo.Id));
            }
        }
    }

    private bool SkeletonWakeCheck(GridPos pos)
    {
        foreach (var ns in _noiseSources)
        {
            int d = pos.ManhattanTo(ns.Pos);
            if (ns.Kind == NoiseKind.Explosion && d <= Config.SkeletonExplosionWakeRadius) return true;
            if (ns.Kind == NoiseKind.Pickaxe   && d <= Config.SkeletonPickaxeWakeRadius)   return true;
            if (ns.Kind == NoiseKind.Stone      && d <= Config.SkeletonStoneWakeRadius)     return true;
        }
        // Wake on miner proximity — feels right that walking past bones disturbs them.
        foreach (var m in _miners.Values)
            if (m.Alive && pos.ManhattanTo(m.Pos) <= Config.SkeletonProximityWakeRadius) return true;
        return false;
    }

    private void AdvanceMonsters(double dt)
    {
        if (_monsters.Count == 0) return;

        var aliveMiners = _miners.Values.Where(m => m.Alive).ToList();

        foreach (var mo in _monsters.OrderBy(x => x.Id))
        {
            if (!mo.Alive) continue;

            if (mo.Dormant) continue;

            if (mo.SlowTimer > 0)
            {
                mo.SlowTimer = Math.Max(0, mo.SlowTimer - dt);
                if (mo.SlowTimer <= 0) mo.SlowMultiplier = 1.0;
            }

            mo.MoveCooldownRemaining -= dt;
            if (mo.MoveCooldownRemaining > 0) continue;
            if (mo.StunRemaining > 0) { mo.MoveCooldownRemaining = MonsterCadence(mo.Kind); continue; }

            // Step first, THEN check mold so the cadence reset below uses
            // the multiplier current AFTER landing (slow takes effect immediately).
            var target = aliveMiners.Count > 0
                ? aliveMiners.MinBy(m => m.Pos.ManhattanTo(mo.Pos))
                : null;
            StepMonster(mo, target);

            if (mo.Alive && mo.Kind != MonsterKind.Ghost && _molds.Any(mp => mp.Pos == mo.Pos))
            {
                mo.SlowTimer = Config.MoldSlowSeconds;
                mo.SlowMultiplier = Config.MoldSlowFactor;
            }

            mo.MoveCooldownRemaining += MonsterCadenceFor(mo) * mo.SlowMultiplier;
        }

        // Kill any ghost currently inside a lantern's AOE
        foreach (var mo in _monsters)
        {
            if (!mo.Alive || mo.Kind != MonsterKind.Ghost) continue;
            if (InLanternLight(mo.Pos))
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }
    }

    private void StepMonster(Monster mo, Miner? target)
    {
        Direction? dir = mo.Kind switch
        {
            MonsterKind.Slime                                     => SlimeDir(mo, target),
            MonsterKind.Ghost                                     => GhostDir(mo, target),
            MonsterKind.Goat                                      => GoatDir(mo, target),
            MonsterKind.ZombieMiner                               => ZombieDir(mo, target),
            MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino => ZombieDir(mo, target),
            MonsterKind.WaterSnake                                => ZombieDir(mo, target),
            _ => null,
        };
        if (dir is not { } d) return;

        var next = mo.Pos + d.ToOffset();
        if (!CanMonsterEnter(mo, next)) return;

        var from = mo.Pos;
        mo.Pos = next;
        mo.Facing = d;
        _events.Add(new MonsterMoved(mo.Id, from, next));

        bool immuneToTile = mo.Kind == MonsterKind.Ghost
                         || (mo.Kind == MonsterKind.WaterSnake && Grid.Get(mo.Pos) == TileType.DeepWater);
        if (!immuneToTile && Grid.Get(mo.Pos).IsLethal())
        {
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
            return;
        }

        // Ground monsters load cracked floor like miners (see MonsterCrackLoad).
        MonsterCrackLoad(mo, from);
        if (!mo.Alive) return;

        if (target is { Alive: true } && mo.Pos == target.Pos)
            MaulMiner(target, mo.Kind);

        if (mo.Kind == MonsterKind.SkeletonDino && mo.Alive)
            DinoFloorDamage(mo, from);
    }

    private void DinoFloorDamage(Monster dino, GridPos from)
    {
        if (Grid.InBounds(from) && Grid.Get(from) == TileType.Floor)
        {
            Grid.Set(from, TileType.Cracked);
            _events.Add(new CrackWeakened(from));
        }

        var landed = Grid.Get(dino.Pos);
        if (landed != TileType.Cracked && landed != TileType.Crumbling) return;

        Grid.Set(dino.Pos, TileType.Pit);
        _events.Add(new CrackCollapsed(dino.Pos));
        dino.Alive = false;
        _events.Add(new MonsterKilled(dino.Id));
        foreach (var m in _miners.Values)
            if (m.Alive && m.Pos == dino.Pos) CollapseKill(m);
    }

    // Ghosts float over cracks; the dino has its own (more destructive) floor damage.
    private static bool MonsterLoadsCracks(MonsterKind kind) =>
        kind != MonsterKind.Ghost && kind != MonsterKind.SkeletonDino;

    // A ground monster on a weak tile drops through: the tile becomes a Pit, the
    // monster dies, and any miner sharing the tile is crushed (mirrors the dino).
    private void CollapseMonster(Monster mo)
    {
        Grid.Set(mo.Pos, TileType.Pit);
        _events.Add(new CrackCollapsed(mo.Pos));
        mo.Alive = false;
        _events.Add(new MonsterKilled(mo.Id));
        foreach (var m in _miners.Values)
            if (m.Alive && m.Pos == mo.Pos) CollapseKill(m);
    }

    // Ground monsters load cracked floor exactly like miners: stepping onto a
    // Crumbling tile drops them into a Pit, while crossing a fresh Cracked tile
    // wears it to Crumbling for the next loading (dwelling is handled in AdvanceCracks).
    private void MonsterCrackLoad(Monster mo, GridPos from)
    {
        if (!MonsterLoadsCracks(mo.Kind)) return;

        if (Grid.Get(mo.Pos) == TileType.Crumbling)
        {
            CollapseMonster(mo);
            return;
        }

        if (Grid.Get(from) == TileType.Cracked)
        {
            Grid.Set(from, TileType.Crumbling);
            _events.Add(new CrackWeakened(from));
        }
    }

    // Rock blocks terrain-bound monsters; ghosts phase rock but are stopped by deep water.
    private bool CanMonsterEnter(Monster mo, GridPos p)
    {
        if (!Grid.InBounds(p)) return false;
        if (mo.Kind == MonsterKind.Ghost) return Grid.Get(p) != TileType.DeepWater;
        return Grid.Get(p).IsEnterable();
    }

    // Zombie always tracks the nearest living miner — no sense radius, but terrain-bound.
    private Direction? ZombieDir(Monster mo, Miner? target)
    {
        if (target is { Alive: true }) return TowardDir(mo.Pos, target.Pos);
        return Card[_rng.Next(Card.Length)];
    }

    private bool InLanternLight(GridPos pos)
    {
        foreach (var m in _miners.Values)
            if (m.Alive && m.Held == ItemKind.Lantern)
                if (pos.ChebyshevTo(m.Pos) <= Config.LanternRadius) return true;
        foreach (var it in _items)
            if (it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose)
                if (pos.ChebyshevTo(it.Pos) <= Config.LanternRadius) return true;
        return false;
    }

    private Direction? SlimeDir(Monster mo, Miner? target)
    {
        var noise = NearestNoiseSourceInRange(mo.Pos, Config.MonsterSenseRadius);
        if (noise is { } n) return TowardDir(mo.Pos, n.Pos);
        if (target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius)
            return TowardDir(mo.Pos, target.Pos);
        return Card[_rng.Next(Card.Length)];
    }

    private Direction? GhostDir(Monster mo, Miner? target)
    {
        var noise = NearestNoiseSourceInRange(mo.Pos, Config.MonsterSenseRadius);
        if (noise == null && target is not { Alive: true }) return null;
        var dest = noise is { } n ? n.Pos
            : target!.Pos;
        var d = TowardDir(mo.Pos, dest);
        var next = mo.Pos + d.ToOffset();
        if (InLanternLight(next)) return null;   // halt at AOE boundary rather than entering
        return d;
    }

    private Direction? GoatDir(Monster mo, Miner? target)
    {
        var noise = NearestNoiseSourceInRange(mo.Pos, Config.MonsterSenseRadius);
        if (noise is { } n)
            mo.ChargeDir = TowardDir(mo.Pos, n.Pos);

        var ahead = mo.Pos + mo.ChargeDir.ToOffset();
        if (CanMonsterEnter(mo, ahead)) return mo.ChargeDir;

        // Slammed into a wall — turn (toward the miner if sensed, else random) and skip this step.
        mo.ChargeDir = target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius
            ? TowardDir(mo.Pos, target.Pos)
            : Card[_rng.Next(Card.Length)];
        return null;
    }

    // Greedy cardinal step that most reduces Manhattan distance (horizontal preferred on a tie; vertical only when dx == 0).
    private static Direction TowardDir(GridPos from, GridPos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? Direction.East
                 : dx < 0 ? Direction.West
                 : dy > 0 ? Direction.South : Direction.North;
        return dy > 0 ? Direction.South : Direction.North;
    }

    public bool TryMove(int id, Direction dir)
    {
        var m = _miners[id];
        if (!m.Alive) return false;
        if (m.MoveCooldownRemaining > 0) return false;   // gate before facing/activity
        if (m.StunRemaining > 0) return false;           // stunned — can't act

        m.Facing = dir;
        CancelActivity(m);

        var target = m.Pos + dir.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsEnterable()) return false;

        var from = m.Pos;
        m.Pos = target;
        _events.Add(new MinerMoved(id, from, target));

        // Colour-coded gate: stepping onto a live portal teleports to its partner.
        // Placed before the arrival hazard checks so those evaluate at the DESTINATION.
        // Sequential per-miner processing gives "lowest id wins" on an unstable gate for
        // free: the first mover collapses it, later movers find it collapsed → no-op.
        var enteredPortal = _portals.FirstOrDefault(p => p.Pos == target);
        if (enteredPortal is not null && IsPortalActive(enteredPortal))
        {
            var link = LinkOf(enteredPortal)!;
            enteredPortal.CooldownRemaining = Config.PortalCooldownSeconds;
            link.CooldownRemaining = Config.PortalCooldownSeconds;
            if (enteredPortal.Kind == PortalKind.Unstable)
            {
                enteredPortal.Collapsed = true;
                link.Collapsed = true;
            }
            _events.Add(new PortalUsed(id, target, link.Pos, enteredPortal.Kind));
            m.Pos = link.Pos;
            target = link.Pos;   // arrival checks below evaluate at the teleport destination
        }

        if (Grid.Get(target).IsLethal())
            KillByTile(m);

        // Stepping onto an already-weakened (Crumbling) tile is the "second loading":
        // the floor gives way to a hole and crushes you.
        if (m.Alive && Grid.Get(target) == TileType.Crumbling)
        {
            Grid.Set(target, TileType.Pit);
            _events.Add(new CrackCollapsed(target));
            CollapseKill(m);
        }

        // Stepping OFF a fresh crack wears it down to Crumbling (you survived the first
        // crossing, but the floor is now weak for the next loading).
        if (Grid.Get(from) == TileType.Cracked)
        {
            Grid.Set(from, TileType.Crumbling);
            _events.Add(new CrackWeakened(from));
        }

        var killer = _monsters.FirstOrDefault(mo => mo.Alive && mo.Pos == target);
        if (m.Alive && killer != null)
            MaulMiner(m, killer.Kind);

        // Reach Center: must carry at least 5 gold (only enforced when the map has gold veins).
        if (Center is { } c && target == c && FirstToReachCenter < 0 && m.Alive
            && (StartingGoldCount == 0 || m.GoldCollected >= 5))
        {
            FirstToReachCenter = id;
            _events.Add(new MinerReachedCenter(id));
        }

        // Treasure Hunt: auto-deposit when stepping onto own chest tile holding an assigned idol.
        if (m.Alive && Config.TreasureHuntMode
            && _chestPos.TryGetValue(id, out var chestAt) && chestAt == target
            && m.Held is ItemKind heldIdol && heldIdol.IsIdol()
            && _idolAssignments.TryGetValue(id, out var assign)
            && (heldIdol == assign.A || heldIdol == assign.B))
        {
            m.Held = null;
            _idolsFound[id]++;
            _events.Add(new IdolDeposited(id, heldIdol));
        }

        if (m.Alive && _molds.Any(mo => mo.Pos == target))
            ApplyEffect(id, EffectKind.SlowMold, EffectChannel.MoveSpeed,
                        Config.MoldSlowFactor, Config.MoldSlowSeconds);

        // Trip mines: stepping onto a planted trap detonates it.
        if (m.Alive)
        {
            for (int ti = _tripCharges.Count - 1; ti >= 0; ti--)
            {
                var tc = _tripCharges[ti];
                if (tc.Pos != target) continue;
                _tripCharges.RemoveAt(ti);
                _events.Add(new TripMineTriggered(tc.OwnerId, id, target));
                DetonateAt(target, tc.BlastBonus, tc.OwnerId);
                break;
            }
        }

        m.MoveCooldownRemaining = EffectiveMoveSeconds(m);   // set from destination tile
        m.CrackDwell = 0;   // moving resets the linger timer; only standing still trips a crack
        return true;
    }

    private void CancelActivity(Miner m)
    {
        m.Activity = ActivityKind.None;
        m.ActivitySecondsRemaining = 0;
    }

    public bool TryStartMining(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;
        if (m.StunRemaining > 0) return false;
        if (m.Activity != ActivityKind.None) return false;

        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target)) return false;

        // Pickaxe stun: if facing an adjacent rival miner or Goat, stun them instead of mining.
        if (m.StunCooldown <= 0 && !Grid.Get(target).IsMinable())
        {
            foreach (var rival in _miners.Values)
            {
                if (rival.Id == id || !rival.Alive || rival.Pos != target) continue;
                rival.StunRemaining = 0.8;
                m.StunCooldown = 2.0;
                _events.Add(new MinerStunned(id, rival.Id));
                return true;
            }
            foreach (var mo in _monsters)
            {
                if (mo.Kind != MonsterKind.Goat || !mo.Alive || mo.Pos != target) continue;
                mo.StunRemaining = 0.8;
                m.StunCooldown = 2.0;
                _events.Add(new MonsterStunned(id, mo.Id));
                return true;
            }
        }

        if (!Grid.Get(target).IsMinable())
        {
            if (Config.UnstableFloorEnabled && Grid.Get(target) == TileType.Floor)
            {
                m.Activity = ActivityKind.FloorCracking;
                m.ActivityTarget = target;
                m.ActivitySecondsRemaining = Config.PickaxeSeconds;
                _events.Add(new ActivityStarted(id, ActivityKind.FloorCracking, target));
                return true;
            }
            return false;
        }

        m.Activity = ActivityKind.Mining;
        m.ActivityTarget = target;
        m.ActivitySecondsRemaining = Config.PickaxeSeconds;
        _events.Add(new ActivityStarted(id, ActivityKind.Mining, target));
        return true;
    }

    public bool TryStartPlanting(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;
        if (!Config.DynamiteEnabled) return false;
        if (m.StunRemaining > 0) return false;
        if (m.Activity != ActivityKind.None) return false;

        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target)) return false;

        var tile = Grid.Get(target);
        bool isWallPlant = tile.IsBlastable()
                           && !_charges.Any(c => c.WallPos == target)
                           && LiveChargeCount(id) < Config.MaxLiveChargesPerMiner;
        bool isTripMine  = Config.TripMinesEnabled
                           && tile == TileType.Floor
                           && !_tripCharges.Any(tc => tc.Pos == target);

        if (!isWallPlant && !isTripMine) return false;

        m.Activity = ActivityKind.Planting;
        m.ActivityTarget = target;
        m.ActivitySecondsRemaining = Config.PlantSeconds;
        _events.Add(new ActivityStarted(id, ActivityKind.Planting, target));
        return true;
    }

    private int LiveChargeCount(int ownerId) => _charges.Count(c => c.OwnerId == ownerId);

    // --- Global prize event -------------------------------------------------
    // A single announced objective that spawns periodically in competitive modes and
    // that players fight over. Host-authoritative; clients render from the snapshot.
    private void AdvancePrizeEvent(double dt)
    {
        if (!Config.PrizeEventsEnabled || Config.Mode == GameMode.Expedition) return;
        if (!_prizeArmed) { _prizeArmed = true; _prizeArmTimer = Config.PrizeFirstDelaySeconds; }

        switch (_prizeState)
        {
            case PrizeState.Idle:
                _prizeArmTimer -= dt;
                if (_prizeArmTimer <= 0) EnterPrizeTelegraph();
                break;
            case PrizeState.Telegraph:
                _prizeStateTimer -= dt;
                if (_prizeStateTimer <= 0) { _prizeState = PrizeState.Active; _prizeStateTimer = Config.PrizeExpirySeconds; }
                break;
            case PrizeState.Active:
                _prizeStateTimer -= dt;
                UpdatePrizeClaim(dt);
                if (_prizeState == PrizeState.Active && _prizeStateTimer <= 0) ExpirePrize();
                break;
        }
    }

    private void EnterPrizeTelegraph()
    {
        _prizeType = (PrizeType)_rng.Next(4);
        _prizePos = SelectPrizeTile();
        _prizeClaimProgress = 0;
        _prizeHolderId = -1;
        _prizeState = PrizeState.Telegraph;
        _prizeStateTimer = Config.PrizeTelegraphSeconds;
        _events.Add(new PrizeTelegraphed(_prizeType, _prizePos));
    }

    private void ExpirePrize()
    {
        _events.Add(new PrizeExpired());
        ResetPrize();
    }

    private void ClaimPrize(int minerId)
    {
        ApplyPrizeReward(minerId);
        _events.Add(new PrizeClaimed(minerId, _prizeType));
        ResetPrize();
    }

    private void ResetPrize()
    {
        _prizeState = PrizeState.Idle;
        _prizeHolderId = -1;
        _prizeClaimProgress = 0;
        _prizeArmTimer = Config.PrizeIntervalSeconds + _rng.NextDouble() * Config.PrizeJitterSeconds;
    }

    // A safe, dry floor tile clear of every living player, biased toward the tile that
    // minimizes the farthest player's distance (pulls the pack together).
    private GridPos SelectPrizeTile()
    {
        GridPos best = default; int bestScore = int.MinValue; bool found = false;
        int spacing = Config.PrizeMinPlayerSpacing;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var p = new GridPos(_rng.Next(Grid.Width), _rng.Next(Grid.Height));
            if (Grid.Get(p) != TileType.Floor) continue;
            if (_miners.Values.Any(m => m.Alive && m.Pos.ChebyshevTo(p) < spacing)) continue;
            int maxD = _miners.Values.Where(m => m.Alive)
                .Select(m => m.Pos.ChebyshevTo(p)).DefaultIfEmpty(0).Max();
            int score = -maxD;
            if (score > bestScore) { bestScore = score; best = p; found = true; }
            if (attempt > 40 && found) break;
        }
        if (!found)
            best = FirstFloorTile();
        return best;
    }

    private GridPos FirstFloorTile()
    {
        for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
                if (Grid.Get(new GridPos(x, y)) == TileType.Floor) return new GridPos(x, y);
        return new GridPos(Grid.Width / 2, Grid.Height / 2);
    }

    // Type-specific claim logic. Later phases fill in the other prize types.
    private void UpdatePrizeClaim(double dt)
    {
        switch (_prizeType)
        {
            case PrizeType.GrabAndGo:
                foreach (var m in _miners.Values)
                    if (m.Alive && m.Pos == _prizePos) { ClaimPrize(m.Id); return; }
                break;

            case PrizeType.MineOut:
            {
                // Channel: a single un-stunned miner must stand on the tile. Leaving,
                // dying, or being stunned resets the crack-open progress.
                var channeler = _miners.Values.FirstOrDefault(m =>
                    m.Alive && m.StunRemaining <= 0 && m.Pos == _prizePos);
                if (channeler == null) { _prizeClaimProgress = 0; _prizeHolderId = -1; break; }
                if (_prizeHolderId != channeler.Id) { _prizeHolderId = channeler.Id; _prizeClaimProgress = 0; }
                _prizeClaimProgress += dt / Config.PrizeMineSeconds;
                if (_prizeClaimProgress >= 1.0) ClaimPrize(channeler.Id);
                break;
            }

            case PrizeType.HoldPoint:
            {
                // Hold the ring solo: empty pauses progress, two or more contests and resets it.
                var inRing = _miners.Values
                    .Where(m => m.Alive && m.Pos.ChebyshevTo(_prizePos) <= Config.PrizeHoldRadius)
                    .ToList();
                if (inRing.Count == 0) break;
                if (inRing.Count > 1) { _prizeClaimProgress = 0; _prizeHolderId = -1; break; }
                var holder = inRing[0];
                if (_prizeHolderId != holder.Id) { _prizeHolderId = holder.Id; _prizeClaimProgress = 0; }
                _prizeClaimProgress += dt / Config.PrizeHoldSeconds;
                if (_prizeClaimProgress >= 1.0) ClaimPrize(holder.Id);
                break;
            }

            case PrizeType.CarryRelic:
            {
                if (_prizeHolderId < 0)
                {
                    // Ungrabbed: the first miner to reach the relic tile picks it up.
                    var picker = _miners.Values.FirstOrDefault(m => m.Alive && m.Pos == _prizePos);
                    if (picker != null) _prizeHolderId = picker.Id;
                }
                else if (!_miners.TryGetValue(_prizeHolderId, out var h) || !h.Alive || h.StunRemaining > 0)
                {
                    // Holder died or was stunned: drop the relic at their last spot, grabbable again.
                    if (_miners.TryGetValue(_prizeHolderId, out var dropper)) _prizePos = dropper.Pos;
                    _prizeHolderId = -1;
                }
                else
                {
                    _prizePos = h.Pos;                          // the relic rides the carrier
                    if (h.Pos == h.SpawnPos) ClaimPrize(h.Id);  // banked at home
                }
                break;
            }
        }
    }

    // Mode-appropriate reward, tuned to how each mode is won. Reuses existing buff infra.
    private void ApplyPrizeReward(int minerId)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        switch (Config.Mode)
        {
            case GameMode.TreasureHunt:
                // A big leg-up: one of the two idols needed to win.
                _idolsFound[minerId] = _idolsFound.GetValueOrDefault(minerId) + 1;
                break;
            case GameMode.LastManStanding:
                // Survive-and-hunt: brief invulnerability + a bigger blast.
                m.InvulnerableRemaining = Math.Max(m.InvulnerableRemaining, 4.0);
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                    BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds);
                break;
            case GameMode.ReachCenter:
                // Win the race: a haste burst (SpeedPotion magnitude < 1 = faster).
                ApplyEffect(minerId, EffectKind.SpeedPotion, EffectChannel.MoveSpeed,
                    BuffTuning.SpeedMagnitude, BuffTuning.SpeedSeconds);
                break;
            case GameMode.DemolitionDerby:
                // Combat loadout: extra throwing stones + a bigger blast.
                AddStones(minerId, 3);
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                    BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds);
                break;
            case GameMode.GoldRush:
            default:
                m.GoldCollected += Config.PrizeGoldReward;
                break;
        }
    }

    private void AdvanceTreasureHeist(double dt)
    {
        if (!Config.TreasureHeistMode) return;

        // Unearth detection: once the buried treasure item is revealed (Loose), adopt it as
        // the mode's tracked treasure token and remove it from the generic item list so the
        // normal pickup/buff path never touches it.
        if (!_treasureUnearthed)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Kind == _treasureKind && _items[i].Placement == ItemPlacement.Loose)
                {
                    _treasurePos = _items[i].Pos;
                    _items.RemoveAt(i);
                    _treasureUnearthed = true;
                    break;
                }
            }
        }

        if (_treasureUnearthed)
            UpdateTreasureCarry(dt);

        foreach (var m in _miners.Values)
            if (m.Held is { } held && (!m.Alive || m.StunRemaining > 0))
                DropHeldItem(m, held);

        if (Config.TreasureRespawnEnabled)
            AdvanceRespawns(dt);

        // Sudden-death accrual: after the clock expires, a lone uncontested holder wins.
        if (TimeExpired && _treasureHolderId >= 0 && _miners.TryGetValue(_treasureHolderId, out var sh) && sh.Alive)
        {
            int nearest = _miners.Values
                .Where(r => r.Id != sh.Id && r.Alive)
                .Select(r => r.Pos.ChebyshevTo(sh.Pos))
                .DefaultIfEmpty(int.MaxValue).Min();
            if (nearest > Config.TreasureSneakRadius) _suddenDeathHold += dt; else _suddenDeathHold = 0;
            if (_suddenDeathHold >= Config.SuddenDeathHoldSeconds) _suddenDeathWinner = sh.Id;
        }
        else _suddenDeathHold = 0;
    }

    private void AdvanceRespawns(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (m.Alive) { _respawnTimers.Remove(m.Id); continue; }
            double t = _respawnTimers.GetValueOrDefault(m.Id) + dt;
            if (t < Config.RespawnSeconds) { _respawnTimers[m.Id] = t; continue; }
            _respawnTimers.Remove(m.Id);
            m.Alive = true;
            m.Pos = m.SpawnPos;
            m.DeathCause = DeathCause.None;
            m.StunRemaining = 0;
            m.Held = null;
            m.StoneCount = Config.StartingStones;
            m.EffectsInternal.Clear();
            _events.Add(new LifeRestored(m.Id));
        }
    }

    private static readonly GridPos[] Neighbours8 =
    {
        new(1,0), new(-1,0), new(0,1), new(0,-1),
        new(1,1), new(1,-1), new(-1,1), new(-1,-1),
    };

    private void DropHeldItem(Miner m, ItemKind kind)
    {
        // Prefer an adjacent free floor tile so the miner does not instantly re-grab it on wake.
        GridPos dest = m.Pos;
        foreach (var off in Neighbours8)
        {
            var p = new GridPos(m.Pos.X + off.X, m.Pos.Y + off.Y);
            if (Grid.InBounds(p) && Grid.Get(p) == TileType.Floor && _items.All(it => it.Pos != p))
            { dest = p; break; }
        }
        _items.Add(new Item(dest, kind, ItemPlacement.Loose));
        m.Held = null;
    }

    private void UpdateTreasureCarry(double dt)
    {
        if (_treasureHolderId < 0)
        {
            // Loose: first alive miner standing on it grabs it.
            var picker = _miners.Values.FirstOrDefault(m => m.Alive && m.StunRemaining <= 0 && m.Pos == _treasurePos);
            if (picker != null)
            {
                _treasureHolderId = picker.Id;
                if (!_treasureFound) { _treasureFound = true; _events.Add(new TreasureFound(picker.Id)); }
                else _events.Add(new TreasureRecovered(picker.Id));
            }
        }
        else if (!_miners.TryGetValue(_treasureHolderId, out var h) || !h.Alive || h.StunRemaining > 0)
        {
            // Holder incapacitated: drop it where they stand, grabbable again.
            if (_miners.TryGetValue(_treasureHolderId, out var dropper))
            {
                _treasurePos = dropper.Pos;
                _events.Add(new TreasureDropped(_treasureHolderId));
            }
            _treasureHolderId = -1;
            _sneakHold = 0;
        }
        else
        {
            _treasurePos = h.Pos; // the treasure rides the carrier
            _holdSeconds[h.Id] = _holdSeconds.GetValueOrDefault(h.Id) + dt;

            _sneakCooldownRemaining = Math.Max(0, _sneakCooldownRemaining - dt);
            int nearest = _miners.Values
                .Where(r => r.Id != h.Id && r.Alive)
                .Select(r => r.Pos.ChebyshevTo(h.Pos))
                .DefaultIfEmpty(int.MaxValue).Min();
            if (nearest > Config.TreasureSneakRadius) _sneakHold += dt; else _sneakHold = 0;
            if (_sneakHold >= Config.TreasureSneakSeconds && _sneakCooldownRemaining <= 0)
            {
                _events.Add(new TreasureSneaking(h.Id));
                _sneakCooldownRemaining = Config.TreasureSneakCooldown;
                _sneakHold = 0;
            }
        }
    }

    public void Tick(double dt)
    {
        Elapsed += dt;
        AdvanceEffects(dt);
        AdvanceInvulnerability(dt);
        AdvanceMolds(dt);
        AdvanceNoiseSources(dt);
        AdvanceCooldowns(dt);
        AdvancePrizeEvent(dt);
        AdvanceTreasureHeist(dt);
        AdvancePortals(dt);
        AdvanceCracks(dt);
        AdvanceLava(dt);
        AdvanceDormantMonsters(); // pre-existing noise (stone/pickaxe) wakes skeletons before they step
        AdvanceMonsters(dt);
        AdvanceOctopus(dt);
        AdvanceReels();
        AdvanceFalls(dt);
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        PickUpItems();
        AdvanceCharges(chargesThisTick, dt);
        AdvanceFlood();
        AdvanceDormantMonsters(); // explosion noise (from AdvanceCharges) wakes skeletons same tick
    }

    private void AdvanceActivities(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (!m.Alive || m.Activity == ActivityKind.None) continue;

            m.ActivitySecondsRemaining -= dt;
            if (m.ActivitySecondsRemaining > 0) continue;

            CompleteActivity(m);
        }
    }

    // Rolls chest loot but skips any perm buff the miner has already capped, so a chest
    // always does something. Falls back to a life restore when every perm buff is maxed.
    private ItemKind RollUsefulChestLoot(Miner m) => ChestLootTable.Roll(_rng);

    private void PickUpItems()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (item.Placement == ItemPlacement.Buried) continue;   // not collectible until unburied
            if (item.Kind.IsCarried()) continue;        // grabbed via the Use verb, not walk-over
            foreach (var m in _miners.Values)
            {
                if (!m.Alive || m.Pos != item.Pos) continue;
                _items.RemoveAt(i);
                ApplyBuff(m.Id, item.Kind);
                _events.Add(new ItemPickedUp(m.Id, item.Pos, item.Kind));
                break; // one miner collects it
            }
        }
    }

    /// <summary>The Use verb (Space). Context-sensitive: if the miner stands on a carried
    /// ground item, pick it up (swapping with whatever is held); otherwise use the held item.</summary>
    public bool TryUseItem(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;

        // 1. pickup / swap when standing on a carried ground item
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.Pos != m.Pos || it.Placement == ItemPlacement.Buried || !it.Kind.IsCarried()) continue;
            // Only the owner can pick up a placed TreasureChest
            if (it.Kind == ItemKind.TreasureChest &&
                (!_chestPos.TryGetValue(m.Id, out var ownedAt) || ownedAt != it.Pos)) continue;
            var taken = it.Kind;
            if (m.Held is { } heldKind)
            {
                _items[i] = new Item(m.Pos, heldKind, ItemPlacement.Loose);
                // Track the chest's new floor position so deposit + re-pickup still work.
                if (heldKind == ItemKind.TreasureChest) _chestPos[m.Id] = m.Pos;
            }
            else { _items.RemoveAt(i); }
            if (taken == ItemKind.TreasureChest) _chestPos[m.Id] = null;
            m.Held = taken;
            _events.Add(new ItemPickedUp(m.Id, m.Pos, taken));
            if (Config.ExpeditionTreasureKind is { } tk && taken == tk && !EscapeOpen && EscapeTile is not null)
            {
                EscapeOpen = true;
                _events.Add(new EscapeOpened());
            }
            return true;
        }

        // 2. use what is held
        if (m.Held is not { } held) return false;
        return held switch
        {
            ItemKind.WaterPlank    => TryPlacePlank(m),
            ItemKind.SlowMold      => DropMold(m),
            ItemKind.Lantern       => DropLantern(m),
            ItemKind.Detonator     => TryPlantDetonator(m),
            ItemKind.Reel          => TryDetonateReel(m),
            ItemKind.TreasureChest => TryPlaceTreasureChest(m),
            var k when k.IsIdol()  => DropIdol(m, k),
            _ => false,
        };
    }

    // Drops a spread of timed trap patches in a Manhattan disc around the miner's tile
    // (refreshing any that already exist), so the slow-zone is harder to skirt.
    private bool DropMold(Miner m)
    {
        int r = Config.MoldRadius;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;
                var p = new GridPos(m.Pos.X + dx, m.Pos.Y + dy);
                if (!Grid.InBounds(p) || !Grid.Get(p).IsEnterable()) continue;
                var existing = _molds.FirstOrDefault(mo => mo.Pos == p);
                if (existing is not null) existing.RemainingSeconds = Config.MoldSeconds;
                else _molds.Add(new MoldPatch(p, Config.MoldSeconds));
            }
        m.Held = null;
        _events.Add(new MoldDropped(m.Pos));
        return true;
    }

    private bool DropLantern(Miner m)
    {
        _items.Add(new Item(m.Pos, ItemKind.Lantern, ItemPlacement.Loose));
        m.Held = null;
        return true;
    }

    private bool DropIdol(Miner m, ItemKind k)
    {
        _items.Add(new Item(m.Pos, k, ItemPlacement.Loose));
        m.Held = null;
        return true;
    }

    private bool TryPlaceTreasureChest(Miner m)
    {
        if (!Grid.Get(m.Pos).IsEnterable()) return false;
        _items.Add(new Item(m.Pos, ItemKind.TreasureChest, ItemPlacement.Toolbox));
        _chestPos[m.Id] = m.Pos;
        m.Held = null;
        _events.Add(new TreasureChestPlaced(m.Id, m.Pos));
        return true;
    }

    // Lays a permanent, flood-immune Plank tile on the faced water-or-pit tile.
    private bool TryPlacePlank(Miner m)
    {
        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsBridgeable()) return false;
        Grid.Set(target, TileType.Plank);
        m.Held = null;
        _events.Add(new PlankPlaced(target));
        return true;
    }

    // Flips any buried item on a tile to a loose floor pickup. Called wherever rock becomes floor
    // (mining completes, blast disc) so a destroyed cache spills its item onto the open tile.
    private void UnburyItemsAt(GridPos pos)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (it.Placement != ItemPlacement.Buried || it.Pos != pos) continue;
            _items[i] = it with { Placement = ItemPlacement.Loose };
            _events.Add(new ItemUnburied(it.Pos, it.Kind));
        }
    }

    private void ApplyBuff(int minerId, ItemKind kind)
    {
        var m = _miners[minerId];
        switch (kind)
        {
            case ItemKind.SpeedPotion:
                ApplyEffect(minerId, EffectKind.SpeedPotion, EffectChannel.MoveSpeed,
                    BuffTuning.SpeedMagnitude, BuffTuning.SpeedSeconds);
                break;
            case ItemKind.LongerVision:
                ApplyEffect(minerId, EffectKind.LongerVision, EffectChannel.VisionRadius,
                    BuffTuning.VisionMagnitude, BuffTuning.VisionSeconds);
                break;
            case ItemKind.BiggerBlast:
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                    BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds);
                break;
            case ItemKind.Chest:
                ApplyBuff(minerId, RollUsefulChestLoot(m));
                break;
            case ItemKind.BossChest:
                if (!EscapeOpen && EscapeTile is not null)
                {
                    EscapeOpen = true;
                    _events.Add(new EscapeOpened());
                }
                break;
            case ItemKind.LifePotion:
                _events.Add(new LifeRestored(minerId));
                break;
            case ItemKind.Stone:
                AddStones(minerId, 2 + _rng.Next(2)); // a small pile: 2 or 3
                break;
        }
    }

    private void AdvanceCharges(List<Charge> snapshot, double dt)
    {
        // Use pre-tick snapshot so newly-planted charges don't advance this tick,
        // and so Detonate's removal of a charge doesn't skip others.
        foreach (var charge in snapshot)
        {
            charge.FuseRemaining -= dt;
            if (charge.FuseRemaining <= 0)
                Detonate(charge);
        }
    }

    // --- Flood (rising-water modifier) -----------------------------------
    // Deep water rises inward from the map edges, paced by the match clock: a
    // tile floods when its edge-distance <= floor(progress * maxDist). The current
    // front ring is shallow (a one-ring warning); everything behind it is deep
    // (lethal). Only open space floods; rock stays a wall until mined. Idempotent
    // on progress, so it also re-floods open tiles freshly exposed inside the zone.
    private void AdvanceFlood()
    {
        if (!_flooding || _timeLimit is not { } lim) return;
        int maxDist = (Math.Min(Grid.Width, Grid.Height) - 1) / 2;
        if (maxDist < 1) return;
        double progress = Math.Min(1.0, Elapsed / lim);
        int floodedMaxDist = (int)(progress * maxDist);
        if (floodedMaxDist < 1) return;

        foreach (var p in Grid.Positions())
        {
            int d = EdgeDistance(p);
            if (d < 1 || d > floodedMaxDist) continue;
            var cur = Grid.Get(p);
            if (cur != TileType.Floor && !cur.IsWater()) continue; // walls don't flood
            var target = d == floodedMaxDist ? TileType.ShallowWater : TileType.DeepWater;
            if (cur != target)
            {
                Grid.Set(p, target);
                _events.Add(new TileFlooded(p, target));
            }
        }
        KillOccupantsOnLethalTiles();
    }

    private int EdgeDistance(GridPos p) =>
        Math.Min(Math.Min(p.X, p.Y), Math.Min(Grid.Width - 1 - p.X, Grid.Height - 1 - p.Y));

    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private bool IsWaterAdjacent(GridPos p)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (Grid.InBounds(n) && Grid.Get(n).IsWater()) return true;
        }
        return false;
    }

    // Breaching the rock cap of a vent (mining or blasting an adjacent tile) wakes it.
    // A woken vent seeds its frontier on its own tile and creeps a ring per interval.
    private void ActivateVentsAround(GridPos pos)
    {
        if (_lavaVents.Count == 0) return;
        foreach (var vent in _lavaVents)
        {
            if (vent.Active) continue;
            if (vent.Pos.ManhattanTo(pos) == 1)
            {
                vent.Active = true;
                vent.Frontier = new List<GridPos> { vent.Pos };
            }
        }
    }

    public void SetGold(int minerId, int amount)
    {
        if (_miners.TryGetValue(minerId, out var m)) m.GoldCollected = amount;
    }

    private void MaulMiner(Miner m, MonsterKind kind)
    {
        if (!m.Alive || m.InvulnerableRemaining > 0) return;
        if (ShopPos is { } shop && m.Pos == shop) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = kind switch
        {
            MonsterKind.Slime          => DeathCause.Slimed,
            MonsterKind.Ghost          => DeathCause.Terrified,
            MonsterKind.Goat           => DeathCause.Headbutted,
            MonsterKind.ZombieMiner    => DeathCause.Mauled,
            MonsterKind.SkeletonHuman  => DeathCause.Boned,
            MonsterKind.SkeletonDino   => DeathCause.Boned,
            MonsterKind.WaterSnake     => DeathCause.Bitten,
            _ => DeathCause.Mauled,
        };
        _events.Add(new MinerMauled(m.Id));
    }

    // Called wherever a GoldRock tile becomes Floor. When the last vein falls and an
    // escape tile is set, the exit opens (once).
    private void OnGoldCleared()
    {
        if (_goldRemaining > 0) _goldRemaining--;
        if (!EscapeOpen && EscapeTile is not null
            && StartingGoldCount > 0 && GoldCollectedFraction >= 0.5
            && Config.ExpeditionTreasureKind is null)
        {
            EscapeOpen = true;
            _events.Add(new EscapeOpened());
        }
    }

    // Kills a miner on a lethal tile, picking the cause/event from the tile under them:
    // a pit makes you Fall, lava burns you, deep water makes you Drown.
    private void KillByTile(Miner m)
    {
        if (m.InvulnerableRemaining > 0) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        var t = Grid.Get(m.Pos);
        if (t == TileType.Pit)
        {
            m.DeathCause = DeathCause.Fell;
            _events.Add(new MinerFell(m.Id));
        }
        else if (t is TileType.Lava or TileType.LavaVent)
        {
            m.DeathCause = DeathCause.Burned;
            _events.Add(new MinerBurned(m.Id));
        }
        else
        {
            m.DeathCause = DeathCause.Drowned;
            _events.Add(new MinerDrowned(m.Id));
        }
    }

    // Kills a miner caught in a collapsing crack (distinct from KillByTile, which
    // assigns Fell/Drowned for stepping onto an already-lethal pit/deep-water tile).
    private void CollapseKill(Miner m)
    {
        if (!m.Alive || m.InvulnerableRemaining > 0) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = DeathCause.Crushed;
        _events.Add(new MinerCrushed(m.Id));
    }

    // A "scree" tile just gave way (mined or blasted). Rolls against its trigger chance;
    // on a hit it fills every Floor tile within the tile's Chebyshev radius with Rock and
    // crushes any miner caught in that square, then announces the collapse for feedback.
    private void TriggerScreeCollapse(GridPos pos, TileType screeType)
    {
        if (_rng.NextDouble() >= screeType.ScreeTriggerChance()) return;

        int radius = screeType.ScreeCollapseRadius();
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;   // the mined/blasted tile stays Floor — you got the rock
                var p = new GridPos(pos.X + dx, pos.Y + dy);
                if (!Grid.InBounds(p) || Grid.Get(p) != TileType.Floor) continue;
                Grid.Set(p, TileType.Rock);
                _events.Add(new RockFell(p));
            }
        foreach (var m in _miners.Values)
            if (m.Pos.ChebyshevTo(pos) <= radius)
                CollapseKill(m);
        _events.Add(new ScreeCollapsed(pos, radius));
    }

    private void AdvanceOctopus(double dt)
    {
        if (_octopus is null) return;
        _octopus.Advance(dt, Grid, _miners.Values);
        foreach (var m in _miners.Values)
            if (m.Alive && m.Pos == _octopus.Pos)
                OctopusMaulKill(m);
    }

    private void OctopusMaulKill(Miner m)
    {
        m.Alive      = false;
        m.Activity   = ActivityKind.None;
        m.DeathCause = DeathCause.Mauled;
        _events.Add(new MinerKilled(m.Id));
    }

    // Kills any living miner standing on a now-lethal tile. Covers water rising
    // *under* a stationary miner and lava creeping under one (move-time deaths
    // stay in TryMove); the tile picks the cause via KillByTile.
    private void KillOccupantsOnLethalTiles()
    {
        foreach (var m in _miners.Values)
        {
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
                KillByTile(m);
        }
        // Terrain-bound monsters die when lava/flood creeps under them, mirroring miners.
        // Ghosts float and stay immune. Water snakes are immune to deep water only.
        foreach (var mo in _monsters)
        {
            if (!mo.Alive) continue;
            bool immuneToTile = mo.Kind == MonsterKind.Ghost
                             || (mo.Kind == MonsterKind.WaterSnake && Grid.Get(mo.Pos) == TileType.DeepWater);
            if (!immuneToTile && Grid.Get(mo.Pos).IsLethal())
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }
    }

    private void Detonate(Charge charge)
    {
        _charges.Remove(charge);
        DetonateAt(charge.WallPos, charge.BlastBonus, charge.OwnerId);
    }

    private void DetonateAt(GridPos wallPos, int blastBonus, int ownerId)
    {
        var destroyed = new List<GridPos>();
        var collapsedCracks = new List<GridPos>();
        var screeHits = new List<(GridPos Pos, TileType Type)>();
        int r = Config.BlastRockRadius + blastBonus;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new GridPos(wallPos.X + dx, wallPos.Y + dy);
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;        // Manhattan disc
                if (!Grid.InBounds(p)) continue;
                if (Grid.Get(p) is TileType.Cracked or TileType.Crumbling)
                {
                    Grid.Set(p, TileType.Pit);                        // the blast shakes the weak floor down
                    _events.Add(new CrackCollapsed(p));
                    collapsedCracks.Add(p);
                    continue;
                }
                var blastTile = Grid.Get(p);
                if (!blastTile.IsBlastable()) continue;
                bool wasGold    = blastTile == TileType.GoldRock;
                bool wasCrystal = blastTile == TileType.CrystalRock;
                Grid.Set(p, TileType.Floor);
                if (wasGold)
                {
                    if (_miners.TryGetValue(ownerId, out var owner) && owner.Alive) owner.GoldCollected++;
                    OnGoldCleared();
                }
                UnburyItemsAt(p);
                ActivateVentsAround(p);
                if (wasCrystal) _events.Add(new CrystalShardDropped(p));
                if (blastTile.IsScree()) screeHits.Add((p, blastTile));
                destroyed.Add(p);
            }

        // Scree tiles caught in the blast collapse after the whole disc is cleared, so a
        // collapse never refills a tile the same blast has yet to process.
        foreach (var (pos, type) in screeHits)
            TriggerScreeCollapse(pos, type);

        foreach (var m in _miners.Values)
        {
            if (m.Alive && m.Pos.ChebyshevTo(wallPos) <= Config.BlastKillRadius + blastBonus)
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                m.DeathCause = DeathCause.Exploded;
                _events.Add(new MinerKilled(m.Id));
            }
        }

        foreach (var mo in _monsters)
        {
            if (mo.Alive && mo.Pos.ChebyshevTo(wallPos) <= Config.BlastKillRadius + blastBonus)
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }

        if (_octopus is { } ocp && ocp.Pos.ChebyshevTo(wallPos) <= Config.BlastKillRadius + blastBonus)
            _octopus = null;

        // Any miner still alive but standing on a crack the blast just dropped falls in.
        foreach (var m in _miners.Values)
            if (m.Alive && collapsedCracks.Contains(m.Pos))
                CollapseKill(m);

        // Chain: charges planted on walls this blast just cleared detonate immediately.
        var chainedFuse = _charges.Where(c => destroyed.Contains(c.WallPos)).ToList();
        foreach (var chained in chainedFuse)
        {
            _charges.Remove(chained);
            DetonateAt(chained.WallPos, chained.BlastBonus, chained.OwnerId);
        }
        var chainedReels = _reelCharges.Where(r => destroyed.Contains(r.WallPos)).ToList();
        foreach (var reel in chainedReels)
        {
            _reelCharges.Remove(reel);
            DetonateAt(reel.WallPos, reel.BlastBonus, reel.OwnerId);
        }

        _noiseSources.Add(new NoiseSource { Pos = wallPos, LifetimeRemaining = 8.0, Kind = NoiseKind.Explosion });
        _events.Add(new Explosion(wallPos, destroyed));
    }

    private void CompleteActivity(Miner m)
    {
        var kind = m.Activity;
        var target = m.ActivityTarget;
        m.Activity = ActivityKind.None;
        m.ActivitySecondsRemaining = 0;

        if (kind == ActivityKind.Mining)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
            var targetTile  = Grid.Get(target);
            bool wasGold    = targetTile == TileType.GoldRock;
            bool wasCrystal = targetTile == TileType.CrystalRock;
            Grid.Set(target, TileType.Floor);
            if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
            UnburyItemsAt(target);
            ActivateVentsAround(target);
            _events.Add(new RockMined(m.Id, target, wasGold));
            if (wasCrystal) _events.Add(new CrystalShardDropped(target));
            if (targetTile.IsScree()) TriggerScreeCollapse(target, targetTile);
            _noiseSources.Add(new NoiseSource { Pos = target, LifetimeRemaining = 2.0, Kind = NoiseKind.Pickaxe });
        }
        else if (kind == ActivityKind.Planting)
        {
            if (!Grid.InBounds(target)) return;
            var tile = Grid.Get(target);
            if (tile.IsBlastable())
            {
                if (LiveChargeCount(m.Id) >= Config.MaxLiveChargesPerMiner) return;
                if (_charges.Any(c => c.WallPos == target)) return;
                _charges.Add(new Charge(m.Id, target, Config.FuseSeconds, EffectiveBlastBonus(m.Id)));
                _events.Add(new ChargePlanted(m.Id, target));
            }
            else if (Config.TripMinesEnabled && tile == TileType.Floor
                     && !_tripCharges.Any(tc => tc.Pos == target))
            {
                _tripCharges.Add(new TripCharge(m.Id, target, EffectiveBlastBonus(m.Id)));
                _events.Add(new TripMinePlanted(m.Id, target));
            }
        }
        else if (kind == ActivityKind.PlantingDetonator)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return;
            if (_reelCharges.Any(r => r.OwnerId == m.Id)) return;
            _reelCharges.Add(new ReelCharge(m.Id, target, EffectiveBlastBonus(m.Id)));
            m.Held = ItemKind.Reel;
            _events.Add(new DetonatorPlanted(m.Id, target));
        }
        else if (kind == ActivityKind.FloorCracking)
        {
            if (Grid.InBounds(target) && Grid.Get(target) == TileType.Floor)
            {
                Grid.Set(target, TileType.Cracked);
                _events.Add(new CrackWeakened(target));
            }
        }
    }

    private bool TryPlantDetonator(Miner m)
    {
        if (m.Activity != ActivityKind.None) return false;
        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return false;
        if (_reelCharges.Any(r => r.OwnerId == m.Id)) return false;
        m.Activity = ActivityKind.PlantingDetonator;
        m.ActivityTarget = target;
        m.ActivitySecondsRemaining = Config.PlantSeconds;
        _events.Add(new ActivityStarted(m.Id, ActivityKind.PlantingDetonator, target));
        return true;
    }

    private bool TryDetonateReel(Miner m)
    {
        var rc = _reelCharges.Find(r => r.OwnerId == m.Id);
        if (rc == null) return false;
        if (m.Pos.ManhattanTo(rc.WallPos) < Config.ReelSafeDistance) return false;
        _reelCharges.Remove(rc);
        m.Held = null;
        _events.Add(new ReelDetonated(m.Id, rc.WallPos));
        DetonateAt(rc.WallPos, rc.BlastBonus, rc.OwnerId);
        return true;
    }

    private static readonly (int dx, int dy)[] CardinalOffsets = { (0,-1),(1,0),(0,1),(-1,0) };

    private void AdvanceFalls(double dt)
    {
        if (!Config.RockFallsEnabled) return;

        // Activate when ≥95% of gold has been collected.
        if (!_rockFallsActive && GoldCollectedFraction >= 0.95)
        {
            _rockFallsActive = true;
            _rockFallTimer = Config.RockFallIntervalSeconds;
        }
        if (!_rockFallsActive) return;

        // Advance existing pending falls; resolve any that expire.
        for (int i = _pendingFalls.Count - 1; i >= 0; i--)
        {
            var pf = _pendingFalls[i];
            pf.SecondsRemaining -= dt;
            if (pf.SecondsRemaining > 0) continue;

            _pendingFalls.RemoveAt(i);
            if (!Grid.InBounds(pf.Pos) || Grid.Get(pf.Pos) != TileType.Floor) continue;

            Grid.Set(pf.Pos, TileType.Rock);
            _events.Add(new RockFell(pf.Pos));
            foreach (var m in _miners.Values)
            {
                if (m.Alive && m.Pos == pf.Pos)
                {
                    m.Alive = false;
                    m.Activity = ActivityKind.None;
                    m.DeathCause = DeathCause.Crushed;
                    _events.Add(new MinerCrushed(m.Id));
                }
            }
        }

        // Spawn new falls on the interval timer (max 3 simultaneous).
        if (_pendingFalls.Count < 3)
        {
            _rockFallTimer -= dt;
            if (_rockFallTimer <= 0)
            {
                _rockFallTimer = Config.RockFallIntervalSeconds;
                var pos = PickRockFallCandidate();
                if (pos.HasValue)
                {
                    double delay = Config.RockFallMinDelay
                                   + _rng.NextDouble() * (Config.RockFallMaxDelay - Config.RockFallMinDelay);
                    _pendingFalls.Add(new PendingFall(pos.Value, delay));
                    _events.Add(new RockFallWarned(pos.Value));
                }
            }
        }
    }

    // Pick an open floor tile (most floor neighbours) as a fall candidate.
    private GridPos? PickRockFallCandidate()
    {
        var best = new List<GridPos>();
        int bestScore = 1; // minimum: at least 2 floor neighbours
        foreach (var p in Grid.Positions())
        {
            if (Grid.Get(p) != TileType.Floor) continue;
            if (_pendingFalls.Exists(pf => pf.Pos == p)) continue;

            int score = 0;
            foreach (var (dx, dy) in CardinalOffsets)
            {
                var nb = new GridPos(p.X + dx, p.Y + dy);
                if (Grid.InBounds(nb) && Grid.Get(nb) == TileType.Floor) score++;
            }
            if (score < bestScore) continue;
            if (score > bestScore) { best.Clear(); bestScore = score; }
            best.Add(p);
        }
        return best.Count > 0 ? best[_rng.Next(best.Count)] : null;
    }

    private void AdvanceReels()
    {
        for (int i = _reelCharges.Count - 1; i >= 0; i--)
        {
            var rc = _reelCharges[i];
            if (!_miners.TryGetValue(rc.OwnerId, out var m) || !m.Alive)
            {
                _reelCharges.RemoveAt(i);
                continue;
            }
            if (m.Pos.ManhattanTo(rc.WallPos) > Config.ReelMaxLength)
            {
                _reelCharges.RemoveAt(i);
                if (m.Held == ItemKind.Reel) m.Held = null;
                _events.Add(new ReelSnapped(rc.OwnerId));
            }
        }
    }
}
