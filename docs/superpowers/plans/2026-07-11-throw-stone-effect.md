# Throw-Stone Animation & Effect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing stone-throw mechanic visible and audible: network the throw, render an arcing stone + landing "noise ping", play a throw pose on the thrower, and add throw SFX.

**Architecture:** Core gains a `StoneThrowSnapshot` data record carried on the world snapshot (data only — no sim change). The host translates the already-emitted `StoneThrown` event into that snapshot list; the client fans it out to `WorldRenderer` (projectile + landing effect), a `_throwUntil` pose timer, and `MatchAudio` (swish + clack), mirroring the existing `PortalUses`/`Whistles`/`Exploded` fan-out.

**Tech Stack:** Godot 4.6 / C# / .NET 10; `Miner49er.Core` (engine-free, unit-tested) for the snapshot + codec; PixelLab MCP for the default miner's throw pose.

## Global Constraints

- **Host-authoritative, deterministic Core.** Only data + codec are added to Core; no sim-logic change.
- **Wire safety.** Append `Throws` after `PortalUses` in both the record and the codec write/read order; never reorder existing fields. Absent throws → count 0.
- **Run `godot` via PowerShell only** (Bash shim breaks headless import). Import art with `godot --headless --import --quit`.
- **Never `git add -A`.** Stage by explicit path. Do not stage `*.uid`, `.superpowers/`, `Temp/`, `_preview_*`, or unrelated edits.
- **PixelLab API key** never in a memory file or committed.
- End every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Verification: Core tasks use `dotnet test` (TDD); Godot tasks use `dotnet build` + the stated manual check.

## Setup (before Task 1)

```bash
git checkout -b feat/throw-stone-effect
```

## File Structure

- **Modify `src/Miner49er.Core/Net/Snapshots.cs`** — add `StoneThrowSnapshot` record + `Throws` field on `WorldSnapshot`.
- **Modify `src/Miner49er.Core/Net/SnapshotCodec.cs`** — write/read the `Throws` list.
- **Create `src/Miner49er.Core.Tests/SnapshotCodecThrowTests.cs`** — codec round-trip.
- **Modify `game/net/MatchHost.cs`** — translate `StoneThrown` → `StoneThrowSnapshot`.
- **Modify `game/net/MatchClient.cs`** — consume throws: `AddThrownStone`, `_throwUntil` pose timer, `StoneTossed` event, `StoneFlightSeconds` const, throw texture category.
- **Modify `game/WorldRenderer.cs`** — arcing stone + shadow + landing ping/dust.
- **Modify `game/audio/SfxLibrary.cs`** — `StoneThrow` + `StoneLand` synths.
- **Modify `game/net/MatchAudio.cs`** — play swish + deferred clack.
- **Create `assets/miners/throw/{n,e,s,w}{0..3}.png`** — default miner throw pose (PixelLab).
- **Modify `docs/superpowers/plans/2026-07-11-miner-character-variants.md`** — fold `throw` into the variant refactor + art briefs.

---

### Task 1: Core snapshot record + codec + round-trip test (TDD)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs:35` (add record near `PortalUseSnapshot`), `:50` (add field)
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs:85-87` (write), `:186-192` (read), `:199-202` (construct)
- Create: `src/Miner49er.Core.Tests/SnapshotCodecThrowTests.cs`

**Interfaces:**
- Produces: `StoneThrowSnapshot(int ThrowerId, int FromX, int FromY, int ToX, int ToY)`; `WorldSnapshot.Throws : IReadOnlyList<StoneThrowSnapshot>?`.

- [ ] **Step 1: Write the failing test** — `src/Miner49er.Core.Tests/SnapshotCodecThrowTests.cs`

```csharp
using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecThrowTests
{
    private static WorldSnapshot Empty(IReadOnlyList<StoneThrowSnapshot>? throws) =>
        new(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>(), Throws: throws);

    [Fact]
    public void Throws_survive_round_trip()
    {
        var throws = new List<StoneThrowSnapshot>
        {
            new(2, 5, 6, 9, 6),
            new(1, 0, 0, 3, 4),
        };
        var update = new TickUpdate(Empty(throws), new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.Throws);
        Assert.Equal(2, back.Snapshot.Throws!.Count);
        Assert.Equal(new StoneThrowSnapshot(2, 5, 6, 9, 6), back.Snapshot.Throws[0]);
        Assert.Equal(new StoneThrowSnapshot(1, 0, 0, 3, 4), back.Snapshot.Throws[1]);
    }

    [Fact]
    public void No_throws_round_trips_as_null()
    {
        var update = new TickUpdate(Empty(null), new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.Throws);
    }
}
```

- [ ] **Step 2: Run the test — verify it fails to compile**

Run: `dotnet test --filter SnapshotCodecThrowTests`
Expected: FAIL — `StoneThrowSnapshot` / `Throws` do not exist.

- [ ] **Step 3: Add the record + field** (`src/Miner49er.Core/Net/Snapshots.cs`)

After line 35 (`PortalUseSnapshot`):

```csharp
public readonly record struct StoneThrowSnapshot(int ThrowerId, int FromX, int FromY, int ToX, int ToY);
```

Extend the `WorldSnapshot` record — change the last line (`:50`) from `PortalUses = null);` to:

```csharp
    IReadOnlyList<PortalUseSnapshot>?        PortalUses       = null,
    IReadOnlyList<StoneThrowSnapshot>?       Throws           = null);
