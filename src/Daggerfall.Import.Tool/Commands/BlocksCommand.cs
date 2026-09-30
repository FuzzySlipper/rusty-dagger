using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Enumerates the classic block archive into the block document, so the tasks that publish dungeons,
/// exteriors and geometry start from one inventory of what the corpus carries rather than decoding the
/// archive again and disagreeing about what a name means.
/// </summary>
internal static class BlocksCommand
{
    public static ToolCommand Command { get; } = new("blocks",
        [Options.Arena2, CommandOption.Required("--out", "BLOCKS.json"), Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        DaggerfallBlocks blocks = DaggerfallBlocksBuilder.Build(
            File.ReadAllBytes(Path.Combine(args["--arena2"], BlockRecordInventoryReader.FileName)),
            Options.Arena2Label(BlockRecordInventoryReader.FileName),
            Options.ReadInventory(args));

        DaggerfallBlockSource publishedSource = blocks.Sources[0];
        Console.WriteLine($"blocks: {blocks.Records.Count} records from {publishedSource.Path}, declared {publishedSource.DeclaredLength}, {blocks.Records.Count(record => record.State == DaggerfallBlockState.Malformed)} malformed");
        foreach (IGrouping<DaggerfallBlockKind, DaggerfallBlockRecord> kind in blocks.Records.GroupBy(record => record.Kind).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {kind.Count()} {kind.Key.ToString().ToLowerInvariant()}");
        }

        Console.WriteLine($"  rdb types: {string.Join(", ", blocks.Records.Where(record => record.Kind == DaggerfallBlockKind.Rdb).GroupBy(record => record.RdbName!.Type).OrderBy(group => group.Key).Select(group => $"{group.Key.ToString().ToLowerInvariant()} {group.Count()}"))}");
        string[] malformed = [.. blocks.Records.Where(record => record.State == DaggerfallBlockState.Malformed).Select(record => record.SourceKey)];
        if (malformed.Length != 0) Console.WriteLine($"  malformed records: {string.Join(", ", malformed)}");
        if (!args.Switch("--update")) return Options.ReportOnly("this inventory");

        // The complete document, placements included, is the one owner of the block records: the
        // daggerfall.blocks pack reads it at runtime and the geometry inventory reads its use sites.
        PayloadFiles.WriteFile(args["--out"], Encoding.UTF8.GetBytes(JsonSerializer.Serialize(blocks, PublishedJson.SectionCompact) + "\n"));
        Console.WriteLine($"blocks: block document written to {args["--out"]}");
        return 0;
    }
}
