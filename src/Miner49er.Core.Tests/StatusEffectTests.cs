using Miner49er.Core;
using Xunit;

public class StatusEffectTests
{
    private static Simulation Sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        return sim;
    }

    [Fact]
    public void ApplyEffect_adds_an_active_effect()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 5.0);
        var e = Assert.Single(sim.GetMiner(1).Effects);
        Assert.Equal(EffectKind.SpeedPotion, e.Kind);
        Assert.Equal(EffectChannel.MoveSpeed, e.Channel);
        Assert.Equal(0.6, e.Magnitude, 3);
        Assert.Equal(5.0, e.RemainingSeconds, 3);
    }

    [Fact]
    public void Tick_expires_an_effect_after_its_duration()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 1.0);
        sim.Tick(1.1);
        Assert.Empty(sim.GetMiner(1).Effects);
    }

    [Fact]
    public void Tick_keeps_an_unexpired_effect_and_decrements_it()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 2.0);
        sim.Tick(0.5);
        var e = Assert.Single(sim.GetMiner(1).Effects);
        Assert.Equal(1.5, e.RemainingSeconds, 3);
    }

    [Fact]
    public void Reapplying_same_kind_refreshes_without_compounding()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 2.0);
        sim.Tick(1.5); // 0.5 left
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 2.0); // refresh
        var e = Assert.Single(sim.GetMiner(1).Effects); // still a single instance
        Assert.Equal(2.0, e.RemainingSeconds, 3);       // refreshed to full
    }

    [Fact]
    public void Different_kinds_coexist_as_separate_instances()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 5.0);
        sim.ApplyEffect(1, EffectKind.BiggerBlast,  EffectChannel.MoveSpeed, 1.8, 5.0);
        Assert.Equal(2, sim.GetMiner(1).Effects.Count);
    }

    [Fact]
    public void ApplyEffect_on_a_dead_miner_is_a_noop()
    {
        var sim = Sim();
        sim.KillMiner(1);
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 0.6, 5.0);
        Assert.Empty(sim.GetMiner(1).Effects);
    }

    [Fact]
    public void EffectiveVisionRadius_adds_bonus_while_active_then_reverts()
    {
        var sim = Sim();
        Assert.Equal(5, sim.EffectiveVisionRadius(1)); // base from SimConfig
        sim.ApplyEffect(1, EffectKind.LongerVision, EffectChannel.VisionRadius, 3, 2.0);
        Assert.Equal(8, sim.EffectiveVisionRadius(1)); // 5 + 3
        sim.Tick(2.1);                                 // expire
        Assert.Equal(5, sim.EffectiveVisionRadius(1));
    }

    [Fact]
    public void EffectiveBlastBonus_sums_active_blast_effects()
    {
        var sim = Sim();
        Assert.Equal(0, sim.EffectiveBlastBonus(1));
        sim.ApplyEffect(1, EffectKind.BiggerBlast, EffectChannel.BlastRadius, 1, 5.0);
        Assert.Equal(1, sim.EffectiveBlastBonus(1));
    }
}
