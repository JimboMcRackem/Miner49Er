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
}
