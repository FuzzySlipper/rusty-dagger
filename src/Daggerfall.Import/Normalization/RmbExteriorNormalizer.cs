using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>Which real RMB half is assembled into one normalized world closure.</summary>
public enum RmbWorldProfileKind { Exterior, Interior }

/// <summary>One explicit selected RMB building, addressed by its MAPPITEM block and donor sub-record ordinal.</summary>
public sealed record RmbBuildingSelection(byte BlockX, byte BlockY, int BuildingIndex)
{
    public void Validate(MapsExteriorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (BlockX >= layout.Width || BlockY >= layout.Height || BuildingIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(BuildingIndex), "The selected RMB building is outside the source exterior layout.");
    }
}

/// <summary>Pure source request for a selected city exterior or one of its building interiors.</summary>
public sealed record RmbExteriorNormalizationRequest(DungeonLogicalSourceSet Sources, int Region, string LocationName, RmbWorldProfileKind ProfileKind)
{
    public RmbBuildingSelection? Building { get; init; }
    public NavigationDerivationConfig Navigation { get; init; } = NavigationDerivationConfig.ClassicDefault;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sources);
        if (Region is < 0 or > 999) throw new ArgumentOutOfRangeException(nameof(Region));
        ArgumentException.ThrowIfNullOrWhiteSpace(LocationName);
        if (!Enum.IsDefined(ProfileKind)) throw new ArgumentOutOfRangeException(nameof(ProfileKind));
        if (ProfileKind == RmbWorldProfileKind.Interior && Building is null)
            throw new ArgumentException("An RMB interior request must select one donor building.", nameof(Building));
        if (ProfileKind == RmbWorldProfileKind.Exterior && Building is not null)
            throw new ArgumentException("An RMB exterior request cannot select one building.", nameof(Building));
        ArgumentNullException.ThrowIfNull(Navigation);
        Navigation.Validate();
    }
}

/// <summary>Deterministic normalized RMB profile, its static mesh/collision/navigation closure, and source-referenced meshes.</summary>
public sealed record RmbExteriorNormalizationResult(
    NormalizedImportDocument Document,
    DungeonSpatialPublication SpatialPublication,
    MapsExteriorLayout Layout,
    RmbBuildingSelection? Building,
    IReadOnlyList<string> ReferencedMeshIds)
{
    public void Validate()
    {
        Document.Validate();
        SpatialPublication.ValidateAgainst(Document);
        ArgumentNullException.ThrowIfNull(Layout);
        NormalizedImportDocument.ValidateUnique(ReferencedMeshIds, id => id, "RMB referenced mesh");
    }
}

/// <summary>
/// Offline RMB assembly using the donor MAPPITEM grid and RMB sub-record transforms. It emits the same
/// normalized static-mesh, collision, navigation, and resource artifacts used by RDB locations; it never
/// constructs Unity or Engine scene objects.
/// </summary>
public static class RmbExteriorNormalizer
{
    private const string ImporterId = "daggerfall-import/rmb-exterior-normalizer";

    public static RmbExteriorNormalizationResult Normalize(RmbExteriorNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        BsaArchive maps = BsaArchive.Parse(request.Sources.Require("MAPS.BSA").Bytes.Span, request.Sources.Require("MAPS.BSA").Label);
        BsaArchive blocks = BsaArchive.Parse(request.Sources.Require("BLOCKS.BSA").Bytes.Span, request.Sources.Require("BLOCKS.BSA").Label);
        BsaArchive arch = BsaArchive.Parse(request.Sources.Require("ARCH3D.BSA").Bytes.Span, request.Sources.Require("ARCH3D.BSA").Label);
        PakMap climate = PakDecoder.Decode(request.Sources.Require("CLIMATE.PAK").Bytes.Span, request.Sources.Require("CLIMATE.PAK").Label);
        MapsExteriorLayout layout = MapsDecoder.DecodeExteriorLayout(maps, request.Region, request.LocationName);
        (int mapX, int climateY) = MapsDecoder.ToMapPixel(layout.Longitude, layout.Latitude);
        // MapsFile.GetClimateIndex advances the X pixel to align the climate and height-map source grids.
        int climateX = mapX + 1;
        if (!climate.TryGetPixel(climateX, climateY, out byte worldClimate))
            throw new InvalidOperationException($"CLIMATE.PAK has no source pixel at ({climateX}, {climateY}) for '{layout.LocationName}'.");
        ushort groundTextureArchive = GroundTextureArchive(worldClimate);
        if (layout.Blocks.Count == 0) throw new InvalidOperationException($"RMB location '{layout.LocationName}' has no exterior blocks.");
        if (request.Building is { } selected) selected.Validate(layout);

        Builder builder = new(request, layout, blocks, arch, groundTextureArchive);
        if (request.ProfileKind == RmbWorldProfileKind.Exterior)
        {
            foreach (MapsExteriorBlock reference in layout.Blocks.OrderBy(block => block.X).ThenBy(block => block.Y).ThenBy(block => block.SourceName, StringComparer.Ordinal))
                builder.AddExterior(reference);
        }
        else
        {
            RmbBuildingSelection selection = request.Building!;
            MapsExteriorBlock reference = layout.Blocks.Single(block => block.X == selection.BlockX && block.Y == selection.BlockY);
            builder.AddInterior(reference, selection.BuildingIndex);
        }
        return builder.Build();
    }

