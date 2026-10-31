# Miner Character Variants — Design Spec

**Date:** 2026-07-11
**Status:** Approved (design), pending implementation plan

## Goal

Let each player pick one of several **playable miner characters** (distinct body
shapes / faces / outfits) in the menus, in addition to their team colour. The
choice is purely cosmetic, client-side, and synced through the lobby exactly
like the existing colour selection. Zero changes to `Miner49er.Core`.

## Global Constraints

- **No `Miner49er.Core` changes.** Variant is a presentation concern only, like
  team colour. No sim logic, no Core tests.
- **Keep team-colour tint.** All new variant art is drawn in a neutral grey-ish
  palette so the existing 8-colour `TintMiner` recolour still reads on the
  clothing. Skin tone and helmet/hat stay untinted (same `TintMiner` rules as
  today: `px.S > 0.3 && px.H < 0.25 && lum > 0.25` is preserved).
- **Wire safety.** New synced field appended to the lobby RPC; never reorder
  existing fields. Old/new peers must not desync — a missing variant defaults to
  0 (Classic).
- **Run `godot` via PowerShell only** (Bash shim breaks headless import with a
  false "assemblies not found"). Import new art with
  `godot --headless --import --quit`.
- **Do not stage** `*.uid`, `.superpowers/`, `Temp/`, `_preview_*`, or unrelated
  pre-existing working-tree edits. Stage feature files by explicit path.
- **PixelLab API key** must never be written to a memory file or committed.

## Roster

Five characters. Variant 0 is the existing sprite, unchanged. Slugs are the
asset subfolder names.

| Index | Name | Slug | Look |
|-------|------|------|------|
| 0 | Classic Miner | *(none — base path)* | existing sprite, unchanged |
| 1 | Burly Prospector | `burly` | fat, bearded, dungarees, hard hat |
| 2 | Gaunt Digger | `gaunt` | emaciated, sallow, tied trousers + t-shirt, cloth cap |
| 3 | Grizzled Veteran | `veteran` | wiry, stubble, eyepatch, headlamp helmet |
| 4 | Stout Matron | `matron` | stocky woman miner, headscarf, rolled sleeves |

## Asset Layout

Variant 0 keeps the current paths (no files move):

```
res://assets/miners/miner_{n,e,s,w}.png     facing stills
res://assets/miners/idle/idle{0,1,2}.png    idle fidgets
res://assets/miners/listen/{n,e,s,w}.png    listen poses
res://assets/miners/walk/{d}{0..3}.png      walk (4 dir × 4 frame)
res://assets/miners/mine/{d}{0..6}.png      mine (4 dir × 7 frame)
res://assets/miners/plant/{d}{0..4}.png     plant (4 dir × 5 frame)
```

Variants 1–4 mirror the exact same tree under a slug subfolder, e.g.:

```
res://assets/miners/burly/miner_{n,e,s,w}.png
res://assets/miners/burly/idle/idle{0,1,2}.png
res://assets/miners/burly/listen/{n,e,s,w}.png
res://assets/miners/burly/walk/{d}{0..3}.png
res://assets/miners/burly/mine/{d}{0..6}.png
res://assets/miners/burly/plant/{d}{0..4}.png
```

Each variant is a **full ~75-frame set** (4 stills + 3 idle + 4 listen + 16 walk
+ 28 mine + 20 plant).

## Architecture

### 1. `MinerVariants` static (new, `game/net/MinerVariants.cs`)

Parallel to `PlayerColors`:

```csharp
public static class MinerVariants
{
    public static readonly string[] Names =
        { "Classic Miner", "Burly Prospector", "Gaunt Digger",
          "Grizzled Veteran", "Stout Matron" };

    // Asset path prefix for each variant. Variant 0 = base; others = slug subfolder.
    public static readonly string[] Slugs = { "", "burly", "gaunt", "veteran", "matron" };

    public static int Count => Names.Length;

    public static int Clamp(int i) => (i % Count + Count) % Count;

    // Path prefix under res://assets/miners/ for a variant, with trailing slash for slugs.
    public static string Prefix(int variant) =>
        Slugs[Clamp(variant)] is "" ? "" : Slugs[Clamp(variant)] + "/";
}
```

