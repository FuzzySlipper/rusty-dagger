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
    public void Resolved_enemy_miss_tallies_dodging_once_before_the_miss_returns()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        using ActorsState actors = new();
        List<DaggerfallSkillUse> uses = [];
        DaggerCombatRules combat = new(
            null!, actors, null!, _ => null, definitions, new Dictionary<long, DaggerfallActorDefinition>(), null!, uses.Add);
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
            new DaggerfallMechanicsState().CreateStats(player, player.PlayerInitialVitals),
            new EntityId(DaggerfallActorIdentity.PlayerEntityId),
            player,
            RandomMinimums(),
            new Dictionary<long, DaggerfallActorDefinition> { [9000] = definitions.RequireActor(new DaggerfallActorId("thief")) });

        rewards.React(new(9000, DaggerfallActorIdentity.PlayerEntityId, 1, 1, 1), new());

        Assert.Equal(0, progression.Experience);
        Assert.Equal(1, progression.Level);
    }

    private static (DaggerfallSkillUseReactions Uses, ProgressionState Progression, StatsComponent Stats) Create()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        ProgressionState progression = new();
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, player.PlayerInitialVitals);
        return (new DaggerfallSkillUseReactions(progression, stats, definitions, player), progression, stats);
    }

    private static IRandomService RandomMinimums() => DispatchProxy.Create<IRandomService, RandomMinimumProxy>();

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static DaggerfallDefinitions LoadDefinitions() => DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
