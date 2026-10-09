using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// One source RMB yaw rotation in the donor's left-handed importer space: a model or building
/// sub-record turns about +Y by <see cref="Arena2SourceTransform.ToRmbYawDegrees"/>.
/// </summary>
internal readonly record struct RmbYaw(float C, float S)
{
    public static RmbYaw Degrees(float degrees)
    {
        float radians = degrees * MathF.PI / 180F;
        return new(MathF.Cos(radians), MathF.Sin(radians));
    }

    public Arena2ImportPoint Transform(Arena2ImportPoint value) =>
        new((C * value.XMetres) + (S * value.ZMetres), value.YMetres, (-S * value.XMetres) + (C * value.ZMetres));
}

/// <summary>Which frame an RMB model's own transform nests inside within its block.</summary>
internal enum RmbModelParent
{
    /// <summary>A building sub-record's outside half: the sub-record's origin and yaw.</summary>
    Building,

    /// <summary>The block's own objects, offset to the donor's misc-object origin.</summary>
    Block,

    /// <summary>A building's inside half, which the donor builds in its own frame.</summary>
    Interior,
}

/// <summary>
/// The exact nested transform one RMB model placement applies inside its block, in the order the donor
/// composes it: the model's own yaw and origin, then its parent's. A site applies its block's grid origin
/// after this, so the block's own frame and a placed site compute the same source arithmetic.
/// </summary>
internal sealed record RmbModelFrame(
    RmbModelPlacement Model,
    RmbModelParent Parent,
    Arena2ImportPoint ParentOrigin,
    float ParentYawDegrees)
{
    private readonly RmbYaw modelYaw = RmbYaw.Degrees(Arena2SourceTransform.ToRmbYawDegrees(Model.YRotation));
    private readonly RmbYaw parentYaw = RmbYaw.Degrees(ParentYawDegrees);
    private readonly Arena2ImportPoint modelOrigin = Arena2SourceTransform.ToRmbImportPoint(Model.X, Model.Y, Model.Z);

    /// <summary>The model's own yaw in source Euler degrees.</summary>
    public float ModelYawDegrees => Arena2SourceTransform.ToRmbYawDegrees(Model.YRotation);

    /// <summary>Places one model-local importer point into the block's frame.</summary>
    public Arena2ImportPoint Place(Arena2ImportPoint point)
    {
        Arena2ImportPoint local = RmbBlockContent.Add(modelOrigin, modelYaw.Transform(point));
        return Parent switch
        {
            RmbModelParent.Building => RmbBlockContent.Add(ParentOrigin, parentYaw.Transform(local)),
            RmbModelParent.Block => RmbBlockContent.Add(ParentOrigin, local),
            _ => local,
        };
    }
}

/// <summary>One RMB model placement with the block-level facts it carries.</summary>
/// <param name="Frame">Its transform within the block.</param>
/// <param name="BuildingIndex">The building sub-record that places it, or null for the block's own objects and interiors.</param>
/// <param name="Ordinal">Its ordinal within the half (or the block's own object list) that places it.</param>
/// <param name="StartingLockValue">The lock an exterior building door of this model starts with (the sub-record's quality halved), else zero.</param>
/// <param name="Identity">A source identity named in a refusal.</param>
internal sealed record RmbBlockModel(RmbModelFrame Frame, int? BuildingIndex, int Ordinal, int StartingLockValue, string Identity)
{
    public string ModelId => Frame.Model.ModelId;
}

/// <summary>One RMB flat in its block's frame, with the source texture that classifies it.</summary>
internal sealed record RmbBlockFlat(int Ordinal, int TextureArchive, int TextureRecord, Arena2ImportPoint Point, RmbFlatPlacement Source);

/// <summary>One fixed RMB person placed by a building sub-record's outside half, in its block's frame.</summary>
internal sealed record RmbBlockPerson(RmbPeoplePlacement Person, int BuildingIndex, int PersonIndex, Arena2ImportPoint Point, RmbBuildingSlot Slot);

/// <summary>
/// One source RMB ground tile as the donor's <c>MeshReader</c> builds it: the four-frame texture record
/// (grass substituted for 56..63) and its quad corners in the block's frame.
/// </summary>
internal sealed record RmbBlockGroundTile(RmbGroundTile Source, ushort TextureRecord, IReadOnlyList<Arena2ImportPoint> Corners, IReadOnlyList<NormalizedVector2> Uvs);

