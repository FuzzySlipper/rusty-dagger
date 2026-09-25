namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Where a poison is in its course: still counting down, ticking, or done.</summary>
internal enum DaggerfallPoisonPhase
{
    /// <summary>Afflicted but not yet acting; the donor calls this waiting.</summary>
    Waiting,

    /// <summary>Acting once a minute.</summary>
    Active,

    /// <summary>Its minutes are spent and it has nothing left to give back.</summary>
    Complete,
}

/// <summary>
/// One actor's poison: which archetype, how many minutes until it starts, and how many minutes it has
/// left to act. The donor keeps these as three fields on its poison effect and ticks them in minutes,
/// which is what this value carries.
/// </summary>
/// <remarks>
/// The two rolls are inclusive because that is how the archetype records its windows; the donor rolls them
/// with an exclusive maximum and adds one, so the caller's roll is asked for the inclusive window here.
/// </remarks>
internal sealed class DaggerfallPoisonAffliction
{
    internal DaggerfallPoisonAffliction(DaggerfallPoisonArchetype archetype, int minutesToStart, int minutesRemaining)
        : this(archetype, minutesToStart, minutesRemaining, allowsComplete: false)
    {
    }

    private DaggerfallPoisonAffliction(DaggerfallPoisonArchetype archetype, int minutesToStart, int minutesRemaining, bool allowsComplete)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        if (minutesToStart < 0)
            throw new ArgumentOutOfRangeException(nameof(minutesToStart), minutesToStart, "A poison cannot start before now.");
        if (minutesRemaining < 1 && !allowsComplete)
            throw new ArgumentOutOfRangeException(nameof(minutesRemaining), minutesRemaining, "A poison lasts at least one minute.");
        Archetype = archetype;
        MinutesToStart = minutesToStart;
        MinutesRemaining = minutesRemaining;
    }

    /// <summary>
    /// The affliction a reload finds: a poison that has run its course and is carried only for the attribute
    /// damage it left, which is a state a fresh affliction cannot be constructed in because every poison
    /// that starts has at least one minute to give.
    /// </summary>
    internal static DaggerfallPoisonAffliction AlreadyComplete(DaggerfallPoisonArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        return new DaggerfallPoisonAffliction(archetype, minutesToStart: 0, minutesRemaining: 0, allowsComplete: true);
    }

    internal DaggerfallPoisonArchetype Archetype { get; }

    /// <summary>The minutes still to pass before the first tick, zero once it is acting.</summary>
    internal int MinutesToStart { get; private set; }

    /// <summary>The ticks the poison still has to give, counting the one it is about to give.</summary>
    internal int MinutesRemaining { get; private set; }

    internal DaggerfallPoisonPhase Phase => MinutesRemaining < 1
        ? DaggerfallPoisonPhase.Complete
        : MinutesToStart < 1 ? DaggerfallPoisonPhase.Active : DaggerfallPoisonPhase.Waiting;

    /// <summary>Whether this affliction's archetype has an arm that helps its victim rather than harming them.</summary>
    internal bool HasPositiveArm => Archetype.Effects.Any(effect => effect.IsPositive);

    /// <summary>
    /// Passes one minute. Returns the arms that act now, empty while the poison is still counting down or
    /// once it is complete, so a caller applies what it is handed rather than deciding the phase again.
    /// </summary>
    internal IReadOnlyList<DaggerfallPoisonEffect> AdvanceMinute()
    {
        if (Phase == DaggerfallPoisonPhase.Complete) return [];
        if (MinutesToStart > 0)
        {
            MinutesToStart--;
            if (MinutesToStart > 0) return [];
        }
        MinutesRemaining--;
        return Archetype.Effects;
    }

    /// <summary>What the affliction still has to give: the onset it waits out plus the ticks it has left.</summary>
    internal int TotalMinutesRemaining => MinutesToStart + MinutesRemaining;

    /// <summary>
    /// Whether a newly applied poison should take this one's place. A second dose does not shorten a
    /// poison already running, so the new one wins only when it has more left to give than what is there.
    /// </summary>
    internal bool SupersededBy(DaggerfallPoisonAffliction candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.TotalMinutesRemaining > TotalMinutesRemaining;
    }

    /// <summary>
    /// Ends the affliction where it stands, for a cure. The arms already given are not taken back here:
    /// the caller knows which of them landed and reverses those.
    /// </summary>
    internal void Cure()
    {
        MinutesToStart = 0;
        MinutesRemaining = 0;
    }
}
