# Phase 2 — Multiplayer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the single-machine Phase 1 loop into host-authoritative direct-IP multiplayer: host/join, a pre-game lobby with ready-up, naive full-state sync, spectate-on-death, and a last-man-standing round lifecycle.

**Architecture:** The authoritative `Simulation` runs only on the host. Clients are thin: they send inputs and render received state over Godot's ENet high-level multiplayer. The map is shipped as a seed (clients regenerate it deterministically); tile mutations ride a per-tick update; entity state rides a per-tick snapshot. All `[Rpc]` methods live on a single `NetworkManager` autoload (stable node path on every peer) to avoid node-path/authority pitfalls. New pure logic (round resolution, snapshot codec) lives in the unit-tested `Miner49er.Core`.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C#, `ENetMultiplayerPeer`, `Miner49er.Core` (pure C#), xUnit.

---

## Conventions

- **Run tests:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` (PowerShell; see `memory/dev-environment.md`).
- **Build the game:** `dotnet build Miner49er.csproj`.
- **Headless boot check:** `godot --headless --quit-after 120` (exit 0, no `ERROR`/`SCRIPT ERROR` lines).
- **Two-instance manual check** (networking tasks): open two windowed instances from the project dir — `godot .` twice — host in one, join `127.0.0.1` in the other.
- Core C# uses 4-space indent; `game/` C# uses **tabs** (the Godot editor enforces this — match it to avoid churn).
- Commit after every task. Branch is `phase2-multiplayer`.

## File Structure

**Created — Core (pure, tested):**
- `src/Miner49er.Core/Sim/RoundResolver.cs` — last-man-standing → `RoundResult`.
- `src/Miner49er.Core/Net/Snapshots.cs` — wire DTOs (`MinerSnapshot`, `ChargeSnapshot`, `WorldSnapshot`, `TileChange`, `TickUpdate`).
- `src/Miner49er.Core/Net/SnapshotFactory.cs` — capture a `WorldSnapshot` from a `Simulation`.
- `src/Miner49er.Core/Net/SnapshotCodec.cs` — `TickUpdate` ⇄ `byte[]`.

**Created — Tests:**
- `src/Miner49er.Core.Tests/MapDeterminismTests.cs`
- `src/Miner49er.Core.Tests/RoundResolverTests.cs`
- `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`
- `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`

**Created — Godot layer (`game/`):**
- `game/net/NetworkManager.cs` — autoload: connection lifecycle, lobby sync, all RPCs.
- `game/net/PlayerColors.cs` — shared color palette.
- `game/net/MatchHost.cs` — host-only authoritative tick loop + broadcast.
- `game/net/MatchClient.cs` — per-peer render replica: applies updates, smooths, fogs.
- `game/ui/MainMenu.cs` + `game/ui/MainMenu.tscn` — Host / Join entry.
- `game/ui/Lobby.cs` + `game/ui/Lobby.tscn` — player list, color, ready, start.
- `game/ui/ResultsOverlay.cs` — winner banner + return-to-lobby.

**Modified:**
- `project.godot` — register `NetworkManager` autoload; main scene stays `Splash.tscn`.
- `game/Splash.cs` — advance to `MainMenu.tscn` (was `Main.tscn`).
- `game/Main.cs` — becomes the in-match controller driven by `MatchHost`/`MatchClient` (replaces single-player driving).
- `game/WorldRenderer.cs`, `game/FogRenderer.cs` — read from `MatchClient` view instead of `Main.Sim`.

---

## Task 1: Lock map-generation determinism (Core)

Seed-based map sync depends on `MapGenerator.Generate` producing a byte-identical grid for the same config. Pin it with a test.

**Files:**
- Test: `src/Miner49er.Core.Tests/MapDeterminismTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapDeterminismTests
{
    private static MapConfig Config() => new() { Seed = 4242, PlayerCount = 4 };

    [Fact]
    public void Same_seed_produces_identical_grid()
    {
        var a = MapGenerator.Generate(Config());
        var b = MapGenerator.Generate(Config());

        Assert.Equal(a.Grid.Width, b.Grid.Width);
        Assert.Equal(a.Grid.Height, b.Grid.Height);
        foreach (var p in a.Grid.Positions())
            Assert.Equal(a.Grid.Get(p), b.Grid.Get(p));
    }

    [Fact]
    public void Same_seed_produces_identical_spawns()
    {
        var a = MapGenerator.Generate(Config());
        var b = MapGenerator.Generate(Config());
        Assert.Equal(a.Spawns.ToList(), b.Spawns.ToList());
    }
}
```

- [ ] **Step 2: Run to verify it passes (generator is already deterministic)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MapDeterminismTests`
Expected: PASS (2 tests). If it FAILS, `MapGenerator` has a nondeterminism bug (e.g. reads `DateTime`/unordered collection) that MUST be fixed before seed-based sync is viable — fix the generator, not the test.

- [ ] **Step 3: Commit**

```bash
git add src/Miner49er.Core.Tests/MapDeterminismTests.cs
git commit -m "test(core): lock map generation determinism for seed-based sync"
```

---

## Task 2: Round resolution (Core, TDD)

Last-man-standing detection lives in Core so it's testable and the host just queries it.

**Files:**
- Create: `src/Miner49er.Core/Sim/RoundResolver.cs`
- Test: `src/Miner49er.Core.Tests/RoundResolverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Miner49er.Core;
using Xunit;

public class RoundResolverTests
{
    private static Simulation TwoMinerSim()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        return sim;
    }

    [Fact]
    public void Two_alive_miners_is_not_over()
    {
        var result = RoundResolver.Resolve(TwoMinerSim());
        Assert.False(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void One_alive_miner_is_over_and_that_miner_wins()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(2).Alive = false; // internal set is visible to Core tests via InternalsVisibleTo? No:

        var result = RoundResolver.Resolve(sim);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Zero_alive_miners_is_over_with_no_winner()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(1).Alive = false;
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }
}
```

