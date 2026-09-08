# C64 Art Cohesion — M1–M3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert all 826 image assets and every hardcoded colour site in `game/` onto the 16-colour C64 hardware palette, enforced by tooling and tests.

**Architecture:** The art direction lives as data in `art/palette.json` + `art/ramps.json`. A Python tool posterises `art/source/**.png` into `assets/**.png` by mapping each pixel's luminance into its family's authored ramp. The same JSON generates `src/Miner49er.Core/Art/C64Palette.g.cs`, so the engine and the tool cannot drift. Palette, ramp and tint logic are pure C# in `Miner49er.Core` (engine-independent, therefore unit-testable); `game/` gets a thin Godot `Color` adapter.

**Tech Stack:** Python 3.14 + Pillow 12.3 (stdlib `unittest` — no pytest), .NET 10, xunit 2.5.3, Godot 4.6.3.

**Spec:** `docs/superpowers/specs/2026-09-08-c64-art-cohesion-design.md`

## Scope

This plan covers **M1–M3 only**: the tooling, the asset conversion, and the code palette pass.

**M4–M6 (terrain redraw, object redraws, icon/splash) are deliberately excluded** and get their own plan. The spec puts a playtest review gate after M3 precisely because the redraws depend on ramps that are not yet tuned. Writing redraw tasks now would be speculation.

## Global Constraints

- **Palette:** exactly the 16 Pepto C64 values in `art/palette.json`. No colour outside this set may appear in any `assets/**.png` pixel.
- **Alpha:** every asset pixel is alpha 0 or 255. No anti-aliasing. (All 826 files already satisfy this — do not regress it.)
- **Pixels:** square 1:1. Never introduce 2:1 double-wide multicolor pixels.
- **Colours per sprite:** 4–5, from one family ramp.
- **Runtime purity:** source assets and UI colours are strict; `LightMap`, bloom, fog alpha and torch falloff keep blending continuously. Colour components **greater than 1.0 are HDR bloom drivers and must stay over-range** (see Task 7).
- **Core stays green:** `Miner49er.Core` gains only the new `Art/` files. The existing **926 tests must still pass** at every commit.
- **Asset paths never change** — all 826 Godot `.import` files stay valid.
- **Style:** `src/` uses 4-space indent and namespace `Miner49er.Core` (flat, even in subdirectories). `game/` uses **tabs** and namespace `Miner49er`.
- **Baseline command:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
- **Python tests:** `python -m unittest discover -s tools/artpass/tests -t .`
- **Always run commands from the repo root** (`D:\Projects\Miner49er`). The Python tools resolve `art/`, `assets/` and `game/` relative to it.
- **Launch the game with `godot` from PowerShell only.** The Bash shim breaks headless mode with a false "assemblies not found" error.

---

## File Structure

**Created:**

| Path | Responsibility |
|---|---|
| `art/palette.json` | the 16 C64 colours, by name |
| `art/ramps.json` | family ramps, team ramps, band edges, glob assignment, per-file overrides |
| `art/source/**.png` | one-time snapshot of today's assets; the converter's only input |
| `tools/artpass/palette.py` | load JSON, luminance, band lookup, ramp resolution |
| `tools/artpass/convert.py` | CLI: `art/source` → `assets`, self-verifying |
| `tools/artpass/verify.py` | CLI: assert `assets/**.png` in-palette + binary alpha |
| `tools/artpass/contact_sheet.py` | CLI: before/after review sheets |
| `tools/artpass/gen_palette_cs.py` | CLI: emit `C64Palette.g.cs` + `C64Ramps.g.cs` |
| `tools/artpass/map_code_colors.py` | CLI: suggest a palette token for each code colour literal |
| `tools/artpass/check_code_palette.py` | CLI: assert no raw colour literals remain in `game/` |
| `tools/artpass/tests/test_palette.py` | unit tests for banding + ramp resolution |
| `tools/artpass/tests/test_convert.py` | unit tests for the posteriser |
| `tools/artpass/tests/test_assets.py` | compliance sweep over `assets/**.png` |
| `src/Miner49er.Core/Art/Rgb.cs` | engine-independent colour struct + HSV + luminance |
| `src/Miner49er.Core/Art/ColorRamp.cs` | ramp type + luminance band lookup |
| `src/Miner49er.Core/Art/MinerTint.cs` | pure per-pixel team tint |
| `src/Miner49er.Core/Art/C64Palette.g.cs` | **generated** — the 16 colours |
| `src/Miner49er.Core/Art/C64Ramps.g.cs` | **generated** — family + team ramps |
| `src/Miner49er.Core.Tests/Art/RgbTests.cs` | |
| `src/Miner49er.Core.Tests/Art/ColorRampTests.cs` | |
| `src/Miner49er.Core.Tests/Art/MinerTintTests.cs` | |
| `game/art/C64Colors.cs` | Godot `Color` adapter over `C64Palette` |

**Modified:** `game/net/PlayerColors.cs`, `game/net/MatchClient.cs` (`TintMiner`), and the 19 files holding colour literals.

**Note (observed, not addressed):** `game/WorldRenderer.cs` is 2288 lines and holds 98 of the 162 colour sites. The Phase 6 spec expected it to be split into `ObjectRenderer` / `TileTransitionOverlay`; that never happened. This plan does **not** restructure it — that is scope the spec did not buy. It is recorded here so the next person knows it is a known debt, not an oversight.

---

## Task 1: Palette data and generated Core palette

**Files:**
- Create: `art/palette.json`
- Create: `tools/artpass/__init__.py`, `tools/artpass/palette.py`
- Create: `tools/artpass/gen_palette_cs.py`
- Create: `src/Miner49er.Core/Art/Rgb.cs`
- Create: `src/Miner49er.Core/Art/C64Palette.g.cs` (generated)
- Create: `src/Miner49er.Core.Tests/Art/RgbTests.cs`
- Create: `tools/artpass/tests/__init__.py`, `tools/artpass/tests/test_palette.py`

**Interfaces:**
- Produces (Python): `palette.load_palette(path) -> dict[str, tuple[int,int,int]]`, `palette.luminance(rgb) -> float` (0.0–1.0)
- Produces (C#): `Miner49er.Core.Rgb` (readonly struct, `byte R,G,B`; `float Luminance`, `float Hue`, `float Saturation`); `Miner49er.Core.C64Palette` (16 static `Rgb` fields, `Rgb[] All`, `Rgb ByName(string)`)

- [ ] **Step 1: Write `art/palette.json`**

```json
{
  "_comment": "Pepto reference values for the Commodore 64 hardware palette. Source of truth for all art and UI colour in this project.",
  "colors": {
    "black":      "000000",
    "white":      "FFFFFF",
    "red":        "813338",
    "cyan":       "75CEC8",
    "purple":     "8E3C97",
    "green":      "56AC4D",
    "blue":       "2E2C9B",
    "yellow":     "EDF171",
    "orange":     "8E5029",
    "brown":      "553800",
    "lightred":   "C46C71",
    "darkgrey":   "4A4A4A",
    "grey":       "7B7B7B",
    "lightgreen": "A9FF9F",
    "lightblue":  "706DEB",
    "lightgrey":  "B2B2B2"
  }
}
```

- [ ] **Step 2: Write the failing Python test**

Create `tools/artpass/tests/__init__.py` (empty) and `tools/artpass/tests/test_palette.py`:

```python
import unittest
from tools.artpass import palette


class LoadPaletteTests(unittest.TestCase):
    def test_loads_sixteen_colors(self):
        p = palette.load_palette()
        self.assertEqual(16, len(p))

    def test_black_and_white_are_exact(self):
        p = palette.load_palette()
        self.assertEqual((0, 0, 0), p["black"])
        self.assertEqual((255, 255, 255), p["white"])

    def test_yellow_matches_pepto(self):
        p = palette.load_palette()
        self.assertEqual((0xED, 0xF1, 0x71), p["yellow"])


class LuminanceTests(unittest.TestCase):
    def test_black_is_zero(self):
        self.assertAlmostEqual(0.0, palette.luminance((0, 0, 0)), places=5)

    def test_white_is_one(self):
        self.assertAlmostEqual(1.0, palette.luminance((255, 255, 255)), places=5)

    def test_uses_rec601_weights(self):
        # 0.299*255/255 == 0.299 for pure red
        self.assertAlmostEqual(0.299, palette.luminance((255, 0, 0)), places=3)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 3: Run it to make sure it fails**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: FAIL — `ModuleNotFoundError` for `tools` (the package `__init__.py` files do not exist yet, and neither does `palette.py`)

- [ ] **Step 4: Write `tools/artpass/palette.py` (palette half only)**

Create `tools/artpass/__init__.py` (empty), `tools/artpass/tests/__init__.py` (empty), and — because `tools/` must be an importable package — `tools/__init__.py` (empty). Then `tools/artpass/palette.py`:

```python
"""Loading and colour maths for the C64 art pass.

The 16-colour palette and every ramp live in JSON under art/. This module is the
only place that parses them; convert.py, verify.py and gen_palette_cs.py all go
through here so there is exactly one interpretation of the art direction.
"""
import json
import os

ART_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "art")
PALETTE_PATH = os.path.normpath(os.path.join(ART_DIR, "palette.json"))


def load_palette(path=PALETTE_PATH):
    """name -> (r, g, b). Values are the C64 hardware palette; nothing else is legal."""
    with open(path, encoding="utf-8") as fh:
        raw = json.load(fh)["colors"]
    return {name: _hex_to_rgb(h) for name, h in raw.items()}


