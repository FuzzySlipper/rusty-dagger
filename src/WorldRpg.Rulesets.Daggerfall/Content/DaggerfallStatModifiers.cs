using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Daggerfall stat modification in product vocabulary: permanent bases versus effect mods, with
/// per-source and per-handle removal. The donor reads live values as permanent plus manager
/// mods; the Engine Stat reads base plus modifiers the same way, so this owner only names which
/// side a change belongs to and which identity may take it back. Effect records arrive with
/// #7983; this is the application path they call.
/// </summary>
internal static class DaggerfallStatModifiers
{
    /// <summary>
    /// Moves a permanent base by the amount: a level or career change, not an effect. Only the
    /// specified source's contribution moves; other sources on the same stat stay.
    /// </summary>
    internal static void AdjustPermanent(StatsComponent stats, DaggerfallStatId id, int delta)
    {
        ArgumentNullException.ThrowIfNull(stats);
        Stat stat = stats.GetStat(StatId.Parse(id.Value));
        stat.BaseValue = stat.BaseValue + delta;
    }

    /// <summary>
    /// Applies an effect mod and answers its handle: removing the handle removes exactly this
    /// mod while overlapping mods from other sources stay.
    /// </summary>
    internal static StatModifierHandle ApplyMod(StatsComponent stats, DaggerfallStatId id, double amount, StatModifierKind kind = StatModifierKind.Add)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return stats.GetStat(StatId.Parse(id.Value)).AddModifier(amount, kind);
    }

    /// <summary>Removes exactly the mod the handle names; answers whether it was still present.</summary>
    internal static bool RemoveMod(StatsComponent stats, DaggerfallStatId id, StatModifierHandle handle)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(handle);
        return stats.GetStat(StatId.Parse(id.Value)).RemoveModifier(handle);
    }

    /// <summary>Removes every modifier the source identity owns on the stat.</summary>
    internal static void RemoveSource(StatsComponent stats, DaggerfallStatId id, MechanicsSourceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(identity);
        stats.GetStat(StatId.Parse(id.Value)).RemoveSource(identity);
    }

    /// <summary>
    /// Recomputes the three vital maxima from live strength, endurance and intelligence after an
    /// attribute change, the way creation computes them from authored bases. Tracks keep their
    /// current values; only the ceilings move. This follows the player vitals shape: enemy
    /// maxima come from their own level formulas, so a caller that changes enemy attributes
    /// recomputes through that owner's formulas instead.
    /// </summary>
    internal static void RefreshPlayerDerivedMaxima(StatsComponent stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        int strength = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).ValueInt;
        int intelligence = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Intelligence.Value)).ValueInt;
        int endurance = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt;
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value)).BaseValue = 25 + ((endurance * 3) / 2);
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).BaseValue = strength + endurance;
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).BaseValue = intelligence;
    }
}
