using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class BotBrainTreasureHeistTests
{
    [Fact]
    public void Bot_moves_toward_the_loose_treasure()
    {
        var cfg = TreasureHeistTests.Cfg();
        var sim = new Simulation(TreasureHeistTests.Grid(20, 20), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.ForceTreasureLooseForTest(new GridPos(10, 2)); // loose, due east
        var brain = new BotBrain(1, BotSkill.Miner, seed: 123);
        int before = sim.GetMiner(1).Pos.X;
        for (int i = 0; i < 20; i++)
        {
            var action = brain.Think(sim, GameMode.TreasureHeist);
            if (action.Dir >= 0) sim.TryMove(1, (Direction)action.Dir);
            sim.Tick(0.2);
        }
        Assert.True(sim.GetMiner(1).Pos.X > before);
    }
}
