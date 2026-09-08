using System;
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

    [Fact]
    public void No_team_ramp_emits_a_preserved_identity_colour()
    {
        // A team whose shirt resolves to yellow or lightred would merge into the
        // miner's own hard hat or skin. Gold and Pink both did before re-assignment.
        var reserved = new HashSet<Rgb>(C64Ramps.TintPreserve);
        foreach (var ramp in C64Ramps.Teams)
            foreach (var c in ramp.Colors)
                Assert.DoesNotContain(c, reserved);
    }

    [Fact]
    public void Team_identity_is_the_band_that_paints_most_of_the_sprite()
    {
        // The middle band paints trousers and torso (~48% of a miner's pixels); the top
        // band is a ~4% shirt highlight. Keying identity off the brightest colour made
        // four of the eight teams read as grey.
        foreach (var ramp in C64Ramps.Teams)
            Assert.Equal(ramp.Colors[1], ramp.Identity);
    }

    [Fact]
    public void A_custom_identity_index_overrides_the_brightest_default()
    {
        var ramp = new ColorRamp(
            new[] { C64Palette.Black, C64Palette.Green, C64Palette.White },
            new[] { 0.2f, 0.8f }, identity: 1);
        Assert.Equal(C64Palette.Green, ramp.Identity);
        Assert.Equal(C64Palette.White, ramp.Map(1f));
    }

    [Fact]
    public void An_identity_index_past_the_end_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorRamp(
            new[] { C64Palette.Black, C64Palette.White }, new[] { 0.5f }, identity: 2));
    }
}
