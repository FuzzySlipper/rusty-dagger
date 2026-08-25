using System.Text.Json;

namespace RustyDagger.Product;

public readonly record struct NavCell(int X, int Z, int Level, float SupportY);

/// <summary>Reads the content-derived navigation projection. It is intentionally not a replacement collision world.</summary>
public sealed class NavigationGrid
{
    private readonly Dictionary<(int X, int Z), List<NavCell>> _columns;
    private readonly float _cellSize;

    private NavigationGrid(float cellSize, Dictionary<(int X, int Z), List<NavCell>> columns)
    {
        _cellSize = cellSize;
        _columns = columns;
    }

    public static NavigationGrid FromJson(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        var root = document.RootElement;
        var cellSize = root.GetProperty("cellSize").GetSingle();
        var columns = new Dictionary<(int X, int Z), List<NavCell>>();
        foreach (var row in root.GetProperty("cells").EnumerateArray())
        {
            var x = row[0].GetInt32();
            var z = row[1].GetInt32();
            var level = row[2].GetInt32();
            var support = row[3].GetSingle();
            var key = (x, z);
            if (!columns.TryGetValue(key, out var cells)) columns[key] = cells = [];
            cells.Add(new NavCell(x, z, level, support));
        }
        return new NavigationGrid(cellSize, columns);
    }

    public WorldPoint Snap(WorldPoint authored)
    {
        return TryFindNear(authored.X, authored.Z, authored.Y, 64f, out var cell)
            ? new WorldPoint((cell.X + .5f) * _cellSize, cell.SupportY, (cell.Z + .5f) * _cellSize)
            : authored;
    }

    public bool TryMove(WorldPoint current, float desiredX, float desiredZ, float stepUp, float maximumDrop, out WorldPoint moved)
    {
        if (!TryFindNear(desiredX, desiredZ, current.Y, Math.Max(stepUp, maximumDrop), out var support))
        {
            moved = current;
            return false;
        }
        var delta = support.SupportY - current.Y;
        if (delta > stepUp || delta < -maximumDrop)
        {
            moved = current;
            return false;
        }
        moved = new WorldPoint(desiredX, support.SupportY, desiredZ);
        return true;
    }

    /// <summary>
    /// Chooses one exact, same-floor nav-grid column near an authored local
    /// offset. Encounter placement uses this instead of inventing coordinates
    /// outside Privateer's Hold's navigation projection.
    /// </summary>
    public WorldPoint WalkableOffset(WorldPoint origin, float offsetX, float offsetZ)
    {
        var desiredX = origin.X + offsetX;
        var desiredZ = origin.Z + offsetZ;
        return TryFindNear(desiredX, desiredZ, origin.Y, .75f, out var cell)
            ? new WorldPoint((cell.X + .5f) * _cellSize, cell.SupportY, (cell.Z + .5f) * _cellSize)
            : origin;
    }

    /// <summary>Small exact-cell sample used by the local retained projection.</summary>
    public WorldPoint? WalkableNeighbor(WorldPoint origin, int offsetX, int offsetZ)
    {
        var cellX = (int)MathF.Floor(origin.X / _cellSize) + offsetX;
        var cellZ = (int)MathF.Floor(origin.Z / _cellSize) + offsetZ;
        if (!_columns.TryGetValue((cellX, cellZ), out var candidates)) return null;
        var selected = candidates
            .Where(candidate => MathF.Abs(candidate.SupportY - origin.Y) <= .75f)
            .OrderBy(candidate => MathF.Abs(candidate.SupportY - origin.Y))
            .FirstOrDefault();
        return selected == default
            ? null
            : new WorldPoint((selected.X + .5f) * _cellSize, selected.SupportY, (selected.Z + .5f) * _cellSize);
    }

    private bool TryFindNear(float x, float z, float y, float verticalLimit, out NavCell selected)
    {
        var cellX = (int)MathF.Floor(x / _cellSize);
        var cellZ = (int)MathF.Floor(z / _cellSize);
        selected = default;
        var found = false;
        var best = float.MaxValue;
        // A missing exact column is collision. Do not borrow support from an
        // adjacent cell: that turns real walls or holes into walkable seams.
        if (_columns.TryGetValue((cellX, cellZ), out var candidates))
        foreach (var candidate in candidates)
        {
            var distance = MathF.Abs(candidate.SupportY - y);
            if (distance <= verticalLimit && distance < best)
            {
                best = distance;
                selected = candidate;
                found = true;
            }
        }
        return found;
    }
}
