using System.Collections.ObjectModel;
using WorldRpg.Kit;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Well-known structural archetype IDs resolved from <c>daggerfall.people/archetypes.json</c>.</summary>
internal static class DaggerfallArchetypeIds
{
    internal const string Civilian = "civilian";
    internal const string StaticNpc = "static-npc";
    internal const string ClutterPerson = "clutter-person";
    internal const string Questor = "questor";
    internal const string Guard = "guard";
    internal const string Combatant = "combatant";

    internal static readonly IReadOnlyList<string> Required =
    [
        Civilian,
        StaticNpc,
        ClutterPerson,
        Questor,
        Guard,
        Combatant,
    ];
}

/// <summary>How combat, spells, and law enforcement may harm an actor, independent of gameplay kind.</summary>
internal enum ActorHarmPolicyKind
{
    /// <summary>Static shopkeepers, questors, and unique cast who cannot be targeted or damaged directly.</summary>
    Protected,

    /// <summary>Townsfolk and bystanders who can be struck (triggering assault/murder law) without active combat deflection.</summary>
    LawProtectedCivilian,

    /// <summary>Active fighters (monsters, enemy classes, promoted city watch) with full combat participation and deflection.</summary>
    Combatant,
}

/// <summary>Attached harm capability on every live actor in <c>EntityStore</c>.</summary>
internal sealed class ActorHarmPolicy(ActorHarmPolicyKind policy)
{
    internal ActorHarmPolicyKind Policy { get; private set; } = policy;

    internal bool IsProtected => Policy == ActorHarmPolicyKind.Protected;
    internal bool IsLawProtectedCivilian => Policy == ActorHarmPolicyKind.LawProtectedCivilian;
    internal bool IsCombatant => Policy == ActorHarmPolicyKind.Combatant;
    internal bool CanBeTargetedInCombat => Policy != ActorHarmPolicyKind.Protected;
    internal bool CanBeAffectedBySpells => Policy != ActorHarmPolicyKind.Protected;
    internal bool CanDeflectAttacks => Policy == ActorHarmPolicyKind.Combatant;

    internal void Set(ActorHarmPolicyKind policy) => Policy = policy;
}

/// <summary>How the player may interact with an actor.</summary>
internal enum ActorInteractionPolicyKind
{
    None,
    AmbientBark,
    FullDialogue,
}

/// <summary>Attached interaction capability on every live actor in <c>EntityStore</c>.</summary>
internal sealed class ActorInteractionPolicy(ActorInteractionPolicyKind policy)
{
    internal ActorInteractionPolicyKind Policy { get; private set; } = policy;

    internal bool CanTalk => Policy == ActorInteractionPolicyKind.FullDialogue;
    internal bool CanBark => Policy == ActorInteractionPolicyKind.AmbientBark;

    internal void Set(ActorInteractionPolicyKind policy) => Policy = policy;
}

/// <summary>
/// Authored baseline profile loaded from <c>daggerfall.people/archetypes.json</c> and used to materialize a real
/// <c>StatsComponent</c>, default capability policies, and initial fungible loadout on every live NPC in
/// <c>EntityStore</c>. Sparsity stays at the <c>ActorLedger</c>/save boundary.
/// </summary>
internal sealed record ArchetypeProfile(
    string Id,
    DaggerfallVitalRange Health,
    int Fatigue,
    int Magicka,
    int BaseAttribute,
    IReadOnlyDictionary<DaggerfallStatId, int> Attributes,
    IReadOnlyDictionary<DaggerfallStatId, int> Skills,
    ActorHarmPolicyKind DefaultHarm,
    ActorInteractionPolicyKind DefaultInteraction,
    bool Walks = true,
    int Armor = 0,
    int Level = 1,
    IReadOnlyList<DaggerfallLoadoutEntry>? Loadout = null)
{
    private static readonly IReadOnlyDictionary<DaggerfallStatId, int> EmptyStats =
        new ReadOnlyDictionary<DaggerfallStatId, int>(new Dictionary<DaggerfallStatId, int>());

    internal IReadOnlyList<DaggerfallLoadoutEntry> EffectiveLoadout => Loadout ?? [];

    /// <summary>
    /// Deterministically picks an initial maximum health within <see cref="Health"/> from the actor's durable ID
    /// without touching runtime RNG, so untouched seeded townspeople re-materialize identical baseline stats across
    /// save/reload and keep 0-byte sparse <c>ActorLedger</c> diffs.
    /// </summary>
    internal int RollDeterministicHealth(long durableId)
    {
        if (Health.Maximum <= Health.Minimum)
            return Health.Minimum;
        ulong span = (ulong)(Health.Maximum - Health.Minimum) + 1UL;
        ulong hash = StableHash.NonZeroFnv1a64($"daggerfall.archetype-hp:{Id}:{durableId}");
        return checked(Health.Minimum + (int)(hash % span));
    }

    /// <summary>Builds the canonical <see cref="DaggerfallActorDefinition"/> backing mechanics, combat, and loadout checks.</summary>
    internal DaggerfallActorDefinition ToActorDefinition(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Dictionary<DaggerfallStatId, int> stats = new();
        foreach (DaggerfallStatId attr in DaggerfallMechanicsIds.Attributes)
            stats[attr] = BaseAttribute;
        foreach ((DaggerfallStatId attr, int value) in Attributes ?? EmptyStats)
            stats[attr] = value;
        foreach ((DaggerfallStatId skill, int value) in Skills ?? EmptyStats)
            stats[skill] = value;

        return new DaggerfallActorDefinition(
            new DaggerfallActorId(kind),
            kind,
            new DaggerfallStatBases(stats),
            Health,
            new DaggerfallCombatProfile(DaggerfallMechanicsIds.Health, null),
            new DaggerfallRewardPolicy(0),
            Armor: Armor,
            MobileId: null,
            HitPointsPerLevel: null,
            Attacks: [],
            Team: null,
            MinimumMaterial: null,
            LootTableKey: null,
            Level: Level,
            Weight: 0,
            ActionId: null,
            Loadout: EffectiveLoadout,
            Presentation: DaggerfallActorPresentationDefinition.None,
            Race: null,
            Career: null);
    }
}

/// <summary>Attached social/quest overlay state on a live actor (HUD escort portrait, mute, archetype id, witness memory, referrals).</summary>
internal sealed class ActorOverlayComponent
{
    private readonly List<string> _witnessedCrimes = [];
    private readonly List<string> _referralTags = [];

    internal string? ArchetypeId { get; set; }
    internal string? FamilyId { get; set; }
    internal bool Met { get; set; }
    internal bool IsMuted { get; set; }
    internal bool IsQuestor { get; set; }
    internal string? EscortPortraitMedia { get; set; }
    internal int EscortOrder { get; set; }
    internal string? StagedPartyRole { get; set; }
    internal IReadOnlyList<string> WitnessedCrimes => _witnessedCrimes;
    internal IReadOnlyList<string> ReferralTags => _referralTags;

    internal void RecordWitnessedCrime(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        if (!_witnessedCrimes.Contains(operationId, StringComparer.Ordinal))
            _witnessedCrimes.Add(operationId);
    }

    internal void RecordReferralTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (!_referralTags.Contains(tag, StringComparer.Ordinal))
            _referralTags.Add(tag);
    }

    internal bool HasReferralTag(string tag) =>
        !string.IsNullOrWhiteSpace(tag) && _referralTags.Contains(tag, StringComparer.Ordinal);
}
