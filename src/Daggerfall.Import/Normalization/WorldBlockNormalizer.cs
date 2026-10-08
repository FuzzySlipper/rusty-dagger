using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>One block's published facts and its collision/navigation artifact, if it has static geometry.</summary>
public sealed record WorldBlockNormalization(DaggerfallWorldBlock Block, GeneratedSpatialArtifact? Spatial);

/// <summary>
/// Normalizes single RMB exteriors, RMB building interiors and RDB blocks in their own frames, from the same
/// block-level content (<see cref="RmbBlockContent"/>, <see cref="RdbBlockContent"/>) the site normalizers
/// place. A block's static meshes are placements of the product-wide meshes; its collision and navigation are
/// derived from those placements exactly as a site derives its own, in the block's frame.
/// </summary>
public sealed class WorldBlockNormalizer
{
    /// <summary>The navigation and collision identities a block's spatial artifact states.</summary>
    private const string Root = "world-blocks";

    private readonly DungeonLogicalSourceSet sources;
    private readonly BsaArchive blocks;
    private readonly BsaArchive arch;
    private readonly IReadOnlyDictionary<int, ClassicFaction> factions;
    private readonly NavigationDerivationConfig navigation;
    private readonly Dictionary<string, Arch3dMesh?> meshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Arch3dMesh> rmbMeshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> unresolved = new(StringComparer.Ordinal);
    private readonly Dictionary<(ushort Archive, ushort Record), (int Width, int Height)> textures = [];

