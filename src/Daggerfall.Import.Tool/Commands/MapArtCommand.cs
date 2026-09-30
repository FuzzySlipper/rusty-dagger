using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Enumerates the map, automap, travel and town artwork against the documented inventory, so a region,
/// call-site or palette binding is a stated source fact rather than a filename a consumer would have to
/// know. Pixels stay in the corpus; only the bindings publish.
/// </summary>
internal static class MapArtCommand
{
    public static ToolCommand Command { get; } = new("map-art", [Options.Arena2, CommandOption.Optional("--inventory", "CSV")], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        List<(string Name, string Path, byte[] Bytes)> sources = [];
        foreach (string pattern in new[] { "FMAP*.IMG", "AMAP*.IMG", "TMAP*.IMG", "TRAV*.IMG", "TOWN*.IMG", "FMAP_PAL.COL", "MAP.PAL" })
        {
            foreach (string path in Directory.EnumerateFiles(arena2, pattern).Order(StringComparer.Ordinal))
            {
                sources.Add((Path.GetFileName(path), path.Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes(path)));
            }
        }

        MapArtInventory inventory = MapArtInventory.Enumerate(sources, Options.CorpusLabel(arena2));
        Console.WriteLine($"map art: {inventory.Records.Count(record => record.Disposition == MapArtDisposition.Decoded)} decoded, {inventory.Records.Count(record => record.Disposition == MapArtDisposition.Unsupported)} unsupported, {inventory.Records.Count(record => record.Disposition == MapArtDisposition.NotSupplied)} not supplied, {inventory.Records.Count(record => record.Disposition is MapArtDisposition.Malformed or MapArtDisposition.Unreadable)} unreadable");
        foreach (MapArtRecord record in inventory.Records.Where(record => record.Disposition is MapArtDisposition.Malformed or MapArtDisposition.Unreadable))
        {
            Console.WriteLine($"  unreadable {record.FileName}: {record.Note}");
        }

        if (!args.Has("--inventory")) return 0;
        return Options.CheckDocumentedFamily(
            Options.DocumentedFiles(Options.ReadInventory(args), row => row.FamilyId == "CNT-022" || row.Id == "CNT-027.file.MAP.PAL"),
            [.. inventory.Records.Where(record => record.Disposition != MapArtDisposition.NotSupplied).Select(record => record.FileName)],
            $"map art files ({inventory.Records.Count(record => record.Disposition == MapArtDisposition.NotSupplied)} region slots without screens)");
    }
}
