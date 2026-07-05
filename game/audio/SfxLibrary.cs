using Godot;
using System;
using System.Collections.Generic;

namespace Miner49er;

/// <summary>Resolves logical sound names to AudioStreams. Loads
/// res://assets/audio/{name}.{ogg|wav} when present; otherwise returns a cached
/// procedural placeholder (16-bit mono PCM). Music has no placeholder — it is
/// null until the user drops in a loop. Missing files never crash.</summary>
public static class SfxLibrary
{
	private const int MixRate = 22050;
	private static readonly Dictionary<string, AudioStream> _cache = new();
	private static List<AudioStream>? _musicPool;
	private static AudioStream? _lastMusic;

	public static AudioStream Footstep => Get("footstep", () => Noise(0.05f, 220f));
	public static AudioStream Pickaxe => Get("pickaxe", () => Noise(0.10f, 400f));
	public static AudioStream Plant => Get("plant", () => Noise(0.04f, 1500f));
	public static AudioStream Explosion => Get("explosion", () => Noise(0.40f, 120f, decay: true));
	public static AudioStream Death => Get("death", () => Tone(0.30f, 300f, 120f));
	public static AudioStream Drip => Get("drip", () => Tone(0.12f, 900f, 600f));
	public static AudioStream Splash => Get("splash", () => Noise(0.25f, 700f, decay: true));
	public static AudioStream Pickup => Get("pickup", () => Tone(0.12f, 700f, 1200f)); // bright rising blip
	public static AudioStream Spill => Get("spill", () => Tone(0.18f, 500f, 160f)); // gritty falling — rubble spilling an item
	public static AudioStream Grab => Get("grab", () => Tone(0.10f, 600f, 900f));   // crisp pickup/swap
	public static AudioStream Plank => Get("plank", () => Noise(0.10f, 800f, decay: true)); // wooden knock
	public static AudioStream Squelch => Get("squelch", () => Tone(0.16f, 380f, 140f)); // wet mold plop
	public static AudioStream Fall => Get("fall", () => Tone(0.50f, 700f, 90f)); // long descending wail — falling
	public static AudioStream CaveIn     => Get("cavein",      () => Noise(0.45f,  90f, decay: true)); // low rumble — collapsing floor
	public static AudioStream Sizzle     => Get("sizzle",      () => Noise(0.35f, 1200f, decay: true)); // hot hiss — burned alive
	public static AudioStream CrackRumble  => Get("crack_rumble",  () => Noise(0.18f, 130f, decay: true)); // stone creak underfoot
	public static AudioStream LavaCrackle => Get("lava_crackle",  () => Noise(1.00f, 480f));               // fire crackle loop near lava
	public static AudioStream ZombieMoan  => Get("zombie_moan",   () => Tone(0.40f, 180f, 120f));          // low descending moan — plays on the miner when mauled
	public static AudioStream ReelSnap   => Get("reel_snap",     () => Noise(0.18f, 2400f, decay: true)); // sharp wire snap

	// Monster movement sounds (positional, per step)
	public static AudioStream GoatHooves   => Get("goat_hooves",   () => Noise(0.06f, 1800f, decay: true)); // sharp hoof click
	public static AudioStream SlimeSlurp   => Get("slime_slurp",   () => Tone(0.12f, 260f, 180f));          // wet squelch
	public static AudioStream GhostWhisper => Get("ghost_whisper", () => Noise(0.15f, 2200f));               // ethereal hiss
	public static AudioStream ZombieGroan  => Get("zombie_groan",  () => Tone(0.35f, 160f, 90f));            // low moaning shuffle

	// Monster death sounds
	public static AudioStream GoatBleat   => Get("goat_bleat",   () => Tone(0.22f, 380f, 260f));            // plaintive bleat
	public static AudioStream SlimeSplat  => Get("slime_splat",  () => Noise(0.14f, 350f, decay: true));    // wet burst
	public static AudioStream GhostScream => Get("ghost_scream", () => Tone(0.45f, 1800f, 300f));           // piercing wail
	public static AudioStream ZombieGrunt => Get("zombie_grunt", () => Noise(0.18f, 140f, decay: true));    // guttural grunt
	public static AudioStream Whistle     => Get("whistle",      () => Tone(0.30f, 1000f, 2200f));          // rising pea-whistle — call to exit
	public static AudioStream Tinnitus   => Get("tinnitus",    () => Tone(5.0f,  4200f, 2600f));           // high-pitched ringing fade — deafen after nearby blast
	public static AudioStream? Music => GetOptional("music_loop");

