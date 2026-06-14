# Phase 5: Infra-free internet play (UPnP + connect code) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a host open their match to the internet with no hosted services — UPnP auto-forwards the port and reports the public IP, and the host shares a short typo-resistant code that friends paste to join.

**Architecture:** One new pure-C# unit in the engine (`ConnectCode`, the only unit-testable piece); a Godot `UpnpService` that does blocking discovery/mapping on background threads; `NetworkManager` gains an `overInternet` host flag, status/code/event, and a unified `JoinByCode`; the main menu adds a toggle + code/IP join field and the lobby shows a host-only code panel. The lobby/match/RPC/sync flow is otherwise untouched.

**Tech Stack:** .NET 8, C#, xUnit; Godot 4.6.3 (.NET/Mono), `Godot.Upnp`.

---

## Conventions & guardrails (read first)

- **Indentation:** `src/Miner49er.Core/**` and all test files use **4 spaces**. `game/**` uses **TAB** indentation. Match the file you are editing exactly.
- **Build:** `dotnet build Miner49er.sln`
- **Core tests:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- **Godot headless:** run `godot` **via PowerShell ONLY** (never the Bash tool — its shim breaks headless with a false "assemblies not found").
- **Staging:** stage the **exact files** each task lists with `git add <paths>`. **Never** `git add -A` — the working tree has pre-existing untracked files (`assets/Splash.png*`, `.superpowers/`, `*.uid`, CRLF-only `project.godot`/`game/Splash.tscn`) that must NOT be committed.
- **Commit trailer:** every commit message MUST end with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- **Branch:** work happens on `phase5-internet-play` (already created off `main` @ `01259a6`; spec committed at `8d92f64`). Do not merge/push without explicit user authorization.
- **Baseline:** Core **290/290** pass at `main` `01259a6`. Each task keeps the suite green; only Task 1 adds Core tests.
- **Spec:** `docs/superpowers/specs/2026-06-14-phase5-internet-play-design.md`.

---

## File Structure

**Core (create):**
- `src/Miner49er.Core/Net/ConnectCode.cs` — pure-C# IPv4+port ↔ share-code codec.
- `src/Miner49er.Core.Tests/ConnectCodeTests.cs`

**Game (create):**
- `game/net/UpnpService.cs` — background-thread UPnP discovery/mapping/cleanup.

**Game (modify):**
- `game/net/NetworkManager.cs` — `InternetStatus` enum, `overInternet` host flag, status/code/event, `OnUpnpComplete`, `JoinByCode`, release on `Leave`.
- `game/ui/MainMenu.cs` — "Host over internet" checkbox, code/IP join field.
- `game/ui/Lobby.cs` — host-only code/status panel with Copy.

---

## Task 1: `ConnectCode` codec (Core, TDD)

**Files:**
- Create: `src/Miner49er.Core/Net/ConnectCode.cs`
- Test: `src/Miner49er.Core.Tests/ConnectCodeTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/ConnectCodeTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core.Net;
using Xunit;

public class ConnectCodeTests
{
    [Theory]
    [InlineData(192, 168, 1, 5, 27649)]
    [InlineData(203, 0, 113, 7, 27649)]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255, 65535)]
    [InlineData(10, 0, 0, 1, 1)]
    public void Round_trips_ip_and_port(byte a, byte b, byte c, byte d, int port)
    {
        var code = ConnectCode.Encode(new[] { a, b, c, d }, (ushort)port);
        Assert.True(ConnectCode.TryDecode(code, out var ip, out var back));
        Assert.Equal(new byte[] { a, b, c, d }, ip);
        Assert.Equal((ushort)port, back);
    }

    [Fact]
    public void Code_is_eleven_chars()
    {
        var code = ConnectCode.Encode(new byte[] { 203, 0, 113, 7 }, 27649);
        Assert.Equal(11, code.Length);
    }

    [Fact]
    public void Decode_is_case_insensitive_and_ignores_spaces_and_dashes()
    {
        var code = ConnectCode.Encode(new byte[] { 203, 0, 113, 7 }, 27649);
        var noisy = "  " + string.Join("-", code.ToLowerInvariant().Select(c => c.ToString())) + " ";
        Assert.True(ConnectCode.TryDecode(noisy, out var ip, out var port));
        Assert.Equal(new byte[] { 203, 0, 113, 7 }, ip);
        Assert.Equal((ushort)27649, port);
    }

    [Fact]
    public void Rejects_a_single_character_typo()
    {
        var code = ConnectCode.Encode(new byte[] { 203, 0, 113, 7 }, 27649).ToCharArray();
        code[0] = code[0] == '0' ? '1' : '0';   // flip a data char; checksum char untouched
        Assert.False(ConnectCode.TryDecode(new string(code), out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("WAYTOOLONG12345")]
    [InlineData("!!!!!!!!!!!")]
    public void Rejects_malformed_input(string bad)
    {
        Assert.False(ConnectCode.TryDecode(bad, out _, out _));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(ConnectCode.TryDecode(null!, out _, out _));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: FAIL — `ConnectCode` does not exist (compile error).

- [ ] **Step 3: Implement the codec**

Create `src/Miner49er.Core/Net/ConnectCode.cs`:

```csharp
using System;

