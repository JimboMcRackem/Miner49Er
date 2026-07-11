using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationChestTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    [Fact]
    public void Chest_pickup_fires_ItemPickedUp_for_the_chest()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor));
        var pos = new GridPos(2, 2);
        sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.Chest, ItemPlacement.Toolbox));

        sim.Tick(0.01);

        Assert.Contains(sim.DrainEvents(), e => e is ItemPickedUp ip && ip.Kind == ItemKind.Chest);
    }
}
