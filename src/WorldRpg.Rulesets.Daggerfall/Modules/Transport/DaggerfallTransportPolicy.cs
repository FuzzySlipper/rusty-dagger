using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Transport;

/// <summary>The transport choices that affect a Daggerfall player's movement and travel policy.</summary>
internal enum DaggerfallTransportMode
{
    Foot,
    Horse,
    Cart,
    Ship,
}

/// <summary>Why a transport request was refused. The value is also safe to project to the UI.</summary>
internal enum DaggerfallTransportRejection
{
    None,
    Indoor,
    MissingHorse,
    MissingCart,
    MissingShip,
    ShipUnavailableAtSite,
    MissingPosition,
    NotOnShip,
    InvalidRestore,
}

/// <summary>
/// Site facts that transport policy needs. The site context remains the authority for the
/// actual Daggerfall site; this small value keeps transport from learning site-content details.
/// </summary>
internal readonly record struct DaggerfallTransportAccessContext
{
    internal bool IsIndoor { get; }
    internal bool IsDungeon { get; }
    internal float? DungeonExitDistance { get; }
    internal bool DungeonExitOverride { get; }
    internal bool ShipAccessAllowed { get; }

    public DaggerfallTransportAccessContext()
        : this(false, false, null, false, true)
    {
    }

    internal DaggerfallTransportAccessContext(bool IsIndoor = false, bool IsDungeon = false,
        float? DungeonExitDistance = null, bool DungeonExitOverride = false, bool ShipAccessAllowed = true)
    {
        this.IsIndoor = IsIndoor;
        this.IsDungeon = IsDungeon;
        this.DungeonExitDistance = DungeonExitDistance;
        this.DungeonExitOverride = DungeonExitOverride;
        this.ShipAccessAllowed = ShipAccessAllowed;
    }

    internal DaggerfallTransportAccessContext Validate()
    {
        if (DungeonExitDistance is float distance && (!float.IsFinite(distance) || distance < 0f))
            throw new ArgumentOutOfRangeException(nameof(DungeonExitDistance));
        if (!IsDungeon && DungeonExitOverride)
            throw new ArgumentException("A dungeon wagon override requires a dungeon context.", nameof(DungeonExitOverride));
        return this;
    }

    internal bool MountsAllowed => !IsIndoor;

    internal bool WagonAllowed(bool ownsCart, float wagonAccessRange)
    {
        Validate();
        if (!ownsCart) return false;
        if (!IsDungeon) return true;
        return DungeonExitOverride || DungeonExitDistance is float distance && distance < wagonAccessRange;
    }
}

/// <summary>A player pose retained while a ship operation moves the player away from land.</summary>
internal sealed record DaggerfallTransportPose(WorldPoint Position, float YawRadians, float PitchRadians)
{
    internal DaggerfallTransportPose Validate()
    {
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z)
            || !float.IsFinite(YawRadians) || !float.IsFinite(PitchRadians))
            throw new ArgumentException("A transport pose must contain finite coordinates and look angles.");
        return this;
    }
}

/// <summary>Durable transport state. The ship return pose is retained across a save boundary.</summary>
internal sealed record DaggerfallTransportSave(
    DaggerfallTransportMode Mode,
    bool OnShip,
    float? ShipReturnX = null,
    float? ShipReturnY = null,
    float? ShipReturnZ = null,
    float? ShipReturnYawRadians = null,
    float? ShipReturnPitchRadians = null)
{
    internal DaggerfallTransportSave Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        bool anyPose = ShipReturnX is not null || ShipReturnY is not null || ShipReturnZ is not null
            || ShipReturnYawRadians is not null || ShipReturnPitchRadians is not null;
        bool completePose = ShipReturnX is not null && ShipReturnY is not null && ShipReturnZ is not null
            && ShipReturnYawRadians is not null && ShipReturnPitchRadians is not null;
        if (anyPose != completePose)
            throw new ArgumentException("A transport save must carry all ship return pose fields or none.");
        if (OnShip != completePose)
            throw new ArgumentException("A ship transport save must carry its return pose exactly while on ship.");
        if (completePose)
        {
            _ = new DaggerfallTransportPose(new WorldPoint(ShipReturnX!.Value, ShipReturnY!.Value, ShipReturnZ!.Value),
                ShipReturnYawRadians!.Value, ShipReturnPitchRadians!.Value).Validate();
        }
        // TransportManager uses Ship as the requested operation, then leaves its
        // persistent land mode at Foot while the ship scene is active.
        if (OnShip && Mode != DaggerfallTransportMode.Foot)
            throw new ArgumentException("A transport save on ship must retain Foot as its persistent land mode.");
        if (!OnShip && Mode == DaggerfallTransportMode.Ship)
            throw new ArgumentException("A transport save away from ship cannot use Ship mode.");
        return this;
    }

    internal static DaggerfallTransportSave Foot { get; } = new(DaggerfallTransportMode.Foot, false);
}

