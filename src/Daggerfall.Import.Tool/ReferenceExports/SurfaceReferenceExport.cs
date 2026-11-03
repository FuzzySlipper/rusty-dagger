using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>
/// The surface textures walls, floors, ceilings, roofs and doors draw (TEXTURE.000-199 and 300-499; the 200s are item
/// and paper-doll art and the 500s nature billboards), grouped by their classic texture set across climates, with each
/// record's size and how many blocks can draw it: town exteriors and interiors (RMB) after the climate and season swap,
/// and dungeons (RDB) after the classic dungeon texture tables. Usage is what a record's refresh is worth. How the
/// models lay a record says how it must tile: the planes it covers (floors and ceilings lie flat, walls stand) and how
/// far their texture coordinates run past the record on each axis, which is where it repeats.
/// </summary>
internal static class SurfaceReferenceExport
{
    // DFU DFLocation.ClimateTextureSet: what each set (archive % 100) holds. A set's winter variant is one archive up.
    private static readonly Dictionary<int, string> Sets = new()
    {
        [2] = "exterior-terrain", [7] = "exterior-ruins", [8] = "exterior-ruins-2", [9] = "exterior-castle",
        [10] = "exterior-castle-snow", [11] = "interior-castle", [12] = "exterior-city-a", [13] = "exterior-city-a-snow",
        [14] = "exterior-city-b", [15] = "exterior-city-b-snow", [16] = "interior-city", [17] = "exterior-city-walls",
        [18] = "exterior-city-walls-snow", [19] = "interior-crypt-a", [20] = "interior-crypt-b", [22] = "interior-dungeons-a",
        [23] = "interior-dungeons-b", [24] = "interior-dungeons-c", [25] = "interior-dungeons-new-cs", [26] = "exterior-farm",
        [27] = "exterior-farm-snow", [28] = "interior-farm", [29] = "exterior-fences", [30] = "exterior-fences-snow",
        [33] = "exterior-hedges", [35] = "exterior-mages-guild", [36] = "exterior-mages-guild-snow",
        [37] = "interior-mages-guild", [38] = "exterior-manor", [39] = "exterior-manor-snow", [40] = "interior-manor",
        [41] = "interior-marble-floors", [42] = "exterior-merchant-homes", [43] = "exterior-merchant-homes-snow",
        [44] = "interior-merchant-homes", [45] = "interior-mines", [46] = "misc", [47] = "interior-caves",
        [48] = "interior-paintings", [58] = "exterior-tavern", [59] = "exterior-tavern-snow", [60] = "interior-tavern",
        [61] = "exterior-temple", [62] = "exterior-temple-snow", [63] = "interior-temple", [64] = "exterior-village",
        [65] = "exterior-village-snow", [66] = "interior-village", [67] = "exterior-misc-wood-planks", [68] = "interior-sewer",
        [69] = "exterior-roofs", [70] = "exterior-roofs-snow", [71] = "exterior-barn", [72] = "exterior-barn-snow",
        [74] = "exterior-doors", [79] = "exterior-city-spec", [80] = "exterior-city-spec-snow", [82] = "exterior-city-spec-b",
        [83] = "exterior-city-spec-b-snow", [85] = "exterior-base-textures", [88] = "exterior-wagon", [90] = "interior-misc-furniture",
    };

    private static readonly string[] Climates = ["desert", "mountain", "", "temperate", "swamp"];

    // Classic dungeon texture tables (DungeonTextureTableTransform): the five crypt and dungeon slots draw from one climate
    // base's x19/x20/x22/x23 (offset two reads as four), the sixth from its sewer x68, and doors from its x74.
    private static readonly int[] ClimateBases = [0, 100, 300, 400];
    private static readonly int[] TableArchives = [119, 120, 122, 123, 124];

    private sealed record Usage(HashSet<string> Exterior, HashSet<string> Interior, HashSet<string> Dungeon);

    /// <summary>How the models lay one texture record: flat and standing planes, and the widest texture-coordinate span on each axis.</summary>
    private sealed class Layout
    {
        internal int Flat;
        internal int Standing;
        internal int SpanU;
        internal int SpanV;
    }

    private static Layout Layouts(Dictionary<int, Layout> layouts, int key)
    {
        if (!layouts.TryGetValue(key, out Layout? layout)) layouts.Add(key, layout = new());
        return layout;
    }

