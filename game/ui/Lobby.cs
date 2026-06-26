using Godot;
using System.Linq;
using Miner49er.Core;

namespace Miner49er;

public partial class Lobby : Control
{
	private VBoxContainer _list = null!;
	private Button _readyBtn = null!;
	private Button _startBtn = null!;
	private Label _hint = null!;
	private OptionButton _modePicker = null!;
	private OptionButton _timePicker = null!;
	private CheckBox _floodCheck = null!;
	private CheckBox _pitsCheck = null!;
	private CheckBox _caveInCheck = null!;
	private CheckBox _lavaCheck = null!;
	private OptionButton _explosivePicker = null!;
	private OptionButton _speedPicker = null!;
	private OptionButton _mapSizePicker = null!;
	private Label _codeLabel = null!;
	private Button _copyBtn = null!;

	public override void _Ready()
	{
		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var box = new VBoxContainer();
		center.AddChild(box);

		var title = new Label { Text = "LOBBY" };
		title.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(title);

		_list = new VBoxContainer { CustomMinimumSize = new Vector2(320, 200) };
		box.AddChild(_list);

		_codeLabel = new Label { Text = "", Visible = false };
		box.AddChild(_codeLabel);

		_copyBtn = new Button { Text = "Copy code", Visible = false };
		_copyBtn.Pressed += () => { if (NetworkManager.Instance.HostCode is { } c) DisplayServer.ClipboardSet(c); };
		box.AddChild(_copyBtn);

		_readyBtn = new Button { Text = "Toggle Ready" };
		_readyBtn.Pressed += () => NetworkManager.Instance.ToggleReady();
		box.AddChild(_readyBtn);

		var (savedMode, savedTime, savedFlood, savedPits, savedCaveIn, savedLava, savedSpeed, savedMapScale, savedExplosive) = SettingsStore.LoadLobby();

		_modePicker = new OptionButton();
		_modePicker.AddItem("Last Man Standing", (int)GameMode.LastManStanding);
		_modePicker.AddItem("Gold Rush", (int)GameMode.GoldRush);
		_modePicker.AddItem("Reach Center", (int)GameMode.ReachCenter);
		_modePicker.AddItem("Expedition", (int)GameMode.Expedition);
		_modePicker.Select(savedMode);
		_modePicker.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_modePicker);

