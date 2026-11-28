using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Closed 3-value gender enum resolved on every concrete live actor instance.</summary>
internal enum ActorGender
{
    Male,
    Female,
    Neutral,
}

internal static class ActorGenderFormat
{
    internal static string Slug(ActorGender gender) => gender switch
    {
        ActorGender.Male => "male",
        ActorGender.Female => "female",
        ActorGender.Neutral => "neutral",
        _ => throw new ArgumentOutOfRangeException(nameof(gender)),
    };

    internal static string SpriteKey(ActorGender gender) => gender switch
    {
        ActorGender.Male => "male",
        ActorGender.Female => "female",
        ActorGender.Neutral => "unspecified",
        _ => throw new ArgumentOutOfRangeException(nameof(gender)),
    };

    internal static bool TryParse(string? value, out ActorGender gender)
    {
        if (string.Equals(value, "male", StringComparison.OrdinalIgnoreCase)) { gender = ActorGender.Male; return true; }
        if (string.Equals(value, "female", StringComparison.OrdinalIgnoreCase)) { gender = ActorGender.Female; return true; }
        if (string.Equals(value, "neutral", StringComparison.OrdinalIgnoreCase)) { gender = ActorGender.Neutral; return true; }
        gender = default;
        return false;
    }
}

/// <summary>How an actor's stats and vitals scale with level.</summary>
internal enum ActorScalingMode
{
    Fixed,
    LevelScaledSkills,
    CareerLeveled,
}

/// <summary>How an actor acquires starting combat equipment on spawn.</summary>
internal enum ActorGearPolicy
{
    FixedLoadout,
    GeneratedClassGear,
    FixedVariantGear,
}

/// <summary>How an actor's base body weight is computed for encumbrance and strike impact.</summary>
internal enum ActorBodyWeightMode
{
    Fixed,
    GenderedHumanoid,
}

/// <summary>Locomotion medium for navigation and grounding.</summary>
internal enum ActorMovementMedium
{
    Ground,
    Flying,
    Aquatic,
    Spectral,
}

/// <summary>How a spawner names materialized instances.</summary>
internal enum ActorNamingMode
{
    Label,
    Generated,
    Designated,
}

/// <summary>Typed family of an actor spawner spec.</summary>
internal enum ActorSpawnerFamily
{
    Player,
    Creature,
    Enemy,
    Town,
    PartyHire,
    Cast,
}

/// <summary>Template-owned enemy spellcasting policy.</summary>
internal enum ActorSpellPolicy
{
    None,
    Fixed,
    ClassTiers,
}

/// <summary>How a town spawner spec forms deterministic households or Great House affiliations.</summary>
internal enum ActorSocialGrouping
{
    None,
    BuildingHousehold,
    FactionChapter,
    TavernStaff,
    TavernPatron,
    NobleHouse,
}

internal static class ActorSocialGroupingFormat
{
    internal static string Slug(ActorSocialGrouping grouping) => grouping switch
    {
        ActorSocialGrouping.None => "none",
        ActorSocialGrouping.BuildingHousehold => "building-household",
        ActorSocialGrouping.FactionChapter => "faction-chapter",
        ActorSocialGrouping.TavernStaff => "tavern-staff",
        ActorSocialGrouping.TavernPatron => "tavern-patron",
        ActorSocialGrouping.NobleHouse => "noble-house",
        _ => throw new ArgumentOutOfRangeException(nameof(grouping)),
    };

    internal static bool TryParse(string? value, out ActorSocialGrouping grouping)
    {
        switch (value)
        {
            case "none": grouping = ActorSocialGrouping.None; return true;
            case "building-household": grouping = ActorSocialGrouping.BuildingHousehold; return true;
            case "faction-chapter": grouping = ActorSocialGrouping.FactionChapter; return true;
            case "tavern-staff": grouping = ActorSocialGrouping.TavernStaff; return true;
            case "tavern-patron": grouping = ActorSocialGrouping.TavernPatron; return true;
            case "noble-house": grouping = ActorSocialGrouping.NobleHouse; return true;
            default: grouping = default; return false;
        }
    }
}

/// <summary>
/// Closed set of mechanical capability fields derived from every ruleset call site that formerly branched on
/// <c>DaggerfallActorKinds</c> or hardcoded classic <c>MobileId</c> switches.
/// </summary>
internal sealed record DaggerfallActorCapabilities(
    bool IsPlayer = false,
    bool IsCombatEncounter = false,
    ActorScalingMode Scaling = ActorScalingMode.Fixed,
    ActorGearPolicy GearPolicy = ActorGearPolicy.FixedLoadout,
    int? FixedEquipmentVariant = null,
    ActorBodyWeightMode BodyWeightMode = ActorBodyWeightMode.Fixed,
    bool UsesNaturalArmor = false,
    bool TakesExtraBluntDamage = false,
    bool CityWatchArrest = false,
    ActorMovementMedium Movement = ActorMovementMedium.Ground,
    bool CanOpenDoors = false,
    bool SeesThroughInvisibility = false,
    bool CastsMagic = false,
    bool PrefersRanged = false,
    bool HasRangedAttack1 = false,
    bool HasRangedAttack2 = false,
    int AttackModifierFlags = 0,
    int ResistanceFlags = 0,
    int ImmunityFlags = 0,
    int LowToleranceFlags = 0,
    int CriticalWeaknessFlags = 0,
    DaggerfallEnemyGroup EnemyGroup = DaggerfallEnemyGroup.None,
    string? LanguageSkill = null,
    string Tactic = DaggerfallMobileDefinition.LegacyTactic,
    DaggerfallTacticTendencies? Tendencies = null,
    IReadOnlyList<ActorGender>? AllowedGenders = null,
    string? FixedRace = null,
    IReadOnlyList<string>? AllowedRaces = null,
    IReadOnlyList<string>? Traits = null)
{
    private static readonly IReadOnlyList<ActorGender> DefaultGenders = [ActorGender.Male, ActorGender.Female];

    internal static DaggerfallActorCapabilities Default { get; } = new();

    internal IReadOnlyList<ActorGender> EffectiveAllowedGenders => AllowedGenders ?? DefaultGenders;
    internal IReadOnlyList<string>? EffectiveAllowedRaces =>
        !string.IsNullOrWhiteSpace(FixedRace) ? [FixedRace]
        : AllowedRaces is { Count: > 0 } ? AllowedRaces
        : null;
    internal IReadOnlyList<string> EffectiveTraits => Traits ?? [];
    internal bool Walks => Movement == ActorMovementMedium.Ground;
    internal bool HasBowAttack => HasRangedAttack1 && (!CastsMagic || HasRangedAttack2);
}

/// <summary>Flat, immutable resolved actor template produced by the load-time inheritance resolver.</summary>
internal sealed record DaggerfallActorTemplateDefinition(
    string Id,
    string? Extends,
    IReadOnlyList<string> InheritanceChain,
    DaggerfallStatBases Stats,
    DaggerfallVitalRange Health,
    int Armor,
    int? HitPointsPerLevel,
    int? Level,
    int? Weight,
    DaggerfallRewardPolicy Rewards,
    IReadOnlyList<DaggerfallAttackRange> Attacks,
    string? ActionId,
    IReadOnlyList<DaggerfallLoadoutEntry> Loadout,
    string? MinimumMaterial,
    string? LootTableKey,
    string? CareerRef,
    DaggerfallActorCapabilities Capabilities,
    ActorSpellPolicy SpellPolicy = ActorSpellPolicy.None,
    IReadOnlyList<string>? Spells = null,
    int Fatigue = 50,
    int Magicka = 0,
    ActorHarmPolicyKind DefaultHarm = ActorHarmPolicyKind.Combatant,
    ActorInteractionPolicyKind DefaultInteraction = ActorInteractionPolicyKind.None,
    bool Walks = true,
    DaggerfallActorLook? Look = null)
{
    internal IReadOnlyList<string> EffectiveSpells => Spells ?? [];

    /// <summary>Projects this resolved actor template into an <see cref="ArchetypeProfile"/> for civilian/town/cast materialization.</summary>
    internal ArchetypeProfile ToArchetypeProfile()
    {
        Dictionary<DaggerfallStatId, int> attrs = [];
        Dictionary<DaggerfallStatId, int> skills = [];
        HashSet<DaggerfallStatId> attrSet = [.. DaggerfallMechanicsIds.Attributes];
        foreach ((DaggerfallStatId id, int val) in Stats.Values)
        {
            if (attrSet.Contains(id)) attrs[id] = val;
            else skills[id] = val;
        }
        return new ArchetypeProfile(
            Id,
            Health,
            Fatigue,
            Magicka,
            BaseAttribute: 50,
            new ReadOnlyDictionary<DaggerfallStatId, int>(attrs),
            new ReadOnlyDictionary<DaggerfallStatId, int>(skills),
            DefaultHarm,
            DefaultInteraction,
            Walks: Walks,
            Armor: Armor,
            Level: Math.Max(1, Level ?? 1),
            Loadout: Loadout);
    }
}

/// <summary>Weighted gender entry inside a spawner's gender distribution.</summary>
internal sealed record DaggerfallActorGenderWeight(ActorGender Gender, int Weight);

/// <summary>Transitional classic Arena2/DFU binding metadata attached to a spawner spec (never to a template).</summary>
internal sealed record DaggerfallActorClassicBinding(
    int MobileId,
    string DonorName,
    string Identity,
    string Affinity,
    string Behaviour,
    int SoulPoints,
    int MapChance,
    string DonorTeam,
    string DonorMinMetalToHit,
    int DonorArmorValue,
    DaggerfallAttackRange? DonorDamage,
    DaggerfallVitalRange? DonorHealth,
    int DonorLevel,
    int DonorWeight,
    string? DonorLootTableKey);

/// <summary>Validated hiring terms attached to an <see cref="ActorSpawnerFamily.PartyHire"/> spawner spec (#11101).</summary>
internal sealed record DaggerfallPartyHireTerms(
    string Venue,
    int? GuildFactionId,
    int BaseDeposit,
    int BaseDailyWage,
    int? MinimumGuildRank,
    bool RequiresVouch,
    bool CanDelveDungeons);

/// <summary>How a <see cref="DaggerfallActorLook"/> draws its actor.</summary>
internal enum DaggerfallActorLookKind
{
    /// <summary>Stands as its billboard sprite.</summary>
    Billboard,
    /// <summary>Walks and stands as a townsperson splat character from the art pack's <c>splatPeople</c>, else its sprite.</summary>
    Townsperson,
    /// <summary>Moves, fights and falls as a classic mobile: its splat character from the art pack's <c>splatEnemies</c>, else its directional sprite.</summary>
    Mobile,
}

/// <summary>
/// The look content gives an actor (a template's <c>look</c>, which its specs inherit unless they state their own): how it
/// draws, the classic mobile a <see cref="DaggerfallActorLookKind.Mobile"/> look draws as, and the art pack's splat
/// character for each gender, where content names one. A gender it names none for takes the one its art binds: a
/// townsperson's by its sprite's archive, a mobile's by its mobile id and gender. Without a look, the factory's fallback
/// draws a walking townsperson as its splat character and anyone else as its sprite or mobile.
/// </summary>
internal sealed record DaggerfallActorLook(DaggerfallActorLookKind Kind, int? MobileId, IReadOnlyDictionary<ActorGender, string> Splats)
{
    /// <summary>The splat character content names for an actor of <paramref name="gender"/> (a sprite key or slug), or null.</summary>
    internal string? SplatFor(string? gender) =>
        ActorGenderFormat.TryParse(gender, out ActorGender parsed) && Splats.TryGetValue(parsed, out string? named) ? named
        : string.Equals(gender, "unspecified", StringComparison.OrdinalIgnoreCase) && Splats.TryGetValue(ActorGender.Neutral, out string? neutral) ? neutral
        : null;
}

