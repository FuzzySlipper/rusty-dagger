using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestCorpusIntegrationTests
{
    private static string[] RequiredSources()
    {
        var sources = TestPayload.Definitions.QuestSources;
        return [.. sources.Catalog.Rows.Select(row => row.Name + ".txt").Where(sources.Quests.ContainsKey),
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
                var blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
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

    /// <summary>
    /// The macros the corpus still leaves empty, which #10901 receives: each fixed one leaves this list, and any
    /// other that resolves empty fails the test. %qdt is here for the letters; a logged message is dated by its entry.
    /// </summary>
    private static readonly string[] KnownEmptyMacros = ["%G", "%G1", "%G3", "%di", "%fn", "%g", "%g1", "%g2", "%g2-", "%g2-self", "%g2self", "%g3",
        "%g3-self", "%god-", "%jok", "%kno", "%mn", "%n", "%nam", "%nrn", "%pct", "%qdat", "%qdt", "%reg", "%rn", "%rt", "%t"];

    [Fact]
    public void Every_shipped_quest_message_macro_resolves_to_text()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var game = fixture.Start(fixture.KnightlyProfile);
        long questor = game.State.Npcs.All.First(value => value.Appearance.FactionId == 368).DurableId;
        game.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "corpus-clan", 0, vampireClan: 153);
        var empty = game.State.Quests.Capture();
        SortedDictionary<string, string> unresolved = new(StringComparer.Ordinal);
        foreach (string file in RequiredSources())
        {
            game.State.Quests.Restore(empty);
            var source = TestPayload.Definitions.QuestSources.Resolve(file);
            DaggerfallQuestInstanceSave quest;
            try
            {
                quest = game.State.Quests.Start(new("corpus:" + source.Name, file, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
                    { QuestorId = questor, FactionId = 368 });
            }
            catch (NotSupportedException) when (file == "A0C0XY04.txt") { continue; } // No local apothecary here, as above.
            HashSet<int> logged = [.. source.Blocks.SelectMany(block => block.Lines)
                .Select(line => System.Text.RegularExpressions.Regex.Match(line, @"\blog\s+(\d+)")).Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];
            foreach (var message in source.Messages)
            {
                // A message's variants are separated by <--->; one with no text is never drawn.
                List<List<string>> variants = [[]];
                foreach (string line in message.Lines)
                    if (line.Trim() == "<--->") variants.Add([]); else variants[^1].Add(line);
                for (int variant = 0; variant < variants.Count; variant++)
                {
                    if (variants[variant].All(line => string.IsNullOrWhiteSpace(line.Replace("<ce>", "", StringComparison.Ordinal)))) continue;
                    foreach (string diagnostic in game.State.Quests.RenderMessage(quest, message.Id, DaggerfallQuestMessageDelivery.Popup, variant).Diagnostics)
                    {
                        string macro = diagnostic[(diagnostic.IndexOf(": ", StringComparison.Ordinal) + 2)..].Replace("\n", "\\n", StringComparison.Ordinal);
                        if (macro == "%qdt" && logged.Contains(message.Id)) continue;
                        unresolved.TryAdd(macro, $"{diagnostic} ({file}:{message.Id})");
                    }
                }
            }
        }
        Assert.True(unresolved.Keys.SequenceEqual(KnownEmptyMacros, StringComparer.Ordinal),
            "Resolving empty beyond #10901's list:\n" + string.Join('\n', unresolved.Where(entry => !KnownEmptyMacros.Contains(entry.Key)).Select(entry => entry.Value))
            + "\nNo longer empty, so leave the list: " + string.Join(", ", KnownEmptyMacros.Except(unresolved.Keys)));
    }

    [Fact]
    public void Every_retained_classic_source_branch_uses_a_compiled_operation()
    {
        var sources = TestPayload.Definitions.QuestSources;
        string[] required = [.. sources.Catalog.Rows.Select(row => row.Name + ".txt").Where(sources.Quests.ContainsKey),
            .. sources.Quests.Keys.Where(file => file.StartsWith("S000", StringComparison.Ordinal) || file is "_TUTOR__.txt" or "_BRISIEN.txt" or "$CUREVAM.txt" or "$CUREWER.txt")];
        var unsupported = required.SelectMany(file => DaggerfallQuestTaskCompiler.Assess(sources.Resolve(file))
            .Select(diagnostic => $"{file}:{diagnostic.Line}: {diagnostic.Text}: {diagnostic.Reason}")).ToArray();
        Assert.True(unsupported.Length == 0, string.Join('\n', unsupported));
    }
}
