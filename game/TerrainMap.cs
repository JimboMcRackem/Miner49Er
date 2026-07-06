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
	private TileMapLayer _deepLayer  = null!;
	private MatchClient _client = null!;
	private bool _ready;
	private int _sourceId;

	// Expedition dungeon: tint wall/floor layer to reflect depth.
	// Bands: 1-5 normal, 6-10 slate-blue, 11-15 purple granite, 16+ volcanic red.
	public void SetFloor(int floor)
	{
		if (_layer == null) return;
		_layer.Modulate = floor switch
		{
			<= 5  => new Color(1.00f, 1.00f, 1.00f),
			<= 10 => new Color(0.80f, 0.86f, 1.10f),
			<= 15 => new Color(0.88f, 0.76f, 1.12f),
			_     => new Color(1.12f, 0.74f, 0.68f),
		};
	}

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

		var waterShader = GD.Load<Shader>("res://assets/tiles/water.gdshader");

		// Shallow water: blue-tinted Wang tiles for every water display cell.
		// Shader outputs white × tile-alpha so Modulate supplies the colour while
		// the Wang tile's alpha mask preserves the rounded edge shape.
		_waterLayer = new TileMapLayer
		{
			Name     = "WaterShallowLayer",
			TileSet  = ts,
			Position = new Vector2(-half, -half),
			ZIndex   = 1,
			Material = new ShaderMaterial { Shader = waterShader },
			Modulate = new Color(0.06f, 0.17f, 0.46f),
		};
		AddChild(_waterLayer);

		// Deep water: darker blue painted over interior deep-water cells only.
		// Same ZIndex as shallow so WorldRenderer sparkles (ZIndex -9, added after
		// TerrainMap) composite on top of both layers.
		_deepLayer = new TileMapLayer
		{
			Name     = "DeepWaterLayer",
			TileSet  = ts,
			Position = new Vector2(-half, -half),
			ZIndex   = 1,
			Material = new ShaderMaterial { Shader = waterShader },
			Modulate = new Color(0.02f, 0.07f, 0.24f),
		};
		AddChild(_deepLayer);

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
		var resolved = Resolve(tl, tr, bl, br);
		_layer.SetCell(cell, _sourceId, resolved);

		// Shallow water layer: paint the SAME resolved Wang tile as _layer whenever
		// any corner is water. The shader replaces the stone texture with pure white;
		// Modulate on the layer tints it blue, so rounded edge shapes are preserved.
		if (tl == Water || tr == Water || bl == Water || br == Water)
			_waterLayer.SetCell(cell, _sourceId, resolved);
		else
			_waterLayer.EraseCell(cell);

		// Deep-water overlay: paint a dark tinted tile over cells whose four world corners are all DeepWater.
		bool deep = IsDeepWaterAt(i - 1, j - 1) && IsDeepWaterAt(i, j - 1)
		         && IsDeepWaterAt(i - 1, j)     && IsDeepWaterAt(i, j);
		if (deep && _solid.TryGetValue(Water, out var dc))
			_deepLayer.SetCell(cell, _sourceId, dc);
		else
			_deepLayer.EraseCell(cell);
	}

	private bool IsDeepWaterAt(int x, int y)
	{
		var p = new GridPos(x, y);
		return _client.Grid.InBounds(p) && _client.Grid.Get(p) == TileType.DeepWater;
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
		TileType.DeepWater    => Water, // same terrain as shallow — avoids missing edge tiles at the border
		TileType.Pit => Pit,
		_ => Wall, // LavaVent — wall underneath; WorldRenderer overlays the vent glow
	};
}
