namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What admitted a calendar interval, and so which calendar consumers it reaches.</summary>
/// <remarks>
/// Every kind reaches regional prices, site lighting, quest clocks, social minutes, loans, active
/// effects and the holiday announcement, in that order. The kinds differ only where this enum says.
/// </remarks>
internal enum DaggerfallCalendarAdvanceKind
{
    /// <summary>
    /// Realtime play inside an admitted update. A single elapsed minute is one ordinary magic round;
    /// the update's simulation steps run after the fan-out and run quest tasks themselves; locomotion
    /// charges the update's minutes after those steps. No skill raise.
    /// </summary>
    OrdinaryPlay,

    /// <summary>
    /// A rest, travel, prison or service interval: quest tasks run against the admitted calendar,
    /// and skills raise. Rest and travel select their own per-minute encounters between slices.
    /// </summary>
    Elapsed,

    /// <summary>
    /// One slice of a rest: as <see cref="Elapsed"/>, but the rest owner raises skills once when the
    /// whole rest ends rather than every slice.
    /// </summary>
    ElapsedDeferringSkills,

    /// <summary>
    /// A quest-owned elapsed interval: skills raise, but quest tasks do not run, because the quest
    /// runtime that asked for the interval is still running its own tasks.
    /// </summary>
    QuestAction,
}
