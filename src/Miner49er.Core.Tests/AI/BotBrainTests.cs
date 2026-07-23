using System.Linq;
using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class BotBrainTests
{
    private static Simulation MakeSim(TileGrid grid) =>
        new Simulation(grid, new SimConfig());

    // Rail east-west on row 5 (x=2..12); empty cart at (8,5); a squashable slime east of it on the
    // rail at (10,5); bot placed on the push tile (7,5) to shove EAST. Bot miner id = 3.
    private static Simulation MakeCartSquashScenario()
    {
        var grid = new TileGrid(14, 10, TileType.Floor);
        var sim  = MakeSim(grid);
        sim.AddTrack(Enumerable.Range(2, 11).Select(x => new GridPos(x, 5)));
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        sim.AddMiner(3, new GridPos(7, 5));
        return sim;
    }

    // Rail east-west row 5 (x=2..10); empty cart at (8,5); a GoldRock seam just past the rail's east
    // end at (11,5) (bomb payoff, no direct squash); a slime OFF the roll path at (8,8) to satisfy the
    // "monster near" gate without offering a squash. Bot on the perpendicular arm tile (8,4). Id = 3.
    private static Simulation MakeCartBombScenario()
    {
        var grid = new TileGrid(16, 12, TileType.Floor);
        var sim  = MakeSim(grid);
        sim.AddTrack(Enumerable.Range(2, 9).Select(x => new GridPos(x, 5)));
        sim.Grid.Set(new GridPos(11, 5), TileType.GoldRock);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(8, 8), MonsterKind.Slime);
        sim.AddMiner(3, new GridPos(8, 4));
        return sim;
    }

    // Miner squashes: on the push tile, Think returns the shove into the cart.
    [Fact]
    public void Miner_pushes_cart_to_squash_a_monster()
    {
        var sim = MakeCartSquashScenario();
        var brain = new BotBrain(3, BotSkill.Miner, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.Equal((int)Direction.East, act.Dir);
        Assert.False(act.Plant);
    }

    // Miner never arms a bomb (bomb is Dan-only): on the gold-seam scenario it must not Plant.
    [Fact]
    public void Miner_does_not_arm_a_cart_bomb()
    {
        var sim = MakeCartBombScenario();
        var brain = new BotBrain(3, BotSkill.Miner, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.False(act.Plant);
    }

    // Foreman does not arm bombs either.
    [Fact]
    public void Foreman_does_not_arm_a_cart_bomb()
    {
        var sim = MakeCartBombScenario();
        var brain = new BotBrain(3, BotSkill.Foreman, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.False(act.Plant);
    }

    // DynamiteDan arms the cart bomb (Plant) toward the gold seam when no direct squash is available.
    [Fact]
    public void DynamiteDan_arms_a_cart_bomb()
    {
        var sim = MakeCartBombScenario();
        var brain = new BotBrain(3, BotSkill.DynamiteDan, seed: 1);
        var act = brain.Think(sim, GameMode.Expedition);
        Assert.True(act.Plant);
    }

    [Fact]
    public void Greenhorn_returns_valid_action_every_tick()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim  = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 2));
        var brain = new BotBrain(1, BotSkill.Greenhorn, seed: 42);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.InRange(action.Dir, -1, 3);
    }

    [Fact]
    public void Miner_heads_toward_gold_rock()
    {
        // GoldRock at east end; miner at west end; open corridor.
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(4, 1), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 1));
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    [Fact]
    public void Miner_sets_Mine_when_next_step_is_GoldRock()
    {
        // GoldRock immediately east of miner.
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(1, 1), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 1));
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.Equal((int)Direction.East, action.Dir);
        Assert.True(action.Mine);
    }

    [Fact]
    public void Foreman_sets_Use_when_holding_SpeedPotion()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim  = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.GiveItemForTest(1, ItemKind.SpeedPotion);
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.True(action.Use);
    }

    [Fact]
    public void DynamiteDan_sets_Plant_when_adjacent_to_three_GoldRocks()
    {
        // Bot at (1,1); GoldRocks north, east, south.
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.GoldRock);
        grid.Set(new GridPos(2, 1), TileType.GoldRock);
        grid.Set(new GridPos(1, 2), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(1, 1));
        var brain = new BotBrain(1, BotSkill.DynamiteDan, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.True(action.Plant);
    }

    [Fact]
    public void Foreman_does_not_step_into_scree_on_the_way_to_gold()
    {
        // Bot at (0,0). GoldRock target at (2,0). Direct east tile (1,0) is UnstableRock.
        // A detour south exists. Foreman is hazard-aware, so it must not step east into the scree.
        var grid = new TileGrid(3, 2, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.UnstableRock);
        grid.Set(new GridPos(2, 0), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 0));
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.NotEqual((int)Direction.East, action.Dir);
    }

    [Fact]
    public void Foreman_falls_back_through_scree_when_it_is_the_only_route()
    {
        // 3×1 corridor: bot (0,0), gold (2,0), scree (1,0) is the ONLY path.
        // Hazard-aware pass finds nothing; fallback routes east through the scree so the bot isn't frozen.
        var grid = new TileGrid(3, 1, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.ScreeRock);
        grid.Set(new GridPos(2, 0), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 0));
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    [Fact]
    public void Miner_on_open_exit_whistles_once_per_floor()
    {
        // Gold-less floor so the escape opens immediately; bot spawns on the escape tile.
        var grid = new TileGrid(5, 5, TileType.Floor);
        var exit = new GridPos(2, 2);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        sim.AddMiner(1, exit);
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var first = brain.Think(sim, GameMode.Expedition);
        Assert.True(first.Whistle);

        // Standing on the exit again the next tick: no repeat whistle.
        var second = brain.Think(sim, GameMode.Expedition);
        Assert.False(second.Whistle);
    }

    [Fact]
    public void Greenhorn_on_open_exit_does_not_whistle()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var exit = new GridPos(2, 2);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        sim.AddMiner(1, exit);
        var brain = new BotBrain(1, BotSkill.Greenhorn, seed: 0);

        Assert.False(brain.Think(sim, GameMode.Expedition).Whistle);
    }

    [Fact]
    public void Miner_occasionally_listens_when_safe_and_idle()
    {
        // Open floor, no hazards, no gold, not escaping. Over many ticks a Miner+ bot
        // should strike the listen pose at least once (cosmetic idle behaviour).
        var grid = new TileGrid(9, 9, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(4, 4));
        var brain = new BotBrain(1, BotSkill.Miner, seed: 12345);

        bool listenedAtLeastOnce = false;
        for (int i = 0; i < 2000; i++)
            if (brain.Think(sim, GameMode.GoldRush).Listen) { listenedAtLeastOnce = true; break; }

        Assert.True(listenedAtLeastOnce);
    }

    [Fact]
    public void Greenhorn_never_listens()
    {
        var grid = new TileGrid(9, 9, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(4, 4));
        var brain = new BotBrain(1, BotSkill.Greenhorn, seed: 12345);

        for (int i = 0; i < 2000; i++)
            Assert.False(brain.Think(sim, GameMode.GoldRush).Listen);
    }

    // ── LMS hunting ─────────────────────────────────────────────────────────

    // Greenhorn stumbles toward a nearby rival in LMS (within its 8-tile leash),
    // whereas in a non-hunting mode it would wander.
    [Fact]
    public void Greenhorn_hunts_a_nearby_rival_in_LMS()
    {
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 1));  // bot
        sim.AddMiner(2, new GridPos(6, 1));  // rival, 4 tiles east
        var brain = new BotBrain(1, BotSkill.Greenhorn, seed: 7);

        var action = brain.Think(sim, GameMode.LastManStanding);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    // A Miner pursues a rival that's within its 12-tile range.
    [Fact]
    public void Miner_pursues_a_rival_within_range_in_LMS()
    {
        var grid = new TileGrid(15, 3, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(1, 1));   // bot
        sim.AddMiner(2, new GridPos(11, 1));  // rival, 10 tiles east (within 12)
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.LastManStanding);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    // A Miner ignores a rival beyond its 12-tile range and falls back to seeking gold.
    [Fact]
    public void Miner_ignores_a_distant_rival_and_seeks_gold_in_LMS()
    {
        // Bot at (14,1). Rival far west at (0,1) — Chebyshev 14, beyond the 12-tile leash.
        // GoldRock just east at (16,1); the bot should head east toward gold, not west at the rival.
        var grid = new TileGrid(20, 3, TileType.Floor);
        grid.Set(new GridPos(16, 1), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(14, 1));
        sim.AddMiner(2, new GridPos(0, 1));
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.LastManStanding);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    // A Miner throws a stone at a rival within 2 tiles in LMS.
    [Fact]
    public void Miner_throws_a_stone_at_a_close_rival_in_LMS()
    {
        var grid = new TileGrid(9, 3, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 1));  // bot
        sim.AddMiner(2, new GridPos(4, 1));  // rival, 2 tiles east
        sim.AddStones(1, 3);
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.LastManStanding);

        Assert.True(action.Throw);
    }

    // A Foreman tracks a rival on the far side of the map (whole-map targeting) and
    // digs toward it (passRock), stepping in the rival's direction.
    [Fact]
    public void Foreman_hunts_a_far_rival_across_the_map_in_LMS()
    {
        var grid = new TileGrid(30, 3, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(1, 1));    // bot
        sim.AddMiner(2, new GridPos(28, 1));   // rival, 27 tiles east
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.LastManStanding);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    // Dynamite Dan lobs dynamite at a rival standing in its facing line within throw range.
    [Fact]
    public void DynamiteDan_throws_dynamite_at_a_rival_in_line_in_LMS()
    {
        // Bot faces South by default; rival 3 tiles south, clear floor between.
        var grid = new TileGrid(5, 8, TileType.Floor);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 2));  // bot, facing South
        sim.AddMiner(2, new GridPos(2, 5));  // rival, 3 tiles south
        var brain = new BotBrain(1, BotSkill.DynamiteDan, seed: 0);

        var action = brain.Think(sim, GameMode.LastManStanding);

        Assert.True(action.ThrowDynamite);
        Assert.Equal(-1, action.Dir); // stands and lobs
    }

    // In LMS a hunter presses an adjacent rival: it steps into the rival and swings
    // (pickaxe stun). In a non-hunting mode (GoldRush) it never swings at the rival.
    [Fact]
    public void Hunter_swings_at_an_adjacent_rival_only_in_LMS()
    {
        static (int dir, bool mine) Act(GameMode mode)
        {
            var grid = new TileGrid(7, 3, TileType.Floor);
            var sim = new Simulation(grid, new SimConfig());
            sim.AddMiner(1, new GridPos(3, 1));  // bot
            sim.AddMiner(2, new GridPos(4, 1));  // rival, adjacent east
            var brain = new BotBrain(1, BotSkill.Miner, seed: 0);
            var a = brain.Think(sim, mode);
            return (a.Dir, a.Mine);
        }

        var lms = Act(GameMode.LastManStanding);
        Assert.Equal((int)Direction.East, lms.dir); // steps into the rival
        Assert.True(lms.mine);                       // pickaxe stun swing

        // GoldRush is not a hunting mode — the bot never swings its pickaxe at the rival.
        Assert.False(Act(GameMode.GoldRush).mine);
    }
}