namespace Miner49er.Core.Net;

/// <summary>Encodes a host endpoint (IPv4 + port) as a short, typo-resistant
/// share code. Crockford base32 over a 6-byte payload (4 IP + 2 port) gives 10
/// data chars; a trailing Crockford mod-37 check char rejects mistyped codes.
/// Pure C# (no Godot/sockets) so it is fully unit-testable.</summary>
public static class ConnectCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // 32 symbols (no I L O U)
    private const string CheckAlphabet = Alphabet + "*~$=U";              // 37 symbols (mod-37 check)

    public static string Encode(byte[] ipv4, ushort port)
    {
        if (ipv4 is null || ipv4.Length != 4)
            throw new ArgumentException("ipv4 must be 4 bytes", nameof(ipv4));

        var payload = new byte[6];
        payload[0] = ipv4[0]; payload[1] = ipv4[1]; payload[2] = ipv4[2]; payload[3] = ipv4[3];
        payload[4] = (byte)(port >> 8); payload[5] = (byte)(port & 0xFF);

        ulong v = 0;
        foreach (var b in payload) v = (v << 8) | b;   // 48-bit value

        var chars = new char[11];
        for (int i = 9; i >= 0; i--) { chars[i] = Alphabet[(int)(v & 31)]; v >>= 5; }
        chars[10] = CheckAlphabet[Checksum(payload)];
        return new string(chars);
    }

    public static bool TryDecode(string code, out byte[] ipv4, out ushort port)
    {
        ipv4 = Array.Empty<byte>();
        port = 0;
        if (code is null) return false;

        // Normalize: uppercase, drop spaces/dashes, map Crockford-equivalent glyphs.
        var sb = new System.Text.StringBuilder(code.Length);
        foreach (var raw in code)
        {
            char c = char.ToUpperInvariant(raw);
            if (c == ' ' || c == '-') continue;
            if (c == 'I' || c == 'L') c = '1';
            else if (c == 'O') c = '0';
            sb.Append(c);
        }
        var norm = sb.ToString();
        if (norm.Length != 11) return false;

        ulong v = 0;
        for (int i = 0; i < 10; i++)
        {
            int idx = Alphabet.IndexOf(norm[i]);
            if (idx < 0) return false;
            v = (v << 5) | (ulong)idx;
        }

        var payload = new byte[6];
        payload[0] = (byte)((v >> 40) & 0xFF);
        payload[1] = (byte)((v >> 32) & 0xFF);
        payload[2] = (byte)((v >> 24) & 0xFF);
        payload[3] = (byte)((v >> 16) & 0xFF);
        payload[4] = (byte)((v >> 8) & 0xFF);
        payload[5] = (byte)(v & 0xFF);

        int provided = CheckAlphabet.IndexOf(norm[10]);
        if (provided < 0 || provided != Checksum(payload)) return false;

        ipv4 = new byte[] { payload[0], payload[1], payload[2], payload[3] };
        port = (ushort)((payload[4] << 8) | payload[5]);
        return true;
    }

    private static int Checksum(byte[] payload)
    {
        int mod = 0;
        foreach (var b in payload) mod = (mod * 256 + b) % 37;
        return mod;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (full suite green; +~10 new cases over the 290 baseline).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Net/ConnectCode.cs src/Miner49er.Core.Tests/ConnectCodeTests.cs
git commit -m "$(printf 'feat(core): ConnectCode share-code codec (Crockford base32 + checksum)\n\nEncodes IPv4+port to an 11-char typo-resistant token; TryDecode is\ncase/space/dash tolerant and rejects bad-checksum codes.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 2: `UpnpService` + `NetworkManager` internet wiring (game, TAB indent)

**Files (TAB indent — match each file's existing tabs exactly):**
- Create: `game/net/UpnpService.cs`
- Modify: `game/net/NetworkManager.cs`

> No automated test harness exists for `game/`; verification is `dotnet build Miner49er.sln` (0/0). `HostGame` gains a **defaulted** `overInternet = false` param so the existing `MainMenu` 2-arg call still compiles; `JoinByCode` is additive. The build stays green after this task alone.

- [ ] **Step 1: Create the UPnP service**

Create `game/net/UpnpService.cs` (TAB indent):

```csharp
using System;
using System.Net;
using System.Threading.Tasks;
using Godot;

namespace Miner49er;

/// <summary>Best-effort UPnP port mapping for internet hosting. Discovery and
/// deletion block, so they run on background threads; the result is reported via
/// a callback the caller marshals back to the main thread. ENet is UDP, so the
/// mapping uses the UDP protocol.</summary>
public sealed class UpnpService
{
	private Upnp? _upnp;   // discovered gateway instance, kept for clean deletion
	private int _port;
	private bool _mapped;

	// Runs discovery + mapping on a background thread. onComplete(success, publicIp)
	// fires on that thread; the caller marshals to the main thread.
	public void Open(int port, Action<bool, string> onComplete)
	{
		_port = port;
		Task.Run(() =>
		{
			try
			{
				var upnp = new Upnp();
				if (upnp.Discover() != (int)Upnp.UpnpResult.Success || upnp.GetGatewayCount() == 0)
				{
					onComplete(false, "");
					return;
				}
				int add = upnp.AddPortMapping(port, port, "Miner49er", "UDP", 0);
				string ext = upnp.QueryExternalAddress();
				bool ok = add == (int)Upnp.UpnpResult.Success
				          && IPAddress.TryParse(ext, out var parsed)
				          && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
				if (ok) { _upnp = upnp; _mapped = true; }
				onComplete(ok, ok ? ext : "");
			}
			catch
			{
				onComplete(false, "");
			}
		});
	}

	// Removes the mapping on a background thread, reusing the discovered gateway.
	public void Release()
	{
		if (!_mapped || _upnp is null) return;
		var upnp = _upnp;
		int port = _port;
		_mapped = false;
		_upnp = null;
		Task.Run(() => { try { upnp.DeletePortMapping(port, "UDP"); } catch { /* best effort */ } });
	}
}
```

- [ ] **Step 2: Add the `InternetStatus` enum**

In `game/net/NetworkManager.cs`, add a top-level enum in the `Miner49er` namespace, just above the `PlayerInfo` struct:

```csharp
public enum InternetStatus { Off, Discovering, Mapped, Failed }
```

- [ ] **Step 3: Add the `System.Net` using**

In `game/net/NetworkManager.cs`, add to the using block (after `using System;`):

```csharp
using System.Net;
```

- [ ] **Step 4: Add the service field, status, code, and event**

In `game/net/NetworkManager.cs`, after the `public readonly Dictionary<long, PlayerInfo> Players = new();` line, add:

```csharp
	private readonly UpnpService _upnp = new();
	private int _internetPort = DefaultPort;
	public InternetStatus Status { get; private set; } = InternetStatus.Off;
	public string? HostCode { get; private set; }
	public event Action? InternetStatusChanged;
```

- [ ] **Step 5: Extend `HostGame` and add `OnUpnpComplete`**

In `game/net/NetworkManager.cs`, replace the whole `HostGame` method with:

```csharp
	public Error HostGame(string playerName, int colorIndex, bool overInternet = false, int port = DefaultPort)
	{
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateServer(port, 8);
		if (err != Error.Ok) return err;
		Multiplayer.MultiplayerPeer = peer;
		IsHost = true;
		Players.Clear();
		Players[LocalId] = new PlayerInfo { Name = playerName, ColorIndex = colorIndex, Ready = false };
		LobbyChanged?.Invoke();

		if (overInternet)
		{
			_internetPort = port;
			Status = InternetStatus.Discovering;
			HostCode = null;
			InternetStatusChanged?.Invoke();
			_upnp.Open(port, (ok, ip) =>
				Callable.From(() => OnUpnpComplete(ok, ip)).CallDeferred());   // back to main thread
		}
		return Error.Ok;
	}

	private void OnUpnpComplete(bool ok, string ip)
	{
		if (ok && IPAddress.TryParse(ip, out var addr))
		{
			HostCode = Miner49er.Core.Net.ConnectCode.Encode(addr.GetAddressBytes(), (ushort)_internetPort);
			Status = InternetStatus.Mapped;
		}
		else
		{
			HostCode = null;
			Status = InternetStatus.Failed;
		}
		InternetStatusChanged?.Invoke();
	}
```

- [ ] **Step 6: Add `JoinByCode`**

In `game/net/NetworkManager.cs`, add this method immediately after the existing `JoinGame` method:

```csharp
	// Accepts either a share code or a raw address[:port]. Codes decode to ip+port;
	// anything else is treated as a direct address with an optional :port suffix.
	public Error JoinByCode(string input, string playerName, int colorIndex)
	{
		var trimmed = (input ?? "").Trim();
		if (Miner49er.Core.Net.ConnectCode.TryDecode(trimmed, out var ip, out var port))
			return JoinGame(new IPAddress(ip).ToString(), playerName, colorIndex, port);

		int p = DefaultPort;
		int idx = trimmed.LastIndexOf(':');
		if (idx > 0 && int.TryParse(trimmed[(idx + 1)..], out var parsed))
		{
			p = parsed;
			trimmed = trimmed[..idx];
		}
		return JoinGame(trimmed, playerName, colorIndex, p);
	}
```

- [ ] **Step 7: Release the mapping on `Leave`**

In `game/net/NetworkManager.cs`, replace the `Leave` method with:

```csharp
	public void Leave()
	{
		_upnp.Release();
		Status = InternetStatus.Off;
		HostCode = null;
		Multiplayer.MultiplayerPeer = null;
		IsHost = false;
		Players.Clear();
	}
```

- [ ] **Step 8: Build**

Run: `dotnet build Miner49er.sln`
Expected: 0 Warning(s), 0 Error(s). (The existing `MainMenu` calls still compile via the defaulted `overInternet`.)

- [ ] **Step 9: Commit**

```bash
git add game/net/UpnpService.cs game/net/NetworkManager.cs
git commit -m "$(printf 'feat(game): UPnP internet hosting + JoinByCode in NetworkManager\n\nUpnpService maps the port and queries the public IP on background\nthreads; HostGame(overInternet) reports Off/Discovering/Mapped/Failed\nand builds a ConnectCode; JoinByCode accepts a code or raw IP; Leave\nreleases the mapping.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 3: MainMenu — internet toggle + code/IP join (game, TAB indent)

**Files (TAB indent):**
- Modify: `game/ui/MainMenu.cs`

- [ ] **Step 1: Add the checkbox field**

In `game/ui/MainMenu.cs`, add a field after `private OptionButton _color = null!;`:

```csharp
	private CheckBox _internet = null!;
```

- [ ] **Step 2: Create the checkbox and relabel the join field**

In `game/ui/MainMenu.cs`, replace the `_address` creation line:

```csharp
		_address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Host IP" };
		box.AddChild(_address);
```

with:

```csharp
		_address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Code or Host IP" };
		box.AddChild(_address);

		_internet = new CheckBox { Text = "Host over internet (UPnP)", ButtonPressed = true };
		box.AddChild(_internet);
```

- [ ] **Step 3: Pass the flag to `HostGame` and route Join through `JoinByCode`**

In `game/ui/MainMenu.cs`, in `OnHost`, replace:

```csharp
		var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected);
```

with:

```csharp
		var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected, _internet.ButtonPressed);