    /// <summary>Maps the donor climate's exterior-ground set as <c>MapsFile.GetClimateSettings()</c> does.</summary>
    private static ushort GroundTextureArchive(byte climate) => climate switch
    {
        223 or 227 or 228 => 402,
        224 or 225 or 229 => 2,
        226 or 230 => 102,
        _ => 302,
    };

    private sealed class Builder(RmbExteriorNormalizationRequest request, MapsExteriorLayout layout, BsaArchive blocks, BsaArchive arch, ushort groundTextureArchive)
    {
        private readonly Dictionary<(ushort Archive, ushort Record), GeometryBuilder> geometry = [];
        private readonly Dictionary<(ushort Archive, ushort Record), TextureInfo> textures = [];
        private readonly SortedSet<string> referencedMeshes = new(StringComparer.Ordinal);
        private readonly HashSet<(int X, int Z)> outdoorNavigationCells = [];
        private NormalizedMarker? startMarker;
        private NormalizedMarker? enterMarker;

        public void AddExterior(MapsExteriorBlock reference)
        {
            (RmbBlockSummary summary, RmbBlockPlacements placements, BsaRecord record) = ReadBlock(reference);
            for (int index = 0; index < summary.Buildings.Count; index++)
            {
                RmbBuildingSlot slot = summary.Buildings[index];
                foreach (RmbModelPlacement model in placements.Buildings[index].Exterior.Models)
                    AddModel(model, Arena2SourceTransform.ToExteriorBlockOrigin(reference), slot, $"{Slug(reference.SourceName)}/{index}");
            }
            Arena2ImportPoint origin = Arena2SourceTransform.ToExteriorBlockOrigin(reference);
            foreach (RmbModelPlacement model in placements.MiscModels)
                AddMiscModel(model, origin, $"{Slug(reference.SourceName)}/misc");
            AddGround(summary, origin, reference);
        }

        /// <summary>
        /// Projects every donor FLD ground tile. The ground plane remains complete below buildings exactly as
        /// MeshReader renders it; CityNavigation's automap is a navigation mask, never a reason to cut a
        /// physical hole in the source ground mesh.
        /// </summary>
        private void AddGround(RmbBlockSummary summary, Arena2ImportPoint origin, MapsExteriorBlock block)
        {
            if (summary.GroundTiles.Count != 256 || summary.AutoMapData.Count != 64 * 64)
                throw new InvalidOperationException($"RMB block '{block.SourceName}' has no complete FLD ground and automap data.");
            for (int sourceY = 0; sourceY < 64; sourceY++)
            for (int sourceX = 0; sourceX < 64; sourceX++)
                if (summary.AutoMapData[(sourceY * 64) + sourceX] == 0)
                    outdoorNavigationCells.Add((checked((block.X * 64) + sourceX), checked((block.Y * 64) + (63 - sourceY))));

            foreach (RmbGroundTile tile in summary.GroundTiles)
            {
                // MeshReader maps the six-bit source record to its four-frame texture record and
                // substitutes grass for 56..63. TextureArchive exposes that same four-frame group
                // by its base ordinal, so retain the group ordinal and make the donor fallback explicit.
                ushort textureRecord = tile.TextureRecord < 56 ? tile.TextureRecord : (ushort)2;
                TextureInfo texture = Texture(groundTextureArchive, textureRecord);
                GeometryBuilder group = Geometry(groundTextureArchive, textureRecord, texture.MaterialId);
                const int side = 256;
                int y = 15 - tile.Y; // MeshReader's ground plane reverses donor tile rows.
                Arena2ImportPoint a = Add(origin, Arena2SourceTransform.ToRmbImportPoint(tile.X * side, 0, y * side));
                Arena2ImportPoint b = Add(origin, Arena2SourceTransform.ToRmbImportPoint((tile.X + 1) * side, 0, y * side));
                Arena2ImportPoint c = Add(origin, Arena2SourceTransform.ToRmbImportPoint((tile.X + 1) * side, 0, (y + 1) * side));
                Arena2ImportPoint d = Add(origin, Arena2SourceTransform.ToRmbImportPoint(tile.X * side, 0, (y + 1) * side));
                IReadOnlyList<NormalizedVector2> uv = tile.Rotated
                    ? [new(0F, 1F), new(0F, 0F), new(1F, 0F), new(1F, 1F)]
                    : [new(0F, 0F), new(1F, 0F), new(1F, 1F), new(0F, 1F)];
                if (tile.Flipped) uv = uv.Reverse().ToArray();
                group.Add([MeshGeometry.ToRightHanded(a), MeshGeometry.ToRightHanded(b), MeshGeometry.ToRightHanded(c), MeshGeometry.ToRightHanded(d)], uv);
            }
        }

