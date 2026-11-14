using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A guild topic a provider answers, as the dialogue asks it.</summary>
internal enum DaggerfallGuildProviderTopic
{
    RankReview,
    Armor,
    House,
    Join,
    Teleport,
    Spymaster,
    RestoreAttributes,
}

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
internal sealed record DaggerfallGuildProviderOption(string Key, string Label, string? Topic = null);

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
    IReadOnlyList<DaggerfallGuildProviderOption>? Options = null,
    int? TextResource = null, string? GuildName = null, string? GodName = null)
{
    internal static DaggerfallGuildProviderResult Refused(
        DaggerfallGuildProviderDenial denial, string message) => new(false, denial, message);
}

/// <summary>
/// A guild's providers: which guild of the hall the player stands in an NPC serves, and what it answers to a guild
/// topic. Rank, claims and membership are the guild runtimes'; the knightly armour offer waits here for the
/// player's pick. A teleport's offer is the session's list and a knightly house is a property award, both inputs.
/// </summary>
/// <param name="providerAvailable">Whether this provider can serve now, as the service transactions answer.</param>
/// <param name="openTeleport">Opens a guild's free teleport from this provider.</param>
/// <param name="knightlyHouse">Awards the order's house for this faction on this day, once the order offers it.</param>
/// <param name="noRegeneration">Whether the player's career denies them regeneration where they stand.</param>
internal sealed class DaggerfallGuildProviders(
    DaggerfallConcreteGuildServiceRuntime services,
    DaggerfallConcreteGuildMembershipRuntime membership,
    DaggerfallKnightlyOrderClaimRuntime knightlyClaims,
    DaggerfallGuildAdmission admission,
    ActorsState actors,
    DaggerfallEffectLifecycle effects,
    DaggerfallCharacterState character,
    DaggerfallDefinitions definitions,
    IRandomService random,
    DaggerfallUniqueItemAllocator uniqueItems,
    MechanicsInventoryCoordinator inventory,
    DaggerfallItemInstances itemInstances,
    Func<DaggerfallCalendar> calendar,
    Func<bool> noRegeneration,
    Func<DaggerfallServiceProvider, DaggerfallServiceDenial> providerAvailable,
    Func<DaggerfallServiceProvider, DaggerfallGuildProviderResult> openTeleport,
    Func<int, int, DaggerfallConcreteGuildServiceInput, DaggerfallGuildProviderResult> knightlyHouse)
{
    private DaggerfallKnightlyArmorOffer? _pendingKnightlyArmor;
    private long _pendingKnightlyArmorProvider;
    private DaggerfallNpcSite? _pendingKnightlyArmorSite;

    /// <summary>What this provider of the guild hall the player stands in answers to a guild topic.</summary>
    internal DaggerfallGuildProviderResult Resolve(
        DaggerfallNpc npc, DaggerfallInteriorBuilding? building, DaggerfallGuildProviderTopic topic, string? key)
    {
        if (building is null || building.BuildingType is not (11 or 14 or 18))
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.InvalidBuilding,
                "This guild service interior is no longer available.");

        DaggerfallConcreteGuildService? requested = topic switch
        {
            DaggerfallGuildProviderTopic.Armor => DaggerfallConcreteGuildService.ReceiveArmor,
            DaggerfallGuildProviderTopic.House => DaggerfallConcreteGuildService.ReceiveHouse,
            DaggerfallGuildProviderTopic.RankReview or DaggerfallGuildProviderTopic.Join or DaggerfallGuildProviderTopic.RestoreAttributes => null,
            DaggerfallGuildProviderTopic.Teleport => DaggerfallConcreteGuildService.Teleport,
            DaggerfallGuildProviderTopic.Spymaster => DaggerfallConcreteGuildService.Spymaster,
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic,
                "This topic is not a guild provider service."),
        };
        if (!TryResolve(npc, building, requested, out DaggerfallConcreteGuildDefinition? guild,
                out DaggerfallServiceProvider provider, out DaggerfallGuildProviderDenial providerDenial))
            return DaggerfallGuildProviderResult.Refused(providerDenial,
                providerDenial switch
                {
                    DaggerfallGuildProviderDenial.SiteChanged => "That provider is no longer at this site.",
                    DaggerfallGuildProviderDenial.ProviderUnavailable => "That provider is no longer available.",
                    DaggerfallGuildProviderDenial.ServiceUnavailable => "That person does not offer this guild service.",
                    _ => "That person is not an admitted provider for this guild interior.",
                });

        int currentDay = checked((int)calendar().DayNumber);
        if (topic == DaggerfallGuildProviderTopic.RestoreAttributes)
        {
            if (!services.Evaluate(guild!.FactionId, DaggerfallConcreteGuildService.FreeHealing, currentDay).CanUse)
                return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible, "Your temple rank does not grant free restoration.");
            if (key != "yes") return new(true, DaggerfallGuildProviderDenial.None, "You decline attribute restoration.");
            foreach (var attribute in DaggerfallAttributeDrainEffects.Attributes)
                DaggerfallAttributeDrainEffects.Heal(effects, actors.Player.DurableId, attribute, int.MaxValue, () => character.Career);
            return new(true, DaggerfallGuildProviderDenial.None, "The temple restores your damaged attributes.");
        }
        if (topic == DaggerfallGuildProviderTopic.Join) return admission.Join(guild!, npc, currentDay, key);
        if (topic is DaggerfallGuildProviderTopic.Teleport or DaggerfallGuildProviderTopic.Spymaster)
        {
            var decision = services.Evaluate(guild!.FactionId, requested!.Value, currentDay, new(provider, provider.Site.Region));
            if (!decision.CanUse) return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NotEligible,
                decision.Membership.IsMember ? "Your guild rank does not grant this service." : "You must join this guild to use that service.");
            if (topic == DaggerfallGuildProviderTopic.Teleport) return openTeleport(provider);
            return new(true, DaggerfallGuildProviderDenial.None, "Ask me what you wish to know.", TextResource: 402);
        }
        if (topic == DaggerfallGuildProviderTopic.RankReview)
            return ReviewRank(guild!, currentDay);

        if (guild!.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.ServiceUnavailable,
                "This order reward is unavailable from the selected guild.");

        DaggerfallConcreteGuildServiceInput input = new(provider, provider.Site.Region);
        if (topic == DaggerfallGuildProviderTopic.Armor)
            return ResolveKnightlyArmor(guild.FactionId, npc, provider, currentDay, key, input);
        return knightlyHouse(guild.FactionId, currentDay, input);
    }

    private DaggerfallGuildProviderResult ReviewRank(
        DaggerfallConcreteGuildDefinition guild, int currentDay)
    {
        DaggerfallGuildMembershipResult review = membership.ReviewRank(guild.FactionId, currentDay);
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
            DaggerfallKnightlyArmorOfferResult offered = knightlyClaims.OfferArmor(
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
        DaggerfallKnightlyClaimTransactionResult claim = knightlyClaims.CompleteArmor(
            offer, template, currentDay, GrantKnightlyArmor, input);
        if (!claim.Applied)
            return KnightlyRefusal(claim.Denial, claim.ServiceDecision, claim);

        _pendingKnightlyArmor = null;
        _pendingKnightlyArmorProvider = 0;
        _pendingKnightlyArmorSite = null;
        return new(true, DaggerfallGuildProviderDenial.None,
            $"The order places armor template {template} in your inventory.", Claim: claim);
    }

    private DaggerfallKnightlyArmorGrantResult GrantKnightlyArmor(
        DaggerfallKnightlyArmorGrantRequest request)
    {
        DaggerfallCharacterIdentity identity = character.Identity;
        string gender = identity.Gender.ToString().ToLowerInvariant();
        DaggerfallItemFactory factory = new(definitions, random);
        DaggerfallCreatedItem created = factory.Create(new DaggerfallItemCreateRequest(
            "Armor",
            $"guild.knightly.armor.{request.Offer.FactionId}.{request.Offer.Rank}.{request.TemplateIndex}",
            DaggerfallItemOwner.Player,
            TemplateIndex: request.TemplateIndex,
            Material: request.Offer.Material,
            Level: actors.Player.Progression.Level,
            Race: identity.RaceId,
            Gender: gender));
        DurableIdentityReference durable = uniqueItems.AllocateReference();
        try
        {
            factory.Materialize(created, inventory, itemInstances, unique: durable);
            return DaggerfallKnightlyArmorGrantResult.Accepted(durable.Value);
        }
        catch
        {
            uniqueItems.Remove(durable);
            throw;
        }
    }

    /// <summary>
    /// The guild of this hall whose service (any, or <paramref name="requested"/>) this NPC provides and may provide now;
    /// otherwise the most specific reason it cannot.
    /// </summary>
    internal bool TryResolve(
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
            candidate.FactionId == building.FactionId || (candidate.ParentFactionId > 0 && candidate.ParentFactionId == building.FactionId)))
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
                DaggerfallServiceDenial availability = providerAvailable(selected);
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

    /// <summary>The order's refusal of a claim it offered, in the words of the service's denial.</summary>
    internal static DaggerfallGuildProviderResult KnightlyRefusal(
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

    /// <summary>The order's refusal to offer a claim.</summary>
    internal static DaggerfallGuildProviderResult KnightlyRefusal(
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

    /// <summary>
    /// The guild's welcome when the player talks to one of its providers: a temple's free healing and a guild's
    /// spell points, as rank grants them, and the offer to restore drained attributes. Null outside a guild hall.
    /// </summary>
    internal DaggerfallGuildProviderResult? Benefits(DaggerfallNpc npc, DaggerfallInteriorBuilding? building)
    {
        if (building is null || !TryResolve(npc, building, null, out var guild, out var provider, out _)) return null;
        int day = checked((int)calendar().DayNumber);
        bool noRegen = noRegeneration();
        List<string> messages = [];
        foreach ((DaggerfallConcreteGuildService service, string trackId, string message) in new[] {
            (DaggerfallConcreteGuildService.FreeHealing, "health", "The temple restores your health."),
            (DaggerfallConcreteGuildService.FreeMagickaRecharge, "magicka", "The guild restores your spell points.") })
        {
            if (!services.Evaluate(guild!.FactionId, service, day, new(provider, provider.Site.Region, noRegen)).CanUse) continue;
            var track = actors.Player.Stats.GetTrack(TrackId.Parse(trackId));
            if (track.Current >= track.Maximum.Value) continue;
            track.SetCurrent(track.Maximum.Value, clamp: true);
            messages.Add(message);
        }
        bool restore = services.Evaluate(guild!.FactionId, DaggerfallConcreteGuildService.FreeHealing, day).CanUse
            && effects.Active.Any(effect => effect.Context.Target.Value == (ulong)actors.Player.DurableId
                && effect.Definition.Spell is { Type: 7 or 11 });
        if (restore) messages.Add("Would you like the temple to restore your damaged attributes?");
        return new(true, DaggerfallGuildProviderDenial.None, string.Join("\n", messages), Options: restore
            ? [new("yes", "Restore my attributes", "restore-attributes"), new("no", "No", "restore-attributes")] : null);
    }

    /// <summary>Whether any guild's rank grants the player free ship passage today.</summary>
    internal bool FreeShipPassage() => DaggerfallConcreteGuildCatalog.All.Any(guild =>
        services.Evaluate(guild.FactionId, DaggerfallConcreteGuildService.FreeShip,
            checked((int)calendar().DayNumber)).CanUse);
}
