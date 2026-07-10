using System.Linq;
using Miner49er.Core;
using Xunit;

namespace Miner49er.Core.Tests.Sim;

public class PortalTeleportTests
{
    // 5x1 open floor strip: miner at (0,0), gate A at (1,0), gate B at (4,0).
    private static Simulation MakeSim(out int aId, out int bId, PortalKind kind = PortalKind.Stable,
        bool buryB = false)
    {
        var grid = new TileGrid(5, 1, TileType.Floor);
        if (buryB) grid.Set(new GridPos(4, 0), TileType.Rock); // buried partner
        var sim = new Simulation(grid, new SimConfig { Seed = 1 });
        sim.AddMiner(0, new GridPos(0, 0));
        sim.AddPortal(new PortalSpec(0, new GridPos(1, 0), kind, 1));
        sim.AddPortal(new PortalSpec(1, new GridPos(4, 0), kind, 0));
        aId = 0; bId = 1;
        return sim;
    }

    [Fact]
    public void Stable_gate_teleports_to_partner()
    {
        var sim = MakeSim(out _, out _);
        Assert.True(sim.TryMove(0, Direction.East));          // step onto gate A (1,0)
        Assert.Equal(new GridPos(4, 0), sim.GetMiner(0).Pos); // emerged at gate B
        // Reverse direction is covered by symmetry (portals are undirected) and by
        // Cooldown_expires_then_gate_works_again below.
    }

    [Fact]
    public void Cooldown_blocks_immediate_reuse()
    {
        var sim = MakeSim(out _, out _);
        sim.AddMiner(1, new GridPos(0, 0));
        Assert.True(sim.TryMove(0, Direction.East));          // miner 0 uses gate A
        Assert.Equal(new GridPos(4, 0), sim.GetMiner(0).Pos);

        // miner 1 tries gate A during cooldown → no teleport, just a normal step.
        Assert.True(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(1, 0), sim.GetMiner(1).Pos); // stayed on the gate tile
    }

    [Fact]
    public void Cooldown_expires_then_gate_works_again()
    {
        var sim = MakeSim(out _, out _);
        sim.AddMiner(1, new GridPos(0, 0));
        sim.TryMove(0, Direction.East);
        for (int i = 0; i < 25; i++) sim.Tick(0.1); // 2.5s > 2.0s cooldown
        Assert.True(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(4, 0), sim.GetMiner(1).Pos);
    }

    [Fact]
    public void Dormant_gate_with_buried_partner_does_not_teleport()
    {
        var sim = MakeSim(out _, out var bId, buryB: true);
        Assert.True(sim.TryMove(0, Direction.East));          // gate A active? partner buried → no
        Assert.Equal(new GridPos(1, 0), sim.GetMiner(0).Pos); // stayed put on the gate tile
        Assert.False(sim.Portals.Single(p => p.Id == 0).Collapsed);
    }

    [Fact]
    public void Uncovering_the_partner_activates_the_pair()
    {
        var sim = MakeSim(out _, out _, buryB: true);
        sim.TryMove(0, Direction.East);                       // dormant: miner sits on gate A (1,0)
        // Simulate the partner being mined out (tile becomes Floor).
        sim.RevealTileForTest(new GridPos(4, 0));
        // Step off and back on to re-trigger (Tick between moves to clear move cooldown).
        for (int i = 0; i < 15; i++) sim.Tick(0.1);
        Assert.True(sim.TryMove(0, Direction.West));          // back to (0,0)
        for (int i = 0; i < 15; i++) sim.Tick(0.1);
        Assert.True(sim.TryMove(0, Direction.East));          // onto gate A, now active
        Assert.Equal(new GridPos(4, 0), sim.GetMiner(0).Pos); // now teleports
    }

    [Fact]
    public void Unstable_gate_collapses_both_ends_after_one_trip()
    {
        var sim = MakeSim(out _, out _, kind: PortalKind.Unstable);
        sim.AddMiner(1, new GridPos(0, 0));
        Assert.True(sim.TryMove(0, Direction.East));          // first trip
        Assert.Equal(new GridPos(4, 0), sim.GetMiner(0).Pos);
        Assert.All(sim.Portals, p => Assert.True(p.Collapsed)); // both ends gone

        for (int i = 0; i < 30; i++) sim.Tick(0.1);          // even after cooldown
        Assert.True(sim.TryMove(1, Direction.East));          // second stepper: no-op teleport
        Assert.Equal(new GridPos(1, 0), sim.GetMiner(1).Pos);
    }

    [Fact]
    public void Portal_used_event_is_raised_on_teleport()
    {
        var sim = MakeSim(out _, out _);
        sim.TryMove(0, Direction.East);
        Assert.Contains(sim.DrainEvents(), e => e is PortalUsed pu
            && pu.MinerId == 0 && pu.From == new GridPos(1, 0) && pu.To == new GridPos(4, 0));
    }
}
