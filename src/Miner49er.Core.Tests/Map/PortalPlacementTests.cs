using System.Linq;
using Miner49er.Core;
using Xunit;

namespace Miner49er.Core.Tests.Map;

public class PortalPlacementTests
{
    [Fact]
    public void No_portals_before_floor_12()
    {
        for (int floor = 1; floor < 12; floor++)
            Assert.Equal(0, MapConfig.FloorConfig(floor, seed: 4321).PortalPairCount);
    }

    [Fact]
    public void Some_deeper_floors_have_portals_and_are_deterministic()
    {
        int gated = 0;
        for (int floor = 12; floor <= 50; floor++)
        {
            var a = MapConfig.FloorConfig(floor, seed: 4321).PortalPairCount;
            var b = MapConfig.FloorConfig(floor, seed: 4321).PortalPairCount;
            Assert.Equal(a, b);                 // deterministic
            Assert.InRange(a, 0, 2);            // never more than 2 pairs
            if (a > 0) gated++;
        }
        Assert.InRange(gated, 3, 25);           // ~1-in-4 of 39 floors, sanity bounds
    }

    [Fact]
    public void Generate_places_linked_pairs_deterministically()
    {
        var cfg = MapConfig.For(GameMode.Expedition, seed: 99, playerCount: 1);
        cfg.PortalPairCount = 2;

        var a = MapGenerator.Generate(cfg).Portals;
        var b = MapGenerator.Generate(cfg).Portals;

        // Deterministic: same seed → identical placement.
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]);

        Assert.Equal(4, a.Count);               // 2 pairs
        foreach (var portal in a)
        {
            var link = a.Single(p => p.Id == portal.LinkId);
            Assert.Equal(portal.Id, link.LinkId);       // mutual link
            Assert.Equal(portal.Kind, link.Kind);       // a pair shares one kind
            Assert.NotEqual(portal.Pos, link.Pos);      // distinct tiles
        }
    }

    [Fact]
    public void Portal_tiles_are_floor_or_ordinary_rock_never_hazard()
    {
        var cfg = MapConfig.For(GameMode.Expedition, seed: 7, playerCount: 1);
        cfg.PortalPairCount = 2;
        var map = MapGenerator.Generate(cfg);

        foreach (var portal in map.Portals)
        {
            var t = map.Grid.Get(portal.Pos);
            // Exposed ends sit on Floor; buried ends sit in ordinary Rock. Never a
            // hazard, water, gold, impermeable, or special tile.
            Assert.True(t == TileType.Floor || t == TileType.Rock,
                $"portal on unexpected tile {t}");
        }
    }
}
