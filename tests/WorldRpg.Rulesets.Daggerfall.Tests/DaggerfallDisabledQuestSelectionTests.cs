using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDisabledQuestSelectionTests
{
    [Fact]
    public void Disabled_entries_never_become_ordinary_offers_and_summon_identities_resolve_explicitly()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallDisabledQuestSelection selection = DaggerfallDisabledQuestSelection.From(DaggerfallClassicQuestCorpusContent.Read(new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.disabled.json")), definitions));
        Assert.True(selection.TryResolveSummon("80C0XY00", out DaggerfallSummonQuestResolution? resolved));
        Assert.Equal("80C0XY00.txt", resolved!.SourceFile);
        Assert.False(selection.IsOrdinaryOffer("80C0XY00"));
        Assert.False(selection.TryResolveSummon("80C00Y00", out _));
        Assert.False(selection.TryResolveSummon("K0C00Y06", out _));
        DaggerfallQuestInstanceSave summoned = new("daedric:80", resolved.SourceFile, resolved.Name, DaggerfallQuestLifecycle.Active, null, [], []);
        DaggerfallQuestInstances instances = new(definitions, RandomMinimum.Create(), new(selection.Receipts), selection);
        Assert.Contains("requires an explicit Daedric summoning identity", Assert.Throws<ArgumentException>(() => instances.Start(summoned)).Message, StringComparison.Ordinal);
        Assert.True(new DaggerfallQuestRuntimeAdmission(selection.Receipts).IsRunnable(resolved.SourceFile));
        // The source is admitted now; this isolated owner still lacks the session's route service.
        Assert.Contains("admitted route calculator", Assert.Throws<NotSupportedException>(() => instances.StartSummoned("80C0XY00", summoned)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => instances.StartSummoned("80C0XY00", summoned with { SourceFile = "10C00Y00.txt" }));
    }
}
