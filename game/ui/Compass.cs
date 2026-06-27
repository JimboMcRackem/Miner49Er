using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Listen-mode overlay. Draws three concentric arcs pulsing outward toward
/// the nearest entity (monster or rival miner). Amber = monster, blue = player.
/// No entity found → faint full-circle shimmer so the player knows Listen is active.</summary>
public partial class Compass : CanvasLayer
{
	public bool Active;
	private MatchClient _client = null!;
	private ArcCanvas _arc = null!;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 40;
		_arc = new ArcCanvas();
		_arc.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_arc);
		Visible = false;
	}

	public override void _Process(double delta)
	{
		Visible = Active;
		if (!Active) return;
		var (angle, isMonster) = ComputeAngle();
		_arc.TargetAngle = angle;
		_arc.IsMonster = isMonster;
		_arc.QueueRedraw();
	}

	private (float? angle, bool isMonster) ComputeAngle()
	{
		GridPos? self = null;
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) { self = new GridPos(m.X, m.Y); break; }
		if (self is null) return (null, false);

		float bestSq = float.MaxValue;
		float? bestAngle = null;
		bool isMonster = false;

		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			float dx = mo.X - self.Value.X, dy = mo.Y - self.Value.Y;
			float sq = dx * dx + dy * dy;
			if (sq < bestSq) { bestSq = sq; bestAngle = Mathf.Atan2(dy, dx); isMonster = true; }
		}

		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.Id == _client.LocalMinerId) continue;
			float dx = m.X - self.Value.X, dy = m.Y - self.Value.Y;
			float sq = dx * dx + dy * dy;
			if (sq < bestSq) { bestSq = sq; bestAngle = Mathf.Atan2(dy, dx); isMonster = false; }
		}

		return (bestAngle, isMonster);
	}

	private partial class ArcCanvas : Control
	{
		public float? TargetAngle;
		public bool IsMonster;

		public override void _Draw()
		{
			var center = Size / 2f;
			float tMs = (float)Time.GetTicksMsec();

			if (TargetAngle is not { } angle)
			{
				// Listening but nothing detected — faint breathing circle.
				float pulse = 0.07f + 0.05f * Mathf.Sin(tMs / 500f);
				DrawArc(center, 52f, 0f, Mathf.Tau, 48, new Color(0.75f, 0.75f, 0.75f, pulse), 2f);
				return;
			}

			// Three arcs pulse outward in sequence (i=0 nearest, i=2 farthest).
			// Phase offset staggers them so they ripple away from the player.
			float t = (tMs % 900f) / 900f;    // 0→1 in 0.9 s
			float spread = Mathf.DegToRad(35f);

			// Amber-orange for monster, ice-blue for rival player.
			var color = IsMonster
				? new Color(1.00f, 0.58f, 0.08f)
				: new Color(0.40f, 0.85f, 1.00f);

			for (int i = 0; i < 3; i++)
			{
				float r = 36f + i * 26f;
				float phase = (t - i * 0.30f + 1f) % 1f;
				float alpha = Mathf.Sin(phase * Mathf.Pi) * Mathf.Lerp(0.90f, 0.40f, i / 2f);
				if (alpha <= 0f) continue;
				DrawArc(center, r, angle - spread, angle + spread, 20,
					color with { A = alpha }, 3.5f);
			}
		}
	}
}
