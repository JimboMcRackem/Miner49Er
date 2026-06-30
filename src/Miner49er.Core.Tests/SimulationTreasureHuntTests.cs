using Miner49er.Core;
using System.Linq;
using Xunit;

public class SimulationTreasureHuntTests
{
    private static Simulation TreasureSim(int playerCount, out Miner[] miners)
    {
        var cfg = new SimConfig { Seed = 42, TreasureHuntMode = true };
        var sim = new Simulation(new TileGrid(10, 10, TileType.Floor), cfg);
        miners = new Miner[playerCount];
        for (int i = 0; i < playerCount; i++)
            miners[i] = sim.AddMiner(i + 1, new GridPos(1 + i * 2, 1));
        return sim;
    }

    [Fact]
    public void Miner_starts_holding_TreasureChest()
    {
        TreasureSim(1, out var miners);
        Assert.Equal(ItemKind.TreasureChest, miners[0].Held);
    }

    [Fact]
    public void Using_held_chest_places_it_on_current_tile()
    {
        var sim = TreasureSim(1, out var miners);
        var startPos = miners[0].Pos;
        sim.TryUseItem(1);
        Assert.Null(miners[0].Held);
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.TreasureChest && it.Pos == startPos);
    }

    [Fact]
    public void GetPlacedChests_reflects_placed_chest_position()
    {
        var sim = TreasureSim(1, out var miners);
        Assert.Empty(sim.GetPlacedChests());
        sim.TryUseItem(1);
        var chests = sim.GetPlacedChests();
        Assert.Single(chests);
        Assert.Equal(1, chests[0].MinerId);
        Assert.Equal(miners[0].Pos.X, chests[0].X);
        Assert.Equal(miners[0].Pos.Y, chests[0].Y);
    }

    [Fact]
    public void Only_owner_can_pick_up_placed_chest()
    {
        var sim = TreasureSim(2, out var miners);
        // Miner 1 places chest at (1,1)
        sim.TryUseItem(1);
        Assert.Null(miners[0].Held);
        // Move miner 2 (starts at (3,1)) to (1,1)
        sim.TryMove(2, Direction.West); // (3,1) → (2,1)
        sim.Tick(1.0);
        sim.TryMove(2, Direction.West); // (2,1) → (1,1) — on miner 1's chest
        // Clear miner 2's held item so UseItem step 1 can attempt pickup
        miners[1].Held = null;
        sim.TryUseItem(2);
        // Miner 2 should NOT have picked up the TreasureChest (ownership guard)
        Assert.Null(miners[1].Held);
        // Miner 1's chest should still be on the floor
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.TreasureChest && it.Pos.X == 1 && it.Pos.Y == 1);
    }

    [Fact]
    public void Walking_onto_own_chest_with_assigned_idol_deposits_it()
    {
        var sim = TreasureSim(1, out var miners);
        sim.TryUseItem(1); // place chest at (1,1)
        Assert.Null(miners[0].Held);
        var (idolA, _) = TreasureAssignment.For(42, 1);
        sim.GiveItemForTest(1, idolA);
        Assert.Equal(idolA, miners[0].Held);
        // Move away then back onto chest tile (Tick between moves to drain cooldown)
        sim.TryMove(1, Direction.East); // (1,1) → (2,1)
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West); // (2,1) → (1,1) — auto-deposit fires
        Assert.Null(miners[0].Held); // idol was deposited
        var progress = sim.GetTreasureProgress();
        Assert.Equal(1, progress.First(p => p.MinerId == 1).Found);
    }

    [Fact]
    public void Non_assigned_idol_does_not_deposit()
    {
        var sim = TreasureSim(2, out var miners);
        sim.TryUseItem(1); // miner 1 places chest at (1,1)
        Assert.Null(miners[0].Held);
        // Give miner 1 an idol that belongs to miner 2
        var (m2A, _) = TreasureAssignment.For(42, 2);
        sim.GiveItemForTest(1, m2A);
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West); // back on chest at (1,1)
        // Should NOT deposit since m2A is not assigned to miner 1
        Assert.Equal(m2A, miners[0].Held);
        Assert.Equal(0, sim.GetTreasureProgress().First(p => p.MinerId == 1).Found);
    }

    [Fact]
    public void TreasureWinner_returns_minus_one_before_two_deposits()
    {
        var sim = TreasureSim(1, out _);
        Assert.Equal(-1, sim.TreasureWinner());
    }

    [Fact]
    public void TreasureWinner_returns_winner_after_two_deposits()
    {
        var sim = TreasureSim(1, out _);
        sim.TryUseItem(1); // place chest at (1,1)
        var (idolA, idolB) = TreasureAssignment.For(42, 1);
        // Deposit idol A
        sim.GiveItemForTest(1, idolA);
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West);
        Assert.Equal(-1, sim.TreasureWinner()); // only 1 of 2
        // Deposit idol B
        sim.GiveItemForTest(1, idolB);
        sim.Tick(1.0); // drain cooldown from previous move
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West);
        Assert.Equal(1, sim.TreasureWinner());
    }

    [Fact]
    public void Swapping_chest_for_idol_tracks_chest_position_and_allows_deposit()
    {
        // Repro: miner swaps held chest for an unburied idol (Space on idol tile).
        // Before the fix, _chestPos stayed null → deposit never fired and chest
        // could not be re-picked-up.
        var sim = TreasureSim(1, out var miners);
        var (idolA, _) = TreasureAssignment.For(42, 1);

        // Place an unburied idol at (3,1) and have the miner walk onto it.
        sim.AddItem(new Item(new GridPos(3, 1), idolA, ItemPlacement.Loose));
        sim.TryMove(1, Direction.East); // (1,1) → (2,1)
        sim.Tick(1.0);
        sim.TryMove(1, Direction.East); // (2,1) → (3,1)  — standing on idol

        // Space while holding chest: swaps — chest drops at (3,1), idol picked up.
        sim.TryUseItem(1);
        Assert.Equal(idolA, miners[0].Held);

        // The dropped chest must be tracked so the owned re-pickup check passes.
        var chests = sim.GetPlacedChests();
        Assert.Single(chests);
        Assert.Equal(3, chests[0].X);
        Assert.Equal(1, chests[0].Y);

        // Move away then walk back onto the (swapped) chest tile — deposit must fire.
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West); // (3,1) → (2,1)
        sim.Tick(1.0);
        sim.TryMove(1, Direction.East); // (2,1) → (3,1)  — auto-deposit
        Assert.Null(miners[0].Held);
        Assert.Equal(1, sim.GetTreasureProgress().First(p => p.MinerId == 1).Found);
    }

    [Fact]
    public void Swapped_out_chest_can_be_re_picked_up()
    {
        var sim = TreasureSim(1, out var miners);
        var (idolA, _) = TreasureAssignment.For(42, 1);
        sim.AddItem(new Item(new GridPos(3, 1), idolA, ItemPlacement.Loose));
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.East); // on idol at (3,1)
        sim.TryUseItem(1); // swap: chest at (3,1), holding idol

        // Deposit the idol by walking off and back.
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.East); // deposit fires; now holding nothing
        Assert.Null(miners[0].Held);

        // Now press Space on the chest tile to pick the chest back up.
        sim.TryUseItem(1);
        Assert.Equal(ItemKind.TreasureChest, miners[0].Held);
        Assert.Empty(sim.GetPlacedChests());
    }

    [Fact]
    public void Owner_can_re_pick_up_placed_chest()
    {
        var sim = TreasureSim(1, out var miners);
        sim.TryUseItem(1); // place chest at (1,1)
        Assert.Null(miners[0].Held);
        // Move away and back (drain cooldown with Tick)
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West); // back on chest tile
        // UseItem step 1 scans for carried item at pos — chest is there and we own it
        sim.TryUseItem(1);
        Assert.Equal(ItemKind.TreasureChest, miners[0].Held);
        Assert.Empty(sim.GetPlacedChests());
    }
}
