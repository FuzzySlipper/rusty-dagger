using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>One transport choice projected for a thin DOM control.</summary>
internal sealed record DaggerfallTransportOptionPresentation(
    string Id,
    DaggerfallTransportMode Mode,
    bool Available,
    bool Selected,
    string Label,
    string Message,
    int TravelModifier);

/// <summary>One wagon item key that a semantic UI action can select from the current projection.</summary>
internal sealed record DaggerfallWagonItemPresentation(string Key, string Definition, string Quantity);

/// <summary>Remote wagon state projected without exposing Engine entities or handles.</summary>
internal sealed record DaggerfallWagonPresentation(
    bool Exists,
    bool Accessible,
    long? Id,
    long UsedClassicUnits,
    long CapacityClassicUnits,
    ulong? StoreRevision,
    string Message,
    IReadOnlyList<DaggerfallWagonItemPresentation> Items);

/// <summary>Transport and wagon values consumed by semantic UI actions and HUD projection.</summary>
internal sealed record DaggerfallTransportPresentation(
    DaggerfallTransportMode Mode,
    bool OnShip,
    bool CanRun,
    int TravelModifier,
    int OceanMinutesPerMapPixel,
    IReadOnlyList<DaggerfallTransportOptionPresentation> Options,
    DaggerfallWagonPresentation Wagon);

/// <summary>Builds transport presentation from current policy and Engine inventory facts.</summary>
internal static class DaggerfallTransportProjection
{
    internal static DaggerfallTransportPresentation Read(DaggerfallTransportPolicy policy, InventoryView player,
        DaggerfallTransportAccessContext context, bool ownsShip, DaggerfallWagonStorage? wagon = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(player);
        context.Validate();

        bool hasHorse = policy.HasHorse(player);
        bool hasCart = policy.HasCart(player);
        DaggerfallTransportOptionPresentation[] options =
        [
            new("foot", DaggerfallTransportMode.Foot, !policy.OnShip, policy.Mode == DaggerfallTransportMode.Foot,
                "Foot", policy.OnShip ? "Leave the ship before walking on land." : "Walk on foot.", policy.Tuning.FootTravelModifier),
            new("horse", DaggerfallTransportMode.Horse, !policy.OnShip && context.MountsAllowed && hasHorse,
                policy.Mode == DaggerfallTransportMode.Horse, "Horse",
                context.IsIndoor ? "Mounts are unavailable indoors." : hasHorse ? "Ride your horse." : "You do not own a horse.",
                policy.Tuning.HorseTravelModifier),
            new("cart", DaggerfallTransportMode.Cart, !policy.OnShip && context.MountsAllowed && hasCart,
                policy.Mode == DaggerfallTransportMode.Cart, "Cart",
                context.IsIndoor ? "Mounts are unavailable indoors." : hasCart ? "Take your cart." : "You do not own a cart.",
                policy.Tuning.CartTravelModifier),
            new("ship", DaggerfallTransportMode.Ship, !policy.OnShip && ownsShip && context.ShipAccessAllowed && !context.IsIndoor,
                policy.OnShip, "Ship",
                policy.OnShip ? "You are aboard your ship." : ownsShip ? "Board your ship." : "You do not own a ship.",
                policy.Tuning.FootTravelModifier),
        ];

        bool wagonAccessible = wagon is not null && wagon.CanAccess(player, context);
        InventoryView? wagonContents = wagonAccessible ? wagon?.Read() : null;
        DaggerfallWagonPresentation wagonView = wagon is null
            ? new(false, false, null, 0, DaggerfallTransportTuning.Donor.WagonCapacityClassicUnits, null,
                "Wagon storage is unavailable.", [])
            : new(wagon.Exists, wagonAccessible, wagon.Id, wagon.CurrentWeightClassicUnits,
                wagon.Tuning.WagonCapacityClassicUnits, wagonContents?.StoreRevision,
                wagonAccessible ? "Wagon storage is available." : "The wagon is unavailable here.",
                WagonItems(wagonContents));
        return new(policy.Mode, policy.OnShip, policy.CanRun, policy.TravelModifier(), policy.OceanMinutesPerMapPixel(),
            Array.AsReadOnly(options), wagonView);
    }

    private static DaggerfallWagonItemPresentation[] WagonItems(InventoryView? contents)
    {
        if (contents is null) return [];
        DaggerfallWagonItemPresentation[] unique = contents.UniqueItems
            .OrderBy(item => item.Entity.Value)
            .Select(item => new DaggerfallWagonItemPresentation(
                $"unique:{item.Entity.Value.ToString(CultureInfo.InvariantCulture)}",
                item.Definition.Value,
                "1"))
            .ToArray();
        DaggerfallWagonItemPresentation[] stacks = contents.Stacks
            .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => new DaggerfallWagonItemPresentation(
                $"stack:{stack.Id.Value}",
                stack.Definition.Value,
                stack.Quantity.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        return [.. unique, .. stacks];
    }
}
