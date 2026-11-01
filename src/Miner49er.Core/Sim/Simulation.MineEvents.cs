namespace Miner49er.Core;

// Rare mine events. A single scheduler runs them one at a time, so they never pile up.
public sealed partial class Simulation
{
    private double? _eventDelay;
    private double _eventElapsed;
    private MineEventKind _lastEvent = MineEventKind.None;
    private Random? _eventRngLazy;
    private Random EventRng => _eventRngLazy ??= new Random(Config.Seed ^ 0x7E30B);   // apart from _rng, so events never reshuffle other rolls

    private readonly List<(GridPos Pos, double FallsAt)> _tremorTargets = new();
    private readonly List<GridPos> _eventTiles = new();    // gas cloud, flood area or gold seam
    private int _eventTilesShown;                          // how much of the gas or water has spread so far
    private readonly List<Phantom> _phantoms = new();

    private sealed class Phantom
    {
        public GridPos Pos;
        public double StepCooldown;
    }

    public MineEventKind ActiveEvent { get; private set; } = MineEventKind.None;

    public double EventIntensity => ActiveEvent == MineEventKind.None ? 0
        : TremorCurve.Intensity(_eventElapsed, EventSeconds(ActiveEvent), EventRamp(ActiveEvent));

    public double TremorIntensity => ActiveEvent == MineEventKind.Tremor ? EventIntensity : 0;
    public double TremorSlow => 1 + (Config.TremorMaxSlow - 1) * TremorIntensity;
    public IReadOnlyList<GridPos> TremorTargets => _tremorTargets.Select(t => t.Pos).ToList();

    /// <summary>Tiles the event is marking: gas, water on its way or gold.</summary>
    public IReadOnlyList<GridPos> EventTiles => ActiveEvent switch
    {
        MineEventKind.Firedamp or MineEventKind.FlashFlood => _eventTiles.Take(_eventTilesShown).ToList(),
        MineEventKind.GoldSeam => _eventTiles,
        _ => Array.Empty<GridPos>(),
    };

    public IReadOnlyList<GridPos> Phantoms => _phantoms.Select(p => p.Pos).ToList();

    private double EventSeconds(MineEventKind kind) => kind switch
    {
        MineEventKind.Tremor     => Config.TremorSeconds,
        MineEventKind.Blackout   => Config.BlackoutSeconds,
        MineEventKind.Firedamp   => Config.FiredampSeconds,
        MineEventKind.FlashFlood => Config.FlashFloodWarningSeconds + Config.FlashFloodSeconds,
        MineEventKind.Awakening  => Config.AwakeningSeconds,
        MineEventKind.GoldSeam   => Config.GoldSeamSeconds,
        MineEventKind.GhostShift => Config.GhostShiftSeconds,
        _ => 0,
    };

    private double EventRamp(MineEventKind kind) => kind == MineEventKind.Tremor ? Config.TremorRampSeconds : 3.0;

    private void AdvanceMineEvents(double dt)
    {
        if (Config.MineEventPool.Count == 0) return;
        if (ActiveEvent == MineEventKind.None)
        {
            _eventDelay ??= Config.MineEventFirstDelaySeconds ?? Config.MineEventGraceSeconds
                + EventRng.NextDouble() * (Config.MineEventMaxIntervalSeconds - Config.MineEventGraceSeconds);
            _eventDelay -= dt;
            if (_eventDelay <= 0) StartEvent(PickEvent());
            return;
        }

        _eventElapsed += dt;
        switch (ActiveEvent)
        {
            case MineEventKind.Tremor:     AdvanceTremor(); break;
            case MineEventKind.Firedamp:   AdvanceFiredamp(); break;
            case MineEventKind.FlashFlood: AdvanceFlashFlood(); break;
            case MineEventKind.GhostShift: AdvancePhantoms(dt); break;
        }
        if (ActiveEvent != MineEventKind.None && _eventElapsed >= EventSeconds(ActiveEvent)) EndEvent();
    }

    private MineEventKind PickEvent()
    {
        var pool = Config.MineEventPool.Where(k => k != _lastEvent || Config.MineEventPool.Count == 1).ToList();
        return pool[EventRng.Next(pool.Count)];
    }

