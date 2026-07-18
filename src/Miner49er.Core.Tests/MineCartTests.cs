using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class MineCartTests
{
    // Horizontal Floor grid with a straight rail along row y for x in [x0, x1].
    private static Simulation RailSim(int x0, int x1, int y, int w = 10, int h = 6)
    {
        var grid = new TileGrid(w, h, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddTrack(Enumerable.Range(x0, x1 - x0 + 1).Select(x => new GridPos(x, y)));
        return sim;
    }

    private static GridPos CartPos(Simulation sim, int id) => sim.Carts.First(c => c.Id == id).Pos;

    [Fact]
    public void Push_RollsCartAlongRail_UntilTrackEnd()
    {
        var sim = RailSim(1, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMiner(1, new GridPos(2, 2));

        Assert.True(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(6, 2), CartPos(sim, 1));       // rolled to the last rail tile
        Assert.Equal(new GridPos(3, 2), sim.GetMiner(1).Pos);   // miner took the cart's old tile
    }

    [Fact]
    public void Push_OffRailDirection_CartImmovable_MinerBlocked()
    {
        var sim = RailSim(3, 3, 2);                              // a single-tile rail: no onward track
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMiner(1, new GridPos(2, 2));

        Assert.False(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(3, 2), CartPos(sim, 1));       // cart didn't move
        Assert.Equal(new GridPos(2, 2), sim.GetMiner(1).Pos);   // miner blocked
    }

    [Fact]
    public void Push_SquashesMonster_AndRollsThrough()
    {
        var sim = RailSim(1, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMonster(9, new GridPos(4, 2), MonsterKind.Slime);
        sim.AddMiner(1, new GridPos(2, 2));

        sim.TryMove(1, Direction.East);
        Assert.False(sim.Monsters.First(m => m.Id == 9).Alive);
        Assert.Equal(new GridPos(6, 2), CartPos(sim, 1));        // cart rolled past the squashed slime
    }

    [Fact]
    public void Push_IgnoresGhost_RollsThrough()
    {
        var sim = RailSim(1, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMonster(9, new GridPos(4, 2), MonsterKind.Ghost);
        sim.AddMiner(1, new GridPos(2, 2));

        sim.TryMove(1, Direction.East);
        Assert.True(sim.Monsters.First(m => m.Id == 9).Alive);   // ghost unharmed
        Assert.Equal(new GridPos(6, 2), CartPos(sim, 1));
    }

    [Fact]
    public void Push_ShovesMiner_AlongRail()
    {
        var sim = RailSim(1, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMiner(1, new GridPos(2, 2));   // pusher
        sim.AddMiner(2, new GridPos(4, 2));   // victim, ahead of the cart

        sim.TryMove(1, Direction.East);
        var victim = sim.GetMiner(2);
        Assert.True(victim.Alive);
        Assert.True(victim.Pos.X > 4);        // shoved forward
    }

    [Fact]
    public void Push_CrushesMiner_WhenPinnedAgainstWall()
    {
        var sim = RailSim(1, 4, 2);
        sim.Grid.Set(new GridPos(5, 2), TileType.Rock);         // wall pinning the victim
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMiner(1, new GridPos(2, 2));   // pusher
        sim.AddMiner(2, new GridPos(4, 2));   // victim, back to the wall

        sim.TryMove(1, Direction.East);
        var victim = sim.GetMiner(2);
        Assert.False(victim.Alive);
        Assert.Equal(DeathCause.Crushed, victim.DeathCause);
        Assert.Equal(new GridPos(4, 2), CartPos(sim, 1));       // cart stops on the crushed miner's tile
    }

    [Fact]
    public void Push_ShovesMinerIntoLava_HazardDeath()
    {
        var sim = RailSim(1, 4, 2);
        sim.Grid.Set(new GridPos(5, 2), TileType.Lava);        // lethal tile beyond the victim (off-rail)
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddMiner(2, new GridPos(4, 2));

        sim.TryMove(1, Direction.East);
        var victim = sim.GetMiner(2);
        Assert.False(victim.Alive);
        Assert.Equal(DeathCause.Burned, victim.DeathCause);
    }

    [Fact]
    public void Push_CartIntoCart_FormsTrain()
    {
        var sim = RailSim(1, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));  // lead pusher-side
        sim.AddCart(new CartSpec(2, new GridPos(4, 2), Direction.East));  // ahead
        sim.AddMiner(1, new GridPos(2, 2));

        sim.TryMove(1, Direction.East);
        Assert.True(CartPos(sim, 1).X > 3);   // both advanced (a train)
        Assert.True(CartPos(sim, 2).X > 4);
        Assert.True(CartPos(sim, 2).X > CartPos(sim, 1).X); // lead cart stays ahead
    }

    [Fact]
    public void Push_IntoLavaRail_DerailsAndDestroys()
    {
        var sim = RailSim(1, 6, 2);
        sim.Grid.Set(new GridPos(5, 2), TileType.Lava);        // a flooded/lava-crept rail tile
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMiner(1, new GridPos(2, 2));

        sim.TryMove(1, Direction.East);
        Assert.Empty(sim.Carts);                               // derailed & destroyed
        Assert.Equal(new GridPos(3, 2), sim.GetMiner(1).Pos);  // miner advanced into the vacated tile
    }

    [Fact]
    public void Monster_CannotEnterCartTile()
    {
        // 1-wide corridor: Rock everywhere except row y=2. Cart blocks the slime's only path.
        var grid = new TileGrid(8, 5, TileType.Rock);
        for (int x = 1; x <= 6; x++) grid.Set(new GridPos(x, 2), TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddTrack(Enumerable.Range(1, 6).Select(x => new GridPos(x, 2)));
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        sim.AddMonster(9, new GridPos(2, 2), MonsterKind.Slime);
        sim.AddMiner(1, new GridPos(5, 2));   // bait ahead of the cart

        for (int i = 0; i < 20; i++) sim.Tick(0.5);
        Assert.NotEqual(new GridPos(3, 2), sim.Monsters.First(m => m.Id == 9).Pos); // never onto the cart
    }

    private static MapConfig RailConfig(int seed) => new() { Seed = seed, MineCarts = true };

    [Fact]
    public void MineCarts_ProduceStraightFloorRail_WithCartsOnIt()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            var map = MapGenerator.Generate(RailConfig(seed));
            if (map.TrackTiles.Count == 0) continue;
            bool sameRow = map.TrackTiles.All(t => t.Y == map.TrackTiles[0].Y);
            bool sameCol = map.TrackTiles.All(t => t.X == map.TrackTiles[0].X);
            Assert.True(sameRow || sameCol);                          // straight
            foreach (var t in map.TrackTiles) Assert.Equal(TileType.Floor, map.Grid.Get(t));
            Assert.NotEmpty(map.Carts);
            var trackSet = map.TrackTiles.ToHashSet();
            foreach (var c in map.Carts) Assert.Contains(c.Pos, trackSet); // carts sit on the rail
            return;
        }
        Assert.Fail("No rail generated in 300 seeds");
    }

    [Fact]
    public void MineCarts_Generation_IsDeterministic()
    {
        var a = MapGenerator.Generate(RailConfig(42));
        var b = MapGenerator.Generate(RailConfig(42));
        Assert.Equal(a.TrackTiles, b.TrackTiles);
        Assert.Equal(a.Carts, b.Carts);
    }

    [Fact]
    public void MineCartsDisabled_ProducesNoRail()
    {
        Assert.Empty(MapGenerator.Generate(new MapConfig { Seed = 42, MineCarts = false }).TrackTiles);
    }

    [Fact]
    public void BotPathfinder_BlockedTile_SealsCorridor()
    {
        var grid = new TileGrid(5, 5, TileType.Rock);
        for (int x = 0; x < 5; x++) grid.Set(new GridPos(x, 2), TileType.Floor);
        var from = new GridPos(0, 2); var to = new GridPos(4, 2);
        Assert.NotEqual(-1, BotPathfinder.NextDir(grid, from, to, passRock: false)); // open corridor
        var blocked = new HashSet<GridPos> { new GridPos(2, 2) };
        Assert.Equal(-1, BotPathfinder.NextDir(grid, from, to, passRock: false, blocked: blocked)); // cart seals it
    }

    [Fact]
    public void BotPathfinder_DetoursAroundCart_InOpenRoom()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var from = new GridPos(0, 2); var to = new GridPos(4, 2);
        var blocked = new HashSet<GridPos> { new GridPos(2, 2) };
        int dir = BotPathfinder.NextDir(grid, from, to, passRock: false, blocked: blocked);
        Assert.NotEqual(-1, dir);                                   // still reachable via detour
        var off = ((Direction)dir).ToOffset();
        Assert.NotEqual(new GridPos(2, 2), new GridPos(from.X + off.X, from.Y + off.Y)); // first step avoids the cart
    }
}
