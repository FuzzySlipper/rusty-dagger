using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Source.Tool.ReferenceExports;

/// <summary>
/// The world's placed sprites that are not people, sorted by what they show: nature (the climate sets TEXTURE.500-511 and the
/// plants, crops and stalagmites placed from other archives), props by kind, and the animals as creatures. Each record has
/// the asset id the authoring catalogs key it by, how many blocks place it and how many times; usage is what a record's
/// refresh is worth. People (any record of a people archive, with or without a faction), editor markers and the archives no
/// block places are left out, except that the nature sets are always written, because the terrain scatters their records
/// 1-31 without any block placing them. A record that casts light says so, since its art and its light source must stay
/// together.
/// </summary>
internal static class SceneryReferenceExport
{
    private const int FirstNature = 500, LastNature = 511, FirstScattered = 1, LastScattered = 31;

    private sealed record Category(string Prefix, string Directory);
    private static readonly Category Nature = new("nature", "nature"), Prop = new("prop", "props"), Creature = new("creature", "creatures");

    // DFU DFLocation.ClimateTextureSet: each nature set's climate; the woodland and mountain sets' winter variant is one archive up.
    // The flora review sorts a set's records into kinds by what each is painted as.
    private static readonly Dictionary<int, string> Climates = new()
    {
        [500] = "rainforest", [501] = "subtropical", [502] = "swamp", [503] = "desert", [504] = "temperate", [505] = "temperate-snow",
        [506] = "hills", [507] = "hills-snow", [508] = "haunted", [509] = "haunted-snow", [510] = "mountains", [511] = "mountains-snow",
    };

    // What each other archive holds, read off its records; an archive not named here exports as an unsorted prop.
    private static readonly Dictionary<int, (Category Category, string Kind)> Archives = new()
    {
        [53] = (Prop, "sign"), [96] = (Prop, "remains"), [97] = (Prop, "statue"), [98] = (Prop, "statue"), [100] = (Prop, "remains"),
        [190] = (Prop, "light"), [200] = (Prop, "household"), [201] = (Creature, "animal"), [202] = (Prop, "statue"), [203] = (Prop, "column"),
        [204] = (Prop, "clothes"), [205] = (Prop, "container"), [206] = (Prop, "remains"), [207] = (Prop, "arms"), [208] = (Prop, "instrument"),
        [209] = (Prop, "book"), [210] = (Prop, "light"), [211] = (Prop, "dungeon"), [212] = (Prop, "town"), [213] = (Nature, "plant"),
        [214] = (Prop, "tool"), [216] = (Prop, "treasure"), [218] = (Prop, "kitchen"), [300] = (Nature, "rock"), [301] = (Nature, "crop"),
    };

    internal sealed class Usage
    {
        internal readonly HashSet<string> ExteriorBlocks = [], InteriorBlocks = [], DungeonBlocks = [];
        internal int Exterior, Interior, Dungeon;
    }

