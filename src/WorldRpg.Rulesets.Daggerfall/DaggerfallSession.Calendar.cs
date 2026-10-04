using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

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
    /// skills raise, and a requested encounter is selected when any time was applied.
    /// </summary>
    Elapsed,

    /// <summary>
    /// One slice of a rest: as <see cref="Elapsed"/>, but the rest owner raises skills once when the
    /// whole rest ends rather than every slice.
    /// </summary>
    ElapsedDeferringSkills,

    /// <summary>
    /// A quest-owned training interval: skills raise, but quest tasks do not run, because the quest
    /// runtime that asked for the interval is still running its own tasks.
    /// </summary>
    QuestTraining,
}

/// <summary>The one calendar fan-out: every path that moves the session calendar reaches its consumers here.</summary>
internal sealed partial class DaggerfallSession
{
    private int _lastHolidayId;

    /// <summary>
    /// The holiday announcement the calendar currently names for the session's site, or null when
    /// the site is no settlement or no holiday is kept there today.
    /// </summary>
    internal World.DaggerfallHolidayAnnouncement? HolidayAnnouncement { get; private set; }

    /// <summary>
    /// Delivers one calendar interval, already applied to the clock, to its consumers in their one
    /// order. Each consumer reads the live calendar rather than a captured end point: a quest clock
    /// can run a training interval of its own, and the consumers after it observe that time too.
    /// </summary>
    /// <param name="before">The calendar before the interval was applied.</param>
    /// <param name="kind">What admitted the interval; see <see cref="DaggerfallCalendarAdvanceKind"/>.</param>
    /// <param name="encounter">An elapsed interval's encounter request, selected after effects advance.</param>
    /// <param name="simulate">Ordinary play's admitted simulation steps, which run between the fan-out and locomotion.</param>
    private void AdvanceCalendar(DaggerfallCalendar before, DaggerfallCalendarAdvanceKind kind,
        DaggerfallEncounterRequest? encounter = null, Action? simulate = null, bool resting = false)
    {
        bool ordinaryPlay = kind == DaggerfallCalendarAdvanceKind.OrdinaryPlay;
        if (ordinaryPlay != simulate is not null)
            throw new ArgumentException("Only ordinary play runs simulation steps inside its calendar interval.", nameof(simulate));
        if (encounter is not null && kind is not (DaggerfallCalendarAdvanceKind.Elapsed or DaggerfallCalendarAdvanceKind.ElapsedDeferringSkills))
            throw new ArgumentException("Only an elapsed interval selects an encounter.", nameof(encounter));
        _locomotion.SetAthletics(
            State.Character.CustomCareer?.Advantages.Any(trait => trait.Id == "athleticism") == true,
            State.HeldEnchantments.Talents.Athleticism);
        long minuteBefore = MinuteIndex(before);
        State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
        _sites.Projection.Lighting.UpdateAmbient(_time.Calendar);
        State.Quests.AdvanceClocks(State.Variables, before, _time.Calendar);
        // Daily conditions and ordinary source-order operations observe the same admitted calendar
        // after rest, travel, prison, or another interval, including an interval with no clock expiry.
        if (kind is DaggerfallCalendarAdvanceKind.Elapsed or DaggerfallCalendarAdvanceKind.ElapsedDeferringSkills)
        {
            State.Quests.Advance(State.Variables, _time.Calendar);
            State.Quests.ReconcileFoeCommands();
        }
        _dialogue?.RefreshEligibility();
        if (kind is DaggerfallCalendarAdvanceKind.Elapsed or DaggerfallCalendarAdvanceKind.QuestTraining)
        {
            State.SkillUses.RaiseSkills(_time.Calendar.ToAbsoluteSeconds());
            State.LevelUps.BeginIfEligible();
        }
        State.Social.AdvanceElapsedMinutes(minuteBefore, MinuteIndex(_time.Calendar));
        AdvanceLoans();
        ExpireConjuredItems();
        AdvanceEffectsForCalendar(before, ordinaryPlay, resting);
        if (encounter is not null) QueueEncounter(encounter);
        AnnounceHoliday();
        if (!ordinaryPlay)
        {
            // Rest suppresses newly incurred idle loss, while travel, prison, and other elapsed
            // callers still settle every calendar minute through this existing owner. Any accepted
            // movement seconds carried from the prior admitted update are settled before reset.
            _locomotion.AdvanceCalendarMinutes(minuteBefore, MinuteIndex(_time.Calendar), State.Actors.Player.Stats,
                includeIdleFatigue: !resting);
            return;
        }
        simulate!();
        // Locomotion charges the update's minutes once its steps have recorded how they were spent.
        _locomotion.AdvanceCalendarMinutes(minuteBefore, MinuteIndex(_time.Calendar), State.Actors.Player.Stats);
    }

