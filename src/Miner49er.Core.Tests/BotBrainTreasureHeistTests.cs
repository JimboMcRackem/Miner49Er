using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class BotBrainTreasureHeistTests
{
    [Fact]
    public void Bot_seeks_and_grabs_the_loose_treasure()
    {
        var cfg = TreasureHeistTests.Cfg();
        var sim = new Simulation(TreasureHeistTests.Grid(20, 20), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.ForceTreasureLooseForTest(new GridPos(10, 2)); // loose, due east
        var brain = new BotBrain(1, BotSkill.Miner, seed: 123);

        for (int i = 0; i < 80; i++)
        {
            var action = brain.Think(sim, GameMode.TreasureHeist);
            if (action.Dir >= 0) sim.TryMove(1, (Direction)action.Dir);
            sim.Tick(0.2);
        }

        // A directed seeker walks onto (10,2) and picks the treasure up. A random walk
        // almost never lands on the exact tile, so this assertion fails without the
        // treasure-seeking branch — making it a real regression guard, not a coincidence
        // of the RNG seed netting positive on X.
        Assert.Equal(1, sim.TreasureHolderId);
    }
}
