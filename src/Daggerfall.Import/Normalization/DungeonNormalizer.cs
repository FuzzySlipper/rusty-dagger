using System.Collections.ObjectModel;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// One immutable, caller-supplied Arena2 logical source.  Import never opens a
/// host path: labels are portable source identities and bytes are copied when
/// the source set is created.
/// </summary>
public sealed class DungeonLogicalSource
{
    private readonly byte[] bytes;

    public DungeonLogicalSource(string label, ReadOnlySpan<byte> bytes)
    {
        NormalizedImportDocument.RequireLogicalPath(label, nameof(label));
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A dungeon logical source cannot be empty.", nameof(bytes));
        }

        Label = label;
        this.bytes = bytes.ToArray();
    }

    public string Label { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;
}

/// <summary>
/// Immutable lookup of the source closure needed to normalize one Arena2
/// dungeon.  Labels may contain a portable prefix, but archive leaf names must
/// be unique so source selection cannot depend on a host directory.
/// </summary>
public sealed class DungeonLogicalSourceSet
{
    private readonly IReadOnlyList<DungeonLogicalSource> sources;
    private readonly IReadOnlyDictionary<string, DungeonLogicalSource> byLeafName;

    public DungeonLogicalSourceSet(IEnumerable<DungeonLogicalSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        DungeonLogicalSource[] materialized = sources.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("A dungeon source set must contain at least one source.", nameof(sources));
        }

        Dictionary<string, DungeonLogicalSource> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (DungeonLogicalSource source in materialized)
        {
            ArgumentNullException.ThrowIfNull(source);
            string leaf = LeafName(source.Label);
            if (!lookup.TryAdd(leaf, source))
            {
                throw new ArgumentException($"The dungeon source set contains multiple logical sources named '{leaf}'.", nameof(sources));
            }
        }

        this.sources = Array.AsReadOnly(materialized.OrderBy(source => source.Label, StringComparer.Ordinal).ToArray());
        byLeafName = new ReadOnlyDictionary<string, DungeonLogicalSource>(lookup);
    }

    public IReadOnlyList<DungeonLogicalSource> Sources => sources;

    public DungeonLogicalSource Require(string leafName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafName);
        return byLeafName.TryGetValue(leafName, out DungeonLogicalSource? source)
            ? source
            : throw new InvalidOperationException($"Dungeon normalization requires logical source '{leafName}'.");
    }

    public bool TryGet(string leafName, out DungeonLogicalSource? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafName);
        return byLeafName.TryGetValue(leafName, out source);
    }

    private static string LeafName(string label) => label[(label.LastIndexOf('/') + 1)..];
}

public enum DungeonTextureTableMode
{
    Classic,
    Default,
}

/// <summary>Explicit, bounded work limits for one offline normalization call.</summary>
public sealed record DungeonNormalizationQuotas(
    int MaximumSources,
    long MaximumSourceBytes,
    int MaximumBlocks,
    int MaximumModels,
    int MaximumVertices,
    int MaximumTriangles,
    int MaximumResources,
    int MaximumPlacements)
{
    public static DungeonNormalizationQuotas Default { get; } = new(512, 512L * 1024L * 1024L, 4096, 100_000, 4_000_000, 4_000_000, 100_000, 100_000);

    public void Validate()
    {
        if (MaximumSources <= 0 || MaximumSourceBytes <= 0 || MaximumBlocks <= 0 || MaximumModels <= 0
            || MaximumVertices <= 0 || MaximumTriangles <= 0 || MaximumResources <= 0 || MaximumPlacements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSources), "Dungeon normalization quotas must all be positive.");
        }
    }
}

