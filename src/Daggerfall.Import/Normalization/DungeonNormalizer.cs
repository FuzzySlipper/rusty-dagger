using System.Globalization;
using System.Diagnostics.CodeAnalysis;
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
            : throw new MissingArena2SourceException(leafName, $"Dungeon normalization requires logical source '{leafName}'.");
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
    DungeonSpatialPublication SpatialPublication,
    IReadOnlyList<string> ReferencedMeshIds,
    IReadOnlyList<GeometryUnresolvedMeshReference> UnresolvedMeshReferences)
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
        NormalizedImportDocument.ValidateUnique(ReferencedMeshIds, meshId => meshId, "referenced mesh number");
        foreach (string meshId in ReferencedMeshIds)
        {
            if (!uint.TryParse(meshId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidOperationException($"The normalized pack references the mesh '{meshId}', which is not a mesh number.");
            }
        }

        // A reference the archive cannot serve is a fact about the pack: it named the mesh, and the pack
        // has to say so rather than quietly carrying the placement without its geometry.
        NormalizedImportDocument.ValidateUnique(UnresolvedMeshReferences, reference => reference.MeshId, "unresolved mesh reference");
        foreach (GeometryUnresolvedMeshReference reference in UnresolvedMeshReferences)
        {
            if (!ReferencedMeshIds.Contains(reference.MeshId, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"The normalized pack reports mesh '{reference.MeshId}' unresolved without referencing it.");
            }

            if (string.IsNullOrWhiteSpace(reference.Reason))
            {
                throw new InvalidOperationException($"The normalized pack reports mesh '{reference.MeshId}' unresolved without a reason.");
            }
        }
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
        private readonly SortedSet<string> referencedMeshIds = new(StringComparer.Ordinal);
        private readonly List<GeometryUnresolvedMeshReference> unresolvedMeshReferences = [];
        private readonly HashSet<string> unresolvedMeshIds = new(StringComparer.Ordinal);
        private readonly Dictionary<GeometryGroupKey, GeometryBuilder> geometry = [];
        private readonly List<GeometryPlacementDraft> geometryPlacements = [];
        private readonly List<ActionModelDraft> actionModelDrafts = [];
        private readonly List<NormalizedVector3> worldBoundsVertices = [];
        private readonly List<NormalizedLightPlacement> lights = [];
        private readonly List<NormalizedBillboardPlacement> billboards = [];
        private readonly List<NormalizedActorPlacement> actors = [];
        private readonly List<NormalizedTreasurePlacement> treasures = [];
        private readonly List<NormalizedDungeonAction> actions = [];
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
            string blockPlacementId = $"{Slug(reference.SourceName)}/{reference.X}/{reference.Z}";
            string blockId = $"block/{blockPlacementId}";
            AddProvenance(blockId, "rdb-block", blocks.Source, record.Ordinal);
            Arena2ImportPoint origin = Arena2SourceTransform.ToBlockOrigin(reference);
            for (int index = 0; index < block.Lights.Count; index++)
            {
                RdbLightSource light = block.Lights[index];
                AddPlacement();
                string id = $"light/{blockPlacementId}/{index}";
                AddProvenance(id, "rdb-light", blocks.Source, index);
                NormalizedVector3 position = MeshGeometry.ToRightHanded(Place(light.X, light.Y, light.Z, reference));
                lights.Add(new(id, position, ToMetres(light.Radius) * LightRangeMultiplier, 1F));
            }

            for (int index = 0; index < block.Flats.Count; index++)
            {
                RdbFlatSource flat = block.Flats[index];
                NormalizedVector3 position = MeshGeometry.ToRightHanded(Place(flat.X, flat.Y, flat.Z, reference));
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
                    string treasurePlacementId = $"treasure/{blockPlacementId}/{index}";
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
                    string actorPlacementId = $"actor/{blockPlacementId}/{index}";
                    string resourceId = $"actor/mobile-{mobile.Id.Value}";
                    actors.Add(new(actorPlacementId, resourceId, position));
                    AddProvenance(actorPlacementId, "rdb-mobile-placement", blocks.Source, index);
                    continue;
                }

                if (flat.TextureArchive == RdbSourceClassification.EditorFlatArchive)
                {
                    continue;
                }

                (ushort archiveId, ushort recordId) = RemapTexture(flat.TextureArchive, flat.TextureRecord);
                TextureInfo texture = ResolveTexture(archiveId, recordId);
                string lightId = $"light-flat/{blockPlacementId}/{index}";
                if (DaggerfallInteriorLightFacts.TryProject(lightId, flat, position, ToMetres(texture.Height), out NormalizedLightPlacement light))
                {
                    AddPlacement();
                    lights.Add(light);
                    AddProvenance(lightId, "rdb-interior-light-flat", blocks.Source, index);
                }

                AddPlacement();
                string id = $"billboard/{blockPlacementId}/{index}";
                billboards.Add(new(id, texture.SpriteId, position, new(ToMetres(texture.Width), ToMetres(texture.Height))));
                AddProvenance(id, "rdb-billboard", blocks.Source, index);
            }

            for (int index = 0; index < block.Models.Count; index++)
            {
                AddModel();
                RdbModelSource model = block.Models[index];
                string modelId = $"model/{blockPlacementId}/{index}";
                AddProvenance(modelId, "rdb-model", blocks.Source, index);
                bool ordinaryActionDoor = RdbSourceClassification.HasActionDoorTag(model);
                bool specialDoorAction = RdbSourceClassification.HasSpecialDoorAction(model);
                bool actionDoor = ordinaryActionDoor || specialDoorAction;
                bool actionModel = model.Action is not null && model.ObjectOffset > 0;
                string? actionId = actionModel ? $"action/{blockPlacementId}/model-{index}" : null;
                string? doorId = null;
                if (actionDoor)
                {
                    AddPlacement();
                    doorId = $"door/{blockPlacementId}/{index}";
                    string doorResourceId = $"door/model-{Slug(model.ModelId)}";
                    Arena2EulerDegrees degrees = Arena2SourceTransform.ToEulerDegrees(model);
                    doorDrafts.Add(new(doorId, doorResourceId, MeshGeometry.ToRightHanded(Place(model.X, model.Y, model.Z, reference)), new(degrees.X, degrees.Y, degrees.Z),
                        specialDoorAction ? "special" : "normal",
                        ordinaryActionDoor ? StartingLockValue(model.TriggerFlagStartingLock, blocks.Source, index) : 0,
                        model.Action is { } action ? new NormalizedDoorAction(action.Axis, action.Duration, action.Magnitude, action.NextObjectOffset, action.Flags) : null));
                    AddProvenance(doorId, "rdb-action-door", blocks.Source, index);
                }

                referencedMeshIds.Add(model.ModelId);
                if (!TryResolveMesh(model.ModelId, out Arch3dMesh? mesh, out string reason))
                {
                    // A placement whose mesh the archive cannot serve keeps the fact that it named one: the
                    // reference is reported unresolved and no geometry stands in for what is not there.
                    AddUnresolvedMesh(model.ModelId, reason: reason);
                    continue;
                }

                if (!mesh.Planes.Any(plane => plane.Points.Count >= 3))
                {
                    // The record decodes but declares nothing drawable, which is the same edge the geometry
                    // publication refuses: reporting it here keeps one definition of an unserved mesh.
                    AddUnresolvedMesh(model.ModelId, reason: $"ARCH3D.BSA model '{model.ModelId}' declares no drawable plane");
                    continue;
                }

                Matrix3 rotation = Matrix3.ForModel(model);
                HashSet<GeometryGroupKey> placementMeshKeys = [];
                List<NormalizedVector3> placementVertices = [];
                List<NormalizedVector3> localModelVertices = [];
                foreach (Arch3dPlane plane in mesh.Planes)
                {
                    if (plane.Points.Count < 3)
                    {
                        continue;
                    }

                    (ushort archiveId, ushort recordId) = RemapTexture(plane.TextureArchive, plane.TextureRecord);
                    TextureInfo texture = ResolveTexture(archiveId, recordId);
                    // Action-bearing models need one local mesh assembly per
                    // source placement. Their current pose is a separate
                    // Engine Transform, so neither render nor collision data
                    // is baked into the immutable world mesh.
                    GeometryGroupKey geometryKey = new(
                        archiveId,
                        recordId,
                        // Collision participation is independent of static placement. Movable
                        // action and door models keep their source collision triangles so the
                        // runtime can admit them under their current Engine transforms.
                        true,
                        actionDoor ? doorId : null,
                        actionId);
                    GeometryBuilder group = Geometry(geometryKey, texture.MaterialId);
                    List<NormalizedVector3> polygon = new(plane.Points.Count);
                    List<NormalizedVector3> worldPolygon = new(plane.Points.Count);
                    List<NormalizedVector2> uvs = new(plane.Points.Count);
                    foreach (Arch3dPoint point in plane.Points)
                    {
                        Arena2ImportPoint local = Arena2SourceTransform.ToImportPoint(point);
                        Arena2ImportPoint rotated = rotation.Transform(local);
                        Arena2ImportPoint placed = new(
                            rotated.XMetres + ToMetres(model.X) + origin.XMetres,
                            rotated.YMetres - ToMetres(model.Y) + origin.YMetres,
                            rotated.ZMetres + ToMetres(model.Z) + origin.ZMetres);
                        NormalizedVector3 worldPoint = MeshGeometry.ToRightHanded(placed);
                        worldPolygon.Add(worldPoint);
                        polygon.Add(actionModel
                            ? MeshGeometry.ToRightHanded(local)
                            : worldPoint);
                        Arena2TextureUv uv = Arena2SourceTransform.ToTextureUv(point, texture.Width, texture.Height);
                        uvs.Add(new(uv.U, uv.V));
                    }

                    NormalizedVector3 normal = MeshGeometry.Normal(polygon);
                    group.AddPolygon(polygon, uvs, normal, AddVertices, AddTriangles);
                    placementMeshKeys.Add(geometryKey);
                    placementVertices.AddRange(worldPolygon);
                    worldBoundsVertices.AddRange(worldPolygon);
                    if (actionModel)
                        localModelVertices.AddRange(polygon);
                }

                if (placementVertices.Count > 0 && !actionModel && !actionDoor)
                {
                    geometryPlacements.Add(new GeometryPlacementDraft(modelId, null, placementMeshKeys, placementVertices));
                }

                if (actionModel && localModelVertices.Count > 0)
                {
                    Arena2EulerDegrees degrees = Arena2SourceTransform.ToEulerDegrees(model);
                    actionModelDrafts.Add(new(
                        actionId!,
                        model.ModelId,
                        model.Description,
                        model.ModelIndex,
                        model.SoundIndex,
                        doorId,
                        MeshGeometry.ToRightHanded(Place(model.X, model.Y, model.Z, reference)),
                        new(degrees.X, degrees.Y, degrees.Z),
                        placementMeshKeys,
                        localModelVertices));
                }
            }

            AddActions(block, blockPlacementId, reference);
        }

        public DungeonNormalizationResult Build()
        {
            if (geometry.Count == 0 || worldBoundsVertices.Count == 0)
            {
                throw new InvalidOperationException("Dungeon normalization produced no drawable model geometry.");
            }

            string locationSlug = Slug(layout.LocationName);
            string root = $"dungeon/{locationSlug}";
            string staticMeshArtifactId = $"artifact/{root}/static-mesh";
            string spatialArtifactId = $"artifact/{root}/collision-navigation";
            string resourceCatalogArtifactId = $"artifact/{root}/resource-catalog";
            string visualMeshAssetId = $"mesh/{locationSlug}";
            List<NormalizedMesh> meshes = [];
            Dictionary<GeometryGroupKey, string> meshIdsByGeometry = [];
            Dictionary<string, List<string>> visualMeshIdsByDoor = new(StringComparer.Ordinal);
            foreach ((GeometryGroupKey key, GeometryBuilder group) in geometry
                .OrderBy(pair => pair.Key.Archive).ThenBy(pair => pair.Key.Record)
                .ThenByDescending(pair => pair.Key.ParticipatesInCollision)
                .ThenBy(pair => pair.Key.DoorId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.ActionId, StringComparer.Ordinal))
            {
                string id = MeshId(locationSlug, key);
                string artifactId = key.ActionId is { } actionId
                    ? ActionModelArtifactId(staticMeshArtifactId, actionId)
                    : staticMeshArtifactId;
                meshes.Add(group.ToMesh(id, artifactId));
                meshIdsByGeometry.Add(key, id);
                if (key.DoorId is not null)
                {
                    if (!visualMeshIdsByDoor.TryGetValue(key.DoorId, out List<string>? doorMeshIds))
                    {
                        doorMeshIds = [];
                        visualMeshIdsByDoor.Add(key.DoorId, doorMeshIds);
                    }

                    doorMeshIds.Add(id);
                }
            }

            NormalizedBounds bounds = MeshGeometry.Bounds(worldBoundsVertices);
            HashSet<string> movableMeshIds = geometry.Keys
                .Where(key => key.ActionId is not null || key.DoorId is not null)
                .Select(key => meshIdsByGeometry[key])
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> staticMeshIds = meshes
                .Where(mesh => mesh.MaterialGroups.Any(group => group.ParticipatesInCollision)
                    && !movableMeshIds.Contains(mesh.Id))
                .Select(mesh => mesh.Id)
                .ToHashSet(StringComparer.Ordinal);
            NormalizedMesh[] staticGeometry = meshes.Where(mesh => staticMeshIds.Contains(mesh.Id)).ToArray();
            NormalizedNavigationSurface navigation = Navigation(spatialArtifactId, staticGeometry);
            List<NormalizedDoorPlacement> doors = doorDrafts
                .OrderBy(door => door.Id, StringComparer.Ordinal)
                .Select(door => new NormalizedDoorPlacement(
                    door.Id,
                    door.DoorResourceId,
                    visualMeshIdsByDoor.TryGetValue(door.Id, out List<string>? meshIds)
                        ? meshIds.OrderBy(meshId => meshId, StringComparer.Ordinal).ToArray()
                        : [],
                    door.Position,
                    door.RotationDegrees,
                    Kind: door.Kind,
                    StartingLockValue: door.StartingLockValue,
                    Action: door.Action))
                .ToList();
            List<NormalizedResourceCatalogEntry> resources = ResourceCatalog(resourceCatalogArtifactId, doors);
            NormalizedGeometryPlacement[] normalizedGeometryPlacements = geometryPlacements
                .Select(placement => new NormalizedGeometryPlacement(
                    placement.Id,
                    MeshGeometry.Bounds(placement.Vertices),
                    placement.MeshKeys.Select(key => meshIdsByGeometry[key]).ToArray(),
                    placement.DoorId)
                {
                    SamplePoints = SelectSurfaceSamples(placement.Vertices),
                })
                .OrderBy(placement => placement.Id, StringComparer.Ordinal)
                .ToArray();
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
                doors)
            {
                Actions = actions,
                GeometryPlacements = normalizedGeometryPlacements,
                StaticMeshIds = staticGeometry.Select(mesh => mesh.Id).ToArray(),
                ActionModels = actionModelDrafts
                    .Select(draft => new NormalizedActionModelPlacement(
                        draft.ActionId,
                        draft.ModelId,
                        draft.Description,
                        draft.ModelIndex,
                        draft.RawIndex,
                        draft.DoorId,
                        draft.Position,
                        draft.RotationDegrees,
                        MeshGeometry.Bounds(draft.LocalVertices),
                        draft.MeshKeys.Select(key => meshIdsByGeometry[key]).ToArray(),
                        ActionModelArtifactId(staticMeshArtifactId, draft.ActionId)))
                    .ToArray(),
            };
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
            DungeonNormalizationResult result = new(
                document,
                provenance.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(),
                spatialPublication,
                [.. referencedMeshIds],
                [.. unresolvedMeshReferences.OrderBy(reference => reference.MeshId, StringComparer.Ordinal)]);
            result.Validate();
            return result;
        }

        /// <summary>
        /// Records one unresolved mesh number. A pack that places the same missing mesh twice names one
        /// number, so it reports one reference: the number is what the archive could not serve.
        /// </summary>
        private void AddUnresolvedMesh(string meshId, string reason)
        {
            if (unresolvedMeshIds.Add(meshId))
            {
                unresolvedMeshReferences.Add(new GeometryUnresolvedMeshReference(meshId, reason));
            }
        }

        private static float ToMetres(int sourceUnits) => sourceUnits * SourceUnitMetres;

        private void AddActions(RdbBlockSource block, string blockPlacementId, MapsDungeonBlock reference)
        {
            Dictionary<int, string> actionIdsByOffset = new();
            Dictionary<int, string?> doorIdsByOffset = new();
            foreach ((RdbModelSource model, int index) in block.Models.Select((model, index) => (model, index)))
            {
                if (model.Action is null || model.ObjectOffset <= 0)
                    continue;

                string id = $"action/{blockPlacementId}/model-{index}";
                if (!actionIdsByOffset.TryAdd(model.ObjectOffset, id))
                    throw new InvalidOperationException($"RDB block '{blockPlacementId}' repeats action object offset {model.ObjectOffset}.");

                doorIdsByOffset[model.ObjectOffset] = RdbSourceClassification.HasActionDoorTag(model)
                    || RdbSourceClassification.HasSpecialDoorAction(model)
                    ? $"door/{blockPlacementId}/{index}"
                    : null;
            }

            foreach ((RdbFlatSource flat, int index) in block.Flats.Select((flat, index) => (flat, index)))
            {
                // Offset zero is Arena2's absolute null-link sentinel. Preserve that authored node
                // even when all other action fields are zero; only the negative no-object sentinel
                // can prove that a flat carries no action record at all.
                if (flat.ObjectOffset <= 0 || flat.Action == 0 && flat.Flags == 0 && flat.NextObjectOffset < 0)
                    continue;

                string id = $"action/{blockPlacementId}/flat-{index}";
                if (!actionIdsByOffset.TryAdd(flat.ObjectOffset, id))
                    throw new InvalidOperationException($"RDB block '{blockPlacementId}' repeats action object offset {flat.ObjectOffset}.");
                doorIdsByOffset[flat.ObjectOffset] = null;
            }

            foreach ((RdbModelSource model, int index) in block.Models.Select((model, index) => (model, index)))
            {
                if (model.Action is not { } action || model.ObjectOffset <= 0)
                    continue;

                string id = actionIdsByOffset[model.ObjectOffset];
                actions.Add(new(
                    id,
                    model.ObjectOffset,
                    model.TriggerFlagStartingLock,
                    action.Flags,
                    action.Axis,
                    action.Duration,
                    action.Magnitude,
                    action.NextObjectOffset,
                    action.NextObjectOffset > 0 && actionIdsByOffset.TryGetValue(action.NextObjectOffset, out string? next) ? next : null,
                    doorIdsByOffset[model.ObjectOffset],
                    IsFlat: false,
                    SoundIndex: model.SoundIndex,
                    Position: null,
                    RawIndex: model.SoundIndex));
                AddProvenance(id, "rdb-action-model", blocks.Source, model.ObjectOffset);
            }

            foreach ((RdbFlatSource flat, int index) in block.Flats.Select((flat, index) => (flat, index)))
            {
                if (flat.ObjectOffset <= 0 || !actionIdsByOffset.TryGetValue(flat.ObjectOffset, out string? id))
                    continue;

                actions.Add(new(
                    id,
                    flat.ObjectOffset,
                    flat.Flags,
                    flat.Action,
                    flat.Magnitude,
                    0,
                    flat.Magnitude,
                    flat.NextObjectOffset,
                    flat.NextObjectOffset > 0 && actionIdsByOffset.TryGetValue(flat.NextObjectOffset, out string? next) ? next : null,
                    DoorId: null,
                    IsFlat: true,
                    SoundIndex: flat.SoundIndex,
                    Position: MeshGeometry.ToRightHanded(Place(flat.X, flat.Y, flat.Z, reference)),
                    RawIndex: flat.SoundIndex));
                AddProvenance(id, "rdb-action-flat", blocks.Source, flat.ObjectOffset);
            }
        }

        private static int StartingLockValue(uint sourceValue, string source, int modelIndex)
        {
            ReadOnlySpan<int> values = [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 25, 30, 50, 128, 255];
            uint selector = sourceValue >> 4;
            if (selector >= (uint)values.Length)
                throw new Arena2FormatException(source, modelIndex, $"RDB action-door lock selector {selector} is outside the classic lock table.");
            return values[(int)selector];
        }

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

        private GeometryBuilder Geometry(GeometryGroupKey key, string materialId)
        {
            if (!geometry.TryGetValue(key, out GeometryBuilder? group))
            {
                group = new GeometryBuilder(materialId, key.ParticipatesInCollision);
                geometry.Add(key, group);
            }

            return group;
        }

        private static string MeshId(string locationSlug, GeometryGroupKey key)
        {
            string participation = key.ActionId is not null || key.DoorId is not null
                ? "action-visual"
                : "static";
            if (key.ActionId is { } actionId)
                return $"mesh/action/{Slug(actionId["action/".Length..])}/texture-{key.Archive}-{key.Record}/{participation}";
            return key.DoorId is null
                ? $"mesh/{locationSlug}/texture-{key.Archive}-{key.Record}/{participation}"
                : $"mesh/{key.DoorId}/texture-{key.Archive}-{key.Record}/{participation}";
        }

        private static string ActionModelArtifactId(string staticMeshArtifactId, string actionId) =>
            $"{staticMeshArtifactId}/action/{Slug(actionId["action/".Length..])}";

        private static NormalizedVector3[] SelectSurfaceSamples(IReadOnlyList<NormalizedVector3> placementVertices)
        {
            NormalizedVector3[] distinctVertices = placementVertices.Distinct().ToArray();
            if (distinctVertices.Length < 2)
            {
                throw new InvalidOperationException("A dungeon model placement must contain at least two distinct source vertices for map visibility samples.");
            }

            if (distinctVertices.Length <= 4)
            {
                return distinctVertices;
            }

            int last = distinctVertices.Length - 1;
            int[] indices = [0, last / 3, (last * 2) / 3, last];
            return indices.Select(index => distinctVertices[index]).Distinct().ToArray();
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

        /// <summary>
        /// Resolves one placement's mesh, reporting rather than throwing when the archive cannot serve it.
        /// </summary>
        private bool TryResolveMesh(string sourceModelId, [NotNullWhen(true)] out Arch3dMesh? mesh, out string reason)
        {
            mesh = null;
            reason = string.Empty;
            if (!uint.TryParse(sourceModelId, NumberStyles.None, CultureInfo.InvariantCulture, out uint recordId)
                || !arch.TryGetByNumericId(recordId, out BsaRecord? record)
                || record is null)
            {
                reason = $"ARCH3D.BSA carries no numeric model '{sourceModelId}'";
                return false;
            }

            try
            {
                mesh = Arch3dDecoder.Decode(arch.GetPayload(record).Span, arch.Source, recordId);
                return true;
            }
            catch (Arena2FormatException error)
            {
                reason = $"ARCH3D.BSA model '{sourceModelId}' at record {record.Ordinal} could not be decoded: {error.Message}";
                return false;
            }
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
            addVertices(polygon.Count);
            addTriangles(polygon.Count - 2);
            _ = MeshGeometry.AppendPolygon(vertices, normals, textureCoordinates, triangles, polygon, uvs, normal);
        }

        public NormalizedMesh ToMesh(string id, string artifactId) => new(
            NormalizedMesh.CurrentSchemaVersion, id, artifactId, vertices, normals, textureCoordinates, triangles,
            [new NormalizedMaterialGroup(materialId, 0, triangles.Count, participatesInCollision)]);
    }

    private sealed record TextureInfo(ushort Archive, ushort Record, int Width, int Height, string TextureId, string MaterialId, string SpriteId);

    private sealed record DoorDraft(string Id, string DoorResourceId, NormalizedVector3 Position, NormalizedVector3 RotationDegrees, string Kind, int StartingLockValue, NormalizedDoorAction? Action);

    private readonly record struct GeometryGroupKey(ushort Archive, ushort Record, bool ParticipatesInCollision, string? DoorId, string? ActionId);

    private sealed record GeometryPlacementDraft(string Id, string? DoorId, IReadOnlySet<GeometryGroupKey> MeshKeys, IReadOnlyList<NormalizedVector3> Vertices);

    private sealed record ActionModelDraft(
        string ActionId,
        string ModelId,
        string Description,
        ushort ModelIndex,
        byte RawIndex,
        string? DoorId,
        NormalizedVector3 Position,
        NormalizedVector3 RotationDegrees,
        IReadOnlySet<GeometryGroupKey> MeshKeys,
        IReadOnlyList<NormalizedVector3> LocalVertices);

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
