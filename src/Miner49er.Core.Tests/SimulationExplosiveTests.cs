using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationExplosiveTests
{
    private static Simulation FacingRockEast(out Miner m, SimConfig? cfg = null)
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(2, y), TileType.Rock);
        var sim = new Simulation(grid, cfg ?? new SimConfig());
        m = sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // blocked by rock, faces east
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Plant_starts_planting_then_creates_charge_with_fuse()
    {
        var sim = FacingRockEast(out var m, new SimConfig { PlantSeconds = 1.0, FuseSeconds = 3.0 });

        Assert.True(sim.TryStartPlanting(1));
        Assert.Equal(ActivityKind.Planting, m.Activity);

        sim.Tick(1.0); // finish planting
        Assert.Equal(ActivityKind.None, m.Activity);
        var charge = Assert.Single(sim.Charges);
        Assert.Equal(new GridPos(2, 2), charge.WallPos);
        Assert.Equal(3.0, charge.FuseRemaining);
    }

    [Fact]
    public void Cannot_plant_on_impermeable_rock()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.ImpermeableRock);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // blocked by impermeable; now facing east toward (2,2)
        Assert.False(sim.TryStartPlanting(1));
        Assert.Empty(sim.Charges);
    }

    [Fact]
    public void Cannot_plant_on_floor()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // moves onto floor (2,2); now facing east toward floor (3,2)
        Assert.False(sim.TryStartPlanting(1));
        Assert.Empty(sim.Charges);
    }

    [Fact]
    public void Charge_cap_blocks_extra_plants()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        for (int x = 0; x < 7; x++) grid.Set(new GridPos(x, 0), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { MaxLiveChargesPerMiner = 2, PlantSeconds = 0.0 });
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.North); sim.TryStartPlanting(1); sim.Tick(0.0);
        sim.TryMove(1, Direction.East); sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.North); sim.TryStartPlanting(1); sim.Tick(0.0);
        sim.TryMove(1, Direction.East); sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.North);
        Assert.False(sim.TryStartPlanting(1));
        Assert.Equal(2, sim.Charges.Count);
    }

    [Fact]
    public void Detonation_destroys_adjacent_rock_and_emits_explosion()
    {
        var sim = FacingRockEast(out var m, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 3.0, BlastRockRadius = 1 });
        sim.TryStartPlanting(1);
        sim.Tick(0.0);        // create charge at (2,2)
        sim.DrainEvents();

        sim.Tick(3.0);        // fuse expires

        Assert.Empty(sim.Charges);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2))); // charged wall gone
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1))); // manhattan-1 rock gone
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 3)));
        var boom = sim.DrainEvents().OfType<Explosion>().Single();
        Assert.Equal(new GridPos(2, 2), boom.WallPos);
    }

    [Fact]
    public void Impermeable_rock_survives_blast()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Rock);
        grid.Set(new GridPos(2, 1), TileType.ImpermeableRock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 1.0, BlastRockRadius = 1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.TryStartPlanting(1); sim.Tick(0.0);

        sim.Tick(1.0);

        Assert.Equal(TileType.ImpermeableRock, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Miner_in_kill_radius_dies_but_bystander_survives()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 3.0, BlastKillRadius = 1 });
        var planter = sim.AddMiner(1, new GridPos(2, 1)); // adjacent to (3,1) -> dies
        var bystander = sim.AddMiner(2, new GridPos(5, 1)); // far -> survives
        sim.TryMove(1, Direction.East);
        sim.TryStartPlanting(1); sim.Tick(0.0);

        sim.Tick(3.0);

        Assert.False(planter.Alive);
        Assert.True(bystander.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MinerKilled k && k.MinerId == 1);
    }
}
