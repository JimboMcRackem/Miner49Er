using System;
using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Dual-grid terrain renderer. A display TileMapLayer is offset by half a
/// cell; each display cell sits over a world vertex and reads the four cells around it
/// as its corner terrains, looked up against the tileset's corner-bit table. No Godot
/// terrain solver — authored boundaries resolve to exact edge tiles, and rare
/// unauthored junctions fall back to the majority terrain's solid tile.</summary>
public partial class TerrainMap : Node2D
{
	private TileMapLayer _layer = null!;
	private TileMapLayer _waterLayer = null!;
	private MatchClient _client = null!;
	private bool _ready;
	private int _sourceId;

	// Terrain ids — MUST match the converter's TERRAIN_REGISTRY.
	private const int Wall      = 0;
	private const int Floor     = 1;
	private const int Lava      = 2;
	private const int Water     = 3;
	private const int Pit       = 4;
	private const int DeepWater = 5;

	private readonly Dictionary<(int, int, int, int), Vector2I> _lookup = new();
	private readonly Dictionary<int, Vector2I> _solid = new();
	private static readonly int[] FallbackPriority = { Wall, Lava, DeepWater, Water, Pit, Floor };

	public void Init(MatchClient client)
	{
		_client = client;
		var ts = GD.Load<TileSet>("res://assets/tiles/combined_terrain.tres");
		if (ts == null) return;
		_sourceId = ts.GetSourceId(0);
		BuildLookup(ts);

		float half = MatchClient.TileSize / 2f;
		_layer = new TileMapLayer { Name = "TileLayer", TileSet = ts, Position = new Vector2(-half, -half) };
		AddChild(_layer);

		var waterMat = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://assets/tiles/water.gdshader"),
		};
		_waterLayer = new TileMapLayer
		{
			Name     = "WaterAnimLayer",
			TileSet  = ts,
			Position = new Vector2(-half, -half),
			ZIndex   = 1,
			Material = waterMat,
		};
		AddChild(_waterLayer);

		_ready = true;
		PaintFullGrid();
	}

	// Corner-signature -> atlas coord, plus one solid tile per terrain for fallback.
	private void BuildLookup(TileSet ts)
	{
		if (ts.GetSource(_sourceId) is not TileSetAtlasSource src) return;
		for (int i = 0; i < src.GetTilesCount(); i++)
		{
			var coord = src.GetTileId(i);
			var td = src.GetTileData(coord, 0);
			int tl = td.GetTerrainPeeringBit(TileSet.CellNeighbor.TopLeftCorner);
			int tr = td.GetTerrainPeeringBit(TileSet.CellNeighbor.TopRightCorner);
			int bl = td.GetTerrainPeeringBit(TileSet.CellNeighbor.BottomLeftCorner);
			int br = td.GetTerrainPeeringBit(TileSet.CellNeighbor.BottomRightCorner);
			if (tl < 0) continue;
			_lookup.TryAdd((tl, tr, bl, br), coord);
			if (tl == tr && tr == bl && bl == br)
				_solid.TryAdd(tl, coord);
		}
	}

	private void PaintFullGrid()
	{
		var grid = _client.Grid;
		for (int j = 0; j <= grid.Height; j++)
			for (int i = 0; i <= grid.Width; i++)
				PaintDisplayCell(i, j);
	}

	// Each changed world cell touches the four display cells around its vertices.
	public void UpdateTiles(IReadOnlyList<TileChange> changes)
	{
		if (!_ready) return;
		foreach (var t in changes)
		{
			PaintDisplayCell(t.X, t.Y);
			PaintDisplayCell(t.X + 1, t.Y);
			PaintDisplayCell(t.X, t.Y + 1);
			PaintDisplayCell(t.X + 1, t.Y + 1);
		}
	}

	private void PaintDisplayCell(int i, int j)
	{
		int tl = TerrainAt(i - 1, j - 1);
		int tr = TerrainAt(i,     j - 1);
		int bl = TerrainAt(i - 1, j);
		int br = TerrainAt(i,     j);
		var cell = new Vector2I(i, j);
		_layer.SetCell(cell, _sourceId, Resolve(tl, tr, bl, br));
		if (tl == tr && tr == bl && bl == br
			&& (tl == Water || tl == DeepWater)
			&& _solid.TryGetValue(tl, out var wc))
			_waterLayer.SetCell(cell, _sourceId, wc);
		else
			_waterLayer.EraseCell(cell);
	}

	private Vector2I Resolve(int tl, int tr, int bl, int br)
	{
		if (_lookup.TryGetValue((tl, tr, bl, br), out var c)) return c;
		int m = Majority(tl, tr, bl, br);
		if (_solid.TryGetValue(m, out var s)) return s;
		return _solid.TryGetValue(Wall, out var w) ? w : new Vector2I(0, 0);
	}

	private static int Majority(int a, int b, int c, int d)
	{
		Span<int> v = stackalloc int[] { a, b, c, d };
		int best = a, bestN = 0;
		foreach (int cand in v)
		{
			int n = 0;
			foreach (int x in v) if (x == cand) n++;
			if (n > bestN || (n == bestN && Pri(cand) < Pri(best))) { best = cand; bestN = n; }
		}
		return best;
	}

	private static int Pri(int terrain)
	{
		for (int k = 0; k < FallbackPriority.Length; k++)
			if (FallbackPriority[k] == terrain) return k;
		return int.MaxValue;
	}

	private int TerrainAt(int x, int y)
	{
		var p = new GridPos(x, y);
		return _client.Grid.InBounds(p) ? TileToTerrain(_client.Grid.Get(p)) : Wall;
	}

	private static int TileToTerrain(TileType t) => t switch
	{
		TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock => Wall,
		TileType.Floor or TileType.Cracked or TileType.Crumbling or TileType.Plank => Floor,
		TileType.Lava => Lava,
		TileType.ShallowWater => Water,
		TileType.DeepWater    => DeepWater,
		TileType.Pit => Pit,
		_ => Wall, // LavaVent — wall underneath; WorldRenderer overlays the vent glow
	};
}
