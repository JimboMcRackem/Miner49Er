using System.Linq;
using Miner49er.Core;
using Xunit;

public class MineCartCargoTests
{
    private static Simulation RailSim(int x0, int x1, int y, SimConfig? cfg = null, int w = 12, int h = 6)
    {
        var grid = new TileGrid(w, h, TileType.Floor);
        var sim = new Simulation(grid, cfg ?? new SimConfig());
        sim.AddTrack(Enumerable.Range(x0, x1 - x0 + 1).Select(x => new GridPos(x, y)));
        return sim;
    }

    private static CartReadModel Cart(Simulation sim, int id) => sim.Carts.First(c => c.Id == id);

    [Fact]
    public void UseBesideCart_AttachesHeldLantern()
    {
        var sim = RailSim(2, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(1, new GridPos(3, 3));   // directly below the cart
        m.Held = ItemKind.Lantern;

        Assert.True(sim.TryUseItem(1));
        Assert.Equal(CartCargo.Lantern, Cart(sim, 1).Cargo);
        Assert.Null(sim.GetMiner(1).Held);            // moved out of hand
    }

    [Fact]
    public void UseBesideLadenCart_EmptyHanded_DetachesCargo()
    {
        var sim = RailSim(2, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(1, new GridPos(3, 3));
        m.Held = ItemKind.Lantern;
        sim.TryUseItem(1);                            // attach
        Assert.True(sim.TryUseItem(1));               // detach
        Assert.Equal(CartCargo.None, Cart(sim, 1).Cargo);
        Assert.Equal(ItemKind.Lantern, sim.GetMiner(1).Held);
    }

    [Fact]
    public void CannotAttachToAlreadyLadenCart()
    {
        var sim = RailSim(2, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(1, new GridPos(3, 3));
        m.Held = ItemKind.Lantern;
        sim.TryUseItem(1);                            // attach lantern
        var m2 = sim.AddMiner(2, new GridPos(3, 1));  // above the cart
        m2.Held = ItemKind.Detonator;
        sim.TryUseItem(2);                            // cart already laden → falls through to default use
        Assert.Equal(CartCargo.Lantern, Cart(sim, 1).Cargo);   // unchanged
    }

    [Fact]
    public void PlantBesideCart_ArmsIt_EvenWhenNotFacingIt()
    {
        var sim = RailSim(2, 6, 2, new SimConfig { DynamiteEnabled = true });
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(1, new GridPos(3, 3));   // below the cart
        m.Facing = Direction.South;                    // facing AWAY from the cart (open floor)
        Assert.True(sim.TryStartPlanting(1));
        Assert.Equal(CartCargo.Charge, Cart(sim, 1).Cargo);
    }

    [Fact]
    public void PlantFacingWall_BesideCart_StillPlantsOnWall_NotCart()
    {
        var sim = RailSim(2, 6, 2, new SimConfig { DynamiteEnabled = true, PlantSeconds = 0.5 });
        sim.Grid.Set(new GridPos(3, 4), TileType.Rock);   // wall below the miner
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(1, new GridPos(3, 3));
        m.Facing = Direction.South;                        // facing the rock wall
        Assert.True(sim.TryStartPlanting(1));
        Assert.Equal(CartCargo.None, Cart(sim, 1).Cargo);  // cart untouched; wall-plant took priority
    }

    [Fact]
    public void LanternCart_KillsAdjacentGhost()
    {
        var sim = RailSim(2, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(1, new GridPos(3, 3));
        m.Held = ItemKind.Lantern;
        sim.TryUseItem(1);                            // cart now a lantern source
        sim.AddMonster(9, new GridPos(4, 2), MonsterKind.Ghost);   // within LanternRadius

        sim.Tick(0.2);
        Assert.False(sim.Monsters.First(g => g.Id == 9).Alive);   // swept by the cart's light
    }

    [Fact]
    public void LaunchedChargeCart_DetonatesAfterFuse_ClearingRock()
    {
        var cfg = new SimConfig { ThrownDynamiteFuseSeconds = 1.0, BlastRockRadius = 1 };
        var sim = RailSim(2, 6, 2, cfg);
        sim.Grid.Set(new GridPos(6, 3), TileType.Rock);            // rock next to the rail's far end
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        // arm the cart with a charge
        var loader = sim.AddMiner(2, new GridPos(3, 1));
        loader.Held = ItemKind.Detonator;
        sim.TryUseItem(2);
        Assert.Equal(CartCargo.Charge, Cart(sim, 1).Cargo);

        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East);               // launch: rolls to (6,2), fuse lit
        for (int i = 0; i < 10; i++) sim.Tick(0.2);   // let the fuse expire
        Assert.Empty(sim.Carts.Where(c => c.Id == 1));            // detonated & destroyed
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(6, 3))); // blast cleared the rock
    }

    [Fact]
    public void UnlaunchedChargeCart_DoesNotDetonate()
    {
        var sim = RailSim(2, 6, 2);
        sim.AddCart(new CartSpec(1, new GridPos(3, 2), Direction.East));
        var m = sim.AddMiner(2, new GridPos(3, 1));
        m.Held = ItemKind.Detonator;
        sim.TryUseItem(2);                            // attach, never launched
        for (int i = 0; i < 20; i++) sim.Tick(0.2);
        Assert.Single(sim.Carts.Where(c => c.Id == 1));          // still there, no boom
    }
}
