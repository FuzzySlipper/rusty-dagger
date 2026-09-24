using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Property;

/// <summary>A bank operation requested by a property purchase or sale.</summary>
internal enum DaggerfallPropertyBankOperation
{
    Purchase,
    Sale,
}

/// <summary>Reasons the common bank settlement refused a property operation.</summary>
internal enum DaggerfallPropertyPaymentDenial
{
    None,
    InsufficientFunds,
    InvalidRegion,
    InvalidAmount,
    Rejected,
}

/// <summary>
/// The explicit handoff between property policy and the regional bank owner. The bank adapter owns
/// cash/account movement; this module never subtracts a synthetic balance of its own.
/// </summary>
internal readonly record struct DaggerfallPropertyBankRequest(
    DaggerfallPropertyBankOperation Operation,
    DaggerfallPropertyKind PropertyKind,
    int Region,
    ulong Amount,
    DaggerfallPropertyStorageKey StorageKey)
{
    internal DaggerfallPropertyBankRequest Validate()
    {
        if (!Enum.IsDefined(Operation) || !Enum.IsDefined(PropertyKind))
            throw new ArgumentOutOfRangeException(nameof(Operation));
        if (Region is < 0 or >= 62)
            throw new ArgumentOutOfRangeException(nameof(Region), "A property bank request must name a classic region.");
        if (Amount == 0)
            throw new ArgumentOutOfRangeException(nameof(Amount), "A property bank request must carry a positive amount.");
        StorageKey.Validate();
        return this;
    }
}

/// <summary>Settlement outcome returned by the existing bank transaction owner.</summary>
internal readonly record struct DaggerfallPropertyPaymentResult(bool Applied, DaggerfallPropertyPaymentDenial Denial)
{
    internal static DaggerfallPropertyPaymentResult Accepted() => new(true, DaggerfallPropertyPaymentDenial.None);

    internal static DaggerfallPropertyPaymentResult Refused(DaggerfallPropertyPaymentDenial denial) =>
        new(false, denial == DaggerfallPropertyPaymentDenial.None ? DaggerfallPropertyPaymentDenial.Rejected : denial);
}

/// <summary>Ruleset-owned adapter implemented by Session using DaggerfallRegionalBankState.</summary>
internal interface IDaggerfallPropertyBankSettlement
{
    DaggerfallPropertyPaymentResult Apply(DaggerfallPropertyBankRequest request);
}

/// <summary>Property state changes returned to Session and semantic UI callers.</summary>
internal enum DaggerfallPropertyTransactionKind
{
    Purchase,
    Sale,
}

internal enum DaggerfallPropertyTransactionDenial
{
    None,
    AlreadyOwned,
    NotOwned,
    InvalidOffer,
    InsufficientFunds,
    PaymentRejected,
}

/// <summary>One complete property purchase or sale outcome.</summary>
internal sealed record DaggerfallPropertyTransactionResult(
    bool Applied,
    DaggerfallPropertyTransactionKind Kind,
    DaggerfallPropertyKind PropertyKind,
    DaggerfallPropertyTransactionDenial Denial,
    ulong Amount,
    DaggerfallPropertyStorageKey StorageKey,
    string Message)
{
    internal static DaggerfallPropertyTransactionResult Refused(
        DaggerfallPropertyTransactionKind kind,
        DaggerfallPropertyKind propertyKind,
        DaggerfallPropertyTransactionDenial denial,
        DaggerfallPropertyStorageKey storageKey,
        string message) => new(false, kind, propertyKind, denial, 0, storageKey, message);
}

/// <summary>Flat current-schema placed house ownership identity for save/load wiring.</summary>
internal sealed record DaggerfallHouseOwnershipSave(
    int Region,
    int SiteIndex,
    string BuildingSourceKey,
    int BuildingIndex,
    int BlockX,
    int BlockY)
{
    internal DaggerfallHouseIdentity Identity => new(
        new DaggerfallSiteId(Region, SiteIndex),
        new DaggerfallRmbBuildingId(BuildingSourceKey, BuildingIndex),
        BlockX,
        BlockY);

    internal DaggerfallHouseOwnershipSave Validate()
    {
        Identity.Validate();
        return this;
    }

    internal static DaggerfallHouseOwnershipSave Capture(DaggerfallHouseIdentity identity)
    {
        identity.Validate();
        return new(identity.Site.Region, identity.Site.Index, identity.Building.SourceKey, identity.Building.Index,
            identity.BlockX, identity.BlockY);
    }
}

