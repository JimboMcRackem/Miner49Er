using Godot;
using Miner49er.Core;

namespace Miner49er;

public partial class MainMenu : Control
{
	// Panels — only one visible at a time
	private Control _modePanel  = null!;
	private Control _soloPanel  = null!;
	private Control _multiPanel = null!;

	// Solo sub-panel fields
	private LineEdit     _soloName   = null!;
	private OptionButton _soloColor  = null!;
	private HSlider      _sizeSlider = null!;
	private CheckBox     _soloFlood  = null!;
	private CheckBox     _soloPits   = null!;
	private CheckBox     _soloCaveIn = null!;
	private CheckBox     _soloLava   = null!;

	// Multiplayer sub-panel fields
	private LineEdit     _multiName  = null!;
	private OptionButton _multiColor = null!;
	private LineEdit     _address    = null!;
	private CheckBox     _internet   = null!;
	private Label        _status     = null!;

	// Overlays (always in tree, shown on demand)
	private SettingsPanel   _audioPanel      = null!;
	private HighScorePanel  _highScorePanel  = null!;

	public override void _Ready()
	{
		var bg = new TextureRect
		{
			Texture     = GD.Load<Texture2D>("res://assets/Splash.png"),
			ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			AnchorRight = 1f, AnchorBottom = 1f,
		};
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		// Root stack — panels sit inside; only one visible at a time
		var root = new VBoxContainer();
		center.AddChild(root);

		_modePanel = BuildModePanel();
		root.AddChild(_modePanel);

		_soloPanel = BuildSoloPanel();
		_soloPanel.Visible = false;
		root.AddChild(_soloPanel);

		_multiPanel = BuildMultiPanel();
		_multiPanel.Visible = false;
		root.AddChild(_multiPanel);

		_audioPanel = new SettingsPanel { Name = "SettingsPanel" };
		AddChild(_audioPanel);

		_highScorePanel = new HighScorePanel { Name = "HighScorePanel" };
		AddChild(_highScorePanel);

		NetworkManager.Instance.JoinFailed    += OnJoinFailed;
		NetworkManager.Instance.MatchStarting += OnMatchStarting;
	}

	public override void _ExitTree()
	{
		NetworkManager.Instance.JoinFailed    -= OnJoinFailed;
		NetworkManager.Instance.MatchStarting -= OnMatchStarting;
	}

	// ── Panel builders ────────────────────────────────────────────────────────

	private Control BuildModePanel()
	{
		var box = new VBoxContainer();

		var title = new Label { Text = "MINER 49ER", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 48);
		box.AddChild(title);

		box.AddChild(new Label { Text = "" }); // spacer

		var soloBtn = new Button { Text = "Solo Expedition" };
		soloBtn.Pressed += () => ShowPanel(_soloPanel);
		box.AddChild(soloBtn);

		var multiBtn = new Button { Text = "Multiplayer" };
		multiBtn.Pressed += () => ShowPanel(_multiPanel);
		box.AddChild(multiBtn);

		box.AddChild(new Label { Text = "" }); // spacer

		var settingsBtn = new Button { Text = "Settings" };
		settingsBtn.Pressed += () => _audioPanel.Open();
		box.AddChild(settingsBtn);

		var scoresBtn = new Button { Text = "High Scores" };
		scoresBtn.Pressed += () => _highScorePanel.Open();
		box.AddChild(scoresBtn);

		return box;
	}

