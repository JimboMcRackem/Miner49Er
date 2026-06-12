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
	public static AudioStream? Music => GetOptional("music_loop");

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
