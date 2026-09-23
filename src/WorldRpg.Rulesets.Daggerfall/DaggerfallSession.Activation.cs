using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Contextual activation composition and named dispatch into existing Daggerfall owners.</summary>
internal sealed partial class DaggerfallSession
{
    private DaggerfallActivationModule? _activation;
    private readonly DaggerfallActivationPresentation _activationPresentation = new();

    /// <summary>
    /// Wires activation after spatial, actor, and corpse owners exist. The normal session
    /// constructor calls this once; keeping it named makes the dependency order explicit.
    /// </summary>
    private void InitializeActivation(IEngineContext engine, DaggerfallLootInteractionTuning reach)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _activation = new DaggerfallActivationModule(
            new InteractionTargetingService(engine.Perception, _spatial, State.Actors.Entities),
            reach,
            new DaggerfallActivationContributions(
                new DaggerfallCorpseActivationOwner(_corpseLoot, _lootUi, State.Actors, _facts),
                new DaggerfallDoorActivationOwner(_doors),
                new DaggerfallPortalActivationOwner(_siteProjection.Portals, ResolvePortalDestination, TryTransitionTo),
                new DaggerfallGroundActivationOwner(_groundContainers, _lootUi)));
    }

    private DaggerfallWorldProfileKey ResolvePortalDestination(string logicalProfile) =>
        (_siteProfiles ?? throw new InvalidOperationException("Site profiles have not been admitted."))
            .RequireLogicalProfile(logicalProfile).ProfileKey;

    internal DaggerfallActivationMode ActivationMode => _activation?.Mode ?? DaggerfallActivationMode.Grab;
    internal InteractionTargetingEvidence? LastActivationTargeting => _activation?.LastEvidence;
    internal DaggerfallActivationView ActivationView => _activationPresentation.View;

    /// <summary>Lets the ordinary HUD projection callback carry activation state with its snapshot.</summary>
    internal void PublishActivationView(Action<DaggerfallActivationView> publish) => _activationPresentation.Publish(publish);

    /// <summary>Consumes one parsed mode action; it does not turn into a world activation.</summary>
    private bool ApplyActivationMode(DaggerfallPlayerUiAction action)
    {
        if (action.Action != "activation-mode" || action.Mode is null || _activation is null) return false;
        DaggerfallActivationMode mode = action.Mode switch
        {
            "grab" => DaggerfallActivationMode.Grab,
            "info" => DaggerfallActivationMode.Info,
            "talk" => DaggerfallActivationMode.Talk,
            "steal" => DaggerfallActivationMode.Steal,
            "bash" => DaggerfallActivationMode.Bash,
            _ => throw new InvalidOperationException($"Parsed activation mode '{action.Mode}' is not declared."),
        };
        _activation.ChangeMode(mode);
        _activationPresentation.SetMode(mode);
        Presentation.SetOutcome(_activationPresentation.View.Message);
        return true;
    }

    /// <summary>
    /// Applies one interaction request. Returning true means contextual activation owned the input,
    /// even when no object was eligible, so callers never fall through to an unrelated use path.
    /// </summary>
    private bool TryActivateContextual(LookReceipt look)
    {
        if (_activation is null) return false;
        DaggerfallActivationOutcome outcome = _activation.Activate(
            State.Actors.Player.Actor.Entity,
            State.PlayerControl,
            look);
        _activationPresentation.Report(outcome);
        Presentation.SetOutcome(outcome.Message);
        return true;
    }

    /// <summary>The current corpse owner composed into the activation seam.</summary>
    private sealed class DaggerfallCorpseActivationOwner(
        DaggerfallCorpseLootModule loot,
        DaggerfallLootPresentation lootUi,
        ActorsState actors,
        FactBuffer<IProductFact> facts) : IDaggerfallCorpseActivationOwner
    {
        public IEnumerable<DaggerfallActivationTarget> CorpseTargets()
        {
            foreach (long actorId in loot.Corpses.Keys.OrderBy(id => id))
            {
                if (!actors.TryGet(actorId, out ActorState actor)) continue;
                yield return new(
                DaggerfallActivationTargetKind.Corpse,
                ActorsState.Identity(actorId),
                actor.Actor.Entity,
                checked((ulong)actorId),
                actor.Position,
                Precedence: 4);
            }
        }

        public DaggerfallActivationOutcome ActivateCorpse(DaggerfallActivationSelection selection)
        {
            long actorId = checked((long)selection.Target.Identity.Value);
            if (selection.Mode == DaggerfallActivationMode.Info)
                return new(true, $"You see the remains of {loot.ContainerName(actorId)}.");
            if (selection.Mode == DaggerfallActivationMode.Bash)
                return new(false, "Bash requires a door.");

            if (lootUi.OpenResolved(actorId) is { IsEmpty: true } empty)
                loot.TryCommitLoot(empty, facts);
            return lootUi.Read() is null
                ? new(false, lootUi.Message)
                : new(true, lootUi.Message);
        }
    }

    /// <summary>Source-authored portal targets that enter or return through the one site transition owner.</summary>
    private sealed class DaggerfallPortalActivationOwner : IDaggerfallPortalActivationOwner
    {
        private readonly DaggerfallSitePortalRuntime _portals;
        private readonly Func<string, DaggerfallWorldProfileKey> _resolveDestination;
        private readonly Func<DaggerfallWorldProfileKey, bool> _transition;
        private readonly IReadOnlyDictionary<ulong, DaggerfallSitePortal> _byIdentity;

        internal DaggerfallPortalActivationOwner(
            DaggerfallSitePortalRuntime portals,
            Func<string, DaggerfallWorldProfileKey> resolveDestination,
            Func<DaggerfallWorldProfileKey, bool> transition)
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
                    identity, entity, entity.Value, portal.Position, Precedence: 2, Label: "entrance", ReachDistance: portal.Radius);
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
                return _transition(_resolveDestination(portal.DestinationLogicalProfile))
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
    private sealed class DaggerfallGroundActivationOwner(DaggerfallGroundContainers ground, DaggerfallLootPresentation loot) : IDaggerfallContainerActivationOwner
    {
        public IEnumerable<DaggerfallActivationTarget> ContainerTargets()
        {
            foreach (DaggerfallGroundContainer container in ground.All.Values.OrderBy(value => value.Id))
                yield return new(DaggerfallActivationTargetKind.Container,
                    new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)container.Id)), container.Owner,
                    checked((ulong)container.Id), container.Position, Precedence: 5);
        }

        public DaggerfallActivationOutcome ActivateContainer(DaggerfallActivationSelection selection)
        {
            long id = checked((long)selection.Target.Identity.Value);
            if (selection.Mode == DaggerfallActivationMode.Info)
                return new(true, "You see dropped items.");
            if (selection.Mode == DaggerfallActivationMode.Bash)
                return new(false, "Bash requires a door.");
            if (selection.Mode == DaggerfallActivationMode.Talk)
                return new(false, "Dropped items do not answer.");
            return loot.OpenGround(id) ? new(true, loot.Message) : new(false, loot.Message);
        }
    }

    /// <summary>Activation contribution for the same persistent RDB owner used by semantic actions.</summary>
    private sealed class DaggerfallDoorActivationOwner : IDaggerfallDoorActivationOwner
    {
        private readonly DaggerfallDoorRuntime _doors;
        private readonly IReadOnlyDictionary<DaggerfallRdbDoorId, DurableIdentityReference> _identities;

        internal DaggerfallDoorActivationOwner(DaggerfallDoorRuntime doors)
        {
            _doors = doors ?? throw new ArgumentNullException(nameof(doors));
            Dictionary<DaggerfallRdbDoorId, DurableIdentityReference> identities = [];
            HashSet<DurableIdentityReference> assigned = [];
            foreach (DaggerfallDoorView door in _doors.All)
            {
                DurableIdentityReference identity = new(DurableIdentityKind.Resource, StableIdentity(door.Id));
                if (!assigned.Add(identity))
                    throw new InvalidOperationException($"RDB door identity hash collision for '{door.Id}'.");
                identities.Add(door.Id, identity);
            }
            _identities = identities;
        }

        public IEnumerable<DaggerfallActivationTarget> DoorTargets()
        {
            foreach (DaggerfallDoorView door in _doors.All)
            {
                yield return new(
                    DaggerfallActivationTargetKind.Door,
                    _identities[door.Id],
                    door.Entity,
                    door.Entity.Value,
                    new WorldPoint(door.Pose.Translation.X, door.Pose.Translation.Y, door.Pose.Translation.Z),
                    Precedence: 3);
            }
        }

        public DaggerfallActivationOutcome ActivateDoor(DaggerfallActivationSelection selection)
        {
            DaggerfallRdbDoorId id = _identities.Single(pair => pair.Value == selection.Target.Identity).Key;
            DaggerfallDoorView door = _doors.Read(id);
            if (selection.Mode == DaggerfallActivationMode.Info)
                return new(true, Describe(door));
            if (selection.Mode == DaggerfallActivationMode.Talk)
                return new(false, "The door does not answer.");
            if (selection.Mode == DaggerfallActivationMode.Steal)
                return new(false, "Lockpicking is not available yet.");

            DaggerfallDoorOperationResult result = selection.Mode == DaggerfallActivationMode.Bash
                ? _doors.Bash(id)
                : door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening
                    ? _doors.Close(id, DaggerfallDoorOperationSource.Player)
                    : _doors.Open(id, DaggerfallDoorOperationSource.Player);
            return new(result == DaggerfallDoorOperationResult.Started, Message(result, door.Motion));
        }

        private static string Describe(DaggerfallDoorView door) => door.Kind == DaggerfallDoorKind.Special
            ? "You see a sealed mechanism."
            : door.IsMagicallyHeld ? "You see a door held by magic."
            : door.IsLocked ? "You see a locked door."
            : door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening ? "You see an open door."
            : "You see a closed door.";

        private static string Message(DaggerfallDoorOperationResult result, DaggerfallDoorMotion before) => result switch
        {
            DaggerfallDoorOperationResult.Started when before is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening => "The door begins to close.",
            DaggerfallDoorOperationResult.Started => "The door begins to open.",
            DaggerfallDoorOperationResult.AlreadyOpen => "The door is already open.",
            DaggerfallDoorOperationResult.AlreadyClosed => "The door is already closed.",
            DaggerfallDoorOperationResult.Locked => "The door is locked.",
            DaggerfallDoorOperationResult.MagicallyHeld => "Magic holds the door fast.",
            DaggerfallDoorOperationResult.SpecialDoor => "This door only responds to its mechanism.",
            DaggerfallDoorOperationResult.BashFailed => "The door resists your blow.",
            DaggerfallDoorOperationResult.AlreadyLocked => "The door is already locked.",
            DaggerfallDoorOperationResult.AlreadyUnlocked => "The door is already unlocked.",
            _ => "The door cannot be moved.",
        };

        private static ulong StableIdentity(DaggerfallRdbDoorId id)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (char character in id.ToString())
            {
                hash ^= character;
                hash *= prime;
            }
            return hash == 0 ? 1UL : hash;
        }
    }

}
