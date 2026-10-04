using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One admitted simulation step and the combat timing it drives.</summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// One simulation step: input, world time, and reactions. Publication is the caller's:
    /// the admitted update publishes once after all its steps, and direct callers publish
    /// with the step.
    /// </summary>
    private void SimulateStep(ProductUpdateState update, ulong generation, ulong simulationStep,
        long? swimmingMinuteBefore = null, long? swimmingMinuteAfter = null)
    {
        _latestUpdateGeneration = generation;
        _latestSimulationStep = simulationStep;
        if (State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? actionGraph))
            actionGraph.Advance(update.DeltaSeconds);
        DaggerfallMolagBalEffects.Reconcile(State.Effects, MolagBalEquipped);
        State.Kit.AttackExecution.ObserveTimeline(generation, simulationStep);
        var restrictions = State.Effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId);
        _input.Apply(State.PlayerControl, update, restrictions);
        // The Engine still receives an ordinary character step (grounding and gravity remain its
        // responsibility), but classic over-capacity removes planar intent before that proposal.
        bool alive = State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0d;
        if (!State.Encumbrance.Read().CanMove || !alive) update.PlanarIntent = Vector2.Zero;
        bool canMove = State.Encumbrance.Read().CanMove && alive && !restrictions.Movement;
        State.Transport.Reconcile(State.Inventory.Read(), new DaggerfallTransportAccessContext(
            IsIndoor: _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior,
            IsDungeon: _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon));
        DaggerfallLocomotionStep locomotion = _locomotion.BeginStep([.. update.Inputs], update.DeltaSeconds,
            State.Actors.Player.Stats, canMove, State.Transport);
        CharacterMotion motionBefore = State.PlayerControl.Motion;
        WorldPoint? positionBefore = State.PlayerControl.Position;
        bool activeLocationLoaded = _sites.ActiveLocationLoaded;
        _doors.Advance(update.DeltaSeconds);
        _sites.Projection.AdvanceMotion(update.DeltaSeconds);
        CharacterStepEnvironment doorEnvironment = _sites.CharacterEnvironment(State.PlayerControl.Motion);
        SpatialEntityCollider[] waterTriggers =
        [
            .. doorEnvironment.WaterVolumes.Span.ToArray().Select(SpatialMovementSystem.ProjectWaterCollider),
            _spatial.ProjectCharacterCollider(State.PlayerControl, State.Actors.Player.Actor.Entity.Value),
        ];
        SpatialTriggerReconcileResult triggerReconciliation = _spatial.ReconcileTriggers(simulationStep, waterTriggers);
        State.Swimming.ObserveTriggers(triggerReconciliation.Facts.Span,
            State.Actors.Player.Actor.Entity.Value, doorEnvironment.WaterVolumes.Span);
        CharacterWaterVolume? activeWater = State.Swimming.ActiveVolume(doorEnvironment.WaterVolumes.Span);
        bool waterWalking = State.Effects.GrantsWaterWalking(DaggerfallActorIdentity.PlayerEntityId);
        bool wallAhead = _spatial.TryProbeClimbWall(State.PlayerControl, CharacterWallProbeDirection.Forward, out SpatialHit forwardHit, doorEnvironment)
            && MathF.Abs(forwardHit.Normal.Y) <= .06f;
        bool wallAtFeet = _climbing.IsAttached
            && _spatial.TryProbeClimbWallAtFeet(State.PlayerControl, CharacterWallProbeDirection.Forward, out SpatialHit footHit, doorEnvironment)
            && MathF.Abs(footHit.Normal.Y) <= .06f;
        bool wallBehind = !motionBefore.Grounded && locomotion.ForwardIntent < 0f
            && _spatial.TryProbeClimbWall(State.PlayerControl, CharacterWallProbeDirection.Backward, out SpatialHit rearHit, doorEnvironment)
            && MathF.Abs(rearHit.Normal.Y) <= .06f;
        DaggerfallClimbStep climb = _climbing.BeginStep(locomotion, motionBefore, wallAhead, wallAtFeet, wallBehind,
            canMove && State.Transport.IsOnFoot && activeWater is null,
            State.Actors.Player.Stats, State.Character.Race.Id == "khajiit",
            State.Effects.EnhancesClimbing(DaggerfallActorIdentity.PlayerEntityId),
            update.DeltaSeconds, () => checked((int)_random.DrawKeyed(new KeyedRngRequest(
                CombatRandomKey.Seed, "daggerfall.climbing.v1", $"generation:{generation}:step:{simulationStep}", 1, 100)).Value));
        DaggerfallLevitationStep levitation = _levitation.Resolve(new DaggerfallLevitationContext(
            State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId),
            Swimming: State.Swimming.IsSwimming && !waterWalking,
            Climbing: climb.Climbing,
            CanMove: canMove,
            UpHeld: locomotion.UpHeld,
            DownHeld: locomotion.DownHeld,
            VerticalSpeed: _tuning.Locomotion.LevitationVerticalSpeed));
        locomotion = locomotion with
        {
            Controls = locomotion.Controls with
            {
                VerticalVelocity = climb.VerticalVelocity ?? levitation.VerticalVelocity,
                CrouchRequested = !levitation.IsLevitating && locomotion.Controls.CrouchRequested,
            },
            Climbing = climb.Climbing,
            Running = !levitation.IsLevitating && locomotion.Running,
            JumpRequested = !levitation.IsLevitating && locomotion.JumpRequested,
        };
        if (activeWater is { } water && !waterWalking && !climb.Climbing && canMove && State.Transport.IsOnFoot)
        {
            float verticalIntent = locomotion.UpHeld ? 1f : locomotion.DownHeld ? -1f : 0f;
            locomotion = locomotion with
            {
                Controls = locomotion.Controls with
                {
                    Movement = State.Swimming.Movement(water, verticalIntent),
                    VerticalVelocity = null,
                    JumpPressed = false,
                    JumpHeld = false,
                    CrouchRequested = false,
                },
                Running = false,
                JumpRequested = false,
            };
        }
        locomotion = locomotion with { Controls = restrictions.Restrict(locomotion.Controls) };
        bool releasedVerticalDrive = _verticalMovementDriven && !locomotion.Controls.VerticalVelocity.HasValue;
        CharacterStepReceipt? movement = _spatial.Step(State.PlayerControl, update, doorEnvironment, locomotion.Controls);
        if (movement is not null) _verticalMovementDriven = locomotion.Controls.VerticalVelocity.HasValue;
        if (movement is not null && _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior)
        {
            _sites.UpdateExteriorResidency();
            // A terrain cell can remain resident while the authored location closure is unloaded.
            // Finish the movement/origin boundary, then keep location-scoped AI, encounters and
            // interactions from observing a projection whose actors and geometry are intentionally absent.
            activeLocationLoaded = _sites.ActiveLocationLoaded;
        }
        if (movement is not null && actionGraph is not null)
            _ = ReportDungeonActions(_sites.ActionTriggers.Reconcile(actionGraph, State.PlayerControl,
                State.Actors.Player.Actor.Entity, simulationStep));
        if (movement is not null && State.DungeonDiscoveries.TryGetValue(_activeProfileKey, out DaggerfallDungeonDiscovery? discovery))
            _dungeonVisibility.Observe(discovery, State.PlayerControl, _doors, doorEnvironment, simulationStep,
                _tuning.Camera.EyeHeight, _sites.LocalCompensation);
        CharacterMotion landingBefore = releasedVerticalDrive && positionBefore is WorldPoint releasePosition
            ? motionBefore with { PeakY = releasePosition.Y, FallOriginY = releasePosition.Y }
            : motionBefore;
        DaggerfallSwimmingStep swimming = movement is not null
            ? State.Swimming.Complete(
                movement,
                State.Effects.GrantsWaterBreathing(DaggerfallActorIdentity.PlayerEntityId),
                State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
                update.DeltaSeconds,
                update.DeltaSeconds * _tuning.Time.GameSecondsPerRealSecond,
                swimmingMinuteAfter ?? MinuteIndex(_time.Calendar),
                use => State.SkillUses.Record(use), swimmingMinuteBefore)
            : DaggerfallSwimmingStep.None;
        DaggerfallLanding? landing = _locomotion.CompleteStep(locomotion, landingBefore, movement,
            update.DeltaSeconds * _tuning.Time.GameSecondsPerRealSecond, State.Actors.Player.Stats,
            use => State.SkillUses.Record(use), swimming.Swimming);
        _climbing.CompleteStep(climb, movement, use => State.SkillUses.Record(use));
        if (swimming.Drowning)
            AppendDamage(_vitality.ResolveDrowning(State.Actors.Player.Actor), DaggerfallDamageCause.Drowning, 0);
        if (_vitality.ResolveLanding(State.Actors.Player.Actor, landing, State.Effects.PreventsFallDamage(DaggerfallActorIdentity.PlayerEntityId)) is { } fall)
            AppendDamage(fall, DaggerfallDamageCause.Fall, 0);
        _sites.RebaseExteriorIfNeeded();
        _camera.Update(State.PlayerControl);
        if (!activeLocationLoaded) return;
        if (!alive || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d) return;
        _ = _encounters.MaterializePending(_activeProfileKey.LogicalId, (definition, pose, level) =>
            SpawnActor(definition, pose, level));
        _enemyBehavior.Update(State.PlayerControl, generation, simulationStep, update.DeltaSeconds, _facts);
        // Enemy attack-start facts must reach presentation before the post-enemy actions below.
        // A hit marker is consumed by the outer admitted update after this simulation step; if
        // that swing is already in flight, keep input and progression behind its same boundary.
        // Immediate (no damage-frame) swings are resolved here so a lethal result also stops the
        // remainder of this step. DeliverFacts remains a stable-batch boundary; any facts emitted
        // by these reactions wait for the existing caller boundary below.
        DeliverFacts();
        ApplyAttackImpacts();
        // Enemy reactions resolve inside this admitted step. A lethal reaction owns the rest of
        // the step: do not recover stamina, toggle equipment, attack, activate a target, or advance
        // quests after the player has been defeated. DeliverFacts still runs at the caller boundary,
        // so the mode transition and death presentation are published from the committed outcome.
        if (State.Actors.Player.IsDefeated
            || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d)
            return;
        if (_appearance.HasPendingEnemyHitTarget(DaggerfallActorIdentity.PlayerEntityId)) return;
        LookReceipt currentLook = _input.ResolveCurrentLook(State.PlayerControl);
        // The swing gesture is measured from the look turns this admitted update committed, so the
        // attack below reads the gesture the player actually drew in the moments before it.
        _playerSwings.Observe(update.DeltaSeconds, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
        _staminaRecovery.Update(State.Actors.Player.Stats, update.DeltaSeconds);
        if (update.IsRequested(DaggerfallInput.ToggleWeapon)) _appearance.ToggleWeaponDrawn();
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        // Interaction owns this slice once requested. Direct semantic input can carry both intents
        // in the same Engine delivery, and it must follow the same no-attack rule as a DOM loot action.
        bool contextualInteraction = update.IsRequested(DaggerfallInput.Interact);
        restrictions = State.Effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId);
        if (!restrictions.PhysicalAttacks && !contextualInteraction && update.IsRequested(DaggerfallInput.Attack) && _appearance.CanStartPlayerAttack)
        {
            State.Kit.Attacks.TryPlayerMelee(State.PlayerControl, currentLook, generation, simulationStep, update.DeltaSeconds, _facts);
            // WeaponManager sends Attack to an action on the environment after the ordinary hit
            // query. The safe Engine hit reports a static surface rather than a source id, so the
            // normalized placement resolver above is the product-side identity join.
            _ = TryTriggerDungeonActionRay(currentLook.Forward, _tuning.MeleeTargeting.MaximumDistance, DaggerfallDungeonActionEvent.Attack);
        }
        if (contextualInteraction)
            _ = TryActivateContextual(currentLook);
        // A panel button asks the DOM for a panel during ordinary play, which is where the keyboard's
        // own I, C and Escape are heard. While a modal or a death holds the world the DOM already has
        // a panel in front of the player, so a request there would fight the mode rather than serve it.
        // Two panel buttons in one admitted slice ask in a fixed order and the last one stands, which
        // is what the DOM's own key handling does with two keys in one frame: one panel can open, so
        // the earlier press must not swallow the later one.
        if (update.IsRequested(DaggerfallInput.Inventory)) RequestPanel(DaggerfallPanel.Inventory);
        if (update.IsRequested(DaggerfallInput.Character)) RequestPanel(DaggerfallPanel.Character);
        if (update.IsRequested(DaggerfallInput.Menu)) RequestPanel(DaggerfallPanel.Menu);
        // Tasks consume the state committed by this admitted step. Clock actions mutate only the
        // quest clock state; elapsed duration is still consumed by the calendar owner above.
        State.Quests.Advance(State.Variables, _time.Calendar);
        _dialogue?.SynchronizeWorldState();
        State.Quests.AdmitPlacements(_sites.Projection.Inputs, this);
        State.Quests.ReconcileFoeCommands();
        ReconcileNpcProjection();
        UpdateCivilianPopulation(simulationStep, update.DeltaSeconds);
        _dialogue?.RefreshEligibility();
    }

    private void ApplyAttackImpacts()
    {
        IReadOnlyList<AttackImpactNotice> impacts = _appearance.TakeAttackImpacts();
        if (impacts.Count == 0) return;
        if (_latestUpdateGeneration is not ulong generation) return;
        State.Kit.AttackExecution.ApplyImpacts(impacts, generation, _facts);
        DeliverFacts();
    }

    internal void ResolveExplicitMelee(ExplicitMeleeRequest request)
    {
        _latestUpdateGeneration = request.Generation;
        _latestSimulationStep = request.SimulationStep;
        State.Kit.AttackExecution.ObserveTimeline(request.Generation, request.SimulationStep);
        _combat.ResolveExplicit(request, _facts);
        DeliverFacts();
        PublishPresentation();
    }

    /// <summary>
    /// Whether admitted static geometry stands between a shot's release and its aim, asked of the
    /// Engine's own segment query at chest height. A shooter standing inside geometry would otherwise
    /// report every shot as blocked, so the segment starts clear of the muzzle.
    /// </summary>
    private bool ShotBlockedByCover(WorldPoint origin, WorldPoint aim)
    {
        Vector3 from = origin.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight;
        Vector3 to = aim.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight;
        Vector3 delta = to - from;
        float distance = delta.Length();
        if (!float.IsFinite(distance) || distance <= CoverCastStartMeters) return false;
        Vector3 direction = delta / distance;
        SpatialHit hit = _spatial.CastRay(from + direction * CoverCastStartMeters, direction, distance - CoverCastStartMeters);
        return hit.Present && hit.Kind == SpatialHitKind.StaticMesh;
    }

    /// <summary>How far along the shot the cover query starts, clear of the shooter's own position.</summary>
    private const float CoverCastStartMeters = .3f;
}
