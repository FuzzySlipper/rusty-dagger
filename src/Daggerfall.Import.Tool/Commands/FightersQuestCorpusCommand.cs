using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Publishes the selected Fighters Guild receipt from already normalized quest sections.</summary>
internal static class FightersQuestCorpusCommand
{
    public static ToolCommand Command { get; } = new("fighters-quest-corpus",
        [Options.Pack, Options.Records, CommandOption.Required("--out", "PAYLOAD.json")], Run);

    private static int Run(CommandArguments args)
    {
        (DaggerfallQuestCatalog catalog, DaggerfallQuestPack sources, DaggerfallQuestOriginalSourceSet originals) = ClassicQuestCorporaCommand.ReadInputs(args);
        DaggerfallFightersGuildQuestCorpus corpus = FightersGuildQuestCorpusPublication.Create(catalog, sources, originals);
        PayloadFiles.WriteFile(args["--out"], FightersGuildQuestCorpusPublication.Serialize(corpus));
        Console.WriteLine($"fighters quest corpus: {corpus.Quests.Count} exact records, fingerprint {corpus.Fingerprint.Value}");
        return 0;
    }
}
