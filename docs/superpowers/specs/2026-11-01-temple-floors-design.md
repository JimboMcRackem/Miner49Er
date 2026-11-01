# Temple Floors — Design

## Why

Most of the mine is caves, and floors blur together. A temple floor gives a run a
memorable discovery: it looks like any other mine until someone digs through a wall
and finds an older, stranger place underneath. The feel is ominous rather than
harder-for-its-own-sake.

Source: `ToDo/Task4.txt` #1, plus the design conversation of 2026-11-01.

## Goals

- Temple floors turn up at random in Expedition, solo and co-op.
- The floor starts as a normal mine. Digging uncovers hidden temple sections.
- Finding an artifact and carrying it out through the exit completes the floor.
- The floor is unmistakable once found: marble, purple flame, different monsters,
  ominous music.

## Non-goals

- No temple floors in any other mode, including Treasure Hunt.
- No whole-floor temple. Rock and caves remain; the temple sits inside them.
- No new mine events and no changes to lobby options.
- No mid-floor save work. Solo Continue re-enters the floor fresh, as it does today.

## Anti-goals

- A floor that can trap the party, because the artifact is unreachable or lost.
- Temple sections visible before anyone breaks in.
- A marble box that plays like a cave with a new texture. The props, flames and
  monsters are the point.

## Decided by the user

- Expedition only, appearing at random.
- Temple sections are uncovered by digging through a normal-looking floor.
- Contents: columns, some collapsed; altars; clusters of offering jars; priest
  spirits; additional skeletons. Mobs are mainly ghosts, human skeletons and priest
  spirits, with a small sprinkling of the rest.
- Marble walls. Only wall surfaces can be mined, as played back and confirmed.
- Wall sconces with purple flames that light as you approach.
- More ominous music.
- The artifact is one of the Treasure Hunt idols, chosen at random, hidden buried, in
  a chest or in a pot. You must take it with you when you exit the floor.

## Claude's calls

The user saw and accepted the first six in chat. The rest were made while writing
this document, and the user has not yet reviewed them.

1. The exit will not let the party leave until someone standing on it holds the artifact.
2. If the carrier dies, the artifact drops where they fell. `Simulation` already drops
   held items on death or stun.
3. The artifact is always inside a temple section and always reachable.
4. About 1 floor in 6, from floor 5 on, never two in a row, never the boss floor.
5. A big gold bonus for leaving with the artifact, and every idol carried out is shown
   in the end-of-run results.
6. Music turns ominous when someone first breaks into a temple section, not on arrival.
7. Temple floors replace nothing on shop floors: they only roll on floors where
   `floor % 4 != 0`, so shops and existing treasure floors keep their rhythm.
8. Temple floors are never flooded and have no portals.
9. Each jar is smashed like mining a tile, and holds small gold (40%), a random chest
   item (10%) or nothing (50%).
10. Priest spirit behaviour, below.
11. Marble mining rule, below. This is one reading of "only mine on walls".

## What already exists

Expedition treasure floors do most of the objective. `MapConfig.ExpeditionTreasureKind`
and `ExpeditionTreasureInChest` place an idol, buried or in a chest. Picking it up opens
the exit (`Simulation.cs`, near line 1772). The Listen compass points at it, and the HUD
and announcement already exist in `Main.cs`. Build on this rather than beside it.

`MapConfig.FloorConfig` is the per-floor roll, and must stay deterministic through
`FloorHash` because clients generate the same map from the seed. Use a new salt.

## Map

Generation stamps 2 to 4 temple sections into solid rock after caves are connected, so
that they stay outside the cave region and receive no ordinary items, spawns or gold.

- **Section:** one or two joined rooms of about 7 by 7 to 13 by 11 tiles, scaling with
  map size.
- **Marble walls:** a new solid tile that blocks sight and light, and can't be mined or
  blasted.
- **Marble panels:** a second, visually distinct solid tile set into the walls. Panels
  are one tile thick and can be mined. They are the "wall surfaces" of the temple and
  may hide a niche holding gold or the artifact.
- **Temple floor:** a new walkable tile that can't be mined and never takes pits, cracks
  or lava.
- **Doorway:** every section has at least one sealed doorway of ordinary rock, facing rock
  or cave. This is how players get in. It must be reachable by digging from the spawn.
- **Fallen rock:** rockfalls and tremors never mark temple tiles.

## Props

Deterministic from the seed, so they need no network data. They go in `GeneratedMap`
and are drawn by the client.

- Columns block movement and sight. Collapsed columns block movement only.
- Altars block movement. One altar per section.
- Jars come in clusters of 3 to 6, and block movement until smashed.
- Sconces sit on marble wall faces, spaced so a room is never fully dark once lit.
  Each client lights a sconce the first time its miner comes within 5 tiles. This is
  cosmetic only. Lit sconces use the Fire flicker with a purple glow.

Jar smashing changes state, so it does travel through the existing tile-change path.

## Artifact

- Chosen with the existing `ExpeditionTreasureKind` roll. Placed in a random section as
  one of: buried in a marble panel, in a chest, or in a pot among a jar cluster.
- Picking it up announces itself and points the compass at the exit, as today.
- New config `RequireArtifactAtExit`: the floor completes only when a miner holding the
  artifact stands on the exit with the party, and the HUD says so. Ordinary treasure
  floors keep their current rule.
- It uses the carried-item slot, so the carrier can't hold a lantern too.
- The compass while listening is the safety net for finding the temple.

## Monsters

`MonsterSpawner` gets a temple roster for temple floors. Monsters in sections are placed
inside them and stay there until the section is opened. Skeletons use the existing
dormant bones and noise-wake. Rough mix inside sections: 40% human skeletons, 30% ghosts,
20% priest spirits, 10% other. The cave part of the floor keeps a reduced normal roster.

**Priest spirit (new `MonsterKind`):** floats like a ghost but stays inside its section.
On seeing a miner it telegraphs for about a second, then chants, waking all dormant
skeletons within 6 tiles. Touch damage matches a ghost. Sprite is robed and pale purple.

## Sound and light

- **Music:** switches to a temple track for everyone when any miner first enters a
  section, and resets on the next floor. See open question.
- **Lighting:** marble reflects no extra light. Darkness works as it does on other floors.
- **Announcement:** "You have uncovered an ancient temple!" on first entry.

## Networking

Tiles and props come from the shared seed. New tile changes use the existing path. The
"temple entered" state and exit requirement go in the snapshot as trailing fields.
Hosts and clients need the same build.

## Bots

Bots already avoid walls they can't mine only by pathing, and do not seek the Expedition
idol. In a party with humans they should follow as they do on treasure floors. Solo with
bots still needs a human carrier, which is acceptable. Bots must treat columns, altars and
jars as blocked, and must not stand forever on the exit waiting.

## Art (PixelLab)

Marble wall and panel set, temple floor, standing column, two collapsed columns, altar,
jar plus smashed jar, sconce unlit and lit with animated flame, priest spirit with walk
animation, and a pot variant that holds the artifact, which should look like the others.

## Testing

Core tests: floor roll rules and determinism, every section has a doorway reachable by
digging, the artifact is always reachable, marble can't be mined or blasted while panels
can, exit refusal without the artifact, artifact dropped on carrier death, jar outcomes,
priest spirit stays in its section and wakes skeletons. Harness gains a `temple=1` option
so screenshots can check each piece.

## Left to the builder

Exact room shapes, tile ids, prop spacing, gold bonus size, and how sections join.

## Music source

The project has no ominous music track, so the existing music plays slowed and muffled
as a stand-in (Claude's call). Keep it easy to replace with a real track later.
