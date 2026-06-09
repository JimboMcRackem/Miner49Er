using Miner49er.Core;
using Xunit;

public class MovementCadenceTests
{
    private static Simulation FloorSim(SimConfig? cfg = null)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg ?? new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        return sim;
    }

    [Fact]
    public void Second_move_within_cooldown_is_rejected()
    {
        var sim = FloorSim();
        Assert.True(sim.TryMove(1, Direction.East));
        Assert.False(sim.TryMove(1, Direction.East));         // cooldown still active
        Assert.Equal(new GridPos(3, 2), sim.GetMiner(1).Pos); // did not advance
    }

    [Fact]
    public void Move_allowed_again_after_cooldown_elapses()
    {
        var sim = FloorSim();
        sim.TryMove(1, Direction.East);
        sim.Tick(0.2); // > 0.12 standard cadence
        Assert.True(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(4, 2), sim.GetMiner(1).Pos);
    }

    [Fact]
    public void Standard_floor_cadence_equals_base_move_seconds()
    {
        var sim = FloorSim();
        sim.TryMove(1, Direction.East);
        Assert.Equal(0.12, sim.GetMiner(1).MoveCooldownRemaining, 3);
    }

    [Fact]
    public void Shallow_water_doubles_the_cadence()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.ShallowWater);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East); // onto shallow (3,2)
        Assert.Equal(0.24, sim.GetMiner(1).MoveCooldownRemaining, 3); // 0.12 * 2.0
    }

    [Fact]
    public void Speed_effect_reduces_the_cadence()
    {
        var sim = FloorSim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
        sim.TryMove(1, Direction.East);
        Assert.Equal(0.072, sim.GetMiner(1).MoveCooldownRemaining, 3); // 0.12 * 0.6
    }

    [Fact]
    public void Two_move_speed_effects_multiply()
    {
        var sim = FloorSim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.5, 5.0);
        sim.ApplyEffect(1, EffectKind.DebugSlow,  EffectChannel.MoveSpeed, 1.5, 5.0);
        Assert.Equal(0.09, sim.EffectiveMoveSeconds(1), 3); // 0.12 * 0.5 * 1.5
    }

    [Fact]
    public void Cadence_is_clamped_to_min()
    {
        var sim = FloorSim(new SimConfig { MinMoveSeconds = 0.05 });
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.1, 5.0); // 0.012 < 0.05
        Assert.Equal(0.05, sim.EffectiveMoveSeconds(1), 3);
    }

    [Fact]
    public void Cadence_is_clamped_to_max()
    {
        var sim = FloorSim(new SimConfig { MaxMoveSeconds = 0.40 });
        sim.ApplyEffect(1, EffectKind.DebugSlow, EffectChannel.MoveSpeed, 5.0, 5.0); // 0.6 > 0.40
        Assert.Equal(0.40, sim.EffectiveMoveSeconds(1), 3);
    }
}
