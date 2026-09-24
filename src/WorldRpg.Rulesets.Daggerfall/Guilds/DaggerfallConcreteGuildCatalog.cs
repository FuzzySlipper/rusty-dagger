using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>The source families retained by Daggerfall's guild services.</summary>
internal enum DaggerfallConcreteGuildKind
{
    Fighters,
    Mages,
    Thieves,
    DarkBrotherhood,
    Temple,
    KnightlyOrder,
}

/// <summary>The canonical membership shape used by a concrete guild definition.</summary>
internal enum DaggerfallGuildMembershipKind
{
    Standalone,
    TempleDeity,
    KnightlyOrder,
}

/// <summary>
/// The crime evidence projection consumed by guild admission. It deliberately contains no tally;
/// #8054 owns incident accumulation and supplies these source-backed qualification facts.
/// </summary>
internal readonly record struct DaggerfallGuildCrimeInvitationEvidence(
    bool ThievingCrimeRequirementSatisfied,
    bool MurderCrimeRequirementSatisfied)
{
    internal bool Satisfies(DaggerfallGuildInvitationRequirement requirement) => requirement switch
    {
        DaggerfallGuildInvitationRequirement.None => true,
        DaggerfallGuildInvitationRequirement.ThievingCrime => ThievingCrimeRequirementSatisfied,
        DaggerfallGuildInvitationRequirement.MurderCrime => MurderCrimeRequirementSatisfied,
        _ => false,
    };
}

/// <summary>Which source-backed invitation fact a concrete guild requires.</summary>
internal enum DaggerfallGuildInvitationRequirement
{
    None,
    ThievingCrime,
    MurderCrime,
}

/// <summary>Concrete service identities retained from the donor guild service table.</summary>
internal enum DaggerfallConcreteGuildService
{
    Training,
    Quests,
    Repair,
    Identify,
    Donate,
    CureDisease,
    BuyPotions,
    MakePotions,
    BuySpells,
    MakeSpells,
    BuyMagicItems,
    MakeMagicItems,
    SellMagicItems,
    Teleport,
    DaedraSummoning,
    Spymaster,
    BuySoulgems,
    ReceiveArmor,
    ReceiveHouse,
    Rest,
    HallAccess,
    FreeMagickaRecharge,
    Library,
    FreeHealing,
    FreeTavernRooms,
    FreeShip,
    Blessing,
    RewardMultiplier,
    RepairCost,
}

/// <summary>A source service gate and its optional donor NPC provider identity.</summary>
internal sealed record DaggerfallConcreteGuildServiceDefinition(
    DaggerfallConcreteGuildService Service,
    int? MinimumRank,
    int? ProviderFactionId,
    bool RequiresMembership,
    bool SourceImplemented = true);

/// <summary>
/// One authored guild or order definition. This is a policy catalog, not another membership store;
/// callers project canonical social state into the service context before evaluating a gate.
/// </summary>
internal sealed class DaggerfallConcreteGuildDefinition
{
    internal DaggerfallConcreteGuildDefinition(
        string key,
        string displayName,
        DaggerfallConcreteGuildKind kind,
        DaggerfallGuildMembershipKind membershipKind,
        int factionId,
        int guildGroup,
        int parentFactionId,
        int region,
        IEnumerable<string> guildSkills,
        int? trainingProviderFactionId,
        DaggerfallGuildInvitationRequirement invitationRequirement,
        bool neverExpels,
        IEnumerable<DaggerfallConcreteGuildServiceDefinition> services,
        IReadOnlyList<DaggerfallGuildRankRequirement>? rankRequirements = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "A guild faction identity is positive.");
        if (guildGroup < -1) throw new ArgumentOutOfRangeException(nameof(guildGroup), guildGroup, "A source guild group is -1 or positive.");
        if (parentFactionId < 0) throw new ArgumentOutOfRangeException(nameof(parentFactionId), parentFactionId, "A source faction parent is non-negative.");
        if (region < -1) throw new ArgumentOutOfRangeException(nameof(region), region, "A source faction region is -1 or positive.");
        ArgumentNullException.ThrowIfNull(guildSkills);
        ArgumentNullException.ThrowIfNull(services);

        string[] skills = guildSkills.ToArray();
        if (skills.Length == 0 || skills.Any(string.IsNullOrWhiteSpace)
            || skills.Distinct(StringComparer.Ordinal).Count() != skills.Length)
            throw new ArgumentException("A concrete guild must publish distinct non-empty guild skills.", nameof(guildSkills));

