using Miner49er.Core;
using Xunit;

// Explosion kills credit the blast's owner (PvP scoring). Self-kills and non-explosion
// deaths credit no one.
public class SimulationKillCreditTests
{
    // Owner (1) plants in a rock wall, steps clear, and the blast kills the rival (2) on the
    // far side of the wall. The owner earns the kill.
    [Fact]
    public void Explosion_credits_a_kill_to_the_charge_owner()
    {
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(4, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 0.5 });
        var owner  = sim.AddMiner(1, new GridPos(3, 1));
        var victim = sim.AddMiner(2, new GridPos(5, 1)); // adjacent to the wall — inside kill radius

        sim.TryMove(1, Direction.East);   // face the rock at (4,1)
        sim.TryStartPlanting(1);
        sim.Tick(0.01);                    // planting completes; charge armed at (4,1)
        sim.TryMove(1, Direction.West);    // owner steps out of the blast (to (2,1), Chebyshev 2)
        sim.Tick(0.6);                     // fuse expires → detonation

        Assert.False(victim.Alive);
        Assert.Equal(DeathCause.Exploded, victim.DeathCause);
        Assert.True(owner.Alive);
        Assert.Equal(1, owner.Kills);
    }

    // Blowing yourself up is not a kill.
    [Fact]
    public void A_self_kill_does_not_count()
    {
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(4, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 0.5 });
        var owner = sim.AddMiner(1, new GridPos(3, 1)); // stays adjacent — caught in own blast

        sim.TryMove(1, Direction.East);
        sim.TryStartPlanting(1);
        sim.Tick(0.01);
        sim.Tick(0.6);

        Assert.False(owner.Alive);
        Assert.Equal(0, owner.Kills);
    }

    // A non-explosion death (here: walking into a pit) credits no one.
    [Fact]
    public void An_environmental_death_credits_no_one()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Pit);
        var sim = new Simulation(grid, new SimConfig());
        var a = sim.AddMiner(1, new GridPos(0, 0));
        var b = sim.AddMiner(2, new GridPos(1, 2)); // just west of the pit

        sim.TryMove(2, Direction.East); // steps east into the pit and falls

        Assert.False(b.Alive);
        Assert.Equal(DeathCause.Fell, b.DeathCause);
        Assert.Equal(0, a.Kills);
        Assert.Equal(0, b.Kills);
    }
}