/// <summary>Donor-derived movement and travel constants in their original integer units.</summary>
internal sealed record DaggerfallTransportTuning(
    int FootTravelModifier = 256,
    int HorseTravelModifier = 128,
    int CartTravelModifier = 192,
    int FootOceanMinutes = 255,
    int ShipOceanMinutes = 51,
    int WalkBaseClassicUnits = 150,
    int HorseBaseClassicUnits = 375,
    int CartBaseClassicUnits = 250,
    int WagonCapacityClassicUnits = 300_000,
    float WagonAccessRange = 5f)
{
    internal DaggerfallTransportTuning Validate()
    {
        if (FootTravelModifier <= 0 || HorseTravelModifier <= 0 || CartTravelModifier <= 0
            || FootOceanMinutes <= 0 || ShipOceanMinutes <= 0 || WalkBaseClassicUnits < 0
            || HorseBaseClassicUnits < 0 || CartBaseClassicUnits < 0 || WagonCapacityClassicUnits <= 0
            || !float.IsFinite(WagonAccessRange) || WagonAccessRange < 0f)
            throw new ArgumentOutOfRangeException(nameof(WagonCapacityClassicUnits), "Transport tuning must contain positive travel and capacity values.");
        return this;
    }

    internal static DaggerfallTransportTuning Donor { get; } = new();
}

/// <summary>One semantic transport request result, including a relocation when a ship is left.</summary>
internal sealed record DaggerfallTransportActionResult(
    bool Applied,
    DaggerfallTransportMode Mode,
    bool OnShip,
    DaggerfallTransportRejection Rejection,
    string Message,
    DaggerfallTransportPose? Relocation = null)
{
    internal static DaggerfallTransportActionResult Accepted(DaggerfallTransportMode mode, bool onShip, string message,
        DaggerfallTransportPose? relocation = null) => new(true, mode, onShip, DaggerfallTransportRejection.None, message, relocation);
}

/// <summary>
/// Ruleset-owned transport state. It only reads Engine inventory facts and emits policy outcomes;
/// the Session owns relocation and the admitted update in which these outcomes are applied.
/// </summary>
internal sealed class DaggerfallTransportPolicy
{
    internal const string HorseItemId = "template-94";
    internal const string CartItemId = "template-93";

    private readonly DaggerfallTransportTuning _tuning;
    private DaggerfallTransportMode _mode;
    private bool _onShip;
    private DaggerfallTransportPose? _shipReturnPose;

    internal DaggerfallTransportPolicy(DaggerfallTransportTuning? tuning = null)
    {
        _tuning = (tuning ?? DaggerfallTransportTuning.Donor).Validate();
    }

    internal DaggerfallTransportMode Mode => _mode;
    internal bool OnShip => _onShip;
    internal DaggerfallTransportPose? ShipReturnPose => _shipReturnPose;
    internal DaggerfallTransportTuning Tuning => _tuning;
    internal bool IsOnFoot => !_onShip && _mode == DaggerfallTransportMode.Foot;
    internal bool CanRun => IsOnFoot;

    internal bool HasHorse(InventoryView inventory) => ContainsUnique(inventory, HorseItemId);
    internal bool HasCart(InventoryView inventory) => ContainsUnique(inventory, CartItemId);

    internal DaggerfallTransportActionResult ToggleMount(InventoryView inventory, DaggerfallTransportAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        context.Validate();
        if (_onShip) return Reject(DaggerfallTransportRejection.ShipUnavailableAtSite, "Leave the ship before choosing a mount.");
        if (_mode is DaggerfallTransportMode.Horse or DaggerfallTransportMode.Cart)
        {
            _mode = DaggerfallTransportMode.Foot;
            return Accepted("You are on foot.");
        }
        if (!context.MountsAllowed) return Reject(DaggerfallTransportRejection.Indoor, "Mounts are unavailable indoors.");
        if (HasHorse(inventory))
        {
            _mode = DaggerfallTransportMode.Horse;
            return Accepted("You mounted your horse.");
        }
        if (HasCart(inventory))
        {
            _mode = DaggerfallTransportMode.Cart;
            return Accepted("You took your cart.");
        }
        return Reject(DaggerfallTransportRejection.MissingHorse, "You do not own a horse or cart.");
    }

    internal DaggerfallTransportActionResult SelectMount(DaggerfallTransportMode mode, InventoryView inventory,
        DaggerfallTransportAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        context.Validate();
        if (mode == DaggerfallTransportMode.Foot)
        {
            if (_onShip) return Reject(DaggerfallTransportRejection.ShipUnavailableAtSite, "Leave the ship before walking on land.");
            _mode = DaggerfallTransportMode.Foot;
            return Accepted("You are on foot.");
        }
        if (mode is not (DaggerfallTransportMode.Horse or DaggerfallTransportMode.Cart))
            throw new ArgumentOutOfRangeException(nameof(mode), "Only Foot, Horse, or Cart can be selected as a land mount.");
        if (_onShip || !context.MountsAllowed)
            return Reject(DaggerfallTransportRejection.Indoor, "Mounts are unavailable at this site.");
        if (mode == DaggerfallTransportMode.Horse && !HasHorse(inventory))
            return Reject(DaggerfallTransportRejection.MissingHorse, "You do not own a horse.");
        if (mode == DaggerfallTransportMode.Cart && !HasCart(inventory))
            return Reject(DaggerfallTransportRejection.MissingCart, "You do not own a cart.");
        _mode = mode;
        return Accepted(mode == DaggerfallTransportMode.Horse ? "You mounted your horse." : "You took your cart.");
    }