```

And in `OnJoin`, replace:

```csharp
		var err = NetworkManager.Instance.JoinGame(_address.Text, _name.Text, _color.Selected);
```

with:

```csharp
		var err = NetworkManager.Instance.JoinByCode(_address.Text, _name.Text, _color.Selected);
```

- [ ] **Step 4: Build**

Run: `dotnet build Miner49er.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Commit**

```bash
git add game/ui/MainMenu.cs
git commit -m "$(printf 'feat(game): main-menu internet toggle and code/IP join field\n\nHost over internet (UPnP) checkbox (default on) feeds HostGame; the\naddress field now accepts a code or IP via JoinByCode.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Task 4: Lobby — host-only code panel (game, TAB indent)

**Files (TAB indent):**
- Modify: `game/ui/Lobby.cs`

- [ ] **Step 1: Add the panel fields**

In `game/ui/Lobby.cs`, add fields after `private OptionButton _speedPicker = null!;`:

```csharp
	private Label _codeLabel = null!;
	private Button _copyBtn = null!;
```

- [ ] **Step 2: Build the panel and subscribe**

In `game/ui/Lobby.cs`, in `_Ready`, insert this block immediately after the `_list` creation block (after `box.AddChild(_list);`):

```csharp
		_codeLabel = new Label { Text = "", Visible = false };
		box.AddChild(_codeLabel);

		_copyBtn = new Button { Text = "Copy code", Visible = false };
		_copyBtn.Pressed += () => { if (NetworkManager.Instance.HostCode is { } c) DisplayServer.ClipboardSet(c); };
		box.AddChild(_copyBtn);
