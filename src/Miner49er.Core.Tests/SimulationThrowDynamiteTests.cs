using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationThrowDynamiteTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig { BaseMoveSeconds = 0.01 });

    [Fact]
    public void TryThrowDynamite_disabled_is_noop()
    {
        var sim = Sim(new TileGrid(9, 3, TileType.Floor),
            new SimConfig { BaseMoveSeconds = 0.01, DynamiteEnabled = false });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.TryThrowDynamite(1);

        Assert.Empty(sim.Charges);
        Assert.Empty(sim.DrainEvents().OfType<DynamiteThrown>());
    }

    [Fact]
    public void TryThrowDynamite_lands_on_last_open_tile_before_wall()
    {
        // Miner throws from col 2 facing East; wall at col 5 → lands at col 4.
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(5, 1), TileType.Rock);
        var sim = Sim(grid);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // → col 2, facing East
        sim.TryThrowDynamite(1);

        var thrown = sim.DrainEvents().OfType<DynamiteThrown>().Single();
        Assert.Equal(new GridPos(4, 1), thrown.LandingPos);
        Assert.Single(sim.Charges);
    }

    [Fact]
    public void TryThrowDynamite_is_capped_at_range()
    {
        // Open row, range 5: from col 2 facing East → lands at col 7.
        var grid = new TileGrid(14, 3, TileType.Floor);
        var sim = Sim(grid, new SimConfig { BaseMoveSeconds = 0.01, ThrownDynamiteRange = 5 });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // → col 2
        sim.TryThrowDynamite(1);

        var thrown = sim.DrainEvents().OfType<DynamiteThrown>().Single();
        Assert.Equal(new GridPos(7, 1), thrown.LandingPos);
    }

    [Fact]
    public void TryThrowDynamite_into_adjacent_wall_lands_at_feet()
    {
        // Wall directly ahead → the stick lands on the thrower's own tile.
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);
        var sim = Sim(grid);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // → col 2, wall at col 3 ahead
        sim.TryThrowDynamite(1);

        var thrown = sim.DrainEvents().OfType<DynamiteThrown>().Single();
        Assert.Equal(new GridPos(2, 1), thrown.LandingPos);
    }

    [Fact]
    public void TryThrowDynamite_is_cooldown_gated()
    {
        var sim = Sim(new TileGrid(14, 3, TileType.Floor));
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.TryThrowDynamite(1);
        sim.TryThrowDynamite(1); // still on cooldown → ignored

        Assert.Single(sim.Charges);
    }

    [Fact]
    public void Thrown_dynamite_detonates_after_fuse_and_kills_nearby_miner()
    {
        // Miner 1 throws from col 2; wall at col 5 lands the stick on col 4, where
        // rival miner 2 stands. After the fuse it detonates and kills the rival.
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(5, 1), TileType.Rock);
        var sim = Sim(grid, new SimConfig { BaseMoveSeconds = 0.01, ThrownDynamiteFuseSeconds = 1.0 });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMiner(2, new GridPos(4, 1));
        sim.TryMove(1, Direction.East); // → col 2 facing East
        sim.TryThrowDynamite(1);

        Assert.True(sim.GetMiner(2).Alive);
        sim.Tick(1.1); // fuse expires → blast

        Assert.False(sim.GetMiner(2).Alive);
        Assert.Equal(DeathCause.Exploded, sim.GetMiner(2).DeathCause);
        Assert.True(sim.GetMiner(1).Alive); // thrower at col 2 is outside the kill radius
    }
}
