namespace Miner49er.Core;

/// <summary>One sealed temple room. <see cref="Floor"/> lists all of its interior, props included.</summary>
public sealed record TempleSection(
    int Id,
    IReadOnlyList<GridPos> Floor,
    IReadOnlyList<GridPos> Panels,
    IReadOnlyList<GridPos> Jars,
    GridPos Doorway);

/// <summary>Stamps temple sections over the raw cave noise, before regions are joined, so caves
/// form around them. A skin of plain rock keeps a section's marble out of sight of the caves.</summary>
public static class TempleLayout
{
    private const int MinInterior = 7, MaxInteriorWidth = 13, MaxInteriorHeight = 11;
    private const int Ring = 1, Skin = 1, Margin = Ring + Skin;
    private const int Attempts = 200;
    private const double MaxMapShare = 0.30;   // the rest stays cave, or there's nowhere to spawn
    private const double PanelChance = 0.25, FallenChance = 0.35;
    private const int ColumnSpacing = 3, SconceSpacing = 4;

    private readonly record struct Room(int X, int Y, int W, int H)   // interior bounds
    {
        public bool Clashes(Room o) =>
            X - Margin * 2 <= o.X + o.W - 1 && o.X <= X + W - 1 + Margin * 2
            && Y - Margin * 2 <= o.Y + o.H - 1 && o.Y <= Y + H - 1 + Margin * 2;
    }

    public static (List<TempleSection> Sections, List<GridPos> Sconces) Stamp(TileGrid grid, Random rng, int count)
    {
        var sections = new List<TempleSection>();
        var sconces = new List<GridPos>();
        foreach (var room in PickRooms(grid, rng, count))
            sections.Add(StampRoom(grid, rng, room, sections.Count, sconces));
        return (sections, sconces);
    }

    /// <summary>Doorways stay marble until corridors are carved, or a corridor could open one.</summary>
    public static void SealDoors(TileGrid grid, IEnumerable<TempleSection> sections)
    {
        foreach (var s in sections) grid.Set(s.Doorway, TileType.Rock);
    }

    private static List<Room> PickRooms(TileGrid grid, Random rng, int count)
    {
        var rooms = new List<Room>();
        int budget = (int)(grid.Width * grid.Height * MaxMapShare);
        for (int i = 0; i < Attempts && rooms.Count < count; i++)
        {
            int w = rooms.Count == 0 && i >= Attempts / 2 ? MinInterior : rng.Next(MinInterior, MaxInteriorWidth + 1);
            int h = rooms.Count == 0 && i >= Attempts / 2 ? MinInterior : rng.Next(MinInterior, MaxInteriorHeight + 1);
            int spareX = grid.Width - w - 2 * (Margin + 1), spareY = grid.Height - h - 2 * (Margin + 1);
            if (spareX <= 0 || spareY <= 0) continue;
            var r = new Room(Margin + 1 + rng.Next(spareX), Margin + 1 + rng.Next(spareY), w, h);
            int area = (w + 2 * Margin) * (h + 2 * Margin);
            if (area > budget || rooms.Any(r.Clashes)) continue;
            budget -= area;
            rooms.Add(r);
        }
        return rooms;
    }

    private static TempleSection StampRoom(TileGrid grid, Random rng, Room r, int id, List<GridPos> sconces)
    {
        int left = r.X - Ring, right = r.X + r.W, top = r.Y - Ring, bottom = r.Y + r.H;

        for (int y = top - Skin; y <= bottom + Skin; y++)
            for (int x = left - Skin; x <= right + Skin; x++)
            {
                bool outside = x < left || x > right || y < top || y > bottom;
                bool ring = !outside && (x == left || x == right || y == top || y == bottom);
                grid.Set(new GridPos(x, y), outside ? TileType.Rock : ring ? TileType.MarbleWall : TileType.TempleFloor);
            }

        var floor = new List<GridPos>();
        for (int y = r.Y; y < r.Y + r.H; y++)
            for (int x = r.X; x < r.X + r.W; x++)
                floor.Add(new GridPos(x, y));

        var doorway = PickDoorway(rng, left, right, top, bottom);
        var walls = RingTiles(left, right, top, bottom).Where(p => p != doorway).ToList();
        var panels = PlacePanels(grid, rng, walls);
        for (int i = 0; i < walls.Count; i += SconceSpacing)
            if (grid.Get(walls[i]) == TileType.MarbleWall) sconces.Add(walls[i]);

        var inner = doorway.X == left ? new GridPos(r.X, doorway.Y)
                  : doorway.X == right ? new GridPos(right - 1, doorway.Y)
                  : doorway.Y == top ? new GridPos(doorway.X, r.Y)
                  : new GridPos(doorway.X, bottom - 1);

        grid.Set(new GridPos(r.X + r.W / 2, r.Y + r.H / 2), TileType.Altar);
        PlaceColumns(grid, rng, r, inner);
        var jars = PlaceJars(grid, rng, r, inner);
        return new TempleSection(id, floor, panels, jars, doorway);
    }