```

- [ ] **Step 4: Write the list in the codec** (`src/Miner49er.Core/Net/SnapshotCodec.cs`, right after the `PortalUses` write block ending at line 87)

```csharp
        w.Write(snap.Throws?.Count ?? 0);
        foreach (var th in snap.Throws ?? System.Array.Empty<StoneThrowSnapshot>())
        { w.Write(th.ThrowerId); w.Write(th.FromX); w.Write(th.FromY); w.Write(th.ToX); w.Write(th.ToY); }
```

- [ ] **Step 5: Read the list** (`src/Miner49er.Core/Net/SnapshotCodec.cs`, right after the `portalUses` read loop ending at line 192)

```csharp
        int throwCount = r.ReadInt32();
        List<StoneThrowSnapshot>? throws = throwCount > 0
            ? new List<StoneThrowSnapshot>(throwCount) : null;
        for (int i = 0; i < throwCount; i++)
            throws!.Add(new StoneThrowSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
```

Then extend the final `WorldSnapshot` construction (lines 199-202) to pass it — change `PortalUses: portalUses), changes);` to:

```csharp
            Whistles: whistles, PortalUses: portalUses, Throws: throws), changes);
```

(The `Throws` read block must sit **before** the `changeCount = r.ReadInt32();` line so the read order matches the write order.)

- [ ] **Step 6: Run the test — verify it passes**

Run: `dotnet test --filter SnapshotCodecThrowTests`
Expected: PASS (both tests).

- [ ] **Step 7: Run the full Core suite** to confirm no regression in existing codec tests.

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecThrowTests.cs
git commit -m "feat(throw): carry StoneThrowSnapshot on the world snapshot"
```

---

### Task 2: Host translates `StoneThrown` → snapshot

**Files:**
- Modify: `game/net/MatchHost.cs` (event switch ~193-238; snapshot assembly ~240-253)

**Interfaces:**
- Consumes: `StoneThrowSnapshot` (Task 1); `StoneThrown(int MinerId, GridPos LandingPos)` (existing Core event); `_sim.GetMiner(id).Pos`.

- [ ] **Step 1: Collect throws in the event loop** — in `TickAndBroadcast`, add a local list beside `screeCollapses` (~192):

```csharp
		var stoneThrows = new List<StoneThrowSnapshot>();
```

Add a case inside the `foreach (var e in _sim.DrainEvents())` switch (alongside the others, ~234):

```csharp
					case StoneThrown st:
					{
						var from = _sim.GetMiner(st.MinerId).Pos;
						stoneThrows.Add(new StoneThrowSnapshot(st.MinerId, from.X, from.Y, st.LandingPos.X, st.LandingPos.Y));
						break;
					}
```

- [ ] **Step 2: Attach to the snapshot** — after the `_portalUses` block (~248-252), add:

```csharp
			if (stoneThrows.Count > 0)
				snapshot = snapshot with { Throws = stoneThrows };
```

