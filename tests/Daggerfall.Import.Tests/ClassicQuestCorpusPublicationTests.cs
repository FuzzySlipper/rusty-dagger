using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ClassicQuestCorpusPublicationTests
{
    [Fact]
    public void Publishes_every_retained_category_with_exact_membership_and_stable_fingerprints()
    {
        string root = RepositoryRoot();
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallQuestCatalog catalog = Section<DaggerfallQuestCatalog>(payload, "questCatalog");
        DaggerfallQuestPack sources = Section<DaggerfallQuestPack>(payload, "questSources");
        DaggerfallQuestOriginalSourceSet originals = Section<DaggerfallQuestOriginalSourceSet>(payload, "questOriginalSources");
        Assert.Equal(new Dictionary<string, int> { ["mages"] = 18, ["temples"] = 24, ["social"] = 45, ["witches-commoners"] = 30, ["merchants-vampires"] = 22, ["disabled"] = 18, ["nobility"] = 28 }, ClassicQuestCorpusPublication.Specifications.ToDictionary(specification => specification.Id, specification => ClassicQuestCorpusPublication.Create(specification.Id, catalog, sources, originals).Quests.Count));
        foreach (DaggerfallClassicQuestCorpusSpecification specification in ClassicQuestCorpusPublication.Specifications)
        {
            DaggerfallClassicQuestCorpus first = ClassicQuestCorpusPublication.Create(specification.Id, catalog, sources, originals);
            DaggerfallClassicQuestCorpus second = ClassicQuestCorpusPublication.Create(specification.Id, catalog, sources, originals);
            Assert.Equal(first.Fingerprint, second.Fingerprint);
            Assert.Equal(ClassicQuestCorpusPublication.Serialize(first), File.ReadAllBytes(Path.Combine(root, $"content/worldrpg/payloads/daggerfall.quests.{specification.Id}.json")));
        }
    }

    [Fact]
    public void Each_task_selection_preserves_its_source_backed_categories_and_disabled_disposition()
    {
        string root = RepositoryRoot();
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallQuestCatalog catalog = Section<DaggerfallQuestCatalog>(payload, "questCatalog");
        DaggerfallQuestPack sources = Section<DaggerfallQuestPack>(payload, "questSources");
        DaggerfallQuestOriginalSourceSet originals = Section<DaggerfallQuestOriginalSourceSet>(payload, "questOriginalSources");

        foreach (DaggerfallClassicQuestCorpusSpecification specification in ClassicQuestCorpusPublication.Specifications)
        {
            DaggerfallClassicQuestCorpus corpus = ClassicQuestCorpusPublication.Create(specification.Id, catalog, sources, originals);
            Assert.Equal(corpus.Quests.Count, corpus.Quests.Select(quest => quest.SourceFile).Distinct(StringComparer.Ordinal).Count());
            Assert.All(corpus.Quests, quest => Assert.Equal(quest.Name + ".txt", quest.SourceFile));
            foreach (DaggerfallClassicQuestCategory category in specification.Categories)
            {
                DaggerfallClassicQuestReceipt[] selected = [.. corpus.Quests.Where(quest => quest.Category == category.Id)];
                Assert.Equal(category.Names, selected.Select(quest => quest.Name));
                Assert.All(selected, quest =>
                {
                    Assert.Equal(category.CatalogGroup, quest.CatalogGroup);
                    Assert.Equal(category.Availability, quest.Availability);
                    Assert.Equal(category.Active, quest.Active);
                    Assert.Equal("present", quest.CatalogSourceDisposition);
                });
            }
        }

        DaggerfallClassicQuestCorpus disabled = ClassicQuestCorpusPublication.Create("disabled", catalog, sources, originals);
        Assert.Equal(16, disabled.Quests.Count(quest => quest.Availability == "summonOnly"));
        Assert.DoesNotContain(disabled.Quests, quest => quest.Name == "80C00Y00");
        Assert.Contains(disabled.Quests, quest => quest.Name == "K0C00Y06" && quest.Availability == "notOffered");
        Assert.Contains(disabled.Quests, quest => quest.Name == "R0C11Y27" && quest.Availability == "notOffered");
        Assert.Contains(ClassicQuestCorpusPublication.Create("nobility", catalog, sources, originals).Quests, quest => quest.Name == "R0C11Y28");
    }

    private static T Section<T>(JsonDocument document, string name) => JsonSerializer.Deserialize<T>(document.RootElement.GetProperty(name).GetRawText(), PublishedJson.SectionRead)!;
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("repository root not found");
    }
}
