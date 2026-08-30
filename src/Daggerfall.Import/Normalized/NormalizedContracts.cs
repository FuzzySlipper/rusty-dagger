using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// The stable, Engine-free schema emitted by an offline content importer.  It
/// records normalized source facts and artifact closure only; ruleset policy
/// and runtime objects deliberately live elsewhere.
/// </summary>
public sealed record NormalizedImportDocument(
    int SchemaVersion,
    ImportProvenance Provenance,
    IReadOnlyList<NormalizedArtifactDescriptor> Artifacts,
    NormalizedCoordinateConvention Coordinates,
    NormalizedBounds Bounds,
    IReadOnlyList<NormalizedMesh> Meshes,
    NormalizedNavigationGrid? Navigation,
    NormalizedWorld World,
    IReadOnlyList<NormalizedResourceCatalogEntry> Resources)
{
    public const int CurrentSchemaVersion = 1;

    public NormalizedImportDocument Canonicalize() => this with
    {
        Provenance = Provenance.Canonicalize(),
        Artifacts = Artifacts.OrderBy(artifact => artifact.Id, StringComparer.Ordinal)
            .Select(artifact => artifact.Canonicalize()).ToArray(),
        Meshes = Meshes.OrderBy(mesh => mesh.Id, StringComparer.Ordinal)
            .Select(mesh => mesh.Canonicalize()).ToArray(),
        Navigation = Navigation?.Canonicalize(),
        World = World.Canonicalize(),
        Resources = Resources.OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => resource.Canonicalize()).ToArray(),
    };

    public void Validate()
    {
        RequireSchemaVersion(SchemaVersion, nameof(SchemaVersion));
        ArgumentNullException.ThrowIfNull(Provenance);
        ArgumentNullException.ThrowIfNull(Artifacts);
        ArgumentNullException.ThrowIfNull(Coordinates);
        ArgumentNullException.ThrowIfNull(Bounds);
        ArgumentNullException.ThrowIfNull(Meshes);
        ArgumentNullException.ThrowIfNull(World);
        ArgumentNullException.ThrowIfNull(Resources);

        Provenance.Validate();
        Coordinates.Validate();
        Bounds.Validate();

        ValidateUnique(Artifacts, artifact => artifact.Id, "artifact");
        foreach (NormalizedArtifactDescriptor artifact in Artifacts)
        {
            artifact.Validate();
        }

        HashSet<string> artifactIds = Artifacts.Select(artifact => artifact.Id).ToHashSet(StringComparer.Ordinal);
        foreach (NormalizedArtifactDescriptor artifact in Artifacts)
        {
            ValidateReferences(artifact.DependsOnArtifactIds, artifactIds, $"artifact '{artifact.Id}' dependency");
        }

        ValidateUnique(Resources, resource => resource.Id, "resource");
        foreach (NormalizedResourceCatalogEntry resource in Resources)
        {
            resource.Validate();
            RequireReference(resource.ArtifactId, artifactIds, $"resource '{resource.Id}' artifact");
        }

        HashSet<string> resourceIds = Resources.Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        foreach (NormalizedResourceCatalogEntry resource in Resources)
        {
            ValidateReferences(resource.Dependencies, resourceIds, $"resource '{resource.Id}' dependency");
        }
        ValidateUnique(Meshes, mesh => mesh.Id, "mesh");
        foreach (NormalizedMesh mesh in Meshes)
        {
            mesh.Validate();
            RequireReference(mesh.ArtifactId, artifactIds, $"mesh '{mesh.Id}' artifact");
            foreach (NormalizedMaterialGroup group in mesh.MaterialGroups)
            {
                RequireReference(group.MaterialResourceId, resourceIds, $"mesh '{mesh.Id}' material group");
            }
        }

        Navigation?.Validate(artifactIds);
        if (World.NavigationId is not null && (Navigation is null || !StringComparer.Ordinal.Equals(World.NavigationId, Navigation.Id)))
        {
            throw new InvalidOperationException($"World navigation '{World.NavigationId}' does not identify the normalized navigation grid.");
        }

        if (World.NavigationId is null && Navigation is not null)
        {
            throw new InvalidOperationException("A normalized navigation grid must be referenced by the normalized world.");
        }

        World.Validate(Meshes.Select(mesh => mesh.Id).ToHashSet(StringComparer.Ordinal), resourceIds);
    }

    internal static void RequireSchemaVersion(int schemaVersion, string name)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(name, schemaVersion, $"Only normalized schema version {CurrentSchemaVersion} is supported.");
        }
    }

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

    internal static void RequireReference(string value, IReadOnlySet<string> candidates, string name)
    {
        RequireLogicalId(value, name);
        if (!candidates.Contains(value))
        {
            throw new InvalidOperationException($"{name} references unknown ID '{value}'.");
        }
    }

    internal static void ValidateReferences(IEnumerable<string> values, IReadOnlySet<string> candidates, string name)
    {
        foreach (string value in values)
        {
            RequireReference(value, candidates, name);
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

/// <summary>One logical source record; paths are intentionally portable, never host paths.</summary>
public sealed record LogicalSourceRecord(int SchemaVersion, string SourcePath, ContentDigest ContentDigest, long ByteLength, int SourceSchemaVersion)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only logical source schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalPath(SourcePath, nameof(SourcePath));
        ContentDigest.Validate();
        if (ByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, "A logical source must retain its positive caller byte length.");
        }

        if (SourceSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceSchemaVersion), SourceSchemaVersion, "A source schema version must be positive.");
        }
    }
}

