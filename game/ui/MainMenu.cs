using Godot;

namespace Miner49er;

public partial class MainMenu : Control
{
	private LineEdit _name = null!;
	private LineEdit _address = null!;
	private OptionButton _color = null!;
	private Label _status = null!;

	public override void _Ready()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(LayoutPreset.Center);
		AddChild(box);

		var title = new Label { Text = "MINER 49ER" };
		title.AddThemeFontSizeOverride("font_size", 48);
		box.AddChild(title);

		_name = new LineEdit { Text = "Miner", PlaceholderText = "Name", CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_name);

		_color = new OptionButton();
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
			_color.AddItem($"Color {i + 1}", i);
		box.AddChild(_color);

		_address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Host IP" };
		box.AddChild(_address);

		var hostBtn = new Button { Text = "Host Game" };
		hostBtn.Pressed += OnHost;
		box.AddChild(hostBtn);

		var joinBtn = new Button { Text = "Join Game" };
		joinBtn.Pressed += OnJoin;
		box.AddChild(joinBtn);

		_status = new Label { Text = "" };
		box.AddChild(_status);

		NetworkManager.Instance.JoinFailed += OnJoinFailed;
	}

	private void OnJoinFailed() => _status.Text = "Connection failed.";

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed(InputBindings.Exit))
		{
			GetViewport().SetInputAsHandled();
			GetTree().Quit(); // main menu: ESC exits the app
		}
	}

	public override void _ExitTree() => NetworkManager.Instance.JoinFailed -= OnJoinFailed;

	private void OnHost()
	{
		var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnJoin()
	{
		var err = NetworkManager.Instance.JoinGame(_address.Text, _name.Text, _color.Selected);
		if (err != Error.Ok) { _status.Text = $"Join failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}
}
