using Miner49er.Core;
using Xunit;

public class ShopItemTests
{
    [Fact] public void SpeedUp_price_is_15()    => Assert.Equal(15, ShopPrices.Price(ShopItemKind.SpeedUp));
    [Fact] public void VisionUp_price_is_15()   => Assert.Equal(15, ShopPrices.Price(ShopItemKind.VisionUp));
    [Fact] public void BlastUp_price_is_20()    => Assert.Equal(20, ShopPrices.Price(ShopItemKind.BlastUp));
    [Fact] public void LifePotion_price_is_25() => Assert.Equal(25, ShopPrices.Price(ShopItemKind.LifePotion));
    [Fact] public void Stones3_price_is_10()    => Assert.Equal(10, ShopPrices.Price(ShopItemKind.Stones3));

    [Fact]
    public void AddStones_increases_StoneCount()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddStones(1, 3);
        Assert.Equal(3, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void AddStones_caps_at_9()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddStones(1, 8);
        sim.AddStones(1, 5);
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void DeductGold_reduces_GoldCollected()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        m.GoldCollected = 20;
        sim.DeductGold(1, 15);
        Assert.Equal(5, sim.GetMiner(1).GoldCollected);
    }

    [Fact]
    public void DeductGold_clamps_at_zero()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.DeductGold(1, 100);
        Assert.Equal(0, sim.GetMiner(1).GoldCollected);
    }
}
