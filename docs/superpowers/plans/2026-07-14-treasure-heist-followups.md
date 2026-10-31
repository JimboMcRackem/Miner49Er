# Treasure Heist Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three Minor follow-ups the final Treasure Heist review logged: a clean competitive map, a hardened treasure-seeding guard, no Space-grab exploit on the loose urn, and a visible sudden-death countdown.

**Architecture:** All three are small, well-scoped fixes to the existing Treasure Heist mode. The sim stays engine-free (`Miner49er.Core`); the host runs it and clients render snapshots. Two tasks are pure Core (TDD); the third threads an already-declared snapshot field (`TreasureSnapshot.SuddenDeathProgress`) from sim → factory → HUD.

**Tech Stack:** Godot 4.6, C#, .NET 10, xUnit.

## Global Constraints

- `Miner49er.Core` is engine-free: no `Godot.*` types in `src/Miner49er.Core/**`.
- Determinism: no `System.Random`, `System.Guid`, or wall-clock time in sim paths. `Time.GetTicksMsec()` only in Godot presentation.
- Host-authoritative: the sim runs only on the host; do not add heist state to the `BeginMatch` RPC.
- Version control: never `git add -A`; stage only the exact files each task lists. Do not stage `.import`/`.uid`/`Temp/`/`Done/`/`.superpowers/` churn. Commit messages end with the trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Build: `dotnet build Miner49er.sln -c Debug` must be 0 warnings / 0 errors. Core tests: `dotnet test src/Miner49er.Core.Tests`. The Godot import step (`godot --headless --import --path "D:\Projects\Miner49er" --quit`) must be run in **PowerShell**, never the Bash tool (the Bash godot shim falsely reports "assemblies not found").
- Existing test count before this plan: 792. Each new test raises it.

---

### Task 1: Clean heist map + hardened treasure seeding

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs` (add to the existing file)

**Interfaces:**
- Consumes: `MapConfig.For(GameMode, int seed, int playerCount, …)`; `Simulation.AddItem(Item)`; `ItemKind.IsIdol()`.
- Produces: heist maps with no scattered gold/buff items; `_treasureKind`/`_treasurePos` seeded only from buried **idols**.

**Context:** `MapConfig.For` (`MapConfig.cs`) has per-mode branches. `TreasureHunt` (lines ~102-109) and `DemolitionDerby` (lines ~115-125) zero `GoldVeinCount`/`BaseItemCount`/`VisibleItemCount`/etc. The `TreasureHeist` branch (lines ~110-114) currently only sets `BuriedIdolKinds` and `ScatterStones`, so heist maps still scatter gold veins and buff items (SpeedPotion/Vision/Blast) — a deviation from the intended clean competitive map. Separately, `Simulation.AddItem` re-seeds the tracked treasure from **every** buried item while `!_treasureUnearthed` (last-buried-wins), so a buried non-idol added after the urn would hijack the treasure position.

- [ ] **Step 1: Write the failing tests**

Add these two tests to `src/Miner49er.Core.Tests/TreasureHeistTests.cs` (it already has `Cfg()`/`Grid()` helpers and `using` directives):

```csharp
    [Fact]
    public void Heist_map_has_no_scattered_gold_or_buff_items()
    {
        var cfg = MapConfig.For(GameMode.TreasureHeist, seed: 42, playerCount: 4,
            pits: false, caveIns: false, lava: false, mapScale: 1, explosive: ExplosiveMode.Dynamite);
        Assert.Equal(0, cfg.GoldVeinCount);
        Assert.Equal(0, cfg.BaseItemCount);
        Assert.Equal(0, cfg.VisibleItemCount);
    }

    [Fact]
    public void Buried_non_idol_does_not_hijack_the_treasure()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddItem(new Item(new GridPos(8, 9), ItemKind.IdolUrn, ItemPlacement.Buried)); // the treasure
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion, ItemPlacement.Buried)); // must not hijack
        Assert.Equal(new GridPos(8, 9), sim.TreasurePos);
    }
```

If `MapConfig.For`'s exact parameter list differs, match the real signature (inspect the file); the assertions on the three count fields are the point.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeist`
Expected: the two new tests FAIL (heist map still scatters items; the buried SpeedPotion reseeds `_treasurePos` to (2,2)).

