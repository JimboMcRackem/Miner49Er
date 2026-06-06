namespace Miner49er.Core;

public enum Direction { North, East, South, West }

public static class DirectionExtensions
{
    public static GridPos ToOffset(this Direction d) => d switch
    {
        Direction.North => new GridPos(0, -1),
        Direction.East  => new GridPos(1, 0),
        Direction.South => new GridPos(0, 1),
        Direction.West  => new GridPos(-1, 0),
        _ => new GridPos(0, 0),
    };
}
