# Phase 3 — Listen Mechanic & Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add hold-to-listen with an 8-point compass to the nearest living rival, plus an audio layer (music + positional SFX) derived entirely from already-synced state.

**Architecture:** One unit-tested Core helper (`ListenCompass`) computes the compass direction. Everything else is a client-side Godot layer: an `AudioManager` autoload (buses, music, duck/lift), a missing-tolerant `SfxLibrary` with procedural placeholder sounds, a per-match `MatchAudio` node that derives positional SFX from `MatchClient` state, and a `Compass` HUD. Hold-to-listen is wired in the existing `Main`/`InputSender`. No networking changes.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C#, `AudioServer`/`AudioStreamPlayer2D`/`AudioStreamWAV`, `Miner49er.Core` (pure C#), xUnit.

---

## Conventions

- **Run tests:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` (PowerShell).
- **Build:** `dotnet build Miner49er.csproj`.
- **Headless boot check:** `godot --headless --quit-after 180` (exit 0, no `ERROR`/`SCRIPT ERROR`).
- Core C# uses 4-space indent; `game/` C# uses **tabs**.
- **No interactive audio here** — this dev box is headless and silent. Each Godot task is verified by *build + headless boot clean* only; the actual sound/compass feel is the user's to play-test. Say so honestly in reports.
- Commit after every task. Branch is `phase3-listen-audio`.

## File Structure

**Created — Core (pure, tested):**
- `src/Miner49er.Core/Listen/ListenCompass.cs` — `CompassDirection` enum + nearest-living-other → 8-point direction.

**Created — Tests:**
- `src/Miner49er.Core.Tests/ListenCompassTests.cs`

**Created — Godot:**
- `game/audio/AudioManager.cs` — autoload: buses, music loop, duck/lift, master mute.
- `game/audio/SfxLibrary.cs` — logical name → stream; file-or-procedural; missing-tolerant.
- `game/audio/MatchAudio.cs` — per-match Node2D: derives positional SFX from `MatchClient`, ambient drips, match music.
- `game/ui/Compass.cs` — 8-point HUD indicator.
- `assets/audio/README.md` — asset manifest (drop-in CC0 files).

**Modified:**
- `project.godot` — register `AudioManager` autoload.
- `game/net/MatchClient.cs` — add an `Exploded` event fired when blast tile-changes arrive.
- `game/net/InputSender.cs` — add a `Listening` flag (send "stop" + suppress actions while listening).
- `game/Main.cs` — build `MatchAudio` + `Compass`; coordinate hold-to-listen each frame.

---

## Task 1: Compass direction helper (Core, TDD)

**Files:**
- Create: `src/Miner49er.Core/Listen/ListenCompass.cs`
- Test: `src/Miner49er.Core.Tests/ListenCompassTests.cs`

- [ ] **Step 1: Write the failing test**

`src/Miner49er.Core.Tests/ListenCompassTests.cs`:
```csharp
using System.Collections.Generic;
using Miner49er.Core;
using Xunit;

public class ListenCompassTests
{
    private static GridPos Self => new(10, 10);

    [Fact]
    public void Empty_others_returns_null()
    {
        Assert.Null(ListenCompass.NearestDirection(Self, new List<GridPos>()));
    }

    [Theory]
    [InlineData(10, 5, CompassDirection.N)]    // straight up (-Y)
    [InlineData(15, 10, CompassDirection.E)]   // right
    [InlineData(10, 15, CompassDirection.S)]   // down (+Y)
    [InlineData(5, 10, CompassDirection.W)]    // left
    [InlineData(13, 7, CompassDirection.NE)]   // up-right
    [InlineData(13, 13, CompassDirection.SE)]  // down-right
    [InlineData(7, 13, CompassDirection.SW)]   // down-left
    [InlineData(7, 7, CompassDirection.NW)]    // up-left
    public void Single_other_buckets_to_expected_direction(int x, int y, CompassDirection expected)
    {
        var dir = ListenCompass.NearestDirection(Self, new[] { new GridPos(x, y) });
        Assert.Equal(expected, dir);
    }