        DaggerfallConcreteGuildServiceDefinition[] serviceValues = services.ToArray();
        if (serviceValues.Length == 0
            || serviceValues.Select(service => service.Service).Distinct().Count() != serviceValues.Length)
            throw new ArgumentException("A concrete guild must publish distinct service identities.", nameof(services));
        foreach (DaggerfallConcreteGuildServiceDefinition service in serviceValues)
        {
            if (service.MinimumRank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
                throw new ArgumentOutOfRangeException(nameof(services), service.MinimumRank, "A concrete service rank is outside the Daggerfall range.");
            if (service.ProviderFactionId is <= 0)
                throw new ArgumentOutOfRangeException(nameof(services), service.ProviderFactionId, "A service provider faction identity is positive.");
        }

        Key = key;
        DisplayName = displayName;
        Kind = kind;
        MembershipKind = membershipKind;
        FactionId = factionId;
        GuildGroup = guildGroup;
        ParentFactionId = parentFactionId;
        Region = region;
        GuildSkills = Array.AsReadOnly(skills);
        TrainingProviderFactionId = trainingProviderFactionId;
        TrainingSkills = trainingProviderFactionId is int provider
            ? ReadTrainingSkills(provider, factionId, parentFactionId)
            : Array.Empty<string>();
        TrainingProviderMembershipFactionId = trainingProviderFactionId is int providerFaction
            ? ReadTrainingMembershipFaction(providerFaction)
            : null;
        InvitationRequirement = invitationRequirement;
        NeverExpels = neverExpels;
        Services = Array.AsReadOnly(serviceValues);
        RankRequirements = Array.AsReadOnly((rankRequirements ?? DaggerfallGuildMembershipPolicy.ClassicRankRequirements).ToArray());
        if (RankRequirements.Count != DaggerfallSocialState.MaximumGuildRank + 1
            || !RankRequirements.Select(requirement => requirement.Rank).SequenceEqual(Enumerable.Range(0, RankRequirements.Count)))
            throw new ArgumentException("A concrete guild must publish all ten rank requirements.", nameof(rankRequirements));
        foreach (DaggerfallGuildRankRequirement requirement in RankRequirements) requirement.Validate();
    }

    internal string Key { get; }
    internal string DisplayName { get; }
    internal DaggerfallConcreteGuildKind Kind { get; }
    internal DaggerfallGuildMembershipKind MembershipKind { get; }
    internal int FactionId { get; }
    internal int GuildGroup { get; }
    internal int ParentFactionId { get; }
    internal int Region { get; }
    internal IReadOnlyList<string> GuildSkills { get; }
    internal int? TrainingProviderFactionId { get; }
    /// <summary>The concrete membership identity published by the shared training provider map.</summary>
    internal int? TrainingProviderMembershipFactionId { get; }
    internal IReadOnlyList<string> TrainingSkills { get; }
    internal DaggerfallGuildInvitationRequirement InvitationRequirement { get; }
    internal bool NeverExpels { get; }
    internal IReadOnlyList<DaggerfallConcreteGuildServiceDefinition> Services { get; }
    internal IReadOnlyList<DaggerfallGuildRankRequirement> RankRequirements { get; }

    internal bool TryGetService(DaggerfallConcreteGuildService service, out DaggerfallConcreteGuildServiceDefinition definition)
    {
        definition = Services.FirstOrDefault(candidate => candidate.Service == service)!;
        return definition is not null;
    }

    internal DaggerfallGuildPolicyDefinition ToMembershipPolicyDefinition() =>
        new(FactionId, GuildSkills, NeverExpels, rankRequirements: RankRequirements,
            reputationFactionId: MembershipKind == DaggerfallGuildMembershipKind.TempleDeity ? ParentFactionId : FactionId);

    private static IReadOnlyList<string> ReadTrainingSkills(int providerFactionId, int membershipFactionId, int parentFactionId)
    {
        if (!DaggerfallSkillTrainingPolicy.TryGetProvider(providerFactionId, out DaggerfallSkillTrainingProviderPolicy provider))
            throw new ArgumentException($"No source training provider {providerFactionId} is configured.", nameof(providerFactionId));
        // Standalone providers point at their membership faction. Temple providers point at the
        // authored group-17 membership faction; the deity parent remains relationship metadata.
        if (provider.MembershipFactionId != membershipFactionId && provider.MembershipFactionId != parentFactionId)
            throw new ArgumentException($"Training provider {providerFactionId} belongs to faction {provider.MembershipFactionId}, not {membershipFactionId} or its parent {parentFactionId}.", nameof(providerFactionId));
        return provider.Skills;
    }

