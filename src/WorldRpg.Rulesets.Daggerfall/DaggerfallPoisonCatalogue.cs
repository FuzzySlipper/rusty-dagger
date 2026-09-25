using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One archetype together with the classic record the pack carries for it, when the pack carries one.
/// </summary>
/// <remarks>
/// The record is optional because the two halves come from different places: the pack owns the eleven
/// poison records the classic spell file carries, while the twelfth archetype exists only in
/// <see cref="DaggerfallPoisonArchetypes"/>. A consumer that needs a name or the classic effect rows reads
/// them through here rather than assuming both halves are always present.
/// </remarks>
internal sealed record DaggerfallPoisonArchetypeRecord(
    DaggerfallPoisonArchetype Archetype,
    DaggerfallSpellDefinition? Record)
{
    /// <summary>The archetype's own name, which is the donor's spelling of the record's.</summary>
    internal string Name => Archetype.Name;

    /// <summary>Whether the pack carries a classic record for this archetype.</summary>
    internal bool HasRecord => Record is not null;

    /// <summary>The classic effect rows the record declares, empty when the pack carries no record.</summary>
    internal IReadOnlyList<DaggerfallSpellEffectDefinition> RecordedEffects =>
        Record?.Effects ?? [];
}

/// <summary>
/// Joins the twelve archetypes to the classic records the pack carries, so a poison consumer reads one
/// value instead of pairing two tables itself.
/// </summary>
/// <remarks>
/// The join is by name rather than by position. Both halves name the same poisons, but the record spells
/// them for display and the donor for an enum, so the comparison drops everything that is not a letter or
/// digit: the record's <c>!Nux Vomica</c> and the donor's <c>Nux_Vomica</c> both become <c>nuxvomica</c>.
/// That is stricter than counting records, so a record that ever moved or a name that was mistyped fails
/// the join instead of silently pairing the wrong poisons; the thirteenth bang-named record, which is
/// lycanthropy, simply has no archetype to join to.
/// </remarks>
internal static class DaggerfallPoisonCatalogue
{
    /// <summary>The archetypes with their records, in the table's own order.</summary>
    internal static IReadOnlyList<DaggerfallPoisonArchetypeRecord> Join(DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        Dictionary<string, DaggerfallSpellDefinition> records = new(StringComparer.Ordinal);
        foreach (DaggerfallSpellDefinition spell in definitions.Magic.Spells.Values)
        {
            if (string.IsNullOrEmpty(spell.Name) || spell.Name[0] != '!') continue;
            records[Comparable(spell.Name)] = spell;
        }

        DaggerfallPoisonArchetypeRecord[] joined = new DaggerfallPoisonArchetypeRecord[DaggerfallPoisonArchetypes.All.Count];
        for (int index = 0; index < DaggerfallPoisonArchetypes.All.Count; index++)
        {
            DaggerfallPoisonArchetype archetype = DaggerfallPoisonArchetypes.All[index];
            joined[index] = new DaggerfallPoisonArchetypeRecord(
                archetype,
                records.GetValueOrDefault(Comparable(archetype.Name)));
        }
        return joined;
    }

    /// <summary>The archetype a classic variant names, with the record the pack carries for it.</summary>
    internal static bool TryResolve(
        DaggerfallDefinitions definitions, int variant, out DaggerfallPoisonArchetypeRecord resolved)
    {
        foreach (DaggerfallPoisonArchetypeRecord candidate in Join(definitions))
        {
            if (candidate.Archetype.Variant != variant) continue;
            resolved = candidate;
            return true;
        }
        resolved = null!;
        return false;
    }

    /// <summary>What two spellings of one poison have in common: its letters and digits, lowercased.</summary>
    private static string Comparable(string name)
    {
        Span<char> buffer = name.Length <= 64 ? stackalloc char[name.Length] : new char[name.Length];
        int length = 0;
        foreach (char character in name)
        {
            if (!char.IsLetterOrDigit(character)) continue;
            buffer[length++] = char.ToLowerInvariant(character);
        }
        return new string(buffer[..length]);
    }
}
