using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class TempleBotTests
{
    private const ItemKind Artifact = ItemKind.IdolUrn;
    private static readonly GridPos Exit = new(1, 1);

    private static Simulation Sim(TileGrid grid) => new(grid, new SimConfig
    {
        Mode = GameMode.Expedition, ExpeditionTreasureKind = Artifact, RequireArtifactAtExit = true,
        BaseMoveSeconds = 0.1, MinMoveSeconds = 0.1,
    }, escapeTile: Exit);

    private static void Run(Simulation sim, BotBrain brain, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            var action = brain.Think(sim, GameMode.Expedition);
            if (action.Use) sim.TryUseItem(1);
            if (action.Dir >= 0) sim.TryMove(1, (Direction)action.Dir);
            sim.Tick(0.1);
        }
    }

    [Theory]
    [InlineData(TileType.Column)]
    [InlineData(TileType.Altar)]
    [InlineData(TileType.Jar)]
    public void Foremen_walk_around_temple_furniture(TileType prop)
    {
        var grid = new TileGrid(7, 3, TileType.TempleFloor);
        grid.Set(new GridPos(3, 1), prop);

        int dir = BotPathfinder.NextDir(grid, new GridPos(2, 1), new GridPos(4, 1), passRock: true);

        Assert.NotEqual((int)Direction.East, dir);
        Assert.NotEqual(-1, dir);
    }

    [Fact]
    public void A_bot_carrying_the_artifact_walks_it_to_the_exit()
    {
        var sim = Sim(new TileGrid(12, 5, TileType.Floor));
        sim.AddItem(new Item(new GridPos(9, 1), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, new GridPos(9, 1));
        Assert.True(sim.TryUseItem(1));

        Run(sim, new BotBrain(1, BotSkill.Greenhorn, seed: 1), 150);

        Assert.True(sim.ArtifactAtExit);
    }

    [Fact]
    public void A_bot_leaves_the_exit_to_fetch_a_dropped_artifact()
    {
        var sim = Sim(new TileGrid(12, 5, TileType.Floor));
        sim.AddItem(new Item(new GridPos(9, 3), Artifact, ItemPlacement.Loose));
        sim.AddMiner(1, Exit);
        sim.AddMiner(2, new GridPos(9, 3));
        Assert.True(sim.TryUseItem(2));
        sim.KillMiner(2);      // drops it where the carrier fell; the escape stays open
        sim.Tick(0.1);

        Run(sim, new BotBrain(1, BotSkill.Miner, seed: 2), 200);

        Assert.Equal(Artifact, sim.GetMiner(1).Held);
    }
}
