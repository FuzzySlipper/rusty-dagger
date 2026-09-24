namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>Why a source-backed guild admission or invitation is not currently eligible.</summary>
internal enum DaggerfallGuildAdmissionDenial
{
    None,
    InsufficientReputation,
    InsufficientSkills,
    MissingCrimeEvidence,
}

/// <summary>One count-free admission input projected from canonical product state.</summary>
internal sealed record DaggerfallGuildAdmissionContext(
    int Reputation,
    IReadOnlyDictionary<string, int> PermanentSkills,
    DaggerfallGuildCrimeInvitationEvidence CrimeEvidence)
{
    internal int SkillValue(string skillId) => PermanentSkills.GetValueOrDefault(skillId);
}

/// <summary>Result of a concrete guild rank-zero gate and its separate invitation condition.</summary>
internal sealed record DaggerfallGuildAdmissionDecision(
    int FactionId,
    bool RankEligible,
    bool InvitationEligible,
    DaggerfallGuildAdmissionDenial Denial,
    DaggerfallGuildInvitationRequirement InvitationRequirement,
    int HighestQualifiedRank)
{
    internal bool Eligible => RankEligible && InvitationEligible;
}

/// <summary>Why a concrete service gate cannot be used at the current context.</summary>
internal enum DaggerfallGuildServiceDenial
{
    None,
    Unavailable,
    NotMember,
    InsufficientRank,
    RequiresNoRegenSpellPoints,
    WrongRegion,
    AlreadyClaimed,
    HouseAlreadyClaimed,
}

/// <summary>The result of a permission check; it never reports an unavailable service as success.</summary>
internal enum DaggerfallGuildServiceDecisionKind
{
    Unavailable,
    Denied,
    Eligible,
}

/// <summary>Saved knightly-order entitlement claims projected into a service check.</summary>
internal sealed record DaggerfallKnightlyOrderClaims
{
    internal DaggerfallKnightlyOrderClaims(IReadOnlySet<int> claimedArmorRanks, bool houseClaimed)
    {
        ArgumentNullException.ThrowIfNull(claimedArmorRanks);
        if (claimedArmorRanks.Any(rank => rank is < 0 or > DaggerfallSocialState.MaximumGuildRank))
            throw new ArgumentOutOfRangeException(nameof(claimedArmorRanks), "Knightly armor claims name only Daggerfall ranks.");
        ClaimedArmorRanks = new HashSet<int>(claimedArmorRanks);
        HouseClaimed = houseClaimed;
    }

    internal IReadOnlySet<int> ClaimedArmorRanks { get; }
    internal bool HouseClaimed { get; }

    internal static DaggerfallKnightlyOrderClaims Empty =>
        new(new HashSet<int>(), houseClaimed: false);

    internal bool HasArmorClaim(int rank) => ClaimedArmorRanks.Contains(rank);
}

/// <summary>
/// Canonical membership and environmental inputs used by concrete service decisions. Product state
/// owners construct this projection; the guild policy never stores or mutates membership.
/// </summary>
internal sealed record DaggerfallGuildServiceContext(
    bool IsMember,
    int Rank,
    int CurrentRegion = -1,
    bool NoRegenSpellPoints = false,
    DaggerfallKnightlyOrderClaims? OrderClaims = null);

/// <summary>A source-backed concrete service permission result.</summary>
internal sealed record DaggerfallGuildServiceDecision(
    int FactionId,
    DaggerfallConcreteGuildService Service,
    DaggerfallGuildServiceDecisionKind Kind,
    DaggerfallGuildServiceDenial Denial,
    int? MinimumRank,
    int? ProviderFactionId)
{
    internal bool Eligible => Kind == DaggerfallGuildServiceDecisionKind.Eligible;
}

/// <summary>
/// Pure concrete guild policy calculations. It returns eligibility and source identities only;
/// service callers still own payment, mutation, item grants, and quest execution.
/// </summary>
internal static class DaggerfallConcreteGuildPolicy
{
    internal static DaggerfallGuildAdmissionDecision AssessAdmission(
        DaggerfallConcreteGuildDefinition definition,
        DaggerfallGuildAdmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        DaggerfallGuildRankRequirement admission = definition.RankRequirements[0];
        if (context.Reputation < admission.MinimumReputation)
        {
            return new(definition.FactionId, RankEligible: false, InvitationEligible: false,
                DaggerfallGuildAdmissionDenial.InsufficientReputation, definition.InvitationRequirement, -1);
        }

        int highestQualifiedRank = -1;
        foreach (DaggerfallGuildRankRequirement requirement in definition.RankRequirements)
        {
            (int high, int low) = CountSkills(definition.GuildSkills, context, requirement);
            bool qualified = context.Reputation >= requirement.MinimumReputation
                && high >= requirement.RequiredHighSkills
                && high + low >= requirement.RequiredTotalSkills;
            if (!qualified) break;
            highestQualifiedRank = requirement.Rank;
        }

        bool rankEligible = highestQualifiedRank >= 0;
        DaggerfallGuildAdmissionDenial denial = rankEligible
            ? DaggerfallGuildAdmissionDenial.None
            : DaggerfallGuildAdmissionDenial.InsufficientSkills;
        bool invitationEligible = context.CrimeEvidence.Satisfies(definition.InvitationRequirement);
        if (!invitationEligible && denial == DaggerfallGuildAdmissionDenial.None)
            denial = DaggerfallGuildAdmissionDenial.MissingCrimeEvidence;

        return new(definition.FactionId, rankEligible, invitationEligible, denial,
            definition.InvitationRequirement, highestQualifiedRank);
    }

