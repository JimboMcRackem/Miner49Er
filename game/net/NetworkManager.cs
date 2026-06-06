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
