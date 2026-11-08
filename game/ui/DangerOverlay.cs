using Godot;

namespace Miner49er;

/// <summary>When the local miner is in danger (see <see cref="MatchClient.Danger"/>) a heartbeat
/// thumps and the screen edges glow red in time with it, faster and stronger the worse it gets.</summary>
public partial class DangerOverlay : CanvasLayer
{
	private MatchClient _client = null!;
	private EdgeCanvas _canvas = null!;
	private AudioStreamPlayer _beatPlayer = null!;
	private float _danger;    // smoothed, so the effect swells in and fades out
	private float _beatClock; // seconds until the next beat
	private float _sinceBeat = 10f;

	private static readonly Color EdgeCol = new(0.7f, 0.0f, 0.0f);
	private const float EdgePx = 110f, DubDelay = 0.17f;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 38;
		_canvas = new EdgeCanvas { OverlayRef = this, MouseFilter = Control.MouseFilterEnum.Ignore };
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_canvas);
		_beatPlayer = new AudioStreamPlayer { Stream = SfxLibrary.Heartbeat, Bus = AudioManager.BusSfx };
		AddChild(_beatPlayer);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		float target = _client?.Danger() ?? 0f;
		_danger = Mathf.MoveToward(_danger, target, dt * (target > _danger ? 2.5f : 0.8f));
		_sinceBeat += dt;
		_beatClock -= dt;
		if (_danger > 0.05f && _beatClock <= 0f)
		{
			_beatClock = Mathf.Lerp(1.0f, 0.42f, _danger);
			_sinceBeat = 0f;
			_beatPlayer.VolumeDb = Mathf.Lerp(-16f, -1f, _danger);
			_beatPlayer.Play();
		}
		_canvas.QueueRedraw();
	}

	// Two swells per beat, matching the lub-dub.
	private float Pulse() =>
		Mathf.Exp(-_sinceBeat * 9f) + 0.7f * (_sinceBeat >= DubDelay ? Mathf.Exp(-(_sinceBeat - DubDelay) * 9f) : 0f);

	private void DrawEdges(Control c)
	{
		if (_danger <= 0.01f) return;
		float a = _danger * (0.04f + 0.22f * Mathf.Min(1f, Pulse()));
		float w = EdgePx * (0.6f + 0.4f * _danger);
		var s = c.Size;
		var solid = EdgeCol with { A = a };
		var clear = EdgeCol with { A = 0f };
		var cols = new[] { solid, solid, clear, clear };
		// Each strip runs from the screen edge (solid) inward (clear); the corners overlap and so read darker.
		c.DrawPolygon(new[] { new Vector2(0, 0), new Vector2(s.X, 0), new Vector2(s.X - w, w), new Vector2(w, w) }, cols);
		c.DrawPolygon(new[] { new Vector2(s.X, s.Y), new Vector2(0, s.Y), new Vector2(w, s.Y - w), new Vector2(s.X - w, s.Y - w) }, cols);
		c.DrawPolygon(new[] { new Vector2(0, s.Y), new Vector2(0, 0), new Vector2(w, w), new Vector2(w, s.Y - w) }, cols);
		c.DrawPolygon(new[] { new Vector2(s.X, 0), new Vector2(s.X, s.Y), new Vector2(s.X - w, s.Y - w), new Vector2(s.X - w, w) }, cols);
	}

	private partial class EdgeCanvas : Control
	{
		public DangerOverlay OverlayRef = null!;
		public override void _Draw() => OverlayRef.DrawEdges(this);
	}
}
