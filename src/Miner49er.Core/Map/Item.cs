namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind
{
    SpeedPotion, LongerVision, BiggerBlast,
    WaterPlank, SlowMold, Lantern,
    Chest,      // loot container: rolls ChestLootTable on pickup
    LifePotion, // from Chest loot only; fires LifeRestored event
    BossChest,  // boss floor only; opens the escape tile
    Detonator,  // two-part: plant on wall then trigger at safe distance
    Reel,       // held after planting a Detonator; Use to detonate; never placed on ground
    // Treasure Hunt
    TreasureChest,
    IdolVishnu, IdolZeus, IdolAnubis, IdolOdin,
    IdolShiva, IdolBuddha, IdolRa, IdolQuetzalcoatl,
    IdolUrn, IdolLamp, IdolMace, IdolSceptre, IdolGlobe,
    IdolTrophyCup, IdolChalice, IdolCrown, IdolSkull,
}

public static class ItemKindExtensions
{
    /// <summary>Carried kinds go into the 1-slot inventory; triggered with Use verb.</summary>
    public static bool IsCarried(this ItemKind k) =>
        k is ItemKind.WaterPlank or ItemKind.SlowMold or ItemKind.Lantern
          or ItemKind.Detonator or ItemKind.TreasureChest
          or (>= ItemKind.IdolVishnu and <= ItemKind.IdolSkull);

    /// <summary>Placeable kinds cycle in PlaceItems (random floor placement).</summary>
    public static bool IsPlaceable(this ItemKind k) =>
        k is ItemKind.SpeedPotion or ItemKind.LongerVision or ItemKind.BiggerBlast;

    public static bool IsIdol(this ItemKind k) =>
        k >= ItemKind.IdolVishnu && k <= ItemKind.IdolSkull;
}

/// <summary>Where an item sits and how it can be collected.</summary>
public enum ItemPlacement
{
    Toolbox,   // visible on a Floor tile, collectible on walk-over
    Buried,    // hidden inside a Rock tile; not collectible until the rock is mined/blasted, which flips it to Loose
    Loose,     // spilled onto a Floor tile after being unburied; collectible on walk-over
}

/// <summary>A collectible. Buried items sit on a Rock tile and are not collectible
/// until the rock is destroyed, which flips them to Loose.</summary>
public readonly record struct Item(GridPos Pos, ItemKind Kind, ItemPlacement Placement = ItemPlacement.Toolbox);