/// <summary>
/// Typed spawner instance spec describing how a template enters the world (art, gender split, race, naming,
/// sound cues, faction/team, context policies, and optional classic binding).
/// </summary>
internal sealed record DaggerfallActorSpawnerSpec(
    string Id,
    ActorSpawnerFamily Family,
    string TemplateId,
    bool Published,
    string DisplayName,
    IReadOnlyList<DaggerfallActorGenderWeight> Genders,
    string? Race,
    ActorNamingMode Naming,
    ActorHarmPolicyKind Harm,
    ActorInteractionPolicyKind Interaction,
    string? Team,
    string? Schedule,
    int? LevelMin,
    int? LevelMax,
    int MaleTexture,
    int FemaleTexture,
    int CorpseArchive,
    int CorpseRecord,
    bool HasIdle,
    bool NoShadow,
    Vector3? GlowColor,
    DaggerfallActorPresentationDefinition Presentation,
    string MoveSound,
    string BarkSound,
    string AttackSound,
    bool ParrySounds,
    DaggerfallActorClassicBinding? ClassicBinding,
    string? Role = null,
    bool DeriveRegionalFaction = false,
    string? VisualMode = null,
    IReadOnlyList<DaggerfallPeoplePoolEntry>? DefaultPool = null,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>? RegionalPools = null,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>? ClimatePools = null,
    int? FactionId = null,
    int? VisualArchive = null,
    int? VisualRecord = null,
    string? HomeBlock = null,
    string? HomeSlotId = null,
    DaggerfallSiteId? HomeSite = null,
    WorldPoint? HomePosition = null,
    string? FamilyId = null,
    ActorSocialGrouping SocialGrouping = ActorSocialGrouping.None,
    string? VisualAssetId = null,
    DaggerfallPartyHireTerms? HireTerms = null,
    DaggerfallActorLook? Look = null);

/// <summary>Primary placement posture of a named NPC schedule profile in <c>actors/schedules.json</c>.</summary>
internal enum DaggerfallSchedulePrimaryMode
{
    ExteriorStreet,
    InteriorBuilding,
    AlwaysStationed,
}

/// <summary>Named coarse schedule and weather-sheltering policy authored in <c>actors/schedules.json</c> and referenced by spawner specs.</summary>
internal sealed record DaggerfallActorScheduleProfile(
    string Id,
    string Label,
    string Description,
    DaggerfallSchedulePrimaryMode PrimaryMode,
    bool UsesBuildingOpeningHours,
    int ActiveStartMinute,
    int ActiveEndMinute,
    int JitterMinutes,
    int? EveningTavernStartMinute,
    int? EveningTavernEndMinute,
    int EveningTavernSharePercent,
    bool SheltersInRain,
    bool SheltersInStorm,
    int StormDelayMinMinutes,
    int StormDelayMaxMinutes,
    bool UnderworldNightPulse);