    public static void Run(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        HashSet<int> people = [.. PeopleReferenceExport.StaticArchives];
        Dictionary<(int Archive, int Record), Usage> usage = [];
        Usage Counted(int archive, int record)
        {
            if (!usage.TryGetValue((archive, record), out Usage? counted)) usage.Add((archive, record), counted = new());
            return counted;
        }
        void Exterior(int archive, int record, string block) { Usage u = Counted(archive, record); u.ExteriorBlocks.Add(block); u.Exterior++; }
        bool Shown(int archive) => archive != RdbSourceClassification.EditorFlatArchive && !people.Contains(archive);

        foreach (BlockRecord block in BlockRecordInventoryReader.Read(export.Read("BLOCKS.BSA"), "arena2/BLOCKS.BSA").Records)
        {
            if (block.RmbPlacements is { } rmb)
            {
                // A building's nature sprite draws its location's climate set, so it reaches every set (RmbFlatFacts.ClimateNature).
                foreach (RmbFlatPlacement flat in rmb.Buildings.SelectMany(building => building.Exterior.Flats).Where(Scenery))
                    if (flat.TextureArchive is >= FirstNature and <= LastNature)
                        foreach (int archive in Climates.Keys) Exterior(archive, flat.TextureRecord, block.SourceKey);
                    else Exterior(flat.TextureArchive, flat.TextureRecord, block.SourceKey);
                foreach (RmbFlatPlacement flat in rmb.MiscFlats.Where(Scenery)) Exterior(flat.TextureArchive, flat.TextureRecord, block.SourceKey);
                // A ground scenery plant names only a record, which its location's climate set draws.
                foreach (RmbGroundScenery plant in block.RmbHeader!.GroundScenery)
                    foreach (int archive in Climates.Keys) Exterior(archive, plant.TextureRecord, block.SourceKey);
                foreach (RmbFlatPlacement flat in rmb.Buildings.SelectMany(building => building.Interior.Flats).Where(flat => Shown(flat.TextureArchive)))
                {
                    Usage u = Counted(flat.TextureArchive, flat.TextureRecord);
                    u.InteriorBlocks.Add(block.SourceKey);
                    u.Interior++;
                }
            }
            if (block.Kind == BlockRecordKind.Rdb && block.Objects is { } rdb)
                foreach (RdbFlatPlacement flat in rdb.FlatPlacements.Where(flat => Shown(flat.TextureArchive) && !StaticNpcNormalization.IsDungeonNpc(flat.TextureArchive)))
                {
                    Usage u = Counted(flat.TextureArchive, flat.TextureRecord);
                    u.DungeonBlocks.Add(block.SourceKey);
                    u.Dungeon++;
                }
        }

        StringBuilder csv = new("id,category,kind,archive,record,climate,directory,width,height,frames,exterior_blocks,exterior_placements,interior_blocks,interior_placements,dungeon_blocks,dungeon_placements,scattered,light\n");
        int archives = 0, records = 0, placed = 0;
        List<string> sources = [];
        foreach (int archive in usage.Keys.Select(key => key.Archive).Concat(Climates.Keys).Where(Shown).Distinct().Order())
        {
            string climate = Climates.GetValueOrDefault(archive, "");
            (Category category, string kind) = climate.Length > 0 ? (Nature, "") : Archives.GetValueOrDefault(archive, (Prop, ""));
            string directory = $"{category.Directory}/texture-{archive:000}";
            TextureArchive texture = export.Texture(archive);
            List<ReferenceImage> images = [];
            List<object> rows = [];
            for (int record = 0; record < texture.RecordCount; record++)
            {
                Usage? counted = usage.GetValueOrDefault((archive, record));
                bool scattered = climate.Length > 0 && record is >= FirstScattered and <= LastScattered;
                if (counted is null && !scattered) continue;
                counted ??= new();
                string id = $"{category.Prefix}-{archive:000}-{record}";
                List<ReferenceImage> frames = export.TextureImages(directory, "sprite", archive, record);
                images.AddRange(frames);
                bool light = archive == DaggerfallInteriorLightFacts.Archive;
                int width = frames.FirstOrDefault()?.Width ?? 0, height = frames.FirstOrDefault()?.Height ?? 0;
                rows.Add(new { id, record, kind, width, height, frames = frames.Count, exteriorBlocks = counted.ExteriorBlocks.Count, exteriorPlacements = counted.Exterior,
                    interiorBlocks = counted.InteriorBlocks.Count, interiorPlacements = counted.Interior, dungeonBlocks = counted.DungeonBlocks.Count,
                    dungeonPlacements = counted.Dungeon, scattered, light });
                csv.Append(CultureInfo.InvariantCulture, $"{id},{category.Prefix},{kind},{archive},{record},{climate},{directory},{width},{height},{frames.Count},{counted.ExteriorBlocks.Count},{counted.Exterior},{counted.InteriorBlocks.Count},{counted.Interior},{counted.DungeonBlocks.Count},{counted.Dungeon},{(scattered ? 1 : 0)},{(light ? 1 : 0)}\n");
                records++;
                if (counted.Exterior + counted.Interior + counted.Dungeon > 0) placed++;
            }
            archives++;
            sources.Add($"arena2/TEXTURE.{archive:000}");
            string title = climate.Length > 0 ? $"{category.Prefix} {climate}" : $"{category.Prefix} {(kind.Length > 0 ? kind : "unsorted")}";
            export.Subject(directory, $"TEXTURE.{archive:000} {title} ({rows.Count} records)",
                new { kind = "scenery-archive", archive, category = category.Prefix, climate, directory, records = rows, images }, images);
        }

        File.WriteAllText(Path.Combine(export.Root, "usage.csv"), csv.ToString());
        export.Finish("Daggerfall scenery references", new { archives, records, placed },
            new { note = "Usage counts distinct blocks and placements: RMB exteriors (a building's nature sprite and a ground scenery plant count toward every nature set, since they draw their location's climate), RMB interiors and RDB dungeons. Scattered records are drawn by the terrain without any block placing them." },
            "Open index.html for the visual catalog, usage.csv for every record's asset id, category, kind, size, placements, terrain scatter and light.\n" +
            "Each subject is one texture archive under its category: nature/, props/ and creatures/.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Source.Tool -- export-scenery --output local/art/reference/NEW_SCENERY_DIRECTORY\n" +
            "Extracted art stays in ignored local/art/reference/.",
            sources);
    }

    /// <summary>Whether an exterior placement is scenery: not an editor marker, and not a person, who carries a faction.</summary>
    private static bool Scenery(RmbFlatPlacement flat) => flat.TextureArchive != RdbSourceClassification.EditorFlatArchive && flat.FactionId == 0
        && !PeopleReferenceExport.StaticArchives.Contains(flat.TextureArchive);

    /// <summary>Whether an exterior placement is a person, who carries a faction.</summary>
    internal static bool Person(RmbFlatPlacement flat) => flat.TextureArchive != RdbSourceClassification.EditorFlatArchive && flat.FactionId != 0;
}