```

Then, in `_Ready`, in the subscription block near the bottom (after `NetworkManager.Instance.MatchStarting += OnMatchStarting;`), add:

```csharp
		NetworkManager.Instance.InternetStatusChanged += RefreshInternet;
```

And change the existing trailing `Refresh();` call to:

```csharp
		Refresh();
		RefreshInternet();   // reflect status that may have resolved during the scene change
```

- [ ] **Step 3: Add `RefreshInternet` and unsubscribe**

In `game/ui/Lobby.cs`, add this method (e.g. after `Refresh`):

```csharp
	private void RefreshInternet()
	{
		if (!NetworkManager.Instance.IsHost) return;   // joiners never see the host code
		var nm = NetworkManager.Instance;
		switch (nm.Status)
		{
			case InternetStatus.Discovering:
				_codeLabel.Visible = true;
				_codeLabel.Text = "Opening router…";
				_copyBtn.Visible = false;
				break;
			case InternetStatus.Mapped:
				_codeLabel.Visible = true;
				_codeLabel.Text = $"Internet code: {nm.HostCode}";
				_copyBtn.Visible = true;
				break;
			case InternetStatus.Failed:
				_codeLabel.Visible = true;
				_codeLabel.Text = "Couldn't open your router automatically (UPnP unavailable).\n"
					+ "LAN players can still join via your local address.\n"
					+ "For internet play, forward port 27649 and share your public IP.";
				_copyBtn.Visible = false;
				break;
			default: // Off
				_codeLabel.Visible = false;
				_copyBtn.Visible = false;
				break;
		}
	}