        public void AddInterior(MapsExteriorBlock reference, int buildingIndex)
        {
            (RmbBlockSummary summary, RmbBlockPlacements placements, _) = ReadBlock(reference);
            if (buildingIndex >= summary.Buildings.Count)
                throw new ArgumentOutOfRangeException(nameof(buildingIndex), $"RMB block '{reference.SourceName}' has {summary.Buildings.Count} buildings, not {buildingIndex + 1}.");
            // The donor's DaggerfallInterior creates this half in its own local frame; it does not carry the
            // exterior block or building-subrecord transform into the interior scene.
            foreach (RmbModelPlacement model in placements.Buildings[buildingIndex].Interior.Models)
                AddInteriorModel(model, $"{Slug(reference.SourceName)}/{buildingIndex}/interior");
            foreach (RmbFlatPlacement flat in placements.Buildings[buildingIndex].Interior.Flats)
                AddInteriorMarker(flat);
        }

        private (RmbBlockSummary Summary, RmbBlockPlacements Placements, BsaRecord Record) ReadBlock(MapsExteriorBlock reference)
        {
            if (!blocks.TryGetByName(reference.SourceName, out BsaRecord? record) || record is null)
                throw new InvalidOperationException($"BLOCKS.BSA is missing requested RMB block '{reference.SourceName}'.");
            ReadOnlyMemory<byte> bytes = blocks.GetPayload(record);
            if (!RmbBlockSummaryReader.TryRead(bytes.ToArray(), blocks.Source, 0, bytes.Length, out RmbBlockSummary? summary, out string reason) || summary is null)
                throw new InvalidOperationException($"RMB block '{reference.SourceName}' cannot be read: {reason}.");
            return (summary, RmbPlacementReader.Read(bytes.ToArray(), 0, summary, blocks.Source), record);
        }

        private void AddModel(RmbModelPlacement model, Arena2ImportPoint exteriorOrigin, RmbBuildingSlot building, string identity)
        {
            Matrix3 buildingRotation = Matrix3.Yaw(Arena2SourceTransform.ToRmbYawDegrees(building.YRotation));
            Arena2ImportPoint buildingOrigin = Arena2SourceTransform.ToRmbBuildingOrigin(building);
            AddMesh(model, point => Add(exteriorOrigin, Add(buildingOrigin, buildingRotation.Transform(point))), identity);
        }

        private void AddMiscModel(RmbModelPlacement model, Arena2ImportPoint exteriorOrigin, string identity) =>
            AddMesh(model, point => Add(exteriorOrigin, Add(Arena2SourceTransform.ToRmbImportPoint(0, 0, 4096), point)), identity);

        private void AddInteriorModel(RmbModelPlacement model, string identity) => AddMesh(model, point => point, identity);

        private void AddInteriorMarker(RmbFlatPlacement flat)
        {
            NormalizedVector3 position = MeshGeometry.ToRightHanded(Arena2SourceTransform.ToRmbImportPoint(flat.X, flat.Y, flat.Z));
            if (flat.TextureArchive == RdbSourceClassification.EditorFlatArchive && flat.TextureRecord == RdbSourceClassification.StartMarkerRecord)
                startMarker ??= new NormalizedMarker("marker/start", position);
            else if (flat.TextureArchive == RdbSourceClassification.EditorFlatArchive && flat.TextureRecord == RdbSourceClassification.EnterMarkerRecord)
                enterMarker ??= new NormalizedMarker("marker/enter", position);
        }

