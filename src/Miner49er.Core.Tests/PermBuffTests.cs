using Miner49er.Core;
using Xunit;

public class PermBuffTests
{
    private static Simulation Sim()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    [Fact]
    public void Perm_levels_start_at_zero()
    {
        var sim = Sim();
        var m = sim.GetMiner(1);
        Assert.Equal(0, m.PermSpeedLevel);
        Assert.Equal(0, m.PermVisionLevel);
        Assert.Equal(0, m.PermBlastLevel);
    }

    [Fact]
    public void Picking_up_SpeedPotion_increments_PermSpeedLevel()
    {
        var sim = Sim();
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(1, sim.GetMiner(1).PermSpeedLevel);
    }

    [Fact]
    public void EffectiveMoveSeconds_decreases_each_perm_speed_level()
    {
        var sim = Sim();
        double baseline = sim.EffectiveMoveSeconds(1);
        sim.SetPermLevels(1, 1, 0, 0);
        double one = sim.EffectiveMoveSeconds(1);
        sim.SetPermLevels(1, 2, 0, 0);
        double two = sim.EffectiveMoveSeconds(1);
        Assert.True(one < baseline);
        Assert.True(two < one);
    }

    [Fact]
    public void SetPermLevels_clamps_to_config_max()
    {
        var sim = Sim();
        sim.SetPermLevels(1, 99, 99, 99);
        var m = sim.GetMiner(1);
        Assert.Equal(sim.Config.MaxPermSpeedLevel,  m.PermSpeedLevel);
        Assert.Equal(sim.Config.MaxPermVisionLevel, m.PermVisionLevel);
        Assert.Equal(sim.Config.MaxPermBlastLevel,  m.PermBlastLevel);
    }

    [Fact]
    public void PermSpeed_and_mold_slow_stack_multiplicatively()
    {
        var sim = Sim();
        sim.SetPermLevels(1, 1, 0, 0);
        double perm = sim.EffectiveMoveSeconds(1);
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 2.0, 5.0);
        double both = sim.EffectiveMoveSeconds(1);
        Assert.True(both > perm);
    }

    [Fact]
    public void LongerVision_increments_PermVisionLevel()
    {
        var sim = Sim();
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(1, sim.GetMiner(1).PermVisionLevel);
        Assert.Equal(6, sim.EffectiveVisionRadius(1)); // 5 base + 1*1 bonus
    }

    [Fact]
    public void BiggerBlast_increments_PermBlastLevel()
    {
        var sim = Sim();
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BiggerBlast));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(1, sim.GetMiner(1).PermBlastLevel);
        Assert.Equal(1, sim.EffectiveBlastBonus(1));
    }

    [Fact]
    public void SpeedPotion_blocked_when_at_max_speed()
    {
        var cfg = new SimConfig { MaxPermSpeedLevel = 2 };
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 2));
        sim.SetPermLevels(1, 2, 0, 0); // already maxed

        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        Assert.Single(sim.Items); // item still on floor
        Assert.Equal(2, sim.GetMiner(1).PermSpeedLevel); // unchanged
        Assert.Contains(sim.DrainEvents(), e => e is PickupBlocked pb && pb.Kind == ItemKind.SpeedPotion);
    }

    [Fact]
    public void BiggerBlast_blocked_when_at_max_blast()
    {
        var cfg = new SimConfig { MaxPermBlastLevel = 1 };
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 2));
        sim.SetPermLevels(1, 0, 0, 1); // already maxed

        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BiggerBlast));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        Assert.Single(sim.Items);
        Assert.Equal(1, sim.GetMiner(1).PermBlastLevel);
        Assert.Contains(sim.DrainEvents(), e => e is PickupBlocked pb && pb.Kind == ItemKind.BiggerBlast);
    }

    [Fact]
    public void LongerVision_blocked_when_at_max_vision()
    {
        var cfg = new SimConfig { MaxPermVisionLevel = 3 };
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 2));
        sim.SetPermLevels(1, 0, 3, 0);

        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        Assert.Single(sim.Items);
        Assert.Equal(3, sim.GetMiner(1).PermVisionLevel);
        Assert.Contains(sim.DrainEvents(), e => e is PickupBlocked);
    }

    [Fact]
    public void SpeedPotion_collected_normally_when_not_maxed()
    {
        var cfg = new SimConfig { MaxPermSpeedLevel = 2 };
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 2));
        sim.SetPermLevels(1, 1, 0, 0); // one below max

        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        Assert.Empty(sim.Items);
        Assert.Equal(2, sim.GetMiner(1).PermSpeedLevel);
    }
}
