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

	// ── Player identity (name, color, last address, internet toggle) ──────────

	private const string PlayerSection = "player";

	public static (string name, int colorIndex, string address, bool overInternet) LoadPlayer()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return ("Miner", 0, "127.0.0.1", true);
		string name    = (string)cfg.GetValue(PlayerSection, "name",          "Miner");
		int    color   = (int)(long)cfg.GetValue(PlayerSection, "color",      0L);
		string address = (string)cfg.GetValue(PlayerSection, "address",       "127.0.0.1");
		bool   inet    = (bool)cfg.GetValue(PlayerSection, "over_internet",   true);
		return (name, Mathf.Clamp(color, 0, 7), address, inet);
	}

	public static void SavePlayerIdentity(string name, int colorIndex)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(PlayerSection, "name",  name);
		cfg.SetValue(PlayerSection, "color", (long)colorIndex);
		cfg.Save(Path);
	}

	public static void SavePlayer(string name, int colorIndex, string address, bool overInternet)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(PlayerSection, "name",         name);
		cfg.SetValue(PlayerSection, "color",        (long)colorIndex);
		cfg.SetValue(PlayerSection, "address",      address);
		cfg.SetValue(PlayerSection, "over_internet", overInternet);
		cfg.Save(Path);
	}

	// ── Solo Expedition options (map scale + hazards) ─────────────────────────

	private const string SoloSection = "solo";

	public static (int mapScale, bool flood, bool pits, bool caveIns, bool lava) LoadSolo()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (1, false, false, false, false);
		int  scale   = (int)(long)cfg.GetValue(SoloSection, "map_scale", 1L);
		bool flood   = (bool)cfg.GetValue(SoloSection, "flood",    false);
		bool pits    = (bool)cfg.GetValue(SoloSection, "pits",     false);
		bool caveIns = (bool)cfg.GetValue(SoloSection, "caveins",  false);
		bool lava    = (bool)cfg.GetValue(SoloSection, "lava",     false);
		return (Mathf.Clamp(scale, 1, 4), flood, pits, caveIns, lava);
	}

	public static void SaveSolo(int mapScale, bool flood, bool pits, bool caveIns, bool lava)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(SoloSection, "map_scale", (long)mapScale);
		cfg.SetValue(SoloSection, "flood",     flood);
		cfg.SetValue(SoloSection, "pits",      pits);
		cfg.SetValue(SoloSection, "caveins",   caveIns);
		cfg.SetValue(SoloSection, "lava",      lava);
		cfg.Save(Path);
	}

	// ── Lobby options (mode, time limit, hazards, speed) ─────────────────────

	private const string LobbySection = "lobby";

	public static (int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale, int explosive) LoadLobby()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (0, 60, false, false, false, false, 1, 1, 0);
		int  mode      = (int)(long)cfg.GetValue(LobbySection, "game_mode",   0L);
		int  time      = (int)(long)cfg.GetValue(LobbySection, "time_limit",  60L);
		bool flood     = (bool)cfg.GetValue(LobbySection, "flood",   false);
		bool pits      = (bool)cfg.GetValue(LobbySection, "pits",    false);
		bool caveIns   = (bool)cfg.GetValue(LobbySection, "caveins", false);
		bool lava      = (bool)cfg.GetValue(LobbySection, "lava",    false);
		int  speed     = (int)(long)cfg.GetValue(LobbySection, "speed",      1L);
		int  scale     = (int)(long)cfg.GetValue(LobbySection, "map_scale",  1L);
		int  explosive = (int)(long)cfg.GetValue(LobbySection, "explosive",  0L);
		return (Mathf.Clamp(mode, 0, 3), time, flood, pits, caveIns, lava,
		        Mathf.Clamp(speed, 0, 2), Mathf.Clamp(scale, 1, 4), Mathf.Clamp(explosive, 0, 2));
	}

	public static void SaveLobby(int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale, int explosive)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(LobbySection, "game_mode",  (long)gameMode);
		cfg.SetValue(LobbySection, "time_limit", (long)timeLimit);
		cfg.SetValue(LobbySection, "flood",      flood);
		cfg.SetValue(LobbySection, "pits",       pits);
		cfg.SetValue(LobbySection, "caveins",    caveIns);
		cfg.SetValue(LobbySection, "lava",       lava);
		cfg.SetValue(LobbySection, "speed",      (long)speed);
		cfg.SetValue(LobbySection, "map_scale",  (long)mapScale);
		cfg.SetValue(LobbySection, "explosive",  (long)explosive);
		cfg.Save(Path);
	}
}
