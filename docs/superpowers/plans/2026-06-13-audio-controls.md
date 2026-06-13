# Audio Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A shared settings panel (Main Menu + in-match) with Music/SFX volume sliders and a Music on/off toggle, backed by `AudioManager` and persisted to disk.

**Architecture:** `AudioManager` (existing autoload) owns the live audio settings and applies them to the Music/SFX buses; the listen-duck becomes a relative offset from the user's chosen levels. A static `SettingsStore` persists to `user://settings.cfg`. A reusable `AudioSettingsPanel` overlay edits the values live and is opened from the Main Menu and in-match.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C#, `game/` adapter (TAB indent). No pure-C# Core surface.

**Conventions for every commit:**
- Build: `dotnet build Miner49er.sln` (expect `0 Warning(s) 0 Error(s)`).
- This feature is entirely engine-coupled (Godot `AudioServer`, `ConfigFile`, UI nodes); there is **nothing to unit-test in the Core suite**. Each task verifies by building; behavior is verified by the play-test in Task 4.
- Stage **only the exact files listed** — never `git add -A`. Do **not** stage `project.godot`, `game/Splash.tscn`, `assets/Splash.png*`, `.superpowers/`.
- Every commit message ends with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- `game/` uses TAB indent.

---

## File Structure

- **Create** `game/audio/SettingsStore.cs` — static ConfigFile load/save of audio prefs.
- **Modify** `game/audio/AudioManager.cs` — volumes + MusicEnabled, relative duck, load on `_Ready`.
- **Create** `game/ui/AudioSettingsPanel.cs` — shared overlay with the sliders + toggle.
- **Modify** `game/InputBindings.cs` — new `settings` action.
- **Modify** `game/ui/MainMenu.cs` — Settings button opens the panel.
- **Modify** `game/Main.cs` — in-match toggle (`O`), ESC-closes-panel, input gating while open.

---

## Task 1: `SettingsStore` — persist audio prefs

**Files:**
- Create: `game/audio/SettingsStore.cs`

- [ ] **Step 1: Create the store**

Create `game/audio/SettingsStore.cs` (TAB indent):

```csharp
using Godot;

namespace Miner49er;

/// <summary>Persists local player settings to user://settings.cfg via Godot's
/// ConfigFile. Audio prefs only for now; the reusable seed for Phase-5 input
/// rebinding. All reads fall back to the supplied defaults on any error.</summary>
public static class SettingsStore
{
	private const string Path = "user://settings.cfg";
	private const string Section = "audio";

	public static (float music, float sfx, bool musicEnabled) LoadAudio(
		float defMusic, float defSfx, bool defMusicEnabled)
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (defMusic, defSfx, defMusicEnabled);

		float music = (float)(double)cfg.GetValue(Section, "music_volume", defMusic);
		float sfx = (float)(double)cfg.GetValue(Section, "sfx_volume", defSfx);
		bool musicEnabled = (bool)cfg.GetValue(Section, "music_enabled", defMusicEnabled);
		return (Mathf.Clamp(music, 0f, 1f), Mathf.Clamp(sfx, 0f, 1f), musicEnabled);
	}

	public static void SaveAudio(float music, float sfx, bool musicEnabled)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path); // preserve any other sections if present
		cfg.SetValue(Section, "music_volume", music);
		cfg.SetValue(Section, "sfx_volume", sfx);
		cfg.SetValue(Section, "music_enabled", musicEnabled);
		cfg.Save(Path);
	}
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Miner49er.sln`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add game/audio/SettingsStore.cs
git commit -m "feat(game): persist audio prefs via a ConfigFile SettingsStore

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `AudioManager` — volumes, music toggle, relative duck

Rewrites `AudioManager` so user-set Music/SFX levels are the bus baseline, the listen-duck applies relative offsets, and music on/off is an independent bus mute. Loads persisted values on startup.

**Files:**
- Modify: `game/audio/AudioManager.cs` (full file)

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `game/audio/AudioManager.cs` with (TAB indent):