/// <summary>Request for one exact region and source location.</summary>
public sealed record DungeonNormalizationRequest(
    DungeonLogicalSourceSet Sources,
    int Region,
    string LocationName,
    DungeonTextureTableMode TextureTableMode,
    DungeonNormalizationQuotas Quotas)
{
    /// <summary>
    /// Explicit offline support-surface policy.  Callers may preserve the
    /// classic defaults or select a validated profile without changing any
    /// source-format transform.
    /// </summary>
    public NavigationDerivationConfig Navigation { get; init; } = NavigationDerivationConfig.ClassicDefault;

    public static DungeonNormalizationRequest Create(DungeonLogicalSourceSet sources, int region, string locationName) =>
        new(sources, region, locationName, DungeonTextureTableMode.Classic, DungeonNormalizationQuotas.Default);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sources);
        if (Region is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(Region), Region, "MAPS region must be within 0..999.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(LocationName);
        if (TextureTableMode is not DungeonTextureTableMode.Classic and not DungeonTextureTableMode.Default)
        {
            throw new ArgumentOutOfRangeException(nameof(TextureTableMode), TextureTableMode, "The dungeon texture table mode is not known.");
        }

        ArgumentNullException.ThrowIfNull(Quotas);
        Quotas.Validate();
        ArgumentNullException.ThrowIfNull(Navigation);
        Navigation.Validate();
    }
}

/// <summary>Stable source identity for each normalized location record.</summary>
public sealed record DungeonRecordProvenance(string Id, string Kind, string SourceLabel, int SourceRecordOrdinal)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalId(Kind, nameof(Kind));
        NormalizedImportDocument.RequireLogicalPath(SourceLabel, nameof(SourceLabel));
        if (SourceRecordOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceRecordOrdinal), SourceRecordOrdinal, "A source record ordinal cannot be negative.");
        }
    }
}

/// <summary>Pure normalized document plus per-record source provenance.</summary>
public sealed record DungeonNormalizationResult(
    NormalizedImportDocument Document,
    IReadOnlyList<DungeonRecordProvenance> RecordProvenance,
    DungeonSpatialPublication SpatialPublication)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Document);
        ArgumentNullException.ThrowIfNull(RecordProvenance);
        ArgumentNullException.ThrowIfNull(SpatialPublication);
        Document.Validate();
        NormalizedImportDocument.ValidateUnique(RecordProvenance, provenance => provenance.Id, "dungeon record provenance");
        foreach (DungeonRecordProvenance provenance in RecordProvenance)
        {
            provenance.Validate();
        }

        SpatialPublication.ValidateAgainst(Document);
    }
}

/// <summary>
/// Pure Arena2 dungeon assembly.  It consumes only supplied byte sources and
/// produces source-normalized geometry, collision participation, sparse
/// navigation facts, placement facts, resources, and provenance.  It creates
/// no Engine object, runtime entity, image, GLB, or filesystem artifact.
/// </summary>
public static class DungeonNormalizer
{
    private const string ImporterId = "daggerfall-import/dungeon-normalizer";
    private const int ImporterVersion = 1;
    // Classic Arena2 coordinate units use a fixed conversion to metres. This
    // is a source-format invariant, not a product or presentation setting.
    private const float SourceUnitMetres = 0.025F;
    private const float LightRangeMultiplier = 3F;

    public static DungeonNormalizationResult Normalize(DungeonNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        EnforceSourceQuotas(request.Sources, request.Quotas);

        DungeonLogicalSource mapsSource = request.Sources.Require("MAPS.BSA");
        DungeonLogicalSource blocksSource = request.Sources.Require("BLOCKS.BSA");
        DungeonLogicalSource archSource = request.Sources.Require("ARCH3D.BSA");
        DungeonLogicalSource paletteSource = request.Sources.Require("PAL.PAL");
        DungeonLogicalSource climateSource = request.Sources.Require("CLIMATE.PAK");
        BsaArchive maps = BsaArchive.Parse(mapsSource.Bytes.Span, mapsSource.Label);
        BsaArchive blocks = BsaArchive.Parse(blocksSource.Bytes.Span, blocksSource.Label);
        BsaArchive arch = BsaArchive.Parse(archSource.Bytes.Span, archSource.Label);
        _ = PaletteDecoder.Decode(paletteSource.Bytes.Span, paletteSource.Label);
        PakMap climate = PakDecoder.Decode(climateSource.Bytes.Span, climateSource.Label);
        MapsDungeonLayout layout = MapsDecoder.DecodeDungeonLayout(maps, request.Region, request.LocationName);
        if (layout.Blocks.Count > request.Quotas.MaximumBlocks)
        {
            throw new InvalidOperationException($"Dungeon contains {layout.Blocks.Count} blocks, above the configured quota {request.Quotas.MaximumBlocks}.");
        }

        (int climateX, int climateY) = MapsDecoder.ToMapPixel(layout.Longitude, layout.Latitude);
        if (!climate.TryGetPixel(climateX, climateY, out byte worldClimate))
        {
            throw new InvalidOperationException($"CLIMATE.PAK has no source pixel at ({climateX}, {climateY}) for '{layout.LocationName}'.");
        }

        ushort[] textureTable = request.TextureTableMode == DungeonTextureTableMode.Classic
            ? DungeonTextureTableTransform.CreateClassic(layout.LocationId, worldClimate)
            : DungeonTextureTableTransform.CreateDefaultTable();
        ushort climateBase = ClimateBase(worldClimate);
        Builder builder = new(request, layout, arch, blocks, textureTable, climateBase);
        foreach (MapsDungeonBlock blockReference in layout.Blocks
            .OrderBy(block => block.X).ThenBy(block => block.Z).ThenBy(block => block.SourceName, StringComparer.Ordinal))
        {
            builder.AddBlock(blockReference);
        }

        return builder.Build();
    }

