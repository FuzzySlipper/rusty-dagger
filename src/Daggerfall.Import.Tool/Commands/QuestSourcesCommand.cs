using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Enumerates the classic quest source corpus, decodes both envelopes, and — when the documented inventory
/// is supplied — checks that every supplied path is one the inventory carries and that it carries no path
/// the corpus does not.
/// </summary>
internal static class QuestSourcesCommand
{
    public static ToolCommand Command { get; } = new("quest-sources",
        [Options.Arena2, CommandOption.Optional("--inventory", "CSV")], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        QuestSourceInventory inventory = Enumerate(arena2);
        int marked = 0, unmarked = 0, decoded = 0, badResources = 0, badBinaries = 0;
        long records = 0;
        foreach (QuestSourceFile file in inventory.Files)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(arena2, file.Path));
            if (file.Family == QuestSourceFamily.QuestBinary)
            {
                QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(bytes, file.Path);
                marked += envelope.HasTerminalMarker ? 1 : 0;
                unmarked += envelope.HasTerminalMarker ? 0 : 1;
                badBinaries += envelope.Disposition == QuestBinaryEnvelopeDisposition.WellFormed ? 0 : 1;
            }
            else
            {
                QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, file.Path);
                decoded += envelope.Disposition == QuestResourceEnvelopeDisposition.Decoded ? 1 : 0;
                badResources += envelope.Disposition == QuestResourceEnvelopeDisposition.Decoded ? 0 : 1;
                records += envelope.Records.Count;
            }
        }

        Console.WriteLine($"quest sources: {inventory.Files.Count} files, {inventory.Binaries.Count()} QBN, {inventory.Resources.Count()} QRC, {inventory.Files.Count(file => file.Pairing == QuestSourcePairing.Paired)} paired, {inventory.BinaryOnly.Count()} binary-only, {inventory.ResourcesOnly.Count()} resources-only");
        Console.WriteLine($"binary envelopes: {marked} with the terminal marker, {unmarked} without, {badBinaries} not well formed");
        Console.WriteLine($"resource envelopes: {decoded} decoded into {records} records, {badResources} not decoded");
        foreach (QuestSourceFile file in inventory.BinaryOnly.Concat(inventory.ResourcesOnly))
        {
            Console.WriteLine($"  unpaired {file.Path} ({file.Pairing})");
        }

        // The inventory is the authority for which paths exist; this checks the corpus and the document
        // against each other rather than trusting either alone.
        if (!args.Has("--inventory")) return 0;
        return Options.CheckDocumentedFamily(
            Options.DocumentedFiles(Options.ReadInventory(args), row => row.FamilyId == "CNT-017"),
            [.. inventory.Files.Select(file => file.Path)],
            "quest source paths");
    }

    /// <summary>Every QBN and QRC file the corpus directory supplies.</summary>
    internal static QuestSourceInventory Enumerate(string arena2)
    {
        string[] paths = [.. Directory.EnumerateFiles(arena2)
            .Where(path => path.EndsWith(QuestSourceInventory.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(QuestSourceInventory.ResourcesExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OfType<string>()];
        return QuestSourceInventory.Enumerate(paths, Options.CorpusLabel(arena2));
    }
}
