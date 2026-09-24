using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Property;

/// <summary>The two durable property families retained by the Daggerfall bank donor.</summary>
internal enum DaggerfallPropertyKind
{
    House,
    Ship,
}

/// <summary>The two ship deeds the donor offers at a port town.</summary>
internal enum DaggerfallShipType
{
    Small,
    Large,
}

/// <summary>A source identity for one placed house building in one actual Daggerfall site.</summary>
/// <remarks>
/// The RMB building slot is a template identity and can occur at several block placements. The
/// block coordinates are therefore part of the durable property identity; callers must provide
/// them from normalized placement data rather than deriving them from a profile name.
/// </remarks>
internal readonly record struct DaggerfallHouseIdentity(
    DaggerfallSiteId Site,
    DaggerfallRmbBuildingId Building,
    int BlockX,
    int BlockY)
{
    internal DaggerfallHouseIdentity Validate()
    {
        if (Site.Region is < 0 or >= 62 || Site.Index < 0)
            throw new ArgumentOutOfRangeException(nameof(Site), "A house must name a published site identity.");
        if (string.IsNullOrWhiteSpace(Building.SourceKey) || Building.Index < 0)
            throw new ArgumentException("A house must name a published RMB building identity.", nameof(Building));
        if (BlockX < 0) throw new ArgumentOutOfRangeException(nameof(BlockX));
        if (BlockY < 0) throw new ArgumentOutOfRangeException(nameof(BlockY));
        return this;
    }

    public override string ToString() => $"{Site}/{Building}@{BlockX},{BlockY}";
}

/// <summary>The map-pixel arrival anchor used by the donor's ship scenes.</summary>
internal readonly record struct DaggerfallShipArrivalAnchor(int MapPixelX, int MapPixelY)
{
    internal DaggerfallShipArrivalAnchor Validate()
    {
        if (MapPixelX is < 0 or >= 1000 || MapPixelY is < 0 or >= 500)
            throw new ArgumentOutOfRangeException(nameof(MapPixelX), "A ship arrival anchor must be inside the classic 1000 by 500 world map.");
        return this;
    }
}

/// <summary>Adjustable property prices and source-backed ship arrival data.</summary>
internal sealed record DaggerfallPropertyTuning
{
    internal DaggerfallPropertyTuning()
        : this(1280, 100_000, 200_000, 85, new(2, 2), new(5, 5))
    {
    }

    internal DaggerfallPropertyTuning(int housePricePerModelRadius, ulong smallShipPrice,
        ulong largeShipPrice, int salePercent, DaggerfallShipArrivalAnchor smallShipArrival,
        DaggerfallShipArrivalAnchor largeShipArrival)
    {
        HousePricePerModelRadius = housePricePerModelRadius;
        SmallShipPrice = smallShipPrice;
        LargeShipPrice = largeShipPrice;
        SalePercent = salePercent;
        SmallShipArrival = smallShipArrival;
        LargeShipArrival = largeShipArrival;
    }

    internal int HousePricePerModelRadius { get; }
    internal ulong SmallShipPrice { get; }
    internal ulong LargeShipPrice { get; }
    internal int SalePercent { get; }
    internal DaggerfallShipArrivalAnchor SmallShipArrival { get; }
    internal DaggerfallShipArrivalAnchor LargeShipArrival { get; }

    /// <summary>The price and scene values retained from DaggerfallBankManager.</summary>
    internal static DaggerfallPropertyTuning Donor { get; } = new();

    internal DaggerfallPropertyTuning Validate()
    {
        if (HousePricePerModelRadius <= 0 || SmallShipPrice == 0 || LargeShipPrice == 0
            || SalePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(HousePricePerModelRadius), "Property tuning must contain positive prices and a bounded sale percentage.");
        SmallShipArrival.Validate();
        LargeShipArrival.Validate();
        return this;
    }
}

