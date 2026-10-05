using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestCorpusIntegrationTests
{
    private static string[] RequiredSources()
    {
        var sources = TestPayload.Definitions.QuestSources;
        return [.. sources.Catalog.Rows.Where(row => row.SourceDisposition == "present").Select(row => row.Name + ".txt"),
            .. sources.Quests.Keys.Where(file => file.StartsWith("S000", StringComparison.Ordinal) || file is "_TUTOR__.txt" or "_BRISIEN.txt" or "$CUREVAM.txt" or "$CUREWER.txt")];
    }

    [Fact]
    public void Cross_quest_references_resolve_except_explicit_missing_donor_backbone_sources()
    {
        var required = RequiredSources().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(243, required.Count); // 248 named records, five explicitly missing sources.
        List<string> missing = [];
        foreach (var file in required)
            foreach (var operation in DaggerfallQuestTaskCompiler.Compile(TestPayload.Definitions.QuestSources.Resolve(file))
                .Tasks.SelectMany(task => task.Operations).Where(operation => operation.Kind is DaggerfallQuestTaskOperationKind.StartQuest or DaggerfallQuestTaskOperationKind.RunQuest))
                if (!required.Contains(operation.Targets[0] + ".txt")) missing.Add($"{file}:{operation.Source}");
        Assert.Equal(new[] { "S0000999.txt:start quest 997 997", "S0000999.txt:start quest 998 998", "S0000999.txt:start quest 998 998" }, missing.Order());
        Assert.Throws<KeyNotFoundException>(() => TestPayload.Definitions.QuestSources.Resolve("S0000997.txt"));
        Assert.Throws<KeyNotFoundException>(() => TestPayload.Definitions.QuestSources.Resolve("S0000998.txt"));
    }

    [Fact]
    public void Retained_corpus_resources_allocate_through_canonical_owners()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var game = fixture.Start(fixture.KnightlyProfile);
        long questor = game.State.Npcs.All.First(value => value.Appearance.FactionId == 368).DurableId;
        game.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "corpus-clan", 0, vampireClan: 153);
        List<string> failures = [];
        var empty = game.State.Quests.Capture();
        foreach (string file in RequiredSources())
        {
            game.State.Quests.Restore(empty);
            var source = TestPayload.Definitions.QuestSources.Resolve(file);
            try
            {
                game.State.Quests.Start(new("corpus:" + source.Name, file, source.Name, DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = questor, FactionId = 368 });
            }
            catch (NotSupportedException error) when (file == "A0C0XY04.txt")
            {
                // The donor requires a local apothecary; this provider's town has none. A catalog
                // match does not make an invalid current-place binding silently succeed.
                Assert.Contains("meetingplace/local", error.Message);
                var definitions = TestPayload.Definitions;
                var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
                var town = definitions.Locations.Records.First(site => site.Exterior?.Buildings.Values.Any(building => building.Source.BuildingType == 0
                    && blocks.QuestMarkers.TryGetValue(new(building.Source.Id.SourceKey, building.Source.Id.Index), out var markers) && markers.Count > 0) == true);
                var sites = new World.DaggerfallSiteContext(definitions.Locations, town.Id, null, []);
                sites.AdmitBuildingNames(fixture.Random, definitions, blocks);
                var places = new DaggerfallQuestPlaceAllocator(definitions, sites, fixture.Random, (_, _) => false, _ => "region", (_, _) => "residence");
                var declared = definitions.QuestSources.Resources.Single(resource => resource.SourceFile == file && resource.CanonicalId == "meetingplace");
                var selected = places.Allocate("local-apothecary", declared, [], []);
                Assert.Equal(town.Id, selected.Binding.Places.Single().Require());
                Assert.NotNull(selected.Binding.Building);
            }
            catch (Exception error) { failures.Add(file + ": " + error.Message); }
        }
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void Every_retained_classic_source_branch_uses_a_compiled_operation()
    {
        var sources = TestPayload.Definitions.QuestSources;
        string[] required = [.. sources.Catalog.Rows.Where(row => row.SourceDisposition == "present").Select(row => row.Name + ".txt"),
            .. sources.Quests.Keys.Where(file => file.StartsWith("S000", StringComparison.Ordinal) || file is "_TUTOR__.txt" or "_BRISIEN.txt" or "$CUREVAM.txt" or "$CUREWER.txt")];
        var unsupported = required.SelectMany(file => DaggerfallQuestTaskCompiler.Assess(sources.Resolve(file))
            .Select(diagnostic => $"{file}:{diagnostic.Line}: {diagnostic.Text}: {diagnostic.Reason}")).ToArray();
        Assert.True(unsupported.Length == 0, string.Join('\n', unsupported));
    }
}
