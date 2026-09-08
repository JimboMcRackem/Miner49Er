# C64 Art Cohesion Pass — Design

**Status:** Design approved 2026-09-08. Ready for implementation planning.

**Goal:** Unify all 826 image assets — plus the 162 hardcoded colour sites in code —
into a single, enforceable Commodore 64 visual language: the 16-colour hardware
palette, 4–5 colours per sprite, hard bands, no gradients, no anti-aliasing.

**Touchstone:** Boulder Dash — a C64 cave-digging game. Dark cave, chunky dirt and
boulder tiles, one bright saturated character, glittering payload.

---

## 1. Context

The art library accreted across Phase 6 plus a dozen feature phases, each with its own
PixelLab prompt. The result reads as several different games stitched together:

- **Render style split** — painterly semi-realistic statues (`idol_odin`, `idol_ra`,
  `idol_zeus`, `idol_quetzalcoatl`) and gritty textured rock tiles, next to bold flat
  cartoon (`item_mold`, `prize_gem`, `slime`).
- **Outlines inconsistent** — `chest` and `mine_cart` carry dark contours; `idol_ra`,
  `stone_pile` and `ghost` have none.
- **No shared palette** — muted browns/greys for terrain and miners, neon cyan/magenta
  for water, crystals and the octopus.
- **Monster scale drift** — the octopus is enormous and hyper-detailed; the water snake
  is a thin squiggle; the goat is a naturalistic animal.
- **Three unrelated chests** — `chest.png`, `chest_boss.png`, `treasure_chest.png` share
  no visual language.
- **Two icon-card outliers** — `item_crystal_shard` and `prize_relic` are drawn as
  inventory *cards* with a painted dark backdrop, while every other object is a bare
  object. (Verified: this is painted artwork, **not** an alpha bug. Both have correct
  transparency.)

### Inventory

| Family | Files | Distinct pieces | Disposition |
|---|---:|---:|---|
| Miners | 546 | 6 variants x 6 states | Scripted conversion |
| Monsters | 211 | 7 creatures | Scripted conversion (octopus also rescaled in M5) |
| Objects | 39 | 39 | 29 scripted; 10 redrawn in M5 |
| Tiles | 26 | 26 (+2 atlases) | Redrawn (M4) |
| Portals | 1 | 1 | Scripted conversion |
| Icon / Splash | 3 | 3 | Redrawn (M6) |

92% of the file volume (miners + monsters) is animation frames of just 13 characters,
and those convert cleanly. The stylistically divergent work is concentrated in the 65
object and tile files.

### Prior-art findings (validated by probe, 2026-09-08)

Three conversion algorithms were prototyped against representative sprites before this
design was written. The findings are load-bearing:

1. **Naive nearest-RGB quantisation desaturates.** Median-cut weights clusters by pixel
   count, so small identity-critical areas are merged into the dominant tone, and
   nearest-RGB snapping sends desaturated golds to grey. The miner's yellow hardhat and
   skin tone were destroyed; `idol_ra` became a featureless grey blob.
2. **Forcing distinct palette entries hallucinates colour.** Requiring 5 *different* C64
   colours injected magenta into `idol_odin` and turned the skeleton green and pink.
3. **Authored per-family ramps work.** Posterising each sprite's luminance into a
   hand-chosen ramp *for its own family* preserved gold-as-gold, stone-as-grey, and the
   miner's hardhat and skin, while producing genuinely C64 output.

**Conclusion: no fully-automatic quantiser can do this job.** Deciding that gold on an
idol must stay gold while grey on a skeleton must stay grey is a semantic call, not a
statistical one. The art direction must be authored as data.

A fourth finding shaped section 5: auto-stretching each sprite's luminance range
independently makes **animation frames shimmer**, because the range shifts frame to
frame.

### Useful existing facts

- **All 826 files already use binary alpha** — zero anti-aliased edges. The hard-edge
  requirement is already met; the converter needs no alpha-threshold heuristic.