- [ ] **Step 3: Clean the heist map**

In `src/Miner49er.Core/Map/MapConfig.cs`, replace the `if (mode == GameMode.TreasureHeist)` branch with (mirrors Derby's zeroing, keeps the urn + scattered stones):

```csharp
        if (mode == GameMode.TreasureHeist)
        {
            cfg.GoldVeinCount    = 0;
            cfg.BaseItemCount    = 0;
            cfg.VisibleItemCount = 0;
            cfg.WaterPlankCount  = 0;
            cfg.SlowMoldCount    = 0;
            cfg.LanternCount     = 0;
            cfg.ChestCount       = 0;
            cfg.DecoyCount       = 0;
            cfg.BuriedIdolKinds  = new[] { ItemKind.IdolUrn };
            cfg.ScatterStones    = 6;
        }
```

(If any of those `cfg.*` fields does not exist, drop that line — match the exact fields Derby's branch sets.)

- [ ] **Step 4: Harden the treasure seeding**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `AddItem`, add an idol guard so only buried idols seed the treasure:

```csharp
        if (Config.TreasureHeistMode && !_treasureUnearthed && item.Placement == ItemPlacement.Buried && item.Kind.IsIdol())
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeist` → PASS.

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test src/Miner49er.Core.Tests` → all green.

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
fix(heist): clean competitive map + seed treasure only from buried idols

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 2: Prevent Space-grabbing the loose treasure urn

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs` (add to the existing file)

**Interfaces:**
- Consumes: `Simulation.TryUseItem(int id)`; `Config.TreasureHeistMode`; `_treasureKind`.
- Produces: the tracked treasure urn cannot be picked up via the Use verb (Space); it is only ever adopted by walking onto it.

**Context:** For one tick between the mining-completion unbury (`AdvanceActivities`) and the next tick's `AdvanceTreasureHeist` adoption, the urn exists as a `Loose` item in `_items`. `TryUseItem` (`Simulation.cs`, the "pickup / swap when standing on a carried ground item" loop, ~line 1440) would let a player press Space in that window to grab the urn into their `Held` slot as an ordinary idol — untracked by the heist system. In heist, the urn must never be Space-grabbed.

- [ ] **Step 1: Write the failing test**

Add to `src/Miner49er.Core.Tests/TreasureHeistTests.cs`:

```csharp
    [Fact]
    public void Space_cannot_grab_the_loose_treasure_urn_in_heist()
    {
        var sim = new Simulation(Grid(), Cfg());
        var pos = new GridPos(5, 5);
        sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.IdolUrn, ItemPlacement.Loose)); // urn on the miner's tile
        bool used = sim.TryUseItem(1);
        Assert.False(used);                       // Space did nothing
        Assert.Null(sim.GetMiner(1).Held);        // urn was NOT taken into the held slot
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests --filter Space_cannot_grab`
Expected: FAIL — `TryUseItem` grabs the urn (`used == true`, `Held == IdolUrn`).

- [ ] **Step 3: Skip the treasure urn in TryUseItem**

In `src/Miner49er.Core/Sim/Simulation.cs`, inside the `TryUseItem` pickup loop, immediately after the existing `if (it.Pos != m.Pos || it.Placement == ItemPlacement.Buried || !it.Kind.IsCarried()) continue;` line, add:

```csharp
            // In Treasure Heist the tracked treasure is only adopted by walking onto it
            // (UpdateTreasureCarry); never let the Use verb grab it as an ordinary idol.
            if (Config.TreasureHeistMode && it.Kind == _treasureKind) continue;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests --filter Space_cannot_grab` → PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test src/Miner49er.Core.Tests` → all green (confirm no other TryUseItem/idol test regressed).

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
fix(heist): Space can't grab the tracked treasure urn as an ordinary idol

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 3: Surface the sudden-death countdown

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (accessor)
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs` (populate the field)
- Modify: `game/Main.cs` (HUD)
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs` (add to the existing file)

**Interfaces:**
- Consumes: `_suddenDeathHold`, `Config.SuddenDeathHoldSeconds`; `TreasureSnapshot.SuddenDeathProgress` (already declared); `_client.Treasure` on the client.
- Produces: `Simulation.SuddenDeathProgress` (0..1); factory populates `TreasureSnapshot.SuddenDeathProgress`; the heist HUD shows a sudden-death indicator.

**Context:** In buzzer mode, if the treasure is loose/held at 0:00 the round enters sudden-death: a lone uncontested holder accrues `_suddenDeathHold` toward `Config.SuddenDeathHoldSeconds` (3.0s) to win (`AdvanceTreasureHeist`). `TreasureSnapshot` already carries a `SuddenDeathProgress` field (from the original plan), but `SnapshotFactory` hardcodes it to `0f`, so the HUD/compass can never show the overtime countdown. The heist HUD lives in `game/Main.cs` in the `else if (… == GameMode.TreasureHeist)` branch (~line 403): the buzzer sub-branch (`!nm3.MatchTreasureWinByCumulative`) builds a string `objective` from `_client.Treasure`.

- [ ] **Step 1: Write the failing test**

Add to `src/Miner49er.Core.Tests/TreasureHeistTests.cs`:

```csharp
    [Fact]
    public void Sudden_death_progress_accrues_for_a_lone_holder_after_expiry()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5));
        sim.Tick(0.1);                       // miner 1 grabs it
        Assert.Equal(0.0, sim.SuddenDeathProgress);
        sim.SetTimeExpiredForTest();
        for (int i = 0; i < 10; i++) sim.Tick(0.1); // ~1s of uncontested overtime holding
        Assert.True(sim.SuddenDeathProgress > 0.0);
        Assert.True(sim.SuddenDeathProgress <= 1.0);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests --filter Sudden_death_progress`
Expected: FAIL — `Simulation.SuddenDeathProgress` does not exist (compile error).

- [ ] **Step 3: Add the accessor**

In `src/Miner49er.Core/Sim/Simulation.cs`, next to the other treasure accessors (near `SuddenDeathWinner`), add:

```csharp
    public double SuddenDeathProgress =>
        Config.SuddenDeathHoldSeconds > 0
            ? System.Math.Clamp(_suddenDeathHold / Config.SuddenDeathHoldSeconds, 0.0, 1.0)
            : 0.0;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests --filter Sudden_death_progress` → PASS.

- [ ] **Step 5: Populate the snapshot field**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, in the heist `TreasureSnapshot` construction, replace the hardcoded `0f` for `SuddenDeathProgress` with `(float)sim.SuddenDeathProgress`. (Find the `new TreasureSnapshot(…, 0f)` in the `if (sim.Config.TreasureHeistMode)` block and pass `(float)sim.SuddenDeathProgress` as the `SuddenDeathProgress` argument.)

- [ ] **Step 6: Show it in the HUD**

In `game/Main.cs`, in the heist HUD branch's buzzer sub-branch (`if (!nm3.MatchTreasureWinByCumulative)`), after `objective` is assigned from the treasure state, prepend a sudden-death indicator when a lone holder is accruing:

```csharp
                            if (_client.Treasure is { SuddenDeathProgress: > 0f } sd)
                                objective = $"⏱ SUDDEN DEATH — holding {sd.SuddenDeathProgress * 100f:0}%";
```

(Place it right before the `_hud.SetHud(0, objective, …)` call for the heist branch so it overrides the normal objective during overtime. Keep the existing `SetHud` call unchanged.)

- [ ] **Step 7: Build + import + commit**

Run: `dotnet test src/Miner49er.Core.Tests` → all green.
Run: `dotnet build Miner49er.sln -c Debug` → 0 warnings / 0 errors.
Run (PowerShell): `godot --headless --import --path "D:\Projects\Miner49er" --quit` → clean.

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Net/SnapshotFactory.cs game/Main.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): surface the sudden-death countdown in the HUD

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## Final verification (before finishing the branch)

- [ ] `dotnet test src/Miner49er.Core.Tests` — all green (795 = 792 + 3 new).
- [ ] `dotnet build Miner49er.sln -c Debug` — 0 warnings, 0 errors.
- [ ] `godot --headless --import --path "D:\Projects\Miner49er" --quit` (PowerShell) — import clean.
- [ ] Then use **superpowers:finishing-a-development-branch**.
