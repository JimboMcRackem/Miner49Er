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