/// <summary>Aggregate importer provenance without machine-local or time-varying fields.</summary>
public sealed record ImportProvenance(int SchemaVersion, string ImporterId, int ImporterVersion, IReadOnlyList<LogicalSourceRecord> Sources)
{
    public const int CurrentSchemaVersion = 1;

    public ImportProvenance Canonicalize() => this with
    {
        Sources = Sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only provenance schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalId(ImporterId, nameof(ImporterId));
        if (ImporterVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ImporterVersion), ImporterVersion, "An importer version must be positive.");
        }

        ArgumentNullException.ThrowIfNull(Sources);
        if (Sources.Count == 0)
        {
            throw new InvalidOperationException("Import provenance must identify at least one logical source.");
        }

        NormalizedImportDocument.ValidateUnique(Sources, source => source.SourcePath, "logical source");
        foreach (LogicalSourceRecord source in Sources)
        {
            source.Validate();
        }
    }
}

/// <summary>A produced file and its explicit closure inside the normalized pack.</summary>
public sealed record NormalizedArtifactDescriptor(
    int SchemaVersion,
    string Id,
    string RelativePath,
    ContentDigest ContentDigest,
    long ByteLength,
    IReadOnlyList<string> DependsOnArtifactIds)
{
    public const int CurrentSchemaVersion = 1;

    public NormalizedArtifactDescriptor Canonicalize() => this with
    {
        DependsOnArtifactIds = DependsOnArtifactIds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only artifact schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        if (ByteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, "An artifact byte length cannot be negative.");
        }

        ContentDigest.Validate();

        ArgumentNullException.ThrowIfNull(DependsOnArtifactIds);
        NormalizedImportDocument.ValidateUnique(DependsOnArtifactIds, value => value, "artifact dependency");
    }
}

public enum NormalizedHandedness
{
    Right,
    Left,
}

public enum NormalizedVerticalAxis
{
    PositiveY,
    NegativeY,
}

public sealed record NormalizedCoordinateConvention(int SchemaVersion, NormalizedHandedness Handedness, NormalizedVerticalAxis VerticalAxis, float UnitsPerMeter)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only coordinate schema version {CurrentSchemaVersion} is supported.");
        }

        if (!Enum.IsDefined(Handedness) || !Enum.IsDefined(VerticalAxis))
        {
            throw new ArgumentOutOfRangeException("coordinate convention", "The normalized coordinate convention is not known.");
        }

        NormalizedImportDocument.RequireFinite(UnitsPerMeter, nameof(UnitsPerMeter));
        if (UnitsPerMeter <= 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(UnitsPerMeter), UnitsPerMeter, "Units per metre must be positive.");
        }
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

public sealed record NormalizedBounds(int SchemaVersion, NormalizedVector3 Minimum, NormalizedVector3 Maximum)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only bounds schema version {CurrentSchemaVersion} is supported.");
        }

        Minimum.Validate(nameof(Minimum));
        Maximum.Validate(nameof(Maximum));
        if (Minimum.X > Maximum.X || Minimum.Y > Maximum.Y || Minimum.Z > Maximum.Z)
        {
            throw new ArgumentException("Normalized bounds minimum cannot exceed maximum on any axis.");
        }
    }
}

