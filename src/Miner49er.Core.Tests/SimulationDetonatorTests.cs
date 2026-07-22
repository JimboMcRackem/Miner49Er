using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationDetonatorTests
{
    // Walk n steps, advancing the move cooldown between steps.
    private static void Walk(Simulation sim, int id, Direction dir, int steps, SimConfig cfg)
    {
        for (int i = 0; i < steps; i++)
        {
            sim.TryMove(id, dir);
            sim.Tick(cfg.BaseMoveSeconds); // drain cooldown
        }
    }

    // Grid: miner at (1,2) facing east toward rock at (2,2), walls of rock at x=2.
    private static Simulation FacingRockWithDetonator(out Miner m, SimConfig? cfg = null)
    {
        var grid = new TileGrid(7, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(2, y), TileType.Rock);
        var sim = new Simulation(grid, cfg ?? new SimConfig { PlantSeconds = 1.0 });
        m = sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // blocked, now facing east toward (2,2)
        m.Held = ItemKind.Detonator;
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void UseItem_with_detonator_starts_PlantingDetonator_activity()
    {
        var sim = FacingRockWithDetonator(out var m);

        bool started = sim.TryUseItem(1);

        Assert.True(started);
        Assert.Equal(ActivityKind.PlantingDetonator, m.Activity);
        Assert.Equal(new GridPos(2, 2), m.ActivityTarget);
    }

    [Fact]
    public void Completing_PlantingDetonator_creates_reel_charge_and_gives_reel()
    {
        var sim = FacingRockWithDetonator(out var m, new SimConfig { PlantSeconds = 1.0 });

        sim.TryUseItem(1);
        sim.Tick(1.0); // complete planting

        Assert.Single(sim.ReelCharges);
        Assert.Equal(1, sim.ReelCharges[0].OwnerId);
        Assert.Equal(new GridPos(2, 2), sim.ReelCharges[0].WallPos);
        Assert.Equal(ItemKind.Reel, m.Held);
    }

    [Fact]
    public void Cannot_plant_detonator_if_already_has_one_in_wall()
    {
        var sim = FacingRockWithDetonator(out var m, new SimConfig { PlantSeconds = 1.0 });

        sim.TryUseItem(1);
        sim.Tick(1.0); // first detonator planted
        m.Held = ItemKind.Detonator; // hypothetically try to plant another

        bool second = sim.TryUseItem(1); // should block

        Assert.False(second);
        Assert.Single(sim.ReelCharges); // still only one
    }

    [Fact]
    public void UseReel_at_safe_distance_detonates_and_clears_reel()
    {
        // Wide grid: rock column at x=9, miner at (8,2), 4 steps west → (4,2), dist=5 > safeDistance=3.
        var grid = new TileGrid(15, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(9, y), TileType.Rock);
        var cfg = new SimConfig { PlantSeconds = 1.0, ReelSafeDistance = 3, ReelMaxLength = 20 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(8, 2));
        sim.TryMove(1, Direction.East); // face east, blocked by rock at (9,2)
        m.Held = ItemKind.Detonator;
        sim.DrainEvents();

        sim.TryUseItem(1);
        sim.Tick(1.0); // plant detonator at (9,2); now holds Reel
        Walk(sim, 1, Direction.West, 4, cfg); // (8→4,2), dist=5
        sim.DrainEvents();

        bool detonated = sim.TryUseItem(1); // use Reel

        Assert.True(detonated);
        Assert.Empty(sim.ReelCharges);
        Assert.Null(m.Held);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(9, 2)));
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ReelDetonated);
    }

    [Fact]
    public void UseReel_too_close_fails()
    {
        var sim = FacingRockWithDetonator(out var m, new SimConfig { PlantSeconds = 1.0, ReelSafeDistance = 5, ReelMaxLength = 20 });

        sim.TryUseItem(1);
        sim.Tick(1.0); // plant; miner is still at (1,2), distance to (2,2) = 1

        bool result = sim.TryUseItem(1);

        Assert.False(result);
        Assert.Single(sim.ReelCharges); // charge still there
        Assert.Equal(ItemKind.Reel, m.Held); // reel still held
    }

    [Fact]
    public void Wire_snaps_when_miner_moves_too_far()
    {
        // Wide grid: rock column at x=9, miner at (8,2), steps west until dist > MaxLength=4.
        var grid = new TileGrid(15, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(9, y), TileType.Rock);
        var cfg = new SimConfig { PlantSeconds = 1.0, ReelSafeDistance = 3, ReelMaxLength = 4 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(8, 2));
        sim.TryMove(1, Direction.East);
        m.Held = ItemKind.Detonator;
        sim.DrainEvents();

        sim.TryUseItem(1);
        sim.Tick(1.0); // plant at (9,2); miner at (8,2), dist=1
        sim.DrainEvents();

        // Move west 5 tiles: (8→3,2), dist from (9,2) = 6, exceeds MaxLength=4
        Walk(sim, 1, Direction.West, 5, cfg);
        sim.Tick(0.01); // one more AdvanceReels to catch any remaining snaps

        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ReelSnapped snapped && snapped.MinerId == 1);
        Assert.Empty(sim.ReelCharges);
        Assert.Null(m.Held);
    }

    [Fact]
    public void DetonatorPlanted_event_is_emitted_when_planting_completes()
    {
        var sim = FacingRockWithDetonator(out _, new SimConfig { PlantSeconds = 1.0 });

        sim.TryUseItem(1);
        sim.Tick(1.0);

        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is DetonatorPlanted dp && dp.WallPos == new GridPos(2, 2));
    }

    [Fact]
    public void UnlimitedDetonators_regrants_detonator_after_detonating()
    {
        // Wide grid: rock column at x=9, miner at (8,2), retreat west to a safe distance.
        var grid = new TileGrid(15, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(9, y), TileType.Rock);
        var cfg = new SimConfig { PlantSeconds = 1.0, ReelSafeDistance = 3, ReelMaxLength = 20, UnlimitedDetonators = true };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(8, 2));
        sim.TryMove(1, Direction.East); // face east, blocked by rock at (9,2)
        m.Held = ItemKind.Detonator;
        sim.DrainEvents();

        sim.TryUseItem(1);
        sim.Tick(1.0); // plant at (9,2); now holds Reel
        Walk(sim, 1, Direction.West, 4, cfg); // retreat to dist=5
        sim.TryUseItem(1); // detonate

        Assert.Empty(sim.ReelCharges);
        Assert.Equal(ItemKind.Detonator, m.Held); // detonator replenished, not consumed
    }

    [Fact]
    public void UnlimitedDetonators_regrants_detonator_when_wire_snaps()
    {
        var grid = new TileGrid(15, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(9, y), TileType.Rock);
        var cfg = new SimConfig { PlantSeconds = 1.0, ReelSafeDistance = 3, ReelMaxLength = 4, UnlimitedDetonators = true };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(8, 2));
        sim.TryMove(1, Direction.East);
        m.Held = ItemKind.Detonator;
        sim.DrainEvents();

        sim.TryUseItem(1);
        sim.Tick(1.0); // plant at (9,2); dist=1
        Walk(sim, 1, Direction.West, 5, cfg); // dist=6 > MaxLength=4 → wire snaps
        sim.Tick(0.01);

        Assert.Empty(sim.ReelCharges);
        Assert.Equal(ItemKind.Detonator, m.Held); // detonator replenished after snap
    }

    [Fact]
    public void Detonation_kills_miner_in_blast_radius()
    {
        // Two miners: one plants detonator, one stands in blast zone.
        // Rock at x=9, planter at (8,2), victim at (10,2). Planter retreats west 6 tiles → (2,2), dist=7 > safeDistance=5.
        var grid = new TileGrid(15, 5, TileType.Floor);
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(9, y), TileType.Rock);
        var cfg = new SimConfig { PlantSeconds = 0.1, ReelSafeDistance = 5, ReelMaxLength = 30, BlastRockRadius = 1, BlastKillRadius = 1 };
        var sim = new Simulation(grid, cfg);
        var m1 = sim.AddMiner(1, new GridPos(8, 2)); // planter
        var m2 = sim.AddMiner(2, new GridPos(10, 2)); // victim east of the rock
        sim.TryMove(1, Direction.East);
        m1.Held = ItemKind.Detonator;
        sim.DrainEvents();

        sim.TryUseItem(1);
        sim.Tick(0.1); // plant at (9,2)

        Walk(sim, 1, Direction.West, 6, cfg); // (8→2,2), dist=7

        sim.TryUseItem(1); // detonate

        Assert.False(m2.Alive);
        Assert.Equal(DeathCause.Exploded, m2.DeathCause);
    }
}
