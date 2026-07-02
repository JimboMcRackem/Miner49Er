using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Listen-mode overlay. Draws three concentric arcs pulsing outward toward
/// the nearest threat or objective. Amber = monster, blue = rival, gold = RC center.
/// No target found → faint full-circle shimmer so the player knows Listen is active.</summary>
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
		var (angle, kind) = ComputeAngle();
		_arc.TargetAngle = angle;
		_arc.TargetKind  = kind;
		_arc.QueueRedraw();
	}

	private enum TargetKind { Monster, Rival, Center }

	private (float? angle, TargetKind kind) ComputeAngle()
	{
		GridPos? self = null;
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) { self = new GridPos(m.X, m.Y); break; }
		if (self is null) return (null, TargetKind.Rival);

		// Reach Center: compass always points to the center tile.
		if (NetworkManager.Instance.MatchMode == GameMode.ReachCenter
		    && _client.CenterTile is { } ct && (ct.X != self.Value.X || ct.Y != self.Value.Y))
		{
			float dx = ct.X - self.Value.X, dy = ct.Y - self.Value.Y;
			return (Mathf.Atan2(dy, dx), TargetKind.Center);
		}

		float bestSq = float.MaxValue;
		float? bestAngle = null;
		TargetKind kind = TargetKind.Rival;

		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			float dx = mo.X - self.Value.X, dy = mo.Y - self.Value.Y;
			float sq = dx * dx + dy * dy;
			if (sq < bestSq) { bestSq = sq; bestAngle = Mathf.Atan2(dy, dx); kind = TargetKind.Monster; }
		}

		foreach (var m in _client.Miners)
		{
			if (!m.Alive || m.Id == _client.LocalMinerId) continue;
			float dx = m.X - self.Value.X, dy = m.Y - self.Value.Y;
			float sq = dx * dx + dy * dy;
			if (sq < bestSq) { bestSq = sq; bestAngle = Mathf.Atan2(dy, dx); kind = TargetKind.Rival; }
		}

		return (bestAngle, kind);
	}

	private partial class ArcCanvas : Control
	{
		public float? TargetAngle;
		public Compass.TargetKind TargetKind;

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
			float t = (tMs % 900f) / 900f;
			float spread = Mathf.DegToRad(35f);

			// Amber-orange = monster, ice-blue = rival, bright gold = center objective.
			var color = TargetKind switch
			{
				Compass.TargetKind.Monster => new Color(1.00f, 0.58f, 0.08f),
				Compass.TargetKind.Center  => new Color(0.95f, 0.88f, 0.15f),
				_                          => new Color(0.40f, 0.85f, 1.00f),
			};

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