> NOTE: `Miner.Alive` has an `internal set`, so the test project must already see Core internals. Check `src/Miner49er.Core/Miner49er.Core.csproj` for `<InternalsVisibleTo Include="Miner49er.Core.Tests" />` (or an `AssemblyInfo`). If it is **not** present, add this to `Miner49er.Core.csproj` inside an `<ItemGroup>` before running:
> ```xml
> <ItemGroup>
>   <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
>     <_Parameter1>Miner49er.Core.Tests</_Parameter1>
>   </AssemblyAttribute>
> </ItemGroup>
> ```
> If existing Phase 1 tests already mutate `Alive`/`Pos` directly, internals are visible and no change is needed.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter RoundResolverTests`
Expected: FAIL — `RoundResolver` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Linq;

namespace Miner49er.Core;

public readonly record struct RoundResult(bool IsOver, int WinnerId);

/// <summary>Last-man-standing resolution. A round is over when one or zero
/// miners remain alive; the sole survivor (if any) is the winner.</summary>
public static class RoundResolver
{
    public static RoundResult Resolve(Simulation sim)
    {
        var alive = sim.Miners.Where(m => m.Alive).ToList();
        if (alive.Count <= 1)
            return new RoundResult(true, alive.Count == 1 ? alive[0].Id : -1);
        return new RoundResult(false, -1);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter RoundResolverTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core.Tests/RoundResolverTests.cs src/Miner49er.Core/Miner49er.Core.csproj
git commit -m "feat(core): add last-man-standing round resolution"
```

---

## Task 3: Wire DTOs + snapshot codec (Core, TDD)

Define the wire shapes and a binary codec so the Godot layer only moves `byte[]`.

> **YAGNI note:** player input is three primitives (direction, mine, plant) sent directly as RPC args — it needs no codec, so we do not build a `PlayerInput` DTO. Only the per-tick world update is serialized.

**Files:**
- Create: `src/Miner49er.Core/Net/Snapshots.cs`
- Create: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

- [ ] **Step 1: Write the DTOs (no test yet — pure data)**

`src/Miner49er.Core/Net/Snapshots.cs`:
```csharp
using System.Collections.Generic;

namespace Miner49er.Core.Net;

public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity, double ActivityRemaining);

public readonly record struct ChargeSnapshot(int OwnerId, int X, int Y, double FuseRemaining);

/// <summary>One floor cell that was just revealed; FromBlast drives the flash.</summary>
public readonly record struct TileChange(int X, int Y, bool FromBlast);

public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges);

public sealed record TickUpdate(WorldSnapshot Snapshot, IReadOnlyList<TileChange> TileChanges);
```

- [ ] **Step 2: Write the failing codec test**

`src/Miner49er.Core.Tests/SnapshotCodecTests.cs`:
```csharp
using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecTests
{
    [Fact]
    public void Round_trips_all_fields()
    {
        var update = new TickUpdate(
            new WorldSnapshot(
                Tick: 7,
                Miners: new List<MinerSnapshot>
                {
                    new(1, 3, 4, 2, true, 5, 1, 2.5),
                    new(2, 9, 0, 0, false, 0, 0, 0.0),
                },
                Charges: new List<ChargeSnapshot> { new(1, 8, 8, 1.25) }),
            TileChanges: new List<TileChange> { new(8, 8, true), new(2, 2, false) });

        byte[] bytes = SnapshotCodec.Write(update);
        TickUpdate back = SnapshotCodec.Read(bytes);

        Assert.Equal(7, back.Snapshot.Tick);
        Assert.Equal(2, back.Snapshot.Miners.Count);
        Assert.Equal(update.Snapshot.Miners[0], back.Snapshot.Miners[0]);
        Assert.Equal(update.Snapshot.Miners[1], back.Snapshot.Miners[1]);
        Assert.Equal(update.Snapshot.Charges[0], back.Snapshot.Charges[0]);
        Assert.Equal(2, back.TileChanges.Count);
        Assert.Equal(update.TileChanges[0], back.TileChanges[0]);
        Assert.Equal(update.TileChanges[1], back.TileChanges[1]);
    }

    [Fact]
    public void Round_trips_empty_collections()
    {
        var update = new TickUpdate(
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>()),
            new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Empty(back.Snapshot.Miners);
        Assert.Empty(back.Snapshot.Charges);
        Assert.Empty(back.TileChanges);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SnapshotCodecTests`
Expected: FAIL — `SnapshotCodec` does not exist.

- [ ] **Step 4: Write the codec**

`src/Miner49er.Core/Net/SnapshotCodec.cs`:
```csharp
using System.Collections.Generic;
using System.IO;

namespace Miner49er.Core.Net;

/// <summary>Compact binary serialization for a per-tick world update.
/// Engine-free so it is unit-testable; the Godot layer only transports bytes.</summary>
public static class SnapshotCodec
{
    public static byte[] Write(TickUpdate update)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        var snap = update.Snapshot;
        w.Write(snap.Tick);

        w.Write(snap.Miners.Count);
        foreach (var m in snap.Miners)
        {
            w.Write(m.Id); w.Write(m.X); w.Write(m.Y); w.Write(m.Facing);
            w.Write(m.Alive); w.Write(m.Gold); w.Write(m.Activity); w.Write(m.ActivityRemaining);
        }

        w.Write(snap.Charges.Count);
        foreach (var c in snap.Charges)
        {
            w.Write(c.OwnerId); w.Write(c.X); w.Write(c.Y); w.Write(c.FuseRemaining);
        }

        w.Write(update.TileChanges.Count);
        foreach (var t in update.TileChanges)
        {
            w.Write(t.X); w.Write(t.Y); w.Write(t.FromBlast);
        }

        w.Flush();
        return ms.ToArray();
    }

    public static TickUpdate Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);

        int tick = r.ReadInt32();

        int minerCount = r.ReadInt32();
        var miners = new List<MinerSnapshot>(minerCount);
        for (int i = 0; i < minerCount; i++)
            miners.Add(new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble()));

        int chargeCount = r.ReadInt32();
        var charges = new List<ChargeSnapshot>(chargeCount);
        for (int i = 0; i < chargeCount; i++)
            charges.Add(new ChargeSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble()));

        int changeCount = r.ReadInt32();
        var changes = new List<TileChange>(changeCount);
        for (int i = 0; i < changeCount; i++)
            changes.Add(new TileChange(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean()));

        return new TickUpdate(new WorldSnapshot(tick, miners, charges), changes);
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SnapshotCodecTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "feat(core): add wire DTOs and binary snapshot codec"
```

---

## Task 4: Capture a snapshot from the Simulation (Core, TDD)

The host turns live sim state into a `WorldSnapshot`.

