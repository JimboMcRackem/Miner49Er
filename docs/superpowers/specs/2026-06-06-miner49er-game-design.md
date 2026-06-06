# Miner49er — Game Design Document

**Date:** 2026-06-06
**Status:** Design approved; Phase 1 is the immediate build target.

---

## 1. Premise

An overhead, tile-based multiplayer mining game for up to 8 players over the
network. Each player controls a miner in a procedurally generated mine. You see
yourself and a small surrounding area; the rest of the map is obscured (fog of
war). You dig with a pickaxe, blast rock with planted explosives, listen for
rivals, and try to be the last miner standing — sometimes while also chasing a
secondary goal.

The tone is tense and atmospheric: jaunty-but-spooky music, dripping water,
the crack of pickaxes, and sudden explosions in the dark.

---

## 2. Core pillars (decided)

| Decision | Choice |
|---|---|
| Platform | Desktop app (Windows primary; cross-platform where free) |
| Engine | Godot 4, C# (.NET) |
| Players | Up to 8, remote |
| Networking topology | Player-hosted **listen server**; host is authoritative |
| Win condition | **Last miner standing** is universal; some maps add a secondary goal |
| Secondary goals (per mode) | e.g. uncover most gold; first to reach the center |
| Explosives | **Unlimited** to plant, capped at N live charges per player (default 3) |
| Items | Optional power-ups found in the world (not required to play) |
| Lethality | **One hit = dead**; eliminated players spectate until round ends |
| Hazards (full vision) | Explosives, water (drown/block), cave-ins, time pressure |

---

## 3. Core gameplay model

### 3.1 World / tiles

The world is a 2D grid of tiles. Tile types:

- **Rock** — minable (pickaxe, ~6s per tile, tunable) or blastable.
- **Impermeable rock** — never affected by pickaxe or blast; forms the
  unbreakable outer border and some internal structure.
- **Floor** — walkable, the carved-out space.
- **Water** — *channels* (slow or block movement) and *deep pools*
  (lethal/impassable). Layered in during Phase 4.
- **Gold / treasure** — embedded in rock; revealed when the rock is mined or
  blasted. Drives the secondary-goal modes.

### 3.2 Movement

- Grid-based: one tile per step in a cardinal direction (N/E/S/W).
- A short smooth slide (tween) between tiles so motion doesn't feel robotic.
- The miner has a **facing direction**, set by the last move input. All
  directional verbs act on the tile directly ahead.

### 3.3 Verbs

All verbs are rebindable actions (keyboard + gamepad). Directional verbs act on
the faced tile.

- **Move** (4 cardinal) — also sets facing.
- **Pickaxe** — mine the rock tile ahead. ~6s timer (tunable). Interrupted if
  the player moves. Plays pickaxe SFX/animation.
- **Plant explosive** — attach a charge to the rock face ahead. 1s to plant,
  then a 3s timer to detonation. The charge lives *on the wall*; adjacent floor
  squares remain walkable — players may pass through a square next to a planted
  charge. On detonation: destroys nearby blastable rock and **one-shots any
  player in blast range**. Capped at N live charges per player.
- **Listen** — stand still; play the listening animation; briefly duck music and
  boost ambient/positional SFX; show an **8-point compass indicator** pointing
  toward the **nearest living player**. The player is stationary and exposed for
  the duration.
- **Pick up / use item** — grab a power-up on/near the current tile, or use a
  carried item.

### 3.4 Combat & death

- **One hit = dead.** Being caught in a blast (later: cave-in or deep water)
  eliminates the player instantly.
- Eliminated players become **spectators** until the round ends.
- **Last miner alive wins.** In secondary-goal modes, the secondary goal
  (most gold, reach center first) is layered on top of the same elimination
  baseline.

---

## 4. Procedural map generation

Built as an isolated `MapGenerator` that takes `(seed, playerCount, modeConfig)`
and returns a **pure tile-grid data structure** — no rendering, no networking.
This keeps it independently unit-testable and tunable.

- **Seeded RNG** — host can reproduce/share a map; tests are deterministic.
- **Cellular-automata cave generation** for organic mine shapes.
- **Impermeable border** around the entire map.
- **Connectivity guarantee** — flood-fill after generation; carve or discard so
  every spawn can reach every objective. A player must never be sealed in.
- **Spawn placement** — up to 8 spawns spread far apart with breathing room, so
  nobody starts adjacent to an enemy or hazard.
- **Feature passes** — place gold veins, water features (Phase 4), and a center
  objective tile (reach-center mode), respecting minimum spacing.
- **Scaling by player count** — map dimensions scale with player count (base
  size per player): tight maps for 2 players, sprawling for 8.

**Testable invariants:** every spawn reaches every objective; spawns respect a
minimum pairwise distance; border is fully impermeable; gold/feature counts
match mode config.

---

## 5. Fog of war & networking

Hidden information is the central mechanic, so this is the architecturally
trickiest area.

### 5.1 Authority

- **Host-authoritative**, fixed-tick simulation.
- Clients send **inputs** (move, plant, mine, listen, use); the host simulates
  and is the single source of truth.

### 5.2 Visibility / interest management

- **Target design:** the host transmits only entities a client can actually see
  (within vision radius / not behind rock), using Godot 4's
  `MultiplayerSynchronizer` per-peer visibility. A modified client still cannot
  see hidden players, so **listen stays meaningful** and the game is
  cheat-resistant.