    private static int ReadTrainingMembershipFaction(int providerFactionId) =>
        DaggerfallSkillTrainingPolicy.TryGetProvider(providerFactionId, out DaggerfallSkillTrainingProviderPolicy provider)
            ? provider.MembershipFactionId
            : throw new ArgumentException($"No source training provider {providerFactionId} is configured.", nameof(providerFactionId));
}

/// <summary>
/// Source-backed concrete guild catalog. The standalone policies can be projected into the existing
/// membership policy; temple definitions use the authored group-17 membership faction and retain
/// the deity root separately for relationship and provider mapping.
/// </summary>
internal static class DaggerfallConcreteGuildCatalog
{
    internal const int FightersFactionId = 41;
    internal const int MagesFactionId = 40;
    internal const int ThievesFactionId = 42;
    internal const int DarkBrotherhoodFactionId = 108;

    internal const int ArkayFactionId = 21;
    internal const int ZenitharFactionId = 22;
    internal const int MaraFactionId = 24;
    internal const int AkatoshFactionId = 26;
    internal const int JulianosFactionId = 27;
    internal const int DibellaFactionId = 29;
    internal const int StendarrFactionId = 33;
    internal const int KynarethFactionId = 35;

    // Group-17 temple membership variants. The deity roots above remain their authored social
    // parents and are also the identities used by the current trainer policy map.
    internal const int ArkayTempleFactionId = 82;
    internal const int ZenitharTempleFactionId = 84;
    internal const int MaraTempleFactionId = 88;
    internal const int AkatoshTempleFactionId = 92;
    internal const int JulianosTempleFactionId = 94;
    internal const int DibellaTempleFactionId = 98;
    internal const int StendarrTempleFactionId = 106;
    internal const int KynarethTempleFactionId = 36;

    private static readonly IReadOnlyList<DaggerfallConcreteGuildDefinition> Definitions =
        Array.AsReadOnly(BuildDefinitions().ToArray());

    private static readonly IReadOnlyDictionary<int, DaggerfallConcreteGuildDefinition> ByFaction =
        Definitions.ToDictionary(definition => definition.FactionId);

    internal static IReadOnlyList<DaggerfallConcreteGuildDefinition> All => Definitions;

    /// <summary>
    /// Every retained concrete variant projected to the shared membership policy. Temple deity
    /// entries key membership by the authored group-17 temple faction while retaining their deity
    /// root in <see cref="DaggerfallConcreteGuildDefinition.ParentFactionId"/>.
    /// </summary>
    internal static IReadOnlyList<DaggerfallGuildPolicyDefinition> AllMembershipPolicies { get; } =
        Array.AsReadOnly(Definitions.Select(definition => definition.ToMembershipPolicyDefinition()).ToArray());

    internal static IReadOnlyList<DaggerfallGuildPolicyDefinition> StandaloneMembershipPolicies { get; } =
        Array.AsReadOnly(Definitions
            .Where(definition => definition.MembershipKind == DaggerfallGuildMembershipKind.Standalone)
            .Select(definition => definition.ToMembershipPolicyDefinition())
            .ToArray());

    internal static bool TryGet(int factionId, out DaggerfallConcreteGuildDefinition definition) =>
        ByFaction.TryGetValue(factionId, out definition!);

    internal static DaggerfallConcreteGuildDefinition ForFaction(int factionId) =>
        TryGet(factionId, out DaggerfallConcreteGuildDefinition? definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "No concrete Daggerfall guild definition names this faction.");

