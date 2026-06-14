using Godot;

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

		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		outer.AddChild(close);

		SyncFromManager();

		_music.ValueChanged += v => AudioManager.Instance.SetMusicVolume((float)v);
		_sfx.ValueChanged += v => AudioManager.Instance.SetSfxVolume((float)v);
		_musicOn.Toggled += on => AudioManager.Instance.SetMusicEnabled(on);

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

		return box;
	}

	// Filled in Task 5. Placeholder keeps the tab present and the file compiling.
	private Control BuildControlsTab()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddChild(new Label { Text = "Controls" });
		return box;
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

	public void Open() { SyncFromManager(); Visible = true; }
	public void Close() => Visible = false;
	public void Toggle() { if (Visible) Close(); else Open(); }
}