    private static void EnforceSourceQuotas(DungeonLogicalSourceSet sources, DungeonNormalizationQuotas quotas)
    {
        if (sources.Sources.Count > quotas.MaximumSources)
        {
            throw new InvalidOperationException($"Dungeon source set contains {sources.Sources.Count} sources, above the configured quota {quotas.MaximumSources}.");
        }

        long total = 0;
        foreach (DungeonLogicalSource source in sources.Sources)
        {
            total = checked(total + source.Bytes.Length);
            if (total > quotas.MaximumSourceBytes)
            {
                throw new InvalidOperationException($"Dungeon source bytes exceed the configured quota {quotas.MaximumSourceBytes}.");
            }
        }
    }

    private static ushort ClimateBase(byte worldClimate) => worldClimate switch
    {
        223 or 227 or 228 => 400,
        224 or 225 or 229 => 0,
        226 => 100,
        _ => 300,
    };

    private sealed class Builder
    {
        private readonly DungeonNormalizationRequest request;
        private readonly MapsDungeonLayout layout;
        private readonly BsaArchive arch;
        private readonly BsaArchive blocks;
        private readonly ushort[] textureTable;
        private readonly ushort climateBase;
        private readonly Dictionary<(ushort Archive, ushort Record), TextureInfo> textures = [];
        private readonly Dictionary<(ushort Archive, ushort Record, bool ParticipatesInCollision, string? DoorId), GeometryBuilder> geometry = [];
        private readonly List<NormalizedLightPlacement> lights = [];
        private readonly List<NormalizedBillboardPlacement> billboards = [];
        private readonly List<NormalizedActorPlacement> actors = [];
        private readonly List<NormalizedTreasurePlacement> treasures = [];
        private readonly List<DoorDraft> doorDrafts = [];
        private readonly List<DungeonRecordProvenance> provenance = [];
        private NormalizedMarker? startMarker;
        private NormalizedMarker? enterMarker;
        private int models;
        private int placements;
        private int vertices;
        private int triangles;

        public Builder(DungeonNormalizationRequest request, MapsDungeonLayout layout, BsaArchive arch, BsaArchive blocks, ushort[] textureTable, ushort climateBase)
        {
            this.request = request;
            this.layout = layout;
            this.arch = arch;
            this.blocks = blocks;
            this.textureTable = textureTable;
            this.climateBase = climateBase;
        }

