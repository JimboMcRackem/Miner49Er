using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Render-side world replica. Holds the grid (regenerated from seed),
/// applies tile changes + entity snapshots, smooths miner visuals toward their
/// authoritative positions, and computes local fog from the local miner.</summary>
public partial class MatchClient : Node2D
{
	public const int TileSize = 32;
	public const int VisionRadius = 5;
	public static readonly float MoveSpeedPixels = TileSize / (float)MatchHost.MoveStepSeconds;

	public TileGrid Grid { get; private set; } = null!;
	public FogState Fog { get; } = new();
	public IReadOnlyList<MinerSnapshot> Miners => _miners;
	public IReadOnlyList<ChargeSnapshot> Charges => _charges;
	public int LocalMinerId { get; private set; }

	private List<MinerSnapshot> _miners = new();
	private List<ChargeSnapshot> _charges = new();
	private readonly Dictionary<int, Vector2> _visualPos = new(); // minerId -> smoothed pixels

	private WorldRenderer _world = null!;
	private FogRenderer _fogRenderer = null!;
	private Node2D _camera = null!;

	public void Begin(TileGrid grid, int localMinerId, Node2D sceneRoot)
	{
		Grid = grid;
		LocalMinerId = localMinerId;

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -10 };
		sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);

		_camera = new Node2D { Name = "CameraRig" };
		sceneRoot.AddChild(_camera);
		var cam = new Camera2D { Zoom = new Vector2(1.5f, 1.5f) };
		_camera.AddChild(cam);
		cam.MakeCurrent();
	}

	public void ApplyUpdate(TickUpdate update)
	{
		foreach (var t in update.TileChanges)
		{
			var p = new GridPos(t.X, t.Y);
			if (Grid.InBounds(p)) Grid.Set(p, TileType.Floor);
			if (t.FromBlast) _world?.AddExplosionFlash(p);
		}

		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		UpdateFog();
	}

	public Vector2 VisualPosOf(MinerSnapshot m)
	{
		var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
		if (!_visualPos.TryGetValue(m.Id, out var cur)) { _visualPos[m.Id] = target; return target; }
		return cur;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Smooth each miner visual toward its authoritative tile position.
		foreach (var m in _miners)
		{
			var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
			var cur = _visualPos.TryGetValue(m.Id, out var v) ? v : target;
			_visualPos[m.Id] = cur.MoveToward(target, MoveSpeedPixels * (float)delta);

			if (m.Id == LocalMinerId)
				_camera.Position = _visualPos[m.Id];
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		// Draw miners as colored squares (color via NetworkManager lobby info).
		foreach (var m in _miners)
		{
			if (!m.Alive) continue;
			var p = _visualPos.TryGetValue(m.Id, out var v) ? v : Vector2.Zero;
			var color = MinerColor(m.Id);
			DrawRect(new Rect2(p.X - 10, p.Y - 10, 20, 20), color);
		}
	}

	private static Color MinerColor(int minerId)
	{
		// minerId is 1-based spawn index; map to palette by index-1.
		return PlayerColors.At(minerId - 1);
	}

	private void UpdateFog()
	{
		foreach (var m in _miners)
			if (m.Id == LocalMinerId && m.Alive)
				Fog.Update(Visibility.Compute(Grid, new GridPos(m.X, m.Y), VisionRadius));
	}
}
