using Godot;
using System.Collections.Generic;

namespace Miner49er;

/// <summary>Persists local player settings to user://settings.cfg via Godot's
/// ConfigFile. Audio prefs only for now; the reusable seed for Phase-5 input
/// rebinding. All reads fall back to the supplied defaults on any error.</summary>
public static class SettingsStore
{
	private const string Path = "user://settings.cfg";
	private const string Section = "audio";
	private const string InputSection = "input";

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

	// Returns saved input overrides as a flat (key -> code) map; empty if none.
	public static Dictionary<string, long> LoadInput()
	{
		var result = new Dictionary<string, long>();
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok) return result;
		if (!cfg.HasSection(InputSection)) return result;
		foreach (var key in cfg.GetSectionKeys(InputSection))
			result[key] = cfg.GetValue(InputSection, key).AsInt64();
		return result;
	}

	// Persists a BindingSet.ToConfig() map under [input], preserving [audio].
	public static void SaveInput(IReadOnlyDictionary<string, long> values)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path); // keep any existing sections (e.g. audio)
		if (cfg.HasSection(InputSection)) cfg.EraseSection(InputSection);
		foreach (var kv in values)
			cfg.SetValue(InputSection, kv.Key, kv.Value);
		cfg.Save(Path);
	}

	private const string DisplaySection = "display";

	public static (bool fullscreen, bool vsync) LoadDisplay()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (false, true);
		bool fs = (bool)cfg.GetValue(DisplaySection, "fullscreen", false);
		bool vs = (bool)cfg.GetValue(DisplaySection, "vsync", true);
		return (fs, vs);
	}

	public static void SaveDisplay(bool fullscreen, bool vsync)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(DisplaySection, "fullscreen", fullscreen);
		cfg.SetValue(DisplaySection, "vsync", vsync);
		cfg.Save(Path);
	}
}
