using Godot;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Host-only authoritative simulation driver. Steps a fixed 30 Hz tick,
/// applies queued inputs (with a per-miner move cadence gate), and broadcasts a
/// TickUpdate each step via NetworkManager.</summary>
public partial class MatchHost : Node
{
	public const double TickSeconds = 1.0 / 30.0;

	private Simulation _sim = null!;
	private readonly Dictionary<long, int> _peerToMiner = new();
	private readonly Dictionary<int, int> _pendingDir = new();   // minerId -> Direction(int) or -1
	private readonly HashSet<int> _pendingMine = new();
	private readonly HashSet<int> _pendingPlant = new();
	private readonly HashSet<int> _pendingUse = new();

	private int _tick;
	private double _accum;
	private bool _running;

	public void Begin(Simulation sim, Dictionary<long, int> peerToMiner)
	{
		_sim = sim;
		foreach (var (peer, miner) in peerToMiner)
		{
			_peerToMiner[peer] = miner;
			_pendingDir[miner] = -1;
		}
		_running = true;
	}

	public void SetDir(long peerId, int dir)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _pendingDir[minerId] = dir;
	}

	public void SetAction(long peerId, bool mine, bool plant, bool use)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine) _pendingMine.Add(minerId);
		if (plant) _pendingPlant.Add(minerId);
		if (use) _pendingUse.Add(minerId);
	}

	public void EliminatePeer(long peerId)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _sim.KillMiner(minerId);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_running) return;
		_accum += delta;
		while (_accum >= TickSeconds) { _accum -= TickSeconds; StepOnce(); }
	}

	private void StepOnce()
	{
		foreach (var (minerId, dir) in _pendingDir)
		{
			if (dir < 0) continue;
			_sim.TryMove(minerId, (Direction)dir);
		}

		foreach (var minerId in _pendingMine) _sim.TryStartMining(minerId);
		_pendingMine.Clear();
		foreach (var minerId in _pendingPlant) _sim.TryStartPlanting(minerId);
		_pendingPlant.Clear();
		foreach (var minerId in _pendingUse) _sim.TryUseItem(minerId);
		_pendingUse.Clear();

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
			}
		}

		var update = new TickUpdate(SnapshotFactory.Capture(_sim, _tick), changes);
		NetworkManager.Instance.BroadcastTick(SnapshotCodec.Write(update));

		var result = RoundResolver.Resolve(_sim, NetworkManager.Instance.MatchMode);
		if (result.FloorCleared)
		{
			AdvanceFloor(result.WinnerId);
			return;   // skip tick broadcast — new floor starts next tick
		}
		if (result.IsOver)
		{
			_running = false;
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
			NetworkManager.Instance.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
		}
	}

	private void AdvanceFloor(int minerId)
	{
		var nm = NetworkManager.Instance;
		int newFloor = nm.MatchFloor + 1;
		int floorSeed = nm.MatchSeed + newFloor * 1000;

		GeneratedMap newMap;
		GridPos? escapeTile;
		if (newFloor == 21)
		{
			newMap     = MapGenerator.GenerateBossFloor(floorSeed);
			escapeTile = null;
		}
		else
		{
			var cfg    = MapConfig.FloorConfig(newFloor, floorSeed);
			newMap     = MapGenerator.Generate(cfg);
			escapeTile = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : null;
		}

		var newSim = new Simulation(
			newMap.Grid,
			new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = floorSeed },
			newMap.Center,
			timeLimitSeconds: null,
			flooding: false,
			escapeTile);

		foreach (var item in newMap.Items)
			newSim.AddItem(item);

		GridPos spawn = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : newMap.Center;
		newSim.AddMiner(minerId, spawn);

		if (newFloor == 21)
		{
			newSim.AddOctopus(newMap.Center);
		}
		else
		{
			int monsterCount = MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor);
			var roster = MonsterSpawner.Place(newMap.Grid, spawn, monsterCount);
			for (int i = 0; i < roster.Count; i++)
				newSim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
		}

		_sim  = newSim;
		_tick = 0;

		foreach (var key in _pendingDir.Keys.ToList()) _pendingDir[key] = -1;
		_pendingMine.Clear();
		_pendingPlant.Clear();
		_pendingUse.Clear();

		nm.BroadcastNewFloor(newFloor);
	}
}
