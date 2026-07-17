using Miner49er.Core.Fx;
using Xunit;

public class ParticleSystemTests
{
    private static Particle P(float life, float vx = 0, float vy = 0, float gravity = 0) =>
        new() { X = 0, Y = 0, Vx = vx, Vy = vy, Gravity = gravity, Life = life, MaxLife = life, Size = 1, A = 1 };

    [Fact]
    public void Position_integrates_by_velocity()
    {
        var sys = new ParticleSystem(8);
        sys.Emit(P(life: 1f, vx: 10f));
        sys.Update(0.5f);
        Assert.Equal(5f, sys.Live[0].X, 3);
    }

    [Fact]
    public void Gravity_accelerates_vertical_velocity()
    {
        var sys = new ParticleSystem(8);
        sys.Emit(P(life: 5f, gravity: 10f));
        sys.Update(1f);
        Assert.Equal(10f, sys.Live[0].Vy, 3);
    }

    [Fact]
    public void Life_decrements_and_dead_particles_are_removed()
    {
        var sys = new ParticleSystem(8);
        sys.Emit(P(life: 1f));
        sys.Update(0.5f);
        Assert.Equal(1, sys.Count);
        Assert.Equal(0.5f, sys.Live[0].Life, 3);
        sys.Update(0.6f);
        Assert.Equal(0, sys.Count);
    }

    [Fact]
    public void Capacity_cap_replaces_the_particle_nearest_death()
    {
        var sys = new ParticleSystem(3);
        foreach (var life in new[] { 1f, 2f, 3f, 4f, 5f })
            sys.Emit(P(life));
        Assert.Equal(3, sys.Count);
        for (int i = 0; i < sys.Count; i++)
            Assert.True(sys.Live[i].Life >= 3f, $"unexpectedly small life {sys.Live[i].Life}");
    }

    [Fact]
    public void Empty_update_is_a_noop()
    {
        var sys = new ParticleSystem(4);
        sys.Update(1f);
        Assert.Equal(0, sys.Count);
    }
}
