using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Miner49er.Core;
using Miner49er.Core.AI;

namespace Miner49er;

public enum InternetStatus { Off, Discovering, Mapped, Failed }

public struct PlayerInfo
{
	public string Name;
	public int ColorIndex;
	public int VariantIndex;
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
	// When Status == Failed, a one-line diagnostic explaining which UPnP step failed.
	public string InternetFailHint { get; private set; } = "";
	public event Action? InternetStatusChanged;

	public event Action? LobbyChanged;
	public event Action? JoinFailed;
	public event Action? Disconnected;

	private string _pendingName = "Miner";
	private int _pendingColor;
	private int _pendingVariant;

	// Bot management (host-only) ────────────────────────────────────────────
	private long _nextBotFakeId = -1001;
	private readonly Dictionary<long, BotSkill> _botSkills = new();

	private static readonly string[] BotNamePool =
		{ "Dusty", "Rocky", "Gravel", "Nugget", "Pickaxe", "Blaster", "Shafty", "Copperhead",
		  "Shale", "Flint", "Chip", "Pebble", "Cobalt", "Granite", "Quartz", "Pyrite",
		  "Tunnel", "Digger", "Chisel", "Hammer", "Drill", "Slaggy", "Goldie", "Craggy" };

	public override void _EnterTree() => Instance = this;

