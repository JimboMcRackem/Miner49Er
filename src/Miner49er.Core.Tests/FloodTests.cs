using System.Linq;
using Miner49er.Core;
using Xunit;

public class FloodTests
{
    // 11x11 all-floor grid: maxDist = (11-1)/2 = 5; centre (5,5) has edge-distance 5.
    private static Simulation FloodSim(double timeLimit = 10.0, bool flooding = true)
        => new(new TileGrid(11, 11, TileType.Floor), new SimConfig(),
               timeLimitSeconds: timeLimit, flooding: flooding);

    [Fact]
    public void Front_floods_border_first_centre_last()
    {
        var sim = FloodSim();
        sim.Tick(2.0); // progress .2 -> floodedMaxDist = 1
        Assert.Equal(TileType.ShallowWater, sim.Grid.Get(new GridPos(1, 5))); // edge-distance 1
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(5, 5)));        // centre still dry
    }

    [Fact]
    public void Leading_ring_is_shallow_and_everything_behind_is_deep()
    {
        var sim = FloodSim();
        sim.Tick(4.0); // progress .4 -> floodedMaxDist = 2
        Assert.Equal(TileType.DeepWater, sim.Grid.Get(new GridPos(1, 5)));    // d=1 < 2 -> deep
        Assert.Equal(TileType.ShallowWater, sim.Grid.Get(new GridPos(2, 5))); // d=2 == front -> shallow
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(5, 5)));        // d=5 dry
    }

    [Fact]
    public void Rock_does_not_flood()
    {
        var sim = FloodSim();
        sim.Grid.Set(new GridPos(1, 5), TileType.Rock);
        sim.Tick(4.0); // d=1 would be deep, but it's a wall
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(1, 5)));
    }

    [Fact]
    public void Open_space_revealed_inside_the_zone_re_floods()
    {
        var sim = FloodSim();
        sim.Grid.Set(new GridPos(1, 5), TileType.Rock);
        sim.Tick(4.0);
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(1, 5))); // wall holds
        sim.Grid.Set(new GridPos(1, 5), TileType.Floor); // simulate a mined reveal inside the zone
        sim.Tick(0.1);
        Assert.Equal(TileType.DeepWater, sim.Grid.Get(new GridPos(1, 5))); // flood reasserts
    }

    [Fact]
    public void A_standing_miner_drowns_when_the_front_reaches_them()
    {
        var sim = FloodSim();
        var m = sim.AddMiner(1, new GridPos(1, 5)); // edge-distance 1
        sim.Tick(4.0); // floodedMaxDist 2 -> (1,5) becomes deep
        Assert.False(m.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MinerDrowned d && d.MinerId == 1);
    }

    [Fact]
    public void Flooding_is_inert_when_disabled()
    {
        var sim = FloodSim(flooding: false);
        sim.Tick(8.0);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(1, 5)));
    }

    [Fact]
    public void Flooding_is_inert_without_a_time_limit()
    {
        var sim = new Simulation(new TileGrid(11, 11, TileType.Floor), new SimConfig(),
            flooding: true); // no timeLimitSeconds
        sim.Tick(8.0);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(1, 5)));
    }

    [Fact]
    public void Drowning_onto_the_centre_tile_does_not_latch_a_reacher()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(1, 1), TileType.DeepWater); // centre is lethal
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 1));
        sim.AddMiner(1, new GridPos(0, 1));
        sim.TryMove(1, Direction.East); // steps onto centre and drowns on entry
        Assert.False(sim.GetMiner(1).Alive);
        Assert.Equal(-1, sim.FirstToReachCenter); // not latched — drowned on arrival
    }
}
