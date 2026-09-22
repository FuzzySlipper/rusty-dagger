using System.Reflection;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDisabledQuestSelectionTests
{
    [Fact]
    public void Disabled_entries_never_become_ordinary_offers_and_summon_identities_resolve_explicitly()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallDisabledQuestSelection selection = DaggerfallDisabledQuestSelection.Read(new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.disabled.json")), definitions);
        Assert.True(selection.TryResolveSummon("80C0XY00", out DaggerfallSummonQuestResolution? resolved));
        Assert.Equal("80C0XY00.txt", resolved!.SourceFile);
        Assert.False(selection.IsOrdinaryOffer("80C0XY00"));
        Assert.False(selection.TryResolveSummon("80C00Y00", out _));
        Assert.False(selection.TryResolveSummon("K0C00Y06", out _));
        DaggerfallQuestInstanceSave summoned = new("daedric:80", resolved.SourceFile, resolved.Name, DaggerfallQuestLifecycle.Active, null, [], []);
        DaggerfallQuestInstances instances = new(definitions, RandomMinimum.Create(), new(selection.Receipts), selection);
        Assert.Contains("requires an explicit Daedric summoning identity", Assert.Throws<ArgumentException>(() => instances.Start(summoned)).Message, StringComparison.Ordinal);
        Assert.Contains("no admitted runnable program", Assert.Throws<ArgumentException>(() => instances.StartSummoned("80C0XY00", summoned)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => instances.StartSummoned("80C0XY00", summoned with { SourceFile = "10C00Y00.txt" }));
    }

    private class RandomMinimum : DispatchProxy
    {
        internal static IRandomService Create() => DispatchProxy.Create<IRandomService, RandomMinimum>();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum) : throw new NotSupportedException(method?.Name);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("repository root not found");
    }
}
