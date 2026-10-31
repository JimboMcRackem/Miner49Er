# Temporary (Decaying) Buff Pickups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the three floor buff pickups (`SpeedPotion`, `LongerVision`, `BiggerBlast`) grant a temporary, time-decaying boost instead of a permanent level-up, killing the permanent snowball; paid shop upgrades stay permanent.

**Architecture:** Reuse the existing `StatusEffect` timed-effect system (channels `MoveSpeed`/`VisionRadius`/`BlastRadius` already exist; `ApplyEffect` refreshes without compounding; `AdvanceEffects` decays and removes; the `Effective*` methods already sum timed effects on top of permanent levels). Rewire `ApplyBuff` for the three kinds from "increment `PermXLevel`" to "apply a timed effect," and remove the now-obsolete permanent-max anti-waste machinery. The client already toasts on pickup — only the message copy changes.

**Tech Stack:** C# / .NET 10; engine-free `Miner49er.Core` (xUnit, TDD) for the sim; Godot 4.6 game assembly (`Miner49er`) for the toast copy.

## Global Constraints

- **Host-authoritative, deterministic Core.** All rules live in `Miner49er.Core`; the game layer only reads results. Vision already flows to clients via `MinerSnapshot.VisionRadius` (host computes `EffectiveVisionRadius`, which includes the timed bonus). No networking changes.
- **Shop upgrades stay permanent.** `MatchHost.ReceiveBuy` → `SetPermLevels` is untouched; permanent progression persists across Expedition floors. Temp buffs stack on top (additively for vision/blast, multiplicatively for speed) via the existing `Effective*` methods.
- **Stacking = refresh-only.** A second pickup of the same kind resets the timer at the same magnitude; it never compounds. This is guaranteed by the existing `ApplyEffect`.
- **Durations authored in minutes**, default **2 minutes** each; magnitudes: vision **+3** tiles, blast **+1** radius, speed **×0.80** cadence (<1 = faster).
- **Run `godot` via PowerShell only** (Bash shim breaks headless import). Not needed for this plan (no art), but any manual run uses PowerShell.
- **Never `git add -A`.** Stage feature files by explicit path. Do not stage `*.uid`, `.superpowers/`, `Temp/`, `_preview_*`, or unrelated pre-existing edits.
- End every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Verification: Core tasks use `dotnet test` (TDD); the game task uses `dotnet build` (no xUnit seam for `Main.cs`, exactly like the rest of the presentation layer).

## Setup (before Task 1)

Create the feature branch (do not commit to `main` directly):

```bash
git checkout -b feat/temporary-buffs
```

## File Structure

- **Create `src/Miner49er.Core/Sim/BuffTuning.cs`** — single responsibility: tuning constants (magnitudes + durations) for the three temporary pickups, in one place both Core and the game reference.
- **Modify `src/Miner49er.Core/Sim/Simulation.cs`** — `ApplyBuff` applies timed effects for the three kinds; remove `IsPermBuffMaxed` + its `PickUpItems` guard; simplify `RollUsefulChestLoot`.
- **Modify `src/Miner49er.Core/Sim/SimEvent.cs`** — remove the now-unused `PickupBlocked` event.
- **Create `src/Miner49er.Core.Tests/TempBuffTests.cs`** — the temporary-buff behavior (expiry, refresh, stacking, never-blocked).
- **Modify `src/Miner49er.Core.Tests/PermBuffTests.cs`** — drop the tests that asserted permanent-increment / blocked-when-maxed for pickups; keep the `SetPermLevels` (shop-path) tests.
- **Modify `src/Miner49er.Core.Tests/SimulationChestTests.cs`** — remove the obsolete "maxed → life instead of wasting" test and its `MaxOut` helper.
- **Modify `game/Main.cs`** — reword the three buff pickup toasts to signal the temporary duration.

---

### Task 1: Temporary buff pickups + remove perm-max anti-waste (Core)

