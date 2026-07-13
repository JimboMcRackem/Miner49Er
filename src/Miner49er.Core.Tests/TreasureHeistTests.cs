using System.Linq;
using Miner49er.Core;
using Xunit;

public class TreasureHeistTests
{
    internal static SimConfig Cfg() => new SimConfig
    {
        Mode = GameMode.TreasureHeist,
        TreasureHeistMode = true,
        BaseMoveSeconds = 0.05,
        TreasureSneakSeconds = 8.0,
        TreasureSneakRadius = 6,
        TreasureSneakCooldown = 10.0,
        SuddenDeathHoldSeconds = 3.0,
        RespawnSeconds = 5.0,
        StartingStones = 9,
    };
    internal static TileGrid Grid(int w = 15, int h = 15) => new TileGrid(w, h, TileType.Floor);

    [Fact]
    public void New_miner_starts_with_configured_stones()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void Stepping_onto_unearthed_treasure_picks_it_up_and_fires_found()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5)); // treasure loose under miner 1
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        Assert.True(sim.TreasureFoundYet);
        Assert.Contains(sim.DrainEvents(), e => e is TreasureFound { MinerId: 1 });
    }
}
