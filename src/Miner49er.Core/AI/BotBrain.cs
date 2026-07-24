using System;
using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er.Core.AI;

public sealed class BotBrain
{
    public int MinerId { get; }
    public BotSkill Skill { get; }

    private GridPos? _goal;
    private int _ticksUntilReeval;
    private readonly Random _rng;
    private readonly HashSet<GridPos> _knownMines = new();
    private bool _hasWhistled;
    private int _listenTicksRemaining;
    private const double ListenChance = 0.003;     // ~ once per 11 s at 30 Hz when safe
    private const int    ListenDurationTicks = 40; // ~1.3 s pose

    private static readonly Direction[] AllDirs =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    public BotBrain(int minerId, BotSkill skill, int seed)
    {
        MinerId = minerId;
        Skill   = skill;
        _rng    = new Random(seed);
        _ticksUntilReeval = 0; // force goal pick on first Think()
    }

    public BotAction Think(Simulation sim, GameMode mode)
    {
        var miner = sim.GetMiner(MinerId);
        if (!miner.Alive) return BotAction.Idle;

        // In Expedition, snap goal to escape the moment it opens so bots react immediately.
        if (mode == GameMode.Expedition && sim.EscapeOpen && sim.EscapeTile is { } escOpen && _goal != escOpen)
        {
            _goal = escOpen;
            _ticksUntilReeval = 0;
        }

        // Re-arm the whistle each floor (escape starts closed on a fresh floor).
        bool escapeOpenNow = mode == GameMode.Expedition && sim.EscapeOpen;
        if (!escapeOpenNow) _hasWhistled = false;

        // First time a Miner+ bot is standing on the open exit, whistle to rally the team.
        if (escapeOpenNow && Skill >= BotSkill.Miner && sim.EscapeTile is { } whistleTile
            && miner.Pos == whistleTile && !_hasWhistled)
        {
            _hasWhistled = true;
            return new BotAction(-1, whistle: true);
        }

        // Let any in-progress activity finish — sending a direction cancels mining.
        if (miner.Activity != ActivityKind.None) return BotAction.Idle;

        bool isPvP  = mode != GameMode.Expedition;
        // Grudge Match is a free-for-all deathmatch — bots fight exactly like Demolition Derby.
        bool derby  = mode is GameMode.DemolitionDerby or GameMode.GrudgeMatch;
        bool lms    = mode == GameMode.LastManStanding;

        _ticksUntilReeval--;
        if (_ticksUntilReeval <= 0)
        {
            _goal = PickGoal(sim, mode, miner);
            _ticksUntilReeval = RevalInterval();
        }

        // Foreman/Dan: use beneficial held items immediately (skips movement this tick)
        if (Skill >= BotSkill.Foreman && miner.Held is ItemKind.SpeedPotion or ItemKind.LongerVision)
            return new BotAction(-1, use: true);

        // Explosive avoidance — skill-gated; Greenhorn is oblivious
        if (Skill > BotSkill.Greenhorn)
        {
            var danger = NearestDangerousCharge(sim, miner);
            if (danger != null)
            {
                var fleeTarget = FleeFrom(sim.Grid, miner.Pos, danger.WallPos);
                if (fleeTarget != null)
                {
                    int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false, avoidHazards: Skill >= BotSkill.Miner);
                    if (fleeDir != -1)
                    {
                        _ticksUntilReeval = 0; // re-pick goal once clear
                        return new BotAction(fleeDir);
                    }
                }
            }
        }

        // Cart squash: Miner+ shove a handy cart to crush a monster instead of fleeing. Checked
        // before the flee block so a bot that can squash does; otherwise it falls through and flees.
        {
            var squash = CartTactics.TrySquash(sim, miner, Skill);
            if (squash != null) { _ticksUntilReeval = 0; return squash.Value; }
        }

        // Cart-bomb: DynamiteDan arms & launches a rolling cart-bomb at a monster cluster / gold seam.
        {
            var bomb = CartTactics.TryBomb(sim, miner, Skill);
            if (bomb != null) { _ticksUntilReeval = 0; return bomb.Value; }
        }

