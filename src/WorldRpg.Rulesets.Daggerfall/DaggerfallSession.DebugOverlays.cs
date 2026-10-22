using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Dagger's debug overlay sets (<see cref="IDeveloperInspectionSession"/>): what the interaction query sees, the people
/// and their services, doors and portals, creatures and their senses, the site's markers, and the player's body. Each
/// set is built from the owners' live facts at presentation, only while it is on, and drawn by the Engine's inspection
/// overlays; nothing here changes play. The transient <see cref="ProbeOverlaySet"/> draws the last spatial probe or
/// clearance query run from the console.
/// </summary>
internal sealed partial class DaggerfallSession : IDeveloperInspectionSession
{
    internal const string InteractionOverlaySet = "interaction";
    internal const string PeopleOverlaySet = "people";
    internal const string DoorsOverlaySet = "doors";
    internal const string ActorsOverlaySet = "actors";
    internal const string MarkersOverlaySet = "markers";
    internal const string PlayerOverlaySet = "player";
    internal const string ProbeOverlaySet = "probe";
    /// <summary>How far from the player the place-wide sets draw: a town has thousands of doors and markers.</summary>
    private const float OverlayRadius = 60F;

    private static readonly IReadOnlyList<string> DaggerOverlaySets =
        [InteractionOverlaySet, PeopleOverlaySet, DoorsOverlaySet, ActorsOverlaySet, MarkersOverlaySet, PlayerOverlaySet];
    private readonly HashSet<string> _debugOverlays = new(StringComparer.Ordinal);

    public IReadOnlyList<string> DebugOverlaySets => DaggerOverlaySets;

    public bool IsDebugOverlayOn(string name) => _debugOverlays.Contains(name);

    public bool SetDebugOverlay(string name, bool on)
    {
        if (!DaggerOverlaySets.Contains(name, StringComparer.Ordinal)) return false;
        if (on) _debugOverlays.Add(name);
        else if (_debugOverlays.Remove(name)) _engine.Graphics.PublishOverlaySet(new(name, []));
        return true;
    }

    public IReadOnlyList<InspectionTarget> InspectionTargets()
    {
        List<InspectionTarget> targets = [];
        if (State.PlayerControl.Position is not WorldPoint position) return targets;
        targets.Add(new("player", OverlayPlace.World, _camera.Viewpoint.ToVector())
            { Rotation = Quaternion.CreateFromYawPitchRoll(-State.PlayerControl.YawRadians, 0, 0), Radius = 1.5F, Framing = "front" });
        if (_dialogue?.CurrentNpc() is { } npc && State.Actors.TryGet(npc.DurableId, out ActorState? spoken))
            targets.Add(new("dialogue", OverlayPlace.World, spoken.Position.ToVector() + Vector3.UnitY) { Radius = 1.5F, Framing = "front" });
        if ((_activation?.InspectTargets() ?? []).Where(target => target.Kind is DaggerfallActivationTargetKind.Door or DaggerfallActivationTargetKind.Portal)
            .OrderBy(target => position.HorizontalDistanceTo(target.Position)).Select(target => (DaggerfallActivationTarget?)target).FirstOrDefault() is { } door)
            targets.Add(new("door", OverlayPlace.World, door.Position.ToVector()) { Radius = 2F, Framing = "front" });
        return targets;
    }

    /// <summary>Republishes every set that is on from the owners' facts at this presentation.</summary>
    private void PublishDebugOverlays()
    {
        if (_debugOverlays.Count == 0 || State.PlayerControl.Position is not WorldPoint position) return;
        foreach (string name in DaggerOverlaySets.Where(_debugOverlays.Contains))
        {
            List<OverlayItem> items = [];
            switch (name)
            {
                case InteractionOverlaySet: InteractionOverlay(items); break;
                case PeopleOverlaySet: PeopleOverlay(items, position); break;
                case DoorsOverlaySet: DoorsOverlay(items, position); break;
                case ActorsOverlaySet: ActorsOverlay(items, position); break;
                case MarkersOverlaySet: MarkersOverlay(items, position); break;
                case PlayerOverlaySet: PlayerOverlay(items, position); break;
            }
            _engine.Graphics.PublishOverlaySet(new(name, items.ToArray()));
        }
    }

