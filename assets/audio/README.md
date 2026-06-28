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

| File                  | Status  |
|-----------------------|---------|
| music_loop.ogg        | ✅ real |
| music_loop1.ogg       | ✅ real |
| music_loop2.ogg       | ✅ real |
| music_loop3.ogg       | ✅ real |
| music_loop4.ogg       | ✅ real |
| music_loop5.ogg       | ✅ real |

## Miner SFX

| Logical name  | File                   | Status      | Used for                                        |
|---------------|------------------------|-------------|-------------------------------------------------|
| footstep      | footstep.ogg/.wav      | ✅ real     | miner steps onto a new tile                     |
| pickaxe       | pickaxe.ogg/.wav       | ✅ real     | mining loop (plays while digging)               |
| plant         | plant.ogg/.wav         | ✅ real     | planting a charge                               |
| explosion     | explosion.ogg/.wav     | ✅ real     | charge detonation                               |
| pickup        | pickup.ogg/.wav        | placeholder | item picked up off the ground                   |
| grab          | grab.ogg/.wav          | placeholder | item picked up or swapped from inventory        |
| spill         | spill.ogg/.wav         | placeholder | buried item unburied (tumbles out of wall)      |
| plank         | plank.ogg/.wav         | placeholder | water plank laid across a flood tile            |
| reel_snap     | reel_snap.ogg/.wav     | placeholder | detonator wire snapped (pulled too far)         |

## Death SFX

| Logical name  | File                   | Status      | Trigger                                         |
|---------------|------------------------|-------------|-------------------------------------------------|
| death         | death.ogg/.wav         | ✅ real     | generic miner death                             |
| splash        | splash.ogg/.wav        | ✅ real     | drowned in deep water                           |
| fall          | fall.ogg/.wav          | placeholder | fell into a pit                                 |
| cavein        | cavein.ogg/.wav        | placeholder | crushed by a cave-in                            |
| sizzle        | sizzle.ogg/.wav        | placeholder | burned by lava                                  |
| zombie_moan   | zombie_moan.ogg/.wav   | placeholder | mauled by a monster                             |

## Ambient SFX

| Logical name  | File                   | Status      | Used for                                        |
|---------------|------------------------|-------------|-------------------------------------------------|
| drip          | drip.ogg/.wav          | ✅ real     | ambient water drips (sparse, randomised timing) |
| crack_rumble  | crack_rumble.ogg/.wav  | ✅ real     | stone creak when stepping on cracked/crumbling floor |
| lava_crackle  | lava_crackle.ogg/.wav  | ✅ real     | looping crackle when within 5 tiles of lava     |
| squelch       | squelch.ogg/.wav       | placeholder | mold patch spreads nearby                       |

## Monster SFX

| Logical name  | File                   | Status      | Used for                                        |
|---------------|------------------------|-------------|-------------------------------------------------|
| goat_hooves   | goat_hooves.ogg/.wav   | placeholder | goat moves                                      |
| goat_bleat    | goat_bleat.ogg/.wav    | placeholder | goat dies                                       |
| slime_slurp   | slime_slurp.ogg/.wav   | placeholder | slime moves                                     |
| slime_splat   | slime_splat.ogg/.wav   | placeholder | slime dies                                      |
| ghost_whisper | ghost_whisper.ogg/.wav | placeholder | ghost moves                                     |
| ghost_scream  | ghost_scream.ogg/.wav  | placeholder | ghost dies                                      |
| zombie_groan  | zombie_groan.ogg/.wav  | placeholder | zombie moves                                    |
| zombie_grunt  | zombie_grunt.ogg/.wav  | placeholder | zombie dies                                     |

## Suggested CC0 sources

- freesound.org — filter by License = Creative Commons 0
- kenney.nl/assets — impact, footstep, and UI packs
- sonniss.com — GDC game audio bundles (free yearly release)
