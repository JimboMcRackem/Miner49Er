using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Listen-mode overlay. While listening:
///  • Pulsing arcs toward nearest monster (amber) or rival (blue); faint ring if nothing near.
///  • Compass rose with a green needle pointing toward the escape tile (when one exists).</summary>
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
		var (listenAngle, kind) = ComputeListenAngle();
		_arc.TargetAngle = listenAngle;
		_arc.TargetKind  = kind;
		_arc.ExitAngle   = ComputeExitAngle();
		_arc.QueueRedraw();
	}

	private enum TargetKind { Monster, Rival }

	private (float? angle, TargetKind kind) ComputeListenAngle()
	{
		GridPos? self = null;
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) { self = new GridPos(m.X, m.Y); break; }
		if (self is null) return (null, TargetKind.Rival);

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

	private float? ComputeExitAngle()
	{
		// In ReachCenter the goal is the centre tile, not an escape ladder.
		GridPos? target = NetworkManager.Instance.MatchMode == GameMode.ReachCenter
			? _client.CenterTile
			: _client.EscapeTile;
		if (target is not { } et) return null;
		GridPos? self = null;
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) { self = new GridPos(m.X, m.Y); break; }
		if (self is null || (et.X == self.Value.X && et.Y == self.Value.Y)) return null;
		float dx = et.X - self.Value.X, dy = et.Y - self.Value.Y;
		return Mathf.Atan2(dy, dx);
	}

	private partial class ArcCanvas : Control
	{
		public float? TargetAngle;
		public Compass.TargetKind TargetKind;
		public float? ExitAngle;

		// Compass rose geometry
		private const float BezelR   = 100f;
		private const float NeedleR  = 88f;
		private const float TailR    = 20f;
		private const float HeadLen  = 14f;
		private const float HeadHalf = 28f; // degrees

		public override void _Draw()
		{
			var center = Size / 2f;
			float tMs = (float)Time.GetTicksMsec();

			// ── Exit compass rose ──────────────────────────────────────────────
			if (ExitAngle is { } exitA)
			{
				// Bezel ring
				DrawArc(center, BezelR, 0f, Mathf.Tau, 64,
					new Color(0.85f, 0.85f, 0.85f, 0.30f), 1.5f);

				// Cardinal tick marks
				for (int i = 0; i < 8; i++)
				{
					float a = i * Mathf.Pi / 4f;
					float inner = i % 2 == 0 ? BezelR - 9f : BezelR - 5f;
					var from = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * inner;
					var to   = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (BezelR + 2f);
					DrawLine(from, to, new Color(0.85f, 0.85f, 0.85f, 0.35f), 1.5f);
				}

				// Needle tip and tail
				var dir   = new Vector2(Mathf.Cos(exitA), Mathf.Sin(exitA));
				var tip   = center + dir * NeedleR;
				var tail  = center - dir * TailR;

				// Tail (dim)
				DrawLine(center, tail, new Color(0.6f, 0.6f, 0.6f, 0.40f), 2f);

				// Needle shaft (bright green)
				var needleCol = new Color(0.20f, 0.92f, 0.42f, 0.88f);
				DrawLine(center, tip, needleCol, 3f);

				// Arrowhead
				float ha = Mathf.DegToRad(HeadHalf);
				DrawLine(tip, tip - dir.Rotated( ha) * HeadLen, needleCol, 3f);
				DrawLine(tip, tip - dir.Rotated(-ha) * HeadLen, needleCol, 3f);

				// Centre dot
				DrawCircle(center, 4f, new Color(0.85f, 0.85f, 0.85f, 0.55f));
			}

			// ── Listen arcs toward nearest entity ─────────────────────────────
			if (TargetAngle is { } angle)
			{
				float t      = (tMs % 900f) / 900f;
				float spread = Mathf.DegToRad(35f);
				var color = TargetKind == Compass.TargetKind.Monster
					? new Color(1.00f, 0.58f, 0.08f)   // amber-orange = monster
					: new Color(0.40f, 0.85f, 1.00f);   // ice-blue = rival
				for (int i = 0; i < 3; i++)
				{
					float r     = 36f + i * 26f;
					float phase = (t - i * 0.30f + 1f) % 1f;
					float alpha = Mathf.Sin(phase * Mathf.Pi) * Mathf.Lerp(0.90f, 0.40f, i / 2f);
					if (alpha <= 0f) continue;
					DrawArc(center, r, angle - spread, angle + spread, 20,
						color with { A = alpha }, 3.5f);
				}
			}
			else
			{
				// Listening but nothing detected — faint breathing ring.
				float pulse = 0.07f + 0.05f * Mathf.Sin(tMs / 500f);
				DrawArc(center, 52f, 0f, Mathf.Tau, 48,
					new Color(0.75f, 0.75f, 0.75f, pulse), 2f);
			}
		}
	}
}
