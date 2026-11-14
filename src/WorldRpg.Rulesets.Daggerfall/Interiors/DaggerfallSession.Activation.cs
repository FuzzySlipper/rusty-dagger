using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Policies;
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

    // A wielded item's template and metal, which choose how the 3D first-person art draws it.
    private (int? Template, string Metal, bool Enchanted) HeldItem(UniqueInventoryItem item)
    {
        DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(item.Definition.Value));
        var metadata = _inventoryUi.EquippedMetadata(item.EntityId);
        return (_definitions.SourceTemplateFor(definition)?.Index, metadata?.Material ?? definition.Weapon?.Material ?? DaggerfallHeldDrawing.Steel, metadata?.HasEnchantment == true);
    }

    // The shield worn on the off arm: its template and metal, which the 3D first-person art draws it by.
    private (int Template, string Metal)? HeldShield(EquipmentRead equipment)
    {
        foreach (EquipmentAssignment assignment in equipment.Assignments)
        {
            if (assignment.Slot.Value != "left-hand") continue;
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if (definition.Shield is not null && _definitions.SourceTemplateFor(definition) is { } template)
                return (template.Index, _inventoryUi.EquippedMetadata(assignment.Item.EntityId)?.Material ?? definition.Armor?.Material ?? DaggerfallHeldDrawing.Steel);
        }
        return null;
    }

    // The arm set the 3D first-person art draws: the player's gender, and the heaviest armour on the arms.
    private string HeldArms(DaggerfallHeldArt art, EquipmentRead equipment)
    {
        if (State.RacialOverrides.Current is { State.BeastForm: true } form) return art.Arms(form.State.Kind);
        List<string> worn = [];
        foreach (EquipmentAssignment assignment in equipment.Assignments)
        {
            if (!DaggerfallArmWearPolicy.Slots.Contains(assignment.Slot.Value)) continue;
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if ((_inventoryUi.EquippedMetadata(assignment.Item.EntityId)?.Material ?? definition.Armor?.Material) is { } material) worn.Add(material);
        }
        return art.Arms(State.Character.Identity.Gender, DaggerfallArmWearPolicy.Heaviest(worn));
    }

    /// <summary>Binds live session policy once whenever a site appearance is created.</summary>
    private void ConfigureSiteAppearance(DaggerfallSiteAppearance appearance)
    {
        appearance.UseEnemyAudibleRange(() => EnemyAudibleRange);
        appearance.UsePlayerVampireGender(() => State.RacialOverrides.Current?.IsVampire == true ? State.Character.Identity.Gender == DaggerfallCharacterGender.Female : null);
        appearance.UsePlayerRide(() => (State.Transport.Mode, new Vector2(State.PlayerControl.Motion.ControlledVelocity.X, State.PlayerControl.Motion.ControlledVelocity.Z).Length(), State.PlayerControl.PitchRadians, State.Transport.Ride.TurnFrom(State.PlayerControl.YawRadians)));
        appearance.UseViewLight(_engine.Tween, _viewLight);
        appearance.UsePlayerBeastForm(() => State.RacialOverrides.Current is { State.BeastForm: true } form ? form.State.Kind : null);
        appearance.UsePeopleRigs(() => _peopleRigs);
        if (_tuning.Viewmodel.Style != DaggerfallViewmodelStyle.Classic && _composition.HeldArt is { } art)
            appearance.UseHeldArt(_engine, art, _tuning.Viewmodel.Style, _composition.ArtReplacements?.Tints ?? new Dictionary<string, DaggerfallDyeTint>(), HeldItem, HeldShield, equipment => HeldArms(art, equipment));
        if (_composition.ArtReplacements is { } refreshed)
            appearance.UseEffectArt(_engine.Presentation, _engine.Animation, refreshed, () => DrawnArt is not null);
    }

    /// <summary>
    /// Wires activation after spatial, actor, and corpse owners exist. The normal session
    /// constructor calls this once; keeping it named makes the dependency order explicit.
    /// </summary>
    private void InitializeActivation(IEngineContext engine, DaggerfallLootInteractionTuning reach)
    {
        ArgumentNullException.ThrowIfNull(engine);
        IDaggerfallNpcActivationOwner people = ComposeDialogue();
        _activation = new DaggerfallActivationModule(
            new InteractionTargetingService(engine.Perception, _spatial, State.Actors.Entities),
            reach,
            new DaggerfallActivationContributions(
                new DaggerfallCorpseActivationOwner(_corpseLoot, _lootUi, State.Actors, _facts, CorpseSightPoint),
                _doorActivation = new DaggerfallDoorActivationOwner(_doors, TriggerDungeonDoorActions, ForceLock, id => _doorSpells.Activate(_doors, id),
                    (door, forceMode) => EnterExteriorBuilding(door, forceMode, skipGreeting: false),
                    door => Entrance.Door(_doors, door, _activeProfileKey.Site), door => Entrance.Name(_doors, door, _activeProfileKey.Site),
                    Locks.DescribeDifficulty, door => Entrance.Discover(_doors, door, _activeProfileKey.Site)),
                new DaggerfallPortalActivationOwner(_sites.Projection.Portals, ResolvePortalDestination,
                    EnterPortal),
                new DaggerfallGroundActivationOwner(() => _groundContainers.All.Values, PropertyLoot.InteractionPoint, id =>
                    PropertyLoot.Open(id) is not bool privateProperty ? new(false, "That container is no longer available.")
                    : _lootUi.OpenGround(id, privateProperty) ? new(true, _lootUi.Message) : new(false, _lootUi.Message)),
                npc: people));
    }

    private DaggerfallWorldProfileKey ResolvePortalDestination(string logicalProfile) =>
        (_sites.Profiles ?? throw new InvalidOperationException("Site profiles have not been admitted."))
            .RequireLogicalKey(logicalProfile);

    internal DaggerfallActivationMode ActivationMode => _activation?.Mode ?? DaggerfallActivationMode.Grab;
    internal InteractionTargetingEvidence? LastActivationTargeting => _activation?.LastEvidence;
    /// <summary>Lets the ordinary HUD projection callback carry activation state with its snapshot.</summary>
    internal void PublishActivationView(Action<DaggerfallActivationView> publish) => publish(ActivationView);

    /// <summary>Consumes one parsed mode action; it does not turn into a world activation.</summary>
    private bool ApplyActivationMode(DaggerfallPlayerUiAction action)
    {
        if (action.Kind != DaggerfallUiActionKind.ActivationMode || action.Mode is null || _activation is null) return false;
        DaggerfallActivationMode mode = action.Mode switch
        {
            "grab" => DaggerfallActivationMode.Grab,
            "info" => DaggerfallActivationMode.Info,
            "talk" => DaggerfallActivationMode.Talk,
            "steal" => DaggerfallActivationMode.Steal,
            "lockpick" => DaggerfallActivationMode.Steal,
            "bash" => DaggerfallActivationMode.Bash,
            _ => throw new InvalidOperationException($"Parsed activation mode '{action.Mode}' is not declared."),
        };
        if (mode != DaggerfallActivationMode.Talk) _dialogue?.Close();
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
        _lastPlaytestUseStep = _latestSimulationStep ?? 0;
        _lastPlaytestUseRefusal = null;
        _preflightedDoorText.Clear();
        // Keep the door identity observed before activation: opening disables its collider, but
        // PlayerActivate invokes the ordinary door operation before its Direct action event.
        DaggerfallRdbDoorId? actionDoor = FindDungeonDoorRay(look.Forward, _tuning.LootInteraction.MaximumDistance);
        bool directDoorActionsDispatched = false;
        if (actionDoor is { } gatedDoor
            && TryTriggerDoorTextBeforeActivation(gatedDoor, out directDoorActionsDispatched))
        {
            _preflightedDoorText.Clear();
            DaggerfallActivationOutcome blocked = new(true, "The door's warning stops you.");
            _activationPresentation.Report(blocked);
            Presentation.SetOutcome(blocked.Message);
            return true;
        }
        DaggerfallActivationOutcome outcome;
        try
        {
            outcome = _activation.Activate(
                State.Actors.Player.Actor.Entity,
                State.PlayerControl,
                look);
            _lastPlaytestUseRefusal = _activation.LastEvidence?.Use.Reason.ToString();
        }
        finally { _preflightedDoorText.Clear(); }
        if (actionDoor is { } door && !directDoorActionsDispatched
            && State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? graph))
        {
            if (ReportDungeonActions(graph.TriggerForDoor(door, DaggerfallDungeonActionEvent.Direct)))
            {
                DaggerfallDoorView current = _doors.Read(door);
                string message = current.Motion switch
                {
                    DaggerfallDoorMotion.Opening => "The door begins to open.",
                    DaggerfallDoorMotion.Closing => "The door begins to close.",
                    DaggerfallDoorMotion.Open => "The door is open.",
                    _ when current.IsLocked => "The door locks.",
                    _ => "The door unlocks.",
                };
                outcome = new(true, message);
            }
        }
        else if (!directDoorActionsDispatched)
            _ = TryTriggerDungeonActionRay(look.Forward, _tuning.LootInteraction.MaximumDistance, DaggerfallDungeonActionEvent.Direct);
        _activationPresentation.Report(outcome);
        Presentation.SetOutcome(outcome.Message);
        return true;
    }

    /// <summary>
    /// DoorText is the donor's first-contact warning. Its valid first display
    /// owns the interaction and blocks the generic door operation; skipped or
    /// already-displayed text lets the ordinary activation proceed.
    /// </summary>
    private readonly HashSet<string> _preflightedDoorText = new(StringComparer.Ordinal);

    private bool TryTriggerDoorTextBeforeActivation(DaggerfallRdbDoorId door, out bool dispatched)
    {
        dispatched = false;
        if (!State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? graph))
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
        _ = ReportDungeonActions(dispatches);
        foreach (DaggerfallDungeonActionExecution execution in dispatches.SelectMany(dispatch => dispatch.Executions))
        {
            if (execution.Outcome is not (DaggerfallDungeonActionOutcome.Applied or DaggerfallDungeonActionOutcome.AwaitingAnswer))
                continue;
            DaggerfallDungeonActionDefinition? definition = graph.Definitions.SingleOrDefault(action => action.Id == execution.ActionId);
            if (definition?.ActionFlag != (byte)DaggerfallDungeonActionFlag.DoorText) continue;
            if (_dungeonTextProjection is { Kind: DaggerfallDungeonTextActionKind.DoorText, ActionId: var actionId }
                && string.Equals(actionId, execution.ActionId, StringComparison.Ordinal))
                return true;
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

    private DaggerfallDoorActivationOwner? _doorActivation;

    /// <summary>
    /// As the donor's WeaponManager.WeaponEnvDamage, a swing that reaches no creature but strikes a closed door
    /// bashes it (DaggerfallActionDoor.AttemptBash, PlayerActivate.AttemptExteriorDoorBash), through the same door
    /// owner, crime and wording the Bash activation uses; no mode has to be chosen first.
    /// </summary>
    private void TryBashDoorWithSwing(LookReceipt look)
    {
        if (_doorActivation is null || State.Kit.Targeting.LastEvidence?.SelectedTargetId is not null) return;
        if (FindDungeonDoorRay(look.Forward, _tuning.MeleeTargeting.MaximumDistance) is not { } id) return;
        DaggerfallDoorView door = _doors.Read(id);
        if (door.Motion != DaggerfallDoorMotion.Closed) return;
        foreach (DaggerfallActivationTarget target in _doorActivation.DoorTargets())
        {
            if (target.Entity != door.Entity) continue;
            DaggerfallActivationOutcome outcome = _doorActivation.ActivateDoor(new(DaggerfallActivationMode.Bash, target));
            _activationPresentation.Report(outcome);
            Presentation.SetOutcome(outcome.Message);
            return;
        }
    }

    private DaggerfallRdbDoorId? FindDungeonDoorRay(Vector3 direction, double maximumDistance)
    {
        if (State.PlayerControl.Position is not WorldPoint position || direction.LengthSquared() <= .000001f)
            return null;
        SpatialHit hit = _spatial.CastRay(position.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight,
            direction, (float)maximumDistance, _sites.Interiors.ActionTriggers.ActiveRayEntities(),
            _exterior.CharacterEnvironment(State.PlayerControl.Motion));
        if (!hit.Present || hit.Kind != SpatialHitKind.Entity) return null;
        foreach (DaggerfallDoorView door in _doors.All)
            if (door.Entity.Value == hit.Entity) return door.Id;
        return null;
    }

    /// <summary>Routes one static or action-door Engine hit to the normalized graph for the active profile.</summary>
    private bool TryTriggerDungeonActionRay(Vector3 direction, double maximumDistance, DaggerfallDungeonActionEvent @event)
    {
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z)
            || direction.LengthSquared() <= .000001f
            || !double.IsFinite(maximumDistance) || maximumDistance <= 0d
            || !State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? graph)
            || State.PlayerControl.Position is not WorldPoint position)
            return false;

        Vector3 rayOrigin = position.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight;
        ReadOnlyMemory<SpatialEntityCollider> actionEntities = _sites.Interiors.ActionTriggers.ActiveRayEntities();
        SpatialHit hit = _spatial.CastRay(
            rayOrigin,
            direction,
            (float)maximumDistance,
            actionEntities,
            _exterior.CharacterEnvironment(State.PlayerControl.Motion));
        if (!hit.Present) return false;

        if (hit.Kind == SpatialHitKind.Entity)
        {
            // A door view is a value type, so FirstOrDefault answers a default view rather than null when
            // nothing matches and the `is { }` test below can never fail. Only a real match may be used: a
            // default identity reaches the door graph as an invalid RDB key and ends the incarnation.
            DaggerfallDoorView? door = null;
            foreach (DaggerfallDoorView candidate in _doors.All)
            {
                if (candidate.Entity.Value != hit.Entity) continue;
                door = candidate;
                break;
            }

            if (door is { } selectedDoor)
            {
                return ReportDungeonActions(graph.TriggerForDoor(selectedDoor.Id, @event));
            }
            if (_sites.Interiors.ActionTriggers.TryResolveAction(new EntityId(hit.Entity), out string? actionId))
                return ReportDungeonAction(graph.Trigger(actionId, @event));
            return false;
        }

        if (hit.Kind == SpatialHitKind.StaticMesh && _sites.Projection.Inputs.DungeonMap is DaggerfallDungeonMapContent map)
        {
            DaggerfallDungeonMapGeometry? placement = PlacementAt(map, _exterior.LocalToProfile(hit.Point));
            if (placement is not null)
            {
                DaggerfallDungeonActionDispatch? dispatch = graph.TriggerForPlacement(placement.PlacementId, @event);
                return ReportDungeonAction(dispatch);
            }
        }
        return false;
    }

    private bool ReportDungeonAction(DaggerfallDungeonActionDispatch? dispatch)
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
        if (diagnostic?.Diagnostic is { Length: > 0 } message) Presentation.SetOutcome(message);
        return dispatch.Applied;
    }

    private DaggerfallDungeonActionExecution? ExecuteDungeonFamilyAction(DaggerfallDungeonActionDefinition action)
    {
        if (action.ActionFlag == (byte)DaggerfallDungeonActionFlag.CastSpell)
            return ExecuteDungeonSpellAction(action);
        if (action.ActionFlag == (byte)DaggerfallDungeonActionFlag.Teleport)
            return ExecuteDungeonTeleport(action);
        if (DaggerfallDungeonDoorActions.Execute(action, _doors) is { } door) return door;
        if (ActivateDungeonMotion(action) is { } motion) return motion;
        ulong count = State.DungeonActions[_activeProfileKey].State[action.Id].ActivationCount;
        DaggerfallDungeonHazardActionResult? hazard = DaggerfallDungeonHazardActions.Execute(action,
            new(State.Actors.Player.Actor, State.Actors.Player.Actor, State.Progression.Level, count),
            _combatResolution, _random);
        if (hazard is not null)
        {
            if (hazard.HealthDamage is { } damage) AppendDamage(damage, DaggerfallDamageCause.Hazard, 0);
            Traps.SpreadHurt(action, count);
            if (hazard.MagickaLost > 0d)
                _facts.Append(new DungeonMagickaDrainedFact(DaggerfallActorIdentity.PlayerEntityId, action.Id,
                    hazard.MagickaLost, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL));
            return hazard.Execution;
        }
        return ExecuteDungeonTextAction(action);
    }

    private DaggerfallDungeonActionExecution ExecuteDungeonTeleport(DaggerfallDungeonActionDefinition action)
    {
        Vector3? position = action.NextObjectPosition;
        Vector3 degrees = action.NextObjectRotationDegrees ?? Vector3.Zero;
        Quaternion rotation = DaggerfallDungeonMotionPolicy.InitialTransform(Vector3.Zero, degrees).Rotation;
        if (action.NextActionId is string target)
        {
            // Linked actions run first; a moving target therefore uses its current admitted pose.
            if (_sites.Projection.Motion.TryGetTransform(target, out Transform pose))
            { position = pose.Translation; rotation = pose.Rotation; }
            else if (_sites.Projection.Inputs.DungeonActions.FirstOrDefault(value => value.Id == target) is { } next)
            {
                position ??= next.SourcePosition;
                if (next.DoorId is string doorId)
                    foreach (var door in _doors.All.Where(door => DaggerfallDungeonActionGraph.DoorSourceId(door.Id) == doorId))
                    { position = door.Pose.Translation; rotation = door.Pose.Rotation; break; }
            }
        }
        if (position is not Vector3 destination)
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon teleport '{action.Id}' has no admitted next-object position.");
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        var anchor = new DaggerfallSiteAnchor($"teleport:{action.Id}", new(destination.X, destination.Y, destination.Z),
            ActorHeading.Yaw(forward), MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)));
        return new(action.Id, _sites.TryRelocatePlayer(_activeProfileKey, anchor)
            ? DaggerfallDungeonActionOutcome.Applied : DaggerfallDungeonActionOutcome.RejectedOperation);
    }

    private DaggerfallDungeonActionExecution ExecuteDungeonSpellAction(DaggerfallDungeonActionDefinition action)
    {
        if (!TryCreateDungeonActionSource(action, out DaggerfallActionCastSource source))
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon spell action '{action.Id}' has no admitted source pose.");

        Vector3? targetPosition = State.PlayerControl.Position is WorldPoint playerPosition
            ? playerPosition.ToVector() : null;
        DaggerfallCastResult result = Casting.TriggerDungeonAction(source, action.SoundIndex, targetPosition);
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

    private bool TryCreateDungeonActionSource(DaggerfallDungeonActionDefinition action,
        out DaggerfallActionCastSource source)
    {
        Vector3? origin = action.SourcePosition;
        if (origin is Vector3 flat && _sites.Interiors.ActionTriggers.TryGetTransform(action.Id, out Transform triggerTransform))
            origin = flat + triggerTransform.Translation;
        if (origin is null && _sites.Projection.Motion.TryGetTransform(action.Id, out Transform transform))
            origin = transform.Translation;
        if (origin is null && action.DoorId is string doorId)
        {
            foreach (DaggerfallDoorView door in _doors.All)
            {
                if (!string.Equals(DaggerfallDungeonActionGraph.DoorSourceId(door.Id), doorId, StringComparison.Ordinal)) continue;
                origin = door.Pose.Translation;
                break;
            }
        }
        source = new(action.Id,
            DaggerfallDungeonActionTriggerRuntime.StableIdentity(_activeProfileKey.LogicalId, action.Id),
            DaggerfallActorIdentity.PlayerEntityId,
            origin ?? default,
            State.Progression.Level);
        return origin is not null && source.IsValid;
    }

    /// <summary>
    /// Admits a normalized motion action through the active site's Engine-backed
    /// projection. Non-motion flags return null so the named hazard and text
    /// owners remain the next family handlers.
    /// </summary>
    private DaggerfallDungeonActionExecution? ActivateDungeonMotion(DaggerfallDungeonActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!DaggerfallDungeonMotionPolicy.TryInterpret(action, modelDescription: null, out _)) return null;
        if (!_sites.Projection.Motion.TryGetEntity(action.Id, out _))
        {
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon motion action '{action.Id}' has no admitted action-model target.");
        }

        DaggerfallDungeonMotionActivation activation = _sites.Projection.ActivateMotion(action.Id);
        return activation == DaggerfallDungeonMotionActivation.IgnoredWhileMoving
            ? new(action.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange,
                Diagnostic: $"Dungeon motion action '{action.Id}' was retriggered while its tween was moving.")
            : new(action.Id, DaggerfallDungeonActionOutcome.Applied,
                Diagnostic: $"Dungeon motion action '{action.Id}' admitted {activation}.");
    }

    private bool ReportDungeonActions(IReadOnlyList<DaggerfallDungeonActionDispatch> dispatches)
    {
        bool applied = false;
        foreach (DaggerfallDungeonActionDispatch dispatch in dispatches)
            applied |= ReportDungeonAction(dispatch);
        return applied;
    }

    /// <summary>Resolves an Engine surface point only when exactly one normalized placement owns it.</summary>
    private DaggerfallDungeonMapGeometry? PlacementAt(DaggerfallDungeonMapContent content, Vector3 point)
    {
        float tolerance = (float)_tuning.Spatial.CollisionVoxelSize;
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

    private void TriggerDungeonDoorActions(DaggerfallRdbDoorId door)
    {
        if (State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? graph))
        {
            if (_preflightedDoorText.Count == 0)
            {
                _ = ReportDungeonActions(graph.TriggerForDoor(door, DaggerfallDungeonActionEvent.Door));
                return;
            }
            string doorId = DaggerfallDungeonActionGraph.DoorSourceId(door);
            _ = ReportDungeonActions(graph.Definitions
                .Where(action => string.Equals(action.DoorId, doorId, StringComparison.Ordinal)
                    && !_preflightedDoorText.Contains(action.Id))
                .OrderBy(action => action.Id, StringComparer.Ordinal)
                .Select(action => graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Door))
                .ToArray());
        }
    }

    private Vector3 HorizontalFacing(bool backward)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(State.PlayerControl.YawRadians);
        float sign = backward ? -1f : 1f;
        return new(sinYaw * sign, 0f, -cosYaw * sign);
    }

    private Vector3 HorizontalRight(bool left)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(State.PlayerControl.YawRadians);
        float sign = left ? -1f : 1f;
        return new(cosYaw * sign, 0f, sinYaw * sign);
    }

    /// <summary>
    /// Where a corpse is sighted and reached: the middle of its lying sprite above the floor contact. A
    /// visibility ray to the contact itself can end on, or under, the terrain it lies on and read as
    /// occluded from every side, as DaggerTargetingPolicy.AimPoint notes for grounded actors.
    /// </summary>
    private WorldPoint CorpseSightPoint(ActorState actor)
    {
        if (ActorSprite(actor) is not { } sprite) return actor.Position;
        float height = (sprite.Corpse ?? sprite).Size.Y;
        return WorldPoint.From(actor.Position.ToVector() + (Vector3.UnitY * (height * .5F)));
    }

    /// <summary>
    /// How far above where a person stands they are sighted for use: the middle of their standing sprite, for
    /// the reason <see cref="CorpseSightPoint"/> gives. A placed person shows their billboard, anyone else their
    /// actor sprite; both stand at their pose. A person with no known sprite keeps their position.
    /// </summary>
    private double? PersonSightHeight(long id)
    {
        DaggerfallSiteProfile inputs = _sites.Projection.Inputs;
        if (State.Npcs.TryGet(id, out DaggerfallNpc? npc)
            && inputs.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? billboard))
            return billboard.Size.Y * .5d;
        return State.Actors.TryGet(id, out ActorState? actor) && ActorSprite(actor) is { } sprite ? sprite.Size.Y * .5d : null;
    }

    private NormalizedActorSprite? ActorSprite(ActorState actor)
    {
        DaggerfallSiteProfile inputs = _sites.Projection.Inputs;
        return inputs.ActorSprites.TryGetValue(actor.DurableId, out NormalizedActorSprite? placed)
            ? placed
            : DefinitionsByActor.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)
                && definition.MobileId is int mobileId && inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? mobile)
                    ? mobile.ForGender(actor.Actor.TryGet<DaggerfallActorGender>(out var gender) ? gender.Value : "unspecified")
                    : null;
    }

    /// <summary>The current corpse owner composed into the activation seam.</summary>
    private sealed class DaggerfallCorpseActivationOwner(
        DaggerfallCorpseLootModule loot,
        DaggerfallLootPresentation lootUi,
        ActorsState actors,
        FactBuffer<IProductFact> facts,
        Func<ActorState, WorldPoint> sightPoint) : IDaggerfallCorpseActivationOwner
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
                sightPoint(actor),
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
    private sealed class DaggerfallGroundActivationOwner(Func<IEnumerable<DaggerfallGroundContainer>> targets, Func<DaggerfallGroundContainer, WorldPoint> interactionPoint, Func<long, DaggerfallActivationOutcome> open) : IDaggerfallContainerActivationOwner
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

    /// <summary>Activation contribution for the same persistent RDB owner used by semantic actions.</summary>
    private sealed class DaggerfallDoorActivationOwner : IDaggerfallDoorActivationOwner
    {
        private readonly DaggerfallDoorRuntime _doors;
        private readonly IReadOnlyDictionary<DaggerfallRdbDoorId, DurableIdentityReference> _identities;
        private readonly Func<DaggerfallRdbDoorId, DaggerfallBuildingDoor?> _buildingDoor;
        private readonly Func<DaggerfallRdbDoorId, string?> _buildingName;
        private readonly Func<DaggerfallDoorView, string> _lockDifficulty;
        private readonly Action<DaggerfallRdbDoorId> _discoverBuilding;

        internal DaggerfallDoorActivationOwner(
            DaggerfallDoorRuntime doors,
            Action<DaggerfallRdbDoorId> triggerDungeonActions,
            Func<DaggerfallRdbDoorId, DaggerfallActivationMode, DaggerfallActivationOutcome> activateForce,
            Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> activateMagic,
            Func<DaggerfallRdbDoorId, DaggerfallActivationMode?, DaggerfallActivationOutcome?> enterExteriorBuilding,
            Func<DaggerfallRdbDoorId, DaggerfallBuildingDoor?> buildingDoor,
            Func<DaggerfallRdbDoorId, string?> buildingName,
            Func<DaggerfallDoorView, string> lockDifficulty,
            Action<DaggerfallRdbDoorId> discoverBuilding)
        {
            _lockDifficulty = lockDifficulty;
            _discoverBuilding = discoverBuilding;
            _buildingDoor = buildingDoor ?? throw new ArgumentNullException(nameof(buildingDoor));
            _buildingName = buildingName ?? throw new ArgumentNullException(nameof(buildingName));
            _doors = doors ?? throw new ArgumentNullException(nameof(doors));
            _triggerDungeonActions = triggerDungeonActions ?? throw new ArgumentNullException(nameof(triggerDungeonActions));
            _activateForce = activateForce ?? throw new ArgumentNullException(nameof(activateForce));
            _activateMagic = activateMagic ?? throw new ArgumentNullException(nameof(activateMagic));
            _enterExteriorBuilding = enterExteriorBuilding ?? throw new ArgumentNullException(nameof(enterExteriorBuilding));
            Dictionary<DaggerfallRdbDoorId, DurableIdentityReference> identities = [];
            HashSet<DurableIdentityReference> assigned = [];
            foreach (DaggerfallDoorView door in _doors.All)
            {
                DurableIdentityReference identity = _doors.IdentityOf(door.Id);
                if (!assigned.Add(identity))
                    throw new InvalidOperationException($"RDB door identity hash collision for '{door.Id}'.");
                identities.Add(door.Id, identity);
            }
            _identities = identities;
        }

        private readonly Action<DaggerfallRdbDoorId> _triggerDungeonActions;
        private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationMode, DaggerfallActivationOutcome> _activateForce;
        private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> _activateMagic;
        private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationMode?, DaggerfallActivationOutcome?> _enterExteriorBuilding;

        public IEnumerable<DaggerfallActivationTarget> DoorTargets(DaggerfallActivationReach? reach = null)
        {
            foreach (DaggerfallDoorView door in reach is { } within ? _doors.Near(within.From, within.Distance) : _doors.All)
            {
                yield return new(
                    DaggerfallActivationTargetKind.Door,
                    _identities[door.Id],
                    door.Entity,
                    door.Entity.Value,
                    new WorldPoint(door.Pose.Translation.X, door.Pose.Translation.Y, door.Pose.Translation.Z),
                    Precedence: 3,
                    Label: Label(door.Id));
            }
        }

        /// <summary>
        /// What ActivateDoor would do now, read without doing it: examine in Information mode, nothing in
        /// Talk, pick a locked lock in Steal (an unlocked building is entered, an unlocked dungeon door is
        /// left as it is), bash in Bash, and otherwise enter a building's entrance or open a shut dungeon
        /// door and close an open one.
        /// </summary>
        public string? PromptAction(DaggerfallActivationSelection selection)
        {
            DaggerfallRdbDoorId id = _identities.Single(pair => pair.Value == selection.Target.Identity).Key;
            DaggerfallDoorView door = _doors.Read(id);
            bool building = _doors.ExteriorBuildingOf(id) is not null;
            bool open = door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening;
            // A building's entrance takes its lock from the building's entry rule when used (a shop
            // locked after hours), so the prompt reads that rule rather than the lock the door last had.
            bool locked = _buildingDoor(id)?.Entry.Unlocked is bool unlocked ? !unlocked : door.IsLocked;
            return selection.Mode switch
            {
                DaggerfallActivationMode.Info => "Examine",
                DaggerfallActivationMode.Talk => null,
                DaggerfallActivationMode.Steal when locked => "Pick the lock of",
                // An unlocked building's entrance lets a thief straight in; an unlocked dungeon door has
                // no lock to pick and Steal neither opens nor closes it, so nothing is promised.
                DaggerfallActivationMode.Steal => building ? "Enter" : null,
                DaggerfallActivationMode.Bash => "Bash",
                // Ordinary use reports a lock rather than passing it, so a locked door promises nothing.
                _ when locked => null,
                _ when building => "Enter",
                _ => open ? "Close" : "Open",
            };
        }

        public DaggerfallActivationOutcome ActivateDoor(DaggerfallActivationSelection selection)
        {
            DaggerfallRdbDoorId id = _identities.Single(pair => pair.Value == selection.Target.Identity).Key;
            // A building's entrance is as its building's entry rule says at this moment (PlayerActivate
            // judges BuildingIsUnlocked on every click), not as an earlier visit left it.
            DaggerfallBuildingDoor? building = _buildingDoor(id);
            if (building is not null)
            {
                _discoverBuilding(id);
                _doors.ApplyBuildingEntry(id, building.Entry.LockValue, !building.Entry.Unlocked);
            }
            DaggerfallDoorView door = _doors.Read(id);
            if (selection.Mode == DaggerfallActivationMode.Info)
            {
                string description = building is { Name.Length: > 0 } named ? DescribeBuilding(named) : Describe(door);
                if (door.IsLocked) description += " " + _lockDifficulty(door);
                if (building?.Entry.Unlocked == true) _ = _enterExteriorBuilding(id, null);
                return new(true, description);
            }
            if (selection.Mode == DaggerfallActivationMode.Talk)
                return new(false, "The door does not answer.");
            if (_activateMagic(id) is { } magic)
            {
                if (magic.Applied) _triggerDungeonActions(id);
                if (CanEnter(id) && _enterExteriorBuilding(id, door.IsLocked ? DaggerfallActivationMode.Steal : null) is { } entered) return entered;
                return magic;
            }
            if (selection.Mode is DaggerfallActivationMode.Steal or DaggerfallActivationMode.Bash)
            {
                DaggerfallActivationOutcome forced = _activateForce(id, selection.Mode);
                if (forced.Applied) _triggerDungeonActions(id);
                if ((forced.Applied || selection.Mode == DaggerfallActivationMode.Steal && !door.IsLocked)
                    && _doors.ExteriorBuildingOf(id) is not null)
                {
                    if (!CanEnter(id)) _ = _doors.Open(id, DaggerfallDoorOperationSource.Player);
                    if (CanEnter(id) && _enterExteriorBuilding(id, forced.Applied ? selection.Mode : null) is { } entered) return entered;
                }
                return forced;
            }

            if (_doors.ExteriorBuildingOf(id) is not null)
            {
                DaggerfallDoorOperationResult opened = _doors.Open(id, DaggerfallDoorOperationSource.Player);
                if (opened == DaggerfallDoorOperationResult.Started) _triggerDungeonActions(id);
                if (CanEnter(id) && _enterExteriorBuilding(id, null) is { } entered) return entered;
                return new(false, opened == DaggerfallDoorOperationResult.Locked && building is not null
                    ? building.LockedMessage + " " + _lockDifficulty(door) : Message(opened, door.Motion));
            }

            DaggerfallDoorOperationResult result = door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening
                ? _doors.Close(id, DaggerfallDoorOperationSource.Player)
                : _doors.Open(id, DaggerfallDoorOperationSource.Player);
            if (result == DaggerfallDoorOperationResult.Started)
                _triggerDungeonActions(id);
            return new(result == DaggerfallDoorOperationResult.Started, Message(result, door.Motion));
        }

        private bool CanEnter(DaggerfallRdbDoorId id)
        {
            DaggerfallDoorView door = _doors.Read(id);
            return !door.IsLocked && door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening;
        }

        private string? Label(DaggerfallRdbDoorId id)
        {
            if (_doors.ExteriorBuildingOf(id) is null) return null;
            return _buildingName(id);
        }

        // ActivateBuilding's Information mode: the building's name, and its hours when a store or guild is shut.
        private static string DescribeBuilding(DaggerfallBuildingDoor building) =>
            building.ClosedNotice is { } closed ? $"{building.Name}. {closed}" : building.Name;

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
            DaggerfallDoorOperationResult.LockpickFailed => "The lock resists your pick.",
            DaggerfallDoorOperationResult.AlreadyLocked => "The door is already locked.",
            DaggerfallDoorOperationResult.AlreadyUnlocked => "The door is already unlocked.",
            _ => "The door cannot be moved.",
        };

    }

}
