namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast, WaterPlank, SlowMold, Lantern }

public static class ItemKindExtensions
{
    /// <summary>Carried kinds are not auto-applied on walk-over; they go into the
    /// 1-slot inventory and are triggered with the Use verb. The other kinds auto-apply.</summary>
    public static bool IsCarried(this ItemKind k) =>
        k is ItemKind.WaterPlank or ItemKind.SlowMold or ItemKind.Lantern;
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
