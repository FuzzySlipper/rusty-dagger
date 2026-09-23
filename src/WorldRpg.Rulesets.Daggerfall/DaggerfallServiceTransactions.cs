using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The exact Daggerfall service provider a request addresses.</summary>
internal sealed record DaggerfallServiceProvider(long NpcId, DaggerfallNpcSite Site, string Service)
{
    internal void Validate()
    {
        if (NpcId <= 0 || string.IsNullOrWhiteSpace(Service))
            throw new ArgumentException("A service provider requires an NPC and a named service.");
        if (Site.Region is < 0 or > 61 || string.IsNullOrWhiteSpace(Site.Location))
            throw new ArgumentException("A service provider requires a classic NPC site.");
    }
}

/// <summary>A selected current item and its minimum usable condition, when a service acts on an item.</summary>
internal sealed record DaggerfallServiceItemReference(ulong DurableItemId, string Definition, int MinimumCondition = 0)
{
    internal void Validate()
    {
        if (DurableItemId == 0 || string.IsNullOrWhiteSpace(Definition) || MinimumCondition < 0)
            throw new ArgumentException("A service item reference is incomplete.");
    }
}

/// <summary>Daggerfall policy supplied by a concrete service before it asks the shared coordinator for a quote.</summary>
internal sealed record DaggerfallServiceEligibility(
    bool RequiresMembership,
    int MinimumRank = 0,
    int? RequiredFaction = null,
    int OpensAtHour = 0,
    int ClosesAtHour = DaggerfallCalendar.HoursPerDay)
{
    internal void Validate()
    {
        if (MinimumRank < 0 || OpensAtHour is < 0 or >= DaggerfallCalendar.HoursPerDay
            || ClosesAtHour is < 1 or > DaggerfallCalendar.HoursPerDay || RequiredFaction < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumRank), "Service eligibility has an invalid rank, faction, or hour.");
        if (RequiresMembership && RequiredFaction is null)
            throw new ArgumentException("Membership eligibility requires a faction.");
    }
}

/// <summary>One Daggerfall price in immediately spendable carried gold.</summary>
internal readonly record struct DaggerfallServicePrice(ulong Gold);

/// <summary>One item materialized into the player inventory as part of a service result.</summary>
internal sealed record DaggerfallServiceGrant(InventoryAtomicGrant Grant, DaggerfallItemInstanceMetadata Metadata)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Grant);
        ArgumentNullException.ThrowIfNull(Metadata);
        Grant.Validate();
        Metadata.Validate();
        if (Metadata.Owner != DaggerfallItemOwner.Player || !string.Equals(Grant.Item.Value, Metadata.ItemId, StringComparison.Ordinal))
            throw new ArgumentException("A service grant must materialize matching player-owned item meaning.");
    }
}

/// <summary>Concrete future work which a service has accepted and must retain across save/load.</summary>
internal sealed record DaggerfallServiceQueuedWork(string Id, string Service, DaggerfallServiceProvider Provider, long CompletesAtMinute)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Service))
            throw new ArgumentException("Queued service work is incomplete.");
        Provider.Validate();
        if (!string.Equals(Service, Provider.Service, StringComparison.Ordinal))
            throw new ArgumentException("Queued work must retain its provider service identity.");
    }
}

/// <summary>The service-specific request a UI, dialogue, or world interaction submits.</summary>
internal sealed record DaggerfallServiceRequest(string Id, DaggerfallServiceProvider Provider, DaggerfallServiceItemReference? Item = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A service request requires an id.");
        Provider.Validate();
        Item?.Validate();
    }
}

internal enum DaggerfallServiceDenial
{
    None,
    UnknownProvider,
    ProviderUnavailable,
    ServiceUnavailable,
    SiteChanged,
    Closed,
    NotMember,
    InsufficientRank,
    MissingItem,
    ItemChanged,
    InsufficientFunds,
    GrantUnavailable,
    AlreadySubmitted,
    WorkNotReady,
    WorkUnavailable,
}

