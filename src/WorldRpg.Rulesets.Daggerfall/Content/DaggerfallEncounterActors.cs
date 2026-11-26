using System.Collections.ObjectModel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Encounter-level scaling and action policy over resolved actor definitions. All 19 classic classes now live
/// explicitly in <c>daggerfall.base/actors/templates/classes.json</c> and <c>actors/specs/enemies.json</c>;
/// runtime class synthesis (<c>AddMissing</c>) is retired.
/// </summary>
internal static class DaggerfallEncounterActors
{
    /// <summary>The donor numbers each career's class mobile from here, in career record order.</summary>
    internal const int FirstClassMobile = 128;

    /// <summary>The action every materialized class encounter fights through with its generated equipment.</summary>
    internal const string ClassEquippedMeleeAction = "enemy-class-equipped-melee";

    internal static DaggerfallActorId ActorFor(DaggerfallMobileDefinition mobile) => mobile.SpawnerId is { } spawner
        ? new DaggerfallActorId(spawner)
        : mobile.Actor is { } actor
            ? new DaggerfallActorId(actor) : new DaggerfallActorId($"encounter-{mobile.Identity}");

    /// <summary>
    /// Applies the definition semantics that are specific to a materialized classic class
    /// encounter. This remains separate from level scaling so a restored dynamic actor can
    /// recover the same action and reward policy without inventing a new actor definition.
    /// </summary>
    internal static DaggerfallActorDefinition ApplyEncounterClassPolicy(DaggerfallActorDefinition actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Capabilities.Scaling != ActorScalingMode.CareerLeveled && actor.Kind != DaggerfallActorKinds.EnemyClass) return actor;
        return actor with
        {
            ActionId = actor.ActionId == "thief-strike" ? ClassEquippedMeleeAction : actor.ActionId,
            Rewards = new DaggerfallRewardPolicy(0),
        };
    }

    /// <summary>EnemyEntity sets every donor skill to level*5+30 after it copies career attributes.</summary>
    internal static DaggerfallActorDefinition AtLevel(DaggerfallActorDefinition actor, DaggerfallVocabulary vocabulary, int level)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(vocabulary);
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        if (actor.Capabilities.Scaling == ActorScalingMode.Fixed
            && actor.Kind is not (DaggerfallActorKinds.EnemyClass or DaggerfallActorKinds.Monster))
            return actor with { Level = level };
        int skill = Math.Min(100, checked(level * 5 + 30));
        Dictionary<DaggerfallStatId, int> values = actor.Stats.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (DaggerfallStatId id in vocabulary.Skills) values[id] = skill;
        return ApplyEncounterClassPolicy(actor) with
        {
            Level = level,
            Stats = new DaggerfallStatBases(new ReadOnlyDictionary<DaggerfallStatId, int>(values)),
        };
    }
}
