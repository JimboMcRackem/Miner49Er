using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SimulationMineEventTests
{
    private static SimConfig Cfg(MineEventKind kind) => new()
    {
        MineEventPool = new[] { kind }, MineEventFirstDelaySeconds = 0.1,
        MineEventMinIntervalSeconds = 300, MineEventMaxIntervalSeconds = 600,
    };

    private static void Run(Simulation sim, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds / 0.1); i++) sim.Tick(0.1);
    }

    private static TileGrid Open(int w = 30, int h = 30) => new(w, h, TileType.Floor);

    // Open floor with rock round the outside, for events that want walls.
    private static TileGrid Cave(int w = 40, int h = 40)
    {
        var grid = new TileGrid(w, h, TileType.Rock);
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                if (x < 20) grid.Set(new GridPos(x, y), TileType.Floor);
        return grid;
    }

    [Fact]
    public void Only_one_event_runs_and_the_same_one_never_repeats_straight_away()
    {
        var cfg = Cfg(MineEventKind.Blackout);
        cfg.MineEventPool = new[] { MineEventKind.Blackout, MineEventKind.Awakening };
        cfg.MineEventMinIntervalSeconds = cfg.MineEventMaxIntervalSeconds = 1;
        var sim = new Simulation(Open(), cfg);
        sim.AddMiner(1, new GridPos(5, 5));

        var seen = new List<MineEventKind>();
        for (int i = 0; i < 3000; i++)
        {
            sim.Tick(0.1);
            if (sim.ActiveEvent != MineEventKind.None && (seen.Count == 0 || seen[^1] != sim.ActiveEvent))
                seen.Add(sim.ActiveEvent);
        }

        Assert.True(seen.Count >= 3);
    }

    [Fact]
    public void Gold_seams_are_only_rolled_where_gold_is_the_goal()
    {
        Assert.Contains(MineEventKind.GoldSeam, MineEvents.PoolFor(GameMode.Expedition));
        Assert.DoesNotContain(MineEventKind.GoldSeam, MineEvents.PoolFor(GameMode.LastManStanding));
    }

    // ---- Blackout ----

    [Fact]
    public void Blackout_shrinks_vision_at_its_height()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.Blackout));
        sim.AddMiner(1, new GridPos(5, 5));

        Run(sim, 8);

        Assert.Equal(sim.Config.BlackoutVisionRadius, sim.EffectiveVisionRadius(1));
    }

    [Fact]
    public void A_held_light_keeps_vision_through_a_blackout()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.Blackout));
        var m = sim.AddMiner(1, new GridPos(5, 5));
        sim.SetMinerHeld(1, ItemKind.CrystalShard);
        int normal = sim.EffectiveVisionRadius(1);

        Run(sim, 8);

        Assert.Equal(normal, sim.EffectiveVisionRadius(1));
    }

    // ---- Firedamp ----

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Events_start_where_the_miner_can_walk_not_behind_solid_rock(int seed)
    {
        var grid = new TileGrid(30, 9, TileType.Floor);
        foreach (var w in grid.Positions())
            if (w.X == 12) grid.Set(w, TileType.Rock);     // sealed wall, open cave beyond
        var cfg = Cfg(MineEventKind.FlashFlood);
        cfg.Seed = seed;
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(8, 4));

        Run(sim, 1);

        Assert.NotEmpty(sim.EventTiles);
        Assert.All(sim.EventTiles, p => Assert.True(p.X < 12));
    }

    [Fact]
    public void Gas_spreads_near_the_miner_and_hurts_nobody_on_its_own()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.Firedamp));
        var m = sim.AddMiner(1, new GridPos(15, 15));

        Run(sim, 1);
        int early = sim.EventTiles.Count;
        Run(sim, sim.Config.FiredampSpreadSeconds);

        Assert.True(sim.EventTiles.Count > early);
        Assert.True(sim.EventTiles.Count > 10);
        Run(sim, sim.Config.FiredampSeconds);
        Assert.True(m.Alive);
        Assert.Equal(MineEventKind.None, sim.ActiveEvent);
    }

    [Fact]
    public void An_explosion_touching_the_gas_kills_everyone_in_the_cloud()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.Firedamp));
        sim.AddMiner(1, new GridPos(15, 15));
        Run(sim, sim.Config.FiredampSpreadSeconds + 0.5);

        var cloud = sim.EventTiles;
        var victim = sim.AddMiner(2, cloud[^1]);
        var monster = sim.AddMonster(1, cloud[^2], MonsterKind.Slime);
        sim.AddMiner(3, cloud[0]);
        sim.TryThrowDynamite(3);
        Run(sim, 3);

        Assert.False(victim.Alive);
        Assert.Equal(DeathCause.Exploded, victim.DeathCause);
        Assert.False(monster.Alive);
        Assert.Equal(MineEventKind.None, sim.ActiveEvent);
    }

    [Fact]
    public void Lava_beside_the_gas_sets_it_off()
    {
        var grid = Open();
        foreach (var p in grid.Positions())
            if (p.X == 0 || p.X == 29) grid.Set(p, TileType.Lava);
        var cfg = Cfg(MineEventKind.Firedamp);
        cfg.FiredampTiles = 900;
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(15, 15));
        sim.DrainEvents();

        Run(sim, cfg.FiredampSeconds);

        Assert.Contains(sim.DrainEvents(), e => e is Explosion);
    }

    // ---- Flash flood ----

    [Fact]
    public void Flood_marks_the_area_before_any_water_arrives()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.FlashFlood));
        sim.AddMiner(1, new GridPos(15, 15));

        Run(sim, sim.Config.FlashFloodWarningSeconds - 0.5);

        Assert.NotEmpty(sim.EventTiles);
        Assert.All(sim.EventTiles, p => Assert.Equal(TileType.Floor, sim.Grid.Get(p)));
    }

    [Fact]
    public void Flood_drowns_those_who_stayed_then_drains_completely()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.FlashFlood));
        sim.AddMiner(1, new GridPos(15, 15));
        Run(sim, 1);
        var area = sim.EventTiles.ToList();
        var stayed = sim.AddMiner(2, area[0]);

        Run(sim, sim.Config.FlashFloodWarningSeconds + 3);
        Assert.False(stayed.Alive);
        Assert.Equal(DeathCause.Drowned, stayed.DeathCause);

        Run(sim, sim.Config.FlashFloodSeconds);
        Assert.Equal(MineEventKind.None, sim.ActiveEvent);
        Assert.All(area, p => Assert.Equal(TileType.Floor, sim.Grid.Get(p)));
    }

    [Fact]
    public void Flood_leaves_items_the_exit_and_spawns_dry()
    {
        var grid = Open(12, 12);
        var cfg = Cfg(MineEventKind.FlashFlood);
        cfg.FlashFloodTiles = 144;
        var sim = new Simulation(grid, cfg, escapeTile: new GridPos(1, 1));
        sim.AddItem(new Item(new GridPos(9, 9), ItemKind.Lantern, ItemPlacement.Loose));
        var spawn = new GridPos(5, 5);
        sim.AddMiner(1, spawn);

        Run(sim, cfg.FlashFloodWarningSeconds + cfg.FlashFloodSeconds / 2);

        Assert.Equal(TileType.Floor, grid.Get(new GridPos(9, 9)));
        Assert.Equal(TileType.Floor, grid.Get(new GridPos(1, 1)));
        Assert.Equal(TileType.Floor, grid.Get(spawn));
    }

    // ---- Awakening ----

    [Fact]
    public void Awakening_rouses_dormant_skeletons_at_once()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.Awakening));
        sim.AddMiner(1, new GridPos(2, 2));
        var skeleton = sim.AddMonster(1, new GridPos(25, 25), MonsterKind.SkeletonHuman);
        skeleton.Dormant = true;

        Run(sim, 0.3);

        Assert.False(skeleton.Dormant);
    }

    [Fact]
    public void Awakened_monsters_sense_miners_from_far_beyond_their_usual_range()
    {
        var sim = new Simulation(Open(40, 5), Cfg(MineEventKind.Awakening));
        sim.AddMiner(1, new GridPos(20, 2));
        var slime = sim.AddMonster(1, new GridPos(2, 2), MonsterKind.Slime);
        Run(sim, 0.3);

        int before = slime.Pos.X;
        Run(sim, 6);

        Assert.True(slime.Pos.X > before + 3);
    }

    // ---- Gold seam ----

    [Fact]
    public void Seam_opens_in_the_rock_within_reach_and_closes_if_left_alone()
    {
        var grid = Cave();
        var sim = new Simulation(grid, Cfg(MineEventKind.GoldSeam));
        var m = sim.AddMiner(1, new GridPos(5, 20));
        double before = sim.GoldCollectedFraction;

        Run(sim, 0.3);
        var seam = sim.EventTiles.ToList();
        Assert.NotEmpty(seam);
        Assert.All(seam, p => Assert.Equal(TileType.GoldRock, grid.Get(p)));

        Run(sim, sim.Config.GoldSeamSeconds);
        Assert.All(seam, p => Assert.Equal(TileType.Rock, grid.Get(p)));
        Assert.Equal(before, sim.GoldCollectedFraction);
    }

    // ---- Ghost shift ----

    [Fact]
    public void Phantoms_walk_to_buried_treasure_and_harm_nobody()
    {
        var grid = Cave();
        var treasure = new GridPos(30, 20);
        var sim = new Simulation(grid, Cfg(MineEventKind.GhostShift));
        sim.AddItem(new Item(treasure, ItemKind.BiggerBlast, ItemPlacement.Buried));
        var m = sim.AddMiner(1, new GridPos(15, 20));

        Run(sim, 0.3);
        Assert.NotEmpty(sim.Phantoms);
        Run(sim, sim.Config.GhostShiftSeconds - 1);

        Assert.All(sim.Phantoms, p => Assert.Equal(treasure, p));
        Assert.True(m.Alive);
    }

    // ---- Network ----

    [Fact]
    public void Event_state_round_trips_through_the_snapshot_codec()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.GhostShift));
        sim.AddMiner(1, new GridPos(15, 15));
        Run(sim, 1);

        var bytes = SnapshotCodec.Write(new TickUpdate(SnapshotFactory.Capture(sim, tick: 0),
            System.Array.Empty<TileChange>()));
        var read = SnapshotCodec.Read(bytes).Snapshot;

        Assert.Equal(MineEventKind.GhostShift, read.MineEvent);
        Assert.Equal((float)sim.EventIntensity, read.MineEventIntensity);
        Assert.Equal(sim.Phantoms.Select(p => new TileSnapshot(p.X, p.Y)), read.Phantoms);
    }

    [Fact]
    public void Marked_tiles_round_trip_through_the_snapshot_codec()
    {
        var sim = new Simulation(Open(), Cfg(MineEventKind.FlashFlood));
        sim.AddMiner(1, new GridPos(15, 15));
        Run(sim, 1);

        var bytes = SnapshotCodec.Write(new TickUpdate(SnapshotFactory.Capture(sim, tick: 0),
            System.Array.Empty<TileChange>()));

        Assert.Equal(sim.EventTiles.Select(p => new TileSnapshot(p.X, p.Y)),
            SnapshotCodec.Read(bytes).Snapshot.EventTiles);
    }
}
