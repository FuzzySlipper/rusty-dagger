using System.Globalization;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A house or ship the property panel offers, and what the player can do with it here.</summary>
internal sealed record DaggerfallPropertyOfferView(string Key, string Name, string Price, string SalePrice, bool Owned, bool CanBuy, bool CanSell, bool CanEnter)
{
    /// <summary>Whether this property is a house the player can walk into once owned; ships are boarded instead.</summary>
    internal bool Enterable { get; init; }
}

/// <summary>
/// The player's houses and ship: whether the player owns the interior they stand in, the houses and ships for sale
/// here, buying and selling them at a bank, a house's interior and entrance, the storage the player can reach, the
/// owned ship's deck and boarding, and a knightly order's house. Ownership and its save are the property state's;
/// the storage containers are <see cref="Storage"/>'s.
/// </summary>
/// <param name="questBuilding">Whether a quest claims this building of a site (a quest's building is not drawn for sale).</param>
/// <param name="atEntrance">Whether the player stands at the entrance to this interior profile of this building: one of its doors, or a portal into it.</param>
internal sealed class DaggerfallPropertyHoldings(
    DaggerfallPropertyState property,
    DaggerfallPropertyStorage storage,
    DaggerfallRegionalBankState bank,
    DaggerfallCurrencyService currency,
    DaggerfallTransportPolicy transport,
    DaggerfallKnightlyOrderClaimRuntime knightlyClaims,
    DaggerfallSiteContext site,
    IRandomService random,
    PlayerControlState player,
    Func<DaggerfallCalendar> calendar,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallSiteProfiles?> profiles,
    Func<DaggerfallInteriorBuilding?> building,
    Func<DaggerfallSiteId, DaggerfallSiteBuildingSource, bool> questBuilding,
    Func<DaggerfallWorldProfileKey, DaggerfallSiteBuildingId, bool> atEntrance)
{
    /// <summary>
    /// Owned property's storage containers, built beside the property state and restored from the save's
    /// property section at construction, so persistence never invents a container.
    /// </summary>
    internal DaggerfallPropertyStorage Storage => storage;

    /// <summary>Whether the player owns this interior: their ship, or a house they hold at the active site.</summary>
    internal bool OwnsInterior(DaggerfallInteriorBuilding interior) =>
        interior.BuildingType == 24 && property.OwnsShip
        || property.OwnedHouses.Any(house => house.Site == activeProfile().Site
            && house.Building == interior.Building && house.BlockX == interior.BlockX && house.BlockY == interior.BlockY);

    // The donor uses a monthly random subset of ordinary houses plus all explicit HouseForSale
    // buildings. Keep the selection stable by durable site and month, not Unity's object hash.
    /// <summary>The houses for sale at the active site this month, with the player's own when <paramref name="includeOwned"/>.</summary>
    internal DaggerfallHouseOffer[] HouseOffers(bool includeOwned = false)
    {
        if (site.ActiveSite is not { Exterior: { } exterior } active) return [];
        DaggerfallHouseCandidate[] candidates = [.. exterior.Buildings.Values
            .Where(building => building.ModelRadius is > 0)
            .OrderBy(building => building.Id.BlockY).ThenBy(building => building.Id.BlockX).ThenBy(building => building.Id.Index)
            .Select(building => new DaggerfallHouseCandidate(active.Id, building.Source.Id,
                building.Source.BuildingType, active.Kind, building.ModelRadius!.Value,
                questBuilding(active.Id, building), building.Id.BlockX, building.Id.BlockY))];
        List<DaggerfallHouseCandidate> selected = [.. candidates.Where(value => value.BuildingType == 1)];
        List<DaggerfallHouseCandidate> ordinary = [.. candidates.Where(value => value.BuildingType is >= 17 and <= 20 && !value.IsQuestBuilding)];
        int limit = Math.Min(exterior.Buildings.Count / 10, 20);
        for (int draw = 0; selected.Count < limit && ordinary.Count > 0; draw++)
        {
            int index = checked((int)random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.property.houses",
                $"{active.Id}/{calendar().Month}/{draw}", 0, ordinary.Count - 1)).Value);
            selected.Add(ordinary[index]); ordinary.RemoveAt(index);
        }
        if (includeOwned) selected.AddRange(candidates.Where(value => property.OwnsHouse(value.Identity)));
        return [.. PriceHouses(selected.DistinctBy(value => value.Identity), includeOwned)];
    }

    /// <summary>
    /// Prices only candidates admitted by the caller's normalized building directory. The method
    /// requires the active geographic site so a profile name cannot accidentally become a property
    /// identity.
    /// </summary>
    private IReadOnlyList<DaggerfallHouseOffer> PriceHouses(IEnumerable<DaggerfallHouseCandidate> candidates, bool includeOwned)
    {
        DaggerfallSiteId active = site.Active
            ?? throw new InvalidOperationException("House offers require an active Daggerfall site.");
        DaggerfallHouseCandidate[] admitted = candidates.Select(candidate => candidate.Validate()).ToArray();
        if (admitted.Any(candidate => candidate.Site != active))
            throw new ArgumentException("House offers must name the active geographic site.", nameof(candidates));
        return DaggerfallPropertyPolicy.HousesForSale(admitted, property.Tuning,
            includeOwned ? property.OwnedHouses.ToHashSet() : null);
    }

    /// <summary>The ship deeds a port town sells.</summary>
    internal DaggerfallShipOffer[] ShipOffers() => site.ActiveSite is { Exterior.PortTownAndUnknown: > 0 }
        ? [.. DaggerfallPropertyPolicy.ShipsForSale(true, site.Region
            ?? throw new InvalidOperationException("Ship offers require an active Daggerfall region."), property.Tuning)]
        : [];

    /// <summary>The house with this storage key among this month's offers and the player's own.</summary>
    internal DaggerfallHouseOffer? HouseOffer(string? key) =>
        HouseOffers(includeOwned: true).SingleOrDefault(offer => offer.StorageKey.Value == key);

    /// <summary>The house's interior profile, when the catalog has one: an authored override or its assembled interior.</summary>
    internal DaggerfallWorldProfileKey? HouseProfile(DaggerfallHouseIdentity house)
    {
        DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Interior(house.Site, HouseBuilding(house));
        return profiles()?.Contains(key) == true ? key : null;
    }

    private static DaggerfallSiteBuildingId HouseBuilding(DaggerfallHouseIdentity house) => new(house.BlockX, house.BlockY, house.Building.Index);

    /// <summary>Whether the player stands at the house's entrance: within reach of one of its doors, or of a portal into it.</summary>
    private bool CanEnter(DaggerfallHouseIdentity house)
    {
        DaggerfallWorldProfileKey active = activeProfile();
        return active.Kind == DaggerfallWorldProfileKind.Exterior && house.Site == active.Site
            && HouseProfile(house) is { } profile && atEntrance(profile, HouseBuilding(house));
    }

    /// <summary>The interior of the player's own house when they stand at its entrance; none otherwise.</summary>
    internal DaggerfallWorldProfileKey? Entrance(DaggerfallHouseOffer? house) =>
        house is not null && property.OwnsHouse(house.Identity) && CanEnter(house.Identity) ? HouseProfile(house.Identity) : null;

    /// <summary>The houses and ships the property panel lists here, and what a bank (<paramref name="bank"/>) lets the player do with each.</summary>
    internal DaggerfallPropertyOfferView[] Offers(bool bank)
    {
        List<DaggerfallPropertyOfferView> offers = [];
        foreach (DaggerfallHouseOffer offer in bank || property.OwnedHouses.Any(house => house.Site == site.Active)
            ? HouseOffers(includeOwned: true) : [])
        {
            bool owned = property.OwnsHouse(offer.Identity);
            string name = site.RequireBuilding(offer.Identity.Site,
                new(offer.Identity.BlockX, offer.Identity.BlockY, offer.Identity.Building.Index)).Name;
            offers.Add(new(offer.StorageKey.Value, name, offer.Price.ToString(CultureInfo.InvariantCulture),
                offer.SalePrice.ToString(CultureInfo.InvariantCulture), owned,
                bank && HouseProfile(offer.Identity) is not null && !property.OwnsHouseInRegion(offer.Identity.Site.Region), bank && owned,
                owned && CanEnter(offer.Identity)) { Enterable = true });
        }
        foreach (DaggerfallShipOffer offer in ShipOffers())
            offers.Add(new(offer.StorageKey.Value, $"{offer.Type} ship", offer.Price.ToString(CultureInfo.InvariantCulture),
                offer.SalePrice.ToString(CultureInfo.InvariantCulture), property.OwnedShip == offer.Type,
                bank && !property.OwnsShip, bank && property.OwnedShip == offer.Type && !transport.OnShip, false));
        if (property.OwnedShip is { } ownedShip && !offers.Any(offer => offer.Key == DaggerfallPropertyStorageKey.ForShip(ownedShip).Value))
        {
            ulong price = DaggerfallPropertyPolicy.ShipPrice(ownedShip, property.Tuning);
            offers.Add(new(DaggerfallPropertyStorageKey.ForShip(ownedShip).Value, $"{ownedShip} ship",
                price.ToString(CultureInfo.InvariantCulture), DaggerfallPropertyPolicy.SalePrice(price, property.Tuning).ToString(CultureInfo.InvariantCulture),
                true, false, bank && !transport.OnShip, false));
        }
        return [.. offers];
    }

    /// <summary>
    /// Buys (<paramref name="buy"/>) or sells the house or ship with this key through the bank; none when it is not
    /// offered, or is the ship the player stands on.
    /// </summary>
    internal DaggerfallPropertyTransactionResult? Trade(bool buy, DaggerfallHouseOffer? house, string? key)
    {
        DaggerfallShipOffer? ship = ShipOffers().SingleOrDefault(offer => offer.StorageKey.Value == key);
        DaggerfallPropertyBankSettlementAdapter settlement = new(bank, currency);
        return buy switch
        {
            true when house is not null && HouseProfile(house.Identity) is not null => property.PurchaseHouse(house, settlement),
            false when house is not null => property.SellHouse(house, settlement),
            true when ship is not null => property.PurchaseShip(ship, settlement),
            false when property.OwnedShip is { } owned
                && key == DaggerfallPropertyStorageKey.ForShip(owned).Value && !transport.OnShip => property.SellShip(site.Region
                    ?? throw new InvalidOperationException("Selling a ship requires an active Daggerfall region."), settlement),
            _ => null,
        };
    }

    /// <summary>The storage the player can reach: the owned ship while aboard it on its deck, or the owned house they stand in.</summary>
    internal DaggerfallPropertyStorageKey? AccessibleStorage()
    {
        DaggerfallWorldProfileKey active = activeProfile();
        if (transport.OnShip && property.OwnedShip is { } ship && OwnedShipProfile()?.Site == active.Site)
            return DaggerfallPropertyStorageKey.ForShip(ship);
        if (active.Kind == DaggerfallWorldProfileKind.Interior && building() is { } placed)
        {
            DaggerfallHouseIdentity house = new(active.Site, placed.Building, placed.BlockX, placed.BlockY);
            if (property.OwnsHouse(house)) return DaggerfallPropertyStorageKey.ForHouse(house);
        }
        return null;
    }

    /// <summary>The owned ship's anchorage, when the player owns one.</summary>
    internal DaggerfallShipArrivalAnchor? ShipArrival() => property.OwnedShip is { } ship
        ? DaggerfallPropertyPolicy.ShipArrival(ship, property.Tuning)
        : null;

    /// <summary>The owned ship's deck: the one ship site at its anchorage whose profile has a start.</summary>
    internal DaggerfallWorldProfileKey? OwnedShipProfile()
    {
        if (ShipArrival() is not { } arrival || profiles() is not { } catalog) return null;
        DaggerfallSiteRecord[] ships = [.. site.Records.Where(record => record.Kind == DaggerfallSiteKind.HomeYourShips
            && record.MapPixelX == arrival.MapPixelX && record.MapPixelY == arrival.MapPixelY)];
        if (ships.Length != 1) return null;
        DaggerfallWorldProfileKey ship = DaggerfallWorldProfileIds.Exterior(ships[0].Id);
        return catalog.TryGet(ship, out DaggerfallSiteProfile deck) && deck.Anchors.ContainsKey(DaggerfallLocationAssembly.StartAnchor)
            ? ship : null;
    }

    /// <summary>
    /// Why the player cannot board the owned ship from here: already aboard, the property's boarding gate, or no deck
    /// reachable. Gives the deck to board otherwise.
    /// </summary>
    internal DaggerfallTransportActionResult? BoardingRefusal(DaggerfallTransportAccessContext context, out DaggerfallWorldProfileKey deck)
    {
        deck = default;
        if (transport.OnShip) return new(false, transport.Mode, true, DaggerfallTransportRejection.ShipUnavailableAtSite, "You are already aboard your ship.");
        if (ShipBoardingRefusal(context) is { } refused) return refused;
        if (!context.ShipAccessAllowed || OwnedShipProfile() is not { } ship || player.Position is null)
            return new(false, transport.Mode, transport.OnShip, DaggerfallTransportRejection.ShipUnavailableAtSite, "No owned ship world is available here.");
        deck = ship;
        return null;
    }

    /// <summary>
    /// Applies the property gate before boarding through the transport state. The transport owner
    /// still performs pose validation and durable on-ship state changes.
    /// </summary>
    internal DaggerfallTransportActionResult Board(DaggerfallTransportAccessContext context, DaggerfallTransportPose? currentPose,
        DaggerfallWorldProfileKey returnProfile)
    {
        if (ShipBoardingRefusal(context) is { } refusal) return refusal;
        return transport.BoardShip(property.OwnsShip, context, currentPose, returnProfile);
    }

    private DaggerfallTransportActionResult? ShipBoardingRefusal(DaggerfallTransportAccessContext context)
    {
        DaggerfallPropertyAccessResult access = property.ShipBoarding(
            new DaggerfallShipAccessContext(context.IsIndoor, context.IsDungeon, context.ShipAccessAllowed));
        if (!access.Allowed)
            return new(false, transport.Mode, transport.OnShip,
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

    /// <summary>
    /// A knightly order's house: once the order offers it, one local house the player can be given (drawn when there
    /// are several) is awarded as property and the claim completes.
    /// </summary>
    internal DaggerfallGuildProviderResult AwardKnightlyHouse(
        int factionId,
        int currentDay,
        DaggerfallConcreteGuildServiceInput input)
    {
        DaggerfallKnightlyHouseOfferResult offered = knightlyClaims.OfferHouse(factionId, currentDay, input);
        if (!offered.Offered)
            return DaggerfallGuildProviders.KnightlyRefusal(offered.Denial, offered.Service);

        DaggerfallHouseOffer[] houses = [.. HouseOffers()
            .Where(offer => HouseProfile(offer.Identity) is not null)
            .Where(offer => !property.OwnsHouseInRegion(offer.Identity.Site.Region))];
        if (houses.Length == 0)
            return DaggerfallGuildProviderResult.Refused(DaggerfallGuildProviderDenial.NoHouseAvailable,
                "The order has no eligible local house to award.");

        int selected = houses.Length == 1
            ? 0
            : checked((int)random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.guild.knightly-house.v1",
                $"{factionId}:{currentDay}", 0, houses.Length - 1)).Value);
        DaggerfallHouseOffer house = houses[selected];
        DaggerfallKnightlyClaimTransactionResult claim = knightlyClaims.CompleteHouse(
            offered.Offer!, currentDay,
            _ => property.AwardHouse(house).AsKnightlyHouse(),
            input);
        if (!claim.Applied)
            return DaggerfallGuildProviders.KnightlyRefusal(claim.Denial, claim.ServiceDecision, claim);
        return new(true, DaggerfallGuildProviderDenial.None,
            $"The order awards you the local house {house.StorageKey.Value}.", Claim: claim);
    }
}
