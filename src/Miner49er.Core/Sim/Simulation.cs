namespace Miner49er.Core;

public sealed class Simulation
{
    public TileGrid Grid { get; }
    public SimConfig Config { get; }

    private readonly Dictionary<int, Miner> _miners = new();
    private readonly List<Charge> _charges = new();
    private readonly List<Item> _items = new();
    private readonly List<MoldPatch> _molds = new();
    private readonly List<LavaVent> _lavaVents = new();
    private readonly List<SimEvent> _events = new();
    private readonly List<Monster> _monsters = new();
    private readonly Random _rng;
    private Octopus? _octopus;
    public  Octopus? Octopus => _octopus;

    public IReadOnlyCollection<Miner> Miners => _miners.Values;
    public IReadOnlyList<Charge> Charges => _charges;
    public IReadOnlyList<Item> Items => _items;
    public IReadOnlyList<MoldPatch> Molds => _molds;
    public IReadOnlyList<Monster> Monsters => _monsters;

    public void AddItem(Item item) => _items.Add(item);   // host seeds these from GeneratedMap.Items

    public GridPos? Center { get; }
    public int FirstToReachCenter { get; private set; } = -1;

    public GridPos? EscapeTile { get; }
    public bool EscapeOpen { get; private set; }
    private int _goldRemaining;
    public bool AllGoldCleared => _goldRemaining == 0;
    public int StartingGoldCount { get; private set; }
    public double GoldCollectedFraction =>
        StartingGoldCount == 0 ? 1.0 : 1.0 - (double)_goldRemaining / StartingGoldCount;

    private readonly double? _timeLimit;
    private readonly bool _flooding;
    public double Elapsed { get; private set; }
    public double SecondsRemaining => _timeLimit is { } lim ? Math.Max(0, lim - Elapsed) : -1;
    public bool TimeExpired => _timeLimit is { } lim && Elapsed >= lim;

    public Simulation(TileGrid grid, SimConfig config,
        GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false,
        GridPos? escapeTile = null)
    {
        Grid = grid;
        Config = config;
        Center = center;
        _timeLimit = timeLimitSeconds;
        _flooding = flooding;
        EscapeTile = escapeTile;

        _rng = new Random(config.Seed);

        foreach (var p in Grid.Positions())
        {
            if (Grid.Get(p) == TileType.LavaVent)
                _lavaVents.Add(new LavaVent { Pos = p, Budget = config.LavaVentBudget });
            if (Grid.Get(p) == TileType.GoldRock) _goldRemaining++;
        }
        StartingGoldCount = _goldRemaining;
        if (EscapeTile is not null && _goldRemaining == 0 && !Config.RequireChestForEscape)
            EscapeOpen = true;   // gold-less map: open at once (unless boss floor)
    }

    public Miner AddMiner(int id, GridPos pos, double invulRemaining = 0.0)
    {
        var m = new Miner(id, pos) { InvulnerableRemaining = invulRemaining };
        _miners[id] = m;
        return m;
    }

    public Monster AddMonster(int id, GridPos pos, MonsterKind kind)
    {
        var mo = new Monster(id, pos, kind) { MoveCooldownRemaining = MonsterCadence(kind) };
        _monsters.Add(mo);
        return mo;
    }

    public void AddOctopus(GridPos pos) => _octopus = new Octopus(pos);

    internal void DropMoldAt(GridPos pos) =>
        _molds.Add(new MoldPatch(pos, Config.MoldSeconds));

    private double MonsterCadence(MonsterKind kind) => kind switch
    {
        MonsterKind.Slime => Config.MonsterSlimeMoveSeconds,
        MonsterKind.Ghost => Config.MonsterGhostMoveSeconds,
        MonsterKind.Goat  => Config.MonsterGoatMoveSeconds,
        _ => Config.MonsterSlimeMoveSeconds,
    };

    public Miner GetMiner(int id) => _miners[id];

    public void KillMiner(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = DeathCause.Left;
        _events.Add(new MinerKilled(id));
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
        return Math.Clamp(Config.BaseMoveSeconds * tile * mult,
                          Config.MinMoveSeconds, Config.MaxMoveSeconds);
    }

    public int EffectiveVisionRadius(int minerId) => EffectiveVisionRadius(_miners[minerId]);

    private int EffectiveVisionRadius(Miner m)
    {
        int bonus = m.PermVisionLevel * Config.PermVisionBonus;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.VisionRadius) bonus += (int)e.Magnitude;
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

