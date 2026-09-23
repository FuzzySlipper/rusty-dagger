using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

internal enum DaggerfallDungeonMapMarkerKind
{
    Entrance,
    Portal,
}

/// <summary>One source model placement with exact bounds and references to its aggregate render meshes.</summary>
internal sealed record DaggerfallDungeonMapGeometry(
    string PlacementId,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    IReadOnlyList<string> MeshIds,
    IReadOnlyList<Vector3> SamplePoints,
    DaggerfallRdbDoorId? DoorId)
{
    internal DaggerfallDungeonMapGeometry Validate()
    {
        if (!DaggerfallBaseContent.ValidId(PlacementId))
            throw new ArgumentException("Dungeon map geometry must use a stable source placement id.", nameof(PlacementId));
        ArgumentNullException.ThrowIfNull(MeshIds);
        if (MeshIds.Count == 0 || MeshIds.Any(meshId => !DaggerfallBaseContent.ValidId(meshId))
            || MeshIds.Distinct(StringComparer.Ordinal).Count() != MeshIds.Count)
        {
            throw new ArgumentException("Dungeon map geometry must reference distinct normalized render meshes.", nameof(MeshIds));
        }
        if (!IsFinite(BoundsMin) || !IsFinite(BoundsMax))
            throw new ArgumentOutOfRangeException(nameof(BoundsMin), "Dungeon map bounds must be finite.");
        if (BoundsMin.X > BoundsMax.X || BoundsMin.Y > BoundsMax.Y || BoundsMin.Z > BoundsMax.Z)
            throw new ArgumentException("Dungeon map bounds must be ordered.", nameof(BoundsMin));
        ArgumentNullException.ThrowIfNull(SamplePoints);
        if (SamplePoints.Count is < 2 or > 4 || SamplePoints.Any(point => !IsFinite(point)
            || point.X < BoundsMin.X || point.X > BoundsMax.X
            || point.Y < BoundsMin.Y || point.Y > BoundsMax.Y
            || point.Z < BoundsMin.Z || point.Z > BoundsMax.Z))
        {
            throw new ArgumentException("Dungeon map geometry requires two to four source surface samples inside its bounds.", nameof(SamplePoints));
        }
        return this;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

/// <summary>A stable source marker admitted with one dungeon profile.</summary>
internal sealed record DaggerfallDungeonMapMarker(
    string Id,
    DaggerfallDungeonMapMarkerKind Kind,
    WorldPoint Position,
    string? DestinationLogicalProfile = null)
{
    internal DaggerfallDungeonMapMarker Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id))
            throw new ArgumentException("Dungeon map markers must use stable source ids.", nameof(Id));
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));

        if (Kind == DaggerfallDungeonMapMarkerKind.Entrance)
        {
            if (DestinationLogicalProfile is not null)
                throw new ArgumentException("An entrance marker cannot name a destination profile.", nameof(DestinationLogicalProfile));
        }
        else if (string.IsNullOrWhiteSpace(DestinationLogicalProfile)
            || !DaggerfallBaseContent.ValidId(DestinationLogicalProfile.Replace('/', '-')))
        {
            throw new ArgumentException("A portal marker must name its stable destination profile.", nameof(DestinationLogicalProfile));
        }

        return this;
    }
}

/// <summary>Source-normalized dungeon map facts, independent of Engine artifact bytes.</summary>
internal sealed class DaggerfallDungeonMapContent
{
    private readonly HashSet<string> _placementIds;
    private readonly IReadOnlyDictionary<string, DaggerfallDungeonMapGeometry> _geometryById;
    private readonly HashSet<DaggerfallRdbDoorId> _doorIds;
    private readonly Dictionary<string, DaggerfallDungeonMapMarker> _markers;

