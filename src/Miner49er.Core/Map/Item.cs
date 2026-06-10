namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map. 4c-2a ships the three
/// auto-apply buffs; 4c-2b appends carried items (WaterPlank, SlowMold).</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast }

/// <summary>A collectible sitting on a Floor tile, removed when a miner walks over it.</summary>
public readonly record struct Item(GridPos Pos, ItemKind Kind);
