# Solo Expedition — Phase 2 (Plumbing & Play) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Solo Expedition actually playable — carry monsters + escape state across the snapshot pipeline, render monsters and the exit marker, wire a one-click solo launch from the main menu, and surface gold-remaining / escape / maul messaging in the HUD.

**Architecture:** Phase 1 already built the whole Expedition simulation in `Miner49er.Core` (monsters, AIs, contact/hazard/blast kills, escape tracking, `RoundResolver`). Phase 2 is the thin Godot adapter plus the small Core net-plumbing needed to transmit the new state. The Core changes (snapshot record, codec, factory, a roster-size helper) are pure and TDD'd headlessly. The Godot changes (host sim wiring, `MatchClient`/`WorldRenderer`/`MainMenu`/`Hud`/`DeathFeed`) are verified by `dotnet build` (0 warnings) and a final play-test — placeholder art only; real sprites are Phase 3.

**Tech Stack:** C# / .NET 8 (`Miner49er.Core` engine, 4-space indent), Godot 4.6.3 .NET adapter (`game/`, TAB indent), xUnit, deterministic 30 Hz host-authoritative sim.

---

## Background the implementer needs

**Project layout & conventions**
- `src/Miner49er.Core/` — pure C# engine, **4-space indent**, no Godot types. Unit-tested with xUnit in `src/Miner49er.Core.Tests/`.
- `game/` — Godot adapter, **TAB indent**, references `Miner49er.Core`. Not headless-unit-testable; verified by build + play-test.
- Build: `dotnet build Miner49er.sln` (expect 0 warnings, 0 errors).
- Core tests: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`.
- **Headless Godot boot** (sanity that the scene tree loads): run `godot` **via PowerShell ONLY** — `godot --headless --quit` from the project dir, expect exit 0. NEVER invoke `godot` through the Bash tool (a shim breaks headless with a false "assemblies not found").
- Commit messages MUST end with the trailer:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- Stage only the exact files each task names — NEVER `git add -A` (the working tree has pre-existing untracked junk: `.superpowers/`, `*.png.import`, `*.uid`).

**How the snapshot pipeline works today (read before Task 1):**
- `src/Miner49er.Core/Net/Snapshots.cs` defines `MinerSnapshot`, `ChargeSnapshot`, `ItemSnapshot`, `MoldSnapshot`, `TileChange`, the `WorldSnapshot` record, and `TickUpdate`.
- `src/Miner49er.Core/Net/SnapshotCodec.cs` serializes a `TickUpdate` to bytes and back (`Write`/`Read`). Format is **not versioned** — host and client both use this one codec, so the only rule is Write and Read stay byte-symmetric.
- `src/Miner49er.Core/Net/SnapshotFactory.cs` `Capture(Simulation, tick)` builds a `WorldSnapshot` from live sim state.
- Host loop `game/net/MatchHost.cs` calls `SnapshotFactory.Capture` + `SnapshotCodec.Write` each tick and broadcasts; `game/net/MatchClient.cs` `ApplyUpdate` consumes the decoded `TickUpdate` and drives rendering.

**Relevant Phase-1 Core API (already merged, do not re-implement):**
- `Simulation.Monsters` → `IReadOnlyList<Monster>`; `Monster` has `int Id`, `GridPos Pos`, `Direction Facing`, `MonsterKind Kind` (`Slime`/`Ghost`/`Goat`), `bool Alive`.
- `Simulation.AddMonster(int id, GridPos pos, MonsterKind kind)`.
- `Simulation.EscapeTile` (`GridPos?`), `Simulation.EscapeOpen` (`bool`), `Simulation.AllGoldCleared` (`bool`).
- `Simulation` ctor: `Simulation(TileGrid grid, SimConfig config, GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false, GridPos? escapeTile = null)`.
- `SimConfig.Seed` (int — seeds monster RNG), `MonsterSlimeMoveSeconds`, `MonsterGhostMoveSeconds`, `MonsterGoatMoveSeconds`, `MonsterSenseRadius`.
- `MonsterSpawner.Place(TileGrid grid, GridPos start, int count)` → `List<(GridPos Pos, MonsterKind Kind)>` (farthest-first from start, round-robin kinds).
- `GameMode.Expedition`; `RoundResolver.Resolve(sim, GameMode.Expedition)` returns win on (alive + all gold + on escape tile), loss (-1) on no survivors.
- `DeathCause.Mauled`.

---

## File structure

**Core (TDD):**
- Modify `src/Miner49er.Core/Net/Snapshots.cs` — add `MonsterSnapshot`; extend `WorldSnapshot` with `Monsters` + `EscapeOpen`.
- Modify `src/Miner49er.Core/Net/SnapshotCodec.cs` — read/write the monster block + escape flag.
- Modify `src/Miner49er.Core/Net/SnapshotFactory.cs` — populate monsters + escape flag.
- Create `src/Miner49er.Core/Map/MonsterRoster.cs` — pure roster-size helper.
- Modify tests: `SnapshotCodecTests.cs`, `SnapshotFactoryTests.cs`; create `MonsterRosterTests.cs`.

**Game (build + play-test):**
- Modify `game/Main.cs` — host builds the Expedition sim (seed, escape tile, monsters); HUD gold/escape text; Expedition result labels.
- Modify `game/net/MatchClient.cs` — carry monsters + escape state, smooth monster visuals, expose escape tile + gold-remaining.
- Modify `game/WorldRenderer.cs` — draw monsters (placeholder shapes) + exit marker.
- Modify `game/ui/MainMenu.cs` — solo Expedition launch button + `MatchStarting` scene change.
- Modify `game/ui/DeathFeed.cs` — `Mauled` banner/toast text.

---

## Task 1: MonsterSnapshot + WorldSnapshot fields (Core)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` (compile-fix existing constructions)

