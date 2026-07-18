namespace Miner49er.Core;

/// <summary>What a cart is carrying. Cargo behaviour (lantern ghost-sweep, charge bomb)
/// arrives in a later phase; Phase 1 only tracks the slot.</summary>
public enum CartCargo { None, Lantern, Charge }

/// <summary>Deterministic cart placement handed from the map generator to the simulation
/// (mirrors <see cref="PortalSpec"/>). Id is unique per floor.</summary>
public readonly record struct CartSpec(int Id, GridPos Pos, Direction Dir);

/// <summary>Live per-floor cart state inside the simulation. Integer/tile-based for
/// determinism; <see cref="FuseRemaining"/> is the sole countdown (a launched Charge cart,
/// used from a later phase).</summary>
internal sealed class Cart
{
    public int Id { get; init; }
    public GridPos Pos { get; set; }
    public Direction Dir { get; set; } = Direction.East;
    public CartCargo Cargo { get; set; } = CartCargo.None;
    public double FuseRemaining { get; set; }
    public bool Destroyed { get; set; }
}

/// <summary>Read-only projection for tests and host-side inspection.</summary>
public readonly record struct CartReadModel(int Id, GridPos Pos, Direction Dir, CartCargo Cargo, bool Destroyed);