public readonly record struct NormalizedTriangle(int FirstVertex, int SecondVertex, int ThirdVertex);

public sealed record NormalizedMaterialGroup(string MaterialResourceId, int StartTriangle, int TriangleCount, bool ParticipatesInCollision)
{
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
    int SchemaVersion,
    string Id,
    string ArtifactId,
    IReadOnlyList<NormalizedVector3> Vertices,
    IReadOnlyList<NormalizedVector3> Normals,
    IReadOnlyList<NormalizedVector2> TextureCoordinates,
    IReadOnlyList<NormalizedTriangle> Triangles,
    IReadOnlyList<NormalizedMaterialGroup> MaterialGroups)
{
    public const int CurrentSchemaVersion = 1;

    public NormalizedMesh Canonicalize() => this with
    {
        MaterialGroups = MaterialGroups.OrderBy(group => group.StartTriangle)
            .ThenBy(group => group.MaterialResourceId, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only mesh schema version {CurrentSchemaVersion} is supported.");
        }

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

public sealed record NormalizedNavigationCell(int Column, int Row, int Level, float SupportHeight, bool Walkable)
{
    public void Validate(NormalizedNavigationGrid grid)
    {
        if (Column < 0 || Column >= grid.Width || Row < 0 || Row >= grid.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(Column), "A navigation cell lies outside its declared grid dimensions.");
        }

        NormalizedImportDocument.RequireFinite(SupportHeight, nameof(SupportHeight));
    }
}

public sealed record NormalizedNavigationGrid(
    int SchemaVersion,
    string Id,
    string ArtifactId,
    NormalizedVector3 Origin,
    float CellSize,
    int Width,
    int Height,
    float LevelHeight,
    IReadOnlyList<NormalizedNavigationCell> Cells)
{
    public const int CurrentSchemaVersion = 1;

    public NormalizedNavigationGrid Canonicalize() => this with
    {
        Cells = Cells.OrderBy(cell => cell.Column).ThenBy(cell => cell.Row).ThenBy(cell => cell.Level).ToArray(),
    };

    public void Validate(IReadOnlySet<string> artifactIds)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only navigation schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireReference(ArtifactId, artifactIds, nameof(ArtifactId));
        Origin.Validate(nameof(Origin));
        NormalizedImportDocument.RequireFinite(CellSize, nameof(CellSize));
        NormalizedImportDocument.RequireFinite(LevelHeight, nameof(LevelHeight));
        if (CellSize <= 0F || LevelHeight <= 0F || Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CellSize), "Navigation grid dimensions and quantization values must be positive.");
        }

        ArgumentNullException.ThrowIfNull(Cells);
        HashSet<(int Column, int Row, int Level)> keys = [];
        foreach (NormalizedNavigationCell cell in Cells)
        {
            cell.Validate(this);
            if (!keys.Add((cell.Column, cell.Row, cell.Level)))
            {
                throw new InvalidOperationException("A navigation grid cannot contain duplicate column, row, and level cells.");
            }
        }
    }
}

public sealed record NormalizedMarker(string Id, NormalizedVector3 Position)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        Position.Validate(nameof(Position));
    }
}

public sealed record NormalizedLightPlacement(string Id, NormalizedVector3 Position, float Range, float Intensity)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        Position.Validate(nameof(Position));
        NormalizedImportDocument.RequireFinite(Range, nameof(Range));
        NormalizedImportDocument.RequireFinite(Intensity, nameof(Intensity));
        if (Range < 0F || Intensity < 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(Range), "Light range and intensity cannot be negative.");
        }
    }
}

public sealed record NormalizedBillboardPlacement(string Id, string SpriteResourceId, NormalizedVector3 Position, NormalizedVector2 Size)
{
    public void Validate(IReadOnlySet<string> resourceIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireReference(SpriteResourceId, resourceIds, nameof(SpriteResourceId));
        Position.Validate(nameof(Position));
        Size.Validate(nameof(Size));
        if (Size.X <= 0F || Size.Y <= 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(Size), "Billboard size must be positive.");
        }
    }
}

public sealed record NormalizedActorPlacement(string Id, string ActorResourceId, NormalizedVector3 Position)
{
    public void Validate(IReadOnlySet<string> resourceIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireReference(ActorResourceId, resourceIds, nameof(ActorResourceId));
        Position.Validate(nameof(Position));
    }
}

