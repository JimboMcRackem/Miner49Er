using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.AI;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Host-only authoritative simulation driver. Steps a fixed 30 Hz tick,
/// applies queued inputs, and broadcasts a TickUpdate each step via NetworkManager.
/// Manages lives, permanent buff levels, and cumulative gold across floors.</summary>
public partial class MatchHost : Node
{
	public const double TickSeconds = 1.0 / 30.0;

	private Simulation _sim = null!;
	private readonly Dictionary<long, int> _peerToMiner = new();
	private readonly Dictionary<int, int> _pendingDir   = new();
	private readonly HashSet<int> _pendingMine  = new();
	private readonly HashSet<int> _pendingPlant = new();
	private readonly HashSet<int> _pendingUse   = new();
	private readonly HashSet<int> _pendingThrow = new();
	private readonly HashSet<int> _pendingThrowDynamite = new();
	private readonly List<WhistleSnapshot> _botWhistles = new();
	private readonly List<PortalUseSnapshot> _portalUses = new();

	private int _tick;
	private double _accum;
	private bool _running;

	private int _livesRemaining;
	private int _livesMax;
	private int _cumulativeGold;
	public int CumulativeGold => _cumulativeGold;
	private readonly Dictionary<int, (int Speed, int Vision, int Blast)> _permLevels = new();

	private readonly Dictionary<int, GridPos>   _spawnByMiner = new();
	private readonly Dictionary<int, BotBrain> _botBrains    = new();
	private readonly Dictionary<int, long>     _botMinerToPeer = new();
	private bool _respawnPending;
	private double _respawnTimer;
	private const double RespawnDelay = 2.0;

	// Co-op expedition: per-miner respawn tracking.
	private readonly HashSet<int> _deadMiners = new();
	private readonly Dictionary<int, double> _coopRespawnTimers = new();

	// Human miner IDs (excludes bots); used for bot-aware game-over logic.
	private readonly HashSet<int> _humanMinerIds = new();

	// True when deaths draw from the shared life pool with per-miner respawns: any
	// Expedition run with more than one being in the party Ã¢â‚¬â€ multiple humans, or bots
	// present. A death (human OR bot) spends one shared life and the being respawns only
	// while lives remain. Pure solo (one human, no bots) keeps the full-wipe respawn model.
	private bool UsesPerMinerRespawn =>
		NetworkManager.Instance.MatchMode == GameMode.Expedition
		&& (NetworkManager.Instance.MatchPlayerCount > 1 || _botBrains.Count > 0);

	// Co-op Expedition: floor-clear choice pending (host awaits "Continue" or "Return to Menu").
	private bool _floorChoicePending;
	private int  _floorChoiceWinnerId;
	private Dictionary<int, int>? _floorChoiceCarryGold;

	public void Begin(Simulation sim, Dictionary<long, int> peerToMiner,
		List<(int minerId, BotSkill skill)>? bots = null,
		Dictionary<int, long>? botMinerToPeer = null)
	{
		_sim = sim;
		_botBrains.Clear();
		_botMinerToPeer.Clear();
		_humanMinerIds.Clear();
		foreach (var (peer, miner) in peerToMiner)
		{
			_peerToMiner[peer] = miner;
			_pendingDir[miner] = -1;
			_humanMinerIds.Add(miner);
		}
		var nm       = NetworkManager.Instance;
		_livesMax       = nm.MatchMode == GameMode.Expedition
			? (nm.MatchPlayerCount == 1 ? 3 : 2 * nm.MatchPlayerCount)
			: 1;
		_livesRemaining = _livesMax;
		_running = true;
		_spawnByMiner.Clear();
		foreach (var (_, mid) in peerToMiner)
			_spawnByMiner[mid] = sim.GetMiner(mid).Pos;

		if (bots != null)
		{
			int seed = sim.Config.Seed;
			foreach (var (minerId, skill) in bots)
			{
				_botBrains[minerId]    = new BotBrain(minerId, skill, (int)((uint)seed ^ (uint)(minerId * 1000003)));
				_pendingDir[minerId]   = -1;
				_spawnByMiner[minerId] = sim.GetMiner(minerId).Pos;
			}
		}
		if (botMinerToPeer != null)
			foreach (var kv in botMinerToPeer) _botMinerToPeer[kv.Key] = kv.Value;

		_deadMiners.Clear();
		_coopRespawnTimers.Clear();

		// Restore state when continuing a saved solo expedition.
		var resume = nm.ExpeditionResume;
		if (resume != null && _peerToMiner.Count == 1 && _botBrains.Count == 0)
		{
			_cumulativeGold = resume.CumulativeGold;
			_livesRemaining = Math.Min(resume.Lives, _livesMax);
			int mid = _peerToMiner.Values.First();
			_sim.SetPermLevels(mid, resume.PermSpeed, resume.PermVision, resume.PermBlast);
			_permLevels[mid] = (resume.PermSpeed, resume.PermVision, resume.PermBlast);
			nm.ExpeditionResume = null;
		}
	}

