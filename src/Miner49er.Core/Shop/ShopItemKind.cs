namespace Miner49er.Core;

public enum ShopItemKind { SpeedUp, VisionUp, BlastUp, LifePotion, Stones3 }

public static class ShopPrices
{
    public static int Price(ShopItemKind kind) => kind switch
    {
        ShopItemKind.SpeedUp    => 15,
        ShopItemKind.VisionUp   => 15,
        ShopItemKind.BlastUp    => 20,
        ShopItemKind.LifePotion => 25,
        ShopItemKind.Stones3    => 10,
        _ => int.MaxValue,
    };
}