        public void AddBlock(MapsDungeonBlock reference)
        {
            if (!blocks.TryGetByName(reference.SourceName, out BsaRecord? record) || record is null)
            {
                throw new InvalidOperationException($"BLOCKS.BSA is missing requested block '{reference.SourceName}'.");
            }

            RdbBlockSource block = RdbDecoder.Decode(blocks.GetPayload(record).Span, blocks.Source);
            string blockId = $"block/{Slug(reference.SourceName)}/{reference.X}/{reference.Z}";
            AddProvenance(blockId, "rdb-block", blocks.Source, record.Ordinal);
            Arena2ImportPoint origin = Arena2SourceTransform.ToBlockOrigin(reference);
            for (int index = 0; index < block.Lights.Count; index++)
            {
                RdbLightSource light = block.Lights[index];
                AddPlacement();
                string id = $"light/{Slug(reference.SourceName)}/{index}";
                AddProvenance(id, "rdb-light", blocks.Source, index);
                NormalizedVector3 position = ToRightHanded(Place(light.X, light.Y, light.Z, reference));
                lights.Add(new(id, position, ToMetres(light.Radius) * LightRangeMultiplier, 1F));
            }

            for (int index = 0; index < block.Flats.Count; index++)
            {
                RdbFlatSource flat = block.Flats[index];
                NormalizedVector3 position = ToRightHanded(Place(flat.X, flat.Y, flat.Z, reference));
                if (reference.IsStart && RdbSourceClassification.IsStartMarker(flat))
                {
                    startMarker ??= new("marker/start", position);
                    AddProvenance("marker/start", "rdb-start-marker", blocks.Source, index);
                    continue;
                }

                if (reference.IsStart && RdbSourceClassification.IsEnterMarker(flat))
                {
                    enterMarker ??= new("marker/enter", position);
                    AddProvenance("marker/enter", "rdb-enter-marker", blocks.Source, index);
                    continue;
                }

                if (RdbSourceClassification.IsRandomTreasureMarker(flat))
                {
                    AddPlacement();
                    string treasurePlacementId = $"treasure/{Slug(reference.SourceName)}/{index}";
                    string resourceId = $"treasure/dungeon-type-{layout.DungeonType}";
                    treasures.Add(new(treasurePlacementId, resourceId, position));
                    AddProvenance(treasurePlacementId, "rdb-treasure-marker", blocks.Source, index);
                    continue;
                }

                // Daggerfall's fixed-mobile meaning is carried only by the
                // editor flat marker (archive 199, record 16).  Its classic
                // source data has garbage high bits; mobile zero is valid and
                // 99 is the one reserved invalid value.  Do not classify an
                // ordinary billboard from an incidental faction low byte.
                byte mobileId = unchecked((byte)flat.FactionOrMobileId);
                if (RdbSourceClassification.IsFixedMobileMarker(flat)
                    && mobileId != 99
                    && MobileSourceMetadata.TryGet(new Arena2MobileId(mobileId), out Arena2MobileSource? mobile))
                {
                    AddPlacement();
                    string actorPlacementId = $"actor/{Slug(reference.SourceName)}/{index}";
                    string resourceId = $"actor/mobile-{mobile.Id.Value}";
                    actors.Add(new(actorPlacementId, resourceId, position));
                    AddProvenance(actorPlacementId, "rdb-mobile-placement", blocks.Source, index);
                    continue;
                }

                if (flat.TextureArchive == RdbSourceClassification.EditorFlatArchive)
                {
                    continue;
                }

                AddPlacement();
                (ushort archiveId, ushort recordId) = RemapTexture(flat.TextureArchive, flat.TextureRecord);
                TextureInfo texture = ResolveTexture(archiveId, recordId);
                string id = $"billboard/{Slug(reference.SourceName)}/{index}";
                billboards.Add(new(id, texture.SpriteId, position, new(ToMetres(texture.Width), ToMetres(texture.Height))));
                AddProvenance(id, "rdb-billboard", blocks.Source, index);
            }

            for (int index = 0; index < block.Models.Count; index++)
            {
                AddModel();
                RdbModelSource model = block.Models[index];
                string modelId = $"model/{Slug(reference.SourceName)}/{index}";
                AddProvenance(modelId, "rdb-model", blocks.Source, index);
                bool actionDoor = RdbSourceClassification.HasActionDoorTag(model);
                string? doorId = null;
                if (actionDoor)
                {
                    AddPlacement();
                    doorId = $"door/{Slug(reference.SourceName)}/{index}";
                    string doorResourceId = $"door/model-{Slug(model.ModelId)}";
                    Arena2EulerDegrees degrees = Arena2SourceTransform.ToEulerDegrees(model);
                    doorDrafts.Add(new(doorId, doorResourceId, ToRightHanded(Place(model.X, model.Y, model.Z, reference)), new(degrees.X, degrees.Y, degrees.Z)));
                    AddProvenance(doorId, "rdb-action-door", blocks.Source, index);
                }

                Arch3dMesh mesh = ResolveMesh(model.ModelId);
                Matrix3 rotation = Matrix3.ForModel(model);
                foreach (Arch3dPlane plane in mesh.Planes)
                {
                    if (plane.Points.Count < 3)
                    {
                        continue;
                    }

                    (ushort archiveId, ushort recordId) = RemapTexture(plane.TextureArchive, plane.TextureRecord);
                    TextureInfo texture = ResolveTexture(archiveId, recordId);
                    // Action-door model geometry remains in the visual mesh so
                    // an eventual ruleset can render its source state, but it
                    // must not become static collision before that policy
                    // exists.
                    GeometryBuilder group = Geometry(archiveId, recordId, texture.MaterialId, !actionDoor, actionDoor ? doorId : null);
                    List<NormalizedVector3> polygon = new(plane.Points.Count);
                    List<NormalizedVector2> uvs = new(plane.Points.Count);
                    foreach (Arch3dPoint point in plane.Points)
                    {
                        Arena2ImportPoint local = Arena2SourceTransform.ToImportPoint(point);
                        Arena2ImportPoint rotated = rotation.Transform(local);
                        Arena2ImportPoint placed = new(
                            rotated.XMetres + ToMetres(model.X) + origin.XMetres,
                            rotated.YMetres - ToMetres(model.Y) + origin.YMetres,
                            rotated.ZMetres + ToMetres(model.Z) + origin.ZMetres);
                        polygon.Add(ToRightHanded(placed));
                        Arena2TextureUv uv = Arena2SourceTransform.ToTextureUv(point, texture.Width, texture.Height);
                        uvs.Add(new(uv.U, uv.V));
                    }

                    NormalizedVector3 normal = Normal(polygon[0], polygon[1], polygon[2]);
                    group.AddPolygon(polygon, uvs, normal, AddVertices, AddTriangles);
                }
            }
        }