    private void AdvanceCooldowns(double dt)
    {
        foreach (var m in _miners.Values)
            if (m.MoveCooldownRemaining > 0)
                m.MoveCooldownRemaining = Math.Max(0, m.MoveCooldownRemaining - dt);
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

    private void AdvanceMonsters(double dt)
    {
        if (_monsters.Count == 0) return;

        // Single-player: the lone living miner is the target. OrderBy(Id) keeps both the
        // target choice and the monster step order deterministic.
        Miner? target = _miners.Values.Where(m => m.Alive).OrderBy(m => m.Id).FirstOrDefault();

        foreach (var mo in _monsters.OrderBy(x => x.Id))
        {
            if (!mo.Alive) continue;

            if (mo.SlowTimer > 0)
            {
                mo.SlowTimer = Math.Max(0, mo.SlowTimer - dt);
                if (mo.SlowTimer <= 0) mo.SlowMultiplier = 1.0;
            }

            mo.MoveCooldownRemaining -= dt;
            if (mo.MoveCooldownRemaining > 0) continue;

            // Step first, THEN check mold so the cadence reset below uses
            // the multiplier current AFTER landing (slow takes effect immediately).
            StepMonster(mo, target);

            if (mo.Alive && mo.Kind != MonsterKind.Ghost && _molds.Any(mp => mp.Pos == mo.Pos))
            {
                mo.SlowTimer = Config.MoldSlowSeconds;
                mo.SlowMultiplier = Config.MoldSlowFactor;
            }

            mo.MoveCooldownRemaining += MonsterCadence(mo.Kind) * mo.SlowMultiplier;
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
            MonsterKind.Slime => SlimeDir(mo, target),
            MonsterKind.Ghost => GhostDir(mo, target),
            MonsterKind.Goat  => GoatDir(mo, target),
            _ => null,
        };
        if (dir is not { } d) return;

        var next = mo.Pos + d.ToOffset();
        if (!CanMonsterEnter(mo, next)) return;

        var from = mo.Pos;
        mo.Pos = next;
        mo.Facing = d;
        _events.Add(new MonsterMoved(mo.Id, from, next));

        if (mo.Kind != MonsterKind.Ghost && Grid.Get(mo.Pos).IsLethal())
        {
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
            return;
        }

        if (target is { Alive: true } && mo.Pos == target.Pos)
            MaulMiner(target, mo.Kind);
    }

    // Rock blocks terrain-bound monsters; ghosts phase rock but are stopped by deep water.
    private bool CanMonsterEnter(Monster mo, GridPos p)
    {
        if (!Grid.InBounds(p)) return false;
        if (mo.Kind == MonsterKind.Ghost) return Grid.Get(p) != TileType.DeepWater;
        return Grid.Get(p).IsEnterable();
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
        if (target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius)
            return TowardDir(mo.Pos, target.Pos);
        return Card[_rng.Next(Card.Length)];
    }

    private Direction? GhostDir(Monster mo, Miner? target)
    {
        if (target is not { Alive: true }) return null;
        var d = TowardDir(mo.Pos, target.Pos);
        var next = mo.Pos + d.ToOffset();
        if (InLanternLight(next)) return null;   // halt at AOE boundary rather than entering
        return d;
    }

    private Direction? GoatDir(Monster mo, Miner? target)
    {
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

        m.Facing = dir;
        CancelActivity(m);

        var target = m.Pos + dir.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsEnterable()) return false;

        var from = m.Pos;
        m.Pos = target;
        _events.Add(new MinerMoved(id, from, target));

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

        if (Center is { } c && target == c && FirstToReachCenter < 0 && m.Alive)
        {
            FirstToReachCenter = id;
            _events.Add(new MinerReachedCenter(id));
        }

        if (m.Alive && _molds.Any(mo => mo.Pos == target))
            ApplyEffect(id, EffectKind.SlowMold, EffectChannel.MoveSpeed,
                        Config.MoldSlowFactor, Config.MoldSlowSeconds);

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

        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return false;

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

        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return false;
        if (LiveChargeCount(id) >= Config.MaxLiveChargesPerMiner) return false;
        if (_charges.Any(c => c.WallPos == target)) return false;

        m.Activity = ActivityKind.Planting;
        m.ActivityTarget = target;
        m.ActivitySecondsRemaining = Config.PlantSeconds;
        _events.Add(new ActivityStarted(id, ActivityKind.Planting, target));
        return true;
    }

    private int LiveChargeCount(int ownerId) => _charges.Count(c => c.OwnerId == ownerId);

    public void Tick(double dt)
    {
        Elapsed += dt;
        AdvanceEffects(dt);
        AdvanceInvulnerability(dt);
        AdvanceMolds(dt);
        AdvanceCooldowns(dt);
        AdvanceCracks(dt);
        AdvanceLava(dt);
        AdvanceMonsters(dt);
        AdvanceOctopus(dt);
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        PickUpItems();
        AdvanceCharges(chargesThisTick, dt);
        AdvanceFlood();
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
            var taken = it.Kind;
            if (m.Held is { } heldKind) _items[i] = new Item(m.Pos, heldKind, ItemPlacement.Loose);
            else                        _items.RemoveAt(i);
            m.Held = taken;
            _events.Add(new ItemPickedUp(m.Id, m.Pos, taken));
            return true;
        }

