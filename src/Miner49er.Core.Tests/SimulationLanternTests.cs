using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationLanternTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig { MonsterGhostMoveSeconds = 999 });

    private static void GiveLantern(Simulation sim, int minerId, GridPos minerPos)
    {
        sim.AddItem(new Item(minerPos, ItemKind.Lantern, ItemPlacement.Loose));
        sim.TryUseItem(minerId);
    }

    [Fact]
    public void Ghost_inside_lantern_aoe_dies_on_tick()
    {
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 999 };
        var grid = new TileGrid(11, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(5, 1));
        GiveLantern(sim, 1, new GridPos(5, 1));
        // Ghost at distance 2 (inside radius 3)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.01);

        Assert.False(ghost.Alive);
    }

    [Fact]
    public void Ghost_outside_lantern_aoe_survives()
    {
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 999 };
        var grid = new TileGrid(15, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(7, 1));
        GiveLantern(sim, 1, new GridPos(7, 1));
        // Ghost at distance 4 (outside radius 3)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.01);

        Assert.True(ghost.Alive);
    }

    [Fact]
    public void Ghost_does_not_step_into_lantern_light()
    {
        // Ghost at (3,1): Chebyshev dist to miner (7,1) = 4, outside radius 3.
        // One step east → (4,1): dist 3 — exactly at boundary, still counted inside (≤3).
        // Ghost should halt rather than step in.
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 0.1,
                                  MonsterSenseRadius = 99 };
        var grid = new TileGrid(15, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(7, 1));
        GiveLantern(sim, 1, new GridPos(7, 1));
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(3, 1), ghost.Pos);   // halted at boundary
        Assert.True(ghost.Alive);                      // didn't step in, not killed
    }

    [Fact]
    public void Placed_lantern_kills_ghost_in_its_aoe()
    {
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 999 };
        var grid = new TileGrid(11, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(5, 1));
        GiveLantern(sim, 1, new GridPos(5, 1));
        sim.TryUseItem(1);   // drop lantern at (5,1)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.01);

        Assert.False(ghost.Alive);
    }

    [Fact]
    public void Dropped_lantern_appears_as_loose_item_and_can_be_picked_back_up()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        var miner = sim.AddMiner(1, new GridPos(3, 1));
        GiveLantern(sim, 1, new GridPos(3, 1));

        sim.TryUseItem(1);   // drop
        Assert.Null(miner.Held);
        Assert.Single(sim.Items.Where(it => it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose));

        sim.TryUseItem(1);   // pick back up
        Assert.Equal(ItemKind.Lantern, miner.Held);
        Assert.Empty(sim.Items.Where(it => it.Kind == ItemKind.Lantern));
    }
}
