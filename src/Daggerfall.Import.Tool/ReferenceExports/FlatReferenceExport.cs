using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>
/// The non-actor billboards the world places: the nature sets (TEXTURE.500-511) and every other archive a block places
/// as a flat, grouped by family and climate, with how many blocks place each record and how many times. Usage is what a
/// record's refresh is worth. People, editor markers and the archives no block places are left out, except that the
/// nature sets are always written, because the terrain scatters their records 1-31 without any block placing them.
/// A record whose flat casts light says so, since its art and its light source must stay together.
/// </summary>
internal static class FlatReferenceExport
{
    private const int FirstNature = 500, LastNature = 511, FirstScattered = 1, LastScattered = 31;

    // DFU DFLocation.ClimateTextureSet: each nature set; the woodland and mountain sets' winter variant is one archive up.
    private static readonly Dictionary<int, (string Family, string Climate)> Nature = new()
    {
        [500] = ("nature-rainforest", "rainforest"), [501] = ("nature-subtropical", "subtropical"), [502] = ("nature-swamp", "swamp"),
        [503] = ("nature-desert", "desert"), [504] = ("nature-temperate-woodland", "temperate"), [505] = ("nature-temperate-woodland-snow", "temperate"),
        [506] = ("nature-woodland-hills", "hills"), [507] = ("nature-woodland-hills-snow", "hills"), [508] = ("nature-haunted-woodlands", "haunted"),
        [509] = ("nature-haunted-woodlands-snow", "haunted"), [510] = ("nature-mountains", "mountains"), [511] = ("nature-mountains-snow", "mountains"),
    };

    // What each prop archive holds, read off its records; an archive not named here exports as props-NNN.
    private static readonly Dictionary<int, string> Props = new()
    {
        [53] = "props-signs", [96] = "props-remains", [97] = "props-statues", [98] = "props-statues", [100] = "props-remains",
        [182] = "props-figures", [184] = "props-figures", [190] = "props-lights", [200] = "props-household", [201] = "props-animals",
        [202] = "props-statues", [203] = "props-columns", [204] = "props-clothes", [205] = "props-containers", [206] = "props-remains",
        [207] = "props-arms", [208] = "props-instruments", [209] = "props-books", [210] = "props-lights", [211] = "props-dungeon",
        [212] = "props-town", [213] = "props-plants", [214] = "props-tools", [216] = "props-treasure", [218] = "props-kitchen",
        [300] = "props-stalagmites", [301] = "props-crops",
    };

    internal sealed class Usage
    {
        internal readonly HashSet<string> ExteriorBlocks = [], InteriorBlocks = [], DungeonBlocks = [];
        internal int Exterior, Interior, Dungeon;
    }

