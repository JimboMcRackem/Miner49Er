using Miner49er.Core;
using Xunit;

public class MinerTintTests
{
    private static ColorRamp Gold => C64Ramps.Teams[0];

    [Fact]
    public void Hardhat_yellow_is_preserved_not_tinted()
    {
        // Warm hue, saturated, bright: the hard hat and skin must survive tinting.
        var hardhat = new Rgb(0xED, 0xC3, 0x4A);
        Assert.True(MinerTint.IsHuePreserved(hardhat));
        Assert.Equal(hardhat, MinerTint.Apply(hardhat, C64Ramps.Teams[1]));
    }

    [Fact]
    public void Dark_clothing_takes_the_team_colour()
    {
        var overalls = new Rgb(0x30, 0x30, 0x38);
        Assert.False(MinerTint.IsHuePreserved(overalls));
        Assert.Equal(Gold.Map(overalls.Luminance), MinerTint.Apply(overalls, Gold));
    }

    [Fact]
    public void Cool_hues_are_never_preserved()
    {
        Assert.False(MinerTint.IsHuePreserved(new Rgb(0x40, 0x90, 0xE0)));
    }

    [Fact]
    public void Dark_warm_pixels_are_not_preserved()
    {
        // Warm and saturated but too dark - this is shadowed cloth, not skin.
        Assert.False(MinerTint.IsHuePreserved(new Rgb(0x40, 0x28, 0x10)));
    }

    [Fact]
    public void Output_is_always_in_palette_for_every_team()
    {
        var palette = new HashSet<Rgb>(C64Palette.All);
        foreach (var ramp in C64Ramps.Teams)
            for (int v = 0; v < 256; v += 5)
            {
                var result = MinerTint.Apply(new Rgb(v, v, v), ramp);
                Assert.Contains(result, palette);
            }
    }

    [Fact]
    public void Tinting_is_deterministic()
    {
        var px = new Rgb(0x55, 0x55, 0x60);
        Assert.Equal(MinerTint.Apply(px, Gold), MinerTint.Apply(px, Gold));
    }
}
