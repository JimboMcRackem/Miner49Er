# Temple Floors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add randomly occurring Expedition floors where digging uncovers hidden temple sections, and the party must carry an artifact out through the exit.

**Architecture:** Generation stamps sealed marble sections into solid rock from the shared seed, so clients derive them with no new map data. Columns, altars and jars are tile types, which gives them the existing tile-change path and sight rules. The objective extends the existing Expedition treasure floor. Presentation stays in `game/`, logic in `src/Miner49er.Core`.

**Tech Stack:** Godot 4 C#, xUnit, PixelLab for art.

**Spec:** `docs/superpowers/specs/2026-11-01-temple-floors-design.md`

Deviation from the spec: columns, altars and jars are tiles rather than a `GeneratedMap` prop list. Behaviour is unchanged. Only sconces are a list.

## Global Constraints

- Expedition only. All map decisions go through `FloorHash` with a new salt, so host and clients agree.
- Roll: about 1 floor in 6, from floor 5, never on `floor % 4 == 0`, never on two floors in a row, never on floor 51.
- A floor without a temple must generate byte-identically to today. New RNG draws happen only on temple floors, from a separate `Random`.
- Temple floors are never flooded and have no portals.
- Keep `project.godot` and stray `.uid` files out of commits. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test: `dotnet test src/Miner49er.Core.Tests --nologo -v q`. Build: `dotnet build Miner49er.csproj --nologo -v q`. Run the harness from PowerShell only.

## Review Focus

1. The carrier is stunned on the exit, which drops the artifact. It must land on a walkable tile and the party can pick it back up. Test in Task 4.
2. The artifact is dropped beside a pit, lava or deep water. It must never be destroyed or land on a lethal tile. Test in Task 4.
3. A co-op carrier disconnects holding the artifact. It must drop where they stood. Test in Task 4.
4. A rockslide or tremor covers a doorway. The section must still be reachable by digging. Test in Task 3.
5. Dynamite beside a jar or panel holding the artifact. It must flip to Loose, not vanish. Test in Task 4.

---

### Task 1: Temple tiles

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Test: `src/Miner49er.Core.Tests/TempleTileTests.cs`

**Interfaces:**
- Produces: `TileType.MarbleWall, MarblePanel, TempleFloor, Column, FallenColumn, Altar, Jar`, appended to the end of the enum, and `bool IsTemple(this TileType)`.

- [ ] **Step 1: Write the failing tests**

`TempleFloor`: `IsWalkable`, `IsEnterable`, not `IsTraversable`, not `IsMinable`. `MarbleWall`: not `IsMinable`, not `IsBlastable`, `BlocksSight`. `MarblePanel`: `IsMinable`, `IsBlastable`, `BlocksSight`. `Column`: blocks sight, not enterable, not minable. `FallenColumn` and `Altar`: not enterable, sight clear, not minable. `Jar`: `IsMinable`, `IsBlastable`, not enterable, sight clear.

- [ ] **Step 2: Run them, expect a compile failure**
- [ ] **Step 3: Add the enum values and extension cases.** `TempleFloor` stays out of `IsTraversable` so sections never join the cave region.
- [ ] **Step 4: Run the full suite, expect PASS**
- [ ] **Step 5: Commit** `feat(map): add temple tiles`

### Task 2: Floor roll

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Test: `src/Miner49er.Core.Tests/TempleRollTests.cs`

**Interfaces:**
- Produces: `MapConfig.TempleSectionCount` (int, 0 = none), `MapConfig.ExpeditionTreasureHidden` (`TempleHideSpot { Buried, Chest, Pot }`), and `public static bool MapConfig.IsTempleFloor(int floor, int seed)`.

- [ ] **Step 1: Write the failing tests**

Over seeds 0..200, floors 1..50: never true below 5, never on `floor % 4 == 0`, never on consecutive floors. Frequency over all eligible floors is between 1/8 and 1/5. Repeat calls agree. On a temple floor: `FloodedCave` false, `PortalPairCount` 0, `ExpeditionTreasureKind` set, `TempleSectionCount` in 2..4. A non-temple floor has `TempleSectionCount` 0.

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement.** `RawRoll` is `(uint)FloorHash(seed, floor, 4) % 6 == 0` plus the floor limits. `IsTempleFloor` is `RawRoll(floor) && !IsTempleFloor(floor - 1)`, recursing until a floor fails `RawRoll`. Apply it at the end of `FloorConfig`, after portals and flooding.
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(map): roll temple floors in expedition`

### Task 3: Stamp temple sections

**Files:**
- Create: `src/Miner49er.Core/Map/TempleLayout.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`, `src/Miner49er.Core/Map/GeneratedMap.cs`
- Test: `src/Miner49er.Core.Tests/TempleLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1 tiles, `MapConfig.TempleSectionCount`.
- Produces: `record TempleSection(int Id, IReadOnlyList<GridPos> Floor, IReadOnlyList<GridPos> Panels, IReadOnlyList<GridPos> Jars, GridPos Doorway)`, and `GeneratedMap.TempleSections` and `GeneratedMap.Sconces` (`IReadOnlyList<GridPos>`), both empty by default. `TempleLayout.Stamp(TileGrid grid, Random rng, int count, HashSet<GridPos> caves, GridPos spawn) -> (sections, sconces)`.

