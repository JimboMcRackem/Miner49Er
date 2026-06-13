using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationPitTests
{
    [Fact]
    public void Moving_onto_a_pit_kills_with_Fell_and_emits_MinerFell()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Pit);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                       // the move resolves (then kills)
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Fell, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerFell f && f.MinerId == 1);
    }

    [Fact]
    public void Moving_onto_deep_water_still_kills_with_Drowned()   // regression
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);

        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Drowned, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerDrowned d && d.MinerId == 1);
    }

    [Fact]
    public void Plank_can_bridge_a_faced_pit_and_then_it_is_safe_to_enter()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Pit);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));
        m.Held = ItemKind.WaterPlank;
        m.Facing = Direction.East;                 // facing the pit

        Assert.True(sim.TryUseItem(1));            // lay the plank over the pit
        Assert.Equal(TileType.Plank, grid.Get(new GridPos(2, 1)));
        Assert.Null(m.Held);                       // plank consumed
        Assert.Contains(sim.DrainEvents(), e => e is PlankPlaced p && p.Pos == new GridPos(2, 1));

        sim.TryMove(1, Direction.East);            // walk onto the bridged tile
        Assert.True(m.Alive);                      // no longer lethal
        Assert.Equal(new GridPos(2, 1), m.Pos);
    }
}