/// <summary>
/// One normalized building candidate supplied by the admitted world content. The property policy does
/// not reopen an Arena2/RMB file or infer identity from a profile name; the importer must provide this
/// record from the actual site and building source.
/// </summary>
internal sealed record DaggerfallHouseCandidate(
    DaggerfallSiteId Site,
    DaggerfallRmbBuildingId Building,
    int BuildingType,
    DaggerfallSiteKind SiteKind,
    float ModelRadius,
    bool IsQuestBuilding,
    int BlockX,
    int BlockY)
{
    internal DaggerfallHouseIdentity Identity => new(Site, Building, BlockX, BlockY);

    internal DaggerfallHouseCandidate Validate()
    {
        Identity.Validate();
        if (!Enum.IsDefined(SiteKind))
            throw new ArgumentOutOfRangeException(nameof(SiteKind));
        if (BuildingType is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(BuildingType), "An RMB building type must retain its source byte.");
        if (!float.IsFinite(ModelRadius) || ModelRadius < 0f)
            throw new ArgumentOutOfRangeException(nameof(ModelRadius));
        return this;
    }
}

/// <summary>A priced, validated house offer from an admitted building directory.</summary>
internal sealed record DaggerfallHouseOffer(DaggerfallHouseCandidate Candidate, ulong Price, ulong SalePrice)
{
    internal DaggerfallHouseIdentity Identity => Candidate.Identity;
    internal DaggerfallPropertyStorageKey StorageKey => DaggerfallPropertyStorageKey.ForHouse(Identity);

    internal DaggerfallHouseOffer Validate(DaggerfallPropertyTuning? tuning = null)
    {
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        Candidate.Validate();
        if (!DaggerfallPropertyPolicy.IsEligibleHouse(Candidate))
            throw new ArgumentException("The supplied building is not an eligible Daggerfall house candidate.", nameof(Candidate));
        ulong expected = DaggerfallPropertyPolicy.HousePrice(Candidate, values);
        if (Price != expected || SalePrice != DaggerfallPropertyPolicy.SalePrice(expected, values))
            throw new ArgumentException("A house offer does not retain its source-derived price.", nameof(Price));
        return this;
    }
}

/// <summary>A priced ship deed available from a source port-town context.</summary>
internal sealed record DaggerfallShipOffer(
    DaggerfallShipType Type,
    int PurchaseRegion,
    ulong Price,
    ulong SalePrice,
    DaggerfallShipArrivalAnchor Arrival,
    bool AtPortTown)
{
    internal DaggerfallPropertyStorageKey StorageKey => DaggerfallPropertyStorageKey.ForShip(Type);

    internal DaggerfallShipOffer Validate(DaggerfallPropertyTuning? tuning = null)
    {
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        if (!Enum.IsDefined(Type) || PurchaseRegion is < 0 or >= 62)
            throw new ArgumentOutOfRangeException(nameof(Type), "A ship offer must name a classic ship type and bank region.");
        if (!AtPortTown)
            throw new ArgumentException("A ship offer may only be admitted at a port town.", nameof(AtPortTown));
        Arrival.Validate();
        ulong expected = DaggerfallPropertyPolicy.ShipPrice(Type, values);
        if (Price != expected || SalePrice != DaggerfallPropertyPolicy.SalePrice(expected, values)
            || Arrival != DaggerfallPropertyPolicy.ShipArrival(Type, values))
            throw new ArgumentException("A ship offer does not retain its source-derived price or arrival anchor.", nameof(Price));
        return this;
    }
}

/// <summary>A stable property storage key that a Kit container owner can bind to an Engine container.</summary>
internal readonly record struct DaggerfallPropertyStorageKey(string Value)
{
    internal DaggerfallPropertyStorageKey Validate()
    {
        if (string.IsNullOrWhiteSpace(Value))
            throw new ArgumentException("A property storage key cannot be empty.", nameof(Value));
        return this;
    }

    internal static DaggerfallPropertyStorageKey ForHouse(DaggerfallHouseIdentity identity)
    {
        identity.Validate();
        return new($"house/{identity.Site.Region}/{identity.Site.Index}/{identity.Building.SourceKey}/{identity.BlockX}/{identity.BlockY}/{identity.Building.Index}");
    }

    internal static DaggerfallPropertyStorageKey ForShip(DaggerfallShipType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        return new($"ship/{type.ToString().ToLowerInvariant()}");
    }
}