- **Phase 2 simplification:** ship **naive full-state sync + client-side fog**
  first (much faster to get running). Among friends it plays identically.
  Switch on visibility culling as a hardening step (Phase 5) once the loop is
  fun.

### 5.3 Client fog rendering

- Currently visible tiles render bright.
- Previously explored tiles render dimmed (remembered tunnels).
- Unexplored tiles render black.
- Vision radius ~5 tiles, tunable.

---

## 6. Audio

- **Music** — jaunty-but-spooky looping track. Listen action briefly ducks music
  and boosts ambient/positional SFX.
- **Positional SFX** — dripping water, pickaxe strikes, explosions, planting,
  footsteps; 2D-positioned (volume/pan by distance). This makes "listen"
  readable by ear, not just via the compass.
- **Assets** — start with placeholder royalty-free / CC0 audio (sources noted in
  the asset manifest); final audio swapped in later.
- **`AudioManager`** with separate buses (Music / SFX / UI) reading volume from
  the Settings service.

---

## 7. Input, options & customization

### 7.1 Input & keybindings

- Built on Godot's **Input Map** with named actions (`move_up`, `move_down`,
  `move_left`, `move_right`, `plant`, `listen`, `pickaxe`, `use_item`, …).
- A **rebinding UI** captures keyboard *and* joystick/gamepad inputs per action,
  with conflict detection and reset-to-defaults. Bindings persist.

### 7.2 Options / settings (persisted)

- Music volume, SFX volume, master mute.
- Player color.
- Player graphic:
  - **MVP:** choose from preset sprites + chosen color.
  - **Later:** a self-contained **custom-sprite editor** (paint a miner on a
    small grid, save/load).
- Host-side tunables: pickaxe time, blast timer/radius, charge cap, vision
  radius, map size scaling, round timer.

### 7.3 Settings architecture

A single `Settings` service (load/save JSON in the user data dir) owns all
persisted state; input, audio, and customization read from it.

---

## 8. Component boundaries

Each unit has one clear purpose and a well-defined interface:

- **`MapGenerator`** — `(seed, playerCount, modeConfig) → TileGrid`. Pure data;
  no engine deps beyond RNG. Independently testable.
- **`TileGrid` / world model** — authoritative tile state; query/mutate tiles;
  no rendering.
- **`Simulation`** — fixed-tick game logic: movement, mining, charges,
  detonation, death, win checks. Consumes inputs, mutates world model. Host-side.
- **`NetworkLayer`** — host/join, input transport, state sync, visibility.
- **`FogOfWar`** — per-player visibility computation + client render state.
- **`AudioManager`** — buses, music, positional SFX.
- **`InputService`** — action mapping + rebinding, keyboard/gamepad.
- **`Settings`** — persisted config; single owner.
- **`Customization`** — color/sprite selection; (later) sprite editor.
- **`MatchFlow`** — lobby, round lifecycle, spectate, win/secondary-goal
  resolution.
- **`ScreenFlow`** — application screen states and transitions
  (Splash → [Menu] → Loading → Game), including the loading screen shown while
  the map is generated and the match is set up.

### 8.1 Application screens (splash + loading)

The app boots through a small screen-state flow rather than dropping straight
into gameplay:

- **Splash screen** — shown at launch: game title/logo, a brief dwell (or
  "press any key"), then transitions onward. It is the engine's `main_scene`.
- **Loading screen** — shown during potentially slow setup: procedural map
  generation and match initialization (and, from Phase 2, connecting/syncing
  players). In Phase 1 generation is near-instant, but the loading state is
  built now so the hook exists where it's genuinely needed later (large 8-player
  maps, network join). Displays a status message (e.g. "Generating mine…").

A minimal version (Splash → Loading → Game) is included in Phase 1 so the shell
exists from the start; a full main menu and settings screens come in Phase 5.

---

## 9. Phased delivery plan

The spec covers the whole vision; each phase is its own spec → plan →
implementation cycle. **Phase 1 is the immediate build target.**

1. **Phase 1 — Core single-machine loop (no net):** tile grid, grid movement +
   facing, pickaxe, plant/blast with proximity death, procedural map gen,
   placeholder art, fog-of-war rendering, and a minimal screen shell
   (splash → loading → game). *Goal: mining and blasting is fun.*
2. **Phase 2 — Multiplayer:** host/join (listen server, LAN/direct-IP),
   input→host→sync, spectate-on-death, last-man-standing round flow.
3. **Phase 3 — Listen mechanic + audio:** positional SFX, music, listen action +
   compass indicator.
4. **Phase 4 — Hazards & modes:** water (drown/block), cave-ins, time pressure,
   secondary-goal modes (gold, reach-center), items/power-ups.
5. **Phase 5 — Polish & hardening:** rebinding UI, full settings, custom-sprite
   editor, visibility culling (cheat-resistant fog), NAT/relay fallback for
   internet play.

---

## 10. Open items / future decisions

- Exact tile pixel size and base map dimensions per player (tune in Phase 1).
- Blast radius shape (cross vs. square) and exact charge cap (default 3).
- Listen indicator duration and whether it has a max range (default: always
  points to nearest living player, no range cap).
- Item set specifics (candidates: speed boost, bigger blast, longer vision,
  water plank, decoy noise-maker) — finalized in Phase 4.
- Round/lobby UX details (Phase 2).
- Final audio and art direction (placeholders until then).
