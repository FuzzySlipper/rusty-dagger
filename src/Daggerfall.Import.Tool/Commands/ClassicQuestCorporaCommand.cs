using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the classic quest corpus payloads a session reads, from the already normalized quest sections
/// and the original-source selections in the import records.
/// </summary>
internal static class ClassicQuestCorporaCommand
{
    public static ToolCommand Command { get; } = new("classic-quest-corpora",
        [Options.Pack, Options.Records, CommandOption.Required("--out", "DIRECTORY")], Run);

    private static int Run(CommandArguments args)
    {
        (DaggerfallQuestCatalog catalog, DaggerfallQuestPack sources, DaggerfallQuestOriginalSourceSet originals) = ReadInputs(args);
        foreach (DaggerfallClassicQuestCorpusSpecification specification in ClassicQuestCorpusPublication.Specifications)
        {
            DaggerfallClassicQuestCorpus corpus = ClassicQuestCorpusPublication.Create(specification.Id, catalog, sources, originals);
            PayloadFiles.WriteFile(Path.Combine(args["--out"], $"daggerfall.quests.{specification.Id}.json"), ClassicQuestCorpusPublication.Serialize(corpus));
            Console.WriteLine($"classic quest corpus {specification.Id}: {corpus.Quests.Count} records, fingerprint {corpus.Fingerprint.Value}");
        }

        foreach (string id in NamedQuestCorpusPublication.Selections.Keys)
        {
            DaggerfallNamedQuestCorpus corpus = NamedQuestCorpusPublication.Create(id, sources, originals);
            PayloadFiles.WriteFile(Path.Combine(args["--out"], $"daggerfall.quests.{id}.json"), NamedQuestCorpusPublication.Serialize(corpus));
            Console.WriteLine($"named quest corpus {id}: {corpus.Quests.Count} records, fingerprint {corpus.Fingerprint.Value}");
        }
        return 0;
    }

    /// <summary>The quest catalog and sources from the imported payload and the selections from the import records.</summary>
    internal static (DaggerfallQuestCatalog, DaggerfallQuestPack, DaggerfallQuestOriginalSourceSet) ReadInputs(CommandArguments args) => (
        PayloadFiles.ReadSection<DaggerfallQuestCatalog>(args["--pack"], "questCatalog"),
        PayloadFiles.ReadSection<DaggerfallQuestPack>(args["--pack"], "questSources"),
        PayloadFiles.ReadSection<DaggerfallQuestOriginalSourceSet>(args["--records"], "questOriginalSources"));
}
