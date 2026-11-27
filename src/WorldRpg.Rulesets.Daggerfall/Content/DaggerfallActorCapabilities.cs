using System.Collections.ObjectModel;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
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

/// <summary>What kind of social introduction or vouch one person issued on the player's behalf.</summary>
internal enum ActorVouchKind
{
    /// <summary>Casual or neighborly introduction pointing the player to a peer contact.</summary>
    Contact = 0,

    /// <summary>Trade or service vouch from a trusted peer that unlocks better prices or waives deposits.</summary>
    Trade = 1,

    /// <summary>Guild or knightly-order sponsorship vouching for the player to an allied faction or court contact.</summary>
    Guild = 2,

    /// <summary>Formal court or noble introduction granting audience and work access with a prominent figure.</summary>
    Court = 3,
}

/// <summary>Explicit persisted reason why a structured <see cref="ActorVouchRecord"/> has been voided.</summary>
internal enum ActorVouchVoidReason
{
    /// <summary>Not explicitly voided (still subject to age and live voucher state).</summary>
    None = 0,

    /// <summary>Voided because the voucher died.</summary>
    Dead = 1,

    /// <summary>Voided because the player wronged (murdered, assaulted, or robbed) the voucher.</summary>
    Wronged = 2,

    /// <summary>Voided because the voucher turned hostile toward the player.</summary>
    Hostile = 3,
}

/// <summary>
/// Structured social vouch issued by a specific person (<see cref="ReferrerDurableId"/>) on the player's behalf.
/// Replaces bare <c>referred-from:{site}</c> strings with durable, trust-evaluated provenance.
/// </summary>
internal sealed record ActorVouchRecord(
    long ReferrerDurableId,
    int ReferrerFactionId,
    ActorVouchKind Kind,
    int IssuedDay,
    string? ReferrerName = null,
    string? OriginSiteName = null,
    string? ReferrerFamilyId = null,
    ActorVouchVoidReason VoidReason = ActorVouchVoidReason.None)
{
    internal void Validate()
    {
        if (ReferrerDurableId <= 0 || ReferrerDurableId == 1L)
            throw new ArgumentOutOfRangeException(nameof(ReferrerDurableId), "Saved actor vouch referrer must be a positive non-player actor id.");
        if (ReferrerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ReferrerFactionId), "Saved actor vouch referrer faction id cannot be negative.");
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Saved actor vouch names an invalid kind.");
        if (!Enum.IsDefined(VoidReason))
            throw new ArgumentOutOfRangeException(nameof(VoidReason), "Saved actor vouch names an invalid void reason.");
        if (ReferrerName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(ReferrerName);
        if (OriginSiteName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(OriginSiteName);
        if (ReferrerFamilyId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ReferrerFamilyId);
            if (!DaggerfallHouseholds.IsValidFamilyId(ReferrerFamilyId))
                throw new ArgumentException($"Saved actor vouch names an invalid referrer family id '{ReferrerFamilyId}'.", nameof(ReferrerFamilyId));
        }
    }

    internal bool SameGrant(ActorVouchRecord other) =>
        other is not null && ReferrerDurableId == other.ReferrerDurableId && Kind == other.Kind;

    /// <summary>Chooses the fresher or more authoritative record when merging two grants with the same key.</summary>
    internal static ActorVouchRecord Prefer(ActorVouchRecord current, ActorVouchRecord incoming)
    {
        if (incoming.IssuedDay > current.IssuedDay)
            return incoming;
        if (incoming.IssuedDay < current.IssuedDay)
            return current;
        if (incoming.VoidReason != ActorVouchVoidReason.None && current.VoidReason == ActorVouchVoidReason.None)
            return incoming;
        if (incoming.VoidReason == ActorVouchVoidReason.Wronged && current.VoidReason != ActorVouchVoidReason.Wronged)
            return incoming;
        if (incoming.VoidReason == ActorVouchVoidReason.Dead && current.VoidReason == ActorVouchVoidReason.Hostile)
            return incoming;
        return current;
    }

    /// <summary>Human-readable summary for dialogue greetings and ambient barks.</summary>
    internal string DisplaySummary() =>
        !string.IsNullOrWhiteSpace(ReferrerName) && !string.IsNullOrWhiteSpace(OriginSiteName)
            ? $"{ReferrerName} of {OriginSiteName}"
            : !string.IsNullOrWhiteSpace(ReferrerName)
                ? ReferrerName
                : !string.IsNullOrWhiteSpace(OriginSiteName)
                    ? OriginSiteName
                    : $"contact #{ReferrerDurableId}";
}

/// <summary>Attached social/quest overlay state on a live actor (HUD escort portrait, mute, archetype id, witness memory, vouches).</summary>
internal sealed class ActorOverlayComponent
{
    private readonly List<string> _witnessedCrimes = [];
    private readonly List<string> _referralTags = [];
    private readonly List<ActorVouchRecord> _vouches = [];

    internal string? ArchetypeId { get; set; }
    internal string? FamilyId { get; set; }
    internal bool Met { get; set; }
    internal int? LastFavourDay { get; set; }
    internal bool IsMuted { get; set; }
    internal bool IsQuestor { get; set; }
    internal string? EscortPortraitMedia { get; set; }
    internal int EscortOrder { get; set; }
    internal string? StagedPartyRole { get; set; }
    /// <summary>Transient runtime state: resident is currently sleeping in a home bed during night hours (not persisted to ledger).</summary>
    internal bool IsSleeping { get; set; }
    /// <summary>Transient runtime state: resident was awakened by an intruder inside their residence during night hours.</summary>
    internal bool AwakenedByIntruder { get; set; }
    internal WorldPoint? PreSleepPosition { get; set; }
    internal float? PreSleepHeading { get; set; }
    internal IReadOnlyList<string> WitnessedCrimes => _witnessedCrimes;
    internal IReadOnlyList<string> ReferralTags => _referralTags;
    internal IReadOnlyList<ActorVouchRecord> Vouches => _vouches;

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

    internal bool RecordVouch(ActorVouchRecord vouch)
    {
        ArgumentNullException.ThrowIfNull(vouch);
        vouch.Validate();
        for (int i = 0; i < _vouches.Count; i++)
        {
            if (_vouches[i].SameGrant(vouch))
            {
                if (_vouches[i] == vouch)
                    return false;
                _vouches[i] = ActorVouchRecord.Prefer(_vouches[i], vouch);
                return _vouches[i] == vouch;
            }
        }
        _vouches.Add(vouch);
        return true;
    }

    internal int VoidVouches(Predicate<ActorVouchRecord> match, ActorVouchVoidReason reason)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (reason == ActorVouchVoidReason.None)
            return 0;
        int voided = 0;
        for (int i = 0; i < _vouches.Count; i++)
        {
            ActorVouchRecord existing = _vouches[i];
            if (!match(existing))
                continue;
            ActorVouchRecord updated = ActorVouchRecord.Prefer(existing, existing with { VoidReason = reason });
            if (updated != existing)
            {
                _vouches[i] = updated;
                voided++;
            }
        }
        return voided;
    }

    internal bool HasVouchFrom(long referrerDurableId) =>
        referrerDurableId > 0 && _vouches.Any(vouch => vouch.ReferrerDurableId == referrerDurableId);

    internal bool HasVouch(long referrerDurableId, ActorVouchKind kind) =>
        referrerDurableId > 0 && _vouches.Any(vouch => vouch.ReferrerDurableId == referrerDurableId && vouch.Kind == kind);

    internal int RemoveVouches(Predicate<ActorVouchRecord> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return _vouches.RemoveAll(match);
    }
}
