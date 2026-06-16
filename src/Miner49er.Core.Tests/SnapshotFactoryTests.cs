using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotFactoryTests
{
    [Fact]
    public void Captures_miner_position_facing_and_alive()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East); // now at (3,3) facing East

        var snap = SnapshotFactory.Capture(sim, tick: 11);

        Assert.Equal(11, snap.Tick);
        var m = Assert.Single(snap.Miners);
        Assert.Equal(1, m.Id);
        Assert.Equal(3, m.X);
        Assert.Equal(3, m.Y);
        Assert.Equal((int)Direction.East, m.Facing);
        Assert.True(m.Alive);
    }

    [Fact]
    public void Captures_effective_move_seconds()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        var snap = SnapshotFactory.Capture(sim, tick: 1);
        Assert.Equal(0.12, Assert.Single(snap.Miners).MoveSeconds, 3); // standard floor cadence
    }

    [Fact]
    public void Captures_charges()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East); // blocked by rock at (3,2) but sets facing East
        sim.TryStartPlanting(1);
        sim.Tick(sim.Config.PlantSeconds + 0.01); // finish planting -> charge exists

        var snap = SnapshotFactory.Capture(sim, tick: 0);
        var c = Assert.Single(snap.Charges);
        Assert.Equal(3, c.X);
        Assert.Equal(2, c.Y);
    }

    [Fact]
    public void Capture_includes_seconds_remaining_from_a_timed_sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig(),
            timeLimitSeconds: 30.0);
        sim.Tick(10.0); // 20s left

        var snap = SnapshotFactory.Capture(sim, tick: 1);

        Assert.Equal(20f, snap.SecondsRemaining, 3);
    }

    [Fact]
    public void Capture_reports_minus_one_for_an_untimed_sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        var snap = SnapshotFactory.Capture(sim, tick: 1);
        Assert.Equal(-1f, snap.SecondsRemaining);
    }

    [Fact]
    public void Captures_held_item_and_mold_patches()
    {
        var sim = new Simulation(new TileGrid(7, 7, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold));
        sim.TryUseItem(1);   // hand = SlowMold
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.WaterPlank));
        sim.TryUseItem(1);   // swap: hand = WaterPlank
        // drop a mold from a second miner so a patch exists:
        sim.AddMiner(2, new GridPos(1, 1));
        sim.AddItem(new Item(new GridPos(1, 1), ItemKind.SlowMold));
        sim.TryUseItem(2);   // hand2 = SlowMold
        sim.TryUseItem(2);   // drop -> patch at (1,1)

        var snap = SnapshotFactory.Capture(sim, tick: 5);

        Assert.Equal((int)ItemKind.WaterPlank, snap.Miners.Single(m => m.Id == 1).Held);
        Assert.Equal(-1, snap.Miners.Single(m => m.Id == 2).Held); // empty hand
        Assert.NotEmpty(snap.Molds);
        Assert.Contains(snap.Molds, mo => mo.X == 1 && mo.Y == 1); // the drop centre
        Assert.All(snap.Molds, mo => Assert.Equal(sim.Config.MoldSeconds, mo.RemainingSeconds, 3));
    }

    [Fact]
    public void Captures_items_and_effective_vision_radius()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(4, 1), ItemKind.LongerVision, ItemPlacement.Buried));
        sim.ApplyEffect(1, EffectKind.LongerVision, EffectChannel.VisionRadius, 3, 5.0);

        var snap = SnapshotFactory.Capture(sim, tick: 3);

        Assert.Equal(8, Assert.Single(snap.Miners).VisionRadius); // 5 + 3
        var item = Assert.Single(snap.Items);
        Assert.Equal(4, item.X);
        Assert.Equal(1, item.Y);
        Assert.Equal(ItemKind.LongerVision, item.Kind);
        Assert.Equal(ItemPlacement.Buried, item.Placement);
    }

    [Fact]
    public void Captures_monsters_and_escape_flag()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: new GridPos(0, 0));
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Goat);

        var snap = SnapshotFactory.Capture(sim, tick: 4);

        var mo = Assert.Single(snap.Monsters);
        Assert.Equal(1, mo.Id);
        Assert.Equal(5, mo.X);
        Assert.Equal(5, mo.Y);
        Assert.Equal(MonsterKind.Goat, mo.Kind);
        Assert.True(mo.Alive);
        // No GoldRock on this all-Floor grid => escape opens immediately.
        Assert.True(snap.EscapeOpen);
    }
}
