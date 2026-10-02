using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// Owned property's storage containers, built beside the property state and restored from the save's
    /// property section at construction, so persistence never invents a container.
    /// </summary>
    private readonly DaggerfallPropertyStorage _propertyStorage;

    internal DaggerfallPropertyStorage PropertyStorage => _propertyStorage;

    internal DaggerfallInventorySave CapturePropertyStorage(DaggerfallPropertyStorageKey key) =>
        PropertyStorage.Capture(key);

    internal InventoryView ReadPropertyStorage(DaggerfallPropertyStorageKey key) =>
        PropertyStorage.Read(key);

    private DaggerfallPropertyBankSettlementAdapter PropertySettlement() =>
        new(State.Bank, State.Currency);

    /// <summary>
    /// Prices only candidates admitted by the caller's normalized building directory. The method
    /// requires the active geographic site so a profile name cannot accidentally become a property
    /// identity.
    /// </summary>
    internal IReadOnlyList<DaggerfallHouseOffer> ReadPropertyHouseOffers(
        IEnumerable<DaggerfallHouseCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        DaggerfallSiteId site = _site.Active
            ?? throw new InvalidOperationException("House offers require an active Daggerfall site.");
        DaggerfallHouseCandidate[] admitted = candidates.Select(candidate => candidate.Validate()).ToArray();
        if (admitted.Any(candidate => candidate.Site != site))
            throw new ArgumentException("House offers must name the active geographic site.", nameof(candidates));
        return DaggerfallPropertyPolicy.HousesForSale(admitted, State.Property.Tuning);
    }

    /// <summary>Reads source-backed ship deeds from an explicitly admitted port-town fact.</summary>
    internal IReadOnlyList<DaggerfallShipOffer> ReadPropertyShipOffers(bool atPortTown)
    {
        int region = _site.Region
            ?? throw new InvalidOperationException("Ship offers require an active Daggerfall region.");
        return DaggerfallPropertyPolicy.ShipsForSale(atPortTown, region, State.Property.Tuning);
    }

    internal DaggerfallPropertyTransactionResult PurchasePropertyHouse(DaggerfallHouseOffer offer) =>
        State.Property.PurchaseHouse(offer, PropertySettlement());

    internal DaggerfallPropertyTransactionResult SellPropertyHouse(DaggerfallHouseOffer offer) =>
        State.Property.SellHouse(offer, PropertySettlement());

    internal DaggerfallPropertyTransactionResult PurchasePropertyShip(DaggerfallShipOffer offer) =>
        State.Property.PurchaseShip(offer, PropertySettlement());

    internal DaggerfallPropertyTransactionResult SellPropertyShip() =>
        State.Property.SellShip(_site.Region ?? throw new InvalidOperationException(
            "Selling a ship requires an active Daggerfall region."), PropertySettlement());

    internal DaggerfallPropertyAccessResult CheckPropertyHouseAccess(DaggerfallHouseEntryContext requested) =>
        State.Property.HouseAccess(requested);

    internal DaggerfallPropertyAccessResult CheckPropertyShipStorage() =>
        State.Property.ShipStorage(State.Transport.OnShip);

    /// <summary>
    /// Applies the property gate before invoking the existing transport state. The transport owner
    /// still performs pose validation and durable on-ship state changes.
    /// </summary>
    internal DaggerfallTransportActionResult BoardOwnedPropertyShip(
        DaggerfallTransportAccessContext context, DaggerfallTransportPose? currentPose, DaggerfallWorldProfileKey? returnProfile = null)
    {
        if (PropertyShipBoardingRefusal(context) is { } refusal) return refusal;
        return State.Transport.BoardShip(State.Property.OwnsShip, context, currentPose, returnProfile ?? _activeProfileKey);
    }

    private DaggerfallTransportActionResult? PropertyShipBoardingRefusal(DaggerfallTransportAccessContext context)
    {
        DaggerfallPropertyAccessResult access = State.Property.ShipBoarding(
            new DaggerfallShipAccessContext(context.IsIndoor, context.IsDungeon, context.ShipAccessAllowed));
        if (!access.Allowed)
            return new(false, State.Transport.Mode, State.Transport.OnShip,
                access.Denial switch
                {
                    DaggerfallPropertyAccessDenial.MissingOwnership => DaggerfallTransportRejection.MissingShip,
                    DaggerfallPropertyAccessDenial.Indoor => DaggerfallTransportRejection.Indoor,
                    DaggerfallPropertyAccessDenial.Dungeon
                        or DaggerfallPropertyAccessDenial.ShipUnavailableAtSite => DaggerfallTransportRejection.ShipUnavailableAtSite,
                    _ => DaggerfallTransportRejection.ShipUnavailableAtSite,
                }, access.Message);
        return null;
    }

    internal DaggerfallShipArrivalAnchor? PropertyShipArrival() => State.Property.OwnedShip is { } ship
        ? DaggerfallPropertyPolicy.ShipArrival(ship, State.Property.Tuning)
        : null;
}
