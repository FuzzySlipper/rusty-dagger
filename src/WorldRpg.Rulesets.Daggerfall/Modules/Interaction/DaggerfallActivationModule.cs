using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

/// <summary>The retained Daggerfall activation intent. A mode changes intent; it never starts an attack or use action.</summary>
internal enum DaggerfallActivationMode { Grab, Info, Talk, Steal, Bash }

/// <summary>The named Daggerfall owner selected by an activation query.</summary>
/// <remarks>
/// These are deliberately concrete rather than a string command registry. Door/container/NPC/item
/// policy joins this switch when its real owner lands; an unavailable owner reports that fact and
/// never pretends an action succeeded.
/// </remarks>
internal enum DaggerfallActivationTargetKind { Door, Portal, Container, Npc, Item, Corpse }

/// <summary>One live Daggerfall target contributed by its owning domain.</summary>
internal readonly record struct DaggerfallActivationTarget(
    DaggerfallActivationTargetKind Kind,
    DurableIdentityReference Identity,
    EntityId Entity,
    ulong QueryIdentity,
    WorldPoint Position,
    int Precedence,
    string? Label = null,
    double? ReachDistance = null)
{
    internal InteractionTargetCandidate ToKitCandidate() => new(Entity, Identity, QueryIdentity, Position, Precedence,
        Label ?? Kind.ToString(), ReachDistance);
}

/// <summary>Stable mode and target selected from one Engine perception query.</summary>
internal sealed record DaggerfallActivationSelection(DaggerfallActivationMode Mode, DaggerfallActivationTarget Target);

/// <summary>Completed or explicitly refused work from the one owner for a selected target kind.</summary>
internal sealed record DaggerfallActivationOutcome(bool Applied, string Message);

// These are deliberately named contracts, rather than registrations in a generic command bus.
// A domain can contribute only the target type it owns and receives only its own activation call.
internal interface IDaggerfallDoorActivationOwner
{
    IEnumerable<DaggerfallActivationTarget> DoorTargets();
    DaggerfallActivationOutcome ActivateDoor(DaggerfallActivationSelection selection);
}

internal interface IDaggerfallPortalActivationOwner
{
    IEnumerable<DaggerfallActivationTarget> PortalTargets();
    DaggerfallActivationOutcome ActivatePortal(DaggerfallActivationSelection selection);
}

internal interface IDaggerfallContainerActivationOwner
{
    IEnumerable<DaggerfallActivationTarget> ContainerTargets();
    DaggerfallActivationOutcome ActivateContainer(DaggerfallActivationSelection selection);
}

internal interface IDaggerfallNpcActivationOwner
{
    IEnumerable<DaggerfallActivationTarget> NpcTargets();
    DaggerfallActivationOutcome ActivateNpc(DaggerfallActivationSelection selection);
}

internal interface IDaggerfallItemActivationOwner
{
    IEnumerable<DaggerfallActivationTarget> ItemTargets();
    DaggerfallActivationOutcome ActivateItem(DaggerfallActivationSelection selection);
}

internal interface IDaggerfallCorpseActivationOwner
{
    IEnumerable<DaggerfallActivationTarget> CorpseTargets();
    DaggerfallActivationOutcome ActivateCorpse(DaggerfallActivationSelection selection);
}

