using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class NamedQuestCorpusPublicationTests
{
    [GeneratedContentFact]
    public void Publishes_exact_named_selections_with_display_names_and_source_provenance()
    {
        string root = TestData.RepositoryRoot;
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.imported.json")));
        using JsonDocument records = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "import-records/daggerfall.import-records.json")));
        DaggerfallQuestPack sources = JsonSerializer.Deserialize<DaggerfallQuestPack>(payload.RootElement.GetProperty("questSources"), PublishedJson.SectionRead)!;
        DaggerfallQuestOriginalSourceSet originals = JsonSerializer.Deserialize<DaggerfallQuestOriginalSourceSet>(records.RootElement.GetProperty("questOriginalSources"), PublishedJson.SectionRead)!;
        foreach ((string id, int count) in new[] { ("story-early", 20), ("story-late", 16), ("cures", 2) })
        {
            DaggerfallNamedQuestCorpus corpus = NamedQuestCorpusPublication.Create(id, sources, originals);
            Assert.Equal(count, corpus.Quests.Count);
            Assert.Equal(NamedQuestCorpusPublication.Selections[id], corpus.Quests.Select(quest => quest.Name));
            Assert.Equal(NamedQuestCorpusPublication.Serialize(corpus), File.ReadAllBytes(Path.Combine(root, $"content/worldrpg/payloads/daggerfall.quests.{id}.json")));
            foreach (DaggerfallNamedQuestReceipt quest in corpus.Quests)
            {
                Assert.Equal(sources.Quests.Single(source => source.Name == quest.Name).DisplayName, quest.DisplayName);
                Assert.Equal(quest.DisplayName.Length == 0, quest.MetadataDiagnostics.Count != 0);
                Assert.Equal("rewrittentext", quest.OriginalSelection);
                Assert.NotEmpty(quest.ActionLines);
            }
        }
        DaggerfallNamedQuestCorpus late = NamedQuestCorpusPublication.Create("story-late", sources, originals);
        var missing = late.Quests.SelectMany(quest => quest.MissingQuestReferences).ToArray();
        Assert.Equal(3, missing.Length);
        Assert.All(missing, diagnostic => Assert.Contains("has no donor source", diagnostic.Reason));
        Assert.Equal(7, late.Quests.Count(quest => quest.DisplayName.Length == 0));
        Assert.Equal(new[] { "S0000977", "S0000999", "_BRISIEN" }, late.Quests.Where(quest => quest.ProtectedLifecycle).Select(quest => quest.Name));
        Assert.All(NamedQuestCorpusPublication.Create("story-early", sources, originals).Quests, quest => Assert.False(quest.ProtectedLifecycle));
        var excluded = Assert.Single(NamedQuestCorpusPublication.Create("story-early", sources, originals).Quests.SelectMany(quest => quest.ExcludedActions));
        Assert.Equal("location _tavern_ 100 27000", excluded.Text.Trim());
        Assert.Contains("Explicitly excluded", excluded.Reason);
        foreach (DaggerfallNamedQuestReceipt cure in NamedQuestCorpusPublication.Create("cures", sources, originals).Quests)
            Assert.Contains(cure.ActionLines, line => line.Text.Contains("cure", StringComparison.OrdinalIgnoreCase));
    }
}