        public DungeonNormalizationResult Build()
        {
            if (geometry.Count == 0)
            {
                throw new InvalidOperationException("Dungeon normalization produced no static geometry.");
            }

            string locationSlug = Slug(layout.LocationName);
            string root = $"dungeon/{locationSlug}";
            string staticMeshArtifactId = $"artifact/{root}/static-mesh";
            string spatialArtifactId = $"artifact/{root}/collision-navigation";
            string resourceCatalogArtifactId = $"artifact/{root}/resource-catalog";
            string visualMeshAssetId = $"mesh/{locationSlug}";
            List<NormalizedMesh> meshes = [];
            List<NormalizedVector3> allVertices = [];
            Dictionary<string, List<string>> visualMeshIdsByDoor = new(StringComparer.Ordinal);
            foreach (((ushort archiveId, ushort recordId, bool participatesInCollision, string? doorId), GeometryBuilder group) in geometry
                .OrderBy(pair => pair.Key.Archive).ThenBy(pair => pair.Key.Record).ThenByDescending(pair => pair.Key.ParticipatesInCollision).ThenBy(pair => pair.Key.DoorId, StringComparer.Ordinal))
            {
                string participation = participatesInCollision ? "static" : "action-visual";
                string id = doorId is null
                    ? $"mesh/{locationSlug}/texture-{archiveId}-{recordId}/{participation}"
                    : $"mesh/{doorId}/texture-{archiveId}-{recordId}/{participation}";
                meshes.Add(group.ToMesh(id, staticMeshArtifactId));
                allVertices.AddRange(group.Vertices);
                if (doorId is not null)
                {
                    if (!visualMeshIdsByDoor.TryGetValue(doorId, out List<string>? doorMeshIds))
                    {
                        doorMeshIds = [];
                        visualMeshIdsByDoor.Add(doorId, doorMeshIds);
                    }

                    doorMeshIds.Add(id);
                }
            }

            NormalizedBounds bounds = Bounds(allVertices);
            NormalizedNavigationSurface navigation = Navigation(spatialArtifactId, meshes);
            List<NormalizedDoorPlacement> doors = doorDrafts
                .OrderBy(door => door.Id, StringComparer.Ordinal)
                .Select(door => new NormalizedDoorPlacement(
                    door.Id,
                    door.DoorResourceId,
                    visualMeshIdsByDoor.TryGetValue(door.Id, out List<string>? meshIds)
                        ? meshIds.OrderBy(meshId => meshId, StringComparer.Ordinal).ToArray()
                        : [],
                    door.Position,
                    door.RotationDegrees))
                .ToList();
            List<NormalizedResourceCatalogEntry> resources = ResourceCatalog(resourceCatalogArtifactId, doors);
            NormalizedWorld world = new(
                NormalizedWorld.CurrentSchemaVersion,
                visualMeshAssetId,
                meshes.Select(mesh => mesh.Id).ToArray(),
                navigation.Id,
                startMarker,
                enterMarker,
                lights,
                billboards,
                actors,
                treasures,
                doors);
            DungeonSpatialPublication spatialPublication = DungeonSpatialPublication.Create(
                staticMeshArtifactId,
                $"spatial/{locationSlug}/static-mesh.json",
                spatialArtifactId,
                $"spatial/{locationSlug}/collision-navigation.json",
                resourceCatalogArtifactId,
                $"resources/{locationSlug}/catalog.json",
                visualMeshAssetId,
                bounds,
                meshes,
                world,
                navigation,
                resources);
            NormalizedImportDocument document = new NormalizedImportDocument(
                NormalizedImportDocument.CurrentSchemaVersion,
                new ImportProvenance(ImportProvenance.CurrentSchemaVersion, ImporterId, ImporterVersion,
                    request.Sources.Sources.Select(source => new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, source.Label, ContentDigest.Compute(source.Bytes.Span), source.Bytes.Length, 1)).ToArray()),
                spatialPublication.ArtifactDescriptors,
                new NormalizedCoordinateConvention(NormalizedCoordinateConvention.CurrentSchemaVersion, NormalizedHandedness.Right, NormalizedVerticalAxis.PositiveY, 1F),
                bounds,
                meshes,
                navigation,
                world,
                resources).Canonicalize();
            DungeonNormalizationResult result = new(document, provenance.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(), spatialPublication);
            result.Validate();
            return result;
        }

