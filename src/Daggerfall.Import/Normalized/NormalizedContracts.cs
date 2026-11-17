using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// The rules every normalized record admits its values by: logical identities and portable paths, finite numbers
/// and uniqueness within a list.
/// </summary>
public static class NormalizedImportDocument
{
    internal static void RequireLogicalId(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A logical ID must be non-empty and whitespace-free.", name);
        }
    }

    internal static void RequireLogicalPath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith('\\')
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Split('/').Any(segment => segment is "." or ".." or ""))
        {
            throw new ArgumentException("A logical path must be relative, slash-separated, and cannot contain dot segments.", name);
        }
    }

    internal static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "A normalized numeric value must be finite.");
        }
    }

    internal static void ValidateUnique<T>(IEnumerable<T> values, Func<T, string> id, string kind)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string candidate = id(value);
            RequireLogicalId(candidate, kind);
            if (!seen.Add(candidate))
            {
                throw new InvalidOperationException($"Duplicate {kind} ID '{candidate}'.");
            }
        }
    }
}

/// <summary>Canonical lowercase SHA-256 content address over final emitted bytes.</summary>
[JsonConverter(typeof(ContentDigestJsonConverter))]
public readonly record struct ContentDigest
{
    public ContentDigest(string value)
    {
        if (value is null || value.Length != 64 || !value.All(IsLowercaseHexadecimal))
        {
            throw new ArgumentException("A content digest must be a lowercase 64-character SHA-256 hexadecimal value.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ContentDigest Compute(ReadOnlySpan<byte> finalBytes)
    {
        return new(Convert.ToHexString(SHA256.HashData(finalBytes)).ToLowerInvariant());
    }

    public override string ToString() => Value;

    public void Validate()
    {
        if (Value is null || Value.Length != 64 || !Value.All(IsLowercaseHexadecimal))
        {
            throw new InvalidOperationException("A content digest must be a lowercase 64-character SHA-256 hexadecimal value.");
        }
    }

    private static bool IsLowercaseHexadecimal(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
}

public sealed class ContentDigestJsonConverter : JsonConverter<ContentDigest>
{
    public override ContentDigest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new(reader.GetString() ?? throw new JsonException("A content digest cannot be null."));
    }

    public override void Write(Utf8JsonWriter writer, ContentDigest value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

public readonly record struct NormalizedVector2(float X, float Y)
{
    public void Validate(string name)
    {
        NormalizedImportDocument.RequireFinite(X, name);
        NormalizedImportDocument.RequireFinite(Y, name);
    }
}

public readonly record struct NormalizedVector3(float X, float Y, float Z)
{
    public void Validate(string name)
    {
        NormalizedImportDocument.RequireFinite(X, name);
        NormalizedImportDocument.RequireFinite(Y, name);
        NormalizedImportDocument.RequireFinite(Z, name);
    }
}

public sealed record NormalizedBounds(NormalizedVector3 Minimum, NormalizedVector3 Maximum);

public readonly record struct NormalizedTriangle(int FirstVertex, int SecondVertex, int ThirdVertex);

public sealed record NormalizedMaterialGroup(string MaterialResourceId, int StartTriangle, int TriangleCount, bool ParticipatesInCollision)
{
    /// <summary>Collision and navigation only: nothing draws it, as a location's ground plane under the walked terrain.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool CollisionOnly { get; init; }

    public void Validate(int triangleCount)
    {
        NormalizedImportDocument.RequireLogicalId(MaterialResourceId, nameof(MaterialResourceId));
        if (StartTriangle < 0 || TriangleCount <= 0 || StartTriangle > triangleCount - TriangleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(TriangleCount), "A material group must select a non-empty in-range triangle span.");
        }
    }
}

public sealed record NormalizedMesh(
    string Id,
    string ArtifactId,
    IReadOnlyList<NormalizedVector3> Vertices,
    IReadOnlyList<NormalizedVector3> Normals,
    IReadOnlyList<NormalizedVector2> TextureCoordinates,
    IReadOnlyList<NormalizedTriangle> Triangles,
    IReadOnlyList<NormalizedMaterialGroup> MaterialGroups)
{
    public NormalizedMesh Canonicalize() => this with
    {
        MaterialGroups = MaterialGroups.OrderBy(group => group.StartTriangle)
            .ThenBy(group => group.MaterialResourceId, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalId(ArtifactId, nameof(ArtifactId));
        ArgumentNullException.ThrowIfNull(Vertices);
        ArgumentNullException.ThrowIfNull(Normals);
        ArgumentNullException.ThrowIfNull(TextureCoordinates);
        ArgumentNullException.ThrowIfNull(Triangles);
        ArgumentNullException.ThrowIfNull(MaterialGroups);
        if (Vertices.Count == 0 || Normals.Count != Vertices.Count || TextureCoordinates.Count != Vertices.Count || Triangles.Count == 0)
        {
            throw new InvalidOperationException("A normalized mesh requires vertices, one normal and UV per vertex, and at least one triangle.");
        }

        foreach (NormalizedVector3 vertex in Vertices)
        {
            vertex.Validate("mesh vertex");
        }

        foreach (NormalizedVector3 normal in Normals)
        {
            normal.Validate("mesh normal");
        }

        foreach (NormalizedVector2 textureCoordinate in TextureCoordinates)
        {
            textureCoordinate.Validate("mesh texture coordinate");
        }

        foreach (NormalizedTriangle triangle in Triangles)
        {
            ValidateVertexIndex(triangle.FirstVertex);
            ValidateVertexIndex(triangle.SecondVertex);
            ValidateVertexIndex(triangle.ThirdVertex);
        }

        if (MaterialGroups.Count == 0)
        {
            throw new InvalidOperationException("A normalized mesh requires material groups.");
        }

        int expectedStart = 0;
        foreach (NormalizedMaterialGroup group in MaterialGroups.OrderBy(group => group.StartTriangle))
        {
            group.Validate(Triangles.Count);
            if (group.StartTriangle != expectedStart)
            {
                throw new InvalidOperationException("Material groups must provide one contiguous, non-overlapping coverage of mesh triangles.");
            }

            expectedStart += group.TriangleCount;
        }

        if (expectedStart != Triangles.Count)
        {
            throw new InvalidOperationException("Material groups must cover every mesh triangle exactly once.");
        }
    }

    private void ValidateVertexIndex(int index)
    {
        if (index < 0 || index >= Vertices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(Triangles), index, "A triangle references a vertex outside the mesh vertex buffer.");
        }
    }
}

/// <summary>
/// Explicit, content-visible controls used by the offline navigation derivation.
/// These are import policy defaults, rather than hidden magic numbers in the
/// geometry walker.  They never create a runtime navigation system.
/// </summary>
/// <param name="FootprintRadius">
/// How far around a cell's centre an actor's footprint reaches. A support whose ground falls away on both sides within
/// it is narrower than the actors that walk the navigation (a log's top, a railing, a wall's top) and admits no cell.
/// The actors are capsules of about this radius; the player's is the tuning's <c>characterController.radius</c>.
/// </param>
public sealed record NavigationDerivationConfig(
    float CellSize,
    float LevelQuantum,
    float MaximumSlopeDegrees,
    float RequiredHeadroom,
    float SupportProbeDrop,
    float FootprintRadius)
{
    public static NavigationDerivationConfig ClassicDefault { get; } = new(
        CellSize: 0.8F,
        LevelQuantum: 0.25F,
        MaximumSlopeDegrees: 45F,
        RequiredHeadroom: 1.8F,
        SupportProbeDrop: 0.05F,
        FootprintRadius: 0.25F);

    public void Validate()
    {
        NormalizedImportDocument.RequireFinite(CellSize, nameof(CellSize));
        NormalizedImportDocument.RequireFinite(LevelQuantum, nameof(LevelQuantum));
        NormalizedImportDocument.RequireFinite(MaximumSlopeDegrees, nameof(MaximumSlopeDegrees));
        NormalizedImportDocument.RequireFinite(RequiredHeadroom, nameof(RequiredHeadroom));
        NormalizedImportDocument.RequireFinite(SupportProbeDrop, nameof(SupportProbeDrop));
        NormalizedImportDocument.RequireFinite(FootprintRadius, nameof(FootprintRadius));
        if (CellSize <= 0F || LevelQuantum <= 0F || MaximumSlopeDegrees is <= 0F or >= 90F
            || RequiredHeadroom <= 0F || SupportProbeDrop < 0F || FootprintRadius <= 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(CellSize), "Navigation derivation values must be finite, positive, and use a slope below ninety degrees.");
        }
    }
}

/// <summary>One signed horizontal cell and quantized support level derived offline from collision geometry.</summary>
public sealed record NormalizedNavigationCell(int Column, int Row, int Level, float SupportHeight, bool Walkable);

/// <summary>
/// Sparse offline navigation supports. Coordinates deliberately remain signed:
/// importing an Arena2 layout must not silently shift authored block space.
/// </summary>
public sealed record NormalizedNavigationSurface(
    string Id,
    string ArtifactId,
    NavigationDerivationConfig Config,
    IReadOnlyList<NormalizedNavigationCell> Cells);

public sealed record NormalizedMarker(string Id, NormalizedVector3 Position);

public sealed record NormalizedLightPlacement(string Id, NormalizedVector3 Position, float Range, float Intensity, NormalizedVector3 Color)
{
    public bool CityLight { get; init; }
}

public sealed record NormalizedBillboardPlacement(string Id, string SpriteResourceId, NormalizedVector3 Position, NormalizedVector2 Size)
{
    public bool BaseAligned { get; init; }
    public bool ClimateNature { get; init; }
    /// <summary>A burning lamp: it glows, and its light is among the place's lights. An extinguished one is a plain flat.</summary>
    public bool Lamp { get; init; }
    /// <summary>
    /// The id, among the place's lights, of the light this flat sheds: a lamp's own, or a dungeon's light record beside it
    /// that outreaches and stands for it. Absent for a flat that sheds none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Light { get; init; }
}

public sealed record NormalizedActorPlacement(string Id, string ActorResourceId, NormalizedVector3 Position,
    int SpawnDistance = 0, bool Passive = false, string Gender = "unspecified");

/// <summary>Normalized RDB action linkage retained for the compiled action graph.</summary>
public sealed record NormalizedDoorAction(byte Axis, ushort Duration, ushort Magnitude, int NextObjectOffset, byte Flags);

/// <summary>An interior door plane in its own right-handed building frame.</summary>
public sealed record NormalizedBuildingEntryDoor(NormalizedVector3 Position, NormalizedVector3 Normal);

/// <summary>The exterior sub-record frame and the interior entry candidates it places.</summary>
public sealed record NormalizedBuildingEntryFrame(NormalizedVector3 ExteriorOrigin, float ExteriorYawDegrees,
    IReadOnlyList<NormalizedVector3> Markers)
{
    public IReadOnlyList<NormalizedBuildingEntryDoor> Doors { get; init; } = [];
}

/// <summary>
/// One source-authored exterior person placement. The importer preserves the source billboard,
/// faction and flags; race, role and service meaning remain ruleset policy over admitted catalogs.
/// </summary>
public sealed record NormalizedPopulationPlacement(
    string Id,
    NormalizedVector3 Position,
    int BillboardArchive,
    int BillboardRecord,
    int FactionId,
    byte Flags)
{
    /// <summary>A deterministic source-placement seed in the runtime's ushort name-seed width.</summary>
    public int NameSeed { get; init; }

    /// <summary>The exterior building slot that supplied this person, or zero for donor dynamic population.</summary>
    public int SourceBuildingType { get; init; }

    /// <summary>The faction on the supplying exterior building slot, or zero for donor dynamic population.</summary>
    public int SourceBuildingFactionId { get; init; }
}

/// <summary>
/// The kind of source-backed dungeon area whose ambient presentation may differ from the ordinary interior.
/// The importer carries only the classification; shelter, light selection, and transition policy remain
/// ruleset owners.
/// </summary>
public enum NormalizedAmbientZoneKind
{
    Castle,
    SpecialArea,
}

/// <summary>Placed furniture and its normalized item groups; source model/table interpretation stays offline.</summary>
public sealed record NormalizedPropertyContainer(string Id, NormalizedVector3 Position, IReadOnlyList<string> ItemGroups, IReadOnlyList<NormalizedVector3> InteractionPoints);

public sealed record NormalizedWaterVolume(string Id, NormalizedBounds Bounds);