/// <summary>Explicitly composed activation owners. A missing owner contributes no target and cannot report success.</summary>
internal sealed class DaggerfallActivationContributions(
    IDaggerfallCorpseActivationOwner corpse,
    IDaggerfallDoorActivationOwner? door = null,
    IDaggerfallPortalActivationOwner? portal = null,
    IDaggerfallContainerActivationOwner? container = null,
    IDaggerfallNpcActivationOwner? npc = null,
    IDaggerfallItemActivationOwner? item = null)
{
    private readonly IDaggerfallCorpseActivationOwner _corpse = corpse ?? throw new ArgumentNullException(nameof(corpse));
    private readonly IDaggerfallDoorActivationOwner? _door = door;
    private readonly IDaggerfallPortalActivationOwner? _portal = portal;
    private readonly IDaggerfallContainerActivationOwner? _container = container;
    private readonly IDaggerfallNpcActivationOwner? _npc = npc;
    private readonly IDaggerfallItemActivationOwner? _item = item;

    internal IEnumerable<DaggerfallActivationTarget> Targets()
    {
        foreach (DaggerfallActivationTarget target in _corpse.CorpseTargets()) yield return RequireKind(target, DaggerfallActivationTargetKind.Corpse);
        if (_door is not null) foreach (DaggerfallActivationTarget target in _door.DoorTargets()) yield return RequireKind(target, DaggerfallActivationTargetKind.Door);
        if (_portal is not null) foreach (DaggerfallActivationTarget target in _portal.PortalTargets()) yield return RequireKind(target, DaggerfallActivationTargetKind.Portal);
        if (_container is not null) foreach (DaggerfallActivationTarget target in _container.ContainerTargets()) yield return RequireKind(target, DaggerfallActivationTargetKind.Container);
        if (_npc is not null) foreach (DaggerfallActivationTarget target in _npc.NpcTargets()) yield return RequireKind(target, DaggerfallActivationTargetKind.Npc);
        if (_item is not null) foreach (DaggerfallActivationTarget target in _item.ItemTargets()) yield return RequireKind(target, DaggerfallActivationTargetKind.Item);
    }

    internal DaggerfallActivationOutcome Activate(DaggerfallActivationSelection selection) => selection.Target.Kind switch
    {
        DaggerfallActivationTargetKind.Corpse => _corpse.ActivateCorpse(selection),
        DaggerfallActivationTargetKind.Door when _door is not null => _door.ActivateDoor(selection),
        DaggerfallActivationTargetKind.Portal when _portal is not null => _portal.ActivatePortal(selection),
        DaggerfallActivationTargetKind.Container when _container is not null => _container.ActivateContainer(selection),
        DaggerfallActivationTargetKind.Npc when _npc is not null => _npc.ActivateNpc(selection),
        DaggerfallActivationTargetKind.Item when _item is not null => _item.ActivateItem(selection),
        _ => new(false, $"{selection.Target.Kind} activation is not available in this session."),
    };

    private static DaggerfallActivationTarget RequireKind(DaggerfallActivationTarget target, DaggerfallActivationTargetKind expected) =>
        target.Kind == expected ? target : throw new InvalidOperationException($"The {expected} activation owner contributed a {target.Kind} target.");
}

/// <summary>
/// Daggerfall policy over Kit's Engine-backed interaction selection. It has no domain mutation:
/// the session dispatches a selected door, container, NPC, item, or corpse to the owner named by
/// <see cref="DaggerfallActivationTargetKind"/>.
/// </summary>
internal sealed class DaggerfallActivationModule(InteractionTargetingService targeting, DaggerfallLootInteractionTuning reach, DaggerfallActivationContributions contributions)
{
    private readonly InteractionTargetingService _targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
    private readonly DaggerfallLootInteractionTuning _reach = (reach ?? throw new ArgumentNullException(nameof(reach))).Validate();
    private readonly DaggerfallActivationContributions _contributions = contributions ?? throw new ArgumentNullException(nameof(contributions));

    internal DaggerfallActivationMode Mode { get; private set; } = DaggerfallActivationMode.Grab;
    internal InteractionTargetingEvidence? LastEvidence => _targeting.LastEvidence;

    internal bool ChangeMode(DaggerfallActivationMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Mode == mode) return false;
        Mode = mode;
        return true;
    }

    internal DaggerfallActivationOutcome Activate(
        EntityId player,
        PlayerControlState control,
        LookReceipt look)
    {
        ArgumentNullException.ThrowIfNull(control);
        DaggerfallActivationTarget[] declared = _contributions.Targets().ToArray();
        Dictionary<DurableIdentityReference, DaggerfallActivationTarget> byIdentity = declared
            .GroupBy(target => target.Identity)
            .ToDictionary(group => group.Key, group => group.Single());
        DaggerfallActivationOutcome? outcome = null;
        InteractionUseReceipt use = _targeting.Activate(
            player,
            control.Position,
            look.Forward,
            _reach.MaximumDistance,
            _reach.MinimumFacingCosine,
            declared.Select(target => target.ToKitCandidate()),
            candidate =>
            {
                if (!byIdentity.TryGetValue(candidate.Identity, out DaggerfallActivationTarget target))
                    return new(false, "The selected target is no longer available.");
                outcome = _contributions.Activate(new DaggerfallActivationSelection(Mode, target));
                return new(outcome.Applied, outcome.Message);
            });
        return outcome ?? new(false, use.Reason == InteractionReason.NoCandidate
            ? "No eligible target within reach."
            : use.Message);
    }
}
