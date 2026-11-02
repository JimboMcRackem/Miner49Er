using Miner49er.Core;
using Xunit;

public class SconceTests
{
    private static readonly GridPos Sconce = new(10, 5);

    [Fact]
    public void A_sconce_lights_within_five_tiles()
    {
        var sconces = new SconceTracker(new[] { Sconce });

        Assert.True(sconces.Update(new GridPos(15, 5)));

        Assert.True(sconces.IsLit(Sconce));
    }

    [Fact]
    public void A_sconce_stays_dark_at_six_tiles()
    {
        var sconces = new SconceTracker(new[] { Sconce });

        Assert.False(sconces.Update(new GridPos(16, 5)));

        Assert.False(sconces.IsLit(Sconce));
    }

    [Fact]
    public void A_lit_sconce_stays_lit_after_you_walk_away()
    {
        var sconces = new SconceTracker(new[] { Sconce });
        sconces.Update(new GridPos(12, 5));

        sconces.Update(new GridPos(40, 5));

        Assert.True(sconces.IsLit(Sconce));
    }

    [Fact]
    public void Update_reports_a_sconce_only_as_it_lights()
    {
        var sconces = new SconceTracker(new[] { Sconce });
        sconces.Update(new GridPos(12, 5));

        Assert.False(sconces.Update(new GridPos(12, 5)));
    }

    [Fact]
    public void Lit_lists_only_the_lit_sconces()
    {
        var far = new GridPos(30, 5);
        var sconces = new SconceTracker(new[] { Sconce, far });
        sconces.Update(Sconce);

        Assert.Equal(new[] { Sconce }, sconces.Lit);
    }
}
