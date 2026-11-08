using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Once the exit is open and off-screen, an arrow at the screen edge points the way to it.
/// It bobs toward the exit and pulses harder for a few seconds after the exit first opens.</summary>
public partial class ExitArrow : CanvasLayer
{
	private MatchClient _client = null!;
	private ArrowCanvas _canvas = null!;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		Layer = 39; // just under the listen compass
		_canvas = new ArrowCanvas { ArrowRef = this, MouseFilter = Control.MouseFilterEnum.Ignore };
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_canvas);
	}

	public override void _Process(double delta) => _canvas.QueueRedraw();

	private static readonly Color ArrowCol = new(1.5f, 1.3f, 0.45f);
	private static readonly Color ArrowEdgeCol = new(0.25f, 0.18f, 0.02f, 0.85f);
	private const float EdgeMargin = 64f, FlareSeconds = 4f;

	private void DrawArrow(Control c)
	{
		if (_client == null || !_client.EscapeOpen || _client.EscapeTile is not { } exit) return;
		if (_client.LocalMinerTile() is null) return; // dead or spectating
		int ts = MatchClient.TileSize;
		var world = new Vector2(exit.X * ts + ts / 2f, exit.Y * ts + ts / 2f);
		var screen = c.GetViewport().GetCanvasTransform() * world;
		var size = c.Size;
		var rect = new Rect2(Vector2.Zero, size).Grow(-EdgeMargin);
		if (rect.HasPoint(screen)) return; // on screen: the beam does the work

		// Clamp the line from screen centre to the exit onto the margin rectangle.
		var centre = size / 2f;
		var d = screen - centre;
		float sx = d.X != 0f ? (size.X / 2f - EdgeMargin) / Mathf.Abs(d.X) : float.MaxValue;
		float sy = d.Y != 0f ? (size.Y / 2f - EdgeMargin) / Mathf.Abs(d.Y) : float.MaxValue;
		var dir = d.Normalized();
		float now = Time.GetTicksMsec() / 1000f;
		float sinceOpen = (Time.GetTicksMsec() - _client.EscapeOpenedAtMs) / 1000f;
		float flare = _client.EscapeOpenedAtMs > 0 ? Mathf.Max(0f, 1f - sinceOpen / FlareSeconds) : 0f;
		var tip = centre + d * Mathf.Min(sx, sy) + dir * (4f * Mathf.Sin(now * 6f));
		float len = 30f + 10f * flare, half = 18f + 5f * flare;
		var side = dir.Orthogonal();
		var pts = new[] { tip, tip - dir * len + side * half, tip - dir * len * 0.7f, tip - dir * len - side * half };
		float a = 0.75f + 0.25f * Mathf.Sin(now * (4f + 8f * flare));
		c.DrawColoredPolygon(pts, ArrowCol with { A = a });
		c.DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, ArrowEdgeCol, 2f);
	}

	private partial class ArrowCanvas : Control
	{
		public ExitArrow ArrowRef = null!;
		public override void _Draw() => ArrowRef.DrawArrow(this);
	}
}
