using Godot;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.AI;

namespace Miner49er;

public partial class Lobby : Control
{
	private VBoxContainer _list = null!;
	private Button _readyBtn = null!;
	private Button _startBtn = null!;
	private Label _hint = null!;
	private Label _modeDesc = null!;
	private OptionButton _modePicker = null!;
	private OptionButton _timePicker = null!;
	private CheckBox _floodCheck = null!;
	private CheckBox _pitsCheck = null!;
	private CheckBox _caveInCheck = null!;
	private CheckBox _lavaCheck = null!;
	private CheckBox _prizeCheck = null!;
	private OptionButton _prizeFreqPicker = null!;
	private OptionButton _heistWinByPicker = null!;
	private OptionButton _heistRespawnPicker = null!;
	private CheckButton _advancedToggle = null!;
	private VBoxContainer _advancedBox = null!;
	private OptionButton _explosivePicker = null!;
	private OptionButton _speedPicker = null!;
	private OptionButton _explosionSizePicker = null!;
	private OptionButton _visionPicker = null!;
	private OptionButton _mapSizePicker = null!;
	private SpinBox _startFloorPicker = null!;
	private Label _codeLabel = null!;
	private Button _copyBtn = null!;
	private OptionButton _botSkillPicker = null!;
	private Button _addBotBtn = null!;
	private Label? _pendingModeLabel;

	public override void _Ready()
	{
		AudioManager.Instance.PlayMusic(SfxLibrary.PickMusic());

		var screen = new CenterContainer();
		screen.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(screen);

		var columns = new HBoxContainer();
		columns.AddThemeConstantOverride("separation", 40);
		screen.AddChild(columns);

		// ── Left column: mode settings ────────────────────────────────────────
		var leftCol = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
		leftCol.AddThemeConstantOverride("separation", 14);
		columns.AddChild(leftCol);

		var (savedMode, savedTime, savedFlood, savedPits, savedCaveIn, savedLava, savedSpeed, savedMapScale, savedExplosive, savedStartFloor, savedBlast, savedVision, savedPrize, savedPrizeFreq, savedAdvanced, savedHeistWinBy, savedHeistRespawn) = SettingsStore.LoadLobby();

		_modePicker = new OptionButton();
		_modePicker.AddItem("Last Man Standing",  (int)GameMode.LastManStanding);
		_modePicker.AddItem("Gold Rush",          (int)GameMode.GoldRush);
		_modePicker.AddItem("Reach Center",       (int)GameMode.ReachCenter);
		_modePicker.AddItem("Expedition",         (int)GameMode.Expedition);
		_modePicker.AddItem("Treasure Hunt",      (int)GameMode.TreasureHunt);
		_modePicker.AddItem("Demolition Derby",   (int)GameMode.DemolitionDerby);
		_modePicker.AddItem("Treasure Heist",     (int)GameMode.TreasureHeist);
		_modePicker.Select(savedMode);
		_modePicker.Visible = NetworkManager.Instance.IsHost;
		leftCol.AddChild(_modePicker);

		_modeDesc = new Label
		{
			Text = ModeDescription(_modePicker.GetSelectedId()),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(220, 0),
		};
		leftCol.AddChild(_modeDesc);

		_timePicker = new OptionButton();
		_timePicker.AddItem("No Time Limit", 0);
		_timePicker.AddItem("1 min",  60);
		_timePicker.AddItem("2 min",  120);
		_timePicker.AddItem("3 min",  180);
		_timePicker.AddItem("5 min",  300);
		_timePicker.AddItem("7 min",  420);
		_timePicker.AddItem("10 min", 600);
		int timeIdx = Enumerable.Range(0, _timePicker.ItemCount)
			.FirstOrDefault(i => _timePicker.GetItemId(i) == savedTime, 1);
		_timePicker.Select(timeIdx);
		_timePicker.Visible = NetworkManager.Instance.IsHost;
		leftCol.AddChild(_timePicker);

		_mapSizePicker = new OptionButton();
		_mapSizePicker.AddItem("Small",  1);
		_mapSizePicker.AddItem("Medium", 2);
		_mapSizePicker.AddItem("Large",  3);
		_mapSizePicker.AddItem("Huge",   4);
		int sizeIdx = Enumerable.Range(0, _mapSizePicker.ItemCount)
			.FirstOrDefault(i => _mapSizePicker.GetItemId(i) == savedMapScale, 0);
		_mapSizePicker.Select(sizeIdx);
		_mapSizePicker.ItemSelected += _ => SuggestTimerForMapSize();
		leftCol.AddChild(_mapSizePicker);

		_startFloorPicker = new SpinBox { MinValue = 1, MaxValue = 50, Step = 1, Value = savedStartFloor };
		leftCol.AddChild(_startFloorPicker);

		_modePicker.ItemSelected += _ =>
		{
			RefreshModeControls();
			NetworkManager.Instance.BroadcastPendingMode(_modePicker.GetSelectedId());
		};

		// Advanced/Simple split: the toggle stays in the always-visible (Simple) column;
		// every "bells & whistles" control below lives in _advancedBox, shown only when on.
		_advancedToggle = new CheckButton { Text = "Advanced options", ButtonPressed = savedAdvanced };
		_advancedToggle.Visible = NetworkManager.Instance.IsHost;
		_advancedToggle.Toggled += _ => RefreshModeControls();
		leftCol.AddChild(_advancedToggle);

		_advancedBox = new VBoxContainer();
		_advancedBox.AddThemeConstantOverride("separation", 14);
		leftCol.AddChild(_advancedBox);

		_floodCheck = new CheckBox { Text = "Flooding", ButtonPressed = savedFlood };
		_floodCheck.Visible = NetworkManager.Instance.IsHost;
		_floodCheck.Toggled += (bool on) => { if (on && _timePicker.Selected == 0) _timePicker.Select(1); };
		_advancedBox.AddChild(_floodCheck);

		_pitsCheck = new CheckBox { Text = "Pits", ButtonPressed = savedPits };
		_pitsCheck.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_pitsCheck);

