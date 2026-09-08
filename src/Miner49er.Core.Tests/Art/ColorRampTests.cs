using Miner49er.Core;
using Xunit;

public class ColorRampTests
{
    private static ColorRamp ThreeTone() =>
        new(new[] { C64Palette.Black, C64Palette.Orange, C64Palette.Yellow },
            new[] { 0.20f, 0.55f });

    [Fact]
    public void Darkest_luminance_maps_to_first_colour()
    {
        Assert.Equal(C64Palette.Black, ThreeTone().Map(0.0f));
    }

    [Fact]
    public void Brightest_luminance_maps_to_last_colour()
    {
        Assert.Equal(C64Palette.Yellow, ThreeTone().Map(1.0f));
    }

    [Fact]
    public void Value_on_an_edge_takes_the_upper_band()
    {
        Assert.Equal(C64Palette.Orange, ThreeTone().Map(0.20f));
    }

    [Fact]
    public void Identity_is_the_brightest_colour()
    {
        Assert.Equal(C64Palette.Yellow, ThreeTone().Identity);
    }

    [Fact]
    public void Eight_team_ramps_exist()
    {
        Assert.Equal(8, C64Ramps.Teams.Length);
    }

    [Fact]
    public void Team_identity_colours_are_unique()
    {
        var ids = new HashSet<Rgb>();
        foreach (var t in C64Ramps.Teams) Assert.True(ids.Add(t.Identity));
        Assert.Equal(8, ids.Count);
    }

    [Fact]
    public void Every_ramp_is_monotonic_in_luminance()
    {
        foreach (var ramp in C64Ramps.Teams)
            for (int i = 1; i < ramp.Colors.Length; i++)
                Assert.True(ramp.Colors[i - 1].Luminance <= ramp.Colors[i].Luminance);
    }

    [Fact]
    public void Map_only_ever_returns_palette_colours()
    {
        var palette = new HashSet<Rgb>(C64Palette.All);
        foreach (var ramp in C64Ramps.Teams)
            for (float l = 0f; l <= 1f; l += 0.01f)
                Assert.Contains(ramp.Map(l), palette);
    }
}
