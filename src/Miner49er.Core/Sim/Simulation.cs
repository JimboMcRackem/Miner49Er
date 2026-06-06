namespace Miner49er.Core;

public sealed class Simulation
{
    public TileGrid Grid { get; }
    public SimConfig Config { get; }

    private readonly Dictionary<int, Miner> _miners = new();
    private readonly List<Charge> _charges = new();
    private readonly List<SimEvent> _events = new();

    public IReadOnlyCollection<Miner> Miners => _miners.Values;
    public IReadOnlyList<Charge> Charges => _charges;

    public Simulation(TileGrid grid, SimConfig config)
    {
        Grid = grid;
        Config = config;
    }

    public Miner AddMiner(int id, GridPos pos)
    {
        var m = new Miner(id, pos);
        _miners[id] = m;
        return m;
    }

    public Miner GetMiner(int id) => _miners[id];

    public IReadOnlyList<SimEvent> DrainEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public bool TryMove(int id, Direction dir)
    {
        var m = _miners[id];
        if (!m.Alive) return false;

        m.Facing = dir;
        CancelActivity(m);

        var target = m.Pos + dir.ToOffset();
        if (!Grid.IsWalkable(target)) return false;

        var from = m.Pos;
        m.Pos = target;
        _events.Add(new MinerMoved(id, from, target));
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
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        AdvanceCharges(chargesThisTick, dt);
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

    private void Detonate(Charge charge)
    {
        _charges.Remove(charge);

        var destroyed = new List<GridPos>();
        int r = Config.BlastRockRadius;
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
                destroyed.Add(p);
            }

        foreach (var m in _miners.Values)
        {
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius)
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
            _events.Add(new RockMined(m.Id, target, wasGold));
        }
        else if (kind == ActivityKind.Planting)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return;
            if (LiveChargeCount(m.Id) >= Config.MaxLiveChargesPerMiner) return;
            if (_charges.Any(c => c.WallPos == target)) return;
            _charges.Add(new Charge(m.Id, target, Config.FuseSeconds));
            _events.Add(new ChargePlanted(m.Id, target));
        }
    }
}
