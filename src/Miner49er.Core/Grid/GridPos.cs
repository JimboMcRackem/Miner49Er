namespace Miner49er.Core;

public readonly record struct GridPos(int X, int Y)
{
    public GridPos Add(GridPos o) => new(X + o.X, Y + o.Y);
    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y);
    public int ManhattanTo(GridPos o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y);
    public int ChebyshevTo(GridPos o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));
}
