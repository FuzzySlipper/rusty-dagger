using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallTempleServiceDenial
{
    None,
    InvalidTemple,
    UnknownProvider,
    ProviderUnavailable,
    ServiceUnavailable,
    SiteChanged,
    NotMember,
    InsufficientFunds,
    NoAffliction,
    BlessingUnavailable,
}

internal sealed record DaggerfallTempleServiceResult(
    bool Accepted,
    DaggerfallTempleServiceDenial Denial,
    ulong PaidGold = 0,
    int AilmentsRemoved = 0,
    string Message = "")
{
    internal static DaggerfallTempleServiceResult Refused(DaggerfallTempleServiceDenial denial, string message) =>
        new(false, denial, Message: message);
}

internal sealed record DaggerfallTempleDonationQuote(
    DaggerfallServiceQuote ServiceQuote,
    int DeityFactionId,
    int MembershipFactionId,
    int Rank,
    DaggerfallTempleBlessingTarget Target,
    int Magnitude,
    int DurationMinutes,
    long ExpiresAtMinute,
    string BlessingInstance);

internal sealed record DaggerfallTempleCureQuote(
    DaggerfallServiceQuote ServiceQuote,
    int DeityFactionId,
    int AfflictionCount,
    ulong Price);

/// <summary>
/// Ordinary source-provider callers for temple donation, cure, and blessing services. It resolves
/// the deity from the admitted interior building and the provider from the admitted static NPC;
/// no arbitrary registry faction can manufacture a temple service.
/// </summary>
internal sealed class DaggerfallTempleServiceRuntime
{
    private const int DonationProviderFactionId = 810;
    private const int CureProviderFactionId = 813;
    private const string DonationService = "donate";
    private const string CureService = "cure-disease";
    private const string BlessingEffectSource = "temple.donation";
    private readonly DaggerfallConcreteGuildServiceRuntime _guildServices;
    private readonly DaggerfallServiceTransactions _transactions;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallSocialState _social;
    private readonly DaggerfallEffectLifecycle _effects;
    private readonly DaggerfallPoisonRuntime _poisons;
    private readonly ActorsState _actors;
    private readonly IRandomService _random;
    private readonly Func<DaggerfallCalendar> _calendar;
    private long _requestSequence;

