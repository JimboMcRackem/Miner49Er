# Cause-of-Death Messaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a miner dies, show the victim a center-screen banner naming the cause and show every other player a stacking kill-feed toast (e.g. "Bob drowned").

**Architecture:** Cause of death is computed authoritatively in the engine-free Core (a `DeathCause` set at each kill site), replicated as one byte on `MinerSnapshot`, and surfaced by a new client-only Godot `DeathFeed` node that watches each miner's `Alive` flag flip `true → false`. No new network channel — the cause rides the existing per-tick snapshot.

**Tech Stack:** C# / .NET 8, pure-C# `Miner49er.Core` (4-space indent, xUnit), thin Godot 4.6.3 adapter in `game/` (TAB indent).

**Conventions for every commit in this plan:**
- Build: `dotnet build Miner49er.sln`
- Test: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- Stage **only the exact files listed** in each commit step — never `git add -A`. Do **not** stage the pre-existing working-tree noise (`project.godot`, `game/Splash.tscn`, `assets/Splash.png*`, `.superpowers/`).
- Every commit message ends with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- Core uses 4-space indent; `game/` uses TAB indent.

---

## File Structure

- **Create** `src/Miner49er.Core/Sim/DeathCause.cs` — the cause enum (one responsibility: the cause taxonomy).
- **Modify** `src/Miner49er.Core/Sim/Miner.cs` — add a `DeathCause` field.
- **Modify** `src/Miner49er.Core/Sim/Simulation.cs` — set the cause at the four kill sites.
- **Modify** `src/Miner49er.Core/Net/Snapshots.cs` — add `Cause` to `MinerSnapshot`.
- **Modify** `src/Miner49er.Core/Net/SnapshotFactory.cs` — populate `Cause`.
- **Modify** `src/Miner49er.Core/Net/SnapshotCodec.cs` — serialize `Cause` (one byte).
- **Create** `game/ui/DeathFeed.cs` — client UI: center banner + stacking toast feed.
- **Modify** `game/Main.cs` — instantiate and wire `DeathFeed`.
- **Modify** `game/net/MatchAudio.cs` — pick splash-vs-death SFX from `Cause` instead of the tile.
- **Modify tests:** `SimulationKillTests.cs`, `SimulationMovementTests.cs`, `FloodTests.cs`, `SimulationExplosiveTests.cs`, `SnapshotCodecTests.cs`.

---

## Task 1: Core — `DeathCause` enum, `Miner` field, tag all kill sites

**Files:**
- Create: `src/Miner49er.Core/Sim/DeathCause.cs`
- Modify: `src/Miner49er.Core/Sim/Miner.cs:10`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (kill sites at lines 49, 168-173, 420-424, 452-459)
- Test: `src/Miner49er.Core.Tests/SimulationKillTests.cs`, `SimulationMovementTests.cs`, `FloodTests.cs`, `SimulationExplosiveTests.cs`

- [ ] **Step 1: Add failing cause assertions to the four existing kill tests**

In `src/Miner49er.Core.Tests/SimulationKillTests.cs`, inside `KillMiner_sets_alive_false_clears_activity_and_emits_event`, after the line `Assert.Equal(ActivityKind.None, m.Activity);` add:

```csharp
        Assert.Equal(DeathCause.Left, m.DeathCause);
```

In `src/Miner49er.Core.Tests/SimulationMovementTests.cs`, inside `Drowning_emits_MinerMoved_then_MinerDrowned`, after the line `Assert.Equal(1, drowned.MinerId);` add:

```csharp
        Assert.Equal(DeathCause.Drowned, sim.GetMiner(1).DeathCause);
```

In `src/Miner49er.Core.Tests/FloodTests.cs`, inside `A_standing_miner_drowns_when_the_front_reaches_them`, after the line `Assert.False(m.Alive);` add:

```csharp
        Assert.Equal(DeathCause.Drowned, m.DeathCause);
```

In `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`, inside `Miner_in_kill_radius_dies_but_bystander_survives`, after the line `Assert.False(planter.Alive);` add:

```csharp
        Assert.Equal(DeathCause.Exploded, planter.DeathCause);
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: BUILD FAILURE — `DeathCause` does not exist and `Miner` has no `DeathCause` member.

- [ ] **Step 3: Create the `DeathCause` enum**

Create `src/Miner49er.Core/Sim/DeathCause.cs`:

```csharp
namespace Miner49er.Core;