    private static IEnumerable<DaggerfallConcreteGuildDefinition> BuildDefinitions()
    {
        yield return Standalone(
            "fighters-guild", "The Fighters Guild", DaggerfallConcreteGuildKind.Fighters, FightersFactionId, 11,
            ["archery", "axe", "blunt-weapon", "giantish", "long-blade", "orcish", "short-blade"],
            849,
            [
                Service(DaggerfallConcreteGuildService.Training, 0, 849),
                Service(DaggerfallConcreteGuildService.Quests, null, 851, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.Repair, 0, 850),
                Service(DaggerfallConcreteGuildService.Rest, 0),
                Service(DaggerfallConcreteGuildService.HallAccess, 6),
                Service(DaggerfallConcreteGuildService.RewardMultiplier, 0),
                Service(DaggerfallConcreteGuildService.RepairCost, 0),
            ]);

        yield return Standalone(
            "mages-guild", "The Mages Guild", DaggerfallConcreteGuildKind.Mages, MagesFactionId, 10,
            ["alteration", "destruction", "illusion", "mysticism", "restoration", "thaumaturgy"],
            61,
            [
                Service(DaggerfallConcreteGuildService.Training, 0, 61),
                Service(DaggerfallConcreteGuildService.Quests, null, 63, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.Identify, null, 801, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.BuySpells, null, 60, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.MakeSpells, 0, 64),
                Service(DaggerfallConcreteGuildService.BuyMagicItems, 3, 65),
                Service(DaggerfallConcreteGuildService.MakeMagicItems, 5, 802),
                Service(DaggerfallConcreteGuildService.Teleport, 8, 62),
                Service(DaggerfallConcreteGuildService.DaedraSummoning, 6, 66),
                Service(DaggerfallConcreteGuildService.Library, 2),
                Service(DaggerfallConcreteGuildService.HallAccess, 6),
                Service(DaggerfallConcreteGuildService.FreeMagickaRecharge, 0),
            ]);

        yield return Standalone(
            "thieves-guild", "The Thieves Guild", DaggerfallConcreteGuildKind.Thieves, ThievesFactionId, 4,
            ["backstabbing", "climbing", "lockpicking", "pickpocket", "short-blade", "stealth", "streetwise"],
            803,
            [
                Service(DaggerfallConcreteGuildService.Training, 0, 803),
                Service(DaggerfallConcreteGuildService.Quests, null, 804, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.SellMagicItems, 2, 805),
                Service(DaggerfallConcreteGuildService.Spymaster, 4, 806),
                Service(DaggerfallConcreteGuildService.HallAccess, 0),
            ], DaggerfallGuildInvitationRequirement.ThievingCrime, neverExpels: true);

        yield return Standalone(
            "dark-brotherhood", "The Dark Brotherhood", DaggerfallConcreteGuildKind.DarkBrotherhood, DarkBrotherhoodFactionId, 3,
            ["archery", "backstabbing", "climbing", "critical-strike", "daedric", "destruction", "short-blade", "stealth", "streetwise"],
            839,
            [
                Service(DaggerfallConcreteGuildService.Training, 0, 839),
                Service(DaggerfallConcreteGuildService.Quests, null, 807, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.BuyPotions, 1, 841),
                Service(DaggerfallConcreteGuildService.MakePotions, 3, 840),
                Service(DaggerfallConcreteGuildService.BuySoulgems, 5, 843),
                Service(DaggerfallConcreteGuildService.Spymaster, 7, 842),
                Service(DaggerfallConcreteGuildService.HallAccess, 0),
            ], DaggerfallGuildInvitationRequirement.MurderCrime, neverExpels: true);

        foreach (DaggerfallConcreteGuildDefinition temple in BuildTemples()) yield return temple;
        foreach (DaggerfallConcreteGuildDefinition order in BuildKnightlyOrders()) yield return order;
    }

    private static IEnumerable<DaggerfallConcreteGuildDefinition> BuildTemples()
    {
        yield return Temple("temple-arkay", "The Order of Arkay", ArkayTempleFactionId, ArkayFactionId, 241, 3, 0, 1, 4, -1, -1, -1, -1, 4, 7,
            buyPotionsProvider: 453, makePotionsProvider: 454, summoningProvider: 456, soulGemsProvider: 455);
        yield return Temple("temple-zenithar", "The Resolution of Z'en", ZenitharTempleFactionId, ZenitharFactionId, 243, 4, 1, 1, 6, -1, -1, -1, -1, -1, 8,
            buyPotionsProvider: 462, makePotionsProvider: 463, summoningProvider: 464);
        yield return Temple("temple-mara", "The Benevolence of Mara", MaraTempleFactionId, MaraFactionId, 245, 4, 1, 2, 5, -1, -1, -1, -1, -1, 7,
            buyPotionsProvider: 468, makePotionsProvider: 469, summoningProvider: 470);
        yield return Temple("temple-akatosh", "The Akatosh Chantry", AkatoshTempleFactionId, AkatoshFactionId, 247, 2, 1, 4, 5, -1, -1, -1, -1, -1, 7,
            buyPotionsProvider: 473, makePotionsProvider: 474, summoningProvider: 475);
        yield return Temple("temple-julianos", "The School of Julianos", JulianosTempleFactionId, JulianosFactionId, 249, 0, 2, -1, -1, 3, 5, -1, -1, -1, 6,
            buyMagicProvider: 480, makeMagicProvider: 481, summoningProvider: 482);
        yield return Temple("temple-dibella", "The House of Dibella", DibellaTempleFactionId, DibellaFactionId, 250, 4, 2, 1, 5, -1, -1, -1, -1, -1, 7,
            buyPotionsProvider: 485, makePotionsProvider: 487, summoningProvider: 488);
        yield return Temple("temple-stendarr", "The Temple of Stendarr", StendarrTempleFactionId, StendarrFactionId, 252, 4, 0, 2, 5, -1, -1, -1, -1, -1, 7,
            buyPotionsProvider: 490, makePotionsProvider: 491, summoningProvider: 492);
        yield return Temple("temple-kynareth", "The Temple of Kynareth", KynarethTempleFactionId, KynarethFactionId, 254, 4, 1, -1, -1, -1, -1, 3, 6, -1, 7,
            buySpellsProvider: 496, makeSpellsProvider: 497, summoningProvider: 498);
    }

