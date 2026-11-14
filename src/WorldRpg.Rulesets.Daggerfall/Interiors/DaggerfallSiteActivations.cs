using System.Numerics;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The current corpse owner composed into the activation seam.</summary>
/// <param name="corpses">The bodies that hold loot, by actor id.</param>
/// <param name="containerName">What a body's loot window names it.</param>
/// <param name="sightPoint">Where a body is sighted and reached.</param>
/// <param name="open">Opens a body's loot window and says what it holds (UiProjection's).</param>
internal sealed class DaggerfallCorpseActivationOwner(
    Func<IEnumerable<long>> corpses,
    Func<long, string> containerName,
    ActorsState actors,
    Func<ActorState, WorldPoint> sightPoint,
    Func<long, DaggerfallActivationOutcome> open) : IDaggerfallCorpseActivationOwner
{
    public IEnumerable<DaggerfallActivationTarget> CorpseTargets()
    {
        foreach (long actorId in corpses().OrderBy(id => id))
        {
            if (!actors.TryGet(actorId, out ActorState actor)) continue;
            yield return new(
            DaggerfallActivationTargetKind.Corpse,
            ActorsState.Identity(actorId),
            actor.Actor.Entity,
            checked((ulong)actorId),
            sightPoint(actor),
            Precedence: 4);
        }
    }

    public DaggerfallActivationOutcome ActivateCorpse(DaggerfallActivationSelection selection)
    {
        long actorId = checked((long)selection.Target.Identity.Value);
        if (selection.Mode == DaggerfallActivationMode.Info)
            return new(true, $"You see the remains of {containerName(actorId)}.");
        if (selection.Mode == DaggerfallActivationMode.Bash)
            return new(false, "Bash requires a door.");

        return open(actorId);
    }
}

/// <summary>Source-authored portal targets that enter or return through the one site transition owner.</summary>
internal sealed class DaggerfallPortalActivationOwner : IDaggerfallPortalActivationOwner
{
    private readonly DaggerfallSitePortalRuntime _portals;
    private readonly Func<string, DaggerfallWorldProfileKey> _resolveDestination;
    private readonly Func<DaggerfallWorldProfileKey, DaggerfallSitePortal, bool> _transition;
    private readonly IReadOnlyDictionary<ulong, DaggerfallSitePortal> _byIdentity;

    internal DaggerfallPortalActivationOwner(
        DaggerfallSitePortalRuntime portals,
        Func<string, DaggerfallWorldProfileKey> resolveDestination,
        Func<DaggerfallWorldProfileKey, DaggerfallSitePortal, bool> transition)
    {
        _portals = portals ?? throw new ArgumentNullException(nameof(portals));
        _resolveDestination = resolveDestination ?? throw new ArgumentNullException(nameof(resolveDestination));
        _transition = transition ?? throw new ArgumentNullException(nameof(transition));
        _byIdentity = _portals.All.ToDictionary(value => value.Identity.Value, value => value.Portal);
    }

    public IEnumerable<DaggerfallActivationTarget> PortalTargets()
    {
        foreach ((DaggerfallSitePortal portal, DurableIdentityReference identity, EntityId entity) in _portals.All)
            yield return new(DaggerfallActivationTargetKind.Portal,
                identity, entity, entity.Value, portal.Position, Precedence: 2, Label: "entrance", ReachDistance: portal.Radius,
                SurfaceNormal: portal.Normal);
    }

    public DaggerfallActivationOutcome ActivatePortal(DaggerfallActivationSelection selection)
    {
        if (!_byIdentity.TryGetValue(selection.Target.Identity.Value, out DaggerfallSitePortal? portal))
            return new(false, "That entrance is no longer available.");
        if (selection.Mode == DaggerfallActivationMode.Info) return new(true, "You see an entrance.");
        if (selection.Mode is DaggerfallActivationMode.Bash or DaggerfallActivationMode.Steal)
            return new(false, "That entrance cannot be forced.");
        if (selection.Mode == DaggerfallActivationMode.Talk) return new(false, "The entrance does not answer.");
        try
        {
            return _transition(_resolveDestination(portal.DestinationLogicalProfile), portal)
                ? new(true, "You pass through the entrance.")
                : new(false, "The entrance cannot be used.");
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or AggregateException)
        {
            return new(false, failure.Message);
        }
    }

}

/// <summary>Activation contribution for persistent dropped-item piles.</summary>
internal sealed class DaggerfallGroundActivationOwner(Func<IEnumerable<DaggerfallGroundContainer>> targets, Func<DaggerfallGroundContainer, WorldPoint> interactionPoint, Func<long, DaggerfallActivationOutcome> open) : IDaggerfallContainerActivationOwner
{
    public IEnumerable<DaggerfallActivationTarget> ContainerTargets()
    {
        foreach (DaggerfallGroundContainer container in targets().OrderBy(value => value.Id))
            yield return new(DaggerfallActivationTargetKind.Container,
                new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)container.Id)), container.Owner,
                checked((ulong)container.Id), interactionPoint(container), Precedence: 5,
                // A pile rests on the floor, so its point lies on that surface; seen along the surface
                // the floor itself would hide it. Furniture is reached at its own interaction points.
                SurfaceNormal: container.PropertyPlacement is null ? Vector3.UnitY : null);
    }

    public DaggerfallActivationOutcome ActivateContainer(DaggerfallActivationSelection selection)
    {
        long id = checked((long)selection.Target.Identity.Value);
        if (selection.Mode == DaggerfallActivationMode.Info)
            return new(true, targets().Single(value => value.Id == id).PropertyPlacement is null ? "You see dropped items." : "You see furniture you can search.");
        if (selection.Mode == DaggerfallActivationMode.Bash)
            return new(false, "Bash requires a door.");
        if (selection.Mode == DaggerfallActivationMode.Talk)
            return new(false, "There is no one to talk to here.");
        return open(id);
    }
}