**Files:**
- Create: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotFactoryTests
{
    [Fact]
    public void Captures_miner_position_facing_and_alive()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East); // now at (3,3) facing East

        var snap = SnapshotFactory.Capture(sim, tick: 11);

        Assert.Equal(11, snap.Tick);
        var m = Assert.Single(snap.Miners);
        Assert.Equal(1, m.Id);
        Assert.Equal(3, m.X);
        Assert.Equal(3, m.Y);
        Assert.Equal((int)Direction.East, m.Facing);
        Assert.True(m.Alive);
    }

    [Fact]
    public void Captures_charges()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East); // face the rock at (3,2)? Move puts miner at (3,2)? no rock blocks
        // Re-aim: miner at (2,2) facing East toward rock (3,2)
        sim.TryStartPlanting(1);
        sim.Tick(sim.Config.PlantSeconds + 0.01); // finish planting -> charge exists

        var snap = SnapshotFactory.Capture(sim, tick: 0);
        var c = Assert.Single(snap.Charges);
        Assert.Equal(3, c.X);
        Assert.Equal(2, c.Y);
    }
}
```

> NOTE on the second test: `TryMove(East)` from (2,2) is blocked by the rock at (3,2) but still sets facing East (verified in Phase 1 `SimulationMovementTests`). So the miner stays at (2,2) facing the rock, and planting targets (3,2).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SnapshotFactoryTests`
Expected: FAIL — `SnapshotFactory` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Linq;

namespace Miner49er.Core.Net;

/// <summary>Captures authoritative simulation state into a transmittable snapshot.</summary>
public static class SnapshotFactory
{
    public static WorldSnapshot Capture(Simulation sim, int tick)
    {
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining))
            .ToList();

        var charges = sim.Charges
            .Select(c => new ChargeSnapshot(c.OwnerId, c.WallPos.X, c.WallPos.Y, c.FuseRemaining))
            .ToList();

        return new WorldSnapshot(tick, miners, charges);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SnapshotFactoryTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the full Core suite (regression)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all Phase 1 tests + the new ones).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs
git commit -m "feat(core): capture world snapshots from the simulation"
```

---

## Task 5: NetworkManager autoload — connection lifecycle

The single networking node. This task gets host/join + connection signals working; lobby and match RPCs come next.

**Files:**
- Create: `game/net/PlayerColors.cs`
- Create: `game/net/NetworkManager.cs`
- Modify: `project.godot` (register autoload)

- [ ] **Step 1: Add the shared color palette**

`game/net/PlayerColors.cs`:
```csharp
using Godot;

namespace Miner49er;

/// <summary>Eight preset miner colors, indexed by lobby selection.</summary>
public static class PlayerColors
{
	public static readonly Color[] Palette =
	{
		new("e8c34a"), new("4a9be8"), new("e84a4a"), new("4ae87a"),
		new("c34ae8"), new("e8964a"), new("4ae8e0"), new("e84ab0"),
	};

	public static Color At(int index) => Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];
}
```

- [ ] **Step 2: Write the NetworkManager (connection lifecycle only)**

`game/net/NetworkManager.cs`:
```csharp
using Godot;
using System;
using System.Collections.Generic;

namespace Miner49er;

public struct PlayerInfo
{
	public string Name;
	public int ColorIndex;
	public bool Ready;
}

/// <summary>Autoload singleton. Owns the ENet peer and every RPC. The host runs
/// the authoritative MatchHost; all peers run a MatchClient for rendering.</summary>
public partial class NetworkManager : Node
{
	public static NetworkManager Instance { get; private set; } = null!;
	public const int DefaultPort = 27649;

	public bool IsHost { get; private set; }
	public long LocalId => Multiplayer.GetUniqueId();
	public readonly Dictionary<long, PlayerInfo> Players = new();

	public event Action? LobbyChanged;
	public event Action? JoinFailed;
	public event Action? Disconnected;

	private string _pendingName = "Miner";
	private int _pendingColor;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;
	}

	public Error HostGame(string playerName, int colorIndex, int port = DefaultPort)
	{
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateServer(port, 8);
		if (err != Error.Ok) return err;
		Multiplayer.MultiplayerPeer = peer;
		IsHost = true;
		Players.Clear();
		Players[LocalId] = new PlayerInfo { Name = playerName, ColorIndex = colorIndex, Ready = false };
		LobbyChanged?.Invoke();
		return Error.Ok;
	}

	public Error JoinGame(string address, string playerName, int colorIndex, int port = DefaultPort)
	{
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateClient(address, port);
		if (err != Error.Ok) return err;
		Multiplayer.MultiplayerPeer = peer;
		IsHost = false;
		_pendingName = playerName;
		_pendingColor = colorIndex;
		return Error.Ok;
	}

	public void Leave()
	{
		Multiplayer.MultiplayerPeer = null;
		IsHost = false;
		Players.Clear();
	}

	// Connection callbacks ---------------------------------------------------

	private void OnPeerConnected(long id) { /* host fills lobby in Task 6 */ }
	private void OnPeerDisconnected(long id) { /* handled in Task 6/13 */ }
	private void OnConnectedToServer() { /* client submits info in Task 6 */ }
	private void OnConnectionFailed() { Multiplayer.MultiplayerPeer = null; JoinFailed?.Invoke(); }
	private void OnServerDisconnected() { Multiplayer.MultiplayerPeer = null; Players.Clear(); Disconnected?.Invoke(); }
}
```

- [ ] **Step 3: Register the autoload in `project.godot`**

Add this section to `project.godot` (place it after the `[application]` section). The leading `*` makes the script auto-instantiate as a singleton node named `NetworkManager` at `/root/NetworkManager`:

```ini
[autoload]