/// <summary>Flat current-schema ship ownership identity for save/load wiring.</summary>
internal sealed record DaggerfallShipOwnershipSave(int Type)
{
    internal DaggerfallShipType Ship => (DaggerfallShipType)Type;

    internal DaggerfallShipOwnershipSave Validate()
    {
        if (!Enum.IsDefined(Ship)) throw new ArgumentOutOfRangeException(nameof(Type));
        return this;
    }

    internal static DaggerfallShipOwnershipSave Capture(DaggerfallShipType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        return new((int)type);
    }
}

/// <summary>Stable property-to-Kit container identity and the save-boundary Engine contents snapshot.</summary>
internal sealed record DaggerfallPropertyStorageSave(string Key, long ContainerId, DaggerfallInventorySave Inventory)
{
    internal DaggerfallPropertyStorageSave(string key, long containerId)
        : this(key, containerId, new DaggerfallInventorySave([], [], []))
    {
    }

    internal DaggerfallPropertyStorageSave Validate()
    {
        new DaggerfallPropertyStorageKey(Key).Validate();
        if (ContainerId <= 0) throw new ArgumentOutOfRangeException(nameof(ContainerId));
        ArgumentNullException.ThrowIfNull(Inventory);
        Inventory.Validate();
        return this;
    }
}

/// <summary>Persisted property ownership and storage bindings.</summary>
internal sealed record DaggerfallPropertySave(
    DaggerfallHouseOwnershipSave[] Houses,
    DaggerfallShipOwnershipSave? Ship,
    DaggerfallPropertyStorageSave[] Storage,
    DaggerfallHouseOwnershipSave[] RetainedHouses,
    DaggerfallShipOwnershipSave[] RetainedShips)
{
    internal static DaggerfallPropertySave Empty { get; } = new([], null, [], [], []);

    internal DaggerfallPropertySave Validate()
    {
        ArgumentNullException.ThrowIfNull(Houses);
        ArgumentNullException.ThrowIfNull(Storage);
        ArgumentNullException.ThrowIfNull(RetainedHouses);
        ArgumentNullException.ThrowIfNull(RetainedShips);
        HashSet<int> regions = [];
        HashSet<DaggerfallHouseIdentity> houses = [];
        foreach (DaggerfallHouseOwnershipSave house in Houses)
        {
            ArgumentNullException.ThrowIfNull(house);
            house.Validate();
            if (!regions.Add(house.Region))
                throw new ArgumentException($"Property save repeats a house in region {house.Region}.", nameof(Houses));
            if (!houses.Add(house.Identity))
                throw new ArgumentException($"Property save repeats house '{house.Identity}'.", nameof(Houses));
        }
        foreach (DaggerfallHouseOwnershipSave house in RetainedHouses)
        {
            ArgumentNullException.ThrowIfNull(house);
            house.Validate();
            if (!houses.Add(house.Identity))
                throw new ArgumentException($"Property save repeats retained house '{house.Identity}'.", nameof(RetainedHouses));
        }
        Ship?.Validate();

        HashSet<DaggerfallShipType> ships = [];
        if (Ship is DaggerfallShipOwnershipSave ownedShip)
            ships.Add(ownedShip.Ship);
        foreach (DaggerfallShipOwnershipSave retainedShip in RetainedShips)
        {
            ArgumentNullException.ThrowIfNull(retainedShip);
            retainedShip.Validate();
            if (!ships.Add(retainedShip.Ship))
                throw new ArgumentException($"Property save repeats ship type '{retainedShip.Ship}'.", nameof(RetainedShips));
        }

        HashSet<DaggerfallPropertyStorageKey> expectedKeys = houses
            .Select(DaggerfallPropertyStorageKey.ForHouse)
            .ToHashSet();
        foreach (DaggerfallShipType ship in ships)
            expectedKeys.Add(DaggerfallPropertyStorageKey.ForShip(ship));
        HashSet<string> keys = new(StringComparer.Ordinal);
        HashSet<long> containers = [];
        foreach (DaggerfallPropertyStorageSave storage in Storage)
        {
            ArgumentNullException.ThrowIfNull(storage);
            storage.Validate();
            DaggerfallPropertyStorageKey key = new DaggerfallPropertyStorageKey(storage.Key).Validate();
            if (!expectedKeys.Contains(key))
                throw new ArgumentException($"Property storage key '{storage.Key}' does not belong to an owned or retained property.", nameof(Storage));
            if (!keys.Add(storage.Key))
                throw new ArgumentException($"Property save repeats storage key '{storage.Key}'.", nameof(Storage));
            if (!containers.Add(storage.ContainerId))
                throw new ArgumentException($"Property save repeats storage container {storage.ContainerId}.", nameof(Storage));
        }
        return this;
    }
}