	private long FindWinnerPeer(int minerId)
	{
		foreach (var kv in _peerToMiner) if (kv.Value == minerId) return kv.Key;
		return _botMinerToPeer.TryGetValue(minerId, out var p) ? p : -1;
	}

	public void SetDir(long peerId, int dir)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _pendingDir[minerId] = dir;
	}

	public void SetAction(long peerId, bool mine, bool plant, bool use, bool throwStone = false, bool throwDynamite = false)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine)          _pendingMine.Add(minerId);
		if (plant)         _pendingPlant.Add(minerId);
		if (use)           _pendingUse.Add(minerId);
		if (throwStone)    _pendingThrow.Add(minerId);
		if (throwDynamite) _pendingThrowDynamite.Add(minerId);
	}

	public void EliminatePeer(long peerId)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _sim.KillMiner(minerId);
	}

	public void ReceiveBuy(long peerId, ShopItemKind kind)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		var miner = _sim.Miners.FirstOrDefault(m => m.Id == minerId);
		if (miner == null || !miner.Alive) return;

		int price = ShopPrices.Price(kind);
		if (miner.GoldCollected < price) return;

		switch (kind)
		{
			case ShopItemKind.SpeedUp:
				if (miner.PermSpeedLevel >= _sim.Config.MaxPermSpeedLevel) return;
				_sim.SetPermLevels(minerId, miner.PermSpeedLevel + 1, miner.PermVisionLevel, miner.PermBlastLevel);
				break;
			case ShopItemKind.VisionUp:
				if (miner.PermVisionLevel >= _sim.Config.MaxPermVisionLevel) return;
				_sim.SetPermLevels(minerId, miner.PermSpeedLevel, miner.PermVisionLevel + 1, miner.PermBlastLevel);
				break;
			case ShopItemKind.BlastUp:
				if (miner.PermBlastLevel >= _sim.Config.MaxPermBlastLevel) return;
				_sim.SetPermLevels(minerId, miner.PermSpeedLevel, miner.PermVisionLevel, miner.PermBlastLevel + 1);
				break;
			case ShopItemKind.LifePotion:
				if (_livesRemaining >= _livesMax) return;
				_livesRemaining = Math.Min(_livesRemaining + 1, _livesMax);
				break;
			case ShopItemKind.Stones3:
				if (miner.StoneCount >= 9) return;
				_sim.AddStones(minerId, 3);
				break;
			default: return;
		}
		_sim.DeductGold(minerId, price);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_running) return;
		_accum += delta;
		while (_accum >= TickSeconds) { _accum -= TickSeconds; StepOnce(); }
	}

	private void TickAndBroadcast()
	{
		_sim.Tick(TickSeconds);
		_tick++;

		var changes = new List<TileChange>();
		var screeCollapses = new List<ScreeCollapseSnapshot>();
		var stoneThrows = new List<StoneThrowSnapshot>();
		var dynamiteThrows = new List<DynamiteThrowSnapshot>();
		PrizeClaimSnapshot? prizeClaim = null;
		TreasureToastSnapshot? treasureToast = null;
		foreach (var e in _sim.DrainEvents())
		{
			switch (e)
			{
				case RockMined rm:
					changes.Add(new TileChange(rm.Pos.X, rm.Pos.Y, false, TileType.Floor));
					break;
				case Explosion ex:
					foreach (var d in ex.DestroyedRock)
						changes.Add(new TileChange(d.X, d.Y, true, TileType.Floor));
					break;
				case TileFlooded tf:
					changes.Add(new TileChange(tf.Pos.X, tf.Pos.Y, false, tf.Type));
					break;
				case PlankPlaced pp:
					changes.Add(new TileChange(pp.Pos.X, pp.Pos.Y, false, TileType.Plank));
					break;
				case CrackWeakened cw:
					changes.Add(new TileChange(cw.Pos.X, cw.Pos.Y, false, TileType.Crumbling));
					break;
				case CrackCollapsed cc:
					changes.Add(new TileChange(cc.Pos.X, cc.Pos.Y, false, TileType.Pit));
					break;
				case LavaSpread ls:
					changes.Add(new TileChange(ls.Pos.X, ls.Pos.Y, false, TileType.Lava));
					break;
				case LavaQuenched lq:
					changes.Add(new TileChange(lq.Pos.X, lq.Pos.Y, false, TileType.Cracked));
					break;
				case RockFell rf:
					changes.Add(new TileChange(rf.Pos.X, rf.Pos.Y, false, TileType.Rock));
					break;
				case CrystalShardDropped csd:
					_sim.AddItem(new Item(csd.Pos, ItemKind.CrystalShard, ItemPlacement.Loose));
					break;
				case ScreeCollapsed sc:
					screeCollapses.Add(new ScreeCollapseSnapshot(sc.Pos.X, sc.Pos.Y, sc.Radius));
					break;
				case PortalUsed pu:
					_portalUses.Add(new PortalUseSnapshot(pu.From.X, pu.From.Y, pu.To.X, pu.To.Y, pu.Kind));
					break;
				case LifeRestored:
					_livesRemaining = Math.Min(_livesRemaining + 1, _livesMax);
					break;
				case StoneThrown st:
				{
					var from = _sim.GetMiner(st.MinerId).Pos;
					stoneThrows.Add(new StoneThrowSnapshot(st.MinerId, from.X, from.Y, st.LandingPos.X, st.LandingPos.Y));
					break;
				}
				case DynamiteThrown dt:
				{
					var from = _sim.GetMiner(dt.MinerId).Pos;
					dynamiteThrows.Add(new DynamiteThrowSnapshot(dt.MinerId, from.X, from.Y, dt.LandingPos.X, dt.LandingPos.Y));
					break;
				}
				case PrizeClaimed pcl:
					prizeClaim = new PrizeClaimSnapshot(pcl.MinerId, (byte)pcl.Type);
					break;
				case TreasureFound tf:
					treasureToast = new TreasureToastSnapshot(0, tf.MinerId);
					break;
				case TreasureRecovered tr:
					treasureToast = new TreasureToastSnapshot(1, tr.MinerId);
					break;
				case TreasureSneaking ts:
					treasureToast = new TreasureToastSnapshot(2, ts.MinerId);
					break;
				case TreasureDropped td:
					treasureToast = new TreasureToastSnapshot(3, td.MinerId);
					break;
			}
		}

		var snapshot = SnapshotFactory.Capture(_sim, _tick, _livesRemaining);
		if (screeCollapses.Count > 0)
			snapshot = snapshot with { ScreeCollapses = screeCollapses };
		if (_botWhistles.Count > 0)
		{
			snapshot = snapshot with { Whistles = new List<WhistleSnapshot>(_botWhistles) };
			_botWhistles.Clear();
		}
		if (_portalUses.Count > 0)
		{
			snapshot = snapshot with { PortalUses = new List<PortalUseSnapshot>(_portalUses) };
			_portalUses.Clear();
		}
		if (stoneThrows.Count > 0)
			snapshot = snapshot with { Throws = stoneThrows };
		if (dynamiteThrows.Count > 0)
			snapshot = snapshot with { DynamiteThrows = dynamiteThrows };
		if (prizeClaim is { } claim)
			snapshot = snapshot with { PrizeClaim = claim };
		if (treasureToast is { } tt)
			snapshot = snapshot with { TreasureToast = tt };
		var update = new TickUpdate(snapshot, changes);
		NetworkManager.Instance.BroadcastTick(SnapshotCodec.Write(update));
	}

	private void StepOnce()
	{
		if (_respawnPending)
		{
			_respawnTimer -= TickSeconds;
			TickAndBroadcast();
			if (_respawnTimer <= 0)
			{
				_respawnPending = false;
				foreach (var (mid, sp) in _spawnByMiner)
					if (!_sim.GetMiner(mid).Alive)
						_sim.ReviveMiner(mid, sp, 3.0);
				foreach (var key in _pendingDir.Keys.ToList()) _pendingDir[key] = -1;
			}
			return;
		}

		// Drive bot miners
		var nmMode = NetworkManager.Instance.MatchMode;
		foreach (var (minerId, brain) in _botBrains)
		{
			var action = brain.Think(_sim, nmMode);
			_pendingDir[minerId] = action.Dir;
			if (action.Mine)  _pendingMine.Add(minerId);
			if (action.Plant) _pendingPlant.Add(minerId);
			if (action.Use)   _pendingUse.Add(minerId);
			if (action.Throw) _pendingThrow.Add(minerId);
			if (action.Whistle)
			{
				WhistleBots();
				var wp = _sim.GetMiner(minerId).Pos;
				_botWhistles.Add(new WhistleSnapshot(wp.X, wp.Y));
			}
			_sim.GetMiner(minerId).Listening = action.Listen;
		}

		foreach (var (minerId, dir) in _pendingDir)
		{
			if (dir < 0) continue;
			_sim.TryMove(minerId, (Direction)dir);
		}

		foreach (var minerId in _pendingMine)  _sim.TryStartMining(minerId);
		_pendingMine.Clear();
		foreach (var minerId in _pendingPlant) _sim.TryStartPlanting(minerId);
		_pendingPlant.Clear();
		foreach (var minerId in _pendingUse)   _sim.TryUseItem(minerId);
		_pendingUse.Clear();
		foreach (var minerId in _pendingThrow) _sim.TryThrowStone(minerId);
		_pendingThrow.Clear();
		foreach (var minerId in _pendingThrowDynamite) _sim.TryThrowDynamite(minerId);
		_pendingThrowDynamite.Clear();

		TickAndBroadcast();

		var nm = NetworkManager.Instance;

		// Detect per-miner deaths and schedule individual respawns (co-op, or solo with bots).
		if (UsesPerMinerRespawn)
			HandleCoopDeaths();

		// With bots: end when all human miners are dead and no human respawns are pending.
		// Prevents bots-only fights continuing after every human is eliminated.
		// Treasure Heist is excluded: its respawns run through the sim (AdvanceRespawns, not
		// _coopRespawnTimers) and the treasure — not survivor count — always decides the winner,
		// so the round must fall through to RoundResolver.Resolve in both respawn sub-modes.
		if (_botBrains.Count > 0 && _humanMinerIds.Count > 0 && nm.MatchMode != GameMode.TreasureHeist)
		{
			bool anyHumanAlive         = _humanMinerIds.Any(mid => _sim.GetMiner(mid).Alive);
			bool anyHumanRespawnPending = _humanMinerIds.Any(mid => _coopRespawnTimers.ContainsKey(mid));
			if (!anyHumanAlive && !anyHumanRespawnPending)
			{
				_running = false;
				nm.BroadcastResult(-1);
				return;
			}
		}

		var result = RoundResolver.Resolve(_sim, nm.MatchMode);

		if (result.FloorCleared && !_floorChoicePending)
		{
			foreach (var m in _sim.Miners) _cumulativeGold += m.GoldCollected;
			var carryGold = _sim.Miners.ToDictionary(m => m.Id, m => m.GoldCollected);
			SavePermLevels();

			// In co-op Expedition with multiple human players, pause for a Continue/Return choice.
			bool coopExpedition = nm.MatchMode == GameMode.Expedition && _peerToMiner.Count > 1;
			if (coopExpedition)
			{
				_floorChoicePending    = true;
				_floorChoiceWinnerId  = result.WinnerId;
				_floorChoiceCarryGold = carryGold;
				nm.BroadcastFloorComplete(nm.MatchFloor);
				return;
			}

			AdvanceFloor(result.WinnerId, carryGold);
			return;
		}

		if (result.IsOver)
		{
			bool expeditionLoss = nm.MatchMode == GameMode.Expedition && result.WinnerId == -1;
			if (expeditionLoss)
			{
				if (UsesPerMinerRespawn)
				{
					// Per-miner deaths are handled in HandleCoopDeaths (spends shared lives).
					// result.IsOver only fires when alive.Count == 0.
					// If any respawn timers are still running, keep going.
					if (_coopRespawnTimers.Count > 0) return;
					// Otherwise all dead, no pending respawns, no lives Ã¢â€ â€™ fall through to game over.
				}
				else
				{
					// Pure solo (one human, no bots): spend a life and respawn all dead miners.
					_livesRemaining--;
					if (_livesRemaining > 0)
					{
						_respawnPending = true;
						_respawnTimer = RespawnDelay;
						return;
					}
				}
			}
			_running = false;
			if (nm.MatchMode == GameMode.Expedition)
			{
				int score = 100 * nm.MatchFloor + _cumulativeGold;
				string name = nm.Players.TryGetValue(nm.LocalId, out var info) ? info.Name : "Player";
				ScoreStore.Submit(name, score, nm.MatchFloor, _cumulativeGold);
				SettingsStore.ClearExpeditionSave();
			}
			long winnerPeer = result.WinnerId == -1 ? -1 : FindWinnerPeer(result.WinnerId);
			nm.BroadcastResult(winnerPeer);
		}
	}

	private void HandleCoopDeaths()
	{
		// Count down respawn timers; revive miners whose timer has expired.
		foreach (var key in _coopRespawnTimers.Keys.ToList())
		{
			_coopRespawnTimers[key] -= TickSeconds;
			if (_coopRespawnTimers[key] <= 0)
			{
				_coopRespawnTimers.Remove(key);
				_deadMiners.Remove(key);
				if (_spawnByMiner.TryGetValue(key, out var sp) && !_sim.GetMiner(key).Alive)
					_sim.ReviveMiner(key, sp, 3.0);
			}
		}

		// Detect miners that just died this tick and haven't been processed yet.
		// Every being Ã¢â‚¬â€ human or bot Ã¢â‚¬â€ draws from the shared life pool; when it is empty,
		// the dead being stays down (no respawn timer scheduled).
		foreach (var m in _sim.Miners)
		{
			if (!m.Alive && !_deadMiners.Contains(m.Id) && !_coopRespawnTimers.ContainsKey(m.Id))
			{
				_deadMiners.Add(m.Id);
				if (_livesRemaining > 0)
				{
					_livesRemaining--;
					_coopRespawnTimers[m.Id] = RespawnDelay;
				}
			}
		}
	}

	private void SavePermLevels()
	{
		foreach (var m in _sim.Miners)
			_permLevels[m.Id] = (m.PermSpeedLevel, m.PermVisionLevel, m.PermBlastLevel);
	}

	private void AdvanceFloor(int minerId, Dictionary<int, int>? carryGold = null)
	{
		var nm = NetworkManager.Instance;
		int newFloor  = nm.MatchFloor + 1;
		int floorSeed = nm.MatchSeed + newFloor * 1000;

		if (newFloor > 51)
		{
			int score = 100 * nm.MatchFloor + _cumulativeGold;
			string name = nm.Players.TryGetValue(nm.LocalId, out var winfo) ? winfo.Name : "Player";
			ScoreStore.Submit(name, score, nm.MatchFloor, _cumulativeGold);
			SettingsStore.ClearExpeditionSave();
			_running = false;
			nm.BroadcastResult(FindWinnerPeer(minerId));
			return;
		}

		// Checkpoint: save progress so the player can continue from this floor.
		if (nm.MatchMode == GameMode.Expedition && _peerToMiner.Count == 1 && _botBrains.Count == 0)
		{
			var (spd, vis, bst) = _permLevels.TryGetValue(1, out var lvl) ? lvl : (0, 0, 0);
			SettingsStore.SaveExpedition(nm.MatchSeed, newFloor, _cumulativeGold, _livesRemaining,
				nm.MatchMapScale, nm.MatchFlooding, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava,
				spd, vis, bst);
		}

		var modifier = FloorModifiers.Pick(nm.MatchSeed, newFloor);

		var simCfg = new SimConfig
		{
			BaseMoveSeconds       = nm.MatchBaseMoveSeconds,
			Seed                  = floorSeed,
			RequireChestForEscape = newFloor == 51,
			RockFallsEnabled      = true,   // ceiling collapses at 95% gold, every floor (matches floor 1)
			BlastRockRadius       = nm.MatchBlastRadius,
			BlastKillRadius       = nm.MatchBlastRadius,
			VisionRadius          = nm.MatchVisionRadius,
		};

		GeneratedMap newMap;
		if (newFloor == 51)
		{
			newMap = MapGenerator.GenerateBossFloor(floorSeed);
		}
		else
		{
			var mapCfg = MapConfig.FloorConfig(newFloor, floorSeed, nm.MatchPlayerCount);
			FloorModifiers.Apply(modifier, mapCfg, simCfg);
			simCfg.ExpeditionTreasureKind = mapCfg.ExpeditionTreasureKind;
			newMap = MapGenerator.Generate(mapCfg);
		}

		var newSim = new Simulation(
			newMap.Grid,
			simCfg,
			newMap.Center,
			timeLimitSeconds: null,
			flooding: false,
			newMap.EscapeTile,
			newMap.ShopPos);

		foreach (var item in newMap.Items)
			newSim.AddItem(item);

		foreach (var spec in newMap.Portals)
			newSim.AddPortal(spec);

		// Spawn every peer; miner IDs are 1-based so spawn index = minerId - 1.
		GridPos monsterRef = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : newMap.Center;
		_spawnByMiner.Clear();
		foreach (var mid in _peerToMiner.Values)
		{
			int idx = mid - 1;
			GridPos sp = idx < newMap.Spawns.Count ? newMap.Spawns[idx] : newMap.Spawns[0];
			if (newMap.EscapeTile is GridPos escapePos && sp == escapePos)
			{
				var east = new GridPos(sp.X + 1, sp.Y);
				if (east.X < newMap.Grid.Width && newMap.Grid.Get(east) == TileType.Floor)
					sp = east;
			}
			newSim.AddMiner(mid, sp, invulRemaining: 3.0);
			if (_permLevels.TryGetValue(mid, out var lvl))
				newSim.SetPermLevels(mid, lvl.Speed, lvl.Vision, lvl.Blast);
			if (carryGold != null && carryGold.TryGetValue(mid, out int gold))
				newSim.SetGold(mid, gold);
			_spawnByMiner[mid] = sp;
		}
		// Spawn bot miners for the new floor
		foreach (var mid in _botBrains.Keys)
		{
			int idx = mid - 1;
			GridPos sp = idx < newMap.Spawns.Count ? newMap.Spawns[idx] : newMap.Spawns[0];
			newSim.AddMiner(mid, sp, invulRemaining: 3.0);
			if (_permLevels.TryGetValue(mid, out var lvl))
				newSim.SetPermLevels(mid, lvl.Speed, lvl.Vision, lvl.Blast);
			if (carryGold != null && carryGold.TryGetValue(mid, out int gold))
				newSim.SetGold(mid, gold);
			_spawnByMiner[mid] = sp;
		}

		if (newFloor == 21)
		{
			newSim.AddOctopus(newMap.Center);
		}
		else
		{
			int monsterCount = (int)(MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor)
									 * simCfg.MonsterCountMultiplier);
			var roster = MonsterSpawner.Place(newMap.Grid, monsterRef, monsterCount, floor: newFloor);
			for (int i = 0; i < roster.Count; i++)
				newSim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
		}

		_sim  = newSim;
		_tick = 0;

		foreach (var key in _pendingDir.Keys.ToList()) _pendingDir[key] = -1;
		_pendingMine.Clear();
		_pendingPlant.Clear();
		_pendingUse.Clear();
		_pendingThrow.Clear();
		_pendingThrowDynamite.Clear();
		_deadMiners.Clear();
		_coopRespawnTimers.Clear();

		nm.BroadcastNewFloor(newFloor);
	}

	// Called by Main when the local player whistles at the escape ladder.
	// Forces all idle bots to redirect to the exit for the next ~4 seconds.
	public void WhistleBots()
	{
		if (_sim.EscapeTile is not GridPos esc) return;
		foreach (var brain in _botBrains.Values)
			brain.ForceEscape(esc);
	}

	// Co-op Expedition floor-clear choice: host pressed "Continue to next floor".
	public void ContinueToNextFloor()
	{
		if (!_floorChoicePending) return;
		var carryGold = _floorChoiceCarryGold;
		int winnerId  = _floorChoiceWinnerId;
		_floorChoicePending    = false;
		_floorChoiceCarryGold  = null;
		AdvanceFloor(winnerId, carryGold);
	}

	// Co-op Expedition floor-clear choice: host pressed "Return to Menu".
	public void ReturnToMenuFromFloorClear()
	{
		if (!_floorChoicePending) return;
		_floorChoicePending = false;
		_running = false;
		var nm = NetworkManager.Instance;
		int score = 100 * nm.MatchFloor + _cumulativeGold;
		string name = nm.Players.TryGetValue(nm.LocalId, out var winfo) ? winfo.Name : "Player";
		ScoreStore.Submit(name, score, nm.MatchFloor, _cumulativeGold);
		SettingsStore.ClearExpeditionSave();
		nm.BroadcastResult(FindWinnerPeer(_floorChoiceWinnerId));
	}
}
