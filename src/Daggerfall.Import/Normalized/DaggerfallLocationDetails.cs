using System.Text;
using System.Text.Json;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// The part of one exterior location a reader needs only once that location is used: its placed buildings and its
/// MAPPITEM building references. The locations section keeps what startup, travel and the map read for every
/// location; this record and the location's ground file hold the rest.
/// </summary>
/// <param name="Buildings">The location's placed buildings, in donor traversal order.</param>
/// <param name="BuildingReferences">The location's raw MAPPITEM building references.</param>
public sealed record DaggerfallLocationDetail(
    IReadOnlyList<DaggerfallLocationBuilding> Buildings,
    IReadOnlyList<DaggerfallLocationBuildingReference> BuildingReferences);

/// <summary>
/// Where each exterior location's detail is published: under <see cref="DaggerfallLocations.Details"/>, the record
/// at <c>&lt;region&gt;/&lt;index&gt;.json</c> and the 128-by-128 ground grid as its raw bytes at
/// <c>&lt;region&gt;/&lt;index&gt;.ground</c>, one byte per tile as <see cref="DaggerfallLocationExterior.GroundTiles"/>
/// states it.
/// </summary>
public static class DaggerfallLocationDetails
{
    /// <summary>The content-root-relative directory the details are published under.</summary>
    public const string Root = "worldrpg/locations";

    /// <summary>The bytes of one location's ground file: the full terrain frame.</summary>
    public const int GroundBytes = 128 * 128;

    /// <summary>One location's detail record path below <paramref name="root"/>.</summary>
    public static string DetailPath(string root, int region, int index) => $"{root}/{region}/{index}.json";

    /// <summary>One location's ground file path below <paramref name="root"/>.</summary>
    public static string GroundPath(string root, int region, int index) => $"{root}/{region}/{index}.ground";

    /// <summary>
    /// Every exterior location's detail record and ground file, by content-root-relative path. An exterior always
    /// carries its full ground frame, so a ground file of any other size is refused rather than published short.
    /// </summary>
    public static IEnumerable<(string Path, byte[] Bytes)> Files(DaggerfallLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        foreach (DaggerfallLocationMap location in locations.Locations)
        {
            if (location.Exterior is not { } exterior) continue;
            if (exterior.GroundTiles.Length != GroundBytes)
                throw new InvalidOperationException($"Exterior location {location.Region}:{location.Index} '{location.Name}' carries {exterior.GroundTiles.Length} ground bytes; its ground file is the {GroundBytes}-byte terrain frame.");
            DaggerfallLocationDetail detail = new(exterior.Buildings, exterior.BuildingReferences);
            yield return (DetailPath(locations.Details, location.Region, location.Index),
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(detail, PublishedJson.SectionCompact)));
            yield return (GroundPath(locations.Details, location.Region, location.Index), exterior.GroundTiles);
        }
    }
}
