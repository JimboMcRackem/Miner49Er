using Godot;

namespace Miner49er;

/// <summary>Autoload owning audio buses, the looping music player, the
/// listen-time duck/lift, and master mute. Positional SFX are spawned by
/// MatchAudio; this manages global state only.</summary>
public partial class AudioManager : Node
{
	public static AudioManager Instance { get; private set; } = null!;

	public const string BusMusic = "Music";
	public const string BusSfx = "SFX";
	public const string BusUi = "UI";

	private const float MusicDefaultDb = -6f;
	private const float MusicDuckedDb = -18f;
	private const float SfxDefaultDb = 0f;
	private const float SfxLiftedDb = 4f;

	private AudioStreamPlayer _music = null!;
	private bool _muted;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		EnsureBus(BusMusic);
		EnsureBus(BusSfx);
		EnsureBus(BusUi);
		SetBusDb(BusMusic, MusicDefaultDb);
		SetBusDb(BusSfx, SfxDefaultDb);

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

	public void PlayMusic(AudioStream? stream)
	{
		if (stream == null) return;
		_music.Stream = stream;
		_music.Play();
	}

	public void StopMusic() => _music.Stop();

	public void SetListening(bool listening)
	{
		float musicTo = listening ? MusicDuckedDb : MusicDefaultDb;
		float sfxTo = listening ? SfxLiftedDb : SfxDefaultDb;
		var tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(db => SetBusDb(BusMusic, db)), CurrentDb(BusMusic), musicTo, 0.2);
		tween.Parallel().TweenMethod(Callable.From<float>(db => SetBusDb(BusSfx, db)), CurrentDb(BusSfx), sfxTo, 0.2);
	}

	public void ToggleMute()
	{
		_muted = !_muted;
		AudioServer.SetBusMute(0, _muted); // master bus
	}
}
