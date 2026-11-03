using Miner49er.Core;
using Xunit;

public class WaterCornersTests
{
    private const int Wall = 0, Floor = 1, Water = 3, Pit = 4;
    private static readonly int[] Priority = { Wall, 2, 5, Water, Pit, Floor };

    private static (int, int, int, int) Dry(int tl, int tr, int bl, int br) =>
        WaterCorners.Dry(tl, tr, bl, br, Water, Priority);

    [Fact]
    public void WaterMeetingAPit_BecomesThePit()
    {
        Assert.Equal((Pit, Pit, Pit, Pit), Dry(Water, Water, Water, Pit));
    }

    [Fact]
    public void WaterMeetingFloor_BecomesTheFloor()
    {
        Assert.Equal((Floor, Floor, Floor, Floor), Dry(Water, Floor, Floor, Water));
    }

    [Fact]
    public void OpenWater_IsLeftAlone()
    {
        Assert.Equal((Water, Water, Water, Water), Dry(Water, Water, Water, Water));
    }

    [Fact]
    public void DryCells_AreLeftAlone()
    {
        Assert.Equal((Wall, Floor, Pit, Floor), Dry(Wall, Floor, Pit, Floor));
    }

    [Fact]
    public void MostCommonDryTerrain_FillsTheWater()
    {
        Assert.Equal((Floor, Floor, Pit, Floor), Dry(Water, Floor, Pit, Floor));
    }

    [Fact]
    public void TiedDryTerrains_FallToThePriorityOrder()
    {
        Assert.Equal((Wall, Wall, Wall, Floor), Dry(Water, Wall, Water, Floor));
    }
}
