using System.Linq;
using Miner49er.Core;
using Xunit;

public class ChestLootTests
{
    // ---- ChestLootTable ----

    [Fact]
    public void Roll_returns_only_valid_loot_kinds()
    {
        var rng = new System.Random(42);
        var valid = new[] { ItemKind.LifePotion, ItemKind.SpeedPotion, ItemKind.LongerVision, ItemKind.BiggerBlast };
        for (int i = 0; i < 200; i++)
            Assert.Contains(ChestLootTable.Roll(rng), valid);
    }

    [Fact]
    public void Roll_produces_LifePotion_roughly_40_percent()
    {
        var rng = new System.Random(0);
        int lifePotions = Enumerable.Range(0, 1000).Count(_ => ChestLootTable.Roll(rng) == ItemKind.LifePotion);
        Assert.InRange(lifePotions, 300, 500);
    }

    // ---- Chest pickup ----

    [Fact]
    public void Chest_pickup_applies_a_rolled_buff_not_wins_the_run()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig { Seed = 1 });
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.Chest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        Assert.Empty(sim.Items);
        var result = RoundResolver.Resolve(sim, GameMode.Expedition);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void LifePotion_fires_LifeRestored_event()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LifePotion, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Contains(sim.DrainEvents(), e => e is LifeRestored lr && lr.MinerId == 1);
    }

    // ---- BossChest ----

    [Fact]
    public void BossChest_opens_escape_when_grabbed()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var escape = new GridPos(0, 2);
        var sim = new Simulation(grid, new SimConfig { RequireChestForEscape = true },
                                 escapeTile: escape);
        Assert.False(sim.EscapeOpen);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BossChest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.True(sim.EscapeOpen);
        Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
    }

    [Fact]
    public void RequireChestForEscape_prevents_auto_open_on_zero_gold_map()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig { RequireChestForEscape = true },
                                 escapeTile: new GridPos(0, 0));
        Assert.False(sim.EscapeOpen);
    }

    [Fact]
    public void Normal_zero_gold_map_still_auto_opens_escape()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: new GridPos(0, 0));
        Assert.True(sim.EscapeOpen);
    }

    // ---- MapConfig ----

    [Fact]
    public void FloorConfig_sets_ChestCount_1_for_early_floors()
    {
        Assert.Equal(1, MapConfig.FloorConfig(3, 1).ChestCount);
        Assert.Equal(1, MapConfig.FloorConfig(10, 1).ChestCount);
    }

    [Fact]
    public void FloorConfig_sets_ChestCount_2_for_late_floors()
    {
        Assert.Equal(2, MapConfig.FloorConfig(11, 1).ChestCount);
        Assert.Equal(2, MapConfig.FloorConfig(20, 1).ChestCount);
    }

    [Fact]
    public void Generate_places_ChestCount_Chest_items_as_toolboxes()
    {
        var cfg = MapConfig.FloorConfig(5, 42);
        var map = MapGenerator.Generate(cfg);
        var chests = map.Items.Where(i => i.Kind == ItemKind.Chest && i.Placement == ItemPlacement.Toolbox).ToList();
        Assert.Equal(cfg.ChestCount, chests.Count);
    }

    [Fact]
    public void BossFloor_has_BossChest_not_regular_Chest()
    {
        var map = MapGenerator.GenerateBossFloor(1);
        Assert.Contains(map.Items, i => i.Kind == ItemKind.BossChest);
        Assert.DoesNotContain(map.Items, i => i.Kind == ItemKind.Chest);
    }

    [Fact]
    public void BossFloor_EscapeTile_is_at_top_of_north_corridor()
    {
        var map = MapGenerator.GenerateBossFloor(1);
        Assert.NotNull(map.EscapeTile);
        Assert.Equal(new GridPos(20, 1), map.EscapeTile!.Value);
    }

    [Fact]
    public void GeneratedMap_EscapeTile_equals_Spawns0_for_normal_maps()
    {
        var cfg = MapConfig.FloorConfig(1, 7);
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(map.Spawns[0], map.EscapeTile);
    }
}
