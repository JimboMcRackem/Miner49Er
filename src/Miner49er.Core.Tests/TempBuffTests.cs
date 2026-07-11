using Miner49er.Core;
using Xunit;

public class TempBuffTests
{
    // Miner at (1,2); a buff item at (2,2). Moving east onto it collects it on the next tick.
    private static Simulation Sim(SimConfig? cfg = null)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg ?? new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    private static void CollectAt(Simulation sim, ItemKind kind)
    {
        sim.AddItem(new Item(new GridPos(2, 2), kind));
        sim.TryMove(1, Direction.East); // step onto the item
        sim.Tick(0.0);                  // PickUpItems runs during Tick
        sim.TryMove(1, Direction.West); // step back so a second pickup can re-enter
        sim.Tick(0.0);
    }

    [Fact]
    public void LongerVision_pickup_is_temporary()
    {
        var sim = Sim();
        int baseR = sim.EffectiveVisionRadius(1);          // 5
        CollectAt(sim, ItemKind.LongerVision);
        Assert.Equal(baseR + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1)); // 8
        sim.Tick(BuffTuning.VisionSeconds + 1.0);          // effect expires
        Assert.Equal(baseR, sim.EffectiveVisionRadius(1)); // back to 5
    }

    [Fact]
    public void Second_vision_pickup_refreshes_not_compounds()
    {
        var sim = Sim();
        int baseR = sim.EffectiveVisionRadius(1);
        CollectAt(sim, ItemKind.LongerVision);
        CollectAt(sim, ItemKind.LongerVision);
        // Still a single magnitude, not doubled.
        Assert.Equal(baseR + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1));
    }

    [Fact]
    public void BiggerBlast_pickup_is_temporary()
    {
        var sim = Sim();
        CollectAt(sim, ItemKind.BiggerBlast);
        Assert.Equal(BuffTuning.BlastMagnitude, sim.EffectiveBlastBonus(1)); // 1
        sim.Tick(BuffTuning.BlastSeconds + 1.0);
        Assert.Equal(0, sim.EffectiveBlastBonus(1));
    }

    [Fact]
    public void SpeedPotion_pickup_is_temporary()
    {
        var sim = Sim();
        double baseS = sim.EffectiveMoveSeconds(1);
        CollectAt(sim, ItemKind.SpeedPotion);
        Assert.True(sim.EffectiveMoveSeconds(1) < baseS); // faster while active
        sim.Tick(BuffTuning.SpeedSeconds + 1.0);
        Assert.True(System.Math.Abs(sim.EffectiveMoveSeconds(1) - baseS) < 1e-9);
    }

    [Fact]
    public void Temp_vision_stacks_on_permanent_shop_level()
    {
        var sim = Sim();
        sim.SetPermLevels(1, 0, 1, 0);                     // shop vision +1 -> radius 6
        CollectAt(sim, ItemKind.LongerVision);             // temp +3
        Assert.Equal(5 + 1 + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1)); // 9
    }

    [Fact]
    public void Buff_is_collected_even_when_permanent_level_maxed()
    {
        var cfg = new SimConfig { MaxPermVisionLevel = 3 };
        var sim = Sim(cfg);
        sim.SetPermLevels(1, 0, 3, 0);                     // perm vision maxed -> radius 8
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Empty(sim.Items);                           // collected, not left on the floor
        Assert.Equal(5 + 3 + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1)); // 11
    }
}