/// <summary>
/// The block-level normalization of one RMB record: every model, flat, person, ground tile and clear
/// automap cell the block places, each in the block's own frame and in the donor's order. A site
/// exterior or interior applies its block's grid origin and its location's identities to these; the
/// world block publication publishes them in the block's frame. Both read the block only through here.
/// </summary>
internal sealed class RmbBlockContent
{
    /// <summary>Raw RMB units per ground tile side.</summary>
    private const int GroundTileSide = 256;

    /// <summary>The source automap is one 64-by-64 grid per block.</summary>
    public const int AutomapSide = 64;

    private RmbBlockContent(string sourceKey, RmbBlockSummary summary, RmbBlockPlacements placements)
    {
        SourceKey = sourceKey;
        Summary = summary;
        Placements = placements;
        List<RmbBlockModel> models = [];
        List<RmbBlockFlat> flats = [];
        List<RmbBlockPerson> people = [];
        string slug = PublishedIds.Slug(sourceKey);
        for (int index = 0; index < summary.Buildings.Count; index++)
        {
            RmbBuildingSlot slot = summary.Buildings[index];
            float slotYaw = Arena2SourceTransform.ToRmbYawDegrees(slot.YRotation);
            RmbYaw rotation = RmbYaw.Degrees(slotYaw);
            Arena2ImportPoint slotOrigin = Arena2SourceTransform.ToRmbBuildingOrigin(slot);
            RmbHalfPlacements exterior = placements.Buildings[index].Exterior;
            foreach ((RmbFlatPlacement flat, int ordinal) in exterior.Flats.Select((flat, ordinal) => (flat, ordinal)))
                flats.Add(new(ordinal, flat.TextureArchive, flat.TextureRecord,
                    Add(slotOrigin, rotation.Transform(Arena2SourceTransform.ToRmbImportPoint(flat.X, flat.Y, flat.Z))), flat));
            foreach ((RmbModelPlacement model, int ordinal) in exterior.Models.Select((model, ordinal) => (model, ordinal)))
                models.Add(new(new(model, RmbModelParent.Building, slotOrigin, slotYaw), index, ordinal, slot.Quality / 2, $"{slug}/{index}"));
            foreach ((RmbPeoplePlacement person, int personIndex) in exterior.People.Select((person, personIndex) => (person, personIndex)))
                people.Add(new(person, index, personIndex,
                    Add(slotOrigin, rotation.Transform(Arena2SourceTransform.ToRmbImportPoint(person.X, person.Y, person.Z))), slot));
        }

        // The donor offsets the block's own objects to the far edge of its grid square.
        Arena2ImportPoint miscOrigin = Arena2SourceTransform.ToRmbImportPoint(0, 0, 4096);
        foreach ((RmbModelPlacement model, int ordinal) in placements.MiscModels.Select((model, ordinal) => (model, ordinal)))
            models.Add(new(new(model, RmbModelParent.Block, miscOrigin, 0F), null, ordinal, 0, $"{slug}/misc"));
        foreach ((RmbFlatPlacement flat, int ordinal) in placements.MiscFlats.Select((flat, ordinal) => (flat, ordinal)))
            flats.Add(new(ordinal, flat.TextureArchive, flat.TextureRecord,
                Add(miscOrigin, Arena2SourceTransform.ToRmbImportPoint(flat.X, flat.Y, flat.Z)), flat));
        ExteriorModels = models;
        ExteriorFlats = flats;
        ExteriorPeople = people;

        if (summary.GroundTiles.Count != 256 || summary.AutoMapData.Count != AutomapSide * AutomapSide)
            throw new InvalidOperationException($"RMB block '{sourceKey}' has no complete FLD ground and automap data.");
        List<(int X, int Z)> clear = [];
        for (int sourceY = 0; sourceY < AutomapSide; sourceY++)
        for (int sourceX = 0; sourceX < AutomapSide; sourceX++)
            if (summary.AutoMapData[(sourceY * AutomapSide) + sourceX] == 0)
                clear.Add((sourceX, AutomapSide - 1 - sourceY));
        ClearOutdoorCells = clear;
        Ground = [.. summary.GroundTiles.Select(GroundTile)];
    }

    public string SourceKey { get; }

    public RmbBlockSummary Summary { get; }

    public RmbBlockPlacements Placements { get; }

    /// <summary>The outside halves' models in building order, then the block's own models.</summary>
    public IReadOnlyList<RmbBlockModel> ExteriorModels { get; }

