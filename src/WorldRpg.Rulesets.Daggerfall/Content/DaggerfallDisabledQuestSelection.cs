namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The quests that never enter ordinary offers, with the explicit summoning selection for the Daedric quests among
/// them: the quests of every corpus category stated as not offered or summon-only. A bundle whose corpora state none
/// has no such quests.
/// </summary>
internal sealed class DaggerfallDisabledQuestSelection
{
    private readonly IReadOnlyDictionary<string, DaggerfallSummonQuestResolution> _summons;
    private readonly IReadOnlySet<string> _disabledSources;

    private DaggerfallDisabledQuestSelection(
        IReadOnlyDictionary<string, DaggerfallSummonQuestResolution> summons,
        IReadOnlySet<string> disabledSources,
        IReadOnlyList<DaggerfallQuestRuntimeReceipt> receipts)
    {
        _summons = summons;
        _disabledSources = disabledSources;
        Receipts = receipts;
    }

    /// <summary>The runtime receipts of every quest withheld from ordinary offers, ordered by name.</summary>
    internal IReadOnlyList<DaggerfallQuestRuntimeReceipt> Receipts { get; }

    /// <summary>A selection with no withheld quests, for a bundle whose corpora state none.</summary>
    internal static DaggerfallDisabledQuestSelection None { get; } = From([]);

    /// <summary>The withheld quests, each with the availability its category states.</summary>
    internal static DaggerfallDisabledQuestSelection From(IEnumerable<(string Availability, DaggerfallQuestRuntimeReceipt Receipt)> withheld)
    {
        ArgumentNullException.ThrowIfNull(withheld);
        (string Availability, DaggerfallQuestRuntimeReceipt Receipt)[] entries = [.. withheld];
        Dictionary<string, DaggerfallSummonQuestResolution> summons = new(StringComparer.Ordinal);
        foreach ((_, DaggerfallQuestRuntimeReceipt status) in entries.Where(entry => entry.Availability == DaggerfallQuestContent.SummonOnly))
        {
            // Two categories summoning the same identity would leave one of them unreachable.
            if (!summons.TryAdd(status.Name, new(status.Name, status.SourceFile, status.Runnable, status.Diagnostics)))
                throw new DaggerfallContentException([$"Admitted quest corpora summon '{status.Name}' more than once."]);
        }
        return new(
            summons,
            new HashSet<string>(entries.Select(entry => entry.Receipt.SourceFile), StringComparer.Ordinal),
            entries.Select(entry => entry.Receipt).OrderBy(receipt => receipt.Name, StringComparer.Ordinal).ToArray());
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