/// <summary>
/// Durable house/ship ownership and property storage bindings. Inventory contents remain owned by
/// Kit's Engine-backed containers; this owner only retains the relationship that gives a container its
/// property meaning.
/// </summary>
internal sealed class DaggerfallPropertyState
{
    private readonly DaggerfallPropertyTuning _tuning;
    private readonly Dictionary<DaggerfallHouseIdentity, DaggerfallHouseOwnershipSave> _houses = [];
    private readonly Dictionary<DaggerfallHouseIdentity, DaggerfallHouseOwnershipSave> _retainedHouses = [];
    private readonly Dictionary<DaggerfallPropertyStorageKey, DaggerfallPropertyStorageSave> _storage = [];
    private readonly HashSet<DaggerfallShipType> _retainedShips = [];
    private DaggerfallShipType? _ship;

    internal DaggerfallPropertyState(DaggerfallPropertySave? restored = null, DaggerfallPropertyTuning? tuning = null)
    {
        _tuning = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        if (restored is not null) Restore(restored);
    }

    internal DaggerfallPropertyTuning Tuning => _tuning;
    internal IReadOnlyList<DaggerfallHouseIdentity> OwnedHouses => Array.AsReadOnly(
        _houses.Keys.OrderBy(identity => identity.Site.Region).ThenBy(identity => identity.Site.Index)
            .ThenBy(identity => identity.Building.SourceKey, StringComparer.Ordinal).ThenBy(identity => identity.BlockX)
            .ThenBy(identity => identity.BlockY).ThenBy(identity => identity.Building.Index).ToArray());
    internal DaggerfallShipType? OwnedShip => _ship;
    internal bool OwnsShip => _ship is not null;

    internal bool OwnsHouse(DaggerfallHouseIdentity identity)
    {
        identity.Validate();
        return _houses.ContainsKey(identity);
    }

    internal bool OwnsHouseInRegion(int region)
    {
        if (region is < 0 or >= 62) throw new ArgumentOutOfRangeException(nameof(region));
        return _houses.Keys.Any(identity => identity.Site.Region == region);
    }

    internal bool TryGetStorageContainer(DaggerfallPropertyStorageKey key, out long containerId)
    {
        if (_storage.TryGetValue(key.Validate(), out DaggerfallPropertyStorageSave? saved))
        {
            containerId = saved.ContainerId;
            return true;
        }
        containerId = 0;
        return false;
    }

    internal bool TryGetStorageSave(DaggerfallPropertyStorageKey key, out DaggerfallPropertyStorageSave saved)
    {
        if (_storage.TryGetValue(key.Validate(), out DaggerfallPropertyStorageSave? current))
        {
            saved = current;
            return true;
        }
        saved = null!;
        return false;
    }

