using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

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

/// <summary>Source-normalized dungeon map facts, independent of Engine artifact bytes.</summary>
internal sealed class DaggerfallDungeonMapContent
{
    private readonly HashSet<string> _placementIds;
    private readonly IReadOnlyDictionary<string, DaggerfallDungeonMapGeometry> _geometryById;
    private readonly HashSet<DaggerfallRdbDoorId> _doorIds;
    private readonly Dictionary<string, DaggerfallSiteMarker> _markers;

    internal DaggerfallDungeonMapContent(
        IEnumerable<DaggerfallDungeonMapGeometry> geometry,
        IEnumerable<DaggerfallRdbDoorId> doors,
        IEnumerable<DaggerfallSiteMarker> markers)
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

        DaggerfallSiteMarker[] admittedMarkers = markers
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
    internal IReadOnlyList<DaggerfallSiteMarker> Markers { get; }

    internal bool ContainsPlacement(string placementId) => _placementIds.Contains(placementId);
    internal bool ContainsDoor(DaggerfallRdbDoorId id) => _doorIds.Contains(id);
    internal bool TryGetMarker(string id, out DaggerfallSiteMarker marker) => _markers.TryGetValue(id, out marker!);

    internal DaggerfallDungeonMapGeometry RequirePlacement(string placementId) =>
        _geometryById.TryGetValue(placementId, out DaggerfallDungeonMapGeometry? geometry)
            ? geometry
            : throw new InvalidOperationException($"Dungeon map profile has no source geometry placement '{placementId}'.");

    internal DaggerfallSiteMarker RequireMarker(string id) => TryGetMarker(id, out DaggerfallSiteMarker marker)
        ? marker
        : throw new InvalidOperationException($"Dungeon map profile has no source marker '{id}'.");

    private static DaggerfallRdbDoorId ValidateDoor(DaggerfallRdbDoorId id)
    {
        DaggerfallDoorIdentity.Validate(id);
        return id;
    }
}
