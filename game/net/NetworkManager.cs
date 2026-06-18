using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Miner49er.Core;

namespace Miner49er;

public enum InternetStatus { Off, Discovering, Mapped, Failed }

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

	private readonly UpnpService _upnp = new();
	private int _internetPort = DefaultPort;
	public InternetStatus Status { get; private set; } = InternetStatus.Off;
	public string? HostCode { get; private set; }
	public event Action? InternetStatusChanged;

	public event Action? LobbyChanged;
	public event Action? JoinFailed;
	public event Action? Disconnected;

	private string _pendingName = "Miner";
	private int _pendingColor;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		InputBindings.EnsureDefaults(); // register actions app-wide (menu/lobby/match), incl. Exit
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;
	}

	public Error HostGame(string playerName, int colorIndex, bool overInternet = false, int port = DefaultPort)
	{
		ResetPeer(); // free the port if a prior session's peer is still bound (e.g. replaying solo)
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

	public Error JoinGame(string address, string playerName, int colorIndex, int port = DefaultPort)
	{
		ResetPeer(); // drop any prior session's peer before connecting fresh
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateClient(address, port);
		if (err != Error.Ok) return err;
		Multiplayer.MultiplayerPeer = peer;
		IsHost = false;
		_pendingName = playerName;
		_pendingColor = colorIndex;
		return Error.Ok;
	}

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

	public void Leave()
	{
		_upnp.Release();
		Status = InternetStatus.Off;
		HostCode = null;
		ResetPeer();
		IsHost = false;
		Players.Clear();
	}

	// Closes the active ENet peer and detaches it. Closing explicitly (not just
	// nulling MultiplayerPeer) ensures the UDP socket is released so a later
	// CreateServer on the same port doesn't fail with CantCreate.
	private void ResetPeer()
	{
		if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer p)
			p.Close();
		Multiplayer.MultiplayerPeer = null;
	}

	// Connection callbacks ---------------------------------------------------

	private void OnPeerConnected(long id)
	{
		// Host waits for the joiner's SubmitPlayerInfo; nothing to do yet.
	}

	private void OnPeerDisconnected(long id)
	{
		if (!IsHost) return;
		_matchHost?.EliminatePeer(id);   // mid-match: treat as eliminated
		if (Players.Remove(id)) BroadcastLobby(); // lobby: drop from list
	}

	private void OnConnectedToServer()
	{
		RpcId(1, nameof(SubmitPlayerInfo), _pendingName, _pendingColor);
	}

	private void OnConnectionFailed() { Multiplayer.MultiplayerPeer = null; JoinFailed?.Invoke(); }
	private void OnServerDisconnected() { Multiplayer.MultiplayerPeer = null; Players.Clear(); Disconnected?.Invoke(); }

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
		var readys = new int[Players.Count]; // bool[] is not a Godot Variant; use int (0/1) instead
		int i = 0;
		foreach (var (id, info) in Players)
		{
			ids[i] = id; names[i] = info.Name; colors[i] = info.ColorIndex; readys[i] = info.Ready ? 1 : 0; i++;
		}
		Rpc(nameof(ReceiveLobby), ids, names, colors, readys);
		ReceiveLobby(ids, names, colors, readys); // apply locally on host too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void ReceiveLobby(long[] ids, string[] names, int[] colors, int[] readys)
	{
		Players.Clear();
		for (int i = 0; i < ids.Length; i++)
			Players[ids[i]] = new PlayerInfo { Name = names[i], ColorIndex = colors[i], Ready = readys[i] != 0 };
		LobbyChanged?.Invoke();
	}

	// Match bootstrap --------------------------------------------------------
	public event System.Action? MatchStarting;
	public event System.Action<long>? MatchEnded; // winner peerId, -1 = draw
	public event System.Action? ReturnToLobbyRequested;

	public int MatchSeed { get; private set; }
	public int MatchPlayerCount { get; private set; }
	public GameMode MatchMode { get; private set; }
	public int MatchTimeLimitSeconds { get; private set; }
	public bool MatchFlooding { get; private set; }
	public bool MatchPits { get; private set; }
	public bool MatchCaveIns { get; private set; }
	public bool MatchLava { get; private set; }
	public float MatchBaseMoveSeconds { get; private set; } = 0.12f;
	public int MatchMapScale { get; set; } = 1;
	public long[] PeerOrder { get; private set; } = System.Array.Empty<long>();
	public int MatchFloor { get; set; } = 1;

	public event System.Action<int>? NewFloor;

	public void BroadcastNewFloor(int floor)
	{
		MatchFloor = floor;
		Rpc(nameof(ReceiveNewFloor), floor);
		ReceiveNewFloor(floor);   // host applies locally too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveNewFloor(int floor)
	{
		MatchFloor = floor;
		NewFloor?.Invoke(floor);
	}

	private MatchHost? _matchHost;
	private MatchClient? _matchClient;

	public void RegisterMatch(MatchHost? host, MatchClient? client)
	{
		_matchHost = host;
		_matchClient = client;
	}

	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, order); // host applies locally too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		MatchFlooding = flooding;
		MatchPits = pits;
		MatchCaveIns = caveIns;
		MatchLava = lava;
		MatchBaseMoveSeconds = baseMoveSeconds;
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

	public void SendAction(bool mine, bool plant, bool use)
	{
		if (IsHost) { _matchHost?.SetAction(LocalId, mine, plant, use); return; }
		RpcId(1, nameof(ReceiveAction), mine, plant, use);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveDir(int dir) => _matchHost?.SetDir(Multiplayer.GetRemoteSenderId(), dir);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveAction(bool mine, bool plant, bool use) =>
		_matchHost?.SetAction(Multiplayer.GetRemoteSenderId(), mine, plant, use);

	// Tick + result broadcast ------------------------------------------------
	public void BroadcastTick(byte[] bytes)
	{
		Rpc(nameof(ReceiveTick), bytes);
		ReceiveTick(bytes); // host renders its own view
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
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
	public void GoToLobby() => ReturnToLobbyRequested?.Invoke();
}
