# Phase 5: Infra-free internet play (UPnP + connect code) — Design

**Status:** Approved (2026-06-14). Ready for implementation plan.

**Goal:** Let the host open their match to the internet with no hosted services and no manual router setup — the game asks the router (via UPnP) to forward the port and report the public IP, then hands the host a short shareable **code** that friends paste to join.

**Non-goals:** No rendezvous/STUN/hole-punching, no relay server, no matchmaking service, no IPv6, no per-client visibility culling (that Phase 5 item was dropped). This is the "just me and friends sharing a code" tier.

---

## 1. Context & constraints

Networking today (`game/net/NetworkManager.cs`) is pure ENet, host-authoritative listen server:
- `HostGame` → `peer.CreateServer(DefaultPort = 27649, 8)`.
- `JoinGame(address, …)` → `peer.CreateClient(address, port)`.
- Direct IP:port only. No UPnP, no public-IP discovery, no NAT traversal, no relay.

So internet play currently requires the host to manually port-forward and share their public IP. This feature automates that for UPnP-capable routers and packages the address as a code.

**Key infra-free insight:** the host's router can both forward the port *and* report the public IP (Godot `Upnp.QueryExternalAddress()`), so no external service is needed. If UPnP is unavailable, infra-free means we cannot auto-discover the public IP — so no code is shown and the host falls back to LAN/direct-IP play.

**Architectural boundary:** this touches only *how a peer obtains the host's address* and *whether the router is opened*. The lobby, match bootstrap, RPC, snapshot sync, and all gameplay are unchanged. Engine (`Miner49er.Core`) gains exactly one new pure-C# unit (the code codec); everything else is Godot-adapter wiring.

---

## 2. The connect code — `Miner49er.Core.Net.ConnectCode`

A pure-C# static class beside `SnapshotCodec` in `Core.Net`. No Godot, no socket dependencies — fully xUnit-testable.

**Payload:** 4 IPv4 bytes + 2 port bytes (big-endian `ushort`) = 6 bytes / 48 bits.

