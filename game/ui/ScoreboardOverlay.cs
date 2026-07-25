using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Hold Tab to see all miners ranked by score. Visible only while Tab is held.</summary>
public partial class ScoreboardOverlay : CanvasLayer
{
	private MatchClient _client = null!;
	private VBoxContainer _rows = null!;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 35;
		Visible = false;

		var bg = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.80f),
			AnchorLeft = 0.5f, AnchorRight = 0.5f,
			AnchorTop = 0.5f, AnchorBottom = 0.5f,
			CustomMinimumSize = new Vector2(320, 0),
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical   = Control.GrowDirection.Both,
		};
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);

		var frame = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
		center.AddChild(frame);

		var title = new Label { Text = "SCOREBOARD", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 24);
		title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
		frame.AddChild(title);

		frame.AddChild(new HSeparator());

		_rows = new VBoxContainer();
		frame.AddChild(_rows);
	}

	public void Refresh()
	{
		foreach (Node child in _rows.GetChildren()) child.QueueFree();

		var nm = NetworkManager.Instance;
		var mode = nm.MatchMode;

		// Build rows sorted: alive first, then by score descending.
		var miners = new List<(MinerSnapshot snap, string name, int colorIdx)>();
		foreach (var m in _client.Miners)
		{
			string name = "Player";
			int colorIdx = m.Id - 1;
			int idx = m.Id - 1;
			if (idx >= 0 && idx < nm.PeerOrder.Length &&
			    nm.Players.TryGetValue(nm.PeerOrder[idx], out var info))
			{
				name = info.Name;
				colorIdx = info.ColorIndex;
			}
			miners.Add((m, name, colorIdx));
		}

		bool killMode = mode.IsKillScored();
		miners.Sort((a, b) =>
		{
			// Combat modes rank by kills first; others by gold, always with alive above dead.
			if (killMode)
			{
				int byKills = b.snap.Kills.CompareTo(a.snap.Kills);
				if (byKills != 0) return byKills;
			}
			if (a.snap.Alive != b.snap.Alive) return b.snap.Alive.CompareTo(a.snap.Alive);
			return b.snap.Gold.CompareTo(a.snap.Gold);
		});

		foreach (var (snap, name, colorIdx) in miners)
		{
			var row = new HBoxContainer();

			var status = new Label
			{
				Text = snap.Alive ? "●" : "✕",
				CustomMinimumSize = new Vector2(22, 0),
			};
			status.AddThemeFontSizeOverride("font_size", 18);
			status.AddThemeColorOverride("font_color", snap.Alive ? Colors.LimeGreen : Colors.DimGray);
			row.AddChild(status);

			var nameLbl = new Label
			{
				Text = name,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			nameLbl.AddThemeFontSizeOverride("font_size", 18);
			nameLbl.AddThemeColorOverride("font_color", PlayerColors.At(colorIdx));
			row.AddChild(nameLbl);

			string scoreStr = mode switch
			{
				GameMode.TreasureHunt => TreasureScore(snap),
				GameMode.GrudgeMatch => $"{snap.Kills} kills",
				GameMode.LastManStanding or GameMode.DemolitionDerby
					=> $"{snap.Kills} kills · {(snap.Alive ? "Alive" : "Dead")}",
				_ => $"{snap.Gold}g",
			};

			var scoreLbl = new Label
			{
				Text = scoreStr,
				CustomMinimumSize = new Vector2(80, 0),
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			scoreLbl.AddThemeFontSizeOverride("font_size", 18);
			scoreLbl.AddThemeColorOverride("font_color", Colors.White);
			row.AddChild(scoreLbl);

			_rows.AddChild(row);
		}
	}

	private string TreasureScore(MinerSnapshot snap)
	{
		int found = 0;
		if (_client.TreasureProgress != null)
			foreach (var tp in _client.TreasureProgress)
				if (tp.MinerId == snap.Id) { found = tp.Found; break; }
		return $"{found}/2 idols";
	}
}
