using Godot;
using System.Collections.Generic;
using Miner49er.Core.Input;

namespace Miner49er;

/// <summary>Reusable settings overlay with Audio and Controls tabs. The Audio tab
/// holds Music/SFX volume + Music on/off (editing AudioManager live, persisting on
/// change). Controls is built in BuildControlsTab(). Hidden until Open(); used by
/// both the Main Menu and the in-match controller. Open/Close/Toggle/IsOpen are
/// unchanged from the former AudioSettingsPanel so callers need no logic changes.</summary>
public partial class SettingsPanel : CanvasLayer
{
	private HSlider _music = null!;
	private HSlider _sfx = null!;
	private CheckBox _musicOn = null!;
	private CheckBox _fullscreen = null!;
	private CheckBox _vsync = null!;

	private BindingSet _bindings = null!;
	private string? _capturingAction;          // null = not capturing
	private readonly Dictionary<string, Label> _kbLabels = new();
	private readonly Dictionary<string, Label> _padLabels = new();
	private readonly Dictionary<string, Button> _rebindButtons = new();
	private Label _conflictLabel = null!;

	private static readonly Dictionary<string, string> FriendlyNames = new()
	{
		[InputBindings.MoveUp] = "Move Up",
		[InputBindings.MoveDown] = "Move Down",
		[InputBindings.MoveLeft] = "Move Left",
		[InputBindings.MoveRight] = "Move Right",
		[InputBindings.Pickaxe] = "Pickaxe",
		[InputBindings.Plant] = "Plant Explosive",
		[InputBindings.Listen] = "Listen",
		[InputBindings.UseItem] = "Use Item",
		[InputBindings.Restart] = "Restart",
		[InputBindings.Mute] = "Mute",
		[InputBindings.Settings] = "Settings",
		[InputBindings.Throw] = "Throw Stone",
	};

	public bool IsOpen => Visible;

	public override void _Ready()
	{
		Layer = 100; // above other UI

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer();
		var bg = new StyleBoxFlat
		{
			BgColor = new Color(0.10f, 0.10f, 0.13f, 1f), // fully opaque backing
			BorderColor = new Color(0.35f, 0.35f, 0.42f, 1f),
			BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
			CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
			ContentMarginTop = 28, ContentMarginBottom = 28, ContentMarginLeft = 32, ContentMarginRight = 32,
		};
		panel.AddThemeStyleboxOverride("panel", bg);
		center.AddChild(panel);

		var outer = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
		outer.AddThemeConstantOverride("separation", 14);
		panel.AddChild(outer);

		var tabs = new TabContainer { CustomMinimumSize = new Vector2(460, 360) };
		outer.AddChild(tabs);

		var audioTab = BuildAudioTab();
		audioTab.Name = "Audio";
		tabs.AddChild(audioTab);

		var controlsTab = BuildControlsTab();
		controlsTab.Name = "Controls";
		tabs.AddChild(controlsTab);

		var displayTab = BuildDisplayTab();
		displayTab.Name = "Display";
		tabs.AddChild(displayTab);

		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		outer.AddChild(close);

		SyncFromManager();
		SyncDisplayFromSystem();

		_music.ValueChanged += v => AudioManager.Instance.SetMusicVolume((float)v);
		_sfx.ValueChanged += v => AudioManager.Instance.SetSfxVolume((float)v);
		_musicOn.Toggled += on => AudioManager.Instance.SetMusicEnabled(on);
		_fullscreen.Toggled += on => ApplyDisplayAndSave(on, _vsync.ButtonPressed);
		_vsync.Toggled += on => ApplyDisplayAndSave(_fullscreen.ButtonPressed, on);

		Visible = false;
	}

	private Control BuildAudioTab()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddThemeConstantOverride("separation", 14);

