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
	private OptionButton _speedPicker = null!;
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

		_modePicker = new OptionButton();
		_modePicker.AddItem("Last Man Standing", (int)GameMode.LastManStanding);
		_modePicker.AddItem("Gold Rush", (int)GameMode.GoldRush);
		_modePicker.AddItem("Reach Center", (int)GameMode.ReachCenter);
		_modePicker.Select(0);
		_modePicker.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_modePicker);

		_timePicker = new OptionButton();
		_timePicker.AddItem("No Time Limit", 0);
		_timePicker.AddItem("1 min", 60);
		_timePicker.AddItem("2 min", 120);
		_timePicker.AddItem("3 min", 180);
		_timePicker.AddItem("5 min", 300);
		_timePicker.Select(1); // default 1 min
		_timePicker.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_timePicker);

		_floodCheck = new CheckBox { Text = "Flooding" };
		_floodCheck.Visible = NetworkManager.Instance.IsHost;
		// Flooding needs a clock: bump "No Time Limit" -> 1 min when enabled.
		_floodCheck.Toggled += (bool on) => { if (on && _timePicker.Selected == 0) _timePicker.Select(1); };
		box.AddChild(_floodCheck);

		_pitsCheck = new CheckBox { Text = "Pits" };
		_pitsCheck.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_pitsCheck);

		_caveInCheck = new CheckBox { Text = "Cave-ins" };
		_caveInCheck.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_caveInCheck);

		_lavaCheck = new CheckBox { Text = "Lava" };
		_lavaCheck.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_lavaCheck);

		_speedPicker = new OptionButton();
		_speedPicker.AddItem("Slow", 0);
		_speedPicker.AddItem("Standard", 1);
		_speedPicker.AddItem("Fast", 2);
		_speedPicker.Select(1); // default Standard
		_speedPicker.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_speedPicker);

		_startBtn = new Button { Text = "Start Match", Disabled = true };
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch(
			(GameMode)_modePicker.GetSelectedId(),
			_timePicker.GetSelectedId(),
			_floodCheck.ButtonPressed,
			_pitsCheck.ButtonPressed,
			_caveInCheck.ButtonPressed,
			_lavaCheck.ButtonPressed,
			new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected]);
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
		switch (nm.Status)
		{
			case InternetStatus.Discovering:
				_codeLabel.Visible = true;
				_codeLabel.Text = "Opening router…";
				_copyBtn.Visible = false;
				break;
			case InternetStatus.Mapped:
				_codeLabel.Visible = true;
				_codeLabel.Text = $"Internet code: {nm.HostCode}";
				_copyBtn.Visible = true;
				break;
			case InternetStatus.Failed:
				_codeLabel.Visible = true;
				_codeLabel.Text = "Couldn't open your router automatically (UPnP unavailable).\n"
					+ "LAN players can still join via your local address.\n"
					+ "For internet play, forward port 27649 and share your public IP.";
				_copyBtn.Visible = false;
				break;
			default: // Off
				_codeLabel.Visible = false;
				_copyBtn.Visible = false;
				break;
		}
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
