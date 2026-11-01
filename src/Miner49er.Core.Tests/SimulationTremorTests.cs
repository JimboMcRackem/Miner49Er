using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SimulationTremorTests
{
    private static SimConfig Cfg() => new()
    {
        TremorsEnabled = true, TremorFirstDelaySeconds = 1, TremorSeconds = 15, TremorRampSeconds = 5,
        TremorMinIntervalSeconds = 300, TremorMaxIntervalSeconds = 600, TremorMaxSlow = 2.0,
        TremorRockfallCount = 3, TremorRockfallRange = 12, BaseMoveSeconds = 0.2,
    };

    private static void Run(Simulation sim, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds / 0.1); i++) sim.Tick(0.1);
    }

    // A floor with a rock wall along the top edge.
    private static TileGrid Walled(int w = 30, int h = 12)
    {
        var grid = new TileGrid(w, h, TileType.Floor);
        for (int x = 0; x < w; x++) grid.Set(new GridPos(x, 0), TileType.Rock);
        return grid;
    }

    [Fact]
    public void Tremors_are_off_unless_enabled()
    {
        var cfg = Cfg();
        cfg.TremorsEnabled = false;
        var sim = new Simulation(Walled(), cfg);
        Run(sim, 5);
        Assert.Equal(0.0, sim.TremorIntensity);
    }

    [Fact]
    public void Tremor_builds_peaks_and_drops_off_over_its_length()
    {
        var sim = new Simulation(Walled(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        Run(sim, 1.0);                // first delay runs out
        Assert.Equal(0.0, sim.TremorIntensity, 3);

        Run(sim, 2.5);
        Assert.InRange(sim.TremorIntensity, 0.3, 0.7);   // building
        Run(sim, 5.0);
        Assert.Equal(1.0, sim.TremorIntensity);          // peak
        Run(sim, 5.0);
        Assert.InRange(sim.TremorIntensity, 0.3, 0.7);   // dropping
        Run(sim, 3.0);
        Assert.Equal(0.0, sim.TremorIntensity);
    }

    [Fact]
    public void Tremors_stay_rare()
    {
        var sim = new Simulation(Walled(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        Run(sim, 20);                 // the first has been and gone

        Run(sim, 280);
        Assert.Equal(0.0, sim.TremorIntensity);
    }

    [Fact]
    public void Miners_move_at_half_speed_at_the_peak()
    {
        var sim = new Simulation(Walled(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        double normal = sim.EffectiveMoveSeconds(1);

        Run(sim, 8);

        Assert.Equal(normal * 2, sim.EffectiveMoveSeconds(1), 6);
    }

    [Fact]
    public void The_slowdown_applies_past_the_normal_speed_limit()
    {
        var cfg = Cfg();
        cfg.BaseMoveSeconds = cfg.MaxMoveSeconds;
        var sim = new Simulation(Walled(), cfg);
        sim.AddMiner(1, new GridPos(5, 5));

        Run(sim, 8);

        Assert.Equal(cfg.MaxMoveSeconds * 2, sim.EffectiveMoveSeconds(1), 6);
    }

    [Fact]
    public void Monsters_move_at_half_speed_at_the_peak()
    {
        int StepsDuringPeak(bool tremor)
        {
            var cfg = Cfg();
            cfg.TremorsEnabled = tremor;
            cfg.TremorRockfallCount = 0;
            cfg.MonsterZombieMoveSeconds = 0.5;
            var sim = new Simulation(new TileGrid(40, 3, TileType.Floor), cfg);
            sim.AddMiner(1, new GridPos(39, 1));
            var zombie = sim.AddMonster(1, new GridPos(0, 1), MonsterKind.ZombieMiner);
            Run(sim, 6.5);
            int from = zombie.Pos.X;
            Run(sim, 4);
            return zombie.Pos.X - from;
        }

        Assert.Equal(StepsDuringPeak(false), StepsDuringPeak(true) * 2);
    }

    [Fact]
    public void Rock_marked_at_the_start_only_falls_at_the_peak()
    {
        var sim = new Simulation(Walled(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));

        Run(sim, 1.2);
        Assert.Equal(3, sim.TremorTargets.Count);
        sim.DrainEvents();

        Run(sim, 4.7);                                  // to just before the peak
        Assert.DoesNotContain(sim.DrainEvents(), e => e is ScreeCollapsed);
        Assert.Equal(3, sim.TremorTargets.Count);

        Run(sim, 5.2);                                  // through the peak
        Assert.Equal(3, sim.DrainEvents().OfType<ScreeCollapsed>().Count());
        Assert.Empty(sim.TremorTargets);
    }

    [Fact]
    public void Scree_near_the_miner_is_marked_ahead_of_plain_rock()
    {
        var grid = Walled();
        grid.Set(new GridPos(6, 0), TileType.ScreeRock);
        grid.Set(new GridPos(8, 0), TileType.VolatileRock);
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(5, 5));

        Run(sim, 1.2);

        Assert.Contains(new GridPos(6, 0), sim.TremorTargets);
        Assert.Contains(new GridPos(8, 0), sim.TremorTargets);
    }

    [Fact]
    public void Only_rock_near_a_miner_and_facing_open_floor_is_marked()
    {
        var grid = new TileGrid(40, 5, TileType.Rock);
        for (int x = 0; x < 40; x++) grid.Set(new GridPos(x, 2), TileType.Floor);
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(2, 2));

        Run(sim, 1.2);

        Assert.All(sim.TremorTargets, t =>
        {
            Assert.InRange(t.X, 0, 14);
            Assert.True(t.Y is 1 or 3);
        });
    }

    [Fact]
    public void A_rockfall_crushes_a_miner_who_stayed_but_not_one_who_left()
    {
        var grid = new TileGrid(30, 8, TileType.Floor);
        grid.Set(new GridPos(5, 0), TileType.Rock);
        var cfg = Cfg();
        cfg.TremorRockfallCount = 1;
        var sim = new Simulation(grid, cfg);
        var stayed = sim.AddMiner(1, new GridPos(5, 1));
        var left = sim.AddMiner(2, new GridPos(9, 1));

        Run(sim, 16);

        Assert.False(stayed.Alive);
        Assert.Equal(DeathCause.Crushed, stayed.DeathCause);
        Assert.True(left.Alive);
    }

    [Fact]
    public void Mining_out_a_marked_rock_cancels_its_fall()
    {
        var grid = new TileGrid(12, 5, TileType.Floor);
        grid.Set(new GridPos(5, 0), TileType.Rock);
        var cfg = Cfg();
        cfg.TremorRockfallCount = 1;
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(10, 4));

        Run(sim, 1.2);
        Assert.Single(sim.TremorTargets);
        grid.Set(new GridPos(5, 0), TileType.Floor);
        sim.DrainEvents();
        Run(sim, 10);

        Assert.DoesNotContain(sim.DrainEvents(), e => e is ScreeCollapsed);
    }

    [Fact]
    public void Rock_that_could_bury_the_exit_is_never_marked()
    {
        var grid = new TileGrid(12, 5, TileType.Floor);
        grid.Set(new GridPos(5, 0), TileType.Rock);
        var sim = new Simulation(grid, Cfg(), escapeTile: new GridPos(5, 1));
        sim.AddMiner(1, new GridPos(8, 3));

        Run(sim, 1.2);

        Assert.Empty(sim.TremorTargets);
    }

    [Fact]
    public void Rock_that_could_bury_cart_rails_is_never_marked()
    {
        var grid = new TileGrid(12, 5, TileType.Floor);
        grid.Set(new GridPos(5, 0), TileType.Rock);
        var sim = new Simulation(grid, Cfg());
        sim.AddTrack(new[] { new GridPos(5, 1) });
        sim.AddMiner(1, new GridPos(8, 3));

        Run(sim, 1.2);

        Assert.Empty(sim.TremorTargets);
    }

    [Fact]
    public void Tremor_state_round_trips_through_the_snapshot_codec()
    {
        var sim = new Simulation(Walled(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        Run(sim, 4);

        var bytes = SnapshotCodec.Write(new TickUpdate(SnapshotFactory.Capture(sim, tick: 0),
            System.Array.Empty<TileChange>()));
        var read = SnapshotCodec.Read(bytes).Snapshot;

        Assert.Equal((float)sim.TremorIntensity, read.TremorIntensity);
        Assert.Equal(sim.TremorTargets.Select(t => new TremorTargetSnapshot(t.X, t.Y)), read.TremorTargets);
    }
}
