using Miner49er.Core;
using Xunit;

public class LifeLedgerTests
{
    [Fact]
    public void Bot_death_does_not_spend_from_the_human_pool()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });

        ledger.TrySpend(2);

        Assert.Equal(3, ledger.PoolRemaining);
    }

    [Fact]
    public void Human_death_spends_one_life_from_the_pool()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });

        bool respawns = ledger.TrySpend(1);

        Assert.True(respawns);
        Assert.Equal(2, ledger.PoolRemaining);
    }

    [Fact]
    public void Bot_respawns_on_its_own_two_lives_then_stays_down()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });

        Assert.True(ledger.TrySpend(2));
        Assert.True(ledger.TrySpend(2));
        Assert.False(ledger.TrySpend(2));
    }

    [Fact]
    public void Refreshing_for_a_new_floor_gives_bots_their_lives_back()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });
        ledger.TrySpend(2);
        ledger.TrySpend(2);

        ledger.RefreshBots();

        Assert.True(ledger.TrySpend(2));
    }

    [Fact]
    public void Refreshing_for_a_new_floor_leaves_the_human_pool_alone()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });
        ledger.TrySpend(1);

        ledger.RefreshBots();

        Assert.Equal(2, ledger.PoolRemaining);
    }

    [Fact]
    public void A_humans_potion_tops_up_the_shared_pool()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });
        ledger.TrySpend(1);

        ledger.Restore(1);

        Assert.Equal(3, ledger.PoolRemaining);
    }

    [Fact]
    public void A_humans_potion_cannot_push_the_pool_past_its_maximum()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });

        ledger.Restore(1);

        Assert.Equal(3, ledger.PoolRemaining);
    }

    [Fact]
    public void A_bots_potion_refills_its_own_lives_not_the_human_pool()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });
        ledger.TrySpend(2);
        ledger.TrySpend(2);

        ledger.Restore(2);

        Assert.True(ledger.TrySpend(2));
        Assert.Equal(3, ledger.PoolRemaining);
    }

    [Fact]
    public void A_bots_potion_cannot_push_it_past_two_lives()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });

        ledger.Restore(2);

        Assert.True(ledger.TrySpend(2));
        Assert.True(ledger.TrySpend(2));
        Assert.False(ledger.TrySpend(2));
    }

    // ---- Guards. These passed the moment they were written: the minimal implementations
    // above already satisfy them. They are here to pin the rules that were chosen rather
    // than derived â€” three lives per human, and a private budget per bot â€” so that a later
    // change to either shows up as a failure instead of a silent rebalance.

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    [InlineData(4, 12)]
    public void The_pool_is_three_lives_per_human(int humans, int expected)
    {
        var ledger = new LifeLedger(humans, botMinerIds: new[] { 99 });

        Assert.Equal(expected, ledger.PoolMax);
        Assert.Equal(expected, ledger.PoolRemaining);
    }

    [Fact]
    public void Bots_do_not_count_towards_the_pool()
    {
        var solo     = new LifeLedger(humanCount: 1, botMinerIds: new int[0]);
        var withBots = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2, 3, 4 });

        Assert.Equal(solo.PoolMax, withBots.PoolMax);
    }

    [Fact]
    public void One_bot_burning_out_leaves_the_other_bots_untouched()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2, 3 });
        ledger.TrySpend(2);
        ledger.TrySpend(2);
        Assert.False(ledger.TrySpend(2));

        Assert.True(ledger.TrySpend(3));
    }

    [Fact]
    public void A_human_stays_down_once_the_pool_is_empty()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new[] { 2 });
        ledger.TrySpend(1);
        ledger.TrySpend(1);
        ledger.TrySpend(1);

        Assert.False(ledger.TrySpend(1));
        Assert.Equal(0, ledger.PoolRemaining);
    }

    [Fact]
    public void A_fixed_pool_does_not_scale_with_the_party()
    {
        var ledger = LifeLedger.WithFixedPool(1);

        Assert.Equal(1, ledger.PoolMax);
        Assert.Equal(1, ledger.PoolRemaining);
    }

    [Fact]
    public void A_resumed_save_seeds_the_pool_with_the_lives_it_stored()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new int[0]);

        ledger.SetPoolRemaining(2);

        Assert.Equal(2, ledger.PoolRemaining);
    }

    [Fact]
    public void A_resumed_save_cannot_seed_more_lives_than_the_pool_holds()
    {
        var ledger = new LifeLedger(humanCount: 1, botMinerIds: new int[0]);

        ledger.SetPoolRemaining(99);

        Assert.Equal(3, ledger.PoolRemaining);
    }
}