        private void AddMesh(RmbModelPlacement model, Func<Arena2ImportPoint, Arena2ImportPoint> parent, string identity)
        {
            referencedMeshes.Add(model.ModelId);
            if (!uint.TryParse(model.ModelId, NumberStyles.None, CultureInfo.InvariantCulture, out uint id)
                || !arch.TryGetByNumericId(id, out BsaRecord? record) || record is null)
                throw new InvalidOperationException($"ARCH3D.BSA has no RMB model '{model.ModelId}' named by '{identity}'.");
            Arch3dMesh mesh = Arch3dDecoder.Decode(arch.GetPayload(record).Span, arch.Source, id);
            Matrix3 rotation = Matrix3.Yaw(Arena2SourceTransform.ToRmbYawDegrees(model.YRotation));
            Arena2ImportPoint modelOrigin = Arena2SourceTransform.ToRmbImportPoint(model.X, model.Y, model.Z);
            foreach (Arch3dPlane plane in mesh.Planes.Where(plane => plane.Points.Count >= 3))
            {
                TextureInfo texture = Texture(plane.TextureArchive, plane.TextureRecord);
                GeometryBuilder group = Geometry(plane.TextureArchive, plane.TextureRecord, texture.MaterialId);
                List<NormalizedVector3> polygon = [];
                List<NormalizedVector2> uvs = [];
                foreach (Arch3dPoint point in plane.Points)
                {
                    Arena2ImportPoint placed = parent(Add(modelOrigin, rotation.Transform(Arena2SourceTransform.ToImportPoint(point))));
                    polygon.Add(MeshGeometry.ToRightHanded(placed));
                    Arena2TextureUv uv = Arena2SourceTransform.ToTextureUv(point, texture.Width, texture.Height);
                    uvs.Add(new(uv.U, uv.V));
                }
                group.Add(polygon, uvs);
            }
        }

        private TextureInfo Texture(ushort archiveId, ushort recordId)
        {
            if (textures.TryGetValue((archiveId, recordId), out TextureInfo? texture)) return texture;
            string leaf = $"TEXTURE.{archiveId:000}";
            DungeonLogicalSource source = request.Sources.Require(leaf);
            TextureArchive archive = TextureArchive.Parse(source.Bytes.Span, source.Label);
            TextureRecordInfo info = archive.GetRecordInfo(recordId);
            if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"RMB texture '{leaf}' record {recordId} has no valid extent.");
            texture = new(info.Width, info.Height, $"texture/{archiveId}-{recordId}", $"material/texture-{archiveId}-{recordId}");
            textures.Add((archiveId, recordId), texture);
            return texture;
        }

        private GeometryBuilder Geometry(ushort archive, ushort record, string material)
        {
            if (!geometry.TryGetValue((archive, record), out GeometryBuilder? group))
            {
                group = new(material);
                geometry.Add((archive, record), group);
            }
            return group;
        }