    public static void Run(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        Dictionary<(int Archive, int Record), Usage> usage = [];
        Usage Counted(int archive, int record)
        {
            if (!usage.TryGetValue((archive, record), out Usage? counted)) usage.Add((archive, record), counted = new());
            return counted;
        }
        void Exterior(int archive, int record, string block) { Usage u = Counted(archive, record); u.ExteriorBlocks.Add(block); u.Exterior++; }

        foreach (BlockRecord block in BlockRecordInventoryReader.Read(export.Read("BLOCKS.BSA"), "arena2/BLOCKS.BSA").Records)
        {
            if (block.RmbPlacements is { } rmb)
            {
                // A building's nature flat draws its location's climate set, so it reaches every set (RmbFlatFacts.ClimateNature).
                foreach (RmbFlatPlacement flat in rmb.Buildings.SelectMany(building => building.Exterior.Flats).Where(Scenery))
                    if (flat.TextureArchive is >= FirstNature and <= LastNature)
                        foreach (int archive in Nature.Keys) Exterior(archive, flat.TextureRecord, block.SourceKey);
                    else Exterior(flat.TextureArchive, flat.TextureRecord, block.SourceKey);
                foreach (RmbFlatPlacement flat in rmb.MiscFlats.Where(Scenery)) Exterior(flat.TextureArchive, flat.TextureRecord, block.SourceKey);
                // A ground scenery plant names only a record, which its location's climate set draws.
                foreach (RmbGroundScenery plant in block.RmbHeader!.GroundScenery)
                    foreach (int archive in Nature.Keys) Exterior(archive, plant.TextureRecord, block.SourceKey);
                foreach (RmbFlatPlacement flat in rmb.Buildings.SelectMany(building => building.Interior.Flats)
                    .Where(flat => flat.TextureArchive != RdbSourceClassification.EditorFlatArchive))
                {
                    Usage u = Counted(flat.TextureArchive, flat.TextureRecord);
                    u.InteriorBlocks.Add(block.SourceKey);
                    u.Interior++;
                }
            }
            if (block.Kind == BlockRecordKind.Rdb && block.Objects is { } rdb)
                foreach (RdbFlatPlacement flat in rdb.FlatPlacements.Where(flat => flat.TextureArchive != RdbSourceClassification.EditorFlatArchive
                    && !StaticNpcNormalization.IsDungeonNpc(flat.TextureArchive)))
                {
                    Usage u = Counted(flat.TextureArchive, flat.TextureRecord);
                    u.DungeonBlocks.Add(block.SourceKey);
                    u.Dungeon++;
                }
        }

        StringBuilder csv = new("archive,record,family,climate,width,height,frames,exterior_blocks,exterior_placements,interior_blocks,interior_placements,dungeon_blocks,dungeon_placements,scattered,light\n");
        int archives = 0, records = 0, placed = 0;
        List<string> sources = [];
        foreach (int archive in usage.Keys.Select(key => key.Archive).Concat(Nature.Keys).Distinct().Order())
        {
            (string family, string climate) = Nature.GetValueOrDefault(archive, (Props.GetValueOrDefault(archive, $"props-{archive:000}"), ""));
            string directory = $"{family}/texture-{archive:000}";
            TextureArchive texture = export.Texture(archive);
            List<ReferenceImage> images = [];
            List<object> rows = [];
            for (int record = 0; record < texture.RecordCount; record++)
            {
                Usage? counted = usage.GetValueOrDefault((archive, record));
                bool scattered = archive is >= FirstNature and <= LastNature && record is >= FirstScattered and <= LastScattered;
                if (counted is null && !scattered) continue;
                counted ??= new();
                List<ReferenceImage> frames = export.TextureImages(directory, "flat", archive, record);
                images.AddRange(frames);
                bool light = archive == DaggerfallInteriorLightFacts.Archive;
                int width = frames.FirstOrDefault()?.Width ?? 0, height = frames.FirstOrDefault()?.Height ?? 0;
                rows.Add(new { record, width, height, frames = frames.Count, exteriorBlocks = counted.ExteriorBlocks.Count, exteriorPlacements = counted.Exterior,
                    interiorBlocks = counted.InteriorBlocks.Count, interiorPlacements = counted.Interior, dungeonBlocks = counted.DungeonBlocks.Count,
                    dungeonPlacements = counted.Dungeon, scattered, light });
                csv.Append(CultureInfo.InvariantCulture, $"{archive},{record},{family},{climate},{width},{height},{frames.Count},{counted.ExteriorBlocks.Count},{counted.Exterior},{counted.InteriorBlocks.Count},{counted.Interior},{counted.DungeonBlocks.Count},{counted.Dungeon},{(scattered ? 1 : 0)},{(light ? 1 : 0)}\n");
                records++;
                if (counted.Exterior + counted.Interior + counted.Dungeon > 0) placed++;
            }
            archives++;
            sources.Add($"arena2/TEXTURE.{archive:000}");
            export.Subject(directory, $"TEXTURE.{archive:000} {family} ({rows.Count} records)",
                new { kind = "flat-archive", archive, family, climate, directory, records = rows, images }, images);
        }

        File.WriteAllText(Path.Combine(export.Root, "usage.csv"), csv.ToString());
        export.Finish("Daggerfall flat references", new { archives, records, placed },
            new { note = "Usage counts distinct blocks and placements: RMB exteriors (a building's nature flat and a ground scenery plant count toward every nature set, since they draw their location's climate), RMB interiors and RDB dungeons. Scattered records are drawn by the terrain without any block placing them." },
            "Open index.html for the visual catalog, usage.csv for every record's size, placements, terrain scatter and light.\n" +
            "Each subject is one texture archive under its family: nature sets by climate, and props by what they hold.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-flats --output local/art/reference/NEW_FLATS_DIRECTORY\n" +
            "Extracted art stays in ignored local/art/reference/.",
            sources);
    }

    /// <summary>Whether an exterior flat is scenery: not an editor marker, and not a person, who carries a faction.</summary>
    private static bool Scenery(RmbFlatPlacement flat) => flat.TextureArchive != RdbSourceClassification.EditorFlatArchive && flat.FactionId == 0;

    /// <summary>Whether an exterior flat is a person, who carries a faction.</summary>
    internal static bool Person(RmbFlatPlacement flat) => flat.TextureArchive != RdbSourceClassification.EditorFlatArchive && flat.FactionId != 0;
}
