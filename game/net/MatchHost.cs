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
	public const double MoveStepSeconds = 0.12; // grid cadence; matches Phase 1 feel

	private Simulation _sim = null!;
	private readonly Dictionary<long, int> _peerToMiner = new();
	private readonly Dictionary<int, int> _pendingDir = new();   // minerId -> Direction(int) or -1
	private readonly HashSet<int> _pendingMine = new();
	private readonly HashSet<int> _pendingPlant = new();
	private readonly Dictionary<int, double> _moveCooldown = new();

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
			_moveCooldown[miner] = 0;
		}
		_running = true;
	}

	public void SetDir(long peerId, int dir)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _pendingDir[minerId] = dir;
	}

	public void SetAction(long peerId, bool mine, bool plant)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine) _pendingMine.Add(minerId);
		if (plant) _pendingPlant.Add(minerId);
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
		foreach (var id in _moveCooldown.Keys.ToList())
			_moveCooldown[id] = Mathf.Max(0, (float)(_moveCooldown[id] - TickSeconds));

		foreach (var (minerId, dir) in _pendingDir)
		{
			if (dir < 0 || _moveCooldown[minerId] > 0) continue;
			if (_sim.TryMove(minerId, (Direction)dir))
			{
				var tile = _sim.Grid.Get(_sim.GetMiner(minerId).Pos);
				_moveCooldown[minerId] = MoveStepSeconds * (float)tile.MoveCostMultiplier();
			}
		}

		foreach (var minerId in _pendingMine) _sim.TryStartMining(minerId);
		_pendingMine.Clear();
		foreach (var minerId in _pendingPlant) _sim.TryStartPlanting(minerId);
		_pendingPlant.Clear();

		_sim.Tick(TickSeconds);
		_tick++;

		var changes = new List<TileChange>();
		foreach (var e in _sim.DrainEvents())
		{
			switch (e)
			{
				case RockMined rm:
					changes.Add(new TileChange(rm.Pos.X, rm.Pos.Y, false));
					break;
				case Explosion ex:
					foreach (var d in ex.DestroyedRock)
						changes.Add(new TileChange(d.X, d.Y, true));
					break;
			}
		}

		var update = new TickUpdate(SnapshotFactory.Capture(_sim, _tick), changes);
		NetworkManager.Instance.BroadcastTick(SnapshotCodec.Write(update));

		var result = RoundResolver.Resolve(_sim);
		if (result.IsOver)
		{
			_running = false;
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
			NetworkManager.Instance.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
		}
	}
}
