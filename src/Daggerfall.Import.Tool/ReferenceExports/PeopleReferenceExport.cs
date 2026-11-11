using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>
/// The people the world shows: static NPC billboards and the townspeople who wander, every record and frame, with how many
/// blocks place each record and how many times. An unplaced record stays, because quests and guild services pick people
/// by record without a block placing them. Enemy classes are the mobile export's.
/// </summary>
internal static class PeopleReferenceExport
{
    // Each static people archive by what TEXTURE's own name says it holds; a placed archive not named here exports as people-NNN.
    private static readonly Dictionary<int, string> Static = new()
    {
        [175] = "people-daedra-lords", [176] = "people-dark-brotherhood", [177] = "people-mages", [178] = "people-necromancers",
        [179] = "people-witches", [180] = "people-courtiers", [181] = "people-temple", [182] = "people-common", [183] = "people-noble",
        [184] = "people-common-2", [186] = "people-big", [197] = "people-town", [334] = "people-daggerfall", [346] = "people-wayrest",
        [357] = "people-sentinel",
    };

    private static readonly int[] Townspeople = [.. Enumerable.Range(381, 18), .. Enumerable.Range(451, 6)];

    public static void Run(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        Dictionary<(int Archive, int Record), FlatReferenceExport.Usage> usage = [];
        FlatReferenceExport.Usage Counted(int archive, int record)
        {
            if (!usage.TryGetValue((archive, record), out FlatReferenceExport.Usage? counted)) usage.Add((archive, record), counted = new());
            return counted;
        }
        void Exterior(int archive, int record, string block) { FlatReferenceExport.Usage u = Counted(archive, record); u.ExteriorBlocks.Add(block); u.Exterior++; }
        void Interior(int archive, int record, string block) { FlatReferenceExport.Usage u = Counted(archive, record); u.InteriorBlocks.Add(block); u.Interior++; }

        foreach (BlockRecord block in BlockRecordInventoryReader.Read(export.Read("BLOCKS.BSA"), "arena2/BLOCKS.BSA").Records)
        {
            if (block.RmbPlacements is { } rmb)
            {
                foreach (RmbFlatPlacement flat in rmb.Buildings.SelectMany(building => building.Exterior.Flats).Concat(rmb.MiscFlats).Where(FlatReferenceExport.Person))
                    Exterior(flat.TextureArchive, flat.TextureRecord, block.SourceKey);
                foreach (RmbPeoplePlacement person in rmb.Buildings.SelectMany(building => building.Exterior.People))
                    Exterior(person.TextureArchive, person.TextureRecord, block.SourceKey);
                foreach (RmbPeoplePlacement person in rmb.Buildings.SelectMany(building => building.Interior.People))
                    Interior(person.TextureArchive, person.TextureRecord, block.SourceKey);
            }
            if (block.Kind == BlockRecordKind.Rdb && block.Objects is { } rdb)
                foreach (RdbFlatPlacement flat in rdb.FlatPlacements.Where(flat => StaticNpcNormalization.IsDungeonNpc(flat.TextureArchive)))
                {
                    FlatReferenceExport.Usage u = Counted(flat.TextureArchive, flat.TextureRecord);
                    u.DungeonBlocks.Add(block.SourceKey);
                    u.Dungeon++;
                }
        }

        StringBuilder csv = new("archive,record,family,width,height,frames,exterior_blocks,exterior_placements,interior_blocks,interior_placements,dungeon_blocks,dungeon_placements,wandering\n");
        int archives = 0, records = 0, placed = 0;
        List<string> sources = [];
        foreach (int archive in usage.Keys.Select(key => key.Archive).Concat(Static.Keys).Concat(Townspeople).Distinct().Order())
        {
            bool wandering = Townspeople.Contains(archive);
            string family = wandering ? "townspeople" : Static.GetValueOrDefault(archive, $"people-{archive:000}");
            string directory = $"{family}/texture-{archive:000}";
            TextureArchive texture = export.Texture(archive);
            List<ReferenceImage> images = [];
            List<object> rows = [];
            for (int record = 0; record < texture.RecordCount; record++)
            {
                // An archive only reached by a placement, such as a lit prop carrying a faction, is not a people archive.
                if (!wandering && !Static.ContainsKey(archive) && !usage.ContainsKey((archive, record))) continue;
                FlatReferenceExport.Usage counted = usage.GetValueOrDefault((archive, record)) ?? new();
                List<ReferenceImage> frames = export.TextureImages(directory, "person", archive, record);
                images.AddRange(frames);
                int width = frames.FirstOrDefault()?.Width ?? 0, height = frames.FirstOrDefault()?.Height ?? 0;
                rows.Add(new { record, width, height, frames = frames.Count, exteriorBlocks = counted.ExteriorBlocks.Count, exteriorPlacements = counted.Exterior,
                    interiorBlocks = counted.InteriorBlocks.Count, interiorPlacements = counted.Interior, dungeonBlocks = counted.DungeonBlocks.Count,
                    dungeonPlacements = counted.Dungeon, wandering });
                csv.Append(CultureInfo.InvariantCulture, $"{archive},{record},{family},{width},{height},{frames.Count},{counted.ExteriorBlocks.Count},{counted.Exterior},{counted.InteriorBlocks.Count},{counted.Interior},{counted.DungeonBlocks.Count},{counted.Dungeon},{(wandering ? 1 : 0)}\n");
                records++;
                if (counted.Exterior + counted.Interior + counted.Dungeon > 0) placed++;
            }
            archives++;
            sources.Add($"arena2/TEXTURE.{archive:000}");
            export.Subject(directory, $"TEXTURE.{archive:000} {family} ({rows.Count} records)",
                new { kind = "people-archive", archive, family, directory, wandering, records = rows, images }, images);
        }

        File.WriteAllText(Path.Combine(export.Root, "usage.csv"), csv.ToString());
        export.Finish("Daggerfall people references", new { archives, records, placed },
            new { note = "Usage counts distinct blocks and placements: RMB exterior people and faction flats, RMB interior people, and RDB flats in the layout NPC archives. Townspeople wander, so no block places them." },
            "Open index.html for the visual catalog, usage.csv for every record's size, frames and placements.\n" +
            "Each subject is one texture archive: static NPC billboards by archive, and the wandering townspeople's outfits under townspeople/.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-people --output local/art/reference/NEW_PEOPLE_DIRECTORY\n" +
            "Extracted art stays in ignored local/art/reference/.",
            sources);
    }
}
