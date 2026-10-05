namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallSpellGrantKind { Lycanthropy, Vampirism }
internal sealed record DaggerfallSpellGrantSave(string Spell, string Source, DaggerfallSpellGrantKind Kind, bool Learned);

internal sealed partial class DaggerfallCharacterState
{
    private readonly Dictionary<string, DaggerfallSpellGrantSave> _spellGrants = new(StringComparer.Ordinal);
    internal bool IsGrantedSpell(string key) => _spellGrants.ContainsKey(key);
    internal IReadOnlyCollection<DaggerfallSpellGrantSave> SpellGrants => _spellGrants.Values;

    internal void GrantSpell(string key, string source, DaggerfallSpellGrantKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Enum.IsDefined(kind) || !_definitions.Magic.Spells.ContainsKey(key))
            throw new ArgumentException("Spell grant requires a published spell and a defined curse kind.");
        if (_spellGrants.TryGetValue(key, out var existing))
        {
            if (existing.Source != source || existing.Kind != kind)
                throw new InvalidOperationException("This spell already belongs to another active curse.");
            return;
        }
        bool learned = _knownSpells.Contains(key);
        _knownSpells.Add(key);
        _spellGrants.Add(key, new(key, source, kind, learned));
    }

    internal void RemoveSpellGrants(string source)
    {
        foreach (var grant in _spellGrants.Values.Where(grant => grant.Source == source).ToArray())
        {
            _spellGrants.Remove(grant.Spell);
            if (!grant.Learned && _knownSpells.Remove(grant.Spell)) SpellForgotten?.Invoke(grant.Spell);
        }
    }

    private void RestoreSpellGrants(DaggerfallSpellGrantSave[] grants)
    {
        foreach (var grant in grants)
        {
            if (string.IsNullOrWhiteSpace(grant.Source) || !Enum.IsDefined(grant.Kind)
                || !_knownSpells.Contains(grant.Spell) || !_spellGrants.TryAdd(grant.Spell, grant))
                throw new ArgumentException("Saved spell grant is malformed, duplicated, or missing its known spell.");
        }
    }
}
