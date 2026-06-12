using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationUseVerbTests
{
    [Fact]
    public void Using_while_standing_on_a_carried_item_picks_it_up()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));

        Assert.True(sim.TryUseItem(1));
        Assert.Equal(ItemKind.WaterPlank, m.Held);
        Assert.Empty(sim.Items);
    }

    [Fact]
    public void Using_with_a_full_hand_on_a_ground_item_swaps_them()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 2));
        sim.AddItem(new Item(new GridPos(1, 2), ItemKind.SlowMold));   // under the miner
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank)); // one tile east

        Assert.True(sim.TryUseItem(1));              // pick up the mold
        Assert.Equal(ItemKind.SlowMold, m.Held);
        sim.TryMove(1, Direction.East);              // move onto the plank tile
        Assert.True(sim.TryUseItem(1));              // swap

        Assert.Equal(ItemKind.WaterPlank, m.Held);
        var onGround = Assert.Single(sim.Items.Where(i => i.Pos == new GridPos(2, 2)));
        Assert.Equal(ItemKind.SlowMold, onGround.Kind);          // dropped held item
        Assert.Equal(ItemPlacement.Loose, onGround.Placement);
    }

    [Fact]
    public void Using_an_empty_hand_on_an_empty_tile_is_a_noop()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        Assert.False(sim.TryUseItem(1));
    }

    [Fact]
    public void Using_a_water_plank_facing_water_lays_a_plank_tile()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.ShallowWater); // north of the miner
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));

        sim.TryUseItem(1);                          // pick up the plank (hand = WaterPlank)
        sim.GetMiner(1).Facing = Direction.North;   // face the water without moving onto it
        Assert.True(sim.TryUseItem(1));             // place the plank northward

        Assert.Equal(TileType.Plank, sim.Grid.Get(new GridPos(2, 1)));
        Assert.Null(m.Held);                        // hand emptied
        Assert.Single(sim.DrainEvents().OfType<PlankPlaced>());
    }

    [Fact]
    public void Using_a_water_plank_facing_non_water_does_nothing()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));

        sim.TryUseItem(1);
        sim.GetMiner(1).Facing = Direction.North;
        Assert.False(sim.TryUseItem(1));            // rock is not water
        Assert.Equal(ItemKind.WaterPlank, m.Held);  // still held
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void A_plank_tile_survives_a_flood_tick()
    {
        // 5x5: edges flood inward. Place a plank on a tile inside the flood zone and tick the clock.
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), timeLimitSeconds: 10.0, flooding: true);
        sim.Grid.Set(new GridPos(1, 1), TileType.Plank); // a tile the flood would otherwise convert
        sim.Tick(10.0);                                   // full progress -> flood reaches inner ring
        Assert.Equal(TileType.Plank, sim.Grid.Get(new GridPos(1, 1)));
    }
}
