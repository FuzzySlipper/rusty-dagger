using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSkillUseReactionTests
{
    [Fact]
    public void Accepted_combat_reasons_share_capped_counters_and_keep_the_donor_attribution_contract()
    {
        (DaggerfallSkillUseReactions uses, ProgressionState progression, _) = Create();

        for (int index = 0; index < DaggerfallSkillUseReactions.MaximumSkillUses + 5; index++)
            uses.Record(new("long-blade", DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseOutcome.Succeeded));
        uses.Record(new("critical-strike", DaggerfallSkillUseReason.CriticalStrikeHit, DaggerfallSkillUseOutcome.Succeeded));

        Assert.Equal(20_000, progression.SkillUses["long-blade"]);
        Assert.Equal(1, progression.SkillUses["critical-strike"]);
        Assert.Contains(DaggerfallSkillUseReactions.AttributionPolicies, policy =>
            policy.Reason == DaggerfallSkillUseReason.WeaponHit
            && policy.Domain == DaggerfallSkillUseDomain.Combat
            && policy.Amount == 1
            && policy.Outcome == DaggerfallSkillUseOutcome.Succeeded
            && policy.Cadence == DaggerfallSkillUseCadence.PerAcceptedOperation);
        Assert.Contains(DaggerfallSkillUseReactions.AttributionPolicies, policy =>
            policy.Reason == DaggerfallSkillUseReason.ReleasedSpellEffect
            && policy.Domain == DaggerfallSkillUseDomain.Magic);
        Assert.Contains(DaggerfallSkillUseReactions.AttributionPolicies, policy =>
            policy.Reason == DaggerfallSkillUseReason.Swimming
            && policy.Domain == DaggerfallSkillUseDomain.Physical
            && policy.Cadence == DaggerfallSkillUseCadence.PerGameMinute);
        Assert.Contains(DaggerfallSkillUseReactions.AttributionPolicies, policy =>
            policy.Reason == DaggerfallSkillUseReason.DialogueEtiquette
            && policy.Domain == DaggerfallSkillUseDomain.Social);
    }

    [Fact]
    public void Minute_governed_uses_reject_invalid_outcomes_and_only_count_one_admitted_minute()
    {
        (DaggerfallSkillUseReactions uses, ProgressionState progression, _) = Create();

        Assert.Throws<ArgumentException>(() => uses.Record(new("lockpicking", DaggerfallSkillUseReason.LockpickingAttempt, DaggerfallSkillUseOutcome.Succeeded)));
        Assert.Throws<ArgumentException>(() => uses.Record(new("swimming", DaggerfallSkillUseReason.Swimming, DaggerfallSkillUseOutcome.Accepted)));
        Assert.Throws<ArgumentException>(() => uses.Record(new("medical", DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseOutcome.Succeeded)));
        Assert.Throws<ArgumentException>(() => uses.Record(new("running", DaggerfallSkillUseReason.ReleasedSpellEffect, DaggerfallSkillUseOutcome.Accepted)));
        Assert.Throws<ArgumentException>(() => uses.Record(new("etiquette", DaggerfallSkillUseReason.PacificationFailed, DaggerfallSkillUseOutcome.Attempted)));
        Assert.True(uses.Record(new("etiquette", DaggerfallSkillUseReason.PacificationSucceeded, DaggerfallSkillUseOutcome.Succeeded)));
        Assert.True(uses.Record(new("swimming", DaggerfallSkillUseReason.Swimming, DaggerfallSkillUseOutcome.Accepted, 42)));
        Assert.False(uses.Record(new("swimming", DaggerfallSkillUseReason.Swimming, DaggerfallSkillUseOutcome.Accepted, 42)));
        Assert.True(uses.Record(new("swimming", DaggerfallSkillUseReason.Swimming, DaggerfallSkillUseOutcome.Accepted, 43)));
        Assert.Throws<ArgumentException>(() => uses.Record(new("swimming", DaggerfallSkillUseReason.Swimming, DaggerfallSkillUseOutcome.Accepted, 41)));

        Assert.Equal(2, progression.SkillUses["swimming"]);

        (DaggerfallSkillUseReactions earlyCalendar, _, _) = Create();
        Assert.True(earlyCalendar.Record(new("stealth", DaggerfallSkillUseReason.StealthCheck, DaggerfallSkillUseOutcome.Attempted, -1)));
        Assert.False(earlyCalendar.Record(new("stealth", DaggerfallSkillUseReason.StealthCheck, DaggerfallSkillUseOutcome.Attempted, -1)));
    }

    [Fact]
    public void Counters_prelevel_baseline_and_minute_gate_round_trip_without_experimental_kill_xp()
    {
        (DaggerfallSkillUseReactions original, ProgressionState progression, _) = Create();
        for (int index = 0; index < 7; index++)
            original.Record(new("long-blade", DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseOutcome.Succeeded));
        for (int index = 0; index < 3; index++)
            original.Record(new("critical-strike", DaggerfallSkillUseReason.CriticalStrikeHit, DaggerfallSkillUseOutcome.Succeeded));
        original.Record(new("stealth", DaggerfallSkillUseReason.StealthCheck, DaggerfallSkillUseOutcome.Attempted, 88));
        DaggerfallSkillProgressionSave saved = original.Capture();

        (DaggerfallSkillUseReactions restored, ProgressionState restoredProgression, _) = Create();
        restored.Restore(saved);

        Assert.Equal(saved.StartingLevelUpSkillSum, restored.StartingLevelUpSkillSum);
        Assert.Equal(7, restoredProgression.SkillUses["long-blade"]);
        Assert.Equal(3, restoredProgression.SkillUses["critical-strike"]);
        Assert.Equal(1, restoredProgression.SkillUses["stealth"]);
        Assert.False(restored.Record(new("stealth", DaggerfallSkillUseReason.StealthCheck, DaggerfallSkillUseOutcome.Attempted, 88)));
        Assert.Equal(0, progression.Experience);
        Assert.Equal(0, restoredProgression.Experience);
    }

    [Fact]
    public void Rest_skill_check_consumes_each_qualified_counter_once_and_exposes_level_eligibility()
    {
        (DaggerfallSkillUseReactions uses, ProgressionState progression, StatsComponent stats) = Create(stats =>
        {
            foreach (string skill in new[] { "mysticism", "alteration", "thaumaturgy", "illusion", "destruction", "restoration", "medical", "short-blade", "blunt-weapon", "dragonish", "daedric", "dodging" })
                stats.GetStat(StatId.Parse(skill)).BaseValue = 10;
            stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;
        });
        int baseline = uses.StartingLevelUpSkillSum;
        progression.TallySkillUse("mysticism", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);
        progression.TallySkillUse("destruction", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);

        Assert.False(uses.RaiseSkills(360));
        Assert.True(uses.RaiseSkills(361));
        Assert.Equal(11d, stats.GetStat(StatId.Parse("mysticism")).BaseValue);
        Assert.Equal(11d, stats.GetStat(StatId.Parse("destruction")).BaseValue);
        Assert.Equal(0, progression.SkillUses["mysticism"]);
        Assert.Equal(0, progression.SkillUses["destruction"]);
        Assert.Equal(baseline + 2, uses.CurrentLevelUpSkillSum);
        Assert.Equal(2, uses.CalculatedPlayerLevel);
        Assert.True(uses.PendingLevelUp);
        Assert.Equal(1, progression.Level);

        Assert.False(uses.RaiseSkills(361));
        Assert.False(uses.RaiseSkills(721));
        Assert.True(uses.RaiseSkills(722));
    }

    [Fact]
    public void Rest_skill_check_consumes_thresholds_before_the_donor_skill_and_primary_master_caps()
    {
        (DaggerfallSkillUseReactions uses, ProgressionState progression, StatsComponent stats) = Create(stats =>
        {
            stats.GetStat(StatId.Parse("mysticism")).BaseValue = 100;
            stats.GetStat(StatId.Parse("destruction")).BaseValue = 100;
            stats.GetStat(StatId.Parse("short-blade")).BaseValue = 95;
            stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;
        });
        progression.TallySkillUse("destruction", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);
        progression.TallySkillUse("short-blade", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);

        Assert.True(uses.RaiseSkills(361));
        Assert.Equal(100d, stats.GetStat(StatId.Parse("destruction")).BaseValue);
        Assert.Equal(95d, stats.GetStat(StatId.Parse("short-blade")).BaseValue);
        Assert.Equal(0, progression.SkillUses["destruction"]);
        Assert.Equal(0, progression.SkillUses["short-blade"]);
    }

    [Fact]
    public void Rest_skill_check_interval_round_trips_with_pending_counters()
    {
        (DaggerfallSkillUseReactions original, ProgressionState progression, StatsComponent stats) = Create(stats =>
        {
            stats.GetStat(StatId.Parse("long-blade")).BaseValue = 10;
            stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;
        });
        Assert.True(original.RaiseSkills(361));
        progression.TallySkillUse("long-blade", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);
        DaggerfallSkillProgressionSave saved = original.Capture();

        (DaggerfallSkillUseReactions restored, ProgressionState restoredProgression, StatsComponent restoredStats) = Create(stats =>
        {
            stats.GetStat(StatId.Parse("long-blade")).BaseValue = 10;
            stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;
        });
        restored.Restore(saved);

        Assert.Equal(361, restored.LastSkillIncreaseCheckSecond);
        Assert.False(restored.RaiseSkills(721));
        Assert.Equal(DaggerfallSkillUseReactions.MaximumSkillUses, restoredProgression.SkillUses["long-blade"]);
        Assert.True(restored.RaiseSkills(722));
        Assert.Equal(11d, restoredStats.GetStat(StatId.Parse("long-blade")).BaseValue);
        Assert.Equal(0, restoredProgression.SkillUses["long-blade"]);
    }

    [Fact]
    public void Rest_skill_check_requires_the_exact_threshold_after_the_donor_reflexes_scale()
    {
        (DaggerfallSkillUseReactions uses, ProgressionState progression, StatsComponent stats) = Create(stats =>
        {
            stats.GetStat(StatId.Parse("mysticism")).BaseValue = 10;
            stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;
        });
        int threshold = DaggerfallFormulaPolicy.CalculateSkillUsesForAdvancement(10, 1, 1.0390625f, 1);
        progression.TallySkillUse("mysticism", threshold - 1, DaggerfallSkillUseReactions.MaximumSkillUses);

        Assert.True(uses.RaiseSkills(361));
        Assert.Equal(10d, stats.GetStat(StatId.Parse("mysticism")).BaseValue);
        progression.TallySkillUse("mysticism", 1, DaggerfallSkillUseReactions.MaximumSkillUses);
        stats.GetStat(StatId.Parse("reflexes")).BaseValue = 3;

        Assert.True(uses.RaiseSkills(722));
        Assert.Equal(10d, stats.GetStat(StatId.Parse("mysticism")).BaseValue);
        Assert.Equal(threshold, progression.SkillUses["mysticism"]);
        stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;

        Assert.True(uses.RaiseSkills(1083));
        Assert.Equal(11d, stats.GetStat(StatId.Parse("mysticism")).BaseValue);
        Assert.Equal(0, progression.SkillUses["mysticism"]);
    }

    [Fact]
    public void Resolved_enemy_miss_tallies_dodging_once_before_the_miss_returns()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        using ActorsState actors = new();
        List<DaggerfallSkillUse> uses = [];
        DaggerCombatRules combat = new(
            null!, actors, null!, _ => null, new DaggerfallItemInstances(), definitions,
            new Dictionary<long, DaggerfallActorDefinition>(), null!, uses.Add);
        FactBuffer<IProductFact> facts = new();

        combat.Apply(
            new AttackRequest(99, DaggerfallActorIdentity.PlayerEntityId, 1, 2, 1d, Delayed: true),
            new PreparedAttack(1d, new AttackOutcome(Hit: false, Allowed: true, Body: 0, Damage: 0, Roll: 99, Chance: 1)),
            facts);

        Assert.Equal([new DaggerfallSkillUse("dodging", DaggerfallSkillUseReason.DodgingEnemyAttack, DaggerfallSkillUseOutcome.Attempted)], uses);
    }

    [Fact]
    public void Kill_rewards_are_inert_without_the_explicit_experimental_configuration()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        ProgressionState progression = new();
        DaggerfallRewardReactions rewards = new(
            progression,
            new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00"))),
            new EntityId(DaggerfallActorIdentity.PlayerEntityId),
            () => definitions.Catalogs.RequireCareer("class00"),
            RandomMinimums(),
            new Dictionary<long, DaggerfallActorDefinition> { [9000] = definitions.RequireActor(new DaggerfallActorId("thief")) });

        rewards.React(new(9000, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 1, 1d, 1, 1), new());

        Assert.Equal(0, progression.Experience);
        Assert.Equal(1, progression.Level);
    }

    private static (DaggerfallSkillUseReactions Uses, ProgressionState Progression, StatsComponent Stats) Create(Action<StatsComponent>? configure = null)
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        ProgressionState progression = new();
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        configure?.Invoke(stats);
        return (new DaggerfallSkillUseReactions(progression, stats, definitions, player), progression, stats);
    }

    private static IRandomService RandomMinimums() => DispatchProxy.Create<IRandomService, RandomMinimumProxy>();

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static DaggerfallDefinitions LoadDefinitions() => TestPayload.Definitions;

}
