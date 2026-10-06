using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallPropertyOfferView(string Key, string Name, string Price, string SalePrice, bool Owned, bool CanBuy, bool CanSell, bool CanEnter)
{
    /// <summary>Whether this property is a house the player can walk into once owned; ships are boarded instead.</summary>
    internal bool Enterable { get; init; }
}
internal sealed record DaggerfallPropertyStorageView(string Key, string Revision, DaggerfallWagonItemPresentation[] Items);
internal sealed record DaggerfallPropertyView(bool BankAvailable, DaggerfallPropertyOfferView[] Offers, DaggerfallPropertyStorageView? Storage);

internal sealed partial class DaggerfallSession
{
    // The donor uses a monthly random subset of ordinary houses plus all explicit HouseForSale
    // buildings. Keep the selection stable by durable site and month, not Unity's object hash.
    private DaggerfallHouseOffer[] CurrentHouseOffers(bool includeOwned = false)
    {
        if (_site.ActiveSite is not { Exterior: { } exterior } site) return [];
        DaggerfallHouseCandidate[] candidates = [.. exterior.Buildings.Values
            .Where(building => building.ModelRadius is > 0)
            .OrderBy(building => building.Id.BlockY).ThenBy(building => building.Id.BlockX).ThenBy(building => building.Id.Index)
            .Select(building => new DaggerfallHouseCandidate(site.Id, building.Source.Id,
                building.Source.BuildingType, site.Kind, building.ModelRadius!.Value,
                State.Quests.ClaimsBuilding(site.Id, building), building.Id.BlockX, building.Id.BlockY))];
        List<DaggerfallHouseCandidate> selected = [.. candidates.Where(value => value.BuildingType == 1)];
        List<DaggerfallHouseCandidate> ordinary = [.. candidates.Where(value => value.BuildingType is >= 17 and <= 20 && !value.IsQuestBuilding)];
        int limit = Math.Min(exterior.Buildings.Count / 10, 20);
        for (int draw = 0; selected.Count < limit && ordinary.Count > 0; draw++)
        {
            int index = checked((int)_engine.Random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.property.houses",
                $"{site.Id}/{_time.Calendar.Month}/{draw}", 0, ordinary.Count - 1)).Value);
            selected.Add(ordinary[index]); ordinary.RemoveAt(index);
        }
        if (includeOwned) selected.AddRange(candidates.Where(value => State.Property.OwnsHouse(value.Identity)));
        return [.. ReadPropertyHouseOffers(selected.DistinctBy(value => value.Identity), includeOwned)];
    }

    private DaggerfallShipOffer[] CurrentShipOffers() => _site.ActiveSite is { Exterior.PortTownAndUnknown: > 0 }
        ? [.. ReadPropertyShipOffers(true)] : [];

    private DaggerfallWorldProfileKey? HouseProfile(DaggerfallHouseIdentity house) => _sites.Profiles?.Keys
        .Where(key => key.Site == house.Site && key.Kind == DaggerfallWorldProfileKind.Interior)
        .Where(key => _sites.Profiles.Require(key).InteriorBuilding is { } placed && placed.Building == house.Building
            && placed.BlockX == house.BlockX && placed.BlockY == house.BlockY)
        .Select(key => (DaggerfallWorldProfileKey?)key).SingleOrDefault();

    private DaggerfallWorldProfileKey? ResolveOwnedShipProfile()
    {
        if (PropertyShipArrival() is not { } arrival || _sites.Profiles is null) return null;
        DaggerfallSiteRecord[] ships = [.. _site.Records.Where(site => site.Kind == DaggerfallSiteKind.HomeYourShips
            && site.MapPixelX == arrival.MapPixelX && site.MapPixelY == arrival.MapPixelY)];
        if (ships.Length != 1) return null;
        DaggerfallWorldProfileKey[] profiles = [.. _sites.Profiles.Keys.Where(key => key.Site == ships[0].Id
            && key.Kind == DaggerfallWorldProfileKind.Exterior && _sites.Profiles.Require(key).Anchors.ContainsKey("start"))];
        return profiles.Length == 1 ? profiles[0] : null;
    }

    private DaggerfallPropertyStorageKey? AccessiblePropertyStorage()
    {
        if (State.Transport.OnShip && State.Property.OwnedShip is { } ship && ResolveOwnedShipProfile()?.Site == _activeProfileKey.Site)
            return DaggerfallPropertyStorageKey.ForShip(ship);
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior && CurrentInteriorBuilding() is { } placed)
        {
            DaggerfallHouseIdentity house = new(_activeProfileKey.Site, placed.Building, placed.BlockX, placed.BlockY);
            if (State.Property.OwnsHouse(house)) return DaggerfallPropertyStorageKey.ForHouse(house);
        }
        return null;
    }

    internal DaggerfallPropertyView ReadPropertyPresentation()
    {
        bool bank = ActiveBankRegion() is not null;
        List<DaggerfallPropertyOfferView> offers = [];
        foreach (DaggerfallHouseOffer offer in bank || State.Property.OwnedHouses.Any(house => house.Site == _site.Active)
            ? CurrentHouseOffers(includeOwned: true) : [])
        {
            bool owned = State.Property.OwnsHouse(offer.Identity);
            string name = _site.RequireBuilding(offer.Identity.Site,
                new(offer.Identity.BlockX, offer.Identity.BlockY, offer.Identity.Building.Index)).Name;
            offers.Add(new(offer.StorageKey.Value, name, offer.Price.ToString(CultureInfo.InvariantCulture),
                offer.SalePrice.ToString(CultureInfo.InvariantCulture), owned,
                bank && HouseProfile(offer.Identity) is not null && !State.Property.OwnsHouseInRegion(offer.Identity.Site.Region), bank && owned,
                owned && CanEnterProperty(offer.Identity)) { Enterable = true });
        }
        foreach (DaggerfallShipOffer offer in CurrentShipOffers())
            offers.Add(new(offer.StorageKey.Value, $"{offer.Type} ship", offer.Price.ToString(CultureInfo.InvariantCulture),
                offer.SalePrice.ToString(CultureInfo.InvariantCulture), State.Property.OwnedShip == offer.Type,
                bank && !State.Property.OwnsShip, bank && State.Property.OwnedShip == offer.Type && !State.Transport.OnShip, false));
        if (State.Property.OwnedShip is { } ownedShip && !offers.Any(offer => offer.Key == DaggerfallPropertyStorageKey.ForShip(ownedShip).Value))
        {
            ulong price = DaggerfallPropertyPolicy.ShipPrice(ownedShip, State.Property.Tuning);
            offers.Add(new(DaggerfallPropertyStorageKey.ForShip(ownedShip).Value, $"{ownedShip} ship",
                price.ToString(CultureInfo.InvariantCulture), DaggerfallPropertyPolicy.SalePrice(price, State.Property.Tuning).ToString(CultureInfo.InvariantCulture),
                true, false, bank && !State.Transport.OnShip, false));
        }
        DaggerfallPropertyStorageView? storage = null;
        if (AccessiblePropertyStorage() is { } key)
        {
            InventoryView contents = PropertyStorage.Read(key);
            storage = new(key.Value, contents.StoreRevision.ToString(CultureInfo.InvariantCulture), PropertyItems(contents));
        }
        return new(bank, [.. offers], storage);
    }

    private bool CanEnterProperty(DaggerfallHouseIdentity house) => _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior
        && HouseProfile(house) is { } profile && State.PlayerControl.Position is { } position
        && _sites.Projection.Portals.All.Any(value => value.Portal.DestinationLogicalProfile == profile.LogicalId
            && System.Numerics.Vector3.Distance(position.ToVector(), value.Portal.Position.ToVector()) <= value.Portal.Radius);

    internal void ChangeProperty(DaggerfallPlayerUiAction action)
    {
        if (action.Kind is DaggerfallUiActionKind.PropertyPut or DaggerfallUiActionKind.PropertyTake)
        { ChangePropertyStorage(action); return; }
        DaggerfallHouseOffer? house = CurrentHouseOffers(includeOwned: true).SingleOrDefault(offer => offer.StorageKey.Value == action.Key);
        if (action.Kind == DaggerfallUiActionKind.PropertyEnter)
        {
            if (house is null || !State.Property.OwnsHouse(house.Identity) || !CanEnterProperty(house.Identity))
            { Presentation.SetOutcome("Stand at your house entrance to enter the admitted interior."); return; }
            Presentation.SetOutcome(TryTransitionTo(HouseProfile(house.Identity)!.Value) ? "Entered your house." : "The house interior is unavailable.");
            return;
        }
        if (ActiveBankRegion() is null) { Presentation.SetOutcome("A bank service is not available here."); return; }
        DaggerfallShipOffer? ship = CurrentShipOffers().SingleOrDefault(offer => offer.StorageKey.Value == action.Key);
        DaggerfallPropertyTransactionResult? result = action.Kind switch
        {
            DaggerfallUiActionKind.PropertyBuy when house is not null && HouseProfile(house.Identity) is not null => PurchasePropertyHouse(house),
            DaggerfallUiActionKind.PropertySell when house is not null => SellPropertyHouse(house),
            DaggerfallUiActionKind.PropertyBuy when ship is not null => PurchasePropertyShip(ship),
            DaggerfallUiActionKind.PropertySell when State.Property.OwnedShip is { } owned
                && action.Key == DaggerfallPropertyStorageKey.ForShip(owned).Value && !State.Transport.OnShip => SellPropertyShip(),
            _ => null,
        };
        Presentation.SetOutcome(result?.Message ?? "That property offer is no longer available.");
    }

    private void ChangePropertyStorage(DaggerfallPlayerUiAction action)
    {
        if (AccessiblePropertyStorage() is not { } key || key.Value != action.Key)
        { Presentation.SetOutcome("You must be inside your property to use its storage."); return; }
        bool put = action.Kind == DaggerfallUiActionKind.PropertyPut;
        InventoryView source = put ? State.Inventory.Read() : PropertyStorage.Read(key);
        if (!ulong.TryParse(action.Revision, out ulong revision) || source.StoreRevision != revision
            || action.Item is null || !TryWagonSelection(source, action.Item, action.Amount, out InventoryContainerSelection? selection))
        { Presentation.SetOutcome("Inventory changed. Choose the item again."); return; }
        if (put && _inventoryUi.Read().Items.Any(item => item.Key == action.Item && item.EquippedSlots.Length > 0))
        { Presentation.SetOutcome("Unequip that item before storing it."); return; }
        try
        {
            if (put) PropertyStorage.TransferTo(key, selection!, revision);
            else PropertyStorage.TransferFrom(key, selection!, revision);
            Presentation.SetOutcome(put ? "Item stored in property." : "Item taken from property.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { Presentation.SetOutcome(put ? "That item cannot be stored here." : "That item cannot be taken from storage."); }
    }

    private DaggerfallWagonItemPresentation[] PropertyItems(InventoryView contents) =>
        [.. contents.UniqueItems.Select(item => new DaggerfallWagonItemPresentation($"unique:{item.Entity.Value}", item.Definition.Value, "1", ItemDefinitionName(item.Definition.Value))),
            .. contents.Stacks.Select(item => new DaggerfallWagonItemPresentation($"stack:{item.Id.Value}", item.Definition.Value, item.Quantity.ToString(CultureInfo.InvariantCulture), ItemDefinitionName(item.Definition.Value)))];

    private DaggerfallTransportActionResult BoardPropertyShip()
    {
        DaggerfallTransportAccessContext context = TransportAccess();
        if (State.Transport.OnShip) return new(false, State.Transport.Mode, true, DaggerfallTransportRejection.ShipUnavailableAtSite, "You are already aboard your ship.");
        if (PropertyShipBoardingRefusal(context) is { } refused) return refused;
        if (!context.ShipAccessAllowed || ResolveOwnedShipProfile() is not { } ship || State.PlayerControl.Position is null)
            return new(false, State.Transport.Mode, State.Transport.OnShip, DaggerfallTransportRejection.ShipUnavailableAtSite, "No owned ship world is available here.");
        _sites.NormalizeExteriorOrigin();
        DaggerfallWorldProfileKey land = _activeProfileKey;
        DaggerfallTransportPose pose = new(State.PlayerControl.Position!.Value, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
        // Admit the real ship projection before changing transport ownership or return state.
        if (!_sites.TryRelocate(new(ship, "start")))
            return new(false, State.Transport.Mode, State.Transport.OnShip, DaggerfallTransportRejection.ShipUnavailableAtSite, "The ship world is unavailable.");
        _sites.ClearReturnDestination();
        return BoardOwnedPropertyShip(context, pose, land);
    }

    private DaggerfallTransportActionResult LeavePropertyShip()
    {
        if (State.Transport.ShipReturnProfile is not { } land || State.Transport.ShipReturnPose is null)
            return State.Transport.LeaveShip();
        // Retire the return relation only after the land projection has actually been readmitted.
        if (!_sites.TryTransitionTo(land))
            return new(false, State.Transport.Mode, true, DaggerfallTransportRejection.ShipUnavailableAtSite, "The saved land destination is unavailable.");
        _sites.ClearReturnDestination();
        return State.Transport.LeaveShip();
    }
}
