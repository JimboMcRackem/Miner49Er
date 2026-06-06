# Miner49er — Phase 2: Multiplayer Design

**Date:** 2026-06-06
**Status:** Design approved; ready for implementation planning.
**Builds on:** `2026-06-06-miner49er-game-design.md` (§5 Fog of war & networking,
§9 phased plan). Phase 1 (core single-machine loop) is merged to `main`.

---

## 1. Goal

Turn the single-machine Phase 1 loop into a networked, host-authoritative
multiplayer game: players host or join over the network, gather in a lobby,
play a last-man-standing round, see results, and play again — all driven by the
existing deterministic `Miner49er.Core` simulation.

**In scope:** direct-IP connectivity (LAN + internet with port-forward), a
pre-game lobby with ready-up, host-authoritative state sync, spectate-on-death,
and the last-man-standing round lifecycle.

**Out of scope (deferred):** NAT traversal / relay (Phase 5), per-peer
visibility culling / cheat-resistant fog (Phase 5), client-side prediction
(possible Phase 5 polish), the §3.5 per-player movement-speed / status-effect
system (independent future work), secondary-goal modes and hazards (Phase 4),
LAN auto-discovery, and a full settings menu (Phase 5).

---

## 2. Connectivity (decided)

- **Transport:** Godot high-level multiplayer over `ENetMultiplayerPeer`.
- **Topology:** player-hosted **listen server**. The host is peer id 1 and also
  plays. Clients connect by entering the host's `IP:port`.
- **Reach:** works on a LAN directly, and over the internet when the host
  port-forwards the chosen UDP port. No discovery and no NAT punch-through are
  built in this phase (both deferred to Phase 5).

---

## 3. Architecture

### 3.1 Where logic lives

- The authoritative `Simulation` (from `Miner49er.Core`) runs **only on the
  host**. Core stays engine-free and is reused unchanged — it already accepts
  explicit inputs (`TryMove`, `TryStartMining`, `TryStartPlanting`) and advances
  via `Tick(dt)`.
- **Clients are thin.** They send inputs and render received state. They run no
  simulation logic; they keep only a *replica* of world state for rendering.
- All networking is a new Godot-side adapter layer, keeping game logic in the
  unit-tested Core.

### 3.2 New components

**Godot side (`game/net/`, `game/ui/`):**

- **`NetworkManager`** (autoload singleton) — owns the `MultiplayerApi`/peer;
  host/join lifecycle; tracks connected peers; routes RPCs; raises signals on
  peer connect/disconnect and on host loss.
- **`MatchHost`** (host-only) — owns the `Simulation`; maps `peerId ↔ minerId`;
  applies inputs received since the last tick; runs the fixed-tick loop;
  broadcasts snapshots and drained sim events; detects round end.
- **`MatchClient`** (runs on every peer, including the host locally) — sends
  input RPCs to the host; receives snapshots + events; updates the local render
  replica; interpolates motion; computes local fog.
- **`MainMenu`** — minimal entry screen: Host, or Join (IP\:port entry).
- **`LobbyScreen` / `LobbyController`** — player list (name + color), ready
  state, host Start button.
- **`ResultsScreen`** — shows the round winner; returns to the lobby.

**Core side (`src/Miner49er.Core`):**

- **Round resolution** — a query on the simulation (or a small `RoundState`)
  that reports whether the round is over and who won (last living miner). Pure,
  unit-tested.
- **`Snapshot` / `PlayerInput` DTOs** — plain C# data types describing the
  wire state, with deterministic serialization round-trips. The Godot layer
  handles the actual RPC transport; Core owns the data shape so it is testable.

---

## 4. State synchronization (naive full-state)

Per the base design §5.2, Phase 2 ships naive full-state sync with client-side
fog; visibility culling is a Phase 5 hardening step.

### 4.1 The map travels as a seed

`MapGenerator` is deterministic, so the host does **not** ship the tile grid. At
match start it broadcasts the `MapConfig` (seed + player count); every client
runs `MapGenerator` locally and obtains a byte-identical grid. A determinism
test (same seed → identical grid) underpins this.

### 4.2 Tile mutations ride the event stream

The host drains `SimEvent`s each tick. `RockMined` and `Explosion` (with their
destroyed-rock lists) are broadcast; clients apply them to their local grid
replica and trigger the matching explosion flashes. The grid stays in sync via
events, never via full-grid resends.