/// <summary>Resolved actor template, spawner spec, and schedule catalog owned by <see cref="DaggerfallDefinitions"/>.</summary>
internal sealed record DaggerfallActorCatalog(
    IReadOnlyDictionary<string, DaggerfallActorTemplateDefinition> Templates,
    IReadOnlyDictionary<string, DaggerfallActorSpawnerSpec> Specs,
    IReadOnlyDictionary<int, DaggerfallActorSpawnerSpec> SpecsByMobileId,
    IReadOnlyDictionary<string, DaggerfallActorScheduleProfile>? SchedulesMap = null)
{
    private static readonly IReadOnlyDictionary<string, DaggerfallActorScheduleProfile> EmptySchedules =
        new ReadOnlyDictionary<string, DaggerfallActorScheduleProfile>(
            new Dictionary<string, DaggerfallActorScheduleProfile>(StringComparer.Ordinal));

    internal IReadOnlyDictionary<string, DaggerfallActorScheduleProfile> Schedules => SchedulesMap ?? EmptySchedules;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> EmptyPoolMap =
        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(
            new Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(StringComparer.OrdinalIgnoreCase));

    internal static DaggerfallActorCatalog Empty { get; } = new(
        new ReadOnlyDictionary<string, DaggerfallActorTemplateDefinition>(new Dictionary<string, DaggerfallActorTemplateDefinition>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, DaggerfallActorSpawnerSpec>(new Dictionary<string, DaggerfallActorSpawnerSpec>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<int, DaggerfallActorSpawnerSpec>(new Dictionary<int, DaggerfallActorSpawnerSpec>()),
        EmptySchedules);

    /// <summary>Projects resolved actor templates with real vitals into <see cref="ArchetypeProfile"/> records.</summary>
    internal IReadOnlyDictionary<string, ArchetypeProfile> ProjectArchetypes() =>
        new ReadOnlyDictionary<string, ArchetypeProfile>(
            Templates.Values
                .Where(t => !t.Capabilities.IsPlayer && t.Health.Minimum > 1)
                .ToDictionary(t => t.Id, t => t.ToArchetypeProfile(), StringComparer.Ordinal));

    /// <summary>Projects all <see cref="ActorSpawnerFamily.PartyHire"/> spawner specs indexed by spec id (#11101).</summary>
    internal IReadOnlyDictionary<string, DaggerfallActorSpawnerSpec> ProjectPartyHireSpecs() =>
        new ReadOnlyDictionary<string, DaggerfallActorSpawnerSpec>(
            Specs.Values
                .Where(spec => spec.Family == ActorSpawnerFamily.PartyHire)
                .ToDictionary(spec => spec.Id, spec => spec, StringComparer.Ordinal));

    /// <summary>Projects all <see cref="ActorSpawnerFamily.Town"/> and <see cref="ActorSpawnerFamily.PartyHire"/> specs into people-slot spawner definitions.</summary>
    internal IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> ProjectTownSpawners() =>
        new ReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition>(
            Specs.Values
                .Where(spec => spec.Family is ActorSpawnerFamily.Town or ActorSpawnerFamily.PartyHire)
                .ToDictionary(
                    spec => spec.Id,
                    spec => new DaggerfallPeopleSpawnerDefinition(
                        spec.Id,
                        spec.Role ?? "person",
                        FormatInteraction(spec.Interaction),
                        FormatHarm(spec.Harm),
                        spec.DeriveRegionalFaction,
                        spec.VisualMode ?? "pool",
                        spec.DefaultPool ?? [],
                        spec.RegionalPools ?? EmptyPoolMap,
                        ArchetypeId: spec.TemplateId,
                        ClimatePoolsMap: spec.ClimatePools ?? EmptyPoolMap,
                        SocialGrouping: spec.SocialGrouping),
                    StringComparer.Ordinal));

    /// <summary>Projects all <see cref="ActorSpawnerFamily.Cast"/> specs into unique cast individual definitions.</summary>
    internal IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> ProjectCastIndividuals() =>
        new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(
            Specs.Values
                .Where(spec => spec.Family == ActorSpawnerFamily.Cast)
                .ToDictionary(
                    spec => spec.Id,
                    spec => new DaggerfallCastIndividualDefinition(
                        spec.Id,
                        spec.FactionId ?? 0,
                        spec.DisplayName,
                        spec.Race,
                        spec.Genders.Count > 0 && spec.Genders[0].Gender == ActorGender.Female ? "Female" : "Male",
                        spec.VisualArchive ?? 0,
                        spec.VisualRecord ?? 0,
                        spec.HomeBlock,
                        spec.HomeSlotId,
                        FormatInteraction(spec.Interaction),
                        FormatHarm(spec.Harm),
                        ArchetypeId: spec.TemplateId,
                        FamilyId: spec.FamilyId,
                        VisualAssetId: spec.VisualAssetId)
                    {
                        HomeSite = spec.HomeSite,
                        HomePosition = spec.HomePosition,
                    },
                    StringComparer.Ordinal));

    private static string FormatInteraction(ActorInteractionPolicyKind kind) => kind switch
    {
        ActorInteractionPolicyKind.None => "none",
        ActorInteractionPolicyKind.AmbientBark => "ambient-bark",
        _ => "full-dialogue",
    };

    private static string FormatHarm(ActorHarmPolicyKind kind) => kind switch
    {
        ActorHarmPolicyKind.Protected => "protected",
        ActorHarmPolicyKind.LawProtectedCivilian => "law-protected-civilian",
        _ => "combatant",
    };
}

/// <summary>
/// Concrete resolved identity attached to every live actor in <c>EntityStore</c> (player, creature, enemy, civilian, cast).
/// Deterministic from seed for procedural actors so untouched instances write zero save bytes.
/// </summary>
internal sealed class ActorInstanceIdentity(
    string spawnerId,
    string templateId,
    ActorGender gender,
    string race,
    string factionId)
{
    internal string SpawnerId { get; } = spawnerId;
    internal string TemplateId { get; } = templateId;
    internal ActorGender Gender { get; } = gender;
    internal string Race { get; } = race;
    internal string FactionId { get; } = factionId;
}

/// <summary>Deterministic resolver turning a spawner/definition + durable id into a concrete <see cref="ActorInstanceIdentity"/>.</summary>
internal static class DaggerfallActorInstanceResolver
{
    private static readonly string[] HumanoidCultureRaces =
        ["breton", "redguard", "nord", "dark-elf", "high-elf", "wood-elf", "khajiit", "argonian"];

    internal static ActorInstanceIdentity Resolve(
        DaggerfallDefinitions? definitions,
        DaggerfallActorDefinition definition,
        long durableId,
        string? explicitGender = null,
        string? explicitRace = null,
        string? explicitFaction = null,
        int? regionIndex = null,
        ulong worldSocialSeed = 0UL)
    {
        ArgumentNullException.ThrowIfNull(definition);
        DaggerfallActorSpawnerSpec? spec = null;
        if (definitions is not null)
        {
            string lookupId = definition.SpawnerId ?? definition.Id.Value;
            definitions.ActorCatalog.Specs.TryGetValue(lookupId, out spec);
        }

        ActorGender gender = ResolveGender(spec, definition, durableId, explicitGender, worldSocialSeed);
        string race = ResolveRace(spec, definition, durableId, explicitRace, regionIndex, worldSocialSeed);
        string faction = !string.IsNullOrWhiteSpace(explicitFaction) ? explicitFaction
            : !string.IsNullOrWhiteSpace(spec?.Team) ? spec.Team
            : !string.IsNullOrWhiteSpace(definition.Team) ? definition.Team
            : "neutral";

        return new ActorInstanceIdentity(
            spec?.Id ?? definition.SpawnerId ?? definition.Id.Value,
            spec?.TemplateId ?? definition.TemplateId ?? definition.Id.Value,
            gender,
            race,
            faction);
    }

    internal static ActorGender ResolveGender(
        DaggerfallActorSpawnerSpec? spec,
        DaggerfallActorDefinition definition,
        long durableId,
        string? explicitGender,
        ulong worldSocialSeed)
    {
        IReadOnlyList<ActorGender> allowed = definition.Capabilities.EffectiveAllowedGenders;
        if (ActorGenderFormat.TryParse(explicitGender, out ActorGender parsed)
            && allowed.Contains(parsed)
            && (spec is not { Genders.Count: > 0 } || spec.Genders.Any(entry => entry.Gender == parsed)))
            return parsed;
        if (spec is { Genders.Count: > 0 })
        {
            if (spec.Genders.Count == 1)
                return spec.Genders[0].Gender;
            int total = 0;
            foreach (DaggerfallActorGenderWeight entry in spec.Genders) total += entry.Weight;
            if (total > 0)
            {
                ulong hash = StableHash.NonZeroFnv1a64($"daggerfall.actor-gender:{worldSocialSeed}:{spec.Id}:{durableId}");
                int pick = (int)(hash % (ulong)total);
                int acc = 0;
                foreach (DaggerfallActorGenderWeight entry in spec.Genders)
                {
                    acc += entry.Weight;
                    if (pick < acc) return entry.Gender;
                }
            }
        }
        if (allowed.Count == 1)
            return allowed[0];
        ulong fallbackHash = StableHash.NonZeroFnv1a64($"daggerfall.actor-gender:{worldSocialSeed}:{definition.Id.Value}:{durableId}");
        return allowed[(int)(fallbackHash % (ulong)allowed.Count)];
    }

    private static string ResolveRace(
        DaggerfallActorSpawnerSpec? spec,
        DaggerfallActorDefinition definition,
        long durableId,
        string? explicitRace,
        int? regionIndex,
        ulong worldSocialSeed)
    {
        // Template race constraints always take precedence: neither spawner nor runtime caller can violate them.
        if (!string.IsNullOrWhiteSpace(definition.Capabilities.FixedRace))
            return definition.Capabilities.FixedRace;

        IReadOnlyList<string>? allowedRaces = definition.Capabilities.EffectiveAllowedRaces;
        if (allowedRaces is { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(explicitRace) && allowedRaces.Contains(explicitRace, StringComparer.Ordinal))
                return explicitRace;
            if (!string.IsNullOrWhiteSpace(spec?.Race) && allowedRaces.Contains(spec.Race, StringComparer.Ordinal))
                return spec.Race;
            if (!string.IsNullOrWhiteSpace(definition.Race) && allowedRaces.Contains(definition.Race, StringComparer.Ordinal))
                return definition.Race;
            if (allowedRaces.Count == 1)
                return allowedRaces[0];
            ulong constrainedHash = StableHash.NonZeroFnv1a64($"daggerfall.actor-race:{worldSocialSeed}:{definition.Id.Value}:{durableId}");
            return allowedRaces[(int)(constrainedHash % (ulong)allowedRaces.Count)];
        }

        if (!string.IsNullOrWhiteSpace(explicitRace)) return explicitRace;
        if (!string.IsNullOrWhiteSpace(spec?.Race)) return spec.Race;
        if (!string.IsNullOrWhiteSpace(definition.Race)) return definition.Race;
        // Regional culture default for procedural humanoids
        if (regionIndex is int region && region is 0 or 1 or 11 or 20 or 22 or 43 or 44 or 45 or 46 or 47 or 48 or 49 or 50 or 51 or 52 or 53 or 54 or 55 or 56 or 61)
        {
            ulong roll = StableHash.NonZeroFnv1a64($"daggerfall.actor-race:{worldSocialSeed}:{definition.Id.Value}:{durableId}") % 100UL;
            return roll < 70UL ? "redguard" : HumanoidCultureRaces[(int)(roll % (ulong)HumanoidCultureRaces.Length)];
        }
        ulong hash = StableHash.NonZeroFnv1a64($"daggerfall.actor-race:{worldSocialSeed}:{definition.Id.Value}:{durableId}");
        return (hash % 100UL) < 65UL ? "breton" : HumanoidCultureRaces[(int)((hash / 100UL) % (ulong)HumanoidCultureRaces.Length)];
    }
}

internal static partial class DaggerfallBaseContent
{
    internal const int MaximumTemplateInheritanceDepth = 4;

    private static readonly string[] TemplateSectionNames =
        ["actorTemplateBases", "actorTemplateCreatures", "actorTemplateClasses", "actorTemplateRoles"];

    private static readonly string[] SpecSectionNames =
        ["actorSpecsCreatures", "actorSpecsEnemies", "actorSpecsTownsfolk", "actorSpecsPartyHire", "actorSpecsCast"];

    private static readonly HashSet<string> ValidPartyHireVenues =
        new(["tavern", "guild", "tavern-or-guild", "town"], StringComparer.Ordinal);

    private static readonly HashSet<string> CompiledTacticNames =
        new(["legacy", "intelligent", "beast"], StringComparer.Ordinal);

    private static readonly HashSet<string> ForbiddenTemplateFields =
        new(["gender", "genders", "name", "displayName", "visual", "appearances", "sounds", "mobileId", "classicBinding", "home", "familyId", "team", "schedule"], StringComparer.Ordinal);

    private static readonly HashSet<string> ForbiddenCastMechanicFields =
        new(["attributes", "skills", "stats", "health", "fatigue", "magicka", "armor", "hitPointsPerLevel", "level", "weight", "xpReward", "attacks", "action", "loadout", "minMetalToHit", "lootTableKey", "careerRef", "tactic", "tendencies", "scaling", "gearPolicy"], StringComparer.Ordinal);

    private const int IndividualFactionType = 4;

    private static readonly string[] CareerAttributeOrder =
        ["strength", "intelligence", "willpower", "agility", "endurance", "personality", "speed", "luck"];

    private static (DaggerfallActorCatalog Catalog, Dictionary<DaggerfallActorId, DaggerfallActorDefinition> Actors, DaggerfallMobileCatalogSet Mobiles) ReadActorCatalog(
        JsonElement root,
        DaggerfallVocabulary vocabulary,
        DaggerfallCatalogSet catalogs,
        IReadOnlyDictionary<string, DaggerfallActionDefinition> actions,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items,
        IReadOnlyDictionary<string, DaggerfallLootTableDefinition> lootTables,
        DaggerfallCreatureFactionSet creatureFactions,
        DaggerfallMagicCatalogSet magic,
        DaggerfallFactionsSet factions,
        DaggerfallContentDiagnostics diagnostics)
    {
        // 1. Collect raw templates across all branch files
        Dictionary<string, JsonObject> rawTemplates = new(StringComparer.Ordinal);
        bool anyTemplateSection = false;
        foreach (string sectionName in TemplateSectionNames)
        {
            if (!root.TryGetProperty(sectionName, out JsonElement section)) continue;
            anyTemplateSection = true;
            JsonElement obj = Object(section, sectionName, diagnostics);
            if (obj.ValueKind != JsonValueKind.Object) continue;
            foreach (JsonElement item in Array(obj, "templates", diagnostics))
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add($"Section '{sectionName}' contains a non-object template entry.");
                    continue;
                }
                RejectDuplicateProperties(item, $"{sectionName}.template", diagnostics);
                string id = Text(item, "id", diagnostics);
                if (!ValidId(id))
                {
                    diagnostics.Add($"Actor template id '{id}' in '{sectionName}' is invalid.");
                    continue;
                }
                foreach (JsonProperty prop in item.EnumerateObject())
                {
                    if (ForbiddenTemplateFields.Contains(prop.Name))
                        diagnostics.Add($"Actor template '{id}' may not declare instance-only field '{prop.Name}'; move it to a spawner spec.");
                }
                JsonObject parsed = JsonNode.Parse(item.GetRawText())!.AsObject();
                if (!rawTemplates.TryAdd(id, parsed))
                    diagnostics.Add($"Duplicate actor template '{id}'.");
            }
        }

        if (!anyTemplateSection || rawTemplates.Count == 0)
        {
            diagnostics.Add("Base payload publishes no actor templates.");
            return (DaggerfallActorCatalog.Empty, [], new(new Dictionary<int, DaggerfallMobileDefinition>(), new Dictionary<string, DaggerfallMobileDefinition>(StringComparer.Ordinal)));
        }

        // 2. Resolve single-parent inheritance (acyclic, depth <= 4)
        Dictionary<string, JsonObject> mergedNodes = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> chains = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallActorTemplateDefinition> resolvedTemplates = new(StringComparer.Ordinal);

        JsonObject? ResolveMerged(string id, List<string> stack)
        {
            if (mergedNodes.TryGetValue(id, out JsonObject? cached)) return cached;
            if (!rawTemplates.TryGetValue(id, out JsonObject? raw))
            {
                diagnostics.Add($"Actor template '{stack[^1]}' extends unknown parent '{id}'.");
                return null;
            }
            int cycleAt = stack.IndexOf(id);
            if (cycleAt >= 0)
            {
                diagnostics.Add($"Actor template inheritance cycle detected: {string.Join(" -> ", stack.Skip(cycleAt).Append(id))}.");
                return null;
            }
            if (stack.Count > MaximumTemplateInheritanceDepth)
            {
                diagnostics.Add($"Actor template '{stack[0]}' exceeds maximum inheritance depth {MaximumTemplateInheritanceDepth} ({string.Join(" -> ", stack.Append(id))}).");
                return null;
            }

            string? parentId = null;
            if (raw.TryGetPropertyValue("extends", out JsonNode? extNode) && extNode is not null)
            {
                if (extNode is JsonValue extVal && extVal.TryGetValue<string>(out string? extStr) && !string.IsNullOrWhiteSpace(extStr))
                    parentId = extStr;
                else
                {
                    diagnostics.Add($"Actor template '{id}' has invalid 'extends' value.");
                    return null;
                }
            }

            JsonObject merged;
            List<string> chain;
            if (parentId is not null)
            {
                stack.Add(id);
                JsonObject? parentMerged = ResolveMerged(parentId, stack);
                stack.RemoveAt(stack.Count - 1);
                if (parentMerged is null) return null;
                merged = parentMerged.DeepClone().AsObject();
                chain = [.. chains[parentId], id];
                if (chain.Count - 1 > MaximumTemplateInheritanceDepth)
                {
                    diagnostics.Add($"Actor template '{id}' exceeds maximum depth {MaximumTemplateInheritanceDepth} ({string.Join(" -> ", chain)}).");
                    return null;
                }
            }
            else
            {
                merged = new JsonObject();
                chain = [id];
            }

            // Apply career defaults at the exact inheritance layer where careerRef is declared, BEFORE
            // applying this node's own properties so local overrides, explicit null clears, and child
            // overrides all take precedence cleanly.
            if (raw.TryGetPropertyValue("careerRef", out JsonNode? careerNode)
                && careerNode is JsonValue careerVal
                && careerVal.TryGetValue<string>(out string? declaredCareerRef)
                && !string.IsNullOrWhiteSpace(declaredCareerRef)
                && catalogs.TryGetCareer(declaredCareerRef, out DaggerfallCareerDefinition? declaredCareer)
                && declaredCareer is not null)
            {
                string? effectiveScaling = raw.TryGetPropertyValue("scaling", out JsonNode? rawScale) && rawScale is JsonValue rsv && rsv.TryGetValue<string>(out string? rsStr)
                    ? rsStr
                    : merged["scaling"] is JsonValue msv && msv.TryGetValue<string>(out string? msStr) ? msStr : null;
                JsonObject attrMap = merged["attributes"] is JsonObject existingAttrs ? existingAttrs : new JsonObject();
                for (int i = 0; i < CareerAttributeOrder.Length && i < declaredCareer.AttributeValues.Count; i++)
                    attrMap[CareerAttributeOrder[i]] = declaredCareer.AttributeValues[i];
                merged["attributes"] = attrMap;

                if (string.Equals(effectiveScaling, "career-leveled", StringComparison.Ordinal))
                {
                    JsonObject skillMap = merged["skills"] is JsonObject existingSkills ? existingSkills : new JsonObject();
                    foreach (DaggerfallStatId skillId in vocabulary.Skills)
                    {
                        if (!skillMap.ContainsKey(skillId.Value))
                            skillMap[skillId.Value] = 35;
                    }
                    merged["skills"] = skillMap;
                    merged["health"] = new JsonObject
                    {
                        ["minimum"] = 11,
                        ["maximum"] = checked(10 + declaredCareer.HitPointsPerLevel),
                    };
                }
                merged["hitPointsPerLevel"] = declaredCareer.HitPointsPerLevel;
            }

            foreach ((string key, JsonNode? value) in raw)
            {
                if (key is "id" or "extends") continue;
                if (key is "attributes" or "skills")
                {
                    if (value is null)
                    {
                        merged[key] = new JsonObject();
                    }
                    else if (value is JsonObject mapOverride)
                    {
                        JsonObject targetMap = merged[key] is JsonObject existing ? existing : new JsonObject();
                        foreach ((string mk, JsonNode? mv) in mapOverride)
                        {
                            if (mv is null) targetMap.Remove(mk);
                            else targetMap[mk] = mv.DeepClone();
                        }
                        merged[key] = targetMap;
                    }
                    else
                    {
                        diagnostics.Add($"Actor template '{id}' field '{key}' must be an object or null.");
                    }
                }
                else
                {
                    merged[key] = value?.DeepClone();
                }
            }
            // If this layer introduces a non-empty spells list without authoring spellPolicy, infer 'fixed' at this layer;
            // if this layer explicitly authors spellPolicy (including 'none'), that explicit choice wins.
            if (!raw.ContainsKey("spellPolicy") && raw.TryGetPropertyValue("spells", out JsonNode? rawSpells))
            {
                if (rawSpells is JsonArray rawSpellsArr && rawSpellsArr.Count > 0)
                    merged["spellPolicy"] = "fixed";
                else if (rawSpells is null && string.Equals(merged["spellPolicy"]?.ToString(), "fixed", StringComparison.Ordinal))
                    merged["spellPolicy"] = "none";
            }
            merged["id"] = id;
            mergedNodes[id] = merged;
            chains[id] = chain;
            return merged;
        }

        foreach (string id in rawTemplates.Keys)
        {
            JsonObject? merged = ResolveMerged(id, []);
            if (merged is null) continue;
            DaggerfallActorTemplateDefinition? built = BuildResolvedTemplate(
                id, rawTemplates[id], merged, chains[id], vocabulary, catalogs, actions, items, lootTables, magic, diagnostics);
            if (built is not null)
                resolvedTemplates[id] = built;
        }

        // 2b. Read named schedule profiles (actors/schedules.json)
        Dictionary<string, DaggerfallActorScheduleProfile> schedules = ReadActorSchedules(root, diagnostics);

        // 3. Read spawner specs & project DaggerfallActorDefinition + DaggerfallMobileCatalogSet
        Dictionary<string, DaggerfallActorSpawnerSpec> specs = new(StringComparer.Ordinal);
        Dictionary<int, DaggerfallActorSpawnerSpec> specsByMobile = [];
        Dictionary<string, DaggerfallActorSpawnerSpec> specsByVisualAsset = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<DaggerfallActorId, DaggerfallActorDefinition> actors = [];
        Dictionary<int, DaggerfallMobileDefinition> mobiles = [];
        Dictionary<string, DaggerfallMobileDefinition> mobilesByActor = new(StringComparer.Ordinal);
        HashSet<string> validCreatureTeams = creatureFactions.Factions.Keys.ToHashSet(StringComparer.Ordinal);
        validCreatureTeams.Add("player-ally");

        bool anySpecSection = false;
        foreach (string sectionName in SpecSectionNames)
        {
            if (!root.TryGetProperty(sectionName, out JsonElement section)) continue;
            anySpecSection = true;
            JsonElement obj = Object(section, sectionName, diagnostics);
            if (obj.ValueKind != JsonValueKind.Object) continue;
            foreach (JsonElement item in Array(obj, "specs", diagnostics))
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add($"Section '{sectionName}' contains a non-object spawner spec.");
                    continue;
                }
                RejectDuplicateProperties(item, $"{sectionName}.spec", diagnostics);
                DaggerfallActorSpawnerSpec? spec = ReadSpawnerSpec(item, sectionName, resolvedTemplates, schedules, validCreatureTeams, factions, diagnostics);
                if (spec is null) continue;
                if (!specs.TryAdd(spec.Id, spec))
                {
                    diagnostics.Add($"Duplicate actor spawner spec '{spec.Id}'.");
                    continue;
                }
                if (spec.ClassicBinding is { } binding)
                {
                    if (!specsByMobile.TryAdd(binding.MobileId, spec))
                        diagnostics.Add($"Actor spawner spec '{spec.Id}' claims classic mobileId {binding.MobileId}, already claimed by '{specsByMobile[binding.MobileId].Id}'.");
                }
                if (!string.IsNullOrWhiteSpace(spec.VisualAssetId))
                {
                    if (!specsByVisualAsset.TryAdd(spec.VisualAssetId, spec))
                        diagnostics.Add($"Actor spawner spec '{spec.Id}' claims visual assetId '{spec.VisualAssetId}', already claimed by '{specsByVisualAsset[spec.VisualAssetId].Id}'.");
                }
            }
        }

        if (!anySpecSection || specs.Count == 0)
            diagnostics.Add("Base payload publishes no actor spawner specs.");

        // Count classic identities to preserve published-variant disposition parity
        Dictionary<string, int> classicIdentityCounts = specs.Values
            .Where(s => s.ClassicBinding is not null)
            .GroupBy(s => s.ClassicBinding!.Identity, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (DaggerfallActorSpawnerSpec spec in specs.Values)
        {
            if (!resolvedTemplates.TryGetValue(spec.TemplateId, out DaggerfallActorTemplateDefinition? template))
                continue;

            if (spec.Published)
            {
                DaggerfallActorId actorId = new(spec.Id);
                string derivedKind = DeriveActorKind(spec, template);
                string? catalogRace = spec.Race is not null && catalogs.Races.Any(race => race.Id == spec.Race)
                    ? spec.Race
                    : null;
                DaggerfallActorDefinition definition = new(
                    actorId,
                    derivedKind,
                    template.Stats,
                    template.Health,
                    new(DaggerfallMechanicsIds.Health, template.Capabilities.IsPlayer ? DaggerfallMechanicsIds.Stamina : null),
                    template.Rewards,
                    template.Armor,
                    spec.ClassicBinding?.MobileId,
                    template.HitPointsPerLevel,
                    template.Attacks,
                    spec.Team,
                    template.MinimumMaterial,
                    template.LootTableKey,
                    template.Level,
                    template.Weight,
                    template.ActionId,
                    template.Loadout,
                    spec.Presentation,
                    catalogRace,
                    template.CareerRef)
                {
                    TemplateId = template.Id,
                    SpawnerId = spec.Id,
                    Capabilities = template.Capabilities,
                    DefaultHarm = spec.Harm,
                    DefaultInteraction = spec.Interaction,
                    SpellPolicy = template.SpellPolicy,
                    Spells = template.EffectiveSpells,
                    Look = spec.Look,
                };
                if (!actors.TryAdd(actorId, definition))
                    diagnostics.Add($"Duplicate actor definition '{actorId.Value}'.");
            }

            if (spec.ClassicBinding is { } binding)
            {
                string numbered = $"{binding.Identity}-{binding.MobileId.ToString(CultureInfo.InvariantCulture)}";
                string? linkedActor = spec.Published ? spec.Id : null;
                string disposition = !spec.Published ? "unpublished"
                    : spec.Family == ActorSpawnerFamily.Enemy && spec.Id.StartsWith("encounter-", StringComparison.Ordinal) ? "human-mobile"
                    : spec.Id == numbered && classicIdentityCounts.GetValueOrDefault(binding.Identity) > 1 ? "published-variant"
                    : "published";
                // Classic HumanMobiles property expects Actor == null for encounter-* synthesized classes? No: ActorFor uses mobile.Actor ?? $"encounter-{identity}"
                // Wait: DaggerfallMobileCatalogTests checks HumanMobiles has Actor == null when disposition == "human-mobile" — keep exact parity!
                string? mobileActorField = disposition == "human-mobile" ? null : linkedActor;
                DaggerfallMobileDefinition mobile = new(
                    binding.MobileId,
                    binding.DonorName,
                    binding.Identity,
                    template.Capabilities.CastsMagic,
                    template.Capabilities.CanOpenDoors,
                    template.Capabilities.SeesThroughInvisibility,
                    mobileActorField,
                    disposition,
                    binding.Behaviour,
                    binding.Affinity,
                    spec.MaleTexture,
                    spec.FemaleTexture,
                    spec.CorpseArchive,
                    spec.CorpseRecord,
                    spec.HasIdle,
                    template.Capabilities.HasRangedAttack1,
                    template.Capabilities.HasRangedAttack2,
                    spec.MoveSound,
                    spec.BarkSound,
                    spec.AttackSound,
                    binding.DonorLootTableKey,
                    binding.DonorMinMetalToHit,
                    binding.DonorDamage?.MinimumDamage ?? 0,
                    binding.DonorDamage?.MaximumDamage ?? 0,
                    binding.DonorHealth?.Minimum ?? 0,
                    binding.DonorHealth?.Maximum ?? 0,
                    binding.DonorLevel,
                    binding.DonorArmorValue,
                    spec.ParrySounds,
                    binding.MapChance,
                    binding.DonorWeight,
                    binding.DonorTeam,
                    template.Capabilities.AttackModifierFlags)
                {
                    SoulPoints = binding.SoulPoints,
                    NoShadow = spec.NoShadow,
                    PrefersRanged = template.Capabilities.PrefersRanged,
                    GlowColor = spec.GlowColor,
                    ResistanceFlags = template.Capabilities.ResistanceFlags,
                    ImmunityFlags = template.Capabilities.ImmunityFlags,
                    LowToleranceFlags = template.Capabilities.LowToleranceFlags,
                    CriticalWeaknessFlags = template.Capabilities.CriticalWeaknessFlags,
                    Tactic = template.Capabilities.Tactic,
                    Tendencies = template.Capabilities.Tendencies,
                    SpawnerId = spec.Id,
                };
                if (mobiles.TryAdd(binding.MobileId, mobile) && spec.Published)
                    mobilesByActor.TryAdd(spec.Id, mobile);
            }
        }

        // A mobile look draws as a classic mobile, which a creature or enemy spec binds.
        foreach ((string owner, DaggerfallActorLook? look) in resolvedTemplates.Values.Select(t => ($"Actor template '{t.Id}'", t.Look))
            .Concat(specs.Values.Select(s => ($"Actor spawner spec '{s.Id}'", s.Look))))
            if (look?.MobileId is int lookMobile && !mobiles.ContainsKey(lookMobile))
                diagnostics.Add($"{owner} look names mobile {lookMobile}, which no spec binds.");
        // A spec that binds a classic mobile has no billboard art of its own, so it draws as a mobile.
        foreach (DaggerfallActorSpawnerSpec bound in specs.Values.Where(s => s.ClassicBinding is not null && s.Look is { Kind: not DaggerfallActorLookKind.Mobile }))
            diagnostics.Add($"Actor spawner spec '{bound.Id}' binds classic mobile {bound.ClassicBinding!.MobileId}, so its look must be a mobile look.");

        DaggerfallActorCatalog catalog = new(
            new ReadOnlyDictionary<string, DaggerfallActorTemplateDefinition>(resolvedTemplates),
            new ReadOnlyDictionary<string, DaggerfallActorSpawnerSpec>(specs),
            new ReadOnlyDictionary<int, DaggerfallActorSpawnerSpec>(specsByMobile),
            new ReadOnlyDictionary<string, DaggerfallActorScheduleProfile>(schedules));
        DaggerfallMobileCatalogSet mobileSet = new(
            new ReadOnlyDictionary<int, DaggerfallMobileDefinition>(mobiles),
            new ReadOnlyDictionary<string, DaggerfallMobileDefinition>(mobilesByActor));
        return (catalog, actors, mobileSet);
    }

    private static string DeriveActorKind(DaggerfallActorSpawnerSpec spec, DaggerfallActorTemplateDefinition template) =>
        template.Capabilities.IsPlayer ? DaggerfallActorKinds.Player
        : spec.Family == ActorSpawnerFamily.PartyHire ? DaggerfallActorKinds.Civilian
        : spec.Family == ActorSpawnerFamily.Enemy || template.Capabilities.Scaling == ActorScalingMode.CareerLeveled ? DaggerfallActorKinds.EnemyClass
        : spec.Family == ActorSpawnerFamily.Creature ? DaggerfallActorKinds.Monster
        : spec.Family == ActorSpawnerFamily.Cast ? DaggerfallActorKinds.StaticNpc
        : DaggerfallActorKinds.Civilian;

    private static DaggerfallActorTemplateDefinition? BuildResolvedTemplate(
        string id,
        JsonObject raw,
        JsonObject merged,
        List<string> chain,
        DaggerfallVocabulary vocabulary,
        DaggerfallCatalogSet catalogs,
        IReadOnlyDictionary<string, DaggerfallActionDefinition> actions,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items,
        IReadOnlyDictionary<string, DaggerfallLootTableDefinition> lootTables,
        DaggerfallMagicCatalogSet magic,
        DaggerfallContentDiagnostics diagnostics)
    {
        using JsonDocument doc = JsonDocument.Parse(merged.ToJsonString());
        JsonElement el = doc.RootElement;

        string? careerRef = OptionalText(el, "careerRef", diagnostics);
        if (careerRef is not null && !catalogs.TryGetCareer(careerRef, out _))
            diagnostics.Add($"Actor template '{id}' refers to unknown career '{careerRef}'.");

        ActorScalingMode scaling = ParseEnum(OptionalText(el, "scaling", diagnostics) ?? "fixed", id, "scaling", diagnostics, static s => s switch
        {
            "fixed" => ActorScalingMode.Fixed,
            "level-scaled-skills" => ActorScalingMode.LevelScaledSkills,
            "career-leveled" => ActorScalingMode.CareerLeveled,
            _ => (ActorScalingMode?)null,
        }, ActorScalingMode.Fixed);

        // Career defaults were already applied at the declaring inheritance layer in ResolveMerged;
        // read the final merged attributes/skills/health/hitPointsPerLevel directly.
        Dictionary<DaggerfallStatId, int> statValues = vocabulary.ActorStats.ToDictionary(s => s, _ => 0);
        if (el.TryGetProperty("attributes", out JsonElement attrsEl) && attrsEl.ValueKind == JsonValueKind.Object)
            ReadStatMap(attrsEl, vocabulary.Attributes, statValues, diagnostics);
        if (el.TryGetProperty("skills", out JsonElement skillsEl) && skillsEl.ValueKind == JsonValueKind.Object)
            ReadStatMap(skillsEl, vocabulary.Skills, statValues, diagnostics);

        if (statValues[DaggerfallMechanicsIds.Strength] is < 0 or > 10_000
            || statValues[DaggerfallMechanicsIds.Endurance] is < 0 or > 10_000
            || statValues[DaggerfallMechanicsIds.Intelligence] is < 0 or > 10_000)
            diagnostics.Add($"Actor template '{id}' attributes are outside the supported range.");

        int? hpPerLevel = OptionalInteger(el, "hitPointsPerLevel", diagnostics);

        DaggerfallVitalRange health = el.TryGetProperty("health", out JsonElement healthEl) && healthEl.ValueKind == JsonValueKind.Object
            ? ReadRange(healthEl, $"template '{id}' health", diagnostics)
            : new DaggerfallVitalRange(10, 20);

        int armor = OptionalInteger(el, "armor", diagnostics) ?? 0;
        if (armor is < -MaximumAuthoredArmor or > MaximumAuthoredArmor)
            diagnostics.Add($"Actor template '{id}' armor is outside the supported range.");

        int? level = OptionalInteger(el, "level", diagnostics);
        int? weight = OptionalInteger(el, "weight", diagnostics);
        int xpReward = OptionalInteger(el, "xpReward", diagnostics) ?? 0;
        IReadOnlyList<DaggerfallAttackRange> attacks = el.TryGetProperty("attacks", out JsonElement attacksEl) && attacksEl.ValueKind == JsonValueKind.Array
            ? ReadAttackRanges(el, diagnostics) : [];
        string? actionId = OptionalText(el, "action", diagnostics);
        if (actionId is not null && !actions.ContainsKey(actionId))
            diagnostics.Add($"Actor template '{id}' refers to missing action '{actionId}'.");
        IReadOnlyList<DaggerfallLoadoutEntry> loadout = el.TryGetProperty("loadout", out JsonElement loadoutEl) && loadoutEl.ValueKind == JsonValueKind.Array
            ? ReadLoadout(el, items, diagnostics) : [];
        string? minMetal = OptionalText(el, "minMetalToHit", diagnostics);
        string? lootTableKey = OptionalText(el, "lootTableKey", diagnostics);
        if (lootTableKey is not null && !lootTables.ContainsKey(lootTableKey))
            diagnostics.Add($"Actor template '{id}' refers to unknown loot table '{lootTableKey}'.");

        string tactic = DaggerfallMobileDefinition.LegacyTactic;
        if (el.TryGetProperty("tactic", out JsonElement tacticEl) && tacticEl.ValueKind != JsonValueKind.Null)
        {
            if (tacticEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tacticEl.GetString()))
                diagnostics.Add($"Actor template '{id}' names a blank or non-text tactic.");
            else
            {
                tactic = tacticEl.GetString()!;
                if (!CompiledTacticNames.Contains(tactic))
                    diagnostics.Add($"Actor template '{id}' names unknown tactic '{tactic}'.");
            }
        }

        DaggerfallTacticTendencies? tendencies = null;
        if (el.TryGetProperty("tendencies", out JsonElement tendEl) && tendEl.ValueKind != JsonValueKind.Null)
        {
            if (tendEl.ValueKind != JsonValueKind.Object)
                diagnostics.Add($"Actor template '{id}' tendencies must be an object or null.");
            else
            {
                tendencies = new(OptionalNumber(tendEl, "courage", diagnostics) ?? double.NaN,
                    OptionalNumber(tendEl, "discipline", diagnostics) ?? double.NaN);
                if (!tendencies.IsValid)
                    diagnostics.Add($"Actor template '{id}' needs tendencies' courage and discipline between 0 and 1.");
                if (tactic != "intelligent")
                    diagnostics.Add($"Actor template '{id}' authors tendencies, which tactic '{tactic}' does not weigh.");
            }
        }

        ActorGearPolicy gearPolicy = ParseEnum(OptionalText(el, "gearPolicy", diagnostics) ?? "fixed-loadout", id, "gearPolicy", diagnostics, static s => s switch
        {
            "fixed-loadout" => ActorGearPolicy.FixedLoadout,
            "generated-class-gear" => ActorGearPolicy.GeneratedClassGear,
            "fixed-variant-gear" => ActorGearPolicy.FixedVariantGear,
            _ => (ActorGearPolicy?)null,
        }, ActorGearPolicy.FixedLoadout);

        ActorBodyWeightMode bodyWeightMode = ParseEnum(OptionalText(el, "bodyWeightMode", diagnostics) ?? "fixed", id, "bodyWeightMode", diagnostics, static s => s switch
        {
            "fixed" => ActorBodyWeightMode.Fixed,
            "gendered-humanoid" => ActorBodyWeightMode.GenderedHumanoid,
            _ => (ActorBodyWeightMode?)null,
        }, ActorBodyWeightMode.Fixed);

        ActorMovementMedium movement = ParseEnum(OptionalText(el, "movement", diagnostics) ?? "ground", id, "movement", diagnostics, static s => s switch
        {
            "ground" => ActorMovementMedium.Ground,
            "flying" => ActorMovementMedium.Flying,
            "aquatic" => ActorMovementMedium.Aquatic,
            "spectral" => ActorMovementMedium.Spectral,
            _ => (ActorMovementMedium?)null,
        }, ActorMovementMedium.Ground);

        DaggerfallEnemyGroup enemyGroup = ParseEnum(OptionalText(el, "enemyGroup", diagnostics) ?? "none", id, "enemyGroup", diagnostics, static s => s switch
        {
            "none" => DaggerfallEnemyGroup.None,
            "animals" => DaggerfallEnemyGroup.Animals,
            "humanoid" => DaggerfallEnemyGroup.Humanoid,
            "undead" => DaggerfallEnemyGroup.Undead,
            "daedra" => DaggerfallEnemyGroup.Daedra,
            _ => (DaggerfallEnemyGroup?)null,
        }, DaggerfallEnemyGroup.None);

        string? languageSkill = OptionalText(el, "languageSkill", diagnostics);
        if (languageSkill is not null && !vocabulary.Skills.Contains(new DaggerfallStatId(languageSkill)))
            diagnostics.Add($"Actor template '{id}' languageSkill '{languageSkill}' is not a known skill.");

        List<ActorGender> allowedGenders = [];
        if (el.TryGetProperty("allowedGenders", out JsonElement gendersEl) && gendersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement g in gendersEl.EnumerateArray())
            {
                if (g.ValueKind == JsonValueKind.String && ActorGenderFormat.TryParse(g.GetString(), out ActorGender parsed))
                {
                    if (!allowedGenders.Contains(parsed)) allowedGenders.Add(parsed);
                }
                else
                    diagnostics.Add($"Actor template '{id}' allowedGenders contains invalid gender '{g}'.");
            }
        }
        if (allowedGenders.Count == 0)
            diagnostics.Add($"Actor template '{id}' must allow at least one gender (male, female, or neutral).");

        List<string> traits = [];
        if (el.TryGetProperty("traits", out JsonElement traitsEl) && traitsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement t in traitsEl.EnumerateArray())
                if (t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                    traits.Add(t.GetString()!);
        }

        string? fixedRace = OptionalText(el, "fixedRace", diagnostics);
        List<string>? allowedRaces = null;
        if (el.TryGetProperty("allowedRaces", out JsonElement allowedRacesEl) && allowedRacesEl.ValueKind != JsonValueKind.Null)
        {
            if (allowedRacesEl.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Actor template '{id}' allowedRaces must be an array of race identifiers or null.");
            }
            else
            {
                allowedRaces = [];
                foreach (JsonElement rEl in allowedRacesEl.EnumerateArray())
                {
                    if (rEl.ValueKind != JsonValueKind.String || !ValidId(rEl.GetString() ?? ""))
                        diagnostics.Add($"Actor template '{id}' allowedRaces contains invalid race identifier '{rEl}'.");
                    else if (!allowedRaces.Contains(rEl.GetString()!, StringComparer.Ordinal))
                        allowedRaces.Add(rEl.GetString()!);
                }
                if (allowedRaces.Count == 0)
                    diagnostics.Add($"Actor template '{id}' allowedRaces cannot be empty; use null for unrestricted.");
            }
        }
        if (fixedRace is not null && allowedRaces is { Count: > 0 } && !allowedRaces.Contains(fixedRace, StringComparer.Ordinal))
            diagnostics.Add($"Actor template '{id}' fixedRace '{fixedRace}' is not present in its allowedRaces.");

        List<string> spells = [];
        bool hasSpellsArray = false;
        if (el.TryGetProperty("spells", out JsonElement spellsEl) && spellsEl.ValueKind != JsonValueKind.Null)
        {
            if (spellsEl.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Actor template '{id}' spells must be an array of spell keys or null.");
            }
            else
            {
                hasSpellsArray = true;
                HashSet<string> seenSpells = new(StringComparer.Ordinal);
                foreach (JsonElement sEl in spellsEl.EnumerateArray())
                {
                    if (sEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(sEl.GetString()))
                    {
                        diagnostics.Add($"Actor template '{id}' spells contains a blank or non-text spell key.");
                        continue;
                    }
                    string spellKey = sEl.GetString()!;
                    if (!seenSpells.Add(spellKey))
                    {
                        diagnostics.Add($"Actor template '{id}' repeats spell '{spellKey}'.");
                        continue;
                    }
                    if (magic.Spells.Count > 0 && !magic.Spells.ContainsKey(spellKey))
                    {
                        diagnostics.Add($"Actor template '{id}' refers to unknown spell '{spellKey}'.");
                        continue;
                    }
                    spells.Add(spellKey);
                }
            }
        }

        string? spellPolicyRaw = OptionalText(el, "spellPolicy", diagnostics);
        ActorSpellPolicy spellPolicy = spellPolicyRaw is not null
            ? ParseEnum(spellPolicyRaw, id, "spellPolicy", diagnostics, static s => s switch
            {
                "none" => ActorSpellPolicy.None,
                "fixed" => ActorSpellPolicy.Fixed,
                "class-tiers" => ActorSpellPolicy.ClassTiers,
                _ => (ActorSpellPolicy?)null,
            }, ActorSpellPolicy.None)
            : spells.Count > 0 ? ActorSpellPolicy.Fixed : ActorSpellPolicy.None;
        // If a child explicitly cleared spells to null (and didn't ask for class-tiers), treat fixed policy as cleared to none.
        if (spellPolicy == ActorSpellPolicy.Fixed && !hasSpellsArray)
            spellPolicy = ActorSpellPolicy.None;
        else if (spellPolicy == ActorSpellPolicy.Fixed && spells.Count == 0 && !diagnostics.HasErrors)
            diagnostics.Add($"Actor template '{id}' uses spellPolicy 'fixed' with an empty spells list.");
        else if (spellPolicy == ActorSpellPolicy.None)
            spells.Clear();

        DaggerfallActorCapabilities capabilities = new(
            IsPlayer: OptionalBool(el, "isPlayer"),
            IsCombatEncounter: OptionalBool(el, "isCombatEncounter"),
            Scaling: scaling,
            GearPolicy: gearPolicy,
            FixedEquipmentVariant: OptionalInteger(el, "fixedEquipmentVariant", diagnostics),
            BodyWeightMode: bodyWeightMode,
            UsesNaturalArmor: OptionalBool(el, "usesNaturalArmor"),
            TakesExtraBluntDamage: OptionalBool(el, "takesExtraBluntDamage"),
            CityWatchArrest: OptionalBool(el, "cityWatchArrest"),
            Movement: movement,
            CanOpenDoors: OptionalBool(el, "canOpenDoors"),
            SeesThroughInvisibility: OptionalBool(el, "seesThroughInvisibility"),
            CastsMagic: OptionalBool(el, "castsMagic"),
            PrefersRanged: OptionalBool(el, "prefersRanged"),
            HasRangedAttack1: OptionalBool(el, "hasRangedAttack1"),
            HasRangedAttack2: OptionalBool(el, "hasRangedAttack2"),
            AttackModifierFlags: FlagByte(el, "attackModifierFlags", diagnostics),
            ResistanceFlags: FlagByte(el, "resistanceFlags", diagnostics),
            ImmunityFlags: FlagByte(el, "immunityFlags", diagnostics),
            LowToleranceFlags: FlagByte(el, "lowToleranceFlags", diagnostics),
            CriticalWeaknessFlags: FlagByte(el, "criticalWeaknessFlags", diagnostics),
            EnemyGroup: enemyGroup,
            LanguageSkill: languageSkill,
            Tactic: tactic,
            Tendencies: tendencies,
            AllowedGenders: allowedGenders.AsReadOnly(),
            FixedRace: fixedRace,
            AllowedRaces: allowedRaces?.AsReadOnly(),
            Traits: traits.AsReadOnly());

        int fatigue = OptionalInteger(el, "fatigue", diagnostics) ?? 50;
        int magicka = OptionalInteger(el, "magicka", diagnostics) ?? 0;
        if (fatigue is < 0 or > 10_000)
            diagnostics.Add($"Actor template '{id}' has invalid fatigue {fatigue}.");
        if (magicka is < 0 or > 10_000)
            diagnostics.Add($"Actor template '{id}' has invalid magicka {magicka}.");
        ActorHarmPolicyKind defaultHarm = ParseEnum(OptionalText(el, "defaultHarm", diagnostics) ?? "combatant", id, "defaultHarm", diagnostics, static s => s switch
        {
            "protected" => ActorHarmPolicyKind.Protected,
            "law-protected-civilian" => ActorHarmPolicyKind.LawProtectedCivilian,
            "combatant" => ActorHarmPolicyKind.Combatant,
            _ => (ActorHarmPolicyKind?)null,
        }, ActorHarmPolicyKind.Combatant);
        ActorInteractionPolicyKind defaultInteraction = ParseEnum(OptionalText(el, "defaultInteraction", diagnostics) ?? "none", id, "defaultInteraction", diagnostics, static s => s switch
        {
            "none" => ActorInteractionPolicyKind.None,
            "ambient-bark" => ActorInteractionPolicyKind.AmbientBark,
            "full-dialogue" => ActorInteractionPolicyKind.FullDialogue,
            _ => (ActorInteractionPolicyKind?)null,
        }, ActorInteractionPolicyKind.None);
        bool walks = el.TryGetProperty("walks", out JsonElement walksEl) && walksEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? walksEl.ValueKind == JsonValueKind.True
            : movement == ActorMovementMedium.Ground;

        string? extendsId = raw.TryGetPropertyValue("extends", out JsonNode? eNode) && eNode is JsonValue eVal && eVal.TryGetValue<string>(out string? eStr)
            ? eStr : null;

        return new DaggerfallActorTemplateDefinition(
            id,
            extendsId,
            chain.AsReadOnly(),
            new DaggerfallStatBases(statValues),
            health,
            armor,
            hpPerLevel,
            level,
            weight,
            new DaggerfallRewardPolicy(xpReward),
            attacks,
            actionId,
            loadout,
            minMetal,
            lootTableKey,
            careerRef,
            capabilities,
            spellPolicy,
            spells.AsReadOnly(),
            fatigue,
            magicka,
            defaultHarm,
            defaultInteraction,
            walks,
            ReadActorLook(el, $"Actor template '{id}'", diagnostics));
    }

    private static readonly HashSet<string> LookFields = new(["kind", "mobileId", "splat"], StringComparer.Ordinal);

    /// <summary>Reads an owner's <c>look</c>: its kind, a mobile look's <c>mobileId</c>, and the splat character it names by gender.</summary>
    private static DaggerfallActorLook? ReadActorLook(JsonElement owner, string context, DaggerfallContentDiagnostics diagnostics)
    {
        if (!owner.TryGetProperty("look", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        JsonElement look = Object(value, $"{context} look", diagnostics);
        if (look.ValueKind != JsonValueKind.Object) return null;
        foreach (JsonProperty field in look.EnumerateObject())
            if (!LookFields.Contains(field.Name)) diagnostics.Add($"{context} look has unknown field '{field.Name}'.");
        string kindText = Text(look, "kind", diagnostics);
        DaggerfallActorLookKind? kind = kindText switch
        {
            "billboard" => DaggerfallActorLookKind.Billboard,
            "townsperson" => DaggerfallActorLookKind.Townsperson,
            "mobile" => DaggerfallActorLookKind.Mobile,
            _ => null,
        };
        if (kind is null)
        {
            diagnostics.Add($"{context} look has unknown kind '{kindText}'; it is billboard, townsperson or mobile.");
            return null;
        }
        int? mobileId = OptionalInteger(look, "mobileId", diagnostics);
        if ((kind == DaggerfallActorLookKind.Mobile) != mobileId.HasValue)
            diagnostics.Add($"{context} look must name a mobileId exactly when its kind is mobile.");
        Dictionary<ActorGender, string> splats = [];
        if (look.TryGetProperty("splat", out JsonElement splatValue) && splatValue.ValueKind != JsonValueKind.Null)
        {
            if (kind == DaggerfallActorLookKind.Billboard)
                diagnostics.Add($"{context} look is a billboard, which draws its sprite and names no splat character.");
            JsonElement named = Object(splatValue, $"{context} look splat", diagnostics);
            if (named.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty entry in named.EnumerateObject())
                {
                    if (!ActorGenderFormat.TryParse(entry.Name, out ActorGender gender))
                        diagnostics.Add($"{context} look splat names unknown gender '{entry.Name}'.");
                    else if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
                        diagnostics.Add($"{context} look splat '{entry.Name}' must name a splat character.");
                    else
                        splats[gender] = entry.Value.GetString()!;
                }
        }
        return new(kind.Value, mobileId, splats.AsReadOnly());
    }

    private static Dictionary<string, DaggerfallActorScheduleProfile> ReadActorSchedules(
        JsonElement root,
        DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallActorScheduleProfile> schedules = new(StringComparer.Ordinal);
        if (!root.TryGetProperty("actorSchedules", out JsonElement section) || section.ValueKind == JsonValueKind.Null)
            return schedules;

        JsonElement obj = Object(section, "actorSchedules", diagnostics);
        if (obj.ValueKind != JsonValueKind.Object) return schedules;

        foreach (JsonElement item in Array(obj, "profiles", diagnostics))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Section 'actorSchedules' contains a non-object schedule profile.");
                continue;
            }
            RejectDuplicateProperties(item, "actorSchedules.profile", diagnostics);
            string id = Text(item, "id", diagnostics);
            if (!ValidId(id))
            {
                diagnostics.Add($"Actor schedule profile id '{id}' is invalid.");
                continue;
            }
            string label = OptionalText(item, "label", diagnostics) ?? id;
            string description = OptionalText(item, "description", diagnostics) ?? string.Empty;
            DaggerfallSchedulePrimaryMode primaryMode = ParseEnum(
                Text(item, "primaryMode", diagnostics),
                id,
                "primaryMode",
                diagnostics,
                static s => s switch
                {
                    "exterior-street" => DaggerfallSchedulePrimaryMode.ExteriorStreet,
                    "interior-building" => DaggerfallSchedulePrimaryMode.InteriorBuilding,
                    "always-stationed" => DaggerfallSchedulePrimaryMode.AlwaysStationed,
                    _ => (DaggerfallSchedulePrimaryMode?)null,
                },
                DaggerfallSchedulePrimaryMode.AlwaysStationed);

            bool usesBuildingOpeningHours = OptionalBool(item, "usesBuildingOpeningHours");
            int activeStartMinute = Integer(item, "activeStartMinute", diagnostics);
            int activeEndMinute = Integer(item, "activeEndMinute", diagnostics);
            int jitterMinutes = OptionalInteger(item, "jitterMinutes", diagnostics) ?? 0;
            int? eveningTavernStartMinute = OptionalInteger(item, "eveningTavernStartMinute", diagnostics);
            int? eveningTavernEndMinute = OptionalInteger(item, "eveningTavernEndMinute", diagnostics);
            int eveningTavernSharePercent = OptionalInteger(item, "eveningTavernSharePercent", diagnostics) ?? 0;
            bool sheltersInRain = OptionalBool(item, "sheltersInRain");
            bool sheltersInStorm = OptionalBool(item, "sheltersInStorm");
            int stormDelayMinMinutes = OptionalInteger(item, "stormDelayMinMinutes", diagnostics) ?? 0;
            int stormDelayMaxMinutes = OptionalInteger(item, "stormDelayMaxMinutes", diagnostics) ?? 0;
            bool underworldNightPulse = OptionalBool(item, "underworldNightPulse");

            if (activeStartMinute is < 0 or >= 1440)
                diagnostics.Add($"Actor schedule '{id}' activeStartMinute {activeStartMinute} must be in [0, 1439].");
            if (activeEndMinute is <= 0 or > 1440)
                diagnostics.Add($"Actor schedule '{id}' activeEndMinute {activeEndMinute} must be in [1, 1440].");
            if (jitterMinutes is < 0 or > 60)
                diagnostics.Add($"Actor schedule '{id}' jitterMinutes {jitterMinutes} must be in [0, 60].");
            if (eveningTavernStartMinute.HasValue != eveningTavernEndMinute.HasValue)
                diagnostics.Add($"Actor schedule '{id}' must define both eveningTavernStartMinute and eveningTavernEndMinute or neither.");
            if (eveningTavernStartMinute is < 0 or >= 1440)
                diagnostics.Add($"Actor schedule '{id}' eveningTavernStartMinute must be in [0, 1439].");
            if (eveningTavernEndMinute is <= 0 or > 1440)
                diagnostics.Add($"Actor schedule '{id}' eveningTavernEndMinute must be in [1, 1440].");
            if (eveningTavernSharePercent is < 0 or > 100)
                diagnostics.Add($"Actor schedule '{id}' eveningTavernSharePercent must be in [0, 100].");
            if (stormDelayMinMinutes < 0 || stormDelayMaxMinutes < stormDelayMinMinutes || stormDelayMaxMinutes > 60)
                diagnostics.Add($"Actor schedule '{id}' storm delay range [{stormDelayMinMinutes}, {stormDelayMaxMinutes}] is invalid.");

            DaggerfallActorScheduleProfile profile = new(
                id,
                label,
                description,
                primaryMode,
                usesBuildingOpeningHours,
                activeStartMinute,
                activeEndMinute,
                jitterMinutes,
                eveningTavernStartMinute,
                eveningTavernEndMinute,
                eveningTavernSharePercent,
                sheltersInRain,
                sheltersInStorm,
                stormDelayMinMinutes,
                stormDelayMaxMinutes,
                underworldNightPulse);
            if (!schedules.TryAdd(id, profile))
                diagnostics.Add($"Duplicate actor schedule profile '{id}'.");
        }

        return schedules;
    }

    private static DaggerfallActorSpawnerSpec? ReadSpawnerSpec(
        JsonElement item,
        string sectionName,
        IReadOnlyDictionary<string, DaggerfallActorTemplateDefinition> templates,
        IReadOnlyDictionary<string, DaggerfallActorScheduleProfile> schedules,
        IReadOnlySet<string> validTeams,
        DaggerfallFactionsSet factions,
        DaggerfallContentDiagnostics diagnostics)
    {
        string id = Text(item, "id", diagnostics);
        if (!ValidId(id))
        {
            diagnostics.Add($"Actor spawner spec id '{id}' in '{sectionName}' is invalid.");
            return null;
        }
        string templateId = Text(item, "template", diagnostics);
        if (!templates.TryGetValue(templateId, out DaggerfallActorTemplateDefinition? template))
        {
            diagnostics.Add($"Actor spawner spec '{id}' refers to unknown template '{templateId}'.");
            return null;
        }
        ActorSpawnerFamily family = ParseEnum(Text(item, "family", diagnostics), id, "family", diagnostics, static s => s switch
        {
            "player" => ActorSpawnerFamily.Player,
            "creature" => ActorSpawnerFamily.Creature,
            "enemy" => ActorSpawnerFamily.Enemy,
            "town" => ActorSpawnerFamily.Town,
            "party-hire" => ActorSpawnerFamily.PartyHire,
            "cast" => ActorSpawnerFamily.Cast,
            _ => (ActorSpawnerFamily?)null,
        }, ActorSpawnerFamily.Creature);

        bool published = !item.TryGetProperty("published", out JsonElement pubEl) || pubEl.ValueKind != JsonValueKind.False;
        string displayName = OptionalText(item, "displayName", diagnostics) ?? id;

        List<DaggerfallActorGenderWeight> genders = [];
        foreach (JsonElement gEl in Array(item, "genders", diagnostics))
        {
            JsonElement gObj = Object(gEl, $"spec '{id}' gender entry", diagnostics);
            string gText = Text(gObj, "gender", diagnostics);
            int weight = Integer(gObj, "weight", diagnostics);
            if (!ActorGenderFormat.TryParse(gText, out ActorGender parsed))
            {
                diagnostics.Add($"Actor spawner spec '{id}' names unknown gender '{gText}'.");
                continue;
            }
            if (!template.Capabilities.EffectiveAllowedGenders.Contains(parsed))
            {
                diagnostics.Add($"Actor spawner spec '{id}' uses gender '{gText}' disallowed by template '{templateId}'.");
                continue;
            }
            if (weight <= 0)
            {
                diagnostics.Add($"Actor spawner spec '{id}' gender '{gText}' weight must be positive.");
                continue;
            }
            genders.Add(new(parsed, weight));
        }
        if (genders.Count == 0)
            diagnostics.Add($"Actor spawner spec '{id}' must specify at least one valid gender entry.");

        ActorNamingMode naming = ParseEnum(OptionalText(item, "naming", diagnostics) ?? "label", id, "naming", diagnostics, static s => s switch
        {
            "label" => ActorNamingMode.Label,
            "generated" => ActorNamingMode.Generated,
            "designated" => ActorNamingMode.Designated,
            _ => (ActorNamingMode?)null,
        }, ActorNamingMode.Label);

        ActorHarmPolicyKind harm = ParseEnum(OptionalText(item, "harm", diagnostics) ?? "combatant", id, "harm", diagnostics, static s => s switch
        {
            "protected" => ActorHarmPolicyKind.Protected,
            "law-protected-civilian" => ActorHarmPolicyKind.LawProtectedCivilian,
            "combatant" => ActorHarmPolicyKind.Combatant,
            _ => (ActorHarmPolicyKind?)null,
        }, ActorHarmPolicyKind.Combatant);

        ActorInteractionPolicyKind interaction = ParseEnum(OptionalText(item, "interaction", diagnostics) ?? "none", id, "interaction", diagnostics, static s => s switch
        {
            "none" => ActorInteractionPolicyKind.None,
            "ambient-bark" => ActorInteractionPolicyKind.AmbientBark,
            "full-dialogue" => ActorInteractionPolicyKind.FullDialogue,
            _ => (ActorInteractionPolicyKind?)null,
        }, ActorInteractionPolicyKind.None);

        string? team = OptionalText(item, "team", diagnostics);
        if (team is not null && !validTeams.Contains(team))
            diagnostics.Add($"Actor spawner spec '{id}' refers to unknown team '{team}'.");

        string? race = OptionalText(item, "race", diagnostics);
        if (race is not null)
        {
            if (template.Capabilities.FixedRace is { } fixedRace
                && !StringComparer.Ordinal.Equals(race, fixedRace))
            {
                diagnostics.Add($"Actor spawner spec '{id}' specifies race '{race}' which conflicts with template '{templateId}' fixedRace '{fixedRace}'.");
            }
            else if (template.Capabilities.EffectiveAllowedRaces is { Count: > 0 } allowedRaces
                && !allowedRaces.Contains(race, StringComparer.Ordinal))
            {
                diagnostics.Add($"Actor spawner spec '{id}' specifies race '{race}' disallowed by template '{templateId}'.");
            }
        }
        string? schedule = OptionalText(item, "schedule", diagnostics);
        if (schedule is not null && !schedules.ContainsKey(schedule))
            diagnostics.Add($"Actor spawner spec '{id}' refers to unknown schedule '{schedule}'.");
        int? levelMin = null, levelMax = null;
        if (item.TryGetProperty("levelBand", out JsonElement lbEl) && lbEl.ValueKind == JsonValueKind.Object)
        {
            levelMin = OptionalInteger(lbEl, "minimum", diagnostics);
            levelMax = OptionalInteger(lbEl, "maximum", diagnostics);
        }

        int maleTexture = 0, femaleTexture = 0, corpseArchive = 0, corpseRecord = 0;
        bool hasIdle = false, noShadow = false;
        Vector3? glowColor = null;
        DaggerfallActorPresentationDefinition presentation = DaggerfallActorPresentationDefinition.None;
        if (item.TryGetProperty("appearances", out JsonElement appEl) && appEl.ValueKind == JsonValueKind.Object)
        {
            maleTexture = OptionalInteger(appEl, "maleTexture", diagnostics) ?? 0;
            femaleTexture = OptionalInteger(appEl, "femaleTexture", diagnostics) ?? 0;
            if (appEl.TryGetProperty("corpse", out JsonElement corpseEl) && corpseEl.ValueKind == JsonValueKind.Object)
            {
                corpseArchive = OptionalInteger(corpseEl, "archive", diagnostics) ?? 0;
                corpseRecord = OptionalInteger(corpseEl, "record", diagnostics) ?? 0;
            }
            hasIdle = OptionalBool(appEl, "hasIdle");
            noShadow = OptionalBool(appEl, "noShadow");
            if (appEl.TryGetProperty("glowColor", out JsonElement glowEl) && glowEl.ValueKind == JsonValueKind.Object)
            {
                glowColor = new Vector3(Number(glowEl, "red", diagnostics), Number(glowEl, "green", diagnostics), Number(glowEl, "blue", diagnostics));
                if (glowColor.Value.X < 0 || glowColor.Value.Y < 0 || glowColor.Value.Z < 0)
                    diagnostics.Add($"Actor spawner spec '{id}' requires nonnegative glow channels.");
            }
            presentation = ReadActorPresentation(appEl, new DaggerfallActorId(id), diagnostics);
        }

        string moveSound = "", barkSound = "", attackSound = "";
        bool parrySounds = false;
        if (item.TryGetProperty("sounds", out JsonElement soundEl) && soundEl.ValueKind == JsonValueKind.Object)
        {
            moveSound = OptionalText(soundEl, "move") ?? "";
            barkSound = OptionalText(soundEl, "bark") ?? "";
            attackSound = OptionalText(soundEl, "attack") ?? "";
            parrySounds = OptionalBool(soundEl, "parrySounds");
        }

        DaggerfallActorClassicBinding? binding = null;
        if (item.TryGetProperty("classicBinding", out JsonElement cbEl) && cbEl.ValueKind == JsonValueKind.Object)
        {
            int mobileId = Integer(cbEl, "mobileId", diagnostics);
            int soulPoints = OptionalInteger(cbEl, "soulPoints", diagnostics) ?? 0;
            if (soulPoints < 0)
                diagnostics.Add($"Actor spawner spec '{id}' requires nonnegative soulPoints.");
            DaggerfallAttackRange? donorDamage = cbEl.TryGetProperty("donorDamage", out JsonElement ddEl) && ddEl.ValueKind == JsonValueKind.Object
                ? new DaggerfallAttackRange(Integer(ddEl, "minimum", diagnostics), Integer(ddEl, "maximum", diagnostics)) : null;
            DaggerfallVitalRange? donorHealth = cbEl.TryGetProperty("donorHealth", out JsonElement dhEl) && dhEl.ValueKind == JsonValueKind.Object
                ? new DaggerfallVitalRange(Integer(dhEl, "minimum", diagnostics), Integer(dhEl, "maximum", diagnostics)) : null;
            binding = new DaggerfallActorClassicBinding(
                mobileId,
                OptionalText(cbEl, "donorName") ?? displayName,
                OptionalText(cbEl, "identity") ?? id,
                OptionalText(cbEl, "affinity") ?? "",
                OptionalText(cbEl, "behaviour") ?? "General",
                soulPoints,
                OptionalInteger(cbEl, "mapChance", diagnostics) ?? 0,
                OptionalText(cbEl, "donorTeam") ?? "",
                OptionalText(cbEl, "donorMinMetalToHit") ?? "",
                OptionalInteger(cbEl, "donorArmorValue", diagnostics) ?? 0,
                donorDamage,
                donorHealth,
                OptionalInteger(cbEl, "donorLevel", diagnostics) ?? 0,
                OptionalInteger(cbEl, "donorWeight", diagnostics) ?? 0,
                cbEl.TryGetProperty("donorLootTableKey", out JsonElement dlEl) && dlEl.ValueKind == JsonValueKind.String ? dlEl.GetString() : null);
        }

        string? role = null;
        bool deriveRegionalFaction = false;
        string? visualMode = null;
        IReadOnlyList<DaggerfallPeoplePoolEntry>? defaultPool = null;
        IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>? regionalPools = null;
        IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>? climatePools = null;
        int? factionId = null;
        int? visualArchive = null;
        int? visualRecord = null;
        string? visualAssetId = null;
        string? homeBlock = null;
        string? homeSlotId = null;
        DaggerfallSiteId? homeSite = null;
        WorldPoint? homePosition = null;
        string? familyId = null;
        ActorSocialGrouping socialGrouping = ActorSocialGrouping.None;
        DaggerfallPartyHireTerms? hireTerms = null;

        if (family == ActorSpawnerFamily.Town)
        {
            role = Text(item, "role", diagnostics);
            deriveRegionalFaction = OptionalBool(item, "deriveRegionalFaction");
            if (OptionalText(item, "socialGrouping", diagnostics) is { } groupingText)
            {
                if (ActorSocialGroupingFormat.TryParse(groupingText, out ActorSocialGrouping parsedGrouping))
                    socialGrouping = parsedGrouping;
                else
                    diagnostics.Add($"Town spawner spec '{id}' has unknown socialGrouping '{groupingText}'.");
            }
            visualMode = Text(item, "visualMode", diagnostics);
            if (visualMode is not ("classic-slot" or "pool"))
                diagnostics.Add($"Town spawner spec '{id}' has invalid visualMode '{visualMode}'.");
            defaultPool = ReadSpawnerPool(Array(item, "defaultPool", diagnostics), $"spec '{id}' defaultPool", diagnostics);

            Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> regMap = new(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("regionalPools", out JsonElement regEl) && regEl.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in regEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Array)
                        diagnostics.Add($"Town spawner spec '{id}' regionalPools.'{prop.Name}' must be an array.");
                    else
                        regMap[prop.Name] = ReadSpawnerPool(prop.Value.EnumerateArray(), $"spec '{id}' regionalPools.{prop.Name}", diagnostics);
                }
            }
            regionalPools = new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(regMap);

            Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> climMap = new(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("climatePools", out JsonElement climEl) && climEl.ValueKind != JsonValueKind.Null)
            {
                if (climEl.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add($"Town spawner spec '{id}' climatePools must be an object.");
                }
                else
                {
                    foreach (JsonProperty prop in climEl.EnumerateObject())
                    {
                        if (!DaggerfallPeopleContent.ValidClimateNames.Contains(prop.Name))
                            diagnostics.Add($"Town spawner spec '{id}' climatePools references unknown climate '{prop.Name}'.");
                        if (prop.Value.ValueKind != JsonValueKind.Array)
                            diagnostics.Add($"Town spawner spec '{id}' climatePools.'{prop.Name}' must be an array.");
                        else
                        {
                            IReadOnlyList<DaggerfallPeoplePoolEntry> pool = ReadSpawnerPool(prop.Value.EnumerateArray(), $"spec '{id}' climatePools.{prop.Name}", diagnostics);
                            if (pool.Count == 0)
                                diagnostics.Add($"Town spawner spec '{id}' climatePools.'{prop.Name}' must contain at least one entry.");
                            else
                                climMap[prop.Name] = pool;
                        }
                    }
                }
            }
            climatePools = new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(climMap);
        }
        else if (family == ActorSpawnerFamily.Cast)
        {
            foreach (JsonProperty prop in item.EnumerateObject())
            {
                if (ForbiddenCastMechanicFields.Contains(prop.Name))
                    diagnostics.Add($"Cast spawner spec '{id}' may not patch mechanic field '{prop.Name}'; give it a child template instead.");
            }
            if (genders.Count != 1)
                diagnostics.Add($"Cast spawner spec '{id}' must pin exactly one gender.");
            factionId = Integer(item, "factionId", diagnostics);
            if (factionId <= 0)
                diagnostics.Add($"Cast spawner spec '{id}' requires a positive factionId.");
            else if (factions.Factions.Count > 0)
            {
                if (!factions.Factions.TryGetValue(factionId.Value, out DaggerfallFactionDefinition? faction))
                    diagnostics.Add($"Cast spawner spec '{id}' references missing faction {factionId.Value}.");
                else if (faction.Type != IndividualFactionType)
                    diagnostics.Add($"Cast spawner spec '{id}' references faction {factionId.Value} ('{faction.Name}') of type {faction.Type}, expected Individual (4).");
            }
            JsonElement vis = Object(Property(item, "visual", diagnostics), $"cast spec '{id}' visual", diagnostics);
            visualArchive = Integer(vis, "archive", diagnostics);
            visualRecord = Integer(vis, "record", diagnostics);
            if (visualArchive < 0 || visualRecord < 0)
                diagnostics.Add($"Cast spawner spec '{id}' has invalid visual ({visualArchive}.{visualRecord}).");
            visualAssetId = OptionalText(vis, "assetId", diagnostics);
            if (visualAssetId is not null && !ValidId(visualAssetId))
                diagnostics.Add($"Cast spawner spec '{id}' has invalid visual.assetId '{visualAssetId}'.");

            if (item.TryGetProperty("home", out JsonElement homeEl) && homeEl.ValueKind != JsonValueKind.Null)
            {
                JsonElement homeObj = Object(homeEl, $"cast spec '{id}' home", diagnostics);
                homeBlock = OptionalText(homeObj, "block", diagnostics);
                homeSlotId = OptionalText(homeObj, "slotId", diagnostics);
                if (homeObj.TryGetProperty("site", out JsonElement siteEl) && siteEl.ValueKind != JsonValueKind.Null)
                {
                    JsonElement siteObj = Object(siteEl, $"cast spec '{id}' home.site", diagnostics);
                    homeSite = new(Integer(siteObj, "region", diagnostics), Integer(siteObj, "index", diagnostics));
                }
                if (homeObj.TryGetProperty("position", out JsonElement posEl) && posEl.ValueKind != JsonValueKind.Null)
                {
                    if (posEl.ValueKind != JsonValueKind.Array || posEl.GetArrayLength() != 3)
                        diagnostics.Add($"Cast spawner spec '{id}' home.position must be a 3-element array.");
                    else
                    {
                        float x = posEl[0].GetSingle(), y = posEl[1].GetSingle(), z = posEl[2].GetSingle();
                        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                            diagnostics.Add($"Cast spawner spec '{id}' home.position has a non-finite coordinate.");
                        else
                            homePosition = new(x, y, z);
                    }
                }
                if (homeBlock is null ? homeSlotId is not null || homePosition is not null || homeSite is not null : (homeSlotId is null) == (homePosition is null))
                    diagnostics.Add($"Cast spawner spec '{id}' must name home.block with exactly one of home.slotId and home.position, and home.site only with home.block.");
                if (homePosition is not null && homeSite is null)
                    diagnostics.Add($"Cast spawner spec '{id}' has a home.position but no home.site to set it in.");
            }
            familyId = OptionalText(item, "familyId", diagnostics);
            if (familyId is not null && !DaggerfallHouseholds.IsValidFamilyId(familyId))
                diagnostics.Add($"Cast spawner spec '{id}' has invalid familyId '{familyId}'.");
        }
        else if (family == ActorSpawnerFamily.PartyHire)
        {
            role = OptionalText(item, "role", diagnostics) ?? "road-guard";
            if (harm != ActorHarmPolicyKind.Combatant)
                diagnostics.Add($"PartyHire spawner spec '{id}' must declare harm='combatant'.");
            if (!string.Equals(team, "player-ally", StringComparison.Ordinal))
                diagnostics.Add($"PartyHire spawner spec '{id}' must declare team='player-ally'.");
            visualMode = OptionalText(item, "visualMode", diagnostics) ?? "pool";
            if (visualMode is not ("classic-slot" or "pool"))
                diagnostics.Add($"PartyHire spawner spec '{id}' has invalid visualMode '{visualMode}'.");
            if (item.TryGetProperty("defaultPool", out JsonElement poolEl) && poolEl.ValueKind == JsonValueKind.Array)
                defaultPool = ReadSpawnerPool(poolEl.EnumerateArray(), $"spec '{id}' defaultPool", diagnostics);
            else if (visualMode == "pool")
                diagnostics.Add($"PartyHire spawner spec '{id}' with visualMode='pool' requires a non-empty defaultPool.");

            JsonElement termsObj = Object(Property(item, "hireTerms", diagnostics), $"party-hire spec '{id}' hireTerms", diagnostics);
            if (termsObj.ValueKind == JsonValueKind.Object)
            {
                string venue = Text(termsObj, "venue", diagnostics);
                if (!ValidPartyHireVenues.Contains(venue))
                    diagnostics.Add($"PartyHire spawner spec '{id}' has unknown hireTerms.venue '{venue}'.");
                int? guildFactionId = OptionalInteger(termsObj, "guildFactionId", diagnostics);
                if (guildFactionId is int gf)
                {
                    if (gf <= 0)
                        diagnostics.Add($"PartyHire spawner spec '{id}' hireTerms.guildFactionId must be positive.");
                    else if (factions.Factions.Count > 0 && !factions.Factions.ContainsKey(gf))
                        diagnostics.Add($"PartyHire spawner spec '{id}' hireTerms.guildFactionId references missing faction {gf}.");
                }
                int baseDeposit = Integer(termsObj, "baseDeposit", diagnostics);
                int baseDailyWage = Integer(termsObj, "baseDailyWage", diagnostics);
                if (baseDeposit < 0)
                    diagnostics.Add($"PartyHire spawner spec '{id}' hireTerms.baseDeposit cannot be negative.");
                if (baseDailyWage <= 0)
                    diagnostics.Add($"PartyHire spawner spec '{id}' hireTerms.baseDailyWage must be positive.");
                int? minGuildRank = OptionalInteger(termsObj, "minimumGuildRank", diagnostics);
                if (minGuildRank is < 0)
                    diagnostics.Add($"PartyHire spawner spec '{id}' hireTerms.minimumGuildRank cannot be negative.");
                bool requiresVouch = OptionalBool(termsObj, "requiresVouch");
                bool canDelveDungeons = OptionalBool(termsObj, "canDelveDungeons");
                hireTerms = new DaggerfallPartyHireTerms(
                    venue, guildFactionId, baseDeposit, baseDailyWage, minGuildRank, requiresVouch, canDelveDungeons);
            }
        }

        return new DaggerfallActorSpawnerSpec(
            id,
            family,
            templateId,
            published,
            displayName,
            genders.AsReadOnly(),
            race,
            naming,
            harm,
            interaction,
            team,
            schedule,
            levelMin,
            levelMax,
            maleTexture,
            femaleTexture,
            corpseArchive,
            corpseRecord,
            hasIdle,
            noShadow,
            glowColor,
            presentation,
            moveSound,
            barkSound,
            attackSound,
            parrySounds,
            binding,
            role,
            deriveRegionalFaction,
            visualMode,
            defaultPool,
            regionalPools,
            climatePools,
            factionId,
            visualArchive,
            visualRecord,
            homeBlock,
            homeSlotId,
            homeSite,
            homePosition,
            familyId,
            socialGrouping,
            visualAssetId,
            hireTerms,
            ReadActorLook(item, $"Actor spawner spec '{id}'", diagnostics) ?? template.Look);
    }

    private static IReadOnlyList<DaggerfallPeoplePoolEntry> ReadSpawnerPool(
        IEnumerable<JsonElement> array, string context, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallPeoplePoolEntry> entries = [];
        foreach (JsonElement item in array)
        {
            JsonElement obj = Object(item, context, diagnostics);
            int archive = Integer(obj, "archive", diagnostics);
            int record = Integer(obj, "record", diagnostics);
            string gender = Text(obj, "gender", diagnostics);
            int weight = Integer(obj, "weight", diagnostics);
            if (archive < 0 || record < 0 || weight <= 0 || gender is not ("Male" or "Female"))
                diagnostics.Add($"{context} has an invalid pool entry ({archive}.{record}, gender='{gender}', weight={weight}).");
            else
                entries.Add(new(archive, record, gender, weight));
        }
        return entries.AsReadOnly();
    }

    private static bool OptionalBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement val) && val.ValueKind == JsonValueKind.True;

    private static TEnum ParseEnum<TEnum>(string text, string owner, string field, DaggerfallContentDiagnostics diagnostics, Func<string, TEnum?> parse, TEnum fallback) where TEnum : struct
    {
        if (parse(text) is { } parsed) return parsed;
        diagnostics.Add($"Actor '{owner}' has unknown {field} value '{text}'.");
        return fallback;
    }
}