    internal DaggerfallTempleServiceRuntime(
        DaggerfallConcreteGuildServiceRuntime guildServices,
        DaggerfallServiceTransactions transactions,
        DaggerfallNpcRegistry npcs,
        DaggerfallSocialState social,
        DaggerfallEffectLifecycle effects,
        DaggerfallPoisonRuntime poisons,
        ActorsState actors,
        IRandomService random,
        Func<DaggerfallCalendar> calendar)
    {
        _guildServices = guildServices ?? throw new ArgumentNullException(nameof(guildServices));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _poisons = poisons ?? throw new ArgumentNullException(nameof(poisons));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    /// <summary>Quotes a positive source donation without charging gold or mutating reputation.</summary>
    internal DaggerfallTempleDonationQuote? QuoteDonation(
        DaggerfallServiceProvider provider,
        DaggerfallInteriorBuilding building,
        ulong gold,
        out DaggerfallTempleServiceResult refusal)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(building);
        if (gold == 0)
        {
            refusal = DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.InsufficientFunds,
                "A temple donation must contain positive carried gold.");
            return null;
        }
        if (!TryResolveDeity(building, out int deity, out DaggerfallConcreteGuildDefinition? temple))
        {
            refusal = DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.InvalidTemple,
                "This interior is not an admitted temple provider.");
            return null;
        }
        if (!TryResolveProvider(provider, DonationProviderFactionId, DonationService, temple!, DaggerfallConcreteGuildService.Donate, out refusal))
            return null;

        int membershipFaction = DaggerfallTemplePolicy.MembershipFaction(deity);
        DaggerfallGuildEligibility membership = _social.GuildEligibility(membershipFaction);
        DaggerfallTempleBlessingTarget target = DaggerfallTemplePolicy.TryResolveTarget(deity, out var resolved)
            ? resolved : throw new InvalidOperationException("Resolved temple deity has no blessing policy.");
        int rank = membership.IsMember ? membership.Rank : -1;
        int magnitude = 0;
        int duration = 0;
        long expires = -1;
        string instance = string.Empty;
        if (target != DaggerfallTempleBlessingTarget.None)
        {
            if (!membership.IsMember)
            {
                // The donation itself remains an ordinary source service for nonmembers, but the
                // approved blessing contract explicitly denies a paid effect to nonmembers.
                target = DaggerfallTempleBlessingTarget.None;
            }
            else
            {
                magnitude = DaggerfallTemplePolicy.CalculateTempleBlessing(gold, rank);
                duration = DaggerfallTemplePolicy.BlessingDurationMinutes(gold);
                expires = checked(CurrentMinute() + duration);
                instance = $"temple-blessing.{checked(++_requestSequence)}";
            }
        }

        DaggerfallServiceRequest request = new(
            RequestId("donation"), provider);
        DaggerfallServiceQuoteResult transaction = _transactions.Quote(request, new(
            RequiresMembership: false,
            RequiredFaction: null), new DaggerfallServicePrice(gold));
        if (transaction.Quote is not { } quote)
        {
            refusal = FromServiceOutcome(transaction.Outcome);
            return null;
        }
        refusal = new(true, DaggerfallTempleServiceDenial.None, Message: "Donation quoted.");
        return new(quote, deity, membershipFaction, rank, target, magnitude, duration, expires, instance);
    }

    /// <summary>
    /// Charges one quoted donation, applies its reputation result, and starts/replaces the one
    /// shared blessing effect when the member and deity policy permit it.
    /// </summary>
    internal DaggerfallTempleServiceResult CommitDonation(DaggerfallTempleDonationQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        DaggerfallServiceOutcome payment = _transactions.Commit(quote.ServiceQuote);
        if (!payment.Accepted) return FromServiceOutcome(payment);

        if (quote.Target != DaggerfallTempleBlessingTarget.None)
        {
            DaggerfallTempleBlessingState state = new DaggerfallTempleBlessingState(quote.DeityFactionId, quote.Target,
                quote.Target == DaggerfallTempleBlessingTarget.LegalReputation
                    ? quote.ServiceQuote.Request.Provider.Site.Region : -1,
                quote.Magnitude, quote.DurationMinutes, quote.ExpiresAtMinute).Validate();
            DaggerfallEffectAdmissionOutcome started = _effects.Start(new(
                quote.BlessingInstance,
                DaggerfallTempleBlessingEffects.Key,
                BlessingEffectSource,
                CasterId: null,
                TargetId: _actors.Player.DurableId,
                Settings: "temple-blessing",
                Element: null,
                ItemId: null,
                Stacks: 1,
                RemainingRounds: checked((uint)quote.DurationMinutes),
                State: DaggerfallTempleBlessingEffects.Encode(state)));
            if (started is not (DaggerfallEffectAdmissionOutcome.Started or DaggerfallEffectAdmissionOutcome.Replaced))
                throw new InvalidOperationException($"Temple blessing admission unexpectedly returned {started} after payment.");
        }

        ApplyDonationReputation(quote.DeityFactionId, quote.ServiceQuote.Price.Gold, quote.ServiceQuote.Request.Provider.NpcId);
        string message = quote.Target == DaggerfallTempleBlessingTarget.None
            ? "Your donation is accepted."
            : $"The temple grants a {quote.Magnitude}-point blessing for {quote.DurationMinutes} minutes.";
        return new(true, DaggerfallTempleServiceDenial.None, payment.PaidGold, Message: message);
    }

    /// <summary>Quotes curing every ordinary disease and poison; transformation cures remain quest-owned.</summary>
    internal DaggerfallTempleCureQuote? QuoteCure(
        DaggerfallServiceProvider provider,
        DaggerfallInteriorBuilding building,
        out DaggerfallTempleServiceResult refusal)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(building);
        if (!TryResolveDeity(building, out int deity, out DaggerfallConcreteGuildDefinition? temple))
        {
            refusal = DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.InvalidTemple,
                "This interior is not an admitted temple provider.");
            return null;
        }
        if (!TryResolveProvider(provider, CureProviderFactionId, CureService, temple!, DaggerfallConcreteGuildService.CureDisease, out refusal))
            return null;

        int ailments = checked(DaggerfallDiseasePolicy.CountOrdinaryDiseases(_effects, _actors.Player.DurableId)
            + _poisons.CountFor(_actors.Player.DurableId));
        if (ailments == 0)
        {
            refusal = DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.NoAffliction,
                "You have no disease or poison to cure.");
            return null;
        }
        int membershipFaction = DaggerfallTemplePolicy.MembershipFaction(deity);
        DaggerfallGuildEligibility membership = _social.GuildEligibility(membershipFaction);
        int rank = membership.IsMember ? membership.Rank : 0;
        ulong price = checked((ulong)DaggerfallTemplePolicy.ReducedCureCost(
            checked(ailments * DaggerfallTemplePolicy.CurePricePerAffliction), deity, rank, membership.IsMember));
        DaggerfallServiceQuoteResult transaction = _transactions.Quote(
            new DaggerfallServiceRequest(RequestId("cure"), provider),
            new(RequiresMembership: false, RequiredFaction: null),
            new DaggerfallServicePrice(price));
        if (transaction.Quote is not { } quote)
        {
            refusal = FromServiceOutcome(transaction.Outcome);
            return null;
        }
        refusal = new(true, DaggerfallTempleServiceDenial.None, Message: "Cure quoted.");
        return new(quote, deity, ailments, price);
    }

    /// <summary>Charges a cure quote, then removes the actual common effect instances.</summary>
    internal DaggerfallTempleServiceResult CommitCure(DaggerfallTempleCureQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        DaggerfallServiceOutcome payment = _transactions.Commit(quote.ServiceQuote);
        if (!payment.Accepted) return FromServiceOutcome(payment);
        int removed = DaggerfallDiseasePolicy.CureOrdinaryDiseases(_effects, _actors.Player.DurableId);
        bool poisonRemoved = _poisons.Cure(_actors.Player.Actor);
        if (poisonRemoved) removed = checked(removed + 1);
        return new(true, DaggerfallTempleServiceDenial.None, payment.PaidGold, removed,
            "The temple cures your active afflictions.");
    }

    private bool TryResolveProvider(
        DaggerfallServiceProvider provider,
        int providerFaction,
        string service,
        DaggerfallConcreteGuildDefinition temple,
        DaggerfallConcreteGuildService concreteService,
        out DaggerfallTempleServiceResult refusal)
    {
        if (provider.Service != service)
        {
            refusal = DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.ServiceUnavailable,
                "That person does not offer this temple service.");
            return false;
        }
        DaggerfallConcreteGuildServiceRuntimeDecision decision = _guildServices.Evaluate(
            temple.FactionId,
            concreteService,
            checked((int)_calendar().DayNumber),
            new(provider, provider.Site.Region));
        if (decision.Policy.ProviderFactionId != providerFaction || !decision.CanUse)
        {
            refusal = decision.ProviderAvailability switch
            {
                DaggerfallGuildProviderAvailability.Unknown => DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.UnknownProvider, "That temple provider is unavailable."),
                DaggerfallGuildProviderAvailability.FactionMismatch => DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.ServiceUnavailable, "That person is not the source temple provider."),
                DaggerfallGuildProviderAvailability.Unavailable => DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.ProviderUnavailable, "That temple provider is unavailable."),
                _ when decision.Policy.Denial == DaggerfallGuildServiceDenial.NotMember => DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.NotMember, "You are not admitted to this temple."),
                _ => DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.ServiceUnavailable, "This temple service is unavailable."),
            };
            return false;
        }
        try { _npcs.Require(provider.NpcId); }
        catch (InvalidOperationException)
        {
            refusal = DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.UnknownProvider,
                "That temple provider is no longer present.");
            return false;
        }
        refusal = new(true, DaggerfallTempleServiceDenial.None);
        return true;
    }

    private bool TryResolveDeity(DaggerfallInteriorBuilding building, out int deity,
        out DaggerfallConcreteGuildDefinition? temple)
    {
        deity = 0;
        temple = null;
        if (building.BuildingType != 14) return false;
        try
        {
            // The normalized interior carries the authored group-17 temple faction (241, 243,
            // ...), while that definition's parent carries the deity root used by the social
            // table (Arkay, Zenithar, ...). Resolve both facts from the catalog instead of
            // treating a building membership faction as a deity identity.
            temple = DaggerfallConcreteGuildCatalog.ForFaction(building.FactionId);
            if (temple.MembershipKind != DaggerfallGuildMembershipKind.TempleDeity)
            {
                temple = null;
                return false;
            }
            deity = temple.ParentFactionId;
        }
        catch (ArgumentOutOfRangeException) { return false; }
        return DaggerfallTemplePolicy.TryResolveTarget(deity, out _);
    }

    private void ApplyDonationReputation(int deityFactionId, ulong gold, long providerId)
    {
        int reputation = Math.Abs(_social.FactionReputation(deityFactionId));
        ulong denominator = (ulong)Math.Max(reputation, 1);
        ulong chance = gold > (ulong.MaxValue - 1) / 2
            ? 100
            : Math.Min(100UL, checked((gold * 2) / denominator + 1));
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(
            0, "daggerfall.temple.donation", $"{providerId}:{CurrentMinute()}:{gold}", 1, 100)).Value);
        if ((ulong)roll <= chance) _social.ChangeFactionReputation(deityFactionId, 1, DaggerfallFactionReputationChange.Direct);
    }

    private string RequestId(string kind) => $"temple.{kind}.{checked(++_requestSequence)}";

    private long CurrentMinute() => _calendar().ToAbsoluteSeconds() / DaggerfallCalendar.SecondsPerMinute;

    private static DaggerfallTempleServiceResult FromServiceOutcome(DaggerfallServiceOutcome outcome) =>
        DaggerfallTempleServiceResult.Refused(outcome.Denial switch
        {
            DaggerfallServiceDenial.UnknownProvider => DaggerfallTempleServiceDenial.UnknownProvider,
            DaggerfallServiceDenial.ProviderUnavailable or DaggerfallServiceDenial.SiteChanged => DaggerfallTempleServiceDenial.ProviderUnavailable,
            DaggerfallServiceDenial.ServiceUnavailable => DaggerfallTempleServiceDenial.ServiceUnavailable,
            DaggerfallServiceDenial.InsufficientFunds => DaggerfallTempleServiceDenial.InsufficientFunds,
            _ => DaggerfallTempleServiceDenial.ServiceUnavailable,
        }, outcome.Denial.ToString());
}