- [ ] **Step 3: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`. (`StoneThrowSnapshot` resolves via `using Miner49er.Core.Net;` already present in MatchHost.)

- [ ] **Step 4: Commit**

```bash
git add game/net/MatchHost.cs
git commit -m "feat(throw): host networks StoneThrown as a snapshot"
```

---

### Task 3: Client fan-out — projectile hook, pose timer, audio event

**Files:**
- Modify: `game/net/MatchClient.cs` (events ~44; fields ~82; `ApplyUpdate` throws block ~192-203; reset ~265; a `StoneFlightSeconds` const)

**Interfaces:**
- Consumes: `WorldSnapshot.Throws` (Task 1); `WorldRenderer.AddThrownStone(Vector2, Vector2)` (Task 4 — declared here as a call, implemented in Task 4).
- Produces: `MatchClient.StoneFlightSeconds : double` const; `MatchClient.StoneTossed : Action<Vector2, Vector2>?`; `_throwUntil` dict populated for the thrower.

- [ ] **Step 1: Add the const, event, and pose-timer field** (`game/net/MatchClient.cs`)

Near the other public events (~44):

```csharp
		public event System.Action<Vector2, Vector2>? StoneTossed; // origin, landing (world coords)
```

Near `_walkUntil` (~82):

```csharp
		private readonly Dictionary<int, double> _throwUntil = new();
```

Add the shared flight-duration const near the other consts (e.g., beside `TileSize`):

```csharp
		public const double StoneFlightSeconds = 0.35;
```

- [ ] **Step 2: Consume throws in `ApplyUpdate`** — after the `PortalUses` block (~203), add:

```csharp
			if (update.Snapshot.Throws is { } throws)
			{
				double now = Time.GetTicksMsec() / 1000.0;
				foreach (var th in throws)
				{
					var from = new Vector2(th.FromX * TileSize + TileSize / 2f, th.FromY * TileSize + TileSize / 2f);
					var to   = new Vector2(th.ToX   * TileSize + TileSize / 2f, th.ToY   * TileSize + TileSize / 2f);
					_world?.AddThrownStone(from, to);
					_throwUntil[th.ThrowerId] = now + StoneFlightSeconds;
					StoneTossed?.Invoke(from, to);
				}
			}
```

- [ ] **Step 3: Clear the pose timer on reset** — next to `_walkUntil.Clear();` (~265):

```csharp
		_throwUntil.Clear();
```

- [ ] **Step 4: Compile**

Run: `dotnet build`
Expected: FAIL — `WorldRenderer` has no `AddThrownStone`. That method is added in Task 4; this compile failure is expected and resolved there. (If executing strictly task-by-task, proceed to Task 4 before re-running.)

- [ ] **Step 5: Commit** (after Task 4 makes it compile — or commit Tasks 3+4 together)

```bash
git add game/net/MatchClient.cs
git commit -m "feat(throw): client fan-out for thrown stones (projectile hook, pose timer, audio event)"
```

---

### Task 4: `WorldRenderer` — arcing stone + landing ping

**Files:**
- Modify: `game/WorldRenderer.cs` (effect-list fields ~14-16; `AddExplosionRing` area ~314-315; `_Process` ~326-339; effect-draw block ~936-945)

**Interfaces:**
- Consumes: `MatchClient.StoneFlightSeconds` (Task 3), `MatchClient.TileSize`, `_client.Fog.IsVisible`, `_client.FogRenderer?.SpectatorMode`.
- Produces: `WorldRenderer.AddThrownStone(Vector2 from, Vector2 to)`.

- [ ] **Step 1: Add the two effect lists** (`game/WorldRenderer.cs`, beside `_rings`/`_rockfallDusts` ~16)

```csharp
	private readonly List<(Vector2 from, Vector2 to, float life)> _throws = new();
	private readonly List<(Vector2 center, float life)> _stoneImpacts = new();
```

- [ ] **Step 2: Add the spawn method** (near `AddRockfallDust` ~315)

```csharp
	public void AddThrownStone(Vector2 from, Vector2 to) => _throws.Add((from, to, 0f));
```

- [ ] **Step 3: Advance both lists in `_Process`** — inside `_Process`, before `QueueRedraw();` (~339):

```csharp
		float flightDur = (float)MatchClient.StoneFlightSeconds;
		for (int i = _throws.Count - 1; i >= 0; i--)
		{
			var t = _throws[i];
			t.life += (float)delta;
			if (t.life >= flightDur) { _stoneImpacts.Add((t.to, 0f)); _throws.RemoveAt(i); }
			else _throws[i] = t;
		}
		const float ImpactDur = 0.45f;
		for (int i = _stoneImpacts.Count - 1; i >= 0; i--)
		{
			var s = _stoneImpacts[i];
			s.life += (float)delta;
			if (s.life >= ImpactDur) _stoneImpacts.RemoveAt(i);
			else _stoneImpacts[i] = s;
		}
