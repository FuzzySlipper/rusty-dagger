using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>A player-authored note anchored to an ordinary world-space map position.</summary>
internal sealed record DaggerfallDungeonNoteMarker(string Id, WorldPoint Position, string Text)
{
    internal DaggerfallDungeonNoteMarker Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id))
            throw new ArgumentException("A dungeon note marker must have a stable id.", nameof(Id));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));
        ArgumentNullException.ThrowIfNull(Text);
        return this;
    }
}

/// <summary>A profile-scoped decimetre cell in normalized world coordinates, hit by an Engine ray.</summary>
internal readonly record struct DaggerfallDungeonSurfaceCell(int X, int Y, int Z)
{
    internal const float Size = .1f;

    internal static DaggerfallDungeonSurfaceCell At(Vector3 point)
    {
        Vector3 scaled = point / Size;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)
            || scaled.X < int.MinValue || scaled.X >= int.MaxValue
            || scaled.Y < int.MinValue || scaled.Y >= int.MaxValue
            || scaled.Z < int.MinValue || scaled.Z >= int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(point));
        return new((int)MathF.Floor(scaled.X), (int)MathF.Floor(scaled.Y), (int)MathF.Floor(scaled.Z));
    }
}

/// <summary>
/// Durable exploration facts for exactly one geographic site and logical world profile. Visit
/// coloring is intentionally transient; current door motion and lock state remain door-runtime truth.
/// </summary>
internal sealed record DaggerfallDungeonDiscoverySnapshot(
    DaggerfallWorldProfileKey Profile,
    string[] DiscoveredPlacementIds,
    DaggerfallRdbDoorId[] DiscoveredDoors,
    string[] DiscoveredMarkerIds,
    DaggerfallDungeonNoteMarker[] NoteMarkers,
    DaggerfallDungeonSurfaceCell[] DiscoveredSurfaceCells);

/// <summary>
/// Incremental exploration state for one admitted dungeon profile. Engine visibility callers pass
/// stable source placement, door, and marker identities after their own spatial checks.
/// </summary>
internal sealed class DaggerfallDungeonDiscovery
{
    private readonly DaggerfallDungeonMapContent _content;
    private readonly HashSet<string> _discoveredPlacements = new(StringComparer.Ordinal);
    private readonly HashSet<DaggerfallRdbDoorId> _discoveredDoors = [];
    private readonly HashSet<string> _discoveredMarkers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visitedPlacements = new(StringComparer.Ordinal);
    private readonly HashSet<DaggerfallRdbDoorId> _visitedDoors = [];
    private readonly HashSet<string> _visitedMarkers = new(StringComparer.Ordinal);
    private readonly HashSet<DaggerfallDungeonSurfaceCell> _discoveredSurfaceCells = [];
    private readonly HashSet<DaggerfallDungeonSurfaceCell> _visitedSurfaceCells = [];
    private readonly Dictionary<string, DaggerfallDungeonNoteMarker> _notes = new(StringComparer.Ordinal);

    internal DaggerfallDungeonDiscovery(
        DaggerfallWorldProfileKey profile,
        DaggerfallDungeonMapContent content,
        DaggerfallDungeonDiscoverySnapshot? restored = null)
    {
        Profile = profile.Validate();
        if (Profile.Kind is not (DaggerfallWorldProfileKind.Dungeon or DaggerfallWorldProfileKind.Interior))
            throw new ArgumentException("Exploration requires a dungeon or building interior profile.", nameof(profile));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        if (restored is not null) Restore(restored);
    }

    internal DaggerfallWorldProfileKey Profile { get; }
    internal DaggerfallDungeonMapContent Content => _content;

    internal bool IsPlacementDiscovered(string placementId)
    {
        _ = _content.RequirePlacement(placementId);
        return _discoveredPlacements.Contains(placementId);
    }

    internal bool IsSurfaceCellDiscovered(DaggerfallDungeonSurfaceCell cell) => _discoveredSurfaceCells.Contains(cell);

