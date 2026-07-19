using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er.Core.AI;

/// <summary>Deterministic bot tactics for weaponizing mine carts in Expedition. Predicts where a
/// pushed cart ends up (mirroring <see cref="Simulation"/>.RollCart) and turns that into squash /
/// cart-bomb / lantern actions. Pure: reads only sim state and a seeded Random.</summary>
public static class CartTactics
{
    public readonly struct RollPrediction
    {
        public readonly IReadOnlyList<GridPos> Tiles;   // tiles the cart occupies in order (excl. start)
        public readonly int  MonstersSquashed;
        public readonly bool MinerInPath;               // a living miner sits on a rolled tile (teammate)
        public readonly bool Derails;                   // roll ends on a lethal tile (cart destroyed there)

        public RollPrediction(IReadOnlyList<GridPos> tiles, int squashed, bool minerInPath, bool derails)
        {
            Tiles = tiles; MonstersSquashed = squashed; MinerInPath = minerInPath; Derails = derails;
        }
    }

    /// <summary>Pure integer walk mirroring RollCart: rolls from <paramref name="cartPos"/> stepping
    /// <paramref name="dir"/> along contiguous track. Counts squashable monsters (rolls through them),
    /// flags any miner in the path, and derails on a lethal tile. A cart ahead stops the prediction
    /// (chain-push is intentionally not modeled — those opportunities are simply not taken).</summary>
    public static RollPrediction PredictRoll(Simulation sim, GridPos cartPos, Direction dir)
    {
        var off = dir.ToOffset();
        var tiles = new List<GridPos>();
        int squashed = 0;
        bool minerInPath = false;
        var pos = cartPos;
        int guard = 0;
        while (guard++ < 10000)
        {
            var next = new GridPos(pos.X + off.X, pos.Y + off.Y);
            if (!sim.IsTrack(next)) break;                       // track end → stop

            if (sim.Grid.Get(next).IsLethal())                  // hazard → derail at this tile
            {
                tiles.Add(next);
                return new RollPrediction(tiles, squashed, minerInPath, derails: true);
            }

            if (CartAt(sim, next) != null) break;               // cart ahead → don't model the train

            foreach (var mo in sim.Monsters)
                if (mo.Alive && mo.Pos == next && Simulation.IsSquashable(mo.Kind)) { squashed++; break; }

            foreach (var m in sim.Miners)
                if (m.Alive && m.Pos == next) { minerInPath = true; break; }

            tiles.Add(next);
            pos = next;
        }
        return new RollPrediction(tiles, squashed, minerInPath, derails: false);
    }

    // Nearest live cart on a tile, or null.
    private static CartReadModel? CartAt(Simulation sim, GridPos p)
    {
        foreach (var c in sim.Carts) if (c.Pos == p) return c;
        return null;
    }
}
