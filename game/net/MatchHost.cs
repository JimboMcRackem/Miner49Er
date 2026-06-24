using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
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

	private int _tick;
	private double _accum;
	private bool _running;

	private int _livesRemaining;
	private int _livesMax;
	private int _cumulativeGold;
	private readonly Dictionary<int, (int Speed, int Vision, int Blast)> _permLevels = new();

	private readonly Dictionary<int, GridPos> _spawnByMiner = new();
	private bool _respawnPending;
	private double _respawnTimer;
	private const double RespawnDelay = 2.0;

	public void Begin(Simulation sim, Dictionary<long, int> peerToMiner)
	{
		_sim = sim;
		foreach (var (peer, miner) in peerToMiner)
		{
			_peerToMiner[peer] = miner;
			_pendingDir[miner] = -1;
		}
		var nm       = NetworkManager.Instance;
		_livesMax       = nm.MatchMode == GameMode.Expedition ? 2 * nm.MatchPlayerCount : 1;
		_livesRemaining = _livesMax;
		_running = true;
		_spawnByMiner.Clear();
		foreach (var (_, mid) in peerToMiner)
			_spawnByMiner[mid] = sim.GetMiner(mid).Pos;
	}

	public void SetDir(long peerId, int dir)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _pendingDir[minerId] = dir;
	}

	public void SetAction(long peerId, bool mine, bool plant, bool use, bool throwStone = false)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine)        _pendingMine.Add(minerId);
		if (plant)       _pendingPlant.Add(minerId);
		if (use)         _pendingUse.Add(minerId);
		if (throwStone)  _pendingThrow.Add(minerId);
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
				case LifeRestored:
					_livesRemaining = Math.Min(_livesRemaining + 1, _livesMax);
					break;
			}
		}

		var update = new TickUpdate(SnapshotFactory.Capture(_sim, _tick, _livesRemaining), changes);
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

		TickAndBroadcast();

		var nm     = NetworkManager.Instance;
		var result = RoundResolver.Resolve(_sim, nm.MatchMode);

		if (result.FloorCleared)
		{
			foreach (var m in _sim.Miners) _cumulativeGold += m.GoldCollected;
			SavePermLevels();
			AdvanceFloor(result.WinnerId);
			return;
		}

		if (result.IsOver)
		{
			bool expeditionLoss = nm.MatchMode == GameMode.Expedition && result.WinnerId == -1;
			if (expeditionLoss)
			{
				_livesRemaining--;
				if (_livesRemaining > 0)
				{
					_respawnPending = true;
					_respawnTimer = RespawnDelay;
					return;
				}
			}
			_running = false;
			if (nm.MatchMode == GameMode.Expedition)
			{
				int score = 100 * nm.MatchFloor + _cumulativeGold;
				string name = nm.Players.TryGetValue(nm.LocalId, out var info) ? info.Name : "Player";
				ScoreStore.Submit(name, score, nm.MatchFloor);
			}
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
			nm.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
		}
	}

	private void SavePermLevels()
	{
		foreach (var m in _sim.Miners)
			_permLevels[m.Id] = (m.PermSpeedLevel, m.PermVisionLevel, m.PermBlastLevel);
	}

	private void AdvanceFloor(int minerId)
	{
		var nm = NetworkManager.Instance;
		int newFloor  = nm.MatchFloor + 1;
		int floorSeed = nm.MatchSeed + newFloor * 1000;

		if (newFloor > 21)
		{
			int score = 100 * nm.MatchFloor + _cumulativeGold;
			string name = nm.Players.TryGetValue(nm.LocalId, out var winfo) ? winfo.Name : "Player";
			ScoreStore.Submit(name, score, nm.MatchFloor);
			_running = false;
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == minerId).Key;
			nm.BroadcastResult(winnerPeer);
			return;
		}

		var modifier = FloorModifiers.Pick(nm.MatchSeed, newFloor);

		var simCfg = new SimConfig
		{
			BaseMoveSeconds       = nm.MatchBaseMoveSeconds,
			Seed                  = floorSeed,
			RequireChestForEscape = newFloor == 21,
		};

		GeneratedMap newMap;
		if (newFloor == 21)
		{
			newMap = MapGenerator.GenerateBossFloor(floorSeed);
		}
		else
		{
			var mapCfg = MapConfig.FloorConfig(newFloor, floorSeed, nm.MatchPlayerCount);
			FloorModifiers.Apply(modifier, mapCfg, simCfg);
			newMap = MapGenerator.Generate(mapCfg);
		}

		var newSim = new Simulation(
			newMap.Grid,
			simCfg,
			newMap.Center,
			timeLimitSeconds: null,
			flooding: false,
			newMap.EscapeTile);

		foreach (var item in newMap.Items)
			newSim.AddItem(item);

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
			var roster = MonsterSpawner.Place(newMap.Grid, monsterRef, monsterCount);
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

		nm.BroadcastNewFloor(newFloor);
	}
}
