using System.Globalization;
using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Why one ordinary source guild provider action did not commit.</summary>
internal enum DaggerfallGuildProviderDenial
{
    None,
    InvalidBuilding,
    UnknownProvider,
    ProviderUnavailable,
    ServiceUnavailable,
    SiteChanged,
    NotMember,
    NotEligible,
    OfferExpired,
    InvalidSelection,
    AlreadyClaimed,
    HouseAlreadyClaimed,
    NoHouseAvailable,
    GrantRejected,
    AllocationRejected,
}

/// <summary>One source-backed choice returned to the ordinary dialogue projection.</summary>
internal sealed record DaggerfallGuildProviderOption(string Key, string Label);

/// <summary>
/// Result of a live guild provider action. The optional options are rendered by the same dialogue
/// owner that admitted the provider; the session never exposes a test-only NPC or a shadow reward
/// inventory to the UI.
/// </summary>
internal sealed record DaggerfallGuildProviderResult(
    bool Accepted,
    DaggerfallGuildProviderDenial Denial,
    string Message,
    DaggerfallGuildMembershipResult? Membership = null,
    DaggerfallKnightlyClaimTransactionResult? Claim = null,
    IReadOnlyList<DaggerfallGuildProviderOption>? Options = null)
{
    internal static DaggerfallGuildProviderResult Refused(
        DaggerfallGuildProviderDenial denial, string message) => new(false, denial, message);
}

/// <summary>
/// Session-side source provider entry for guild reviews and knightly claims. It resolves the guild
/// from the admitted interior/building and the NPC's authored provider faction, then delegates rank,
/// claim, inventory, and property mutations to their existing owners.
/// </summary>
internal sealed partial class DaggerfallSession
{
    private DaggerfallKnightlyArmorOffer? _pendingKnightlyArmor;
    private long _pendingKnightlyArmorProvider;
    private DaggerfallNpcSite? _pendingKnightlyArmorSite;

    private DaggerfallGuildProviderResult ResolveGuildProvider(
        DaggerfallNpc npc, DaggerfallDialogueTopic topic, string? key)
    {
        DaggerfallInteriorBuilding? building = CurrentInteriorBuilding();
        if (building is null || building.BuildingType is not (11 or 14))
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.InvalidBuilding,
                "This guild service interior is no longer available.");