		box.AddChild(new Label { Text = "Music" });
		_music = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(400, 28) };
		box.AddChild(_music);

		box.AddChild(new Label { Text = "SFX" });
		_sfx = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(400, 28) };
		box.AddChild(_sfx);

		_musicOn = new CheckBox { Text = "Music on" };
		box.AddChild(_musicOn);

		return TabPad(box);
	}

	private Control BuildControlsTab()
	{
		_bindings = InputBindings.BuildBindingSet();
		_bindings.FromConfig(SettingsStore.LoadInput());

		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(440, 320) };
		var grid = new GridContainer { Columns = 4 };
		grid.AddThemeConstantOverride("h_separation", 16);
		grid.AddThemeConstantOverride("v_separation", 8);
		scroll.AddChild(grid);

		foreach (var action in InputBindings.RebindableActions)
		{
			grid.AddChild(new Label { Text = FriendlyNames[action] });

			var kb = new Label { Text = KeyName(_bindings.Get(action, BindDevice.Keyboard)) };
			_kbLabels[action] = kb;
			grid.AddChild(kb);

			var pad = new Label { Text = PadName(_bindings.Get(action, BindDevice.Gamepad)) };
			_padLabels[action] = pad;
			grid.AddChild(pad);

			var btn = new Button { Text = "Rebind" };
			string captured = action; // capture loop variable
			btn.Pressed += () => BeginCapture(captured);
			_rebindButtons[action] = btn;
			grid.AddChild(btn);
		}

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddChild(scroll);
		_conflictLabel = new Label { Text = "" };
		_conflictLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.35f));
		box.AddChild(_conflictLabel);
		return TabPad(box);
	}

	private static string KeyName(int code) =>
		code < 0 ? "—" : OS.GetKeycodeString((Key)code);

	private static string PadName(int code) =>
		code < 0 ? "—" : ((JoyButton)code).ToString();

	private void BeginCapture(string action)
	{
		if (_capturingAction != null) return; // one at a time
		_capturingAction = action;
		_conflictLabel.Text = "";
		_rebindButtons[action].Text = "Press a key or button…";
	}

	private void EndCapture()
	{
		if (_capturingAction is { } a) _rebindButtons[a].Text = "Rebind";
		_capturingAction = null;
	}

	public override void _Input(InputEvent @event)
	{
		if (!Visible || _capturingAction is not { } action) return;

		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			GetViewport().SetInputAsHandled();
			if (key.PhysicalKeycode == Key.Escape) { EndCapture(); return; } // cancel
			ApplyRebind(action, BindDevice.Keyboard, (int)key.PhysicalKeycode);
		}
		else if (@event is InputEventJoypadButton btn && btn.Pressed)
		{
			GetViewport().SetInputAsHandled();
			// Settings is keyboard-only: ignore gamepad, keep listening.
			if (_bindings.Get(action, BindDevice.Gamepad) < 0) return;
			ApplyRebind(action, BindDevice.Gamepad, (int)btn.ButtonIndex);
		}
	}

	private void ApplyRebind(string action, BindDevice device, int code)
	{
		if (_bindings.TryRebind(action, device, code, out var conflict))
		{
			InputBindings.Apply(_bindings);
			SettingsStore.SaveInput(_bindings.ToConfig());
			RefreshRow(action);
			_conflictLabel.Text = "";
		}
		else
		{
			var name = conflict != null && FriendlyNames.TryGetValue(conflict, out var f) ? f : conflict;
			_conflictLabel.Text = $"Already used by {name}";
		}
		EndCapture();
	}

	private void RefreshRow(string action)
	{
		_kbLabels[action].Text = KeyName(_bindings.Get(action, BindDevice.Keyboard));
		_padLabels[action].Text = PadName(_bindings.Get(action, BindDevice.Gamepad));
	}

	private Control BuildDisplayTab()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddThemeConstantOverride("separation", 14);

		_fullscreen = new CheckBox { Text = "Fullscreen" };
		box.AddChild(_fullscreen);

		_vsync = new CheckBox { Text = "VSync" };
		box.AddChild(_vsync);

		return TabPad(box);
	}

	private static MarginContainer TabPad(Control inner)
	{
		var m = new MarginContainer();
		m.AddThemeConstantOverride("margin_top", 12);
		m.AddThemeConstantOverride("margin_left", 4);
		m.AddChild(inner);
		return m;
	}

	private void SyncDisplayFromSystem()
	{
		bool fs = DisplayServer.WindowGetMode() is
			DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
		bool vs = DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled;
		_fullscreen.SetBlockSignals(true); _fullscreen.ButtonPressed = fs; _fullscreen.SetBlockSignals(false);
		_vsync.SetBlockSignals(true); _vsync.ButtonPressed = vs; _vsync.SetBlockSignals(false);
	}

	private static void ApplyDisplayAndSave(bool fullscreen, bool vsync)
	{
		DisplayServer.WindowSetMode(fullscreen
			? DisplayServer.WindowMode.Fullscreen
			: DisplayServer.WindowMode.Windowed);
		DisplayServer.WindowSetVsyncMode(vsync
			? DisplayServer.VSyncMode.Enabled
			: DisplayServer.VSyncMode.Disabled);
		SettingsStore.SaveDisplay(fullscreen, vsync);
	}

	// Push current AudioManager values into the controls without firing their
	// change signals (avoids a redundant save when (re)opening).
	private void SyncFromManager()
	{
		var am = AudioManager.Instance;
		_music.SetBlockSignals(true); _music.Value = am.MusicVolume; _music.SetBlockSignals(false);
		_sfx.SetBlockSignals(true); _sfx.Value = am.SfxVolume; _sfx.SetBlockSignals(false);
		_musicOn.SetBlockSignals(true); _musicOn.ButtonPressed = am.MusicEnabled; _musicOn.SetBlockSignals(false);
	}

	public void Open() { SyncFromManager(); SyncDisplayFromSystem(); Visible = true; }
	public void Close() => Visible = false;
	public void Toggle() { if (Visible) Close(); else Open(); }
}
