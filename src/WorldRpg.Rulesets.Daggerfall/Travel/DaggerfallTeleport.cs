using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
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

    internal void Resolve(DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallSiteProfiles? profiles,
        Func<DaggerfallShipType, (int MapPixelX, int MapPixelY)> shipArrival)
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
            var arrival = shipArrival(ship);
            if (site.Kind != DaggerfallSiteKind.HomeYourShips || site.MapPixelX != arrival.MapPixelX || site.MapPixelY != arrival.MapPixelY)
                throw new ArgumentException("Teleport ship anchor does not name that ship's actual world site.");
        }
        else if (site.Kind == DaggerfallSiteKind.HomeYourShips)
            throw new ArgumentException("Teleport ship anchor is missing its boarding return context.");
    }
}

/// <summary>The teleport choice: set the anchor here, or recall to it once one is set.</summary>
internal sealed record DaggerfallTeleportView(string Revision, bool AnchorSet)
{
    internal IReadOnlyList<DaggerfallTeleportOption> Options =>
        [new("anchor", "Set anchor", true), new("recall", "Recall to anchor", AnchorSet)];
}

internal sealed record DaggerfallTeleportOption(string Id, string Label, bool Available);

/// <summary>
/// What the teleport asks of where the player stands and the site crossing, which the session answers: the active
/// profile, the doorway's return, a point in the active profile's own frame, and relocation.
/// </summary>
internal interface IDaggerfallTeleportSites
{
    DaggerfallWorldProfileKey ActiveProfile { get; }
    DaggerfallWorldProfileKey? ReturnProfile { get; }
    DaggerfallSiteReturnPose? ReturnPose { get; }

    /// <summary>A scene-local point in the active profile's own frame, as <see cref="TryRelocatePlayer"/> takes it.</summary>
    Vector3 ProfilePosition(WorldPoint local);

    bool TryRelocatePlayer(DaggerfallWorldProfileKey profile, DaggerfallSiteAnchor pose);
    void RestoreReturnDestination(DaggerfallWorldProfileKey? profile, DaggerfallSiteReturnPose? pose);
}

/// <summary>
/// The teleport choice a Teleport spell on the player opens: remember where the player stands (aboard a ship, the
/// ship and its land return with it), or recall there through the site crossing. Relocation, so Travel's. The
/// pending choice and the anchor are saved.
/// </summary>
/// <param name="ownedShip">The ship the player owns now, if any.</param>
internal sealed class DaggerfallTeleport(IDaggerfallTeleportSites sites, PlayerControlState playerControl,
    DaggerfallTransportPolicy transport, Func<DaggerfallShipType?> ownedShip, PresentationState presentation,
    string? restoredPending, DaggerfallTeleportAnchor? restoredAnchor)
{
    private string? _pending = restoredPending;
    private DaggerfallTeleportAnchor? _anchor = restoredAnchor;

    internal string? Pending => _pending;
    internal DaggerfallTeleportAnchor? Anchor => _anchor;
    internal DaggerfallTeleportView? View => _pending is { } request ? new(request, _anchor is not null) : null;

    internal void Request(string request) => _pending = request;

    internal void Choose(string revision, string choice)
    {
        if (_pending != revision) { presentation.SetOutcome("Teleport choice is no longer current."); return; }
        _pending = null;
        if (choice == "cancel") { presentation.SetOutcome("Teleport cancelled."); return; }
        if (choice == "anchor")
        {
            var player = playerControl;
            var position = sites.ProfilePosition(player.Position!.Value);
            var returned = sites.ReturnPose;
            _anchor = new(DaggerfallWorldProfileKeySave.Capture(sites.ActiveProfile),
                new(position.X, position.Y, position.Z, player.YawRadians, player.PitchRadians),
                sites.ReturnProfile is { } profile ? DaggerfallWorldProfileKeySave.Capture(profile) : null,
                returned is null ? null : new(returned.Position.X, returned.Position.Y, returned.Position.Z, returned.YawRadians, returned.PitchRadians),
                transport.OnShip ? ownedShip() : null,
                transport.OnShip ? transport.Capture() : null);
            presentation.SetOutcome("Teleport anchor set."); return;
        }
        if (choice != "recall") { presentation.SetOutcome("Unknown teleport choice."); return; }
        if (_anchor is not { } anchor) { presentation.SetOutcome("A teleport anchor must be set first."); return; }
        if (anchor.ShipType is { } ship && ownedShip() != ship)
        { presentation.SetOutcome("The anchored ship is no longer owned."); return; }
        try
        {
            var pose = anchor.Pose;
            if (!sites.TryRelocatePlayer(anchor.Profile.Require(), new("teleport-recall", new(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians)))
            { presentation.SetOutcome("The teleport destination could not be admitted."); return; }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { presentation.SetOutcome("The teleport failed; you remain where you are."); return; }
        sites.RestoreReturnDestination(anchor.ReturnProfile?.Require(), anchor.ReturnPose is { } entrance
            ? new(new(entrance.X, entrance.Y, entrance.Z), entrance.YawRadians, entrance.PitchRadians) : null);
        if (anchor.ShipReturn is { } aboard) transport.Restore(aboard);
        else if (transport.OnShip) _ = transport.LeaveShip();
        _anchor = null;
        presentation.SetOutcome("Recalled to the teleport anchor.");
    }
}
