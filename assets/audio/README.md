# Audio assets

Drop CC0 / royalty-free files here to replace the procedural placeholders.
Files are loaded by logical name; `.ogg` is preferred, `.wav` also works.
Missing files fall back to a generated placeholder (music has no placeholder —
silence until you add at least one `music_*.ogg`).

## Music

Any number of tracks are supported. Name them `music_1.ogg`, `music_dungeon.ogg`,
`music_boss.ogg`, etc. — any file matching `music_*.ogg` or `music_*.wav` is
added to the pool automatically. A random track plays at match start and changes
on each floor descent, never repeating the previous track.

| File pattern     | Used for                                         |
|------------------|--------------------------------------------------|
| music_*.ogg/wav  | looping match music — add as many as you like    |

## Miner SFX

| Logical name  | File                  | Used for                                        |
|---------------|-----------------------|-------------------------------------------------|
| footstep      | footstep.ogg/.wav     | miner steps onto a new tile                     |
| pickaxe       | pickaxe.ogg/.wav      | mining loop (plays while digging)               |
| plant         | plant.ogg/.wav        | planting a charge                               |
| explosion     | explosion.ogg/.wav    | charge detonation                               |
| pickup        | pickup.ogg/.wav       | item picked up off the ground                   |
| grab          | grab.ogg/.wav         | item picked up or swapped from inventory        |
| spill         | spill.ogg/.wav        | buried item unburied (tumbles out of wall)      |
| plank         | plank.ogg/.wav        | water plank laid across a flood tile            |
| reel_snap     | reel_snap.ogg/.wav    | detonator wire snapped (pulled too far)         |

## Death SFX

| Logical name  | File                  | Trigger                                         |
|---------------|-----------------------|-------------------------------------------------|
| death         | death.ogg/.wav        | generic miner death                             |
| splash        | splash.ogg/.wav       | drowned in deep water                           |
| fall          | fall.ogg/.wav         | fell into a pit                                 |
| cavein        | cavein.ogg/.wav       | crushed by a cave-in                            |
| sizzle        | sizzle.ogg/.wav       | burned by lava                                  |
| zombie_moan   | zombie_moan.ogg/.wav  | mauled by a monster                             |

## Ambient SFX

| Logical name  | File                  | Used for                                        |
|---------------|-----------------------|-------------------------------------------------|
| drip          | drip.ogg/.wav         | ambient water drips (sparse, randomised timing) |
| crack_rumble  | crack_rumble.ogg/.wav | stone creak when stepping on cracked/crumbling floor |
| lava_crackle  | lava_crackle.ogg/.wav | looping crackle when within 5 tiles of lava     |
| squelch       | squelch.ogg/.wav      | mold patch spreads nearby                       |

## Monster SFX

| Logical name  | File                  | Used for                                        |
|---------------|-----------------------|-------------------------------------------------|
| goat_hooves   | goat_hooves.ogg/.wav  | goat moves                                      |
| goat_bleat    | goat_bleat.ogg/.wav   | goat dies                                       |
| slime_slurp   | slime_slurp.ogg/.wav  | slime moves                                     |
| slime_splat   | slime_splat.ogg/.wav  | slime dies                                      |
| ghost_whisper | ghost_whisper.ogg/.wav| ghost moves                                     |
| ghost_scream  | ghost_scream.ogg/.wav | ghost dies                                      |
| zombie_groan  | zombie_groan.ogg/.wav | zombie moves                                    |
| zombie_grunt  | zombie_grunt.ogg/.wav | zombie dies                                     |

## Suggested CC0 sources

- freesound.org — filter by License = Creative Commons 0
- kenney.nl/assets — impact, footstep, and UI packs
- sonniss.com — GDC game audio bundles (free yearly release)