    /// <summary>The outside halves' flats in building order, then the block's own flats.</summary>
    public IReadOnlyList<RmbBlockFlat> ExteriorFlats { get; }

    /// <summary>The outside halves' fixed people in building order.</summary>
    public IReadOnlyList<RmbBlockPerson> ExteriorPeople { get; }

    /// <summary>Every FLD ground tile, in source order.</summary>
    public IReadOnlyList<RmbBlockGroundTile> Ground { get; }

    /// <summary>
    /// The block's clear automap cells: (x, z) on the 64-by-64 grid with z counted from the block's near
    /// edge as the normalized frame's Z flip places it. CityNavigation's mask, never a hole in the ground.
    /// </summary>
    public IReadOnlyList<(int X, int Z)> ClearOutdoorCells { get; }

    /// <summary>Reads one RMB record from the block archive, refusing a record that is absent or unreadable.</summary>
    public static RmbBlockContent Read(BsaArchive blocks, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (!blocks.TryGetByName(sourceKey, out BsaRecord? record) || record is null)
            throw new InvalidOperationException($"BLOCKS.BSA is missing requested RMB block '{sourceKey}'.");
        byte[] bytes = blocks.GetPayload(record).ToArray();
        if (!RmbBlockSummaryReader.TryRead(bytes, blocks.Source, 0, bytes.Length, out RmbBlockSummary? summary, out string reason) || summary is null)
            throw new InvalidOperationException($"RMB block '{sourceKey}' cannot be read: {reason}.");
        return new(sourceKey, summary, RmbPlacementReader.Read(bytes, 0, summary, blocks.Source));
    }

    /// <summary>One building's inside half: its models in the donor interior's own frame.</summary>
    public IReadOnlyList<RmbBlockModel> InteriorModels(int buildingIndex)
    {
        RequireBuilding(buildingIndex);
        string identity = $"{PublishedIds.Slug(SourceKey)}/{buildingIndex}/interior";
        return [.. Placements.Buildings[buildingIndex].Interior.Models
            .Select((model, ordinal) => new RmbBlockModel(new(model, RmbModelParent.Interior, default, 0F), null, ordinal, 0, identity))];
    }

    /// <summary>One building's inside-half flats in the interior's own frame.</summary>
    public IReadOnlyList<RmbBlockFlat> InteriorFlats(int buildingIndex)
    {
        RequireBuilding(buildingIndex);
        return [.. Placements.Buildings[buildingIndex].Interior.Flats
            .Select((flat, ordinal) => new RmbBlockFlat(ordinal, flat.TextureArchive, flat.TextureRecord,
                Arena2SourceTransform.ToRmbImportPoint(flat.X, flat.Y, flat.Z), flat))];
    }

    /// <summary>Whether a building's inside half places any model, which is what makes it an interior.</summary>
    public bool HasInterior(int buildingIndex)
    {
        RequireBuilding(buildingIndex);
        return Placements.Buildings[buildingIndex].Interior.Models.Count != 0;
    }

    private void RequireBuilding(int buildingIndex)
    {
        if (buildingIndex < 0 || buildingIndex >= Summary.Buildings.Count)
            throw new ArgumentOutOfRangeException(nameof(buildingIndex), $"RMB block '{SourceKey}' has {Summary.Buildings.Count} buildings, not {buildingIndex + 1}.");
    }

    /// <summary>
    /// MeshReader maps the six-bit source record to its four-frame texture record and substitutes grass for
    /// 56..63; the ground plane reverses the donor tile rows and a flipped or rotated tile turns its UVs.
    /// </summary>
    private static RmbBlockGroundTile GroundTile(RmbGroundTile tile)
    {
        ushort textureRecord = tile.TextureRecord < 56 ? tile.TextureRecord : (ushort)2;
        int y = 15 - tile.Y;
        Arena2ImportPoint a = Arena2SourceTransform.ToRmbImportPoint(tile.X * GroundTileSide, 0, y * GroundTileSide);
        Arena2ImportPoint b = Arena2SourceTransform.ToRmbImportPoint((tile.X + 1) * GroundTileSide, 0, y * GroundTileSide);
        Arena2ImportPoint c = Arena2SourceTransform.ToRmbImportPoint((tile.X + 1) * GroundTileSide, 0, (y + 1) * GroundTileSide);
        Arena2ImportPoint d = Arena2SourceTransform.ToRmbImportPoint(tile.X * GroundTileSide, 0, (y + 1) * GroundTileSide);
        IReadOnlyList<NormalizedVector2> uv = tile.Rotated
            ? [new(0F, 1F), new(0F, 0F), new(1F, 0F), new(1F, 1F)]
            : [new(0F, 0F), new(1F, 0F), new(1F, 1F), new(0F, 1F)];
        if (tile.Flipped) uv = uv.Reverse().ToArray();
        return new(tile, textureRecord, [a, b, c, d], uv);
    }

