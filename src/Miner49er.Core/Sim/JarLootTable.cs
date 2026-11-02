namespace Miner49er.Core;

public enum JarLoot { Nothing, Gold, Item }

/// <summary>What an offering jar holds: small gold 40%, a chest item 10%, otherwise nothing.</summary>
public static class JarLootTable
{
    public static JarLoot Roll(Random rng) => rng.Next(10) switch
    {
        < 4 => JarLoot.Gold,
        4   => JarLoot.Item,
        _   => JarLoot.Nothing,
    };
}