		_timePicker = new OptionButton();
		_timePicker.AddItem("No Time Limit", 0);
		_timePicker.AddItem("1 min", 60);
		_timePicker.AddItem("2 min", 120);
		_timePicker.AddItem("3 min", 180);
		_timePicker.AddItem("5 min", 300);
		// Select the item whose ID matches the saved time; fall back to index 1 (1 min).
		int timeIdx = Enumerable.Range(0, _timePicker.ItemCount)
			.FirstOrDefault(i => _timePicker.GetItemId(i) == savedTime, 1);
		_timePicker.Select(timeIdx);
		_timePicker.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_timePicker);

		_mapSizePicker = new OptionButton();
		_mapSizePicker.AddItem("Small",  1);
		_mapSizePicker.AddItem("Medium", 2);
		_mapSizePicker.AddItem("Large",  3);
		_mapSizePicker.AddItem("Huge",   4);
		int sizeIdx = Enumerable.Range(0, _mapSizePicker.ItemCount)
			.FirstOrDefault(i => _mapSizePicker.GetItemId(i) == savedMapScale, 0);
		_mapSizePicker.Select(sizeIdx);
		box.AddChild(_mapSizePicker);

		_modePicker.ItemSelected += _ => RefreshModeControls();

		_floodCheck = new CheckBox { Text = "Flooding", ButtonPressed = savedFlood };
		_floodCheck.Visible = NetworkManager.Instance.IsHost;
		_floodCheck.Toggled += (bool on) => { if (on && _timePicker.Selected == 0) _timePicker.Select(1); };
		box.AddChild(_floodCheck);

		_pitsCheck = new CheckBox { Text = "Pits", ButtonPressed = savedPits };
		_pitsCheck.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_pitsCheck);

		_caveInCheck = new CheckBox { Text = "Cave-ins", ButtonPressed = savedCaveIn };
		_caveInCheck.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_caveInCheck);

		_lavaCheck = new CheckBox { Text = "Lava", ButtonPressed = savedLava };
		_lavaCheck.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_lavaCheck);

		_explosivePicker = new OptionButton();
		_explosivePicker.AddItem("Dynamite",           (int)ExplosiveMode.Dynamite);
		_explosivePicker.AddItem("Detonator Specials", (int)ExplosiveMode.DetonatorSpecials);
		_explosivePicker.AddItem("Detonators Only",    (int)ExplosiveMode.DetonatorsOnly);
		_explosivePicker.Select(savedExplosive);
		_explosivePicker.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_explosivePicker);

		_speedPicker = new OptionButton();
		_speedPicker.AddItem("Slow", 0);
		_speedPicker.AddItem("Standard", 1);
		_speedPicker.AddItem("Fast", 2);
		_speedPicker.Select(savedSpeed);
		_speedPicker.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_speedPicker);

		_startBtn = new Button { Text = "Start Match", Disabled = true };
		_startBtn.Pressed += () =>
		{
			bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
			int mapScale = expedition ? _mapSizePicker.GetSelectedId() : 1;
			int explosive = expedition ? 0 : _explosivePicker.GetSelectedId();
			SettingsStore.SaveLobby(_modePicker.GetSelectedId(), _timePicker.GetSelectedId(),
				_floodCheck.ButtonPressed, _pitsCheck.ButtonPressed, _caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed, _speedPicker.Selected, mapScale, explosive);
			NetworkManager.Instance.StartMatch(
				(GameMode)_modePicker.GetSelectedId(),
				expedition ? 0 : _timePicker.GetSelectedId(),
				_floodCheck.ButtonPressed,
				_pitsCheck.ButtonPressed,
				_caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed,
				new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected],
				mapScale,
				(ExplosiveMode)explosive);
		};
		_startBtn.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_startBtn);

		_hint = new Label { Text = "" };
		box.AddChild(_hint);

		NetworkManager.Instance.LobbyChanged += Refresh;
		NetworkManager.Instance.Disconnected += OnDisconnected;
		NetworkManager.Instance.MatchStarting += OnMatchStarting;
		NetworkManager.Instance.InternetStatusChanged += RefreshInternet;
		Refresh();
		RefreshInternet();   // reflect status that may have resolved during the scene change
		RefreshModeControls();
	}

	private void RefreshModeControls()
	{
		bool isHost = NetworkManager.Instance.IsHost;
		bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
		_timePicker.Visible       = isHost && !expedition;
		_mapSizePicker.Visible    = isHost && expedition;
		_explosivePicker.Visible  = isHost && !expedition;
	}

	public override void _ExitTree()
	{
		NetworkManager.Instance.LobbyChanged -= Refresh;
		NetworkManager.Instance.Disconnected -= OnDisconnected;
		NetworkManager.Instance.MatchStarting -= OnMatchStarting;
		NetworkManager.Instance.InternetStatusChanged -= RefreshInternet;
	}

	private void Refresh()
	{
		foreach (var c in _list.GetChildren()) c.QueueFree();
		foreach (var (id, info) in NetworkManager.Instance.Players)
		{
			var row = new Label
			{
				Text = $"{info.Name}  {(info.Ready ? "[READY]" : "[...]")}",
			};
			row.AddThemeColorOverride("font_color", PlayerColors.At(info.ColorIndex));
			_list.AddChild(row);
		}

		var players = NetworkManager.Instance.Players.Values;
		bool canStart = players.Count >= 2 && players.All(p => p.Ready);
		_startBtn.Disabled = !canStart;
		_hint.Text = canStart ? "" : "Need ≥2 players, all ready.";
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
				_codeLabel.Text = $"{lanLine}\nCouldn't open router (UPnP unavailable). For internet play, forward port {NetworkManager.DefaultPort} and share your public IP.";
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
