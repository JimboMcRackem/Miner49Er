namespace Miner49er.Core;

public sealed class Simulation
{
    public TileGrid Grid { get; }
    public SimConfig Config { get; }

    private readonly Dictionary<int, Miner> _miners = new();
    private readonly List<Charge> _charges = new();
    private readonly List<Item> _items = new();
    private readonly List<SimEvent> _events = new();

    public IReadOnlyCollection<Miner> Miners => _miners.Values;
    public IReadOnlyList<Charge> Charges => _charges;
    public IReadOnlyList<Item> Items => _items;

    public void AddItem(Item item) => _items.Add(item);   // host seeds these from GeneratedMap.Items

    public GridPos? Center { get; }
    public int FirstToReachCenter { get; private set; } = -1;

    private readonly double? _timeLimit;
    private readonly bool _flooding;
    public double Elapsed { get; private set; }
    public double SecondsRemaining => _timeLimit is { } lim ? Math.Max(0, lim - Elapsed) : -1;
    public bool TimeExpired => _timeLimit is { } lim && Elapsed >= lim;

    public Simulation(TileGrid grid, SimConfig config,
        GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false)
    {
        Grid = grid;
        Config = config;
        Center = center;
        _timeLimit = timeLimitSeconds;
        _flooding = flooding;
    }

    public Miner AddMiner(int id, GridPos pos)
    {
        var m = new Miner(id, pos);
        _miners[id] = m;
        return m;
    }

    public Miner GetMiner(int id) => _miners[id];

    public void KillMiner(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
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

    public double EffectiveMoveSeconds(int minerId) => EffectiveMoveSeconds(_miners[minerId]);

    private double EffectiveMoveSeconds(Miner m)
    {
        double mult = 1.0;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.MoveSpeed) mult *= e.Magnitude;
        double tile = Grid.Get(m.Pos).MoveCostMultiplier();   // shallow water = ×2
        return Math.Clamp(Config.BaseMoveSeconds * tile * mult,
                          Config.MinMoveSeconds, Config.MaxMoveSeconds);
    }

    public int EffectiveVisionRadius(int minerId) => EffectiveVisionRadius(_miners[minerId]);

    private int EffectiveVisionRadius(Miner m)
    {
        int bonus = 0;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.VisionRadius) bonus += (int)e.Magnitude;
        return Config.VisionRadius + bonus;
    }

    public int EffectiveBlastBonus(int minerId) => EffectiveBlastBonus(_miners[minerId]);

    private int EffectiveBlastBonus(Miner m)
    {
        int bonus = 0;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.BlastRadius) bonus += (int)e.Magnitude;
        return bonus;
    }

    private void AdvanceCooldowns(double dt)
    {
        foreach (var m in _miners.Values)
            if (m.MoveCooldownRemaining > 0)
                m.MoveCooldownRemaining = Math.Max(0, m.MoveCooldownRemaining - dt);
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
        {
            m.Alive = false;
            m.Activity = ActivityKind.None;
            _events.Add(new MinerDrowned(id));
        }

        if (Center is { } c && target == c && FirstToReachCenter < 0 && m.Alive)
        {
            FirstToReachCenter = id;
            _events.Add(new MinerReachedCenter(id));
        }

        m.MoveCooldownRemaining = EffectiveMoveSeconds(m);   // set from destination tile
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
        AdvanceCooldowns(dt);
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
        switch (kind)
        {
            case ItemKind.SpeedPotion:
                ApplyEffect(minerId, EffectKind.SpeedPotion, EffectChannel.MoveSpeed,
                            Config.SpeedPotionFactor, Config.SpeedPotionSeconds);
                break;
            case ItemKind.LongerVision:
                ApplyEffect(minerId, EffectKind.LongerVision, EffectChannel.VisionRadius,
                            Config.VisionBonus, Config.VisionSeconds);
                break;
            case ItemKind.BiggerBlast:
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                            Config.BlastBonus, Config.BlastSeconds);
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
        DrownOccupants();
    }

    private int EdgeDistance(GridPos p) =>
        Math.Min(Math.Min(p.X, p.Y), Math.Min(Grid.Width - 1 - p.X, Grid.Height - 1 - p.Y));

    // Kills any living miner standing on a now-lethal (deep) tile. Covers water
    // rising *under* a stationary miner; move-time drowning stays in TryMove.
    private void DrownOccupants()
    {
        foreach (var m in _miners.Values)
        {
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                _events.Add(new MinerDrowned(m.Id));
            }
        }
    }

    private void Detonate(Charge charge)
    {
        _charges.Remove(charge);

        var destroyed = new List<GridPos>();
        int r = Config.BlastRockRadius + charge.BlastBonus;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new GridPos(charge.WallPos.X + dx, charge.WallPos.Y + dy);
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;        // Manhattan disc
                if (!Grid.InBounds(p) || !Grid.Get(p).IsBlastable()) continue;
                bool wasGold = Grid.Get(p) == TileType.GoldRock;
                Grid.Set(p, TileType.Floor);
                if (wasGold)
                {
                    var owner = _miners[charge.OwnerId];
                    if (owner.Alive) owner.GoldCollected++;
                }
                UnburyItemsAt(p);
                destroyed.Add(p);
            }

        foreach (var m in _miners.Values)
        {
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                _events.Add(new MinerKilled(m.Id));
            }
        }

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
            if (wasGold) m.GoldCollected++;
            UnburyItemsAt(target);
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