```csharp
using Godot;

namespace Miner49er;

/// <summary>Autoload owning audio buses, the looping music player, the
/// listen-time duck/lift, master mute, and the player's Music/SFX levels.
/// User levels are the bus baseline; the listen-duck applies a relative offset
/// on top. Positional SFX are spawned by MatchAudio; this manages global state
/// only.</summary>
public partial class AudioManager : Node
{
	public static AudioManager Instance { get; private set; } = null!;

	public const string BusMusic = "Music";
	public const string BusSfx = "SFX";
	public const string BusUi = "UI";

	// Listen-time relative offsets (preserve the original feel: music -6 -> -18,
	// sfx 0 -> +4 dB), now applied on top of the user's chosen baseline.
	private const float MusicDuckOffsetDb = -12f;
	private const float SfxLiftOffsetDb = 4f;
	private const float SilentDb = -80f;     // finite stand-in for -inf at 0%
	private const float VolumeEpsilon = 0.0005f;

	// Defaults preserve today's mix: music 50% = -6 dB, sfx 100% = 0 dB.
	private float _musicVolume = 0.5f;
	private float _sfxVolume = 1.0f;
	private bool _musicEnabled = true;
	private bool _listening;

	private AudioStreamPlayer _music = null!;
	private bool _muted;

	public float MusicVolume => _musicVolume;
	public float SfxVolume => _sfxVolume;
	public bool MusicEnabled => _musicEnabled;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		EnsureBus(BusMusic);
		EnsureBus(BusSfx);
		EnsureBus(BusUi);

		(_musicVolume, _sfxVolume, _musicEnabled) =
			SettingsStore.LoadAudio(_musicVolume, _sfxVolume, _musicEnabled);
		ApplyBuses();

		_music = new AudioStreamPlayer { Name = "Music", Bus = BusMusic };
		AddChild(_music);
		_music.Finished += () => { if (_music.Stream != null) _music.Play(); }; // loop
	}

	private static void EnsureBus(string name)
	{
		if (AudioServer.GetBusIndex(name) != -1) return;
		int idx = AudioServer.BusCount;
		AudioServer.AddBus(idx);
		AudioServer.SetBusName(idx, name);
	}

	private static void SetBusDb(string bus, float db)
	{
		int idx = AudioServer.GetBusIndex(bus);
		if (idx != -1) AudioServer.SetBusVolumeDb(idx, db);
	}

	private static float CurrentDb(string bus)
	{
		int idx = AudioServer.GetBusIndex(bus);
		return idx != -1 ? AudioServer.GetBusVolumeDb(idx) : 0f;
	}

	private static float ToDb(float frac) =>
		frac <= VolumeEpsilon ? SilentDb : Mathf.LinearToDb(frac);

	private float MusicTargetDb => ToDb(_musicVolume) + (_listening ? MusicDuckOffsetDb : 0f);
	private float SfxTargetDb => ToDb(_sfxVolume) + (_listening ? SfxLiftOffsetDb : 0f);

	// Snap both buses to the current target dB + music mute. Used on load and on
	// every settings change (the listen tween animates toward the same targets).
	private void ApplyBuses()
	{
		SetBusDb(BusMusic, MusicTargetDb);
		SetBusDb(BusSfx, SfxTargetDb);
		int mi = AudioServer.GetBusIndex(BusMusic);
		if (mi != -1) AudioServer.SetBusMute(mi, !_musicEnabled);
	}

	public void SetMusicVolume(float v)
	{
		_musicVolume = Mathf.Clamp(v, 0f, 1f);
		ApplyBuses();
		Save();
	}

	public void SetSfxVolume(float v)
	{
		_sfxVolume = Mathf.Clamp(v, 0f, 1f);
		ApplyBuses();
		Save();
	}

	public void SetMusicEnabled(bool on)
	{
		_musicEnabled = on;
		int mi = AudioServer.GetBusIndex(BusMusic);
		if (mi != -1) AudioServer.SetBusMute(mi, !on);
		Save();
	}

	private void Save() => SettingsStore.SaveAudio(_musicVolume, _sfxVolume, _musicEnabled);

	public void PlayMusic(AudioStream? stream)
	{
		if (stream == null) return;
		_music.Stream = stream;
		_music.Play();
	}

	public void StopMusic() => _music.Stop();

	public void SetListening(bool listening)
	{
		_listening = listening;
		var tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(db => SetBusDb(BusMusic, db)),
			CurrentDb(BusMusic), MusicTargetDb, 0.2);
		tween.Parallel().TweenMethod(Callable.From<float>(db => SetBusDb(BusSfx, db)),
			CurrentDb(BusSfx), SfxTargetDb, 0.2);
	}

	public void ToggleMute()
	{
		_muted = !_muted;
		AudioServer.SetBusMute(0, _muted); // master bus
	}
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Miner49er.sln`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add game/audio/AudioManager.cs
git commit -m "feat(game): user Music/SFX levels + music toggle, relative listen-duck

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `AudioSettingsPanel` — the shared overlay

**Files:**
- Create: `game/ui/AudioSettingsPanel.cs`

