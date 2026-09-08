using Miner49er.Core;
using Xunit;

public class RgbTests
{
    [Fact]
    public void Black_luminance_is_zero()
    {
        Assert.Equal(0f, new Rgb(0, 0, 0).Luminance, 5);
    }

    [Fact]
    public void White_luminance_is_one()
    {
        Assert.Equal(1f, new Rgb(255, 255, 255).Luminance, 5);
    }

    [Fact]
    public void Grey_has_zero_saturation()
    {
        Assert.Equal(0f, new Rgb(123, 123, 123).Saturation, 5);
    }

    [Fact]
    public void Pure_red_hue_is_zero()
    {
        Assert.Equal(0f, new Rgb(255, 0, 0).Hue, 5);
    }

    [Fact]
    public void Palette_has_sixteen_colors()
    {
        Assert.Equal(16, C64Palette.All.Length);
    }

    [Fact]
    public void Palette_yellow_matches_pepto()
    {
        Assert.Equal(new Rgb(0xED, 0xF1, 0x71), C64Palette.Yellow);
    }

    [Fact]
    public void ByName_resolves_case_insensitively()
    {
        Assert.Equal(C64Palette.LightBlue, C64Palette.ByName("lightblue"));
        Assert.Equal(C64Palette.LightBlue, C64Palette.ByName("LightBlue"));
    }

    [Fact]
    public void ByName_rejects_unknown()
    {
        Assert.Throws<KeyNotFoundException>(() => C64Palette.ByName("chartreuse"));
    }

    [Fact]
    public void FromFloat_round_trips_every_byte_value()
    {
        // An engine hands pixels back as floats. If this truncated instead of rounding,
        // palette colours would come back one short and preservation would silently fail.
        for (int v = 0; v <= 255; v++)
        {
            float f = v / 255f;
            Assert.Equal(new Rgb(v, v, v), Rgb.FromFloat(f, f, f));
        }
    }

    [Fact]
    public void FromFloat_recovers_every_palette_colour_exactly()
    {
        foreach (var c in C64Palette.All)
            Assert.Equal(c, Rgb.FromFloat(c.R / 255f, c.G / 255f, c.B / 255f));
    }

    [Fact]
    public void FromFloat_clamps_out_of_range_input()
    {
        Assert.Equal(new Rgb(255, 0, 255), Rgb.FromFloat(1.4f, -0.2f, 1.0f));
    }
}