/// <summary>Why a miner died, replicated to clients for the death banner/feed.
/// None means the miner is still alive.</summary>
public enum DeathCause { None, Drowned, Exploded, Left }
```

- [ ] **Step 4: Add the field to `Miner`**

In `src/Miner49er.Core/Sim/Miner.cs`, immediately after the line `public bool Alive { get; internal set; } = true;` (line 10) add:

```csharp
    public DeathCause DeathCause { get; internal set; } = DeathCause.None;
```

- [ ] **Step 5: Set the cause at the four kill sites in `Simulation.cs`**

Site A — `KillMiner` (disconnect path). The method body currently reads:

```csharp
        m.Alive = false;
        m.Activity = ActivityKind.None;
        _events.Add(new MinerKilled(id));
```

Change it to:

```csharp
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = DeathCause.Left;
        _events.Add(new MinerKilled(id));
```

Site B — drown-on-move in `TryMove`. Currently:

```csharp
        if (Grid.Get(target).IsLethal())
        {
            m.Alive = false;
            m.Activity = ActivityKind.None;
            _events.Add(new MinerDrowned(id));
        }
```

Change to:

```csharp
        if (Grid.Get(target).IsLethal())
        {
            m.Alive = false;
            m.Activity = ActivityKind.None;
            m.DeathCause = DeathCause.Drowned;
            _events.Add(new MinerDrowned(id));
        }
```

Site C — `DrownOccupants` (flood under a standing miner). Currently:

```csharp
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                _events.Add(new MinerDrowned(m.Id));
            }
```

Change to:

```csharp
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                m.DeathCause = DeathCause.Drowned;
                _events.Add(new MinerDrowned(m.Id));
            }
```

Site D — blast radius kill. Currently:

```csharp
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                _events.Add(new MinerKilled(m.Id));
            }
```

Change to:

```csharp
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                m.DeathCause = DeathCause.Exploded;
                _events.Add(new MinerKilled(m.Id));
            }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all four amended tests pass, full suite green.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/DeathCause.cs src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationKillTests.cs src/Miner49er.Core.Tests/SimulationMovementTests.cs src/Miner49er.Core.Tests/FloodTests.cs src/Miner49er.Core.Tests/SimulationExplosiveTests.cs
git commit -m "feat(core): tag each miner death with a DeathCause

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Replicate the cause through the snapshot

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs:5-7`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs:11-15`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs` (miner write loop ~22-24, miner read loop ~66-69)
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, add this test method inside the `SnapshotCodecTests` class (after `Round_trips_empty_collections`):

```csharp
    [Fact]
    public void Round_trips_death_cause()
    {
        var update = new TickUpdate(
            new WorldSnapshot(1,
                new List<MinerSnapshot>
                {
                    new(1, 0, 0, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Drowned),
                    new(2, 1, 1, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Exploded),
                    new(3, 2, 2, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Left),
                    new(4, 3, 3, 0, true,  0, 0, 0.0, 0.1, 5, -1),
                },
                new List<ChargeSnapshot>(), new List<ItemSnapshot>(), new List<MoldSnapshot>()),
            new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.Equal(DeathCause.Drowned, back.Snapshot.Miners[0].Cause);
        Assert.Equal(DeathCause.Exploded, back.Snapshot.Miners[1].Cause);
        Assert.Equal(DeathCause.Left, back.Snapshot.Miners[2].Cause);
        Assert.Equal(DeathCause.None, back.Snapshot.Miners[3].Cause);
    }
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~Round_trips_death_cause`
Expected: BUILD FAILURE — `MinerSnapshot` has no `Cause` member and the 12-arg constructor does not exist.

- [ ] **Step 3: Add `Cause` to `MinerSnapshot`**

In `src/Miner49er.Core/Net/Snapshots.cs`, replace the `MinerSnapshot` record (lines 5-7):

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held);
```

with:

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held,
    DeathCause Cause = DeathCause.None);
```

- [ ] **Step 4: Populate `Cause` in `SnapshotFactory`**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, the miner projection (lines 11-15) currently ends with `m.Held is { } h ? (int)h : -1))`. Replace that closing argument so the projection reads:

```csharp
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id),
                m.Held is { } h ? (int)h : -1, m.DeathCause))