- [ ] **Step 1: Create the panel**

Create `game/ui/AudioSettingsPanel.cs` (TAB indent):

```csharp
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
```

- [ ] **Step 2: Build**

Run: `dotnet build Miner49er.sln`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add game/ui/AudioSettingsPanel.cs
git commit -m "feat(game): reusable AudioSettingsPanel overlay

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Access points — Main Menu button + in-match toggle

**Files:**
- Modify: `game/InputBindings.cs:22` (add the action const) and `EnsureDefaults`
- Modify: `game/ui/MainMenu.cs`
- Modify: `game/Main.cs`

- [ ] **Step 1: Add the `settings` input action**

In `game/InputBindings.cs`, add the const after the `Exit` const (line 22):

```csharp
	public const string Settings = "settings";  // open the audio settings panel
```

In `EnsureDefaults`, add after the `Bind(Exit, Key.Escape);` line:

```csharp
		Bind(Settings, Key.O);
```

- [ ] **Step 2: Add the Settings button to the Main Menu**

In `game/ui/MainMenu.cs`, add a field beside the others (after `private Label _status = null!;`):

```csharp
	private AudioSettingsPanel _audioPanel = null!;
```

In `_Ready()`, after the `joinBtn` block (`box.AddChild(joinBtn);`) add:

```csharp
		var settingsBtn = new Button { Text = "Settings" };
		settingsBtn.Pressed += () => _audioPanel.Open();
		box.AddChild(settingsBtn);
```

Then after the `_status` block (`box.AddChild(_status);`) add:

```csharp
		_audioPanel = new AudioSettingsPanel { Name = "AudioSettingsPanel" };
		AddChild(_audioPanel);
```

- [ ] **Step 3: Wire the in-match panel into `Main`**

In `game/Main.cs`, add a field after `private DeathFeed _deathFeed = null!;` (line 18):

```csharp
	private AudioSettingsPanel _audioPanel = null!;
```

In `_Ready()`, after the `_deathFeed` block (the `_deathFeed.Init(_client);` line) add:

```csharp
		_audioPanel = new AudioSettingsPanel { Name = "AudioSettingsPanel" };
		AddChild(_audioPanel);
```

In `_UnhandledInput`, make ESC close the panel first (so it doesn't quit the match while the panel is open). Replace the existing method body's `if` with:

```csharp
		if (@event.IsActionPressed(InputBindings.Exit))
		{
			GetViewport().SetInputAsHandled();
			if (_audioPanel.IsOpen) { _audioPanel.Close(); return; }
			NetworkManager.Instance.Leave(); // in-match: ESC backs out to the main menu
			GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
		}
```

In `_PhysicsProcess`, add the panel toggle and fold the panel-open state into the input/listen gates. Replace the line:

```csharp
		if (_input != null) _input.Enabled = !sawLocal || localAlive;
```

with:

```csharp
		if (Input.IsActionJustPressed(InputBindings.Settings))
			_audioPanel.Toggle();
		bool panelOpen = _audioPanel.IsOpen;
		if (_input != null) _input.Enabled = (!sawLocal || localAlive) && !panelOpen;
```

And replace the listening line:

```csharp
		bool listening = localAlive && Input.IsActionPressed(InputBindings.Listen);
```

with:

```csharp
		bool listening = localAlive && !panelOpen && Input.IsActionPressed(InputBindings.Listen);
```

- [ ] **Step 4: Build**

Run: `dotnet build Miner49er.sln`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Play-test (user)**

- From the Main Menu, **Settings** opens the panel; Music/SFX sliders change levels live; the **Music on/off** checkbox silences/restores music **independently** of master mute (`M`).
- In a match, **`O`** toggles the panel; while open, WASD does not move the miner and Listen is suppressed; **ESC** closes the panel (rather than quitting); closing restores movement.
- Hold **Listen** with music up — music ducks ~12 dB from the slider level and SFX lifts, then returns.
- Set custom levels, **quit and relaunch** — the panel reflects the saved values and the mix matches.

- [ ] **Step 6: Commit**

```bash
git add game/InputBindings.cs game/ui/MainMenu.cs game/Main.cs
git commit -m "feat(game): open audio settings from the menu and in-match (O)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Completion

After all four tasks and a successful play-test: announce and use **superpowers:finishing-a-development-branch**. There is no Core test suite surface for this feature, so test verification is the build (`dotnet build Miner49er.sln`, 0/0) plus the Task 4 play-test. Merge only with explicit user authorization. Branch: `audio-controls`.