public sealed record NormalizedTreasurePlacement(string Id, string TreasureResourceId, NormalizedVector3 Position)
{
    public void Validate(IReadOnlySet<string> resourceIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireReference(TreasureResourceId, resourceIds, nameof(TreasureResourceId));
        Position.Validate(nameof(Position));
    }
}

public sealed record NormalizedDoorPlacement(string Id, string DoorResourceId, NormalizedVector3 Position, NormalizedVector3 RotationDegrees)
{
    public void Validate(IReadOnlySet<string> resourceIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireReference(DoorResourceId, resourceIds, nameof(DoorResourceId));
        Position.Validate(nameof(Position));
        RotationDegrees.Validate(nameof(RotationDegrees));
    }
}

public sealed record NormalizedWorld(
    int SchemaVersion,
    string MeshId,
    string? NavigationId,
    NormalizedMarker? StartMarker,
    NormalizedMarker? EnterMarker,
    IReadOnlyList<NormalizedLightPlacement> Lights,
    IReadOnlyList<NormalizedBillboardPlacement> Billboards,
    IReadOnlyList<NormalizedActorPlacement> Actors,
    IReadOnlyList<NormalizedTreasurePlacement> Treasures,
    IReadOnlyList<NormalizedDoorPlacement> Doors)
{
    public const int CurrentSchemaVersion = 1;

    public NormalizedWorld Canonicalize() => this with
    {
        Lights = Lights.OrderBy(light => light.Id, StringComparer.Ordinal).ToArray(),
        Billboards = Billboards.OrderBy(billboard => billboard.Id, StringComparer.Ordinal).ToArray(),
        Actors = Actors.OrderBy(actor => actor.Id, StringComparer.Ordinal).ToArray(),
        Treasures = Treasures.OrderBy(treasure => treasure.Id, StringComparer.Ordinal).ToArray(),
        Doors = Doors.OrderBy(door => door.Id, StringComparer.Ordinal).ToArray(),
    };

    public void Validate(IReadOnlySet<string> meshIds, IReadOnlySet<string> resourceIds)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only world schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireReference(MeshId, meshIds, nameof(MeshId));
        if (NavigationId is not null)
        {
            NormalizedImportDocument.RequireLogicalId(NavigationId, nameof(NavigationId));
        }

        StartMarker?.Validate();
        EnterMarker?.Validate();
        ArgumentNullException.ThrowIfNull(Lights);
        ArgumentNullException.ThrowIfNull(Billboards);
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(Treasures);
        ArgumentNullException.ThrowIfNull(Doors);
        NormalizedImportDocument.ValidateUnique(Lights, light => light.Id, "light placement");
        NormalizedImportDocument.ValidateUnique(Billboards, billboard => billboard.Id, "billboard placement");
        NormalizedImportDocument.ValidateUnique(Actors, actor => actor.Id, "actor placement");
        NormalizedImportDocument.ValidateUnique(Treasures, treasure => treasure.Id, "treasure placement");
        NormalizedImportDocument.ValidateUnique(Doors, door => door.Id, "door placement");
        foreach (NormalizedLightPlacement light in Lights) light.Validate();
        foreach (NormalizedBillboardPlacement billboard in Billboards) billboard.Validate(resourceIds);
        foreach (NormalizedActorPlacement actor in Actors) actor.Validate(resourceIds);
        foreach (NormalizedTreasurePlacement treasure in Treasures) treasure.Validate(resourceIds);
        foreach (NormalizedDoorPlacement door in Doors) door.Validate(resourceIds);
    }
}

public enum NormalizedResourceKind
{
    Material,
    Texture,
    Sprite,
    ActorDefinition,
    TreasureDefinition,
    DoorDefinition,
    Other,
}

/// <summary>Atlas coordinates and named source-frame metadata, with no playback behaviour or timing.</summary>
public sealed record NormalizedSpriteFrame(string Id, int FrameIndex, int X, int Y, int Width, int Height, NormalizedVector2 Pivot)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (FrameIndex < 0 || X < 0 || Y < 0 || Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FrameIndex), "Sprite frame coordinates and dimensions must be non-negative with a non-empty extent.");
        }

        Pivot.Validate(nameof(Pivot));
    }
}