    [Fact]
    public void Picks_the_nearest_of_several()
    {
        var others = new[] { new GridPos(5, 10), new GridPos(20, 10) }; // west dist 5, east dist 10
        Assert.Equal(CompassDirection.W, ListenCompass.NearestDirection(Self, others));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter ListenCompassTests`
Expected: FAIL — `ListenCompass`/`CompassDirection` do not exist.

- [ ] **Step 3: Write the implementation**

`src/Miner49er.Core/Listen/ListenCompass.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Miner49er.Core;

public enum CompassDirection { N, NE, E, SE, S, SW, W, NW }

/// <summary>Pure helper for the Listen compass: the 8-point direction from the
/// listener to the nearest other position. Caller passes only living rivals
/// (excluding self).</summary>
public static class ListenCompass
{
    public static CompassDirection? NearestDirection(GridPos self, IEnumerable<GridPos> others)
    {
        GridPos? best = null;
        long bestSq = long.MaxValue;
        foreach (var o in others)
        {
            long dx = o.X - self.X, dy = o.Y - self.Y;
            long sq = dx * dx + dy * dy;
            if (sq < bestSq) { bestSq = sq; best = o; }
        }
        if (best is null) return null;
        return Bucket(best.Value.X - self.X, best.Value.Y - self.Y);
    }

    // North = up = -Y. Bearing measured clockwise from North, snapped to 8 sectors.
    internal static CompassDirection Bucket(int dx, int dy)
    {
        double degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (degrees < 0) degrees += 360.0;
        int sector = (int)Math.Round(degrees / 45.0) % 8;
        return (CompassDirection)sector;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter ListenCompassTests`
Expected: PASS (10 cases).

- [ ] **Step 5: Run the full Core suite (regression)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (50 prior + new = 60).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Listen/ListenCompass.cs src/Miner49er.Core.Tests/ListenCompassTests.cs
git commit -m "feat(core): add 8-point listen compass direction helper"
```

---

## Task 2: AudioManager autoload

Buses, looping music, the listen duck/lift, and master mute.

**Files:**
- Create: `game/audio/AudioManager.cs`
- Modify: `project.godot`

- [ ] **Step 1: Write AudioManager**

`game/audio/AudioManager.cs`:
```csharp
using Godot;

namespace Miner49er;

/// <summary>Autoload owning audio buses, the looping music player, the
/// listen-time duck/lift, and master mute. Positional SFX are spawned by
/// MatchAudio; this manages global state only.</summary>
public partial class AudioManager : Node
{
	public static AudioManager Instance { get; private set; } = null!;

	public const string BusMusic = "Music";
	public const string BusSfx = "SFX";
	public const string BusUi = "UI";

	private const float MusicDefaultDb = -6f;
	private const float MusicDuckedDb = -18f;
	private const float SfxDefaultDb = 0f;
	private const float SfxLiftedDb = 4f;

	private AudioStreamPlayer _music = null!;
	private bool _muted;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		EnsureBus(BusMusic);
		EnsureBus(BusSfx);
		EnsureBus(BusUi);
		SetBusDb(BusMusic, MusicDefaultDb);
		SetBusDb(BusSfx, SfxDefaultDb);

		_music = new AudioStreamPlayer { Name = "Music", Bus = BusMusic };
		AddChild(_music);
		_music.Finished += () => { if (_music.Stream != null) _music.Play(); }; // loop
	}

	private static void EnsureBus(string name)
	{
		if (AudioServer.GetBusIndex(name) != -1) return;
		int idx = AudioServer.BusCount;
		AudioServer.AddBus(idx);
		AudioServer.SetBusName(idx, name);
	}

	private static void SetBusDb(string bus, float db)
	{
		int idx = AudioServer.GetBusIndex(bus);
		if (idx != -1) AudioServer.SetBusVolumeDb(idx, db);
	}

	private static float CurrentDb(string bus)
	{
		int idx = AudioServer.GetBusIndex(bus);
		return idx != -1 ? AudioServer.GetBusVolumeDb(idx) : 0f;
	}

	public void PlayMusic(AudioStream? stream)
	{
		if (stream == null) return;
		_music.Stream = stream;
		_music.Play();
	}

	public void StopMusic() => _music.Stop();

	public void SetListening(bool listening)
	{
		float musicTo = listening ? MusicDuckedDb : MusicDefaultDb;
		float sfxTo = listening ? SfxLiftedDb : SfxDefaultDb;
		var tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(db => SetBusDb(BusMusic, db)), CurrentDb(BusMusic), musicTo, 0.2);
		tween.Parallel().TweenMethod(Callable.From<float>(db => SetBusDb(BusSfx, db)), CurrentDb(BusSfx), sfxTo, 0.2);
	}

	public void ToggleMute()
	{
		_muted = !_muted;
		AudioServer.SetBusMute(0, _muted); // master bus
	}
}
```

- [ ] **Step 2: Register the autoload in `project.godot`**

The `[autoload]` section already exists (from Phase 2's `NetworkManager`). Add the `AudioManager` line beneath it so the section reads:
```ini
[autoload]

NetworkManager="*res://game/net/NetworkManager.cs"
AudioManager="*res://game/audio/AudioManager.cs"
```

- [ ] **Step 3: Build + headless boot**

Run:
```
dotnet build Miner49er.csproj
godot --headless --quit-after 180
```
Expected: build 0 errors; boot exits 0, no `ERROR`/`SCRIPT ERROR`. (The buses are created at startup; no audio plays yet.)

- [ ] **Step 4: Commit**

```bash
git add game/audio/AudioManager.cs project.godot
git commit -m "feat(audio): add AudioManager autoload with buses, music loop, duck/lift"
```

---

## Task 3: SfxLibrary (file-or-procedural)

Logical sound names resolve to real files when present, else generated placeholder PCM so the game is audible with zero bundled assets.

**Files:**
- Create: `game/audio/SfxLibrary.cs`
- Create: `assets/audio/README.md`

- [ ] **Step 1: Write SfxLibrary**

`game/audio/SfxLibrary.cs`:
```csharp
using Godot;
using System;
using System.Collections.Generic;

namespace Miner49er;

/// <summary>Resolves logical sound names to AudioStreams. Loads
/// res://assets/audio/{name}.{ogg|wav} when present; otherwise returns a cached
/// procedural placeholder (16-bit mono PCM). Music has no placeholder — it is
/// null until the user drops in a loop. Missing files never crash.</summary>
public static class SfxLibrary
{
	private const int MixRate = 22050;
	private static readonly Dictionary<string, AudioStream> _cache = new();

	public static AudioStream Footstep => Get("footstep", () => Noise(0.05f, 220f));
	public static AudioStream Pickaxe => Get("pickaxe", () => Noise(0.10f, 400f));
	public static AudioStream Plant => Get("plant", () => Noise(0.04f, 1500f));
	public static AudioStream Explosion => Get("explosion", () => Noise(0.40f, 120f, decay: true));
	public static AudioStream Death => Get("death", () => Tone(0.30f, 300f, 120f));
	public static AudioStream Drip => Get("drip", () => Tone(0.12f, 900f, 600f));
	public static AudioStream? Music => GetOptional("music_loop");

	private static AudioStream Get(string name, Func<AudioStream> placeholder)
	{
		if (_cache.TryGetValue(name, out var s)) return s;
		var result = TryLoad(name) ?? placeholder();
		_cache[name] = result;
		return result;
	}

	private static AudioStream? GetOptional(string name)
	{
		if (_cache.TryGetValue(name, out var s)) return s;
		var loaded = TryLoad(name);
		if (loaded != null) _cache[name] = loaded;
		return loaded;
	}

	private static AudioStream? TryLoad(string name)
	{
		foreach (var ext in new[] { "ogg", "wav" })
		{
			string path = $"res://assets/audio/{name}.{ext}";
			if (ResourceLoader.Exists(path)) return ResourceLoader.Load<AudioStream>(path);
		}
		return null;
	}

	private static AudioStreamWAV Noise(float seconds, float lowpassHz, bool decay = false)
	{
		int n = Mathf.Max(1, (int)(seconds * MixRate));
		var data = new byte[n * 2];
		var rng = new Random(unchecked((int)(seconds * 1000f) ^ (int)lowpassHz));
		float prev = 0f;
		float alpha = Mathf.Clamp(lowpassHz / MixRate, 0.02f, 1f);
		for (int i = 0; i < n; i++)
		{
			float white = (float)(rng.NextDouble() * 2.0 - 1.0);
			prev += alpha * (white - prev);
			float env = decay ? 1f - (float)i / n : 1f;
			short v = (short)(Mathf.Clamp(prev * env, -1f, 1f) * 12000f);
			data[i * 2] = (byte)(v & 0xff);
			data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
		}
		return Wav(data);
	}

	private static AudioStreamWAV Tone(float seconds, float startHz, float endHz)
	{
		int n = Mathf.Max(1, (int)(seconds * MixRate));
		var data = new byte[n * 2];
		double phase = 0;
		for (int i = 0; i < n; i++)
		{
			float t = (float)i / n;
			float hz = Mathf.Lerp(startHz, endHz, t);
			phase += 2.0 * Mathf.Pi * hz / MixRate;
			float env = 1f - t;
			short v = (short)(Mathf.Sin((float)phase) * env * 12000f);
			data[i * 2] = (byte)(v & 0xff);
			data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
		}
		return Wav(data);
	}

	private static AudioStreamWAV Wav(byte[] data) => new()
	{
		Format = AudioStreamWAV.FormatEnum.Format16Bits,
		MixRate = MixRate,
		Stereo = false,
		Data = data,
	};
}
```

- [ ] **Step 2: Write the asset manifest**

`assets/audio/README.md`:
```markdown
# Audio assets

Drop CC0 / royalty-free files here to replace the procedural placeholders.
Files are loaded by logical name; `.ogg` is preferred, `.wav` also works.
Missing files fall back to a generated placeholder (music has none — it is
silent until you add `music_loop.ogg`).

| Logical name   | File                 | Used for                        |
|----------------|----------------------|---------------------------------|
| music_loop     | music_loop.ogg       | looping match music (no placeholder) |
| footstep       | footstep.ogg/.wav    | miner steps onto a new tile     |
| pickaxe        | pickaxe.ogg/.wav     | mining loop                     |
| plant          | plant.ogg/.wav       | planting a charge               |
| explosion      | explosion.ogg/.wav   | charge detonation               |
| death          | death.ogg/.wav       | a miner is killed               |
| drip           | drip.ogg/.wav        | ambient water drips             |

Suggested CC0 sources: freesound.org (filter License = Creative Commons 0),
kenney.nl/assets (impact/footstep packs), sonniss.com GDC bundles.
```

- [ ] **Step 3: Build (compile check)**

Run: `dotnet build Miner49er.csproj`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add game/audio/SfxLibrary.cs assets/audio/README.md
git commit -m "feat(audio): add missing-tolerant SfxLibrary with procedural placeholders"
```

---

## Task 4: MatchAudio — positional SFX from match state

A per-match Node2D that derives footsteps, pickaxe loops, plant, explosion, death, and ambient drips from `MatchClient`, and drives match music.

**Files:**
- Modify: `game/net/MatchClient.cs` (add an `Exploded` event)
- Create: `game/net/MatchAudio.cs`
- Modify: `game/Main.cs` (build MatchAudio)

- [ ] **Step 1: Add the `Exploded` event to MatchClient**

In `game/net/MatchClient.cs`, add an event field near the top of the class (after `LocalMinerId`):
```csharp
	public event System.Action<Vector2>? Exploded; // world position of a detonation
```
Then replace the `ApplyUpdate` method body's tile-change loop so it computes a blast centroid and fires the event:
```csharp
	public void ApplyUpdate(TickUpdate update)
	{
		float bx = 0f, by = 0f;
		int blastCount = 0;
		foreach (var t in update.TileChanges)
		{
			var p = new GridPos(t.X, t.Y);
			if (Grid.InBounds(p)) Grid.Set(p, TileType.Floor);
			if (t.FromBlast)
			{
				_world?.AddExplosionFlash(p);
				bx += t.X; by += t.Y; blastCount++;
			}
		}
		if (blastCount > 0)
		{
			var c = new Vector2(bx / blastCount * TileSize + TileSize / 2f,
								 by / blastCount * TileSize + TileSize / 2f);
			Exploded?.Invoke(c);
		}

		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		UpdateFog();
	}
```

- [ ] **Step 2: Write MatchAudio**

`game/net/MatchAudio.cs`:
```csharp
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
				OneShot(SfxLibrary.Death, WorldOf(m.X, m.Y));
			_prevAlive[m.Id] = m.Alive;
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
			_dripEmitters.Add(MakeLoop(SfxLibrary.Drip, WorldOf(gp.X, gp.Y)));
			placed++;
		}
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
```

- [ ] **Step 3: Build MatchAudio into Main**

In `game/Main.cs`, add a field next to the others:
```csharp
	private MatchAudio _audio = null!;
```
In `_Ready`, after `_client.Begin(map.Grid, localMinerId, this);` (and before or after the host block — placement is fine after the InputSender/Hud creation), add:
```csharp
		_audio = new MatchAudio { Name = "MatchAudio" };
		AddChild(_audio);
		_audio.Begin(_client);
```

- [ ] **Step 4: Build + headless boot**

Run:
```
dotnet build Miner49er.csproj
godot --headless --quit-after 180
```
Expected: build 0 errors; boot exit 0, no errors. (No match runs headlessly, so MatchAudio isn't exercised — this only confirms it compiles and the autoloads/scene load clean.)

- [ ] **Step 5: Commit**

```bash
git add game/net/MatchClient.cs game/net/MatchAudio.cs game/Main.cs
git commit -m "feat(audio): derive positional match SFX, drips, and music from client state"
```

---

## Task 5: Compass UI + hold-to-listen wiring

The 8-point HUD indicator and the hold-to-listen coordination.

**Files:**
- Modify: `game/InputBindings.cs` (add a `Mute` action)
- Modify: `game/net/InputSender.cs` (add `Listening` flag)
- Create: `game/ui/Compass.cs`
- Modify: `game/Main.cs` (build Compass; coordinate listen + mute each frame)

- [ ] **Step 1a: Add a `Mute` action to InputBindings**

In `game/InputBindings.cs`, add the constant alongside the others (e.g. after `Restart`):
```csharp
	public const string Mute = "mute";          // master mute (Phase 3)
```
And add its binding inside `EnsureDefaults()` (after the existing `Bind(...)` calls):
```csharp
		Bind(Mute, Key.M, JoyButton.Back);
```

- [ ] **Step 1: Add the `Listening` flag to InputSender**

In `game/net/InputSender.cs`, add a public field and gate movement. Replace the class body's `_PhysicsProcess` with:
```csharp
	public bool Enabled = true;   // false while the local miner is dead (spectating)
	public bool Listening = false; // true while the listen key is held

	public override void _PhysicsProcess(double delta)
	{
		if (!Enabled) return;
		if (Listening)
		{
			NetworkManager.Instance.SendDir(-1); // actively stand still; no actions
			return;
		}

		int dir = ReadDir();
		NetworkManager.Instance.SendDir(dir);

		bool mine = Input.IsActionJustPressed(InputBindings.Pickaxe);
		bool plant = Input.IsActionJustPressed(InputBindings.Plant);
		if (mine || plant) NetworkManager.Instance.SendAction(mine, plant);
	}
```
(Keep the existing `ReadDir()` method and the `Enabled` field is now declared here — remove the old standalone `public bool Enabled = true;` line if it duplicates.)

- [ ] **Step 2: Write the Compass UI**

`game/ui/Compass.cs`:
```csharp
using Godot;
using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er;

/// <summary>8-point listen compass. When Active, highlights the arrow toward the
/// nearest living rival (computed via Core ListenCompass). Hidden otherwise.</summary>
public partial class Compass : CanvasLayer
{
	public bool Active;

	private MatchClient _client = null!;
	private Control _root = null!;
	private readonly Label[] _points = new Label[8];
	// index order matches CompassDirection: N, NE, E, SE, S, SW, W, NW
	private static readonly string[] Glyphs = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 40;
		_root = new Control();
		_root.SetAnchorsPreset(Control.LayoutPreset.Center);
		AddChild(_root);
		for (int i = 0; i < 8; i++)
		{
			double ang = i * Mathf.Pi / 4.0 - Mathf.Pi / 2.0; // i=0 -> up
			var lbl = new Label { Text = Glyphs[i] };
			lbl.AddThemeFontSizeOverride("font_size", 28);
			lbl.Position = new Vector2((float)(Mathf.Cos((float)ang) * 70f), (float)(Mathf.Sin((float)ang) * 70f));
			_root.AddChild(lbl);
			_points[i] = lbl;
		}
		_root.Visible = false;
	}

	public override void _Process(double delta)
	{
		_root.Visible = Active;
		if (!Active) return;
		var dir = ComputeDir();
		for (int i = 0; i < 8; i++)
			_points[i].Modulate = dir.HasValue && (int)dir.Value == i
				? Colors.Yellow
				: new Color(1, 1, 1, 0.25f);
	}

	private CompassDirection? ComputeDir()
	{
		GridPos? self = null;
		var others = new List<GridPos>();
		foreach (var m in _client.Miners)
		{
			if (!m.Alive) continue;
			if (m.Id == _client.LocalMinerId) self = new GridPos(m.X, m.Y);
			else others.Add(new GridPos(m.X, m.Y));
		}
		return self is null ? null : ListenCompass.NearestDirection(self.Value, others);
	}
}
```

- [ ] **Step 3: Build the Compass into Main and coordinate listening**

In `game/Main.cs`, add fields:
```csharp
	private Compass _compass = null!;
	private bool _wasListening;
```
In `_Ready`, after `_audio.Begin(_client);`, add:
```csharp
		_compass = new Compass { Name = "Compass" };
		AddChild(_compass);
		_compass.Init(_client);
```
In `_PhysicsProcess`, after the existing `if (_input != null) _input.Enabled = !sawLocal || localAlive;` line, add the listen coordination:
```csharp
		bool listening = localAlive && Input.IsActionPressed(InputBindings.Listen);
		if (_input != null) _input.Listening = listening;
		_compass.Active = listening;
		if (listening != _wasListening)
		{
			AudioManager.Instance.SetListening(listening);
			_audio.SetListening(listening);
			_wasListening = listening;
		}

		if (Input.IsActionJustPressed(InputBindings.Mute))
			AudioManager.Instance.ToggleMute();
```

> The `Mute` action is registered by `InputBindings.EnsureDefaults()` (called in `Main._Ready`), so it is guaranteed to exist in-match where this handler runs. (Global/menu mute is part of the Phase 5 settings work.)

- [ ] **Step 4: Build + headless boot**

Run:
```
dotnet build Miner49er.csproj
godot --headless --quit-after 180
```
Expected: build 0 errors; boot exit 0, no errors.

- [ ] **Step 5: Commit**

```bash
git add game/InputBindings.cs game/net/InputSender.cs game/ui/Compass.cs game/Main.cs
git commit -m "feat(listen): add 8-point compass HUD, hold-to-listen, and master mute"
```

---

## Final verification

- [ ] **Full Core suite:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` → all green (60: 50 prior + 10 compass cases).
- [ ] **Build:** `dotnet build Miner49er.csproj` → 0 errors.
- [ ] **Headless boot:** `godot --headless --quit-after 180` → exit 0, no errors.
- [ ] **Manual play-test (user, two instances):** confirm — holding `L` stops your miner and shows the compass arrow toward the nearest living rival (and a neutral ring when you're the last alive); music ducks and the world's SFX lift while held; you hear footsteps, a pickaxe loop while mining, a plant blip, explosions, and a death stinger, all panned by position; ambient drips murmur around the map. Drop a `music_loop.ogg` into `assets/audio/` to hear the track. (Audio cannot be verified headlessly — this step is the user's.)
- [ ] **Update memory:** add/refresh a Phase 3 status note (listen + audio implemented on `phase3-listen-audio`; compass math unit-tested; audio/feel user-verified; assets are drop-in).

---

## Notes carried forward (not built here)

- **No networking changes** — Phase 3 reads only existing synced state.
- **Persisted settings UI / volume sliders** and a **broadcast listen animation** remain Phase 5.
- **Ambient drips are atmospheric placeholders**, not tied to the Phase 4 water system.
- **Final audio assets** are user-supplied; procedural placeholders stand in until then.
