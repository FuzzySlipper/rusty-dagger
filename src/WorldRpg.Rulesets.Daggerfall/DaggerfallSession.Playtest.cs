using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Interaction;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private ulong _playtestCombatEventSequence;
    private string? _lastPlaytestCombatEvent;
    private ulong _lastPlaytestCombatStep, _lastPlaytestCombatGeneration;
    private ulong? _lastPlaytestUseStep;
    private string? _lastPlaytestUseRefusal;

    private void ObservePlaytestCombatFact(IProductFact fact)
    {
        string? kind = fact switch
        {
            AttackRejectedFact rejected => $"AttackRejected:{rejected.Reason}",
            PlayerAttackStartedFact => "PlayerAttackStarted",
            AttackMissedFact => "AttackMissed",
            AttackHitFact => "AttackHit",
            ActorDiedFact => "ActorDied",
            _ => null,
        };
        if (kind is null) return;
        _playtestCombatEventSequence++;
        _lastPlaytestCombatEvent = kind;
        _lastPlaytestCombatStep = _latestSimulationStep ?? 0;
        _lastPlaytestCombatGeneration = _latestUpdateGeneration ?? 0;
    }

    public IReadOnlyList<string> PlaytestActions { get; } =
        ["forward", "back", "left", "right", "attack", "use", "jump", "toggle-weapon", "inventory", "character", "menu"];

    public PlaytestAction InspectPlaytestAction(string id)
    {
        string binding = id switch { "forward" => "move.forward", "back" => "move.backward",
            "left" => "move.left", "right" => "move.right", "use" => "interact", _ => id };
        if (!PlaytestActions.Contains(id)) return new(id, "", 0, false, false, "unknown-action");
        string key = _controlSettings.KeysFor(binding).FirstOrDefault() ?? "";
        bool moving = binding.StartsWith("move.", StringComparison.Ordinal);
        bool available = _mode == ProductMode.Playing && !State.Actors.Player.IsDefeated;
        string? reason = available ? null : State.Actors.Player.IsDefeated ? "player-dead" : $"mode-{_mode}; use ordinary UI";
        double duration = moving ? 200 : 100;
        string? equipment = null;
        if (id == "attack")
        {
            var timing = _combat.InspectPlayerTiming();
            // Untargeted swings use authored playback; targeted impacts use live speed.
            bool targeted = State.PlayerControl.Position is WorldPoint attackOrigin
                && State.Kit.Targeting.Inspect(attackOrigin, _input.ResolveCurrentLook(State.PlayerControl).Forward,
                    _combat.ReachOf(DaggerfallActorIdentity.PlayerEntityId)).SelectedTargetId is not null;
            duration = Math.Clamp(Math.Ceiling(Math.Max(timing.CooldownSeconds,
                _appearance.InspectPlayerStrikeSeconds(State.Equipment.Read(), targeted ? timing.FrameSeconds : 0)) * 1000), 1, 2000);
            equipment = timing.Equipment;
            if (available && !_appearance.IsWeaponDrawn) { available = false; reason = "weapon-sheathed; toggle-weapon"; }
            else if (available && !_appearance.CanStartPlayerAttack)
            { available = false; reason = "attack-animation-active; advance then inspect"; }
            else if (available && !State.Kit.AttackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId,
                _latestUpdateGeneration ?? 1, _latestSimulationStep ?? 0))
            { available = false; reason = "attack-cooldown; advance then inspect"; }
            else if (available && _combat.InspectPlayerAttackRefusal() is { } refusal)
            { available = false; reason = refusal.ToString(); }
        }
        if (moving && available && !State.Encumbrance.Read().CanMove) { available = false; reason = "encumbered"; }
        if (id == "jump" && available && !State.PlayerControl.Motion.Grounded) { available = false; reason = "not-grounded"; }
        return new(id, key, duration, moving, available, reason, equipment);
    }

    public DebugCommandResult InspectPlaytestLook(double yawDegrees, double pitchDegrees)
    {
        if (!double.IsFinite(yawDegrees) || !double.IsFinite(pitchDegrees))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Look requires finite degrees.");
        _input.InspectLook(State.PlayerControl, yawDegrees, pitchDegrees);
        _playerSwings.Rebase(State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
        _camera.Update(State.PlayerControl);
        return ReadPlaytestObservation();
    }

    public DebugCommandResult ReadPlaytestTargets() => PlaytestJson(w => WriteTargets(w, compact: false));

    public DebugCommandResult ReadPlaytestObservation() => PlaytestJson(w =>
    {
        var player = State.PlayerControl;
        LookReceipt look = _input.ResolveCurrentLook(player);
        w.WriteStartObject();
        w.WriteString("mode", _mode.ToString());
        w.WriteString("simulationStep", (_latestSimulationStep ?? 0).ToString());
        w.WriteString("generation", (_latestUpdateGeneration ?? 0).ToString());
        w.WriteString("hostState", "Native world, player and time are shared across browser clients. Reconnect does not reset; new game uses ordinary menu.");
        w.WriteStartObject("axes"); w.WriteString("up", "Y"); w.WriteString("yaw", "positive right; zero faces -Z");
        w.WriteString("positionMeaning", "character center"); w.WriteNull("floorY");
        Vector(w, "forward", look.Forward); w.WriteEndObject();
        w.WriteStartObject("player");
        if (player.Position is WorldPoint position) Vector(w, "position", position.ToVector()); else w.WriteNull("position");
        w.WriteNumber("yawDegrees", player.YawRadians * 180 / Math.PI); w.WriteNumber("pitchDegrees", player.PitchRadians * 180 / Math.PI);
        Vector(w, "viewpoint", _camera.Viewpoint.ToVector());
        w.WriteBoolean("dead", State.Actors.Player.IsDefeated);
        foreach (string track in new[] { "health", "stamina", "magicka" })
            w.WriteNumber(track, State.Actors.Player.Stats.GetTrack(TrackId.Parse(track)).Current);
        w.WriteStartObject("movement"); w.WriteBoolean("grounded", player.Motion.Grounded);
        w.WriteString("stance", player.Motion.Stance.ToString()); Vector(w, "velocity", player.Motion.ControlledVelocity);
        w.WriteBoolean("canMove", State.Encumbrance.Read().CanMove); w.WriteEndObject();
        w.WriteBoolean("weaponDrawn", _appearance.IsWeaponDrawn);
        w.WriteString("equipment", _combat.InspectPlayerTiming().Equipment);
        w.WriteString("attackUnavailableReason", InspectPlaytestAction("attack").Reason);
        w.WriteNumber("attackReadyAtStep", State.Actors.Player.Attack.ReadyAtStep);
        w.WriteEndObject();
        w.WriteStartObject("controls");
        foreach (var pair in _controlSettings.All) { w.WriteStartArray(pair.Key); foreach (string key in pair.Value) w.WriteStringValue(key); w.WriteEndArray(); }
        w.WriteEndObject();
        w.WriteString("lastOutcome", Presentation.LastOutcome);
        w.WriteString("outcomeSemantics", "HUD text is historical. Compare lastCombatEvent.sequence and lastUseStep across action receipts; inspection never creates events.");
        if (_lastPlaytestCombatEvent is { } combatEvent)
        {
            w.WriteStartObject("lastCombatEvent"); w.WriteNumber("sequence", _playtestCombatEventSequence);
            w.WriteString("kind", combatEvent); w.WriteString("observedStep", _lastPlaytestCombatStep.ToString());
            w.WriteString("observedGeneration", _lastPlaytestCombatGeneration.ToString()); w.WriteEndObject();
        }
        else w.WriteNull("lastCombatEvent");
        if (_lastPlaytestUseStep is { } useStep) w.WriteString("lastUseStep", useStep.ToString()); else w.WriteNull("lastUseStep");
        w.WriteString("attackTiming", "Bounded observation window from current equipment, live speed, longest eligible strike and cooldown; inspect again for recovery.");
        w.WriteString("activationMode", ActivationMode.ToString());
        w.WriteString("interactionMessage", ActivationView.Message);
        w.WriteString("lastInteractionRefusal", _lastPlaytestUseRefusal);
        if (OpenLoot is { } loot)
        {
            w.WriteStartObject("loot"); w.WriteString("title", loot.Title); w.WriteBoolean("empty", loot.Empty);
            w.WriteNumber("itemCount", loot.Items.Length); w.WriteString("message", loot.Message);
            w.WriteString("controls", "Use the visible loot panel's ordinary DOM buttons."); w.WriteEndObject();
        }
        else w.WriteNull("loot");
        w.WritePropertyName("targets"); WriteTargets(w, compact: true);
        w.WriteEndObject();
    });

    private void WriteTargets(Utf8JsonWriter w, bool compact)
    {
        WorldPoint? origin = State.PlayerControl.Position;
        LookReceipt look = _input.ResolveCurrentLook(State.PlayerControl);
        TargetingEvidence? combat = origin is WorldPoint p
            ? State.Kit.Targeting.Inspect(p, look.Forward, _combat.ReachOf(DaggerfallActorIdentity.PlayerEntityId)) : null;
        w.WriteStartObject();
        w.WriteString("route", "unavailable; targets are loaded positions, not traversable routes");
        w.WriteString("visibilityCoverage", "Current gameplay perception checks retained geometry; call-local moving doors/supports are not included. Visible is not a walking route or full collision clearance.");
        w.WriteString("angleOrigin", "character center used by gameplay targeting; visual angles also supplied from camera viewpoint");
        var pairs = combat?.Receipt.Pairs.ToArray() ?? [];
        w.WriteStartArray("actors");
        foreach (var actor in State.Actors.All.OrderBy(a => origin?.HorizontalDistanceTo(a.Position) ?? 0).Take(compact ? 24 : int.MaxValue))
        {
            w.WriteStartObject(); w.WriteString("id", $"actor:{actor.DurableId}");
            w.WriteString("label", _definitionsByActor.TryGetValue(actor.DurableId, out var definition) ? definition.Id.Value : "loaded actor");
            if (definition is not null) w.WriteBoolean("hostile", IsHostileActor(actor.DurableId, definition)); else w.WriteNull("hostile");
            w.WriteBoolean("alive", !actor.IsDefeated); w.WriteBoolean("attackEligible", State.Kit.Targeting.IsValidTarget(actor));
            w.WriteNumber("health", actor.Stats.GetTrack(TrackId.Parse("health")).Current);
            WriteLocation(w, State.Kit.Targeting.AimPoint(actor));
            Vector(w, "bodyPosition", actor.Position.ToVector());
            if (combat is not null)
            {
                var pair = pairs.FirstOrDefault(value => value.Target == (ulong)actor.DurableId);
                w.WriteString("currentAttackVisibility", pairs.Any(value => value.Target == (ulong)actor.DurableId) ? pair.Kind.ToString() : "unavailable");
                w.WriteBoolean("selectedForAttack", combat.SelectedTargetId == actor.DurableId);
            }
            w.WriteEndObject();
        }
        w.WriteEndArray(); w.WriteStartArray("interactions");
        WorldInteractionReadout? interactions = _activation?.Inspect(State.Actors.Player.Actor.Entity, State.PlayerControl, look);
        if (_activation is not null)
        foreach (var target in _activation.InspectTargets().OrderBy(t => origin?.HorizontalDistanceTo(t.Position) ?? 0).Take(compact ? 24 : int.MaxValue))
        {
            w.WriteStartObject(); w.WriteString("id", $"{target.Identity.Kind}:{target.Identity.Value}");
            w.WriteString("kind", target.Kind.ToString()); w.WriteString("label", target.Label ?? target.Kind.ToString());
            WriteLocation(w, target.Position.ToVector());
            w.WriteNumber("reach", target.ReachDistance ?? _tuning.LootInteraction.MaximumDistance);
            if (interactions?.Focus.Candidates.Where(row => row.Candidate.Target.Id == target.QueryIdentity)
                .Select(row => (InteractionObservation?)row).FirstOrDefault() is { } row)
            {
                w.WriteString("focusReason", row.Reason.ToString());
                w.WriteBoolean("withinAcquisition", row.WithinAcquisition);
                w.WriteString("visibility", row.Candidate.Visibility.ToString());
            }
            w.WriteString("use", "use action performs fresh focus/reach checks and then owner policy (locks, mode, etc.); focus readiness does not promise success"); w.WriteEndObject();
        }
        w.WriteEndArray(); w.WriteEndObject();
    }

    private void WriteLocation(Utf8JsonWriter w, Vector3 point)
    {
        Vector(w, "position", point);
        if (State.PlayerControl.Position is not WorldPoint origin) return;
        Vector3 d = point - origin.ToVector();
        w.WriteNumber("distance", d.Length());
        double yaw = Math.Atan2(d.X, -d.Z) - State.PlayerControl.YawRadians;
        w.WriteNumber("yawDeltaDegrees", Math.Atan2(Math.Sin(yaw), Math.Cos(yaw)) * 180 / Math.PI);
        w.WriteNumber("pitchDeltaDegrees", (Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) - State.PlayerControl.PitchRadians) * 180 / Math.PI);
        Vector3 visual = point - _camera.Viewpoint.ToVector();
        double visualYaw = Math.Atan2(visual.X, -visual.Z) - State.PlayerControl.YawRadians;
        w.WriteNumber("visualYawDeltaDegrees", Math.Atan2(Math.Sin(visualYaw), Math.Cos(visualYaw)) * 180 / Math.PI);
        w.WriteNumber("visualPitchDeltaDegrees", (Math.Atan2(visual.Y, Math.Sqrt(visual.X * visual.X + visual.Z * visual.Z)) - State.PlayerControl.PitchRadians) * 180 / Math.PI);
    }

    private static void Vector(Utf8JsonWriter w, string name, Vector3 value)
    {
        w.WriteStartObject(name); w.WriteNumber("x", value.X); w.WriteNumber("y", value.Y); w.WriteNumber("z", value.Z); w.WriteEndObject();
    }

    private static DebugCommandResult PlaytestJson(Action<Utf8JsonWriter> write)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream)) write(writer);
        return DebugCommandResult.Success(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