/// <summary>The actual result of requesting, quoting, or committing one service.</summary>
internal sealed record DaggerfallServiceOutcome(
    bool Accepted,
    DaggerfallServiceDenial Denial,
    ulong PaidGold = 0,
    IReadOnlyList<string>? GrantedItems = null,
    DaggerfallServiceQueuedWork? QueuedWork = null)
{
    internal static DaggerfallServiceOutcome Refused(DaggerfallServiceDenial denial) => new(false, denial);
    internal static DaggerfallServiceOutcome Quoted() => new(true, DaggerfallServiceDenial.None);
}

/// <summary>A quote retains typed requirements, not a mutable service callback or UI command.</summary>
internal sealed record DaggerfallServiceQuote(
    DaggerfallServiceRequest Request,
    DaggerfallServiceEligibility Eligibility,
    DaggerfallServicePrice Price,
    IReadOnlyList<DaggerfallServiceGrant> Grants,
    DaggerfallServiceQueuedWork? QueuedWork,
    DaggerfallCalendar QuotedAt);

internal sealed record DaggerfallServiceQuoteResult(DaggerfallServiceQuote? Quote, DaggerfallServiceOutcome Outcome);

/// <summary>Persisted pending service work. Completed service work needs no receipt ledger.</summary>
internal sealed record DaggerfallServiceStateSave(DaggerfallServiceQueuedWork[] Pending)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Pending);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallServiceQueuedWork work in Pending)
        {
            ArgumentNullException.ThrowIfNull(work);
            work.Validate();
            if (!ids.Add(work.Id)) throw new ArgumentException("Pending service work must have distinct ids.");
        }
    }
}

/// <summary>
/// Shared Daggerfall service admission and transaction coordination. Concrete services provide
/// their own pricing, grants, and queued-work meaning; this owner only refuses stale eligibility
/// and commits the established item/money operations through the named inventory and currency owners.
/// </summary>
internal sealed class DaggerfallServiceTransactions
{
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallSocialState _social;
    private readonly MechanicsInventoryCoordinator _inventory;
    private readonly DaggerfallItemInstances _instances;
    private readonly DaggerfallCurrencyService _currency;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly Func<DaggerfallCalendar> _calendar;
    private readonly Func<DaggerfallNpcSite?> _currentSite;
    private readonly Dictionary<string, DaggerfallServiceQueuedWork> _pending = [];
    private readonly HashSet<string> _submitted = new(StringComparer.Ordinal);

    internal DaggerfallServiceTransactions(DaggerfallNpcRegistry npcs, DaggerfallSocialState social,
        MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances, DaggerfallCurrencyService currency,
        DaggerfallUniqueItemAllocator uniqueItems, Func<DaggerfallCalendar> calendar, Func<DaggerfallNpcSite?> currentSite,
        DaggerfallServiceStateSave? restored = null)
    {
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _currentSite = currentSite ?? throw new ArgumentNullException(nameof(currentSite));
        if (restored is not null)
        {
            restored.Validate();
            foreach (DaggerfallServiceQueuedWork work in restored.Pending)
                _pending.Add(work.Id, work);
        }
    }

    internal IReadOnlyList<DaggerfallServiceQueuedWork> Pending => [.. _pending.Values.OrderBy(work => work.CompletesAtMinute).ThenBy(work => work.Id, StringComparer.Ordinal)];

    internal DaggerfallServiceQuoteResult Quote(DaggerfallServiceRequest request, DaggerfallServiceEligibility eligibility,
        DaggerfallServicePrice price, IEnumerable<DaggerfallServiceGrant>? grants = null, DaggerfallServiceQueuedWork? queuedWork = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(eligibility);
        request.Validate();
        eligibility.Validate();
        if (_submitted.Contains(request.Id) || _pending.ContainsKey(request.Id))
            return new(null, DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.AlreadySubmitted));
        DaggerfallServiceGrant[] awarded = (grants ?? []).Select(grant => { grant.Validate(); return grant; }).ToArray();
        queuedWork?.Validate();
        if (queuedWork is not null && !string.Equals(queuedWork.Id, request.Id, StringComparison.Ordinal))
            throw new ArgumentException("Queued service work must use its request identity.", nameof(queuedWork));

