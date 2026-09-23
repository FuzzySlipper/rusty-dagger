using System.Collections.ObjectModel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Resolves every classic human mobile through its published mobile and career records.  The
/// record is deliberately a normal actor definition: encounter selection, combat, corpse loot,
/// and presentation all use the same identity rather than treating a table result as a special
/// case.
/// </summary>
internal static class DaggerfallEncounterActors
{
    internal const int FirstClassMobile = 128;
    internal const int LastClassMobile = 146;

    internal static void AddMissing(
        IDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors,
        DaggerfallCatalogSet catalogs,
        DaggerfallMobileCatalogSet mobiles,
        DaggerfallVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(mobiles);
        ArgumentNullException.ThrowIfNull(vocabulary);
        if (catalogs.Careers.Count != 19)
            throw new InvalidOperationException("Classic class-mobile construction requires the nineteen normalized career records.");

        for (int mobileId = FirstClassMobile; mobileId <= LastClassMobile; mobileId++)
        {
            DaggerfallMobileDefinition mobile = mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? value)
                ? value : throw new InvalidOperationException($"Classic class mobile {mobileId} is absent from the normalized mobile catalog.");
            if (mobile.Actor is not null) continue;
            DaggerfallCareerDefinition career = catalogs.Careers[mobileId - FirstClassMobile];
            DaggerfallActorId id = ActorFor(mobile);
            if (!actors.TryAdd(id, Create(id, mobile, career, vocabulary)))
                throw new InvalidOperationException($"Classic class encounter actor '{id.Value}' is already defined.");
        }
    }

    internal static DaggerfallActorId ActorFor(DaggerfallMobileDefinition mobile) => mobile.Actor is { } actor
        ? new DaggerfallActorId(actor) : new DaggerfallActorId($"encounter-{mobile.Identity}");

    /// <summary>
    /// Applies the definition semantics that are specific to a materialized classic class
    /// encounter.  This remains separate from level scaling so a restored dynamic actor can
    /// recover the same action and reward policy without inventing a new actor definition.
    /// </summary>
    internal static DaggerfallActorDefinition ApplyEncounterClassPolicy(DaggerfallActorDefinition actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Kind != DaggerfallActorKinds.EnemyClass) return actor;
        return actor with
        {
            ActionId = actor.ActionId == "thief-strike" ? "enemy-class-equipped-melee" : actor.ActionId,
            Rewards = new DaggerfallRewardPolicy(0),
        };
    }

    /// <summary>EnemyEntity sets every donor skill to level*5+30 after it copies career attributes.</summary>
    internal static DaggerfallActorDefinition AtLevel(DaggerfallActorDefinition actor, DaggerfallVocabulary vocabulary, int level)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(vocabulary);
        if (actor.Kind != DaggerfallActorKinds.EnemyClass) return actor;
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        int skill = Math.Min(100, checked(level * 5 + 30));
        Dictionary<DaggerfallStatId, int> values = actor.Stats.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (DaggerfallStatId id in vocabulary.Skills) values[id] = skill;
        // The two authored placements retain their fixed site action/reward contracts.  A mobile
        // selected by FORM07 is an EnemyEntity class and therefore fights through its generated
        // equipment and does not receive the old projected flat-XP reward.
        return ApplyEncounterClassPolicy(actor) with
        {
            Stats = new DaggerfallStatBases(new ReadOnlyDictionary<DaggerfallStatId, int>(values)),
        };
    }

    private static DaggerfallActorDefinition Create(DaggerfallActorId id, DaggerfallMobileDefinition mobile, DaggerfallCareerDefinition career, DaggerfallVocabulary vocabulary)
    {
        Dictionary<DaggerfallStatId, int> stats = [];
        foreach ((string attribute, int value) in career.Attributes.Zip(career.AttributeValues)) stats.Add(new(attribute), value);
        // EnemyEntity gives every donor skill level-one's 35, rather than only its career skills.
        // Spawn-time AtLevel replaces this baseline for the actual player-relative level.
        foreach (DaggerfallStatId skill in vocabulary.Skills) stats[skill] = 35;
        return new(id, DaggerfallActorKinds.EnemyClass, new DaggerfallStatBases(new ReadOnlyDictionary<DaggerfallStatId, int>(stats)),
            new DaggerfallVitalRange(11, checked(10 + career.HitPointsPerLevel)), new DaggerfallCombatProfile(DaggerfallMechanicsIds.Health, null),
            // The historical projected value was not a donor field. Daggerfall Unity awards career
            // progression through its skill-use path, so no synthetic flat XP is attached here.
            new DaggerfallRewardPolicy(0), 0, mobile.DonorId, career.HitPointsPerLevel, [], Team(mobile.Team), null,
            mobile.LootTableKey ?? throw new InvalidOperationException($"Classic class mobile {mobile.DonorId} has no published EnemyBasics loot table key."),
            null, null, "enemy-class-equipped-melee", [], DaggerfallActorPresentationDefinition.None, GroundOnSpawn: true, Career: career.Id);
    }

    private static string Team(string source) => source switch
    {
        "KnightsAndMages" => "knights-and-mages", "Criminals" => "criminals", "CityWatch" => "city-watch",
        _ => throw new InvalidOperationException($"Classic class mobile team '{source}' has no normalized actor team."),
    };

}
