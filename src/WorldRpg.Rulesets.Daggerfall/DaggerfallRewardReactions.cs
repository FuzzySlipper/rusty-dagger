using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Daggerfall-owned reward policy for defeated authored actors.</summary>
internal sealed class DaggerfallRewardReactions(ProgressionState progression, ActorMechanicsState playerMechanics, DaggerfallActorDefinition playerDefinition, IRandomService random, IReadOnlyDictionary<long, DaggerfallActorDefinition> actors)
{
    private readonly HashSet<long> _awarded = [];
    private readonly HashSet<long> _experienceAwarded = [];

    internal void React(ActorDiedFact fact, FactBuffer<IProductFact> facts)
    {
        if (fact.KillerId != DaggerfallActorIdentity.PlayerEntityId) return;
        if (_awarded.Contains(fact.ActorId) || !actors.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;

        ProgressionAwardPlan? progressionPlan = PlanProgression(fact.ActorId, actor);
        if (progressionPlan is not null)
        {
            progressionPlan.HealthCandidate?.Publish();
            progression.AdvanceTo(progressionPlan.NextExperience, progressionPlan.NextLevel);
            facts.Append(new ExperienceAwardedFact(fact.ActorId, actor.Rewards.ExperienceReward));
            _experienceAwarded.Add(fact.ActorId);
        }
        _awarded.Add(fact.ActorId);
    }

    private ProgressionAwardPlan? PlanProgression(long defeatedActorId, DaggerfallActorDefinition defeated)
    {
        if (defeated.Rewards.ExperienceReward <= 0 || _experienceAwarded.Contains(defeatedActorId)) return null;

        int nextExperience = checked(progression.Experience + defeated.Rewards.ExperienceReward);
        int curveLevel = checked(1 + DaggerfallFormulaPolicy.ExperimentalXpLevel(nextExperience, DaggerfallFormulaPolicy.Experimental));
        int nextLevel = Math.Max(progression.Level, curveLevel);
        if (nextLevel == progression.Level)
            return new ProgressionAwardPlan(nextExperience, nextLevel, null);

        int hitPointsPerLevel = playerDefinition.HitPointsPerLevel
            ?? throw new InvalidOperationException("The Daggerfall player definition must provide hitPointsPerLevel.");
        int endurance = checked((int)playerMechanics.ReadStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).Value.Raw);
        (int minimum, int maximum) = DaggerfallFormulaPolicy.HitPointsPerLevelRollBounds(hitPointsPerLevel, DaggerfallFormulaPolicy.Experimental);
        List<ExactSource> expectedSources = [];
        for (int level = checked(progression.Level + 1); ; level++)
        {
            long rollRaw = random.DrawKeyed(new KeyedRngRequest(
                CombatRandomKey.Seed,
                CombatRandomKey.PlayerScope,
                $"player.level-up.{level}.hp-roll",
                minimum,
                maximum)).Value;
            int roll = checked((int)rollRaw);
            if (roll < minimum || roll > maximum)
                throw new MechanicsException($"Daggerfall level-up roll for level {level} was outside [{minimum}, {maximum}].");
            int gain = DaggerfallFormulaPolicy.HitPointsPerLevelUp(roll, endurance, DaggerfallFormulaPolicy.Experimental);
            expectedSources.Add(DaggerfallLevelUpHealthSource.Create(playerMechanics.Entity, level, gain));
            if (level == nextLevel) break;
        }

        ExactStatTrackState health = playerMechanics.ReadStatTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        List<ExactSource> prospectiveSources = health.Sources.ToList();
        bool changed = false;
        foreach (ExactSource expected in expectedSources)
        {
            ExactSource? existing = prospectiveSources.SingleOrDefault(source => source.Identity == expected.Identity);
            if (existing is null)
            {
                prospectiveSources.Add(expected);
                changed = true;
                continue;
            }

            if (!DaggerfallLevelUpHealthSource.Matches(existing, expected))
                throw new MechanicsException($"Daggerfall level-up source {expected.Identity} already exists with different policy.");
        }

        ExactStatTrackChangeCandidate? candidate = null;
        if (changed)
        {
            ExactStatTrackSnapshot before = health.Read();
            long expectedGain = expectedSources
                .Where(source => !health.Sources.Any(existing => existing.Identity == source.Identity))
                .Select(source => ((ExactStatContribution.Add)source.Contributions[0].Contribution).Amount.Raw)
                .Aggregate(0L, (total, gain) => checked(total + gain));
            ExactValue expectedMaximum = before.Stat.Value.CheckedAdd(new ExactValue(expectedGain));
            ExactValue expectedCurrent = before.TrackCurrent.CheckedAdd(new ExactValue(expectedGain));
            candidate = health.PrepareSourceChange(
                health.Base,
                prospectiveSources,
                ExactStatTrackCurrentPolicy.PreserveDistanceFromMaximum,
                health.Revision);
            if (candidate.Preview.After.Stat.Value != expectedMaximum
                || candidate.Preview.After.TrackCurrent != expectedCurrent)
            {
                throw new MechanicsException("Daggerfall level-up health gain was constrained before it could raise maximum and current equally.");
            }
        }
        return new ProgressionAwardPlan(nextExperience, nextLevel, candidate);
    }

