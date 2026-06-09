using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotFactoryTests
{
    [Fact]
    public void Captures_miner_position_facing_and_alive()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East); // now at (3,3) facing East

        var snap = SnapshotFactory.Capture(sim, tick: 11);

        Assert.Equal(11, snap.Tick);
        var m = Assert.Single(snap.Miners);
        Assert.Equal(1, m.Id);
        Assert.Equal(3, m.X);
        Assert.Equal(3, m.Y);
        Assert.Equal((int)Direction.East, m.Facing);
        Assert.True(m.Alive);
    }

    [Fact]
    public void Captures_effective_move_seconds()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        var snap = SnapshotFactory.Capture(sim, tick: 1);
        Assert.Equal(0.12, Assert.Single(snap.Miners).MoveSeconds, 3); // standard floor cadence
    }

    [Fact]
    public void Captures_charges()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East); // blocked by rock at (3,2) but sets facing East
        sim.TryStartPlanting(1);
        sim.Tick(sim.Config.PlantSeconds + 0.01); // finish planting -> charge exists

        var snap = SnapshotFactory.Capture(sim, tick: 0);
        var c = Assert.Single(snap.Charges);
        Assert.Equal(3, c.X);
        Assert.Equal(2, c.Y);
    }

    [Fact]
    public void Capture_includes_seconds_remaining_from_a_timed_sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig(),
            timeLimitSeconds: 30.0);
        sim.Tick(10.0); // 20s left

        var snap = SnapshotFactory.Capture(sim, tick: 1);

        Assert.Equal(20f, snap.SecondsRemaining, 3);
    }

    [Fact]
    public void Capture_reports_minus_one_for_an_untimed_sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        var snap = SnapshotFactory.Capture(sim, tick: 1);
        Assert.Equal(-1f, snap.SecondsRemaining);
    }
}