- [ ] **Step 1: Add the record + extend WorldSnapshot**

In `src/Miner49er.Core/Net/Snapshots.cs`, add after the `MoldSnapshot` record (line ~14):

```csharp
public readonly record struct MonsterSnapshot(
    int Id, int X, int Y, int Facing, MonsterKind Kind, bool Alive);
```

Replace the `WorldSnapshot` record with (adds `Monsters` as a required positional before the optional tail, plus `EscapeOpen` with a default so untouched call sites elsewhere stay minimal):

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    IReadOnlyList<MonsterSnapshot> Monsters,
    float SecondsRemaining = -1f, bool EscapeOpen = false);
```

- [ ] **Step 2: Build to find the broken call sites**

Run: `dotnet build src/Miner49er.Core/Miner49er.Core.csproj`
Expected: PASS (Core itself has no other `new WorldSnapshot(...)`). The test project and `SnapshotFactory`/`SnapshotCodec` are fixed in later steps/tasks; building just Core here should be clean.

- [ ] **Step 3: Fix the codec's WorldSnapshot construction temporarily**

`SnapshotCodec.Read` (last line) currently builds `new WorldSnapshot(tick, miners, charges, items, molds, secondsRemaining)`. To keep Core compiling until Task 2, change it to pass an empty monsters list:

```csharp
return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
    new List<MonsterSnapshot>(), secondsRemaining), changes);
```

Run: `dotnet build src/Miner49er.Core/Miner49er.Core.csproj`
Expected: PASS, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs
git commit -m "feat(net): MonsterSnapshot + WorldSnapshot monsters/escape fields

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: SnapshotCodec read/write monsters + escape flag (Core, TDD)

**Files:**
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

- [ ] **Step 1: Update existing tests to the new constructor shape + add a monster round-trip test**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`:

`Round_trips_all_fields` — change the `WorldSnapshot(...)` so a `Monsters` list is supplied and `SecondsRemaining` is named. Replace the `Molds:` line + `SecondsRemaining:` line with:

```csharp
                Molds: new List<MoldSnapshot> { new(4, 6, 12.5), new(0, 1, 3.0) },
                Monsters: new List<MonsterSnapshot>
                {
                    new(1, 7, 2, (int)Direction.South, MonsterKind.Slime, true),
                    new(2, 0, 9, (int)Direction.East, MonsterKind.Ghost, false),
                    new(3, 5, 5, (int)Direction.West, MonsterKind.Goat, true),
                },
                SecondsRemaining: 42.5f,
                EscapeOpen: true),
```

Add these assertions at the end of that test (before the closing brace):

```csharp
        Assert.Equal(3, back.Snapshot.Monsters.Count);
        Assert.Equal(update.Snapshot.Monsters[0], back.Snapshot.Monsters[0]);
        Assert.Equal(update.Snapshot.Monsters[1], back.Snapshot.Monsters[1]);
        Assert.Equal(update.Snapshot.Monsters[2], back.Snapshot.Monsters[2]);
        Assert.Equal(MonsterKind.Ghost, back.Snapshot.Monsters[1].Kind);
        Assert.False(back.Snapshot.Monsters[1].Alive);
        Assert.True(back.Snapshot.EscapeOpen);
```

`Round_trips_death_cause` — its `WorldSnapshot(1, ...miners..., charges, items, molds)` is missing the monsters arg. Add an empty monster list right after the `new List<MoldSnapshot>()` argument:

```csharp
                new List<ChargeSnapshot>(), new List<ItemSnapshot>(), new List<MoldSnapshot>(),
                new List<MonsterSnapshot>()),
```

`Round_trips_empty_collections` — same fix, and assert monsters/escape defaults. Replace its `WorldSnapshot(...)` line and add two assertions:

```csharp
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>()),
```

```csharp
        Assert.Empty(back.Snapshot.Monsters);
        Assert.False(back.Snapshot.EscapeOpen);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SnapshotCodecTests`
Expected: FAIL — `Round_trips_all_fields` asserts 3 monsters / `EscapeOpen` true but the codec writes neither yet (monsters come back empty, escape false).

- [ ] **Step 3: Write the monster block + escape flag into the codec**

In `src/Miner49er.Core/Net/SnapshotCodec.cs` `Write`, after the molds loop (the `foreach (var mo in snap.Molds)` block) and before the `update.TileChanges` block, insert:

```csharp
        w.Write(snap.Monsters.Count);
        foreach (var mo in snap.Monsters)
        {
            w.Write(mo.Id); w.Write(mo.X); w.Write(mo.Y);
            w.Write(mo.Facing); w.Write((int)mo.Kind); w.Write(mo.Alive);
        }

        w.Write(snap.EscapeOpen);
```

In `Read`, after the molds loop and before the `changeCount` read, insert:

```csharp
        int monsterCount = r.ReadInt32();
        var monsters = new List<MonsterSnapshot>(monsterCount);
        for (int i = 0; i < monsterCount; i++)
            monsters.Add(new MonsterSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadInt32(), (MonsterKind)r.ReadInt32(), r.ReadBoolean()));

        bool escapeOpen = r.ReadBoolean();
```

Replace the final `return` (from Task 1 Step 3) with:

```csharp
        return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
            monsters, secondsRemaining, escapeOpen), changes);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SnapshotCodecTests`
Expected: PASS (all 3 codec tests).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "feat(net): serialize monsters + escape flag in snapshot codec

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: SnapshotFactory captures monsters + escape (Core, TDD)

