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

	public TileGrid Grid { get; private set; } = null!;
	public FogState Fog { get; } = new();
	public IReadOnlyList<MinerSnapshot> Miners => _miners;
	public IReadOnlyList<ChargeSnapshot> Charges => _charges;
	public IReadOnlyList<ItemSnapshot> Items => _items;
	public IReadOnlyList<MoldSnapshot> Molds => _molds;
	public int LocalMinerId { get; private set; }
	public bool Listening; // set by Main each frame; gates the buried-item shimmer
	public IReadOnlyList<GridPos> Decoys { get; private set; } = System.Array.Empty<GridPos>();
	public float SecondsRemaining { get; private set; } = -1f;
	public event System.Action<Vector2>? Exploded; // world position of a detonation

	private List<MinerSnapshot> _miners = new();
	private List<ChargeSnapshot> _charges = new();
	private List<ItemSnapshot> _items = new();
	private List<MoldSnapshot> _molds = new();
	private readonly Dictionary<int, Vector2> _visualPos = new(); // minerId -> smoothed pixels

	private WorldRenderer _world = null!;
	private FogRenderer _fogRenderer = null!;
	private Node2D _camera = null!;
	private Texture2D[,]? _minerTex; // [colorIndex 0-7, facing 0=N 1=E 2=S 3=W]

	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot)
	{
		Grid = grid;
		LocalMinerId = localMinerId;
		Decoys = decoys;

		_world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -10 };
		sceneRoot.AddChild(_world);
		_world.Init(this);

		_fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
		sceneRoot.AddChild(_fogRenderer);
		_fogRenderer.Init(this);

		_minerTex = BuildMinerTextures();

		_camera = new Node2D { Name = "CameraRig" };
		sceneRoot.AddChild(_camera);
		var cam = new Camera2D { Zoom = new Vector2(1.5f, 1.5f) };
		_camera.AddChild(cam);
		cam.MakeCurrent();
	}

	public void ApplyUpdate(TickUpdate update)
	{
		float bx = 0f, by = 0f;
		int blastCount = 0;
		foreach (var t in update.TileChanges)
		{
			var p = new GridPos(t.X, t.Y);
			if (Grid.InBounds(p)) Grid.Set(p, t.NewType);
			if (t.FromBlast)
			{
				_world?.AddExplosionFlash(p);
				bx += t.X; by += t.Y; blastCount++;
			}
		}
		if (blastCount > 0)
		{
			var c = new Vector2(bx / blastCount * TileSize + TileSize / 2f,
								 by / blastCount * TileSize + TileSize / 2f);
			Exploded?.Invoke(c);
		}

		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		_items = new List<ItemSnapshot>(update.Snapshot.Items);
		_molds = new List<MoldSnapshot>(update.Snapshot.Molds);
		SecondsRemaining = update.Snapshot.SecondsRemaining;
		UpdateFog();
	}

	public override void _PhysicsProcess(double delta)
	{
		// Smooth each miner visual toward its authoritative tile position.
		foreach (var m in _miners)
		{
			var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
			var cur = _visualPos.TryGetValue(m.Id, out var v) ? v : target;
			float pixelsPerSec = TileSize / (float)m.MoveSeconds;
			_visualPos[m.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);

			if (m.Id == LocalMinerId)
				_camera.Position = _visualPos[m.Id];
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		foreach (var m in _miners)
		{
			if (!m.Alive) continue;
			var p = _visualPos.TryGetValue(m.Id, out var v) ? v : Vector2.Zero;
			int colorIdx = (m.Id - 1) % PlayerColors.Palette.Length;
			int facing = m.Facing; // 0=N 1=E 2=S 3=W
			var tex = _minerTex?[colorIdx, facing];
			if (tex != null)
				DrawTextureRect(tex, new Rect2(p.X - 16, p.Y - 16, 32, 32), false);
			else
				DrawRect(new Rect2(p.X - 10, p.Y - 10, 20, 20), PlayerColors.At(m.Id - 1));
		}
	}

	private static Texture2D[,] BuildMinerTextures()
	{
		var paths = new[]
		{
			"res://assets/miners/miner_n.png",
			"res://assets/miners/miner_e.png",
			"res://assets/miners/miner_s.png",
			"res://assets/miners/miner_w.png",
		};
		var srcs = new Image?[4];
		for (int d = 0; d < 4; d++)
		{
			var img = new Image();
			if (img.Load(paths[d]) == Error.Ok) { img.Convert(Image.Format.Rgba8); srcs[d] = img; }
		}
		var tex = new Texture2D[PlayerColors.Palette.Length, 4];
		for (int c = 0; c < PlayerColors.Palette.Length; c++)
			for (int d = 0; d < 4; d++)
				if (srcs[d] != null)
					tex[c, d] = ImageTexture.CreateFromImage(TintMiner(srcs[d]!, PlayerColors.At(c)));
		return tex;
	}

	private static Image TintMiner(Image src, Color tint)
	{
		var img = (Image)src.Duplicate();
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var px = img.GetPixel(x, y);
				float lum = 0.299f * px.R + 0.587f * px.G + 0.114f * px.B;
				img.SetPixel(x, y, new Color(tint.R * lum, tint.G * lum, tint.B * lum, px.A));
			}
		return img;
	}

	private void UpdateFog()
	{
		foreach (var m in _miners)
			if (m.Id == LocalMinerId && m.Alive)
				Fog.Update(Visibility.Compute(Grid, new GridPos(m.X, m.Y), m.VisionRadius));
	}
}