    internal bool ObserveSurface(Vector3 point)
    {
        // A source placement can overlap another, but the world-space cell of the
        // Engine-confirmed surface never depends on guessing which source owns it.
        if (!_content.GeometryPlacements.Any(geometry => point.X >= geometry.BoundsMin.X - .5f
            && point.X <= geometry.BoundsMax.X + .5f
            && point.Y >= geometry.BoundsMin.Y - .5f
            && point.Y <= geometry.BoundsMax.Y + .5f
            && point.Z >= geometry.BoundsMin.Z - .5f
            && point.Z <= geometry.BoundsMax.Z + .5f))
            return false;
        DaggerfallDungeonSurfaceCell cell = DaggerfallDungeonSurfaceCell.At(point);
        _visitedSurfaceCells.Add(cell);
        return _discoveredSurfaceCells.Add(cell);
    }

    internal bool WasPlacementVisitedThisEntry(string placementId)
    {
        _ = _content.RequirePlacement(placementId);
        return _visitedPlacements.Contains(placementId);
    }

    internal bool IsDoorDiscovered(DaggerfallRdbDoorId id)
    {
        RequireDoor(id);
        return _discoveredDoors.Contains(id);
    }

    internal bool WasDoorVisitedThisEntry(DaggerfallRdbDoorId id)
    {
        RequireDoor(id);
        return _visitedDoors.Contains(id);
    }

    internal bool IsMarkerDiscovered(string markerId)
    {
        _ = _content.RequireMarker(markerId);
        return _discoveredMarkers.Contains(markerId);
    }

    internal bool WasMarkerVisitedThisEntry(string markerId)
    {
        _ = _content.RequireMarker(markerId);
        return _visitedMarkers.Contains(markerId);
    }

    /// <summary>Records a source geometry placement after Engine visibility admits its source mesh.</summary>
    internal bool ObservePlacement(string placementId)
    {
        _ = _content.RequirePlacement(placementId);
        _visitedPlacements.Add(placementId);
        return _discoveredPlacements.Add(placementId);
    }

    /// <summary>Records the stable door identity, without caching its live open, closed, or lock state.</summary>
    internal bool ObserveDoor(DaggerfallRdbDoorId id)
    {
        RequireDoor(id);
        _visitedDoors.Add(id);
        return _discoveredDoors.Add(id);
    }

    /// <summary>Records an authored entrance or portal marker after the caller confirms discovery.</summary>
    internal bool RevealMarker(string markerId)
    {
        _ = _content.RequireMarker(markerId);
        _visitedMarkers.Add(markerId);
        return _discoveredMarkers.Add(markerId);
    }

    /// <summary>Starts another entry into the same profile while retaining durable discoveries and notes.</summary>
    internal void BeginVisit()
    {
        _visitedSurfaceCells.Clear();
        _visitedPlacements.Clear();
        _visitedDoors.Clear();
        _visitedMarkers.Clear();
    }

    internal IReadOnlyList<DaggerfallDungeonNoteMarker> NoteMarkers => Array.AsReadOnly(_notes.Values
        .OrderBy(marker => marker.Id, StringComparer.Ordinal)
        .ToArray());

    internal void AddNoteMarker(string id, WorldPoint position, string text)
    {
        DaggerfallDungeonNoteMarker marker = new DaggerfallDungeonNoteMarker(id, position, text).Validate();
        if (_content.ContainsPlacement(id) || _content.TryGetMarker(id, out _))
            throw new ArgumentException($"Dungeon note marker id '{id}' conflicts with a source map identity.", nameof(id));
        if (!_notes.TryAdd(marker.Id, marker))
            throw new InvalidOperationException($"Dungeon note marker '{id}' already exists.");
    }