**Files:**
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs` (before the final closing brace):

```csharp
    [Fact]
    public void Captures_monsters_and_escape_flag()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: new GridPos(0, 0));
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Goat);

        var snap = SnapshotFactory.Capture(sim, tick: 4);

        var mo = Assert.Single(snap.Monsters);
        Assert.Equal(1, mo.Id);
        Assert.Equal(5, mo.X);
        Assert.Equal(5, mo.Y);
        Assert.Equal(MonsterKind.Goat, mo.Kind);
        Assert.True(mo.Alive);
        // No GoldRock on this all-Floor grid => escape opens immediately.
        Assert.True(snap.EscapeOpen);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter Captures_monsters_and_escape_flag`
Expected: FAIL to compile (no `Monsters`/`EscapeOpen` populated) or empty `snap.Monsters`.

- [ ] **Step 3: Populate monsters + escape in the factory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, add a monsters projection after the `molds` projection and pass both new args into the returned `WorldSnapshot`:

```csharp
        var monsters = sim.Monsters
            .Select(mo => new MonsterSnapshot(
                mo.Id, mo.Pos.X, mo.Pos.Y, (int)mo.Facing, mo.Kind, mo.Alive))
            .ToList();

        return new WorldSnapshot(tick, miners, charges, items, molds, monsters,
            (float)sim.SecondsRemaining, sim.EscapeOpen);
```

(Remove the old single-line `return new WorldSnapshot(... secondsRemaining);`.)

- [ ] **Step 4: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all 345+ tests green (Phase-1 344 + the new factory test).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs
git commit -m "feat(net): capture monsters + escape flag into world snapshot

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Monster roster-size helper (Core, TDD)

The host needs a deterministic, lightly map-scaled roster count (spec: fixed 3–5). Pure + testable.

**Files:**
- Create: `src/Miner49er.Core/Map/MonsterRoster.cs`
- Test: `src/Miner49er.Core.Tests/MonsterRosterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/MonsterRosterTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class MonsterRosterTests
{
    [Fact]
    public void Small_map_gets_the_floor_of_three()
    {
        Assert.Equal(3, MonsterRoster.CountFor(24, 24));
    }

    [Fact]
    public void Large_map_is_capped_at_five()
    {
        Assert.Equal(5, MonsterRoster.CountFor(40, 40));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void Never_below_three(int w, int h)
    {
        Assert.True(MonsterRoster.CountFor(w, h) >= 3);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MonsterRosterTests`
Expected: FAIL — `MonsterRoster` does not exist.

- [ ] **Step 3: Implement the helper**

Create `src/Miner49er.Core/Map/MonsterRoster.cs`:

```csharp
using System;

namespace Miner49er.Core;

/// <summary>Light, deterministic roster sizing for an Expedition: a fixed band of
/// 3–5 monsters that grows one step at a time with map area. Pure so the host can
/// pick the count before seeding <see cref="MonsterSpawner"/>.</summary>
public static class MonsterRoster
{
    public const int Min = 3;
    public const int Max = 5;

    /// <summary>One extra monster per ~512 tiles above the base 24x24 map, clamped to [3, 5].</summary>
    public static int CountFor(int width, int height)
    {
        int area = width * height;
        int extra = Math.Max(0, (area - 24 * 24) / 512);
        return Math.Clamp(Min + extra, Min, Max);
    }
}
```

Check the math against the tests: 24*24=576 → extra 0 → 3. 40*40=1600 → (1600-576)/512 = 2 → 5 (clamped). Good.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MonsterRosterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Map/MonsterRoster.cs src/Miner49er.Core.Tests/MonsterRosterTests.cs
git commit -m "feat(map): MonsterRoster.CountFor — 3-5 monsters by map area

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Host builds the Expedition sim — seed, escape tile, monsters (game)

The host-only branch in `Main._Ready` constructs the authoritative `Simulation`. For Expedition it must seed RNG deterministically, pass the escape tile (the solo miner's spawn), and populate monsters.

**Files:**
- Modify: `game/Main.cs` (host sim construction, ~lines 45-66)

- [ ] **Step 1: Set the sim seed and (Expedition) the escape tile**

In `game/Main.cs`, in the `if (nm.IsHost)` block, replace the `var sim = new Simulation(...)` construction with one that seeds RNG and passes the escape tile only for Expedition. The escape tile is the solo miner's spawn — `hostMap.Spawns[0]` (single-player has exactly one spawn at index 0):

```csharp
			var hostMap = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava));
			GridPos? escapeTile = nm.MatchMode == GameMode.Expedition ? hostMap.Spawns[0] : null;
			var sim = new Simulation(
				hostMap.Grid,
				new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = seed },
				hostMap.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding,
				escapeTile);
