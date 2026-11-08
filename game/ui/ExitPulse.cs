using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>When the exit opens off-screen, a gold glow pulses in from the screen edge on the
/// exit's side, like its light spilling round the corner, for a few beats and then fades away.</summary>
public partial class ExitPulse : CanvasLayer
{
	private MatchClient _client = null!;
	private PulseCanvas _canvas = null!;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 39; // just under the listen compass
		_canvas = new PulseCanvas { PulseRef = this, MouseFilter = Control.MouseFilterEnum.Ignore };
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_canvas);
	}

	public override void _Process(double delta) => _canvas.QueueRedraw();

	private static readonly Color GlowCol = new(1.4f, 1.1f, 0.3f);
	private const float GlowRadius = 140f, FlareSeconds = 4f, PulseSeconds = 1.4f;
	private const float ShowSeconds = 6f, FadeSeconds = 2f; // ~4 beats, the last couple fading out
	private const int GlowRings = 14;

	private void DrawPulse(Control c)
	{
		if (_client == null || !_client.EscapeOpen || _client.EscapeTile is not { } exit) return;
		if (_client.LocalMinerTile() is null) return; // dead or spectating
		int ts = MatchClient.TileSize;
		var world = new Vector2(exit.X * ts + ts / 2f, exit.Y * ts + ts / 2f);
		var screen = c.GetViewport().GetCanvasTransform() * world;
		var size = c.Size;
		if (new Rect2(Vector2.Zero, size).HasPoint(screen)) return; // on screen: the beam does the work

		// Where the line from screen centre to the exit leaves the screen.
		var centre = size / 2f;
		var d = screen - centre;
		float sx = d.X != 0f ? size.X / 2f / Mathf.Abs(d.X) : float.MaxValue;
		float sy = d.Y != 0f ? size.Y / 2f / Mathf.Abs(d.Y) : float.MaxValue;
		var at = centre + d * Mathf.Min(sx, sy);

		// Only around the moment it opens: a few beats, then gone.
		if (_client.EscapeOpenedAtMs == 0) return;
		float sinceOpen = (Time.GetTicksMsec() - _client.EscapeOpenedAtMs) / 1000f;
		if (sinceOpen >= ShowSeconds) return;
		float fade = Mathf.Min(1f, (ShowSeconds - sinceOpen) / FadeSeconds);
		float flare = Mathf.Max(0f, 1f - sinceOpen / FlareSeconds);
		float phase = sinceOpen / PulseSeconds % 1f;
		float beat = Mathf.Exp(-phase * 5f); // swells sharply, then ebbs
		float strength = (0.35f + 0.35f * beat) * (1f + 0.8f * flare) * fade;
		float radius = GlowRadius * (0.85f + 0.25f * beat + 0.4f * flare);

		// A soft radial glow, faked with stacked translucent discs (bright middle, faint rim).
		for (int i = GlowRings; i >= 1; i--)
		{
			float f = (float)i / GlowRings;
			c.DrawCircle(at, radius * f, GlowCol with { A = strength * 0.045f * (1f - f * 0.6f) });
		}
		// A ring rolling inward from the edge on each beat.
		c.DrawArc(at, radius * (0.4f + 0.9f * phase), 0f, Mathf.Tau, 48,
			GlowCol with { A = 0.35f * (1f - phase) * (0.6f + 0.4f * flare) * fade }, 2.5f);
	}

	private partial class PulseCanvas : Control
	{
		public ExitPulse PulseRef = null!;
		public override void _Draw() => PulseRef.DrawPulse(this);
	}
}
