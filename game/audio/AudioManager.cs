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
	private Tween? _listenTween;
	private bool _muted;

	private float _deafenOffset;  // extra dB applied to all buses; tweens 0→-60→0 on deafen
	private Tween? _deafenTween;

	public bool IsDeafened => _deafenOffset < -0.5f;

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

	private float MusicTargetDb => ToDb(_musicVolume) + (_listening ? MusicDuckOffsetDb : 0f) + _deafenOffset;
	private float SfxTargetDb   => ToDb(_sfxVolume)   + (_listening ? SfxLiftOffsetDb   : 0f) + _deafenOffset;

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
		_listenTween?.Kill();
		_listenTween = null;
		ApplyBuses();
		_music.Stream = stream;
		_music.Play();
	}

	public void StopMusic() => _music.Stop();

	public void SetListening(bool listening)
	{
		_listening = listening;
		_listenTween?.Kill();
		_listenTween = CreateTween();
		_listenTween.TweenMethod(Callable.From<float>(db => SetBusDb(BusMusic, db)),
			CurrentDb(BusMusic), MusicTargetDb, 0.2);
		_listenTween.Parallel().TweenMethod(Callable.From<float>(db => SetBusDb(BusSfx, db)),
			CurrentDb(BusSfx), SfxTargetDb, 0.2);
	}

	public void TriggerDeafen()
	{
		_listenTween?.Kill();
		_deafenTween?.Kill();
		_deafenOffset = -60f;
		ApplyBuses(); // snap to near-silent immediately

		// Both tinnitus layers play on Master bus so they aren't muted by the SFX/Music cutoff
		var p = new AudioStreamPlayer { Stream = SfxLibrary.Tinnitus };
		AddChild(p);
		p.Play();
		p.Finished += () => { if (IsInstanceValid(p)) p.QueueFree(); };

		var w = new AudioStreamPlayer { Stream = SfxLibrary.TinnitusWhine, VolumeDb = -10f };
		AddChild(w);
		w.Play();
		w.Finished += () => { if (IsInstanceValid(w)) w.QueueFree(); };

		// Restore both buses over 5 seconds
		_deafenTween = CreateTween();
		_deafenTween.TweenMethod(
			Callable.From<float>(v => { _deafenOffset = v; ApplyBuses(); }),
			-60f, 0f, 5.0);
	}

	public void ToggleMute()
	{
		_muted = !_muted;
		AudioServer.SetBusMute(0, _muted); // master bus
	}
}