```

- [ ] **Step 5: Serialize `Cause` in `SnapshotCodec`**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`, in the miner **write** loop, the body currently ends with `w.Write(m.MoveSeconds); w.Write(m.VisionRadius); w.Write(m.Held);`. Change that line to:

```csharp
            w.Write(m.MoveSeconds); w.Write(m.VisionRadius); w.Write(m.Held); w.Write((byte)m.Cause);
```

In the miner **read** loop, the constructor call currently ends with `r.ReadInt32(), r.ReadInt32()));` (VisionRadius then Held). Change it to:

```csharp
            miners.Add(new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
                r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte()));
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — `Round_trips_death_cause` passes and the existing `Round_trips_all_fields` still passes (its miners default to `Cause = None` on both sides).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "feat(net): replicate DeathCause on the miner snapshot

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Client `DeathFeed` — center banner + stacking toast feed

No unit tests (Godot UI is verified by play-test, consistent with prior phases). Verification is a clean build plus a manual play-test.

**Files:**
- Create: `game/ui/DeathFeed.cs`
- Modify: `game/Main.cs` (`_Ready`, after the `_hud` block ~line 69-71)

- [ ] **Step 1: Create the `DeathFeed` node**

Create `game/ui/DeathFeed.cs` (TAB indent):

```csharp
using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Watches each miner's Alive flag flip true->false and announces the
/// death using the authoritative MinerSnapshot.Cause: a center banner for the
/// local miner, a short-lived stacking toast (top-right) for everyone else.</summary>
public partial class DeathFeed : CanvasLayer
{
	private MatchClient _client = null!;
	private readonly Dictionary<int, bool> _prevAlive = new();

	private Label _banner = null!;
	private float _bannerLife;
	private const float BannerSeconds = 3f;

	private VBoxContainer _feed = null!;
	private readonly List<(Label label, float life)> _toasts = new();
	private const float ToastSeconds = 4f;
	private const int MaxToasts = 4;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		_banner = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.4f, AnchorBottom = 0.4f,
			Modulate = new Color(1, 1, 1, 0f),
		};
		_banner.AddThemeFontSizeOverride("font_size", 48);
		AddChild(_banner);

		_feed = new VBoxContainer
		{
			AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
			OffsetLeft = -320, OffsetTop = 16, OffsetRight = -16,
			GrowHorizontal = Control.GrowDirection.Begin,
		};
		AddChild(_feed);
	}

	public override void _Process(double delta)
	{
		if (_client == null) return;
		DetectDeaths();
		TickBanner((float)delta);
		TickToasts((float)delta);
	}

	private void DetectDeaths()
	{
		foreach (var m in _client.Miners)
		{
			bool prev = !_prevAlive.TryGetValue(m.Id, out var a) || a; // assume alive until first seen
			if (prev && !m.Alive && m.Cause != DeathCause.None)
			{
				if (m.Id == _client.LocalMinerId) ShowBanner(m.Cause);
				else PushToast(m.Id, m.Cause);
			}
			_prevAlive[m.Id] = m.Alive;
		}
	}

	private void ShowBanner(DeathCause cause)
	{
		_banner.Text = cause switch
		{
			DeathCause.Drowned => "YOU HAVE DROWNED",
			DeathCause.Exploded => "YOU WERE BLOWN UP",
			_ => "YOU DIED",
		};
		_bannerLife = BannerSeconds;
	}

	private void TickBanner(float delta)
	{
		if (_bannerLife <= 0f) { _banner.Modulate = new Color(1, 1, 1, 0f); return; }
		_bannerLife -= delta;
		_banner.Modulate = new Color(1, 1, 1, Mathf.Min(1f, _bannerLife)); // fade over the last second
	}

	private void PushToast(int minerId, DeathCause cause)
	{
		string name = NameOf(minerId);
		string text = cause switch
		{
			DeathCause.Drowned => $"{name} drowned",
			DeathCause.Exploded => $"{name} was blown up",
			DeathCause.Left => $"{name} left",
			_ => $"{name} died",
		};
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 18);
		_feed.AddChild(label);
		_feed.MoveChild(label, 0); // newest on top
		_toasts.Add((label, ToastSeconds));

		while (_toasts.Count > MaxToasts)
		{
			var oldest = _toasts[0];
			_toasts.RemoveAt(0);
			if (IsInstanceValid(oldest.label)) oldest.label.QueueFree();
		}
	}

	private void TickToasts(float delta)
	{
		for (int i = _toasts.Count - 1; i >= 0; i--)
		{
			var t = _toasts[i];
			t.life -= delta;
			if (t.life <= 0f)
			{
				if (IsInstanceValid(t.label)) t.label.QueueFree();
				_toasts.RemoveAt(i);
			}
			else
			{
				if (IsInstanceValid(t.label))
					t.label.Modulate = new Color(1, 1, 1, Mathf.Min(1f, t.life));
				_toasts[i] = t;
			}
		}
	}

	private static string NameOf(int minerId)
	{
		var nm = NetworkManager.Instance;
		int idx = minerId - 1;
		if (idx >= 0 && idx < nm.PeerOrder.Length
			&& nm.Players.TryGetValue(nm.PeerOrder[idx], out var info))
			return info.Name;
		return $"Miner {minerId}";
	}
}
```

- [ ] **Step 2: Wire `DeathFeed` into `Main`**

In `game/Main.cs`, add a field beside the other node fields (after `private Compass _compass = null!;`):

```csharp
	private DeathFeed _deathFeed = null!;