**Encoding:** Crockford base32 (alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ` — no `I L O U`, so no ambiguity with `1 0`), uppercase. 48 bits → 10 base32 chars, plus **1 checksum char** (Crockford mod-37 check symbol over the 6 payload bytes) → an **11-char token**, e.g. `K7X2P9MQ4A` + check. The checksum makes a mistyped code fail validation instead of dialing a random IP.

**API:**
```
public static string Encode(byte[] ipv4 /*len 4*/, ushort port);
public static bool TryDecode(string code, out byte[] ipv4, out ushort port);
```

**`TryDecode` robustness:** case-insensitive; strips spaces and dashes before decoding (so `K7X2-P9MQ-4A…` and lowercase both work); maps Crockford-equivalent glyphs (`I/L→1`, `O→0`) defensively; rejects wrong length, non-alphabet chars, and checksum mismatch by returning `false` with `ipv4 = null`, `port = 0`.

---

## 3. UPnP service — `game/net/UpnpService.cs`

Encapsulates Godot's `Upnp` plus the threading and lifecycle so `NetworkManager` stays focused. ENet is UDP, so all mappings use the `"UDP"` protocol.

**Status model** (an enum surfaced to the UI):
- `Off` — internet hosting not requested.
- `Discovering` — UPnP discovery / mapping in progress.
- `Mapped` — port mapped and public IP known (carries the public IP → drives the code).
- `Failed` — no gateway, mapping error, or empty external address.

**Open flow** (only when host requested internet):
1. On a **background thread** (so the UI never blocks): `var upnp = new Upnp(); int disc = upnp.Discover();` (`Discover`/`AddPortMapping` return `int`; compare against `(int)Upnp.UpnpResult.Success`).
2. If `disc == (int)Upnp.UpnpResult.Success` and `upnp.GetGatewayCount() > 0`:
   - `int add = upnp.AddPortMapping(27649, 27649, "Miner49er", "UDP", 0);` (0 = indefinite lease; we delete explicitly on leave).
   - `string ext = upnp.QueryExternalAddress();`
   - If `add == (int)Upnp.UpnpResult.Success` and `ext` is a valid IPv4 → status `Mapped(ext)`.
3. Any failure (`Discover` not Success, no gateway, `AddPortMapping` error, blank/invalid `ext`) → status `Failed`.
4. Result is marshalled back to the main thread (Godot `CallDeferred`) before raising the status-changed event, so all UI/state touches happen on the main thread.

**Release flow** (on `Leave()` / quit): if currently `Mapped`, run `upnp.DeletePortMapping(27649, "UDP")` best-effort on a background thread. Stale mappings are harmless (routers also expire them), so a failed delete is swallowed.

---

## 4. `NetworkManager` wiring

- **`HostGame(string name, int color, bool overInternet, int port = DefaultPort)`** — creates the ENet server exactly as today (hosting *always* works regardless of UPnP), then if `overInternet` starts `UpnpService.Open()`. Adds:
  - `InternetStatus Status { get; }` (the enum above),
  - `string? HostCode { get; }` (set from `ConnectCode.Encode(publicIpBytes, port)` once `Mapped`; null otherwise),
  - `event Action InternetStatusChanged` (raised on the main thread when the service reports a transition).
- **`JoinByCode(string input, string name, int color)`** — if `ConnectCode.TryDecode(input, out ip, out port)` succeeds, `JoinGame(ipString, name, color, port)`; otherwise treat `input` as a raw `address[:port]` (split on `:` for an optional port) and `JoinGame` directly. One call handles both a code and a bare IP, preserving the existing direct-IP path.
- **`Leave()`** — calls `UpnpService.Release()` before tearing down the peer.

`DefaultPort` and the existing `HostGame`/`JoinGame` signatures stay; `HostGame` gains the `overInternet` parameter (callers updated).

---

## 5. UI

**MainMenu (`game/ui/MainMenu.cs`, code-built VBox):**
- Add a `"Host over internet (UPnP)"` `CheckBox` **defaulted on (`ButtonPressed = true`)** above/near the Host button; pass `_internetCheck.ButtonPressed` into `HostGame`.
- Relabel the `_address` `LineEdit` placeholder to `"Code or Host IP"` and route the Join button through `NetworkManager.JoinByCode(...)`.

**Lobby (`game/ui/Lobby.cs`, host-only code panel):**
- A label/box that reflects `NetworkManager.Status`, updated via `InternetStatusChanged`:
  - `Discovering` → *"Opening router…"*.
  - `Mapped` → the **code** in a selectable field + a **Copy** button (`DisplayServer.ClipboardSet(code)`).
  - `Failed` → the graceful notice: *"Couldn't open your router automatically (UPnP unavailable). LAN players can still join via your local address. For internet play, forward port 27649 and share your public IP."*
  - `Off` → panel hidden.
- Visible to the host only (joiners never see it). Unsubscribe from the event in `_ExitTree`.

---

## 6. Error handling & threading

- **UPnP failure** (`Failed`): the ENet server is already up, so LAN/direct-IP join still works; the host just sees the graceful notice and no code.
- **Threading:** `Discover()` and `DeletePortMapping()` are blocking and run only on background threads; every state/UI mutation is marshalled to the main thread via `CallDeferred`.
- **Bad join input:** an `input` that isn't a valid code falls through to raw-address join; an invalid address then triggers the existing `JoinFailed` path/`OnJoinFailed` status text — no new error surface.
- **Lifecycle races:** if the host leaves while `Discovering`, `Release()` records intent and the deferred result is ignored (no mapping to delete, or delete once it lands). Guard the status-changed event against a torn-down `NetworkManager`.

---

## 7. Testing

- **xUnit on `ConnectCode`:** round-trip across IP/port boundaries (`0.0.0.0:0`, a typical `203.0.113.7:27649`, `255.255.255.255:65535`); reject wrong-length, non-alphabet, and bad-checksum inputs; a single-character typo is caught by the checksum; case-insensitivity and dash/space tolerance.
- **UPnP is router-dependent** (not unit-testable): a Godot headless smoke confirms the host path doesn't crash when discovery finds no gateway (headless/CI has none — that's exactly the `Failed` path), exiting cleanly.
- **Final verification:** a real over-internet play-test — host with the toggle on, share the code with a friend on a different network, confirm they join and play.

---

## 8. File summary

**Core (create):**
- `src/Miner49er.Core/Net/ConnectCode.cs`
- `src/Miner49er.Core.Tests/ConnectCodeTests.cs`

**Game (create):**
- `game/net/UpnpService.cs`

**Game (modify):**
- `game/net/NetworkManager.cs` — `overInternet` param, status/code/event, `JoinByCode`, `Release` on leave.
- `game/ui/MainMenu.cs` — internet checkbox, code/IP join field, `JoinByCode` call.
- `game/ui/Lobby.cs` — host-only code/status panel + Copy.
