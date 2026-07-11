using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationChestTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    // Feeds `count` copies of `kind` onto the miner's tile and ticks once so they
    // are all collected, driving the matching perm buff toward its cap.
    private static void MaxOut(Simulation sim, int minerId, GridPos pos, ItemKind kind, int count)
    {
        for (int i = 0; i < count; i++)
            sim.AddItem(new Item(pos, kind, ItemPlacement.Toolbox));
        sim.Tick(0.01);
    }

    [Fact]
    public void Chest_pickup_when_all_perm_buffs_maxed_restores_life_instead_of_wasting()
    {
        var cfg = new SimConfig();
        var sim = Sim(new TileGrid(5, 5, TileType.Floor), cfg);
        var pos = new GridPos(2, 2);
        sim.AddMiner(1, pos);

        // Drive every perm buff to its cap.
        MaxOut(sim, 1, pos, ItemKind.SpeedPotion,  cfg.MaxPermSpeedLevel);
        MaxOut(sim, 1, pos, ItemKind.LongerVision, cfg.MaxPermVisionLevel);
        MaxOut(sim, 1, pos, ItemKind.BiggerBlast,  cfg.MaxPermBlastLevel);
        sim.DrainEvents(); // discard the buff-pickup events

        // Now open a chest while fully maxed: it must not silently waste its roll.
        sim.AddItem(new Item(pos, ItemKind.Chest, ItemPlacement.Toolbox));
        sim.Tick(0.01);

        var events = sim.DrainEvents().ToList();
        Assert.Contains(events, e => e is LifeRestored);
        Assert.Contains(events, e => e is ItemPickedUp ip && ip.Kind == ItemKind.Chest);
    }

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
