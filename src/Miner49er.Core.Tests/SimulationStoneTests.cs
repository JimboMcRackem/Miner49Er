using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationStoneTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    // --- throw mechanics ---

    [Fact]
    public void TryThrowStone_with_no_stones_is_noop()
    {
        var sim = Sim(new TileGrid(7, 3, TileType.Floor));
        sim.AddMiner(1, new GridPos(2, 1));
        sim.TryThrowStone(1);
        Assert.Equal(0, sim.GetMiner(1).StoneCount);
        Assert.Empty(sim.DrainEvents().OfType<StoneThrown>());
    }

    [Fact]
    public void TryThrowStone_facing_east_lands_before_wall()
    {
        // 7-wide: miner at col 1, wall at col 5. Stone should land at col 4.
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(5, 1), TileType.Rock);
        var cfg = new SimConfig { BaseMoveSeconds = 0.01 };
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);  // sets facing East, moves to col 2
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);           // throw from col 2 facing East

        var thrown = sim.DrainEvents().OfType<StoneThrown>().Single();
        Assert.Equal(new GridPos(4, 1), thrown.LandingPos);
        Assert.Equal(0, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void TryThrowStone_stops_at_map_boundary()
    {
        // 6-wide row: miner at col 0, no walls. East boundary is col 5.
        var grid = new TileGrid(6, 3, TileType.Floor);
        var cfg = new SimConfig { BaseMoveSeconds = 0.01 };
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        sim.TryMove(1, Direction.East);  // move to col 1, facing East
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);           // from col 1 facing East

        var thrown = sim.DrainEvents().OfType<StoneThrown>().Single();
        Assert.Equal(5, thrown.LandingPos.X);  // col 5 = last valid tile
    }

    [Fact]
    public void TryThrowStone_decrements_StoneCount()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        var cfg = new SimConfig { BaseMoveSeconds = 0.01 };
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.AddStones(1, 3);
        sim.TryThrowStone(1);
        Assert.Equal(2, sim.GetMiner(1).StoneCount);
    }

    // --- noise source distraction ---

    [Fact]
    public void Slime_moves_toward_noise_source_not_player()
    {
        // Grid 13 wide. Miner at col 0, slime at col 5, noise will land at col 12.
        // Slime should step East (toward noise) not West (toward miner).
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 12, BaseMoveSeconds = 0.01 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var slime = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Slime);
        // Place miner at col 1 facing East, throw stone to col 12 (boundary)
        sim.TryMove(1, Direction.East);   // miner → col 1, facing East
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);            // noise at col 12

        sim.Tick(0.1);
        Assert.Equal(new GridPos(6, 1), slime.Pos);  // moved East
    }

    [Fact]
    public void Noise_source_expires_and_slime_chases_player_again()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 12, BaseMoveSeconds = 0.01 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var slime = sim.AddMonster(1, new GridPos(6, 1), MonsterKind.Slime);
        sim.TryMove(1, Direction.East);  // miner → col 1
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);           // noise at col 12

        sim.Tick(4.2);  // noise expires after 4s, slime keeps moving East for ~42 steps

        // After expiry slime chases miner (col 1), so next step should go West
        var xAfterExpiry = slime.Pos.X;
        sim.Tick(0.1);
        Assert.True(slime.Pos.X < xAfterExpiry);  // moved West toward player
    }

    [Fact]
    public void Ghost_targets_noise_source_when_within_sense_radius()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1, MonsterSenseRadius = 12, BaseMoveSeconds = 0.01 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var ghost = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Ghost);
        sim.TryMove(1, Direction.East);
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);  // noise at col 12

        sim.Tick(0.1);
        Assert.Equal(new GridPos(6, 1), ghost.Pos);  // moved East
    }

    [Fact]
    public void Goat_reorients_charge_toward_noise_source()
    {
        // Goat at col 5, miner at col 0. Noise at col 12. Goat should charge East.
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.1, MonsterSenseRadius = 12, BaseMoveSeconds = 0.01 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var goat = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Goat);
        sim.TryMove(1, Direction.East);
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);  // noise at col 12

        sim.Tick(0.1);
        Assert.Equal(new GridPos(6, 1), goat.Pos);  // moved East toward noise
    }
}