    /// <summary>
    /// Captures ownership and storage at an explicit save boundary. The callback reads current
    /// Engine contents; a restored snapshot is used only when no callback is supplied.
    /// </summary>
    internal DaggerfallPropertySave Capture(Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave>? readStorage = null) => new DaggerfallPropertySave(
        _houses.Values.OrderBy(value => value.Region).ThenBy(value => value.SiteIndex).ThenBy(value => value.BuildingSourceKey, StringComparer.Ordinal)
            .ThenBy(value => value.BlockX).ThenBy(value => value.BlockY).ThenBy(value => value.BuildingIndex).ToArray(),
        _ship is DaggerfallShipType ship ? DaggerfallShipOwnershipSave.Capture(ship) : null,
        _storage.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
            .Select(pair => new DaggerfallPropertyStorageSave(pair.Key.Value, pair.Value.ContainerId,
                readStorage is null ? pair.Value.Inventory : readStorage(pair.Key))).ToArray(),
        _retainedHouses.Values.OrderBy(value => value.Region).ThenBy(value => value.SiteIndex).ThenBy(value => value.BuildingSourceKey, StringComparer.Ordinal)
            .ThenBy(value => value.BlockX).ThenBy(value => value.BlockY).ThenBy(value => value.BuildingIndex).ToArray(),
        _retainedShips.OrderBy(type => type).Select(DaggerfallShipOwnershipSave.Capture).ToArray()).Validate();

    /// <summary>Restores only current-schema identity relationships; the Session must rebuild Kit containers.</summary>
    internal void Restore(DaggerfallPropertySave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        if (_houses.Count != 0 || _retainedHouses.Count != 0 || _storage.Count != 0 || _ship is not null || _retainedShips.Count != 0)
            throw new InvalidOperationException("Property state is already materialized.");
        foreach (DaggerfallHouseOwnershipSave house in saved.Houses)
            _houses.Add(house.Identity, house);
        foreach (DaggerfallHouseOwnershipSave house in saved.RetainedHouses)
            _retainedHouses.Add(house.Identity, house);
        _ship = saved.Ship?.Ship;
        foreach (DaggerfallShipOwnershipSave ship in saved.RetainedShips)
            _retainedShips.Add(ship.Ship);
        foreach (DaggerfallPropertyStorageSave storage in saved.Storage)
            _storage.Add(new DaggerfallPropertyStorageKey(storage.Key), storage);
    }

    /// <summary>Binds a real Kit container after its Engine owner has been materialized.</summary>
    internal void BindStorage(DaggerfallPropertyStorageKey key, long containerId)
    {
        key.Validate();
        if (containerId <= 0) throw new ArgumentOutOfRangeException(nameof(containerId));
        if (!IsKnownPropertyKey(key))
            throw new InvalidOperationException($"Property storage key '{key.Value}' does not belong to owned or retained property state.");
        if (_storage.TryGetValue(key, out DaggerfallPropertyStorageSave? existing) && existing.ContainerId != containerId)
            throw new InvalidOperationException($"Property storage key '{key.Value}' is already bound to container {existing.ContainerId}.");
        if (_storage.Any(pair => pair.Key != key && pair.Value.ContainerId == containerId))
            throw new InvalidOperationException($"Property storage container {containerId} is already bound to another property.");
        _storage[key] = _storage.TryGetValue(key, out DaggerfallPropertyStorageSave? saved)
            ? saved with { ContainerId = containerId }
            : new DaggerfallPropertyStorageSave(key.Value, containerId);
    }

