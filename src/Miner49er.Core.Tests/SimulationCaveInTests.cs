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

    [Fact]
    public void Lingering_on_a_crack_collapses_it_under_you()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var cfg = new SimConfig { CrackDwellSeconds = 0.5 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);        // onto the crack, then stand still
        sim.DrainEvents();

        sim.Tick(0.3);
        Assert.True(m.Alive);                  // under the dwell threshold so far
        sim.Tick(0.3);                         // total 0.6 >= 0.5 -> gives way
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Walking_straight_across_a_crack_does_not_collapse_under_you()
    {
        var grid = new TileGrid(4, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var cfg = new SimConfig { CrackDwellSeconds = 0.5 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);        // onto the crack
        sim.Tick(0.1);                         // brief dwell, well under threshold
        m.MoveCooldownRemaining = 0;
        sim.TryMove(1, Direction.East);        // keep moving off it
        sim.Tick(0.1);

        Assert.True(m.Alive);                  // you kept moving, so you live
        Assert.Equal(new GridPos(3, 1), m.Pos);
    }

    [Fact]
    public void Blast_collapses_cracks_in_its_disc_and_crushes_those_outside_the_kill_radius()
    {
        // Wide rock radius, tight kill radius: a crack at Manhattan distance 2 is inside
        // the destruction disc but a miner on it is outside the Chebyshev-1 kill radius.
        var grid = new TileGrid(7, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);          // wall to plant the charge on
        grid.Set(new GridPos(5, 2), TileType.Cracked);       // distance 2 east of the wall
        var cfg = new SimConfig { BlastRockRadius = 2, BlastKillRadius = 1, FuseSeconds = 0.1, PlantSeconds = 0.1 };
        var sim = new Simulation(grid, cfg);

        var planter = sim.AddMiner(1, new GridPos(3, 3));     // adjacent to the wall, faces it
        planter.Facing = Direction.North;
        var victim = sim.AddMiner(2, new GridPos(5, 2));      // standing on the crack, far from the wall

        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.1);   // planting completes -> charge armed
        sim.Tick(0.1);   // fuse expires -> detonation

        Assert.Equal(TileType.Pit, grid.Get(new GridPos(5, 2)));   // crack shaken into a hole
        Assert.False(victim.Alive);
        Assert.Equal(DeathCause.Crushed, victim.DeathCause);       // outside kill radius -> crushed, not exploded
        Assert.Contains(sim.DrainEvents(), e => e is CrackCollapsed cc && cc.Pos == new GridPos(5, 2));
    }
}