```

Then in `_Ready()`, immediately after the existing HUD block:

```csharp
		_hud = new Hud { Name = "Hud" };
		AddChild(_hud);
```

add:

```csharp
		_deathFeed = new DeathFeed { Name = "DeathFeed" };
		AddChild(_deathFeed);
		_deathFeed.Init(_client);
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Play-test (user)**

Launch a match (host + at least one client, flooding on). Verify:
- Walking your own miner into deep water shows **YOU HAVE DROWNED**, fading after ~3s into the existing "Dead — spectating" HUD.
- A rival drowning shows a **"{name} drowned"** toast top-right (the original gap).
- A rival caught in a blast shows **"{name} was blown up"**; your own blast death shows **YOU WERE BLOWN UP**.
- Multiple deaths stack as toasts (newest on top) and fade.

- [ ] **Step 5: Commit**

```bash
git add game/ui/DeathFeed.cs game/Main.cs
git commit -m "feat(game): death banner for the victim and a kill-feed toast for others

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `MatchAudio` — pick splash-vs-death SFX from `Cause`

**Files:**
- Modify: `game/net/MatchAudio.cs:76-84`

- [ ] **Step 1: Replace the tile heuristic with the cause field**

In `game/net/MatchAudio.cs`, the death-SFX block currently reads:

```csharp
				bool prevAlive = !_prevAlive.TryGetValue(m.Id, out var al) || al;
				if (prevAlive && !m.Alive)
				{
					var tile = new GridPos(m.X, m.Y);
					bool drowned = _client.Grid.InBounds(tile)
						&& _client.Grid.Get(tile) == TileType.DeepWater;
					OneShot(drowned ? SfxLibrary.Splash : SfxLibrary.Death, WorldOf(m.X, m.Y));
				}
				_prevAlive[m.Id] = m.Alive;
```

Replace it with:

```csharp
				bool prevAlive = !_prevAlive.TryGetValue(m.Id, out var al) || al;
				if (prevAlive && !m.Alive)
					OneShot(m.Cause == DeathCause.Drowned ? SfxLibrary.Splash : SfxLibrary.Death,
						WorldOf(m.X, m.Y));
				_prevAlive[m.Id] = m.Alive;
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Play-test (user)**

In a match: drowning plays the splash cue; a blast death plays the death cue. (Behavior is unchanged for these common cases; the change just removes the tile heuristic and fixes a dry-land disconnect playing the non-splash cue.)

- [ ] **Step 4: Commit**

```bash
git add game/net/MatchAudio.cs
git commit -m "refactor(game): drive death SFX from DeathCause, not the tile

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Completion

After all four tasks: announce and use **superpowers:finishing-a-development-branch** to verify the Core suite (`dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`), then present merge/PR options. Merge only with explicit user authorization. Branch: `death-cause-messaging`.
