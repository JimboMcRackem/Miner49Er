using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class TreasureHuntCompassTests
{
    private static GridPos Self => new(10, 10);
    private const ItemKind A = ItemKind.IdolZeus;
    private const ItemKind B = ItemKind.IdolOdin;

    private static ItemSnapshot Item(int x, int y, ItemKind kind, ItemPlacement placement = ItemPlacement.Buried)
        => new(x, y, kind, placement);

    [Fact]
    public void No_items_returns_null()
    {
        Assert.Null(TreasureHuntCompass.NearestIdolTarget(Self, A, B, new List<ItemSnapshot>()));
    }

    [Fact]
    public void Ignores_idols_not_assigned_to_this_player()
    {
        var items = new[] { Item(12, 10, ItemKind.IdolRa), Item(5, 5, ItemKind.IdolShiva) };
        Assert.Null(TreasureHuntCompass.NearestIdolTarget(Self, A, B, items));
    }

    [Fact]
    public void Picks_nearest_of_the_two_assigned_idols()
    {
        // A is far east (dist 10), B is near west (dist 3).
        var items = new[] { Item(20, 10, A), Item(7, 10, B) };
        Assert.Equal(new GridPos(7, 10), TreasureHuntCompass.NearestIdolTarget(Self, A, B, items));
    }

    [Fact]
    public void Counts_both_buried_and_loose_placements()
    {
        var loose = new[] { Item(13, 10, A, ItemPlacement.Loose) };
        Assert.Equal(new GridPos(13, 10), TreasureHuntCompass.NearestIdolTarget(Self, A, B, loose));

        var buried = new[] { Item(13, 10, A, ItemPlacement.Buried) };
        Assert.Equal(new GridPos(13, 10), TreasureHuntCompass.NearestIdolTarget(Self, A, B, buried));
    }

    [Fact]
    public void Ignores_toolbox_placement()
    {
        // A toolbox idol should not exist in Treasure Hunt; guard against it anyway.
        var items = new[] { Item(12, 10, A, ItemPlacement.Toolbox) };
        Assert.Null(TreasureHuntCompass.NearestIdolTarget(Self, A, B, items));
    }
}