    /// <summary>
    /// Evaluates only the invitation fact. Crime tallying, three-day letters, and quest state remain
    /// in #8054; a caller maps that canonical state to the two booleans in the evidence projection.
    /// </summary>
    internal static bool IsInvitationEligible(
        DaggerfallConcreteGuildDefinition definition,
        DaggerfallGuildCrimeInvitationEvidence evidence) =>
        definition.InvitationRequirement switch
        {
            DaggerfallGuildInvitationRequirement.None => true,
            _ => evidence.Satisfies(definition.InvitationRequirement),
        };

    internal static DaggerfallGuildServiceDecision EvaluateService(
        DaggerfallConcreteGuildDefinition definition,
        DaggerfallConcreteGuildService service,
        DaggerfallGuildServiceContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        if (!definition.TryGetService(service, out DaggerfallConcreteGuildServiceDefinition? gate))
            return Unavailable(definition, service);
        if (!gate.SourceImplemented)
            return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Unavailable,
                DaggerfallGuildServiceDenial.Unavailable, gate.MinimumRank, gate.ProviderFactionId);
        if (gate.RequiresMembership && !context.IsMember)
            return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Denied,
                DaggerfallGuildServiceDenial.NotMember, gate.MinimumRank, gate.ProviderFactionId);
        if (gate.MinimumRank is int minimumRank && context.Rank < minimumRank)
            return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Denied,
                DaggerfallGuildServiceDenial.InsufficientRank, gate.MinimumRank, gate.ProviderFactionId);

        switch (service)
        {
            case DaggerfallConcreteGuildService.FreeMagickaRecharge when !context.NoRegenSpellPoints:
                return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Denied,
                    DaggerfallGuildServiceDenial.RequiresNoRegenSpellPoints, gate.MinimumRank, gate.ProviderFactionId);

            case DaggerfallConcreteGuildService.FreeTavernRooms:
                if (context.Rank < 4 && context.CurrentRegion != definition.Region)
                    return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Denied,
                        DaggerfallGuildServiceDenial.WrongRegion, gate.MinimumRank, gate.ProviderFactionId);
                break;

            case DaggerfallConcreteGuildService.ReceiveArmor:
                if ((context.OrderClaims ?? DaggerfallKnightlyOrderClaims.Empty).HasArmorClaim(context.Rank))
                    return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Denied,
                        DaggerfallGuildServiceDenial.AlreadyClaimed, gate.MinimumRank, gate.ProviderFactionId);
                break;

            case DaggerfallConcreteGuildService.ReceiveHouse:
                if ((context.OrderClaims ?? DaggerfallKnightlyOrderClaims.Empty).HouseClaimed)
                    return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Denied,
                        DaggerfallGuildServiceDenial.HouseAlreadyClaimed, gate.MinimumRank, gate.ProviderFactionId);
                break;
        }

        return new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Eligible,
            DaggerfallGuildServiceDenial.None, gate.MinimumRank, gate.ProviderFactionId);
    }

    /// <summary>Fighters Guild donor reward multiplier, preserving the original fixed-point formula.</summary>
    internal static int FightersReward(int reward, int rank)
    {
        ValidatePriceInput(reward, rank);
        return (((10 + rank) << 8) / 10 * reward) >> 8;
    }

    /// <summary>Fighters Guild donor repair-cost reduction, preserving the original fixed-point formula.</summary>
    internal static int FightersRepairCost(int price, int rank)
    {
        ValidatePriceInput(price, rank);
        return (((10 - rank) << 8) / 10 * price) >> 8;
    }

    private static DaggerfallGuildServiceDecision Unavailable(
        DaggerfallConcreteGuildDefinition definition,
        DaggerfallConcreteGuildService service) =>
        new(definition.FactionId, service, DaggerfallGuildServiceDecisionKind.Unavailable,
            DaggerfallGuildServiceDenial.Unavailable, MinimumRank: null, ProviderFactionId: null);

    private static (int High, int Low) CountSkills(
        IReadOnlyList<string> skills,
        DaggerfallGuildAdmissionContext context,
        DaggerfallGuildRankRequirement requirement)
    {
        int high = 0;
        int low = 0;
        foreach (string skill in skills)
        {
            int value = context.SkillValue(skill);
            if (value >= requirement.HighSkillMinimum) high++;
            else if (value >= requirement.LowSkillMinimum) low++;
        }
        return (high, low);
    }

    private static void ValidatePriceInput(int price, int rank)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), price, "A guild price or reward is non-negative.");
        if (rank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "A guild rank is outside the admitted Daggerfall range.");
    }
}
