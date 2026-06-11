namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast }

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
