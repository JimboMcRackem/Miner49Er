using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMonsterTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    [Fact]
    public void AddMonster_registers_a_living_monster()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor));
        var mo = sim.AddMonster(1, new GridPos(2, 2), MonsterKind.Slime);

        Assert.True(mo.Alive);
        Assert.Equal(MonsterKind.Slime, mo.Kind);
        Assert.Single(sim.Monsters);
        Assert.Equal(new GridPos(2, 2), sim.Monsters[0].Pos);
    }

    [Fact]
    public void Slime_steps_toward_the_miner_when_within_sense_radius()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var sim = Sim(new TileGrid(9, 3, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);   // cooldown started at cadence (0.1) -> elapses this tick -> one step

        Assert.Equal(new GridPos(3, 1), slime.Pos);   // moved east, toward the miner
    }

    [Fact]
    public void Slime_is_blocked_by_rock()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);     // wall east of the slime
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));             // miner is east, slime wants to go east
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(2, 1), slime.Pos);     // rock blocked the step; stayed put
    }
}
