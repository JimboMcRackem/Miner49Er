using System;
using Miner49er.Core;
using Xunit;

public class FlickerTests
{
    [Fact]
    public void Result_stays_within_floor_and_one_for_fire()
    {
        for (int seed = 0; seed < 20; seed++)
            for (double t = 0; t < 40; t += 0.013)
            {
                float m = Flicker.Multiplier(seed, t, Flicker.Fire);
                Assert.InRange((double)m, Flicker.Fire.FloorLevel, 1.0);
            }
    }

    [Fact]
    public void Result_stays_within_floor_and_one_for_crystal()
    {
        for (int seed = 0; seed < 20; seed++)
            for (double t = 0; t < 40; t += 0.013)
            {
                float m = Flicker.Multiplier(seed, t, Flicker.Crystal);
                Assert.InRange((double)m, Flicker.Crystal.FloorLevel, 1.0);
            }
    }

    [Fact]
    public void Deterministic_for_same_inputs()
    {
        Assert.Equal(Flicker.Multiplier(7, 12.34, Flicker.Fire),
                     Flicker.Multiplier(7, 12.34, Flicker.Fire));
    }

    [Fact]
    public void Different_seeds_are_not_all_identical()
    {
        bool anyDifference = false;
        for (double t = 0; t < 40 && !anyDifference; t += 0.01)
            if (Flicker.Multiplier(1, t, Flicker.Fire) != Flicker.Multiplier(2, t, Flicker.Fire))
                anyDifference = true;
        Assert.True(anyDifference);
    }

    [Fact]
    public void Mostly_full_brightness_between_dips()
    {
        int atFull = 0, total = 0;
        for (double t = 0; t < 200; t += 0.01, total++)
            if (Flicker.Multiplier(3, t, Flicker.Fire) > 0.999f) atFull++;
        // Dips are sparse (~8% duty); the clear majority of time is full brightness.
        Assert.True(atFull > total * 0.6, $"only {atFull}/{total} samples at full");
    }

    [Fact]
    public void Dips_actually_happen()
    {
        bool dipped = false;
        for (double t = 0; t < 200 && !dipped; t += 0.01)
            if (Flicker.Multiplier(3, t, Flicker.Fire) < 0.9f) dipped = true;
        Assert.True(dipped);
    }

    [Fact]
    public void A_flame_never_holds_still()
    {
        for (double t = 0; t < 20; t += 0.25)
            Assert.NotEqual(Flicker.Flame(3, t), Flicker.Flame(3, t + 0.05));
    }

    [Fact]
    public void A_flame_stays_bright_enough_to_see_by()
    {
        for (int seed = 0; seed < 10; seed++)
            for (double t = 0; t < 60; t += 0.01)
            {
                Assert.InRange((double)Flicker.Flame(seed, t), 0.4, 1.0);
            }
    }

    [Fact]
    public void Two_flames_move_differently()
    {
        Assert.NotEqual(Flicker.Flame(1, 5.0), Flicker.Flame(2, 5.0));
    }

    [Fact]
    public void A_flame_moves_smoothly_between_frames()
    {
        const double step = 1.0 / 120.0;
        for (int seed = 0; seed < 10; seed++)
            for (double t = 0; t < 60; t += step)
                Assert.True(Math.Abs(Flicker.Flame(seed, t) - Flicker.Flame(seed, t + step)) < 0.1f);
    }

    [Fact]
    public void No_sudden_jumps_between_adjacent_frames()
    {
        const double step = 1.0 / 120.0;
        for (int seed = 0; seed < 10; seed++)
            for (double t = 0; t < 60; t += step)
            {
                float a = Flicker.Multiplier(seed, t, Flicker.Fire);
                float b = Flicker.Multiplier(seed, t + step, Flicker.Fire);
                Assert.True(Math.Abs(a - b) < 0.1f, $"jump {Math.Abs(a - b)} at seed {seed} t {t}");
            }
    }
}
