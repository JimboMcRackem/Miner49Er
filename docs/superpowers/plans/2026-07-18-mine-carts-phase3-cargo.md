# Mine Carts — Phase 3 (Cargo) Implementation Plan

> Inline (executing-plans). Builds on Phase 2 (origin/main @ 2b24cbd). Core logic TDD'd; presentation verified by running.

**Goal:** Attach/detach a lantern (moving ghost-sweep light) or a charge (rolling bomb) to a cart, via the existing Use action.

**Key reuse:** ghost-kill = existing `InLanternLight`; bomb = existing `DetonateAt` + fuse-tick idiom (`AdvanceCharges`); attach/detach folds into `TryUseItem` (no new input). Cargo/fuse already in `CartSnapshot` (Phase 1).

## Global Constraints
- Determinism: cargo is integer/enum state; fuse is the one countdown (`Config.ThrownDynamiteFuseSeconds`). Resolved in `Simulation`.
- `DetonateAt(pos, 0, ownerId: -1)` for cart blasts (ownerId -1 = no gold credit; verified safe).
- Build `dotnet build`; run via PowerShell. Commit by explicit path; Co-Authored-By trailer; plans stay untracked.

---

### Task 1: Attach / detach cargo (sim, TDD)
**Files:** `src/Miner49er.Core/Sim/Simulation.cs`, `src/Miner49er.Core/Sim/Cart.cs` (add `FuseRemaining` to `CartReadModel`), test `MineCartCargoTests.cs`.
- Add `CartReadModel.FuseRemaining` (double) + update the `Carts` projection.
- Helper `private Cart? AdjacentCart(Miner m)` — first non-destroyed cart orthogonally adjacent to `m.Pos`.
- In `TryUseItem`, **detach** (empty-handed): after the ground-item pickup loop, if `m.Held is null` and `AdjacentCart(m)` has `Cargo != None` and (for Charge) `FuseRemaining <= 0`, set `m.Held` = cargo→item (`Lantern`→`Lantern`, `Charge`→`Detonator`), clear the cart's cargo, return true.
- **Attach**: before the held-item `switch`, if `held is Lantern or Detonator` and `AdjacentCart(m)` has `Cargo == None`, set the cart's `Cargo` (`Lantern`→`Lantern`, `Detonator`→`Charge`), `m.Held = null`, return true.
- Tests: attach lantern→cart (miner hand empties, cart.Cargo==Lantern); detach→miner holds Lantern again; attach detonator→Charge; can't attach to a laden cart; can't detach a lit charge.

### Task 2: Lantern cart = moving ghost light (sim, TDD)
**Files:** `Simulation.cs`, test.
- In `InLanternLight(pos)`, add: `foreach (var c in _carts) if (!c.Destroyed && c.Cargo == CartCargo.Lantern && pos.ChebyshevTo(c.Pos) <= Config.LanternRadius) return true;`.
- Test: a ghost within `LanternRadius` of a lantern-cart dies on the tick (mirrors the held-lantern ghost-kill test).

### Task 3: Charge cart = rolling bomb (sim, TDD)
**Files:** `Simulation.cs`, test.
- In `RollCart` entry: `if (cart.Cargo == CartCargo.Charge && cart.FuseRemaining <= 0) cart.FuseRemaining = Config.ThrownDynamiteFuseSeconds;` (launch lights the fuse).
- New `AdvanceCartFuses(double dt)`: over a pre-tick snapshot copy of `_carts`, for each `Cargo == Charge && FuseRemaining > 0 && !Destroyed`: decrement; at `<= 0` → `DetonateAt(cart.Pos, 0, -1)`, `cart.Destroyed = true`, add `CartDerailed(cart.Id, cart.Pos)`. Call it in `Tick` right after `AdvanceCharges`.
- In `DerailCart`: if `cart.Cargo == CartCargo.Charge` → `DetonateAt(cart.Pos, 0, -1)` (a charge cart that derails goes off).
- Tests: launched charge cart detonates after the fuse (rock within blast radius cleared, cart destroyed); a charge cart that derails into lava detonates; an un-launched (unpushed) charge cart does not detonate.

### Task 4: Networking + presentation
**Files:** `Net/SnapshotFactory.cs`, `game/net/MatchClient.cs` (expose cargo/fuse already via CartSnapshot), `game/LightRenderer.cs`, `game/WorldRenderer.cs`.
- Factory: pass `c.FuseRemaining` (not 0) into `CartSnapshot`.
- `LightRenderer.AddSourceLights`: for each `_client.Carts` with `Cargo == CartCargo.Lantern`, add a lantern-radius light at `CartVisualPos` (mirror the held-lantern source).
- `WorldRenderer` cart draw: if `Cargo == Lantern` draw a warm glow (reuse `_lanternGlowTex`/`LanternGlowBoost`); if `Cargo == Charge` draw a small charge marker and, when `FuseRemaining > 0`, a fuse spark (reuse the fuse-spark FX).
- Verify: build + full Core suite green; launch, attach a lantern to a cart (Use key beside it) and push it through ghosts; attach a detonator and launch it into rock. User visual approval → finish branch (merge/push) → update memory.

## Tuning knobs
Cart fuse = `Config.ThrownDynamiteFuseSeconds`; lantern radius = `Config.LanternRadius`; cargo→item mapping (Lantern↔Lantern, Charge↔Detonator).