- The 8 `PlayerColors` entries already map near-1:1 onto C64 hues (`e8c34a` to yellow,
  `4a9be8` to lightblue, `4ae87a` to lightgreen, `c34ae8` to purple, and so on).
- Asset *paths* do not change, so all 826 Godot `.import` files are untouched.

---

## 2. Locked decisions

1. **Target: C64 era**, neon-arcade leaning — dark neutral terrain, bright saturated
   colour reserved for things that matter (glow = it matters).
2. **Fidelity: palette + low colour count.** The 16-colour C64 palette, 4–5 colours per
   sprite, hard bands. **Square 1:1 pixels** — no 2:1 multicolor double-wide pixels.
3. **Approach: convert the bulk, redraw what is structurally wrong.** Script-convert
   miners, monsters and most objects; regenerate only the pieces where recolouring
   cannot fix the underlying drawing.
4. **Sequencing: convert first, review, then redraw.** The whole game is playable in C64
   colour at M2, before any redraw effort is spent.
5. **Purity: source assets and UI are strict; runtime blending is free.** Every PNG and
   every UI colour is strictly in-palette. `LightMap` blending, bloom, fog alpha and
   torch falloff continue to blend continuously. The lit frame is therefore not
   literally 16-colour — this is accepted, to preserve the finished and playtested
   lighting work.

---

## 3. Architecture

### 3.1 Single source of truth

```
art/palette.json     16 C64 colours as named tokens (Pepto reference values)
art/ramps.json       every family ramp + explicit band edges + glob-to-ramp mapping
```

Both files are consumed by the Python tool **and** used to generate a committed
`game/art/C64Palette.cs`. Changing the art direction means editing one file; the tool
and the engine cannot drift apart, because the C# is generated rather than
hand-maintained.

`C64Palette.cs` exposes the 16 colours as named `Color` constants and the team ramps as
arrays. It performs no runtime file I/O.

### 3.2 Preserve originals before converting

Posterising is lossy and **cannot be re-tuned from its own output**. The pipeline is:

```
art/source/**.png              one-time copy of today's assets, git-tracked
        |
        |  tools/artpass/convert.py   (reads palette.json + ramps.json)
        v
assets/**.png                  generated, git-tracked, what the game loads
```

Tweaking a ramp re-runs the converter from source. Without `art/source/`, the first bad
ramp is permanent. This is not optional.

The converter is deterministic and idempotent: same source plus same ramps yields the
same bytes.

### 3.3 Component boundaries

| Unit | Responsibility | Depends on |
|---|---|---|
| `art/palette.json`, `art/ramps.json` | the art direction, as data | nothing |
| `tools/artpass/convert.py` | source-to-assets posterisation | the two JSON files |
| `tools/artpass/verify.py` | assert assets are in-palette | `palette.json` |
| `tools/artpass/contact_sheet.py` | reviewable before/after sheets | nothing |
| `tools/artpass/gen_palette_cs.py` | emit `game/art/C64Palette.cs` | the two JSON files |
| `game/art/C64Palette.cs` | palette tokens + team ramps for engine code | generated |

Each can be understood and run independently. The converter never reads `assets/`; the
verifier never reads `art/source/`.

---

## 4. Family ramps

Ramps are ordered dark to light and **must be monotonically increasing in luminance**
(a validated invariant — see section 9). Initial set:

| Ramp | Colours (dark to light) | Applies to |
|---|---|---|
| `rock` | black, brown, orange, grey | cave rock, dirt tiles |
| `bedrock` | black, darkgrey, grey, lightgrey | impermeable rock |
| `gold_ore` | black, brown, orange, yellow | gold-flecked rock |
| `gold` | black, brown, orange, yellow, white | gold idols, chalice, crown, coins |
| `wood` | black, brown, orange, lightred | chests, planks, props, rails |
| `metal` | black, darkgrey, grey, lightgrey, white | detonator, toolbox, cart, gate |
| `stone` | black, darkgrey, grey, lightgrey | stone statues, stone piles |
| `bone` | black, darkgrey, lightgrey, white | skeletons, skull idol |
| `flesh` | black, brown, red, lightred, yellow | miners (untinted regions) |
| `slime` | black, green, lightgreen, white | slime, mold |
| `spectral` | black, blue, lightblue, cyan, white | ghost |
| `crystal` | black, blue, lightblue, cyan, white | crystal shard, gem, vision |
| `water` | black, blue, lightblue, cyan | water tiles, water snake |
| `lava` | black, red, orange, yellow, white | lava tiles, blast, fire |
| `beast` | black, blue, purple, lightred, cyan | octopus, exotic monsters |
| `foliage` | black, green, lightgreen, yellow | mold patch, organic growth |