    internal DaggerfallPropertyTransactionResult PurchaseHouse(DaggerfallHouseOffer offer,
        IDaggerfallPropertyBankSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(settlement);
        try { offer.Validate(_tuning); }
        catch (ArgumentException exception)
        {
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Purchase,
                DaggerfallPropertyKind.House, DaggerfallPropertyTransactionDenial.InvalidOffer, offer.StorageKey,
                exception.Message);
        }
        if (OwnsHouseInRegion(offer.Identity.Site.Region))
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Purchase,
                DaggerfallPropertyKind.House, DaggerfallPropertyTransactionDenial.AlreadyOwned, offer.StorageKey,
                "You already own a house in this region.");
        DaggerfallPropertyBankRequest request = new DaggerfallPropertyBankRequest(DaggerfallPropertyBankOperation.Purchase,
            DaggerfallPropertyKind.House, offer.Identity.Site.Region, offer.Price, offer.StorageKey).Validate();
        return ApplyPurchase(request, settlement, () =>
        {
            _retainedHouses.Remove(offer.Identity);
            _houses.Add(offer.Identity, DaggerfallHouseOwnershipSave.Capture(offer.Identity));
        },
            DaggerfallPropertyKind.House, offer.StorageKey);
    }

    internal DaggerfallPropertyTransactionResult PurchaseShip(DaggerfallShipOffer offer,
        IDaggerfallPropertyBankSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(settlement);
        try { offer.Validate(_tuning); }
        catch (ArgumentException exception)
        {
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Purchase,
                DaggerfallPropertyKind.Ship, DaggerfallPropertyTransactionDenial.InvalidOffer, offer.StorageKey,
                exception.Message);
        }
        if (_ship is not null)
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Purchase,
                DaggerfallPropertyKind.Ship, DaggerfallPropertyTransactionDenial.AlreadyOwned, offer.StorageKey,
                "You already own a ship.");
        DaggerfallPropertyBankRequest request = new DaggerfallPropertyBankRequest(DaggerfallPropertyBankOperation.Purchase,
            DaggerfallPropertyKind.Ship, offer.PurchaseRegion, offer.Price, offer.StorageKey).Validate();
        return ApplyPurchase(request, settlement, () =>
        {
            _retainedShips.Remove(offer.Type);
            _ship = offer.Type;
        },
            DaggerfallPropertyKind.Ship, offer.StorageKey);
    }

    internal DaggerfallPropertyTransactionResult SellHouse(DaggerfallHouseOffer offer,
        IDaggerfallPropertyBankSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(settlement);
        try { offer.Validate(_tuning); }
        catch (ArgumentException exception)
        {
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Sale,
                DaggerfallPropertyKind.House, DaggerfallPropertyTransactionDenial.InvalidOffer, offer.StorageKey,
                exception.Message);
        }
        if (!OwnsHouse(offer.Identity))
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Sale,
                DaggerfallPropertyKind.House, DaggerfallPropertyTransactionDenial.NotOwned, offer.StorageKey,
                "You do not own this house.");
        DaggerfallPropertyBankRequest request = new DaggerfallPropertyBankRequest(DaggerfallPropertyBankOperation.Sale,
            DaggerfallPropertyKind.House, offer.Identity.Site.Region, offer.SalePrice, offer.StorageKey).Validate();
        return ApplySale(request, settlement, () =>
        {
            _houses.Remove(offer.Identity);
            _retainedHouses[offer.Identity] = DaggerfallHouseOwnershipSave.Capture(offer.Identity);
        },
            DaggerfallPropertyKind.House, offer.StorageKey);
    }

    internal DaggerfallPropertyTransactionResult SellShip(int region, IDaggerfallPropertyBankSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (region is < 0 or >= 62) throw new ArgumentOutOfRangeException(nameof(region));
        if (_ship is not DaggerfallShipType ship)
        {
            DaggerfallPropertyStorageKey missing = DaggerfallPropertyStorageKey.ForShip(DaggerfallShipType.Small);
            return DaggerfallPropertyTransactionResult.Refused(DaggerfallPropertyTransactionKind.Sale,
                DaggerfallPropertyKind.Ship, DaggerfallPropertyTransactionDenial.NotOwned, missing,
                "You do not own a ship.");
        }
        DaggerfallPropertyStorageKey key = DaggerfallPropertyStorageKey.ForShip(ship);
        ulong salePrice = DaggerfallPropertyPolicy.SalePrice(DaggerfallPropertyPolicy.ShipPrice(ship, _tuning), _tuning);
        DaggerfallPropertyBankRequest request = new DaggerfallPropertyBankRequest(DaggerfallPropertyBankOperation.Sale,
            DaggerfallPropertyKind.Ship, region, salePrice, key).Validate();
        return ApplySale(request, settlement, () =>
        {
            _ship = null;
            _retainedShips.Add(ship);
        },
            DaggerfallPropertyKind.Ship, key);
    }

    internal DaggerfallPropertyAccessResult HouseAccess(DaggerfallHouseEntryContext requested)
    {
        requested.Validate();
        DaggerfallHouseIdentity identity = new(requested.Site, requested.Building, requested.BlockX, requested.BlockY);
        if (_houses.TryGetValue(identity, out DaggerfallHouseOwnershipSave? exact))
            return DaggerfallPropertyPolicy.HouseAccess(exact.Identity, requested);
        DaggerfallHouseOwnershipSave? atSite = _houses.Values.SingleOrDefault(value => value.Identity.Site == requested.Site);
        return atSite is null
            ? DaggerfallPropertyPolicy.MissingHouseAccess(requested)
            : DaggerfallPropertyPolicy.HouseAccess(atSite.Identity, requested);
    }

    internal DaggerfallPropertyAccessResult ShipBoarding(DaggerfallShipAccessContext context) =>
        _ship is DaggerfallShipType ship
            ? DaggerfallPropertyPolicy.ShipBoarding(ship, context)
            : DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.MissingOwnership,
                "You do not own a ship.");

    internal DaggerfallPropertyAccessResult ShipStorage(bool onShip) =>
        _ship is DaggerfallShipType ship
            ? DaggerfallPropertyPolicy.ShipStorage(ship, onShip)
            : DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.MissingOwnership,
                "You do not own a ship.");

    internal DaggerfallPropertyStorageDecision StorageAfter(DaggerfallPropertyStorageEvent @event) =>
        DaggerfallPropertyPolicy.StorageAfter(@event);

    private DaggerfallPropertyTransactionResult ApplyPurchase(DaggerfallPropertyBankRequest request,
        IDaggerfallPropertyBankSettlement settlement, Action commit, DaggerfallPropertyKind propertyKind,
        DaggerfallPropertyStorageKey storageKey)
    {
        DaggerfallPropertyPaymentResult payment = settlement.Apply(request);
        if (!payment.Applied)
            return PaymentRefusal(DaggerfallPropertyTransactionKind.Purchase, propertyKind, storageKey, payment);
        commit();
        return new(true, DaggerfallPropertyTransactionKind.Purchase, propertyKind,
            DaggerfallPropertyTransactionDenial.None, request.Amount, storageKey,
            propertyKind == DaggerfallPropertyKind.House ? "House purchased." : "Ship purchased.");
    }

    private DaggerfallPropertyTransactionResult ApplySale(DaggerfallPropertyBankRequest request,
        IDaggerfallPropertyBankSettlement settlement, Action commit, DaggerfallPropertyKind propertyKind,
        DaggerfallPropertyStorageKey storageKey)
    {
        DaggerfallPropertyPaymentResult payment = settlement.Apply(request);
        if (!payment.Applied)
            return PaymentRefusal(DaggerfallPropertyTransactionKind.Sale, propertyKind, storageKey, payment);
        commit();
        return new(true, DaggerfallPropertyTransactionKind.Sale, propertyKind,
            DaggerfallPropertyTransactionDenial.None, request.Amount, storageKey,
            propertyKind == DaggerfallPropertyKind.House ? "House sold." : "Ship sold.");
    }

    private static DaggerfallPropertyTransactionResult PaymentRefusal(DaggerfallPropertyTransactionKind kind,
        DaggerfallPropertyKind propertyKind, DaggerfallPropertyStorageKey storageKey, DaggerfallPropertyPaymentResult payment)
    {
        DaggerfallPropertyTransactionDenial denial = payment.Denial == DaggerfallPropertyPaymentDenial.InsufficientFunds
            ? DaggerfallPropertyTransactionDenial.InsufficientFunds
            : DaggerfallPropertyTransactionDenial.PaymentRejected;
        string message = denial == DaggerfallPropertyTransactionDenial.InsufficientFunds
            ? "The bank could not cover that property price."
            : "The bank rejected that property transaction.";
        return DaggerfallPropertyTransactionResult.Refused(kind, propertyKind, denial, storageKey, message);
    }

    private bool IsKnownPropertyKey(DaggerfallPropertyStorageKey key) =>
        _storage.ContainsKey(key)
        || _houses.Keys.Any(identity => DaggerfallPropertyStorageKey.ForHouse(identity) == key)
        || _retainedHouses.Keys.Any(identity => DaggerfallPropertyStorageKey.ForHouse(identity) == key)
        || _ship is DaggerfallShipType ship && DaggerfallPropertyStorageKey.ForShip(ship) == key
        || _retainedShips.Any(type => DaggerfallPropertyStorageKey.ForShip(type) == key);
}
