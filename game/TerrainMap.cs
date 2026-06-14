using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Paints the tile grid with Wang autotile terrain via TileMapLayer.
/// Terrain indices match converter output (lava_wall first, then floor_wall):
///   0 = river/lava/water  1 = cave wall  2 = cave floor  3 = cave wall w/ crack</summary>
public partial class TerrainMap : Node2D
{
	private TileMapLayer _layer = null!;
	private MatchClient _client = null!;
	private bool _ready;

	private const int TerrainSet     = 0;
	private const int TerrainLava    = 0; // river, lava pit, lava flow
	private const int TerrainRock    = 1; // cave wall
	private const int TerrainFloor   = 2; // cave floor, crazed cave floor
	private const int TerrainImpRock = 3; // cave wall, cave wall with crack
	private const int TerrainPit     = 4; // dark bottomless pit, black void abyss
	private const int TerrainCount   = 5;

	public void Init(MatchClient client)
	{
		_client = client;
		var ts = GD.Load<TileSet>("res://assets/tiles/combined_terrain.tres");
		if (ts == null) return;

		_layer = new TileMapLayer { Name = "TileLayer" };
		_layer.TileSet = ts;
		AddChild(_layer);
		_ready = true;
		PaintFullGrid();
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

		// Paint rock terrains first so floor/pit corner-resolution sees the walls
		for (int i = 0; i < TerrainCount; i++)
			if (groups[i].Count > 0)
				_layer.SetCellsTerrainConnect(groups[i], TerrainSet, i);
		foreach (var cell in toErase)
			_layer.EraseCell(cell);
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

		// Rock first so floor/pit/lava corners resolve correctly against walls
		_layer.SetCellsTerrainConnect(groups[TerrainRock],    TerrainSet, TerrainRock);
		_layer.SetCellsTerrainConnect(groups[TerrainImpRock], TerrainSet, TerrainImpRock);
		_layer.SetCellsTerrainConnect(groups[TerrainFloor],   TerrainSet, TerrainFloor);
		_layer.SetCellsTerrainConnect(groups[TerrainLava],    TerrainSet, TerrainLava);
		_layer.SetCellsTerrainConnect(groups[TerrainPit],     TerrainSet, TerrainPit);
	}

	private static int TileToTerrain(TileType t) => t switch
	{
		TileType.Rock or TileType.GoldRock                           => TerrainRock,
		TileType.ImpermeableRock                                     => TerrainImpRock,
		TileType.Floor or TileType.Cracked
			or TileType.Crumbling or TileType.Plank                 => TerrainFloor,
		TileType.Lava or TileType.ShallowWater or TileType.DeepWater => TerrainLava,
		TileType.Pit                                                 => TerrainPit,
		_ => -1, // LavaVent — not managed here; WorldRenderer draws it
	};
}