    internal DaggerfallDungeonMapContent(
        IEnumerable<DaggerfallDungeonMapGeometry> geometry,
        IEnumerable<DaggerfallRdbDoorId> doors,
        IEnumerable<DaggerfallDungeonMapMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(markers);

        DaggerfallDungeonMapGeometry[] admittedGeometry = geometry
            .Select(value => (value ?? throw new ArgumentException("Dungeon map geometry cannot contain null.", nameof(geometry))).Validate())
            .OrderBy(value => value.PlacementId, StringComparer.Ordinal)
            .ToArray();
        if (admittedGeometry.Select(value => value.PlacementId).Distinct(StringComparer.Ordinal).Count() != admittedGeometry.Length)
            throw new ArgumentException("Dungeon map geometry repeats a source placement id.", nameof(geometry));

        DaggerfallRdbDoorId[] admittedDoors = doors
            .Select(value => ValidateDoor(value))
            .OrderBy(value => value.SourceKey, StringComparer.Ordinal)
            .ThenBy(value => value.BlockX)
            .ThenBy(value => value.BlockZ)
            .ThenBy(value => value.ModelIndex)
            .ToArray();
        if (admittedDoors.Distinct().Count() != admittedDoors.Length)
            throw new ArgumentException("Dungeon map content repeats a normalized RDB door id.", nameof(doors));

        DaggerfallDungeonMapMarker[] admittedMarkers = markers
            .Select(value => (value ?? throw new ArgumentException("Dungeon map markers cannot contain null.", nameof(markers))).Validate())
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
        if (admittedMarkers.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != admittedMarkers.Length)
            throw new ArgumentException("Dungeon map content repeats a source marker id.", nameof(markers));

        GeometryPlacements = Array.AsReadOnly(admittedGeometry);
        DoorIds = Array.AsReadOnly(admittedDoors);
        Markers = Array.AsReadOnly(admittedMarkers);
        _doorIds = admittedDoors.ToHashSet();
        foreach (DaggerfallDungeonMapGeometry placement in admittedGeometry)
        {
            if (placement.DoorId is { } doorId && !_doorIds.Contains(doorId))
                throw new ArgumentException($"Dungeon map placement '{placement.PlacementId}' refers to an unknown source door '{doorId}'.", nameof(geometry));
        }
        _placementIds = admittedGeometry.Select(value => value.PlacementId).ToHashSet(StringComparer.Ordinal);
        _geometryById = admittedGeometry.ToDictionary(value => value.PlacementId, StringComparer.Ordinal);
        _markers = admittedMarkers.ToDictionary(value => value.Id, StringComparer.Ordinal);
    }

    internal IReadOnlyList<DaggerfallDungeonMapGeometry> GeometryPlacements { get; }
    internal IReadOnlyList<DaggerfallRdbDoorId> DoorIds { get; }
    internal IReadOnlyList<DaggerfallDungeonMapMarker> Markers { get; }

    internal bool ContainsPlacement(string placementId) => _placementIds.Contains(placementId);
    internal bool ContainsDoor(DaggerfallRdbDoorId id) => _doorIds.Contains(id);
    internal bool TryGetMarker(string id, out DaggerfallDungeonMapMarker marker) => _markers.TryGetValue(id, out marker!);

    internal DaggerfallDungeonMapGeometry RequirePlacement(string placementId) =>
        _geometryById.TryGetValue(placementId, out DaggerfallDungeonMapGeometry? geometry)
            ? geometry
            : throw new InvalidOperationException($"Dungeon map profile has no source geometry placement '{placementId}'.");

    internal DaggerfallDungeonMapMarker RequireMarker(string id) => TryGetMarker(id, out DaggerfallDungeonMapMarker marker)
        ? marker
        : throw new InvalidOperationException($"Dungeon map profile has no source marker '{id}'.");

    private static DaggerfallRdbDoorId ValidateDoor(DaggerfallRdbDoorId id)
    {
        DaggerfallDoorIdentity.Validate(id);
        return id;
    }
}

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
        if (Profile.Kind != DaggerfallWorldProfileKind.Dungeon)
            throw new ArgumentException("Dungeon discovery requires a dungeon world profile.", nameof(profile));
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