        // 2. use what is held
        if (m.Held is not { } held) return false;
        return held switch
        {
            ItemKind.WaterPlank => TryPlacePlank(m),
            ItemKind.SlowMold   => DropMold(m),
            ItemKind.Lantern    => DropLantern(m),
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
                m.PermSpeedLevel = Math.Min(m.PermSpeedLevel + 1, Config.MaxPermSpeedLevel);
                break;
            case ItemKind.LongerVision:
                m.PermVisionLevel = Math.Min(m.PermVisionLevel + 1, Config.MaxPermVisionLevel);
                break;
            case ItemKind.BiggerBlast:
                m.PermBlastLevel = Math.Min(m.PermBlastLevel + 1, Config.MaxPermBlastLevel);
                break;
            case ItemKind.Chest:
                ApplyBuff(minerId, ChestLootTable.Roll(_rng));
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

    private void MaulMiner(Miner m, MonsterKind kind)
    {
        if (!m.Alive || m.InvulnerableRemaining > 0) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = kind switch
        {
            MonsterKind.Slime => DeathCause.Slimed,
            MonsterKind.Ghost => DeathCause.Terrified,
            MonsterKind.Goat  => DeathCause.Headbutted,
            _ => DeathCause.Slimed,
        };
        _events.Add(new MinerMauled(m.Id));
    }

    // Called wherever a GoldRock tile becomes Floor. When the last vein falls and an
    // escape tile is set, the exit opens (once).
    private void OnGoldCleared()
    {
        if (_goldRemaining > 0) _goldRemaining--;
        if (!EscapeOpen && EscapeTile is not null
            && StartingGoldCount > 0 && GoldCollectedFraction >= 0.5)
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

    private void AdvanceOctopus(double dt)
    {
        if (_octopus is null) return;
        _octopus.Advance(dt);
        var danger = new HashSet<GridPos>(_octopus.DangerTiles(Grid));
        foreach (var m in _miners.Values)
            if (m.Alive && danger.Contains(m.Pos))
                CollapseKill(m);
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
        // Ghosts float and stay immune.
        foreach (var mo in _monsters)
        {
            if (mo.Alive && mo.Kind != MonsterKind.Ghost && Grid.Get(mo.Pos).IsLethal())
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }
    }

    private void Detonate(Charge charge)
    {
        _charges.Remove(charge);

        var destroyed = new List<GridPos>();
        var collapsedCracks = new List<GridPos>();
        int r = Config.BlastRockRadius + charge.BlastBonus;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new GridPos(charge.WallPos.X + dx, charge.WallPos.Y + dy);
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;        // Manhattan disc
                if (!Grid.InBounds(p)) continue;
                if (Grid.Get(p) is TileType.Cracked or TileType.Crumbling)
                {
                    Grid.Set(p, TileType.Pit);                        // the blast shakes the weak floor down
                    _events.Add(new CrackCollapsed(p));
                    collapsedCracks.Add(p);
                    continue;
                }
                if (!Grid.Get(p).IsBlastable()) continue;
                bool wasGold = Grid.Get(p) == TileType.GoldRock;
                Grid.Set(p, TileType.Floor);
                if (wasGold)
                {
                    var owner = _miners[charge.OwnerId];
                    if (owner.Alive) owner.GoldCollected++;
                    OnGoldCleared();
                }
                UnburyItemsAt(p);
                ActivateVentsAround(p);
                destroyed.Add(p);
            }

        foreach (var m in _miners.Values)
        {
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                m.DeathCause = DeathCause.Exploded;
                _events.Add(new MinerKilled(m.Id));
            }
        }

        foreach (var mo in _monsters)
        {
            if (mo.Alive && mo.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }

        // Any miner still alive but standing on a crack the blast just dropped falls in.
        foreach (var m in _miners.Values)
            if (m.Alive && collapsedCracks.Contains(m.Pos))
                CollapseKill(m);

        _events.Add(new Explosion(charge.WallPos, destroyed));
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
            bool wasGold = Grid.Get(target) == TileType.GoldRock;
            Grid.Set(target, TileType.Floor);
            if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
            UnburyItemsAt(target);
            ActivateVentsAround(target);
            _events.Add(new RockMined(m.Id, target, wasGold));
        }
        else if (kind == ActivityKind.Planting)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return;
            if (LiveChargeCount(m.Id) >= Config.MaxLiveChargesPerMiner) return;
            if (_charges.Any(c => c.WallPos == target)) return;
            _charges.Add(new Charge(m.Id, target, Config.FuseSeconds, EffectiveBlastBonus(m.Id)));
            _events.Add(new ChargePlanted(m.Id, target));
        }
    }
}
