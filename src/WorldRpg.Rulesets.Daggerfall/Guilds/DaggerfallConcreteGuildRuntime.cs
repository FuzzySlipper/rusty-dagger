namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>One concrete admission attempt and the canonical membership change it was allowed to make.</summary>
internal sealed record DaggerfallConcreteGuildMembershipOperation(
    DaggerfallConcreteGuildDefinition Definition,
    DaggerfallGuildAdmissionDecision Admission,
    DaggerfallGuildMembershipResult? Membership)
{
    internal bool Applied => Membership?.Applied == true;
}

/// <summary>
/// Concrete guild admission, review, and expulsion facade. It delegates every state change to the
/// existing DaggerfallGuildMembershipPolicy, so this class never creates a second membership map.
/// </summary>
internal sealed class DaggerfallConcreteGuildMembershipRuntime
{
    private readonly DaggerfallGuildMembershipPolicy _membership;

    internal DaggerfallConcreteGuildMembershipRuntime(DaggerfallGuildMembershipPolicy membership)
    {
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
    }

    internal DaggerfallConcreteGuildMembershipOperation Admit(
        int factionId,
        int currentDay,
        DaggerfallGuildAdmissionContext context)
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        DaggerfallGuildAdmissionDecision admission = DaggerfallConcreteGuildPolicy.AssessAdmission(definition, context);
        if (!admission.Eligible)
            return new(definition, admission, Membership: null);

        return new(definition, admission, _membership.Admit(factionId, currentDay));
    }

    internal DaggerfallConcreteGuildMembershipOperation Rejoin(
        int factionId,
        int currentDay,
        DaggerfallGuildAdmissionContext context)
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        DaggerfallGuildAdmissionDecision admission = DaggerfallConcreteGuildPolicy.AssessAdmission(definition, context);
        if (!admission.Eligible)
            return new(definition, admission, Membership: null);

        return new(definition, admission, _membership.Rejoin(factionId, currentDay));
    }

    internal DaggerfallGuildMembershipView Read(int factionId, int currentDay)
    {
        _ = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        return _membership.Read(factionId, currentDay);
    }

    internal DaggerfallGuildMembershipResult ReviewRank(int factionId, int currentDay)
    {
        _ = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        return _membership.ReviewRank(factionId, currentDay);
    }

    internal DaggerfallGuildMembershipResult Expel(int factionId, int currentDay)
    {
        _ = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        return _membership.Expel(factionId, currentDay);
    }
}

/// <summary>How a provider-bound concrete service resolved its selected NPC provider.</summary>
internal enum DaggerfallGuildProviderAvailability
{
    NotRequired,
    NotChecked,
    Required,
    Unknown,
    FactionMismatch,
    Unavailable,
    Ready,
}

/// <summary>
/// Runtime-only inputs that sit beside canonical membership. Knight reward claims must be supplied
/// from the owning save/state composition; this type intentionally has no mutable claim ledger.
/// </summary>
internal sealed record DaggerfallConcreteGuildServiceInput(
    DaggerfallServiceProvider? Provider = null,
    int CurrentRegion = -1,
    bool NoRegenSpellPoints = false,
    DaggerfallKnightlyOrderClaims? OrderClaims = null);

/// <summary>A concrete policy decision combined with live provider and canonical membership facts.</summary>
internal sealed record DaggerfallConcreteGuildServiceRuntimeDecision(
    DaggerfallConcreteGuildDefinition Definition,
    DaggerfallGuildMembershipView Membership,
    DaggerfallGuildServiceDecision Policy,
    DaggerfallGuildProviderAvailability ProviderAvailability,
    DaggerfallServiceDenial ProviderDenial)
{
    internal bool CanUse => Policy.Eligible
        && (ProviderAvailability is DaggerfallGuildProviderAvailability.NotRequired
            or DaggerfallGuildProviderAvailability.Ready);
}

/// <summary>
/// Provider-bound concrete service evaluator. It combines the source gate with canonical Social
/// membership and the existing service transaction provider admission; it does not quote, charge,
/// grant, or execute a service.
/// </summary>
internal sealed class DaggerfallConcreteGuildServiceRuntime
{
    private readonly DaggerfallGuildMembershipPolicy _membership;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallServiceTransactions _services;

