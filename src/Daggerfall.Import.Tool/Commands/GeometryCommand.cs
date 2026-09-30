using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Enumerates the classic mesh archive into the import records, so a mesh identity is published whether or
/// not anything references it and the blocks that do reference one can be answered.
/// </summary>
/// <remarks>
/// The use sites come from the block document: which blocks name a mesh is a fact the block inventory
/// publishes, and reading it there keeps one owner for it. Running this before blocks exists is refused
/// rather than publishing an inventory whose use sites are silently empty. Nothing at runtime reads the
/// inventory, so it is written to the import records rather than into the runtime content root.
/// </remarks>
internal static class GeometryCommand
{
    public static ToolCommand Command { get; } = new("geometry",
        [Options.Arena2, CommandOption.Required("--blocks", "BLOCKS.json"), Options.Records, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string blocksPayload = args["--blocks"];
        if (!File.Exists(blocksPayload))
        {
            throw new ArgumentException($"there is no block document at '{blocksPayload}', which is where mesh use sites come from: run the blocks command first");
        }

        // The document is read through its own contract rather than by matching member names here. A document
        // this build cannot read has to refuse: walking members by name would fold a shape it does not
        // recognize into a geometry section where every mesh is unused.
        DaggerfallBlocks publishedBlocks = JsonSerializer.Deserialize<DaggerfallBlocks>(File.ReadAllBytes(blocksPayload), PublishedJson.SectionRead)
            ?? throw new ArgumentException($"the block document '{blocksPayload}' could not be read");
        publishedBlocks.Validate();
        DaggerfallGeometryUseSite[] useSites = [.. publishedBlocks.Records
            .SelectMany(block => (block.Objects?.ModelIds ?? []).Select(model => new DaggerfallGeometryUseSite(model, block.SourceKey)))];

        DaggerfallGeometry geometry = DaggerfallGeometryBuilder.Build(
            File.ReadAllBytes(Path.Combine(args["--arena2"], Arch3dInventoryReader.FileName)),
            Options.Arena2Label(Arch3dInventoryReader.FileName),
            Options.ReadInventory(args),
            useSites);

        DaggerfallGeometrySource publishedSource = geometry.Sources[0];
        Console.WriteLine($"geometry: {geometry.Records.Count} records from {publishedSource.Path}, declared {publishedSource.DeclaredLength}, {geometry.Records.Count(record => record.State == DaggerfallGeometryState.Malformed)} malformed, {geometry.Records.Sum(record => record.Facts?.Planes ?? 0)} planes");
        foreach (IGrouping<DaggerfallGeometryDisposition, DaggerfallGeometryRecord> disposition in geometry.Records.GroupBy(record => record.Disposition).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {disposition.Count()} {disposition.Key.ToString().ToLowerInvariant()}");
        }

        foreach (IGrouping<string, DaggerfallGeometryRecord> version in geometry.Records.Where(record => record.Facts is not null).GroupBy(record => record.Facts!.Version).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {version.Count()} records state {version.Key}");
        }

        Console.WriteLine($"  {geometry.Records.Count(record => record.DuplicateOf is not null)} records reuse an earlier number, {geometry.Records.Count(record => record.PayloadDuplicateOf is not null)} repeat an earlier record's bytes");
        Console.WriteLine($"  unresolved use sites: {(geometry.UnresolvedUseSites.Count == 0 ? "none" : string.Join(", ", geometry.UnresolvedUseSites.Select(unresolved => unresolved.MeshId)))}");
        if (!args.Switch("--update")) return Options.ReportOnly("this inventory");
        PayloadFiles.WriteSection(args["--records"], "geometry", geometry);
        return 0;
    }
}
