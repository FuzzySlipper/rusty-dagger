namespace WorldRpg.Kit.Progression;

public sealed class ProgressionState
{
    private readonly Dictionary<string, int> _skillUses = new(StringComparer.Ordinal);

    public int Experience { get; private set; }
    public int Level { get; private set; } = 1;
    /// <summary>Ruleset-keyed use counters. The ruleset supplies the keys, caps and advancement policy.</summary>
    public IReadOnlyDictionary<string, int> SkillUses => _skillUses;

    public void Award(int amount)
    {
        if (amount < 0) return;
        Experience = checked(Experience + amount);
    }

    /// <summary>Applies an explicitly resolved profile state; Kit owns no XP curve.</summary>
    public void AdvanceTo(int experience, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(experience);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        if (experience < Experience || level < Level) throw new ArgumentException("Progression cannot move backwards.");
        Experience = experience;
        Level = level;
    }

    /// <summary>Records one accepted use under a ruleset-owned skill key.</summary>
    public int TallySkillUse(string skill, int amount, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        int current = _skillUses.GetValueOrDefault(skill);
        int next = Math.Min(maximum, checked(current + amount));
        _skillUses[skill] = next;
        return next;
    }

    /// <summary>Resets a ruleset-owned counter after its advancement policy consumed it.</summary>
    public void ResetSkillUse(string skill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);
        _skillUses[skill] = 0;
    }

    /// <summary>Restores the complete current counter family after the ruleset validated its keys.</summary>
    public void RestoreSkillUses(IEnumerable<KeyValuePair<string, int>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, int> restored = new(StringComparer.Ordinal);
        foreach ((string skill, int amount) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skill);
            ArgumentOutOfRangeException.ThrowIfNegative(amount);
            if (!restored.TryAdd(skill, amount)) throw new ArgumentException($"Skill use '{skill}' appears more than once.", nameof(values));
        }

        _skillUses.Clear();
        foreach ((string skill, int amount) in restored) _skillUses.Add(skill, amount);
    }
}
