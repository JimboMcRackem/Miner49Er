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
	private LineEdit     _soloName    = null!;
	private OptionButton _soloColor   = null!;
	private OptionButton _soloVariant = null!;
	private HSlider      _sizeSlider = null!;
	private CheckBox     _soloFlood  = null!;
	private CheckBox     _soloPits   = null!;
	private CheckBox     _soloCaveIn = null!;
	private CheckBox     _soloLava   = null!;

	// Multiplayer sub-panel fields
	private LineEdit     _multiName    = null!;
	private OptionButton _multiColor   = null!;
	private OptionButton _multiVariant = null!;
	private LineEdit     _address    = null!;
	private CheckBox     _internet   = null!;
	private Label        _status     = null!;

	// Overlays (always in tree, shown on demand)
	private SettingsPanel   _audioPanel      = null!;
	private HighScorePanel  _highScorePanel  = null!;
	private CreditsPanel    _creditsPanel    = null!;

	public override void _Ready()
	{
		AudioManager.Instance.PlayMusic(SfxLibrary.PickMusic());

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

		_creditsPanel = new CreditsPanel { Name = "CreditsPanel" };
		AddChild(_creditsPanel);

		NetworkManager.Instance.JoinFailed    += OnJoinFailed;
		NetworkManager.Instance.MatchStarting += OnMatchStarting;

		var verLabel = new Label { Text = GameVersion.Full, HorizontalAlignment = HorizontalAlignment.Right };
		verLabel.SetAnchorsPreset(LayoutPreset.BottomRight);
		verLabel.OffsetLeft = -190f;
		verLabel.OffsetTop  = -22f;
		verLabel.AddThemeFontSizeOverride("font_size", 11);
		verLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.45f));
		verLabel.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(verLabel);
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

		var creditsBtn = new Button { Text = "Credits" };
		creditsBtn.Pressed += () => _creditsPanel.Open();
		box.AddChild(creditsBtn);

		return box;
	}

	private Control BuildSoloPanel()
	{
		var (savedName, savedColor, savedVariant, _, _) = SettingsStore.LoadPlayer();
		var (savedScale, savedFlood, savedPits, savedCaveIn, savedLava) = SettingsStore.LoadSolo();

		var box = new VBoxContainer();

		var title = new Label { Text = "SOLO EXPEDITION", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(title);

		box.AddChild(new Label { Text = "" });

		_soloName = new LineEdit { Text = savedName, PlaceholderText = "Name",
			CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_soloName);

		_soloVariant = BuildVariantPicker(savedVariant);
		box.AddChild(_soloVariant);

		// The colour picker doubles as the character preview: each colour swatch
		// is the chosen variant tinted, so there's no separate preview to duplicate.
		_soloColor = BuildColorPicker(savedColor, savedVariant);
		box.AddChild(_soloColor);
		_soloVariant.ItemSelected += _ => RefreshColorIcons(_soloColor, _soloVariant.Selected);

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

		// "Continue" — only shown when a floor-checkpoint save exists.
		if (SettingsStore.HasExpeditionSave())
		{
			var save = SettingsStore.LoadExpeditionSave()!;
			var continueBtn = new Button { Text = $"Continue  (Floor {save.Floor})" };
			continueBtn.Pressed += () => OnContinueExpedition(save);
			box.AddChild(continueBtn);
		}

		var startBtn = new Button { Text = "New Expedition" };
		startBtn.Pressed += OnSoloExpedition;
		box.AddChild(startBtn);

		var backBtn = new Button { Text = "Back" };
		backBtn.Pressed += () => ShowPanel(_modePanel);
		box.AddChild(backBtn);

		return box;
	}

	private Control BuildMultiPanel()
	{
		var (savedName, savedColor, savedVariant, savedAddress, savedInternet) = SettingsStore.LoadPlayer();

		var box = new VBoxContainer();

		var title = new Label { Text = "MULTIPLAYER", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(title);

		box.AddChild(new Label { Text = "" });

		_multiName = new LineEdit { Text = savedName, PlaceholderText = "Name",
			CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_multiName);

		_multiVariant = BuildVariantPicker(savedVariant);
		box.AddChild(_multiVariant);

		// The colour picker doubles as the character preview: each colour swatch
		// is the chosen variant tinted, so there's no separate preview to duplicate.
		_multiColor = BuildColorPicker(savedColor, savedVariant);
		box.AddChild(_multiColor);
		_multiVariant.ItemSelected += _ => RefreshColorIcons(_multiColor, _multiVariant.Selected);

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
		// Rebuild the solo panel each time it's shown so the Continue button
		// reflects the current save state (e.g., after returning from a run).
		bool showSolo = target == _soloPanel;
		if (showSolo)
		{
			var root = _modePanel.GetParent<VBoxContainer>()!;
			root.RemoveChild(_soloPanel);
			_soloPanel.QueueFree();
			_soloPanel = BuildSoloPanel();
			root.AddChild(_soloPanel);
			root.MoveChild(_soloPanel, 1); // order: modePanel(0), soloPanel(1), multiPanel(2)
		}
		_modePanel.Visible  = target == _modePanel;
		_soloPanel.Visible  = showSolo;
		_multiPanel.Visible = target == _multiPanel;
	}

	private static string ScaleName(int scale) => scale switch
	{
		1 => "Small", 2 => "Medium", 3 => "Large", _ => "Huge"
	};

	private static OptionButton BuildColorPicker(int selected, int variant)
	{
		var picker = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
		var icons  = BuildMinerIcons(variant);
		for (int i = 0; i < PlayerColors.Palette.Length; i++)
		{
			if (icons[i] != null) picker.AddIconItem(icons[i]!, PlayerColors.Names[i], i);
			else                  picker.AddItem(PlayerColors.Names[i], i);
		}
		picker.Select(selected);
		return picker;
	}

	// Repaint a colour picker's swatches to show `variant` (called when the
	// variant selection changes), keeping the current colour selected.
	private static void RefreshColorIcons(OptionButton picker, int variant)
	{
		var icons = BuildMinerIcons(variant);
		for (int i = 0; i < PlayerColors.Palette.Length && i < picker.ItemCount; i++)
			if (icons[i] != null) picker.SetItemIcon(i, icons[i]!);
	}

	private static OptionButton BuildVariantPicker(int selected)
	{
		var picker = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
		for (int i = 0; i < MinerVariants.Count; i++)
			picker.AddItem(MinerVariants.Names[i], i);
		picker.Select(MinerVariants.Clamp(selected));
		return picker;
	}

	// ── Input ─────────────────────────────────────────────────────────────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!@event.IsActionPressed(InputBindings.Exit)) return;
		GetViewport().SetInputAsHandled();

		if (_audioPanel.IsOpen)     { _audioPanel.Close();     return; }
		if (_highScorePanel.IsOpen) { _highScorePanel.Close(); return; }
		if (_creditsPanel.IsOpen)   { _creditsPanel.Close();   return; }
		if (_soloPanel.Visible)     { ShowPanel(_modePanel);   return; }
		if (_multiPanel.Visible)    { _status.Text = ""; ShowPanel(_modePanel); return; }
		GetTree().Quit();
	}

	// ── Launchers ─────────────────────────────────────────────────────────────

	private void OnJoinFailed() => _status.Text = "Connection failed.";

	private void OnHost()
	{
		SettingsStore.SavePlayer(_multiName.Text, _multiColor.Selected, _multiVariant.Selected, _address.Text, _internet.ButtonPressed);
		var err = NetworkManager.Instance.HostGame(_multiName.Text, _multiColor.Selected, _internet.ButtonPressed, NetworkManager.DefaultPort, _multiVariant.Selected);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnJoin()
	{
		SettingsStore.SavePlayer(_multiName.Text, _multiColor.Selected, _multiVariant.Selected, _address.Text, _internet.ButtonPressed);
		var err = NetworkManager.Instance.JoinByCode(_address.Text, _multiName.Text, _multiColor.Selected, _multiVariant.Selected);
		if (err != Error.Ok) { _status.Text = $"Join failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnSoloExpedition()
	{
		SettingsStore.SavePlayerIdentity(_soloName.Text, _soloColor.Selected, _soloVariant.Selected);
		SettingsStore.SaveSolo((int)_sizeSlider.Value, _soloFlood.ButtonPressed, _soloPits.ButtonPressed,
			_soloCaveIn.ButtonPressed, _soloLava.ButtonPressed);
		SettingsStore.ClearExpeditionSave(); // discard any old save — this is a fresh run
		var err = NetworkManager.Instance.HostGame(_soloName.Text, _soloColor.Selected, overInternet: false, variantIndex: _soloVariant.Selected);
		if (err != Error.Ok) return;
		NetworkManager.Instance.StartMatch(GameMode.Expedition, 0,
			_soloFlood.ButtonPressed, _soloPits.ButtonPressed, _soloCaveIn.ButtonPressed, _soloLava.ButtonPressed, 0.12f,
			(int)_sizeSlider.Value);
	}

	private void OnContinueExpedition(SettingsStore.ExpeditionSaveData save)
	{
		SettingsStore.SavePlayerIdentity(_soloName.Text, _soloColor.Selected, _soloVariant.Selected);
		var nm = NetworkManager.Instance;
		nm.ExpeditionResume = new NetworkManager.ExpeditionResumeData(
			save.CumulativeGold, save.Lives, save.PermSpeed, save.PermVision, save.PermBlast);
		var err = nm.HostGame(_soloName.Text, _soloColor.Selected, overInternet: false, variantIndex: _soloVariant.Selected);
		if (err != Error.Ok) { nm.ExpeditionResume = null; return; }
		nm.StartMatch(GameMode.Expedition, 0,
			save.Flood, save.Pits, save.CaveIns, save.Lava, 0.12f,
			save.MapScale, startFloor: save.Floor);
	}

	private void OnMatchStarting() => GetTree().ChangeSceneToFile("res://game/Main.tscn");

	// ── Sprite helpers ────────────────────────────────────────────────────────

	private static Texture2D?[] BuildMinerIcons(int variant)
	{
		var icons = new Texture2D?[PlayerColors.Palette.Length];
		// Image.Load("res://...") redirects to .ctex in exports; use resource loader instead.
		string prefix = MinerVariants.Prefix(variant);
		var ctex = GD.Load<CompressedTexture2D>($"res://assets/miners/{prefix}miner_s.png")
		         ?? GD.Load<CompressedTexture2D>("res://assets/miners/miner_s.png");
		var src  = ctex?.GetImage();
		if (src == null) return icons;
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
