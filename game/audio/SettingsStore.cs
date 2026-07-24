using Godot;
using Miner49er.Core;
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

	public static (bool fullscreen, bool vsync, int windowWidth, int windowHeight) LoadDisplay()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (false, true, 0, 0);
		bool fs = (bool)cfg.GetValue(DisplaySection, "fullscreen", false);
		bool vs = (bool)cfg.GetValue(DisplaySection, "vsync", true);
		int  ww = (int)(long)cfg.GetValue(DisplaySection, "window_width",  0L);
		int  wh = (int)(long)cfg.GetValue(DisplaySection, "window_height", 0L);
		return (fs, vs, ww, wh);
	}

	public static void SaveDisplay(bool fullscreen, bool vsync, int windowWidth = 0, int windowHeight = 0)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(DisplaySection, "fullscreen",     fullscreen);
		cfg.SetValue(DisplaySection, "vsync",          vsync);
		cfg.SetValue(DisplaySection, "window_width",   (long)windowWidth);
		cfg.SetValue(DisplaySection, "window_height",  (long)windowHeight);
		cfg.Save(Path);
	}

	// Returns the last-saved window position, or has=false when none is stored yet
	// (first launch) so the caller can leave the window at its centered default.
	public static (bool has, int x, int y) LoadWindowPosition()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok) return (false, 0, 0);
		if (!cfg.HasSectionKey(DisplaySection, "window_x") || !cfg.HasSectionKey(DisplaySection, "window_y"))
			return (false, 0, 0);
		int x = (int)(long)cfg.GetValue(DisplaySection, "window_x", 0L);
		int y = (int)(long)cfg.GetValue(DisplaySection, "window_y", 0L);
		return (true, x, y);
	}

	// Persists the current window position + size so the app reopens where it was left.
	// No-op in fullscreen (position/size aren't meaningful there).
	public static void SaveWindowGeometry()
	{
		if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
		var pos  = DisplayServer.WindowGetPosition();
		var size = DisplayServer.WindowGetSize();
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(DisplaySection, "window_x",      (long)pos.X);
		cfg.SetValue(DisplaySection, "window_y",      (long)pos.Y);
		cfg.SetValue(DisplaySection, "window_width",  (long)size.X);
		cfg.SetValue(DisplaySection, "window_height", (long)size.Y);
		cfg.Save(Path);
	}

	// ── Player identity (name, color, last address, internet toggle) ──────────

	private const string PlayerSection = "player";

	public static (string name, int colorIndex, int variant, string address, bool overInternet) LoadPlayer()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return ("Miner", 0, 0, "127.0.0.1", true);
		string name    = (string)cfg.GetValue(PlayerSection, "name",          "Miner");
		int    color   = (int)(long)cfg.GetValue(PlayerSection, "color",      0L);
		int    variant = (int)(long)cfg.GetValue(PlayerSection, "variant",    0L);
		string address = (string)cfg.GetValue(PlayerSection, "address",       "127.0.0.1");
		bool   inet    = (bool)cfg.GetValue(PlayerSection, "over_internet",   true);
		return (name, Mathf.Clamp(color, 0, 7), Mathf.Clamp(variant, 0, MinerVariants.Count - 1), address, inet);
	}

	public static void SavePlayerIdentity(string name, int colorIndex, int variantIndex)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(PlayerSection, "name",    name);
		cfg.SetValue(PlayerSection, "color",   (long)colorIndex);
		cfg.SetValue(PlayerSection, "variant", (long)variantIndex);
		cfg.Save(Path);
	}

	public static void SavePlayer(string name, int colorIndex, int variantIndex, string address, bool overInternet)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(PlayerSection, "name",          name);
		cfg.SetValue(PlayerSection, "color",         (long)colorIndex);
		cfg.SetValue(PlayerSection, "variant",       (long)variantIndex);
		cfg.SetValue(PlayerSection, "address",       address);
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

	public static (int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale, int explosive, int startFloor, int blast, int vision, bool prizeEvents, int prizeFreq, bool advanced, bool heistWinByCumulative, bool heistRespawn) LoadLobby()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (0, 60, false, false, false, false, 1, 1, 0, 1, 1, 5, true, 1, false, false, true);
		int  mode       = (int)(long)cfg.GetValue(LobbySection, "game_mode",    0L);
		int  time       = (int)(long)cfg.GetValue(LobbySection, "time_limit",   60L);
		bool flood      = (bool)cfg.GetValue(LobbySection, "flood",   false);
		bool pits       = (bool)cfg.GetValue(LobbySection, "pits",    false);
		bool caveIns    = (bool)cfg.GetValue(LobbySection, "caveins", false);
		bool lava       = (bool)cfg.GetValue(LobbySection, "lava",    false);
		int  speed      = (int)(long)cfg.GetValue(LobbySection, "speed",       1L);
		int  scale      = (int)(long)cfg.GetValue(LobbySection, "map_scale",   1L);
		int  explosive  = (int)(long)cfg.GetValue(LobbySection, "explosive",   0L);
		int  startFloor = (int)(long)cfg.GetValue(LobbySection, "start_floor", 1L);
		int  blast      = (int)(long)cfg.GetValue(LobbySection, "blast_radius", 1L);
		int  vision     = (int)(long)cfg.GetValue(LobbySection, "vision_radius", 5L);
		bool prize      = (bool)cfg.GetValue(LobbySection, "prize_events", true);
		int  prizeFreq  = (int)(long)cfg.GetValue(LobbySection, "prize_freq", 1L);
		bool advanced   = (bool)cfg.GetValue(LobbySection, "advanced", false);
		bool heistWinBy   = (bool)cfg.GetValue(LobbySection, "heist_winby",   false);
		bool heistRespawn = (bool)cfg.GetValue(LobbySection, "heist_respawn", true);
		// Upper bound must track the highest GameMode value (TreasureHeist=6) or saved modes above
		// Expedition(3) get silently clamped away on load, defeating persistence.
		return (Mathf.Clamp(mode, 0, (int)GameMode.GrudgeMatch), time, flood, pits, caveIns, lava,
		        Mathf.Clamp(speed, 0, 2), Mathf.Clamp(scale, 1, 4), Mathf.Clamp(explosive, 0, 2),
		        Mathf.Clamp(startFloor, 1, 20), Mathf.Clamp(blast, 1, 3), Mathf.Clamp(vision, 3, 8), prize,
		        Mathf.Clamp(prizeFreq, 0, 2), advanced, heistWinBy, heistRespawn);
	}

	public static void SaveLobby(int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale, int explosive, int startFloor = 1, int blast = 1, int vision = 5, bool prizeEvents = true, int prizeFreq = 1, bool advanced = false, bool heistWinByCumulative = false, bool heistRespawn = true)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(LobbySection, "game_mode",    (long)gameMode);
		cfg.SetValue(LobbySection, "time_limit",   (long)timeLimit);
		cfg.SetValue(LobbySection, "flood",        flood);
		cfg.SetValue(LobbySection, "pits",         pits);
		cfg.SetValue(LobbySection, "caveins",      caveIns);
		cfg.SetValue(LobbySection, "lava",         lava);
		cfg.SetValue(LobbySection, "speed",        (long)speed);
		cfg.SetValue(LobbySection, "map_scale",    (long)mapScale);
		cfg.SetValue(LobbySection, "explosive",    (long)explosive);
		cfg.SetValue(LobbySection, "start_floor",  (long)startFloor);
		cfg.SetValue(LobbySection, "blast_radius", (long)blast);
		cfg.SetValue(LobbySection, "vision_radius", (long)vision);
		cfg.SetValue(LobbySection, "prize_events", prizeEvents);
		cfg.SetValue(LobbySection, "prize_freq",   (long)prizeFreq);
		cfg.SetValue(LobbySection, "advanced",     advanced);
		cfg.SetValue(LobbySection, "heist_winby",   heistWinByCumulative);
		cfg.SetValue(LobbySection, "heist_respawn", heistRespawn);
		cfg.Save(Path);
	}

	// ── Solo Expedition save / load (floor checkpoint) ────────────────────────

	private const string ExpeditionSection = "expedition_save";

	public record ExpeditionSaveData(
		long Seed, int Floor, int CumulativeGold, int Lives,
		int MapScale, bool Flood, bool Pits, bool CaveIns, bool Lava,
		int PermSpeed, int PermVision, int PermBlast);

	public static bool HasExpeditionSave()
	{
		var cfg = new ConfigFile();
		return cfg.Load(Path) == Error.Ok && cfg.HasSection(ExpeditionSection);
	}

	public static ExpeditionSaveData? LoadExpeditionSave()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok || !cfg.HasSection(ExpeditionSection))
			return null;
		return new ExpeditionSaveData(
			Seed:          (long)cfg.GetValue(ExpeditionSection, "seed",           0L),
			Floor:         (int)(long)cfg.GetValue(ExpeditionSection, "floor",     2L),
			CumulativeGold:(int)(long)cfg.GetValue(ExpeditionSection, "cumulative_gold", 0L),
			Lives:         (int)(long)cfg.GetValue(ExpeditionSection, "lives",     3L),
			MapScale:      (int)(long)cfg.GetValue(ExpeditionSection, "map_scale", 1L),
			Flood:         (bool)cfg.GetValue(ExpeditionSection, "flood",   false),
			Pits:          (bool)cfg.GetValue(ExpeditionSection, "pits",    false),
			CaveIns:       (bool)cfg.GetValue(ExpeditionSection, "caveins", false),
			Lava:          (bool)cfg.GetValue(ExpeditionSection, "lava",    false),
			PermSpeed:     (int)(long)cfg.GetValue(ExpeditionSection, "perm_speed",  0L),
			PermVision:    (int)(long)cfg.GetValue(ExpeditionSection, "perm_vision", 0L),
			PermBlast:     (int)(long)cfg.GetValue(ExpeditionSection, "perm_blast",  0L));
	}

	public static void SaveExpedition(long seed, int floor, int cumulativeGold, int lives,
		int mapScale, bool flood, bool pits, bool caveIns, bool lava,
		int permSpeed, int permVision, int permBlast)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(ExpeditionSection, "seed",           seed);
		cfg.SetValue(ExpeditionSection, "floor",          (long)floor);
		cfg.SetValue(ExpeditionSection, "cumulative_gold",(long)cumulativeGold);
		cfg.SetValue(ExpeditionSection, "lives",          (long)lives);
		cfg.SetValue(ExpeditionSection, "map_scale",      (long)mapScale);
		cfg.SetValue(ExpeditionSection, "flood",          flood);
		cfg.SetValue(ExpeditionSection, "pits",           pits);
		cfg.SetValue(ExpeditionSection, "caveins",        caveIns);
		cfg.SetValue(ExpeditionSection, "lava",           lava);
		cfg.SetValue(ExpeditionSection, "perm_speed",     (long)permSpeed);
		cfg.SetValue(ExpeditionSection, "perm_vision",    (long)permVision);
		cfg.SetValue(ExpeditionSection, "perm_blast",     (long)permBlast);
		cfg.Save(Path);
	}

	public static void ClearExpeditionSave()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok || !cfg.HasSection(ExpeditionSection)) return;
		cfg.EraseSection(ExpeditionSection);
		cfg.Save(Path);
	}
}