    private static System.Numerics.Vector3 Point(Arch3dPoint point) => new(point.X, point.Y, point.Z);

    public static void Run(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        Dictionary<string, IReadOnlyList<Arch3dTextureReference>> models = new(StringComparer.Ordinal);
        foreach (Arch3dMeshRecord mesh in Arch3dInventoryReader.Read(export.Read(Arch3dInventoryReader.FileName), "arena2/ARCH3D.BSA").Records)
            if (mesh.DuplicateOf is null && mesh.Facts is { } facts)
                models.TryAdd(mesh.RecordId.ToString(CultureInfo.InvariantCulture), facts.Textures);
        Dictionary<int, Layout> layouts = [];
        BsaArchive meshes = BsaArchive.Parse(export.Read(Arch3dInventoryReader.FileName), "arena2/ARCH3D.BSA");
        foreach (BsaRecord record in meshes.Records)
        {
            Arch3dMesh mesh;
            try { mesh = Arch3dDecoder.Decode(meshes.GetPayload(record).Span, "arena2/ARCH3D.BSA", record.NumericId!.Value); }
            catch (Arena2FormatException) { continue; }
            foreach (Arch3dPlane plane in mesh.Planes.Where(plane => plane.Points.Count >= 3))
            {
                Layout layout = Layouts(layouts, plane.TextureArchive * 1000 + plane.TextureRecord);
                System.Numerics.Vector3 a = Point(plane.Points[0]), b = Point(plane.Points[1]), c = Point(plane.Points[2]);
                System.Numerics.Vector3 normal = System.Numerics.Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() == 0) continue;
                if (Math.Abs(System.Numerics.Vector3.Normalize(normal).Y) > 0.7F) layout.Flat++;
                else layout.Standing++;
                layout.SpanU = Math.Max(layout.SpanU, plane.Points.Max(p => p.U) - plane.Points.Min(p => p.U));
                layout.SpanV = Math.Max(layout.SpanV, plane.Points.Max(p => p.V) - plane.Points.Min(p => p.V));
            }
        }

        Dictionary<(int Archive, int Record), Usage> usage = [];
        void Count(IEnumerable<string> modelIds, string block, Func<Usage, HashSet<string>> side, Func<int, int, IEnumerable<int>> reach)
        {
            foreach (string model in modelIds)
                foreach (Arch3dTextureReference texture in models.GetValueOrDefault(model) ?? [])
                    foreach (int archive in reach(texture.Archive, texture.Record))
                    {
                        if (!usage.TryGetValue((archive, texture.Record), out Usage? counted))
                            usage.Add((archive, texture.Record), counted = new([], [], []));
                        side(counted).Add(block);
                    }
        }

        IEnumerable<int> Swapped(int archive, int record, bool seasons) =>
            Enum.GetValues<ClassicClimateBase>().SelectMany(climate =>
                (seasons ? Enum.GetValues<ClassicClimateSeason>() : [ClassicClimateSeason.Summer])
                    .Select(season => ClassicClimateSwaps.Apply(archive, record, climate, season))).Distinct();
        IEnumerable<int> Tabled(int archive, int record) => archive switch
        {
            _ when TableArchives.Contains(archive) => ClimateBases.SelectMany(b => new[] { 19, 20, 22, 23 }.Select(o => b + o)),
            168 => ClimateBases.Select(b => b + 68),
            74 => ClimateBases.Select(b => b + 74),
            _ => [archive],
        };

        foreach (BlockRecord block in BlockRecordInventoryReader.Read(export.Read("BLOCKS.BSA"), "arena2/BLOCKS.BSA").Records)
        {
            if (block.RmbPlacements is { } rmb)
            {
                Count(rmb.Buildings.SelectMany(b => b.Exterior.Models).Concat(rmb.MiscModels).Select(m => m.ModelId), block.SourceKey,
                    u => u.Exterior, (a, r) => Swapped(a, r, seasons: true));
                Count(rmb.Buildings.SelectMany(b => b.Interior.Models).Select(m => m.ModelId), block.SourceKey,
                    u => u.Interior, (a, r) => Swapped(a, r, seasons: false));
            }
            if (block.Kind == BlockRecordKind.Rdb && block.Objects is { } rdb)
                Count(rdb.ModelIds, block.SourceKey, u => u.Dungeon, Tabled);
        }

