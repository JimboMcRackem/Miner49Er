# Miner49er

A top-down multiplayer mining game built with **Godot 4.6** and **C#**. Dig through procedurally generated caves, race or fight rival miners, dodge underground hazards, and haul out gold and treasure — solo, co-op, or head-to-head over LAN or the internet.

> ⚠️ Miner49er is an in-development hobby project. Expect rough edges.

## The core mechanic: Listen

The mine is dark and your vision is a small circle of torchlight. Hold **Listen** to go quiet and *hear* what the rock is hiding — buried items and hazards shimmer into view around you, colour-coded by what they are. It's the difference between digging blind and digging smart:

- **Buried caches** shimmer where loot waits inside the rock (some are empty decoys — only digging tells).
- **Scree / rockfall** glows amber → red the more unstable it is: amber may collapse when you dig it, red always will, and the worst tier buries a wide area and crushes anyone nearby.

## Game modes

| Mode | Goal |
|------|------|
| **Expedition** | Descend a 50-floor dungeon (solo or co-op). Shared lives, a shop every 4th floor, permanent upgrades, buried idols, and a boss at the bottom. |
| **Gold Rush** | Collect the most gold before the timer runs out. |
| **Reach Center** | Race rivals to dig your way to the sealed center of the map. |
| **Treasure Hunt** | Find your assigned buried idols and deposit them at your chest. |
| **Last Man Standing** | Blast and outlast everyone else. |
| **Demolition Derby** | All-out explosive mayhem. |

## Hazards & monsters

The deeper you go, the deadlier the mine:

- **Water & floods** — shallow water slows you, deep water drowns, and some floors slowly flood.
- **Pits & cave-ins** — bottomless pits, and cracked floors that give way when crossed twice or blasted.
- **Lava** — static pools plus rock-buried vents that creep outward; water quenches them.
- **Glowing crystals** — light up the dark; break one for a portable shard-lantern.
- **Scree / rockfall** — unstable rock that triggers rockslides.
- **Monsters** — goats, slimes, ghosts, zombies, dormant skeletons that wake to noise, water snakes, and an animated octopus boss.

## Tools of the trade

Speed potions, longer-vision and bigger-blast buffs, water planks (bridge water/pits), slow-mold traps, lanterns, throwable stones, and dynamite or wired detonators — plus the crystal shards you pry from the walls.

## AI bots

Add computer-controlled miners at four skill levels (Greenhorn up to Foreman and beyond). Bots work in every mode and share the party's life pool.

## Multiplayer

- Host/client model with an **authoritative simulation** running on the host and broadcasting compact per-tick snapshots.
- **LAN** play, plus **internet** play via UPnP port-forwarding and a shareable connect code.
- Deterministic, seed-based map generation so every client builds an identical map.

## Architecture

The codebase is split so the game logic is testable without the engine:

- **`src/Miner49er.Core`** — engine-free game logic: the deterministic simulation, map generation, AI, and the network snapshot codec. No Godot dependency.
- **`src/Miner49er.Core.Tests`** — xUnit test suite (700+ tests) covering the Core library.
- **`game/`** — the Godot presentation layer: rendering, audio, input, UI, and networking transport.
- **`assets/`** — sprites (many generated with [PixelLab](https://pixellab.ai)), audio, and tiles.

The host steps a fixed 30 Hz simulation, applies queued player/bot inputs, and broadcasts the resulting world state; clients render from those snapshots and interpolate.

## Building & running

Requires the **.NET 10 SDK** and **Godot 4.6** (the .NET/Mono build).

```bash
# Build the engine-free core and its tests
dotnet build src/Miner49er.Core/Miner49er.Core.csproj

# Run the test suite
dotnet test src/Miner49er.Core.Tests/
```

To play, open the project root in the Godot 4.6 editor and run it, or build/export from the editor.

## License

The game uses the Godot Engine, which is distributed under the MIT License (see the in-game credits). A project license has not yet been declared.
