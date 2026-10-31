using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class MonsterTelegraphTests
{
    private static MonsterSnapshot Mo(int x, int y, MonsterKind kind = MonsterKind.Slime,
        bool alive = true, float stun = 0f, bool dormant = false) =>
        new(1, x, y, 0, kind, alive, stun, dormant);

    [Fact]
    public void A_monster_one_step_away_is_about_to_strike()
    {
        Assert.True(MonsterTelegraph.IsAboutToStrike(Mo(4, 3), new GridPos(3, 3)));
    }

    [Theory]
    [InlineData(5, 3)]   // two tiles off
    [InlineData(4, 4)]   // diagonal: monsters step along one axis
    public void A_monster_that_cannot_reach_the_miner_this_step_gives_no_warning(int x, int y)
    {
        Assert.False(MonsterTelegraph.IsAboutToStrike(Mo(x, y), new GridPos(3, 3)));
    }

    [Fact]
    public void A_goat_uses_its_own_wind_up_instead()
    {
        Assert.False(MonsterTelegraph.IsAboutToStrike(Mo(4, 3, MonsterKind.Goat), new GridPos(3, 3)));
    }

    [Fact]
    public void Monsters_that_cannot_move_give_no_warning()
    {
        var miner = new GridPos(3, 3);
        Assert.False(MonsterTelegraph.IsAboutToStrike(Mo(4, 3, alive: false), miner));
        Assert.False(MonsterTelegraph.IsAboutToStrike(Mo(4, 3, stun: 1f), miner));
        Assert.False(MonsterTelegraph.IsAboutToStrike(Mo(4, 3, MonsterKind.SkeletonHuman, dormant: true), miner));
    }
}

public class HitPauseTests
{
    [Fact]
    public void Pause_holds_until_its_time_runs_out()
    {
        var pause = new HitPause();
        pause.Trigger(0.1);

        Assert.Equal(0.0, pause.Scale(0.06));
        Assert.Equal(0.0, pause.Scale(0.03));
        Assert.Equal(0.016, pause.Scale(0.016));   // only 0.01 left, but a frame is never split
    }

    [Fact]
    public void Runs_at_full_time_when_idle()
    {
        Assert.Equal(0.016, new HitPause().Scale(0.016));
    }

    [Fact]
    public void A_smaller_hit_does_not_cut_a_bigger_pause_short()
    {
        var pause = new HitPause();
        pause.Trigger(0.12);
        pause.Trigger(0.05);

        Assert.Equal(0.0, pause.Scale(0.1));
    }

    [Fact]
    public void Repeated_hits_do_not_stack_into_a_long_freeze()
    {
        var pause = new HitPause();
        pause.Trigger(0.1);
        pause.Trigger(0.1);
        pause.Scale(0.1);

        Assert.False(pause.Active);
    }
}