/// <summary>The exact placed building identity supplied when entering or using a house.</summary>
internal readonly record struct DaggerfallHouseEntryContext(
    DaggerfallSiteId Site,
    DaggerfallRmbBuildingId Building,
    int BlockX,
    int BlockY)
{
    internal DaggerfallHouseEntryContext Validate()
    {
        new DaggerfallHouseIdentity(Site, Building, BlockX, BlockY).Validate();
        return this;
    }
}

/// <summary>Caller-provided admission facts for a ship operation.</summary>
internal readonly record struct DaggerfallShipAccessContext(bool IsIndoor, bool IsDungeon, bool IsAvailableAtSite)
{
    internal DaggerfallShipAccessContext Validate() => this;
}

/// <summary>Reasons a property or its storage cannot be used at the requested context.</summary>
internal enum DaggerfallPropertyAccessDenial
{
    None,
    MissingOwnership,
    WrongSite,
    WrongBuilding,
    Indoor,
    Dungeon,
    ShipUnavailableAtSite,
    NotOnShip,
}

/// <summary>One explicit access result for Session and DOM callers.</summary>
internal sealed record DaggerfallPropertyAccessResult(
    bool Allowed,
    DaggerfallPropertyAccessDenial Denial,
    string Message)
{
    internal static DaggerfallPropertyAccessResult Allow(string message) => new(true, DaggerfallPropertyAccessDenial.None, message);

    internal static DaggerfallPropertyAccessResult Deny(DaggerfallPropertyAccessDenial denial, string message) => new(false, denial, message);
}

/// <summary>Storage lifecycle outcomes. Property unload and sale retain contents under the donor policy.</summary>
internal enum DaggerfallPropertyStorageEvent
{
    Unload,
    Sale,
}

internal readonly record struct DaggerfallPropertyStorageDecision(bool PreserveContents, string Message);

/// <summary>Pure Daggerfall property and transport policy over normalized source facts.</summary>
internal static class DaggerfallPropertyPolicy
{
    // DFLocation.BuildingTypes.HouseForSale and House1 through House4. Building5/6 are not returned
    // by BuildingDirectory.GetHousesForSale in the donor.
    private const int HouseForSale = 1;
    private const int House1 = 17;
    private const int House4 = 20;

    internal static bool IsEligibleHouse(DaggerfallHouseCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.Validate();
        return candidate.SiteKind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage
            && !candidate.IsQuestBuilding
            && (candidate.BuildingType == HouseForSale || candidate.BuildingType is >= House1 and <= House4)
            && candidate.ModelRadius > 0f;
    }

    internal static IReadOnlyList<DaggerfallHouseOffer> HousesForSale(IEnumerable<DaggerfallHouseCandidate> candidates,
        DaggerfallPropertyTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        DaggerfallHouseOffer[] offers = candidates
            .Select(candidate => candidate.Validate())
            .Where(IsEligibleHouse)
            .Select(candidate =>
            {
                ulong price = HousePrice(candidate, values);
                return new DaggerfallHouseOffer(candidate, price, SalePrice(price, values));
            })
            .OrderBy(offer => offer.Identity.Site.Region)
            .ThenBy(offer => offer.Identity.Site.Index)
            .ThenBy(offer => offer.Identity.Building.SourceKey, StringComparer.Ordinal)
            .ThenBy(offer => offer.Identity.BlockX)
            .ThenBy(offer => offer.Identity.BlockY)
            .ThenBy(offer => offer.Identity.Building.Index)
            .ToArray();
        return Array.AsReadOnly(offers);
    }

    internal static IReadOnlyList<DaggerfallShipOffer> ShipsForSale(bool atPortTown, int region,
        DaggerfallPropertyTuning? tuning = null)
    {
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        if (!atPortTown) return [];
        if (region is < 0 or >= 62) throw new ArgumentOutOfRangeException(nameof(region));
        DaggerfallShipOffer[] offers = Enum.GetValues<DaggerfallShipType>()
            .Select(type =>
            {
                ulong price = ShipPrice(type, values);
                return new DaggerfallShipOffer(type, region, price, SalePrice(price, values), ShipArrival(type, values), true);
            })
            .ToArray();
        return Array.AsReadOnly(offers);
    }