    private sealed record ProgressionAwardPlan(
        int NextExperience,
        int NextLevel,
        ExactStatTrackChangeCandidate? HealthCandidate);
}

/// <summary>Daggerfall's durable, per-level health-max source policy.</summary>
internal static class DaggerfallLevelUpHealthSource
{
    private const string Definition = "daggerfall.player.level-up.health";
    private const string Group = "daggerfall.player.level-up.health";

    internal static ExactSource Create(Rusty.Engine.Entities.EntityId player, int level, int gain)
    {
        if (level < 2) throw new ArgumentOutOfRangeException(nameof(level));
        if (gain < 1) throw new ArgumentOutOfRangeException(nameof(gain));
        return new ExactSource(
            new IntrinsicSourceIdentity(player, SourceInstanceId.Parse($"daggerfall.player.level-up.{level}.health")),
            SourceDefinitionId.Parse(Definition),
            priority: 0,
            [new ExactStatContributionDefinition(
                StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value),
                StackingGroupId.Parse(Group),
                MechanicsStackingPolicy.Sum,
                new ExactStatContribution.Add(new ExactValue(gain)))]);
    }

    internal static bool Matches(ExactSource actual, ExactSource expected) =>
        actual.Identity == expected.Identity
        && actual.Definition == expected.Definition
        && actual.Priority == expected.Priority
        && actual.Contributions.Count == 1
        && expected.Contributions.Count == 1
        && actual.Contributions[0] == expected.Contributions[0];
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0;
    internal const string Scope = "dagger.combat.ai.v1";
    internal static string For(ulong generation, ulong update, long actor, string roll) => $"generation:{generation}:step:{update}:loot:{actor}:{roll}";
}

/// <summary>Session-owned monotonic allocator for generated unique loot entities.</summary>
internal sealed class DaggerfallUniqueItemAllocator(ulong firstEntityId, IEnumerable<ulong>? reserved = null)
{
    internal const ulong DefaultFirstEntityId = 1_000_000_000_000UL;
    private ulong _next = firstEntityId;
    private readonly HashSet<ulong> _reserved = (reserved ?? []).ToHashSet();

    internal ulong Allocate()
    {
        while (_reserved.Contains(_next))
        {
            if (_next == ulong.MaxValue) throw new InvalidOperationException("The Daggerfall loot entity range is exhausted.");
            _next++;
        }
        if (_next == 0 || _next == ulong.MaxValue) throw new InvalidOperationException("The Daggerfall loot entity range is exhausted.");
        ulong allocated = _next++;
        _reserved.Add(allocated);
        return allocated;
    }
}