    private void StartEvent(MineEventKind kind)
    {
        ActiveEvent = _lastEvent = kind;
        _eventElapsed = 0;
        _eventTilesShown = 0;
        _eventTiles.Clear();
        switch (kind)
        {
            case MineEventKind.Tremor:     MarkTremorRock(); break;
            case MineEventKind.Firedamp:   _eventTiles.AddRange(OpenFloorNearMiner(Config.FiredampTiles)); break;
            case MineEventKind.FlashFlood: _eventTiles.AddRange(OpenFloorNearMiner(Config.FlashFloodTiles)); break;
            case MineEventKind.Awakening:  WakeAllDormant(); break;
            case MineEventKind.GoldSeam:   OpenGoldSeam(); break;
            case MineEventKind.GhostShift: SpawnPhantoms(); break;
        }
    }

    private void EndEvent()
    {
        if (ActiveEvent == MineEventKind.GoldSeam) CloseGoldSeam();
        if (ActiveEvent == MineEventKind.FlashFlood) DrainTo(0);
        ActiveEvent = MineEventKind.None;
        _tremorTargets.Clear();
        _eventTiles.Clear();
        _phantoms.Clear();
        _eventDelay = Config.MineEventMinIntervalSeconds
            + EventRng.NextDouble() * (Config.MineEventMaxIntervalSeconds - Config.MineEventMinIntervalSeconds);
    }

