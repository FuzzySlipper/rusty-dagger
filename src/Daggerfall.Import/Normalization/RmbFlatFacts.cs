using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>RMB decorative flats and their light sources, shared by block and site publication.</summary>
internal sealed class RmbFlatFacts(DungeonLogicalSourceSet sources)
{
    /// <summary>The nature set ground scenery names until its location's climate selects the one it draws.</summary>
    private const int GroundSceneryArchive = 504;

    private readonly Dictionary<int, TextureArchive> textures = [];

    internal (NormalizedBillboardPlacement[] Billboards, NormalizedLightPlacement[] Lights) Read(
        RmbBlockContent content, int? interior = null)
    {
        List<NormalizedBillboardPlacement> billboards = [];
        List<NormalizedLightPlacement> lights = [];
        string scope = PublishedIds.Slug(content.SourceKey);
        foreach (var flat in interior is int building ? content.InteriorFlats(building) : content.ExteriorFlats)
        {
            if (flat.TextureArchive == RdbSourceClassification.EditorFlatArchive
                || interior is null && flat.Source.FactionId != 0) continue;
            var size = WorldSize(flat.TextureArchive, flat.TextureRecord);
            Arena2ImportPoint point = flat.Point;
            if (interior is null && flat.BuildingIndex is int index)
            {
                // RMBLayout positions building flats by translation, independently of the model yaw.
                point = RmbBlockContent.Add(Arena2SourceTransform.ToRmbBuildingOrigin(content.Summary.Buildings[index]),
                    Arena2SourceTransform.ToRmbImportPoint(flat.Source.X, flat.Source.Y, flat.Source.Z));
            }
            var at = MeshGeometry.ToRightHanded(point);
            var basePosition = at with { Y = at.Y + (interior is null ? -6f * .025f : 0f) };
            string suffix = $"{scope}/{flat.Source.SourceOffset}";
            billboards.Add(new($"billboard/{suffix}", $"sprite/texture-{flat.TextureArchive}-{flat.TextureRecord}", basePosition,
                new(size.WidthMeters, size.HeightMeters))
            {
                BaseAligned = true,
                ClimateNature = interior is null && flat.BuildingIndex is not null && flat.TextureArchive is >= 500 and <= 511,
            });
            if (flat.TextureArchive != DaggerfallInteriorLightFacts.Archive) continue;
            if (interior is null)
                lights.Add(new($"light-flat/{suffix}", at with { Y = at.Y + size.HeightMeters }, 18f, 1f, new(1f, 1f, 1f)) { CityLight = true });
            else if (DaggerfallInteriorLightFacts.TryProject($"light-flat/{suffix}", flat.TextureArchive, flat.TextureRecord,
                at with { Y = at.Y + size.HeightMeters / 2f }, size.HeightMeters, out var light)) lights.Add(light);
        }
        if (interior is null)
            foreach (RmbBlockScenery plant in content.GroundScenery)
            {
                var size = WorldSize(GroundSceneryArchive, plant.Source.TextureRecord);
                billboards.Add(new($"billboard/{scope}/scenery-{plant.Source.X}-{plant.Source.Y}",
                    $"sprite/texture-{GroundSceneryArchive}-{plant.Source.TextureRecord}", MeshGeometry.ToRightHanded(plant.Point),
                    new(size.WidthMeters, size.HeightMeters)) { BaseAligned = true, ClimateNature = true });
            }
        return ([.. billboards], [.. lights]);
    }

    private Arena2RecordWorldSize WorldSize(int archive, int record)
    {
        if (!textures.TryGetValue(archive, out var texture))
        {
            var source = sources.Require($"TEXTURE.{archive:000}");
            textures.Add(archive, texture = TextureArchive.Parse(source.Bytes.Span, source.Label));
        }
        var info = texture.GetRecordInfo(record);
        return MobileSourceMetadata.GetRecordWorldSize(info.Width, info.Height, info.ScaleX, info.ScaleY);
    }
}