    /// <summary>
    /// Whether an exterior plane is a building door: MeshReader classifies climate door archives by their
    /// base archive. The two source archives excluded are dungeon/Scourge exceptions, not RMB building doors.
    /// </summary>
    public static bool IsBuildingDoor(int textureArchive) => DoorArchive(textureArchive) == BuildingDoorArchive;

    /// <summary>
    /// The transition a door plane makes when it is not an exterior building door, as MeshReader classifies
    /// it: a building door (74) leads out of an interior, a dungeon entrance door (56, or a non-zero record
    /// of the ruin entrance archive 331) leads into a dungeon and a dungeon exit door (95) leads out of one.
    /// The caller knows which half it reads, so a building door here is always seen from inside.
    /// </summary>
    public static DaggerfallWorldBlockTransitionKind? TransitionDoor(int textureArchive, int textureRecord) => DoorArchive(textureArchive) switch
    {
        BuildingDoorArchive => DaggerfallWorldBlockTransitionKind.BuildingExit,
        DungeonEnterDoorArchive => DaggerfallWorldBlockTransitionKind.DungeonEntrance,
        DungeonRuinEnterDoorArchive when textureRecord > 0 => DaggerfallWorldBlockTransitionKind.DungeonEntrance,
        DungeonExitDoorArchive => DaggerfallWorldBlockTransitionKind.DungeonExit,
        _ => null,
    };

    private const int BuildingDoorArchive = 74;
    private const int DungeonEnterDoorArchive = 56;
    private const int DungeonRuinEnterDoorArchive = 331;
    private const int ScourgExteriorArchive = 156;
    private const int DungeonExitDoorArchive = 95;

    /// <summary>MeshReader compares a climate door archive by its base archive, except the ruin entrance and Scourg exterior.</summary>
    private static int DoorArchive(int textureArchive) =>
        textureArchive > 100 && textureArchive != DungeonRuinEnterDoorArchive && textureArchive != ScourgExteriorArchive
            ? textureArchive % 100
            : textureArchive;

    /// <summary>One plane's centre and unit normal, the donor's static door centre and normal.</summary>
    public static (NormalizedVector3 Centre, NormalizedVector3 Normal) DoorPlane(IReadOnlyList<NormalizedVector3> polygon) =>
        (new((polygon[0].X + polygon[2].X) / 2F, (polygon[0].Y + polygon[2].Y) / 2F, (polygon[0].Z + polygon[2].Z) / 2F),
            MeshGeometry.Normal(polygon));

    /// <summary>The two ARCH3D city gate models, open and closed (DFU <c>RMBLayout.CityGateOpenModelID</c>, <c>CityGateClosedModelID</c>).</summary>
    public const string CityGateOpenModel = "446";

    public const string CityGateClosedModel = "447";

    public static bool IsCityGate(string modelId) => modelId is CityGateOpenModel or CityGateClosedModel;

    /// <summary>
    /// One exterior building door from its placed plane: DFU <c>GameObjectHelper.GetStaticDoors</c> takes the
    /// opposite source corners for the centre and a uniform horizontal volume, because ARCH3D doors are planes.
    /// </summary>
    public static (NormalizedVector3 Centre, NormalizedVector3 RotationDegrees, NormalizedBounds CollisionBounds) Door(
        Arch3dPlane plane, IReadOnlyList<NormalizedVector3> polygon)
    {
        Arena2ImportPoint first = Arena2SourceTransform.ToImportPoint(plane.Points[0]);
        Arena2ImportPoint opposite = Arena2SourceTransform.ToImportPoint(plane.Points[2]);
        float thickness = MathF.Max(MathF.Abs(opposite.XMetres - first.XMetres), MathF.Abs(opposite.ZMetres - first.ZMetres));
        float height = MathF.Abs(opposite.YMetres - first.YMetres);
        NormalizedVector3 half = new(thickness / 2F, MathF.Max(height, thickness) / 2F, MathF.Min(height, thickness) / 2F);
        NormalizedVector3 centre = new((polygon[0].X + polygon[2].X) / 2F, (polygon[0].Y + polygon[2].Y) / 2F, (polygon[0].Z + polygon[2].Z) / 2F);
        NormalizedVector3 normal = MeshGeometry.Normal(polygon);
        float yaw = MathF.Atan2(normal.X, normal.Z) * (180F / MathF.PI);
        return (centre, new(0F, yaw, 0F), new(new(-half.X, -half.Y, -half.Z), half));
    }

