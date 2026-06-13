using Godot;

namespace Miner49er;

/// <summary>Reusable audio settings overlay: Music + SFX volume sliders and a
/// Music on/off toggle, editing AudioManager live and persisting on change.
/// Hidden until Open(); used by both the Main Menu and the in-match controller.</summary>
public partial class AudioSettingsPanel : CanvasLayer
{
	private HSlider _music = null!;
	private HSlider _sfx = null!;
	private CheckBox _musicOn = null!;

	public bool IsOpen => Visible;

	public override void _Ready()
	{
		Layer = 100; // above other UI

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		AddChild(panel);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
		panel.AddChild(box);

		var title = new Label { Text = "Audio" };
		title.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(title);

		box.AddChild(new Label { Text = "Music" });
		_music = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_music);

		box.AddChild(new Label { Text = "SFX" });
		_sfx = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(240, 0) };
		box.AddChild(_sfx);

		_musicOn = new CheckBox { Text = "Music on" };
		box.AddChild(_musicOn);

		var close = new Button { Text = "Close" };
		box.AddChild(close);

		SyncFromManager();

		_music.ValueChanged += v => AudioManager.Instance.SetMusicVolume((float)v);
		_sfx.ValueChanged += v => AudioManager.Instance.SetSfxVolume((float)v);
		_musicOn.Toggled += on => AudioManager.Instance.SetMusicEnabled(on);
		close.Pressed += Close;

		Visible = false;
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