        DaggerfallConcreteGuildService? requested = topic switch
        {
            DaggerfallDialogueTopic.Armor => DaggerfallConcreteGuildService.ReceiveArmor,
            DaggerfallDialogueTopic.House => DaggerfallConcreteGuildService.ReceiveHouse,
            DaggerfallDialogueTopic.RankReview => null,
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic,
                "This dialogue topic is not a guild provider service."),
        };
        if (!TryResolveGuildProvider(npc, building, requested, out DaggerfallConcreteGuildDefinition? guild,
                out DaggerfallServiceProvider provider, out DaggerfallGuildProviderDenial providerDenial))
            return DaggerfallGuildProviderResult.Refused(providerDenial,
                providerDenial switch
                {
                    DaggerfallGuildProviderDenial.SiteChanged => "That provider is no longer at this site.",
                    DaggerfallGuildProviderDenial.ProviderUnavailable => "That provider is no longer available.",
                    DaggerfallGuildProviderDenial.ServiceUnavailable => "That person does not offer this guild service.",
                    _ => "That person is not an admitted provider for this guild interior.",
                });

        int currentDay = checked((int)_time.Calendar.DayNumber);
        if (topic == DaggerfallDialogueTopic.RankReview)
            return ReviewGuildRank(guild!, currentDay);

        if (guild!.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.ServiceUnavailable,
                "This order reward is unavailable from the selected guild.");

        DaggerfallConcreteGuildServiceInput input = new(provider, provider.Site.Region);
        if (topic == DaggerfallDialogueTopic.Armor)
            return ResolveKnightlyArmor(guild.FactionId, npc, provider, currentDay, key, input);
        return ResolveKnightlyHouse(guild.FactionId, provider, currentDay, input);
    }

    private DaggerfallGuildProviderResult ReviewGuildRank(
        DaggerfallConcreteGuildDefinition guild, int currentDay)
    {
        DaggerfallGuildMembershipResult review = State.ConcreteGuildMembership.ReviewRank(guild.FactionId, currentDay);
        string message = review.Change switch
        {
            DaggerfallGuildMembershipChange.Promoted =>
                $"The guild promotes you to rank {review.View.Rank}.",
            DaggerfallGuildMembershipChange.Demoted =>
                $"The guild demotes you to rank {review.View.Rank}.",
            DaggerfallGuildMembershipChange.Expelled =>
                "The guild expels you after reviewing your standing.",
            _ when review.Denial == DaggerfallGuildMembershipDenial.ReviewTooSoon =>
                $"Your next guild review is in {review.View.DaysUntilReview} days.",
            _ when review.Denial == DaggerfallGuildMembershipDenial.NotMember =>
                "You are not a member of this guild.",
            _ => $"The guild finds no rank change; you remain at rank {review.View.Rank}.",
        };
        DaggerfallGuildProviderDenial denial = review.Denial switch
        {
            DaggerfallGuildMembershipDenial.NotMember => DaggerfallGuildProviderDenial.NotMember,
            _ => DaggerfallGuildProviderDenial.None,
        };
        return new(denial == DaggerfallGuildProviderDenial.None, denial, message, Membership: review);
    }

    private DaggerfallGuildProviderResult ResolveKnightlyArmor(
        int factionId,
        DaggerfallNpc npc,
        DaggerfallServiceProvider provider,
        int currentDay,
        string? key,
        DaggerfallConcreteGuildServiceInput input)
    {
        if (key is null)
        {
            DaggerfallKnightlyArmorOfferResult offered = State.KnightlyClaimActions.OfferArmor(
                factionId, currentDay, $"{npc.StableKey}:{npc.DurableId}", input);
            if (!offered.Offered)
                return KnightlyRefusal(offered.Denial, offered.Service);

            _pendingKnightlyArmor = offered.Offer;
            _pendingKnightlyArmorProvider = npc.DurableId;
            _pendingKnightlyArmorSite = provider.Site;
            IReadOnlyList<DaggerfallGuildProviderOption> choices = offered.Offer!.TemplateIndices
                .Select(template => new DaggerfallGuildProviderOption(
                    template.ToString(CultureInfo.InvariantCulture), $"Claim armor template {template}"))
                .ToArray();
            return new(true, DaggerfallGuildProviderDenial.None,
                "Choose one of the order's source-backed armor pieces.", Options: choices);
        }

        if (_pendingKnightlyArmor is null || _pendingKnightlyArmorProvider != npc.DurableId
            || _pendingKnightlyArmorSite != provider.Site)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.OfferExpired,
                "That armor offer is no longer current.");
        if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int template))
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.InvalidSelection,
                "That armor choice is not published.");

        DaggerfallKnightlyArmorOffer offer = _pendingKnightlyArmor;
        DaggerfallKnightlyClaimTransactionResult claim = State.KnightlyClaimActions.CompleteArmor(
            offer, template, currentDay, GrantKnightlyArmor, input);
        if (!claim.Applied)
            return KnightlyRefusal(claim.Denial, claim.ServiceDecision, claim);

        _pendingKnightlyArmor = null;
        _pendingKnightlyArmorProvider = 0;
        _pendingKnightlyArmorSite = null;
        return new(true, DaggerfallGuildProviderDenial.None,
            $"The order places armor template {template} in your inventory.", Claim: claim);
    }

    private DaggerfallGuildProviderResult ResolveKnightlyHouse(
        int factionId,
        DaggerfallServiceProvider provider,
        int currentDay,
        DaggerfallConcreteGuildServiceInput input)
    {
        DaggerfallKnightlyHouseOfferResult offered = State.KnightlyClaimActions.OfferHouse(factionId, currentDay, input);
        if (!offered.Offered)
            return KnightlyRefusal(offered.Denial, offered.Service);

        DaggerfallHouseOffer[] houses = [.. CurrentHouseOffers()
            .Where(offer => HouseProfile(offer.Identity) is not null)
            .Where(offer => !State.Property.OwnsHouseInRegion(offer.Identity.Site.Region))];
        if (houses.Length == 0)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NoHouseAvailable,
                "The order has no eligible local house to award.");

        int selected = houses.Length == 1
            ? 0
            : checked((int)_random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.guild.knightly-house.v1",
                $"{factionId}:{currentDay}", 0, houses.Length - 1)).Value);
        DaggerfallHouseOffer house = houses[selected];
        DaggerfallKnightlyClaimTransactionResult claim = State.KnightlyClaimActions.CompleteHouse(
            offered.Offer!, currentDay,
            _ => DaggerfallKnightlyHouseAllocationResult.FromPropertyTransaction(State.Property.AwardHouse(house)),
            input);
        if (!claim.Applied)
            return KnightlyRefusal(claim.Denial, claim.ServiceDecision, claim);
        return new(true, DaggerfallGuildProviderDenial.None,
            $"The order awards you the local house {house.StorageKey.Value}.", Claim: claim);
    }

    private DaggerfallKnightlyArmorGrantResult GrantKnightlyArmor(
        DaggerfallKnightlyArmorGrantRequest request)
    {
        DaggerfallCharacterIdentity identity = State.Character.Identity;
        string gender = identity.Gender.ToString().ToLowerInvariant();
        DaggerfallItemFactory factory = new(_definitions, _random);
        DaggerfallCreatedItem created = factory.Create(new DaggerfallItemCreateRequest(
            "Armor",
            $"guild.knightly.armor.{request.Offer.FactionId}.{request.Offer.Rank}.{request.TemplateIndex}",
            DaggerfallItemOwner.Player,
            TemplateIndex: request.TemplateIndex,
            Material: request.Offer.Material,
            Level: State.Progression.Level,
            Race: identity.RaceId,
            Gender: gender));
        DurableIdentityReference durable = _uniqueItems.AllocateReference();
        try
        {
            factory.Materialize(created, State.Inventory, State.ItemInstances, unique: durable);
            return DaggerfallKnightlyArmorGrantResult.Accepted(durable.Value);
        }
        catch
        {
            _uniqueItems.Remove(durable);
            throw;
        }
    }

    private bool TryResolveGuildProvider(
        DaggerfallNpc npc,
        DaggerfallInteriorBuilding building,
        DaggerfallConcreteGuildService? requested,
        out DaggerfallConcreteGuildDefinition? guild,
        out DaggerfallServiceProvider provider,
        out DaggerfallGuildProviderDenial denial)
    {
        guild = null;
        provider = null!;
        denial = DaggerfallGuildProviderDenial.UnknownProvider;
        foreach (DaggerfallConcreteGuildDefinition candidate in DaggerfallConcreteGuildCatalog.All.Where(candidate =>
            candidate.FactionId == building.FactionId || candidate.ParentFactionId == building.FactionId))
        {
            foreach (DaggerfallConcreteGuildServiceDefinition service in candidate.Services.Where(service =>
                service.ProviderFactionId == npc.Appearance.FactionId && service.SourceImplemented
                && (requested is null || service.Service == requested.Value)))
            {
                string serviceName = DaggerfallConcreteGuildServiceRuntime.ProviderServiceName(service.Service);
                if (!npc.Services.Contains(serviceName, StringComparer.Ordinal))
                {
                    denial = MoreSpecificProviderDenial(denial, DaggerfallGuildProviderDenial.ServiceUnavailable);
                    continue;
                }
                DaggerfallServiceProvider selected = new(npc.DurableId, npc.Site, serviceName);
                DaggerfallServiceDenial availability = State.Services.ProviderAvailable(selected);
                if (availability != DaggerfallServiceDenial.None)
                {
                    denial = MoreSpecificProviderDenial(denial, availability switch
                    {
                        DaggerfallServiceDenial.ProviderUnavailable => DaggerfallGuildProviderDenial.ProviderUnavailable,
                        DaggerfallServiceDenial.SiteChanged => DaggerfallGuildProviderDenial.SiteChanged,
                        DaggerfallServiceDenial.ServiceUnavailable => DaggerfallGuildProviderDenial.ServiceUnavailable,
                        _ => DaggerfallGuildProviderDenial.UnknownProvider,
                    });
                    continue;
                }
                guild = candidate;
                provider = selected;
                return true;
            }
        }
        return false;
    }

    private static DaggerfallGuildProviderDenial MoreSpecificProviderDenial(
        DaggerfallGuildProviderDenial current, DaggerfallGuildProviderDenial candidate) =>
        ProviderDenialSpecificity(candidate) > ProviderDenialSpecificity(current) ? candidate : current;

    private static int ProviderDenialSpecificity(DaggerfallGuildProviderDenial denial) => denial switch
    {
        DaggerfallGuildProviderDenial.SiteChanged => 4,
        DaggerfallGuildProviderDenial.ProviderUnavailable => 3,
        DaggerfallGuildProviderDenial.ServiceUnavailable => 2,
        _ => 1,
    };

    private static DaggerfallGuildProviderResult KnightlyRefusal(
        DaggerfallKnightlyClaimTransactionDenial denial,
        DaggerfallConcreteGuildServiceRuntimeDecision service,
        DaggerfallKnightlyClaimTransactionResult? claim = null) =>
        new(false, denial switch
        {
            DaggerfallKnightlyClaimTransactionDenial.ProviderUnavailable => DaggerfallGuildProviderDenial.ProviderUnavailable,
            DaggerfallKnightlyClaimTransactionDenial.AlreadyClaimed => DaggerfallGuildProviderDenial.AlreadyClaimed,
            DaggerfallKnightlyClaimTransactionDenial.HouseAlreadyClaimed => DaggerfallGuildProviderDenial.HouseAlreadyClaimed,
            DaggerfallKnightlyClaimTransactionDenial.OfferExpired => DaggerfallGuildProviderDenial.OfferExpired,
            DaggerfallKnightlyClaimTransactionDenial.InvalidSelection => DaggerfallGuildProviderDenial.InvalidSelection,
            DaggerfallKnightlyClaimTransactionDenial.AllocationRejected => DaggerfallGuildProviderDenial.AllocationRejected,
            DaggerfallKnightlyClaimTransactionDenial.GrantRejected => DaggerfallGuildProviderDenial.GrantRejected,
            DaggerfallKnightlyClaimTransactionDenial.NotEligible => DaggerfallGuildProviderDenial.NotEligible,
            _ => DaggerfallGuildProviderDenial.ServiceUnavailable,
        }, service.Policy.Denial switch
        {
            DaggerfallGuildServiceDenial.NotMember => "You are not a member of this order.",
            DaggerfallGuildServiceDenial.InsufficientRank => "Your order rank is not high enough for that service.",
            DaggerfallGuildServiceDenial.AlreadyClaimed => "You already claimed this rank's armor.",
            DaggerfallGuildServiceDenial.HouseAlreadyClaimed => "You already claimed the order's house.",
            _ => "The order cannot complete that service.",
        }, Claim: claim);

    private static DaggerfallGuildProviderResult KnightlyRefusal(
        DaggerfallKnightlyClaimTransactionDenial denial,
        DaggerfallConcreteGuildService service) =>
        DaggerfallGuildProviderResult.Refused(denial switch
        {
            DaggerfallKnightlyClaimTransactionDenial.ProviderUnavailable => DaggerfallGuildProviderDenial.ProviderUnavailable,
            DaggerfallKnightlyClaimTransactionDenial.AlreadyClaimed => DaggerfallGuildProviderDenial.AlreadyClaimed,
            DaggerfallKnightlyClaimTransactionDenial.HouseAlreadyClaimed => DaggerfallGuildProviderDenial.HouseAlreadyClaimed,
            DaggerfallKnightlyClaimTransactionDenial.NotEligible => DaggerfallGuildProviderDenial.NotEligible,
            _ => DaggerfallGuildProviderDenial.ServiceUnavailable,
        }, $"The order cannot complete {service.ToString().ToLowerInvariant()}.");
}
