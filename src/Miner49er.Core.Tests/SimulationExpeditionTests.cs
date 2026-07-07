using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationExpeditionTests
{
    // Grid with two gold veins the miner can mine instantly.
    private static (Simulation sim, Miner miner) Setup()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.GoldRock);
        grid.Set(new GridPos(4, 1), TileType.GoldRock);
        var cfg = new SimConfig { PickaxeSeconds = 0.1 };
        var sim = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
        var miner = sim.AddMiner(1, new GridPos(1, 1));
        return (sim, miner);
    }

    [Fact]
    public void Escape_opens_at_50_percent_gold_collected()
    {
        // Setup: 2 gold veins — 50% threshold = 1 vein
        var (sim, miner) = Setup();

        Assert.False(sim.EscapeOpen);   // starts locked

        // Clear first vein (50%)
        miner.Facing = Direction.East;
        sim.TryStartMining(1);
        sim.Tick(0.1);

        Assert.False(sim.AllGoldCleared);   // still 1 remaining
        Assert.True(sim.EscapeOpen);        // escape open at 50%
        Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
    }

    [Fact]
    public void Escape_does_not_open_below_50_percent()
    {
        // 4 gold veins — need >= 2 before escape opens
        var grid = new TileGrid(12, 3, TileType.Floor);
        for (int x = 2; x <= 8; x += 2)
            grid.Set(new GridPos(x, 1), TileType.GoldRock);   // 4 veins at x=2,4,6,8
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 },
            escapeTile: new GridPos(0, 1));
        var miner = sim.AddMiner(1, new GridPos(1, 1));

        // Clear first vein (25%) — escape stays shut
        miner.Facing = Direction.East;
        sim.TryStartMining(1);
        sim.Tick(0.1);
        sim.DrainEvents();   // discard

        Assert.False(sim.EscapeOpen);
    }

    [Fact]
    public void Treasure_floor_gold_threshold_does_not_open_escape()
    {
        var (sim, miner) = Setup();
        sim.Config.ExpeditionTreasureKind = ItemKind.IdolVishnu;

        miner.Facing = Direction.East;
        sim.TryStartMining(1);
        sim.Tick(0.1);

        // 50% gold collected but it's a treasure floor — escape stays shut
        Assert.False(sim.EscapeOpen);
    }

    [Fact]
    public void Treasure_floor_escape_opens_when_correct_idol_picked_up()
    {
        var grid = new TileGrid(5, 3, TileType.Floor);
        var cfg  = new SimConfig { ExpeditionTreasureKind = ItemKind.IdolVishnu };
        var sim  = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddItem(new Item(new GridPos(2, 1), ItemKind.IdolVishnu, ItemPlacement.Loose));

        Assert.False(sim.EscapeOpen);

        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        sim.TryUseItem(1);
        sim.Tick(0.0);

        Assert.True(sim.EscapeOpen);
        Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
    }

    [Fact]
    public void Treasure_floor_wrong_idol_does_not_open_escape()
    {
        var grid = new TileGrid(5, 3, TileType.Floor);
        var cfg  = new SimConfig { ExpeditionTreasureKind = ItemKind.IdolVishnu };
        var sim  = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddItem(new Item(new GridPos(2, 1), ItemKind.IdolSkull, ItemPlacement.Loose));

        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        sim.TryUseItem(1);
        sim.Tick(0.0);

        Assert.False(sim.EscapeOpen);
    }
}