        DaggerfallServiceDenial denial = Admit(request, eligibility);
        if (denial != DaggerfallServiceDenial.None) return new(null, DaggerfallServiceOutcome.Refused(denial));
        if (_currency.Read().Gold < price.Gold) return new(null, DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.InsufficientFunds));
        if (!GrantsAvailable(awarded)) return new(null, DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.GrantUnavailable));
        return new(new(request, eligibility, price, Array.AsReadOnly(awarded), queuedWork, _calendar()), DaggerfallServiceOutcome.Quoted());
    }

    internal DaggerfallServiceOutcome Commit(DaggerfallServiceQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        quote.Request.Validate();
        quote.Eligibility.Validate();
        quote.QueuedWork?.Validate();
        if (quote.QueuedWork is { } queued && (queued.Id != quote.Request.Id || queued.Provider != quote.Request.Provider
            || !string.Equals(queued.Service, quote.Request.Provider.Service, StringComparison.Ordinal)))
            return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.GrantUnavailable);
        if (_pending.ContainsKey(quote.Request.Id)) return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.AlreadySubmitted);
        if (_submitted.Contains(quote.Request.Id)) return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.AlreadySubmitted);

        DaggerfallServiceDenial denial = Admit(quote.Request, quote.Eligibility);
        if (denial != DaggerfallServiceDenial.None) return DaggerfallServiceOutcome.Refused(denial);
        if (!GrantsAvailable(quote.Grants)) return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.GrantUnavailable);
        try
        {
            if (!_currency.TrySpendGold(quote.Price.Gold, quote.Grants.Select(grant => grant.Grant)))
                return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.InsufficientFunds);
        }
        catch (Exception error) when (error is MechanicsException or InvalidOperationException or ArgumentException)
        {
            // The Engine discarded its detached candidate, so an unavailable award cannot debit
            // the player.  The concrete service receives a denial rather than an exception that
            // a DOM caller could mistake for a completed purchase.
            return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.GrantUnavailable);
        }

        // Every following operation was prevalidated against the same single-threaded admitted update;
        // none can reject after the Engine candidate publishes, so the payment, grants, and queue are
        // one concrete committed result rather than a compensating rollback protocol.
        foreach (DaggerfallServiceGrant grant in quote.Grants) RegisterGrant(grant);
        if (quote.QueuedWork is { } acceptedWork) _pending.Add(acceptedWork.Id, acceptedWork);
        _submitted.Add(quote.Request.Id);
        return new(true, DaggerfallServiceDenial.None, quote.Price.Gold,
            quote.Grants.Select(grant => grant.Grant.Item.Value).ToArray(), quote.QueuedWork);
    }

    internal DaggerfallServiceStateSave Capture() => new([.. Pending]);

    /// <summary>
    /// Completes one accepted concrete service work item only when its own due minute has arrived
    /// and the player is again at the live provider.  Concrete services perform their named final
    /// mutation before calling this removal; this owner never fabricates a repair, training, or cure.
    /// </summary>
    internal DaggerfallServiceOutcome CompletePending(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (!_pending.TryGetValue(requestId, out DaggerfallServiceQueuedWork? work))
            return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.WorkUnavailable);
        if (CurrentMinute() < work.CompletesAtMinute)
            return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.WorkNotReady);
        if (ProviderAvailable(work.Provider) != DaggerfallServiceDenial.None)
            return DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.WorkUnavailable);
        _pending.Remove(requestId);
        return new(true, DaggerfallServiceDenial.None, QueuedWork: work);
    }

    private DaggerfallServiceDenial Admit(DaggerfallServiceRequest request, DaggerfallServiceEligibility eligibility)
    {
        DaggerfallServiceDenial provider = ProviderAvailable(request.Provider);
        if (provider != DaggerfallServiceDenial.None) return provider;
        if (!OpenAt(_calendar().Hour, eligibility)) return DaggerfallServiceDenial.Closed;
        if (eligibility.RequiresMembership)
        {
            DaggerfallGuildEligibility member = _social.GuildEligibility(eligibility.RequiredFaction!.Value);
            if (!member.IsMember) return DaggerfallServiceDenial.NotMember;
            if (member.Rank < eligibility.MinimumRank) return DaggerfallServiceDenial.InsufficientRank;
        }
        return ItemAvailable(request.Item) ? DaggerfallServiceDenial.None : DaggerfallServiceDenial.ItemChanged;
    }

    /// <summary>Revalidates an already selected provider against the live NPC and site.</summary>
    internal DaggerfallServiceDenial ProviderAvailable(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = _npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return DaggerfallServiceDenial.UnknownProvider; }
        if (npc.Presence != DaggerfallNpcPresence.Active) return DaggerfallServiceDenial.ProviderUnavailable;
        if (npc.Site != provider.Site) return DaggerfallServiceDenial.SiteChanged;
        if (!npc.Services.Contains(provider.Service, StringComparer.Ordinal)) return DaggerfallServiceDenial.ServiceUnavailable;
        DaggerfallNpcSite? current = _currentSite();
        return current is { } site && site.Region == provider.Site.Region
            && string.Equals(site.Location, provider.Site.Location, StringComparison.Ordinal)
            ? DaggerfallServiceDenial.None
            : DaggerfallServiceDenial.SiteChanged;
    }

    private long CurrentMinute()
    {
        DaggerfallCalendar value = _calendar();
        return checked((value.DayNumber * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)
            + (value.Hour * DaggerfallCalendar.MinutesPerHour) + value.Minute);
    }

    private bool ItemAvailable(DaggerfallServiceItemReference? required)
    {
        if (required is null) return true;
        Rusty.Engine.Mechanics.UniqueInventoryItem[] items = _inventory.Read().UniqueItems.Where(item =>
            _inventory.GetDurableItemId(item.Entity).Value == required.DurableItemId).ToArray();
        if (items.Length != 1 || !string.Equals(items[0].Definition.Value, required.Definition, StringComparison.Ordinal)) return false;
        DaggerfallItemInstanceMetadata metadata;
        try { metadata = _instances.RequireUnique(required.DurableItemId); }
        catch (InvalidOperationException) { return false; }
        return metadata.Owner == DaggerfallItemOwner.Player && metadata.CurrentCondition >= required.MinimumCondition;
    }

    private bool GrantsAvailable(IEnumerable<DaggerfallServiceGrant> grants)
    {
        HashSet<DurableIdentityReference> unique = [];
        HashSet<InventoryStackId> stacks = [];
        foreach (DaggerfallServiceGrant grant in grants)
        {
            grant.Validate();
            if (grant.Grant.UniqueItem is DurableIdentityReference identity)
            {
                if (!unique.Add(identity) || !_uniqueItems.ReservedEntityIds.Contains(identity.Value)
                    || _inventory.Entities.TryResolve(identity, out _) || _instances.ContainsUnique(identity.Value)) return false;
            }
            else if (!stacks.Add(grant.Grant.Stack!) || _inventory.Read().Stacks.Any(stack => stack.Id == grant.Grant.Stack)
                || _instances.ContainsStack(DaggerfallItemOwner.Player, grant.Grant.Stack!)) return false;
        }
        return true;
    }

    private void RegisterGrant(DaggerfallServiceGrant grant)
    {
        if (grant.Grant.UniqueItem is DurableIdentityReference identity)
            _instances.RegisterUnique(identity.Value, grant.Metadata);
        else
            _instances.RegisterStack(DaggerfallItemOwner.Player, grant.Grant.Stack!, grant.Metadata);
    }

    private static bool OpenAt(int hour, DaggerfallServiceEligibility eligibility)
    {
        if (eligibility.OpensAtHour == 0 && eligibility.ClosesAtHour == DaggerfallCalendar.HoursPerDay) return true;
        return eligibility.OpensAtHour < eligibility.ClosesAtHour
            ? hour >= eligibility.OpensAtHour && hour < eligibility.ClosesAtHour
            : hour >= eligibility.OpensAtHour || hour < eligibility.ClosesAtHour;
    }
}
