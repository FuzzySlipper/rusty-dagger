using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the active site's dungeon actions do and how the player sets them off: a family action run by its graph (a
/// spell, a teleport, a door, a mover, a hazard, or text through Text's input), the door and action rays the player's
/// use and swing cast, a door's actions as it opens with its first-contact warning shown first, and the diagnostics
/// a dispatch reports as an outcome line.
/// </summary>
/// <param name="graphs">Every site's action graph by profile (the session's state).</param>
/// <param name="activeProfile">The active site's profile.</param>
/// <param name="inputs">The active site's inputs: its dungeon map and actions.</param>
/// <param name="doors">The active site's doors.</param>
/// <param name="motion">The active site's movable models.</param>
/// <param name="triggers">The action triggers' Engine entities and poses.</param>
/// <param name="environment">The character step environment the rays are cast in.</param>
/// <param name="localToProfile">A session-local point in the active profile's frame.</param>
/// <param name="relocate">Stands the player at an anchor of a profile (the site crossing's), for a teleport.</param>
/// <param name="spreadHurt">Spreads a trap's hurt to the actors about it (the traps').</param>
/// <param name="castSpell">Casts an action's spell at the player (Magic's casting).</param>
/// <param name="hazardHarm">Harm a hazard deals the player, as hazard damage facts.</param>
/// <param name="generation">The admitted update's generation, for a drain's fact.</param>
/// <param name="step">The admitted simulation step, for a drain's fact.</param>
/// <param name="textAction">Runs a text or riddle action (Text's).</param>
/// <param name="doorTextShown">Whether the dungeon text now shown is this action's door warning (Text's).</param>
/// <param name="line">Says a diagnostic on the outcome line (UiProjection's).</param>
internal sealed class DaggerfallDungeonActions(
    IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> graphs,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallSiteProfile> inputs,
    Func<DaggerfallDoorRuntime> doors,
    Func<DaggerfallDungeonMotionProjection> motion,
    DaggerfallDungeonActionTriggerRuntime triggers,
    SpatialMovementSystem spatial,
    Func<CharacterStepEnvironment> environment,
    Func<Vector3, Vector3> localToProfile,
    Func<DaggerfallWorldProfileKey, DaggerfallSiteAnchor, bool> relocate,
    PlayerControlState player,
    ActorsState actors,
    float eyeHeight,
    double collisionVoxel,
    Action<DaggerfallDungeonActionDefinition, ulong> spreadHurt,
    CombatResolution combat,
    IRandomService random,
    Func<DaggerfallActionCastSource, int, Vector3?, DaggerfallCastResult> castSpell,
    FactBuffer<IProductFact> facts,
    Action<DamageResult> hazardHarm,
    Func<ulong?> generation,
    Func<ulong?> step,
    Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?> textAction,
    Func<string, bool> doorTextShown,
    Action<string> line)
{
    private DaggerfallDoorRuntime Doors => doors();

    /// <summary>Forgets the door warnings shown ahead of this use; a use starts and ends with none.</summary>
    internal void ClearPreflight() => _preflightedDoorText.Clear();

    /// <summary>
    /// The Direct actions of a door the player has just used, when the active site has an action graph: whether any
    /// applied, or null with no graph.
    /// </summary>
    internal bool? TriggerDoorDirect(DaggerfallRdbDoorId door) =>
        graphs.TryGetValue(activeProfile(), out DaggerfallDungeonActionGraph? graph)
            ? Report(graph.TriggerForDoor(door, DaggerfallDungeonActionEvent.Direct))
            : null;

    /// <summary>
    /// DoorText is the donor's first-contact warning. Its valid first display
    /// owns the interaction and blocks the generic door operation; skipped or
    /// already-displayed text lets the ordinary activation proceed.
    /// </summary>
    private readonly HashSet<string> _preflightedDoorText = new(StringComparer.Ordinal);

    internal bool PreflightDoorText(DaggerfallRdbDoorId door, out bool dispatched)
    {
        dispatched = false;
        if (!graphs.TryGetValue(activeProfile(), out DaggerfallDungeonActionGraph? graph))
            return false;
        string doorId = DaggerfallDungeonActionGraph.DoorSourceId(door);
        DaggerfallDungeonActionDefinition[] doorTexts = graph.Definitions.Where(action =>
            string.Equals(action.DoorId, doorId, StringComparison.Ordinal)
            && action.ActionFlag == (byte)DaggerfallDungeonActionFlag.DoorText).ToArray();
        if (doorTexts.Length == 0) return false;

        dispatched = doorTexts.Any(action => DirectTrigger(action.TriggerFlag, action.DoorId is not null));
        List<DaggerfallDungeonActionDispatch> dispatches = dispatched
            ? [.. graph.TriggerForDoor(door, DaggerfallDungeonActionEvent.Direct)]
            : [];
        // A Door-triggered warning also precedes the first open. Dispatch only that text here;
        // the normal Door event still runs the other linked actions after a later accepted open.
        foreach (DaggerfallDungeonActionDefinition action in doorTexts.Where(action =>
            DoorTrigger(action.TriggerFlag) && graph.State[action.Id].ActivationCount == 0))
        {
            dispatches.Add(graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Door));
            _preflightedDoorText.Add(action.Id);
        }
        if (dispatches.Count == 0) return false;
        _ = Report(dispatches);
        foreach (DaggerfallDungeonActionExecution execution in dispatches.SelectMany(dispatch => dispatch.Executions))
        {
            if (execution.Outcome is not (DaggerfallDungeonActionOutcome.Applied or DaggerfallDungeonActionOutcome.AwaitingAnswer))
                continue;
            DaggerfallDungeonActionDefinition? definition = graph.Definitions.SingleOrDefault(action => action.Id == execution.ActionId);
            if (definition?.ActionFlag != (byte)DaggerfallDungeonActionFlag.DoorText) continue;
            if (doorTextShown(execution.ActionId)) return true;
        }
        return false;
    }

    private static bool DirectTrigger(uint rawTrigger, bool doorAction)
    {
        uint trigger = doorAction && rawTrigger > 0x0A ? rawTrigger & 0x0F : rawTrigger;
        return trigger is (uint)DaggerfallDungeonTriggerFlag.Direct
            or (uint)DaggerfallDungeonTriggerFlag.Direct6
            or (uint)DaggerfallDungeonTriggerFlag.MultiTrigger
            or (uint)DaggerfallDungeonTriggerFlag.Collision09;
    }

    private static bool DoorTrigger(uint rawTrigger) =>
        (rawTrigger > 0x0A ? rawTrigger & 0x0F : rawTrigger) == (uint)DaggerfallDungeonTriggerFlag.Door;

    internal DaggerfallRdbDoorId? FindDoor(Vector3 direction, double maximumDistance)
    {
        if (player.Position is not WorldPoint position || direction.LengthSquared() <= .000001f)
            return null;
        SpatialHit hit = spatial.CastRay(position.ToVector() + Vector3.UnitY * eyeHeight,
            direction, (float)maximumDistance, triggers.ActiveRayEntities(),
            environment());
        if (!hit.Present || hit.Kind != SpatialHitKind.Entity) return null;
        foreach (DaggerfallDoorView door in Doors.All)
            if (door.Entity.Value == hit.Entity) return door.Id;
        return null;
    }

    /// <summary>Routes one static or action-door Engine hit to the normalized graph for the active profile.</summary>
    internal bool TriggerRay(Vector3 direction, double maximumDistance, DaggerfallDungeonActionEvent @event)
    {
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z)
            || direction.LengthSquared() <= .000001f
            || !double.IsFinite(maximumDistance) || maximumDistance <= 0d
            || !graphs.TryGetValue(activeProfile(), out DaggerfallDungeonActionGraph? graph)
            || player.Position is not WorldPoint position)
            return false;

        Vector3 rayOrigin = position.ToVector() + Vector3.UnitY * eyeHeight;
        ReadOnlyMemory<SpatialEntityCollider> actionEntities = triggers.ActiveRayEntities();
        SpatialHit hit = spatial.CastRay(
            rayOrigin,
            direction,
            (float)maximumDistance,
            actionEntities,
            environment());
        if (!hit.Present) return false;

        if (hit.Kind == SpatialHitKind.Entity)
        {
            // A door view is a value type, so FirstOrDefault answers a default view rather than null when
            // nothing matches and the `is { }` test below can never fail. Only a real match may be used: a
            // default identity reaches the door graph as an invalid RDB key and ends the incarnation.
            DaggerfallDoorView? door = null;
            foreach (DaggerfallDoorView candidate in Doors.All)
            {
                if (candidate.Entity.Value != hit.Entity) continue;
                door = candidate;
                break;
            }

            if (door is { } selectedDoor)
            {
                return Report(graph.TriggerForDoor(selectedDoor.Id, @event));
            }
            if (triggers.TryResolveAction(new EntityId(hit.Entity), out string? actionId))
                return Report(graph.Trigger(actionId, @event));
            return false;
        }

        if (hit.Kind == SpatialHitKind.StaticMesh && inputs().DungeonMap is DaggerfallDungeonMapContent map)
        {
            DaggerfallDungeonMapGeometry? placement = PlacementAt(map, localToProfile(hit.Point));
            if (placement is not null)
            {
                DaggerfallDungeonActionDispatch? dispatch = graph.TriggerForPlacement(placement.PlacementId, @event);
                return Report(dispatch);
            }
        }
        return false;
    }

    internal bool Report(DaggerfallDungeonActionDispatch? dispatch)
    {
        if (dispatch is null) return false;
        DaggerfallDungeonActionExecution? diagnostic = dispatch.Executions.LastOrDefault(execution =>
            execution.Diagnostic is not null
            && (execution.Outcome is DaggerfallDungeonActionOutcome.UnsupportedAction
                or DaggerfallDungeonActionOutcome.MissingTarget
                or DaggerfallDungeonActionOutcome.InvalidVariable
                or DaggerfallDungeonActionOutcome.RejectedOperation
                or DaggerfallDungeonActionOutcome.CycleSuppressed
                || execution.Diagnostic.Contains("unknown source trigger", StringComparison.Ordinal)));
        if (diagnostic?.Diagnostic is { Length: > 0 } message) line(message);
        return dispatch.Applied;
    }

    internal DaggerfallDungeonActionExecution? Execute(DaggerfallDungeonActionDefinition action)
    {
        if (action.ActionFlag == (byte)DaggerfallDungeonActionFlag.CastSpell)
            return CastSpell(action);
        if (action.ActionFlag == (byte)DaggerfallDungeonActionFlag.Teleport)
            return Teleport(action);
        if (DaggerfallDungeonDoorActions.Execute(action, Doors) is { } door) return door;
        if (ActivateMotion(action) is { } motion) return motion;
        ulong count = graphs[activeProfile()].State[action.Id].ActivationCount;
        DaggerfallDungeonHazardActionResult? hazard = DaggerfallDungeonHazardActions.Execute(action,
            new(actors.Player.Actor, actors.Player.Actor, actors.Player.Progression.Level, count),
            combat, random);
        if (hazard is not null)
        {
            if (hazard.HealthDamage is { } damage) hazardHarm(damage);
            spreadHurt(action, count);
            if (hazard.MagickaLost > 0d)
                facts.Append(new DungeonMagickaDrainedFact(DaggerfallActorIdentity.PlayerEntityId, action.Id,
                    hazard.MagickaLost, generation() ?? 1UL, step() ?? 1UL));
            return hazard.Execution;
        }
        return textAction(action);
    }

    private DaggerfallDungeonActionExecution Teleport(DaggerfallDungeonActionDefinition action)
    {
        Vector3? position = action.NextObjectPosition;
        Vector3 degrees = action.NextObjectRotationDegrees ?? Vector3.Zero;
        Quaternion rotation = DaggerfallDungeonMotionPolicy.InitialTransform(Vector3.Zero, degrees).Rotation;
        if (action.NextActionId is string target)
        {
            // Linked actions run first; a moving target therefore uses its current admitted pose.
            if (motion().TryGetTransform(target, out Transform pose))
            { position = pose.Translation; rotation = pose.Rotation; }
            else if (inputs().DungeonActions.FirstOrDefault(value => value.Id == target) is { } next)
            {
                position ??= next.SourcePosition;
                if (next.DoorId is string doorId)
                    foreach (var door in Doors.All.Where(door => DaggerfallDungeonActionGraph.DoorSourceId(door.Id) == doorId))
                    { position = door.Pose.Translation; rotation = door.Pose.Rotation; break; }
            }
        }
        if (position is not Vector3 destination)
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon teleport '{action.Id}' has no admitted next-object position.");
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        var anchor = new DaggerfallSiteAnchor($"teleport:{action.Id}", new(destination.X, destination.Y, destination.Z),
            ActorHeading.Yaw(forward), MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)));
        return new(action.Id, relocate(activeProfile(), anchor)
            ? DaggerfallDungeonActionOutcome.Applied : DaggerfallDungeonActionOutcome.RejectedOperation);
    }

    private DaggerfallDungeonActionExecution CastSpell(DaggerfallDungeonActionDefinition action)
    {
        if (!TryCreateSource(action, out DaggerfallActionCastSource source))
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon spell action '{action.Id}' has no admitted source pose.");

        Vector3? targetPosition = player.Position is WorldPoint playerPosition
            ? playerPosition.ToVector() : null;
        DaggerfallCastResult result = castSpell(source, action.SoundIndex, targetPosition);
        return result.Outcome switch
        {
            DaggerfallCastOutcome.Ready => new(action.Id, DaggerfallDungeonActionOutcome.Applied,
                Diagnostic: $"Dungeon spell action '{action.Id}' readied the player's caster-only spell without a spell-point cost."),
            DaggerfallCastOutcome.Released => new(action.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange,
                Diagnostic: $"Dungeon spell action '{action.Id}' admitted a missile from action resource {source.ResourceIdentity}; terminal effect delivery is pending."),
            DaggerfallCastOutcome.UnknownSpell or DaggerfallCastOutcome.UnsupportedEffect => new(action.Id,
                DaggerfallDungeonActionOutcome.UnsupportedAction,
                Diagnostic: $"Dungeon spell action '{action.Id}' could not admit catalog ordinal {action.SoundIndex}: {result.Outcome}."),
            DaggerfallCastOutcome.InvalidTarget or DaggerfallCastOutcome.SourceUnavailable => new(action.Id,
                DaggerfallDungeonActionOutcome.RejectedOperation,
                Diagnostic: $"Dungeon spell action '{action.Id}' did not admit its target/source: {result.Outcome}."),
            _ => new(action.Id, DaggerfallDungeonActionOutcome.RejectedOperation,
                Diagnostic: $"Dungeon spell action '{action.Id}' returned {result.Outcome} without an admitted delivery."),
        };
    }

    private bool TryCreateSource(DaggerfallDungeonActionDefinition action,
        out DaggerfallActionCastSource source)
    {
        Vector3? origin = action.SourcePosition;
        if (origin is Vector3 flat && triggers.TryGetTransform(action.Id, out Transform triggerTransform))
            origin = flat + triggerTransform.Translation;
        if (origin is null && motion().TryGetTransform(action.Id, out Transform transform))
            origin = transform.Translation;
        if (origin is null && action.DoorId is string doorId)
        {
            foreach (DaggerfallDoorView door in Doors.All)
            {
                if (!string.Equals(DaggerfallDungeonActionGraph.DoorSourceId(door.Id), doorId, StringComparison.Ordinal)) continue;
                origin = door.Pose.Translation;
                break;
            }
        }
        source = new(action.Id,
            DaggerfallDungeonActionTriggerRuntime.StableIdentity(activeProfile().LogicalId, action.Id),
            DaggerfallActorIdentity.PlayerEntityId,
            origin ?? default,
            actors.Player.Progression.Level);
        return origin is not null && source.IsValid;
    }

    /// <summary>
    /// Admits a normalized motion action through the active site's Engine-backed
    /// projection. Non-motion flags return null so the named hazard and text
    /// owners remain the next family handlers.
    /// </summary>
    private DaggerfallDungeonActionExecution? ActivateMotion(DaggerfallDungeonActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!DaggerfallDungeonMotionPolicy.TryInterpret(action, modelDescription: null, out _)) return null;
        if (!motion().TryGetEntity(action.Id, out _))
        {
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon motion action '{action.Id}' has no admitted action-model target.");
        }

        DaggerfallDungeonMotionActivation activation = motion().Activate(action.Id);
        return activation == DaggerfallDungeonMotionActivation.IgnoredWhileMoving
            ? new(action.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange,
                Diagnostic: $"Dungeon motion action '{action.Id}' was retriggered while its tween was moving.")
            : new(action.Id, DaggerfallDungeonActionOutcome.Applied,
                Diagnostic: $"Dungeon motion action '{action.Id}' admitted {activation}.");
    }

    internal bool Report(IReadOnlyList<DaggerfallDungeonActionDispatch> dispatches)
    {
        bool applied = false;
        foreach (DaggerfallDungeonActionDispatch dispatch in dispatches)
            applied |= Report(dispatch);
        return applied;
    }

    /// <summary>Resolves an Engine surface point only when exactly one normalized placement owns it.</summary>
    private DaggerfallDungeonMapGeometry? PlacementAt(DaggerfallDungeonMapContent content, Vector3 point)
    {
        float tolerance = (float)collisionVoxel;
        DaggerfallDungeonMapGeometry? matched = null;
        foreach (DaggerfallDungeonMapGeometry geometry in content.GeometryPlacements.Where(geometry =>
            point.X >= geometry.BoundsMin.X - tolerance && point.X <= geometry.BoundsMax.X + tolerance
            && point.Y >= geometry.BoundsMin.Y - tolerance && point.Y <= geometry.BoundsMax.Y + tolerance
            && point.Z >= geometry.BoundsMin.Z - tolerance && point.Z <= geometry.BoundsMax.Z + tolerance))
        {
            if (matched is not null) return null;
            matched = geometry;
        }
        return matched;
    }

    internal void TriggerDoor(DaggerfallRdbDoorId door)
    {
        if (graphs.TryGetValue(activeProfile(), out DaggerfallDungeonActionGraph? graph))
        {
            if (_preflightedDoorText.Count == 0)
            {
                _ = Report(graph.TriggerForDoor(door, DaggerfallDungeonActionEvent.Door));
                return;
            }
            string doorId = DaggerfallDungeonActionGraph.DoorSourceId(door);
            _ = Report(graph.Definitions
                .Where(action => string.Equals(action.DoorId, doorId, StringComparison.Ordinal)
                    && !_preflightedDoorText.Contains(action.Id))
                .OrderBy(action => action.Id, StringComparer.Ordinal)
                .Select(action => graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Door))
                .ToArray());
        }
    }
}
