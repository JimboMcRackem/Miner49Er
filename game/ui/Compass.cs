using Godot;
using System.Collections.Generic;
using Miner49er.Core;

namespace Miner49er;

/// <summary>8-point listen compass. When Active, highlights the arrow toward the
/// nearest living rival (computed via Core ListenCompass). Hidden otherwise.</summary>
public partial class Compass : CanvasLayer
{
	public bool Active;

	private MatchClient _client = null!;
	private Control _root = null!;
	private readonly Label[] _points = new Label[8];
	// index order matches CompassDirection: N, NE, E, SE, S, SW, W, NW
	private static readonly string[] Glyphs = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 40;
		_root = new Control();
		_root.SetAnchorsPreset(Control.LayoutPreset.Center);
		AddChild(_root);
		for (int i = 0; i < 8; i++)
		{
			double ang = i * Mathf.Pi / 4.0 - Mathf.Pi / 2.0; // i=0 -> up
			var lbl = new Label { Text = Glyphs[i] };
			lbl.AddThemeFontSizeOverride("font_size", 28);
			lbl.Position = new Vector2((float)(Mathf.Cos((float)ang) * 70f), (float)(Mathf.Sin((float)ang) * 70f));
			_root.AddChild(lbl);
			_points[i] = lbl;
		}
		_root.Visible = false;
	}

	public override void _Process(double delta)
	{
		_root.Visible = Active;
		if (!Active) return;
		var dir = ComputeDir();
		for (int i = 0; i < 8; i++)
			_points[i].Modulate = dir.HasValue && (int)dir.Value == i
				? Colors.Yellow
				: new Color(1, 1, 1, 0.25f);
	}

	private CompassDirection? ComputeDir()
	{
		GridPos? self = null;
		var others = new List<GridPos>();
		foreach (var m in _client.Miners)
		{
			if (!m.Alive) continue;
			if (m.Id == _client.LocalMinerId) self = new GridPos(m.X, m.Y);
			else others.Add(new GridPos(m.X, m.Y));
		}
		return self is null ? null : ListenCompass.NearestDirection(self.Value, others);
	}
}
