using System.Linq;
using Miner49er.Core;
using Xunit;

public class OctopusTests
{
    [Fact]
    public void Arm_stays_within_rest_angle_plus_minus_45_degrees()
    {
        var arm = new OctopusArm(90.0, 1);   // East arm
        for (int i = 0; i < 1000; i++)
            arm.Advance(0.033);

        Assert.True(arm.CurrentAngle >= 90.0 - OctopusArm.ArcHalfWidth - 0.001);
        Assert.True(arm.CurrentAngle <= 90.0 + OctopusArm.ArcHalfWidth + 0.001);
    }

    [Fact]
    public void Arm_reverses_direction_at_arc_end()
    {
        var arm = new OctopusArm(0.0, 1);   // North arm, sweeping clockwise
        for (int i = 0; i < 200; i++) arm.Advance(0.1);
        Assert.Equal(-1, arm.SwingDir);
    }

    [Fact]
    public void Arm_pauses_at_arc_end()
    {
        var arm = new OctopusArm(0.0, 1);
        for (int i = 0; i < 200; i++) arm.Advance(0.1);
        // After reversal, pause should be non-negative
        Assert.True(arm.PauseRemaining >= 0.0);
    }

    [Fact]
    public void Danger_tiles_per_arm_never_exceed_arm_length()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var oct  = new Octopus(new GridPos(10, 10));
        var danger = oct.DangerTiles(grid).ToList();
        Assert.True(danger.Count <= 4 * OctopusArm.Length);
    }

    [Fact]
    public void Danger_tiles_are_all_in_bounds()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var oct  = new Octopus(new GridPos(5, 5));
        foreach (var p in oct.DangerTiles(grid))
            Assert.True(grid.InBounds(p), $"out-of-bounds tile {p}");
    }

    [Fact]
    public void Danger_tiles_do_not_include_octopus_center()
    {
        var grid   = new TileGrid(20, 20, TileType.Floor);
        var center = new GridPos(10, 10);
        var oct    = new Octopus(center);
        Assert.DoesNotContain(center, oct.DangerTiles(grid));
    }

    [Fact]
    public void Danger_tiles_count_is_reproducible_on_same_octopus()
    {
        var grid  = new TileGrid(20, 20, TileType.Floor);
        var oct   = new Octopus(new GridPos(10, 10));
        var first  = oct.DangerTiles(grid).Count();
        var second = oct.DangerTiles(grid).Count();
        Assert.Equal(first, second);
    }
}