```

- [ ] **Step 4: Draw the effects** — in `_Draw`, right after the rockfall-dust block (~945), add:

```csharp
		// Thrown stones: a pebble arcing from thrower to landing tile, with a ground shadow.
		bool throwSpectating = _client.FogRenderer?.SpectatorMode == true;
		float throwDur = (float)MatchClient.StoneFlightSeconds;
		foreach (var (from, to, life) in _throws)
		{
			var landTile = new GridPos((int)(to.X / ts), (int)(to.Y / ts));
			if (!_client.Fog.IsVisible(landTile) && !throwSpectating) continue;
			float tt   = Mathf.Clamp(life / throwDur, 0f, 1f);
			var flat   = from.Lerp(to, tt);
			float hop  = ts * 0.6f * 4f * tt * (1f - tt);       // parabolic rise/fall
			// Shadow shrinks as the stone rises.
			float shadowR = ts * 0.16f * (1f - 0.5f * (hop / (ts * 0.6f)));
			DrawCircle(flat, shadowR, new Color(0f, 0f, 0f, 0.28f));
			// Pebble.
			DrawCircle(new Vector2(flat.X, flat.Y - hop), ts * 0.13f, new Color(0.62f, 0.60f, 0.56f));
			DrawCircle(new Vector2(flat.X, flat.Y - hop), ts * 0.13f, new Color(0.30f, 0.28f, 0.26f), false, 1.2f);
		}
		// Landing "noise ping" + dust puff.
		const float StoneImpactDur = 0.45f;
		foreach (var (center, life) in _stoneImpacts)
		{
			var impTile = new GridPos((int)(center.X / ts), (int)(center.Y / ts));
			if (!_client.Fog.IsVisible(impTile) && !throwSpectating) continue;
			float p = Mathf.Clamp(life / StoneImpactDur, 0f, 1f);
			float alpha = 1f - p;
			DrawCircle(center, ts * 0.30f * (1f - p), new Color(0.55f, 0.50f, 0.42f, alpha * 0.35f)); // dust
			DrawArc(center, ts * 0.55f * p, 0f, Mathf.Tau, 40, new Color(0.9f, 0.9f, 0.85f, alpha * 0.6f), 2f); // ping ring
		}
```

- [ ] **Step 5: Compile** (this also resolves Task 3's pending `AddThrownStone` reference)

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Manual verify — projectile**

Run the game (PowerShell): `godot` → solo Expedition → pick up stones (walk over a stone pile) → face a direction and press the Throw key (`throw_stone`). Confirm: a pebble arcs from the miner to the landing tile with a moving shadow, then a dust puff + expanding pale ring at the landing spot. Throw toward a wall and confirm it lands just before the wall.

- [ ] **Step 7: Commit** (Tasks 3 + 4 together if not already committed)

```bash
git add game/net/MatchClient.cs game/WorldRenderer.cs
git commit -m "feat(throw): arcing thrown-stone projectile + landing noise ping"
```

---

### Task 5: Throw SFX

**Files:**
- Modify: `game/audio/SfxLibrary.cs` (add two entries near `ReelSnap` ~37)
- Modify: `game/net/MatchAudio.cs` (subscribe/unsubscribe `StoneTossed`; handler near `OnPortaled` ~281)

**Interfaces:**
- Consumes: `MatchClient.StoneTossed` (Task 3), `MatchClient.StoneFlightSeconds`.
- Produces: `SfxLibrary.StoneThrow`, `SfxLibrary.StoneLand`.

- [ ] **Step 1: Add the two synth entries** (`game/audio/SfxLibrary.cs`, after `ReelSnap` ~37)

```csharp
	public static AudioStream StoneThrow => Get("stone_throw", () => Noise(0.14f, 900f, decay: true));  // light swish on release
	public static AudioStream StoneLand  => Get("stone_land",  () => Noise(0.10f, 500f, decay: true));  // dry clack on landing
```

- [ ] **Step 2: Subscribe to `StoneTossed`** — wherever `MatchAudio` wires the other client events (it already handles `OnExploded`/`OnPortaled`; find where `_client.Portaled += OnPortaled;` is registered) add:

```csharp
		_client.StoneTossed += OnStoneTossed;
