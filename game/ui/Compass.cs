using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Listen-mode overlay. While listening:
///  • Pulsing arcs toward nearest monster (amber) or rival (blue); faint ring if nothing near.
///  • Compass rose with a green needle pointing toward the escape tile (when one exists).</summary>
public partial class Compass : CanvasLayer
{
	public bool Active;
	public float ListenTime;
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
		_arc.TargetAngle  = listenAngle;
		_arc.TargetKind   = kind;
		// In Treasure Heist the exit rose points at the treasure (see ComputeExitAngle),
		// but only while listening — the compass is a Listen tool in every mode.
		_arc.ExitAngle    = ComputeExitAngle();
		_arc.ExitSettle   = Mathf.Clamp(ListenTime, 0f, 1.0f); // 0→1 over first second
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
		GridPos? target;
		if (NetworkManager.Instance.MatchMode == GameMode.TreasureHeist && _client.Treasure is { } t)
		{
			// Treasure Heist: point at the treasure in all states (including buried) so players
			// can navigate to dig it out, then track it once it's loose or carried.
			target = new GridPos(t.X, t.Y);
		}
		else
		{
			// On treasure floors the compass points to the hidden idol until it's found,
			// then pivots to the exit once EscapeOpen (ExpeditionTreasurePos cleared on that transition).
			target = _client.ExpeditionTreasurePos
				// In ReachCenter the goal is the centre tile, not an escape ladder.
				?? (NetworkManager.Instance.MatchMode == GameMode.ReachCenter
					? _client.CenterTile
					: _client.EscapeTile);
		}
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
		public float ExitSettle; // 0=just started stage 2, 1=fully settled

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

			// ── Exit compass rose (stage 2: appears after 1s, needle spins in) ──
			if (ExitAngle is { } exitA)
			{
				// Ease: slow start, fast deceleration into final angle
				float ease = 1f - Mathf.Pow(1f - ExitSettle, 3f);
				// Spin offset: starts 2 full rotations ahead, decelerates to zero
				float spinOffset = Mathf.Tau * 2f * (1f - ease);
				float displayA   = exitA + spinOffset;

				// Fade the whole rose in during the first half of settle
				float roseAlpha = Mathf.Clamp(ExitSettle * 2f, 0f, 1f);

				// Bezel ring
				DrawArc(center, BezelR, 0f, Mathf.Tau, 64,
					new Color(0.85f, 0.85f, 0.85f, 0.30f * roseAlpha), 1.5f);

				// Cardinal tick marks
				for (int i = 0; i < 8; i++)
				{
					float a = i * Mathf.Pi / 4f;
					float inner = i % 2 == 0 ? BezelR - 9f : BezelR - 5f;
					var from = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * inner;
					var to   = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (BezelR + 2f);
					DrawLine(from, to, new Color(0.85f, 0.85f, 0.85f, 0.35f * roseAlpha), 1.5f);
				}

				// Needle tip and tail (spinning)
				var dir  = new Vector2(Mathf.Cos(displayA), Mathf.Sin(displayA));
				var tip  = center + dir * NeedleR;
				var tail = center - dir * TailR;

				DrawLine(center, tail, new Color(0.6f, 0.6f, 0.6f, 0.40f * roseAlpha), 2f);

				var needleCol = new Color(0.20f, 0.92f, 0.42f, 0.88f * roseAlpha);
				DrawLine(center, tip, needleCol, 3f);

				float ha = Mathf.DegToRad(HeadHalf);
				DrawLine(tip, tip - dir.Rotated( ha) * HeadLen, needleCol, 3f);
				DrawLine(tip, tip - dir.Rotated(-ha) * HeadLen, needleCol, 3f);

				DrawCircle(center, 4f, new Color(0.85f, 0.85f, 0.85f, 0.55f * roseAlpha));
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
