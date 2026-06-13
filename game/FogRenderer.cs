using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness as a soft round lantern light. Unexplored = opaque
/// black, explored-but-not-visible = flat dim, currently visible = a radial falloff
/// that stays fully clear through the play area and feathers into fog only at the
/// rim. Wall shadows are carved for free: occluded tiles are simply absent from the
/// visible set, so they read as full dark and the gradient never bleeds into
/// them.</summary>
public partial class FogRenderer : Node2D
{
	private MatchClient _client = null!;
	private static readonly Color Unexplored = new(0, 0, 0, 1f);
	private static readonly Color Dim = new(0, 0, 0, 0.6f);
	private const float EdgeVeil = 0.35f;   // max darkness alpha at the lit rim
	private const float ClearUntil = 0.7f;  // fraction of radius that stays fully clear

	public void Init(MatchClient client) => _client = client;

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		var fog = _client.Fog;
		int ts = MatchClient.TileSize;
		var (origin, radius) = LocalMinerView();

		foreach (var p in grid.Positions())
		{
			Color color;
			if (fog.IsVisible(p))
			{
				if (radius <= 0) continue; // no falloff info: leave clear
				int ddx = p.X - origin.X, ddy = p.Y - origin.Y;
				float t = Mathf.Sqrt(ddx * ddx + ddy * ddy) / radius; // 0 at miner, 1 at rim
				if (t <= ClearUntil) continue;                         // clear core
				float k = (t - ClearUntil) / (1f - ClearUntil);
				float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k, 0f, 1f)) * EdgeVeil;
				color = new Color(0, 0, 0, alpha);
			}
			else
			{
				color = fog.IsExplored(p) ? Dim : Unexplored;
			}
			DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
		}
	}

	// Local miner's grid position and vision radius, for the radial falloff.
	private (GridPos origin, int radius) LocalMinerView()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
				return (new GridPos(m.X, m.Y), m.VisionRadius);
		return (new GridPos(0, 0), 0);
	}
}
