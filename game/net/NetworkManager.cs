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
}
