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

/// <summary>The paths that move the session calendar; each reaches its consumers through the calendar's list, AdvanceCalendar in DaggerfallSession.Update.cs.</summary>
internal sealed partial class DaggerfallSession
{
    private int _lastHolidayId;

    /// <summary>
    /// The holiday announcement the calendar currently names for the session's site, or null when
    /// the site is no settlement or no holiday is kept there today.
    /// </summary>
    internal World.DaggerfallHolidayAnnouncement? HolidayAnnouncement { get; private set; }

    /// <summary>
    /// Advances a rest, travel, prison, or other ruleset-owned elapsed interval through the session's
    /// single calendar.  A caller resumes <see cref="DaggerfallCalendarAdvance.RemainingSeconds"/>
    /// after handling a consequence; only the portion the calendar accepted advances effects.
    /// </summary>
    internal DaggerfallCalendarAdvance AdvanceElapsedTime(long gameSeconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)>? consequences = null,
        bool deferSkillAdvancement = false, bool resting = false, bool idleFatigue = true,
        bool completeTimeIncrease = true)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        DaggerfallCalendarAdvance advance = _time.AdvanceInterval(gameSeconds, consequences ?? []);
        AdvanceCalendar(calendarBefore,
            deferSkillAdvancement ? DaggerfallCalendarAdvanceKind.ElapsedDeferringSkills : DaggerfallCalendarAdvanceKind.Elapsed,
            resting: resting, idleFatigue: idleFatigue);
        // Elapsed rest/travel/service time can cross the donor outdoor population's dawn or dusk
        // boundary without an Engine admitted frame. Reconcile through the existing NPC/lifetime
        // owner so source civilians hide, retire, restore and preserve their durable identities.
        if (advance.AppliedSeconds > 0)
            ReconcileNpcProjection();
        // No admitted update refills Stamina through elapsed time, so the time itself does.
        if (advance.AppliedSeconds > 0) _stamina.Fill(State.Actors.Player.Stats);
        // Rest and travel admit many slices and complete the time increase once, at their end.
        if (!resting && completeTimeIncrease && advance.AppliedSeconds > 0) CompleteTimeIncrease();
        return advance;
    }

    /// <summary>Applies a quest-owned elapsed interval through the existing calendar without recursively re-running quest tasks.</summary>
    private void AdvanceQuestTime(long gameSeconds, bool idleFatigue = true)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        var advance = _time.AdvanceInterval(gameSeconds, []);
        AdvanceCalendar(calendarBefore, DaggerfallCalendarAdvanceKind.QuestAction, idleFatigue: idleFatigue);
        if (advance.AppliedSeconds > 0) CompleteTimeIncrease();
    }

    private void AdvanceEffectsForCalendar(DaggerfallCalendar before, DaggerfallCalendar after, bool ordinaryPlay, bool resting)
    {
        long minuteBefore = MinuteIndex(before);
        long minutes = MinuteIndex(after) - minuteBefore;
        if (minutes <= 0) return;

        long roundBefore = State.Effects.MagicRounds;
        AdvancePassiveRounds(roundBefore, minutes);
        AdvanceLycanthropyRound(after);
        RefreshLycanthropy();
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
    /// <summary>
    /// Ends one synthetic time increase (a rest, a journey, a service): held items reroll once and the
    /// day it ended on announces its holiday, as the donor shows only the current holiday's text.
    /// </summary>
    private void CompleteTimeIncrease()
    {
        _itemCastTriggers.CompleteTimeIncrease();
        AnnounceHoliday();
    }

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
