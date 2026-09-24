using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Property;

namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>Why a concrete knightly entitlement transaction did not commit.</summary>
internal enum DaggerfallKnightlyClaimTransactionDenial
{
    None,
    NotEligible,
    ProviderUnavailable,
    AlreadyClaimed,
    HouseAlreadyClaimed,
    OfferExpired,
    InvalidSelection,
    GrantRejected,
    AllocationRejected,
}

/// <summary>
/// The source-shaped armor choices shown to the player at one order rank. The item owner turns
/// the chosen template and material into a real unique inventory item.
/// </summary>
internal sealed record DaggerfallKnightlyArmorOffer(
    int FactionId,
    int Rank,
    string Material,
    IReadOnlyList<int> TemplateIndices)
{
    internal DaggerfallKnightlyArmorOffer Validate()
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(FactionId);
        if (definition.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            throw new ArgumentException($"Faction {FactionId} is not a retained knightly order.", nameof(FactionId));
        if (Rank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            throw new ArgumentOutOfRangeException(nameof(Rank));
        if (!string.Equals(Material, Policies.DaggerfallItemMaterialPolicy.WeaponMaterials[Rank], StringComparison.Ordinal))
            throw new ArgumentException("Knightly armor material does not match the donor rank band.", nameof(Material));
        ArgumentNullException.ThrowIfNull(TemplateIndices);
        if (TemplateIndices.Count is < 3 or > 6
            || TemplateIndices.Any(index => index is < 102 or > 108))
            throw new ArgumentException("A knightly armor offer contains three to six classic armor templates.", nameof(TemplateIndices));
        return this;
    }

    internal bool ContainsTemplate(int templateIndex) => TemplateIndices.Contains(templateIndex);
}

/// <summary>The exact armor choice handed to the real item materializer.</summary>
internal sealed record DaggerfallKnightlyArmorGrantRequest(
    DaggerfallKnightlyArmorOffer Offer,
    int TemplateIndex)
{
    internal DaggerfallKnightlyArmorGrantRequest Validate()
    {
        ArgumentNullException.ThrowIfNull(Offer);
        Offer.Validate();
        if (!Offer.ContainsTemplate(TemplateIndex))
            throw new ArgumentException("The selected armor template is not in the retained offer.", nameof(TemplateIndex));
        return this;
    }
}

/// <summary>The rank and order identity captured before a local house allocation is attempted.</summary>
internal sealed record DaggerfallKnightlyHouseOffer(int FactionId, int Rank)
{
    internal DaggerfallKnightlyHouseOffer Validate()
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(FactionId);
        if (definition.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            throw new ArgumentException($"Faction {FactionId} is not a retained knightly order.", nameof(FactionId));
        if (Rank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            throw new ArgumentOutOfRangeException(nameof(Rank));
        return this;
    }
}

/// <summary>
/// Receipt returned by the actual inventory owner. An accepted armor claim must name the durable
/// unique item which was materialized, so a caller cannot mark a claim from an uncommitted offer.
/// </summary>
internal readonly record struct DaggerfallKnightlyArmorGrantResult(bool Applied, ulong DurableItemId)
{
    internal static DaggerfallKnightlyArmorGrantResult Accepted(ulong durableItemId)
    {
        if (durableItemId == 0) throw new ArgumentOutOfRangeException(nameof(durableItemId));
        return new(true, durableItemId);
    }

    internal static DaggerfallKnightlyArmorGrantResult Rejected() => new(false, 0);

    internal DaggerfallKnightlyArmorGrantResult Validate()
    {
        if (Applied ? DurableItemId == 0 : DurableItemId != 0)
            throw new ArgumentException("An armor grant receipt must pair its applied flag with one durable item identity.");
        return this;
    }
}

/// <summary>
/// Receipt returned by the property owner. The helper accepts only an applied house purchase/allocation
/// result and retains its stable property storage identity for presentation and save diagnostics.
/// </summary>
internal readonly record struct DaggerfallKnightlyHouseAllocationResult(bool Applied, string? StorageKey)
{
    internal static DaggerfallKnightlyHouseAllocationResult FromPropertyTransaction(
        DaggerfallPropertyTransactionResult transaction)
    {
        if (!transaction.Applied || transaction.Kind != DaggerfallPropertyTransactionKind.Purchase
            || transaction.PropertyKind != DaggerfallPropertyKind.House)
            return Rejected();
        return new DaggerfallKnightlyHouseAllocationResult(true, transaction.StorageKey.Value).Validate();
    }

    internal static DaggerfallKnightlyHouseAllocationResult Rejected() => new(false, null);

    internal DaggerfallKnightlyHouseAllocationResult Validate()
    {
        if (Applied ? string.IsNullOrWhiteSpace(StorageKey) : StorageKey is not null)
            throw new ArgumentException("A house allocation receipt must pair its applied flag with one property identity.");
        return this;
    }
}

/// <summary>Result of preparing the donor's armor choice popup.</summary>
internal sealed record DaggerfallKnightlyArmorOfferResult(
    DaggerfallConcreteGuildServiceRuntimeDecision Service,
    DaggerfallKnightlyArmorOffer? Offer,
    DaggerfallKnightlyClaimTransactionDenial Denial)
{
    internal bool Offered => Offer is not null;
}

/// <summary>Result of preparing the donor's one local-house entitlement.</summary>
internal sealed record DaggerfallKnightlyHouseOfferResult(
    DaggerfallConcreteGuildServiceRuntimeDecision Service,
    DaggerfallKnightlyHouseOffer? Offer,
    DaggerfallKnightlyClaimTransactionDenial Denial)
{
    internal bool Offered => Offer is not null;
}

/// <summary>One claim transaction result after the real owner either accepted or rejected its grant.</summary>
internal sealed record DaggerfallKnightlyClaimTransactionResult(
    bool Applied,
    int FactionId,
    int Rank,
    DaggerfallConcreteGuildService Service,
    DaggerfallKnightlyClaimTransactionDenial Denial,
    DaggerfallConcreteGuildServiceRuntimeDecision ServiceDecision,
    ulong? DurableItemId = null,
    string? HouseStorageKey = null);

/// <summary>
/// Source-grounded knight armor and house entitlement flow. It owns only durable claim flags and
/// commits them after the existing inventory/property owner reports the real result.
/// </summary>
internal sealed class DaggerfallKnightlyOrderClaimRuntime
{
    private const ulong RandomSeed = 0;
    private const string RandomScope = "daggerfall.guild-knightly.v1";

    private readonly DaggerfallConcreteGuildServiceRuntime _services;
    private readonly DaggerfallKnightlyOrderClaimState _claims;
    private readonly IRandomService _random;

    internal DaggerfallKnightlyOrderClaimRuntime(
        DaggerfallConcreteGuildServiceRuntime services,
        DaggerfallKnightlyOrderClaimState claims,
        IRandomService random)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _claims = claims ?? throw new ArgumentNullException(nameof(claims));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>Builds the donor's three-to-six armor choices without changing claim state.</summary>
    internal DaggerfallKnightlyArmorOfferResult OfferArmor(
        int factionId,
        int currentDay,
        string offerKey,
        DaggerfallConcreteGuildServiceInput? input = null)
    {
        RequireOrder(factionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(offerKey);
        DaggerfallConcreteGuildServiceRuntimeDecision service = Evaluate(
            factionId, DaggerfallConcreteGuildService.ReceiveArmor, currentDay, input);
        if (!service.CanUse)
            return new(service, null, Denial(service));

        int rank = service.Membership.Rank;
        string key = $"{factionId}:{rank}:{offerKey}";
        int count = Draw(key + ":count", 3, 6);
        int[] templates = Enumerable.Range(0, count)
            .Select(index => Draw($"{key}:template:{index}", 102, 108))
            .ToArray();
        DaggerfallKnightlyArmorOffer offer = new DaggerfallKnightlyArmorOffer(
            factionId,
            rank,
            Policies.DaggerfallItemMaterialPolicy.WeaponMaterials[rank],
            Array.AsReadOnly(templates)).Validate();
        return new(service, offer, DaggerfallKnightlyClaimTransactionDenial.None);
    }

    /// <summary>Checks the named seneschal and rank before a house candidate is selected.</summary>
    internal DaggerfallKnightlyHouseOfferResult OfferHouse(
        int factionId,
        int currentDay,
        DaggerfallConcreteGuildServiceInput? input = null)
    {
        RequireOrder(factionId);
        DaggerfallConcreteGuildServiceRuntimeDecision service = Evaluate(
            factionId, DaggerfallConcreteGuildService.ReceiveHouse, currentDay, input);
        if (!service.CanUse)
            return new(service, null, Denial(service));
        DaggerfallKnightlyHouseOffer offer = new DaggerfallKnightlyHouseOffer(factionId, service.Membership.Rank).Validate();
        return new(service, offer, DaggerfallKnightlyClaimTransactionDenial.None);
    }

    /// <summary>
    /// Gives the item owner the selected template and commits the rank flag only after it returns an
    /// applied durable-item receipt. A rejected callback leaves the claim state untouched.
    /// </summary>
    internal DaggerfallKnightlyClaimTransactionResult CompleteArmor(
        DaggerfallKnightlyArmorOffer offer,
        int selectedTemplateIndex,
        int currentDay,
        Func<DaggerfallKnightlyArmorGrantRequest, DaggerfallKnightlyArmorGrantResult> grant,
        DaggerfallConcreteGuildServiceInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(grant);
        offer.Validate();
        DaggerfallConcreteGuildServiceRuntimeDecision service = Evaluate(
            offer.FactionId, DaggerfallConcreteGuildService.ReceiveArmor, currentDay, input);
        if (!service.CanUse)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank, Denial(service));
        if (service.Membership.Rank != offer.Rank)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.OfferExpired);
        if (!offer.ContainsTemplate(selectedTemplateIndex))
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.InvalidSelection);
        if (_claims.Read(offer.FactionId).HasArmorClaim(offer.Rank))
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.AlreadyClaimed);

        DaggerfallKnightlyArmorGrantResult result;
        try
        {
            result = grant(new DaggerfallKnightlyArmorGrantRequest(offer, selectedTemplateIndex).Validate()).Validate();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.GrantRejected);
        }
        if (!result.Applied)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.GrantRejected);
        if (!_claims.TryCommitArmor(offer.FactionId, offer.Rank))
            return Refused(service, DaggerfallConcreteGuildService.ReceiveArmor, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.AlreadyClaimed);
        return new(true, offer.FactionId, offer.Rank, DaggerfallConcreteGuildService.ReceiveArmor,
            DaggerfallKnightlyClaimTransactionDenial.None, service, result.DurableItemId);
    }

    /// <summary>
    /// Gives the property owner the selected local-house allocation and commits the house flag only
    /// after it returns an applied property identity.
    /// </summary>
    internal DaggerfallKnightlyClaimTransactionResult CompleteHouse(
        DaggerfallKnightlyHouseOffer offer,
        int currentDay,
        Func<DaggerfallKnightlyHouseOffer, DaggerfallKnightlyHouseAllocationResult> allocate,
        DaggerfallConcreteGuildServiceInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(allocate);
        offer.Validate();
        DaggerfallConcreteGuildServiceRuntimeDecision service = Evaluate(
            offer.FactionId, DaggerfallConcreteGuildService.ReceiveHouse, currentDay, input);
        if (!service.CanUse)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveHouse, offer.Rank, Denial(service));
        if (service.Membership.Rank != offer.Rank)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveHouse, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.OfferExpired);
        if (_claims.Read(offer.FactionId).HouseClaimed)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveHouse, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.HouseAlreadyClaimed);

        DaggerfallKnightlyHouseAllocationResult result;
        try
        {
            result = allocate(offer).Validate();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return Refused(service, DaggerfallConcreteGuildService.ReceiveHouse, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.AllocationRejected);
        }
        if (!result.Applied)
            return Refused(service, DaggerfallConcreteGuildService.ReceiveHouse, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.AllocationRejected);
        if (!_claims.TryCommitHouse(offer.FactionId))
            return Refused(service, DaggerfallConcreteGuildService.ReceiveHouse, offer.Rank,
                DaggerfallKnightlyClaimTransactionDenial.HouseAlreadyClaimed);
        return new(true, offer.FactionId, offer.Rank, DaggerfallConcreteGuildService.ReceiveHouse,
            DaggerfallKnightlyClaimTransactionDenial.None, service, HouseStorageKey: result.StorageKey);
    }

    private DaggerfallConcreteGuildServiceRuntimeDecision Evaluate(
        int factionId,
        DaggerfallConcreteGuildService service,
        int currentDay,
        DaggerfallConcreteGuildServiceInput? input)
    {
        DaggerfallConcreteGuildServiceInput request = input ?? new();
        return _services.Evaluate(factionId, service, currentDay,
            request with { OrderClaims = _claims.Read(factionId) });
    }

    private int Draw(string key, int minimum, int maximum) => checked((int)_random.DrawKeyed(
        new KeyedRngRequest(RandomSeed, RandomScope, key, minimum, maximum)).Value);

    private static DaggerfallKnightlyClaimTransactionDenial Denial(
        DaggerfallConcreteGuildServiceRuntimeDecision service)
    {
        DaggerfallKnightlyClaimTransactionDenial policyDenial = service.Policy.Denial switch
        {
            DaggerfallGuildServiceDenial.AlreadyClaimed => DaggerfallKnightlyClaimTransactionDenial.AlreadyClaimed,
            DaggerfallGuildServiceDenial.HouseAlreadyClaimed => DaggerfallKnightlyClaimTransactionDenial.HouseAlreadyClaimed,
            _ => DaggerfallKnightlyClaimTransactionDenial.None,
        };
        if (policyDenial != DaggerfallKnightlyClaimTransactionDenial.None)
            return policyDenial;
        if (service.ProviderAvailability is not (DaggerfallGuildProviderAvailability.Ready
            or DaggerfallGuildProviderAvailability.NotRequired))
            return DaggerfallKnightlyClaimTransactionDenial.ProviderUnavailable;
        return DaggerfallKnightlyClaimTransactionDenial.NotEligible;
    }

    private static DaggerfallKnightlyClaimTransactionResult Refused(
        DaggerfallConcreteGuildServiceRuntimeDecision service,
        DaggerfallConcreteGuildService serviceKind,
        int rank,
        DaggerfallKnightlyClaimTransactionDenial denial) =>
        new(false, service.Definition.FactionId, rank, serviceKind, denial, service);

    private static DaggerfallConcreteGuildDefinition RequireOrder(int factionId)
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        if (definition.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            throw new ArgumentException($"Faction {factionId} is not a retained knightly order.", nameof(factionId));
        return definition;
    }
}