```

and the matching unsubscribe alongside the other `-=` cleanups (if the class unsubscribes; match the existing pattern for `Portaled`).

- [ ] **Step 3: Add the handler** (near `OnPortaled` ~281)

```csharp
	private void OnStoneTossed(Vector2 fromWorld, Vector2 toWorld)
	{
		OneShot(SfxLibrary.StoneThrow, fromWorld);
		var timer = GetTree().CreateTimer(MatchClient.StoneFlightSeconds);
		timer.Timeout += () => OneShot(SfxLibrary.StoneLand, toWorld);
	}
```

- [ ] **Step 4: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Manual verify — audio**

Run the game (PowerShell): `godot` → throw a stone. Confirm a soft swish plays at the thrower and a drier clack plays ~0.35 s later at the landing tile (roughly when the puff appears). Positional volume falls off with distance.

- [ ] **Step 6: Commit**

```bash
git add game/audio/SfxLibrary.cs game/net/MatchAudio.cs
git commit -m "feat(throw): swish-on-release + clack-on-landing SFX"
```

---

### Task 6: Throw pose plumbing (current texture arrays)

**Files:**
- Modify: `game/net/MatchClient.cs` (texture field ~80; build call ~134; draw block ~431-465)

**Interfaces:**
- Consumes: `_throwUntil` (Task 3), `StoneFlightSeconds` (Task 3), the existing `BuildMinerActivityTextures(string, int)` builder.
- Produces: `_minerThrowTex` field; a throw-priority branch in the draw block.

- [ ] **Step 1: Add the texture field** (`game/net/MatchClient.cs`, beside `_minerPlantTex` ~80)

```csharp
	private Texture2D[,,]? _minerThrowTex; // [colorIndex, facing, frame 0-3]
```

- [ ] **Step 2: Build it** (beside the other build calls ~134)

```csharp
		_minerThrowTex  = BuildMinerActivityTextures("throw", 4);
