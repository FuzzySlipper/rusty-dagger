using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Lycanthropy, the werewolf's or wereboar's curse once its infection has run: becoming one, changing into beast
/// form and back (forced under a full moon unless Hircine's Ring is worn), the hunger that lowers the player's
/// maximum health until an innocent is killed, the cure, and the 84-day chance of the cure quest. It is handed the
/// racial override it rides on, the clock, its tuning, whether Hircine's Ring is worn, what empties the player's
/// hands for the change, the player, the draw, the line it tells, the quests it asks, the healing a new curse
/// brings, and "pass this much time": an elapsed interval and a quest-owned one.
/// </summary>
internal sealed class DaggerfallLycanthropy(DaggerfallRacialOverrides overrides, DaggerfallWorldTime time,
    DaggerfallLycanthropyTuning tuning, Func<bool> hircinesRing, Action emptyHands,
    ActorsState actors, IRandomService random, PresentationState presentation, IDaggerfallAfflictionQuests quests,
    DaggerfallTransformationHealing healing, Action<long> passTime, Action<long> passQuestTime)
{
    private const string CureQuest = "$CUREWER.txt";

    /// <summary>
    /// A werewolf or wereboar infection becomes the curse: the racial override, the healing, and the line.
    /// Refused while another racial override is active.
    /// </summary>
    internal DaggerfallInfectionConsumption Become(DaggerfallInfectionTransition transition)
    {
        var kind = transition.Kind == DaggerfallInfectionKind.Werewolf ? DaggerfallRacialKind.Werewolf : DaggerfallRacialKind.Wereboar;
        if (!overrides.Select(kind, $"lycanthropy:{transition.Instance}", time.Calendar.MinuteNumber))
            return new(false, "A racial override is already active.");
        healing.Heal();
        presentation.SetOutcome($"You are now a {overrides.Current!.Name.ToLowerInvariant()}.");
        return new(true, null);
    }

    internal bool Morph(bool forced = false, long? transitionMinute = null)
    {
        if (overrides.Current is not { IsVampire: false } racial) return false;
        long minute = transitionMinute ?? time.Calendar.MinuteNumber;
        if (!racial.State.BeastForm && !forced && !hircinesRing()
            && racial.State.LastMorphMinute is long last && minute - last <= tuning.MorphCooldownMinutes)
        {
            presentation.SetOutcome("You can only change into beast form once per day.");
            return false;
        }
        if (!racial.State.BeastForm) emptyHands();
        overrides.SetBeastForm(!racial.State.BeastForm, minute);
        Refresh();
        var health = actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Maximum.Value);
        presentation.SetOutcome(racial.State.BeastForm ? "You return to your human form." : $"You transform into a {racial.Name.ToLowerInvariant()}.");
        return true;
    }

    internal bool Cure(bool fromQuest = false)
    {
        if (overrides.Current is not { IsVampire: false } racial)
        {
            presentation.SetOutcome("Lycanthropy cure has no active curse to remove.");
            return false;
        }
        if (racial.State.BeastForm) Morph(forced: true);
        overrides.Remove(racial.Source);
        var health = actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Maximum.Value);
        if (fromQuest) passQuestTime(60);
        else passTime(60);
        presentation.SetOutcome("Your lycanthropy is cured.");
        return true;
    }

    /// <summary>Every 84 days the curse has lasted inside this calendar interval offers the cure quest once.</summary>
    internal void AdvanceQuestOpportunities(DaggerfallCalendar before, DaggerfallCalendar after)
    {
        if (overrides.Current is not { IsVampire: false } racial) return;
        const long period = 84L * 1440;
        long first = Math.Max(before.MinuteNumber, racial.State.AcquiredMinute), last = after.MinuteNumber;
        for (long boundary = ((first + period - 1) / period) * period; boundary < last; boundary += period)
            StartCureQuestOpportunity(boundary);
    }

    internal bool StartCureQuestOpportunity(long minute)
    {
        if (overrides.Current is not { IsVampire: false } racial) return false;
        if (random.DrawKeyed(new(0, "daggerfall.lycanthropy", $"{racial.Source}:{minute}:quest", 1, 100)).Value >= 30) return false;
        if (quests.Active(CureQuest)) return false;
        string identity = $"lycanthropy-cure:{racial.Source}:{minute}";
        if (quests.Started(identity)) return false;
        if (quests.Offer(identity, CureQuest, 0)) return true;
        presentation.SetOutcome("The cure for lycanthropy cannot be offered right now.");
        return false;
    }

    /// <summary>One magic round of the interval ending at <paramref name="after"/>: a full moon forces the change.</summary>
    internal void AdvanceRound(DaggerfallCalendar after)
    {
        if (overrides.Current is not { State.BeastForm: false, IsVampire: false } || hircinesRing()) return;
        // The donor's magic round reads the current date, and an elapsed catch-up runs its rounds
        // after the clock has moved: an interval forces the change only when it ends under a full
        // moon. Rest and travel deliver minute slices, so they observe every moon they cross.
        if (after.IsFullMoon) Morph(forced: true, transitionMinute: after.MinuteNumber);
    }

    /// <summary>The hunger's limit on the player's maximum health, as long as no innocent has been killed.</summary>
    internal void Refresh()
    {
        if (overrides.Current is not { IsVampire: false } racial) return;
        var actor = actors.Player;
        var maximum = actor.Stats.GetStat(StatId.Parse("health-maximum"));
        var identity = new EffectSourceIdentity(actor.Actor.Entity, EffectInstanceId.Parse(racial.Source), 1,
            SourceDefinitionId.Parse(DaggerfallRacialOverrides.EffectKey));
        long overdue = time.Calendar.MinuteNumber - racial.State.LastInnocentKilledMinute - tuning.HungerPeriodMinutes;
        var unbounded = maximum.Copy();
        unbounded.SetSources(StatId.Parse("health-maximum"), maximum.Sources.Where(source => source.Identity != identity));
        double? limit = overdue > 0 && !hircinesRing()
            ? Math.Max(tuning.MinimumHealth, unbounded.Value - Math.Round(overdue * tuning.HealthLossPerMinute, MidpointRounding.ToEven)) : null;
        var prior = maximum.Sources.SingleOrDefault(source => source.Identity == identity);
        if (limit is null && prior is null || limit is double same && prior?.Contributions.SingleOrDefault()?.Contribution is StatContribution.Maximum previous && previous.Value == same) return;
        if (limit is not null && prior is null) presentation.SetOutcome("Your lycanthropic hunger weakens you. You need to hunt an innocent.");
        var sources = maximum.Sources.Where(source => source.Identity != identity).ToList();
        if (limit is double value)
            sources.Add(new(identity, SourceDefinitionId.Parse(DaggerfallRacialOverrides.EffectKey), 0,
                [new(StatId.Parse("health-maximum"), StackingGroupId.Parse("daggerfall.lycanthropy.hunger"), MechanicsStackingPolicy.Sum, new StatContribution.Maximum(value))]));
        maximum.SetSources(StatId.Parse("health-maximum"), sources);
    }
}

internal sealed record DaggerfallLycanthropyTuning(int AttributeBonus, int SkillBonus, int MorphCooldownMinutes,
    int HungerPeriodMinutes, double HealthLossPerMinute, int MinimumHealth)
{
    internal static DaggerfallLycanthropyTuning Classic { get; } = new(40, 30, 1440, 43200, .0166667, 4);
    internal DaggerfallLycanthropyTuning Validate() => AttributeBonus >= 0 && SkillBonus >= 0 && MorphCooldownMinutes >= 0
        && HungerPeriodMinutes > 0 && double.IsFinite(HealthLossPerMinute) && HealthLossPerMinute > 0 && MinimumHealth > 0
        ? this : throw new ArgumentException("Lycanthropy tuning contains invalid bonuses, time periods or hunger limits.");
}
