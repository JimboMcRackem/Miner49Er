using Miner49er.Core;
using Xunit;

public class TempleRollTests
{
    [Fact]
    public void Temples_never_appear_on_early_shop_or_boss_floors()
    {
        for (int seed = 0; seed <= 200; seed++)
        {
            for (int floor = 1; floor < 5; floor++) Assert.False(MapConfig.IsTempleFloor(floor, seed));
            for (int floor = 4; floor <= 48; floor += 4) Assert.False(MapConfig.IsTempleFloor(floor, seed));
            Assert.False(MapConfig.IsTempleFloor(51, seed));
        }
    }

    [Fact]
    public void Temples_never_come_two_in_a_row()
    {
        for (int seed = 0; seed <= 200; seed++)
            for (int floor = 5; floor < 50; floor++)
                Assert.False(MapConfig.IsTempleFloor(floor, seed) && MapConfig.IsTempleFloor(floor + 1, seed));
    }

    [Fact]
    public void About_one_eligible_floor_in_six_is_a_temple()
    {
        int temples = 0, eligible = 0;
        for (int seed = 0; seed <= 200; seed++)
            for (int floor = 5; floor <= 50; floor++)
            {
                if (floor % 4 == 0) continue;
                eligible++;
                if (MapConfig.IsTempleFloor(floor, seed)) temples++;
            }
        double rate = (double)temples / eligible;
        Assert.InRange(rate, 1.0 / 8, 1.0 / 5);
    }

    [Fact]
    public void A_real_run_never_gets_two_temples_in_a_row()
    {
        // The game hands FloorConfig a different seed on each floor.
        for (int run = 0; run <= 300; run++)
            for (int floor = 5; floor < 50; floor++)
            {
                bool here = MapConfig.FloorConfig(floor, run + floor * 1000).TempleSectionCount > 0;
                bool next = MapConfig.FloorConfig(floor + 1, run + (floor + 1) * 1000).TempleSectionCount > 0;
                Assert.False(here && next, $"run {run} floor {floor}");
            }
    }

    [Fact]
    public void A_run_mixes_odd_and_even_temple_floors()
    {
        int runs = 0, mixed = 0;
        for (int seed = 0; seed <= 500; seed++)
        {
            var floors = Enumerable.Range(5, 46).Where(f => MapConfig.IsTempleFloor(f, seed)).ToList();
            if (floors.Count < 4) continue;
            runs++;
            if (floors.Any(f => f % 2 == 0) && floors.Any(f => f % 2 == 1)) mixed++;
        }
        Assert.True(mixed >= runs * 0.8, $"{mixed}/{runs} runs mixed");
    }

    [Fact]
    public void Nearly_every_run_has_a_temple()
    {
        int none = Enumerable.Range(0, 501)
            .Count(seed => !Enumerable.Range(5, 46).Any(f => MapConfig.IsTempleFloor(f, seed)));
        Assert.True(none <= 10, $"{none} runs without a temple");
    }

    [Fact]
    public void A_temple_floor_is_set_up_for_the_artifact()
    {
        var (floor, seed) = FirstTemple();
        var cfg = MapConfig.FloorConfig(floor, MapConfig.FloorSeed(seed, floor));

        Assert.InRange(cfg.TempleSectionCount, 2, 4);
        Assert.NotNull(cfg.ExpeditionTreasureKind);
    }

    [Fact]
    public void Temple_floors_are_dry_and_have_no_portals()
    {
        for (int seed = 0; seed <= 200; seed++)
            for (int floor = 10; floor <= 50; floor++)
            {
                if (!MapConfig.IsTempleFloor(floor, seed)) continue;
                var cfg = MapConfig.FloorConfig(floor, MapConfig.FloorSeed(seed, floor));
                Assert.False(cfg.FloodedCave);
                Assert.Equal(0, cfg.PortalPairCount);
            }
    }

    [Fact]
    public void Temple_floors_never_roll_the_flooded_modifier()
    {
        for (int seed = 0; seed <= 200; seed++)
            for (int floor = 5; floor <= 50; floor++)
                if (MapConfig.IsTempleFloor(floor, seed))
                    Assert.NotEqual(FloorModifier.Flooded, FloorModifiers.Pick(seed, floor));
    }

    [Fact]
    public void The_artifact_can_hide_in_all_three_places()
    {
        var seen = new HashSet<TempleHideSpot>();
        for (int seed = 0; seed <= 200; seed++)
            for (int floor = 5; floor <= 50; floor++)
                if (MapConfig.IsTempleFloor(floor, seed))
                    seen.Add(MapConfig.FloorConfig(floor, MapConfig.FloorSeed(seed, floor)).TempleHide);
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void An_ordinary_floor_has_no_sections()
    {
        Assert.Equal(0, MapConfig.FloorConfig(4, 1).TempleSectionCount);
    }

    private static (int Floor, int Seed) FirstTemple()
    {
        for (int seed = 0; ; seed++)
            for (int floor = 5; floor <= 50; floor++)
                if (MapConfig.IsTempleFloor(floor, seed)) return (floor, seed);
    }
}
