using System;
using Godot;
using Miner49er.Core;
using Miner49er.Core.AI;

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
	private ShopPanel _shopPanel = null!;
	private bool _wasListening;
	private bool _wasAtShop;
	private ColorRect _fadeOverlay = null!;
	private float _fadeAlpha;
	private const float FadeOutSpeed = 3f;  // 0→1 in ~0.33s
	private const float FadeInSpeed  = 1.5f; // 1→0 in ~0.67s

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
		var clientMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale, nm.MatchExplosive);
		clientMapCfg.Flooding = nm.MatchFlooding;
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
			var hostMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale, nm.MatchExplosive);
			hostMapCfg.Flooding = nm.MatchFlooding;
			var f1SimCfg = new SimConfig
			{
				BaseMoveSeconds  = nm.MatchBaseMoveSeconds,
				Seed             = seed,
				DynamiteEnabled  = nm.MatchExplosive != ExplosiveMode.DetonatorsOnly,
				TreasureHuntMode = nm.MatchMode == GameMode.TreasureHunt,
			};
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
			var peerToMiner    = new System.Collections.Generic.Dictionary<long, int>();
			var botConfigs     = new System.Collections.Generic.List<(int minerId, BotSkill skill)>();
			var botMinerToPeer = new System.Collections.Generic.Dictionary<int, long>();
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
				long peer   = nm.PeerOrder[i];
				GridPos sp  = (nm.MatchMode == GameMode.Expedition && i == 0) ? soloSpawn
				              : (i < hostMap.Spawns.Count ? hostMap.Spawns[i] : hostMap.Spawns[0]);
				sim.AddMiner(minerId, sp);
				if (nm.IsBotPeer(peer))
				{
					botConfigs.Add((minerId, nm.GetBotSkill(peer)));
					botMinerToPeer[minerId] = peer;
				}
				else
				{
					peerToMiner[peer] = minerId;
				}
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
			_host.Begin(sim, peerToMiner, botConfigs, botMinerToPeer);
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

		_shopPanel = new ShopPanel { Name = "ShopPanel" };
		AddChild(_shopPanel);

		_fadeOverlay = new ColorRect { Name = "FadeOverlay", ZIndex = 50 };
		_fadeOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_fadeOverlay.Color = new Color(0f, 0f, 0f, 0f);
		_fadeOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_fadeOverlay);

		nm.RegisterMatch(_host, _client);
		nm.MatchEnded += OnMatchEnded;
		nm.NewFloor += OnNewFloor;
		nm.ReturnToLobbyRequested += OnReturnToLobby;
		nm.Disconnected += OnDisconnected;
	}

	public override void _ExitTree()
	{
		AudioManager.Instance.SetListening(false);
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
				var heldKind = m.Held >= 0 ? (ItemKind?)((ItemKind)m.Held) : null;
					string heldStr = heldKind switch
					{
						ItemKind.WaterPlank    => "    Held: Plank",
						ItemKind.SlowMold      => "    Held: Mold",
						ItemKind.TreasureChest => "    Held: Chest",
						{ } k when k.IsIdol()  => $"    Held: {IdolName(k)}",
						_                      => "",
					};
					string stonesStr = m.StoneCount > 0 ? $"    Stones: {m.StoneCount}" : "";
					if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
					{
						var nm2 = NetworkManager.Instance;
						var hudMod = FloorModifiers.Pick(nm2.MatchSeed, nm2.MatchFloor);
						string modTag = hudMod != FloorModifier.None ? $"  [{FloorModifiers.DisplayName(hudMod)}]" : "";
						string objective;
						if (nm2.MatchFloor == 21)
						{
							objective = "BOSS FLOOR  Reach the chest!";
						}
						else if (_client.EscapeOpen)
						{
							objective = $"Floor {nm2.MatchFloor}/20  Gold ✓ — ESCAPE!{modTag}";
						}
						else
						{
							int pct = _client.StartingGoldCount > 0
								? (int)(100.0 * (_client.StartingGoldCount - _client.GoldRemaining) / _client.StartingGoldCount)
								: 0;
							objective = $"Floor {nm2.MatchFloor}/20  Gold: {pct}%{modTag}";
						}
						_hud.SetHud(Math.Max(0, _client.Lives), objective, $"{status}{timeStr}{heldStr}{stonesStr}");
					}
					else if (NetworkManager.Instance.MatchMode == GameMode.TreasureHunt)
					{
						var nm2 = NetworkManager.Instance;
						var (idolA, idolB) = TreasureAssignment.For(nm2.MatchSeed, _client.LocalMinerId);
						int foundCount = 0;
						if (_client.TreasureProgress != null)
							foreach (var tp in _client.TreasureProgress)
								if (tp.MinerId == _client.LocalMinerId) { foundCount = tp.Found; break; }
						string aState = foundCount >= 1 ? "✓" : "○";
						string bState = foundCount >= 2 ? "✓" : "○";
						_hud.SetHud(0, $"{IdolName(idolA)} {aState}  {IdolName(idolB)} {bState}", $"{status}{heldStr}");
					}
					else
					{
						_hud.SetHud(0, $"Gold: {m.Gold}", $"{status}{timeStr}{heldStr}{stonesStr}");
					}

					// Shop proximity — Expedition only
					if (localAlive && NetworkManager.Instance.MatchMode == GameMode.Expedition)
					{
						var localPos = new GridPos(m.X, m.Y);
						bool atShop = _client.ShopPos is GridPos sPos && localPos == sPos;
						_shopPanel.UpdateSnapshot(m, _client.Lives, 3);
						bool shopKeyPressed = atShop && Input.IsActionJustPressed(InputBindings.UseItem);
						if (atShop && (!_wasAtShop || shopKeyPressed) && !_shopPanel.IsOpen)
						{
							_shopPanel.Open(m, _client.Lives, 3);
							NetworkManager.Instance.SendDir(-1); // clear pending direction so miner doesn't walk off shop tile
						}
						else if (!atShop && _shopPanel.IsOpen)
							_shopPanel.Close();
						_wasAtShop = atShop;
					}
			}
		// Death-fade overlay (expedition only): black out on death, fade in on revive.
		if (sawLocal && NetworkManager.Instance.MatchMode == GameMode.Expedition)
		{
			float target = localAlive ? 0f : 1f;
			if (_fadeAlpha < target)
				_fadeAlpha = Math.Min(target, _fadeAlpha + FadeOutSpeed * (float)delta);
			else if (_fadeAlpha > target)
				_fadeAlpha = Math.Max(target, _fadeAlpha - FadeInSpeed * (float)delta);
			_fadeOverlay.Color = new Color(0f, 0f, 0f, _fadeAlpha);
		}

		if (Input.IsActionJustPressed(InputBindings.Settings))
			_audioPanel.Toggle();
		bool panelOpen = _audioPanel.IsOpen;
		if (_input != null) _input.Enabled = (!sawLocal || localAlive) && !panelOpen && !_shopPanel.IsOpen;

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
		AudioManager.Instance.PlayMusic(SfxLibrary.PickMusic());

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
			bool won = winnerPeerId != -1;
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

	private static string IdolName(ItemKind k) => k switch
	{
		ItemKind.IdolVishnu       => "Vishnu",
		ItemKind.IdolZeus         => "Zeus",
		ItemKind.IdolAnubis       => "Anubis",
		ItemKind.IdolOdin         => "Odin",
		ItemKind.IdolShiva        => "Shiva",
		ItemKind.IdolBuddha       => "Buddha",
		ItemKind.IdolRa           => "Ra",
		ItemKind.IdolQuetzalcoatl => "Quetzal",
		ItemKind.IdolUrn          => "Urn",
		ItemKind.IdolLamp         => "Lamp",
		ItemKind.IdolMace         => "Mace",
		ItemKind.IdolSceptre      => "Sceptre",
		ItemKind.IdolGlobe        => "Globe",
		ItemKind.IdolTrophyCup    => "Trophy",
		ItemKind.IdolChalice      => "Chalice",
		ItemKind.IdolCrown        => "Crown",
		ItemKind.IdolSkull        => "Skull",
		_                         => "Idol",
	};

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