        public RmbExteriorNormalizationResult Build()
        {
            if (geometry.Count == 0) throw new InvalidOperationException("RMB normalization produced no ARCH3D static geometry.");
            string slug = Slug(layout.LocationName);
            string profile = request.ProfileKind == RmbWorldProfileKind.Exterior ? "exterior" : $"interior-{request.Building!.BlockX}-{request.Building.BlockY}-{request.Building.BuildingIndex}";
            string root = $"rmb/{slug}/{profile}";
            string staticId = $"artifact/{root}/static-mesh", collisionId = $"artifact/{root}/collision-navigation", resourcesId = $"artifact/{root}/resource-catalog";
            List<NormalizedMesh> meshes = geometry.OrderBy(pair => pair.Key.Archive).ThenBy(pair => pair.Key.Record)
                .Select(pair => pair.Value.ToMesh($"mesh/{root}/texture-{pair.Key.Archive}-{pair.Key.Record}", staticId)).ToList();
            NormalizedBounds bounds = MeshGeometry.Bounds(meshes.SelectMany(mesh => mesh.Vertices).ToArray());
            NormalizedNavigationSurface navigation = OfflineNavigationDeriver.Derive($"navigation/{root}", collisionId, meshes, request.Navigation);
            if (request.ProfileKind == RmbWorldProfileKind.Exterior)
            {
                navigation = navigation with
                {
                    Cells = navigation.Cells.Where(IsClearOutdoorGroundCell).ToArray(),
                };
            }
            List<NormalizedResourceCatalogEntry> resources = textures.OrderBy(pair => pair.Key.Archive).ThenBy(pair => pair.Key.Record)
                .SelectMany(pair => new[]
                {
                    new NormalizedResourceCatalogEntry(NormalizedResourceCatalogEntry.CurrentSchemaVersion, pair.Value.TextureId, NormalizedResourceKind.Texture, resourcesId, [], []),
                    new NormalizedResourceCatalogEntry(NormalizedResourceCatalogEntry.CurrentSchemaVersion, pair.Value.MaterialId, NormalizedResourceKind.Material, resourcesId, [pair.Value.TextureId], []),
                }).ToList();
            NormalizedWorld world = new(NormalizedWorld.CurrentSchemaVersion, $"mesh/{root}", meshes.Select(mesh => mesh.Id).ToArray(), navigation.Id, startMarker, enterMarker, [], [], [], [], []);
            DungeonSpatialPublication spatial = DungeonSpatialPublication.Create(staticId, $"spatial/{slug}/{profile}/static-mesh.json", collisionId,
                $"spatial/{slug}/{profile}/collision-navigation.json", resourcesId, $"resources/{slug}/{profile}/catalog.json", world.VisualMeshAssetId, bounds, meshes, world, navigation, resources);
            NormalizedImportDocument document = new NormalizedImportDocument(NormalizedImportDocument.CurrentSchemaVersion,
                new ImportProvenance(ImportProvenance.CurrentSchemaVersion, ImporterId, 1, request.Sources.Sources.Select(source => new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, source.Label, ContentDigest.Compute(source.Bytes.Span), source.Bytes.Length, 1)).ToArray()),
                spatial.ArtifactDescriptors, new NormalizedCoordinateConvention(NormalizedCoordinateConvention.CurrentSchemaVersion, NormalizedHandedness.Right, NormalizedVerticalAxis.PositiveY, 1F),
                bounds, meshes, navigation, world, resources).Canonicalize();
            RmbExteriorNormalizationResult result = new(document, spatial, layout, request.Building, referencedMeshes.ToArray());
            result.Validate();
            return result;
        }

        private bool IsClearOutdoorGroundCell(NormalizedNavigationCell cell)
        {
            // CityNavigation has one 64-by-64 automap value per 64 raw-unit (1.6m) square. Sample the
            // derived cell centre against that exact source grid after the normalized right-handed Z flip.
            // The exterior navigation profile intentionally admits the original ground plane only: roofs
            // and interior floors are collision geometry, not outdoor walkable ground.
            if (MathF.Abs(cell.SupportHeight) > 0.0001F) return false;
            const float sourceAutomapCellMetres = 64F * 0.025F;
            int x = checked((int)MathF.Floor(((cell.Column + 0.5F) * request.Navigation.CellSize) / sourceAutomapCellMetres));
            int z = checked((int)MathF.Floor((-((cell.Row + 0.5F) * request.Navigation.CellSize)) / sourceAutomapCellMetres));
            return outdoorNavigationCells.Contains((x, z));
        }

        private static Arena2ImportPoint Add(Arena2ImportPoint left, Arena2ImportPoint right) => new(left.XMetres + right.XMetres, left.YMetres + right.YMetres, left.ZMetres + right.ZMetres);
    }

    private sealed class GeometryBuilder(string material)
    {
        private readonly List<NormalizedVector3> vertices = [];
        private readonly List<NormalizedVector3> normals = [];
        private readonly List<NormalizedVector2> uvs = [];
        private readonly List<NormalizedTriangle> triangles = [];
        public void Add(IReadOnlyList<NormalizedVector3> polygon, IReadOnlyList<NormalizedVector2> coordinates) => MeshGeometry.AppendPolygon(vertices, normals, uvs, triangles, polygon, coordinates, MeshGeometry.Normal(polygon));
        public NormalizedMesh ToMesh(string id, string artifact) => new(NormalizedMesh.CurrentSchemaVersion, id, artifact, vertices, normals, uvs, triangles, [new NormalizedMaterialGroup(material, 0, triangles.Count, true)]);
    }

    private sealed record TextureInfo(int Width, int Height, string TextureId, string MaterialId);
    private readonly record struct Matrix3(float C, float S)
    {
        public static Matrix3 Yaw(float degrees) { float radians = degrees * MathF.PI / 180F; return new(MathF.Cos(radians), MathF.Sin(radians)); }
        public Arena2ImportPoint Transform(Arena2ImportPoint value) => new((C * value.XMetres) + (S * value.ZMetres), value.YMetres, (-S * value.XMetres) + (C * value.ZMetres));
    }
    private static string Slug(string value) => new(value.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray());
}