- [ ] **Step 1: Write the failing tests**

Across 50 seeds on temple floors: no section tile is in the cave region; each section's floor is ringed by marble except its doorway, which is `TileType.Rock`; from the spawn, a walk that treats minable tiles as passable reaches every doorway; each section has an `Altar`, at least one `Column`, at least one `FallenColumn`, and jars in groups of 3 to 6; a non-temple floor produces the same grid as `git stash`-era output (compare against a hash recorded before the change). Pin Review Focus 4: set a doorway's neighbours to Rock and the digging walk still reaches the section.

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement `TempleLayout.Stamp`,** using `new Random(config.Seed ^ 0x54454D50)`. Call it after `KeepLargestRegion` and before `PlaceWater`, and only for a non-zero count. Rooms are 7x7 to 13x11. Try up to 200 positions in solid rock, clear of the map border and 4 tiles from caves, and accept fewer sections rather than overlap, but at least 1: if none fit, widen by stamping over rock nearest the map centre.
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Keep temple tiles out of rockfalls.** Add a test that tremor marking in `Simulation.MineEvents.cs` and scree collapse never change a temple tile, then filter them in `WouldBuryLandmark` and the collapse fill.
- [ ] **Step 6: Commit** `feat(map): stamp hidden temple sections`

### Task 4: Artifact and exit rule

**Files:**
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`, `src/Miner49er.Core/Sim/SimConfig.cs`, `src/Miner49er.Core/Sim/Simulation.cs`, `game/Main.cs`, `game/net/MatchHost.cs`
- Test: `src/Miner49er.Core.Tests/TempleArtifactTests.cs`

**Interfaces:**
- Consumes: `GeneratedMap.TempleSections`, `TempleHideSpot`.
- Produces: `SimConfig.RequireArtifactAtExit` (bool), `bool Simulation.ArtifactAtExit` (true when a living miner on `EscapeTile` holds `ExpeditionTreasureKind`), `SimConfig.ArtifactGoldBonus` (int, default 200).

- [ ] **Step 1: Write the failing tests**

Placement across 50 seeds: Buried sits in a `MarblePanel`, Pot in a `Jar`, Chest as `Toolbox` on `TempleFloor`, always in a section. Rules: with `RequireArtifactAtExit`, `ArtifactAtExit` is false until the holder stands on the exit; dropping it makes it false again. Review Focus: stun on the exit drops the artifact to a walkable tile and it can be picked up (1); carrier killed beside lava or deep water leaves it on a non-lethal tile (2); `RemoveMiner` on a disconnect leaves it where they stood (3); a blast next to the panel or jar holding it flips it to Loose (5).

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement placement** by branching at the top of `PlaceExpeditionTreasure` when sections exist.
- [ ] **Step 4: Implement the rule.** Harden `DropHeldItem` to never choose a lethal tile. In `Main.cs`, the `[Space] ESCAPE!` hint shows "Bring the artifact to the exit" until `ArtifactAtExit`, and the host's escape handler refuses until then. `MatchHost` and `Main` set `RequireArtifactAtExit` from `map.TempleSections.Count > 0`. The snapshot gains a trailing `ArtifactAtExit`.
- [ ] **Step 5: Run, expect PASS**
- [ ] **Step 6: Commit** `feat(sim): make temple floors end with the artifact at the exit`

### Task 5: Priest spirit and temple roster

**Files:**
- Modify: `src/Miner49er.Core/Sim/Monster.cs`, `src/Miner49er.Core/Map/MonsterSpawner.cs`, the monster step in `src/Miner49er.Core/Sim/Simulation.cs`, `game/net/MatchHost.cs`, `game/Main.cs`
- Test: `src/Miner49er.Core.Tests/PriestSpiritTests.cs`

**Interfaces:**
- Produces: `MonsterKind.PriestSpirit` (appended), `MonsterSpawner.PlaceTemple(IReadOnlyList<TempleSection> sections, int perSection, int seed) -> List<(GridPos, MonsterKind)>`, `SimConfig.PriestWakeRadius = 6`, `SimConfig.PriestChantSeconds = 1.0`.

- [ ] **Step 1: Write the failing tests**

Spirit never leaves its section's floor over 2000 ticks. Seeing a miner, it telegraphs for 1s, then wakes dormant skeletons within 6 tiles and none at 7. Touch damage equals a ghost's. Over a large sample, `PlaceTemple` gives roughly 40% `SkeletonHuman`, 30% `Ghost`, 20% `PriestSpirit`, 10% other, with skeletons dormant. Spawned monsters stay in their section until its doorway is opened.

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement,** reusing ghost movement with a section-floor boundary, the existing noise-wake for skeletons, and the `MonsterTelegraph` phase. Halve the normal cave roster on temple floors.
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(sim): add priest spirits and the temple roster`

