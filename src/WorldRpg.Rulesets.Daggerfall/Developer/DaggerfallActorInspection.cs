using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using static WorldRpg.Rulesets.Daggerfall.DaggerfallDeveloperReplies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The developer's actor commands (<c>actor.inspect</c>, <c>actor.trace</c>, <c>ai.freeze</c> and the per-actor
/// overrides). Inspection reads the live components and the behaviour owners' latest evidence and keeps nothing of its
/// own; the trace and the overrides live on <see cref="DaggerfallEnemyBehaviorModule"/>, which reads them at its inputs.
/// </summary>
/// <param name="equipment">An actor's equipment, read from its live facade.</param>
/// <param name="team">The team an actor fights for now, quest overrides included.</param>
/// <param name="step">The latest admitted simulation step, null before the first.</param>
internal sealed class DaggerfallActorInspection(ActorsState actors, PlayerControlState player, DaggerfallActorRoster roster,
    DaggerfallEnemySenses senses, DaggerfallEnemyBehaviorModule behavior, DaggerfallEnemyMagicModule magic,
    DaggerfallCasting casting, DaggerfallCombatRules combat, DaggerfallEffectLifecycle effects,
    Func<long, EquipmentRead?> equipment, Func<long, string?> team, Func<ulong?> step,
    DaggerfallDefinitions? definitions = null, DaggerfallNpcRegistry? npcs = null, DaggerfallNames? names = null,
    Func<ulong>? worldSocialSeed = null)
    : IDeveloperActorCommands
{
    /// <summary>How far <c>target</c> and <c>nearest</c> look for an actor.</summary>
    private const float PickRange = 40F;
    /// <summary>How far to either side of the player's facing an actor may stand and still be <c>target</c>: 15 degrees.</summary>
    private const float TargetCone = .966F;

    public DebugCommandResult Inspect(string actor)
    {
        if (Resolve(actor) is not { } found) return Refused(Unresolved(actor));
        return Json(w => WriteActor(w, found));
    }

    public DebugCommandResult Trace(string actor)
    {
        if (actor == "off")
        {
            behavior.Trace.Follow(null);
            return DebugCommandResult.Success("Decision trace off.");
        }
        if (Resolve(actor) is not { } found) return Refused(Unresolved(actor));
        behavior.Trace.Follow(found.DurableId);
        return Json(w => WriteTrace(w, found.DurableId));
    }

    public DebugCommandResult Freeze(string state)
    {
        if (OnOff(state) is not { } on) return Refused("ai.freeze takes on or off.");
        behavior.Overrides.Frozen = on;
        return DebugCommandResult.Success(on ? "Every enemy holds still for this run." : "Enemies act again.");
    }

    public DebugCommandResult Passive(string actor, string state) => Toggle(actor, state, "passive", behavior.Overrides.SetPassive);

    public DebugCommandResult Hostile(string actor, string state) => Toggle(actor, state, "hostile", behavior.Overrides.SetHostile);

    public DebugCommandResult ForceState(string actor, string state)
    {
        if (Resolve(actor) is not { } found) return Refused(Unresolved(actor));
        if (state == "off")
        {
            behavior.Overrides.Force(found.DurableId, null);
            return DebugCommandResult.Success($"{Name(found)} chooses its own maneuvers again.");
        }
        if (!Enum.TryParse(state, ignoreCase: true, out PursuitState forced) || !DaggerfallEnemyOverrides.ForcibleStates.Contains(forced))
            return Refused($"STATE is one of {string.Join(", ", DaggerfallEnemyOverrides.ForcibleStates)} or off.");
        behavior.Overrides.Force(found.DurableId, forced);
        return DebugCommandResult.Success($"{Name(found)} is held in {forced} for this run.");
    }

    public DebugCommandResult Target(string actor, string target)
    {
        if (Resolve(actor) is not { } found) return Refused(Unresolved(actor));
        if (target == "off")
        {
            behavior.Overrides.SetTarget(found.DurableId, null);
            return DebugCommandResult.Success($"{Name(found)} chooses its own target again.");
        }
        long chosen;
        if (target == "player") chosen = DaggerfallActorIdentity.PlayerEntityId;
        else if (Resolve(target) is { } other && other.DurableId != found.DurableId) chosen = other.DurableId;
        else return Refused($"TARGET is player, another live actor's id, target, nearest or off; '{target}' is none of them.");
        behavior.Overrides.SetTarget(found.DurableId, chosen);
        return DebugCommandResult.Success($"{Name(found)} pursues {chosen} for this run.");
    }

    private DebugCommandResult Toggle(string actor, string state, string name, Action<long, bool> set)
    {
        if (Resolve(actor) is not { } found) return Refused(Unresolved(actor));
        if (OnOff(state) is not { } on) return Refused($"actor.{name} takes an actor and on or off.");
        set(found.DurableId, on);
        return DebugCommandResult.Success($"{Name(found)} is {(on ? "" : "no longer ")}{name} for this run.");
    }

    private static bool? OnOff(string state) => state switch { "on" => true, "off" => false, _ => null };

    /// <summary>An actor by durable id, the live one most nearly ahead of the player (<c>target</c>), or the live one nearest the player (<c>nearest</c>).</summary>
    internal ActorState? Resolve(string actor)
    {
        IEnumerable<ActorState> others = actors.All.Where(value => value.DurableId != actors.Player.DurableId);
        if (long.TryParse(actor, out long id))
            return id != actors.Player.DurableId && actors.TryGet(id, out ActorState found) ? found : null;
        if (player.Position is not { } standing) return null;
        Vector3 feet = standing.ToVector();
        IEnumerable<ActorState> near = others.Where(value => !value.IsDefeated && Vector3.Distance(value.Position.ToVector(), feet) <= PickRange);
        if (actor == "nearest") return near.MinBy(value => Vector3.DistanceSquared(value.Position.ToVector(), feet));
        if (actor != "target") return null;
        Vector3 forward = ActorHeading.Forward(player.YawRadians);
        return near.Select(value => (Actor: value, Facing: Bearing(value.Position.ToVector() - feet, forward)))
            .Where(pair => pair.Facing >= TargetCone).OrderByDescending(pair => pair.Facing).Select(pair => pair.Actor).FirstOrDefault();
    }

    /// <summary>The cosine between the player's facing and the way to an actor, on the level.</summary>
    private static float Bearing(Vector3 toward, Vector3 forward)
    {
        toward.Y = forward.Y = 0;
        return toward.LengthSquared() < 1e-6F ? 1F : Vector3.Dot(Vector3.Normalize(toward), Vector3.Normalize(forward));
    }

    private static string Unresolved(string actor) =>
        $"No live actor answers to '{actor}': give an actor id, target (the one the player faces, within 15 degrees and {PickRange:0} m) or nearest.";

    private string Name(ActorState actor) =>
        roster.Definitions.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition) ? $"{definition.Id.Value} {actor.DurableId}" : $"actor {actor.DurableId}";

    private void WriteActor(Utf8JsonWriter w, ActorState actor)
    {
        long id = actor.DurableId;
        roster.Definitions.TryGetValue(id, out DaggerfallActorDefinition? definition);
        w.WriteStartObject();
        w.WriteNumber("durableId", id);
        w.WriteNumber("entityId", actor.Actor.Entity.Value);
        w.WriteString("kind", actor.Actor.TypeId.Value);
        w.WriteBoolean("defeated", actor.IsDefeated);
        if (definition is not null)
        {
            w.WriteStartObject("definition");
            w.WriteString("id", definition.Id.Value); w.WriteString("kind", definition.Kind);
            if (definition.TemplateId is { } templateId) w.WriteString("template", templateId); else w.WriteNull("template");
            if (definition.SpawnerId is { } spawnerId) w.WriteString("spawner", spawnerId); else w.WriteNull("spawner");
            if (definition.MobileId is { } mobile) w.WriteNumber("mobileId", mobile); else w.WriteNull("mobileId");
            if (definition.Level is { } level) w.WriteNumber("level", level); else w.WriteNull("level");
            w.WriteString("career", definition.Career); w.WriteString("action", definition.ActionId); w.WriteString("team", definition.Team);
            w.WriteEndObject();
        }
        else w.WriteNull("definition");
        if (actor.Actor.TryGet<ActorInstanceIdentity>(out ActorInstanceIdentity? identity))
        {
            w.WriteStartObject("identity");
            w.WriteString("spawner", identity.SpawnerId);
            w.WriteString("template", identity.TemplateId);
            w.WriteString("gender", ActorGenderFormat.Slug(identity.Gender));
            w.WriteString("race", identity.Race);
            w.WriteString("faction", identity.FactionId);
            w.WriteEndObject();
        }
        else w.WriteNull("identity");
        Vector(w, "position", actor.Position.ToVector());
        w.WriteNumber("headingDegrees", actor.HeadingYawRadians * 180 / Math.PI);
        if (player.Position is { } standing) w.WriteNumber("distanceToPlayer", Vector3.Distance(actor.Position.ToVector(), standing.ToVector()));

        w.WriteStartObject("stats");
        foreach (StatCapture stat in StatsComponentCapture.Capture(actor.Stats).Stats.OrderBy(stat => stat.Id, StringComparer.Ordinal))
            w.WriteNumber(stat.Id, actor.Stats.GetStat(StatId.Parse(stat.Id)).Value);
        w.WriteEndObject();
        w.WriteStartObject("tracks");
        foreach (var (trackId, track) in actor.Stats.Tracks.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            w.WriteStartObject(trackId.Value); w.WriteNumber("current", track.Current); w.WriteNumber("maximum", track.Maximum.Value); w.WriteEndObject();
        }
        w.WriteEndObject();

        w.WriteStartArray("effects");
        foreach (DaggerfallActiveEffect effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)id)))
        {
            w.WriteStartObject(); w.WriteString("key", effect.Definition.Key); w.WriteString("settings", effect.Settings);
            w.WriteString("element", effect.Element); w.WriteNumber("stacks", effect.Stacks); w.WriteEndObject();
        }
        w.WriteEndArray();
        ActorControlRestrictions controls = effects.ControlsFor(id);
        w.WriteStartObject("controls"); w.WriteBoolean("movementRestricted", controls.Movement); w.WriteBoolean("attacksRestricted", controls.PhysicalAttacks); w.WriteEndObject();

        w.WriteStartObject("equipment");
        if (equipment(id) is { } worn)
            foreach (WorldRpg.Kit.Inventory.EquipmentAssignment assignment in worn.Assignments)
                w.WriteString(assignment.Slot.Value, assignment.Item.Definition.Value);
        w.WriteEndObject();

        w.WriteStartObject("disposition");
        if (definition is not null) w.WriteBoolean("hostile", senses.IsHostile(id, definition));
        w.WriteString("team", team(id));
        w.WriteBoolean("pacified", behavior.IsPacified(id));
        if (actor.Actor.TryGet<DaggerfallEnemyPerceptionMemory>(out DaggerfallEnemyPerceptionMemory? memory))
        {
            w.WriteBoolean("forcedHostile", memory.ForcedHostile); w.WriteBoolean("languagePacified", memory.LanguagePacified);
            w.WriteBoolean("magicallyPacified", memory.MagicallyPacified); w.WriteBoolean("passive", memory.Passive);
            w.WriteBoolean("detected", memory.Detected); w.WriteBoolean("encounteredPlayer", memory.HasEncounteredPlayer);
            w.WriteBoolean("lairTruce", memory.LairTruce); w.WriteBoolean("factionInfighting", memory.FactionInfighting);
        }
        w.WriteEndObject();

        WriteBehavior(w, actor, memory);
        WriteAttack(w, actor);
        WriteSpells(w, actor, definition);
        WriteSocial(w, actor);
        if (actor.Actor.TryGet<WanderMemoryComponent>(out WanderMemoryComponent? wander))
        {
            w.WriteStartObject("wander");
            w.WriteString("state", wander.State.ToString()); w.WriteNumber("waypoint", wander.WaypointIndex);
            w.WriteNumber("idleSeconds", wander.IdleRemainingSeconds); w.WriteNumber("navigationFailures", wander.NavigationFailures);
            if (wander.Target is { } goal) Vector(w, "goal", goal.ToVector());
            w.WriteBoolean("staged", wander.Route is not null);
            w.WriteEndObject();
        }
        w.WriteStartArray("overrides");
        foreach (string active in behavior.Overrides.On(id)) w.WriteStringValue(active);
        w.WriteEndArray();
        w.WriteBoolean("traced", behavior.Trace.ActorId == id);
        w.WriteEndObject();
    }

    private void WriteBehavior(Utf8JsonWriter w, ActorState actor, DaggerfallEnemyPerceptionMemory? memory)
    {
        long id = actor.DurableId;
        w.WriteStartObject("behavior");
        if (actor.Actor.TryGet<PursuitMemoryComponent>(out PursuitMemoryComponent? pursuit))
        {
            w.WriteString("pursuit", pursuit.State.ToString()); w.WriteNumber("navigationFailures", pursuit.NavigationFailures);
            if (pursuit.LastKnownPosition is { } known) Vector(w, "lastKnownTarget", known.ToVector());
        }
        if (behavior.LastEvidence.TryGetValue(id, out EnemyBehaviorEvidence? evidence))
        {
            w.WriteString("decided", evidence.State.ToString());
            if (evidence.Target is { } target) w.WriteNumber("target", target.DurableId); else w.WriteNull("target");
            if (evidence.Navigation is { } navigation) w.WriteString("navigation", navigation.Outcome.ToString());
            if (evidence.Route is { } route) w.WriteString("route", route);
        }
        if (behavior.LastPerception.TryGetValue(id, out DaggerfallEnemyPerceptionDecision? perception))
        {
            w.WriteStartObject("perception");
            w.WriteBoolean("inSight", perception.InSight); w.WriteBoolean("inEarshot", perception.InEarshot);
            w.WriteBoolean("detected", perception.Detected); w.WriteBoolean("pacified", perception.Pacified);
            w.WriteBoolean("blockedByIllusion", perception.BlockedByIllusion);
            w.WriteEndObject();
        }
        if (behavior.LastSenses.TryGetValue(id, out DaggerfallEnemyPerceptionSource? source))
        {
            w.WriteNumber("sightRadius", source.SightRadius + source.SightModifier);
            w.WriteNumber("hearingRadius", source.HearingRadius + source.HearingModifier);
        }
        if (memory is not null)
        {
            DaggerfallEnemyMotorMemory motor = memory.Motor;
            w.WriteStartObject("motor");
            w.WriteBoolean("advance", motor.Advance); w.WriteNumber("advanceSeconds", motor.AdvanceTimer);
            w.WriteBoolean("strafe", motor.Strafe); w.WriteNumber("strafeSeconds", motor.StrafeTimer);
            w.WriteNumber("strafeDirection", motor.StrafeDirection); w.WriteBoolean("retreating", motor.Retreating);
            w.WriteNumber("retreatMultiplier", motor.RetreatMultiplier); w.WriteNumber("decisions", motor.Decision);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private void WriteAttack(Utf8JsonWriter w, ActorState actor)
    {
        w.WriteStartObject("attack");
        double? reach = combat.ReachOf(actor.DurableId);
        if (reach is { } value) w.WriteNumber("reach", value); else w.WriteNull("reach");
        if (player.Position is { } standing
            && combat.EnemyAttackAtDistance(actor.DurableId, DaggerfallActorIdentity.PlayerEntityId,
                Vector3.Distance(actor.Position.ToVector(), standing.ToVector())) is { } atPlayer)
        {
            w.WriteNumber("reachAtPlayer", atPlayer.Reach); w.WriteBoolean("rangedAtPlayer", atPlayer.Ranged);
        }
        if (actor.Actor.TryGet<WorldRpg.Kit.Combat.AttackState>(out WorldRpg.Kit.Combat.AttackState? attack))
        {
            ulong now = step() ?? 0;
            w.WriteBoolean("ready", attack.Pending is null && attack.ReadyAtStep <= now);
            w.WriteNumber("readyAtStep", attack.ReadyAtStep); w.WriteNumber("step", now);
            w.WriteNumber("cooldownSteps", attack.ReadyAtStep > now ? attack.ReadyAtStep - now : 0);
            w.WriteBoolean("swinging", attack.Pending is not null);
        }
        w.WriteEndObject();
    }

    private void WriteSpells(Utf8JsonWriter w, ActorState actor, DaggerfallActorDefinition? definition)
    {
        w.WriteStartObject("spells");
        w.WriteBoolean("casting", casting.HasPending(actor.DurableId));
        if (actor.Stats.TryGetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), out Track? magicka)) w.WriteNumber("magicka", magicka.Current);
        if (definition is not null && player.Position is { } standing)
            w.WriteBoolean("rangedReady", magic.HasRangedSpellReady(actor, new PursuitTarget(DaggerfallActorIdentity.PlayerEntityId, standing),
                Vector3.Distance(actor.Position.ToVector(), standing.ToVector())));
        if (magic.LastEvidence.TryGetValue(actor.DurableId, out DaggerfallEnemySpellEvidence? last))
        {
            w.WriteStartObject("last");
            w.WriteString("spell", last.SpellKey); w.WriteString("target", last.Target.ToString());
            w.WriteString("outcome", last.Outcome.ToString()); w.WriteNumber("step", last.SimulationStep);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private void WriteSocial(Utf8JsonWriter w, ActorState actor)
    {
        string? familyId = actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay) && !string.IsNullOrWhiteSpace(overlay.FamilyId)
            ? overlay.FamilyId
            : actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person) && !string.IsNullOrWhiteSpace(person.Snapshot.FamilyId)
                ? person.Snapshot.FamilyId
                : null;
        if (familyId is null && !actor.Actor.Has<ActorPersonComponent>())
            return;

        DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(familyId);
        ulong seed = worldSocialSeed?.Invoke() ?? 0UL;
        string? label = DaggerfallHouseholds.FormatLabel(
            familyId,
            definitions,
            names is not null ? names.Surname : null,
            names is not null ? (bank, id) => names.FirstName(bank, false, id) : null,
            seed);

        w.WriteStartObject("social");
        if (familyId is not null) w.WriteString("familyId", familyId); else w.WriteNull("familyId");
        if (label is not null) w.WriteString("householdLabel", label); else w.WriteNull("householdLabel");
        w.WriteString("scope", scope switch
        {
            DaggerfallHouseholdScope.Dynasty => "dynasty",
            DaggerfallHouseholdScope.GreatHouse => "great-house",
            DaggerfallHouseholdScope.Local => "local",
            _ => "none",
        });
        w.WriteStartArray("localKin");
        if (familyId is not null)
        {
            foreach (ActorState other in actors.All.OrderBy(a => a.DurableId))
            {
                if (other.DurableId == actor.DurableId || other.DurableId == actors.Player.DurableId)
                    continue;
                string? otherFamily = other.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? otherOv) && !string.IsNullOrWhiteSpace(otherOv.FamilyId)
                    ? otherOv.FamilyId
                    : other.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? otherPerson) ? otherPerson.Snapshot.FamilyId : null;
                if (!StringComparer.Ordinal.Equals(familyId, otherFamily))
                    continue;
                w.WriteStartObject();
                w.WriteNumber("durableId", other.DurableId);
                string? kinName = other.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? kinPerson)
                    ? kinPerson.Snapshot.DisplayName
                    : npcs is not null && npcs.TryGet(other.DurableId, out DaggerfallNpc? kinNpc)
                        ? kinNpc.DisplayName
                        : null;
                if (!string.IsNullOrWhiteSpace(kinName)) w.WriteString("name", kinName);
                else w.WriteNull("name");
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private void WriteTrace(Utf8JsonWriter w, long actorId)
    {
        w.WriteStartObject();
        w.WriteNumber("actor", actorId);
        w.WriteNumber("capacity", DaggerfallEnemyDecisionTrace.Capacity);
        w.WriteStartArray("decisions");
        foreach (DaggerfallDecisionRecord record in behavior.Trace.Recent)
        {
            w.WriteStartObject();
            w.WriteNumber("firstStep", record.FirstStep); w.WriteNumber("lastStep", record.LastStep); w.WriteNumber("repeats", record.Repeats);
            w.WriteString("state", record.State.ToString());
            w.WriteStartArray("gates");
            foreach (DaggerfallDecisionGate gate in record.Gates)
            {
                w.WriteStartObject();
                w.WriteString("policy", gate.Policy); w.WriteString("gate", gate.Gate);
                w.WriteString("outcome", gate.Outcome.ToString()); w.WriteString("reason", gate.Reason);
                if (gate.Values.Length > 0)
                {
                    w.WriteStartObject("values");
                    foreach (DaggerfallGateValue value in gate.Values) w.WriteNumber(value.Name, value.Value);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