```

- [ ] **Step 2: Seed monsters after the miners are added**

Still in the host block, immediately after the `for` loop that adds miners (after the loop's closing brace, before `_host = new MatchHost ...`), add:

```csharp
				if (nm.MatchMode == GameMode.Expedition)
				{
					int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height);
					var roster = MonsterSpawner.Place(hostMap.Grid, hostMap.Spawns[0], monsterCount);
					for (int i = 0; i < roster.Count; i++)
						sim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
				}
```

> Note: `TileGrid` exposes `public int Width`/`public int Height` and `IEnumerable<GridPos> Positions()` (in `src/Miner49er.Core/Grid/TileGrid.cs`) — both confirmed present.

- [ ] **Step 3: Build**

Run (PowerShell): `dotnet build Miner49er.sln`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add game/Main.cs
git commit -m "feat(game): host seeds Expedition escape tile + monsters

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: MatchClient carries monsters, escape state, escape tile, gold count (game)

The render replica must expose monster snapshots (smoothed), the `EscapeOpen` flag, the escape tile (for the marker), and a gold-remaining count (for the HUD).

**Files:**
- Modify: `game/net/MatchClient.cs`

- [ ] **Step 1: Add fields + public accessors**

In `game/net/MatchClient.cs`, alongside the other `IReadOnlyList` accessors (after `public IReadOnlyList<MoldSnapshot> Molds => _molds;`), add:

```csharp
	public IReadOnlyList<MonsterSnapshot> Monsters => _monsters;
	public bool EscapeOpen { get; private set; }
	public GridPos? EscapeTile { get; private set; }
	public int GoldRemaining { get; private set; }
```

With the other backing lists (after `private List<MoldSnapshot> _molds = new();`):

```csharp
	private List<MonsterSnapshot> _monsters = new();
	private readonly Dictionary<int, Vector2> _monsterVisualPos = new(); // monsterId -> smoothed pixels
