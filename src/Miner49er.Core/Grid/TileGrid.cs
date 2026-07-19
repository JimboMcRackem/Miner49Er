namespace Miner49er.Core;

public sealed class TileGrid
{
    public int Width { get; }
    public int Height { get; }
    private readonly TileType[] _tiles;

    /// <summary>Monotonic counter bumped whenever a tile actually changes value. Lets
    /// presentation caches (e.g. the client's static terrain-light fields) detect that the
    /// map mutated without hooking every mutation path — compare against a stored snapshot.</summary>
    public int Version { get; private set; }

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
        int i = p.Y * Width + p.X;
        if (_tiles[i] == t) return;   // no-op write leaves Version (and the grid) unchanged
        _tiles[i] = t;
        Version++;
    }

    public bool IsWalkable(GridPos p) => InBounds(p) && Get(p).IsWalkable();

    public IEnumerable<GridPos> Positions()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new GridPos(x, y);
    }
}
