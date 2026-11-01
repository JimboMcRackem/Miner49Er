using Miner49er.Core;
using Xunit;

public class SimulationMinerSpacingTests
{
    private static Simulation Sim(GridPos? exit = null) =>
        new(new TileGrid(10, 3, TileType.Floor), new SimConfig { BaseMoveSeconds = 0.1, MinMoveSeconds = 0.1 },
            escapeTile: exit);

    private static void Run(Simulation sim, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds / 0.1); i++) sim.Tick(0.1);
    }

    [Fact]
    public void A_miner_cannot_step_onto_a_busy_miners_tile()
    {
        var sim = Sim();
        var mover = sim.AddMiner(1, new GridPos(2, 1));
        var other = sim.AddMiner(2, new GridPos(3, 1));
        other.Facing = Direction.East;
        Run(sim, 0.2);

        Assert.False(sim.TryMove(1, Direction.East));

        Assert.Equal(new GridPos(2, 1), mover.Pos);
        Assert.Equal(new GridPos(3, 1), other.Pos);
    }

    [Fact]
    public void A_dead_miner_does_not_block_the_tile()
    {
        var sim = Sim();
        var mover = sim.AddMiner(1, new GridPos(2, 1));
        sim.AddMiner(2, new GridPos(3, 1));
        sim.KillMiner(2);

        Assert.True(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(3, 1), mover.Pos);
    }

    [Fact]
    public void Miners_meeting_head_on_swap_places()
    {
        var sim = Sim();
        var a = sim.AddMiner(1, new GridPos(2, 1));
        var b = sim.AddMiner(2, new GridPos(3, 1));
        b.Facing = Direction.West;

        Assert.True(sim.TryMove(1, Direction.East));

        Assert.Equal(new GridPos(3, 1), a.Pos);
        Assert.Equal(new GridPos(2, 1), b.Pos);
    }

    [Fact]
    public void A_miner_left_standing_gives_way()
    {
        var sim = Sim();
        var a = sim.AddMiner(1, new GridPos(2, 1));
        var b = sim.AddMiner(2, new GridPos(3, 1));
        b.Facing = Direction.East;
        Run(sim, sim.Config.MinerGiveWaySeconds + 0.1);

        Assert.True(sim.TryMove(1, Direction.East));

        Assert.Equal(new GridPos(2, 1), b.Pos);
    }

    [Fact]
    public void A_miner_who_has_just_moved_does_not_give_way()
    {
        var sim = Sim();
        var a = sim.AddMiner(1, new GridPos(2, 1));
        var b = sim.AddMiner(2, new GridPos(4, 1));
        Run(sim, sim.Config.MinerGiveWaySeconds + 0.1);
        sim.TryMove(2, Direction.West);
        b.Facing = Direction.East;
        Run(sim, 0.2);

        Assert.False(sim.TryMove(1, Direction.East));
    }

    [Fact]
    public void A_miner_who_is_working_does_not_give_way()
    {
        var grid = new TileGrid(10, 3, TileType.Floor);
        grid.Set(new GridPos(4, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 30 });
        var a = sim.AddMiner(1, new GridPos(2, 1));
        var b = sim.AddMiner(2, new GridPos(3, 1));
        b.Facing = Direction.East;
        sim.TryStartMining(2);
        Run(sim, sim.Config.MinerGiveWaySeconds + 0.1);

        Assert.False(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(3, 1), b.Pos);
    }

    [Fact]
    public void The_whole_party_can_gather_on_the_exit()
    {
        var exit = new GridPos(3, 1);
        var sim = Sim(exit);
        var a = sim.AddMiner(1, new GridPos(2, 1));
        var b = sim.AddMiner(2, exit);
        b.Facing = Direction.East;
        Run(sim, 0.2);

        Assert.True(sim.TryMove(1, Direction.East));

        Assert.Equal(exit, a.Pos);
        Assert.Equal(exit, b.Pos);
    }

    [Fact]
    public void A_portal_with_someone_on_its_far_end_does_not_teleport()
    {
        var sim = Sim();
        sim.AddPortal(new PortalSpec(1, new GridPos(3, 1), PortalKind.Stable, 2));
        sim.AddPortal(new PortalSpec(2, new GridPos(8, 1), PortalKind.Stable, 1));
        var a = sim.AddMiner(1, new GridPos(2, 1));
        var b = sim.AddMiner(2, new GridPos(8, 1));

        sim.TryMove(1, Direction.East);

        Assert.Equal(new GridPos(3, 1), a.Pos);
    }

    [Fact]
    public void A_shared_spawn_moves_the_second_miner_to_a_free_tile()
    {
        var sim = Sim();
        var spawn = new GridPos(4, 1);
        sim.AddMiner(1, sim.FreeSpawnNear(spawn));

        var second = sim.FreeSpawnNear(spawn);

        Assert.NotEqual(spawn, second);
        Assert.Equal(1, second.ManhattanTo(spawn));
    }
}