    private static IEnumerable<DaggerfallConcreteGuildDefinition> BuildKnightlyOrders()
    {
        string[] skills = ["archery", "critical-strike", "dragonish", "etiquette", "giantish", "long-blade", "medical"];
        yield return Knightly("order-dragon", "Knights of the Dragon", 368, 367, 17, skills);
        yield return Knightly("order-owl", "Knights of the Owl", 413, 202, 18, skills);
        yield return Knightly("order-candle", "Order of the Candle", 408, 386, 20, skills);
        yield return Knightly("order-flame", "Knights of the Flame", 410, 400, 21, skills);
        yield return Knightly("order-horn", "Host of the Horn", 411, 379, 22, skills);
        yield return Knightly("order-rose", "Knights of the Rose", 409, 397, 23, skills);
        yield return Knightly("order-wheel", "Knights of the Wheel", 415, 219, 43, skills);
        yield return Knightly("order-raven", "Order of the Raven", 414, 200, 5, skills);
        yield return Knightly("order-scarab", "Order of the Scarab", 416, 227, 51, skills);
        yield return Knightly("order-hawk", "Knights of the Hawk", 417, 230, 54, skills);
    }

    private static DaggerfallConcreteGuildDefinition Standalone(
        string key,
        string name,
        DaggerfallConcreteGuildKind kind,
        int factionId,
        int guildGroup,
        IEnumerable<string> guildSkills,
        int trainingProviderFactionId,
        IEnumerable<DaggerfallConcreteGuildServiceDefinition> services,
        DaggerfallGuildInvitationRequirement invitationRequirement = DaggerfallGuildInvitationRequirement.None,
        bool neverExpels = false) =>
        new(key, name, kind, DaggerfallGuildMembershipKind.Standalone, factionId, guildGroup, 0, -1,
            guildSkills, trainingProviderFactionId, invitationRequirement, neverExpels, services);

