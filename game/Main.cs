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
	private DeathFeed _deathFeed = null!;
	private AudioSettingsPanel _audioPanel = null!;
	private bool _wasListening;

	public override void _Ready()
	{
		var nm = NetworkManager.Instance;
		InputBindings.EnsureDefaults();

		int seed = nm.MatchSeed;
		int playerCount = nm.MatchPlayerCount;
		var map = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits));

		int localMinerId = nm.LocalMinerId();

		_client = new MatchClient { Name = "MatchClient", ZIndex = 5 };
		AddChild(_client);
		_client.Begin(map.Grid, map.Decoys, localMinerId, this);

		_audio = new MatchAudio { Name = "MatchAudio" };
		AddChild(_audio);
		_audio.Begin(_client);

		_compass = new Compass { Name = "Compass" };
		AddChild(_compass);
		_compass.Init(_client);

		if (nm.IsHost)
		{
			var hostMap = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits));
			var sim = new Simulation(
				hostMap.Grid,
				new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds },
				hostMap.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding);
			foreach (var item in hostMap.Items)
				sim.AddItem(item);
			var peerToMiner = new System.Collections.Generic.Dictionary<long, int>();
			for (int i = 0; i < nm.PeerOrder.Length; i++)
			{
				int minerId = i + 1;
				sim.AddMiner(minerId, hostMap.Spawns[i]);
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

		_deathFeed = new DeathFeed { Name = "DeathFeed" };
		AddChild(_deathFeed);
		_deathFeed.Init(_client);

		_audioPanel = new AudioSettingsPanel { Name = "AudioSettingsPanel" };
		AddChild(_audioPanel);

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

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed(InputBindings.Exit))
		{
			GetViewport().SetInputAsHandled();
			if (_audioPanel.IsOpen) { _audioPanel.Close(); return; }
			NetworkManager.Instance.Leave(); // in-match: ESC backs out to the main menu
			GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
		}
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
				string timeStr = _client.SecondsRemaining >= 0 ? $"    Time: {_client.SecondsRemaining:0}s" : "";
				string heldStr = m.Held switch
					{
						(int)ItemKind.WaterPlank => "    Held: Plank",
						(int)ItemKind.SlowMold => "    Held: Mold",
						_ => "",
					};
					_hud.SetText($"Gold: {m.Gold}    {status}{timeStr}{heldStr}");
			}
		if (Input.IsActionJustPressed(InputBindings.Settings))
			_audioPanel.Toggle();
		bool panelOpen = _audioPanel.IsOpen;
		if (_input != null) _input.Enabled = (!sawLocal || localAlive) && !panelOpen;

		bool listening = localAlive && !panelOpen && Input.IsActionPressed(InputBindings.Listen);
		if (_input != null) _input.Listening = listening;
		_compass.Active = listening;
		_client.Listening = listening;
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
