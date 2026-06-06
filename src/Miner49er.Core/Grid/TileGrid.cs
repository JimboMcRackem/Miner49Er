namespace Miner49er.Core;

public sealed class TileGrid
{
    public int Width { get; }
    public int Height { get; }
    private readonly TileType[] _tiles;

    public TileGrid(int width, int height, TileType fill = TileType.Rock)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");
        Width = width;
        Height = height;
        _tiles = new TileType[width * height];
        Array.Fill(_tiles, fill);
    }

    public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public TileType Get(GridPos p)
    {
        if (!InBounds(p)) throw new ArgumentOutOfRangeException(nameof(p));
        return _tiles[p.Y * Width + p.X];
    }

    public void Set(GridPos p, TileType t)
    {
        if (!InBounds(p)) throw new ArgumentOutOfRangeException(nameof(p));
        _tiles[p.Y * Width + p.X] = t;
    }

    public bool IsWalkable(GridPos p) => InBounds(p) && Get(p).IsWalkable();

    public IEnumerable<GridPos> Positions()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new GridPos(x, y);
    }
}
