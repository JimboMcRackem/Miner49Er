using Godot;
using Miner49er.Core;

namespace Miner49er;

public partial class MainMenu : Control
{
	private LineEdit _name = null!;
	private LineEdit _address = null!;
	private OptionButton _color = null!;
	private CheckBox _internet = null!;
	private Label _status = null!;
	private SettingsPanel _audioPanel = null!;
	private HighScorePanel _highScorePanel = null!;
	private HSlider _sizeSlider = null!;
	private CheckBox _soloFlood  = null!;
	private CheckBox _soloPits   = null!;
	private CheckBox _soloCaveIn = null!;
	private CheckBox _soloLava   = null!;

	public override void _Ready()
	{
		var bg = new TextureRect
		{
			Texture = GD.Load<Texture2D>("res://assets/Splash.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			AnchorRight = 1f,
			AnchorBottom = 1f,
		};
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var box = new VBoxContainer();
		center.AddChild(box);

		var title = new Label { Text = "MINER 49ER" };
		title.AddThemeFontSizeOverride("font_size", 48);
		box.AddChild(title);

		_name = new LineEdit { Text = "Miner", PlaceholderText = "Name", CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_name);

		_color = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
		var minerIcons = BuildMinerIcons();
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
		{
			if (minerIcons[i] != null)
				_color.AddIconItem(minerIcons[i]!, PlayerColors.Names[i], i);
			else
				_color.AddItem(PlayerColors.Names[i], i);
		}
		box.AddChild(_color);

		_address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Code or Host IP" };
		box.AddChild(_address);

		_internet = new CheckBox { Text = "Host over internet (UPnP)", ButtonPressed = true };
		box.AddChild(_internet);

		box.AddChild(new HSeparator());

		var sizeRow = new HBoxContainer();
		sizeRow.AddChild(new Label { Text = "Map:" });
		_sizeSlider = new HSlider { MinValue = 1, MaxValue = 4, Step = 1, Value = 1,
			CustomMinimumSize = new Vector2(120, 0), TickCount = 4, TicksOnBorders = true };
		var sizeName = new Label { Text = "Small" };
		_sizeSlider.ValueChanged += v => sizeName.Text = v switch { 1 => "Small", 2 => "Medium", 3 => "Large", _ => "Huge" };
		sizeRow.AddChild(_sizeSlider);
		sizeRow.AddChild(sizeName);
		box.AddChild(sizeRow);

		_soloFlood  = new CheckBox { Text = "Flooding" };
		_soloPits   = new CheckBox { Text = "Pits" };
		_soloCaveIn = new CheckBox { Text = "Cave-ins" };
		_soloLava   = new CheckBox { Text = "Lava" };
		box.AddChild(_soloFlood);
		box.AddChild(_soloPits);
		box.AddChild(_soloCaveIn);
		box.AddChild(_soloLava);

		var soloBtn = new Button { Text = "Expedition (Solo)" };
		soloBtn.Pressed += OnSoloExpedition;
		box.AddChild(soloBtn);

		var hostBtn = new Button { Text = "Host Game" };
		hostBtn.Pressed += OnHost;
		box.AddChild(hostBtn);

		var joinBtn = new Button { Text = "Join Game" };
		joinBtn.Pressed += OnJoin;
		box.AddChild(joinBtn);

		var settingsBtn = new Button { Text = "Settings" };
		settingsBtn.Pressed += () => _audioPanel.Open();
		box.AddChild(settingsBtn);

		var scoresBtn = new Button { Text = "High Scores" };
		scoresBtn.Pressed += () => _highScorePanel.Open();
		box.AddChild(scoresBtn);

		_status = new Label { Text = "" };
		box.AddChild(_status);

		_audioPanel = new SettingsPanel { Name = "SettingsPanel" };
		AddChild(_audioPanel);

		_highScorePanel = new HighScorePanel { Name = "HighScorePanel" };
		AddChild(_highScorePanel);

		NetworkManager.Instance.JoinFailed += OnJoinFailed;
		NetworkManager.Instance.MatchStarting += OnMatchStarting;
	}

	private void OnJoinFailed() => _status.Text = "Connection failed.";

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed(InputBindings.Exit))
		{
			GetViewport().SetInputAsHandled();
			if (_audioPanel.IsOpen)     { _audioPanel.Close();     return; }
			if (_highScorePanel.IsOpen) { _highScorePanel.Close(); return; }
			GetTree().Quit();
		}
	}

	public override void _ExitTree()
	{
		NetworkManager.Instance.JoinFailed -= OnJoinFailed;
		NetworkManager.Instance.MatchStarting -= OnMatchStarting;
	}

	private void OnHost()
	{
		var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected, _internet.ButtonPressed);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnJoin()
	{
		var err = NetworkManager.Instance.JoinByCode(_address.Text, _name.Text, _color.Selected);
		if (err != Error.Ok) { _status.Text = $"Join failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnSoloExpedition()
	{
		var err = NetworkManager.Instance.HostGame(_name.Text, _color.Selected, overInternet: false);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		NetworkManager.Instance.MatchMapScale = (int)_sizeSlider.Value;
		NetworkManager.Instance.StartMatch(GameMode.Expedition, 0,
			_soloFlood.ButtonPressed, _soloPits.ButtonPressed, _soloCaveIn.ButtonPressed, _soloLava.ButtonPressed, 0.12f);
	}

	private void OnMatchStarting() => GetTree().ChangeSceneToFile("res://game/Main.tscn");

	private static Texture2D?[] BuildMinerIcons()
	{
		var icons = new Texture2D?[PlayerColors.Palette.Length];
		var src = new Image();
		if (src.Load("res://assets/miners/miner_s.png") != Error.Ok)
			return icons;
		src.Convert(Image.Format.Rgba8);
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
			icons[i] = ImageTexture.CreateFromImage(TintGrayscale(src, PlayerColors.At(i)));
		return icons;
	}

	private static Image TintGrayscale(Image src, Color tint)
	{
		var img = (Image)src.Duplicate();
		for (int y = 0; y < img.GetHeight(); y++)
		{
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var px = img.GetPixel(x, y);
				float lum = 0.299f * px.R + 0.587f * px.G + 0.114f * px.B;
				img.SetPixel(x, y, new Color(tint.R * lum, tint.G * lum, tint.B * lum, px.A));
			}
		}
		return img;
	}
}
