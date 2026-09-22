using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class FightersGuildQuestCorpusPublicationTests
{
    [Fact]
    public void Publishes_the_exact_twenty_records_with_per_source_provenance_and_a_stable_fingerprint()
    {
        string root = RepositoryRoot();
        using JsonDocument basePayload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallFightersGuildQuestCorpus first = FightersGuildQuestCorpusPublication.Create(
            Section<DaggerfallQuestCatalog>(basePayload, "questCatalog"),
            Section<DaggerfallQuestPack>(basePayload, "questSources"),
            Section<DaggerfallQuestOriginalSourceSet>(basePayload, "questOriginalSources"));
        DaggerfallFightersGuildQuestCorpus second = FightersGuildQuestCorpusPublication.Create(
            Section<DaggerfallQuestCatalog>(basePayload, "questCatalog"),
            Section<DaggerfallQuestPack>(basePayload, "questSources"),
            Section<DaggerfallQuestOriginalSourceSet>(basePayload, "questOriginalSources"));

        Assert.Equal(FightersGuildQuestCorpusPublication.RequiredQuestNames, first.Quests.Select(quest => quest.Name));
        Assert.All(first.Quests, quest =>
        {
            Assert.Equal(quest.Name + ".txt", quest.SourceFile);
            Assert.Equal("present", quest.CatalogSourceDisposition);
            Assert.Equal("compiled", quest.SourceDisposition);
            Assert.Equal("rewrittentext", quest.OriginalSelection);
            Assert.NotEmpty(quest.ActionLines);
            Assert.Empty(quest.SourceDiagnostics);
            Assert.Matches("^[0-9a-f]{64}$", quest.SourceFingerprint.Value);
        });
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(FightersGuildQuestCorpusPublication.Serialize(first), FightersGuildQuestCorpusPublication.Serialize(second));
        Assert.Equal(FightersGuildQuestCorpusPublication.Serialize(first), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.fighters.json")));
    }

    private static T Section<T>(JsonDocument document, string name) =>
        JsonSerializer.Deserialize<T>(document.RootElement.GetProperty(name).GetRawText(), PublishedJson.SectionRead)
        ?? throw new InvalidOperationException($"Missing {name} section.");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("repository root not found");
    }
}