### Task 6: Jars and the artifact reward

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`, `game/net/MatchHost.cs`, the results screen in `game/Main.cs`
- Test: `src/Miner49er.Core.Tests/TempleJarTests.cs`

**Interfaces:**
- Consumes: `SimConfig.ArtifactGoldBonus`, `ChestLootTable.Roll`.
- Produces: `IReadOnlyList<ItemKind> MatchHost.ArtifactsCarriedOut`.

- [ ] **Step 1: Write the failing tests**

Mining a `Jar` turns it into `TempleFloor`. Over 1000 jars, 40% give gold, 10% an item, 50% nothing, within 5%. A jar holding the artifact always gives the artifact. Completing the floor with the artifact adds the bonus once.

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement,** with jar rolls from a seeded RNG on the host. Mining a jar or panel must leave `TempleFloor`, not `Floor`.
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(sim): add offering jars and the artifact bonus`

### Task 7: Entering the temple

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`, `src/Miner49er.Core/Net/Snapshots.cs`, `SnapshotFactory.cs`, `SnapshotCodec.cs`, `game/net/MatchClient.cs`, `game/Main.cs`, `game/audio/AudioManager.cs`
- Test: `src/Miner49er.Core.Tests/TempleEntryTests.cs`

**Interfaces:**
- Produces: `bool Simulation.TempleEntered`, snapshot trailing `TempleEntered`, `MatchClient.TempleEntered`, `AudioManager.SetTempleMood(bool on)`.

- [ ] **Step 1: Write the failing tests**

False until a miner stands on `TempleFloor`, then stays true. Round-trips through the codec. Old snapshots without it decode as false.

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement.** `SetTempleMood` sets the music `PitchScale` to 0.75 and adds a low-pass effect on the music bus, and clears on `OnNewFloor`. On the transition, `Main` shows "You have uncovered an ancient temple!".
- [ ] **Step 4: Run, expect PASS, and build**
- [ ] **Step 5: Commit** `feat(fx): turn the music ominous on entering a temple`

### Task 8: Drawing the temple

**Files:**
- Modify: `game/WorldRenderer.cs`, `game/LightRenderer.cs`, `game/net/MatchClient.cs`, `game/dev/Harness.cs`
- Test: `src/Miner49er.Core.Tests/SconceTests.cs`

**Interfaces:**
- Consumes: `GeneratedMap.Sconces`, `Flicker.Multiplier(seed, t, Flicker.Fire)`.
- Produces: `Core.SconceTracker` with `bool Update(GridPos miner)` and `IsLit(GridPos)`, lighting within 5 tiles and never unlighting. Harness option `temple=1`, which forces a temple floor through `NetworkManager`.

- [ ] **Step 1: Write the failing tests** for `SconceTracker`: lit at 5 tiles, unlit at 6, stays lit after walking away.
- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement the tracker, then draw all new tiles and sconces in placeholder colours,** with a purple glow and light for lit sconces. Draw marble through the same pass as `ImpermeableRock`. Also draw `PriestSpirit` as a placeholder.
- [ ] **Step 4: Run tests, build, and take harness screenshots with `temple=1`. Read them and confirm the section is hidden, then visible once entered.**
- [ ] **Step 5: Commit** `feat(fx): draw temple tiles and sconces`

### Task 9: Art

**Files:**
- Create: `assets/temple/*.png`, `assets/monsters/priest_spirit/*`
- Modify: `game/WorldRenderer.cs`

- [ ] **Step 1: Generate with PixelLab** at the existing tile size: marble wall set, panel, floor, column, two fallen columns, altar, jar and smashed jar, sconce unlit and animated lit, priest spirit walk. The pot holding the artifact uses the ordinary jar sprite.
- [ ] **Step 2: Replace the placeholders**
- [ ] **Step 3: Harness screenshots, read them, fix what looks wrong**
- [ ] **Step 4: Commit** `feat(art): add temple sprites`

### Task 10: Bots and the whole floor

**Files:**
- Modify: `src/Miner49er.Core/AI/BotPathfinder.cs`, `src/Miner49er.Core/AI/BotBrain.cs`
- Test: `src/Miner49er.Core.Tests/TempleBotTests.cs`

- [ ] **Step 1: Write the failing tests**

Bots path around columns, altars and unbroken jars. With `RequireArtifactAtExit` and no artifact at the exit, a bot does not idle on the exit forever. A bot holding the artifact walks it to the exit.

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement, then run the full suite, build, and a `temple=1 bots=3` harness run with no exceptions**
- [ ] **Step 4: Commit** `feat(ai): teach bots the temple floor`