		_caveInCheck = new CheckBox { Text = "Cave-ins", ButtonPressed = savedCaveIn };
		_caveInCheck.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_caveInCheck);

		_lavaCheck = new CheckBox { Text = "Lava", ButtonPressed = savedLava };
		_lavaCheck.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_lavaCheck);

		_prizeCheck = new CheckBox { Text = "Prize Events", ButtonPressed = savedPrize };
		_prizeCheck.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_prizeCheck);

		// Prize cadence preset — item id is the frequency level fed to SimConfig.SetPrizeFrequency.
		_prizeFreqPicker = new OptionButton();
		_prizeFreqPicker.AddItem("Events: Rare",       0);
		_prizeFreqPicker.AddItem("Events: Occasional", 1);
		_prizeFreqPicker.AddItem("Events: Frequent",   2);
		SelectById(_prizeFreqPicker, savedPrizeFreq);
		_prizeFreqPicker.Visible = NetworkManager.Instance.IsHost;
		_prizeCheck.Toggled += on => _prizeFreqPicker.Disabled = !on;
		_prizeFreqPicker.Disabled = !_prizeCheck.ButtonPressed;
		_advancedBox.AddChild(_prizeFreqPicker);

		_explosivePicker = new OptionButton();
		_explosivePicker.AddItem("Dynamite",           (int)ExplosiveMode.Dynamite);
		_explosivePicker.AddItem("Detonator Specials", (int)ExplosiveMode.DetonatorSpecials);
		_explosivePicker.AddItem("Detonators Only",    (int)ExplosiveMode.DetonatorsOnly);
		_explosivePicker.Select(savedExplosive);
		_explosivePicker.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_explosivePicker);

		_speedPicker = new OptionButton();
		_speedPicker.AddItem("Slow", 0);
		_speedPicker.AddItem("Standard", 1);
		_speedPicker.AddItem("Fast", 2);
		_speedPicker.Select(savedSpeed);
		_speedPicker.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_speedPicker);

		// Explosion size — item id is the blast radius (rock + kill) applied to SimConfig.
		_explosionSizePicker = new OptionButton();
		_explosionSizePicker.AddItem("Blast: Standard", 1);
		_explosionSizePicker.AddItem("Blast: Large",    2);
		_explosionSizePicker.AddItem("Blast: Huge",     3);
		SelectById(_explosionSizePicker, savedBlast);
		_explosionSizePicker.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_explosionSizePicker);

		// Vision — item id is the base fog radius applied to SimConfig.VisionRadius.
		_visionPicker = new OptionButton();
		_visionPicker.AddItem("Vision: Short",  3);
		_visionPicker.AddItem("Vision: Normal", 5);
		_visionPicker.AddItem("Vision: Far",    7);
		SelectById(_visionPicker, savedVision);
		_visionPicker.Visible = NetworkManager.Instance.IsHost;
		_advancedBox.AddChild(_visionPicker);

		_heistWinByPicker = new OptionButton();
		_heistWinByPicker.AddItem("Win: Hold at buzzer", 0);
		_heistWinByPicker.AddItem("Win: Most time",      1);
		_heistWinByPicker.Select(savedHeistWinBy ? 1 : 0);
		_advancedBox.AddChild(_heistWinByPicker);

		_heistRespawnPicker = new OptionButton();
		_heistRespawnPicker.AddItem("Respawns: Unlimited",   1);
		_heistRespawnPicker.AddItem("Respawns: Death match", 0);
		_heistRespawnPicker.Select(savedHeistRespawn ? 0 : 1);
		_advancedBox.AddChild(_heistRespawnPicker);

		// ── Center column: player list ─────────────────────────────────────────
		var centerCol = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
		centerCol.AddThemeConstantOverride("separation", 12);
		columns.AddChild(centerCol);

		var title = new Label { Text = "LOBBY" };
		title.AddThemeFontSizeOverride("font_size", 32);
		centerCol.AddChild(title);

		if (!NetworkManager.Instance.IsHost)
		{
			_pendingModeLabel = new Label { Text = "" };
			_pendingModeLabel.AddThemeFontSizeOverride("font_size", 18);
			centerCol.AddChild(_pendingModeLabel);
		}

		_list = new VBoxContainer { CustomMinimumSize = new Vector2(280, 200) };
		centerCol.AddChild(_list);

		// ── Right column: options ──────────────────────────────────────────────
		var rightCol = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
		rightCol.AddThemeConstantOverride("separation", 14);
		columns.AddChild(rightCol);

		// Fixed, word-wrapping area for the LAN/internet status. Reserving space here keeps a
		// long "UPnP failed" message from widening the column or pushing the buttons below it.
		_codeLabel = new Label
		{
			Text = "",
			Visible = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(240, 92), // reserve for the longest UPnP-failure hint (~4 lines) so buttons don't shift
		};
		rightCol.AddChild(_codeLabel);

		_copyBtn = new Button { Text = "Copy code", Visible = false };
		_copyBtn.Pressed += () => { if (NetworkManager.Instance.HostCode is { } c) DisplayServer.ClipboardSet(c); };
		rightCol.AddChild(_copyBtn);

		if (NetworkManager.Instance.IsHost)
		{
			var botRow = new HBoxContainer();
			rightCol.AddChild(botRow);

			_botSkillPicker = new OptionButton();
			_botSkillPicker.AddItem("Greenhorn",    (int)BotSkill.Greenhorn);
			_botSkillPicker.AddItem("Miner",        (int)BotSkill.Miner);
			_botSkillPicker.AddItem("Foreman",      (int)BotSkill.Foreman);
			_botSkillPicker.AddItem("Dynamite Dan", (int)BotSkill.DynamiteDan);
			botRow.AddChild(_botSkillPicker);

			_addBotBtn = new Button { Text = "+ Add Bot" };
			_addBotBtn.Pressed += () =>
				NetworkManager.Instance.AddBot((BotSkill)_botSkillPicker.GetSelectedId());
			botRow.AddChild(_addBotBtn);
		}

		_readyBtn = new Button { Text = "Toggle Ready" };
		_readyBtn.Pressed += () => NetworkManager.Instance.ToggleReady();
		rightCol.AddChild(_readyBtn);

		_startBtn = new Button { Text = "Start Match", Disabled = true };
		_startBtn.Pressed += () =>
		{
			bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
			bool treasure   = _modePicker.GetSelectedId() == (int)GameMode.TreasureHunt;
			bool derby      = _modePicker.GetSelectedId() == (int)GameMode.DemolitionDerby;
			int mapScale   = _mapSizePicker.GetSelectedId();
			int explosive  = (expedition || treasure || derby) ? 0 : _explosivePicker.GetSelectedId();
			int timeLimit  = (expedition || treasure || derby) ? 0 : _timePicker.GetSelectedId();
			int startFloor = expedition ? (int)_startFloorPicker.Value : 1;
			int blastRadius  = _explosionSizePicker.GetSelectedId();
			int visionRadius = _visionPicker.GetSelectedId();
			SettingsStore.SaveLobby(_modePicker.GetSelectedId(), timeLimit,
				_floodCheck.ButtonPressed, _pitsCheck.ButtonPressed, _caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed, _speedPicker.Selected, mapScale, explosive, startFloor, blastRadius, visionRadius,
				_prizeCheck.ButtonPressed, _prizeFreqPicker.GetSelectedId(), _advancedToggle.ButtonPressed,
				_heistWinByPicker.GetSelectedId() == 1, _heistRespawnPicker.GetSelectedId() == 1);
			NetworkManager.Instance.StartMatch(
				(GameMode)_modePicker.GetSelectedId(),
				timeLimit,
				_floodCheck.ButtonPressed,
				_pitsCheck.ButtonPressed,
				_caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed,
				new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected],
				mapScale,
				(ExplosiveMode)explosive,
				startFloor,
				blastRadius,
				visionRadius,
				_prizeCheck.ButtonPressed,
				_prizeFreqPicker.GetSelectedId(),
				treasureWinByCumulative: _heistWinByPicker.GetSelectedId() == 1,
				treasureRespawn: _heistRespawnPicker.GetSelectedId() == 1);
		};
		_startBtn.Visible = NetworkManager.Instance.IsHost;
		rightCol.AddChild(_startBtn);

		var leaveBtn = new Button { Text = "Leave Game" };
		leaveBtn.Pressed += () =>
		{
			NetworkManager.Instance.Leave();
			GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
		};
		rightCol.AddChild(leaveBtn);

		_hint = new Label { Text = "" };
		rightCol.AddChild(_hint);

		NetworkManager.Instance.LobbyChanged += Refresh;
		NetworkManager.Instance.Disconnected += OnDisconnected;
		NetworkManager.Instance.MatchStarting += OnMatchStarting;
		NetworkManager.Instance.InternetStatusChanged += RefreshInternet;
		Refresh();
		RefreshInternet();
		RefreshModeControls();
		if (NetworkManager.Instance.IsHost)
			NetworkManager.Instance.BroadcastPendingMode(_modePicker.GetSelectedId());
	}

	// Selects the item whose id equals `id` (falls back to the first item).
	private static void SelectById(OptionButton picker, int id)
	{
		for (int i = 0; i < picker.ItemCount; i++)
			if (picker.GetItemId(i) == id) { picker.Select(i); return; }
		if (picker.ItemCount > 0) picker.Select(0);
	}

	private void RefreshModeControls()
	{
		bool isHost     = NetworkManager.Instance.IsHost;
		bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
		bool treasure   = _modePicker.GetSelectedId() == (int)GameMode.TreasureHunt;
		bool derby      = _modePicker.GetSelectedId() == (int)GameMode.DemolitionDerby;
		bool heist      = _modePicker.GetSelectedId() == (int)GameMode.TreasureHeist;
		bool normalMode = !expedition && !treasure && !derby;
		_advancedBox.Visible       = isHost && _advancedToggle.ButtonPressed;
		_timePicker.Visible        = isHost && normalMode;
		_mapSizePicker.Visible     = isHost;
		_startFloorPicker.Visible  = isHost && expedition;
		_explosivePicker.Visible   = isHost && normalMode;
		_prizeCheck.Visible        = isHost && !expedition; // competitive only
		_prizeFreqPicker.Visible   = isHost && !expedition;
		_heistWinByPicker.Visible   = isHost && heist;
		_heistRespawnPicker.Visible = isHost && heist;

		// Treasure Heist is decided at the buzzer, so it must always have a clock:
		// forbid "No Time Limit" and bump off it to a map-size-appropriate default.
		_timePicker.SetItemDisabled(0, heist);
		if (heist && _timePicker.GetSelectedId() == 0)
			SelectById(_timePicker, SuggestedTimeSeconds());

		_modeDesc.Text = ModeDescription(_modePicker.GetSelectedId());
	}

	// Map-size-appropriate round length in seconds (matches the _timePicker item ids).
	private int SuggestedTimeSeconds()
	{
		int mapScale = _mapSizePicker.GetSelectedId();
		return mapScale switch { 1 => 120, 2 => 180, 3 => 300, _ => 420 };
	}

	private void SuggestTimerForMapSize()
	{
		if (_timePicker.GetSelectedId() == 0) return; // "No Time Limit" — don't override user's choice
		SelectById(_timePicker, SuggestedTimeSeconds());
	}

	private static string ModeDescription(int modeId) => (GameMode)modeId switch
	{
		GameMode.LastManStanding => "Rivals. Rivals everywhere. You've all heard the same tip-off and scrambled into the same shaft. There's gold down here — maybe — but your greatest threat isn't the dark. It's each other. Only one miner walks back out.",
		GameMode.GoldRush        => "The seam is real. You've seen the samples. So have the others. Every second you spend reading this is a second they're filling their pockets. Get in, dig fast, and haul out more gold than anyone else before the bell rings.",
		GameMode.ReachCenter     => "They say there's something at the heart of this mountain. Nobody who's gone looking has come back to say what. You've decided you'll be the first. So have a few other idiots. May the fastest miner win.",
		GameMode.Expedition      => "Hearing rumours of ancient treasure you gain entry to the abandoned mine, only to find you are not alone. Survive with your life...and riches.",
		GameMode.TreasureHunt    => "Someone moved the idols. Centuries-old relics, scattered through the dark by hands unknown — and your buyer wants two specific ones. You've got the descriptions, you've got a chest, and you've got competition. First to seal their haul wins.",
		GameMode.DemolitionDerby => "No gold. No mercy. Just dynamite and bad intentions. Every miner starts with unlimited charges and one objective: be the last one breathing. Blow through walls, chase rivals into dead ends, and don't stand next to anything that's about to explode.",
		GameMode.TreasureHeist   => "Grab the buried treasure and hold it when the clock runs out — stone rivals to make them drop it.",
		_                        => "",
	};

	public override void _ExitTree()
	{
		NetworkManager.Instance.LobbyChanged -= Refresh;
		NetworkManager.Instance.Disconnected -= OnDisconnected;
		NetworkManager.Instance.MatchStarting -= OnMatchStarting;
		NetworkManager.Instance.InternetStatusChanged -= RefreshInternet;
	}

	private static string ModeName(int modeId) => (GameMode)modeId switch
	{
		GameMode.LastManStanding => "Last Man Standing",
		GameMode.GoldRush        => "Gold Rush",
		GameMode.ReachCenter     => "Reach Center",
		GameMode.Expedition      => "Expedition",
		GameMode.TreasureHunt    => "Treasure Hunt",
		GameMode.DemolitionDerby => "Demolition Derby",
		GameMode.TreasureHeist   => "Treasure Heist",
		_                        => "Unknown",
	};

	private void Refresh()
	{
		if (_pendingModeLabel != null)
			_pendingModeLabel.Text = $"Mode: {ModeName(NetworkManager.Instance.PendingLobbyMode)}";
		foreach (var c in _list.GetChildren()) c.QueueFree();
		bool isHost = NetworkManager.Instance.IsHost;
		foreach (var (id, info) in NetworkManager.Instance.Players)
		{
			if (NetworkManager.Instance.IsBotPeer(id) && isHost)
			{
				var row = new HBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
				var lbl = new Label
				{
					Text = $"{info.Name}  [{MinerVariants.Names[MinerVariants.Clamp(info.VariantIndex)]}]  [READY]",
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
					ClipText = true, // never let a long name/variant widen the column (layout jump)
				};
				lbl.AddThemeColorOverride("font_color", PlayerColors.At(info.ColorIndex));
				row.AddChild(lbl);
				var removeBtn = new Button { Text = "✕" };
				long capturedId = id;
				removeBtn.Pressed += () => NetworkManager.Instance.RemoveBot(capturedId);
				row.AddChild(removeBtn);
				_list.AddChild(row);
			}
			else
			{
				var row = new Label
				{
					Text = $"{info.Name}  [{MinerVariants.Names[MinerVariants.Clamp(info.VariantIndex)]}]  {(info.Ready ? "[READY]" : "[...]")}",
					ClipText = true, // keep the column a fixed width so the layout doesn't jump
				};
				row.AddThemeColorOverride("font_color", PlayerColors.At(info.ColorIndex));
				_list.AddChild(row);
			}
		}

		var players = NetworkManager.Instance.Players.Values;
		bool canStart = players.Count >= 2 && players.All(p => p.Ready);
		_startBtn.Disabled = !canStart;
		_hint.Text = canStart ? "" : "Need ≥2 players, all ready.";
		if (_addBotBtn != null)
			_addBotBtn.Disabled = NetworkManager.Instance.Players.Count >= 8;
	}

	private void RefreshInternet()
	{
		if (!NetworkManager.Instance.IsHost) return;   // joiners never see the host code
		var nm = NetworkManager.Instance;
		string lanIp   = GetLanIp() ?? "?";
		string lanLine = $"LAN: {lanIp}:{NetworkManager.DefaultPort}";
		switch (nm.Status)
		{
			case InternetStatus.Discovering:
				_codeLabel.Visible = true;
				_codeLabel.Text = $"{lanLine}\nOpening router…";
				_copyBtn.Visible = false;
				break;
			case InternetStatus.Mapped:
				_codeLabel.Visible = true;
				_codeLabel.Text = $"{lanLine}\nInternet: {nm.HostCode}";
				_copyBtn.Visible = true;
				break;
			case InternetStatus.Failed:
				_codeLabel.Visible = true;
				_codeLabel.Text = $"{lanLine}\n{nm.InternetFailHint}";
				_copyBtn.Visible = false;
				break;
			default: // Off — LAN host
				_codeLabel.Visible = true;
				_codeLabel.Text = lanLine;
				_copyBtn.Visible = false;
				break;
		}
	}

	private static string? GetLanIp()
	{
		foreach (string addr in IP.GetLocalAddresses())
		{
			if (addr.Contains(':')) continue; // skip IPv6
			var parts = addr.Split('.');
			if (parts.Length != 4) continue;
			if (addr.StartsWith("192.168.") || addr.StartsWith("10.")) return addr;
			if (addr.StartsWith("172.") && int.TryParse(parts[1], out int b) && b >= 16 && b <= 31) return addr;
		}
		return null;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed(InputBindings.Exit))
		{
			GetViewport().SetInputAsHandled();
			NetworkManager.Instance.Leave(); // lobby: ESC backs out to the main menu
			GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
		}
	}

	private void OnDisconnected()
	{
		GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
	}

	private void OnMatchStarting()
	{
		GetTree().ChangeSceneToFile("res://game/Main.tscn");
	}
}
