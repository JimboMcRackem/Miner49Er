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
}