    /// <summary>
    /// A searchable furniture surface point: an exposed authored face's centre lifted off the face, not the
    /// source pivot, which can lie below the floor or inside the solid. Downward faces offer none.
    /// </summary>
    public static NormalizedVector3? InteractionPoint(IReadOnlyList<NormalizedVector3> polygon, NormalizedVector3 surfaceNormal)
    {
        if (surfaceNormal.Y < -.5f) return null;
        const float surfaceSeparation = .01f;
        return new(polygon.Average(point => point.X) + surfaceNormal.X * surfaceSeparation,
            polygon.Average(point => point.Y) + surfaceNormal.Y * surfaceSeparation,
            polygon.Average(point => point.Z) + surfaceNormal.Z * surfaceSeparation);
    }

    /// <summary>
    /// Whether a derived navigation cell is clear outdoor ground. CityNavigation has one automap value per 64
    /// raw-unit (1.6m) square; the derived cell centre is sampled against that exact source grid after the
    /// normalized right-handed Z flip. The exterior profile admits the original ground plane only: roofs and
    /// interior floors are collision geometry, not outdoor walkable ground.
    /// </summary>
    public static bool IsClearOutdoorGroundCell(NormalizedNavigationCell cell, float cellSize, IReadOnlySet<(int X, int Z)> clearCells)
    {
        if (MathF.Abs(cell.SupportHeight) > 0.0001F) return false;
        const float sourceAutomapCellMetres = 64F * Arena2SourceTransform.SourceUnitMetres;
        int x = checked((int)MathF.Floor(((cell.Column + 0.5F) * cellSize) / sourceAutomapCellMetres));
        int z = checked((int)MathF.Floor((-((cell.Row + 0.5F) * cellSize)) / sourceAutomapCellMetres));
        return clearCells.Contains((x, z));
    }

    /// <summary>The classic static-person race StaticNPC.GetRaceFromFaction reads, or null for the regional bank.</summary>
    public static string? StaticNpcRace(int factionId, IReadOnlyDictionary<int, ClassicFaction> factions)
    {
        string? race = factions.GetValueOrDefault(factionId)?.Race switch
        {
            0 => "nord", 1 => "khajiit", 2 => "redguard", 3 => "breton",
            4 => "argonian", 5 => "wood-elf", 6 => "high-elf", 7 => "dark-elf", _ => null,
        };
        // A null faction race requests the existing published regional name-bank table at
        // ruleset content admission; this normalizer never carries another copy of that table.
        return factionId != 0 ? race : null;
    }

    /// <summary>
    /// The deterministic name seed of a fixed exterior person: its stable normalized identity hashed into the
    /// runtime's ushort width. Source offsets are provenance only and would truncate.
    /// </summary>
    public static ushort StablePopulationSeed(string sourceKey)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char value in sourceKey) hash = (hash ^ value) * 16777619;
            return (ushort)(hash & ushort.MaxValue);
        }
    }

    /// <summary>The stable identity of one fixed exterior person, which also seeds its name.</summary>
    public static string PopulationId(string sourceKey, int buildingIndex, int personIndex) =>
        $"population/{PublishedIds.Slug(sourceKey)}/{buildingIndex}/{personIndex}";

    /// <summary>Decodes one placement's ARCH3D mesh, refusing a model number the archive does not serve.</summary>
    public static Arch3dMesh Mesh(BsaArchive arch, RmbBlockModel model)
    {
        if (!uint.TryParse(model.ModelId, NumberStyles.None, CultureInfo.InvariantCulture, out uint id)
            || !arch.TryGetByNumericId(id, out BsaRecord? record) || record is null)
            throw new InvalidOperationException($"ARCH3D.BSA has no RMB model '{model.ModelId}' named by '{model.Identity}'.");
        return Arch3dDecoder.Decode(arch.GetPayload(record).Span, arch.Source, id);
    }

    internal static Arena2ImportPoint Add(Arena2ImportPoint left, Arena2ImportPoint right) =>
        new(left.XMetres + right.XMetres, left.YMetres + right.YMetres, left.ZMetres + right.ZMetres);
}