    /// <param name="sources">BLOCKS.BSA, ARCH3D.BSA, FACTION.TXT and the texture leaves the blocks' billboards name.</param>
    /// <param name="navigation">The navigation derivation every site closure uses, so placed blocks share its grid.</param>
    public WorldBlockNormalizer(DungeonLogicalSourceSet sources, NavigationDerivationConfig? navigation = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        this.sources = sources;
        blocks = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, sources.Require("BLOCKS.BSA").Label);
        arch = BsaArchive.Parse(sources.Require("ARCH3D.BSA").Bytes.Span, sources.Require("ARCH3D.BSA").Label);
        factions = FactionReader.Read(Encoding.UTF8.GetString(sources.Require("FACTION.TXT").Bytes.Span), sources.Require("FACTION.TXT").Label)
            .ToDictionary(faction => faction.Id);
        this.navigation = navigation ?? NavigationDerivationConfig.ClassicDefault;
        this.navigation.Validate();
    }

    /// <summary>The block archive the blocks are read from.</summary>
    public BsaArchive Blocks => blocks;

    /// <summary>The publication key of an RMB exterior, an RMB interior and an RDB block.</summary>
    public static string RmbExteriorKey(string sourceKey) => $"rmb/{PublishedIds.Slug(sourceKey)}/exterior";

    public static string RmbInteriorKey(string sourceKey, int buildingIndex) => $"rmb/{PublishedIds.Slug(sourceKey)}/interior-{buildingIndex.ToString(CultureInfo.InvariantCulture)}";

    public static string RdbKey(string sourceKey) => $"rdb/{PublishedIds.Slug(sourceKey)}";

    /// <summary>Reads one RMB record's block-level content, for its exterior and the interiors it declares.</summary>
    internal RmbBlockContent ReadRmb(string sourceKey) => RmbBlockContent.Read(blocks, sourceKey);

    /// <summary>Normalizes one RMB block's exterior in its own frame.</summary>
    public WorldBlockNormalization RmbExterior(string sourceKey) => RmbExterior(ReadRmb(sourceKey));

    internal WorldBlockNormalization RmbExterior(RmbBlockContent content)
    {
        string key = RmbExteriorKey(content.SourceKey);
        string slug = PublishedIds.Slug(content.SourceKey);
        List<DaggerfallWorldBlockModel> models = [];
        List<DaggerfallWorldBlockDoor> doors = [];
        BlockGeometry geometry = new();
        int doorOrdinal = 0;
        foreach (RmbBlockModel model in content.ExteriorModels)
        {
            string id = model.BuildingIndex is int building
                ? $"model/{slug}/{building}/{model.Ordinal}"
                : $"model/{slug}/misc/{model.Ordinal}";
            Arch3dMesh mesh = RmbMesh(model);
            models.Add(RmbModel(id, model));
            foreach ((Arch3dPlane plane, int planeIndex) in mesh.Planes.Select((plane, index) => (plane, index)).Where(value => value.plane.Points.Count >= 3))
            {
                List<NormalizedVector3> polygon = [.. plane.Points.Select(point => MeshGeometry.ToRightHanded(model.Frame.Place(Arena2SourceTransform.ToImportPoint(point))))];
                if (!RmbBlockContent.IsBuildingDoor(plane.TextureArchive))
                {
                    geometry.AddStatic(polygon);
                    continue;
                }

                geometry.AddMovable(polygon);
                (NormalizedVector3 centre, NormalizedVector3 rotation, NormalizedBounds bounds) = RmbBlockContent.Door(plane, polygon);
                doors.Add(new($"door/{slug}-rmb/{doorOrdinal++}", id, "door/rmb-exterior", centre, rotation)
                {
                    Plane = planeIndex,
                    Texture = new(plane.TextureArchive, plane.TextureRecord),
                    StartingLockValue = model.BuildingIndex is not null ? model.StartingLockValue : 0,
                    CollisionBounds = bounds,
                    BuildingIndex = model.BuildingIndex,
                });
            }
        }

        foreach (RmbBlockGroundTile tile in content.Ground)
            geometry.AddStatic([.. tile.Corners.Select(MeshGeometry.ToRightHanded)]);

        HashSet<(int X, int Z)> clear = [.. content.ClearOutdoorCells];
        (NormalizedBounds? blockBounds, DaggerfallWorldBlockSpatial? spatial, GeneratedSpatialArtifact? artifact) =
            Spatial(key, geometry, cell => RmbBlockContent.IsClearOutdoorGroundCell(cell, navigation.CellSize, clear));
        DaggerfallWorldBlock block = new(key, DaggerfallWorldBlockKind.RmbExterior, content.SourceKey, Ordinal(content.SourceKey), null, blockBounds, spatial, models)
        {
            Doors = doors,
            StartMarker = FirstMarker(content.ExteriorFlats, RdbSourceClassification.StartMarkerRecord, "marker/start"),
            EnterMarker = FirstMarker(content.ExteriorFlats, RdbSourceClassification.EnterMarkerRecord, "marker/enter"),
            Buildings = [.. content.Summary.Buildings.Select(slot => new DaggerfallWorldBlockBuilding(
                slot.Index, slot.BuildingType, slot.FactionId, slot.Quality, slot.NameSeed, content.HasInterior(slot.Index)))],
            Ground = [.. content.Ground.Select(tile => new DaggerfallWorldBlockGroundTile(tile.Source.X, tile.Source.Y, tile.TextureRecord, tile.Source.Rotated, tile.Source.Flipped))],
            ClearGround = ClearGround(content.ClearOutdoorCells),
            Population = [.. content.ExteriorPeople.Select(person =>
            {
                string id = RmbBlockContent.PopulationId(content.SourceKey, person.BuildingIndex, person.PersonIndex);
                return new NormalizedPopulationPlacement(id, MeshGeometry.ToRightHanded(person.Point),
                    person.Person.TextureArchive, person.Person.TextureRecord, person.Person.FactionId, person.Person.Flags)
                {
                    NameSeed = RmbBlockContent.StablePopulationSeed(id),
                    SourceBuildingType = person.Slot.BuildingType,
                    SourceBuildingFactionId = person.Slot.FactionId,
                };
            })],
        };
        return new(block, artifact);
    }

    /// <summary>Normalizes one RMB building's inside half in the frame the donor builds it in.</summary>
    public WorldBlockNormalization RmbInterior(string sourceKey, int buildingIndex) => RmbInterior(ReadRmb(sourceKey), buildingIndex);

    internal WorldBlockNormalization RmbInterior(RmbBlockContent content, int buildingIndex)
    {
        string key = RmbInteriorKey(content.SourceKey, buildingIndex);
        IReadOnlyList<RmbBlockModel> interiorModels = content.InteriorModels(buildingIndex);
        RmbBuildingSlot slot = content.Summary.Buildings[buildingIndex];
        List<DaggerfallWorldBlockModel> models = [];
        List<NormalizedPropertyContainer> containers = [];
        BlockGeometry geometry = new();
        foreach (RmbBlockModel model in interiorModels)
        {
            Arch3dMesh mesh = RmbMesh(model);
            models.Add(RmbModel($"model/{model.Ordinal}", model));
            NormalizedPropertyContainer? container = RmbPropertyContainerFacts.Read(model.Frame.Model, model.Ordinal, slot.BuildingType);
            List<NormalizedVector3> interactionPoints = [];
            foreach (Arch3dPlane plane in mesh.Planes.Where(plane => plane.Points.Count >= 3))
            {
                List<NormalizedVector3> polygon = [.. plane.Points.Select(point => MeshGeometry.ToRightHanded(model.Frame.Place(Arena2SourceTransform.ToImportPoint(point))))];
                geometry.AddStatic(polygon);
                if (container is not null && RmbBlockContent.InteractionPoint(polygon, MeshGeometry.Normal(polygon)) is { } point)
                    interactionPoints.Add(point);
            }

            if (container is not null) containers.Add(container with { InteractionPoints = interactionPoints });
        }

        IReadOnlyList<RmbBlockFlat> flats = content.InteriorFlats(buildingIndex);
        (NormalizedBounds? blockBounds, DaggerfallWorldBlockSpatial? spatial, GeneratedSpatialArtifact? artifact) = Spatial(key, geometry, null);
        DaggerfallWorldBlock block = new(key, DaggerfallWorldBlockKind.RmbInterior, content.SourceKey, Ordinal(content.SourceKey), buildingIndex, blockBounds, spatial, models)
        {
            InteriorBuilding = new(slot.Index, slot.BuildingType, slot.FactionId, slot.Quality, slot.NameSeed, true),
            StartMarker = FirstMarker(flats, RdbSourceClassification.StartMarkerRecord, "marker/start"),
            EnterMarker = FirstMarker(flats, RdbSourceClassification.EnterMarkerRecord, "marker/enter"),
            QuestMarkers = [.. flats
                .Select(flat => (flat, Kind: QuestMarkerNormalization.KindOf(flat.TextureArchive, flat.TextureRecord)))
                .Where(value => value.Kind is not null)
                .Select(value => new DaggerfallWorldBlockQuestMarker($"quest/{value.flat.Ordinal}", value.Kind!.Value, MeshGeometry.ToRightHanded(value.flat.Point), value.flat.Ordinal))],
            StaticNpcs = [.. content.Placements.Buildings[buildingIndex].Interior.People.Select((person, ordinal) => new DaggerfallWorldBlockStaticNpc($"person/{ordinal}",
                MeshGeometry.ToRightHanded(Arena2SourceTransform.ToRmbImportPoint(person.X, person.Y, person.Z)),
                person.TextureArchive, person.TextureRecord, person.FactionId,
                RmbBlockContent.StaticNpcRace(person.FactionId, factions),
                (person.Flags & 32) != 0 ? "Female" : "Male",
                person.SourceOffset))],
            PropertyContainers = containers,
        };
        return new(block, artifact);
    }

    /// <summary>Normalizes one RDB block in its own frame, before any location's texture table or start block.</summary>
    public WorldBlockNormalization Rdb(string sourceKey)
    {
        RdbBlockContent content = RdbBlockContent.Read(blocks, sourceKey);
        string key = RdbKey(sourceKey);
        string scope = PublishedIds.Slug(sourceKey);
        Arena2ImportPoint origin = new(0F, 0F, 0F);
        List<DaggerfallWorldBlockModel> models = [];
        List<DaggerfallWorldBlockDoor> doors = [];
        BlockGeometry geometry = new();
        foreach (RdbBlockModel model in content.Models)
        {
            string id = $"model/{scope}/{model.Index}";
            string? doorId = model.ActionDoor ? $"door/{scope}/{model.Index}" : null;
            Arena2EulerDegrees degrees = model.RotationDegrees;
            Arch3dMesh? mesh = RdbMesh(model.Source.ModelId);
            models.Add(new(id, model.Source.ModelId, mesh is null ? null : MeshArtifactId(model.Source.ModelId),
                MeshGeometry.ToRightHanded(model.Point), new(degrees.X, degrees.Y, degrees.Z))
            {
                UnresolvedReason = mesh is null ? unresolved[model.Source.ModelId] : null,
                Action = model.ActionModel
                    ? new($"action/{scope}/model-{model.Index}", model.Source.Description, model.Source.ModelIndex, model.Source.SoundIndex)
                    : null,
                DoorId = doorId,
            });
            if (doorId is not null)
                doors.Add(new(doorId, id, $"door/model-{PublishedIds.Slug(model.Source.ModelId)}", MeshGeometry.ToRightHanded(model.Point), new(degrees.X, degrees.Y, degrees.Z))
                {
                    Kind = model.SpecialDoorAction ? "special" : "normal",
                    StartingLockValue = model.StartingLockValue,
                    Action = model.Source.Action is { } action ? new(action.Axis, action.Duration, action.Magnitude, action.NextObjectOffset, action.Flags) : null,
                });
            if (mesh is null) continue;
            bool movable = model.ActionModel || model.ActionDoor;
            foreach (Arch3dPlane plane in mesh.Planes.Where(plane => plane.Points.Count >= 3))
            {
                List<NormalizedVector3> polygon = [.. plane.Points.Select(point =>
                    MeshGeometry.ToRightHanded(model.Place(model.Rotate(Arena2SourceTransform.ToImportPoint(point)), origin)))];
                if (movable) geometry.AddMovable(polygon);
                else geometry.AddStatic(polygon);
            }
        }

        List<NormalizedLightPlacement> lights = [.. content.Lights.Select(light =>
            new NormalizedLightPlacement($"light/{scope}/{light.Index}", MeshGeometry.ToRightHanded(light.Point), light.Range, 1F, new(1F, 1F, 1F)))];
        List<NormalizedBillboardPlacement> billboards = [];
        foreach (RdbBlockFlat flat in content.Flats.Where(flat => flat.Kind == RdbFlatKind.Billboard))
        {
            NormalizedVector3 position = MeshGeometry.ToRightHanded(flat.Point);
            (int width, int height) = TextureExtent(flat.Source.TextureArchive, flat.Source.TextureRecord);
            if (DaggerfallInteriorLightFacts.TryProject($"light-flat/{scope}/{flat.Index}", flat.Source, position, RdbBlockContent.ToMetres(height), out NormalizedLightPlacement light))
                lights.Add(light);
            billboards.Add(new($"billboard/{scope}/{flat.Index}", $"sprite/texture-{flat.Source.TextureArchive}-{flat.Source.TextureRecord}", position,
                new(RdbBlockContent.ToMetres(width), RdbBlockContent.ToMetres(height))));
        }

        RdbBlockFlat? start = content.Flats.FirstOrDefault(flat => flat.Kind == RdbFlatKind.StartMarker);
        RdbBlockFlat? enter = content.Flats.FirstOrDefault(flat => flat.Kind == RdbFlatKind.EnterMarker);
        (NormalizedBounds? blockBounds, DaggerfallWorldBlockSpatial? spatial, GeneratedSpatialArtifact? artifact) = Spatial(key, geometry, null);
        DaggerfallWorldBlock block = new(key, DaggerfallWorldBlockKind.Rdb, sourceKey, content.SourceOrdinal, null, blockBounds, spatial, models)
        {
            Doors = doors,
            StartMarker = start is null ? null : new("marker/start", MeshGeometry.ToRightHanded(start.Point)),
            EnterMarker = enter is null ? null : new("marker/enter", MeshGeometry.ToRightHanded(enter.Point)),
            QuestMarkers = [.. content.Flats.Where(flat => flat.Kind == RdbFlatKind.QuestMarker)
                .Select(flat => new DaggerfallWorldBlockQuestMarker($"quest/{scope}/{flat.Index}",
                    QuestMarkerNormalization.KindOf(flat.Source.TextureArchive, flat.Source.TextureRecord)!.Value, MeshGeometry.ToRightHanded(flat.Point), flat.Index))],
            Lights = lights,
            Billboards = billboards,
            Actors = [.. content.Flats.Where(flat => flat.Kind == RdbFlatKind.FixedMobile)
                .Select(flat => new NormalizedActorPlacement($"actor/{scope}/{flat.Index}", $"actor/mobile-{flat.Mobile!.Id.Value}", MeshGeometry.ToRightHanded(flat.Point)))],
            Treasures = [.. content.Flats.Where(flat => flat.Kind == RdbFlatKind.Treasure)
                .Select(flat => new NormalizedMarker($"treasure/{scope}/{flat.Index}", MeshGeometry.ToRightHanded(flat.Point)))],
            Actions = content.Actions(scope, MeshGeometry.ToRightHanded),
            WaterLevel = content.WaterLevel is int level ? Arena2SourceTransform.ToImportPoint(0, level, 0).YMetres : null,
            AmbientZone = content.AmbientZone,
        };
        return new(block, artifact);
    }

    private static DaggerfallWorldBlockModel RmbModel(string id, RmbBlockModel model) => new(
        id,
        model.ModelId,
        MeshArtifactId(model.ModelId),
        MeshGeometry.ToRightHanded(model.Frame.Place(new(0F, 0F, 0F))),
        new(0F, model.Frame.ParentYawDegrees + model.Frame.ModelYawDegrees, 0F))
    {
        BuildingIndex = model.BuildingIndex,
    };

    private static string MeshArtifactId(string modelId) =>
        GeometryPublicationBuilder.MeshArtifactId(uint.Parse(modelId, NumberStyles.None, CultureInfo.InvariantCulture));

    private static NormalizedMarker? FirstMarker(IEnumerable<RmbBlockFlat> flats, ushort record, string id) =>
        flats.FirstOrDefault(flat => flat.TextureArchive == RdbSourceClassification.EditorFlatArchive && flat.TextureRecord == record) is { } marker
            ? new(id, MeshGeometry.ToRightHanded(marker.Point))
            : null;

    private int Ordinal(string sourceKey) => blocks.TryGetByName(sourceKey, out BsaRecord? record) && record is not null
        ? record.Ordinal
        : throw new InvalidOperationException($"BLOCKS.BSA is missing requested block '{sourceKey}'.");

    /// <summary>The 64-by-64 clear automap grid as base64 bits, cell (x, z) at bit x + 64 z.</summary>
    private static string ClearGround(IReadOnlyList<(int X, int Z)> cells)
    {
        byte[] bits = new byte[RmbBlockContent.AutomapSide * RmbBlockContent.AutomapSide / 8];
        foreach ((int x, int z) in cells)
        {
            int index = x + (RmbBlockContent.AutomapSide * z);
            bits[index / 8] |= (byte)(1 << (index % 8));
        }

        return Convert.ToBase64String(bits);
    }

    /// <summary>
    /// The block's bounds and its collision/navigation artifact, derived from its static geometry with the
    /// sites' navigation config; an exterior admits only its clear outdoor ground. A block with no static
    /// geometry has no artifact.
    /// </summary>
    private (NormalizedBounds?, DaggerfallWorldBlockSpatial?, GeneratedSpatialArtifact?) Spatial(
        string key, BlockGeometry geometry, Func<NormalizedNavigationCell, bool>? admitted)
    {
        if (geometry.All.Count == 0) return (null, null, null);
        NormalizedBounds bounds = MeshGeometry.Bounds(geometry.All);
        if (geometry.Static.Vertices.Count == 0) return (bounds, null, null);
        string artifactId = $"artifact/{Root}/{key}/collision-navigation";
        string staticMeshArtifactId = $"artifact/{Root}/{key}";
        NormalizedMesh[] staticMeshes = [geometry.Static.ToMesh($"mesh/{Root}/{key}/collision", staticMeshArtifactId)];
        NormalizedNavigationSurface surface = OfflineNavigationDeriver.Derive($"navigation/{Root}/{key}", artifactId, staticMeshes, navigation);
        if (admitted is not null) surface = surface with { Cells = [.. surface.Cells.Where(admitted)] };
        string relativePath = $"{key}{SpatialArtifactBinary.Extension}";
        GeneratedSpatialArtifact artifact = new(artifactId, relativePath,
            CollisionNavigationArtifact.Serialize(staticMeshArtifactId, bounds, staticMeshes, surface), []);
        return (bounds, new(relativePath, artifact.ContentDigest, artifact.Bytes.Length, surface.Id, staticMeshArtifactId), artifact);
    }

    /// <summary>Decodes an RMB placement's mesh once, refusing a model number the archive does not serve, as a site does.</summary>
    private Arch3dMesh RmbMesh(RmbBlockModel model)
    {
        if (rmbMeshes.TryGetValue(model.ModelId, out Arch3dMesh? cached)) return cached;
        Arch3dMesh mesh = RmbBlockContent.Mesh(arch, model);
        rmbMeshes.Add(model.ModelId, mesh);
        return mesh;
    }

    /// <summary>Decodes an RDB model's mesh, recording rather than throwing when the archive cannot serve it.</summary>
    private Arch3dMesh? RdbMesh(string modelId)
    {
        if (meshes.TryGetValue(modelId, out Arch3dMesh? cached)) return cached;
        Arch3dMesh? mesh = null;
        if (!uint.TryParse(modelId, NumberStyles.None, CultureInfo.InvariantCulture, out uint recordId)
            || !arch.TryGetByNumericId(recordId, out BsaRecord? record) || record is null)
            unresolved[modelId] = $"ARCH3D.BSA carries no numeric model '{modelId}'";
        else
        {
            try
            {
                mesh = Arch3dDecoder.Decode(arch.GetPayload(record).Span, arch.Source, recordId);
                if (!mesh.Planes.Any(plane => plane.Points.Count >= 3))
                {
                    unresolved[modelId] = $"ARCH3D.BSA model '{modelId}' declares no drawable plane";
                    mesh = null;
                }
            }
            catch (Arena2FormatException error)
            {
                unresolved[modelId] = $"ARCH3D.BSA model '{modelId}' at record {record.Ordinal} could not be decoded: {error.Message}";
            }
        }

        meshes[modelId] = mesh;
        return mesh;
    }

    private (int Width, int Height) TextureExtent(ushort archiveId, ushort recordId)
    {
        if (textures.TryGetValue((archiveId, recordId), out (int Width, int Height) extent)) return extent;
        string leaf = $"TEXTURE.{archiveId:000}";
        DungeonLogicalSource source = sources.Require(leaf);
        TextureArchive archive = TextureArchive.Parse(source.Bytes.Span, source.Label);
        TextureRecordInfo info = archive.GetRecordInfo(recordId);
        IndexedTextureFrame frame = archive.DecodeFrame(recordId, 0);
        if (info.Width <= 0 || info.Height <= 0 || frame.Width != info.Width || frame.Height != info.Height)
            throw new InvalidOperationException($"Texture '{leaf}' record {recordId} has an invalid decoded extent.");
        extent = (info.Width, info.Height);
        textures.Add((archiveId, recordId), extent);
        return extent;
    }

    /// <summary>A block's placed geometry: the static part collides and navigates; doors and action models move.</summary>
    private sealed class BlockGeometry
    {
        private readonly List<NormalizedVector3> all = [];

        /// <summary>Collision has no material; the static part is one collision mesh with no texture coordinates.</summary>
        public NormalizedMeshBuilder Static { get; } = new("material/collision", participatesInCollision: true);

        public IReadOnlyList<NormalizedVector3> All => all;

        public void AddStatic(IReadOnlyList<NormalizedVector3> polygon)
        {
            Static.Add(polygon, [.. polygon.Select(_ => new NormalizedVector2(0F, 0F))], MeshGeometry.Normal(polygon));
            all.AddRange(polygon);
        }

        public void AddMovable(IReadOnlyList<NormalizedVector3> polygon) => all.AddRange(polygon);
    }
}
