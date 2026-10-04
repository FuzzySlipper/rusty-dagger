using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One generated spoken-world event that was admitted when a current world variable
/// changed. The event remains in the save after it expires so a flag that stays true
/// cannot recreate the same rumor on every conversation.
/// </summary>
internal sealed record DaggerfallDialogueWorldRumorSave(int Type, int Region, int TextId, long ExpiresAtMinute)
{
    internal DaggerfallDialogueWorldRumorSave Validate()
    {
        if (Type != DaggerfallDialogueWorldState.CrimeWaveType)
            throw new ArgumentOutOfRangeException(nameof(Type), Type, "A generated dialogue rumor names no admitted world event.");
        if (Region is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(Region), Region, "A generated dialogue rumor names no classic region.");
        if (TextId != DaggerfallDialogueWorldState.CrimeWaveTextId)
            throw new ArgumentOutOfRangeException(nameof(TextId), TextId, $"A crime-wave dialogue rumor must use resource {DaggerfallDialogueWorldState.CrimeWaveTextId}.");
        if (ExpiresAtMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(ExpiresAtMinute), ExpiresAtMinute, "A generated dialogue rumor expires before the calendar origin.");
        return this;
    }
}

/// <summary>Current generated world-news state shared by dialogue and the save owner.</summary>
internal sealed record DaggerfallDialogueWorldSave(DaggerfallDialogueWorldRumorSave[] Rumors)
{
    internal static DaggerfallDialogueWorldSave Empty { get; } = new([]);

    internal DaggerfallDialogueWorldSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Rumors);
        HashSet<(int Type, int Region)> keys = [];
        foreach (DaggerfallDialogueWorldRumorSave rumor in Rumors)
        {
            ArgumentNullException.ThrowIfNull(rumor);
            rumor.Validate();
            if (!keys.Add((rumor.Type, rumor.Region)))
                throw new ArgumentException($"Generated dialogue rumor {rumor.Type}/{rumor.Region} appears more than once.", nameof(Rumors));
        }
        return this;
    }
}

/// <summary>
/// Owns transition and expiry state for generated spoken-world news. A true world
/// variable creates one event; while the flag remains true the same event is reused,
/// and after expiry it is not recreated until the flag has gone false and true again.
/// </summary>
internal sealed class DaggerfallDialogueWorldState
{
    internal const int CrimeWaveType = 11;
    internal const int CrimeWaveTextId = 1410;
    internal const long GeneratedRumorDurationMinutes = 43140;

    private readonly Dictionary<(int Type, int Region), DaggerfallDialogueWorldRumorSave> _rumors = [];

    internal DaggerfallDialogueWorldState(DaggerfallDialogueWorldSave? saved = null)
    {
        foreach (DaggerfallDialogueWorldRumorSave rumor in (saved ?? DaggerfallDialogueWorldSave.Empty).Validate().Rumors)
            _rumors.Add((rumor.Type, rumor.Region), rumor);
    }

    /// <summary>
    /// Synchronizes one source-backed world rule and returns its current event when it
    /// is still speakable. Clearing the source flag retires the old transition marker.
    /// </summary>
    internal DaggerfallDialogueWorldRumorSave? Synchronize(int type, int region, int textId, bool enabled, long nowMinute)
    {
        if (type != CrimeWaveType)
            throw new ArgumentOutOfRangeException(nameof(type), type, "The dialogue world state has no owner for this generated event.");
        if (region is < 0 or > 61) throw new ArgumentOutOfRangeException(nameof(region), region, "A generated dialogue rumor names no classic region.");
        if (textId != CrimeWaveTextId) throw new ArgumentOutOfRangeException(nameof(textId), textId, $"A crime-wave dialogue rumor must use resource {CrimeWaveTextId}.");
        if (nowMinute < 0) throw new ArgumentOutOfRangeException(nameof(nowMinute), nowMinute, "A generated dialogue rumor starts before the calendar origin.");

        (int Type, int Region) key = (type, region);
        if (!enabled)
        {
            _rumors.Remove(key);
            return null;
        }

        if (!_rumors.TryGetValue(key, out DaggerfallDialogueWorldRumorSave? rumor))
        {
            rumor = new DaggerfallDialogueWorldRumorSave(type, region, textId, checked(nowMinute + GeneratedRumorDurationMinutes)).Validate();
            _rumors.Add(key, rumor);
        }

        return rumor.ExpiresAtMinute > nowMinute ? rumor : null;
    }

    /// <summary>
    /// Applies every authored dialogue-world rule to its complete scope. The variable store is
    /// the source of the transition; this owner retains the generated event and its expiry so
    /// opening a conversation is normally only a read of current news. The dialogue owner may
    /// invoke this same synchronization as a bounded catch-up when a variable changed between
    /// admitted updates.
    /// </summary>
    internal void Synchronize(DaggerfallDialogueWorldRules rules, DaggerfallVariableStore variables, long nowMinute)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(variables);
        if (nowMinute < 0) throw new ArgumentOutOfRangeException(nameof(nowMinute), nowMinute, "A dialogue-world transition starts before the calendar origin.");

        foreach (DaggerfallDialogueWorldNewsRule rule in rules.News)
        {
            if (rule.Scope != DaggerfallVariableScope.Region)
                throw new InvalidOperationException($"Dialogue world rule {rule.Type}/{rule.TextId} names {rule.Scope}, but the generated-event owner is regional.");
            for (int region = 0; region <= 61; region++)
            {
                // An authored condition with no variables cannot prove that its world event is
                // enabled. Current content carries one key (RegionDataFlags.CrimeWave), while
                // All retains the correct conjunction if another source-backed rule adds keys.
                bool enabled = rule.VariableKeys.Count != 0
                    && rule.VariableKeys.All(key => variables.Read(new DaggerfallVariableAddress(rule.Scope, region, key)) == rule.RequiredValue);
                Synchronize(rule.Type, region, rule.TextId, enabled, nowMinute);
            }
        }
    }

    /// <summary>Reads one still-speakable generated event without changing its transition marker.</summary>
    internal DaggerfallDialogueWorldRumorSave? ReadActive(int type, int region, int textId, long nowMinute)
    {
        if (type != CrimeWaveType)
            throw new ArgumentOutOfRangeException(nameof(type), type, "The dialogue world state has no owner for this generated event.");
        if (region is < 0 or > 61) throw new ArgumentOutOfRangeException(nameof(region), region, "A generated dialogue rumor names no classic region.");
        if (textId != CrimeWaveTextId) throw new ArgumentOutOfRangeException(nameof(textId), textId, $"A crime-wave dialogue rumor must use resource {CrimeWaveTextId}.");
        if (nowMinute < 0) throw new ArgumentOutOfRangeException(nameof(nowMinute), nowMinute, "A generated dialogue rumor is read before the calendar origin.");
        return _rumors.TryGetValue((type, region), out DaggerfallDialogueWorldRumorSave? rumor)
            && rumor.TextId == textId && rumor.ExpiresAtMinute > nowMinute ? rumor : null;
    }

    internal DaggerfallDialogueWorldSave Capture() =>
        new DaggerfallDialogueWorldSave([.. _rumors.Values.OrderBy(value => value.Type).ThenBy(value => value.Region)]).Validate();
}
