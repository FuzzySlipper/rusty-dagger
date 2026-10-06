using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Permanent curse policy uses the current racial effect, calendar, equipment and actor owners.</summary>
internal sealed partial class DaggerfallSession : IDaggerfallTransformationConsumer
{
    bool IDaggerfallTransformationConsumer.HasRacialOverride => State.RacialOverrides.Current is not null;

    DaggerfallInfectionConsumption IDaggerfallTransformationConsumer.Consume(DaggerfallInfectionTransition transition)
    {
        if (transition.Kind == DaggerfallInfectionKind.Vampire)
            return ConsumeVampireInfection(transition);
        var kind = transition.Kind == DaggerfallInfectionKind.Werewolf ? DaggerfallRacialKind.Werewolf : DaggerfallRacialKind.Wereboar;
        if (!State.RacialOverrides.Select(kind, $"lycanthropy:{transition.Instance}", MinuteIndex(_time.Calendar)))
            return new(false, "A racial override is already active.");
        HealRacialTransformation();
        Presentation.SetOutcome($"You are now a {State.RacialOverrides.Current!.Name.ToLowerInvariant()}.");
        return new(true, null);
    }

    private void HealRacialTransformation()
    {
        foreach (var disease in Enum.GetValues<DaggerfallClassicDisease>()) CureDisease(disease);
        CurePoison();
        foreach (var attribute in DaggerfallMechanicsIds.Attributes)
            DaggerfallAttributeDrainEffects.Heal(State.Effects, DaggerfallActorIdentity.PlayerEntityId, attribute,
                int.MaxValue, () => State.Character.Career);
        foreach (string track in new[] { "health", "stamina", "magicka" })
        {
            var value = State.Actors.Player.Stats.GetTrack(TrackId.Parse(track));
            value.SetCurrent(value.Maximum.Value);
        }
    }

    internal bool MorphPlayer(bool forced = false, long? transitionMinute = null)
    {
        if (State.RacialOverrides.Current is not { IsVampire: false } racial) return false;
        long minute = transitionMinute ?? MinuteIndex(_time.Calendar);
        if (!racial.State.BeastForm && !forced && !State.HeldEnchantments.HircinesRingEquipped
            && racial.State.LastMorphMinute is long last && minute - last <= _tuning.Lycanthropy.MorphCooldownMinutes)
        {
            Presentation.SetOutcome("You can only change into beast form once per day.");
            return false;
        }
        if (!racial.State.BeastForm) _equipmentMoves.UnequipHands();
        State.RacialOverrides.SetBeastForm(!racial.State.BeastForm, minute);
        RefreshLycanthropy();
        var health = State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Maximum.Value);
        Presentation.SetOutcome(racial.State.BeastForm ? "You return to your human form." : $"You transform into a {racial.Name.ToLowerInvariant()}.");
        return true;
    }

    internal bool CureLycanthropy(bool fromQuest = false)
    {
        if (State.RacialOverrides.Current is not { IsVampire: false } racial)
        {
            Presentation.SetOutcome("Lycanthropy cure has no active curse to remove.");
            return false;
        }
        if (racial.State.BeastForm) MorphPlayer(forced: true);
        State.RacialOverrides.Remove(racial.Source);
        var health = State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Maximum.Value);
        if (fromQuest) AdvanceQuestTime(60);
        else AdvanceElapsedTime(60);
        Presentation.SetOutcome("Your lycanthropy is cured.");
        return true;
    }

    private void AdvanceLycanthropyQuestOpportunities(DaggerfallCalendar before, DaggerfallCalendar after)
    {
        if (State.RacialOverrides.Current is not { IsVampire: false } racial) return;
        const long period = 84L * 1440;
        long first = Math.Max(MinuteIndex(before), racial.State.AcquiredMinute), last = MinuteIndex(after);
        for (long boundary = ((first + period - 1) / period) * period; boundary < last; boundary += period)
            StartLycanthropyCureQuestOpportunity(boundary);
    }

    internal bool StartLycanthropyCureQuestOpportunity(long minute)
    {
        if (State.RacialOverrides.Current is not { IsVampire: false } racial) return false;
        if (_random.DrawKeyed(new(0, "daggerfall.lycanthropy", $"{racial.Source}:{minute}:quest", 1, 100)).Value >= 30) return false;
        if (State.Quests.All.Any(value => value.SourceFile == "$CUREWER.txt" && value.Lifecycle == DaggerfallQuestLifecycle.Active)) return false;
        string identity = $"lycanthropy-cure:{racial.Source}:{minute}";
        if (State.Quests.All.Any(value => value.InstanceId == identity)) return false;
        try
        {
            var source = _definitions.QuestSources.Resolve("$CUREWER.txt");
            State.Quests.Start(new(identity, source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            Presentation.SetOutcome($"Lycanthropy cure quest is unavailable: {error.Message}");
            return false;
        }
    }

    private void AdvanceLycanthropyRound(DaggerfallCalendar after)
    {
        if (State.RacialOverrides.Current is not { State.BeastForm: false, IsVampire: false } || State.HeldEnchantments.HircinesRingEquipped) return;
        // The donor's magic round reads the current date, and an elapsed catch-up runs its rounds
        // after the clock has moved: an interval forces the change only when it ends under a full
        // moon. Rest and travel deliver minute slices, so they observe every moon they cross.
        if (after.IsFullMoon) MorphPlayer(forced: true, transitionMinute: MinuteIndex(after));
    }

    private void RefreshLycanthropy()
    {
        if (State.RacialOverrides.Current is not { IsVampire: false } racial) return;
        var actor = State.Actors.Player;
        var maximum = actor.Stats.GetStat(StatId.Parse("health-maximum"));
        var identity = new EffectSourceIdentity(actor.Actor.Entity, EffectInstanceId.Parse(racial.Source), 1,
            SourceDefinitionId.Parse(DaggerfallRacialOverrides.EffectKey));
        long overdue = MinuteIndex(_time.Calendar) - racial.State.LastInnocentKilledMinute - _tuning.Lycanthropy.HungerPeriodMinutes;
        var unbounded = maximum.Copy();
        unbounded.SetSources(StatId.Parse("health-maximum"), maximum.Sources.Where(source => source.Identity != identity));
        double? limit = overdue > 0 && !State.HeldEnchantments.HircinesRingEquipped
            ? Math.Max(_tuning.Lycanthropy.MinimumHealth, unbounded.Value - Math.Round(overdue * _tuning.Lycanthropy.HealthLossPerMinute, MidpointRounding.ToEven)) : null;
        var prior = maximum.Sources.SingleOrDefault(source => source.Identity == identity);
        if (limit is null && prior is null || limit is double same && prior?.Contributions.SingleOrDefault()?.Contribution is StatContribution.Maximum previous && previous.Value == same) return;
        if (limit is not null && prior is null) Presentation.SetOutcome("Your lycanthropic hunger weakens you. You need to hunt an innocent.");
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
