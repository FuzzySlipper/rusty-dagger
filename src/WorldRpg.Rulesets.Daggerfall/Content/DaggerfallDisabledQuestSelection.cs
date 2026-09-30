namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The admitted classic entries that never enter ordinary offers, with the explicit donor summoning
/// selection for the Daedric sources among them. Every admitted corpus contributes the entries its
/// categories state as not offered or summon-only; a bundle that admits none has no such entries.
/// </summary>
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

    /// <summary>The runtime receipts of every entry withheld from ordinary offers, ordered by name.</summary>
    internal IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> Receipts { get; }

    /// <summary>A selection with no withheld entries, for a bundle that admits no corpus stating any.</summary>
    internal static DaggerfallDisabledQuestSelection None { get; } = From([]);

    internal static DaggerfallDisabledQuestSelection From(IEnumerable<DaggerfallClassicQuestCorpusReceipt> corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        DaggerfallClassicQuestCorpusReceipt[] withheld = [.. corpus.Where(receipt => !receipt.IsOrdinaryOffer)];
        Dictionary<string, DaggerfallSummonQuestResolution> summons = new(StringComparer.Ordinal);
        foreach (DaggerfallClassicQuestCorpusReceipt receipt in withheld.Where(receipt => receipt.Availability == DaggerfallClassicQuestCorpusContent.SummonOnly))
        {
            DaggerfallFightersGuildQuestRuntimeReceipt status = receipt.Runtime;
            // Two corpora summoning the same identity would leave one of them unreachable.
            if (!summons.TryAdd(status.Name, new(status.Name, status.SourceFile, status.Runnable, status.Diagnostics)))
                throw new DaggerfallContentException([$"Admitted classic corpora summon '{status.Name}' more than once."]);
        }
        return new(
            summons,
            new HashSet<string>(withheld.Select(receipt => receipt.Runtime.SourceFile), StringComparer.Ordinal),
            withheld.Select(receipt => receipt.Runtime).OrderBy(receipt => receipt.Name, StringComparer.Ordinal).ToArray());
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
