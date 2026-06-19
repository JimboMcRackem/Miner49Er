# Floor Modifiers Design

**Goal:** Each Expedition floor rolls one random twist that changes how that floor plays, making every run feel different.

**Architecture:** A `FloorModifier` enum + static helpers in Core derive the modifier deterministically from the match seed and floor number. The host applies it to `MapConfig` (map generation) and `SimConfig` (gameplay rules). The client calls the same `Pick` to display the modifier in the floor banner and HUD — no network changes required.

---

## Modifier Roster

| Modifier | Effect |
|---|---|
| **Dark Mine** | Vision radius halved (`max(2, VisionRadius / 2)`) |
| **Unstable** | Cave-ins enabled; crack site count doubled |
| **Monster Surge** | 50% more monsters spawned |
| **Flooded** | +3 water pools, +2 rivers on the generated map |
| **Haste** | Player and all monster move cadences × 0.7 (faster) |

## Floor Schedule

- Floors where `floor % 4 == 0` (4, 8, 12, 16, 20) → `None` (clean floor, no modifier)
- Floor 21 (boss) → always `None`
- All other floors → `Pick(matchSeed, floor)` seeded RNG from the 5 modifiers

The same `(matchSeed, floor)` pair always produces the same modifier, so a run is reproducible but different runs feel different.

---

## Core: `FloorModifier.cs`

**Location:** `src/Miner49er.Core/Sim/FloorModifier.cs`

```csharp
namespace Miner49er.Core;

public enum FloorModifier { None, DarkMine, Unstable, MonsterSurge, Flooded, Haste }

public static class FloorModifiers
{
    private static readonly FloorModifier[] Pool =
    {
        FloorModifier.DarkMine,
        FloorModifier.Unstable,
        FloorModifier.MonsterSurge,
        FloorModifier.Flooded,
        FloorModifier.Haste,
    };

    public static FloorModifier Pick(int matchSeed, int floor)
    {
        if (floor >= 21 || floor % 4 == 0) return FloorModifier.None;
        var rng = new System.Random(matchSeed ^ (floor * 7919));
        return Pool[rng.Next(Pool.Length)];
    }

    public static void Apply(FloorModifier mod, MapConfig map, SimConfig sim)
    {
        switch (mod)
        {
            case FloorModifier.DarkMine:
                sim.VisionRadius = System.Math.Max(2, sim.VisionRadius / 2);
                break;
            case FloorModifier.Unstable:
                map.CaveIns = true;
                map.CrackSiteCount *= 2;
                break;
            case FloorModifier.MonsterSurge:
                sim.MonsterCountMultiplier = 1.5f;
                break;
            case FloorModifier.Flooded:
                map.PoolCount += 3;
                map.RiverCount += 2;
                break;
            case FloorModifier.Haste:
                sim.BaseMoveSeconds           *= 0.7;
                sim.MonsterSlimeMoveSeconds   *= 0.7;
                sim.MonsterGhostMoveSeconds   *= 0.7;
                sim.MonsterGoatMoveSeconds    *= 0.7;
                break;
        }
    }

    public static string DisplayName(FloorModifier mod) => mod switch
    {
        FloorModifier.DarkMine     => "DARK MINE",
        FloorModifier.Unstable     => "UNSTABLE",
        FloorModifier.MonsterSurge => "MONSTER SURGE",
        FloorModifier.Flooded      => "FLOODED",
        FloorModifier.Haste        => "HASTE",
        _                          => "",
    };
}
```

---

## SimConfig change

Add one field to `src/Miner49er.Core/Sim/SimConfig.cs`:

```csharp
public float MonsterCountMultiplier { get; set; } = 1.0f;
```

---

## MatchHost changes (`game/net/MatchHost.cs`)

In `AdvanceFloor`, after building `mapCfg` and `simCfg` but before generating the map and creating the simulation:

```csharp
var modifier = FloorModifiers.Pick(nm.MatchSeed, newFloor);
FloorModifiers.Apply(modifier, mapCfg, simCfg);
```

When placing monsters, multiply by the multiplier:

```csharp
int monsterCount = (int)(MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor)
                         * simCfg.MonsterCountMultiplier);
```

---

## Main.cs changes (`game/Main.cs`)

### Floor 1 (initial spawn)

Apply modifier to the host-side map config and sim config before generating the map and creating the simulation:

```csharp
var modifier = FloorModifiers.Pick(nm.MatchSeed, 1);
// apply to hostMapCfg and simCfg before Generate() / new Simulation()
FloorModifiers.Apply(modifier, hostMapCfg, simCfg);
```

Also apply to the client-side map config so both generate the same grid when Flooded or Unstable modifiers add tiles.

### Floor banner (`OnNewFloor`)

```csharp
var mod = FloorModifiers.Pick(nm.MatchSeed, floor);
string modSuffix = mod != FloorModifier.None ? $": {FloorModifiers.DisplayName(mod)}" : "";
Text = floor == 21 ? "BOSS FLOOR" : $"FLOOR {floor}{modSuffix}";
```

### HUD objective line

Remove `(need 50%)`. Add modifier tag when active:

```csharp
var mod = FloorModifiers.Pick(nm.MatchSeed, nm2.MatchFloor);
string modTag = mod != FloorModifier.None ? $"  [{FloorModifiers.DisplayName(mod)}]" : "";
objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold: {pct}%{modTag}";
```

---

## Tests

**File:** `src/Miner49er.Core.Tests/FloorModifierTests.cs`

- `Pick` returns `None` for floor 4, 8, 12, 16, 20, 21
- `Pick` returns a non-`None` modifier for floors 1, 2, 3, 5, 7, 19
- `Pick` is deterministic: same inputs always return same result
- `Pick` returns all 5 modifier types across a range of seeds/floors (coverage check)
- `Apply(DarkMine)` halves VisionRadius and floors at 2
- `Apply(Unstable)` sets `CaveIns = true` and doubles `CrackSiteCount`
- `Apply(MonsterSurge)` sets `MonsterCountMultiplier = 1.5f`
- `Apply(Flooded)` increments `PoolCount` by 3 and `RiverCount` by 2
- `Apply(Haste)` reduces all cadence fields by factor 0.7
- `Apply(None)` changes nothing
