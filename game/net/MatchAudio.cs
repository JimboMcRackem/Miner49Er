using Godot;
using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Per-match audio. Reads MatchClient state each frame and spawns
/// positional SFX (footsteps, pickaxe loops, plant, death), plays explosion SFX
/// on the MatchClient.Exploded event, seeds a few ambient drip emitters, and
/// runs the match music. Lives as a Node2D under Main so 2D panning is correct.</summary>
public partial class MatchAudio : Node2D
{
	private const float DefaultMaxDistance = 600f;
	private const float ListenMaxDistance = 1400f;

	private MatchClient _client = null!;
	private bool _listening;

	private readonly Dictionary<int, (int x, int y)> _prevPos = new();
	private readonly Dictionary<int, int> _prevActivity = new();
	private readonly Dictionary<int, bool> _prevAlive = new();
	private readonly HashSet<(int x, int y)> _prevItems = new();
	private readonly Dictionary<(int x, int y), ItemPlacement> _prevPlacement = new();
	private readonly Dictionary<int, int> _prevHeld = new();
	private readonly HashSet<(int x, int y)> _prevMolds = new();
	private readonly Dictionary<int, AudioStreamPlayer2D> _pickaxeLoops = new();
	private readonly List<AudioStreamPlayer2D> _dripEmitters = new();

	public void Begin(MatchClient client)
	{
		_client = client;
		_client.Exploded += OnExploded;
		AudioManager.Instance.PlayMusic(SfxLibrary.Music);
		SpawnDrips();
	}

	public override void _ExitTree()
	{
		if (_client != null) _client.Exploded -= OnExploded;
		AudioManager.Instance.StopMusic();
	}

	public void SetListening(bool listening)
	{
		if (_listening == listening) return;
		_listening = listening;
		float d = listening ? ListenMaxDistance : DefaultMaxDistance;
		foreach (var e in _dripEmitters) if (IsInstanceValid(e)) e.MaxDistance = d;
		foreach (var e in _pickaxeLoops.Values) if (IsInstanceValid(e)) e.MaxDistance = d;
	}

	public override void _Process(double delta)
	{
		foreach (var m in _client.Miners)
		{
			if (m.Alive && _prevPos.TryGetValue(m.Id, out var pp) && (pp.x != m.X || pp.y != m.Y))
				OneShot(SfxLibrary.Footstep, WorldOf(m.X, m.Y));
			_prevPos[m.Id] = (m.X, m.Y);

			bool mining = m.Alive && m.Activity == (int)ActivityKind.Mining;
			bool hasLoop = _pickaxeLoops.ContainsKey(m.Id);
			if (mining && !hasLoop)
				_pickaxeLoops[m.Id] = MakeLoop(SfxLibrary.Pickaxe, WorldOf(m.X, m.Y));
			else if (!mining && hasLoop)
			{
				var p = _pickaxeLoops[m.Id];
				_pickaxeLoops.Remove(m.Id);
				if (IsInstanceValid(p)) p.QueueFree();
			}

			int prevAct = _prevActivity.TryGetValue(m.Id, out var pa) ? pa : (int)ActivityKind.None;
			if (m.Alive && m.Activity == (int)ActivityKind.Planting && prevAct != (int)ActivityKind.Planting)
				OneShot(SfxLibrary.Plant, WorldOf(m.X, m.Y));
			_prevActivity[m.Id] = m.Activity;

			bool prevAlive = !_prevAlive.TryGetValue(m.Id, out var al) || al;
			if (prevAlive && !m.Alive)
			{
				var tile = new GridPos(m.X, m.Y);
				bool drowned = _client.Grid.InBounds(tile)
					&& _client.Grid.Get(tile) == TileType.DeepWater;
				OneShot(drowned ? SfxLibrary.Splash : SfxLibrary.Death, WorldOf(m.X, m.Y));
			}
			_prevAlive[m.Id] = m.Alive;

			if (m.Id == _client.LocalMinerId)
			{
				int prevHeld = _prevHeld.TryGetValue(m.Id, out var ph) ? ph : -1;
				if (m.Held != prevHeld)
				{
					if (m.Held != -1)
						OneShot(SfxLibrary.Grab, WorldOf(m.X, m.Y));        // picked up / swapped
					else if (prevHeld == (int)ItemKind.WaterPlank)
						OneShot(SfxLibrary.Plank, WorldOf(m.X, m.Y));       // laid a plank (hand emptied)
					// SlowMold -> empty (mold dropped) is covered by the new-patch Squelch below
				}
				_prevHeld[m.Id] = m.Held;
			}
		}

		var localTile = LocalTile();
		foreach (var prev in _prevItems)
		{
			bool stillThere = false;
			foreach (var it in _client.Items)
				if (it.X == prev.x && it.Y == prev.y) { stillThere = true; break; }
			// An item that vanished next to the local miner = a pickup; play near it.
			if (!stillThere && localTile is { } lt
				&& System.Math.Abs(lt.x - prev.x) <= 1 && System.Math.Abs(lt.y - prev.y) <= 1)
				OneShot(SfxLibrary.Pickup, WorldOf(prev.x, prev.y));
		}

		// An item that flipped Buried -> Loose at the same tile = freshly unburied; spill near the local miner.
		foreach (var it in _client.Items)
		{
			if (it.Placement == ItemPlacement.Loose
				&& _prevPlacement.TryGetValue((it.X, it.Y), out var prevP) && prevP == ItemPlacement.Buried
				&& localTile is { } lt2
				&& System.Math.Abs(lt2.x - it.X) <= 1 && System.Math.Abs(lt2.y - it.Y) <= 1)
				OneShot(SfxLibrary.Spill, WorldOf(it.X, it.Y));
		}

		// New mold patches (not present last frame) -> squelch near the local miner.
		var moldNow = new HashSet<(int x, int y)>();
		foreach (var mo in _client.Molds) moldNow.Add((mo.X, mo.Y));
		bool squelched = false; // one squelch per drop, not one per patch in the spread
		foreach (var key in moldNow)
			if (!squelched && !_prevMolds.Contains(key) && localTile is { } lt3
				&& System.Math.Abs(lt3.x - key.x) <= 8 && System.Math.Abs(lt3.y - key.y) <= 8)
			{
				OneShot(SfxLibrary.Squelch, WorldOf(key.x, key.y));
				squelched = true;
			}
		_prevMolds.Clear();
		foreach (var key in moldNow) _prevMolds.Add(key);

		_prevItems.Clear();
		_prevPlacement.Clear();
		foreach (var it in _client.Items)
		{
			_prevItems.Add((it.X, it.Y));
			_prevPlacement[(it.X, it.Y)] = it.Placement;
		}
	}