    /// <summary>The activation query's ray, and each target's point, reach and focus reason in its colour.</summary>
    private void InteractionOverlay(List<OverlayItem> items)
    {
        if (_activation?.Inspect(State.Actors.Player.Actor.Entity, State.PlayerControl, _input.ResolveCurrentLook(State.PlayerControl)) is not { } readout) return;
        var query = readout.Scene.Query;
        items.Add(OverlayItem.Segment(OverlayPlace.World, query.Origin, query.Origin + query.Direction * query.MaximumDistance, Ink.Query, onTop: true));
        items.Add(OverlayItem.Marker(OverlayPlace.World, query.Origin, .1F, Ink.Query, onTop: true));
        Dictionary<ulong, InteractionObservation> rows = readout.Focus.Candidates.ToArray().ToDictionary(row => row.Candidate.Target.Id);
        foreach (DaggerfallActivationTarget target in _activation.InspectTargets())
        {
            Vector3 point = target.Position.ToVector();
            if (Vector3.Distance(point, query.Origin) > OverlayRadius) continue;
            bool observed = rows.TryGetValue(target.QueryIdentity, out InteractionObservation row);
            string reason = observed ? row.Reason.ToString() : "unobserved";
            Color color = reason == "Ready" ? Ink.Ready : reason is "Occluded" or "OutOfReach" or "Unavailable" ? Ink.Refused : Ink.Unfocused;
            items.Add(OverlayItem.Marker(OverlayPlace.World, point, .25F, color, onTop: true));
            if (observed && row.WithinRelease)
                items.Add(OverlayItem.WireSphere(OverlayPlace.World, point, (float)(target.ReachDistance ?? _tuning.LootInteraction.MaximumDistance), color));
            items.Add(OverlayItem.Label(OverlayPlace.World, point + Vector3.UnitY * .3F, $"{target.Label ?? target.Kind.ToString()} · {reason}", color, onTop: true));
        }
    }

    /// <summary>Each loaded person: where they stand, which way they face, and their role and services.</summary>
    private void PeopleOverlay(List<OverlayItem> items, WorldPoint player)
    {
        Dictionary<long, string> labels = (_activation?.InspectTargets() ?? []).Where(target => target.Kind == DaggerfallActivationTargetKind.Npc)
            .GroupBy(target => (long)target.Identity.Value).ToDictionary(group => group.Key, group => group.First().Label ?? "person");
        foreach (DaggerfallNpc npc in State.Npcs.All.Where(npc => npc.Presence == DaggerfallNpcPresence.Active))
        {
            if (!State.Actors.TryGet(npc.DurableId, out ActorState? actor) || player.HorizontalDistanceTo(actor.Position) > OverlayRadius) continue;
            Vector3 feet = actor.Position.ToVector();
            items.Add(OverlayItem.Segment(OverlayPlace.World, feet, feet + Vector3.UnitY * 1.8F, Ink.People));
            items.Add(OverlayItem.Segment(OverlayPlace.World, feet + Vector3.UnitY, feet + Vector3.UnitY + ActorHeading.Forward(actor.HeadingYawRadians) * .6F, Ink.People));
            string services = string.Join(", ", npc.Services.Where(service => service != "talk"));
            items.Add(OverlayItem.Label(OverlayPlace.World, feet + Vector3.UnitY * 2F,
                $"{labels.GetValueOrDefault(npc.DurableId, npc.Role)} · {npc.Role}{(services.Length > 0 ? $" · {services}" : "")}", Ink.People, onTop: true));
        }
    }

    /// <summary>Doors and portals: their point, outward normal, lock and, for a building's door, whether its entry rule opens it now.</summary>
    private void DoorsOverlay(List<OverlayItem> items, WorldPoint player)
    {
        Dictionary<EntityId, DaggerfallDoorView> doors = _doors.All.ToDictionary(door => door.Entity);
        foreach (DaggerfallActivationTarget target in (_activation?.InspectTargets() ?? [])
            .Where(target => target.Kind is DaggerfallActivationTargetKind.Door or DaggerfallActivationTargetKind.Portal))
        {
            if (player.HorizontalDistanceTo(target.Position) > OverlayRadius) continue;
            Vector3 point = target.Position.ToVector();
            string state = target.Kind == DaggerfallActivationTargetKind.Portal ? "portal" : "door";
            Color color = Ink.Ready;
            if (doors.TryGetValue(target.Entity, out DaggerfallDoorView door))
            {
                if (door.IsLocked) { state = $"locked {door.LockValue}"; color = Ink.Refused; }
                if (ExteriorBuildingDoor(door.Id) is { } building) state += building.Entry.Unlocked ? " · open now" : " · closed now";
            }
            items.Add(OverlayItem.Marker(OverlayPlace.World, point, .4F, color, onTop: true));
            if (target.SurfaceNormal is { } normal) items.Add(OverlayItem.Segment(OverlayPlace.World, point, point + normal, color, onTop: true));
            items.Add(OverlayItem.Label(OverlayPlace.World, point + Vector3.UnitY * .5F, $"{target.Label ?? state} · {state}", color, onTop: true));
        }
    }