### 2. `PlayerInfo.VariantIndex` (net sync)

- Add `public int VariantIndex;` to `PlayerInfo` in `NetworkManager.cs`.
- `HostGame` / `JoinGame` / `JoinByCode` gain a `variantIndex` parameter, stored
  like `colorIndex` (a `_pendingVariant` mirrors `_pendingColor`).
- `SubmitPlayerInfo(name, colorIndex, variantIndex)` — append the new param.
- `BroadcastLobby` serialises a `variants[]` int array alongside `colors[]`;
  `ReceiveLobby` deserialises it. On the receiving side, if the array is shorter
  than expected (older host), missing entries default to 0.
- Bots: `AddBot` assigns a random `VariantIndex` (any of 0–4).

### 3. Texture build — lazy per (variant, colour) — **Approach B**

Today `MatchClient` builds `[colorIndex, …]` texture arrays for all 8 colours at
load. With 5 variants that would be 5× the tinting and GPU memory even for
combos nobody picked.

Instead, build tinted textures **only for the (variant, colour) pairs actually
present among current players** (≤ 8), cached by key:

```csharp
private readonly Dictionary<(int variant, int color), MinerTextureSet> _minerSets = new();
```

where `MinerTextureSet` holds that combo's facing / idle / listen / walk / mine /
plant textures. On match start (once the roster is known), iterate the distinct
`(variant, color)` pairs among `NetworkManager.Players` (+ bots) and build each
missing set. `MinerVariantIndex(minerId)` mirrors the existing
`MinerColorIndex(minerId)` lookup into `PlayerInfo`.

The `BuildMiner*Textures` helpers gain a `string prefix` parameter and load from
`res://assets/miners/{prefix}…`. A variant whose art is missing (e.g. before its
PixelLab frames exist) falls back to the base set so the game never renders
blank — `GD.Load` returning null already degrades gracefully in the current
code; the loader treats a fully-missing variant folder as "use variant 0".

### 4. Draw path

`MatchClient` sprite draw (~line 426) resolves both `variant` and `color` for
the miner, looks up `_minerSets[(variant, color)]` (falling back to
`(0, color)`), and indexes frames as before. Facing/idle/listen/walk/mine/plant
selection logic is unchanged.

## Selection UI (`game/ui/MainMenu.cs`)

- Add a **character `OptionButton`** next to the existing colour `OptionButton`
  in both the solo and multiplayer panels (populated from `MinerVariants.Names`).
- Add a **small live preview**: a `TextureRect` showing the selected variant's
  south-facing still, tinted with the currently-selected colour, updated when
  either picker changes. Reuses `TintMiner` (exposed as needed).
- `HostGame` / `JoinGame` calls pass `_soloVariant.Selected` /
  `_multiVariant.Selected`.
- **Lobby (`game/ui/Lobby.cs`):** show each player's character name next to their
  colour-tinted name label.

## PixelLab Generation (sequenced)

Prove the pipeline on **one** variant before spending credits on all four:

1. Build **Burly Prospector** end-to-end: PixelLab `create_character` (4-dir,
   neutral palette, matching the default miner's size/style) → `animate_character`
   for walk / mine / plant / listen / idle templates → download frames → place
   under `assets/miners/burly/…` → `godot --headless --import --quit` → wire into
   code → verify in-game (sprite, tint, all animations, lobby sync).
2. Only after variant 1 is verified in-game, repeat for `gaunt`, `veteran`,
   `matron`.

The default miner's PixelLab character (size, view angle, canvas) is the
reference; new characters must match it so scale and framing are consistent.

## Testing

Pure Godot presentation, mirroring the colour system which has no Core tests.
Validation is manual in-Godot:

- Each of the 5 characters selectable in solo + multiplayer menus; preview shows
  the right sprite + tint.
- In a match, the local miner and remote/bot miners render the chosen character
  with correct team tint across idle, walk, mine, plant, listen.
- Lobby lists each player's character.
- Wire safety: a peer that defaults variant 0 doesn't desync.
- A variant with missing art falls back to Classic without crashing.

## Out of Scope (YAGNI)

- Per-variant gameplay differences (all characters play identically).
- Unlocks / progression / persistence of the last-picked character.
- Any change to monster or NPC sprites.
