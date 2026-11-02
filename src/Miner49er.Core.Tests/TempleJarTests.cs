using Miner49er.Core;
using Xunit;

public class TempleJarTests
{
    private const ItemKind Artifact = ItemKind.IdolUrn;
    private static readonly GridPos Exit = new(1, 1);

    private static SimConfig Cfg() => new()
    {
        Mode = GameMode.Expedition, ExpeditionTreasureKind = Artifact, RequireArtifactAtExit = true,
        PickaxeSeconds = 0.1, ArtifactGoldBonus = 30,
    };

    private static (Simulation Sim, Miner Miner, GridPos Target) Beside(TileType tile, SimConfig? cfg = null)
    {
        var grid = new TileGrid(8, 3, TileType.TempleFloor);
        var target = new GridPos(4, 1);
        grid.Set(target, tile);
        var sim = new Simulation(grid, cfg ?? Cfg(), escapeTile: Exit);
        var m = sim.AddMiner(1, new GridPos(3, 1));
        m.Facing = Direction.East;
        return (sim, m, target);
    }

    private static void Mine(Simulation sim)
    {
        Assert.True(sim.TryStartMining(1));
        for (int i = 0; i < 5; i++) sim.Tick(0.1);
    }

    [Theory]
    [InlineData(TileType.Jar)]
    [InlineData(TileType.MarblePanel)]
    public void Breaking_temple_stone_leaves_temple_floor(TileType tile)
    {
        var (sim, _, target) = Beside(tile);

        Mine(sim);

        Assert.Equal(TileType.TempleFloor, sim.Grid.Get(target));
    }

    [Fact]
    public void A_blast_leaves_temple_floor_too()
    {
        var (sim, _, target) = Beside(TileType.Jar);

        sim.DetonateForTest(target);

        Assert.Equal(TileType.TempleFloor, sim.Grid.Get(target));
    }

    [Fact]
    public void A_mined_doorway_is_ordinary_floor()
    {
        var (sim, _, target) = Beside(TileType.Rock);

        Mine(sim);

        Assert.Equal(TileType.Floor, sim.Grid.Get(target));
    }

    [Fact]
    public void Jars_mostly_hold_nothing_or_a_little_gold()
    {
        var rng = new Random(5);
        var rolls = Enumerable.Range(0, 10000).Select(_ => JarLootTable.Roll(rng)).ToList();
        double Share(JarLoot l) => rolls.Count(r => r == l) / (double)rolls.Count;

        Assert.InRange(Share(JarLoot.Gold), 0.35, 0.45);
        Assert.InRange(Share(JarLoot.Item), 0.05, 0.15);
        Assert.InRange(Share(JarLoot.Nothing), 0.45, 0.55);
    }

    [Fact]
    public void A_jar_never_pays_when_it_holds_the_artifact()
    {
        for (int i = 0; i < 40; i++)
        {
            var (sim, m, target) = Beside(TileType.Jar);
            sim.AddItem(new Item(target, Artifact, ItemPlacement.Buried));

            Mine(sim);

            var only = Assert.Single(sim.Items);
            Assert.Equal(Artifact, only.Kind);
            Assert.Equal(ItemPlacement.Loose, only.Placement);
            Assert.Equal(0, m.GoldCollected);
        }
    }

    [Fact]
    public void Smashing_jars_eventually_pays_the_miner()
    {
        int gold = 0, items = 0;
        for (int i = 0; i < 100; i++)
        {
            var (sim, m, _) = Beside(TileType.Jar, new SimConfig { PickaxeSeconds = 0.1, Seed = i });
            Mine(sim);
            gold += m.GoldCollected;
            items += sim.Items.Count;
        }
        Assert.True(gold > 0);
        Assert.True(items > 0);
    }

    [Fact]
    public void Leaving_with_the_artifact_pays_the_bonus_once()
    {
        var (sim, m, _) = Beside(TileType.TempleFloor);
        sim.GiveItemForTest(1, Artifact);
        sim.SetMinerPositionForTest(1, Exit);

        Assert.Equal(Artifact, sim.ClaimArtifact());
        Assert.Equal(30, m.GoldCollected);
        Assert.Null(sim.ClaimArtifact());
        Assert.Equal(30, m.GoldCollected);
    }

    [Fact]
    public void No_bonus_without_the_artifact_at_the_exit()
    {
        var (sim, m, _) = Beside(TileType.TempleFloor);

        Assert.Null(sim.ClaimArtifact());
        Assert.Equal(0, m.GoldCollected);
    }

    [Fact]
    public void An_ordinary_treasure_floor_pays_no_bonus()
    {
        var cfg = Cfg();
        cfg.RequireArtifactAtExit = false;
        var (sim, m, _) = Beside(TileType.TempleFloor, cfg);
        sim.GiveItemForTest(1, Artifact);
        sim.SetMinerPositionForTest(1, Exit);

        Assert.Null(sim.ClaimArtifact());
    }
}
