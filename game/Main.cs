using System;
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
	private SettingsPanel _audioPanel = null!;
	private bool _wasListening;

	private Label? _floorBanner;
	private float  _floorBannerTimer;
	private const float BannerFade  = 0.3f;
	private const float BannerHold  = 1.5f;
	private const float BannerTotal = BannerHold + BannerFade * 2;

	public override void _Ready()
	{
		var nm = NetworkManager.Instance;
		InputBindings.EnsureDefaults();

		int seed = nm.MatchSeed;
		int playerCount = nm.MatchPlayerCount;
		var f1Modifier = nm.MatchMode == GameMode.Expedition ? FloorModifiers.Pick(seed, 1) : FloorModifier.None;
		var clientMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale);
		FloorModifiers.Apply(f1Modifier, clientMapCfg, new SimConfig());
		var map = MapGenerator.Generate(clientMapCfg);

		int localMinerId = nm.LocalMinerId();

		GridPos? clientEscape = nm.MatchMode == GameMode.Expedition ? map.EscapeTile : null;
		_client = new MatchClient { Name = "MatchClient", ZIndex = 5 };
		AddChild(_client);
		_client.Begin(map.Grid, map.Decoys, localMinerId, this, clientEscape, map.ShopPos);

		_audio = new MatchAudio { Name = "MatchAudio" };
		AddChild(_audio);
		_audio.Begin(_client);

		_compass = new Compass { Name = "Compass" };
		AddChild(_compass);
		_compass.Init(_client);

		if (nm.IsHost)
		{
			var hostMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale);
			var f1SimCfg = new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = seed };
			FloorModifiers.Apply(f1Modifier, hostMapCfg, f1SimCfg);
			var hostMap = MapGenerator.Generate(hostMapCfg);
			GridPos? escapeTile = nm.MatchMode == GameMode.Expedition ? hostMap.EscapeTile : null;
			var sim = new Simulation(
				hostMap.Grid,
				f1SimCfg,
				hostMap.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding,
				escapeTile);
			foreach (var item in hostMap.Items)
				sim.AddItem(item);
			var peerToMiner = new System.Collections.Generic.Dictionary<long, int>();
			GridPos soloSpawn = hostMap.Spawns.Count > 0 ? hostMap.Spawns[0] : hostMap.Center;
			if (nm.MatchMode == GameMode.Expedition && hostMap.EscapeTile is GridPos esc0 && soloSpawn == esc0)
			{
				var east = new GridPos(soloSpawn.X + 1, soloSpawn.Y);
				if (east.X < hostMap.Grid.Width && hostMap.Grid.Get(east) == TileType.Floor)
					soloSpawn = east;
			}
			for (int i = 0; i < nm.PeerOrder.Length; i++)
			{
				int minerId = i + 1;
				GridPos sp = (nm.MatchMode == GameMode.Expedition && i == 0) ? soloSpawn : hostMap.Spawns[i];
				sim.AddMiner(minerId, sp);
				peerToMiner[nm.PeerOrder[i]] = minerId;
			}
			if (nm.MatchMode == GameMode.Expedition)
			{
				int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height);
				var roster = MonsterSpawner.Place(hostMap.Grid, soloSpawn, monsterCount);
				for (int i = 0; i < roster.Count; i++)
					sim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
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

		_audioPanel = new SettingsPanel { Name = "SettingsPanel" };
		AddChild(_audioPanel);

		nm.RegisterMatch(_host, _client);
		nm.MatchEnded += OnMatchEnded;
		nm.NewFloor += OnNewFloor;
		nm.ReturnToLobbyRequested += OnReturnToLobby;
		nm.Disconnected += OnDisconnected;
	}

	public override void _ExitTree()
	{
		var nm = NetworkManager.Instance;
		nm.MatchEnded -= OnMatchEnded;
		nm.NewFloor -= OnNewFloor;
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
		if (_floorBanner != null)
		{
			_floorBannerTimer -= (float)delta;
			float alpha;
			if (_floorBannerTimer > BannerHold + BannerFade)
				alpha = 1f - (_floorBannerTimer - BannerHold - BannerFade) / BannerFade;
			else if (_floorBannerTimer > BannerFade)
				alpha = 1f;
			else
				alpha = Math.Max(0f, _floorBannerTimer / BannerFade);
			if (_floorBannerTimer <= 0f) { _floorBanner.QueueFree(); _floorBanner = null; }
			else _floorBanner.Modulate = new Color(1, 1, 1, alpha);
		}

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
					string objective;
					if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
					{
						var nm2 = NetworkManager.Instance;
						string hearts = new string('♥', Math.Max(0, _client.Lives));
						var hudMod = FloorModifiers.Pick(nm2.MatchSeed, nm2.MatchFloor);
						string modTag = hudMod != FloorModifier.None ? $"  [{FloorModifiers.DisplayName(hudMod)}]" : "";
						if (nm2.MatchFloor == 21)
						{
							objective = $"{hearts}  BOSS FLOOR  Reach the chest!";
						}
						else if (_client.EscapeOpen)
						{
							objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold ✓ — ESCAPE!{modTag}";
						}
						else
						{
							int pct = _client.StartingGoldCount > 0
								? (int)(100.0 * (_client.StartingGoldCount - _client.GoldRemaining) / _client.StartingGoldCount)
								: 0;
							objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold: {pct}%{modTag}";
						}
					}
					else
					{
						objective = $"Gold: {m.Gold}";
					}
					_hud.SetText($"{objective}    {status}{timeStr}{heldStr}");
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

	private void OnNewFloor(int floor)
	{
		_client.ResetFloor(floor);

		var bannerMod = FloorModifiers.Pick(NetworkManager.Instance.MatchSeed, floor);
		string bannerText = floor == 21 ? "BOSS FLOOR"
			: bannerMod != FloorModifier.None ? $"FLOOR {floor}: {FloorModifiers.DisplayName(bannerMod)}"
			: $"FLOOR {floor}";
		_floorBanner?.QueueFree();
		_floorBanner = new Label
		{
			Text = bannerText,
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0f, AnchorRight = 1f,
			AnchorTop = 0.45f, AnchorBottom = 0.45f,
			Modulate = new Color(1, 1, 1, 0f),
			ZIndex = 20,
		};
		_floorBanner.AddThemeFontSizeOverride("font_size", 64);
		AddChild(_floorBanner);
		_floorBannerTimer = BannerTotal;
	}

	private void OnMatchEnded(long winnerPeerId)
	{
		if (_results != null) return;
		_results = new ResultsOverlay { Name = "ResultsOverlay" };
		AddChild(_results);
		var nm = NetworkManager.Instance;
		bool expedition = nm.MatchMode == GameMode.Expedition;
		string label;
		string scoreText = "";
		if (expedition)
		{
			bool won = winnerPeerId == nm.LocalId;
			label = won
				? (nm.MatchFloor == 21 ? "You conquered the dungeon!" : "You escaped with the gold!")
				: "You died in the mine.";
			scoreText = $"Floor {nm.MatchFloor}  (score submitted)";
		}
		else
		{
			label = winnerPeerId == -1
				? "Draw — no survivors"
				: $"Winner: {NameOf(winnerPeerId)}";
		}
		_results.Show(label, nm.IsHost,
			expedition ? "Return to Menu" : "Return to Lobby", scoreText);
	}

	private static string NameOf(long peerId) =>
		NetworkManager.Instance.Players.TryGetValue(peerId, out var info) ? info.Name : $"Peer {peerId}";

	private void OnReturnToLobby()
	{
		// Solo Expedition has no lobby — tear down the host and go back to the menu.
		if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
		{
			NetworkManager.Instance.Leave();
			GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
			return;
		}
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnDisconnected() => GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
}
