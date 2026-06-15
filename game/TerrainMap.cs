using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Paints the tile grid with Wang autotile terrain via TileMapLayer.
/// Terrain indices match converter output (the two "cave wall" names are merged):
///   0 = river/lava/water  1 = cave wall  2 = cave floor  3 = bottomless pit.
/// Any cell the autotiler can't resolve is solid-filled so terrain is never blank.</summary>
public partial class TerrainMap : Node2D
{
	private TileMapLayer _layer = null!;
	private MatchClient _client = null!;
	private bool _ready;
	private int _sourceId;
	private readonly System.Collections.Generic.Dictionary<int, Vector2I> _solid = new(); // terrain -> solid-fill atlas coord

	private const int TerrainSet   = 0;
	private const int TerrainLava  = 0; // river, lava pit, lava flow, water
	private const int TerrainWall  = 1; // cave wall (rock, gold rock, bedrock)
	private const int TerrainFloor = 2; // cave floor, cracked, crumbling, plank
	private const int TerrainPit   = 3; // dark bottomless pit
	private const int TerrainCount = 4;

	public void Init(MatchClient client)
	{
		_client = client;
		var ts = GD.Load<TileSet>("res://assets/tiles/combined_terrain.tres");
		if (ts == null) return;

		_layer = new TileMapLayer { Name = "TileLayer" };
		_layer.TileSet = ts;
		AddChild(_layer);
		BuildSolidFills(ts);
		_ready = true;
		PaintFullGrid();
	}

	// Record one "all corners == T" tile per terrain, used to backfill cells the
	// autotiler leaves empty (unsupported corner combos, e.g. water on floor).
	private void BuildSolidFills(TileSet ts)
	{
		_sourceId = ts.GetSourceId(0);
		if (ts.GetSource(_sourceId) is not TileSetAtlasSource src) return;
		for (int i = 0; i < src.GetTilesCount(); i++)
		{
			var coord = src.GetTileId(i);
			var td = src.GetTileData(coord, 0);
			int tl = td.GetTerrainPeeringBit(TileSet.CellNeighbor.TopLeftCorner);
			int tr = td.GetTerrainPeeringBit(TileSet.CellNeighbor.TopRightCorner);
			int bl = td.GetTerrainPeeringBit(TileSet.CellNeighbor.BottomLeftCorner);
			int br = td.GetTerrainPeeringBit(TileSet.CellNeighbor.BottomRightCorner);
			if (tl >= 0 && tl == tr && tr == bl && bl == br && !_solid.ContainsKey(tl))
				_solid[tl] = coord;
		}
	}

	private void FillBlank(Vector2I cell, int terrain)
	{
		if (terrain >= 0 && _layer.GetCellSourceId(cell) == -1 && _solid.TryGetValue(terrain, out var atlas))
			_layer.SetCell(cell, _sourceId, atlas);
	}

	public void UpdateTiles(IReadOnlyList<TileChange> changes)
	{
		if (!_ready) return;
		var groups = new Array<Vector2I>[TerrainCount];
		for (int i = 0; i < TerrainCount; i++) groups[i] = new Array<Vector2I>();
		var toErase = new List<Vector2I>();

		foreach (var t in changes)
		{
			var cell = new Vector2I(t.X, t.Y);
			int terrain = TileToTerrain(t.NewType);
			if (terrain < 0) toErase.Add(cell);
			else groups[terrain].Add(cell);
		}

		// Wall first so floor/lava/pit corner-resolution sees the walls.
		for (int i = 0; i < TerrainCount; i++)
			if (groups[i].Count > 0)
				_layer.SetCellsTerrainConnect(groups[i], TerrainSet, i);
		foreach (var cell in toErase)
			_layer.EraseCell(cell);

		// SetCellsTerrainConnect can blank a changed cell or its neighbours when no
		// tile matches; backfill those (and the edited cells) with solid tiles.
		foreach (var t in changes)
			for (int dy = -1; dy <= 1; dy++)
				for (int dx = -1; dx <= 1; dx++)
				{
					var gp = new GridPos(t.X + dx, t.Y + dy);
					if (_client.Grid.InBounds(gp))
						FillBlank(new Vector2I(gp.X, gp.Y), TileToTerrain(_client.Grid.Get(gp)));
				}
	}

	private void PaintFullGrid()
	{
		var grid = _client.Grid;
		var groups = new Array<Vector2I>[TerrainCount];
		for (int i = 0; i < TerrainCount; i++) groups[i] = new Array<Vector2I>();

		foreach (var p in grid.Positions())
		{
			int terrain = TileToTerrain(grid.Get(p));
			if (terrain >= 0)
				groups[terrain].Add(new Vector2I(p.X, p.Y));
		}

		// Wall first so floor/pit/lava corners resolve correctly against walls;
		// floor before pit because pit transitions are authored against floor.
		_layer.SetCellsTerrainConnect(groups[TerrainWall],  TerrainSet, TerrainWall);
		_layer.SetCellsTerrainConnect(groups[TerrainFloor], TerrainSet, TerrainFloor);
		_layer.SetCellsTerrainConnect(groups[TerrainLava],  TerrainSet, TerrainLava);
		_layer.SetCellsTerrainConnect(groups[TerrainPit],   TerrainSet, TerrainPit);

		foreach (var p in grid.Positions())
			FillBlank(new Vector2I(p.X, p.Y), TileToTerrain(grid.Get(p)));
	}

	private static int TileToTerrain(TileType t) => t switch
	{
		TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock => TerrainWall,
		TileType.Floor or TileType.Cracked
			or TileType.Crumbling or TileType.Plank                   => TerrainFloor,
		TileType.Lava or TileType.ShallowWater or TileType.DeepWater   => TerrainLava,
		TileType.Pit                                                   => TerrainPit,
		_ => -1, // LavaVent — not managed here; WorldRenderer draws it
	};
}