```

- [ ] **Step 2: Accept the escape tile in Begin**

Change the `Begin` signature to take an optional escape tile and store it. Replace:

```csharp
	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot)
	{
		Grid = grid;
		LocalMinerId = localMinerId;
		Decoys = decoys;
```

with:

```csharp
	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot, GridPos? escapeTile = null)
	{
		Grid = grid;
		LocalMinerId = localMinerId;
		Decoys = decoys;
		EscapeTile = escapeTile;
		GoldRemaining = CountGold(grid);
```

Add a small helper at the bottom of the class (before the final closing brace):

```csharp
	private static int CountGold(TileGrid grid)
	{
		int n = 0;
		foreach (var p in grid.Positions())
			if (grid.Get(p) == TileType.GoldRock) n++;
		return n;
	}
```

- [ ] **Step 3: Consume monsters + escape + gold in ApplyUpdate**

In `ApplyUpdate`, after `_molds = new List<MoldSnapshot>(update.Snapshot.Molds);`, add:

```csharp
			_monsters = new List<MonsterSnapshot>(update.Snapshot.Monsters);
			EscapeOpen = update.Snapshot.EscapeOpen;
			GoldRemaining = CountGold(Grid);
```

(`Grid` has already had this tick's `TileChange`s applied earlier in `ApplyUpdate`, so the count is current.)

- [ ] **Step 4: Smooth monster visuals in _PhysicsProcess**

In `_PhysicsProcess`, after the miner smoothing `foreach` loop and before `QueueRedraw();`, add monster smoothing (reuse each monster's per-kind cadence implicitly via a fixed visual speed — monsters move at most every 0.15s, so smooth at ~1 tile / its cadence is overkill; use the slime's slowest pace as a safe visual speed cap is wrong for goats. Instead smooth fast enough to keep up with the goat):

```csharp
			foreach (var mo in _monsters)
			{
				if (!mo.Alive) { _monsterVisualPos.Remove(mo.Id); continue; }
				var target = new Vector2(mo.X * TileSize + TileSize / 2f, mo.Y * TileSize + TileSize / 2f);
				var cur = _monsterVisualPos.TryGetValue(mo.Id, out var v) ? v : target;
				// Goat cadence is the fastest (~0.15s/tile); match it so no monster visually lags.
				float pixelsPerSec = TileSize / 0.15f;
				_monsterVisualPos[mo.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);
			}
```

- [ ] **Step 5: Expose the smoothed monster position to the renderer**

Add a public accessor so `WorldRenderer` can read smoothed monster pixels (after the `Monsters` accessor block, or near other getters):

```csharp
	public Vector2 MonsterVisualPos(int id, int x, int y) =>
		_monsterVisualPos.TryGetValue(id, out var v)
			? v : new Vector2(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);
```

- [ ] **Step 6: Build**

Run (PowerShell): `dotnet build Miner49er.sln`
Expected: PASS, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add game/net/MatchClient.cs
git commit -m "feat(game): MatchClient carries monsters, escape state, gold count

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: WorldRenderer draws monsters + exit marker (game, placeholder art)

**Files:**
- Modify: `game/WorldRenderer.cs`

- [ ] **Step 1: Add monster + marker colors**

In `game/WorldRenderer.cs`, with the other `static readonly Color` fields, add:

```csharp
	private static readonly Color SlimeColor = new("5fbf4f");
	private static readonly Color GhostColor = new("dfe8ff");
	private static readonly Color GoatColor  = new("b08050");
	private static readonly Color ExitColor  = new("ffe24a");
```

- [ ] **Step 2: Draw monsters (fog-gated) at the end of _Draw**

At the end of `_Draw` (after the `_flashes` loop), add monster rendering. Monsters are gated by fog like items/molds so they loom out of the dark; ghosts draw translucent:

```csharp
		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (mo.Kind != MonsterKind.Ghost && !_client.Fog.IsVisible(mp)) continue; // ghosts seen even through walls when lit
			if (!_client.Fog.IsVisible(mp)) continue;

			var c = _client.MonsterVisualPos(mo.Id, mo.X, mo.Y);
			switch (mo.Kind)
			{
				case MonsterKind.Slime:
					DrawCircle(c, ts * 0.34f, SlimeColor);
					break;
				case MonsterKind.Ghost:
					DrawCircle(c, ts * 0.36f, GhostColor with { A = 0.6f });
					break;
				case MonsterKind.Goat:
					DrawRect(new Rect2(c.X - ts * 0.3f, c.Y - ts * 0.3f, ts * 0.6f, ts * 0.6f), GoatColor);
					break;
			}
		}
```

> Note: the two consecutive `if (!_client.Fog.IsVisible(mp))` lines above are intentional but redundant — simplify to a single fog gate for all kinds (placeholder art doesn't need the ghost-through-wall special case yet):
>
> ```csharp
> 			if (!_client.Fog.IsVisible(mp)) continue;
> ```
>
> Use just the single line. (Phase 3 can revisit whether ghosts reveal through walls.)

- [ ] **Step 3: Draw the exit marker once escape opens**

Immediately after the monster loop, add:

```csharp
		if (_client.EscapeOpen && _client.EscapeTile is { } exit)
		{
			float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * Mathf.Pi * 2f / 0.9f);
			var col = ExitColor with { A = 0.4f + 0.4f * pulse };
			DrawRect(new Rect2(exit.X * ts, exit.Y * ts, ts, ts), col, false, 3f);
		}
```

- [ ] **Step 4: Build**

Run (PowerShell): `dotnet build Miner49er.sln`
Expected: PASS, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add game/WorldRenderer.cs
git commit -m "feat(game): render monsters (placeholder) + pulsing exit marker

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Solo Expedition launch from the main menu (game)

One-click solo: host a local-only game (no internet), set mode Expedition, start the match, and route to the match scene. `StartMatch` fires `MatchStarting`; the menu listens and changes scene (the Lobby normally does this, but solo skips the lobby).

**Files:**
- Modify: `game/ui/MainMenu.cs`

- [ ] **Step 1: Subscribe to MatchStarting and add the button**

In `game/ui/MainMenu.cs` `_Ready`, after the Join button is added (after `box.AddChild(joinBtn);`), add an Expedition button:

```csharp
			var soloBtn = new Button { Text = "Expedition (Solo)" };
			soloBtn.Pressed += OnSoloExpedition;
			box.AddChild(soloBtn);