    private static GridPos PickDoorway(Random rng, int left, int right, int top, int bottom)
    {
        int x = left + 2 + rng.Next(right - left - 3), y = top + 2 + rng.Next(bottom - top - 3);
        return rng.Next(4) switch
        {
            0 => new GridPos(x, top),
            1 => new GridPos(x, bottom),
            2 => new GridPos(left, y),
            _ => new GridPos(right, y),
        };
    }

    private static IEnumerable<GridPos> RingTiles(int left, int right, int top, int bottom)
    {
        for (int x = left + 1; x < right; x++) yield return new GridPos(x, top);
        for (int y = top + 1; y < bottom; y++) yield return new GridPos(right, y);
        for (int x = right - 1; x > left; x--) yield return new GridPos(x, bottom);
        for (int y = bottom - 1; y > top; y--) yield return new GridPos(left, y);
    }

    private static List<GridPos> PlacePanels(TileGrid grid, Random rng, List<GridPos> walls)
    {
        var panels = walls.Where(_ => rng.NextDouble() < PanelChance).ToList();
        foreach (var p in walls.OrderBy(_ => rng.Next()).Take(Math.Max(0, 2 - panels.Count))) panels.Add(p);
        panels = panels.Distinct().ToList();
        foreach (var p in panels) grid.Set(p, TileType.MarblePanel);
        return panels;
    }

    // In a regular grid, so the room stays walkable between them.
    private static void PlaceColumns(TileGrid grid, Random rng, Room r, GridPos keepClear)
    {
        var spots = new List<GridPos>();
        for (int y = r.Y + 1; y < r.Y + r.H - 1; y += ColumnSpacing)
            for (int x = r.X + 1; x < r.X + r.W - 1; x += ColumnSpacing)
            {
                var p = new GridPos(x, y);
                if (grid.Get(p) == TileType.TempleFloor && p.ManhattanTo(keepClear) > 1) spots.Add(p);
            }
        for (int i = 0; i < spots.Count; i++)
        {
            bool fallen = i == 1 || (i > 1 && rng.NextDouble() < FallenChance);
            grid.Set(spots[i], fallen ? TileType.FallenColumn : TileType.Column);
        }
    }

    // One cluster along a corner of the room. It only spreads over bare floor.
    private static List<GridPos> PlaceJars(TileGrid grid, Random rng, Room r, GridPos keepClear)
    {
        var corners = new[]
        {
            new GridPos(r.X, r.Y), new GridPos(r.X + r.W - 1, r.Y),
            new GridPos(r.X, r.Y + r.H - 1), new GridPos(r.X + r.W - 1, r.Y + r.H - 1),
        }.OrderBy(_ => rng.Next());
        foreach (var corner in corners)
        {
            if (corner.ManhattanTo(keepClear) < 4) continue;
            int want = 3 + rng.Next(4);
            var jars = new List<GridPos> { corner };
            for (int i = 0; i < jars.Count && jars.Count < want; i++)
                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = new GridPos(jars[i].X + d.Item1, jars[i].Y + d.Item2);
                    bool inside = n.X >= r.X && n.X < r.X + r.W && n.Y >= r.Y && n.Y < r.Y + r.H;
                    if (inside && jars.Count < want && !jars.Contains(n) && grid.Get(n) == TileType.TempleFloor)
                        jars.Add(n);
                }
            if (jars.Count < 3) continue;
            foreach (var p in jars) grid.Set(p, TileType.Jar);
            return jars;
        }
        return new List<GridPos>();
    }
}
