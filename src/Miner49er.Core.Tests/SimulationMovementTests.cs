using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMovementTests
{
    private static TileGrid OpenGrid() => new(3, 3, TileType.Floor);

    [Fact]
    public void Move_into_floor_updates_position_and_facing()
    {
        var sim = new Simulation(OpenGrid(), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);
        Assert.Equal(new GridPos(2, 1), m.Pos);
        Assert.Equal(Direction.East, m.Facing);
    }

    [Fact]
    public void Move_into_rock_is_blocked_but_still_sets_facing()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.False(moved);
        Assert.Equal(new GridPos(1, 1), m.Pos);
        Assert.Equal(Direction.East, m.Facing);
    }

    [Fact]
    public void Move_emits_MinerMoved_event()
    {
        var sim = new Simulation(OpenGrid(), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.North);
        var events = sim.DrainEvents();

        var moved = Assert.IsType<MinerMoved>(Assert.Single(events));
        Assert.Equal(new GridPos(1, 0), moved.To);
    }

    [Fact]
    public void DrainEvents_clears_the_buffer()
    {
        var sim = new Simulation(OpenGrid(), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.North);
        sim.DrainEvents();
        Assert.Empty(sim.DrainEvents());
    }
}
