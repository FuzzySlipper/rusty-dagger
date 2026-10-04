using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
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
    private DaggerfallDialogueService? _dialogue;

    /// <summary>
    /// Wires activation after spatial, actor, and corpse owners exist. The normal session
    /// constructor calls this once; keeping it named makes the dependency order explicit.
    /// </summary>
    private void InitializeActivation(IEngineContext engine, DaggerfallLootInteractionTuning reach)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _dialogue = new DaggerfallDialogueService(
            State.Npcs,
            State.Actors,
            State.Social,
            State.SkillUses,
            State.Actors.Player.Stats,
            _definitions,
            _random,
            () => _site.ActiveSite,
            () => State.Character.Identity,
            view => _activationPresentation.SetDialogue(view),
            message => Presentation.SetOutcome(message), MapDirections, State.Quests.IsNpcMuted, State.Quests.QuestContacts,
            resolveDirection: ResolveDialogueDirection,
            directionDirectory: DialogueDirectory,
            questTopics: State.Quests.DialogueTopics,
            resolveQuestTopic: ResolveQuestTopic,
            calendar: () => _time.Calendar,
            workAvailable: DialogueWorkAvailable,
            variables: () => State.Variables,
            templeService: ResolveTempleService,
            guildService: ResolveGuildProvider,
            activeProfile: () => _sites.ActiveProfile,
            discloseDirection: DiscloseDialogueDirection,
            dialogueWorld: State.DialogueWorld);
        _dialogue.SynchronizeWorldState();
        _activation = new DaggerfallActivationModule(
            new InteractionTargetingService(engine.Perception, _spatial, State.Actors.Entities),
            reach,
            new DaggerfallActivationContributions(
                new DaggerfallCorpseActivationOwner(_corpseLoot, _lootUi, State.Actors, _facts),
                new DaggerfallDoorActivationOwner(_doors, TriggerDungeonDoorActions, ActivateDoorForce, ActivateDoorMagic, EnterExteriorBuilding),
                new DaggerfallPortalActivationOwner(_sites.Projection.Portals, ResolvePortalDestination, TryTransitionTo),
                new DaggerfallGroundActivationOwner(_groundContainers, _lootUi),
                npc: new DaggerfallCrimeActivationOwner(_dialogue, State.Actors, IsPickpocketTarget, PickpocketActor)));
    }

    private DaggerfallWorldProfileKey ResolvePortalDestination(string logicalProfile) =>
        (_sites.Profiles ?? throw new InvalidOperationException("Site profiles have not been admitted."))
            .RequireLogicalProfile(logicalProfile).ProfileKey;

    private DaggerfallActivationOutcome? EnterExteriorBuilding(DaggerfallRdbDoorId door)
    {
        if (_doors.ExteriorBuildingOf(door) is not { } building) return null;
        DaggerfallSiteProfiles profiles = _sites.Profiles ?? throw new InvalidOperationException("Site profiles have not been admitted.");
        DaggerfallSiteProfile[] destinations = profiles.Keys
            .Where(key => key.Site == _activeProfileKey.Site && key.Kind == DaggerfallWorldProfileKind.Interior)
            .Select(profiles.Require)
            .Where(profile => profile.InteriorBuilding is { } interior
                && interior.BlockX == building.BlockX && interior.BlockY == building.BlockY && interior.Building.Index == building.Index)
            .ToArray();
        if (destinations.Length == 0) return new(false, "This building's interior is not available in the selected content.");
        if (destinations.Length != 1) throw new InvalidOperationException($"Building '{building}' has multiple admitted interiors.");
        return TryTransitionTo(ResolvePortalDestination(destinations[0].ProfileKey.LogicalId))
            ? new(true, "You pass through the building entrance.")
            : new(false, "The building entrance cannot be used.");
    }

    internal DaggerfallActivationMode ActivationMode => _activation?.Mode ?? DaggerfallActivationMode.Grab;
    internal InteractionTargetingEvidence? LastActivationTargeting => _activation?.LastEvidence;
    internal DaggerfallActivationView ActivationView => _activationPresentation.View with
    {
        Dialogue = _activationPresentation.View.Dialogue is { } dialogue ? dialogue with
        {
            BankAvailable = CurrentBankServiceAvailable(),
            ComprehendLanguagesBonus = State.Effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId).ComprehendLanguagesBonus,
            Training = CurrentTrainingProvider(dialogue.Revision),
            Merchant = ReadCurrentMerchant(),
        } : null,
    };
    internal DaggerfallDialogueService Dialogue => _dialogue ?? throw new InvalidOperationException("The session has no dialogue owner.");

    private DaggerfallTempleServiceResult ResolveTempleService(DaggerfallNpc npc, DaggerfallDialogueTopic topic, ulong? amount)
    {
        DaggerfallInteriorBuilding? building = CurrentInteriorBuilding();
        if (building is null)
            return DaggerfallTempleServiceResult.Refused(DaggerfallTempleServiceDenial.InvalidTemple,
                "This temple interior is no longer available.");

        string service = topic switch
        {
            DaggerfallDialogueTopic.Donate => "donate",
            DaggerfallDialogueTopic.Cure => "cure-disease",
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "This dialogue topic is not a temple service."),
        };
        DaggerfallServiceProvider provider = new(npc.DurableId, npc.Site, service);
        if (topic == DaggerfallDialogueTopic.Donate)
        {
            DaggerfallTempleDonationQuote? quote = State.TempleServices.QuoteDonation(provider, building, amount ?? 0, out DaggerfallTempleServiceResult refusal);
            return quote is null ? refusal : State.TempleServices.CommitDonation(quote);
        }

        DaggerfallTempleCureQuote? cure = State.TempleServices.QuoteCure(provider, building, out DaggerfallTempleServiceResult cureRefusal);
        return cure is null ? cureRefusal : State.TempleServices.CommitCure(cure);
    }

    /// <summary>Lets the ordinary HUD projection callback carry activation state with its snapshot.</summary>
    internal void PublishActivationView(Action<DaggerfallActivationView> publish) => publish(ActivationView);

    private DaggerfallSkillTrainingProviderView? CurrentTrainingProvider(string? revision = null)
    {
        DaggerfallNpc? npc = _dialogue?.CurrentNpc(revision);
        if (npc is null || !npc.Services.Contains(DaggerfallSkillTrainingPolicy.ServiceName, StringComparer.Ordinal)) return null;
        return State.SkillTraining.ReadProvider(new(npc.DurableId, npc.Site, DaggerfallSkillTrainingPolicy.ServiceName));
    }

    private (string Text, IReadOnlyList<string> Diagnostics)? ResolveQuestTopic(long npcId, string topicId)
    {
        return State.Quests.TryResolveDialogueTopic(npcId, topicId,
            out string? text, out IReadOnlyList<string> diagnostics)
            ? (text!, diagnostics) : null;
    }

    private bool DialogueWorkAvailable(long npcId)
    {
        DaggerfallNpc npc;
        try { npc = State.Npcs.Require(npcId); }
        catch (InvalidOperationException) { return false; }
        if (npc.Appearance.FactionId == 0 || !_definitions.Factions.Factions.TryGetValue(npc.Appearance.FactionId, out DaggerfallFactionDefinition? faction))
            return false;
        bool member = faction.GuildGroup > 0 && State.Social.GuildEligibility(faction.Id).IsMember;
        int reputation = State.Social.FactionReputation(faction.Id);
        int rank = faction.GuildGroup > 0 ? State.Social.GuildEligibility(faction.Id).Rank : 0;
        return State.Quests.HasOrdinaryWorkOffer(faction.Id, member, State.Progression.Level, reputation, rank,
            State.Character.Identity.Gender, checked((int)_time.Calendar.DayNumber));
    }

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

    private DaggerfallRdbDoorId? FindDungeonDoorRay(Vector3 direction, double maximumDistance)
    {
        if (State.PlayerControl.Position is not WorldPoint position || direction.LengthSquared() <= .000001f)
            return null;
        SpatialHit hit = _spatial.CastRay(position.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight,
            direction, (float)maximumDistance, _sites.ActionTriggers.ActiveRayEntities(),
            _sites.Projection.CharacterEnvironment(State.PlayerControl.Motion));
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
        ReadOnlyMemory<SpatialEntityCollider> actionEntities = _sites.ActionTriggers.ActiveRayEntities();
        SpatialHit hit = _spatial.CastRay(
            rayOrigin,
            direction,
            (float)maximumDistance,
            actionEntities,
            _sites.Projection.CharacterEnvironment(State.PlayerControl.Motion));
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
            if (_sites.ActionTriggers.TryResolveAction(new EntityId(hit.Entity), out string? actionId))
                return ReportDungeonAction(graph.Trigger(actionId, @event));
            return false;
        }

        if (hit.Kind == SpatialHitKind.StaticMesh && _sites.Projection.Inputs.DungeonMap is DaggerfallDungeonMapContent map)
        {
            DaggerfallDungeonMapGeometry? placement = PlacementAt(map, _sites.LocalToProfile(hit.Point));
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
        if (DaggerfallDungeonDoorActions.Execute(action, _doors) is { } door) return door;
        if (ActivateDungeonMotion(action) is { } motion) return motion;
        ulong count = State.DungeonActions[_activeProfileKey].State[action.Id].ActivationCount;
        DaggerfallDungeonHazardActionResult? hazard = DaggerfallDungeonHazardActions.Execute(action,
            new(State.Actors.Player.Actor, State.Actors.Player.Actor, State.Progression.Level, count),
            _combatResolution, _random);
        if (hazard is not null)
        {
            if (hazard.HealthDamage is { } damage) AppendDamage(damage, DaggerfallDamageCause.Hazard, 0);
            if (hazard.MagickaLost > 0d)
                _facts.Append(new DungeonMagickaDrainedFact(DaggerfallActorIdentity.PlayerEntityId, action.Id,
                    hazard.MagickaLost, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL));
            return hazard.Execution;
        }
        return ExecuteDungeonTextAction(action);
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
        if (origin is Vector3 flat && _sites.ActionTriggers.TryGetTransform(action.Id, out Transform triggerTransform))
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

        internal DaggerfallDoorActivationOwner(
            DaggerfallDoorRuntime doors,
            Action<DaggerfallRdbDoorId> triggerDungeonActions,
            Func<DaggerfallRdbDoorId, DaggerfallActivationMode, DaggerfallActivationOutcome> activateForce,
            Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> activateMagic,
            Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> enterExteriorBuilding)
        {
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
        private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> _enterExteriorBuilding;

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
            if (_activateMagic(id) is { } magic)
            {
                if (magic.Applied) _triggerDungeonActions(id);
                if (CanEnter(id) && _enterExteriorBuilding(id) is { } entered) return entered;
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
                    if (CanEnter(id) && _enterExteriorBuilding(id) is { } entered) return entered;
                }
                return forced;
            }

            if (_doors.ExteriorBuildingOf(id) is not null)
            {
                DaggerfallDoorOperationResult opened = _doors.Open(id, DaggerfallDoorOperationSource.Player);
                if (opened == DaggerfallDoorOperationResult.Started) _triggerDungeonActions(id);
                if (CanEnter(id) && _enterExteriorBuilding(id) is { } entered) return entered;
                return new(false, Message(opened, door.Motion));
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
