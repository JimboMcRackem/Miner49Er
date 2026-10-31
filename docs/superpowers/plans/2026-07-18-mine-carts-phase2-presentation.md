# Mine Carts — Phase 2 (Presentation + Enable) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline). Client-side presentation — verified by running the game, no new unit tests. Builds on Phase 1 (origin/main @ 0d577c0).

**Goal:** Make the Phase-1 carts visible and live: render rails + carts (smoothly interpolated), expose them on `MatchClient`, and turn `MineCarts` on for exploration floors so they actually appear.

**Architecture:** `MatchClient` ingests `WorldSnapshot.Carts` each tick (like monsters) and holds `TrackTiles` from the map (like `Portals`), with a per-cart smoothed pixel position. `WorldRenderer` draws the rail along the track tiles and a cart sprite at each smoothed position. No sim/Core changes except flipping the exploration `MapConfig` flag. Cart lights + cargo visuals are Phase 3.

**Tech Stack:** Godot 4.6 / C#; PixelLab for the rail + cart sprites.

## Global Constraints

- Client-side only except enabling `MineCarts` in the exploration map config. No sim logic changes.
- Carts/track come from the snapshot/map — all clients match. Smoothing is wall-clock cosmetic (mirror the monster `MoveToward` loop).
- Build via `dotnet build`; launch via **PowerShell only** (`godot --path .`). New sprites' `.import` sidecars ARE committed; never stage the `.import` LF-churn of existing assets. Commit by explicit path; messages end with the Co-Authored-By trailer. Plans/specs stay untracked.

---

### Task 1: MatchClient exposes carts + track + smoothed positions

**Files:** Modify `game/net/MatchClient.cs`

- Add fields: `private List<CartSnapshot> _carts = new();`, `private readonly Dictionary<int, Vector2> _cartVisualPos = new();`, and `public IReadOnlyList<GridPos> TrackTiles { get; private set; } = System.Array.Empty<GridPos>();`.
- Expose `public IReadOnlyList<CartSnapshot> Carts => _carts;` and
  `public Vector2 CartVisualPos(int id, int x, int y) => _cartVisualPos.TryGetValue(id, out var v) ? v : new Vector2(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);`.
- In `ApplyUpdate`, after the monster list is reassigned, add `_carts = new List<CartSnapshot>(update.Snapshot.Carts ?? System.Array.Empty<CartSnapshot>());`.
- On new floor (`ResetFloor`, where `Portals = newMap.Portals;` and `_monsters.Clear()` live): set `TrackTiles = newMap.TrackTiles;` and `_carts.Clear(); _cartVisualPos.Clear();`.
- In the `_Process(delta)` smoothing section (beside the monster loop at ~600-607), add a cart loop mirroring it exactly:
  ```csharp
  foreach (var ct in _carts)
  {
      var target = new Vector2(ct.X * TileSize + TileSize / 2f, ct.Y * TileSize + TileSize / 2f);
      var cur = _cartVisualPos.TryGetValue(ct.Id, out var v) ? v : target;
      _cartVisualPos[ct.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);
  }
  ```
  (Use the same `pixelsPerSec` the monster/miner loops use.)
- Verify: `dotnet build`. Commit `game/net/MatchClient.cs` — `feat(carts): client ingests carts + track + smoothed positions`.

### Task 2: PixelLab rail + cart sprites

**Files:** Create `assets/tiles/rail.png`, `assets/objects/mine_cart.png` (+ `.import`)

- Generate a **rail/track** tile (two parallel rails with sleepers, top-down, transparent background, 32×32 to match tiles) → `assets/tiles/rail.png`.
- Generate a **mine cart** (wooden ore cart, top-down, transparent background, ~32×32) → `assets/objects/mine_cart.png`.
- Import via PowerShell `godot --path . --headless --import`; commit both PNGs + sidecars — `art: mine cart + rail sprites`.

### Task 3: WorldRenderer draws rails + carts

**Files:** Modify `game/WorldRenderer.cs`

- Load `_railTex` / `_cartTex` with the `ResourceLoader.Exists(...) ? GD.Load : null` guard (procedural fallback: rails = two grey lines + ties; cart = a brown `DrawRect` box).
- In `_Draw`, before the entity layer: for each `p in _client.TrackTiles` that is within the viewport/visible, draw `_railTex` over the tile rect (fog-culled like other tile overlays).
- Draw carts: `foreach (var ct in _client.Carts)` draw `_cartTex` centered at `_client.CartVisualPos(ct.Id, ct.X, ct.Y)` (size ~tile), oriented/rotated by `ct.Dir` if desired; fog/viewport-culled. Draw a ground shadow via the shared `DrawGroundShadow` helper.
- Verify: `dotnet build`. Commit `game/WorldRenderer.cs` — `feat(carts): render rails + carts`.

### Task 4: Enable MineCarts on exploration floors

**Files:** Modify `src/Miner49er.Core/Map/MapConfig.cs`

- In the exploration config builder(s) — `MapConfig.FloorConfig(...)` (Expedition/co-op floors) — set `MineCarts = true`. (Leave competitive/other modes off; confirm which builder feeds Expedition.) This is the only Core change: it flips the Phase-1 flag so rails/carts seed and flow through the already-wired host → snapshot → renderer.
- Verify: full Core suite still green (`dotnet test`), build clean.
- Commit `src/Miner49er.Core/Map/MapConfig.cs` — `feat(carts): enable mine carts on exploration floors`.

## Final verification

- Build clean; Core suite green.
- Launch via PowerShell, play an exploration floor: rails appear on some floors with carts on them; walking into a cart shoves it rolling down the rail; it squashes monsters and shoves miners; derails into hazards. Get user visual approval, then finish the branch (merge/push) and write the Phase 3 plan (cargo).

## Tuning knobs

Cart/rail sprite look; smoothing `pixelsPerSec` (shared); which map builders set `MineCarts`.
