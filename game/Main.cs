using System;
using System.Collections.Generic;
using System.Linq;
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
	private float _listenTime;
	private bool _wasAtShop = true; // suppresses auto-open on frame 1; prevents spawn-on-shop lock
	private ColorRect _fadeOverlay = null!;
	private float _fadeAlpha;
	private const float FadeOutSpeed = 3f;  // 0→1 in ~0.33s
	private const float FadeInSpeed  = 1.5f; // 1→0 in ~0.67s

	private Label? _floorBanner;
	private CanvasLayer? _floorBannerLayer;
	private float  _floorBannerTimer;
	private const float BannerFade  = 0.3f;
	private const float BannerHold  = 1.5f;
	private const float BannerTotal = BannerHold + BannerFade * 2;

	// Pause menu
	private CanvasLayer _pauseOverlay = null!;
	private bool _pauseShown;

	// Tab scoreboard
	private ScoreboardOverlay _scoreboard = null!;

	// Minimap
	private MinimapOverlay _minimap = null!;

	// F3 debug stats overlay
	private StatsOverlay _stats = null!;

	// Co-op Expedition floor-clear choice panel
	private CanvasLayer? _floorChoicePanel;

	// Pickup announcements
	private string? _announcement;
	private double  _announcementExpiry;
	private readonly HashSet<ItemKind> _tutorialShown = new();
	private List<(int X, int Y, ItemKind Kind)> _prevAnnounceItems = new();
	private GridPos? _prevLocalPos;
	private bool _prevEscapeOpen;
	private bool _hadTreasureFloor;

	public override void _Ready()
	{
		var nm = NetworkManager.Instance;
		InputBindings.EnsureDefaults();

		int seed = nm.MatchSeed;
		int playerCount = nm.MatchPlayerCount;
		int startFloor = nm.MatchStartFloor;
		int floorSeed = startFloor > 1 ? seed + startFloor * 1000 : seed;
		var floorModifier = nm.MatchMode == GameMode.Expedition ? FloorModifiers.Pick(seed, startFloor) : FloorModifier.None;
		MapConfig clientMapCfg;
		if (nm.MatchMode == GameMode.Expedition && startFloor > 1)
		{
			clientMapCfg = MapConfig.FloorConfig(startFloor, floorSeed, playerCount);
		}
		else
		{
			clientMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale, nm.MatchExplosive);
			clientMapCfg.Flooding = nm.MatchFlooding;
		}
		FloorModifiers.Apply(floorModifier, clientMapCfg, new SimConfig());
		var map = MapGenerator.Generate(clientMapCfg);

		int localMinerId = nm.LocalMinerId();

		// ReachCenter has no ladder — goal is the center tile, not an escape tile.
		GridPos? clientEscape = nm.MatchMode == GameMode.ReachCenter ? null : map.EscapeTile;
		GridPos? clientCenter = nm.MatchMode == GameMode.ReachCenter ? (GridPos?)map.Center : null;
		_client = new MatchClient { Name = "MatchClient", ZIndex = 5 };
		AddChild(_client);
		_client.Begin(map.Grid, map.Decoys, localMinerId, this, clientEscape, map.ShopPos, clientCenter,
			expeditionTreasurePos: map.ExpeditionTreasurePos);

		_audio = new MatchAudio { Name = "MatchAudio" };
		AddChild(_audio);
		_audio.Begin(_client);

		_compass = new Compass { Name = "Compass" };
		AddChild(_compass);
		_compass.Init(_client);

		if (nm.IsHost)
		{
			MapConfig hostMapCfg;
			if (nm.MatchMode == GameMode.Expedition && startFloor > 1)
			{
				hostMapCfg = MapConfig.FloorConfig(startFloor, floorSeed, playerCount);
			}
			else
			{
				hostMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale, nm.MatchExplosive);
				hostMapCfg.Flooding = nm.MatchFlooding;
			}
			bool isPvP = nm.MatchMode != GameMode.Expedition;
			var f1SimCfg = new SimConfig
			{
				BaseMoveSeconds  = nm.MatchBaseMoveSeconds,
				Seed             = floorSeed,
				DynamiteEnabled  = nm.MatchExplosive != ExplosiveMode.DetonatorsOnly,
				TreasureHuntMode = nm.MatchMode == GameMode.TreasureHunt,
				TripMinesEnabled = nm.MatchMode == GameMode.LastManStanding || nm.MatchMode == GameMode.DemolitionDerby,
				UnstableFloorEnabled = nm.MatchMode == GameMode.LastManStanding
					|| nm.MatchMode == GameMode.DemolitionDerby,
				RockFallsEnabled = true,
				BlastRockRadius = nm.MatchBlastRadius,
				BlastKillRadius = nm.MatchBlastRadius,
				VisionRadius    = nm.MatchVisionRadius,
				Mode            = nm.MatchMode,
				PrizeEventsEnabled = nm.MatchPrizeEvents && nm.MatchMode != GameMode.Expedition,
			};
			if (nm.MatchMode == GameMode.DemolitionDerby)
			{
				f1SimCfg.DynamiteEnabled = true;
				f1SimCfg.FuseSeconds     = 2.0;
			}
			FloorModifiers.Apply(floorModifier, hostMapCfg, f1SimCfg);
			f1SimCfg.ExpeditionTreasureKind = hostMapCfg.ExpeditionTreasureKind;
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
			if (nm.MatchExplosive == ExplosiveMode.DetonatorsOnly)
			{
				foreach (var minerId in peerToMiner.Values)
					sim.SetMinerHeld(minerId, ItemKind.Detonator);
				foreach (var (minerId, _) in botConfigs)
					sim.SetMinerHeld(minerId, ItemKind.Detonator);
			}
			if (nm.MatchMode == GameMode.Expedition)
			{
				int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height, startFloor);
				var roster = MonsterSpawner.Place(hostMap.Grid, soloSpawn, monsterCount, floor: startFloor);
				for (int i = 0; i < roster.Count; i++)
					sim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
			}
			else if (nm.MatchMode == GameMode.ReachCenter && hostMap.Spawns.Count > 0)
			{
				int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height);
				var roster = MonsterSpawner.Place(hostMap.Grid, hostMap.Spawns[0], monsterCount, floor: 10);
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

		// Tab scoreboard
		_scoreboard = new ScoreboardOverlay { Name = "ScoreboardOverlay" };
		AddChild(_scoreboard);
		_scoreboard.Init(_client);

		// Minimap
		_minimap = new MinimapOverlay { Name = "MinimapOverlay" };
		AddChild(_minimap);
		_minimap.Init(_client);

		// F3 debug stats overlay
		_stats = new StatsOverlay { Name = "StatsOverlay" };
		AddChild(_stats);
		_stats.Init(_client);

		// Pause overlay (Escape menu)
		_pauseOverlay = BuildPauseOverlay();
		AddChild(_pauseOverlay);

		_fadeOverlay = new ColorRect { Name = "FadeOverlay", ZIndex = 50 };
		_fadeOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_fadeOverlay.Color = new Color(0f, 0f, 0f, 0f);
		_fadeOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_fadeOverlay);

		nm.RegisterMatch(_host, _client);
		nm.MatchEnded += OnMatchEnded;
		nm.NewFloor += OnNewFloor;
		nm.FloorComplete += OnFloorComplete;
		nm.ReturnToLobbyRequested += OnReturnToLobby;
		nm.Disconnected += OnDisconnected;
	}

	public override void _ExitTree()
	{
		AudioManager.Instance.SetListening(false);
		var nm = NetworkManager.Instance;
		nm.MatchEnded -= OnMatchEnded;
		nm.NewFloor -= OnNewFloor;
		nm.FloorComplete -= OnFloorComplete;
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
			if (_pauseShown) { HidePauseMenu(); return; }
			ShowPauseMenu();
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
			if (_floorBannerTimer <= 0f) { _floorBannerLayer?.QueueFree(); _floorBannerLayer = null; _floorBanner = null; }
			else _floorBanner.Modulate = new Color(1, 1, 1, alpha);
		}

		// Treasure-floor escape-open announcement.
		if (_client.ExpeditionTreasurePos != null) _hadTreasureFloor = true;
		if (_client.EscapeOpen && !_prevEscapeOpen && _hadTreasureFloor)
			SetAnnouncement("You found the treasure! Escape is open!", 3500);
		_prevEscapeOpen = _client.EscapeOpen;

		// Disable input + HUD activity once the local miner is dead (spectate).
		ProcessPickupAnnouncements(_client.LocalMinerId);
		bool sawLocal = false;
		bool localAlive = false;
		string status = "Spectating";
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
			{
				sawLocal = true;
				localAlive = m.Alive;
				string rawStatus = m.Alive
					? (m.Activity == (int)ActivityKind.Mining           ? $"Mining… {m.ActivityRemaining:0.0}s"
						: m.Activity == (int)ActivityKind.Planting          ? $"Planting… {m.ActivityRemaining:0.0}s"
						: m.Activity == (int)ActivityKind.PlantingDetonator ? $"Planting detonator… {m.ActivityRemaining:0.0}s"
						: "Ready")
					: "Dead — spectating";
				status = IsAnnouncementActive() ? _announcement! : rawStatus;
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
						if (nm2.MatchFloor == 51)
						{
							objective = "BOSS FLOOR  Reach the chest!";
						}
						else
						{
							bool isTreasureFloor = _client.ExpeditionTreasurePos != null || (_hadTreasureFloor && _client.EscapeOpen);
						if (isTreasureFloor)
						{
							objective = _client.EscapeOpen
								? $"Floor {nm2.MatchFloor}/50  Treasure found — ESCAPE!{modTag}"
								: $"Floor {nm2.MatchFloor}/50  Find the hidden treasure!{modTag}";
						}
						else
						{
							int pct = _client.StartingGoldCount > 0
								? (int)(100.0 * (_client.StartingGoldCount - _client.GoldRemaining) / _client.StartingGoldCount)
								: 0;
							objective = _client.EscapeOpen
								? $"Floor {nm2.MatchFloor}/50  Gold: {pct}% — ESCAPE!{modTag}"
								: $"Floor {nm2.MatchFloor}/50  Gold: {pct}%{modTag}";
						}
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
						_hud.SetTreasureHud(IdolName(idolA), foundCount >= 1, IdolName(idolB), foundCount >= 2, $"{status}{heldStr}");
					}
					else if (NetworkManager.Instance.MatchMode == GameMode.ReachCenter)
					{
						string rcCenter = m.Gold >= 5
							? "Reach the Center!"
							: $"Center — Gold: {m.Gold}/5";
						_hud.SetHud(0, rcCenter, $"{status}{timeStr}{heldStr}{stonesStr}");
					}
					else if (NetworkManager.Instance.MatchMode == GameMode.DemolitionDerby)
					{
						_hud.SetHud(0, "Demolition Derby", $"{status}{heldStr}");
					}
					else
					{
						_hud.SetHud(0, $"Gold: {m.Gold}", $"{status}{timeStr}{heldStr}{stonesStr}");
					}

					// Timer urgency: flash red when under 30 s.
					_hud.TimerUrgent = _client.SecondsRemaining >= 0f && _client.SecondsRemaining < 30f;

					// Contextual hint — what Space does right now.
					_hud.SetHint(localAlive ? ComputeContextHint(m, new GridPos(m.X, m.Y)) : "");

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

						// Whistle at the ladder — call bots to the exit.
						bool atEscape = _client.EscapeOpen && _client.EscapeTile is GridPos eTile && localPos == eTile;
						if (atEscape && Input.IsActionJustPressed(InputBindings.UseItem) && _host != null)
						{
							_host.WhistleBots();
							PlayLocalSound(SfxLibrary.Whistle);
						}
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
		if (_input != null) _input.Enabled = (!sawLocal || localAlive) && !panelOpen && !_shopPanel.IsOpen && !_pauseShown;

		bool listening = localAlive && !panelOpen && !AudioManager.Instance.IsDeafened && Input.IsActionPressed(InputBindings.Listen);
		_listenTime = listening ? _listenTime + (float)delta : 0f;
		if (_input != null) _input.Listening = listening;
		_compass.Active    = listening;
		_compass.ListenTime = _listenTime;
		_client.Listening  = listening;
		_client.ListenTime = _listenTime;
		if (listening != _wasListening)
		{
			AudioManager.Instance.SetListening(listening);
			_audio.SetListening(listening);
			_wasListening = listening;
		}

		if (Input.IsActionJustPressed(InputBindings.Mute))
			AudioManager.Instance.ToggleMute();

		if (Input.IsActionJustPressed(InputBindings.Stats))
			_stats.Toggle();

		// Tab scoreboard — show while Tab held, hide when released.
		bool wantScoreboard = !_pauseShown && Input.IsKeyPressed(Key.Tab);
		if (wantScoreboard && !_scoreboard.Visible)
		{
			_scoreboard.Refresh();
			_scoreboard.Visible = true;
		}
		else if (!wantScoreboard && _scoreboard.Visible)
		{
			_scoreboard.Visible = false;
		}
	}

	private void SetAnnouncement(string text, double durationMs = 2500)
	{
		_announcement       = text;
		_announcementExpiry = Time.GetTicksMsec() + durationMs;
	}

	private bool IsAnnouncementActive() =>
		_announcement != null && Time.GetTicksMsec() < _announcementExpiry;

	private static bool IsPermBuff(ItemKind k) =>
		k is ItemKind.SpeedPotion or ItemKind.BiggerBlast or ItemKind.LongerVision;

	private static bool IsAnnounceKind(ItemKind k) =>
		k is ItemKind.SpeedPotion or ItemKind.BiggerBlast or ItemKind.LongerVision
		  or ItemKind.LifePotion or ItemKind.WaterPlank or ItemKind.Lantern or ItemKind.Chest
		  or ItemKind.Stone;

	private string PickupMessage(ItemKind kind, bool isBlocked) =>
		(kind, isBlocked) switch
		{
			(ItemKind.SpeedPotion,  true)  => "Already maxed out — Speed Tonic!",
			(ItemKind.BiggerBlast,  true)  => "Already maxed out — Bigger Blast!",
			(ItemKind.LongerVision, true)  => "Already maxed out — Keen Eyes!",
			(ItemKind.SpeedPotion,  false) => $"Speed Tonic! Faster for {(int)BuffTuning.SpeedMinutes} min.",
			(ItemKind.BiggerBlast,  false) => $"Bigger Blast! Larger radius for {(int)BuffTuning.BlastMinutes} min.",
			(ItemKind.LongerVision, false) => $"Keen Eyes! See further for {(int)BuffTuning.VisionMinutes} min.",
			(ItemKind.LifePotion,   false) => "Life Restored!",
			(ItemKind.WaterPlank,   false) => "Water Plank — place it across deep water.",
			(ItemKind.Lantern,      false) => "Lantern — drop it to light the area.",
			(ItemKind.Chest,        false) => "Opened a chest!",
			(ItemKind.Stone,        false) => "Picked up stones — throw to distract.",
			_ => "",
		};

	private void ProcessPickupAnnouncements(int localMinerId)
	{
		GridPos? localPos = null;
		foreach (var m in _client.Miners)
			if (m.Id == localMinerId && m.Alive) { localPos = new GridPos(m.X, m.Y); break; }
		if (localPos is not { } pos) { _prevLocalPos = null; return; }

		bool movedThisTick = pos != _prevLocalPos;

		var curItems = new List<(int X, int Y, ItemKind Kind)>();
		foreach (var it in _client.Items)
			if (IsAnnounceKind(it.Kind)) curItems.Add((it.X, it.Y, it.Kind));

		// Detect disappeared items at miner pos → pickup
		foreach (var prev in _prevAnnounceItems)
		{
			bool stillThere = false;
			foreach (var cur in curItems)
				if (cur.X == prev.X && cur.Y == prev.Y && cur.Kind == prev.Kind) { stillThere = true; break; }
			if (!stillThere && pos.X == prev.X && pos.Y == prev.Y)
			{
				if (prev.Kind == ItemKind.WaterPlank && _tutorialShown.Add(ItemKind.WaterPlank))
					SetAnnouncement(PickupMessage(ItemKind.WaterPlank, false));
				else if (prev.Kind == ItemKind.Lantern && _tutorialShown.Add(ItemKind.Lantern))
					SetAnnouncement(PickupMessage(ItemKind.Lantern, false));
				else if (IsPermBuff(prev.Kind) || prev.Kind == ItemKind.LifePotion
					  || prev.Kind == ItemKind.Chest || prev.Kind == ItemKind.Stone)
					SetAnnouncement(PickupMessage(prev.Kind, false));
			}
		}

		// Detect perm-buff item at miner pos that didn't disappear + miner just arrived → blocked
		if (movedThisTick)
		{
			foreach (var cur in curItems)
			{
				if (cur.X == pos.X && cur.Y == pos.Y && IsPermBuff(cur.Kind))
				{
					bool wasHereLastFrame = false;
					foreach (var prev in _prevAnnounceItems)
						if (prev.X == cur.X && prev.Y == cur.Y && prev.Kind == cur.Kind) { wasHereLastFrame = true; break; }
					if (wasHereLastFrame)
						SetAnnouncement(PickupMessage(cur.Kind, true));
				}
			}
		}

		_prevLocalPos      = pos;
		_prevAnnounceItems = curItems;
	}

	private string ComputeContextHint(Core.Net.MinerSnapshot m, GridPos localPos)
	{
		var nm = NetworkManager.Instance;
		var heldKind = m.Held >= 0 ? (ItemKind?)((ItemKind)m.Held) : null;

		if (nm.MatchMode == GameMode.Expedition)
		{
			if (_client.EscapeOpen && _client.EscapeTile is GridPos exitTile && exitTile == localPos)
			{
				var aliveMiners = _client.Miners.Where(x => x.Alive).ToList();
				var atExit      = aliveMiners.Where(x => new GridPos(x.X, x.Y) == exitTile).ToList();
				if (atExit.Count >= aliveMiners.Count)
					return "[Space]  ESCAPE!";
				var missing = aliveMiners.First(x => new GridPos(x.X, x.Y) != exitTile);
				int dx = missing.X - exitTile.X, dy = missing.Y - exitTile.Y;
				string dir = (dx, dy) switch
				{
					( > 0,  0) => "E",
					( < 0,  0) => "W",
					(  0, > 0) => "S",
					(  0, < 0) => "N",
					( > 0, > 0) => "SE",
					( > 0, < 0) => "NE",
					( < 0, > 0) => "SW",
					_          => "NW",
				};
				return $"{atExit.Count}/{aliveMiners.Count} miners at exit — teammate {dir}";
			}
			if (_client.ShopPos == localPos && !_shopPanel.IsOpen)
				return "[Space]  Open Shop";
		}

		if (nm.MatchMode == GameMode.ReachCenter && _client.CenterTile is GridPos ct)
		{
			int dx = Math.Abs(ct.X - localPos.X), dy = Math.Abs(ct.Y - localPos.Y);
			bool adjacent = dx <= 1 && dy <= 1;
			if (adjacent && m.Gold < 5)
				return $"Need {5 - m.Gold} more gold to unlock the center!";
		}

		if (heldKind != null)
		{
			const int ReelSafeDist = 3; // mirrors SimConfig.ReelSafeDistance

			if (heldKind == ItemKind.Detonator)
			{
				if (m.Activity == (int)ActivityKind.PlantingDetonator)
					return $"Planting detonator… {m.ActivityRemaining:0.0}s";
				foreach (var rc in _client.ReelCharges)
					if (rc.OwnerId == _client.LocalMinerId)
						return "Detonator planted — detonate it first!";
				return "[Space]  Plant Detonator";
			}

			if (heldKind == ItemKind.Reel)
			{
				foreach (var rc in _client.ReelCharges)
					if (rc.OwnerId == _client.LocalMinerId)
					{
						int dist = Math.Abs(rc.WallX - localPos.X) + Math.Abs(rc.WallY - localPos.Y);
						return dist < ReelSafeDist
							? $"Move {ReelSafeDist - dist} more tile{(ReelSafeDist - dist == 1 ? "" : "s")} back to detonate"
							: "[Space]  Detonate!";
					}
				return ""; // charge snapped; item will clear next tick
			}

			if (heldKind == ItemKind.TreasureChest)
				return "[Space]  Drop Chest";
			if (heldKind.Value.IsIdol())
			{
				// Check if we're adjacent to our own placed chest.
				if (_client.PlacedChests != null)
					foreach (var c in _client.PlacedChests)
						if (c.MinerId == _client.LocalMinerId)
						{
							int dx = Math.Abs(c.X - localPos.X);
							int dy = Math.Abs(c.Y - localPos.Y);
							if (dx <= 1 && dy <= 1)
								return $"[Space]  Deposit {IdolName(heldKind.Value)}";
						}
				return $"[Space]  Drop {IdolName(heldKind.Value)}";
			}
		}

		return "";
	}

	private CanvasLayer BuildPauseOverlay()
	{
		var layer = new CanvasLayer { Layer = 45, Visible = false, Name = "PauseOverlay" };

		var bg = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.70f),
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
		};
		layer.AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(center);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
		box.AddThemeConstantOverride("separation", 12);
		center.AddChild(box);

		var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 36);
		box.AddChild(title);

		box.AddChild(new HSeparator());

		var resume = new Button { Text = "Resume" };
		resume.Pressed += HidePauseMenu;
		box.AddChild(resume);

		var settings = new Button { Text = "Settings" };
		settings.Pressed += () => { HidePauseMenu(); _audioPanel.Open(); };
		box.AddChild(settings);

		var quit = new Button { Text = "Quit to Menu" };
		quit.Pressed += () =>
		{
			NetworkManager.Instance.Leave();
			GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
		};
		box.AddChild(quit);

		return layer;
	}

	private void ShowPauseMenu()
	{
		_pauseShown = true;
		_pauseOverlay.Visible = true;
		if (_input != null) _input.Enabled = false;
	}

	private void HidePauseMenu()
	{
		_pauseShown = false;
		_pauseOverlay.Visible = false;
	}

	private void OnNewFloor(int floor)
	{
		_prevEscapeOpen   = false;
		_hadTreasureFloor = false;
		_client.ResetFloor(floor);
		AudioManager.Instance.PlayMusic(SfxLibrary.PickMusic(NetworkManager.Instance.MatchSeed ^ floor));

		var bannerMod = FloorModifiers.Pick(NetworkManager.Instance.MatchSeed, floor);
		_floorChoicePanel?.QueueFree();
		_floorChoicePanel = null;
		string bannerText = floor == 51 ? "BOSS FLOOR"
			: bannerMod != FloorModifier.None ? $"FLOOR {floor}: {FloorModifiers.DisplayName(bannerMod)}"
			: $"FLOOR {floor}";
		_floorBannerLayer?.QueueFree();
		_floorBanner = new Label
		{
			Text = bannerText,
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0f, AnchorRight = 1f,
			AnchorTop = 0.45f, AnchorBottom = 0.45f,
			Modulate = new Color(1, 1, 1, 0f),
		};
		_floorBanner.AddThemeFontSizeOverride("font_size", 64);
		// Host on a CanvasLayer so the banner is fixed to the view window; added straight to
		// Main (a Node2D) it would ride the Camera2D and pin to the map centre instead.
		_floorBannerLayer = new CanvasLayer { Layer = 20, Name = "FloorBannerLayer" };
		_floorBannerLayer.AddChild(_floorBanner);
		AddChild(_floorBannerLayer);
		_floorBannerTimer = BannerTotal;
	}

	private void OnFloorComplete(int floor)
	{
		_floorChoicePanel?.QueueFree();
		var nm = NetworkManager.Instance;

		var layer = new CanvasLayer { Layer = 10 };
		var bg = new ColorRect
		{
			Color = new Color(0f, 0f, 0f, 0.75f),
			AnchorLeft = 0f, AnchorRight = 1f,
			AnchorTop = 0.3f, AnchorBottom = 0.7f,
		};
		layer.AddChild(bg);

		var col = new VBoxContainer
		{
			AnchorLeft = 0.3f, AnchorRight = 0.7f,
			AnchorTop = 0.35f, AnchorBottom = 0.65f,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical   = Control.GrowDirection.Both,
		};

		var title = new Label
		{
			Text = $"Floor {floor} Complete!",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 36);
		col.AddChild(title);

		if (nm.IsHost)
		{
			var btnContinue = new Button { Text = "Continue to next floor" };
			btnContinue.Pressed += () => { _host!.ContinueToNextFloor(); };
			col.AddChild(btnContinue);

			var btnReturn = new Button { Text = "Return to Menu" };
			btnReturn.Pressed += () => { _host!.ReturnToMenuFromFloorClear(); };
			col.AddChild(btnReturn);
		}
		else
		{
			var wait = new Label
			{
				Text = "Waiting for host...",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			col.AddChild(wait);
		}

		layer.AddChild(col);
		AddChild(layer);
		_floorChoicePanel = layer;
	}

	private void OnMatchEnded(long winnerPeerId)
	{
		if (_results != null) return;
		HidePauseMenu();

		// Fog is an alive-only mechanic. Once the local miner is dead and the results
		// screen is up, lift the fog so the whole map is visible behind it. Competitive
		// modes already spectate-on-death via MatchClient, but Expedition is excluded
		// there, so set it explicitly here — a dead miner never re-enters the alive fog
		// branch, so this sticks.
		bool localDead = _client.Miners.Any(m => m.Id == _client.LocalMinerId && !m.Alive);
		if (localDead && _client.FogRenderer != null)
			_client.FogRenderer.SpectatorMode = true;

		_results = new ResultsOverlay { Name = "ResultsOverlay" };
		AddChild(_results);
		var nm = NetworkManager.Instance;
		bool expedition = nm.MatchMode == GameMode.Expedition;
		string label;
		string scoreText = "";
		Action? playAgain = null;

		if (expedition)
		{
			bool won = winnerPeerId != -1;
			label = won
				? (nm.MatchFloor == 51 ? "You conquered the dungeon!" : "You escaped with the gold!")
				: "You died in the mine.";

			int gold = _host?.CumulativeGold ?? 0;
			int score = 100 * nm.MatchFloor + gold;

			// Death cause from the last local miner snapshot.
			string causeStr = "";
			foreach (var m in _client.Miners)
				if (m.Id == _client.LocalMinerId && m.Cause != DeathCause.None)
					causeStr = $"\nCause: {DeathCauseLabel(m.Cause)}";

			scoreText = $"Floor {nm.MatchFloor}  ·  Gold: {gold}g  ·  Score: {score}{causeStr}";

			// Play Again restarts expedition with same settings.
			if (nm.IsHost)
			{
				playAgain = () =>
				{
					nm.StartMatch(nm.MatchMode, nm.MatchTimeLimitSeconds, nm.MatchFlooding,
						nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchBaseMoveSeconds,
						nm.MatchMapScale, nm.MatchExplosive, nm.MatchStartFloor);
					GetTree().ChangeSceneToFile("res://game/Main.tscn");
				};
			}
		}
		else
		{
			label = winnerPeerId == -1
				? "Draw — no survivors"
				: $"Winner: {NameOf(winnerPeerId)}";
		}
		_results.Show(label, nm.IsHost,
			expedition ? "Return to Menu" : "Return to Lobby", scoreText, playAgain);
	}

	private static string NameOf(long peerId) =>
		NetworkManager.Instance.Players.TryGetValue(peerId, out var info) ? info.Name : $"Peer {peerId}";

	private static string DeathCauseLabel(DeathCause cause) => cause switch
	{
		DeathCause.Drowned    => "Drowned",
		DeathCause.Exploded   => "Blown up",
		DeathCause.Left       => "Left behind",
		DeathCause.Fell       => "Fell into a pit",
		DeathCause.Crushed    => "Crushed by a rock",
		DeathCause.Burned     => "Burned by lava",
		DeathCause.Slimed     => "Dissolved by a slime",
		DeathCause.Terrified  => "Scared to death",
		DeathCause.Headbutted => "Headbutted by a goat",
		DeathCause.Mauled     => "Mauled by a zombie",
		DeathCause.Boned      => "Skeletonised",
		DeathCause.Bitten     => "Bitten by a water snake",
		_                     => cause.ToString(),
	};

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

	private void PlayLocalSound(AudioStream stream)
	{
		var p = new AudioStreamPlayer { Stream = stream };
		AddChild(p);
		p.Play();
		p.Finished += () => { if (IsInstanceValid(p)) p.QueueFree(); };
	}

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