    // A place to start from: open floor a short walk from a random living miner, measured along
    // the tunnels so it can't land in a sealed cave they'd never see. Close is the point of these
    // events, but never on top of them, so there's always a moment to react.
    private GridPos? EventCentre(int minSteps, int maxSteps)
    {
        var alive = _miners.Values.Where(m => m.Alive).ToList();
        if (alive.Count == 0) return null;
        var start = alive[EventRng.Next(alive.Count)].Pos;

        var steps = new Dictionary<GridPos, int> { [start] = 0 };
        var queue = new Queue<GridPos>();
        queue.Enqueue(start);
        var spots = new List<GridPos>();
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            int walked = steps[p];
            if (walked >= minSteps && Grid.Get(p) == TileType.Floor) spots.Add(p);
            if (walked == maxSteps) continue;
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (Grid.InBounds(n) && Grid.Get(n).IsWalkable() && steps.TryAdd(n, walked + 1)) queue.Enqueue(n);
            }
        }
        return spots.Count == 0 ? null : spots[EventRng.Next(spots.Count)];
    }

    // Connected plain floor spreading out from the centre, nearest first. Leaves out anything a
    // flood or gas pocket shouldn't sit on, so the exit, shop and loose items stay reachable.
    private List<GridPos> OpenFloorNearMiner(int count)
    {
        var result = new List<GridPos>();
        if (EventCentre(3, 9) is not { } centre) return result;
        var seen = new HashSet<GridPos> { centre };
        var queue = new Queue<GridPos>();
        queue.Enqueue(centre);
        while (queue.Count > 0 && result.Count < count)
        {
            var p = queue.Dequeue();
            if (!IsProtectedFloor(p)) result.Add(p);
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (Grid.InBounds(n) && Grid.Get(n) == TileType.Floor && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return result;
    }

    private bool IsProtectedFloor(GridPos p) =>
        WouldBuryLandmark(p, 0) || _items.Any(it => it.Pos == p) || _miners.Values.Any(m => m.SpawnPos == p);

    // --- Tremor -----------------------------------------------------------
    // The rock that will fall is chosen as the tremor starts and sent to clients, so the
    // build-up shows exactly where to get away from. It then falls during the peak.
    private void AdvanceTremor()
    {
        for (int i = _tremorTargets.Count - 1; i >= 0; i--)
        {
            var (pos, fallsAt) = _tremorTargets[i];
            if (Grid.Get(pos).IsWalkable()) { _tremorTargets.RemoveAt(i); continue; }   // mined out
            if (_eventElapsed < fallsAt) continue;
            _tremorTargets.RemoveAt(i);
            CollapseRockAround(pos, Grid.Get(pos).TremorFallRadius());
        }
    }

    private void MarkTremorRock()
    {
        var alive = _miners.Values.Where(m => m.Alive).ToList();
        var candidates = new List<GridPos>();
        for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
            {
                var p = new GridPos(x, y);
                var tile = Grid.Get(p);
                if (tile != TileType.Rock && !tile.IsScree()) continue;
                if (!Card.Any(d => Grid.InBounds(p + d.ToOffset()) && Grid.Get(p + d.ToOffset()) == TileType.Floor)) continue;
                if (!alive.Any(m => m.Pos.ChebyshevTo(p) <= Config.TremorRockfallRange)) continue;
                if (WouldBuryLandmark(p, tile.TremorFallRadius())) continue;
                candidates.Add(p);
            }

        double peak = Config.TremorRampSeconds;
        double peakLength = Config.TremorSeconds - 2 * peak;
        foreach (var p in candidates.OrderBy(_ => EventRng.Next())
                     .OrderByDescending(c => Grid.Get(c).IsScree())
                     .Take(Config.TremorRockfallCount))
            _tremorTargets.Add((p, peak + EventRng.NextDouble() * peakLength));
    }

    private bool WouldBuryLandmark(GridPos p, int radius)
    {
        if (new[] { EscapeTile, ShopPos, Center }.Any(l => l is { } tile && tile.ChebyshevTo(p) <= radius))
            return true;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (IsTrack(new GridPos(p.X + dx, p.Y + dy))) return true;
        return false;
    }

    // --- Blackout ---------------------------------------------------------
    // Any lantern or crystal keeps its bearer seeing normally, and a lit lantern nearby does too.
    private int BlackoutVision(Miner m, int vision)
    {
        if (ActiveEvent != MineEventKind.Blackout || vision <= Config.BlackoutVisionRadius) return vision;
        if (m.Held is ItemKind.Lantern or ItemKind.CrystalShard || InLanternLight(m.Pos)) return vision;
        return (int)Math.Round(vision + (Config.BlackoutVisionRadius - vision) * EventIntensity);
    }

    // --- Firedamp ---------------------------------------------------------
    private void AdvanceFiredamp()
    {
        double spread = Math.Min(1.0, _eventElapsed / Config.FiredampSpreadSeconds);
        _eventTilesShown = (int)(_eventTiles.Count * spread);
        foreach (var p in EventTiles)
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (Grid.InBounds(n) && Grid.Get(n) is TileType.Lava or TileType.LavaVent)
                {
                    IgniteGas(-1);
                    return;
                }
            }
    }

    // Called for every explosion: fire reaching the gas sets the whole cloud off.
    private void IgniteGasNear(GridPos blast, int reach, int ownerId)
    {
        if (ActiveEvent != MineEventKind.Firedamp) return;
        if (EventTiles.Any(p => p.ChebyshevTo(blast) <= reach)) IgniteGas(ownerId);
    }

    private void IgniteGas(int ownerId)
    {
        var cloud = EventTiles.ToHashSet();
        EndEvent();                    // first, so an explosion further down can't set it off twice
        if (cloud.Count == 0) return;

        foreach (var m in _miners.Values)
        {
            if (!m.Alive || m.InvulnerableRemaining > 0 || !cloud.Contains(m.Pos)) continue;
            m.Alive = false;
            m.Activity = ActivityKind.None;
            m.DeathCause = DeathCause.Exploded;
            if (m.Id != ownerId && _miners.TryGetValue(ownerId, out var killer)) killer.Kills++;
            _events.Add(new MinerKilled(m.Id));
        }
        foreach (var mo in _monsters)
        {
            if (!mo.Alive || !cloud.Contains(mo.Pos)) continue;
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
        }
        var first = cloud.First();
        _noiseSources.Add(new NoiseSource { Pos = first, LifetimeRemaining = 8.0, Kind = NoiseKind.Explosion });
        _events.Add(new Explosion(first, cloud.ToList()));
    }

    // --- Flash flood ------------------------------------------------------
    // The marked tiles fill up in order from the first, and drain back the same way.
    private int _floodedTiles;

    private void AdvanceFlashFlood()
    {
        _eventTilesShown = _eventTiles.Count;
        double warned = _eventElapsed - Config.FlashFloodWarningSeconds;
        if (warned < 0) return;

        double remaining = Config.FlashFloodSeconds - warned;
        int wanted = (int)(Math.Min(warned, remaining) * Config.FlashFloodTilesPerSecond);
        wanted = Math.Clamp(wanted, 0, _eventTiles.Count);
        if (wanted < _floodedTiles) { DrainTo(wanted); return; }
        for (; _floodedTiles < wanted; _floodedTiles++)
        {
            var p = _eventTiles[_floodedTiles];
            if (Grid.Get(p) != TileType.Floor) continue;
            Grid.Set(p, TileType.DeepWater);
            _events.Add(new TileFlooded(p, TileType.DeepWater));
        }
        KillOccupantsOnLethalTiles();
    }

    private void DrainTo(int count)
    {
        for (; _floodedTiles > count; _floodedTiles--)
        {
            var p = _eventTiles[_floodedTiles - 1];
            if (Grid.Get(p) != TileType.DeepWater) continue;
            Grid.Set(p, TileType.Floor);
            _events.Add(new TileFlooded(p, TileType.Floor));
        }
    }

    // --- The Awakening ----------------------------------------------------
    private void WakeAllDormant()
    {
        foreach (var mo in _monsters)
        {
            if (!mo.Alive || !mo.Dormant) continue;
            mo.Dormant = false;
            _events.Add(new SkeletonAroused(mo.Id));
        }
    }

    /// <summary>How far monsters notice things; the Awakening has them hearing right across the level.</summary>
    private int MonsterSenseRadius => ActiveEvent == MineEventKind.Awakening
        ? Config.MonsterSenseRadius * Config.AwakeningSenseMultiplier
        : Config.MonsterSenseRadius;

    // --- Gold seam --------------------------------------------------------
    private void OpenGoldSeam()
    {
        var miners = _miners.Values.Where(m => m.Alive).ToList();
        if (miners.Count == 0) return;
        var rock = new List<GridPos>();
        foreach (var p in Grid.Positions())
        {
            if (Grid.Get(p) != TileType.Rock || !Card.Any(d => Grid.InBounds(p + d.ToOffset()) && Grid.Get(p + d.ToOffset()).IsWalkable())) continue;
            int nearest = miners.Min(m => m.Pos.ChebyshevTo(p));
            if (nearest >= Config.GoldSeamMinDistance && nearest <= Config.GoldSeamMaxDistance) rock.Add(p);
        }
        if (rock.Count == 0) return;

        var start = rock[EventRng.Next(rock.Count)];
        var seen = new HashSet<GridPos> { start };
        var queue = new Queue<GridPos>();
        queue.Enqueue(start);
        while (queue.Count > 0 && _eventTiles.Count < Config.GoldSeamTiles)
        {
            var p = queue.Dequeue();
            Grid.Set(p, TileType.GoldRock);
            _goldRemaining++;
            _eventTiles.Add(p);
            _events.Add(new TileFlooded(p, TileType.GoldRock));
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (Grid.InBounds(n) && Grid.Get(n) == TileType.Rock && seen.Add(n)) queue.Enqueue(n);
            }
        }
    }

    // Unmined gold seals back up. It was never part of the floor's total, so it isn't "cleared".
    private void CloseGoldSeam()
    {
        foreach (var p in _eventTiles)
        {
            if (Grid.Get(p) != TileType.GoldRock) continue;
            Grid.Set(p, TileType.Rock);
            _goldRemaining--;
            _events.Add(new TileFlooded(p, TileType.Rock));
        }
    }

    // --- Ghost shift ------------------------------------------------------
    // Harmless apparitions drifting through the rock toward whatever is buried nearest them.
    private void SpawnPhantoms()
    {
        for (int i = 0; i < Config.GhostShiftPhantoms; i++)
            if (EventCentre(3, 8) is { } p)
                _phantoms.Add(new Phantom { Pos = p, StepCooldown = Config.GhostShiftStepSeconds });
    }

    private void AdvancePhantoms(double dt)
    {
        var buried = _items.Where(it => it.Placement == ItemPlacement.Buried).Select(it => it.Pos).ToList();
        foreach (var ph in _phantoms)
        {
            ph.StepCooldown -= dt;
            if (ph.StepCooldown > 0) continue;
            ph.StepCooldown += Config.GhostShiftStepSeconds;

            var pos = ph.Pos;
            var dest = buried.Count > 0 ? buried.MinBy(b => b.ManhattanTo(pos)) : pos;
            var step = dest != pos ? pos + TowardDir(pos, dest).ToOffset()
                                   : pos + Card[EventRng.Next(Card.Length)].ToOffset();
            if (buried.Count > 0 && dest == pos) continue;   // arrived: it stands over the spot
            if (Grid.InBounds(step)) ph.Pos = step;
        }
    }
}