### 4.3 Dynamic snapshots

Each host tick broadcasts a small snapshot of mutable entity state:

- **Per miner:** id, position, facing, alive, gold collected, current activity
  + seconds remaining.
- **Per charge:** owner id, wall position, fuse remaining.

For ≤8 players this is a few hundred bytes per tick.

### 4.4 Tick rate & smoothing

- The host runs a **fixed 30 Hz simulation tick**: gather inputs received since
  the previous tick → apply them → `Tick(fixedDt)` → broadcast snapshot +
  events.
- Clients **interpolate** between the last two received snapshots (~100 ms
  buffer) for smooth on-screen motion, since there is no prediction.

### 4.5 Fog

Each client computes visibility locally from **its own** miner's position over
its grid replica (the existing `Visibility`/`FogState` code). This matches the
base design's Phase 2 "client-side fog." Because full state is transmitted, a
modified client could see everything; making fog cheat-resistant via per-peer
visibility culling is explicitly Phase 5.

---

## 5. Input flow

- Each frame, a client sends its **desired movement direction** (one of N/E/S/W,
  or none) to the host, plus **edge-triggered action** RPCs (mine, plant) sent
  reliably on key-down.
- The host stores the latest desired direction per peer and applies it via
  `TryMove` whenever that miner is ready to take its next step; action RPCs call
  `TryStartMining` / `TryStartPlanting`.
- The host validates every input through the existing simulation methods —
  clients cannot move illegally or act out of turn.

---

## 6. Identity, lobby & round lifecycle

### 6.1 Player identity

- Each connected peer has Godot's stable unique multiplayer id. On match start
  the host assigns each ready player a `minerId` and a spawn from the generated
  map; the lobby-chosen **name and color** travel with that player.
- The host is also a player (peer id 1).

### 6.2 Screen flow

Phase 1 was Splash → Loading → Game. Phase 2 inserts the multiplayer shell:

```
Splash → Main Menu (Host / Join) → Lobby → Loading → Match → Results → Lobby
```

- **Main Menu** — minimal: Host, or Join (IP\:port entry). The full settings
  menu remains Phase 5; this is only the multiplayer entry point.
- **Lobby** — player list with name + color (chosen here), per-player ready
  state, and a host Start button (host may start when players are ready). Late
  joiners wait in the lobby until the current round ends.
- **Loading** — host generates the map and broadcasts the seed/config; clients
  regenerate and confirm readiness; then everyone enters the match.
- **Match → Results** — the host detects last-man-standing (≤1 alive),
  broadcasts the winner, and everyone transitions to a results screen, then back
  to the same lobby for another round (new seed).

### 6.3 Death & spectate

When a miner is killed (`MinerKilled`), that peer's input is disabled and the
player spectates the remaining miners until the round resolves.

### 6.4 Win condition

Last living miner wins. Round resolution lives in Core (testable): the host
queries it each tick and ends the round when one-or-zero miners remain alive.
(Secondary-goal modes remain Phase 4.)

---

## 7. Disconnect handling

- **Client drops mid-match** — the host eliminates that peer's miner and the
  round continues.
- **Host drops** — the match cannot continue (listen server); clients return to
  the Main Menu with a "host disconnected" message.
- **Lobby** — peers may join and leave freely; the player list updates live.

---

## 8. Testing & verification

- **Core logic stays unit-tested:** new pure pieces — round resolution
  (last-man-standing → winner) and `Snapshot` / `PlayerInput` serialization
  round-trips — get xUnit coverage alongside the existing Phase 1 tests.
- **Determinism test:** same seed → byte-identical grid, underpinning seed-based
  map sync.
- **Transport smoke test:** two headless Godot instances (host + one client)
  connect, exchange a few ticks, and agree on state; plus the user's manual
  multi-instance play-test (movement feel under real latency, blasts, death,
  spectate, results, re-lobby).
- The network layer is kept deliberately thin (transport only) so that
  correctness lives in the tested Core.

---

## 9. Scope notes

- Phase 2 keeps the current **single movement cadence**; the §3.5 per-player
  speed / status-effect system is independent future work and is not pulled in
  here.
- No prediction, no NAT traversal, no visibility culling, no discovery, no full
  settings menu — all deferred as listed in §1.