Assignment is by glob in `ramps.json`, with per-file overrides where a family rule is
wrong (for example `idol_odin` maps to `stone`, not `gold`).

Two pairs — `bedrock`/`stone` and `spectral`/`crystal` — start with identical colours.
They are deliberately kept as separate named ramps rather than merged, because their
**band edges** (section 5) are tuned independently: bedrock wants a flat, low-contrast
wall read, while a stone statue wants its form picked out. Merging them would couple two
unrelated tuning decisions to one knob.

---

## 5. Fixed luminance bands

Band edges are **pinned per ramp** in `ramps.json`, not derived per sprite. This is the
animation-shimmer fix: with per-sprite auto-stretch, a walk cycle's palette shifts frame
to frame as each frame's min and max luminance change.

Band edges are **explicit and non-uniform**, not equal divisions. This matters for
readability: the body band should own the largest luminance span, while outline and rim
bands stay thin. Edges are tuned once per ramp during M1 against contact sheets.

**Atlas caveat:** `combined_terrain_atlas.png` and `pit_wall_spritesheet.png` are
autotile sheets. Band statistics are computed **per sheet, not per tile** — otherwise
adjacent tiles land in different bands and autotiled edges stop being seamless.

---

## 6. Team colours become ramps

`MatchClient.TintMiner` (game/net/MatchClient.cs:882) currently recolours miners at
runtime with a continuous multiply:

```csharp
float l = lum * 0.6f + 0.4f;
img.SetPixel(x, y, new Color(tint.R * l, tint.G * l, tint.B * l, px.A));
```

This generates arbitrary RGB values. However perfectly the source PNGs are converted,
the miners on screen would be off-palette in 8 different ways.

**Replacement:** `PlayerColors` changes from 8 hex values to 8 three-step C64 ramps, and
`TintMiner` becomes a luminance-band lookup into the team's ramp. Output is then
strictly in-palette, and hard colour bands are how real C64 multicolor sprites worked.

The existing hue-preservation branch (skip warm, bright pixels so skin and the yellow
hardhat survive) is retained, and applies before the band lookup.

| # | Team | Ramp (outline, shadow, body) | Identity |
|---|---|---|---|
| 1 | Gold | black, orange, yellow | yellow |
| 2 | Blue | black, blue, lightblue | lightblue |
| 3 | Red | black, brown, red | red |
| 4 | Green | black, green, lightgreen | lightgreen |
| 5 | Purple | black, blue, purple | purple |
| 6 | Orange | black, brown, orange | orange |
| 7 | Cyan | black, lightblue, cyan | cyan |
| 8 | Pink | black, red, lightred | lightred |

---

## 7. Code palette pass

162 `new Color(...)` call sites across 19 files are replaced with `C64Palette` tokens:

`FogRenderer`, `Hud`, `LightRenderer`, `LoadingScreen`, `Main`, `MatchClient`,
`TerrainMap`, `WorldRenderer`, and the 11 `game/ui/` panels (`Compass`, `CreditsPanel`,
`DeathFeed`, `HighScorePanel`, `MainMenu`, `MinimapOverlay`, `ResultsOverlay`,
`ScoreboardOverlay`, `SettingsPanel`, `ShopPanel`, `StatsOverlay`).

