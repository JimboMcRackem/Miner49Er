namespace Miner49er;

/// <summary>Playable miner character variants. Cosmetic only, synced like team colour.
/// Variant 0 is the default sprite at res://assets/miners/; others live in a slug subfolder.</summary>
public static class MinerVariants
{
    public static readonly string[] Names =
        { "Classic Miner", "Burly Prospector", "Gaunt Digger", "Grizzled Veteran", "Stout Matron", "Buxom Maiden" };

    // Asset subfolder for each variant; "" means the base res://assets/miners/ path.
    public static readonly string[] Slugs = { "", "burly", "gaunt", "veteran", "matron", "maiden" };

    public static int Count => Names.Length;

    public static int Clamp(int i) => (i % Count + Count) % Count;

    /// <summary>Path prefix under res://assets/miners/ for a variant, with trailing slash for slugs.</summary>
    public static string Prefix(int variant)
    {
        string slug = Slugs[Clamp(variant)];
        return slug == "" ? "" : slug + "/";
    }
}