    internal DaggerfallConcreteGuildServiceRuntime(
        DaggerfallGuildMembershipPolicy membership,
        DaggerfallNpcRegistry npcs,
        DaggerfallServiceTransactions services)
    {
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    internal DaggerfallConcreteGuildServiceRuntimeDecision Evaluate(
        int factionId,
        DaggerfallConcreteGuildService service,
        int currentDay,
        DaggerfallConcreteGuildServiceInput? input = null)
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        DaggerfallConcreteGuildServiceInput request = input ?? new();
        DaggerfallGuildMembershipView membership = _membership.Read(factionId, currentDay);
        DaggerfallGuildServiceDecision policy = DaggerfallConcreteGuildPolicy.EvaluateService(
            definition, service, new(membership.IsMember, membership.Rank, request.CurrentRegion,
                request.NoRegenSpellPoints, request.OrderClaims));

        if (!policy.Eligible)
            return new(definition, membership, policy, DaggerfallGuildProviderAvailability.NotChecked,
                DaggerfallServiceDenial.None);
        if (policy.ProviderFactionId is not int providerFactionId)
            return new(definition, membership, policy, DaggerfallGuildProviderAvailability.NotRequired,
                DaggerfallServiceDenial.None);
        if (request.Provider is null)
            return new(definition, membership, policy, DaggerfallGuildProviderAvailability.Required,
                DaggerfallServiceDenial.UnknownProvider);

        DaggerfallNpc npc;
        try
        {
            npc = _npcs.Require(request.Provider.NpcId);
        }
        catch (InvalidOperationException)
        {
            return new(definition, membership, policy, DaggerfallGuildProviderAvailability.Unknown,
                DaggerfallServiceDenial.UnknownProvider);
        }

        if (npc.Appearance.FactionId != providerFactionId)
            return new(definition, membership, policy, DaggerfallGuildProviderAvailability.FactionMismatch,
                DaggerfallServiceDenial.ServiceUnavailable);

        if (!StringComparer.Ordinal.Equals(request.Provider.Service, ProviderServiceName(service)))
            return new(definition, membership, policy, DaggerfallGuildProviderAvailability.Unavailable,
                DaggerfallServiceDenial.ServiceUnavailable);

        DaggerfallServiceDenial providerDenial = _services.ProviderAvailable(request.Provider);
        return new(definition, membership, policy,
            providerDenial == DaggerfallServiceDenial.None
                ? DaggerfallGuildProviderAvailability.Ready
                : DaggerfallGuildProviderAvailability.Unavailable,
            providerDenial);
    }

    private static string ProviderServiceName(DaggerfallConcreteGuildService service) => service switch
    {
        DaggerfallConcreteGuildService.Training => "training",
        DaggerfallConcreteGuildService.Quests => "quests",
        DaggerfallConcreteGuildService.Repair => "repair",
        DaggerfallConcreteGuildService.Identify => "identify",
        DaggerfallConcreteGuildService.Donate => "donate",
        DaggerfallConcreteGuildService.CureDisease => "cure-disease",
        DaggerfallConcreteGuildService.BuyPotions => "buy-potions",
        DaggerfallConcreteGuildService.MakePotions => "make-potions",
        DaggerfallConcreteGuildService.BuySpells => "buy-spells",
        DaggerfallConcreteGuildService.MakeSpells => "make-spells",
        DaggerfallConcreteGuildService.BuyMagicItems => "buy-magic-items",
        DaggerfallConcreteGuildService.MakeMagicItems => "make-magic-items",
        DaggerfallConcreteGuildService.SellMagicItems => "sell-magic-items",
        DaggerfallConcreteGuildService.Teleport => "teleport",
        DaggerfallConcreteGuildService.DaedraSummoning => "daedra-summoning",
        DaggerfallConcreteGuildService.Spymaster => "spymaster",
        DaggerfallConcreteGuildService.BuySoulgems => "buy-soulgems",
        DaggerfallConcreteGuildService.ReceiveArmor => "armor",
        DaggerfallConcreteGuildService.ReceiveHouse => "house",
        _ => throw new InvalidOperationException($"{service} has no NPC provider identity."),
    };
}