```

- [ ] **Step 3: Give the throw pose priority in the draw block** — replace the opening of the tex-selection (from `double drawNow = Time.GetTicksMsec() / 1000.0;` through the `if (m.Activity == 1 …` line, ~429-431) so the throw is checked first:

```csharp
			double drawNow = Time.GetTicksMsec() / 1000.0;
			Texture2D? tex;
			bool throwing = _throwUntil.TryGetValue(m.Id, out double throwEnd) && drawNow < throwEnd && _minerThrowTex != null;
			if (throwing)
			{
				double telapsed = MatchClient.StoneFlightSeconds - (throwEnd - drawNow);
				int frame = Mathf.Clamp((int)(telapsed / MatchClient.StoneFlightSeconds * 4), 0, 3);
				tex = _minerThrowTex![colorIdx, facing, frame];
			}
			else if (m.Activity == 1 && m.ActivityRemaining > 0 && _minerMineTex != null)
```

(The rest of the `else if` chain is unchanged.)

- [ ] **Step 4: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`. (`BuildMinerActivityTextures("throw", 4)` returns null-filled textures until the art exists — the `_minerThrowTex != null` guard means the miner just keeps its normal pose for now, so no regression.)

- [ ] **Step 5: Commit**

```bash
git add game/net/MatchClient.cs
git commit -m "feat(throw): throw-pose selection wired to the thrower (art follows)"
```

---

### Task 7: Default miner throw pose (PixelLab)

**Files:**
- Create: `assets/miners/throw/{n,e,s,w}{0,1,2,3}.png` (+ `.import`)

**Interfaces:**
- Consumes: the loader path `res://assets/miners/throw/{dir}{frame}.png` and 4-frame count from Task 6's `BuildMinerActivityTextures("throw", 4)`. Folder/frame names MUST match exactly.

- [ ] **Step 1: Inspect the reference** — read `assets/miners/miner_s.png` (and a `walk`/`mine` frame) to capture the default miner's canvas size, top-down 4-direction style, and palette, so the recreated character matches.

- [ ] **Step 2: Recreate the default miner** with PixelLab `create_character`: a classic hard-hat miner in overalls, **matching the existing default sprite** (same size/style/colours). Poll `get_character` to completion.

- [ ] **Step 3: Animate a throw** with `animate_character`: an **overhand throw** — wind-up → release → follow-through, **4 frames**, for all 4 directions (N/E/S/W). Poll to completion.

- [ ] **Step 4: Download** the 16 frames (curl the PixelLab URLs) to `assets/miners/throw/{n,e,s,w}{0,1,2,3}.png` (direction letter + zero-based frame index).

- [ ] **Step 5: Import** (PowerShell only):

Run: `godot --headless --import --quit`
Expected: no import errors for `assets/miners/throw/**`; each `.png` gets a sibling `.import`.

- [ ] **Step 6: Manual verify in-game** — throw a stone with the default (Classic) miner and confirm the throw pose plays during the arc in all four facings, then returns to the normal pose. Verify a remote/bot throw shows the pose on another client's view.

- [ ] **Step 7: Commit** (stage only `.png` + `.import` — never `*.uid`):

```bash
git add assets/miners/throw
git commit -m "art(throw): default miner overhand throw pose (PixelLab)"
```

---

### Task 8: Fold `throw` into the miner-variants plan

The variants plan (`docs/superpowers/plans/2026-07-11-miner-character-variants.md`) refactors the miner textures into a lazy `(variant,colour)` set cache and generates full art per variant. Because the throw pose is now a 7th animation category, update that plan so nothing is dropped when it runs after this feature.

**Files:**
- Modify: `docs/superpowers/plans/2026-07-11-miner-character-variants.md`

- [ ] **Step 1: Add `throw` to the set structures** — in the variants plan Task 2, add a `Throw` member to both `MinerTextureSet` and `MinerSourceImages` (`Texture2D?[,] Throw = new Texture2D?[4, 4]` / `Image?[,] Throw = new Image?[4, 4]`), load it in `GetVariantSources` (`for (int f = 0; f < 4; f++) s.Throw[d, f] = LoadMinerImage(variant, $"throw/{dir[d]}{f}.png");`), and tint it in `GetOrBuildSet`.

- [ ] **Step 2: Preserve the throw-priority branch** — in the variants plan Task 2 draw-block rewrite, add the throw-first branch (the `_throwUntil` check from this plan's Task 6) using `set.Throw[facing, frame]`, ahead of the mine/plant/walk branches. Also note that the refactor removes the temporary `_minerThrowTex` field/build call this feature added (subsumed by the set cache).

- [ ] **Step 3: Add throw frames to each variant's art brief** — in the variants plan Tasks 5 and 6, add "throw = 4 frames" to the `animate_character` list and add `assets/miners/<slug>/throw/{n,e,s,w}{0..3}.png` to each variant's Files list.

- [ ] **Step 4: Commit** (the plan doc is normally untracked like the others; commit it here since it is a deliverable of this task)

```bash
git add docs/superpowers/plans/2026-07-11-miner-character-variants.md
git commit -m "docs(variants): fold throw pose category into the variants plan"
```

---

## Self-Review

**Spec coverage:**
- Network the throw (record + field + codec + host translate) → Tasks 1-2. ✅
- Wire safety (append after PortalUses, absent → count 0) → Task 1 Steps 3-5; test covers null case. ✅
- Client fan-out (projectile hook, pose timer, audio event) → Task 3. ✅
- Arcing stone + shadow + landing puff + noise ping, fog-culled → Task 4. ✅
- Throw pose category + priority; default via recreated PixelLab → Tasks 6-7. ✅
- Variants get their own throw sets; plan updated → Task 8. ✅
- Swish + deferred clack SFX → Task 5. ✅
- Testing: Core codec TDD (Task 1) + manual Godot (Tasks 4-7). ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete code. Task 7 is generative but pins exact output paths + frame counts.

**Type consistency:** `StoneThrowSnapshot(int ThrowerId,int FromX,int FromY,int ToX,int ToY)`, `WorldSnapshot.Throws`, `MatchClient.StoneFlightSeconds`, `MatchClient.StoneTossed`, `WorldRenderer.AddThrownStone(Vector2,Vector2)`, `_throwUntil`, `_minerThrowTex`, `SfxLibrary.StoneThrow/StoneLand` — used consistently across tasks. Codec read order (Throws before TileChanges) matches write order. The 4-frame throw count matches `BuildMinerActivityTextures("throw", 4)` and the art brief.

**Cross-plan note:** Task 3's compile intentionally fails until Task 4 adds `AddThrownStone`; commit Tasks 3+4 together (noted in both tasks).
