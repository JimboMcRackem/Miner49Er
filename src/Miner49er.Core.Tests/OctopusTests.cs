using System.Collections.Generic;
using Miner49er.Core;
using Xunit;

public class OctopusTests
{
    [Fact]
    public void Octopus_does_not_move_before_cooldown_expires()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var oct  = new Octopus(new GridPos(5, 5));
        var startPos = oct.Pos;

        oct.Advance(0.01, grid, System.Array.Empty<Miner>());

        Assert.Equal(startPos, oct.Pos);
    }

    [Fact]
    public void Octopus_moves_toward_miner_after_cooldown()
    {
        var grid  = new TileGrid(20, 20, TileType.Floor);
        var oct   = new Octopus(new GridPos(5, 5));
        var miner = new Miner(1, new GridPos(5, 10));

        oct.Advance(Octopus.LandCooldown + 0.01, grid, new[] { miner });

        Assert.NotEqual(new GridPos(5, 5), oct.Pos);
        Assert.Equal(5, oct.Pos.X);
        Assert.Equal(6, oct.Pos.Y); // stepped south toward miner
    }

    [Fact]
    public void Octopus_moves_faster_in_deep_water()
    {
        var grid = new TileGrid(20, 20, TileType.DeepWater);
        var oct  = new Octopus(new GridPos(5, 5));

        Assert.True(Octopus.DeepCooldown < Octopus.LandCooldown);
        Assert.Equal(Octopus.DeepCooldown, Octopus.CooldownFor(TileType.DeepWater));
    }

    [Fact]
    public void Octopus_cannot_move_into_impermeable_rock()
    {
        var grid = new TileGrid(5, 5, TileType.ImpermeableRock);
        grid.Set(new GridPos(2, 2), TileType.Floor);
        var oct   = new Octopus(new GridPos(2, 2));
        var miner = new Miner(1, new GridPos(2, 4)); // blocked by rock at (2,3)

        oct.Advance(Octopus.LandCooldown + 0.01, grid, new[] { miner });

        Assert.Equal(new GridPos(2, 2), oct.Pos); // couldn't move — rock in the way
    }

    [Fact]
    public void Octopus_stays_within_grid_bounds()
    {
        var grid  = new TileGrid(10, 10, TileType.Floor);
        var oct   = new Octopus(new GridPos(5, 5));
        var miner = new Miner(1, new GridPos(5, 5));

        for (int i = 0; i < 50; i++)
            oct.Advance(Octopus.LandCooldown + 0.01, grid, new[] { miner });

        Assert.True(grid.InBounds(oct.Pos));
    }
}