    internal DaggerfallTransportActionResult BoardShip(bool ownsShip, DaggerfallTransportAccessContext context,
        DaggerfallTransportPose? currentPose)
    {
        context.Validate();
        if (_onShip) return Reject(DaggerfallTransportRejection.ShipUnavailableAtSite, "You are already on the ship.");
        if (!ownsShip) return Reject(DaggerfallTransportRejection.MissingShip, "You do not own a ship.");
        if (!context.ShipAccessAllowed) return Reject(DaggerfallTransportRejection.ShipUnavailableAtSite, "The ship is unavailable at this site.");
        if (context.IsIndoor) return Reject(DaggerfallTransportRejection.Indoor, "You cannot board a ship indoors.");
        if (currentPose is null) return Reject(DaggerfallTransportRejection.MissingPosition, "A ship trip requires a current position.");
        _shipReturnPose = currentPose.Validate();
        _onShip = true;
        // Ship is a travel state rather than a riding speed. Retain Foot as the land
        // mode so leaving the ship never revives a stale horse or cart selection.
        _mode = DaggerfallTransportMode.Foot;
        return Accepted("You boarded your ship.");
    }

    internal DaggerfallTransportActionResult LeaveShip()
    {
        if (!_onShip || _shipReturnPose is null)
            return Reject(DaggerfallTransportRejection.NotOnShip, "You are not on a ship.");
        DaggerfallTransportPose returnPose = _shipReturnPose;
        _shipReturnPose = null;
        _onShip = false;
        _mode = DaggerfallTransportMode.Foot;
        return Accepted("You left the ship.", returnPose);
    }

    /// <summary>Interior transitions always end a land mount, matching TransportManager.</summary>
    internal void ForceFootOnInteriorTransition()
    {
        if (!_onShip) _mode = DaggerfallTransportMode.Foot;
    }

    /// <summary>Removes a mount that was lost or consumed from the current policy state.</summary>
    internal void Reconcile(InventoryView inventory, DaggerfallTransportAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        context.Validate();
        if (_onShip) return;
        if (!context.MountsAllowed
            || _mode == DaggerfallTransportMode.Horse && !HasHorse(inventory)
            || _mode == DaggerfallTransportMode.Cart && !HasCart(inventory))
            _mode = DaggerfallTransportMode.Foot;
    }

    internal int TravelModifier() => _mode switch
    {
        DaggerfallTransportMode.Horse => _tuning.HorseTravelModifier,
        DaggerfallTransportMode.Cart => _tuning.CartTravelModifier,
        _ => _tuning.FootTravelModifier,
    };

    internal int OceanMinutesPerMapPixel() => _onShip ? _tuning.ShipOceanMinutes : _tuning.FootOceanMinutes;

    internal int MovementBaseClassicUnits() => _mode switch
    {
        DaggerfallTransportMode.Horse => _tuning.HorseBaseClassicUnits,
        DaggerfallTransportMode.Cart => _tuning.CartBaseClassicUnits,
        _ => _tuning.WalkBaseClassicUnits,
    };

    internal double MovementSpeed(double liveSpeed, double classicToEngineRatio)
    {
        if (!double.IsFinite(liveSpeed) || !double.IsFinite(classicToEngineRatio) || classicToEngineRatio <= 0d)
            throw new ArgumentOutOfRangeException(nameof(classicToEngineRatio));
        return (liveSpeed + MovementBaseClassicUnits()) / classicToEngineRatio;
    }

    internal DaggerfallTransportSave Capture() => (_onShip
        ? new DaggerfallTransportSave(DaggerfallTransportMode.Foot, true, _shipReturnPose!.Position.X,
            _shipReturnPose.Position.Y, _shipReturnPose.Position.Z, _shipReturnPose.YawRadians, _shipReturnPose.PitchRadians)
        : new DaggerfallTransportSave(_mode, false)).Validate();

    internal void Restore(DaggerfallTransportSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        _mode = saved.Mode;
        _onShip = saved.OnShip;
        _shipReturnPose = saved.OnShip
            ? new DaggerfallTransportPose(new WorldPoint(saved.ShipReturnX!.Value, saved.ShipReturnY!.Value, saved.ShipReturnZ!.Value),
                saved.ShipReturnYawRadians!.Value, saved.ShipReturnPitchRadians!.Value).Validate()
            : null;
    }

    private DaggerfallTransportActionResult Accepted(string message, DaggerfallTransportPose? relocation = null) =>
        DaggerfallTransportActionResult.Accepted(_mode, _onShip, message, relocation);

    private DaggerfallTransportActionResult Reject(DaggerfallTransportRejection rejection, string message) =>
        new(false, _mode, _onShip, rejection, message);

    private static bool ContainsUnique(InventoryView inventory, string definition) =>
        inventory.UniqueItems.Any(item => item.Definition.Value == definition);
}
