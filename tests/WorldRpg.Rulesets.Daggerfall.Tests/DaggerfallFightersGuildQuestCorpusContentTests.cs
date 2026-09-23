using System.Reflection;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallFightersGuildQuestCorpusContentTests
{
    [Fact]
    public void Admits_all_twenty_receipts_and_reports_compiler_diagnostics_per_source()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> receipts = DaggerfallFightersGuildQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.fighters.json")), definitions);

        Assert.Equal(20, receipts.Count);
        Assert.Equal(
        [
            "M0C00Y11", "M0C00Y12", "M0C00Y13", "M0C00Y14", "M0B00Y00", "M0B00Y06", "M0B00Y07", "M0B00Y15", "M0B00Y16", "M0B00Y17",
            "M0B1XY01", "M0B11Y18", "M0B20Y02", "M0B21Y19", "M0B30Y03", "M0B30Y04", "M0B30Y08", "M0B40Y05", "M0B50Y09", "M0B60Y10",
        ], receipts.Select(receipt => receipt.Name));
        Assert.All(receipts, receipt => Assert.Equal(receipt.Name + ".txt", receipt.SourceFile));
        Assert.All(receipts.Where(receipt => receipt.Runnable), receipt => Assert.Empty(receipt.Diagnostics));
        Assert.All(receipts.Where(receipt => !receipt.Runnable), receipt => Assert.NotEmpty(receipt.Diagnostics));
    }

    [Fact]
    public void Actual_instance_start_and_restore_refuse_a_non_runnable_selected_source()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> receipts = DaggerfallFightersGuildQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.fighters.json")), definitions);
        DaggerfallFightersGuildQuestRuntimeReceipt blocked = receipts.First(receipt => !receipt.Runnable);
        DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve(blocked.SourceFile);
        DaggerfallQuestRuntimeAdmission admission = new(receipts);
        DaggerfallQuestInstances instances = new(definitions, RandomMinimum.Create(), admission);
        DaggerfallQuestInstanceSave fresh = new("fighters:blocked", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []);
        ArgumentException start = Assert.Throws<ArgumentException>(() => instances.Start(fresh));
        ArgumentException restore = Assert.Throws<ArgumentException>(() => instances.Restore(new([fresh])));

        Assert.Contains("no admitted runnable program", start.Message, StringComparison.Ordinal);
        Assert.Contains("no admitted runnable program", restore.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_duplicate_or_metadata_substitution_against_the_admitted_catalog()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.fighters.json")))!.AsObject();
        JsonArray quests = payload["quests"]!.AsArray();
        quests[1]!["name"] = quests[0]!["name"]!.GetValue<string>();

        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallFightersGuildQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()),
            System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), definitions));

        Assert.Contains("repeats a quest name", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_admission_refuses_a_source_with_compiler_diagnostics_before_a_program_can_run()
    {
        DaggerfallQuestRuntimeAdmission admission = new([
            new DaggerfallFightersGuildQuestRuntimeReceipt("blocked", "blocked.txt", false,
                [new DaggerfallQuestDiagnosticDefinition(12, "talk to _giver_", "No current quest task runner operation supports this action.")]),
        ]);

        ArgumentException failure = Assert.Throws<ArgumentException>(() => admission.RequireRunnable("blocked.txt"));

        Assert.Contains("line 12: talk to _giver_", failure.Message, StringComparison.Ordinal);
        Assert.Contains("runner operation supports", failure.Message, StringComparison.Ordinal);
    }

    internal class RandomMinimum : DispatchProxy
    {
        internal static IRandomService Create() => DispatchProxy.Create<IRandomService, RandomMinimum>();

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("repository root not found");
    }
}
