using Miner49er.Core;
using Xunit;

public class TreasureAssignmentTests
{
    [Fact]
    public void For_returns_two_distinct_idols()
    {
        var (a, b) = TreasureAssignment.For(42, 1);
        Assert.NotEqual(a, b);
        Assert.True(a.IsIdol());
        Assert.True(b.IsIdol());
    }

    [Fact]
    public void AllAssigned_length_equals_playerCount_times_two()
    {
        var idols = TreasureAssignment.AllAssigned(42, 3);
        Assert.Equal(6, idols.Length);
    }

    [Fact]
    public void AllAssigned_all_unique_for_eight_players()
    {
        var idols = TreasureAssignment.AllAssigned(99, 8);
        Assert.Equal(16, idols.Length);
        Assert.Equal(idols.Length, new System.Collections.Generic.HashSet<ItemKind>(idols).Count);
    }

    [Fact]
    public void Different_seeds_produce_different_assignments()
    {
        var (a1, b1) = TreasureAssignment.For(1, 1);
        var (a2, b2) = TreasureAssignment.For(2, 1);
        Assert.False(a1 == a2 && b1 == b2);
    }

    [Fact]
    public void For_is_consistent_with_AllAssigned_slice()
    {
        int seed = 77;
        var all = TreasureAssignment.AllAssigned(seed, 4);
        for (int minerId = 1; minerId <= 4; minerId++)
        {
            var (a, b) = TreasureAssignment.For(seed, minerId);
            Assert.Equal(all[(minerId - 1) * 2],     a);
            Assert.Equal(all[(minerId - 1) * 2 + 1], b);
        }
    }

    [Fact]
    public void TreasureChest_is_carried()
    {
        Assert.True(ItemKind.TreasureChest.IsCarried());
    }

    [Fact]
    public void Idols_are_carried()
    {
        Assert.True(ItemKind.IdolVishnu.IsCarried());
        Assert.True(ItemKind.IdolSkull.IsCarried());
    }

    [Fact]
    public void AllIdols_has_17_entries()
    {
        Assert.Equal(17, TreasureAssignment.AllIdols().Length);
    }
}
