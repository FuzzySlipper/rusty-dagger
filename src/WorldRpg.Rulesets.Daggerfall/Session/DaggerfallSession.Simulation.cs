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

/// <summary>The calls of one simulation step that carry state between them, and the combat timing it drives.</summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>What one step's player movement carries from one call of the step's list to the next.</summary>
    private struct PlayerStep
    {
        internal ActorControlRestrictions Restrictions;
        internal bool Alive;
        internal bool CanMove;
        internal bool Slowfall;
        internal DaggerfallLocomotionStep Locomotion;
        internal CharacterMotion MotionBefore;
        internal WorldPoint? PositionBefore;
        internal CharacterStepEnvironment Environment;
        internal CharacterWaterVolume? ActiveWater;
        internal bool WaterWalking;
        internal DaggerfallClimbStep Climb;
        internal DaggerfallLevitationStep Levitation;
        internal bool SlowfallDescending;
        internal bool ReleasedVerticalDrive;
        internal CharacterStepReceipt? Movement;
        internal bool WasSwimming;
        internal DaggerfallSwimmingStep Swimming;
        internal DaggerfallLanding? Landing;
    }

    /// <summary>The step's observed input under the player's control restrictions; encumbrance and death stop the feet.</summary>
    private PlayerStep ApplyPlayerControls(ProductUpdateState update)
    {
        var restrictions = State.Effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId);
        _input.ApplyObserved(update, restrictions);
        // The Engine still receives an ordinary character step (grounding and gravity remain its
        // responsibility), but classic over-capacity removes planar intent before that proposal.
        bool alive = State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0d;
        if (!State.Encumbrance.Read().CanMove || !alive) update.PlanarIntent = Vector2.Zero;
        bool canMove = State.Encumbrance.Read().CanMove && alive && !restrictions.Movement;
        return new PlayerStep { Restrictions = restrictions, Alive = alive, CanMove = canMove };
    }

    /// <summary>The step's locomotion from its controls, and where the player stood before it.</summary>
    private void BeginLocomotion(ref PlayerStep player, ProductUpdateState update)
    {
        player.Slowfall = State.Effects.GrantsSlowfall(DaggerfallActorIdentity.PlayerEntityId);
        player.Locomotion = _locomotion.BeginStep([], update.DeltaSeconds,
            State.Actors.Player.Stats, player.CanMove, State.Transport,
            State.Effects.EnhancesJumping(DaggerfallActorIdentity.PlayerEntityId), player.Slowfall);
        player.MotionBefore = State.PlayerControl.Motion;
        player.PositionBefore = State.PlayerControl.Position;
    }

    /// <summary>The water the player is in, a wall to climb and levitation, each reshaping the step's locomotion.</summary>
    private void ShapeLocomotion(ref PlayerStep player, ProductUpdateState update, ulong generation, ulong simulationStep)
    {
        player.ActiveWater = player.PositionBefore is { } waterPoint
            ? _exterior.WaterAt(State.Swimming.ActiveVolume(player.Environment.WaterVolumes.Span), waterPoint.ToVector())
            : State.Swimming.ActiveVolume(player.Environment.WaterVolumes.Span);
        player.WaterWalking = State.Effects.GrantsWaterWalking(DaggerfallActorIdentity.PlayerEntityId);
        bool wallAhead = _spatial.TryProbeClimbWall(State.PlayerControl, CharacterWallProbeDirection.Forward, out SpatialHit forwardHit, player.Environment)
            && MathF.Abs(forwardHit.Normal.Y) <= .06f;
        bool wallAtFeet = _climbing.IsAttached
            && _spatial.TryProbeClimbWallAtFeet(State.PlayerControl, CharacterWallProbeDirection.Forward, out SpatialHit footHit, player.Environment)
            && MathF.Abs(footHit.Normal.Y) <= .06f;
        bool wallBehind = !player.MotionBefore.Grounded && player.Locomotion.ForwardIntent < 0f
            && _spatial.TryProbeClimbWall(State.PlayerControl, CharacterWallProbeDirection.Backward, out SpatialHit rearHit, player.Environment)
            && MathF.Abs(rearHit.Normal.Y) <= .06f;
        player.Climb = _climbing.BeginStep(player.Locomotion, player.MotionBefore, wallAhead, wallAtFeet, wallBehind,
            player.CanMove && State.Transport.IsOnFoot && player.ActiveWater is null,
            State.Actors.Player.Stats, State.Character.Race.Id == "khajiit",
            State.Effects.EnhancesClimbing(DaggerfallActorIdentity.PlayerEntityId),
            update.DeltaSeconds, () => checked((int)_random.DrawKeyed(new KeyedRngRequest(
                DaggerfallRandomKey.Seed, "daggerfall.climbing.v1", $"generation:{generation}:step:{simulationStep}", 1, 100)).Value));
        player.Levitation = _levitation.Resolve(new DaggerfallLevitationContext(
            State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId),
            Swimming: State.Swimming.IsSwimming && !player.WaterWalking,
            Climbing: player.Climb.Climbing,
            CanMove: player.CanMove,
            UpHeld: player.Locomotion.UpHeld,
            DownHeld: player.Locomotion.DownHeld,
            VerticalSpeed: _tuning.Locomotion.LevitationVerticalSpeed));
        player.Locomotion = player.Locomotion with
        {
            Controls = player.Locomotion.Controls with
            {
                VerticalVelocity = player.Climb.VerticalVelocity ?? player.Levitation.VerticalVelocity,
                CrouchRequested = !player.Levitation.IsLevitating && player.Locomotion.Controls.CrouchRequested,
            },
            Climbing = player.Climb.Climbing,
            Running = !player.Levitation.IsLevitating && player.Locomotion.Running,
            JumpRequested = !player.Levitation.IsLevitating && player.Locomotion.JumpRequested,
        };
        if (player.ActiveWater is { } water && !player.WaterWalking && !player.Climb.Climbing && player.CanMove && !State.Transport.OnShip)
        {
            float verticalIntent = player.Locomotion.UpHeld ? 1f : player.Locomotion.DownHeld ? -1f : 0f;
            player.Locomotion = player.Locomotion with
            {
                Controls = player.Locomotion.Controls with
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
    }

    /// <summary>The player's control restrictions on the step's locomotion, then slowfall's descent.</summary>
    private void ConstrainLocomotion(ref PlayerStep player)
    {
        player.Locomotion = player.Locomotion with { Controls = player.Restrictions.Restrict(player.Locomotion.Controls) };
        // Slowfall is a passive descent constraint, not voluntary movement. Engine still owns
        // the accepted pose and collision. Climb, levitation and water support take precedence.
        player.SlowfallDescending = player.Slowfall && !player.MotionBefore.Grounded && player.MotionBefore.ControlledVelocity.Y <= 0f
            && player.ActiveWater is null && !player.Climb.Climbing && !player.Levitation.IsLevitating;
        if (player.SlowfallDescending)
            player.Locomotion = player.Locomotion with { Controls = player.Locomotion.Controls with { VerticalVelocity = -_tuning.Locomotion.SlowfallDescentSpeed } };
        player.ReleasedVerticalDrive = _spatial.IsVerticalDriven && !player.Locomotion.Controls.VerticalVelocity.HasValue;
    }

    /// <summary>Swimming, the landing and climbing settle against the movement the Engine accepted.</summary>
    private void CompleteLocomotion(ref PlayerStep player, ProductUpdateState update, long? swimmingMinuteBefore, long? swimmingMinuteAfter)
    {
        CharacterMotion landingBefore = (player.ReleasedVerticalDrive || player.SlowfallDescending) && player.PositionBefore is WorldPoint releasePosition
            ? player.MotionBefore with { PeakY = releasePosition.Y, FallOriginY = releasePosition.Y }
            : player.MotionBefore;
        player.WasSwimming = State.Swimming.IsSwimming;
        player.Swimming = player.Movement is { } movement
            ? State.Swimming.Complete(
                movement,
                State.Effects.GrantsWaterBreathing(DaggerfallActorIdentity.PlayerEntityId),
                State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
                update.DeltaSeconds,
                update.DeltaSeconds * _tuning.Time.GameSecondsPerRealSecond,
                swimmingMinuteAfter ?? _time.Calendar.MinuteNumber,
                use => State.SkillUses.Record(use), swimmingMinuteBefore)
            : DaggerfallSwimmingStep.None;
        player.Landing = _locomotion.CompleteStep(player.Locomotion, landingBefore, player.Movement,
            update.DeltaSeconds * _tuning.Time.GameSecondsPerRealSecond, State.Actors.Player.Stats,
            use => State.SkillUses.Record(use), player.Swimming.Swimming);
        _climbing.CompleteStep(player.Climb, player.Movement, use => State.SkillUses.Record(use));
    }

    /// <summary>
    /// The player's own requests from the step, performed now or, behind an enemy hit on the player
    /// already in flight, deferred to the admitted update's impact boundary.
    /// </summary>
    private void PerformOrDeferPlayerActs(ProductUpdateState update)
    {
        DaggerfallPlayerActs acts = new(
            ToggleWeapon: update.IsRequested(DaggerfallInput.ToggleWeapon),
            Attack: update.IsRequested(DaggerfallInput.Attack),
            Kick: update.IsRequested(DaggerfallInput.Kick),
            Interact: update.IsRequested(DaggerfallInput.Interact),
            Inventory: update.IsRequested(DaggerfallInput.Inventory),
            Character: update.IsRequested(DaggerfallInput.Character),
            Menu: update.IsRequested(DaggerfallInput.Menu),
            Map: update.IsRequested(DaggerfallInput.Map),
            Shortcuts: DaggerfallInput.Shortcuts.Where(id => update.IsRequested(DaggerfallInput.Shortcut(id))).ToArray());
        // An enemy hit on the player that is already in flight lands at its damage frame, which the
        // outer admitted update reaches after these steps. The player's own acts wait for that same
        // boundary rather than being dropped, so a lethal frame reached there still resolves first and
        // a frame that is not reached yet lets the player act in this update, as the donor's
        // independent attack timing does. Once one step defers, the rest of the update defers behind it
        // so the acts keep their order.
        if (_deferredPlayerActs is not null || acts.Any && (_combat.HoldsImpactAt(DaggerfallActorIdentity.PlayerEntityId)
            || _sites.Appearances.Any(appearance => appearance.HasPendingEnemyHitTarget(DaggerfallActorIdentity.PlayerEntityId))))
        {
            _deferredPlayerActs = _deferredPlayerActs?.Then(acts) ?? acts;
            _appearance.UpdateRightHandEquipment(State.Equipment.Read(), State.WeaponDrawn);
        }
        else
        {
            ApplyPlayerActs(acts);
        }
    }

    /// <summary>
    /// The player's own requests from one simulation step: the weapon toggle, an attack or a kick,
    /// contextual interaction and the panel buttons.
    /// </summary>
    private sealed record DaggerfallPlayerActs(bool ToggleWeapon, bool Attack, bool Kick, bool Interact,
        bool Inventory, bool Character, bool Menu, bool Map, string[] Shortcuts)
    {
        internal bool Any => ToggleWeapon || Attack || Kick || Interact || Inventory || Character || Menu || Map || Shortcuts.Length != 0;

        /// <summary>Folds a later step's requests into acts still waiting.</summary>
        internal DaggerfallPlayerActs Then(DaggerfallPlayerActs later) => new(
            ToggleWeapon || later.ToggleWeapon, Attack || later.Attack, Kick || later.Kick, Interact || later.Interact,
            Inventory || later.Inventory, Character || later.Character, Menu || later.Menu, Map || later.Map, Shortcuts.Concat(later.Shortcuts).Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Acts waiting for this admitted update's enemy impacts; never carried past the update.</summary>
    private DaggerfallPlayerActs? _deferredPlayerActs;

    /// <summary>Readies or sheathes the weapon: its key on a step, or the radial's command, which a held world never steps.</summary>
    private void ToggleWeapon()
    {
        if (WeaponHandsFree) State.WeaponDrawn = !State.WeaponDrawn;
    }

    private void ApplyPlayerActs(DaggerfallPlayerActs acts)
    {
        foreach (string shortcut in acts.Shortcuts) ApplyShortcut(shortcut);
        // A menu/mode shortcut consumes this slice's attack and activation, just like a UI request.
        if (acts.Shortcuts.Length != 0) acts = acts with { Attack = false, Kick = false, Interact = false };
        if (acts.ToggleWeapon) ToggleWeapon();
        _appearance.UpdateRightHandEquipment(State.Equipment.Read(), State.WeaponDrawn);
        LookReceipt currentLook = _input.ResolveCurrentLook(State.PlayerControl);
        // Interaction owns this slice once requested. Direct semantic input can carry both intents
        // in the same Engine delivery, and it must follow the same no-attack rule as a DOM loot action.
        ActorControlRestrictions restrictions = State.Effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId);
        if (!restrictions.PhysicalAttacks && !acts.Interact && State.WeaponDrawn)
        {
            if (acts.Attack) PlayerAttack.Clock.Press();
            if (acts.Kick) PlayerAttack.Clock.PressKick();
        }
        if (acts.Interact)
            _ = TryActivateContextual(currentLook);
        // A panel button asks the DOM for a panel during ordinary play, which is where the keyboard's
        // own I, C and Escape are heard. While a modal or a death holds the world the DOM already has
        // a panel in front of the player, so a request there would fight the mode rather than serve it.
        // Two panel buttons in one admitted slice ask in a fixed order and the last one stands, which
        // is what the DOM's own key handling does with two keys in one frame: one panel can open, so
        // the earlier press must not swallow the later one.
        if (acts.Inventory) RequestPanel(DaggerfallPanel.Inventory);
        if (acts.Character) RequestPanel(DaggerfallPanel.Character);
        if (acts.Map) RequestPanel(DaggerfallPanel.Map);
        if (acts.Menu) RequestPanel(DaggerfallPanel.Menu);
    }

    /// <summary>
    /// Runs the acts a step deferred behind an enemy hit on the player, once this update's impacts have
    /// landed. A lethal impact or a legal modal leaves them unperformed, as it
    /// would have in the step itself.
    /// </summary>
    private void ApplyDeferredPlayerActs()
    {
        if (_deferredPlayerActs is not { } acts) return;
        _deferredPlayerActs = null;
        if (Law.ModalOpen || State.Actors.Player.IsDefeated
            || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d)
            return;
        ApplyPlayerActs(acts);
        DeliverFacts();
    }

    /// <summary>
    /// Reconciles the environment's water volumes and the player's collider with the Engine trigger
    /// service. The player's swimming reads the overlap facts; every other actor's water is the trigger
    /// rows this leaves current.
    /// </summary>
    /// <summary>
    /// The step's one trigger reconcile. Engine replaces its whole trigger geometry and overlap set on
    /// every reconcile, so water volumes, the active dungeon action triggers and the player go into one
    /// request: a request missing either family would drop its rows (actor navigation then finds no
    /// water) and report false exits and re-entries. Each owner reads its own facts from the result.
    /// </summary>
    // The water's trigger colliders, kept while the environment keeps its water.
    private (ReadOnlyMemory<CharacterWaterVolume> Water, SpatialEntityCollider[] Colliders) _waterColliders = (ReadOnlyMemory<CharacterWaterVolume>.Empty, []);

    private void ReconcileTriggers(CharacterStepEnvironment environment, ulong simulationStep, DaggerfallDungeonActionGraph? actionGraph)
    {
        EntityId player = State.Actors.Player.Actor.Entity;
        if (!_waterColliders.Water.Equals(environment.WaterVolumes))
            _waterColliders = (environment.WaterVolumes, [.. environment.WaterVolumes.ToArray().Select(SpatialMovementSystem.ProjectWaterCollider)]);
        SpatialEntityCollider[] triggers =
        [
            .. _waterColliders.Colliders,
            .. actionGraph is null ? [] : _sites.Interiors.ActionTriggers.ContactColliders(actionGraph, simulationStep),
            _spatial.ProjectCharacterCollider(State.PlayerControl, player.Value),
        ];
        SpatialTriggerReconcileResult reconciliation = _spatial.ReconcileTriggers(simulationStep, triggers);
        State.Swimming.ObserveTriggers(reconciliation.Facts.Span, player.Value, environment.WaterVolumes.Span);
        if (actionGraph is not null)
            _ = ReportDungeonActions(_sites.Interiors.ActionTriggers.Dispatch(actionGraph, reconciliation.Facts.Span, player));
    }

    private void ApplyAttackImpacts()
    {
        if (_latestUpdateGeneration is ulong held && _latestSimulationStep is ulong step)
        {
            _combat.LandDueImpacts(held, step, _facts);
            DeliverFacts();
        }
        AttackImpactNotice[] impacts = [.. PlayerAttack.TakeImpacts(), .. _sites.Appearances.SelectMany(appearance => appearance.TakeAttackImpacts())];
        if (impacts.Length == 0) return;
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
