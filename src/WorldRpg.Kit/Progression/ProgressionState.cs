namespace WorldRpg.Kit.Progression;

public sealed class ProgressionState
{
    public int Experience { get; private set; }
    public int Level { get; private set; } = 1;

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
}
