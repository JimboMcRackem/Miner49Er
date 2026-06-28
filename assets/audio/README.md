# Audio assets

Drop CC0 / royalty-free files here to replace any sound with a better one.
Files are loaded by logical name; `.ogg` is preferred, `.wav` also works.

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

| Logical name  | File                   | Status                         | Used for                                        |
|---------------|------------------------|--------------------------------|-------------------------------------------------|
| footstep      | footstep.ogg/.wav      | ✅ real                        | miner steps onto a new tile                     |
| pickaxe       | pickaxe.ogg/.wav       | ✅ real                        | mining loop (plays while digging)               |
| plant         | plant.ogg/.wav         | ✅ real                        | planting a charge                               |
| explosion     | explosion.ogg/.wav     | ✅ real                        | charge detonation                               |
| pickup        | pickup.ogg             | ✅ real (Kenney Interface)     | item picked up off the ground                   |
| grab          | grab.ogg               | ✅ real (Kenney RPG)           | item picked up or swapped from inventory        |
| spill         | spill.ogg              | ✅ real (Kenney RPG)           | buried item unburied (tumbles out of wall)      |
| plank         | plank.ogg              | ✅ real (Kenney Impact)        | water plank laid across a flood tile            |
| reel_snap     | reel_snap.ogg          | ✅ real (Kenney Interface)     | detonator wire snapped (pulled too far)         |

## Death SFX

| Logical name  | File                   | Status                         | Trigger                                         |
|---------------|------------------------|--------------------------------|-------------------------------------------------|
| death         | death.ogg/.wav         | ✅ real                        | generic miner death                             |
| splash        | splash.ogg/.wav        | ✅ real                        | drowned in deep water                           |
| fall          | fall.ogg               | ✅ real (Kenney Interface)     | fell into a pit                                 |
| cavein        | cavein.ogg             | ✅ real (Kenney Impact)        | crushed by a cave-in                            |
| sizzle        | sizzle.ogg             | ✅ real (Kenney Sci-fi)        | burned by lava                                  |
| zombie_moan   | zombie_moan.ogg        | ✅ real (Kenney Impact)        | mauled by a monster                             |

## Ambient SFX

| Logical name  | File                   | Status                         | Used for                                        |
|---------------|------------------------|--------------------------------|-------------------------------------------------|
| drip          | drip.ogg/.wav          | ✅ real                        | ambient water drips (sparse, randomised timing) |
| crack_rumble  | crack_rumble.ogg/.wav  | ✅ real                        | stone creak when stepping on cracked/crumbling floor |
| lava_crackle  | lava_crackle.ogg/.wav  | ✅ real                        | looping crackle when within 5 tiles of lava     |
| squelch       | squelch.ogg            | ✅ real (Kenney Impact)        | mold patch spreads nearby                       |

## Monster SFX

| Logical name  | File                   | Status                         | Used for                                        |
|---------------|------------------------|--------------------------------|-------------------------------------------------|
| goat_hooves   | goat_hooves.wav        | ✅ real                        | goat moves                                      |
| goat_bleat    | goat_bleat.wav         | ✅ real                        | goat dies                                       |
| slime_slurp   | slime_slurp.ogg        | ✅ real (Kenney Sci-fi)        | slime moves                                     |
| slime_splat   | slime_splat.ogg        | ✅ real (Kenney Sci-fi)        | slime dies                                      |
| ghost_whisper | ghost_whisper.wav      | ✅ real                        | ghost moves                                     |
| ghost_scream  | ghost_scream.ogg       | ✅ real (Kenney Sci-fi)        | ghost dies                                      |
| zombie_groan  | zombie_groan.ogg       | ✅ real                        | zombie moves                                    |
| zombie_grunt  | zombie_grunt.ogg       | ✅ real (Kenney Impact)        | zombie dies                                     |

## Sources

All sounds are CC0. Kenney packs used:
- **Impact Sounds** — kenney.nl/assets/impact-sounds
- **RPG Audio** — kenney.nl/assets/rpg-audio
- **Interface Sounds** — kenney.nl/assets/interface-sounds
- **Sci-fi Sounds** — kenney.nl/assets/sci-fi-sounds

To replace any sound with a better one, drop a file with the same logical name
into this directory. `.ogg` takes priority over `.wav` when both exist.
