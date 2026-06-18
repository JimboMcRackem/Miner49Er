using Godot;
using System;
using System.Collections.Generic;

namespace Miner49er;

public sealed record ScoreEntry(string Name, int Score, int Floor, string Date);

/// <summary>Persists a top-10 high score list to user://scores.cfg using Godot ConfigFile.</summary>
public static class ScoreStore
{
	private const string Path     = "user://scores.cfg";
	private const int    MaxCount = 10;

	public static List<ScoreEntry> Load()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok) return new List<ScoreEntry>();

		var entries = new List<ScoreEntry>();
		foreach (var section in cfg.GetSections())
		{
			string name  = (string)cfg.GetValue(section, "name",  "");
			int    score = (int)   cfg.GetValue(section, "score", 0);
			int    floor = (int)   cfg.GetValue(section, "floor", 0);
			string date  = (string)cfg.GetValue(section, "date",  "");
			entries.Add(new ScoreEntry(name, score, floor, date));
		}

		entries.Sort((a, b) => b.Score.CompareTo(a.Score));
		return entries;
	}

	public static void Submit(string name, int score, int floor)
	{
		var entries = Load();
		entries.Add(new ScoreEntry(name, score, floor, DateTime.Now.ToString("yyyy-MM-dd")));
		entries.Sort((a, b) => b.Score.CompareTo(a.Score));
		if (entries.Count > MaxCount) entries.RemoveRange(MaxCount, entries.Count - MaxCount);

		var cfg = new ConfigFile();
		for (int i = 0; i < entries.Count; i++)
		{
			string section = $"score_{i}";
			cfg.SetValue(section, "name",  entries[i].Name);
			cfg.SetValue(section, "score", entries[i].Score);
			cfg.SetValue(section, "floor", entries[i].Floor);
			cfg.SetValue(section, "date",  entries[i].Date);
		}
		cfg.Save(Path);
	}
}