```

At the end of `_Ready`, alongside the existing `JoinFailed` subscription, add:

```csharp
			NetworkManager.Instance.MatchStarting += OnMatchStarting;
```

In `_ExitTree`, unsubscribe:

```csharp
		public override void _ExitTree()
		{
			NetworkManager.Instance.JoinFailed -= OnJoinFailed;
			NetworkManager.Instance.MatchStarting -= OnMatchStarting;
		}
```

- [ ] **Step 2: Implement the launch + scene change**

Add these two methods to `MainMenu`:

```csharp
		private void OnSoloExpedition()
		{
			var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected, overInternet: false);
			if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
			// Solo: skip the lobby entirely — start an Expedition match immediately.
			// No time limit, no flood/pits/caveins/lava, standard speed (0.12s/tile).
			NetworkManager.Instance.StartMatch(GameMode.Expedition, 0, false, false, false, false, 0.12f);
		}

		private void OnMatchStarting()
		{
			GetTree().ChangeSceneToFile("res://game/Main.tscn");
		}
```

> Note: `StartMatch` uses `Players.Keys.ToArray()` for the peer order. After `HostGame`, `Players` holds exactly the host, so the solo match has one miner (`MatchPlayerCount == 1`) — correct for Expedition. `Main.cs` already maps `PeerOrder[0]` → miner id 1 at `hostMap.Spawns[0]`, which Task 5 uses as the escape tile.

- [ ] **Step 3: Build + headless boot**

Run (PowerShell): `dotnet build Miner49er.sln`
Expected: PASS, 0 warnings.

Run (PowerShell, from project root): `godot --headless --quit`
Expected: exit 0 (scene tree + autoloads load clean).

- [ ] **Step 4: Commit**

```bash
git add game/ui/MainMenu.cs
git commit -m "feat(game): one-click solo Expedition launch from main menu

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9: HUD gold-remaining + escape prompt, Mauled messaging, Expedition result labels (game)

**Files:**
- Modify: `game/Main.cs` (HUD text in `_PhysicsProcess`; `OnMatchEnded` label)
- Modify: `game/ui/DeathFeed.cs` (`Mauled` banner + toast)

- [ ] **Step 1: Mauled death messaging in DeathFeed**

In `game/ui/DeathFeed.cs` `ShowBanner`, add a `Mauled` arm before the `_ =>` default:

```csharp
			DeathCause.Burned => "BURNED ALIVE!",
			DeathCause.Mauled => "MAULED BY A MONSTER!",
			_ => "YOU DIED",
```

In `PushToast`, add a `Mauled` arm before the `_ =>` default:

```csharp
			DeathCause.Burned => $"{name} was incinerated",
			DeathCause.Mauled => $"{name} was mauled",
			_ => $"{name} died",
```

- [ ] **Step 2: HUD gold-remaining + escape prompt (Expedition only)**

In `game/Main.cs` `_PhysicsProcess`, inside the `if (m.Id == _client.LocalMinerId)` block, the HUD text is built as
`_hud.SetText($"Gold: {m.Gold}    {status}{timeStr}{heldStr}");`.
Replace that single line with mode-aware text that, in Expedition, shows gold-remaining and the escape prompt:

```csharp
						if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
						{
							string objective = _client.EscapeOpen
								? "  —  ESCAPE at your start!"
								: $"  —  Gold left: {_client.GoldRemaining}";
							_hud.SetText($"Gold: {m.Gold}    {status}{objective}");
						}
						else
						{
							_hud.SetText($"Gold: {m.Gold}    {status}{timeStr}{heldStr}");
						}
```