	private void OnExploded(Vector2 worldPos) => OneShot(SfxLibrary.Explosion, worldPos);

	private void SpawnDrips()
	{
		var grid = _client.Grid;
		var rng = new System.Random(1234);
		int placed = 0, attempts = 0;
		while (placed < 6 && attempts < 200)
		{
			attempts++;
			var gp = new GridPos(rng.Next(grid.Width), rng.Next(grid.Height));
			if (!grid.IsWalkable(gp)) continue;
			var p = NewPlayer(SfxLibrary.Drip, WorldOf(gp.X, gp.Y));
			AddChild(p);
			_dripEmitters.Add(p);
			ScheduleDrip(p, rng); // occasional drip with a randomized gap, not a continuous loop
			placed++;
		}
	}

	// Plays a drip, then reschedules itself after a random pause so the ambience
	// is sparse (a drip every few seconds) rather than a continuous tone.
	private void ScheduleDrip(AudioStreamPlayer2D p, System.Random rng)
	{
		if (!IsInstanceValid(p)) return;
		float delay = 2f + (float)rng.NextDouble() * 5f; // 2–7s between drips
		var timer = GetTree().CreateTimer(delay);
		timer.Timeout += () =>
		{
			if (!IsInstanceValid(p)) return;
			p.Play();
			ScheduleDrip(p, rng);
		};
	}

	private (int x, int y)? LocalTile()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) return (m.X, m.Y);
		return null;
	}

	private static Vector2 WorldOf(int x, int y) =>
		new(x * MatchClient.TileSize + MatchClient.TileSize / 2f,
			y * MatchClient.TileSize + MatchClient.TileSize / 2f);

	private void OneShot(AudioStream stream, Vector2 pos)
	{
		if (stream == null) return;
		var p = NewPlayer(stream, pos);
		AddChild(p);
		p.Finished += () => { if (IsInstanceValid(p)) p.QueueFree(); };
		p.Play();
	}

	private AudioStreamPlayer2D MakeLoop(AudioStream stream, Vector2 pos)
	{
		var p = NewPlayer(stream, pos);
		AddChild(p);
		p.Finished += () => { if (IsInstanceValid(p)) p.Play(); }; // restart => loop
		p.Play();
		return p;
	}

	private AudioStreamPlayer2D NewPlayer(AudioStream stream, Vector2 pos) => new()
	{
		Stream = stream,
		Bus = AudioManager.BusSfx,
		Position = pos,
		MaxDistance = _listening ? ListenMaxDistance : DefaultMaxDistance,
	};
}
