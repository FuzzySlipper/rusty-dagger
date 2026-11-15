using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;
using static WorldRpg.Rulesets.Daggerfall.DaggerfallDeveloperReplies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The playtest state writer (playtest.observe, playtest.action, playtest.look, navigation.targets): the product's
/// gameplay facts as the crew playtest tools read them, and the last combat event and use those tools compare across
/// receipts. The session's <c>React</c> hands it every fact (<see cref="ObserveFact"/>) and its contextual use list
/// marks each use (<see cref="ObserveUse"/>, <see cref="ObserveUseRefusal"/>); reading never creates an event.
/// </summary>
/// <param name="mode">The product mode the session runs under.</param>
/// <param name="weaponDrawn">Whether the player's weapon is drawn.</param>
/// <param name="generation">The latest admitted update's generation, or null before one.</param>
/// <param name="step">The latest admitted simulation step, or null before one.</param>
/// <param name="lootReach">The reach a target without its own is used at.</param>
/// <param name="floorY">The floor under the player, or null where it is not loaded or there is none.</param>
internal sealed class DaggerfallPlaytestState(DaggerfallPlayerControls controls, Func<ProductMode> mode, ActorsState actors,
    DaggerfallEffectLifecycle effects, DaggerfallCombatRules combat, Func<bool> weaponDrawn, DaggerfallPlayerAttack playerAttack,
    AttackExecution<IProductFact> attackExecution, TargetingService targeting, Func<ulong?> generation, Func<ulong?> step,
    DaggerfallEncumbrancePolicy encumbrance, PlayerControlState player, PlayerInputSystem input, FirstPersonCameraSystem camera,
    PresentationState presentation, DaggerfallActivation activation, DaggerfallActivationPanel activationPanel, DaggerfallLootPresentation loot,
    DaggerfallActorRoster roster, DaggerfallEnemySenses senses, DaggerfallEnemyBehaviorModule enemyBehavior, ActorReactions reactions,
    double lootReach, Func<double?> floorY)
{
    private ulong _combatEvents;

    /// <summary>The last combat event seen, or null before one.</summary>
    internal DaggerfallPlaytestCombatEvent? LastCombatEvent { get; private set; }

    /// <summary>The simulation step of the last contextual use, or null before one.</summary>
    internal ulong? LastUseStep { get; private set; }

    /// <summary>Why the last contextual use was refused, or null.</summary>
    internal string? LastUseRefusal { get; private set; }

    internal IReadOnlyList<string> Actions { get; } =
        ["forward", "back", "left", "right", "attack", "use", "jump", "toggle-weapon", "inventory", "character", "menu"];

    /// <summary>Notes a combat fact as the last combat event, with the step and generation it was seen at.</summary>
    internal void ObserveFact(IProductFact fact)
    {
        string? kind = fact switch
        {
            AttackRejectedFact rejected => $"AttackRejected:{rejected.Reason}",
            PlayerAttackStartedFact => "PlayerAttackStarted",
            AttackNegatedFact negated => $"AttackNegated:{negated.Negation}",
            AttackWhiffedFact => "AttackWhiffed",
            EnemyAttackTelegraphFact telegraph => FormattableString.Invariant($"EnemyAttackTelegraph:{telegraph.WindUpSeconds:0.###}"),
            AttackHitFact => "AttackHit",
            ActorDiedFact => "ActorDied",
            _ => null,
        };
        if (kind is null) return;
        LastCombatEvent = new(++_combatEvents, kind, step() ?? 0, generation() ?? 0);
    }

    /// <summary>A contextual use begins at <paramref name="atStep"/>; its refusal is not known yet.</summary>
    internal void ObserveUse(ulong atStep)
    {
        LastUseStep = atStep;
        LastUseRefusal = null;
    }

    /// <summary>Why the use just made was refused, or null when it was not.</summary>
    internal void ObserveUseRefusal(string? refusal) => LastUseRefusal = refusal;

    internal PlaytestAction InspectAction(string id)
    {
        string binding = id switch { "forward" => "move.forward", "back" => "move.backward",
            "left" => "move.left", "right" => "move.right", "use" => "interact", _ => id };
        if (!Actions.Contains(id)) return new(id, "", 0, false, false, "unknown-action");
        string key = controls.Settings.KeysFor(binding).FirstOrDefault() ?? "";
        bool moving = binding.StartsWith("move.", StringComparison.Ordinal);
        ProductMode now = mode();
        bool available = now == ProductMode.Playing && !actors.Player.IsDefeated;
        string? reason = available ? null : actors.Player.IsDefeated ? "player-dead" : $"mode-{now}; use ordinary UI";
        var restrictions = effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId);
        if (available && (moving && restrictions.Movement || id == "attack" && restrictions.PhysicalAttacks))
        { available = false; reason = "incapacitated"; }
        double duration = moving ? 200 : 100;
        string? equipment = null;
        if (id == "attack")
        {
            var timing = combat.InspectPlayerTiming();
            // A tap; the attack's own motion runs on from it in whatever time the window admits.
            duration = 50;
            equipment = timing.Equipment;
            if (available && !weaponDrawn()) { available = false; reason = "weapon-sheathed; toggle-weapon"; }
            else if (available && !playerAttack.Clock.Free)
            { available = false; reason = "attack-animation-active; advance then inspect"; }
            else if (available && (!combat.PlayerWeaponReady || !attackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId,
                generation() ?? 1, step() ?? 0)))
            { available = false; reason = "attack-cooldown; advance then inspect"; }
            else if (available && combat.InspectPlayerAttackRefusal() is { } refusal)
            { available = false; reason = refusal.ToString(); }
        }
        if (moving && available && !encumbrance.Read().CanMove) { available = false; reason = "encumbered"; }
        if (id == "jump" && available && !player.Motion.Grounded) { available = false; reason = "not-grounded"; }
        return new(id, key, duration, moving, available, reason, equipment);
    }

    internal DebugCommandResult InspectLook(double yawDegrees, double pitchDegrees)
    {
        if (!double.IsFinite(yawDegrees) || !double.IsFinite(pitchDegrees))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Look requires finite degrees.");
        input.InspectLook(player, yawDegrees, pitchDegrees);
        camera.Update(player);
        return ReadObservation();
    }

    internal DebugCommandResult ReadTargets() => Json(w => WriteTargets(w, compact: false));

    internal DebugCommandResult ReadObservation() => Json(w =>
    {
        LookReceipt look = input.ResolveCurrentLook(player);
        w.WriteStartObject();
        w.WriteString("mode", mode().ToString());
        w.WriteString("simulationStep", (step() ?? 0).ToString());
        w.WriteString("generation", (generation() ?? 0).ToString());
        w.WriteString("hostState", "Native world, player and time are shared across browser clients. Reconnect does not reset; new game uses ordinary menu.");
        w.WriteStartObject("axes"); w.WriteString("up", "Y"); w.WriteString("yaw", "positive right; zero faces -Z");
        w.WriteString("positionMeaning", "character center"); if (floorY() is double floor) w.WriteNumber("floorY", floor); else w.WriteNull("floorY");
        w.WriteString("floorMeaning", "Floor under the player's feet; null where it is not loaded or there is none: navigation.floor says which.");
        Vector(w, "forward", look.Forward); w.WriteEndObject();
        w.WriteStartObject("player");
        if (player.Position is WorldPoint position) Vector(w, "position", position.ToVector()); else w.WriteNull("position");
        w.WriteNumber("yawDegrees", player.YawRadians * 180 / Math.PI); w.WriteNumber("pitchDegrees", player.PitchRadians * 180 / Math.PI);
        Vector(w, "viewpoint", camera.Viewpoint.ToVector());
        w.WriteBoolean("dead", actors.Player.IsDefeated);
        foreach (string track in new[] { "health", "fatigue", "stamina", "magicka" })
            w.WriteNumber(track, actors.Player.Stats.GetTrack(TrackId.Parse(track)).Current);
        w.WriteStartObject("movement"); w.WriteBoolean("grounded", player.Motion.Grounded);
        w.WriteString("stance", player.Motion.Stance.ToString()); Vector(w, "velocity", player.Motion.ControlledVelocity);
        w.WriteBoolean("canMove", encumbrance.Read().CanMove); w.WriteEndObject();
        w.WriteBoolean("weaponDrawn", weaponDrawn());
        w.WriteString("equipment", combat.InspectPlayerTiming().Equipment);
        w.WriteString("attackUnavailableReason", InspectAction("attack").Reason);
        w.WriteNumber("attackReadyAtStep", actors.Player.Attack.ReadyAtStep);
        IDaggerfallPlayerAttacks attacks = playerAttack;
        w.WriteString("attackPlaying", playerAttack.Clock.Current);
        w.WriteNumber("attackTimeScale", attacks.TimeScale(DaggerfallAttackIds.Fast[0]));
        w.WriteNumber("attackChainLength", attacks.ChainLength);
        w.WriteBoolean("winded", attacks.Winded);
        w.WriteNumber("staminaMaximum", actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Maximum.Value);
        w.WriteStartObject("attributes");
        foreach (DaggerfallStatId stat in new[] { DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Speed, DaggerfallMechanicsIds.Luck })
            w.WriteNumber(stat.Value, actors.Player.Stats.GetStat(StatId.Parse(stat.Value)).ValueInt);
        w.WriteEndObject();
        w.WriteEndObject();
        w.WriteStartObject("controls");
        foreach (var pair in controls.Settings.All) { w.WriteStartArray(pair.Key); foreach (string key in pair.Value) w.WriteStringValue(key); w.WriteEndArray(); }
        w.WriteEndObject();
        w.WriteString("lastOutcome", presentation.LastOutcome);
        w.WriteString("outcomeSemantics", "HUD text is historical. Compare lastCombatEvent.sequence and lastUseStep across action receipts; inspection never creates events.");
        if (LastCombatEvent is { } combatEvent)
        {
            w.WriteStartObject("lastCombatEvent"); w.WriteNumber("sequence", combatEvent.Sequence);
            w.WriteString("kind", combatEvent.Kind); w.WriteString("observedStep", combatEvent.Step.ToString());
            w.WriteString("observedGeneration", combatEvent.Generation.ToString()); w.WriteEndObject();
        }
        else w.WriteNull("lastCombatEvent");
        if (LastUseStep is { } useStep) w.WriteString("lastUseStep", useStep.ToString()); else w.WriteNull("lastUseStep");
        w.WriteString("attackTiming", "Bounded observation window from current equipment, live speed, longest eligible strike and cooldown; inspect again for recovery.");
        w.WriteString("activationMode", activation.Mode.ToString());
        w.WriteString("interactionMessage", activationPanel.View.Message);
        w.WriteString("lastInteractionRefusal", LastUseRefusal);
        if (loot.Read() is { } open)
        {
            w.WriteStartObject("loot"); w.WriteString("title", open.Title); w.WriteBoolean("empty", open.Empty);
            w.WriteNumber("itemCount", open.Items.Length); w.WriteString("message", open.Message);
            w.WriteString("controls", "Use the visible loot panel's ordinary DOM buttons."); w.WriteEndObject();
        }
        else w.WriteNull("loot");
        w.WritePropertyName("targets"); WriteTargets(w, compact: true);
        w.WriteEndObject();
    });

    private void WriteTargets(Utf8JsonWriter w, bool compact)
    {
        WorldPoint? origin = player.Position;
        LookReceipt look = input.ResolveCurrentLook(player);
        TargetingEvidence? aimed = origin is WorldPoint p
            ? targeting.Inspect(p, look.Forward, combat.ReachOf(DaggerfallActorIdentity.PlayerEntityId)) : null;
        w.WriteStartObject();
        w.WriteString("route", "Targets are loaded positions, not routes: navigation.route X Y Z reads one.");
        w.WriteString("visibilityCoverage", "Current gameplay perception checks retained geometry; call-local moving doors/supports are not included. Visible is not a walking route or full collision clearance.");
        w.WriteString("angleOrigin", "character center used by gameplay targeting; visual angles also supplied from camera viewpoint");
        w.WriteString("attackVisibilityMeaning", "Player's current weapon-reach query; unavailable beyond its envelope is not an enemy perception result. enemyBehavior contains the last admitted enemy decision.");
        // What the last admitted melee request's perception query compared. The player's line only
        // says nothing was in reach; these counts are what explain why.
        if (targeting.LastEvidence is { } melee)
        {
            PerceptionReadoutResult receipt = melee.Receipt;
            w.WriteStartObject("lastMeleeQuery");
            if (melee.SelectedTargetId is long selected) w.WriteString("selectedTarget", $"actor:{selected}"); else w.WriteNull("selectedTarget");
            w.WriteNumber("observers", receipt.SelectedObservers); w.WriteNumber("targets", receipt.SelectedTargets);
            w.WriteNumber("compared", receipt.SelectionComparisons); w.WriteNumber("outOfRange", receipt.DistanceRejects);
            w.WriteNumber("outOfCone", receipt.FacingRejects); w.WriteNumber("visibilityCasts", receipt.VisibilityCasts);
            w.WriteNumber("occluded", receipt.OcclusionRejects);
            w.WriteEndObject();
        }
        else w.WriteNull("lastMeleeQuery");
        var pairs = aimed?.Receipt.Pairs.ToArray() ?? [];
        w.WriteStartArray("actors");
        foreach (var actor in actors.All.OrderBy(a => origin?.HorizontalDistanceTo(a.Position) ?? 0).Take(compact ? 24 : int.MaxValue))
        {
            w.WriteStartObject(); w.WriteString("id", $"actor:{actor.DurableId}");
            w.WriteString("label", roster.Definitions.TryGetValue(actor.DurableId, out var definition) ? definition.Id.Value : "loaded actor");
            if (definition is not null) w.WriteBoolean("hostile", senses.IsHostile(actor.DurableId, definition)); else w.WriteNull("hostile");
            w.WriteBoolean("alive", !actor.IsDefeated); w.WriteBoolean("attackEligible", targeting.IsValidTarget(actor));
            w.WriteNumber("health", actor.Stats.GetTrack(TrackId.Parse("health")).Current);
            WriteLocation(w, targeting.AimPoint(actor));
            Vector(w, "bodyPosition", actor.Position.ToVector());
            if (aimed is not null)
            {
                var pair = pairs.FirstOrDefault(value => value.Target == (ulong)actor.DurableId);
                w.WriteString("currentAttackVisibility", pairs.Any(value => value.Target == (ulong)actor.DurableId) ? pair.Kind.ToString() : "unavailable");
                w.WriteBoolean("selectedForAttack", aimed.SelectedTargetId == actor.DurableId);
            }
            if (enemyBehavior.LastEvidence.TryGetValue(actor.DurableId, out var behavior))
            {
                w.WriteStartObject("enemyBehavior"); w.WriteString("state", behavior.State.ToString()); w.WriteBoolean("reacting", reactions.Holds(behavior.ActorId));
                if (behavior.Visibility is { } visibility)
                {
                    var enemyPairs = visibility.Pairs.ToArray();
                    if (enemyPairs.Where(pair => pair.Observer == (ulong)actor.DurableId)
                        .Select(pair => (PerceptionPair?)pair).FirstOrDefault() is { } enemyPair)
                    {
                        w.WriteString("visibility", enemyPair.Kind.ToString()); w.WriteNumber("distance", enemyPair.Distance);
                        w.WriteNumber("facingCosine", enemyPair.FacingCosine);
                    }
                    else w.WriteString("visibility", "no-pair");
                }
                if (enemyBehavior.LastPerception.TryGetValue(actor.DurableId, out var perception))
                {
                    w.WriteBoolean("inSight", perception.InSight); w.WriteBoolean("inEarshot", perception.InEarshot);
                    w.WriteBoolean("detected", perception.Detected); w.WriteBoolean("pacified", perception.Pacified);
                    w.WriteBoolean("blockedByIllusion", perception.BlockedByIllusion);
                }
                if (behavior.Navigation is { } navigation)
                {
                    w.WriteString("navigation", navigation.Outcome.ToString());
                    Vector(w, "nextWaypoint", navigation.NextWaypoint);
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        w.WriteEndArray(); w.WriteStartArray("interactions");
        DaggerfallActivationModule? module = activation.Module;
        WorldInteractionReadout? interactions = module?.Inspect(actors.Player.Actor.Entity, player, look);
        if (module is not null)
        foreach (var target in module.InspectTargets().OrderBy(t => origin?.HorizontalDistanceTo(t.Position) ?? 0).Take(compact ? 24 : int.MaxValue))
        {
            w.WriteStartObject(); w.WriteString("id", $"{target.Identity.Kind}:{target.Identity.Value}");
            w.WriteString("kind", target.Kind.ToString()); w.WriteString("label", target.Label ?? target.Kind.ToString());
            WriteLocation(w, target.Position.ToVector());
            w.WriteNumber("reach", target.ReachDistance ?? lootReach);
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
        if (player.Position is not WorldPoint origin) return;
        Vector3 d = point - origin.ToVector();
        w.WriteNumber("distance", d.Length());
        double yaw = Math.Atan2(d.X, -d.Z) - player.YawRadians;
        w.WriteNumber("yawDeltaDegrees", Math.Atan2(Math.Sin(yaw), Math.Cos(yaw)) * 180 / Math.PI);
        w.WriteNumber("pitchDeltaDegrees", (Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) - player.PitchRadians) * 180 / Math.PI);
        Vector3 visual = point - camera.Viewpoint.ToVector();
        double visualYaw = Math.Atan2(visual.X, -visual.Z) - player.YawRadians;
        w.WriteNumber("visualYawDeltaDegrees", Math.Atan2(Math.Sin(visualYaw), Math.Cos(visualYaw)) * 180 / Math.PI);
        w.WriteNumber("visualPitchDeltaDegrees", (Math.Atan2(visual.Y, Math.Sqrt(visual.X * visual.X + visual.Z * visual.Z)) - player.PitchRadians) * 180 / Math.PI);
    }
}

/// <summary>A combat event the playtest tools compare across receipts: its number in order, its kind, and the step and generation it was seen at.</summary>
internal readonly record struct DaggerfallPlaytestCombatEvent(ulong Sequence, string Kind, ulong Step, ulong Generation);
