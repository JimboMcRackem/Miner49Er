using Miner49er.Core;
using Xunit;

public class TempleArtifactTests
{
    private const ItemKind Artifact = ItemKind.IdolUrn;
    private static readonly GridPos Exit = new(1, 1);

    private static SimConfig Cfg() => new()
    {
        Mode = GameMode.Expedition, ExpeditionTreasureKind = Artifact, RequireArtifactAtExit = true,
        BaseMoveSeconds = 0.1, MinMoveSeconds = 0.1,
    };

    private static Simulation Sim(TileGrid? grid = null, params Item[] items) =>
        Build(grid ?? new TileGrid(10, 5, TileType.Floor), Cfg(), items);

    private static Simulation Build(TileGrid grid, SimConfig cfg, params Item[] items)
    {
        var sim = new Simulation(grid, cfg, escapeTile: Exit);
        foreach (var item in items) sim.AddItem(item);
        return sim;
    }

    private static void TakeArtifact(Simulation sim, int id)
    {
        Assert.True(sim.TryUseItem(id));
        Assert.Equal(Artifact, sim.GetMiner(id).Held);
    }

    [Fact]
    public void Standing_on_the_exit_without_the_artifact_does_not_end_the_floor()
    {
        var sim = Sim(null, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMiner(2, new GridPos(4, 1));
        TakeArtifact(sim, 1);
        sim.SetMinerPositionForTest(2, Exit);
        sim.KillMiner(1);
        sim.Tick(0.1);

        Assert.False(sim.ArtifactAtExit);
        Assert.False(RoundResolver.Resolve(sim, GameMode.Expedition).FloorCleared);
    }

    [Fact]
    public void Carrying_the_artifact_to_the_exit_ends_the_floor()
    {
        var sim = Sim(null, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        TakeArtifact(sim, 1);
        sim.SetMinerPositionForTest(1, Exit);

        Assert.True(sim.ArtifactAtExit);
        Assert.True(RoundResolver.Resolve(sim, GameMode.Expedition).FloorCleared);
    }

    [Fact]
    public void Dropping_the_artifact_on_the_way_out_stops_the_floor_ending()
    {
        var sim = Sim(null, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        TakeArtifact(sim, 1);
        sim.SetMinerPositionForTest(1, new GridPos(2, 1));
        sim.TryUseItem(1);                         // puts it down
        sim.SetMinerPositionForTest(1, Exit);

        Assert.False(sim.ArtifactAtExit);
        Assert.False(RoundResolver.Resolve(sim, GameMode.Expedition).FloorCleared);
    }

    [Fact]
    public void An_ordinary_treasure_floor_does_not_need_it_at_the_exit()
    {
        var cfg = Cfg();
        cfg.RequireArtifactAtExit = false;
        var sim = Build(new TileGrid(10, 5, TileType.Floor), cfg, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        TakeArtifact(sim, 1);
        sim.TryUseItem(1);
        sim.SetMinerPositionForTest(1, Exit);

        Assert.True(RoundResolver.Resolve(sim, GameMode.Expedition).FloorCleared);
    }

    [Fact]
    public void A_dead_carrier_leaves_the_artifact_where_they_fell()
    {
        var sim = Sim(null, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMiner(2, new GridPos(8, 3));
        TakeArtifact(sim, 1);
        sim.SetMinerPositionForTest(1, new GridPos(5, 2));
        sim.KillMiner(1);
        sim.Tick(0.1);

        Assert.Null(sim.GetMiner(1).Held);
        Assert.Contains(sim.Items, it => it.Kind == Artifact && it.Pos == new GridPos(5, 2) && it.Placement == ItemPlacement.Loose);
    }

    [Theory]
    [InlineData(TileType.Lava)]
    [InlineData(TileType.DeepWater)]
    [InlineData(TileType.Pit)]
    public void A_carrier_lost_to_a_hazard_leaves_the_artifact_on_safe_ground(TileType hazard)
    {
        var grid = new TileGrid(10, 5, TileType.Floor);
        var sim = Sim(grid, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMiner(2, new GridPos(8, 3));
        TakeArtifact(sim, 1);
        grid.Set(new GridPos(5, 2), hazard);
        sim.SetMinerPositionForTest(1, new GridPos(5, 2));
        sim.KillMiner(1);
        sim.Tick(0.1);

        var dropped = sim.Items.Single(it => it.Kind == Artifact);
        Assert.False(grid.Get(dropped.Pos).IsLethal());
    }

    [Fact]
    public void A_carrier_who_disconnects_leaves_it_behind_for_the_party()
    {
        var sim = Sim(null, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMiner(2, new GridPos(8, 3));
        TakeArtifact(sim, 1);
        sim.KillMiner(1);                          // MatchHost does this when a peer drops
        sim.Tick(0.1);
        sim.SetMinerPositionForTest(2, sim.Items.Single(it => it.Kind == Artifact).Pos);

        TakeArtifact(sim, 2);
    }

    [Fact]
    public void A_stunned_carrier_keeps_hold_of_it()
    {
        var sim = Sim(null, new Item(new GridPos(3, 1), Artifact, ItemPlacement.Loose));
        var m = sim.AddMiner(1, new GridPos(3, 1));
        TakeArtifact(sim, 1);
        m.StunRemaining = 2;
        sim.Tick(0.1);

        Assert.Equal(Artifact, m.Held);
    }

    [Theory]
    [InlineData(TileType.MarblePanel)]
    [InlineData(TileType.Jar)]
    public void A_blast_uncovers_an_artifact_instead_of_destroying_it(TileType hiding)
    {
        var grid = new TileGrid(12, 5, TileType.TempleFloor);
        grid.Set(new GridPos(6, 2), hiding);
        var sim = Build(grid, Cfg(), new Item(new GridPos(6, 2), Artifact, ItemPlacement.Buried));
        sim.AddMiner(1, new GridPos(1, 3));
        sim.DetonateForTest(new GridPos(6, 2));

        Assert.Contains(sim.Items, it => it.Kind == Artifact && it.Placement == ItemPlacement.Loose);
    }

    [Fact]
    public void The_artifact_hides_where_the_floor_says()
    {
        var seen = new HashSet<TempleHideSpot>();
        foreach (var (floor, seed) in TempleFloors())
        {
            var cfg = MapConfig.FloorConfig(floor, MapConfig.FloorSeed(seed, floor));
            var map = MapGenerator.Generate(cfg);
            var item = map.Items.Single(it => it.Kind == cfg.ExpeditionTreasureKind);
            var tile = map.Grid.Get(item.Pos);
            seen.Add(cfg.TempleHide);

            Assert.Equal(item.Pos, map.ExpeditionTreasurePos);
            Assert.Contains(map.TempleSections, s => s.Floor.Contains(item.Pos) || s.Panels.Contains(item.Pos));
            switch (cfg.TempleHide)
            {
                case TempleHideSpot.Buried:
                    Assert.Equal(TileType.MarblePanel, tile);
                    Assert.Equal(ItemPlacement.Buried, item.Placement);
                    break;
                case TempleHideSpot.Pot:
                    Assert.Equal(TileType.Jar, tile);
                    Assert.Equal(ItemPlacement.Buried, item.Placement);
                    break;
                default:
                    Assert.Equal(TileType.TempleFloor, tile);
                    Assert.Equal(ItemPlacement.Toolbox, item.Placement);
                    break;
            }
        }
        Assert.Equal(3, seen.Count);
    }

    private static IEnumerable<(int Floor, int Seed)> TempleFloors() =>
        (from seed in Enumerable.Range(0, 60)
         from floor in Enumerable.Range(5, 16)
         where MapConfig.IsTempleFloor(floor, seed)
         select (floor, seed)).Take(30).ToList();
}
