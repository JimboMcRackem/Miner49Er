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

    [Fact]
    public void Crossing_a_fresh_crack_weakens_it_to_crumbling_but_you_survive()
    {
        var grid = new TileGrid(4, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        Assert.True(sim.TryMove(1, Direction.East));        // step ONTO the crack
        Assert.True(m.Alive);
        Assert.Equal(TileType.Cracked, grid.Get(new GridPos(2, 1))); // still fresh while you're on it
        sim.DrainEvents();

        m.MoveCooldownRemaining = 0;                          // clear cadence gate for the test
        Assert.True(sim.TryMove(1, Direction.East));        // step OFF it
        Assert.True(m.Alive);
        Assert.Equal(TileType.Crumbling, grid.Get(new GridPos(2, 1))); // worn down behind you
        Assert.Contains(sim.DrainEvents(), e => e is CrackWeakened cw && cw.Pos == new GridPos(2, 1));
    }

    [Fact]
    public void Re_crossing_a_crack_collapses_it()
    {
        var grid = new TileGrid(4, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);                      // onto crack (now standing on it)
        m.MoveCooldownRemaining = 0;
        sim.TryMove(1, Direction.East);                      // off it -> Crumbling
        m.MoveCooldownRemaining = 0;
        sim.DrainEvents();

        sim.TryMove(1, Direction.West);                      // back onto the Crumbling tile
        Assert.False(m.Alive);                               // "going over again" collapses it
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(2, 1)));
    }
}
