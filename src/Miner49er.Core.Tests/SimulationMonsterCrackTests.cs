using Miner49er.Core;
using System.Linq;
using Xunit;

// Ground monsters (zombies, slimes, skeletons, goats, water snakes) load cracked
// floor exactly like miners: crossing a fresh Cracked tile wears it to Crumbling,
// stepping onto a Crumbling tile drops them into a Pit, and lingering collapses it.
// Ghosts float over cracks; the SkeletonDino keeps its own (more destructive) rule.
public class SimulationMonsterCrackTests
{
    private static SimConfig Cfg() => new SimConfig
    {
        MonsterZombieMoveSeconds = 0.2,
        MonsterGhostMoveSeconds  = 0.2,
        CrackDwellSeconds        = 0.75,
    };

    private static TileGrid MakeGrid(int w = 15, int h = 15)
    {
        var g = new TileGrid(w, h, TileType.Floor);
        for (int x = 0; x < w; x++) { g.Set(new GridPos(x, 0), TileType.Rock); g.Set(new GridPos(x, h - 1), TileType.Rock); }
        for (int y = 0; y < h; y++) { g.Set(new GridPos(0, y), TileType.Rock); g.Set(new GridPos(w - 1, y), TileType.Rock); }
        return g;
    }

    [Fact]
    public void Zombie_steps_onto_crumbling_and_falls()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(4, 7), TileType.Crumbling);
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(2, 7));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.ZombieMiner);

        sim.Tick(0.3); // one step west: (5,7) -> (4,7) crumbling -> collapse

        Assert.False(sim.Monsters.First().Alive);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(4, 7)));
    }

    [Fact]
    public void Zombie_crossing_fresh_crack_wears_it_to_crumbling()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(4, 7), TileType.Cracked);
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(2, 7));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.ZombieMiner);

        // Each Tick advances a monster at most one step, so tick until it has
        // stepped onto the crack (surviving the first crossing) and then off it.
        for (int i = 0; i < 3; i++) sim.Tick(0.2);

        Assert.True(sim.Monsters.First().Alive);
        Assert.Equal(TileType.Crumbling, grid.Get(new GridPos(4, 7)));
    }

    [Fact]
    public void Zombie_lingering_on_a_crack_falls_through()
    {
        var grid = MakeGrid();
        // Box the zombie in on a cracked tile so it cannot move and must dwell.
        grid.Set(new GridPos(7, 7), TileType.Cracked);
        grid.Set(new GridPos(6, 7), TileType.Rock);
        grid.Set(new GridPos(8, 7), TileType.Rock);
        grid.Set(new GridPos(7, 6), TileType.Rock);
        grid.Set(new GridPos(7, 8), TileType.Rock);
        var sim = new Simulation(grid, Cfg());
        sim.AddMonster(1, new GridPos(7, 7), MonsterKind.ZombieMiner);

        sim.Tick(1.0); // dwell exceeds 0.75s -> collapse

        Assert.False(sim.Monsters.First().Alive);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(7, 7)));
    }

    [Fact]
    public void Ghost_floats_over_crumbling_without_collapsing_it()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(4, 7), TileType.Crumbling);
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(2, 7));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.Ghost);

        sim.Tick(0.5); // ghost crosses (4,7) toward the miner

        Assert.True(sim.Monsters.First().Alive);
        Assert.Equal(TileType.Crumbling, grid.Get(new GridPos(4, 7)));
    }
}