    internal void EditNoteMarker(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_notes.TryGetValue(id, out DaggerfallDungeonNoteMarker? existing))
            throw new InvalidOperationException($"Dungeon note marker '{id}' does not exist.");
        _notes[id] = existing with { Text = text };
    }

    internal bool RemoveNoteMarker(string id) => _notes.Remove(id);

    /// <summary>The next unused note identity on this dungeon's map.</summary>
    internal string NextNoteId()
    {
        int next = 1;
        foreach (DaggerfallDungeonNoteMarker note in NoteMarkers)
            if (note.Id.StartsWith("note-", StringComparison.Ordinal) && int.TryParse(note.Id.AsSpan(5), out int used) && used >= next)
                next = checked(used + 1);
        return $"note-{next.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    internal DaggerfallDungeonDiscoverySnapshot Capture() => new(
        Profile,
        _discoveredPlacements.Order(StringComparer.Ordinal).ToArray(),
        _discoveredDoors.OrderBy(id => id.SourceKey, StringComparer.Ordinal)
            .ThenBy(id => id.BlockX).ThenBy(id => id.BlockZ).ThenBy(id => id.ModelIndex).ToArray(),
        _discoveredMarkers.Order(StringComparer.Ordinal).ToArray(),
        _notes.Values.OrderBy(marker => marker.Id, StringComparer.Ordinal).ToArray(),
        _discoveredSurfaceCells.OrderBy(cell => cell.X).ThenBy(cell => cell.Y).ThenBy(cell => cell.Z).ToArray());

    private void Restore(DaggerfallDungeonDiscoverySnapshot restored)
    {
        ArgumentNullException.ThrowIfNull(restored);
        if (restored.Profile.Validate() != Profile)
            throw new InvalidOperationException($"Dungeon discovery state for profile '{restored.Profile.LogicalId}' cannot restore into '{Profile.LogicalId}'.");
        ArgumentNullException.ThrowIfNull(restored.DiscoveredPlacementIds);
        ArgumentNullException.ThrowIfNull(restored.DiscoveredDoors);
        ArgumentNullException.ThrowIfNull(restored.DiscoveredMarkerIds);
        ArgumentNullException.ThrowIfNull(restored.NoteMarkers);
        ArgumentNullException.ThrowIfNull(restored.DiscoveredSurfaceCells);

        foreach (DaggerfallDungeonSurfaceCell cell in restored.DiscoveredSurfaceCells)
        {
            if (!_content.GeometryPlacements.Any(geometry => (cell.X + 1f) * DaggerfallDungeonSurfaceCell.Size >= geometry.BoundsMin.X - .5f
                && cell.X * DaggerfallDungeonSurfaceCell.Size <= geometry.BoundsMax.X + .5f
                && (cell.Y + 1f) * DaggerfallDungeonSurfaceCell.Size >= geometry.BoundsMin.Y - .5f
                && cell.Y * DaggerfallDungeonSurfaceCell.Size <= geometry.BoundsMax.Y + .5f
                && (cell.Z + 1f) * DaggerfallDungeonSurfaceCell.Size >= geometry.BoundsMin.Z - .5f
                && cell.Z * DaggerfallDungeonSurfaceCell.Size <= geometry.BoundsMax.Z + .5f))
                throw new InvalidOperationException($"Dungeon discovery state names surface cell {cell} outside profile '{Profile.LogicalId}'.");
            if (!_discoveredSurfaceCells.Add(cell))
                throw new InvalidOperationException($"Dungeon discovery state repeats surface cell {cell}.");
        }

        foreach (string placementId in restored.DiscoveredPlacementIds)
        {
            _ = _content.RequirePlacement(placementId);
            if (!_discoveredPlacements.Add(placementId))
                throw new InvalidOperationException($"Dungeon discovery state repeats source geometry placement '{placementId}'.");
        }

        foreach (DaggerfallRdbDoorId door in restored.DiscoveredDoors)
        {
            RequireDoor(door);
            if (!_discoveredDoors.Add(door))
                throw new InvalidOperationException($"Dungeon discovery state repeats door '{door}'.");
        }

        foreach (string markerId in restored.DiscoveredMarkerIds)
        {
            _ = _content.RequireMarker(markerId);
            if (!_discoveredMarkers.Add(markerId))
                throw new InvalidOperationException($"Dungeon discovery state repeats marker '{markerId}'.");
        }

        foreach (DaggerfallDungeonNoteMarker marker in restored.NoteMarkers)
        {
            marker.Validate();
            if (_content.ContainsPlacement(marker.Id) || _content.TryGetMarker(marker.Id, out _))
                throw new InvalidOperationException($"Dungeon discovery note marker '{marker.Id}' conflicts with a source map identity.");
            if (!_notes.TryAdd(marker.Id, marker))
                throw new InvalidOperationException($"Dungeon discovery state repeats note marker '{marker.Id}'.");
        }
    }

    private void RequireDoor(DaggerfallRdbDoorId id)
    {
        if (!_content.ContainsDoor(id))
            throw new InvalidOperationException($"Dungeon map profile '{Profile.LogicalId}' has no normalized door '{id}'.");
    }
}