	private Control BuildSoloPanel()
	{
		var (savedName, savedColor, _, _) = SettingsStore.LoadPlayer();
		var (savedScale, savedFlood, savedPits, savedCaveIn, savedLava) = SettingsStore.LoadSolo();

		var box = new VBoxContainer();

		var title = new Label { Text = "SOLO EXPEDITION", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(title);

		box.AddChild(new Label { Text = "" });

		_soloName = new LineEdit { Text = savedName, PlaceholderText = "Name",
			CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_soloName);

		_soloColor = BuildColorPicker(savedColor);
		box.AddChild(_soloColor);

		box.AddChild(new HSeparator());

		var sizeRow = new HBoxContainer();
		sizeRow.AddChild(new Label { Text = "Map size:" });
		_sizeSlider = new HSlider { MinValue = 1, MaxValue = 4, Step = 1, Value = savedScale,
			CustomMinimumSize = new Vector2(120, 0), TickCount = 4, TicksOnBorders = true };
		var sizeName = new Label { Text = ScaleName(savedScale) };
		_sizeSlider.ValueChanged += v => sizeName.Text = ScaleName((int)v);
		sizeRow.AddChild(_sizeSlider);
		sizeRow.AddChild(sizeName);
		box.AddChild(sizeRow);

		_soloFlood  = new CheckBox { Text = "Flooding",  ButtonPressed = savedFlood };
		_soloPits   = new CheckBox { Text = "Pits",      ButtonPressed = savedPits };
		_soloCaveIn = new CheckBox { Text = "Cave-ins",  ButtonPressed = savedCaveIn };
		_soloLava   = new CheckBox { Text = "Lava",      ButtonPressed = savedLava };
		box.AddChild(_soloFlood);
		box.AddChild(_soloPits);
		box.AddChild(_soloCaveIn);
		box.AddChild(_soloLava);

		box.AddChild(new HSeparator());

		var startBtn = new Button { Text = "Start" };
		startBtn.Pressed += OnSoloExpedition;
		box.AddChild(startBtn);

		var backBtn = new Button { Text = "Back" };
		backBtn.Pressed += () => ShowPanel(_modePanel);
		box.AddChild(backBtn);

		return box;
	}

	private Control BuildMultiPanel()
	{
		var (savedName, savedColor, savedAddress, savedInternet) = SettingsStore.LoadPlayer();

		var box = new VBoxContainer();

		var title = new Label { Text = "MULTIPLAYER", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(title);

		box.AddChild(new Label { Text = "" });

		_multiName = new LineEdit { Text = savedName, PlaceholderText = "Name",
			CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_multiName);

		_multiColor = BuildColorPicker(savedColor);
		box.AddChild(_multiColor);

		box.AddChild(new HSeparator());

		_address  = new LineEdit { Text = savedAddress, PlaceholderText = "Code or Host IP" };
		box.AddChild(_address);

		_internet = new CheckBox { Text = "Host over internet (UPnP)", ButtonPressed = savedInternet };
		box.AddChild(_internet);

		var hostBtn = new Button { Text = "Host Game" };
		hostBtn.Pressed += OnHost;
		box.AddChild(hostBtn);

		var joinBtn = new Button { Text = "Join Game" };
		joinBtn.Pressed += OnJoin;
		box.AddChild(joinBtn);

		_status = new Label { Text = "" };
		box.AddChild(_status);

		var backBtn = new Button { Text = "Back" };
		backBtn.Pressed += () => { _status.Text = ""; ShowPanel(_modePanel); };
		box.AddChild(backBtn);

		return box;
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private void ShowPanel(Control target)
	{
		_modePanel.Visible  = target == _modePanel;
		_soloPanel.Visible  = target == _soloPanel;
		_multiPanel.Visible = target == _multiPanel;
	}

	private static string ScaleName(int scale) => scale switch
	{
		1 => "Small", 2 => "Medium", 3 => "Large", _ => "Huge"
	};

	private static OptionButton BuildColorPicker(int selected)
	{
		var picker = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
		var icons  = BuildMinerIcons();
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
		{
			if (icons[i] != null) picker.AddIconItem(icons[i]!, PlayerColors.Names[i], i);
			else                  picker.AddItem(PlayerColors.Names[i], i);
		}
		picker.Select(selected);
		return picker;
	}

	// ── Input ─────────────────────────────────────────────────────────────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!@event.IsActionPressed(InputBindings.Exit)) return;
		GetViewport().SetInputAsHandled();

		if (_audioPanel.IsOpen)     { _audioPanel.Close();     return; }
		if (_highScorePanel.IsOpen) { _highScorePanel.Close(); return; }
		if (_soloPanel.Visible)     { ShowPanel(_modePanel);   return; }
		if (_multiPanel.Visible)    { _status.Text = ""; ShowPanel(_modePanel); return; }
		GetTree().Quit();
	}

	// ── Launchers ─────────────────────────────────────────────────────────────

	private void OnJoinFailed() => _status.Text = "Connection failed.";

	private void OnHost()
	{
		SettingsStore.SavePlayer(_multiName.Text, _multiColor.Selected, _address.Text, _internet.ButtonPressed);
		var err = NetworkManager.Instance.HostGame(_multiName.Text, _multiColor.Selected, _internet.ButtonPressed);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnJoin()
	{
		SettingsStore.SavePlayer(_multiName.Text, _multiColor.Selected, _address.Text, _internet.ButtonPressed);
		var err = NetworkManager.Instance.JoinByCode(_address.Text, _multiName.Text, _multiColor.Selected);
		if (err != Error.Ok) { _status.Text = $"Join failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnSoloExpedition()
	{
		SettingsStore.SavePlayerIdentity(_soloName.Text, _soloColor.Selected);
		SettingsStore.SaveSolo((int)_sizeSlider.Value, _soloFlood.ButtonPressed, _soloPits.ButtonPressed,
			_soloCaveIn.ButtonPressed, _soloLava.ButtonPressed);
		var err = NetworkManager.Instance.HostGame(_soloName.Text, _soloColor.Selected, overInternet: false);
		if (err != Error.Ok) return;
		NetworkManager.Instance.StartMatch(GameMode.Expedition, 0,
			_soloFlood.ButtonPressed, _soloPits.ButtonPressed, _soloCaveIn.ButtonPressed, _soloLava.ButtonPressed, 0.12f,
			(int)_sizeSlider.Value);
	}

	private void OnMatchStarting() => GetTree().ChangeSceneToFile("res://game/Main.tscn");

	// ── Sprite helpers ────────────────────────────────────────────────────────

	private static Texture2D?[] BuildMinerIcons()
	{
		var icons = new Texture2D?[PlayerColors.Palette.Length];
		var src   = new Image();
		if (src.Load("res://assets/miners/miner_s.png") != Error.Ok) return icons;
		src.Convert(Image.Format.Rgba8);
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
			icons[i] = ImageTexture.CreateFromImage(TintGrayscale(src, PlayerColors.At(i)));
		return icons;
	}

	private static Image TintGrayscale(Image src, Color tint)
	{
		var img = (Image)src.Duplicate();
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var px = img.GetPixel(x, y);
				if (px.A < 0.05f) continue;
				float lum = 0.299f * px.R + 0.587f * px.G + 0.114f * px.B;
				if (px.S > 0.3f && px.H < 0.25f && lum > 0.25f) continue; // preserve skin/hat
				float l = lum * 0.6f + 0.4f;
				img.SetPixel(x, y, new Color(tint.R * l, tint.G * l, tint.B * l, px.A));
			}
		return img;
	}
}