Sites that define a *blend input* consumed by the lighting system (fog alpha, light
tints, bloom sources) take a palette token as their base colour; their continuous
blending is unaffected, per the purity decision in section 2.5.

---

## 8. Redraw list

Deferred behind the M3 review gate, so ramps are proven before art effort is spent.

**M4 — terrain.** All 26 tile files plus both atlases, redrawn Boulder-Dash chunky:
flat blocky rock, readable dirt, hard-edged boulders. This is the change that actually
sells the era, because terrain is most of the screen.

**M5 — objects.** The 3 chests unified into one visual language. Painterly idols
(`odin`, `ra`, `zeus`, `quetzalcoatl`, `vishnu`) flattened. `item_crystal_shard` and
`prize_relic` repainted without their icon-card backdrops. Octopus rescaled to sit with
the other monsters.

**M6 — marketing art.** `Icon.png` (1024 square) and `Splash.png` (1376x768).

---

## 9. Testing

`Miner49er.Core` is untouched, so the existing **926 tests stay green without
modification**. This is a `game/`, `assets/`, `art/` and `tools/` change only.

New tests:

- **Palette compliance** — every pixel of every `assets/**.png` is one of the 16 palette
  colours or fully transparent. This is the permanent guard that stops cohesion decaying
  by the next feature phase.
- **Binary alpha** — every pixel's alpha is 0 or 255.
- **Ramp validity** — every ramp's colours increase monotonically in luminance, every
  token name resolves, and every band-edge list matches its ramp length.
- **Tint output** — `TintMiner` output pixels are in-palette for all 8 teams across a
  sample of source frames.
- **Generated C# is current** — `C64Palette.cs` matches what the generator emits from the
  JSON, so a JSON edit without regeneration fails the build.

Manual review:

- Contact sheets (before and after, per family) generated at M2 and M5.
- Playtest at M2 (assets converted) and M3 (code palette pass).

---

## 10. Risks and accepted trade-offs

1. **Eight distinguishable team ramps out of 16 fixed colours is tight.** Teams Red,
   Purple and Orange have low-luminance identity colours and may read poorly in a dark
   cave. *Mitigation:* explicit per-ramp band edges (section 5) plus playtest at M3; fall
   back to 6 teams if any pair is genuinely unreadable.

2. **Posterising is lossy.** Mitigated by `art/source/` (section 3.2), which makes ramp
   tuning repeatable. If `art/source/` is ever skipped, this risk becomes unrecoverable.

3. **The lit frame is not literally 16-colour.** Accepted per section 2.5, to preserve
   the lighting, bloom and radial-fog work. A full-frame quantising shader was considered
   and deliberately not taken, because it would flatten those systems.

4. **Palette alone will not make the miners cartoonish.** The 6 miner variants are
   realistically proportioned RPG sprites, and this plan deliberately preserves those
   drawings — they are the most animated, most expensive assets, and they convert
   cleanly. Converting them makes them *cohesive and unmistakably C64*, but not
   *cartoonish*. **Decision point:** if, after the M3 playtest, they still read too
   realistic against the redrawn terrain, a miner redraw becomes a separate M7 — costing
   6 variants x 6 states x 4 facings. This is called out now so that it is a choice
   later, not a surprise.

5. **Autotile seams.** Mitigated by per-sheet band statistics (section 5). A regression
   here shows up as mismatched tile edges, so M2 review must include an in-game terrain
   look, not only contact sheets.

---

## 11. Milestones

| # | Deliverable | Gate |
|---|---|---|
| M1 | `palette.json`, `ramps.json`, converter, verifier, contact-sheet tool, `C64Palette.cs` generator | tests green |
| M2 | `art/source/` populated; all 826 assets converted; ramps tuned | **playtest** |
| M3 | Code palette pass (162 sites), `PlayerColors` ramps, `TintMiner` band swap | **playtest + review gate** |
| M4 | Terrain redraw (26 tiles + 2 atlases) | playtest |
| M5 | Object redraws (chests, idols, crystal shard, relic, octopus) | contact sheet |
| M6 | Icon + Splash | — |
