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

    public void Tick(double dt)
    {
        AdvanceActivities(dt);
        // Charge fuses are advanced in a later task.
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
        // Planting completion handled in a later task.
    }
}