- [ ] **Step 3: Expedition win/lose label in OnMatchEnded**

In `game/Main.cs` `OnMatchEnded`, special-case Expedition so the result reads as escape/death rather than "Winner/Draw". Replace the `string label = ...` assignment with:

```csharp
			string label;
			if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
				label = winnerPeerId == NetworkManager.Instance.LocalId
					? "You escaped with the gold!"
					: "You died in the mine.";
			else
				label = winnerPeerId == -1
					? "Draw — no survivors"
					: $"Winner: {NameOf(winnerPeerId)}";
```

> Note: on an Expedition win, `MatchHost` maps the winning miner id back to the host peer, so `winnerPeerId == LocalId`. On a loss it broadcasts -1.

- [ ] **Step 4: Build + headless boot**

Run (PowerShell): `dotnet build Miner49er.sln`
Expected: PASS, 0 warnings.

Run (PowerShell, from project root): `godot --headless --quit`
Expected: exit 0.

- [ ] **Step 5: Commit**

```bash
git add game/Main.cs game/ui/DeathFeed.cs
git commit -m "feat(game): Expedition HUD objective, maul messaging, result labels

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 10: Full verification + play-test gate

**Files:** none (verification only).

- [ ] **Step 1: Full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all tests green (Phase-1 344 + Tasks 2/3/4 additions).

- [ ] **Step 2: Full solution build**

Run (PowerShell): `dotnet build Miner49er.sln`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 3: Headless boot**

Run (PowerShell, from project root): `godot --headless --quit`
Expected: exit 0.

- [ ] **Step 4: Hand off to the user for play-test**

Phase 2 is the first playable Expedition. The agent CANNOT play-test; report readiness and ask the user to launch the editor and verify:
- Main menu → "Expedition (Solo)" starts a solo match (no lobby).
- Monsters appear and move with distinct behaviours (slime wanders→chases, ghost phases walls, goat charges).
- Touching a monster kills the miner with the "MAULED BY A MONSTER!" banner.
- HUD shows "Gold left: N"; clearing the last gold flips it to "ESCAPE at your start!" and the exit marker pulses on the start tile.
- Standing on the start tile after clearing all gold shows "You escaped with the gold!"; dying shows "You died in the mine."

Do NOT proceed to finishing-a-development-branch (merge) until the user confirms the play-test, per the project's play-test-before-merge gate.

---

## Self-review notes (for the executor)

- **Spec coverage:** monster snapshot/codec/factory (Tasks 1-3), monster rendering + exit marker (Task 7), solo launch flow (Task 8), HUD gold-remaining + escape prompt (Task 9), monster spawning on the host (Task 5, using Phase-1 `MonsterSpawner` + new `MonsterRoster`). The spec's `MonsterMoved`/`MonsterKilled`/`MinerMauled`/`EscapeOpened` events already exist (Phase 1) but are intentionally NOT transported — the client renders monsters and escape purely from the per-tick `WorldSnapshot` (positions + `EscapeOpen` flag), and maul deaths surface via the existing `MinerSnapshot.Cause` path that `DeathFeed` already watches. This is simpler than event transport and loses nothing visible. New monster audio stings are deferred (no asset work in Phase 2).
- **Type consistency:** `WorldSnapshot` gains `Monsters` (required positional, before the optional `SecondsRemaining`) + `EscapeOpen` (optional, default false). All four constructions (codec `Read`, `SnapshotFactory.Capture`, and the three test fixtures) are updated in Tasks 1-3. `MonsterSnapshot` field order (`Id, X, Y, Facing, Kind, Alive`) is identical in the record, codec Write/Read, and factory.
- **Assumptions verified:** `TileGrid.Width`/`Height` and `Positions()` exist (`src/Miner49er.Core/Grid/TileGrid.cs`); `Fog.IsVisible(GridPos)`, `MatchClient.Grid`, and the `MatchStarting` event are all already used by existing code paths this plan extends.