**Files:**
- Create: `src/Miner49er.Core/Sim/BuffTuning.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`ApplyBuff` ~1143-1157; `IsPermBuffMaxed` ~995-1001; `PickUpItems` guard ~1013-1017; `RollUsefulChestLoot` ~985-993)
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (`PickupBlocked` ~21)
- Create: `src/Miner49er.Core.Tests/TempBuffTests.cs`
- Modify: `src/Miner49er.Core.Tests/PermBuffTests.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationChestTests.cs`

**Interfaces:**
- Consumes: existing `Simulation.ApplyEffect(int minerId, EffectKind kind, EffectChannel channel, double magnitude, double durationSeconds)`; `EffectiveVisionRadius(int)`, `EffectiveBlastBonus(int)`, `EffectiveMoveSeconds(int)`; `SetPermLevels(int,int,int,int)`; `ChestLootTable.Roll(Random)`.
- Produces: `BuffTuning` static class with `VisionMagnitude:int`, `BlastMagnitude:int`, `SpeedMagnitude:double`, `VisionMinutes/BlastMinutes/SpeedMinutes:double`, and `VisionSeconds/BlastSeconds/SpeedSeconds:double` accessors (used by Task 2).

- [ ] **Step 1: Write the failing temporary-buff tests**

Create `src/Miner49er.Core.Tests/TempBuffTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TempBuffTests
{
    // Miner at (1,2); a buff item at (2,2). Moving east onto it collects it on the next tick.
    private static Simulation Sim(SimConfig? cfg = null)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg ?? new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    private static void CollectAt(Simulation sim, ItemKind kind)
    {
        sim.AddItem(new Item(new GridPos(2, 2), kind));
        sim.TryMove(1, Direction.East); // step onto the item
        sim.Tick(0.0);                  // PickUpItems runs during Tick
        sim.TryMove(1, Direction.West); // step back so a second pickup can re-enter
        sim.Tick(0.0);
    }

    [Fact]
    public void LongerVision_pickup_is_temporary()
    {
        var sim = Sim();
        int baseR = sim.EffectiveVisionRadius(1);          // 5
        CollectAt(sim, ItemKind.LongerVision);
        Assert.Equal(baseR + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1)); // 8
        sim.Tick(BuffTuning.VisionSeconds + 1.0);          // effect expires
        Assert.Equal(baseR, sim.EffectiveVisionRadius(1)); // back to 5
    }

    [Fact]
    public void Second_vision_pickup_refreshes_not_compounds()
    {
        var sim = Sim();
        int baseR = sim.EffectiveVisionRadius(1);
        CollectAt(sim, ItemKind.LongerVision);
        CollectAt(sim, ItemKind.LongerVision);
        // Still a single magnitude, not doubled.
        Assert.Equal(baseR + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1));
    }

    [Fact]
    public void BiggerBlast_pickup_is_temporary()
    {
        var sim = Sim();
        CollectAt(sim, ItemKind.BiggerBlast);
        Assert.Equal(BuffTuning.BlastMagnitude, sim.EffectiveBlastBonus(1)); // 1
        sim.Tick(BuffTuning.BlastSeconds + 1.0);
        Assert.Equal(0, sim.EffectiveBlastBonus(1));
    }

    [Fact]
    public void SpeedPotion_pickup_is_temporary()
    {
        var sim = Sim();
        double baseS = sim.EffectiveMoveSeconds(1);
        CollectAt(sim, ItemKind.SpeedPotion);
        Assert.True(sim.EffectiveMoveSeconds(1) < baseS); // faster while active
        sim.Tick(BuffTuning.SpeedSeconds + 1.0);
        Assert.True(System.Math.Abs(sim.EffectiveMoveSeconds(1) - baseS) < 1e-9);
    }

    [Fact]
    public void Temp_vision_stacks_on_permanent_shop_level()
    {
        var sim = Sim();
        sim.SetPermLevels(1, 0, 1, 0);                     // shop vision +1 -> radius 6
        CollectAt(sim, ItemKind.LongerVision);             // temp +3
        Assert.Equal(5 + 1 + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1)); // 9
    }

    [Fact]
    public void Buff_is_collected_even_when_permanent_level_maxed()
    {
        var cfg = new SimConfig { MaxPermVisionLevel = 3 };
        var sim = Sim(cfg);
        sim.SetPermLevels(1, 0, 3, 0);                     // perm vision maxed -> radius 8
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Empty(sim.Items);                           // collected, not left on the floor
        Assert.Equal(5 + 3 + BuffTuning.VisionMagnitude, sim.EffectiveVisionRadius(1)); // 11
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~TempBuff"`
Expected: FAIL — `BuffTuning` does not exist (compile error), and pickups still grant permanent levels.

- [ ] **Step 3: Create `BuffTuning`**

Create `src/Miner49er.Core/Sim/BuffTuning.cs`:

```csharp
namespace Miner49er.Core;

/// <summary>Tuning for the temporary floor-pickup buffs. Durations are authored in
/// minutes; the *Seconds accessors feed Simulation.ApplyEffect (which takes seconds).
/// Magnitudes match the EffectChannel each buff drives: vision/blast add tiles/radius,
/// speed is a move-cadence multiplier where &lt;1 is faster.</summary>
public static class BuffTuning
{
    public const int    VisionMagnitude = 3;      // +tiles of fog radius
    public const int    BlastMagnitude  = 1;      // +explosion radius
    public const double SpeedMagnitude  = 0.80;   // move-cadence multiplier (<1 = faster)

    public const double VisionMinutes = 2.0;
    public const double BlastMinutes  = 2.0;
    public const double SpeedMinutes  = 2.0;

    public static double VisionSeconds => VisionMinutes * 60.0;
    public static double BlastSeconds  => BlastMinutes  * 60.0;
    public static double SpeedSeconds  => SpeedMinutes  * 60.0;
}
```

- [ ] **Step 4: Rewire `ApplyBuff` to apply timed effects**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the three permanent-increment cases in `ApplyBuff`:

```csharp
            case ItemKind.SpeedPotion:
                m.PermSpeedLevel = Math.Min(m.PermSpeedLevel + 1, Config.MaxPermSpeedLevel);
                break;
            case ItemKind.LongerVision:
                m.PermVisionLevel = Math.Min(m.PermVisionLevel + 1, Config.MaxPermVisionLevel);
                break;
            case ItemKind.BiggerBlast:
                m.PermBlastLevel = Math.Min(m.PermBlastLevel + 1, Config.MaxPermBlastLevel);
                break;
```

with:

```csharp
            case ItemKind.SpeedPotion:
                ApplyEffect(minerId, EffectKind.SpeedPotion, EffectChannel.MoveSpeed,
                    BuffTuning.SpeedMagnitude, BuffTuning.SpeedSeconds);
                break;
            case ItemKind.LongerVision:
                ApplyEffect(minerId, EffectKind.LongerVision, EffectChannel.VisionRadius,
                    BuffTuning.VisionMagnitude, BuffTuning.VisionSeconds);
                break;
            case ItemKind.BiggerBlast:
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                    BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds);
                break;
```

(The `Miner49er.Core` namespace is shared, so `BuffTuning` needs no `using`. `m` is still referenced by other cases in the switch — leave the local `var m = _miners[minerId];` at the top of `ApplyBuff` unchanged.)

- [ ] **Step 5: Remove the permanent-max anti-waste**

In `src/Miner49er.Core/Sim/Simulation.cs`:

Delete the guard block in `PickUpItems` so every non-buried, non-carried item at a miner's tile is collected:

```csharp
                if (!m.Alive || m.Pos != item.Pos) continue;
                if (IsPermBuffMaxed(m, item.Kind))
                {
                    _events.Add(new PickupBlocked(m.Id, item.Pos, item.Kind));
                    break;
                }
                _items.RemoveAt(i);
```

becomes:

```csharp
                if (!m.Alive || m.Pos != item.Pos) continue;
                _items.RemoveAt(i);
```

Simplify `RollUsefulChestLoot` (temp buffs are never wasted, so no max-avoidance is needed):

```csharp
    private ItemKind RollUsefulChestLoot(Miner m)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var kind = ChestLootTable.Roll(_rng);
            if (!IsPermBuffMaxed(m, kind)) return kind;
        }
        return ItemKind.LifePotion;
    }
```

becomes:

```csharp
    private ItemKind RollUsefulChestLoot(Miner m) => ChestLootTable.Roll(_rng);
```

Delete the now-unused `IsPermBuffMaxed` method:

```csharp
    private bool IsPermBuffMaxed(Miner m, ItemKind kind) => kind switch
    {
        ItemKind.SpeedPotion  => m.PermSpeedLevel  >= Config.MaxPermSpeedLevel,
        ItemKind.BiggerBlast  => m.PermBlastLevel  >= Config.MaxPermBlastLevel,
        ItemKind.LongerVision => m.PermVisionLevel >= Config.MaxPermVisionLevel,
        _ => false,
    };
```

(`m` is now unused by `RollUsefulChestLoot` — the parameter stays to keep the call site `RollUsefulChestLoot(m)` unchanged; C# does not warn on unused parameters.)

- [ ] **Step 6: Remove the unused `PickupBlocked` event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, delete the line:

```csharp
public sealed record PickupBlocked(int MinerId, GridPos Pos, ItemKind Kind) : SimEvent;
```

- [ ] **Step 7: Update `PermBuffTests.cs` — drop the pickup-permanent + blocked tests**

In `src/Miner49er.Core.Tests/PermBuffTests.cs`, DELETE these six test methods (their behavior is now covered by `TempBuffTests`, or no longer exists):
`Picking_up_SpeedPotion_increments_PermSpeedLevel`, `LongerVision_increments_PermVisionLevel`, `BiggerBlast_increments_PermBlastLevel`, `SpeedPotion_blocked_when_at_max_speed`, `BiggerBlast_blocked_when_at_max_blast`, `LongerVision_blocked_when_at_max_vision`, and `SpeedPotion_collected_normally_when_not_maxed`.

KEEP these (they exercise the still-permanent shop path via `SetPermLevels`, unaffected):
`Perm_levels_start_at_zero`, `EffectiveMoveSeconds_decreases_each_perm_speed_level`, `SetPermLevels_clamps_to_config_max`, `PermSpeed_and_mold_slow_stack_multiplicatively`.

After editing, the file's `using Miner49er.Core;` and class shell remain; only the listed methods are removed.

- [ ] **Step 8: Update `SimulationChestTests.cs` — remove the obsolete maxed→life test**

In `src/Miner49er.Core.Tests/SimulationChestTests.cs`, DELETE the `MaxOut` helper method (lines defining `private static void MaxOut(...)`) and the test `Chest_pickup_when_all_perm_buffs_maxed_restores_life_instead_of_wasting` — the "restore life instead of wasting" behavior no longer exists (chest-rolled buffs are temporary and never wasted). Leave the file's other test(s) intact.

- [ ] **Step 9: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: `Passed!` with 0 failures. (The 6 new `TempBuffTests` pass; removed tests are gone; no other test references `PickupBlocked`, `IsPermBuffMaxed`, or `MaxOut`.)

- [ ] **Step 10: Commit**

```bash
git add src/Miner49er.Core/Sim/BuffTuning.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core.Tests/TempBuffTests.cs src/Miner49er.Core.Tests/PermBuffTests.cs src/Miner49er.Core.Tests/SimulationChestTests.cs
git commit -m "feat(buffs): floor pickups grant temporary decaying boosts instead of permanent levels

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Pickup toast wording (game)

**Files:**
- Modify: `game/Main.cs` (`PickupMessage` ~477-492)

**Interfaces:**
- Consumes: `BuffTuning.VisionMinutes`, `BuffTuning.BlastMinutes`, `BuffTuning.SpeedMinutes` (Task 1). `Main.cs` already has `using Miner49er.Core;` (it uses `ItemKind` unqualified), so `BuffTuning` resolves without a new import.

- [ ] **Step 1: Reword the three buff pickup messages**

In `game/Main.cs`, in the `PickupMessage` switch, replace the three successful (`false`) buff cases:

```csharp
			(ItemKind.SpeedPotion,  false) => "Speed Tonic! Move faster.",
			(ItemKind.BiggerBlast,  false) => "Bigger Blast! Larger explosion radius.",
			(ItemKind.LongerVision, false) => "Keen Eyes! See further.",
```

with duration-aware copy read from `BuffTuning`:

```csharp
			(ItemKind.SpeedPotion,  false) => $"Speed Tonic! Faster for {(int)BuffTuning.SpeedMinutes} min.",
			(ItemKind.BiggerBlast,  false) => $"Bigger Blast! Larger radius for {(int)BuffTuning.BlastMinutes} min.",
			(ItemKind.LongerVision, false) => $"Keen Eyes! See further for {(int)BuffTuning.VisionMinutes} min.",
```

Leave the three `true` ("Already maxed out …") cases in place — they are now unreachable (buffs are never blocked, so their items always disappear on pickup) but harmless; removing them would cascade into unused client-side detection state and is out of scope.

- [ ] **Step 2: Build the game assembly**

Run: `dotnet build Miner49er.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Manual verify (PowerShell: `godot`)**

Start a solo Expedition, walk over a Sight Opal / Speed Tonic / Bigger Blast pickup, and confirm the toast now reads e.g. "Keen Eyes! See further for 2 min." Confirm the vision boost visibly widens the fog and then shrinks back after ~2 minutes.

- [ ] **Step 4: Commit**

```bash
git add game/Main.cs
git commit -m "feat(buffs): pickup toasts note the temporary duration

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- Floor pickups become temporary timed effects → Task 1 Step 4. ✅
- Reuse `StatusEffect` system / `Effective*` summation → consumed as-is (no math change). ✅
- Shop upgrades stay permanent + carry across floors → `SetPermLevels`/`ReceiveBuy` untouched; kept shop-path tests (Task 1 Step 7). ✅
- Refresh-not-compound stacking → existing `ApplyEffect`; test `Second_vision_pickup_refreshes_not_compounds` (Task 1 Step 1). ✅
- `BuffTuning` constants in minutes with second accessors → Task 1 Step 3. ✅
- `IsPermBuffMaxed` dropped so buffs are never "wasted" → removed in Task 1 Step 5; test `Buff_is_collected_even_when_permanent_level_maxed`. ✅
- Chest loot rolling a buff inherits temporary behavior → `ApplyBuff` is the shared path; `RollUsefulChestLoot` simplified (Task 1 Step 5). ✅
- Toast reworded, no new networking → Task 2. ✅
- Testing scenarios 1–6 from the spec → `TempBuffTests` (Task 1 Step 1). ✅

**Placeholder scan:** No TBD/TODO; every code step shows the exact before/after. Manual verify (Task 2 Step 3) is a stated concrete check, consistent with the presentation-layer's no-xUnit-seam convention.

**Type consistency:** `BuffTuning.{VisionMagnitude:int, BlastMagnitude:int, SpeedMagnitude:double, VisionMinutes/BlastMinutes/SpeedMinutes:double, VisionSeconds/BlastSeconds/SpeedSeconds:double}` are used consistently across `ApplyEffect` calls (Task 1) and the toast copy (Task 2). `ApplyEffect(int, EffectKind, EffectChannel, double, double)` matches the existing signature; `int` magnitudes widen to `double` implicitly. `EffectiveVisionRadius`/`EffectiveBlastBonus` cast magnitude with `(int)`, so integer magnitudes (3, 1) apply exactly.
