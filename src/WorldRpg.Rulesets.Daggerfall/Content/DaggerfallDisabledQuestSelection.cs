using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Explicit donor summoning selection for disabled Daedric sources; these entries never enter ordinary offers.</summary>
internal sealed class DaggerfallDisabledQuestSelection
{
    private readonly IReadOnlyDictionary<string, DaggerfallSummonQuestResolution> _summons;
    private readonly IReadOnlySet<string> _disabledSources;

    private DaggerfallDisabledQuestSelection(
        IReadOnlyDictionary<string, DaggerfallSummonQuestResolution> summons,
        IReadOnlySet<string> disabledSources,
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> receipts)
    {
        _summons = summons;
        _disabledSources = disabledSources;
        Receipts = receipts;
    }

    internal IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> Receipts { get; }

    internal static DaggerfallDisabledQuestSelection Read(ProductContent content, ReadOnlyMemory<byte> payload, DaggerfallDefinitions definitions)
    {
        IReadOnlyDictionary<string, DaggerfallFightersGuildQuestRuntimeReceipt> runtime = DaggerfallClassicQuestCorpusContent.Read(content, payload, definitions, DaggerfallClassicQuestCorpusExpectations.Require("disabled"))
            .ToDictionary(receipt => receipt.Name, StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(payload);
        Dictionary<string, DaggerfallSummonQuestResolution> summons = [];
        foreach (JsonElement receipt in document.RootElement.GetProperty("quests").EnumerateArray())
        {
            if (!string.Equals(receipt.GetProperty("availability").GetString(), "summonOnly", StringComparison.Ordinal)) continue;
            string name = receipt.GetProperty("name").GetString()!;
            DaggerfallFightersGuildQuestRuntimeReceipt status = runtime[name];
            summons.Add(name, new(name, status.SourceFile, status.Runnable, status.Diagnostics));
        }
        if (summons.Count != 16) throw new DaggerfallContentException(["Disabled classic corpus must retain sixteen explicit summon-only Daedric identities."]);
        return new(
            new Dictionary<string, DaggerfallSummonQuestResolution>(summons, StringComparer.Ordinal),
            new HashSet<string>(runtime.Values.Select(value => value.SourceFile), StringComparer.Ordinal),
            runtime.Values.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray());
    }

    internal bool TryResolveSummon(string questName, out DaggerfallSummonQuestResolution? resolution)
    {
        if (string.IsNullOrWhiteSpace(questName) || !_summons.TryGetValue(questName, out DaggerfallSummonQuestResolution? value)) { resolution = null; return false; }
        resolution = value;
        return true;
    }

    internal bool IsOrdinaryOffer(string questName) =>
        !string.IsNullOrWhiteSpace(questName) && !_disabledSources.Contains(questName + ".txt");

    internal bool IsSummonOnlySource(string sourceFile) =>
        TryResolveSummon(Path.GetFileNameWithoutExtension(sourceFile), out _);
}

internal sealed record DaggerfallSummonQuestResolution(string Name, string SourceFile, bool Runnable, IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Diagnostics);