        // Monster avoidance: Miner+ flees monsters within 2 tiles
        if (Skill >= BotSkill.Miner)
        {
            var nearestMonster = NearestMonsterPos(sim, miner);
            if (nearestMonster.HasValue && miner.Pos.ChebyshevTo(nearestMonster.Value) <= 2)
            {
                var fleeTarget = FleeFrom(sim.Grid, miner.Pos, nearestMonster.Value);
                if (fleeTarget != null)
                {
                    int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false, avoidHazards: Skill >= BotSkill.Miner);
                    if (fleeDir != -1)
                    {
                        _ticksUntilReeval = 0;
                        return new BotAction(fleeDir);
                    }
                }
            }
        }

        // Rock fall avoidance: all skill levels flee a pending fall within 3 tiles.
        foreach (var pf in sim.PendingFalls)
        {
            if (miner.Pos.ChebyshevTo(pf.Pos) > 3) continue;
            var fleeTarget = FleeFrom(sim.Grid, miner.Pos, pf.Pos);
            if (fleeTarget != null)
            {
                int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false, avoidHazards: Skill >= BotSkill.Miner);
                if (fleeDir != -1)
                {
                    _ticksUntilReeval = 0;
                    return new BotAction(fleeDir);
                }
            }
            break; // only react to the nearest fall
        }

        // Trip mine detection: skill-gated chance to notice and avoid planted mines.
        // Greenhorn is oblivious; higher skills detect at increasing ranges and rates.
        double mineDetectChance = Skill switch
        {
            BotSkill.Miner       => 0.008,   // ~25% chance per second at 30 Hz
            BotSkill.Foreman     => 0.025,   // ~55% per second
            BotSkill.DynamiteDan => 1.0,     // instant detection
            _ => 0.0,
        };
        int mineDetectRange = Skill switch
        {
            BotSkill.Miner       => 4,
            BotSkill.Foreman     => 5,
            BotSkill.DynamiteDan => 6,
            _ => 0,
        };
        if (mineDetectChance > 0)
        {
            // Discard triggered mines from our known set
            if (_knownMines.Count > 0)
            {
                var active = new HashSet<GridPos>();
                foreach (var tc in sim.TripCharges) active.Add(tc.Pos);
                _knownMines.IntersectWith(active);
            }
            // Detection attempt for each undiscovered mine in range
            foreach (var tc in sim.TripCharges)
            {
                if (!_knownMines.Contains(tc.Pos)
                    && miner.Pos.ManhattanTo(tc.Pos) <= mineDetectRange
                    && _rng.NextDouble() < mineDetectChance)
                    _knownMines.Add(tc.Pos);
            }
            // Flee any known mine that's adjacent (don't step on it)
            foreach (var minePos in _knownMines)
            {
                if (miner.Pos.ManhattanTo(minePos) > 1) continue;
                var fleeTarget = FleeFrom(sim.Grid, miner.Pos, minePos);
                if (fleeTarget == null) continue;
                int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false, avoidHazards: Skill >= BotSkill.Miner);
                if (fleeDir != -1) { _ticksUntilReeval = 0; return new BotAction(fleeDir); }
            }
        }

        // Rival proximity (PvP, defensive): Miner+ flees when a rival is adjacent,
        // unless in a mode where this bot is already chasing rivals (handled by attack swing instead).
        // In LMS every hunter presses the attack instead of backing off.
        if (isPvP && Skill >= BotSkill.Miner && !derby && !lms)
        {
            var nearestRival = NearestRivalPos(sim, miner);
            // Don't flee a rival that's banking a prize — press in to contest (stun) it instead.
            if (nearestRival.HasValue && miner.Pos.ChebyshevTo(nearestRival.Value) <= 1
                && !RivalIsPrizeClaimant(sim, nearestRival.Value, miner.Id))
            {
                var fleeTarget = FleeFrom(sim.Grid, miner.Pos, nearestRival.Value);
                if (fleeTarget != null)
                {
                    int fleeDir = BotPathfinder.NextDir(sim.Grid, miner.Pos, fleeTarget.Value, passRock: false, avoidHazards: Skill >= BotSkill.Miner);
                    if (fleeDir != -1) { _ticksUntilReeval = 0; return new BotAction(fleeDir); }
                }
            }
        }

        // Cosmetic listen pose: Miner+ occasionally pauses to "listen" when nothing urgent is
        // happening (no hazard fled this tick, not racing for the exit). Purely visual.
        bool escapeUrgentNow = mode == GameMode.Expedition && sim.EscapeOpen;

        // Foreman+ : occasionally grab a lantern off a nearby cart when nothing urgent is happening.
        if (!escapeUrgentNow)
        {
            var lantern = CartTactics.TryLantern(sim, miner, Skill, _rng);
            if (lantern != null) return lantern.Value;
        }

        if (Skill >= BotSkill.Miner && !escapeUrgentNow)
        {
            if (_listenTicksRemaining > 0)
            {
                _listenTicksRemaining--;
                return new BotAction(-1, listen: true);
            }
            if (_rng.NextDouble() < ListenChance)
            {
                _listenTicksRemaining = ListenDurationTicks;
                return new BotAction(-1, listen: true);
            }
        }

        // Prize event (competitive modes): divert to seek/contest an active prize. Runs after
        // every hazard-flee block (never walk into a fuse for a coin) but before the treasure
        // carry logic, so an idol/urn carrier's own objective still wins. Reassigns the goal
        // every tick because CarryRelic rides its carrier.
        var prizeGoal = PrizeGoal(sim, mode, miner);
        if (prizeGoal.HasValue) { _goal = prizeGoal; _ticksUntilReeval = 0; }

        // Treasure Hunt: place chest, pick up idol, navigate to deposit.
        if (mode == GameMode.TreasureHunt)
        {
            var (idolA, idolB) = TreasureAssignment.For(sim.Config.Seed, MinerId);

            // Place chest at current tile the moment we're idle.
            if (miner.Held == ItemKind.TreasureChest)
                return new BotAction(-1, use: true);

            // Pick up assigned idol when standing on it.
            foreach (var it in sim.Items)
                if (it.Placement == ItemPlacement.Loose && it.Pos == miner.Pos
                    && (it.Kind == idolA || it.Kind == idolB))
                    return new BotAction(-1, use: true);

            // While carrying an assigned idol, override goal to own chest tile.
            if (miner.Held is { } h && h.IsIdol() && (h == idolA || h == idolB))
            {
                var chestAt = sim.ChestPosFor(MinerId);
                if (chestAt.HasValue) { _goal = chestAt.Value; _ticksUntilReeval = 3; }
            }
        }

        // Treasure Heist: carry it to safety, chase the carrier, or seek the (loose/buried) urn.
        if (mode == GameMode.TreasureHeist)
        {
            if (sim.TreasureHolderId == MinerId)
            {
                // I'm carrying it — flee the nearest rival. No rival in sight: keep current goal.
                var r = NearestRivalPos(sim, miner);
                _goal = (r is { } rv ? FleeFrom(sim.Grid, miner.Pos, rv) : null) ?? _goal;
            }
            else if (sim.TreasureHolderId >= 0)
            {
                // A rival carries it — chase the carrier; re-evaluate every tick since they move.
                _goal = sim.GetMiner(sim.TreasureHolderId).Pos;
                _ticksUntilReeval = 0;
            }
            else
            {
                // Loose or still buried — head for its known position either way.
                _goal = sim.TreasurePos;
            }
        }

        // Dan (LMS / Derby): bombard from range. If a rival sits in the current facing line ≥2
        // tiles out (self-preservation) and within throw range, lob dynamite instead of closing.
        if ((lms || derby) && Skill == BotSkill.DynamiteDan && sim.Config.DynamiteEnabled
            && miner.DynamiteThrowCooldown <= 0 && RivalInThrowLine(sim, miner))
            return new BotAction(-1, throwDynamite: true);

        if (_goal == null) return BotAction.Idle;

        bool passRock = Skill >= BotSkill.Foreman
            || (mode == GameMode.ReachCenter && miner.GoldCollected >= 5)
            || (mode == GameMode.TreasureHeist && sim.TreasureHolderId < 0);
        bool hazardAware = Skill >= BotSkill.Miner;
        // Carts block their tile like a wall — route around them.
        IReadOnlySet<GridPos>? cartTiles = sim.Carts.Count > 0
            ? sim.Carts.Select(c => c.Pos).ToHashSet() : null;
        int dir = BotPathfinder.NextDir(sim.Grid, miner.Pos, _goal.Value, passRock, avoidHazards: hazardAware, blocked: cartTiles);
        // Two-pass fallback: if hazards box in the only route, accept the risk rather than freeze.
        if (dir == -1 && hazardAware)
            dir = BotPathfinder.NextDir(sim.Grid, miner.Pos, _goal.Value, passRock, avoidHazards: false, blocked: cartTiles);

        if (dir == -1) { _ticksUntilReeval = 0; return BotAction.Idle; }

        var off      = ((Direction)dir).ToOffset();
        var nextPos  = new GridPos(miner.Pos.X + off.X, miner.Pos.Y + off.Y);
        var nextTile = sim.Grid.InBounds(nextPos) ? sim.Grid.Get(nextPos) : TileType.Rock;
        bool mine = nextTile.IsMinable();

        // Greenhorn: occasionally swing pickaxe at an adjacent minable tile even when not
        // routing through rock (the pathfinder avoids rock, so mine=false most of the time).
        // Skip when escape is open — bots must reach the exit.
        bool escapeUrgent = mode == GameMode.Expedition && sim.EscapeOpen;
        if (Skill == BotSkill.Greenhorn && !mine && !escapeUrgent && _rng.NextDouble() < 0.10)
        {
            foreach (var d2 in AllDirs)
            {
                var off2 = d2.ToOffset();
                var nb = new GridPos(miner.Pos.X + off2.X, miner.Pos.Y + off2.Y);
                if (sim.Grid.InBounds(nb) && sim.Grid.Get(nb).IsMinable())
                    return new BotAction((int)d2, mine: true);
            }
        }

        // Aggressive swing: Derby/LMS-pursuing bots swing (pickaxe stun) at a rival in the step direction.
        // In LMS every tier swings — hunting is the whole point of the mode.
        bool aggressiveTowardRivals = isPvP && (derby || lms
            || (mode == GameMode.TreasureHeist && sim.TreasureHolderId >= 0 && sim.TreasureHolderId != MinerId));
        if (aggressiveTowardRivals && !mine && sim.Grid.InBounds(nextPos) && RivalAt(sim, nextPos, miner.Id))
            mine = true;

        // Prize contest (even in non-combat modes): Miner+ swing at the specific rival banking a
        // claim-over-time prize (MineOut/HoldPoint/CarryRelic set PrizeHolderId). A stun resets
        // their progress / drops the relic. Narrow: only the holder, never bystanders.
        if (!mine && Skill >= BotSkill.Miner && sim.Grid.InBounds(nextPos)
            && RivalIsPrizeClaimant(sim, nextPos, miner.Id))
            mine = true;

        // Hold the current goal while actively mining so the bot doesn't drift mid-swing
        if (mine)
            _ticksUntilReeval = Math.Max(_ticksUntilReeval, 30);

        bool plant = derby
            ? Skill >= BotSkill.Miner && nextTile.IsMinable()
            : lms
                ? (Skill >= BotSkill.Foreman && NearestRivalDist(sim, miner) <= 2 && nextTile.IsMinable())
                    || (Skill == BotSkill.DynamiteDan && GoldClusterAdjacent(sim.Grid, miner.Pos))
                : Skill == BotSkill.DynamiteDan && GoldClusterAdjacent(sim.Grid, miner.Pos);
        bool rivalInStoneRange = miner.StoneCount > 0 && NearestRivalDist(sim, miner) <= 2;
        bool throwStone = rivalInStoneRange && (
            derby ? Skill >= BotSkill.Miner
            : lms ? Skill >= BotSkill.Miner
            : Skill == BotSkill.DynamiteDan
              || (mode == GameMode.TreasureHeist && Skill >= BotSkill.Miner
                  && sim.TreasureHolderId >= 0 && sim.TreasureHolderId != MinerId));

        return new BotAction(dir, mine, plant, throwStone: throwStone);
    }

    // ── Goal selection ─────────────────────────────────────────────────────

    private GridPos? PickGoal(Simulation sim, GameMode mode, Miner miner)
    {
        // All skill levels rush the exit when it opens — floor won't advance until every alive miner is there.
        if (mode == GameMode.Expedition && sim.EscapeOpen && sim.EscapeTile is { } esc)
            return esc;

        if (mode is GameMode.DemolitionDerby or GameMode.GrudgeMatch) return DerbyGoal(sim, miner);
        if (mode == GameMode.LastManStanding)
        {
            // LMS is won by being the last miner alive, so gold is irrelevant — hunt rivals.
            // Pursuit range scales with skill: Greenhorn only stumbles toward nearby rivals,
            // Miner tracks at mid-range, Foreman/Dan lock on across the whole map.
            int huntRange = Skill switch
            {
                BotSkill.Greenhorn => 8,
                BotSkill.Miner     => 12,
                _ => int.MaxValue, // Foreman/Dan: map-wide
            };
            var nearest = NearestRivalPos(sim, miner);
            if (nearest.HasValue && miner.Pos.ChebyshevTo(nearest.Value) <= huntRange)
                return nearest;
            // No rival in range — fall through to the skill's default (wander / gold).
        }
        return Skill switch
        {
            BotSkill.Greenhorn   => RandomFloor(sim.Grid, miner.Pos),
            BotSkill.Miner       => MinerGoal(sim, mode, miner),
            BotSkill.Foreman     => ForemanGoal(sim, mode, miner),
            BotSkill.DynamiteDan => ForemanGoal(sim, mode, miner),
            _ => null,
        };
    }

    private GridPos? DerbyGoal(Simulation sim, Miner miner)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var m in sim.Miners)
        {
            if (m.Id == miner.Id || !m.Alive) continue;
            int d = miner.Pos.ChebyshevTo(m.Pos);
            if (d < bestDist) { bestDist = d; best = m.Pos; }
        }
        return best ?? RandomFloor(sim.Grid, miner.Pos);
    }

    private GridPos? RandomFloor(TileGrid grid, GridPos from)
    {
        for (int i = 0; i < 20; i++)
        {
            var p = new GridPos(from.X + _rng.Next(-6, 7), from.Y + _rng.Next(-6, 7));
            if (grid.InBounds(p) && grid.Get(p).IsWalkable()) return p;
        }
        return null;
    }

    private GridPos? MinerGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.ReachCenter && sim.Center is { } center && miner.GoldCollected >= 5)
            return center;

        if (mode == GameMode.TreasureHunt)
        {
            var (a, b) = TreasureAssignment.For(sim.Config.Seed, MinerId);
            foreach (var item in sim.Items)
                if (item.Placement == ItemPlacement.Loose && (item.Kind == a || item.Kind == b))
                    return item.Pos;
        }

        // Nearest GoldRock by Manhattan distance
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var p in sim.Grid.Positions())
        {
            if (sim.Grid.Get(p) != TileType.GoldRock) continue;
            int d = miner.Pos.ManhattanTo(p);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best ?? RandomFloor(sim.Grid, miner.Pos);
    }

    private GridPos? ForemanGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.ReachCenter && sim.Center is { } center && miner.GoldCollected >= 5)
            return center;

        if (mode == GameMode.Expedition && sim.EscapeOpen && sim.EscapeTile is { } esc)
            return esc;

        if (mode == GameMode.TreasureHunt) return TreasureGoal(sim, miner);

        // Nearest GoldRock by BFS (plans through walls)
        var gold = new List<GridPos>();
        foreach (var p in sim.Grid.Positions())
            if (sim.Grid.Get(p) == TileType.GoldRock) gold.Add(p);
        return BotPathfinder.Nearest(sim.Grid, miner.Pos, gold, passRock: true)
               ?? RandomFloor(sim.Grid, miner.Pos);
    }

    private GridPos? TreasureGoal(Simulation sim, Miner miner)
    {
        var (a, b) = TreasureAssignment.For(sim.Config.Seed, MinerId);
        // Only navigate to already-discovered (loose) idols — buried positions are unknown.
        foreach (var item in sim.Items)
            if (item.Placement == ItemPlacement.Loose && (item.Kind == a || item.Kind == b))
                return item.Pos;
        // No loose idol yet — mine toward gold; buried idols surface when rock is cleared.
        var gold = new List<GridPos>();
        foreach (var p in sim.Grid.Positions())
            if (sim.Grid.Get(p) == TileType.GoldRock) gold.Add(p);
        return BotPathfinder.Nearest(sim.Grid, miner.Pos, gold, passRock: true)
               ?? RandomFloor(sim.Grid, miner.Pos);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool GoldClusterAdjacent(TileGrid grid, GridPos pos)
    {
        int count = 0;
        foreach (var d in AllDirs)
        {
            var off = d.ToOffset();
            var nb  = new GridPos(pos.X + off.X, pos.Y + off.Y);
            if (grid.InBounds(nb) && grid.Get(nb) == TileType.GoldRock) count++;
        }
        return count >= 3;
    }

    private static int NearestRivalDist(Simulation sim, Miner self)
    {
        int min = int.MaxValue;
        foreach (var m in sim.Miners)
        {
            if (m.Id == self.Id || !m.Alive) continue;
            int d = self.Pos.ChebyshevTo(m.Pos);
            if (d < min) min = d;
        }
        return min;
    }

    private static bool RivalAt(Simulation sim, GridPos pos, int selfId)
    {
        foreach (var m in sim.Miners)
            if (m.Id != selfId && m.Alive && m.Pos == pos) return true;
        return false;
    }

    // True if a living rival stands in the bot's current facing line, at least 2 tiles out
    // (so a lobbed stick doesn't land on the thrower) and within throw range, with only
    // enterable tiles in between (a wall blocks the lob).
    private static bool RivalInThrowLine(Simulation sim, Miner miner)
    {
        var off = miner.Facing.ToOffset();
        for (int i = 1; i <= sim.Config.ThrownDynamiteRange; i++)
        {
            var p = new GridPos(miner.Pos.X + off.X * i, miner.Pos.Y + off.Y * i);
            if (!sim.Grid.InBounds(p) || !sim.Grid.Get(p).IsEnterable()) break;
            if (i >= 2 && RivalAt(sim, p, miner.Id)) return true;
        }
        return false;
    }

    // Where a prize-seeking bot should head, or null if there's no active prize worth chasing.
    // CarryRelic: if I'm the holder, bank it at my spawn; otherwise (and for every other type)
    // head for the prize tile (which rides the carrier for an already-grabbed relic). Commitment
    // range scales with skill so lower tiers only notice a prize that's close.
    private GridPos? PrizeGoal(Simulation sim, GameMode mode, Miner miner)
    {
        if (mode == GameMode.Expedition || sim.PrizeState != PrizeState.Active) return null;

        // Carrying the relic home is a committed objective — pursue it regardless of range.
        if (sim.PrizeType == PrizeType.CarryRelic && sim.PrizeHolderId == miner.Id)
            return miner.SpawnPos;

        int commitRange = Skill switch
        {
            BotSkill.Greenhorn => 8,
            BotSkill.Miner     => 14,
            _ => int.MaxValue, // Foreman/Dan: map-wide
        };
        return miner.Pos.ChebyshevTo(sim.PrizePos) <= commitRange ? sim.PrizePos : null;
    }

    // True if the rival at pos is the one currently banking a claim-over-time prize
    // (MineOut/HoldPoint/CarryRelic populate PrizeHolderId; GrabAndGo is instant and never does).
    private static bool RivalIsPrizeClaimant(Simulation sim, GridPos pos, int selfId)
    {
        if (sim.PrizeState != PrizeState.Active || sim.PrizeHolderId < 0 || sim.PrizeHolderId == selfId)
            return false;
        foreach (var m in sim.Miners)
            if (m.Id != selfId && m.Alive && m.Pos == pos && m.Id == sim.PrizeHolderId) return true;
        return false;
    }

    private static GridPos? NearestRivalPos(Simulation sim, Miner self)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var m in sim.Miners)
        {
            if (m.Id == self.Id || !m.Alive) continue;
            int d = self.Pos.ChebyshevTo(m.Pos);
            if (d < bestDist) { bestDist = d; best = m.Pos; }
        }
        return best;
    }

    private static GridPos? NearestMonsterPos(Simulation sim, Miner self)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        foreach (var mo in sim.Monsters)
        {
            if (!mo.Alive) continue;
            int d = self.Pos.ChebyshevTo(mo.Pos);
            if (d < bestDist) { bestDist = d; best = mo.Pos; }
        }
        return best;
    }

    // Override the bot's next goal to the escape tile for ~4 s (120 ticks at 30 Hz).
    // The bot finishes any in-progress activity first, then heads for the exit.
    public void ForceEscape(GridPos escapeTile)
    {
        _goal = escapeTile;
        _ticksUntilReeval = 120;
    }

    private int RevalInterval() => Skill switch
    {
        BotSkill.Greenhorn   => 30,
        BotSkill.Miner       => 15,
        BotSkill.Foreman     => 7,
        BotSkill.DynamiteDan => 3,
        _ => 15,
    };

    // Returns the nearest charge worth fleeing, or null if none.
    // Miner: reacts within 1 tile, fuse ≤ 1.5 s.
    // Foreman: 2 tiles, fuse ≤ 2.5 s.
    // Dan: 3 tiles, any fuse, but ignores own charges beyond adjacent.
    private Charge? NearestDangerousCharge(Simulation sim, Miner miner)
    {
        int distThreshold = Skill switch
        {
            BotSkill.Miner       => 1,
            BotSkill.Foreman     => 2,
            BotSkill.DynamiteDan => 3,
            _ => 0,
        };
        double fuseThreshold = Skill switch
        {
            BotSkill.Miner       => 1.5,
            BotSkill.Foreman     => 2.5,
            BotSkill.DynamiteDan => double.MaxValue, // Dan reacts immediately
            _ => 0,
        };

        Charge? nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var charge in sim.Charges)
        {
            // Dan ignores charges it planted unless they're right next door
            if (Skill == BotSkill.DynamiteDan && charge.OwnerId == MinerId
                && miner.Pos.ChebyshevTo(charge.WallPos) > 1)
                continue;

            int d = miner.Pos.ChebyshevTo(charge.WallPos);
            if (d <= distThreshold && charge.FuseRemaining <= fuseThreshold && d < nearestDist)
            {
                nearestDist = d;
                nearest = charge;
            }
        }
        return nearest;
    }

    // Finds a walkable floor tile several steps away from the danger position.
    private static GridPos? FleeFrom(TileGrid grid, GridPos pos, GridPos danger)
    {
        int dx = pos.X - danger.X;
        int dy = pos.Y - danger.Y;
        int nx = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int ny = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

        for (int steps = 5; steps >= 2; steps--)
        {
            var candidates = new[]
            {
                new GridPos(pos.X + nx * steps, pos.Y + ny * steps),
                new GridPos(pos.X + nx * steps, pos.Y),
                new GridPos(pos.X,               pos.Y + ny * steps),
            };
            foreach (var c in candidates)
                if (grid.InBounds(c) && grid.Get(c).IsWalkable()) return c;
        }
        return null;
    }
}
