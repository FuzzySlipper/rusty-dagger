using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One remembered destination, in the destination profile's authored frame.</summary>
internal sealed record DaggerfallTeleportAnchor(
    DaggerfallWorldProfileKeySave Profile, DaggerfallSiteReturnPoseSave Pose,
    DaggerfallWorldProfileKeySave? ReturnProfile, DaggerfallSiteReturnPoseSave? ReturnPose,
    DaggerfallShipType? ShipType, DaggerfallTransportSave? ShipReturn)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Profile); ArgumentNullException.ThrowIfNull(Pose);
        Profile.Validate(); Pose.Validate(); ReturnProfile?.Validate(); ReturnPose?.Validate();
        if ((ReturnProfile is null) != (ReturnPose is null))
            throw new ArgumentException("A teleport anchor entrance requires both its return profile and pose.");
        if ((ShipType is null) != (ShipReturn is null) || ShipType is { } type && !Enum.IsDefined(type))
            throw new ArgumentException("A ship teleport anchor requires its owned type and land return.");
        if (ShipReturn is { } ship)
        {
            ship.Validate();
            if (!ship.OnShip || Profile.Require().Kind == DaggerfallWorldProfileKind.Dungeon)
                throw new ArgumentException("A ship teleport anchor must be aboard its deck or interior.");
            if (Profile.Require().Kind == DaggerfallWorldProfileKind.Exterior && ReturnProfile is not null
                || Profile.Require().Kind == DaggerfallWorldProfileKind.Interior
                    && (ReturnProfile?.Require() is not { Kind: DaggerfallWorldProfileKind.Exterior } deck || deck.Site != Profile.Require().Site))
                throw new ArgumentException("A ship teleport anchor must retain only its doorway-to-deck relation.");
        }
    }

    internal void Resolve(DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallSiteProfiles? profiles, DaggerfallTuning tuning)
    {
        Validate();
        void Admit(DaggerfallWorldProfileKey key)
        {
            if (key != inputs.ProfileKey) _ = (profiles ?? throw new ArgumentException("Teleport anchor names an unadmitted profile.")).Require(key);
        }
        var profile = Profile.Require(); Admit(profile);
        if (ReturnProfile is { } doorway) Admit(doorway.Require());
        if (ShipReturn?.ShipReturnProfile is { } land) Admit(land.Require());
        var site = definitions.Locations.Records.Single(record => record.Id == profile.Site);
        if (ShipType is { } ship)
        {
            var arrival = DaggerfallPropertyPolicy.ShipArrival(ship, tuning.Property);
            if (site.Kind != DaggerfallSiteKind.HomeYourShips || site.MapPixelX != arrival.MapPixelX || site.MapPixelY != arrival.MapPixelY)
                throw new ArgumentException("Teleport ship anchor does not name that ship's actual world site.");
        }
        else if (site.Kind == DaggerfallSiteKind.HomeYourShips)
            throw new ArgumentException("Teleport ship anchor is missing its boarding return context.");
    }
}

internal sealed record DaggerfallTeleportView(string Revision, bool AnchorSet);

internal sealed partial class DaggerfallSession
{
    private string? _pendingTeleport;
    private DaggerfallTeleportAnchor? _teleportAnchor;
    internal DaggerfallTeleportView? TeleportView => _pendingTeleport is { } request ? new(request, _teleportAnchor is not null) : null;

    internal void ChooseTeleport(string revision, string choice)
    {
        if (_pendingTeleport != revision) { Presentation.SetOutcome("Teleport choice is no longer current."); return; }
        _pendingTeleport = null;
        if (choice == "cancel") { Presentation.SetOutcome("Teleport cancelled."); return; }
        if (choice == "anchor")
        {
            var player = State.PlayerControl;
            var position = _sites.LocalToProfile(player.Position!.Value.ToVector());
            var returned = _site.ReturnPose;
            _teleportAnchor = new(DaggerfallWorldProfileKeySave.Capture(_activeProfileKey),
                new(position.X, position.Y, position.Z, player.YawRadians, player.PitchRadians),
                _sites.ReturnProfile is { } profile ? DaggerfallWorldProfileKeySave.Capture(profile) : null,
                returned is null ? null : new(returned.Position.X, returned.Position.Y, returned.Position.Z, returned.YawRadians, returned.PitchRadians),
                State.Transport.OnShip ? State.Property.OwnedShip : null,
                State.Transport.OnShip ? State.Transport.Capture() : null);
            Presentation.SetOutcome("Teleport anchor set."); return;
        }
        if (choice != "recall") { Presentation.SetOutcome("Unknown teleport choice."); return; }
        if (_teleportAnchor is not { } anchor) { Presentation.SetOutcome("A teleport anchor must be set first."); return; }
        if (anchor.ShipType is { } ship && State.Property.OwnedShip != ship)
        { Presentation.SetOutcome("The anchored ship is no longer owned."); return; }
        try
        {
            var pose = anchor.Pose;
            if (!_sites.TryRelocatePlayer(anchor.Profile.Require(), new("teleport-recall", new(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians)))
            { Presentation.SetOutcome("The teleport destination could not be admitted."); return; }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { Presentation.SetOutcome($"Teleport failed: {error.Message}"); return; }
        _sites.RestoreReturnDestination(anchor.ReturnProfile?.Require(), anchor.ReturnPose is { } entrance
            ? new(new(entrance.X, entrance.Y, entrance.Z), entrance.YawRadians, entrance.PitchRadians) : null);
        if (anchor.ShipReturn is { } aboard) State.Transport.Restore(aboard);
        else if (State.Transport.OnShip) _ = State.Transport.LeaveShip();
        _teleportAnchor = null;
        Presentation.SetOutcome("Recalled to the teleport anchor.");
    }
}