    /// <summary>Creatures: their behaviour state, melee reach and hearing, and where navigation sends them next.</summary>
    private void ActorsOverlay(List<OverlayItem> items, WorldPoint player)
    {
        HashSet<long> people = [.. State.Npcs.All.Select(npc => npc.DurableId)];
        foreach (ActorState actor in State.Actors.All)
        {
            if (actor.DurableId == State.Actors.Player.DurableId || people.Contains(actor.DurableId) || actor.IsDefeated
                || player.HorizontalDistanceTo(actor.Position) > OverlayRadius) continue;
            Vector3 centre = State.Kit.Targeting.AimPoint(actor);
            string label = _roster.Definitions.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition) ? definition.Id.Value : "actor";
            bool hostile = definition is not null && IsHostileActor(actor.DurableId, definition);
            Color color = hostile ? Ink.Hostile : Ink.People;
            string state = _enemyBehavior.LastEvidence.TryGetValue(actor.DurableId, out EnemyBehaviorEvidence? behavior) ? behavior.State.ToString() : "no decision";
            items.Add(OverlayItem.WireSphere(OverlayPlace.World, centre, (float)(_combat.ReachOf(actor.DurableId) ?? 0), color));
            if (hostile) items.Add(OverlayItem.WireSphere(OverlayPlace.World, centre, (float)DaggerfallPerceptionQueryDefaults.HearingRadius, Ink.Senses));
            if (behavior?.Navigation is { NextWaypoint: Vector3 next })
                items.Add(OverlayItem.Segment(OverlayPlace.World, actor.Position.ToVector(), next, color, onTop: true));
            items.Add(OverlayItem.Label(OverlayPlace.World, centre + Vector3.UnitY, $"{label} · {state}", color, onTop: true));
        }
    }

    /// <summary>The active profile's markers: dungeon enemies and treasure, quest markers, arrival anchors and street population.</summary>
    private void MarkersOverlay(List<OverlayItem> items, WorldPoint player)
    {
        DaggerfallSiteProfile profile = _sites.Projection.Inputs;
        void Add(WorldPoint at, string label, Color color)
        {
            WorldPoint local = _sites.ProfileToLocal(at);
            if (player.HorizontalDistanceTo(local) > OverlayRadius) return;
            items.Add(OverlayItem.Marker(OverlayPlace.World, local.ToVector(), .5F, color, onTop: true));
            items.Add(OverlayItem.Label(OverlayPlace.World, local.ToVector() + Vector3.UnitY * .4F, label, color, onTop: true));
        }
        foreach (var enemy in profile.DungeonPopulation.Enemies) Add(enemy.Position, $"enemy {enemy.Id}", Ink.Hostile);
        foreach (var treasure in profile.DungeonPopulation.Treasures) Add(treasure.Position, $"treasure {treasure.Id}", Ink.People);
        foreach (var quest in profile.QuestMarkers) Add(quest.Position, $"quest {quest.Kind} {quest.Id}", Ink.Marker);
        foreach (var anchor in profile.Anchors.Values) Add(anchor.Position, $"anchor {anchor.Id}", Ink.Query);
        foreach (var civilian in profile.Population) Add(civilian.Position, "population", Ink.Unfocused);
    }

    /// <summary>The player's standing capsule, the height a step clears, and the floor below.</summary>
    private void PlayerOverlay(List<OverlayItem> items, WorldPoint position)
    {
        var shape = _spatial.CurrentController.Shape;
        Vector3 centre = position.ToVector();
        float height = State.PlayerControl.Motion.Stance == CharacterStance.Crouched ? shape.CrouchedHeight : shape.StandingHeight;
        float reach = height * .5F - shape.Radius;
        items.Add(OverlayItem.WireSphere(OverlayPlace.World, centre + Vector3.UnitY * reach, shape.Radius, Ink.Body));
        items.Add(OverlayItem.WireSphere(OverlayPlace.World, centre - Vector3.UnitY * reach, shape.Radius, Ink.Body));
        foreach (Vector3 side in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            items.Add(OverlayItem.Segment(OverlayPlace.World, centre + side * shape.Radius + Vector3.UnitY * reach, centre + side * shape.Radius - Vector3.UnitY * reach, Ink.Body));
        Vector3 feet = centre - Vector3.UnitY * (height * .5F);
        Vector3 step = feet + Vector3.UnitY * _spatial.CurrentController.Surface.MaximumStepHeight;
        items.Add(OverlayItem.WireBox(OverlayPlace.World, step, new(shape.Radius, 0, shape.Radius), Quaternion.Identity, Ink.Query));
        SpatialHit floor = _spatial.CastRay(centre, -Vector3.UnitY, height * 2F, LiveCollision());
        if (floor.Present)
        {
            items.Add(OverlayItem.Segment(OverlayPlace.World, centre, floor.Point, Ink.Ready, onTop: true));
            items.Add(OverlayItem.Marker(OverlayPlace.World, floor.Point, .3F, Ink.Ready, onTop: true));
        }
        else items.Add(OverlayItem.Segment(OverlayPlace.World, centre, centre - Vector3.UnitY * height * 2F, Ink.Refused, onTop: true));
        items.Add(OverlayItem.Label(OverlayPlace.World, centre + Vector3.UnitY * height * .6F,
            $"{State.PlayerControl.Motion.Stance} · {(State.PlayerControl.Motion.Grounded ? "grounded" : "airborne")}", Ink.Body, onTop: true));
    }

    /// <summary>
    /// Draws a spatial probe's or clearance query's answer as the transient <see cref="ProbeOverlaySet"/>: each ray to
    /// its hit (red) or its full range (green), and a clearance query's body at its target and its contacts.
    /// </summary>
    private void PublishProbeOverlay(string json)
    {
        List<OverlayItem> items = [];
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        static Vector3 V(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        if (root.TryGetProperty("samples", out JsonElement samples))
            foreach (JsonElement sample in samples.EnumerateArray())
            {
                Vector3 origin = V(sample.GetProperty("origin"));
                bool hit = sample.GetProperty("hit").GetBoolean();
                Vector3 end = hit ? V(sample.GetProperty("point")) : origin + V(sample.GetProperty("direction")) * sample.GetProperty("range").GetSingle();
                items.Add(OverlayItem.Segment(OverlayPlace.World, origin, end, hit ? Ink.Refused : Ink.Ready, onTop: true));
                if (hit) items.Add(OverlayItem.Marker(OverlayPlace.World, end, .08F, Ink.Refused, onTop: true));
            }
        if (root.TryGetProperty("targetFeet", out JsonElement targetFeet))
        {
            var shape = _spatial.CurrentController.Shape;
            Vector3 centre = V(root.GetProperty("center")), target = V(targetFeet) + Vector3.UnitY * (root.GetProperty("bodyHeight").GetSingle() * .5F);
            bool overlapping = root.GetProperty("targetStatus").GetString() == "overlapping";
            float reach = root.GetProperty("bodyHeight").GetSingle() * .5F - shape.Radius;
            items.Add(OverlayItem.Segment(OverlayPlace.World, centre, target, Ink.Query, onTop: true));
            items.Add(OverlayItem.WireSphere(OverlayPlace.World, target + Vector3.UnitY * reach, shape.Radius, overlapping ? Ink.Refused : Ink.Ready));
            items.Add(OverlayItem.WireSphere(OverlayPlace.World, target - Vector3.UnitY * reach, shape.Radius, overlapping ? Ink.Refused : Ink.Ready));
            foreach (string contact in new[] { "sweep", "targetOverlap", "supportBelowTarget" })
                if (root.GetProperty(contact) is { } facts && facts.GetProperty("present").GetBoolean())
                {
                    Vector3 point = V(facts.GetProperty("point"));
                    items.Add(OverlayItem.Marker(OverlayPlace.World, point, .2F, contact == "supportBelowTarget" ? Ink.Ready : Ink.Refused, onTop: true));
                    items.Add(OverlayItem.Label(OverlayPlace.World, point + Vector3.UnitY * .2F, contact, contact == "supportBelowTarget" ? Ink.Ready : Ink.Refused, onTop: true));
                }
        }
        _engine.Graphics.PublishOverlaySet(new(ProbeOverlaySet, items.ToArray()));
    }
}

/// <summary>The debug overlays' colours: green for what works, red for what refuses, grey for what is not in play.</summary>
file static class Ink
{
    internal static readonly Color Ready = new(.2F, 1F, .3F, 1F), Refused = new(1F, .3F, .25F, 1F), Unfocused = new(.65F, .65F, .65F, 1F),
        Query = new(.2F, .9F, 1F, 1F), People = new(1F, .85F, .3F, 1F), Hostile = new(1F, .35F, .2F, 1F), Senses = new(1F, .6F, .2F, .35F),
        Marker = new(.85F, .4F, 1F, 1F), Body = new(.3F, .7F, 1F, 1F);
}