    private static DaggerfallConcreteGuildDefinition Temple(
        string key,
        string name,
        int factionId,
        int deityFactionId,
        int trainingProvider,
        int libraryRank,
        int healingRank,
        int buyPotionsRank,
        int makePotionsRank,
        int buyMagicRank,
        int makeMagicRank,
        int buySpellsRank,
        int makeSpellsRank,
        int soulGemsRank,
        int summoningRank,
        int? buyPotionsProvider = null,
        int? makePotionsProvider = null,
        int? buyMagicProvider = null,
        int? makeMagicProvider = null,
        int? buySpellsProvider = null,
        int? makeSpellsProvider = null,
        int? summoningProvider = null,
        int? soulGemsProvider = null)
    {
        List<DaggerfallConcreteGuildServiceDefinition> services =
        [
            // Temple trainers are explicitly usable by non-members in the donor.
            Service(DaggerfallConcreteGuildService.Training, null, trainingProvider, requiresMembership: false),
            Service(DaggerfallConcreteGuildService.Quests, null, 240, requiresMembership: false),
            Service(DaggerfallConcreteGuildService.Donate, null, 810, requiresMembership: false),
            Service(DaggerfallConcreteGuildService.CureDisease, null, 813, requiresMembership: false),
            Service(DaggerfallConcreteGuildService.Blessing, null, null, requiresMembership: true, sourceImplemented: false),
        ];
        AddRanked(services, DaggerfallConcreteGuildService.Library, libraryRank, null);
        AddRanked(services, DaggerfallConcreteGuildService.FreeHealing, healingRank, null);
        AddRanked(services, DaggerfallConcreteGuildService.BuyPotions, buyPotionsRank, buyPotionsProvider);
        AddRanked(services, DaggerfallConcreteGuildService.MakePotions, makePotionsRank, makePotionsProvider);
        AddRanked(services, DaggerfallConcreteGuildService.BuyMagicItems, buyMagicRank, buyMagicProvider);
        AddRanked(services, DaggerfallConcreteGuildService.MakeMagicItems, makeMagicRank, makeMagicProvider);
        AddRanked(services, DaggerfallConcreteGuildService.BuySpells, buySpellsRank, buySpellsProvider);
        AddRanked(services, DaggerfallConcreteGuildService.MakeSpells, makeSpellsRank, makeSpellsProvider);
        AddRanked(services, DaggerfallConcreteGuildService.BuySoulgems, soulGemsRank, soulGemsProvider);
        AddRanked(services, DaggerfallConcreteGuildService.DaedraSummoning, summoningRank, summoningProvider);

        string[] guildSkills = deityFactionId switch
        {
            ArkayFactionId => ["axe", "backstabbing", "daedric", "destruction", "medical", "restoration", "short-blade"],
            ZenitharFactionId => ["blunt-weapon", "centaurian", "daedric", "etiquette", "giantish", "harpy", "mercantile", "orcish", "pickpocket", "spriggan", "streetwise", "thaumaturgy"],
            MaraFactionId => ["archery", "critical-strike", "daedric", "etiquette", "harpy", "illusion", "medical", "nymph", "restoration", "streetwise"],
            AkatoshFactionId => ["alteration", "daedric", "destruction", "dragonish", "long-blade", "running", "stealth"],
            JulianosFactionId => ["alteration", "daedric", "impish", "lockpicking", "mysticism", "short-blade", "thaumaturgy"],
            DibellaFactionId => ["daedric", "etiquette", "illusion", "lockpicking", "long-blade", "nymph", "orcish", "restoration"],
            StendarrFactionId => ["axe", "blunt-weapon", "critical-strike", "daedric", "dodging", "medical", "restoration"],
            KynarethFactionId => ["archery", "climbing", "daedric", "destruction", "dodging", "dragonish", "harpy", "illusion", "jumping", "running", "stealth"],
            _ => throw new ArgumentOutOfRangeException(nameof(deityFactionId), deityFactionId, "Unknown temple deity."),
        };
        return new(key, name, DaggerfallConcreteGuildKind.Temple, DaggerfallGuildMembershipKind.TempleDeity,
            factionId, 17, deityFactionId, -1, guildSkills, trainingProvider, DaggerfallGuildInvitationRequirement.None,
            neverExpels: false, services);
    }

    private static DaggerfallConcreteGuildDefinition Knightly(
        string key,
        string name,
        int factionId,
        int parentFactionId,
        int region,
        IReadOnlyList<string> skills) =>
        new(key, name, DaggerfallConcreteGuildKind.KnightlyOrder, DaggerfallGuildMembershipKind.KnightlyOrder,
            factionId, 9, parentFactionId, region, skills, trainingProviderFactionId: null,
            DaggerfallGuildInvitationRequirement.None, neverExpels: false,
            [
                Service(DaggerfallConcreteGuildService.Quests, null, 846, requiresMembership: false),
                Service(DaggerfallConcreteGuildService.ReceiveArmor, 0, 845),
                Service(DaggerfallConcreteGuildService.ReceiveHouse, 9, 848),
                Service(DaggerfallConcreteGuildService.FreeTavernRooms, 0),
                Service(DaggerfallConcreteGuildService.FreeShip, 6),
            ]);

    private static void AddRanked(
        ICollection<DaggerfallConcreteGuildServiceDefinition> services,
        DaggerfallConcreteGuildService service,
        int rank,
        int? providerFactionId)
    {
        if (rank >= 0)
            services.Add(Service(service, rank, providerFactionId));
    }

    private static DaggerfallConcreteGuildServiceDefinition Service(
        DaggerfallConcreteGuildService service,
        int? minimumRank,
        int? providerFactionId = null,
        bool requiresMembership = true,
        bool sourceImplemented = true) =>
        new(service, minimumRank, providerFactionId, requiresMembership, sourceImplemented);
}