        private static float ToMetres(int sourceUnits) => sourceUnits * SourceUnitMetres;

        private List<NormalizedResourceCatalogEntry> ResourceCatalog(string resourceCatalogArtifactId, IReadOnlyList<NormalizedDoorPlacement> doors)
        {
            List<NormalizedResourceCatalogEntry> resources = [];
            foreach (TextureInfo texture in textures.Values.OrderBy(value => value.Archive).ThenBy(value => value.Record))
            {
                resources.Add(new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, texture.TextureId, NormalizedResourceKind.Texture,
                    resourceCatalogArtifactId, [], []));
                resources.Add(new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, texture.MaterialId, NormalizedResourceKind.Material,
                    resourceCatalogArtifactId, [texture.TextureId], []));
                resources.Add(new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, texture.SpriteId, NormalizedResourceKind.Sprite,
                    resourceCatalogArtifactId, [texture.TextureId],
                    [new($"frame/texture-{texture.Archive}-{texture.Record}", 0, 0, 0, texture.Width, texture.Height, new(0.5F, 0F))]));
            }

            foreach (string actorId in actors.Select(actor => actor.ActorResourceId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                resources.Add(new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, actorId, NormalizedResourceKind.ActorDefinition, resourceCatalogArtifactId, [], []));
            }

            foreach (string treasureId in treasures.Select(treasure => treasure.TreasureResourceId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                resources.Add(new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, treasureId, NormalizedResourceKind.TreasureDefinition, resourceCatalogArtifactId, [], []));
            }

            foreach (string doorId in doors.Select(door => door.DoorResourceId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                resources.Add(new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, doorId, NormalizedResourceKind.DoorDefinition, resourceCatalogArtifactId, [], []));
            }

            if (resources.Count > request.Quotas.MaximumResources)
            {
                throw new InvalidOperationException($"Dungeon normalization produced {resources.Count} resources, above the configured quota {request.Quotas.MaximumResources}.");
            }

            return resources;
        }

        private NormalizedNavigationSurface Navigation(string artifactId, IReadOnlyList<NormalizedMesh> meshes)
        {
            return OfflineNavigationDeriver.Derive($"navigation/{Slug(layout.LocationName)}", artifactId, meshes, request.Navigation);
        }

        private GeometryBuilder Geometry(ushort archiveId, ushort recordId, string materialId, bool participatesInCollision, string? doorId)
        {
            (ushort Archive, ushort Record, bool ParticipatesInCollision, string? DoorId) key = (archiveId, recordId, participatesInCollision, doorId);
            if (!geometry.TryGetValue(key, out GeometryBuilder? group))
            {
                group = new GeometryBuilder(materialId, participatesInCollision);
                geometry.Add(key, group);
            }

            return group;
        }

        private TextureInfo ResolveTexture(ushort archiveId, ushort recordId)
        {
            if (textures.TryGetValue((archiveId, recordId), out TextureInfo? existing))
            {
                return existing;
            }

            string leaf = $"TEXTURE.{archiveId:000}";
            DungeonLogicalSource source = request.Sources.Require(leaf);
            TextureArchive archive = TextureArchive.Parse(source.Bytes.Span, source.Label);
            TextureRecordInfo info = archive.GetRecordInfo(recordId);
            IndexedTextureFrame frame = archive.DecodeFrame(recordId, 0);
            if (info.Width <= 0 || info.Height <= 0 || frame.Width != info.Width || frame.Height != info.Height)
            {
                throw new InvalidOperationException($"Texture '{leaf}' record {recordId} has an invalid decoded extent.");
            }

            TextureInfo texture = new(archiveId, recordId, info.Width, info.Height,
                $"texture/{archiveId}-{recordId}", $"material/texture-{archiveId}-{recordId}", $"sprite/texture-{archiveId}-{recordId}");
            textures.Add((archiveId, recordId), texture);
            return texture;
        }

        private Arch3dMesh ResolveMesh(string sourceModelId)
        {
            if (!uint.TryParse(sourceModelId, out uint recordId) || !arch.TryGetByNumericId(recordId, out BsaRecord? record) || record is null)
            {
                throw new InvalidOperationException($"ARCH3D.BSA is missing numeric model '{sourceModelId}'.");
            }

            return Arch3dDecoder.Decode(arch.GetPayload(record).Span, arch.Source, recordId);
        }

        private (ushort Archive, ushort Record) RemapTexture(ushort archiveId, ushort recordId) =>
            (DungeonTextureTableTransform.RemapArchive(archiveId, textureTable, climateBase), recordId);

        private Arena2ImportPoint Place(int x, int y, int z, MapsDungeonBlock reference) =>
            Arena2SourceTransform.PlaceInBlock(Arena2SourceTransform.ToImportPoint(x, y, z), reference);

        private void AddModel()
        {
            models++;
            if (models > request.Quotas.MaximumModels)
            {
                throw new InvalidOperationException($"Dungeon model count exceeds the configured quota {request.Quotas.MaximumModels}.");
            }
        }

        private void AddPlacement()
        {
            placements++;
            if (placements > request.Quotas.MaximumPlacements)
            {
                throw new InvalidOperationException($"Dungeon placement count exceeds the configured quota {request.Quotas.MaximumPlacements}.");
            }
        }

        private void AddVertices(int count)
        {
            vertices = checked(vertices + count);
            if (vertices > request.Quotas.MaximumVertices)
            {
                throw new InvalidOperationException($"Dungeon vertex count exceeds the configured quota {request.Quotas.MaximumVertices}.");
            }
        }

        private void AddTriangles(int count)
        {
            triangles = checked(triangles + count);
            if (triangles > request.Quotas.MaximumTriangles)
            {
                throw new InvalidOperationException($"Dungeon triangle count exceeds the configured quota {request.Quotas.MaximumTriangles}.");
            }
        }

        private void AddProvenance(string id, string kind, string sourceLabel, int ordinal)
        {
            if (provenance.Any(value => StringComparer.Ordinal.Equals(value.Id, id)))
            {
                return;
            }

            provenance.Add(new(id, kind, sourceLabel, ordinal));
        }

    }

    private sealed class GeometryBuilder(string materialId, bool participatesInCollision)
    {
        private readonly List<NormalizedVector3> vertices = [];
        private readonly List<NormalizedVector3> normals = [];
        private readonly List<NormalizedVector2> textureCoordinates = [];
        private readonly List<NormalizedTriangle> triangles = [];

        public IReadOnlyList<NormalizedVector3> Vertices => vertices;

        public void AddPolygon(IReadOnlyList<NormalizedVector3> polygon, IReadOnlyList<NormalizedVector2> uvs, NormalizedVector3 normal, Action<int> addVertices, Action<int> addTriangles)
        {
            int first = vertices.Count;
            addVertices(polygon.Count);
            addTriangles(polygon.Count - 2);
            vertices.AddRange(polygon);
            normals.AddRange(Enumerable.Repeat(normal, polygon.Count));
            textureCoordinates.AddRange(uvs);
            for (int index = 1; index < polygon.Count - 1; index++)
            {
                triangles.Add(new(first, first + index, first + index + 1));
            }
        }

        public NormalizedMesh ToMesh(string id, string artifactId) => new(
            NormalizedMesh.CurrentSchemaVersion, id, artifactId, vertices, normals, textureCoordinates, triangles,
            [new NormalizedMaterialGroup(materialId, 0, triangles.Count, participatesInCollision)]);
    }

    private sealed record TextureInfo(ushort Archive, ushort Record, int Width, int Height, string TextureId, string MaterialId, string SpriteId);

    private sealed record DoorDraft(string Id, string DoorResourceId, NormalizedVector3 Position, NormalizedVector3 RotationDegrees);

    private readonly record struct Matrix3(float M11, float M12, float M13, float M21, float M22, float M23, float M31, float M32, float M33)
    {
        public static Matrix3 ForModel(RdbModelSource model)
        {
            Arena2EulerDegrees degrees = Arena2SourceTransform.ToEulerDegrees(model);
            return RotationZ(degrees.Z) * RotationX(degrees.X) * RotationY(degrees.Y);
        }

        public Arena2ImportPoint Transform(Arena2ImportPoint value) => new(
            (M11 * value.XMetres) + (M12 * value.YMetres) + (M13 * value.ZMetres),
            (M21 * value.XMetres) + (M22 * value.YMetres) + (M23 * value.ZMetres),
            (M31 * value.XMetres) + (M32 * value.YMetres) + (M33 * value.ZMetres));

        public static Matrix3 operator *(Matrix3 left, Matrix3 right) => new(
            (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31), (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32), (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
            (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31), (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32), (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
            (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31), (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32), (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));

        private static Matrix3 RotationX(float degrees)
        {
            (float sin, float cos) = MathF.SinCos(DegreesToRadians(degrees));
            return new(1F, 0F, 0F, 0F, cos, -sin, 0F, sin, cos);
        }

        private static Matrix3 RotationY(float degrees)
        {
            (float sin, float cos) = MathF.SinCos(DegreesToRadians(degrees));
            return new(cos, 0F, sin, 0F, 1F, 0F, -sin, 0F, cos);
        }

        private static Matrix3 RotationZ(float degrees)
        {
            (float sin, float cos) = MathF.SinCos(DegreesToRadians(degrees));
            return new(cos, -sin, 0F, sin, cos, 0F, 0F, 0F, 1F);
        }

        private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180F);
    }

    private static NormalizedVector3 ToRightHanded(Arena2ImportPoint point) => new(point.XMetres, point.YMetres, -point.ZMetres);

    private static NormalizedVector3 Normal(NormalizedVector3 first, NormalizedVector3 second, NormalizedVector3 third)
    {
        float ax = second.X - first.X;
        float ay = second.Y - first.Y;
        float az = second.Z - first.Z;
        float bx = third.X - first.X;
        float by = third.Y - first.Y;
        float bz = third.Z - first.Z;
        float x = (ay * bz) - (az * by);
        float y = (az * bx) - (ax * bz);
        float z = (ax * by) - (ay * bx);
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        return length > 1E-12F ? new(x / length, y / length, z / length) : new(0F, 1F, 0F);
    }

    private static NormalizedBounds Bounds(IReadOnlyList<NormalizedVector3> vertices)
    {
        if (vertices.Count == 0)
        {
            throw new InvalidOperationException("Dungeon geometry has no vertices.");
        }

        float minX = vertices.Min(vertex => vertex.X);
        float minY = vertices.Min(vertex => vertex.Y);
        float minZ = vertices.Min(vertex => vertex.Z);
        float maxX = vertices.Max(vertex => vertex.X);
        float maxY = vertices.Max(vertex => vertex.Y);
        float maxZ = vertices.Max(vertex => vertex.Z);
        return new(NormalizedBounds.CurrentSchemaVersion, new(minX, minY, minZ), new(maxX, maxY, maxZ));
    }

    private static string Slug(string value)
    {
        StringBuilder result = new(value.Length);
        foreach (char character in value)
        {
            result.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        string slug = result.ToString().Trim('-');
        return slug.Length == 0 ? "source" : slug;
    }
}