	// Save window placement when the player closes the window (X / Alt+F4) so the app
	// reopens where it was. In-app quit paths call SaveWindowGeometry() directly.
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
			SettingsStore.SaveWindowGeometry();
	}

	public override void _Ready()
	{
		InputBindings.EnsureDefaults(); // register actions app-wide (menu/lobby/match), incl. Exit
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;
	}

	public Error HostGame(string playerName, int colorIndex, bool overInternet = false, int port = DefaultPort, int variantIndex = 0)
	{
		ResetPeer(); // free the port if a prior session's peer is still bound (e.g. replaying solo)
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateServer(port, 7); // 7 clients + 1 host = 8 total
		if (err != Error.Ok) return err;
		Multiplayer.MultiplayerPeer = peer;
		IsHost = true;
		_botSkills.Clear();
		_nextBotFakeId = -1001;
		Players.Clear();
		Players[LocalId] = new PlayerInfo { Name = playerName, ColorIndex = colorIndex, VariantIndex = variantIndex, Ready = false };
		LobbyChanged?.Invoke();

		if (overInternet)
		{
			_internetPort = port;
			Status = InternetStatus.Discovering;
			HostCode = null;
			InternetStatusChanged?.Invoke();
			_upnp.Open(port, (ok, ip, reason) =>
				Callable.From(() => OnUpnpComplete(ok, ip, reason)).CallDeferred());   // back to main thread
		}
		return Error.Ok;
	}

	private void OnUpnpComplete(bool ok, string ip, UpnpFailure reason)
	{
		if (ok && IPAddress.TryParse(ip, out var addr))
		{
			HostCode = Miner49er.Core.Net.ConnectCode.Encode(addr.GetAddressBytes(), (ushort)_internetPort);
			Status = InternetStatus.Mapped;
			InternetFailHint = "";
		}
		else
		{
			HostCode = null;
			Status = InternetStatus.Failed;
			InternetFailHint = UpnpFailHint(reason);
		}
		InternetStatusChanged?.Invoke();
	}

	// One-line, player-facing explanation of a UPnP failure, tuned to the step that
	// failed: router-fixable causes vs. CGNAT (which no port setting can fix).
	private static string UpnpFailHint(UpnpFailure reason) => reason switch
	{
		UpnpFailure.NoGateway    => $"No UPnP gateway — enable UPnP on your router, or forward UDP {DefaultPort}.",
		UpnpFailure.MappingRefused => $"Router refused the mapping — check UPnP is on / no conflict, or forward UDP {DefaultPort}.",
		UpnpFailure.NoPublicIPv4 => "No public IPv4 (CGNAT or IPv6-only) — forwarding won't help; use a VPN relay.",
		_                        => $"UPnP error — forward UDP {DefaultPort} for internet play.",
	};

	public Error JoinGame(string address, string playerName, int colorIndex, int port = DefaultPort, int variantIndex = 0)
	{
		ResetPeer(); // drop any prior session's peer before connecting fresh
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateClient(address, port);
		if (err != Error.Ok) return err;
		Multiplayer.MultiplayerPeer = peer;
		IsHost = false;
		_pendingName = playerName;
		_pendingColor = colorIndex;
		_pendingVariant = variantIndex;
		return Error.Ok;
	}

	// Accepts either a share code or a raw address[:port]. Codes decode to ip+port;
	// anything else is treated as a direct address with an optional :port suffix.
	public Error JoinByCode(string input, string playerName, int colorIndex, int variantIndex = 0)
	{
		var trimmed = (input ?? "").Trim();
		if (Miner49er.Core.Net.ConnectCode.TryDecode(trimmed, out var ip, out var port))
			return JoinGame(new IPAddress(ip).ToString(), playerName, colorIndex, port, variantIndex);

		int p = DefaultPort;
		int idx = trimmed.LastIndexOf(':');
		if (idx > 0 && int.TryParse(trimmed[(idx + 1)..], out var parsed))
		{
			p = parsed;
			trimmed = trimmed[..idx];
		}
		return JoinGame(trimmed, playerName, colorIndex, p, variantIndex);
	}

	public void Leave()
	{
		_upnp.Release();
		Status = InternetStatus.Off;
		HostCode = null;
		ResetPeer();
		IsHost = false;
		Players.Clear();
		_botSkills.Clear();
		_nextBotFakeId = -1001;
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
		RpcId(1, nameof(SubmitPlayerInfo), _pendingName, _pendingColor, _pendingVariant);
	}

	private void OnConnectionFailed() { Multiplayer.MultiplayerPeer = null; JoinFailed?.Invoke(); }
	private void OnServerDisconnected() { Multiplayer.MultiplayerPeer = null; Players.Clear(); Disconnected?.Invoke(); }

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void SubmitPlayerInfo(string name, int colorIndex, int variantIndex)
	{
		if (!IsHost) return;
		long sender = Multiplayer.GetRemoteSenderId();
		Players[sender] = new PlayerInfo { Name = name, ColorIndex = colorIndex, VariantIndex = variantIndex, Ready = false };
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

	public void AddBot(BotSkill skill)
	{
		if (!IsHost || Players.Count >= 8) return;
		long fakeId = _nextBotFakeId--;
		string name = $"{PickBotName()} ({BotSkillDisplayName(skill)})";
		Players[fakeId] = new PlayerInfo { Name = name, ColorIndex = PickBotColor(), VariantIndex = PickBotVariant(), Ready = true };
		_botSkills[fakeId] = skill;
		BroadcastLobby();
	}

	private int PickBotVariant() => (int)(GD.Randi() % (uint)MinerVariants.Count);

	public void RemoveBot(long fakePeerId)
	{
		if (!IsHost || !_botSkills.ContainsKey(fakePeerId)) return;
		Players.Remove(fakePeerId);
		_botSkills.Remove(fakePeerId);
		BroadcastLobby();
	}

	public bool IsBotPeer(long peerId) => peerId <= -1001;

	public BotSkill GetBotSkill(long peerId) =>
		_botSkills.TryGetValue(peerId, out var s) ? s : BotSkill.Greenhorn;

	public static string BotSkillDisplayName(BotSkill s) => s switch
	{
		BotSkill.Greenhorn   => "Greenhorn",
		BotSkill.Miner       => "Miner",
		BotSkill.Foreman     => "Foreman",
		BotSkill.DynamiteDan => "Dynamite Dan",
		_ => s.ToString(),
	};

	private string PickBotName()
	{
		var used = new HashSet<string>();
		foreach (var info in Players.Values) used.Add(info.Name.Split(' ')[0]);
		foreach (var n in BotNamePool) if (!used.Contains(n)) return n;
		return $"Bot{_botSkills.Count + 1}";
	}

	private int PickBotColor()
	{
		var used = new HashSet<int>();
		foreach (var info in Players.Values) used.Add(info.ColorIndex);
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
			if (!used.Contains(i)) return i;
		return _botSkills.Count % PlayerColors.Palette.Length;
	}

	// Pending mode (host selection visible to joiners during lobby) ─────────
	public int PendingLobbyMode { get; private set; } = 0;

	public void BroadcastPendingMode(int modeId)
	{
		if (!IsHost) return;
		PendingLobbyMode = modeId;
		Rpc(nameof(ReceivePendingMode), modeId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void ReceivePendingMode(int modeId)
	{
		PendingLobbyMode = modeId;
		LobbyChanged?.Invoke();
	}

	private void BroadcastLobby()
	{
		var ids = new long[Players.Count];
		var names = new string[Players.Count];
		var colors = new int[Players.Count];
		var variants = new int[Players.Count];
		var readys = new int[Players.Count]; // bool[] is not a Godot Variant; use int (0/1) instead
		int i = 0;
		foreach (var (id, info) in Players)
		{
			ids[i] = id; names[i] = info.Name; colors[i] = info.ColorIndex;
			variants[i] = info.VariantIndex; readys[i] = info.Ready ? 1 : 0; i++;
		}
		Rpc(nameof(ReceiveLobby), ids, names, colors, variants, readys);
		ReceiveLobby(ids, names, colors, variants, readys); // apply locally on host too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void ReceiveLobby(long[] ids, string[] names, int[] colors, int[] variants, int[] readys)
	{
		Players.Clear();
		for (int i = 0; i < ids.Length; i++)
			Players[ids[i]] = new PlayerInfo
			{
				Name = names[i],
				ColorIndex = colors[i],
				VariantIndex = variants != null && i < variants.Length ? variants[i] : 0,
				Ready = readys[i] != 0,
			};
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
	public ExplosiveMode MatchExplosive { get; private set; }
	public int MatchBlastRadius { get; private set; } = 1;   // host-set explosion size (rock + kill radius)
	public int MatchVisionRadius { get; private set; } = 5;  // host-set base fog radius
	public float MatchBaseMoveSeconds { get; private set; } = 0.12f;
	public int MatchMapScale { get; set; } = 1;
	public long[] PeerOrder { get; private set; } = System.Array.Empty<long>();
	public int MatchFloor { get; set; } = 1;
	public int MatchStartFloor { get; private set; } = 1;

	// Set before StartMatch when continuing a saved solo expedition.
	public record ExpeditionResumeData(int CumulativeGold, int Lives, int PermSpeed, int PermVision, int PermBlast);
	public ExpeditionResumeData? ExpeditionResume { get; set; }

	public event System.Action<int>? NewFloor;
	public event System.Action<int>? FloorComplete; // co-op Expedition floor cleared — host awaits choice

	public void BroadcastFloorComplete(int floor)
	{
		Rpc(nameof(ReceiveFloorComplete), floor);
		ReceiveFloorComplete(floor);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveFloorComplete(int floor) => FloorComplete?.Invoke(floor);

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

	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale = 1, ExplosiveMode explosive = ExplosiveMode.Dynamite, int startFloor = 1, int blastRadius = 1, int visionRadius = 5)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, (int)explosive, order, startFloor, blastRadius, visionRadius);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, (int)explosive, order, startFloor, blastRadius, visionRadius); // host applies locally too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale, int explosive, long[] peerOrder, int startFloor = 1, int blastRadius = 1, int visionRadius = 5)
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
		MatchMapScale = mapScale;
		MatchExplosive = (ExplosiveMode)explosive;
		MatchBlastRadius = blastRadius;
		MatchVisionRadius = visionRadius;
		PeerOrder = peerOrder;
		MatchStartFloor = startFloor;
		MatchFloor = startFloor;
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

	public void SendAction(bool mine, bool plant, bool use, bool throwStone = false, bool throwDynamite = false)
	{
		if (IsHost) { _matchHost?.SetAction(LocalId, mine, plant, use, throwStone, throwDynamite); return; }
		RpcId(1, nameof(ReceiveAction), mine, plant, use, throwStone, throwDynamite);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveDir(int dir) => _matchHost?.SetDir(Multiplayer.GetRemoteSenderId(), dir);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveAction(bool mine, bool plant, bool use, bool throwStone, bool throwDynamite) =>
		_matchHost?.SetAction(Multiplayer.GetRemoteSenderId(), mine, plant, use, throwStone, throwDynamite);

	public void BuyShopItem(ShopItemKind kind)
	{
		if (IsHost) { _matchHost?.ReceiveBuy(LocalId, kind); return; }
		RpcId(1, nameof(ReceiveBuy), (int)kind);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveBuy(int kind) =>
		_matchHost?.ReceiveBuy(Multiplayer.GetRemoteSenderId(), (ShopItemKind)kind);

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
