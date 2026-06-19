using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class FloorModifierTests
{
    [Theory]
    [InlineData(4)] [InlineData(8)] [InlineData(12)] [InlineData(16)] [InlineData(20)]
    public void Pick_returns_None_for_clean_floors(int floor)
        => Assert.Equal(FloorModifier.None, FloorModifiers.Pick(42, floor));

    [Fact]
    public void Pick_returns_None_for_boss_floor()
        => Assert.Equal(FloorModifier.None, FloorModifiers.Pick(42, 21));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(7)] [InlineData(19)]
    public void Pick_returns_modifier_for_non_clean_floors(int floor)
        => Assert.NotEqual(FloorModifier.None, FloorModifiers.Pick(42, floor));

    [Fact]
    public void Pick_is_deterministic()
    {
        var a = FloorModifiers.Pick(123, 7);
        var b = FloorModifiers.Pick(123, 7);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pick_varies_by_seed()
    {
        var results = Enumerable.Range(0, 20)
            .Select(s => FloorModifiers.Pick(s * 100, 1))
            .Distinct().ToList();
        Assert.True(results.Count > 1);
    }

    [Fact]
    public void All_five_modifiers_appear_across_seeds_and_floors()
    {
        var seen = new HashSet<FloorModifier>();
        for (int seed = 0; seed < 200; seed++)
            for (int floor = 1; floor <= 20; floor++)
            {
                var m = FloorModifiers.Pick(seed, floor);
                if (m != FloorModifier.None) seen.Add(m);
            }
        Assert.Contains(FloorModifier.DarkMine,    seen);
        Assert.Contains(FloorModifier.Unstable,     seen);
        Assert.Contains(FloorModifier.MonsterSurge, seen);
        Assert.Contains(FloorModifier.Flooded,      seen);
        Assert.Contains(FloorModifier.Haste,        seen);
    }

    [Fact]
    public void Apply_None_is_noop()
    {
        var map = new MapConfig { PoolCount = 3, RiverCount = 2 };
        var sim = new SimConfig();
        int origPool  = map.PoolCount;
        int origVis   = sim.VisionRadius;
        FloorModifiers.Apply(FloorModifier.None, map, sim);
        Assert.Equal(origPool, map.PoolCount);
        Assert.Equal(origVis,  sim.VisionRadius);
    }

    [Fact]
    public void Apply_DarkMine_halves_vision_radius()
    {
        var sim = new SimConfig { VisionRadius = 6 };
        FloorModifiers.Apply(FloorModifier.DarkMine, new MapConfig(), sim);
        Assert.Equal(3, sim.VisionRadius);
    }

    [Fact]
    public void Apply_DarkMine_floors_vision_at_2()
    {
        var sim = new SimConfig { VisionRadius = 3 };
        FloorModifiers.Apply(FloorModifier.DarkMine, new MapConfig(), sim);
        Assert.Equal(2, sim.VisionRadius); // 3/2=1, floored to 2
    }

    [Fact]
    public void Apply_Unstable_enables_caveins_and_doubles_crack_sites()
    {
        var map = new MapConfig { CaveIns = false, CrackSiteCount = 4 };
        FloorModifiers.Apply(FloorModifier.Unstable, map, new SimConfig());
        Assert.True(map.CaveIns);
        Assert.Equal(8, map.CrackSiteCount);
    }

    [Fact]
    public void Apply_MonsterSurge_sets_monster_count_multiplier()
    {
        var sim = new SimConfig();
        FloorModifiers.Apply(FloorModifier.MonsterSurge, new MapConfig(), sim);
        Assert.Equal(1.5f, sim.MonsterCountMultiplier);
    }

    [Fact]
    public void Apply_Flooded_increases_pool_and_river_counts()
    {
        var map = new MapConfig { PoolCount = 3, RiverCount = 2 };
        FloorModifiers.Apply(FloorModifier.Flooded, map, new SimConfig());
        Assert.Equal(6, map.PoolCount);
        Assert.Equal(4, map.RiverCount);
    }

    [Fact]
    public void Apply_Haste_reduces_all_move_cadences_by_30_percent()
    {
        var sim = new SimConfig
        {
            BaseMoveSeconds         = 0.12,
            MonsterSlimeMoveSeconds = 0.5,
            MonsterGhostMoveSeconds = 1.0,
            MonsterGoatMoveSeconds  = 0.15,
        };
        FloorModifiers.Apply(FloorModifier.Haste, new MapConfig(), sim);
        Assert.Equal(0.12 * 0.7, sim.BaseMoveSeconds,         precision: 6);
        Assert.Equal(0.5  * 0.7, sim.MonsterSlimeMoveSeconds, precision: 6);
        Assert.Equal(1.0  * 0.7, sim.MonsterGhostMoveSeconds, precision: 6);
        Assert.Equal(0.15 * 0.7, sim.MonsterGoatMoveSeconds,  precision: 6);
    }
}
