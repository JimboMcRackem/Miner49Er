using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationCaveInTests
{
    [Fact]
    public void Entering_a_crumbling_tile_collapses_it_and_crushes_you()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Crumbling);   // already weakened
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                                // the move resolves, then collapses
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(2, 1)));   // floor gave way to a hole
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is CrackCollapsed cc && cc.Pos == new GridPos(2, 1));
        Assert.Contains(events, e => e is MinerCrushed mc && mc.MinerId == 1);
    }
}