    /// <summary>
    /// Advances a rest, travel, prison, or other ruleset-owned elapsed interval through the session's
    /// single calendar.  A caller resumes <see cref="DaggerfallCalendarAdvance.RemainingSeconds"/>
    /// after handling a consequence; only the portion the calendar accepted advances effects.
    /// </summary>
    internal DaggerfallCalendarAdvance AdvanceElapsedTime(long gameSeconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)>? consequences = null,
        DaggerfallEncounterRequest? encounter = null,
        bool deferSkillAdvancement = false, bool resting = false)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        DaggerfallCalendarAdvance advance = _time.AdvanceInterval(gameSeconds, consequences ?? []);
        AdvanceCalendar(calendarBefore,
            deferSkillAdvancement ? DaggerfallCalendarAdvanceKind.ElapsedDeferringSkills : DaggerfallCalendarAdvanceKind.Elapsed,
            advance.AppliedSeconds > 0 ? encounter : null, resting: resting);
        if (!resting && advance.AppliedSeconds > 0) _itemCastTriggers.CompleteTimeIncrease();
        return advance;
    }

    /// <summary>Applies a quest-owned training interval through the existing calendar without recursively re-running quest tasks.</summary>
    private void AdvanceQuestTraining(long gameSeconds)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        var advance = _time.AdvanceInterval(gameSeconds, []);
        AdvanceCalendar(calendarBefore, DaggerfallCalendarAdvanceKind.QuestTraining);
        if (advance.AppliedSeconds > 0) _itemCastTriggers.CompleteTimeIncrease();
    }

    private void AdvanceEffectsForCalendar(DaggerfallCalendar before, bool ordinaryPlay, bool resting)
    {
        long minuteBefore = MinuteIndex(before);
        long minutes = MinuteIndex(_time.Calendar) - minuteBefore;
        if (minutes <= 0) return;

        long roundBefore = State.Effects.MagicRounds;
        // The normal path is expressed as its normal one-round operation.  Multiple minutes (whether
        // an unusually long admitted update or an elapsed interval) retain the donor's bounded
        // catch-up policy inside the lifecycle.
        if (ordinaryPlay && minutes == 1)
        {
            State.Effects.AdvanceOrdinaryRound();
            State.HeldEnchantments.AdvanceRounds(1);
            _itemCastTriggers.AdvanceRounds(1, synthetic: false, roundBefore: roundBefore);
            return;
        }

        _ = State.Effects.AdvanceElapsedRounds(minutes);
        State.HeldEnchantments.AdvanceRounds(checked((int)Math.Min(minutes, int.MaxValue)), synthetic: !ordinaryPlay && !resting);
        _itemCastTriggers.AdvanceRounds(minutes, synthetic: !ordinaryPlay && !resting, resting: resting, roundBefore: roundBefore);
    }

    private static long MinuteIndex(DaggerfallCalendar calendar) =>
        (calendar.DayNumber * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)
        + (calendar.Hour * DaggerfallCalendar.MinutesPerHour)
        + calendar.Minute;

    /// <summary>
    /// Publishes the holiday announcement when the calendar names a new one for the session's site.
    /// </summary>
    /// <remarks>
    /// The check runs on the first playing update and on every date change after it, which is how a
    /// session constructed or restored onto a holiday announces once, the way the donor announces on
    /// entering an eligible location and after loading: the tracking restarts unannounced, so the first observation
    /// of a kept holiday is itself the entry. A session standing at a dungeon, a graveyard, a coven or
    /// the player's ship never announces, and leaving a holiday clears the tracking silently rather than reporting the ordinary day.
    /// </remarks>
    private void AnnounceHoliday()
    {
        World.DaggerfallHolidayAnnouncement? announcement =
            World.DaggerfallHolidayAnnouncement.ForDate(_time.Calendar, _site.Region, _site.ActiveSite?.Kind);
        int holidayId = announcement?.HolidayId ?? 0;
        if (holidayId == _lastHolidayId)
        {
            return;
        }

        _lastHolidayId = holidayId;
        HolidayAnnouncement = announcement;
        if (announcement is not null)
        {
            Presentation.SetOutcome(_definitions.TextPresentation.Resolve(announcement.TextKey, DaggerfallTextContext.Empty).Text);
        }
    }
}