    internal static ulong HousePrice(DaggerfallHouseCandidate candidate, DaggerfallPropertyTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        candidate.Validate();
        if (!IsEligibleHouse(candidate))
            throw new ArgumentException("The supplied building is not an eligible house candidate.", nameof(candidate));
        double price = Math.Truncate(candidate.ModelRadius * values.HousePricePerModelRadius);
        if (!double.IsFinite(price) || price <= 0d || price > ulong.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(candidate), "The house model radius produces no representable price.");
        return checked((ulong)price);
    }

    internal static ulong ShipPrice(DaggerfallShipType type, DaggerfallPropertyTuning? tuning = null)
    {
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        return type switch
        {
            DaggerfallShipType.Small => values.SmallShipPrice,
            DaggerfallShipType.Large => values.LargeShipPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    internal static ulong SalePrice(ulong purchasePrice, DaggerfallPropertyTuning? tuning = null)
    {
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        return checked(purchasePrice * (ulong)values.SalePercent / 100UL);
    }

    internal static DaggerfallShipArrivalAnchor ShipArrival(DaggerfallShipType type, DaggerfallPropertyTuning? tuning = null)
    {
        DaggerfallPropertyTuning values = (tuning ?? DaggerfallPropertyTuning.Donor).Validate();
        return type switch
        {
            DaggerfallShipType.Small => values.SmallShipArrival,
            DaggerfallShipType.Large => values.LargeShipArrival,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    internal static DaggerfallPropertyAccessResult HouseAccess(DaggerfallHouseIdentity owned,
        DaggerfallHouseEntryContext requested)
    {
        owned.Validate();
        requested.Validate();
        if (owned.Site != requested.Site)
            return DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.WrongSite,
                "The owned house is at another site.");
        if (owned.Building != requested.Building || owned.BlockX != requested.BlockX || owned.BlockY != requested.BlockY)
            return DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.WrongBuilding,
                "The requested building is not the owned house.");
        return DaggerfallPropertyAccessResult.Allow("The owned house is available here.");
    }

    internal static DaggerfallPropertyAccessResult MissingHouseAccess(DaggerfallHouseEntryContext requested)
    {
        requested.Validate();
        return DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.MissingOwnership,
            "You do not own this house.");
    }

    internal static DaggerfallPropertyAccessResult ShipBoarding(DaggerfallShipType owned,
        DaggerfallShipAccessContext context)
    {
        if (!Enum.IsDefined(owned)) throw new ArgumentOutOfRangeException(nameof(owned));
        context.Validate();
        if (context.IsIndoor)
            return DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.Indoor,
                "You cannot board a ship indoors.");
        if (context.IsDungeon)
            return DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.Dungeon,
                "The ship is unavailable inside a dungeon.");
        if (!context.IsAvailableAtSite)
            return DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.ShipUnavailableAtSite,
                "The owned ship is unavailable at this site.");
        return DaggerfallPropertyAccessResult.Allow($"Your {owned.ToString().ToLowerInvariant()} ship is available.");
    }

    internal static DaggerfallPropertyAccessResult ShipStorage(DaggerfallShipType owned, bool onShip)
    {
        if (!Enum.IsDefined(owned)) throw new ArgumentOutOfRangeException(nameof(owned));
        return onShip
            ? DaggerfallPropertyAccessResult.Allow("Ship storage is available while aboard.")
            : DaggerfallPropertyAccessResult.Deny(DaggerfallPropertyAccessDenial.NotOnShip,
                "Ship storage is available only while aboard the owned ship.");
    }

    internal static DaggerfallPropertyStorageDecision StorageAfter(DaggerfallPropertyStorageEvent @event) => @event switch
    {
        DaggerfallPropertyStorageEvent.Unload => new(true, "Property contents remain attached to their durable property storage while the site unloads."),
        DaggerfallPropertyStorageEvent.Sale => new(true, "Property contents remain attached to their durable property storage after sale."),
        _ => throw new ArgumentOutOfRangeException(nameof(@event)),
    };
}
