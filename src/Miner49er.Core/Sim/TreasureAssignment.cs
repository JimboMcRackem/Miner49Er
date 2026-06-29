namespace Miner49er.Core;

public static class TreasureAssignment
{
    public static (ItemKind A, ItemKind B) For(int seed, int minerId)
    {
        var pool = AllIdols();
        Shuffle(pool, seed);
        int i = (minerId - 1) * 2;
        return (pool[i], pool[i + 1]);
    }

    public static ItemKind[] AllAssigned(int seed, int playerCount)
    {
        var pool = AllIdols();
        Shuffle(pool, seed);
        return pool[..(playerCount * 2)];
    }

    public static ItemKind[] AllIdols() => new[]
    {
        ItemKind.IdolVishnu,    ItemKind.IdolZeus,       ItemKind.IdolAnubis,
        ItemKind.IdolOdin,      ItemKind.IdolShiva,      ItemKind.IdolBuddha,
        ItemKind.IdolRa,        ItemKind.IdolQuetzalcoatl,
        ItemKind.IdolUrn,       ItemKind.IdolLamp,       ItemKind.IdolMace,
        ItemKind.IdolSceptre,   ItemKind.IdolGlobe,
        ItemKind.IdolTrophyCup, ItemKind.IdolChalice,
        ItemKind.IdolCrown,     ItemKind.IdolSkull,
    };

    private static void Shuffle(ItemKind[] arr, int seed)
    {
        var rng = new Random(seed);
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