```

And in `_ExitTree`, add the matching unsubscribe (after `NetworkManager.Instance.MatchStarting -= OnMatchStarting;`):

```csharp
		NetworkManager.Instance.InternetStatusChanged -= RefreshInternet;
```

- [ ] **Step 4: Build**

Run: `dotnet build Miner49er.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Godot headless smoke (PowerShell ONLY)**

Run (PowerShell): `godot --headless --import` then `godot --headless --quit`.
Expected: both exit 0 with no C#/assembly load errors.

- [ ] **Step 6: Commit**

```bash
git add game/ui/Lobby.cs
git commit -m "$(printf 'feat(game): host-only lobby internet code panel\n\nShows Opening router… / the share code with a Copy button / the UPnP\nfailure notice, driven by NetworkManager.InternetStatusChanged.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## Completion

After all tasks: Core suite green (290 + ConnectCode cases), `dotnet build Miner49er.sln` clean (0/0), Godot headless exits 0.

- **REQUIRED SUB-SKILL:** Use superpowers:finishing-a-development-branch to verify tests, then present merge/PR/keep/discard options. The user gates merges with an explicit play-test (here: host with the toggle on, copy the code, have a friend on another network join). They may waive with a direct "merge and push".

## Self-review notes (plan author)

- **Spec coverage:** §2 ConnectCode → T1; §3 UpnpService → T2 (Step 1); §4 NetworkManager (status/code/event, HostGame flag, OnUpnpComplete, JoinByCode, Leave release) → T2 (Steps 2–7); §5 MainMenu → T3, Lobby panel → T4; §6 error/threading (background threads, CallDeferred marshalling, raw-address fallthrough) → T2; §7 testing → T1 (xUnit) + T4 (headless smoke) + play-test gate at completion. All covered.
- **Type consistency:** `InternetStatus { Off, Discovering, Mapped, Failed }` defined in T2 Step 2, consumed in T4 Step 3. `HostGame(string, int, bool overInternet = false, int port = DefaultPort)` (T2) called 2-arg in baseline and 3-arg in T3. `JoinByCode(string, string, int)` defined T2, called T3. `UpnpService.Open(int, Action<bool,string>)` / `Release()` defined T2 Step 1, used T2 Step 5/7. `ConnectCode.Encode(byte[], ushort)` / `TryDecode(string, out byte[], out ushort)` defined T1, used in T2. `InternetStatusChanged` event defined T2, subscribed T4.
- **Ordering / build-green:** Task 2 keeps the build compiling on its own via the defaulted `overInternet`; T3 then wires the real flag. Each task ends green.
