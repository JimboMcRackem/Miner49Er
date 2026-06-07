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
	private MatchAudio _audio = null!;
	private Compass _compass = null!;
	private bool _wasListening;

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

		_audio = new MatchAudio { Name = "MatchAudio" };
		AddChild(_audio);
		_audio.Begin(_client);

		_compass = new Compass { Name = "Compass" };
		AddChild(_compass);
		_compass.Init(_client);

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
		nm.RegisterMatch(null, null);
	}

	public override void _PhysicsProcess(double delta)
	{
		// Disable input + HUD activity once the local miner is dead (spectate).
		bool sawLocal = false;
		bool localAlive = false;
		string status = "Spectating";
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
			{
				sawLocal = true;
				localAlive = m.Alive;
				status = m.Alive
					? (m.Activity == (int)ActivityKind.Mining ? $"Mining… {m.ActivityRemaining:0.0}s"
						: m.Activity == (int)ActivityKind.Planting ? $"Planting… {m.ActivityRemaining:0.0}s"
						: "Ready")
					: "Dead — spectating";
				_hud.SetText($"Gold: {m.Gold}    {status}");
			}
		if (_input != null) _input.Enabled = !sawLocal || localAlive;

		bool listening = localAlive && Input.IsActionPressed(InputBindings.Listen);
		if (_input != null) _input.Listening = listening;
		_compass.Active = listening;
		if (listening != _wasListening)
		{
			AudioManager.Instance.SetListening(listening);
			_audio.SetListening(listening);
			_wasListening = listening;
		}

		if (Input.IsActionJustPressed(InputBindings.Mute))
			AudioManager.Instance.ToggleMute();
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
