using System.Linq;
using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class CartTacticsTests
{
    // Builds a sim with an all-Floor grid and a straight east-west rail across row `railY`.
    private static Simulation MakeSim(int w = 20, int h = 12, int railY = 6)
    {
        var grid = new TileGrid(w, h, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        sim.AddTrack(Enumerable.Range(0, w).Select(x => new GridPos(x, railY)));
        return sim;
    }

    // Turns cart `id` into a lantern cart via the real attach path (mirrors MineCartCargoTests):
    // an adjacent miner holding a lantern Uses it onto the cart.
    private static void MakeLanternCart(Simulation sim, int cartId, int loaderId, GridPos loaderPos)
    {
        var loader = sim.AddMiner(loaderId, loaderPos);
        loader.Held = ItemKind.Lantern;
        sim.TryUseItem(loaderId);
    }

    // Arms cart `id` with a charge via the real attach path: an adjacent miner holding a
    // Detonator Uses it onto the cart (MineCartCargoTests:102-105). Cargo becomes Charge, fuse unlit.
    private static void ArmCart(Simulation sim, int cartId, int loaderId, GridPos loaderPos)
    {
        var loader = sim.AddMiner(loaderId, loaderPos);
        loader.Held = ItemKind.Detonator;
        sim.TryUseItem(loaderId);
    }

    [Fact]
    public void PredictRoll_stops_at_track_end()
    {
        var sim = MakeSim(w: 10, railY: 5);   // rail spans x=0..9
        // Cart at x=7 pushed east: rolls x=8,9 then stops (x=10 off-grid, not track).
        var pred = CartTactics.PredictRoll(sim, new GridPos(7, 5), Direction.East);
        Assert.Equal(new[] { new GridPos(8, 5), new GridPos(9, 5) }, pred.Tiles.ToArray());
        Assert.False(pred.Derails);
        Assert.Equal(0, pred.MonstersSquashed);
        Assert.False(pred.MinerInPath);
    }

    [Fact]
    public void PredictRoll_counts_squashable_monster_and_rolls_through()
    {
        var sim = MakeSim(railY: 5);
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Slime);   // squashable, on the rail
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.Equal(1, pred.MonstersSquashed);
        Assert.Contains(new GridPos(6, 5), pred.Tiles);            // rolled past the slime
    }

    [Fact]
    public void PredictRoll_ignores_non_squashable_monster()
    {
        var sim = MakeSim(railY: 5);
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Ghost);   // NOT squashable
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.Equal(0, pred.MonstersSquashed);
    }

    [Fact]
    public void PredictRoll_flags_miner_in_path()
    {
        var sim = MakeSim(railY: 5);
        sim.AddMiner(2, new GridPos(6, 5));
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.True(pred.MinerInPath);
    }

    [Fact]
    public void PredictRoll_derails_on_lethal_tile()
    {
        var sim = MakeSim(railY: 5);
        sim.Grid.Set(new GridPos(5, 5), TileType.Lava);           // lethal on the rail
        var pred = CartTactics.PredictRoll(sim, new GridPos(3, 5), Direction.East);
        Assert.True(pred.Derails);
        Assert.Equal(new GridPos(5, 5), pred.Tiles.Last());       // stops AT the lethal tile
    }

    [Fact]
    public void Squash_pushes_into_cart_when_on_the_push_tile()
    {
        var sim = MakeSim(railY: 5);
        // rail east-west on row 5. Cart at x=8, slime at x=10 (east of cart). Bot at x=7 (push tile
        // to shove the cart EAST). Expected: push east (Direction.East ordinal 1).
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        var act = CartTactics.TrySquash(sim, bot, BotSkill.Miner);
        Assert.NotNull(act);
        Assert.Equal((int)Direction.East, act!.Value.Dir);
        Assert.False(act.Value.Plant);
    }

    [Fact]
    public void Squash_navigates_toward_push_tile_when_not_on_it()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(5, 5));   // west of push tile (x=7); step east toward it
        var act = CartTactics.TrySquash(sim, bot, BotSkill.Miner);
        Assert.NotNull(act);
        Assert.Equal((int)Direction.East, act!.Value.Dir);
    }

    [Fact]
    public void Squash_skips_when_a_teammate_is_in_the_roll_path()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        sim.AddMiner(9, new GridPos(9, 5));             // teammate between cart and slime
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        Assert.Null(CartTactics.TrySquash(sim, bot, BotSkill.Miner));
    }

    [Fact]
    public void Squash_does_nothing_with_no_monster_near()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        Assert.Null(CartTactics.TrySquash(sim, bot, BotSkill.Miner));
    }

    [Fact]
    public void Squash_is_gated_off_for_greenhorn()
    {
        var sim = MakeSim(railY: 5);
        sim.AddCart(new CartSpec(1, new GridPos(8, 5), Direction.East));
        sim.AddMonster(1, new GridPos(10, 5), MonsterKind.Slime);
        var bot = sim.AddMiner(3, new GridPos(7, 5));
        Assert.Null(CartTactics.TrySquash(sim, bot, BotSkill.Greenhorn));
    }
}