        // A swapped or tabled variant is laid as its source record is.
        Dictionary<int, Layout> reached = [];
        void Inherit(int from, IEnumerable<int> to)
        {
            if (!layouts.TryGetValue(from, out Layout? source)) return;
            foreach (int key in to)
            {
                Layout target = Layouts(reached, key);
                target.Flat += source.Flat; target.Standing += source.Standing;
                target.SpanU = Math.Max(target.SpanU, source.SpanU); target.SpanV = Math.Max(target.SpanV, source.SpanV);
            }
        }
        foreach (int key in layouts.Keys.ToArray())
        {
            int archive = key / 1000, record = key % 1000;
            Inherit(key, Swapped(archive, record, seasons: true).Concat(Tabled(archive, record)).Append(archive).Distinct().Select(a => a * 1000 + record));
        }

        StringBuilder csv = new("archive,record,set,climate,width,height,frames,exterior_blocks,interior_blocks,dungeon_blocks,flat_planes,standing_planes,span_u,span_v\n");
        int archives = 0, records = 0, used = 0;
        List<string> sources = [];
        foreach (string name in export.FileNames.Where(file => file.StartsWith("TEXTURE.", StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse(name.AsSpan(8), CultureInfo.InvariantCulture, out int archive)) continue;
            if (archive is >= 200 and < 300 or >= 500) continue;
            string set = Sets.GetValueOrDefault(archive % 100, $"set-{archive % 100:00}");
            string climate = Climates[archive / 100];
            string directory = $"{set}/texture-{archive:000}";
            TextureArchive texture = export.Texture(archive);
            List<ReferenceImage> images = [];
            List<object> rows = [];
            for (int record = 0; record < texture.RecordCount; record++)
            {
                List<ReferenceImage> frames = export.TextureImages(directory, "surface", archive, record);
                images.AddRange(frames);
                Usage counted = usage.GetValueOrDefault((archive, record)) ?? new([], [], []);
                Layout laid = reached.GetValueOrDefault(archive * 1000 + record) ?? new();
                int width = frames.FirstOrDefault()?.Width ?? 0, height = frames.FirstOrDefault()?.Height ?? 0;
                rows.Add(new { record, width, height, frames = frames.Count, exteriorBlocks = counted.Exterior.Count,
                    interiorBlocks = counted.Interior.Count, dungeonBlocks = counted.Dungeon.Count,
                    flatPlanes = laid.Flat, standingPlanes = laid.Standing, spanU = laid.SpanU, spanV = laid.SpanV });
                csv.Append(CultureInfo.InvariantCulture, $"{archive},{record},{set},{climate},{width},{height},{frames.Count},{counted.Exterior.Count},{counted.Interior.Count},{counted.Dungeon.Count},{laid.Flat},{laid.Standing},{laid.SpanU},{laid.SpanV}\n");
                records++;
                if (counted.Exterior.Count + counted.Interior.Count + counted.Dungeon.Count > 0) used++;
            }
            archives++;
            sources.Add("arena2/" + name);
            export.Subject(directory, $"{name} {set} ({climate}, {texture.RecordCount} records)",
                new { kind = "surface-archive", archive, set, climate, directory, records = rows, images }, images);
        }

        File.WriteAllText(Path.Combine(export.Root, "usage.csv"), csv.ToString());
        export.Finish("Daggerfall surface references", new { archives, records, used },
            new { note = "Usage counts distinct blocks: RMB exteriors through every climate and season swap, RMB interiors through every climate, RDB dungeons through every classic texture table. A record no block reaches may still be drawn by a placed flat or another path; check before skipping it." },
            "Open index.html for the visual catalog, usage.csv for every record's size and block usage.\n" +
            "Each subject is one texture archive under its classic texture set (DFU DFLocation.ClimateTextureSet); a set's climates are 0xx desert, 1xx mountain, 3xx temperate, 4xx swamp, and its winter variant is one archive up.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-surfaces --output authoring/df-raw/NEW_SURFACES_DIRECTORY\n" +
            "Extracted art stays in ignored authoring/df-raw/.",
            sources);
    }
}
