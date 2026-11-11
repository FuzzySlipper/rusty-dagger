using System.Globalization;
using System.Text;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>
/// The ground tiles exterior terrain draws: each climate's TEXTURE.x02 summer set, its x03 winter set and its x04 rain
/// set, 56 records in one layout (bases, the transitions between them, paving and a ploughed field), grouped by climate
/// and season. The desert's winter set is left out: the climate swap never draws it.
/// </summary>
internal static class TerrainReferenceExport
{
    private static readonly (string Climate, int Base)[] Climates = [("desert", 0), ("mountain", 100), ("temperate", 300), ("swamp", 400)];
    private static readonly (string Season, int Offset)[] Seasons = [("summer", 2), ("winter", 3), ("rain", 4)];

    public static void Run(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        StringBuilder csv = new("archive,record,climate,season,width,height\n");
        List<string> sources = [];
        int archives = 0, records = 0;
        foreach ((string climate, int climateBase) in Climates)
            foreach ((string season, int offset) in Seasons)
            {
                if (climate == "desert" && season == "winter") continue;
                int archive = climateBase + offset;
                string directory = $"{climate}-{season}/texture-{archive:000}";
                List<ReferenceImage> images = [];
                for (int record = 0; record < export.Texture(archive).RecordCount; record++)
                {
                    List<ReferenceImage> frames = export.TextureImages(directory, "tile", archive, record);
                    images.AddRange(frames);
                    csv.Append(CultureInfo.InvariantCulture, $"{archive},{record},{climate},{season},{frames[0].Width},{frames[0].Height}\n");
                    records++;
                }
                archives++;
                sources.Add($"arena2/TEXTURE.{archive:000}");
                export.Subject(directory, $"TEXTURE.{archive:000} {climate} {season} ground", new { kind = "terrain-archive", archive, climate, season, directory, images }, images);
            }

        File.WriteAllText(Path.Combine(export.Root, "tiles.csv"), csv.ToString());
        export.Finish("Daggerfall terrain references", new { archives, records },
            new { note = "Every set shares one record layout, so a record is the same ground in each climate and season." },
            "Open index.html for the visual catalog, tiles.csv for every record's climate, season and size.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-terrain --output local/art/reference/NEW_TERRAIN_DIRECTORY\n" +
            "Extracted art stays in ignored local/art/reference/.",
            sources);
    }
}