NetworkManager="*res://game/net/NetworkManager.cs"
```

- [ ] **Step 4: Build and headless-boot to verify the autoload loads clean**

Run:
```
dotnet build Miner49er.csproj
godot --headless --quit-after 120
```
Expected: build succeeds; boot exits 0 with no `ERROR`/`SCRIPT ERROR`. (The Splash scene still loads; the autoload is now present.)

- [ ] **Step 5: Commit**

```bash
git add game/net/PlayerColors.cs game/net/NetworkManager.cs project.godot
git commit -m "feat(net): add NetworkManager autoload with connection lifecycle"
```

---

## Task 6: Lobby state sync (RPC handshake + ready)

Replicate the player list to all peers and support ready toggling.

**Files:**
- Modify: `game/net/NetworkManager.cs`

- [ ] **Step 1: Add the lobby RPCs and handshake**

In `game/net/NetworkManager.cs`, replace the four placeholder callbacks and add the RPC methods + broadcast helper:

```csharp
	private void OnPeerConnected(long id)
	{
		// Host waits for the joiner's SubmitPlayerInfo; nothing to do yet.
	}

	private void OnPeerDisconnected(long id)
	{
		if (!IsHost) return;
		if (Players.Remove(id)) BroadcastLobby();
	}

	private void OnConnectedToServer()
	{
		RpcId(1, nameof(SubmitPlayerInfo), _pendingName, _pendingColor);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void SubmitPlayerInfo(string name, int colorIndex)
	{
		if (!IsHost) return;
		long sender = Multiplayer.GetRemoteSenderId();
		Players[sender] = new PlayerInfo { Name = name, ColorIndex = colorIndex, Ready = false };
		BroadcastLobby();
	}

	public void ToggleReady()
	{
		if (IsHost)
		{
			var info = Players[LocalId];
			info.Ready = !info.Ready;
			Players[LocalId] = info;
			BroadcastLobby();
		}
		else
		{
			RpcId(1, nameof(RequestToggleReady));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void RequestToggleReady()
	{
		if (!IsHost) return;
		long sender = Multiplayer.GetRemoteSenderId();
		if (!Players.TryGetValue(sender, out var info)) return;
		info.Ready = !info.Ready;
		Players[sender] = info;
		BroadcastLobby();
	}

	private void BroadcastLobby()
	{
		var ids = new long[Players.Count];
		var names = new string[Players.Count];
		var colors = new int[Players.Count];
		var readys = new bool[Players.Count];
		int i = 0;
		foreach (var (id, info) in Players)
		{
			ids[i] = id; names[i] = info.Name; colors[i] = info.ColorIndex; readys[i] = info.Ready; i++;
		}
		Rpc(nameof(ReceiveLobby), ids, names, colors, readys);
		ReceiveLobby(ids, names, colors, readys); // apply locally on host too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void ReceiveLobby(long[] ids, string[] names, int[] colors, bool[] readys)
	{
		Players.Clear();
		for (int i = 0; i < ids.Length; i++)
			Players[ids[i]] = new PlayerInfo { Name = names[i], ColorIndex = colors[i], Ready = readys[i] };
		LobbyChanged?.Invoke();
	}
```

> `Rpc(...)` from the host (peer 1) reaches all clients; `ReceiveLobby` is marked `Authority` so only the host may originate it. The host also applies it locally (the explicit call) so host and clients share one code path.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Miner49er.csproj`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add game/net/NetworkManager.cs
git commit -m "feat(net): replicate lobby state with ready toggling"
```

---

## Task 7: Main Menu scene (Host / Join)

The entry screen after Splash.

**Files:**
- Create: `game/ui/MainMenu.cs`
- Create: `game/ui/MainMenu.tscn`
- Modify: `game/Splash.cs`

- [ ] **Step 1: Write the MainMenu script**

`game/ui/MainMenu.cs`:
```csharp
using Godot;

namespace Miner49er;

public partial class MainMenu : Control
{
	private LineEdit _name = null!;
	private LineEdit _address = null!;
	private OptionButton _color = null!;
	private Label _status = null!;

	public override void _Ready()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(LayoutPreset.Center);
		AddChild(box);

		var title = new Label { Text = "MINER 49ER" };
		title.AddThemeFontSizeOverride("font_size", 48);
		box.AddChild(title);

		_name = new LineEdit { Text = "Miner", PlaceholderText = "Name", CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_name);

		_color = new OptionButton();
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
			_color.AddItem($"Color {i + 1}", i);
		box.AddChild(_color);

		_address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Host IP" };
		box.AddChild(_address);

		var hostBtn = new Button { Text = "Host Game" };
		hostBtn.Pressed += OnHost;
		box.AddChild(hostBtn);

		var joinBtn = new Button { Text = "Join Game" };
		joinBtn.Pressed += OnJoin;
		box.AddChild(joinBtn);

		_status = new Label { Text = "" };
		box.AddChild(_status);

		NetworkManager.Instance.JoinFailed += () => _status.Text = "Connection failed.";
	}

	private void OnHost()
	{
		var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnJoin()
	{
		var err = NetworkManager.Instance.JoinGame(_address.Text, _name.Text, _color.Selected);
		if (err != Error.Ok) { _status.Text = $"Join failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}
}
```

- [ ] **Step 2: Create the scene file**

`game/ui/MainMenu.tscn`:
```ini
[gd_scene load_steps=2 format=3 uid="uid://miner49ermenu"]

[ext_resource type="Script" path="res://game/ui/MainMenu.cs" id="1"]

[node name="MainMenu" type="Control"]
layout_mode = 3
anchors_preset = 15
script = ExtResource("1")
```

- [ ] **Step 3: Point Splash at the menu**

In `game/Splash.cs`, change the scene path in `Advance()`:
```csharp
		GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
```
(was `res://game/Main.tscn`).

- [ ] **Step 4: Build + headless boot**

Run:
```
dotnet build Miner49er.csproj
godot --headless --quit-after 180
```
Expected: build OK; boot exits 0; logs show Splash advancing to MainMenu with no errors.

- [ ] **Step 5: Commit**

```bash
git add game/ui/MainMenu.cs game/ui/MainMenu.tscn game/Splash.cs
git commit -m "feat(ui): add main menu with host/join entry"
```

---

## Task 8: Lobby scene (list, ready, start)

Shows replicated players; host can start once ≥2 players are ready.

**Files:**
- Create: `game/ui/Lobby.cs`
- Create: `game/ui/Lobby.tscn`

- [ ] **Step 1: Write the Lobby script**

`game/ui/Lobby.cs`:
```csharp
using Godot;
using System.Linq;

namespace Miner49er;

public partial class Lobby : Control
{
	private VBoxContainer _list = null!;
	private Button _readyBtn = null!;
	private Button _startBtn = null!;
	private Label _hint = null!;

	public override void _Ready()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(LayoutPreset.Center);
		AddChild(box);

		var title = new Label { Text = "LOBBY" };
		title.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(title);

		_list = new VBoxContainer { CustomMinimumSize = new Vector2(320, 200) };
		box.AddChild(_list);

		_readyBtn = new Button { Text = "Toggle Ready" };
		_readyBtn.Pressed += () => NetworkManager.Instance.ToggleReady();
		box.AddChild(_readyBtn);

		_startBtn = new Button { Text = "Start Match", Disabled = true };
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch();
		_startBtn.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_startBtn);

		_hint = new Label { Text = "" };
		box.AddChild(_hint);

		NetworkManager.Instance.LobbyChanged += Refresh;
		NetworkManager.Instance.Disconnected += OnDisconnected;
		NetworkManager.Instance.MatchStarting += OnMatchStarting;
		Refresh();
	}

	public override void _ExitTree()
	{
		NetworkManager.Instance.LobbyChanged -= Refresh;
		NetworkManager.Instance.Disconnected -= OnDisconnected;
		NetworkManager.Instance.MatchStarting -= OnMatchStarting;
	}

	private void Refresh()
	{
		foreach (var c in _list.GetChildren()) c.QueueFree();
		foreach (var (id, info) in NetworkManager.Instance.Players)
		{
			var row = new Label
			{
				Text = $"{info.Name}  {(info.Ready ? "[READY]" : "[...]")}",
			};
			row.AddThemeColorOverride("font_color", PlayerColors.At(info.ColorIndex));
			_list.AddChild(row);
		}

		var players = NetworkManager.Instance.Players.Values;
		bool canStart = players.Count >= 2 && players.All(p => p.Ready);
		_startBtn.Disabled = !canStart;
		_hint.Text = canStart ? "" : "Need ≥2 players, all ready.";
	}

	private void OnDisconnected()
	{
		GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
	}

	private void OnMatchStarting()
	{
		GetTree().ChangeSceneToFile("res://game/Main.tscn");
	}
}
```

> `StartMatch()`, `MatchStarting`, and the seed/peer-order fields used on match entry are added to `NetworkManager` in Task 12. This script compiles only after Task 12 adds them — so build verification for this task is deferred to Task 12 Step (build). Create the files now; the lobby is wired end-to-end there.

- [ ] **Step 2: Create the scene file**

`game/ui/Lobby.tscn`:
```ini
[gd_scene load_steps=2 format=3 uid="uid://miner49erlobby"]

[ext_resource type="Script" path="res://game/ui/Lobby.cs" id="1"]

[node name="Lobby" type="Control"]
layout_mode = 3
anchors_preset = 15
script = ExtResource("1")
```

- [ ] **Step 3: Commit (compiles after Task 12)**

```bash
git add game/ui/Lobby.cs game/ui/Lobby.tscn
git commit -m "feat(ui): add lobby scene (player list, ready, start)"
```

---

## Task 9: MatchHost — authoritative tick loop

Host-only node: owns the `Simulation`, applies inputs at a fixed 30 Hz, gates movement cadence, broadcasts updates, and detects round end.

**Files:**
- Create: `game/net/MatchHost.cs`

- [ ] **Step 1: Write MatchHost**

`game/net/MatchHost.cs`:
```csharp
using Godot;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Host-only authoritative simulation driver. Steps a fixed 30 Hz tick,
/// applies queued inputs (with a per-miner move cadence gate), and broadcasts a
/// TickUpdate each step via NetworkManager.</summary>
public partial class MatchHost : Node
{
	public const double TickSeconds = 1.0 / 30.0;
	public const double MoveStepSeconds = 0.12; // grid cadence; matches Phase 1 feel

	private Simulation _sim = null!;
	private readonly Dictionary<long, int> _peerToMiner = new();
	private readonly Dictionary<int, int> _pendingDir = new();   // minerId -> Direction(int) or -1
	private readonly HashSet<int> _pendingMine = new();
	private readonly HashSet<int> _pendingPlant = new();
	private readonly Dictionary<int, double> _moveCooldown = new();

	private int _tick;
	private double _accum;
	private bool _running;

	public void Begin(Simulation sim, Dictionary<long, int> peerToMiner)
	{
		_sim = sim;
		foreach (var (peer, miner) in peerToMiner)
		{
			_peerToMiner[peer] = miner;
			_pendingDir[miner] = -1;
			_moveCooldown[miner] = 0;
		}
		_running = true;
	}

	public void SetDir(long peerId, int dir)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _pendingDir[minerId] = dir;
	}

	public void SetAction(long peerId, bool mine, bool plant)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine) _pendingMine.Add(minerId);
		if (plant) _pendingPlant.Add(minerId);
	}

	public void EliminatePeer(long peerId)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		var m = _sim.Miners.FirstOrDefault(x => x.Id == minerId);
		if (m is { Alive: true }) m.Alive = false; // Core internals visible to game assembly? see note
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_running) return;
		_accum += delta;
		while (_accum >= TickSeconds) { _accum -= TickSeconds; StepOnce(); }
	}

	private void StepOnce()
	{
		foreach (var id in _moveCooldown.Keys.ToList())
			_moveCooldown[id] = Mathf.Max(0, (float)(_moveCooldown[id] - TickSeconds));

		foreach (var (minerId, dir) in _pendingDir)
		{
			if (dir < 0 || _moveCooldown[minerId] > 0) continue;
			if (_sim.TryMove(minerId, (Direction)dir)) _moveCooldown[minerId] = MoveStepSeconds;
		}

		foreach (var minerId in _pendingMine) _sim.TryStartMining(minerId);
		_pendingMine.Clear();
		foreach (var minerId in _pendingPlant) _sim.TryStartPlanting(minerId);
		_pendingPlant.Clear();

		_sim.Tick(TickSeconds);
		_tick++;

		var changes = new List<TileChange>();
		foreach (var e in _sim.DrainEvents())
		{
			switch (e)
			{
				case RockMined rm:
					changes.Add(new TileChange(rm.Pos.X, rm.Pos.Y, false));
					break;
				case Explosion ex:
					foreach (var d in ex.DestroyedRock)
						changes.Add(new TileChange(d.X, d.Y, true));
					break;
			}
		}

		var update = new TickUpdate(SnapshotFactory.Capture(_sim, _tick), changes);
		NetworkManager.Instance.BroadcastTick(SnapshotCodec.Write(update));

		var result = RoundResolver.Resolve(_sim);
		if (result.IsOver)
		{
			_running = false;
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
			NetworkManager.Instance.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
		}
	}
}
```

> **Internals note:** `EliminatePeer` and any `m.Alive = false` require Core internals to be visible to the `Miner49er` game assembly. If not already configured, add to `src/Miner49er.Core/Miner49er.Core.csproj`:
> ```xml
> <ItemGroup>
>   <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
>     <_Parameter1>Miner49er</_Parameter1>
>   </AssemblyAttribute>
> </ItemGroup>
> ```
> Alternatively (cleaner, preferred if you'd rather not widen visibility): add a public `Simulation.KillMiner(int id)` method to Core that sets `Alive = false`, emits `MinerKilled`, and clears activity — and call that here instead. If you add it, also add a one-line Core test. Pick one approach and keep it consistent.

- [ ] **Step 2: Build (will fail until Task 12 adds NetworkManager.BroadcastTick/BroadcastResult)**

`NetworkManager.BroadcastTick(byte[])` and `BroadcastResult(long)` are added in Task 12. Defer the compile check to Task 12. Create the file now.

- [ ] **Step 3: Commit**

```bash
git add game/net/MatchHost.cs
git commit -m "feat(net): add host-authoritative match tick loop"
```

---

## Task 10: Client input capture & send

Client reads input each frame and sends it to the host. Implemented as a small node added by the match controller; the receiving RPCs live on NetworkManager (Task 12).

**Files:**
- Create: `game/net/InputSender.cs`

- [ ] **Step 1: Write InputSender**

`game/net/InputSender.cs`:
```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Reads local input each frame and forwards it to the host:
/// the current desired direction (or -1) every frame, and edge-triggered
/// mine/plant actions on key-down.</summary>
public partial class InputSender : Node
{
	public bool Enabled = true; // disabled when the local miner is dead (spectating)

	public override void _PhysicsProcess(double delta)
	{
		if (!Enabled) return;

		int dir = ReadDir();
		NetworkManager.Instance.SendDir(dir);

		bool mine = Input.IsActionJustPressed(InputBindings.Pickaxe);
		bool plant = Input.IsActionJustPressed(InputBindings.Plant);
		if (mine || plant) NetworkManager.Instance.SendAction(mine, plant);
	}

	private static int ReadDir()
	{
		if (Input.IsActionPressed(InputBindings.MoveUp)) return (int)Direction.North;
		if (Input.IsActionPressed(InputBindings.MoveDown)) return (int)Direction.South;
		if (Input.IsActionPressed(InputBindings.MoveLeft)) return (int)Direction.West;
		if (Input.IsActionPressed(InputBindings.MoveRight)) return (int)Direction.East;
		return -1;
	}
}
```

- [ ] **Step 2: Commit (compiles after Task 12 adds SendDir/SendAction)**

```bash
git add game/net/InputSender.cs
git commit -m "feat(net): add client input sender"
```

---

## Task 11: MatchClient — render replica

Per-peer render state: a replica grid (regenerated from seed), entity visuals smoothed toward the latest snapshot, local fog, and HUD. The existing renderers are repointed to read from this.

**Files:**
- Create: `game/net/MatchClient.cs`
- Modify: `game/WorldRenderer.cs`
- Modify: `game/FogRenderer.cs`

- [ ] **Step 1: Write MatchClient**

`game/net/MatchClient.cs`:
```csharp
using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Render-side world replica. Holds the grid (regenerated from seed),
/// applies tile changes + entity snapshots, smooths miner visuals toward their
/// authoritative positions, and computes local fog from the local miner.</summary>
public partial class MatchClient : Node
{
	public const int TileSize = 32;
	public const int VisionRadius = 5;
	public static readonly float MoveSpeedPixels = TileSize / (float)MatchHost.MoveStepSeconds;

	public TileGrid Grid { get; private set; } = null!;
	public FogState Fog { get; } = new();
	public IReadOnlyList<MinerSnapshot> Miners => _miners;
	public IReadOnlyList<ChargeSnapshot> Charges => _charges;
	public int LocalMinerId { get; private set; }

	private List<MinerSnapshot> _miners = new();
	private List<ChargeSnapshot> _charges = new();
	private readonly Dictionary<int, Vector2> _visualPos = new(); // minerId -> smoothed pixels

	private WorldRenderer _world = null!;
	private FogRenderer _fogRenderer = null!;
	private Node2D _camera = null!;

	public void Begin(TileGrid grid, int localMinerId, Node2D sceneRoot)
	{
		Grid = grid;
		LocalMinerId = localMinerId;

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -10 };
		sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);

		_camera = new Camera2D { Zoom = new Vector2(1.5f, 1.5f) };
		sceneRoot.AddChild(_camera);
		((Camera2D)_camera).MakeCurrent();
	}

	public void ApplyUpdate(TickUpdate update)
	{
		foreach (var t in update.TileChanges)
		{
			var p = new GridPos(t.X, t.Y);
			if (Grid.InBounds(p)) Grid.Set(p, TileType.Floor);
			if (t.FromBlast) _world?.AddExplosionFlash(p);
		}

		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		UpdateFog();
	}

	public Vector2 VisualPosOf(MinerSnapshot m)
	{
		var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
		if (!_visualPos.TryGetValue(m.Id, out var cur)) { _visualPos[m.Id] = target; return target; }
		return cur;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Smooth each miner visual toward its authoritative tile position.
		foreach (var m in _miners)
		{
			var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
			var cur = _visualPos.TryGetValue(m.Id, out var v) ? v : target;
			_visualPos[m.Id] = cur.MoveToward(target, MoveSpeedPixels * (float)delta);

			if (m.Id == LocalMinerId)
				_camera.Position = _visualPos[m.Id];
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		// Draw miners as colored squares (color via NetworkManager lobby info).
		foreach (var m in _miners)
		{
			if (!m.Alive) continue;
			var p = _visualPos.TryGetValue(m.Id, out var v) ? v : Vector2.Zero;
			var color = MinerColor(m.Id);
			DrawRect(new Rect2(p.X - 10, p.Y - 10, 20, 20), color);
		}
	}

	private static Color MinerColor(int minerId)
	{
		// minerId is 1-based spawn index; map to palette by index-1.
		return PlayerColors.At(minerId - 1);
	}

	private void UpdateFog()
	{
		foreach (var m in _miners)
			if (m.Id == LocalMinerId && m.Alive)
				Fog.Update(Visibility.Compute(Grid, new GridPos(m.X, m.Y), VisionRadius));
	}
}
```

> **Self-draw note:** `MatchClient` extends `Node`, which has no `_Draw`. To draw miners, make `MatchClient` extend `Node2D` instead (change `public partial class MatchClient : Node` → `: Node2D`) and give it `ZIndex = 5` when constructed. `Node2D` still has `_PhysicsProcess`. Apply that change when writing the file.

- [ ] **Step 2: Repoint WorldRenderer to MatchClient**

In `game/WorldRenderer.cs`, change the data source from `Main` to `MatchClient`. Replace the field and `Init`, and the `_Draw` grid/charge access:

```csharp
	private MatchClient _client = null!;

	public void Init(MatchClient client) => _client = client;
```
In `_Draw`, replace `_main` usages:
- `if (_main == null) return;` → `if (_client == null) return;`
- `var grid = _main.Sim.Grid;` → `var grid = _client.Grid;`
- `int ts = Main.TileSize;` → `int ts = MatchClient.TileSize;`
- The charge loop `foreach (var c in _main.Sim.Charges)` → iterate snapshots:
```csharp
		foreach (var c in _client.Charges)
		{
			var center = new Vector2(c.X * ts + ts / 2f, c.Y * ts + ts / 2f);
			DrawCircle(center, ts * 0.25f, ChargeColor);
		}
```
(`c.WallPos.X` → `c.X`, etc., since `ChargeSnapshot` exposes `X`/`Y`.)

- [ ] **Step 3: Repoint FogRenderer to MatchClient**

In `game/FogRenderer.cs`:
```csharp
	private MatchClient _client = null!;

	public void Init(MatchClient client) => _client = client;
```
In `_Draw`: `if (_main == null) return;` → `if (_client == null) return;`; `var grid = _main.Sim.Grid;` → `var grid = _client.Grid;`; `var fog = _main.Fog;` → `var fog = _client.Fog;`; `int ts = Main.TileSize;` → `int ts = MatchClient.TileSize;`.

- [ ] **Step 4: Commit (full compile happens in Task 12)**

```bash
git add game/net/MatchClient.cs game/WorldRenderer.cs game/FogRenderer.cs
git commit -m "feat(net): add client render replica and repoint renderers"
```

---

## Task 12: Match bootstrap, screen flow & results

Wire it together: `NetworkManager.StartMatch` (host) generates the map, assigns miners, and broadcasts `BeginMatch`; every peer loads `Main.tscn`, which builds a `MatchClient` (+ `MatchHost`/`InputSender` on the host). Add the transport RPCs the earlier tasks referenced, plus results.

**Files:**
- Modify: `game/net/NetworkManager.cs`
- Rewrite: `game/Main.cs`
- Create: `game/ui/ResultsOverlay.cs`

- [ ] **Step 1: Add match RPCs + state to NetworkManager**

Append to `game/net/NetworkManager.cs` (inside the class):
```csharp
	// Match bootstrap --------------------------------------------------------
	public event System.Action? MatchStarting;
	public event System.Action<long>? MatchEnded; // winner peerId, -1 = draw

	public int MatchSeed { get; private set; }
	public int MatchPlayerCount { get; private set; }
	public long[] PeerOrder { get; private set; } = System.Array.Empty<long>();

	private MatchHost? _matchHost;
	private MatchClient? _matchClient;

	public void RegisterMatch(MatchHost? host, MatchClient client)
	{
		_matchHost = host;
		_matchClient = client;
	}

	public void StartMatch()
	{
		if (!IsHost) return;
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = (int)(Time.GetUnixTimeFromSystem() % int.MaxValue);
		Rpc(nameof(BeginMatch), seed, order.Length, order);
		BeginMatch(seed, order.Length, order); // host applies locally too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}

	public int LocalMinerId()
	{
		for (int i = 0; i < PeerOrder.Length; i++)
			if (PeerOrder[i] == LocalId) return i + 1; // minerId = spawn index + 1
		return -1;
	}

	// Input transport --------------------------------------------------------
	public void SendDir(int dir)
	{
		if (IsHost) { _matchHost?.SetDir(LocalId, dir); return; }
		RpcId(1, nameof(ReceiveDir), dir);
	}

	public void SendAction(bool mine, bool plant)
	{
		if (IsHost) { _matchHost?.SetAction(LocalId, mine, plant); return; }
		RpcId(1, nameof(ReceiveAction), mine, plant);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveDir(int dir) => _matchHost?.SetDir(Multiplayer.GetRemoteSenderId(), dir);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveAction(bool mine, bool plant) =>
		_matchHost?.SetAction(Multiplayer.GetRemoteSenderId(), mine, plant);

	// Tick + result broadcast ------------------------------------------------
	public void BroadcastTick(byte[] bytes)
	{
		Rpc(nameof(ReceiveTick), bytes);
		ReceiveTick(bytes); // host renders its own view
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveTick(byte[] bytes) =>
		_matchClient?.ApplyUpdate(Miner49er.Core.Net.SnapshotCodec.Read(bytes));

	public void BroadcastResult(long winnerPeerId)
	{
		Rpc(nameof(ReceiveResult), winnerPeerId);
		ReceiveResult(winnerPeerId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void ReceiveResult(long winnerPeerId) => MatchEnded?.Invoke(winnerPeerId);

	// Return to lobby --------------------------------------------------------
	public void ReturnToLobby()
	{
		if (!IsHost) return;
		Rpc(nameof(GoToLobby));
		GoToLobby();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void GoToLobby() => MatchStarting?.Invoke(); // Lobby/Main listen and swap scenes
```

> Add `using System.Linq;` to the top of `NetworkManager.cs` for `.ToArray()`/`.Keys`. Note `GoToLobby` reuses no extra event — instead, the match scene listens for a dedicated `ReturnRequested` action; to keep it explicit, add `public event System.Action? ReturnToLobbyRequested;` and invoke it in `GoToLobby()` instead of `MatchStarting`. Wire `Main` to it in Step 2.

Replace the `GoToLobby` body accordingly:
```csharp
	public event System.Action? ReturnToLobbyRequested;

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void GoToLobby() => ReturnToLobbyRequested?.Invoke();
```

- [ ] **Step 2: Rewrite `game/Main.cs` as the match controller**

`game/Main.cs` (full replacement):
```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>In-match controller. On every peer it builds a MatchClient render
/// replica from the broadcast seed; on the host it additionally builds the
/// authoritative MatchHost and the local InputSender.</summary>
public partial class Main : Node2D
{
	private MatchClient _client = null!;
	private MatchHost? _host;
	private InputSender? _input;
	private Hud _hud = null!;
	private ResultsOverlay? _results;

	public override void _Ready()
	{
		var nm = NetworkManager.Instance;
		InputBindings.EnsureDefaults();

		int seed = nm.MatchSeed;
		int playerCount = nm.MatchPlayerCount;
		var map = MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = playerCount });

		int localMinerId = nm.LocalMinerId();

		_client = new MatchClient { Name = "MatchClient", ZIndex = 5 };
		AddChild(_client);
		_client.Begin(map.Grid, localMinerId, this);

		if (nm.IsHost)
		{
			var sim = new Simulation(MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = playerCount }).Grid, new SimConfig());
			var peerToMiner = new System.Collections.Generic.Dictionary<long, int>();
			for (int i = 0; i < nm.PeerOrder.Length; i++)
			{
				int minerId = i + 1;
				sim.AddMiner(minerId, map.Spawns[i]);
				peerToMiner[nm.PeerOrder[i]] = minerId;
			}
			_host = new MatchHost { Name = "MatchHost" };
			AddChild(_host);
			_host.Begin(sim, peerToMiner);
		}

		_input = new InputSender { Name = "InputSender" };
		AddChild(_input);

		_hud = new Hud { Name = "Hud" };
		AddChild(_hud);

		nm.RegisterMatch(_host, _client);
		nm.MatchEnded += OnMatchEnded;
		nm.ReturnToLobbyRequested += OnReturnToLobby;
		nm.Disconnected += OnDisconnected;
	}

	public override void _ExitTree()
	{
		var nm = NetworkManager.Instance;
		nm.MatchEnded -= OnMatchEnded;
		nm.ReturnToLobbyRequested -= OnReturnToLobby;
		nm.Disconnected -= OnDisconnected;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Disable input + HUD activity once the local miner is dead (spectate).
		bool localAlive = false;
		string status = "Spectating";
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
			{
				localAlive = m.Alive;
				status = m.Alive
					? (m.Activity == (int)ActivityKind.Mining ? $"Mining… {m.ActivityRemaining:0.0}s"
						: m.Activity == (int)ActivityKind.Planting ? $"Planting… {m.ActivityRemaining:0.0}s"
						: "Ready")
					: "Dead — spectating";
				_hud.SetText($"Gold: {m.Gold}    {status}");
			}
		if (_input != null) _input.Enabled = localAlive;
	}

	private void OnMatchEnded(long winnerPeerId)
	{
		if (_results != null) return;
		_results = new ResultsOverlay { Name = "ResultsOverlay" };
		AddChild(_results);
		string label = winnerPeerId == -1
			? "Draw — no survivors"
			: $"Winner: {NameOf(winnerPeerId)}";
		_results.Show(label, NetworkManager.Instance.IsHost);
	}

	private static string NameOf(long peerId) =>
		NetworkManager.Instance.Players.TryGetValue(peerId, out var info) ? info.Name : $"Peer {peerId}";

	private void OnReturnToLobby() => GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");

	private void OnDisconnected() => GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
}
```

- [ ] **Step 3: Write ResultsOverlay**

`game/ui/ResultsOverlay.cs`:
```csharp
using Godot;

namespace Miner49er;

public partial class ResultsOverlay : CanvasLayer
{
	private Label _label = null!;
	private Button _return = null!;

	public override void _Ready()
	{
		Layer = 50;
		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);

		var box = new VBoxContainer();
		center.AddChild(box);

		_label = new Label();
		_label.AddThemeFontSizeOverride("font_size", 40);
		box.AddChild(_label);

		_return = new Button { Text = "Return to Lobby" };
		_return.Pressed += () => NetworkManager.Instance.ReturnToLobby();
		box.AddChild(_return);
	}

	public void Show(string text, bool hostControls)
	{
		_label.Text = text;
		_return.Visible = hostControls; // only the host returns everyone to the lobby
	}
}
```

- [ ] **Step 4: Full build — everything should now compile**

Run: `dotnet build Miner49er.csproj`
Expected: build succeeds, 0 errors. (Resolves the deferred compiles from Tasks 8–11.)

- [ ] **Step 5: Headless boot smoke**

Run: `godot --headless --quit-after 180`
Expected: exit 0, no errors (Splash → MainMenu loads; no match started in headless).

- [ ] **Step 6: Two-instance manual play-test**

From the project dir, open two windowed instances: run `godot .` twice (or launch once and use the editor's second-run). In instance A: name yourself, **Host Game**. In instance B: keep `127.0.0.1`, **Join Game**. Both should land in the Lobby and see two players. Toggle **Ready** in both; host clicks **Start Match**.
Verify: both windows enter the mine; each sees its own fog; moving in one window moves that miner in the other; mining/planting/blasting replicate; a player caught in a blast dies and switches to "spectating"; when one remains, both see the results banner; host clicks **Return to Lobby** and both return.

- [ ] **Step 7: Commit**

```bash
git add game/net/NetworkManager.cs game/Main.cs game/ui/ResultsOverlay.cs
git commit -m "feat(net): wire match bootstrap, sync, and results flow"
```

---

## Task 13: Disconnect handling

Make client and host disconnects graceful during a match.

**Files:**
- Modify: `game/net/NetworkManager.cs`

- [ ] **Step 1: Eliminate a dropped client's miner on the host**

In `game/net/NetworkManager.cs`, update `OnPeerDisconnected` so that during a match the host eliminates the miner (lobby removal already handled):
```csharp
	private void OnPeerDisconnected(long id)
	{
		if (!IsHost) return;
		_matchHost?.EliminatePeer(id);   // mid-match: treat as eliminated
		if (Players.Remove(id)) BroadcastLobby(); // lobby: drop from list
	}
```

- [ ] **Step 2: Host loss already returns clients to the menu**

Confirm `OnServerDisconnected` (Task 5) clears state and raises `Disconnected`, and that `Main.OnDisconnected` / `Lobby.OnDisconnected` change scene to `MainMenu.tscn`. No code change if both are wired; if the match scene doesn't subscribe, ensure `Main._Ready` has `nm.Disconnected += OnDisconnected;` (added in Task 12 Step 2).

- [ ] **Step 3: Build**

Run: `dotnet build Miner49er.csproj`
Expected: 0 errors.

- [ ] **Step 4: Two-instance manual check**

Start a 2-player match (Task 12 Step 6). Close the **client** window mid-match: the host should see that miner die and the round resolve to the host as winner. Restart, then close the **host** window mid-match: the client should return to the Main Menu with no crash.

- [ ] **Step 5: Commit**

```bash
git add game/net/NetworkManager.cs
git commit -m "feat(net): handle client and host disconnects during a match"
```

---

## Final verification

- [ ] **Run the full Core test suite:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` → all green (Phase 1 + Tasks 1–4).
- [ ] **Build:** `dotnet build Miner49er.csproj` → 0 errors.
- [ ] **Headless boot:** `godot --headless --quit-after 180` → exit 0, no errors.
- [ ] **Manual 3-instance test** (optional but recommended): one host + two clients; verify lobby list, fog independence, replication, two deaths resolving to a single winner, and return-to-lobby for another round.
- [ ] **Update memory** `phase1-status.md` / add a `phase2-status.md`: Phase 2 implemented on `phase2-multiplayer`, what was manually verified, and that prediction / NAT / visibility-culling remain Phase 5.

---

## Notes carried forward (not built here)

- **§3.5 movement speed / status effects** — Phase 2 hard-codes the 0.12 s move cadence in `MatchHost.MoveStepSeconds`. When §3.5 lands, that cadence moves into Core per-miner state and `MatchHost` reads it from the sim.
- **Client-side prediction**, **NAT/relay**, **per-peer visibility culling (cheat-resistant fog)**, **LAN discovery**, and the **full settings menu** are all Phase 5.
