using Miner49er.Core;
using Xunit;

public class MinerTintTests
{
    private static ColorRamp Gold => C64Ramps.Teams[0];
    private static ColorRamp Blue => C64Ramps.Teams[1];

    // Source sprites are guaranteed to hold only palette colours, and the flesh ramp
    // puts skin on lightred and the hard hat on yellow. Preservation is therefore an
    // exact set membership test, not an HSV heuristic. The heuristic it replaced was
    // calibrated on the pre-conversion art and failed on both colours at once:
    // lightred's hue wraps to 0.991 and yellow sits at 0.172, so a `hue < 0.14` test
    // rejected them and the entire miner took the team colour, skin included.

    [Fact]
    public void Skin_is_preserved_for_every_team()
    {
        Assert.True(MinerTint.IsHuePreserved(C64Palette.LightRed));
        foreach (var ramp in C64Ramps.Teams)
            Assert.Equal(C64Palette.LightRed, MinerTint.Apply(C64Palette.LightRed, ramp));
    }

    [Fact]
    public void Hardhat_is_preserved_for_every_team()
    {
        Assert.True(MinerTint.IsHuePreserved(C64Palette.Yellow));
        foreach (var ramp in C64Ramps.Teams)
            Assert.Equal(C64Palette.Yellow, MinerTint.Apply(C64Palette.Yellow, ramp));
    }

    [Fact]
    public void Trousers_take_the_team_colour()
    {
        Assert.False(MinerTint.IsHuePreserved(C64Palette.Brown));
        Assert.Equal(Blue.Map(C64Palette.Brown.Luminance),
                     MinerTint.Apply(C64Palette.Brown, Blue));
    }

    [Fact]
    public void Shirt_takes_the_team_colour()
    {
        Assert.False(MinerTint.IsHuePreserved(C64Palette.White));
        Assert.Equal(Blue.Map(C64Palette.White.Luminance),
                     MinerTint.Apply(C64Palette.White, Blue));
    }

    [Fact]
    public void Outline_stays_black()
    {
        Assert.Equal(C64Palette.Black, MinerTint.Apply(C64Palette.Black, Blue));
    }

    [Fact]
    public void A_blue_team_miner_keeps_skin_and_hat_but_wears_blue()
    {
        // The exact bug reported from playtest: the miner rendered entirely blue.
        var ramp = Blue;
        Assert.Equal(C64Palette.LightRed, MinerTint.Apply(C64Palette.LightRed, ramp)); // skin
        Assert.Equal(C64Palette.Yellow, MinerTint.Apply(C64Palette.Yellow, ramp));     // hat
        Assert.NotEqual(C64Palette.LightRed, MinerTint.Apply(C64Palette.Brown, ramp)); // trousers
        Assert.NotEqual(C64Palette.White, MinerTint.Apply(C64Palette.White, ramp));    // shirt
    }

    [Fact]
    public void Every_team_leaves_at_least_one_non_team_colour_on_the_miner()
    {
        // Guards the regression class: a miner must never be a single flat silhouette.
        foreach (var ramp in C64Ramps.Teams)
        {
            var painted = new[]
            {
                MinerTint.Apply(C64Palette.Black, ramp),
                MinerTint.Apply(C64Palette.Brown, ramp),
                MinerTint.Apply(C64Palette.LightRed, ramp),
                MinerTint.Apply(C64Palette.Yellow, ramp),
                MinerTint.Apply(C64Palette.White, ramp),
            };
            Assert.Contains(C64Palette.LightRed, painted);
            Assert.Contains(C64Palette.Yellow, painted);
        }
    }

    [Fact]
    public void Output_is_always_in_palette_for_every_team()
    {
        var palette = new HashSet<Rgb>(C64Palette.All);
        foreach (var ramp in C64Ramps.Teams)
            foreach (var src in C64Palette.All)
                Assert.Contains(MinerTint.Apply(src, ramp), palette);
    }

    [Fact]
    public void Preserved_set_comes_from_the_ramp_data()
    {
        Assert.Equal(2, C64Ramps.TintPreserve.Length);
        Assert.Contains(C64Palette.LightRed, C64Ramps.TintPreserve);
        Assert.Contains(C64Palette.Yellow, C64Ramps.TintPreserve);
    }

    [Fact]
    public void Tinting_is_deterministic()
    {
        Assert.Equal(MinerTint.Apply(C64Palette.Brown, Gold),
                     MinerTint.Apply(C64Palette.Brown, Gold));
    }
}
