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
	private double _listenPingTimer;
	private const double ListenPingInterval = 2.0;

	private readonly Dictionary<int, (int x, int y)> _prevPos = new();
	private readonly Dictionary<int, int> _prevActivity = new();
	private readonly Dictionary<int, bool> _prevAlive = new();
	private readonly Dictionary<int, (int x, int y)> _prevMonsterPos   = new();
	private readonly Dictionary<int, bool>            _prevMonsterAlive = new();
	private readonly HashSet<(int x, int y)> _prevItems = new();
	private readonly Dictionary<(int x, int y), ItemPlacement> _prevPlacement = new();
	private readonly Dictionary<int, int> _prevHeld = new();
	private readonly HashSet<(int x, int y)> _prevMolds = new();
	private readonly Dictionary<int, AudioStreamPlayer2D> _pickaxeLoops = new();
	private readonly List<AudioStreamPlayer2D> _dripEmitters = new();
	private AudioStreamPlayer2D? _lavaLoop;
	private bool _hadExplosionThisFrame;
	private readonly System.Random _bleatRng = new();
	private double _bleatTimer = 3.0;

	public void Begin(MatchClient client)
	{
		_client = client;
		_client.Exploded += OnExploded;
		_client.ScreeCollapsed += OnScreeCollapsed;
		_client.Whistled += OnWhistled;
		_client.Portaled += OnPortaled;
		AudioManager.Instance.PlayMusic(SfxLibrary.PickMusic(NetworkManager.Instance.MatchSeed));
		SpawnDrips();
	}

	public override void _ExitTree()
	{
		if (_client != null) { _client.Exploded -= OnExploded; _client.ScreeCollapsed -= OnScreeCollapsed; _client.Whistled -= OnWhistled; _client.Portaled -= OnPortaled; }
		// Don't StopMusic here — the incoming scene (Lobby/MainMenu) always calls PlayMusic
		// and StopMusic can fire after the new scene's PlayMusic in some Godot orderings.
		if (_lavaLoop != null && IsInstanceValid(_lavaLoop)) _lavaLoop.QueueFree();
	}

	public void SetListening(bool listening)
	{
		if (_listening == listening) return;
		_listening = listening;
		float d = listening ? ListenMaxDistance : DefaultMaxDistance;
		foreach (var e in _dripEmitters) if (IsInstanceValid(e)) e.MaxDistance = d;
		foreach (var e in _pickaxeLoops.Values) if (IsInstanceValid(e)) e.MaxDistance = d;
		if (listening) _listenPingTimer = 0; // fire first ping immediately
	}

	// Returns the local miner's grid position, or null if dead/not found.
	private GridPos? LocalPos()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) return new GridPos(m.X, m.Y);
		return null;
	}

	public override void _Process(double delta)
	{
		_hadExplosionThisFrame = false;

		if (_listening)
		{
			_listenPingTimer -= delta;
			if (_listenPingTimer <= 0)
			{
				_listenPingTimer = ListenPingInterval;
				PingNearestEntity();
			}
		}

		foreach (var m in _client.Miners)
		{
			if (m.Alive && _prevPos.TryGetValue(m.Id, out var pp) && (pp.x != m.X || pp.y != m.Y))
			{
				OneShot(SfxLibrary.Footstep, WorldOf(m.X, m.Y));
				if (m.Id == _client.LocalMinerId)
				{
					var newTile = _client.Grid.Get(new GridPos(m.X, m.Y));
					if (newTile == TileType.Cracked || newTile == TileType.Crumbling)
						OneShot(SfxLibrary.CrackRumble, WorldOf(m.X, m.Y));
				}
			}
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
				OneShot(m.Cause switch
				{
					DeathCause.Drowned => SfxLibrary.Splash,
					DeathCause.Fell    => SfxLibrary.Fall,
					DeathCause.Crushed => SfxLibrary.CaveIn,
					DeathCause.Burned  => SfxLibrary.Sizzle,
					DeathCause.Mauled  => SfxLibrary.ZombieMoan,
					_                  => SfxLibrary.Death,
				}, WorldOf(m.X, m.Y));
			_prevAlive[m.Id] = m.Alive;

			{
				int prevHeld = _prevHeld.TryGetValue(m.Id, out var ph) ? ph : -1;
				if (m.Held != prevHeld)
				{
					if (m.Id == _client.LocalMinerId)
					{
						if (m.Held != -1)
							OneShot(SfxLibrary.Grab, WorldOf(m.X, m.Y));        // picked up / swapped
						else if (prevHeld == (int)ItemKind.WaterPlank)
							OneShot(SfxLibrary.Plank, WorldOf(m.X, m.Y));       // laid a plank (hand emptied)
						// SlowMold -> empty (mold dropped) is covered by the new-patch Squelch below
					}
					// Wire snap: Reel → empty with no explosion this frame means the wire pulled too far.
					if (prevHeld == (int)ItemKind.Reel && m.Held == -1 && !_hadExplosionThisFrame)
						OneShot(SfxLibrary.ReelSnap, WorldOf(m.X, m.Y));
				}
				_prevHeld[m.Id] = m.Held;
			}
		}

		foreach (var mo in _client.Monsters)
		{
			if (mo.Alive && _prevMonsterPos.TryGetValue(mo.Id, out var mp) && (mp.x != mo.X || mp.y != mo.Y))
				OneShot(MonsterMoveSound(mo.Kind), WorldOf(mo.X, mo.Y));
			_prevMonsterPos[mo.Id] = (mo.X, mo.Y);

			bool prevMonAlive = !_prevMonsterAlive.TryGetValue(mo.Id, out var ma) || ma;
			if (prevMonAlive && !mo.Alive)
				OneShot(MonsterDeathSound(mo.Kind), WorldOf(mo.X, mo.Y));
			_prevMonsterAlive[mo.Id] = mo.Alive;
		}

		// Occasional goat bleat — sparse positional ambience from a random living goat.
		_bleatTimer -= delta;
		if (_bleatTimer <= 0)
		{
			var goats = new List<(int x, int y)>();
			foreach (var mo in _client.Monsters)
				if (mo.Alive && mo.Kind == MonsterKind.Goat) goats.Add((mo.X, mo.Y));
			if (goats.Count > 0)
			{
				var g = goats[_bleatRng.Next(goats.Count)];
				OneShot(SfxLibrary.GoatBleat, WorldOf(g.x, g.y));
				_bleatTimer = 4.0 + _bleatRng.NextDouble() * 5.0; // 4–9s between bleats
			}
			else _bleatTimer = 2.0; // no goats about — re-check shortly
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

		// Lava crackle loop: start/track when local miner is within 5 tiles of any lava.
		var llt = LocalTile();
		if (llt is { } lt4)
		{
			const int LavaRadius = 5;
			GridPos? nearestLava = null;
			int bestDist = int.MaxValue;
			for (int dy = -LavaRadius; dy <= LavaRadius; dy++)
			for (int dx = -LavaRadius; dx <= LavaRadius; dx++)
			{
				int d = System.Math.Abs(dx) + System.Math.Abs(dy);
				if (d > LavaRadius) continue;
				var p = new GridPos(lt4.x + dx, lt4.y + dy);
				if (!_client.Grid.InBounds(p)) continue;
				var tt = _client.Grid.Get(p);
				if (tt == TileType.Lava || tt == TileType.LavaVent)
					if (d < bestDist) { bestDist = d; nearestLava = p; }
			}
			if (nearestLava is { } lp)
			{
				var lavaWorld = WorldOf(lp.X, lp.Y);
				if (_lavaLoop == null)
				{
					_lavaLoop = MakeLoop(SfxLibrary.LavaCrackle, lavaWorld);
					_lavaLoop.VolumeDb = -10f;
				}
				else _lavaLoop.Position = lavaWorld;
			}
			else if (_lavaLoop != null)
			{ if (IsInstanceValid(_lavaLoop)) _lavaLoop.QueueFree(); _lavaLoop = null; }
		}
		else if (_lavaLoop != null)
		{ if (IsInstanceValid(_lavaLoop)) _lavaLoop.QueueFree(); _lavaLoop = null; }
	}

	private void OnExploded(Vector2 worldPos)
	{
		OneShot(SfxLibrary.Explosion, worldPos);
		_hadExplosionThisFrame = true;

		if (_listening && LocalPos() is { } lp)
		{
			var localWorld = WorldOf(lp.X, lp.Y);
			if (localWorld.DistanceTo(worldPos) <= 10 * MatchClient.TileSize)
				AudioManager.Instance.TriggerDeafen();
		}
	}

	private void OnScreeCollapsed(Vector2 worldPos, int radius)
	{
		OneShot(SfxLibrary.Rockfall, worldPos);
	}

	private void OnWhistled(Vector2 worldPos)
	{
		OneShot(SfxLibrary.Whistle, worldPos);
	}

	private void OnPortaled(Vector2 worldPos, PortalKind kind)
		{
			OneShot(SfxLibrary.PortalWhoosh, worldPos);
		}

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

	private (int x, int y)? LocalTile() => LocalPos() is { } p ? (p.X, p.Y) : null;

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

	private void PingNearestEntity()
	{
		if (LocalPos() is not { } selfPos) return;

		float bestSq = float.MaxValue;
		int nx = 0, ny = 0;
		AudioStream? nearestSound = null;

		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			float dx = mo.X - selfPos.X, dy = mo.Y - selfPos.Y;
			float sq = dx * dx + dy * dy;
			if (sq < bestSq) { bestSq = sq; nx = mo.X; ny = mo.Y; nearestSound = MonsterMoveSound(mo.Kind); }
		}

		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.Id == _client.LocalMinerId) continue;
			float dx = m.X - selfPos.X, dy = m.Y - selfPos.Y;
			float sq = dx * dx + dy * dy;
			if (sq < bestSq) { bestSq = sq; nx = m.X; ny = m.Y; nearestSound = SfxLibrary.Footstep; }
		}

		if (nearestSound != null)
			OneShot(nearestSound, WorldOf(nx, ny));
	}

	private static AudioStream MonsterMoveSound(MonsterKind kind) => kind switch
	{
		MonsterKind.Goat        => SfxLibrary.GoatHooves,
		MonsterKind.Slime       => SfxLibrary.SlimeSlurp,
		MonsterKind.Ghost       => SfxLibrary.GhostWhisper,
		MonsterKind.ZombieMiner => SfxLibrary.ZombieGroan,
		_                       => SfxLibrary.Footstep,
	};

	private static AudioStream MonsterDeathSound(MonsterKind kind) => kind switch
	{
		MonsterKind.Goat        => SfxLibrary.GoatBleat,
		MonsterKind.Slime       => SfxLibrary.SlimeSplat,
		MonsterKind.Ghost       => SfxLibrary.GhostScream,
		MonsterKind.ZombieMiner => SfxLibrary.ZombieGrunt,
		_                       => SfxLibrary.Death,
	};
}
