using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private ActionDrivenTime? _playerTime;
    private bool _pendingWorldAction;
    private ActionDrivenTime PlayerTime => _playerTime ??= new(_engine.GameplayTime);

    internal bool ActionDrivenPlay => _controlSettings.ActionDrivenTime;
    internal bool PlayerTimeHeld => _playerTime?.Held == true;

    private void ChangePlayerTime(bool enabled)
    {
        _controlSettings.SetActionDrivenTime(enabled);
        PlayerTime.Select(enabled);
        _preferencesToSave = _controlSettings.Serialize();
    }

    private void ObservePlayerInput(ReadOnlySpan<ProductInputEvent> input, float lookSeconds, ProductUpdateState firstStep)
    {
        _input.Observe(State.PlayerControl, input, lookSeconds, firstStep.PlanarIntent);
        foreach (var action in firstStep.RequestedActions) _input.Request(action);
        _locomotion.Observe(input);
        _guard.Observe(input);
        _playerSwings.Observe(lookSeconds, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
    }

    // Opening a panel or preparing a spell must work without spending a world step. Physical
    // attack/use edges remain queued in the input owner until an Engine-admitted step consumes them.
    private void ApplyIdleInterfaceActions()
    {
        bool panel = false;
        foreach (var (action, name) in new[]
        {
            (DaggerfallInput.Inventory, DaggerfallPanel.Inventory),
            (DaggerfallInput.Character, DaggerfallPanel.Character),
            (DaggerfallInput.Map, DaggerfallPanel.Map),
            (DaggerfallInput.Menu, DaggerfallPanel.Menu),
        })
            if (_input.TakeAction(action)) { RequestPanel(name); panel = true; }
        foreach (string shortcut in DaggerfallInput.Shortcuts)
            if (_input.TakeAction(DaggerfallInput.Shortcut(shortcut))) { ApplyShortcut(shortcut); panel = true; }
        if (panel) _input.ClearPendingActions();
    }

    private void UpdatePlayerTime(ProductUpdateFacts facts)
    {
        bool wasHeld = PlayerTime.Held;
        PlayerTime.Select(ActionDrivenPlay);
        bool presentation = _mode == ProductMode.Title || _openingCinematics.IsActive || Cinematics?.ActiveSource is not null;
        bool playing = _mode == ProductMode.Playing && !_interactions.HoldsWorldOpen
            && !State.Actors.Player.IsDefeated && !LegalModalOpen && !Summoning.PendingOffer
            && State.Quests.PendingOffer is null && _pendingDispel is null
            && _pendingIdentify is null && _pendingCreateItem is null;
        CharacterMotion motion = State.PlayerControl.Motion;
        // Slowfall uses Engine vertical drive too, but it is still passive descent. Only
        // the ruleset's actual hover/support modes let an idle airborne player hold time.
        bool falling = !motion.Grounded && !State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId)
            && !State.Swimming.IsSwimming && !_climbing.IsAttached;
        // Ignore numerical controller residue smaller than a centimetre per second.
        bool movement = _input.ObservedPlanarIntent != Vector2.Zero || _locomotion.HasPendingJump
            || _locomotion.ObservedInput.JumpHeld
            || (_locomotion.ObservedInput.CrouchHeld && (_spatial.IsVerticalDriven || State.Swimming.IsSwimming))
            || _locomotion.HasPendingStanceChange
            // Ground adhesion retains downward controller velocity while standing. Only planar
            // momentum needs settling here; airborne gravity is covered by falling above.
            || falling || new Vector2(motion.ControlledVelocity.X, motion.ControlledVelocity.Z).LengthSquared() > .0001f
            || motion.ExternalVelocity.LengthSquared() > .0001f;
        bool action = _pendingWorldAction || _input.HasPendingActions || !_appearance.CanStartPlayerAttack || !_combat.PlayerWeaponReady
            || State.Actors.Player.Attack.ReadyAtStep > facts.SimulationStep
            || _combat.HasPlayerRangedFlight
            || Casting.PendingRangedFlights.Any(flight => flight.CasterId == DaggerfallActorIdentity.PlayerEntityId)
            || _doors.All.Any(door => door.Motion is DaggerfallDoorMotion.Opening or DaggerfallDoorMotion.Closing)
            || _sites.Projection.Motion.IsMoving;
        PlayerTime.Update(presentation || playing && (movement || action));
        if (wasHeld != PlayerTime.Held) PublishPresentation();
    }
}
