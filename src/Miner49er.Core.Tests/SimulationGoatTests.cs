using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SimulationGoatTests
{
    private static SimConfig Cfg() => new()
    {
        MonsterGoatWanderMoveSeconds = 0.5, GoatWindUpSeconds = 0.7, MonsterGoatMoveSeconds = 0.2,
        MonsterSenseRadius = 6,
    };

    private static void Run(Simulation sim, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds / 0.1); i++) sim.Tick(0.1);
    }

    [Fact]
    public void Goat_wanders_slowly_when_nothing_is_in_sight()
    {
        var sim = new Simulation(new TileGrid(12, 3, TileType.Floor), Cfg());
        var goat = sim.AddMonster(1, new GridPos(1, 1), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;
        goat.ChargeDir = Direction.East;

        Run(sim, 1.2);

        Assert.Equal(GoatPhase.Wander, goat.Phase);
        Assert.Equal(new GridPos(4, 1), goat.Pos);   // steps at 0, 0.5 and 1.0s
    }

    [Fact]
    public void Spotting_a_miner_stops_the_goat_and_faces_it_at_the_miner()
    {
        var sim = new Simulation(new TileGrid(12, 3, TileType.Floor), Cfg());
        sim.AddMiner(1, new GridPos(1, 1));
        var goat = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;
        goat.ChargeDir = Direction.East;

        sim.Tick(0.1);

        Assert.Equal(GoatPhase.WindUp, goat.Phase);
        Assert.Equal(new GridPos(5, 1), goat.Pos);
        Assert.Equal(Direction.West, goat.ChargeDir);
        Assert.Equal(Direction.West, goat.Facing);
    }

    [Fact]
    public void Goat_takes_the_other_axis_when_rock_blocks_its_way_to_a_diagonal_miner()
    {
        var grid = new TileGrid(8, 6, TileType.Floor);
        grid.Set(new GridPos(3, 4), TileType.Rock);   // directly east of the goat
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(4, 2));           // two east, two north
        var goat = sim.AddMonster(1, new GridPos(2, 4), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;

        sim.Tick(0.1);

        Assert.Equal(GoatPhase.WindUp, goat.Phase);
        Assert.Equal(Direction.North, goat.ChargeDir);
    }

    [Fact]
    public void Goat_does_not_spot_a_miner_through_rock()
    {
        var grid = new TileGrid(12, 3, TileType.Floor);
        for (int y = 0; y < 3; y++) grid.Set(new GridPos(3, y), TileType.Rock);
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(1, 1));
        var goat = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;

        sim.Tick(0.1);

        Assert.Equal(GoatPhase.Wander, goat.Phase);
    }

    [Fact]
    public void Goat_holds_still_for_the_whole_wind_up_then_charges()
    {
        var sim = new Simulation(new TileGrid(12, 3, TileType.Floor), Cfg());
        sim.AddMiner(1, new GridPos(1, 1));
        var goat = sim.AddMonster(1, new GridPos(6, 1), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;

        sim.Tick(0.1);
        Run(sim, 0.5);
        Assert.Equal(GoatPhase.WindUp, goat.Phase);
        Assert.Equal(new GridPos(6, 1), goat.Pos);

        Run(sim, 0.2);
        Assert.Equal(GoatPhase.Charge, goat.Phase);
        Assert.Equal(new GridPos(5, 1), goat.Pos);
    }

    [Fact]
    public void A_charging_goat_does_not_turn_so_it_can_be_sidestepped()
    {
        var sim = new Simulation(new TileGrid(12, 5, TileType.Floor), Cfg());
        var miner = sim.AddMiner(1, new GridPos(2, 2));
        var goat = sim.AddMonster(1, new GridPos(8, 2), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;

        Run(sim, 0.9);                     // wind-up done, charge under way
        Assert.Equal(GoatPhase.Charge, goat.Phase);
        sim.TryMove(1, Direction.North);   // sidestep off the charge line
        Run(sim, 1.0);

        Assert.True(miner.Alive);
        Assert.Equal(2, goat.Pos.Y);
    }

    [Fact]
    public void Hitting_a_wall_ends_the_charge()
    {
        var sim = new Simulation(new TileGrid(6, 3, TileType.Floor), new SimConfig
        {
            MonsterGoatMoveSeconds = 0.1, MonsterGoatWanderMoveSeconds = 0.5, MonsterSenseRadius = 0,
        });
        var goat = sim.AddMonster(1, new GridPos(4, 1), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;
        goat.ChargeDir = Direction.East;
        goat.Phase = GoatPhase.Charge;

        Run(sim, 0.2);   // reaches the east edge, then blocked

        Assert.Equal(new GridPos(5, 1), goat.Pos);
        Assert.Equal(GoatPhase.Wander, goat.Phase);
    }

    [Fact]
    public void A_pickaxe_stun_cancels_the_wind_up()
    {
        var sim = new Simulation(new TileGrid(6, 3, TileType.Floor), Cfg());
        sim.AddMiner(1, new GridPos(1, 1));
        var goat = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Goat);
        goat.MoveCooldownRemaining = 0;

        sim.Tick(0.1);
        Assert.Equal(GoatPhase.WindUp, goat.Phase);

        sim.GetMiner(1).Facing = Direction.East;
        sim.TryStartMining(1);

        Assert.True(goat.StunRemaining > 0);
        Assert.Equal(GoatPhase.Wander, goat.Phase);
    }

    [Fact]
    public void Goat_phase_round_trips_through_the_snapshot_codec()
    {
        var sim = new Simulation(new TileGrid(12, 3, TileType.Floor), Cfg());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Goat).MoveCooldownRemaining = 0;
        sim.Tick(0.1);

        var snap = SnapshotFactory.Capture(sim, tick: 0);
        Assert.Equal(GoatPhase.WindUp, snap.Monsters[0].Phase);

        var bytes = SnapshotCodec.Write(new TickUpdate(snap, System.Array.Empty<TileChange>()));
        Assert.Equal(GoatPhase.WindUp, SnapshotCodec.Read(bytes).Snapshot.Monsters[0].Phase);
    }
}
