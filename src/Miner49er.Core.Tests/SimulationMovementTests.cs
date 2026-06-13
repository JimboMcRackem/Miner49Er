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

    [Fact]
    public void Move_into_shallow_water_succeeds_and_miner_lives()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.ShallowWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);
        Assert.Equal(new GridPos(2, 1), m.Pos);
        Assert.True(m.Alive);
    }

    [Fact]
    public void Move_into_deep_water_drowns_the_miner()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                       // the step happens
        Assert.Equal(new GridPos(2, 1), m.Pos);   // onto the deep tile
        Assert.False(m.Alive);                    // then drowns
        Assert.Equal(ActivityKind.None, m.Activity);
    }

    [Fact]
    public void Drowning_emits_MinerMoved_then_MinerDrowned()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(1, 0), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.North);
        var events = sim.DrainEvents();

        Assert.Equal(2, events.Count);
        Assert.IsType<MinerMoved>(events[0]);
        var drowned = Assert.IsType<MinerDrowned>(events[1]);
        Assert.Equal(1, drowned.MinerId);
        Assert.Equal(DeathCause.Drowned, sim.GetMiner(1).DeathCause);
    }

    [Fact]
    public void Dead_miner_cannot_move()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // drowns
        sim.DrainEvents();

        bool moved = sim.TryMove(1, Direction.West);

        Assert.False(moved);
    }
}