	// Returns a track from the music pool. When seed is provided all callers with
	// the same seed pick the same track (used so all players in a match hear the same
	// music). When seed is null a random non-repeating track is chosen.
	public static AudioStream? PickMusic(int? seed = null)
	{
		_musicPool ??= BuildMusicPool();
		if (_musicPool.Count == 0) return null;
		if (seed.HasValue)
		{
			int idx = Math.Abs(seed.Value) % _musicPool.Count;
			_lastMusic = _musicPool[idx];
			return _lastMusic;
		}
		if (_musicPool.Count == 1) return _musicPool[0];
		var rng = new Random();
		AudioStream pick;
		int tries = 0;
		do { pick = _musicPool[rng.Next(_musicPool.Count)]; tries++; }
		while (ReferenceEquals(pick, _lastMusic) && tries < 10);
		_lastMusic = pick;
		return pick;
	}

	// DirAccess directory listing on res:// is unreliable in exported PCK builds,
	// so enumerate candidates explicitly rather than scanning the directory at runtime.
	private static readonly string[] MusicCandidates =
	{
		"res://assets/audio/music_loop.ogg",
		"res://assets/audio/music_loop1.ogg",
		"res://assets/audio/music_loop2.ogg",
		"res://assets/audio/music_loop3.ogg",
		"res://assets/audio/music_loop4.ogg",
		"res://assets/audio/music_loop5.ogg",
		"res://assets/audio/music_loop6.ogg",
		"res://assets/audio/music_loop7.ogg",
		"res://assets/audio/music_loop8.ogg",
	};

	private static List<AudioStream> BuildMusicPool()
	{
		var pool = new List<AudioStream>();
		foreach (var path in MusicCandidates)
			if (ResourceLoader.Exists(path))
				pool.Add(ResourceLoader.Load<AudioStream>(path));
		return pool;
	}

	private static AudioStream Get(string name, Func<AudioStream> placeholder)
	{
		if (_cache.TryGetValue(name, out var s)) return s;
		var result = TryLoad(name) ?? placeholder();
		_cache[name] = result;
		return result;
	}

	private static AudioStream? GetOptional(string name)
	{
		if (_cache.TryGetValue(name, out var s)) return s;
		var loaded = TryLoad(name);
		if (loaded != null) _cache[name] = loaded;
		return loaded;
	}

	private static AudioStream? TryLoad(string name)
	{
		foreach (var ext in new[] { "ogg", "wav" })
		{
			string path = $"res://assets/audio/{name}.{ext}";
			if (ResourceLoader.Exists(path)) return ResourceLoader.Load<AudioStream>(path);
		}
		return null;
	}

	private static AudioStreamWav Noise(float seconds, float lowpassHz, bool decay = false)
	{
		int n = Mathf.Max(1, (int)(seconds * MixRate));
		var data = new byte[n * 2];
		var rng = new Random(unchecked((int)(seconds * 1000f) ^ (int)lowpassHz));
		float prev = 0f;
		float alpha = Mathf.Clamp(lowpassHz / MixRate, 0.02f, 1f);
		for (int i = 0; i < n; i++)
		{
			float white = (float)(rng.NextDouble() * 2.0 - 1.0);
			prev += alpha * (white - prev);
			float env = decay ? 1f - (float)i / n : 1f;
			short v = (short)(Mathf.Clamp(prev * env, -1f, 1f) * 12000f);
			data[i * 2] = (byte)(v & 0xff);
			data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
		}
		return Wav(data);
	}

	private static AudioStreamWav Tone(float seconds, float startHz, float endHz)
	{
		int n = Mathf.Max(1, (int)(seconds * MixRate));
		var data = new byte[n * 2];
		double phase = 0;
		for (int i = 0; i < n; i++)
		{
			float t = (float)i / n;
			float hz = Mathf.Lerp(startHz, endHz, t);
			phase += 2.0 * Mathf.Pi * hz / MixRate;
			float env = 1f - t;
			short v = (short)(Mathf.Sin((float)phase) * env * 12000f);
			data[i * 2] = (byte)(v & 0xff);
			data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
		}
		return Wav(data);
	}

	private static AudioStreamWav Wav(byte[] data) => new()
	{
		Format = AudioStreamWav.FormatEnum.Format16Bits,
		MixRate = MixRate,
		Stereo = false,
		Data = data,
	};
}