def _hex_to_rgb(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def luminance(rgb):
    """Rec.601 luma, normalised to 0.0-1.0.

    Absolute (not per-sprite-stretched) on purpose: fixed thresholds are what stop
    animation frames shimmering as each frame's brightness range shifts.
    """
    r, g, b = rgb
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
```

- [ ] **Step 5: Run the tests and make sure they pass**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: PASS — 6 tests

- [ ] **Step 6: Write the failing C# test**

Create `src/Miner49er.Core.Tests/Art/RgbTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class RgbTests
{
    [Fact]
    public void Black_luminance_is_zero()
    {
        Assert.Equal(0f, new Rgb(0, 0, 0).Luminance, 5);
    }

    [Fact]
    public void White_luminance_is_one()
    {
        Assert.Equal(1f, new Rgb(255, 255, 255).Luminance, 5);
    }

    [Fact]
    public void Grey_has_zero_saturation()
    {
        Assert.Equal(0f, new Rgb(123, 123, 123).Saturation, 5);
    }

    [Fact]
    public void Pure_red_hue_is_zero()
    {
        Assert.Equal(0f, new Rgb(255, 0, 0).Hue, 5);
    }

    [Fact]
    public void Palette_has_sixteen_colors()
    {
        Assert.Equal(16, C64Palette.All.Length);
    }

    [Fact]
    public void Palette_yellow_matches_pepto()
    {
        Assert.Equal(new Rgb(0xED, 0xF1, 0x71), C64Palette.Yellow);
    }

    [Fact]
    public void ByName_resolves_case_insensitively()
    {
        Assert.Equal(C64Palette.LightBlue, C64Palette.ByName("lightblue"));
        Assert.Equal(C64Palette.LightBlue, C64Palette.ByName("LightBlue"));
    }

    [Fact]
    public void ByName_rejects_unknown()
    {
        Assert.Throws<KeyNotFoundException>(() => C64Palette.ByName("chartreuse"));
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: FAIL — `Rgb` and `C64Palette` do not exist (compile error)

- [ ] **Step 8: Write `src/Miner49er.Core/Art/Rgb.cs`**

```csharp
using System;

namespace Miner49er.Core;

/// <summary>An 8-bit RGB colour with no engine dependency, so palette and tint logic
/// can be unit-tested without Godot. <c>game/art/C64Colors.cs</c> adapts these to
/// <c>Godot.Color</c> at the boundary.</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }

    public Rgb(int r, int g, int b) : this((byte)r, (byte)g, (byte)b) { }

    /// <summary>Rec.601 luma, 0.0-1.0. Matches tools/artpass/palette.py:luminance.</summary>
    public float Luminance => (0.299f * R + 0.587f * G + 0.114f * B) / 255f;

    /// <summary>HSV saturation, 0.0-1.0. Matches Godot's Color.S.</summary>
    public float Saturation
    {
        get
        {
            int max = Math.Max(R, Math.Max(G, B));
            int min = Math.Min(R, Math.Min(G, B));
            return max == 0 ? 0f : (max - min) / (float)max;
        }
    }

    /// <summary>HSV hue, 0.0-1.0. Matches Godot's Color.H.</summary>
    public float Hue
    {
        get
        {
            float r = R / 255f, g = G / 255f, b = B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            if (d <= 0f) return 0f;

            float h;
            if (max == r) h = (g - b) / d % 6f;
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;

            h /= 6f;
            return h < 0f ? h + 1f : h;
        }
    }

    public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is Rgb o && Equals(o);

    public override int GetHashCode() => (R << 16) | (G << 8) | B;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);

    public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);
}
```

- [ ] **Step 9: Write the generator `tools/artpass/gen_palette_cs.py`**

```python
"""Emit src/Miner49er.Core/Art/C64Palette.g.cs from art/palette.json.

Generated rather than hand-written so the engine cannot drift from the tool.
Run after any edit to art/palette.json; test_palette.py asserts the file on disk
matches what this emits.
"""
import os
import sys

from tools.artpass import palette

CS_NAMES = {
    "black": "Black", "white": "White", "red": "Red", "cyan": "Cyan",
    "purple": "Purple", "green": "Green", "blue": "Blue", "yellow": "Yellow",
    "orange": "Orange", "brown": "Brown", "lightred": "LightRed",
    "darkgrey": "DarkGrey", "grey": "Grey", "lightgreen": "LightGreen",
    "lightblue": "LightBlue", "lightgrey": "LightGrey",
}

OUT_PATH = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "src", "Miner49er.Core", "Art", "C64Palette.g.cs"))


def render(pal):
    lines = [
        "// <auto-generated>",
        "//     Generated by tools/artpass/gen_palette_cs.py from art/palette.json.",
        "//     Do not edit by hand - edit the JSON and regenerate.",
        "// </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace Miner49er.Core;",
        "",
        "/// <summary>The 16-colour Commodore 64 hardware palette.</summary>",
        "public static class C64Palette",
        "{",
    ]
    for name, cs in CS_NAMES.items():
        r, g, b = pal[name]
        lines.append(f"    public static readonly Rgb {cs} = new(0x{r:02X}, 0x{g:02X}, 0x{b:02X});")
    lines.append("")
    lines.append("    public static readonly Rgb[] All =")
    lines.append("    {")
    lines.append("        " + ", ".join(CS_NAMES[n] for n in CS_NAMES))
    lines.append("    };")
    lines.append("")
    lines.append("    private static readonly Dictionary<string, Rgb> ByLowerName = new()")
    lines.append("    {")
    for name, cs in CS_NAMES.items():
        lines.append(f'        ["{name}"] = {cs},')
    lines.append("    };")
    lines.append("")
    lines.append("    /// <summary>Look a palette colour up by its JSON name, case-insensitively.</summary>")
    lines.append("    public static Rgb ByName(string name) => ByLowerName[name.ToLowerInvariant()];")
    lines.append("}")
    return "\n".join(lines) + "\n"


def main():
    text = render(palette.load_palette())
    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    if "--check" in sys.argv:
        current = open(OUT_PATH, encoding="utf-8").read() if os.path.exists(OUT_PATH) else ""
        if current != text:
            print("STALE: C64Palette.g.cs does not match art/palette.json", file=sys.stderr)
            return 1
        print("OK: C64Palette.g.cs is current")
        return 0
    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"wrote {OUT_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 10: Generate the palette and run both test suites**

```bash
python -m tools.artpass.gen_palette_cs
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo
```
Expected: `wrote .../C64Palette.g.cs`, then **934 passed** (926 baseline + 8 new)

- [ ] **Step 11: Add the drift guard to the Python tests**

Append to `tools/artpass/tests/test_palette.py`:

```python
class GeneratorTests(unittest.TestCase):
    def test_generated_cs_is_current(self):
        from tools.artpass import gen_palette_cs
        expected = gen_palette_cs.render(palette.load_palette())
        with open(gen_palette_cs.OUT_PATH, encoding="utf-8") as fh:
            self.assertEqual(expected, fh.read(),
                             "C64Palette.g.cs is stale - run python -m tools.artpass.gen_palette_cs")
```

- [ ] **Step 12: Run the Python tests**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: PASS — 7 tests

- [ ] **Step 13: Commit**

```bash
git add art/palette.json tools/__init__.py tools/artpass src/Miner49er.Core/Art src/Miner49er.Core.Tests/Art
git commit -m "feat(art): C64 palette as data, generated into Core"
```

---

## Task 2: Ramps, band lookup, and ramp validation

**Files:**
- Create: `art/ramps.json`
- Modify: `tools/artpass/palette.py` (add ramp loading + banding)
- Modify: `tools/artpass/gen_palette_cs.py` (also emit `C64Ramps.g.cs`)
- Create: `src/Miner49er.Core/Art/ColorRamp.cs`
- Create: `src/Miner49er.Core/Art/C64Ramps.g.cs` (generated)
- Create: `src/Miner49er.Core.Tests/Art/ColorRampTests.cs`
- Modify: `tools/artpass/tests/test_palette.py`

**Interfaces:**
- Consumes: `palette.load_palette`, `palette.luminance`, `Rgb`, `C64Palette` (Task 1)
- Produces (Python): `palette.load_ramps() -> dict`, `palette.band_index(lum01, edges) -> int`, `palette.ramp_for(relpath, ramps) -> str`
- Produces (C#): `Miner49er.Core.ColorRamp` (`Rgb[] Colors`, `float[] Edges`, `Rgb Identity`, `Rgb Map(float luminance)`); `Miner49er.Core.C64Ramps` (`ColorRamp[] Teams`, `ColorRamp ByName(string)`)

- [ ] **Step 1: Write `art/ramps.json`**

Band edges are absolute normalised-luminance thresholds, `len(edges) == len(colors) - 1`, ascending. Absolute (not per-image) thresholds are what make the conversion shimmer-free, and they also remove the autotile-atlas hazard from the spec entirely: with no per-image statistics, adjacent tiles in a sheet cannot land in different bands.

```json
{
  "_comment": "Authored art direction. Ramps run dark -> light and must be monotonic in luminance. Edges are absolute normalised-luminance thresholds; len(edges) == len(colors) - 1.",
  "families": {
    "rock":       { "colors": ["black", "brown", "orange", "grey"],                     "edges": [0.10, 0.30, 0.55] },
    "bedrock":    { "colors": ["black", "darkgrey", "grey", "lightgrey"],               "edges": [0.12, 0.38, 0.62] },
    "gold_ore":   { "colors": ["black", "brown", "orange", "yellow"],                   "edges": [0.10, 0.32, 0.62] },
    "gold":       { "colors": ["black", "brown", "orange", "yellow", "white"],          "edges": [0.08, 0.28, 0.55, 0.86] },
    "wood":       { "colors": ["black", "brown", "orange", "lightred"],                 "edges": [0.10, 0.30, 0.58] },
    "metal":      { "colors": ["black", "darkgrey", "grey", "lightgrey", "white"],      "edges": [0.08, 0.28, 0.50, 0.80] },
    "stone":      { "colors": ["black", "darkgrey", "grey", "lightgrey"],               "edges": [0.10, 0.34, 0.60] },
    "bone":       { "colors": ["black", "darkgrey", "lightgrey", "white"],              "edges": [0.10, 0.34, 0.72] },
    "flesh":      { "colors": ["black", "brown", "red", "lightred", "yellow"],          "edges": [0.08, 0.26, 0.46, 0.74] },
    "slime":      { "colors": ["black", "green", "lightgreen", "white"],                "edges": [0.10, 0.40, 0.78] },
    "spectral":   { "colors": ["black", "blue", "lightblue", "cyan", "white"],          "edges": [0.08, 0.26, 0.48, 0.80] },
    "crystal":    { "colors": ["black", "blue", "lightblue", "cyan", "white"],          "edges": [0.08, 0.26, 0.48, 0.80] },
    "water":      { "colors": ["black", "blue", "lightblue", "cyan"],                   "edges": [0.08, 0.30, 0.60] },
    "lava":       { "colors": ["black", "red", "orange", "yellow", "white"],            "edges": [0.08, 0.26, 0.50, 0.84] },
    "beast":      { "colors": ["black", "blue", "purple", "lightred", "cyan"],          "edges": [0.08, 0.26, 0.48, 0.74] },
    "foliage":    { "colors": ["black", "green", "lightgreen", "yellow"],               "edges": [0.10, 0.40, 0.76] }
  },
  "teams": [
    { "name": "Gold",   "colors": ["black", "orange", "yellow"],     "edges": [0.20, 0.55] },
    { "name": "Blue",   "colors": ["black", "blue", "lightblue"],    "edges": [0.20, 0.55] },
    { "name": "Red",    "colors": ["black", "brown", "red"],         "edges": [0.20, 0.55] },
    { "name": "Green",  "colors": ["black", "green", "lightgreen"],  "edges": [0.20, 0.55] },
    { "name": "Purple", "colors": ["black", "blue", "purple"],       "edges": [0.20, 0.55] },
    { "name": "Orange", "colors": ["black", "brown", "orange"],      "edges": [0.20, 0.55] },
    { "name": "Cyan",   "colors": ["black", "lightblue", "cyan"],    "edges": [0.20, 0.55] },
    { "name": "Pink",   "colors": ["black", "red", "lightred"],      "edges": [0.20, 0.55] }
  ],
  "_exclude_comment": "Marketing art, redrawn wholesale in M6. Excluded from BOTH conversion and verification - posterising a 1024px icon to four bands would look worse than leaving it alone until it is redrawn. M6 deletes these entries.",
  "exclude": [
    "Icon.png",
    "Icon_Old.png",
    "Splash.png"
  ],
  "assign": [
    { "glob": "miners/**",                 "ramp": "flesh" },
    { "glob": "monsters/skeleton_*",       "ramp": "bone" },
    { "glob": "monsters/ghost/**",         "ramp": "spectral" },
    { "glob": "monsters/slime/**",         "ramp": "slime" },
    { "glob": "monsters/octopus/**",       "ramp": "beast" },
    { "glob": "monsters/water_snake/**",   "ramp": "water" },
    { "glob": "monsters/goat/**",          "ramp": "flesh" },
    { "glob": "tiles/**",                  "ramp": "rock" },
    { "glob": "portals/**",                "ramp": "metal" },
    { "glob": "objects/idol_*",            "ramp": "gold" },
    { "glob": "objects/prize_*",           "ramp": "gold" },
    { "glob": "objects/**",                "ramp": "metal" }
  ],
  "overrides": {
    "objects/idol_odin.png":          "stone",
    "objects/idol_ra.png":            "stone",
    "objects/idol_zeus.png":          "stone",
    "objects/idol_quetzalcoatl.png":  "stone",
    "objects/idol_vishnu.png":        "stone",
    "objects/idol_shiva.png":         "gold",
    "objects/idol_skull.png":         "bone",
    "objects/chest.png":              "wood",
    "objects/chest_boss.png":         "wood",
    "objects/treasure_chest.png":     "wood",
    "objects/item_plank.png":         "wood",
    "objects/pit_prop.png":           "wood",
    "objects/mine_cart.png":          "wood",
    "objects/stone_pile.png":         "stone",
    "objects/item_mold.png":          "slime",
    "objects/mold_patch.png":         "foliage",
    "objects/item_crystal_shard.png": "crystal",
    "objects/item_vision.png":        "crystal",
    "objects/prize_gem.png":          "crystal",
    "objects/item_blast.png":         "lava",
    "objects/charge.png":             "lava",
    "objects/item_speed.png":         "slime",
    "objects/item_lifepot.png":       "flesh",
    "objects/item_lantern.png":       "gold",
    "objects/item_detonator.png":     "metal",
    "objects/toolbox.png":            "metal",
    "objects/prize_flag.png":         "flesh",
    "tiles/singletiles/crystal_rock.png": "crystal",
    "tiles/water_floor_image.png":    "water",
    "tiles/deep_water_shallow_image.png": "water",
    "tiles/lava_wall_image.png":      "lava",
    "tiles/rail.png":                 "metal"
  }
}
```

- [ ] **Step 2: Write the failing Python tests**

Append to `tools/artpass/tests/test_palette.py`:

```python
class BandTests(unittest.TestCase):
    def test_below_first_edge_is_band_zero(self):
        self.assertEqual(0, palette.band_index(0.05, [0.10, 0.30, 0.55]))

    def test_above_last_edge_is_final_band(self):
        self.assertEqual(3, palette.band_index(0.99, [0.10, 0.30, 0.55]))

    def test_exactly_on_edge_takes_upper_band(self):
        self.assertEqual(1, palette.band_index(0.10, [0.10, 0.30, 0.55]))

    def test_middle_band(self):
        self.assertEqual(2, palette.band_index(0.40, [0.10, 0.30, 0.55]))


class RampTests(unittest.TestCase):
    def test_every_family_ramp_is_monotonic_in_luminance(self):
        pal = palette.load_palette()
        ramps = palette.load_ramps()
        for name, spec in ramps["families"].items():
            lums = [palette.luminance(pal[c]) for c in spec["colors"]]
            self.assertEqual(sorted(lums), lums, f"{name} is not monotonic: {spec['colors']}")

    def test_every_team_ramp_is_monotonic_in_luminance(self):
        pal = palette.load_palette()
        for spec in palette.load_ramps()["teams"]:
            lums = [palette.luminance(pal[c]) for c in spec["colors"]]
            self.assertEqual(sorted(lums), lums, f"{spec['name']} is not monotonic")

    def test_edge_counts_match_colour_counts(self):
        ramps = palette.load_ramps()
        for name, spec in ramps["families"].items():
            self.assertEqual(len(spec["colors"]) - 1, len(spec["edges"]), name)
        for spec in ramps["teams"]:
            self.assertEqual(len(spec["colors"]) - 1, len(spec["edges"]), spec["name"])

    def test_edges_are_ascending(self):
        ramps = palette.load_ramps()
        for name, spec in ramps["families"].items():
            self.assertEqual(sorted(spec["edges"]), spec["edges"], name)

    def test_all_ramp_tokens_resolve(self):
        pal = palette.load_palette()
        ramps = palette.load_ramps()
        for spec in list(ramps["families"].values()) + ramps["teams"]:
            for c in spec["colors"]:
                self.assertIn(c, pal)

    def test_eight_teams_with_unique_identity_colours(self):
        ramps = palette.load_ramps()
        self.assertEqual(8, len(ramps["teams"]))
        identities = [t["colors"][-1] for t in ramps["teams"]]
        self.assertEqual(8, len(set(identities)))

    def test_override_beats_glob(self):
        ramps = palette.load_ramps()
        self.assertEqual("stone", palette.ramp_for("objects/idol_odin.png", ramps))
        self.assertEqual("gold", palette.ramp_for("objects/idol_buddha.png", ramps))

    def test_first_matching_glob_wins(self):
        ramps = palette.load_ramps()
        self.assertEqual("flesh", palette.ramp_for("miners/burly/walk/e0.png", ramps))
        self.assertEqual("bone", palette.ramp_for("monsters/skeleton_human/walk/north_0.png", ramps))

    def test_flat_files_match_their_family_glob(self):
        # These sit directly in their family folder, not a subdirectory. fnmatch's '*'
        # spans '/', so 'monsters/skeleton_*' and 'miners/**' catch both shapes.
        ramps = palette.load_ramps()
        self.assertEqual("bone", palette.ramp_for("monsters/skeleton_bones_pile.png", ramps))
        self.assertEqual("flesh", palette.ramp_for("miners/miner_e.png", ramps))

    def test_unmatched_path_raises(self):
        ramps = palette.load_ramps()
        with self.assertRaises(KeyError):
            palette.ramp_for("nowhere/mystery.png", ramps)


class ExclusionTests(unittest.TestCase):
    def test_marketing_art_is_excluded(self):
        ramps = palette.load_ramps()
        self.assertTrue(palette.is_excluded("Splash.png", ramps))
        self.assertTrue(palette.is_excluded("Icon.png", ramps))

    def test_normal_assets_are_not_excluded(self):
        ramps = palette.load_ramps()
        self.assertFalse(palette.is_excluded("objects/chest.png", ramps))
```

- [ ] **Step 3: Run to verify failure**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: FAIL — `module 'tools.artpass.palette' has no attribute 'band_index'`

- [ ] **Step 4: Extend `tools/artpass/palette.py`**

Append:

```python
import fnmatch

RAMPS_PATH = os.path.normpath(os.path.join(ART_DIR, "ramps.json"))


def load_ramps(path=RAMPS_PATH):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def band_index(lum01, edges):
    """Which ramp slot a luminance falls in. A value exactly on an edge takes the
    upper band, so bands are [lo, hi)."""
    i = 0
    for e in edges:
        if lum01 >= e:
            i += 1
        else:
            break
    return i


def is_excluded(relpath, ramps):
    """True for art deliberately left unconverted (marketing art, redrawn in M6)."""
    return relpath.replace("\\", "/") in set(ramps.get("exclude", []))


def ramp_for(relpath, ramps):
    """Resolve an asset path (relative to assets/, forward slashes) to a ramp name.
    Per-file overrides beat globs; the first matching glob wins."""
    relpath = relpath.replace("\\", "/")
    override = ramps.get("overrides", {}).get(relpath)
    if override:
        return override
    for rule in ramps["assign"]:
        if fnmatch.fnmatch(relpath, rule["glob"]):
            return rule["ramp"]
    raise KeyError(f"no ramp assigned for {relpath}")


def ramp_colors(name, ramps, pal):
    """(list[(r,g,b)], list[float]) for a named family ramp."""
    spec = ramps["families"][name]
    return [pal[c] for c in spec["colors"]], spec["edges"]
```

- [ ] **Step 5: Run the Python tests**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: PASS — 24 tests

> **Note:** `fnmatch` treats `*` as matching `/` too, so `objects/idol_*` also matches nested paths. That is fine here because `assets/objects/` is flat. `miners/**` relies on the same behaviour.

- [ ] **Step 6: Write the failing C# test**

Create `src/Miner49er.Core.Tests/Art/ColorRampTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class ColorRampTests
{
    private static ColorRamp ThreeTone() =>
        new(new[] { C64Palette.Black, C64Palette.Orange, C64Palette.Yellow },
            new[] { 0.20f, 0.55f });

    [Fact]
    public void Darkest_luminance_maps_to_first_colour()
    {
        Assert.Equal(C64Palette.Black, ThreeTone().Map(0.0f));
    }

    [Fact]
    public void Brightest_luminance_maps_to_last_colour()
    {
        Assert.Equal(C64Palette.Yellow, ThreeTone().Map(1.0f));
    }

    [Fact]
    public void Value_on_an_edge_takes_the_upper_band()
    {
        Assert.Equal(C64Palette.Orange, ThreeTone().Map(0.20f));
    }

    [Fact]
    public void Identity_is_the_brightest_colour()
    {
        Assert.Equal(C64Palette.Yellow, ThreeTone().Identity);
    }

    [Fact]
    public void Eight_team_ramps_exist()
    {
        Assert.Equal(8, C64Ramps.Teams.Length);
    }

    [Fact]
    public void Team_identity_colours_are_unique()
    {
        var ids = new HashSet<Rgb>();
        foreach (var t in C64Ramps.Teams) Assert.True(ids.Add(t.Identity));
        Assert.Equal(8, ids.Count);
    }

    [Fact]
    public void Every_ramp_is_monotonic_in_luminance()
    {
        foreach (var ramp in C64Ramps.Teams)
            for (int i = 1; i < ramp.Colors.Length; i++)
                Assert.True(ramp.Colors[i - 1].Luminance <= ramp.Colors[i].Luminance);
    }

    [Fact]
    public void Map_only_ever_returns_palette_colours()
    {
        var palette = new HashSet<Rgb>(C64Palette.All);
        foreach (var ramp in C64Ramps.Teams)
            for (float l = 0f; l <= 1f; l += 0.01f)
                Assert.Contains(ramp.Map(l), palette);
    }
}
```

- [ ] **Step 7: Run to verify failure**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: FAIL — `ColorRamp` / `C64Ramps` do not exist

- [ ] **Step 8: Write `src/Miner49er.Core/Art/ColorRamp.cs`**

```csharp
using System;

namespace Miner49er.Core;

/// <summary>An authored dark-to-light run of C64 palette colours, plus the absolute
/// luminance thresholds that divide them. Mapping by fixed thresholds (rather than each
/// image's own brightness range) is what keeps animation frames from shimmering.</summary>
public sealed class ColorRamp
{
    public readonly Rgb[] Colors;

    /// <summary>Ascending luminance thresholds; always <c>Colors.Length - 1</c> of them.</summary>
    public readonly float[] Edges;

    public ColorRamp(Rgb[] colors, float[] edges)
    {
        if (colors.Length < 2)
            throw new ArgumentException("a ramp needs at least two colours", nameof(colors));
        if (edges.Length != colors.Length - 1)
            throw new ArgumentException(
                $"expected {colors.Length - 1} edges for {colors.Length} colours, got {edges.Length}",
                nameof(edges));

        Colors = colors;
        Edges = edges;
    }

    /// <summary>The brightest colour — what identifies this ramp to a player.</summary>
    public Rgb Identity => Colors[Colors.Length - 1];

    /// <summary>Map a 0.0-1.0 luminance to its band. A value exactly on an edge takes
    /// the upper band, matching tools/artpass/palette.py:band_index.</summary>
    public Rgb Map(float luminance)
    {
        int i = 0;
        foreach (var e in Edges)
        {
            if (luminance >= e) i++;
            else break;
        }
        return Colors[i];
    }
}
```

- [ ] **Step 9: Extend the generator to emit `C64Ramps.g.cs`**

Append to `tools/artpass/gen_palette_cs.py`, above `main()`:

```python
RAMPS_OUT_PATH = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "src", "Miner49er.Core", "Art", "C64Ramps.g.cs"))


def render_ramps(ramps):
    def ramp_expr(spec):
        colors = ", ".join(f"C64Palette.{CS_NAMES[c]}" for c in spec["colors"])
        edges = ", ".join(f"{e}f" for e in spec["edges"])
        return f"new(new[] {{ {colors} }}, new[] {{ {edges} }})"

    lines = [
        "// <auto-generated>",
        "//     Generated by tools/artpass/gen_palette_cs.py from art/ramps.json.",
        "//     Do not edit by hand - edit the JSON and regenerate.",
        "// </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace Miner49er.Core;",
        "",
        "/// <summary>Authored colour ramps: the art direction, as code.</summary>",
        "public static class C64Ramps",
        "{",
        "    /// <summary>Per-player ramps, indexed by lobby colour selection.</summary>",
        "    public static readonly ColorRamp[] Teams =",
        "    {",
    ]
    for spec in ramps["teams"]:
        lines.append(f"        {ramp_expr(spec)}, // {spec['name']}")
    lines.append("    };")
    lines.append("")
    lines.append("    public static readonly string[] TeamNames =")
    lines.append("    {")
    lines.append("        " + ", ".join(f'"{t["name"]}"' for t in ramps["teams"]))
    lines.append("    };")
    lines.append("")
    lines.append("    private static readonly Dictionary<string, ColorRamp> Families = new()")
    lines.append("    {")
    for name, spec in ramps["families"].items():
        lines.append(f'        ["{name}"] = {ramp_expr(spec)},')
    lines.append("    };")
    lines.append("")
    lines.append("    /// <summary>Look a family ramp up by its JSON name.</summary>")
    lines.append("    public static ColorRamp ByName(string name) => Families[name];")
    lines.append("}")
    return "\n".join(lines) + "\n"
```

Then replace `main()` with:

```python
def main():
    outputs = [
        (OUT_PATH, render(palette.load_palette())),
        (RAMPS_OUT_PATH, render_ramps(palette.load_ramps())),
    ]
    if "--check" in sys.argv:
        stale = []
        for path, text in outputs:
            current = open(path, encoding="utf-8").read() if os.path.exists(path) else ""
            if current != text:
                stale.append(os.path.basename(path))
        if stale:
            print(f"STALE: {', '.join(stale)} - regenerate", file=sys.stderr)
            return 1
        print("OK: generated C# is current")
        return 0
    for path, text in outputs:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        print(f"wrote {path}")
    return 0
```

- [ ] **Step 10: Extend the drift guard**

In `tools/artpass/tests/test_palette.py`, replace `GeneratorTests` with:

```python
class GeneratorTests(unittest.TestCase):
    def test_generated_palette_cs_is_current(self):
        from tools.artpass import gen_palette_cs
        expected = gen_palette_cs.render(palette.load_palette())
        with open(gen_palette_cs.OUT_PATH, encoding="utf-8") as fh:
            self.assertEqual(expected, fh.read(),
                             "C64Palette.g.cs is stale - run python -m tools.artpass.gen_palette_cs")

    def test_generated_ramps_cs_is_current(self):
        from tools.artpass import gen_palette_cs
        expected = gen_palette_cs.render_ramps(palette.load_ramps())
        with open(gen_palette_cs.RAMPS_OUT_PATH, encoding="utf-8") as fh:
            self.assertEqual(expected, fh.read(),
                             "C64Ramps.g.cs is stale - run python -m tools.artpass.gen_palette_cs")
```

- [ ] **Step 11: Regenerate and run both suites**

```bash
python -m tools.artpass.gen_palette_cs
python -m unittest discover -s tools/artpass/tests -t .
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo
```
Expected: 25 Python tests pass; **942 xunit tests pass** (934 + 8)

- [ ] **Step 12: Commit**

```bash
git add art/ramps.json tools/artpass src/Miner49er.Core/Art src/Miner49er.Core.Tests/Art
git commit -m "feat(art): authored colour ramps with fixed luminance bands"
```

---

## Task 3: Pure team-tint function

**Files:**
- Create: `src/Miner49er.Core/Art/MinerTint.cs`
- Create: `src/Miner49er.Core.Tests/Art/MinerTintTests.cs`

**Interfaces:**
- Consumes: `Rgb`, `ColorRamp`, `C64Palette`, `C64Ramps` (Tasks 1–2)
- Produces: `Miner49er.Core.MinerTint.Apply(Rgb source, ColorRamp teamRamp) -> Rgb` and `MinerTint.IsHuePreserved(Rgb) -> bool`

**Why this exists:** `MatchClient.TintMiner` currently does `tint.R * (lum * 0.6 + 0.4)`, a continuous multiply that emits arbitrary RGB. That would put off-palette miners on screen no matter how clean the source PNGs are. Replacing it with a band lookup keeps output in-palette and matches how real C64 multicolor sprites worked. The pixel maths lives in Core so it can be tested without Godot; the `Image` iteration stays in `game/`.

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/Art/MinerTintTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class MinerTintTests
{
    private static ColorRamp Gold => C64Ramps.Teams[0];

    [Fact]
    public void Hardhat_yellow_is_preserved_not_tinted()
    {
        // Warm hue, saturated, bright: the hard hat and skin must survive tinting.
        var hardhat = new Rgb(0xED, 0xC3, 0x4A);
        Assert.True(MinerTint.IsHuePreserved(hardhat));
        Assert.Equal(hardhat, MinerTint.Apply(hardhat, C64Ramps.Teams[1]));
    }

    [Fact]
    public void Dark_clothing_takes_the_team_colour()
    {
        var overalls = new Rgb(0x30, 0x30, 0x38);
        Assert.False(MinerTint.IsHuePreserved(overalls));
        Assert.Equal(Gold.Map(overalls.Luminance), MinerTint.Apply(overalls, Gold));
    }

    [Fact]
    public void Cool_hues_are_never_preserved()
    {
        Assert.False(MinerTint.IsHuePreserved(new Rgb(0x40, 0x90, 0xE0)));
    }

    [Fact]
    public void Dark_warm_pixels_are_not_preserved()
    {
        // Warm and saturated but too dark - this is shadowed cloth, not skin.
        Assert.False(MinerTint.IsHuePreserved(new Rgb(0x40, 0x28, 0x10)));
    }

    [Fact]
    public void Output_is_always_in_palette_for_every_team()
    {
        var palette = new HashSet<Rgb>(C64Palette.All);
        foreach (var ramp in C64Ramps.Teams)
            for (int v = 0; v < 256; v += 5)
            {
                var result = MinerTint.Apply(new Rgb(v, v, v), ramp);
                Assert.Contains(result, palette);
            }
    }

    [Fact]
    public void Tinting_is_deterministic()
    {
        var px = new Rgb(0x55, 0x55, 0x60);
        Assert.Equal(MinerTint.Apply(px, Gold), MinerTint.Apply(px, Gold));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: FAIL — `MinerTint` does not exist

- [ ] **Step 3: Write `src/Miner49er.Core/Art/MinerTint.cs`**

```csharp
namespace Miner49er.Core;

/// <summary>Recolours a miner sprite pixel into a team's ramp.
///
/// Replaces the old continuous multiply in MatchClient.TintMiner, which produced
/// arbitrary RGB and so broke the 16-colour guarantee at runtime. Banding into an
/// authored ramp keeps every output pixel in-palette.</summary>
public static class MinerTint
{
    // Warm hue, reasonably saturated, and bright: skin and the yellow hard hat.
    // Thresholds carried over unchanged from the original TintMiner so the variants'
    // lighter, less-saturated skin tones keep their colour.
    private const float MaxWarmHue = 0.14f;
    private const float MinSaturation = 0.18f;
    private const float MinLuminance = 0.5f;

    /// <summary>True when a pixel keeps its own colour instead of taking the team's.</summary>
    public static bool IsHuePreserved(Rgb px) =>
        px.Hue < MaxWarmHue && px.Saturation > MinSaturation && px.Luminance > MinLuminance;

    /// <summary>Map one sprite pixel into the team ramp, preserving skin and hard hat.</summary>
    public static Rgb Apply(Rgb source, ColorRamp teamRamp) =>
        IsHuePreserved(source) ? source : teamRamp.Map(source.Luminance);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS — **948 tests** (942 + 6)

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Art/MinerTint.cs src/Miner49er.Core.Tests/Art/MinerTintTests.cs
git commit -m "feat(art): in-palette team tint as a pure Core function"
```

---

## Task 4: The posteriser

**Files:**
- Create: `tools/artpass/convert.py`
- Create: `tools/artpass/verify.py`
- Create: `tools/artpass/tests/test_convert.py`

**Interfaces:**
- Consumes: `palette.load_palette`, `load_ramps`, `luminance`, `band_index`, `ramp_for`, `ramp_colors` (Tasks 1–2)
- Produces: `convert.posterize_image(img, colors, edges) -> PIL.Image` (RGBA); `convert.convert_tree(src_root, dst_root, ...) -> (converted, skipped)`; `verify.check_tree(root) -> list[str]` (violation messages, empty when clean)

- [ ] **Step 1: Write the failing test**

Create `tools/artpass/tests/test_convert.py`:

```python
import unittest

from PIL import Image

from tools.artpass import convert, palette


def solid(size, rgba):
    return Image.new("RGBA", size, rgba)


def pixels(img):
    """Flat list of RGBA tuples. Avoids Image.getdata(), deprecated in Pillow 12."""
    px = img.load()
    return [px[x, y] for y in range(img.height) for x in range(img.width)]


class PosterizeTests(unittest.TestCase):
    def setUp(self):
        pal = palette.load_palette()
        self.colors = [pal["black"], pal["orange"], pal["yellow"]]
        self.edges = [0.20, 0.55]

    def test_transparent_pixels_stay_transparent(self):
        out = convert.posterize_image(solid((2, 2), (200, 200, 200, 0)), self.colors, self.edges)
        self.assertEqual((0, 0, 0, 0), out.getpixel((0, 0)))

    def test_dark_pixel_takes_first_ramp_colour(self):
        out = convert.posterize_image(solid((1, 1), (10, 10, 10, 255)), self.colors, self.edges)
        self.assertEqual(self.colors[0] + (255,), out.getpixel((0, 0)))

    def test_bright_pixel_takes_last_ramp_colour(self):
        out = convert.posterize_image(solid((1, 1), (250, 250, 250, 255)), self.colors, self.edges)
        self.assertEqual(self.colors[2] + (255,), out.getpixel((0, 0)))

    def test_alpha_is_forced_binary(self):
        img = Image.new("RGBA", (2, 1))
        img.putpixel((0, 0), (200, 200, 200, 127))  # below threshold
        img.putpixel((1, 0), (200, 200, 200, 128))  # at threshold
        out = convert.posterize_image(img, self.colors, self.edges)
        self.assertEqual(0, out.getpixel((0, 0))[3])
        self.assertEqual(255, out.getpixel((1, 0))[3])

    def test_output_only_uses_ramp_colours(self):
        img = Image.new("RGBA", (16, 16))
        for y in range(16):
            for x in range(16):
                img.putpixel((x, y), (x * 16, y * 16, (x + y) * 8, 255))
        out = convert.posterize_image(img, self.colors, self.edges)
        used = {p[:3] for p in pixels(out) if p[3]}
        self.assertTrue(used.issubset(set(self.colors)), used)

    def test_is_idempotent(self):
        img = Image.new("RGBA", (8, 8))
        for y in range(8):
            for x in range(8):
                img.putpixel((x, y), (x * 32, y * 32, 128, 255))
        once = convert.posterize_image(img, self.colors, self.edges)
        twice = convert.posterize_image(once, self.colors, self.edges)
        self.assertEqual(pixels(once), pixels(twice))


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run to verify it fails**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: FAIL — `No module named 'tools.artpass.convert'`

- [ ] **Step 3: Write `tools/artpass/convert.py`**

```python
"""Posterise art/source/**.png into assets/**.png using authored family ramps.

Deterministic and idempotent: the same source plus the same ramps always yields the
same bytes. Reads only art/source, writes only assets - so ramp tuning is repeatable.
Posterising is lossy, which is exactly why the originals must be kept.
"""
import os
import sys

from PIL import Image

from tools.artpass import palette

ALPHA_THRESHOLD = 128

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC_ROOT = os.path.join(ROOT, "art", "source")
DST_ROOT = os.path.join(ROOT, "assets")


def posterize_image(img, colors, edges):
    """Map every opaque pixel's luminance into the ramp; force alpha binary."""
    img = img.convert("RGBA")
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    src = img.load()
    dst = out.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = src[x, y]
            if a < ALPHA_THRESHOLD:
                continue
            i = palette.band_index(palette.luminance((r, g, b)), edges)
            cr, cg, cb = colors[i]
            dst[x, y] = (cr, cg, cb, 255)
    return out


def convert_tree(src_root=SRC_ROOT, dst_root=DST_ROOT, pal=None, ramps=None, verbose=True):
    """Convert every PNG under src_root into dst_root, mirroring paths."""
    pal = pal or palette.load_palette()
    ramps = ramps or palette.load_ramps()
    converted, skipped = 0, []

    for dirpath, _dirs, files in os.walk(src_root):
        for fn in files:
            if not fn.lower().endswith(".png"):
                continue
            abs_src = os.path.join(dirpath, fn)
            rel = os.path.relpath(abs_src, src_root).replace("\\", "/")
            if palette.is_excluded(rel, ramps):
                continue
            try:
                ramp_name = palette.ramp_for(rel, ramps)
            except KeyError:
                skipped.append(rel)
                continue

            colors, edges = palette.ramp_colors(ramp_name, ramps, pal)
            out = posterize_image(Image.open(abs_src), colors, edges)

            abs_dst = os.path.join(dst_root, rel.replace("/", os.sep))
            os.makedirs(os.path.dirname(abs_dst), exist_ok=True)
            out.save(abs_dst)
            converted += 1
            if verbose:
                print(f"  {rel} -> {ramp_name}")

    return converted, skipped


def main():
    converted, skipped = convert_tree(verbose="--quiet" not in sys.argv)
    print(f"\nconverted {converted} file(s)")
    if skipped:
        print(f"SKIPPED {len(skipped)} file(s) with no ramp assignment:", file=sys.stderr)
        for rel in skipped:
            print(f"  {rel}", file=sys.stderr)
        return 1

    from tools.artpass import verify
    violations = verify.check_tree(DST_ROOT)
    if violations:
        print(f"\nVERIFY FAILED - {len(violations)} violation(s):", file=sys.stderr)
        for v in violations[:20]:
            print(f"  {v}", file=sys.stderr)
        return 1
    print("verify: all output in-palette with binary alpha")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 4: Write `tools/artpass/verify.py`**

```python
"""Assert that every shipped asset obeys the palette contract.

This is the permanent guard: it is what stops cohesion decaying the next time a
feature phase adds art. convert.py runs it automatically over its own output.
"""
import os
import sys

from PIL import Image

from tools.artpass import palette

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS_ROOT = os.path.join(ROOT, "assets")


def check_image(path, legal):
    """Return a list of human-readable violations for one PNG."""
    problems = []
    img = Image.open(path).convert("RGBA")
    px = img.load()
    bad_colors = set()
    soft_alpha = 0
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a not in (0, 255):
                soft_alpha += 1
            elif a == 255 and (r, g, b) not in legal:
                bad_colors.add((r, g, b))

    if soft_alpha:
        problems.append(f"{path}: {soft_alpha} pixel(s) with non-binary alpha")
    for c in sorted(bad_colors):
        problems.append(f"{path}: off-palette colour #{c[0]:02X}{c[1]:02X}{c[2]:02X}")
    return problems


def check_tree(root=ASSETS_ROOT, pal=None, ramps=None):
    """Every asset must be in-palette, except art explicitly excluded in ramps.json."""
    legal = set((pal or palette.load_palette()).values())
    ramps = ramps or palette.load_ramps()
    problems = []
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            if not fn.lower().endswith(".png"):
                continue
            abs_path = os.path.join(dirpath, fn)
            rel = os.path.relpath(abs_path, root).replace("\\", "/")
            if palette.is_excluded(rel, ramps):
                continue
            problems.extend(check_image(abs_path, legal))
    return problems


def main():
    problems = check_tree()
    if problems:
        print(f"FAIL - {len(problems)} violation(s):", file=sys.stderr)
        for p in problems:
            print(f"  {p}", file=sys.stderr)
        return 1
    print("OK: every asset is in-palette with binary alpha")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 5: Run the tests**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: PASS — 31 tests

- [ ] **Step 6: Commit**

```bash
git add tools/artpass/convert.py tools/artpass/verify.py tools/artpass/tests/test_convert.py
git commit -m "feat(art): ramp posteriser and palette verifier"
```

---

## Task 5: Snapshot originals and convert all 826 assets (M2)

**Files:**
- Create: `art/source/**.png` (826 files, copied)
- Modify: `assets/**.png` (826 files, regenerated)
- Create: `tools/artpass/contact_sheet.py`
- Create: `tools/artpass/tests/test_assets.py`

**Interfaces:**
- Consumes: `convert.convert_tree`, `verify.check_tree` (Task 4)
- Produces: `art/source/` as the permanent conversion input

> **This task is destructive to `assets/`.** `art/source/` must be committed *before* `assets/` is overwritten — that commit is the only thing making ramp tuning repeatable. Do not reorder these steps.

- [ ] **Step 1: Snapshot the originals and commit them first**

```bash
python -c "
import shutil, os
src, dst = 'assets', 'art/source'
n = 0
for dirpath, dirs, files in os.walk(src):
    for fn in files:
        if not fn.lower().endswith('.png'): continue
        rel = os.path.relpath(os.path.join(dirpath, fn), src)
        out = os.path.join(dst, rel)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        shutil.copy2(os.path.join(dirpath, fn), out)
        n += 1
print('copied', n)
"
git add art/source
git commit -m "chore(art): snapshot pre-conversion assets as converter input"
```
Expected: `copied 826`

- [ ] **Step 2: Verify the snapshot is complete before touching anything**

```bash
python -c "
import os
c=lambda r:sum(1 for d,_,f in os.walk(r) for n in f if n.lower().endswith('.png'))
a,s=c('assets'),c('art/source')
print('assets',a,'source',s)
assert a==s, 'snapshot incomplete - STOP'
print('snapshot OK')
"
```
Expected: `assets 826 source 826` then `snapshot OK`

- [ ] **Step 3: Write the failing asset-compliance test**

Create `tools/artpass/tests/test_assets.py`:

```python
import os
import unittest

from tools.artpass import verify


class AssetComplianceTests(unittest.TestCase):
    """The permanent guard from the spec: no asset may ship an off-palette pixel."""

    def test_every_asset_is_in_palette_with_binary_alpha(self):
        problems = verify.check_tree()
        self.assertEqual([], problems[:20], f"{len(problems)} violation(s)")

    def test_exclusion_list_has_not_grown(self):
        """Exclusions are a deliberate M6 deferral, not a place to hide failures."""
        from tools.artpass import palette
        self.assertEqual(
            {"Icon.png", "Icon_Old.png", "Splash.png"},
            set(palette.load_ramps().get("exclude", [])))

    def test_asset_and_source_trees_have_the_same_files(self):
        def rels(root):
            return {
                os.path.relpath(os.path.join(d, n), root).replace("\\", "/")
                for d, _dirs, files in os.walk(root) for n in files
                if n.lower().endswith(".png")
            }
        self.assertEqual(rels("art/source"), rels("assets"))


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 4: Run it to confirm it fails against unconverted assets**

Run: `python -m unittest discover -s tools/artpass/tests -t .`
Expected: FAIL — the current assets are full of off-palette colours

- [ ] **Step 5: Write the contact-sheet reviewer**

Create `tools/artpass/contact_sheet.py`:

```python
"""Before/after contact sheets, so a human can actually judge the conversion."""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
CELL = 112
PAD = 16


def sheet(rel_paths, out_path, src_root="art/source", dst_root="assets", cols=10):
    rel_paths = [p for p in rel_paths
                 if os.path.exists(os.path.join(ROOT, src_root, p))
                 and os.path.exists(os.path.join(ROOT, dst_root, p))]
    if not rel_paths:
        print("nothing to draw", file=sys.stderr)
        return

    rows = (len(rel_paths) + cols - 1) // cols
    w = cols * CELL
    h = rows * (2 * CELL + PAD + 12)
    img = Image.new("RGBA", (w, h), (28, 28, 34, 255))
    d = ImageDraw.Draw(img)

    for i, rel in enumerate(rel_paths):
        cx, cy = (i % cols) * CELL, (i // cols) * (2 * CELL + PAD + 12)
        for row, root in ((0, src_root), (1, dst_root)):
            im = Image.open(os.path.join(ROOT, root, rel)).convert("RGBA")
            s = min(CELL / im.width, CELL / im.height)
            im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.NEAREST)
            y = cy + 12 + row * CELL
            img.alpha_composite(im, (cx + (CELL - im.width) // 2, y + (CELL - im.height) // 2))
        d.text((cx + 2, cy + 1), os.path.basename(rel)[:17], fill=(215, 215, 225, 255))

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    img.convert("RGB").save(out_path)
    print(f"wrote {out_path}  ({len(rel_paths)} pairs, top row = original)")


def main():
    import glob
    groups = {
        "objects": "assets/objects/*.png",
        "tiles": "assets/tiles/**/*.png",
        "monsters": "assets/monsters/*/*.png",
        "miners": "assets/miners/*/idle/*.png",
    }
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "art/review"
    for name, pattern in groups.items():
        rels = sorted(
            os.path.relpath(p, os.path.join(ROOT, "assets")).replace("\\", "/")
            for p in glob.glob(os.path.join(ROOT, pattern), recursive=True))
        sheet(rels, os.path.join(out_dir, f"{name}.png"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 6: Run the conversion**

```bash
python -m tools.artpass.convert --quiet
```
Expected: `converted 823 file(s)` (826 minus the 3 excluded marketing files) then `verify: all output in-palette with binary alpha`, exit 0.
If any file is reported as SKIPPED, add a rule to `art/ramps.json` and re-run — the converter is idempotent, so re-running is always safe.

- [ ] **Step 7: Run all tests**

```bash
python -m unittest discover -s tools/artpass/tests -t .
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo
```
Expected: 34 Python tests pass; 948 xunit tests pass

- [ ] **Step 8: Generate contact sheets and review them**

```bash
python -m tools.artpass.contact_sheet art/review
```
Then **open `art/review/objects.png`, `tiles.png`, `monsters.png`, `miners.png`** and check each: the gold idols still read gold, stone idols still read grey, miners keep hardhat and skin, crystals read cyan. If a family is wrong, adjust that ramp's `edges` in `art/ramps.json`, re-run Step 6, and look again. `art/review/` is scratch output — do not commit it.

- [ ] **Step 9: Add `art/review/` to .gitignore**

Append to `.gitignore`:

```
# Art-pass review sheets (regenerate with python -m tools.artpass.contact_sheet)
art/review/
```

- [ ] **Step 10: Commit the conversion**

```bash
git add assets .gitignore tools/artpass/contact_sheet.py tools/artpass/tests/test_assets.py
git commit -m "feat(art): convert all 826 assets to the C64 palette"
```

- [ ] **Step 11: PLAYTEST GATE**

Run the game (`godot` via **PowerShell only** — the Bash shim breaks headless mode) and confirm: terrain reads, miners are recognisable, items are distinguishable, nothing is invisible against its background. Note anything wrong as a ramp-edge adjustment — that is a data change, not a code change.

---

## Task 6: Godot adapter, team ramps, and rewired tint (M3a)

**Files:**
- Create: `game/art/C64Colors.cs`
- Modify: `game/net/PlayerColors.cs` (full rewrite, 21 lines)
- Modify: `game/net/MatchClient.cs:856-880` (tint cache) and `:882-901` (`TintMiner`)

**Interfaces:**
- Consumes: `C64Palette`, `C64Ramps`, `ColorRamp`, `MinerTint` (Tasks 1–3)
- Produces: `Miner49er.C64Colors.From(Rgb) -> Godot.Color`, plus 16 named `Color` fields (`C64Colors.Black`, `.Yellow`, …); `PlayerColors.RampAt(int) -> ColorRamp`, `PlayerColors.At(int) -> Color` (unchanged signature), `PlayerColors.Palette -> Color[]` (unchanged shape)

> `game/` uses **tabs**, not spaces. Match the surrounding file.

- [ ] **Step 1: Write `game/art/C64Colors.cs`**

```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Godot-side view of the C64 palette. The palette itself lives in
/// Miner49er.Core so it can be unit-tested without an engine; this is the thin
/// adapter that turns it into Godot Colors.</summary>
public static class C64Colors
{
	public static Color From(Rgb c) => Color.Color8(c.R, c.G, c.B);

	public static readonly Color Black      = From(C64Palette.Black);
	public static readonly Color White      = From(C64Palette.White);
	public static readonly Color Red        = From(C64Palette.Red);
	public static readonly Color Cyan       = From(C64Palette.Cyan);
	public static readonly Color Purple     = From(C64Palette.Purple);
	public static readonly Color Green      = From(C64Palette.Green);
	public static readonly Color Blue       = From(C64Palette.Blue);
	public static readonly Color Yellow     = From(C64Palette.Yellow);
	public static readonly Color Orange     = From(C64Palette.Orange);
	public static readonly Color Brown      = From(C64Palette.Brown);
	public static readonly Color LightRed   = From(C64Palette.LightRed);
	public static readonly Color DarkGrey   = From(C64Palette.DarkGrey);
	public static readonly Color Grey       = From(C64Palette.Grey);
	public static readonly Color LightGreen = From(C64Palette.LightGreen);
	public static readonly Color LightBlue  = From(C64Palette.LightBlue);
	public static readonly Color LightGrey  = From(C64Palette.LightGrey);
}
```

- [ ] **Step 2: Rewrite `game/net/PlayerColors.cs`**

```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Eight preset miner colours, indexed by lobby selection.
///
/// Each team is a three-step C64 ramp rather than a single colour: sprites are banded
/// into the ramp (see <see cref="MinerTint"/>) so tinted miners stay inside the
/// 16-colour palette. <see cref="At"/> returns the ramp's brightest colour, which is
/// what identifies the team in menus, the scoreboard and the minimap.</summary>
public static class PlayerColors
{
	public static readonly string[] Names = C64Ramps.TeamNames;

	/// <summary>Identity colour per team - the brightest band of each ramp.</summary>
	public static readonly Color[] Palette = BuildPalette();

	private static Color[] BuildPalette()
	{
		var colors = new Color[C64Ramps.Teams.Length];
		for (int i = 0; i < colors.Length; i++)
			colors[i] = C64Colors.From(C64Ramps.Teams[i].Identity);
		return colors;
	}

	private static int Wrap(int index) =>
		((index % C64Ramps.Teams.Length) + C64Ramps.Teams.Length) % C64Ramps.Teams.Length;

	public static Color At(int index) => Palette[Wrap(index)];

	/// <summary>The full ramp for a team, for sprite tinting.</summary>
	public static ColorRamp RampAt(int index) => C64Ramps.Teams[Wrap(index)];
}
```

- [ ] **Step 3: Rewrite `TintMiner` in `game/net/MatchClient.cs`**

Replace the whole method at `game/net/MatchClient.cs:882-901` with:

```csharp
	private static Image TintMiner(Image src, ColorRamp ramp)
	{
		var img = (Image)src.Duplicate();
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var px = img.GetPixel(x, y);
				if (px.A < 0.05f) continue;
				var source = new Rgb((byte)(px.R * 255f), (byte)(px.G * 255f), (byte)(px.B * 255f));
				var mapped = MinerTint.Apply(source, ramp);
				img.SetPixel(x, y, new Color(mapped.R / 255f, mapped.G / 255f, mapped.B / 255f, px.A));
			}
		return img;
	}
```

- [ ] **Step 4: Update the tint cache call sites**

At `game/net/MatchClient.cs:856`, replace `var tint = PlayerColors.At(color);` with:

```csharp
		var ramp = PlayerColors.RampAt(color);
```

Then in the same method (lines ~860–867) replace every `tint` argument with `ramp`. There are **7 call sites** — `Facing[d]`, `Listen[d]`, `Walk[d, f]`, `Mine[d, f]`, `Plant[d, f]`, `Throw[d, f]`, `Idle[i]` — each inside a loop, so every `TintMiner(src.X, tint)` becomes `TintMiner(src.X, ramp)`.

Confirm none are missed:

```bash
grep -n "TintMiner(" game/net/MatchClient.cs
```
Expected: every hit passes `ramp`, none passes `tint`.

- [ ] **Step 5: Add the Core using directive if absent**

Check the top of `game/net/MatchClient.cs` for `using Miner49er.Core;`. If missing, add it after the existing `using Godot;`.

- [ ] **Step 6: Build**

```bash
dotnet build Miner49er.csproj --nologo
```
Expected: build succeeds, 0 errors.

- [ ] **Step 7: Run the Core tests**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS — 948 tests

- [ ] **Step 8: Commit**

```bash
git add game/art/C64Colors.cs game/net/PlayerColors.cs game/net/MatchClient.cs
git commit -m "feat(art): team colours as C64 ramps, in-palette miner tinting"
```

---

## Task 7: Replace colour literals in WorldRenderer (M3b)

**Files:**
- Create: `tools/artpass/map_code_colors.py`
- Create: `tools/artpass/check_code_palette.py`
- Modify: `game/WorldRenderer.cs` (98 colour sites)

**Interfaces:**
- Consumes: `C64Colors` (Task 6), `palette.load_palette` (Task 1)
- Produces: `map_code_colors.suggest(rgb) -> str` (a `C64Colors.X` token name)

**Why a helper:** there are 93 distinct RGB triples across 162 sites. Choosing each token by hand is slow and inconsistent; the helper proposes a token per literal using the same saturation-aware rule the art direction uses, and a human accepts or overrides each one.

**HDR constraint:** some literals have components **above 1.0** (e.g. `new Color(1.12f, 0.74f, 0.68f)`). These are deliberate bloom drivers. They must be converted as `C64Colors.X * k` where `k > 1.0f` preserves the over-range magnitude — **never** clamped to a plain palette token, or the glow the design deliberately kept will disappear.

- [ ] **Step 1: Write the suggestion helper**

Create `tools/artpass/map_code_colors.py`:

```python
"""Suggest a C64Colors token for every colour literal in game/.

Advisory, not automatic: it prints a table for a human to apply and override.
Literals with a component above 1.0 are HDR bloom drivers - it reports the scale
factor to keep rather than proposing a flat token.
"""
import collections
import colorsys
import glob
import math
import re
import sys

from tools.artpass import palette

CS_NAMES = {
    "black": "Black", "white": "White", "red": "Red", "cyan": "Cyan",
    "purple": "Purple", "green": "Green", "blue": "Blue", "yellow": "Yellow",
    "orange": "Orange", "brown": "Brown", "lightred": "LightRed",
    "darkgrey": "DarkGrey", "grey": "Grey", "lightgreen": "LightGreen",
    "lightblue": "LightBlue", "lightgrey": "LightGrey",
}

PATTERN = re.compile(
    r'new Color\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*(?:,\s*([0-9.]+)f?\s*)?\)')


def _hls(rgb):
    r, g, b = [v / 255.0 for v in rgb]
    return colorsys.rgb_to_hls(r, g, b)


def _cost(src, cand):
    """Prefer keeping hue and saturation over minimising raw RGB distance."""
    hs, ls, ss = _hls(src)
    hc, lc, sc = _hls(cand)
    base = math.dist(src, cand) / 441.7
    cost = base + abs(ss - sc) * 1.5 + abs(ls - lc) * 1.2
    if ss > 0.18:
        dh = abs(hs - hc)
        cost += min(dh, 1 - dh) * (3.0 if sc > 0.10 else 0.0)
        if sc <= 0.10:
            cost += 1.0
    return cost


def suggest(rgb, pal=None):
    """Best C64Colors token name for an 8-bit RGB triple."""
    pal = pal or palette.load_palette()
    best = min(pal.items(), key=lambda kv: _cost(rgb, kv[1]))
    return CS_NAMES[best[0]]


def main():
    targets = sys.argv[1:] or sorted(glob.glob("game/**/*.cs", recursive=True))
    pal = palette.load_palette()
    rows = collections.Counter()
    hdr = []

    for path in targets:
        with open(path, encoding="utf-8", errors="replace") as fh:
            text = fh.read()
        for m in PATTERN.finditer(text):
            r, g, b = (float(m.group(i)) for i in (1, 2, 3))
            if max(r, g, b) > 1.0:
                k = max(r, g, b)
                token = suggest((int(r / k * 255), int(g / k * 255), int(b / k * 255)), pal)
                hdr.append((path, m.group(0), token, round(k, 3)))
            else:
                rows[((round(r, 3), round(g, 3), round(b, 3)), suggest(
                    (int(r * 255), int(g * 255), int(b * 255)), pal))] += 1

    print(f"{'literal':28s} {'n':>3s}  suggested token")
    for ((r, g, b), token), n in sorted(rows.items(), key=lambda kv: -kv[1]):
        print(f"{f'({r}f, {g}f, {b}f)':28s} {n:3d}  C64Colors.{token}")

    if hdr:
        print(f"\nHDR bloom drivers - keep them over-range ({len(hdr)}):")
        for path, lit, token, k in hdr:
            print(f"  {path}: {lit}  ->  C64Colors.{token} * {k}f")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Write the code guard**

Create `tools/artpass/check_code_palette.py`:

```python
"""Fail if any raw colour literal survives outside the palette adapter."""
import glob
import re
import sys

PATTERN = re.compile(r'new Color\(\s*[0-9]')
ALLOWED = {"game/art/C64Colors.cs"}


def offenders(paths=None):
    paths = paths or sorted(glob.glob("game/**/*.cs", recursive=True))
    out = []
    for path in paths:
        if path.replace("\\", "/") in ALLOWED:
            continue
        with open(path, encoding="utf-8", errors="replace") as fh:
            for n, line in enumerate(fh, 1):
                if PATTERN.search(line):
                    out.append(f"{path}:{n}: {line.strip()}")
    return out


def main():
    bad = offenders()
    if bad:
        print(f"FAIL - {len(bad)} raw colour literal(s) remain:", file=sys.stderr)
        for b in bad:
            print(f"  {b}", file=sys.stderr)
        return 1
    print("OK: no raw colour literals outside C64Colors.cs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 3: Generate the suggestion table for WorldRenderer**

```bash
python -m tools.artpass.map_code_colors game/WorldRenderer.cs
```
Expected: a table of literals with a suggested `C64Colors.X` token for each, plus an HDR section listing any over-range literals with their scale factor.

- [ ] **Step 4: Apply the tokens to `game/WorldRenderer.cs`**

Work through the file's 98 sites, replacing each `new Color(r, g, b)` with the suggested `C64Colors.Token`, and each `new Color(r, g, b, a)` with `new Color(C64Colors.Token, a)` to preserve alpha.

Three rules that override the suggestion:
1. **Over-range literals** become `C64Colors.Token * k` (keep `k` from the HDR table) so bloom still receives an HDR value.
2. **Near-black ambience** such as `new Color(0.02f, 0.02f, 0.03f)` becomes `C64Colors.Black` — it is a darkness floor, not a hue.
3. **Two literals that suggest the same token but must stay distinguishable in play** (for example two hazard tints) — pick the next-best token for one of them and leave a short `//` comment saying why.

Add `using Miner49er.Core;` at the top if it is not already present.

- [ ] **Step 5: Build and confirm no literals remain in this file**

```bash
dotnet build Miner49er.csproj --nologo
python -m tools.artpass.check_code_palette game/WorldRenderer.cs
```
Expected: build succeeds; `OK: no raw colour literals outside C64Colors.cs`

- [ ] **Step 6: Run the Core tests**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo`
Expected: PASS — 948 tests

- [ ] **Step 7: Commit**

```bash
git add tools/artpass/map_code_colors.py tools/artpass/check_code_palette.py game/WorldRenderer.cs
git commit -m "refactor(art): WorldRenderer colours onto the C64 palette"
```

---

## Task 8: Replace colour literals in the remaining 18 files (M3c)

**Files:**
- Modify: `game/net/MatchClient.cs` (10), `game/Main.cs` (9), `game/ui/Compass.cs` (8), `game/ui/MinimapOverlay.cs` (7), `game/TerrainMap.cs` (5), `game/ui/DeathFeed.cs` (4), `game/ui/SettingsPanel.cs` (3), `game/Hud.cs` (3), `game/ui/ShopPanel.cs` (2), `game/ui/ScoreboardOverlay.cs` (2), `game/ui/MainMenu.cs` (2), `game/ui/HighScorePanel.cs` (2), `game/ui/CreditsPanel.cs` (2), `game/ui/StatsOverlay.cs` (1), `game/ui/ResultsOverlay.cs` (1), `game/LoadingScreen.cs` (1), `game/LightRenderer.cs` (1), `game/FogRenderer.cs` (1)
- Modify: `tools/artpass/tests/test_convert.py` (add the code guard as a test)

**Interfaces:**
- Consumes: `C64Colors` (Task 6), `map_code_colors`, `check_code_palette` (Task 7)

- [ ] **Step 1: Generate the suggestion table for everything else**

```bash
python -m tools.artpass.map_code_colors
```

- [ ] **Step 2: Apply tokens file by file, building after each**

For each file in the list above, replace its literals using the same three override rules as Task 7 Step 4, then:

```bash
dotnet build Miner49er.csproj --nologo
```

Two files need particular care:

- **`game/FogRenderer.cs`** and **`game/LightRenderer.cs`** — their single literals are blend inputs. Give each a palette token as its *base* colour and leave the surrounding alpha/intensity maths untouched. The smooth radial fog falloff (`Pow(3.0)`) must be preserved exactly; it is not a colour decision.
- **`game/ui/MinimapOverlay.cs`** — its 7 colours must stay mutually distinguishable at minimap scale. If two resolve to the same token, override one.

- [ ] **Step 3: Confirm no literals remain anywhere**

```bash
python -m tools.artpass.check_code_palette
```
Expected: `OK: no raw colour literals outside C64Colors.cs`

- [ ] **Step 4: Add the guard as a permanent test**

Append to `tools/artpass/tests/test_convert.py`:

```python
class CodePaletteTests(unittest.TestCase):
    def test_no_raw_colour_literals_in_game_code(self):
        from tools.artpass import check_code_palette
        self.assertEqual([], check_code_palette.offenders())
```

- [ ] **Step 5: Run everything**

```bash
python -m unittest discover -s tools/artpass/tests -t .
dotnet build Miner49er.csproj --nologo
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo
```
Expected: 35 Python tests pass; build succeeds; 948 xunit tests pass

- [ ] **Step 6: Commit**

```bash
git add game tools/artpass/tests/test_convert.py
git commit -m "refactor(art): remaining UI and renderer colours onto the C64 palette"
```

- [ ] **Step 7: Document the workflow in the README**

Add to `README.md`:

```markdown
## Art pipeline

All art is locked to the 16-colour Commodore 64 palette.

- `art/palette.json` — the 16 colours. The single source of truth.
- `art/ramps.json` — family and team ramps, plus which assets use which.
- `art/source/**.png` — pre-conversion originals. **Never delete these**; posterising
  is lossy and ramps cannot be re-tuned without them.

```bash
python -m tools.artpass.gen_palette_cs      # regenerate the C# palette after a JSON edit
python -m tools.artpass.convert             # art/source -> assets, then self-verify
python -m tools.artpass.verify              # check assets on their own
python -m tools.artpass.contact_sheet       # before/after sheets into art/review/
python -m tools.artpass.check_code_palette  # no raw colour literals in game/
python -m unittest discover -s tools/artpass/tests -t .
```

**Adding new art:** drop the PNG in `art/source/`, add a rule to `art/ramps.json`, and
run the converter. The verifier fails the build if anything ships off-palette.
```

- [ ] **Step 8: Commit**

```bash
git add README.md
git commit -m "docs(art): C64 art pipeline workflow"
```

- [ ] **Step 9: PLAYTEST GATE (end of M3)**

Run the game via PowerShell and check all of:
- All 8 team colours are distinguishable in the lobby, on the scoreboard and on the minimap. **Teams Red, Purple and Orange are the known risk** (low-luminance identities) — if any pair is genuinely ambiguous in a dark cave, adjust those ramps in `art/ramps.json`, regenerate, and re-check.
- HUD, death feed, results and settings panels read correctly.
- Fog gradient, torch falloff and bloom still look smooth — they must not have become hard bands.
- Miners are recognisable and tinted correctly in all 6 states.

Then answer the spec's open question: **do the miners still read too realistic against the converted art?** If yes, a miner redraw becomes M7 in the follow-up plan.

---

## Definition of Done (M1–M3)

- [ ] `python -m unittest discover -s tools/artpass/tests -t .` → all pass
- [ ] `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo` → 948 pass
- [ ] `dotnet build Miner49er.csproj --nologo` → 0 errors
- [ ] `python -m tools.artpass.verify` → every asset in-palette, binary alpha
- [ ] `python -m tools.artpass.check_code_palette` → no raw literals
- [ ] `art/source/` committed with all 826 originals
- [ ] Both playtest gates passed
- [ ] M4–M6 follow-up plan written, informed by what the M3 playtest showed