public sealed record NormalizedResourceCatalogEntry(
    int SchemaVersion,
    string Id,
    NormalizedResourceKind Kind,
    string ArtifactId,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<NormalizedSpriteFrame> SpriteFrames)
{
    public const int CurrentSchemaVersion = 1;

    public NormalizedResourceCatalogEntry Canonicalize() => this with
    {
        Dependencies = Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        SpriteFrames = SpriteFrames.OrderBy(frame => frame.FrameIndex).ThenBy(frame => frame.Id, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only resource schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "The normalized resource kind is not known.");
        }

        NormalizedImportDocument.RequireLogicalId(ArtifactId, nameof(ArtifactId));
        ArgumentNullException.ThrowIfNull(Dependencies);
        ArgumentNullException.ThrowIfNull(SpriteFrames);
        NormalizedImportDocument.ValidateUnique(Dependencies, value => value, "resource dependency");
        NormalizedImportDocument.ValidateUnique(SpriteFrames, frame => frame.Id, "sprite frame");
        foreach (NormalizedSpriteFrame frame in SpriteFrames)
        {
            frame.Validate();
        }

        if (Kind != NormalizedResourceKind.Sprite && SpriteFrames.Count != 0)
        {
            throw new InvalidOperationException("Only sprite resources may contain sprite frame metadata.");
        }
    }
}

/// <summary>Canonical JSON writer and reader.  Serialization always appends one final newline.</summary>
public static class NormalizedImportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] Serialize(NormalizedImportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        NormalizedImportDocument canonical = document.Canonicalize();
        canonical.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, Options);
        return bytes.Concat([Encoding.UTF8.GetBytes("\n")[0]]).ToArray();
    }

    public static ContentDigest ComputeDigest(NormalizedImportDocument document) => ContentDigest.Compute(Serialize(document));

    public static NormalizedImportDocument Deserialize(ReadOnlySpan<byte> finalBytes)
    {
        if (finalBytes.IsEmpty || finalBytes[^1] != (byte)'\n')
        {
            throw new JsonException("Normalized JSON must end with exactly one final newline.");
        }

        NormalizedImportDocument document = JsonSerializer.Deserialize<NormalizedImportDocument>(finalBytes, Options)
            ?? throw new JsonException("Normalized JSON did not contain a document.");
        document.Validate();
        return document.Canonicalize();
    }
}

/// <summary>
/// Compatibility view for the existing external <c>*.import.json</c> manifest.
/// It preserves its <c>sourcePath</c> and byte-length spelling while normal
/// contracts use logical sources and <see cref="NormalizedArtifactDescriptor.ByteLength"/>.
/// </summary>
public sealed record ExternalImportManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("sourcePath")] string SourcePath,
    [property: JsonPropertyName("sourceHash")] ContentDigest SourceHash,
    [property: JsonPropertyName("sourceByteLen")] long SourceByteLen,
    [property: JsonPropertyName("sourceSchemaVersion")] int SourceSchemaVersion,
    [property: JsonPropertyName("importerVersion")] int ImporterVersion,
    [property: JsonPropertyName("meshAssetId")] string MeshAssetId,
    [property: JsonPropertyName("guid")] string? Guid,
    [property: JsonPropertyName("artifacts")] IReadOnlyList<ExternalImportArtifact> Artifacts);

public sealed record ExternalImportArtifact(
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("contentHash")] ContentDigest ContentHash,
    [property: JsonPropertyName("byteLen")] long ByteLen);

public static class ExternalImportManifestAdapter
{
    public static ImportProvenance ToProvenance(ExternalImportManifest manifest, string importerId)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new(
            ImportProvenance.CurrentSchemaVersion,
            importerId,
            manifest.ImporterVersion,
            [new(LogicalSourceRecord.CurrentSchemaVersion, manifest.SourcePath, manifest.SourceHash, manifest.SourceByteLen, manifest.SourceSchemaVersion)]);
    }

    public static IReadOnlyList<NormalizedArtifactDescriptor> ToArtifacts(ExternalImportManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Artifacts.Select(artifact => new NormalizedArtifactDescriptor(
            NormalizedArtifactDescriptor.CurrentSchemaVersion,
            artifact.RelativePath,
            artifact.RelativePath,
            artifact.ContentHash,
            artifact.ByteLen,
            [])).ToArray();
    }
}
