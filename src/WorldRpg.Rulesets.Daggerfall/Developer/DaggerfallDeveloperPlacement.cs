using System.Globalization;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using static WorldRpg.Rulesets.Daggerfall.DaggerfallDeveloperReplies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Developer movement (world.teleport, move, forward, travel, enter, goto, where, stand, scout, find, spawn). Every
/// landing goes through the site lifecycle's one relocation owner, as quest teleports, recall and guild teleport do: the
/// player is stood on the floor, the exterior's residency follows, and a change of place is an ordinary site transition.
/// A journey lands as guild teleport does, with no time, cost or encounter; a building is entered as its door enters
/// it, without the door's greeting, hours or trespass. Positions in and out are scene-local, as
/// <c>navigation.targets</c> reports them.
/// </summary>
/// <param name="engine">The Engine, whose spatial service a refused spot's clearance is read from.</param>
/// <param name="time">The world's time; a scout looks for winter's ground in winter.</param>
/// <param name="mode">The product mode the session runs under.</param>
/// <param name="spawn">Registers one actor of a published definition at a pose (the session's roster spawn).</param>
/// <param name="profilePosition">A scene-local point in the active profile's own frame, as a relocation takes it.</param>
/// <param name="buildingLanding">Where entering through an exterior door lands inside its building.</param>
internal sealed class DaggerfallDeveloperPlacement(DaggerfallDeveloperHold hold, IEngineContext engine, SpatialMovementSystem spatial,
    DaggerfallSiteCrossing sites, DaggerfallSiteContext site, PlayerControlState player, ActorsState actors, DaggerfallNpcRegistry npcs,
    DaggerfallDefinitions definitions, DaggerfallFastTravel fastTravel, DaggerfallActivation activation, DaggerfallBuildingEntrance entrance,
    DaggerfallHorizon horizon, DaggerfallWorldTime time, Func<ProductMode> mode, PresentationState presentation,
    Func<string, ActorPose, long> spawn, Func<WorldPoint, Vector3> profilePosition,
    Func<DaggerfallSiteProfile, DaggerfallRdbDoorId, DaggerfallSiteAnchor> buildingLanding) : IDeveloperPlacementCommands
{
    /// <summary>How far from a target <see cref="GoTo"/> stands: inside every interaction reach, clear of a counter's far side.</summary>
    private const float GoToStandoff = 1.5F;
    /// <summary>How many standing spots around a target <see cref="GoTo"/> tries.</summary>
    private const int GoToBearings = 16;
    /// <summary>The farthest a scout stands the player from where they are, well inside the Engine's local envelope.</summary>
    private const double ScoutStandMetres = 4000;

    public DebugCommandResult Teleport(double x, double y, double z, bool force)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "A teleport target must be finite.");
        Vector3 feet = new((float)x, (float)y, (float)z);
        // An interior is a box in empty space and a dungeon has gaps: a spot with nothing to stand on is refused too.
        if (!force && FloorBelow(feet + Vector3.UnitY) is null)
            return Refused($"There is no floor within three metres below ({Format(feet)}); world.teleport-force places the player there anyway.");
        if (!force && BodyOverlap(feet) is { Present: true })
            return Refused($"The body would overlap collision at ({Format(feet)}); world.teleport-force places it anyway. "
                + SpatialClearanceSnapshot.Capture(engine.Spatial, spatial.Session, PlayerCentre(), feet + Vector3.UnitY * FloorClearance,
                    BodyHeight, spatial.CurrentController, SpatialMovementSystem.SpatialColliders(LiveCollision())));
        return Land(feet, player.YawRadians, player.PitchRadians, "developer-teleport");
    }

    public DebugCommandResult Move(double dx, double dy, double dz)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        Vector3 feet = PlayerFloor();
        return Teleport(feet.X + dx, feet.Y + dy, feet.Z + dz, force: false);
    }

    public DebugCommandResult Forward(double metres)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (!double.IsFinite(metres)) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "A distance must be finite.");
        Vector3 offset = ActorHeading.Forward(player.YawRadians) * (float)metres;
        return Move(offset.X, 0, offset.Z);
    }

    public DebugCommandResult Spawn(string actor, double metres)
    {
        if (!double.IsFinite(metres) || metres <= 0) return Refused("A distance is finite and above 0.");
        if (!definitions.Actors.TryGetValue(new DaggerfallActorId(actor), out DaggerfallActorDefinition? definition) || definition.Kind == DaggerfallActorKinds.Player)
            return Refused($"'{actor}' is no actor definition that can be spawned.");
        if (player.Position is not WorldPoint position) return Refused("The player has no position.");
        float yaw = player.YawRadians;
        Vector3 at = position.ToVector() + (ActorHeading.Forward(yaw) * (float)metres);
        long spawned = spawn(actor, new ActorPose(WorldPoint.From(at), yaw + MathF.PI));
        return DebugCommandResult.Success($"{actor} ({spawned}) stands {metres:0.#} m ahead.");
    }

    public DebugCommandResult Travel(string place)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (ResolvePlace(place, out DaggerfallSiteProfile? exterior) is { } unresolved) return Refused(unresolved);
        if (!ArriveAt(exterior!)) return Refused($"{exterior!.ProfileKey.LogicalId} could not be admitted.");
        return Where();
    }

    public DebugCommandResult Enter(string place, string building)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (ResolvePlace(place, out DaggerfallSiteProfile? exterior) is { } unresolved) return Refused(unresolved);
        DaggerfallSiteRecord record = site.Require(exterior!.Site!.Value);
        if (ResolveBuilding(record, building, out DaggerfallSiteBuildingId id, out DaggerfallSiteProfile? inside) is { } missing) return Refused(missing);
        if (sites.ActiveProfile != exterior.ProfileKey && !ArriveAt(exterior)) return Refused($"{exterior.ProfileKey.LogicalId} could not be admitted.");
        DaggerfallDoorRuntime doors = sites.Projection.Doors;
        DaggerfallDoorView? door = doors.All.Where(value => doors.ExteriorBuildingOf(value.Id) == id)
            .Select(value => (DaggerfallDoorView?)value).FirstOrDefault();
        if (door is not { } entrance) return Refused($"{BuildingLabel(record, id)} has no door in the loaded exterior.");
        // Stand at the door first, so leaving the building returns the player to its doorstep as walking in would.
        if (activation.Module?.InspectTargets().Where(target => target.Entity == entrance.Entity)
            .Select(target => (DaggerfallActivationTarget?)target).FirstOrDefault() is { } doorTarget)
            _ = StandWithinReach(doorTarget, out _);
        if (!sites.TryEnterBuilding(inside!.ProfileKey, buildingLanding(inside, entrance.Id)))
            return Refused($"{BuildingLabel(record, id)} could not be entered.");
        return Where();
    }

    public DebugCommandResult GoTo(string target)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (activation.Module is not { } module) return Refused("No interaction targets are loaded.");
        WorldPoint origin = player.Position!.Value;
        DaggerfallActivationTarget[] targets = [.. module.InspectTargets()];
        DaggerfallActivationTarget[] matches = [.. targets.Where(value => TargetId(value).Equals(target, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length == 0) matches = [.. targets.Where(value => string.Equals(value.Label, target, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length == 0) matches = [.. targets.Where(value => value.Label?.Contains(target, StringComparison.OrdinalIgnoreCase) == true)];
        if (matches.Length == 0) return Refused($"No loaded person, door or container is named '{target}'. navigation.targets lists them.");
        string[] labels = [.. matches.Select(value => value.Label ?? value.Kind.ToString()).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (labels.Length > 1)
            return Refused($"'{target}' names several targets: {string.Join("; ", matches.OrderBy(value => origin.HorizontalDistanceTo(value.Position)).Take(12).Select(value => $"{value.Label} ({TargetId(value)})"))}.");
        // Several targets with one name (a building's doors) are one place: the nearest is meant.
        DaggerfallActivationTarget chosen = matches.MinBy(value => origin.HorizontalDistanceTo(value.Position));
        if (!StandWithinReach(chosen, out string? why)) return Refused(why!);
        return Where();
    }

    public DebugCommandResult Where()
    {
        if (player.Position is not WorldPoint position) return Refused("The player has no position.");
        return Json(w =>
        {
            w.WriteStartObject();
            DaggerfallSiteRecord? record = site.ActiveSite;
            DaggerfallWorldProfileKey active = sites.ActiveProfile;
            w.WriteString("place", record?.Name); w.WriteString("site", record?.Id.ToString());
            w.WriteString("profile", active.LogicalId); w.WriteString("kind", active.Kind.ToString());
            // The title stands a world behind it too, so whether a game is running is said outright.
            w.WriteBoolean("playing", mode() != ProductMode.Title);
            if (record is not null && sites.Projection.Inputs.InteriorBuilding is { } interior)
                w.WriteString("building", BuildingLabel(record, new(interior.BlockX, interior.BlockY, interior.Building.Index)));
            Vector(w, "feet", PlayerFloor()); Vector(w, "centre", position.ToVector());
            w.WriteNumber("yawDegrees", player.YawRadians * 180 / MathF.PI);
            w.WriteNumber("pitchDegrees", player.PitchRadians * 180 / MathF.PI);
            w.WriteString("lastOutcome", presentation.LastOutcome);
            w.WriteStartArray("nearest");
            foreach (DaggerfallActivationTarget target in (activation.Module?.InspectTargets() ?? []).OrderBy(value => position.HorizontalDistanceTo(value.Position)).Take(8))
            {
                w.WriteStartObject(); w.WriteString("id", TargetId(target)); w.WriteString("label", target.Label ?? target.Kind.ToString());
                w.WriteNumber("distance", Math.Round(position.HorizontalDistanceTo(target.Position), 2)); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Stand(double east, double south)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (sites.Exterior.ActiveExteriorOrigin is not { } origin || player.Position is not { } position)
            return Refused("The player is not outdoors.");
        Vector3 feet = PlayerFloor();
        double rise = horizon.GroundRise(origin, position.ToVector(), east, south);
        return Teleport(feet.X + east, feet.Y + rise + .05, feet.Z + south, force: true);
    }

    public DebugCommandResult Scout(string kind, double kilometres)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (!DaggerfallHorizonGround.Kinds.Contains(kind)) return Refused($"Ground is one of {string.Join(", ", DaggerfallHorizonGround.Kinds)}.");
        if (!double.IsFinite(kilometres) || kilometres is <= 0 or > 100) return Refused("Search out to more than 0 and at most 100 kilometres.");
        if (sites.Exterior.ActiveExteriorOrigin is not { } origin || player.Position is not { } position) return Refused("The player is not outdoors.");
        bool winter = time.Calendar.Season == DaggerfallSeason.Winter;
        if (horizon.FindGround(origin, position.ToVector(), kind, kilometres * 1000, winter) is not { } found)
            return Refused($"No open ground within {kilometres} km is mostly {kind}{(winter ? " in winter" : "")}; travel nearer first.");
        (double fromX, double fromZ) = DaggerfallHorizon.MapPoint(origin, position.ToVector());
        string by = "";
        // A stand moves the player within the Engine's local envelope; farther, the player lands at the place nearest the spot first.
        if (Math.Max(Math.Abs(found.X - fromX), Math.Abs(found.Z - fromZ)) > ScoutStandMetres)
        {
            DaggerfallSiteProfiles profiles = sites.RequireProfiles();
            DaggerfallSiteRecord? nearest = site.Records
                .Where(record => record.Exterior is not null && profiles.TryGet(DaggerfallWorldProfileIds.Exterior(record.Id), out _))
                .MinBy(record => Math.Pow(((record.MapPixelX + .5) * DaggerfallExteriorCellResidency.CellSize) - found.X, 2)
                    + Math.Pow(((record.MapPixelY + .5) * DaggerfallExteriorCellResidency.CellSize) - found.Z, 2));
            if (nearest is null || !profiles.TryGet(DaggerfallWorldProfileIds.Exterior(nearest.Id), out DaggerfallSiteProfile exterior) || !ArriveAt(exterior))
                return Refused($"Ground that is mostly {kind} lies {Math.Max(Math.Abs(found.X - fromX), Math.Abs(found.Z - fromZ)) / 1000:F0} km away, and no place near it could be landed at.");
            if (sites.Exterior.ActiveExteriorOrigin is not { } landed || player.Position is not { } there) return Refused($"{nearest.Name} has no ground to stand on.");
            (fromX, fromZ) = DaggerfallHorizon.MapPoint(landed, there.ToVector());
            by = $" by way of {nearest.Name}";
        }

        double east = found.X - fromX, south = found.Z - fromZ;
        DebugCommandResult stood = Stand(east, south);
        return stood.Status == DebugCommandStatus.Success
            ? DebugCommandResult.Success(string.Create(CultureInfo.InvariantCulture, $"Standing in {kind}, {east:F0} m east and {south:F0} m south of where the player was{by}."))
            : stood;
    }

    public DebugCommandResult Find(string service)
    {
        if (site.ActiveSite is not { } record) return Refused("The player is at no place with people.");
        Dictionary<long, string> labels = (activation.Module?.InspectTargets() ?? [])
            .Where(target => target.Kind == DaggerfallActivationTargetKind.Npc)
            .GroupBy(target => (long)target.Identity.Value).ToDictionary(group => group.Key, group => group.First().Label ?? "person");
        return Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("place", record.Name); w.WriteString("service", service);
            // The people loaded here now, by the services the ruleset admitted for them.
            w.WriteStartArray("loaded");
            foreach (DaggerfallNpc npc in npcs.All.Where(npc => npc.Presence == DaggerfallNpcPresence.Active
                && npc.Services.Contains(service, StringComparer.OrdinalIgnoreCase) && actors.TryGet(npc.DurableId, out _)))
            {
                w.WriteStartObject(); w.WriteString("id", $"Actor:{npc.DurableId}");
                w.WriteString("label", labels.GetValueOrDefault(npc.DurableId, npc.Role)); w.WriteString("role", npc.Role);
                Vector(w, "position", actors.Get(npc.DurableId).Position.ToVector()); w.WriteEndObject();
            }
            w.WriteEndArray();
            // Everyone placed in the place's buildings, so a service is found before its building is entered.
            w.WriteStartArray("buildings");
            if (record.Exterior is { } exterior && sites.Profiles is { } profiles)
                foreach (DaggerfallSiteBuildingSource building in exterior.Buildings.Values
                    .Where(value => !DaggerfallBuildingEntryPolicy.IsResidence(value.Source.BuildingType)).OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
                {
                    if (!profiles.TryGet(DaggerfallWorldProfileIds.Interior(record.Id, building.Id), out DaggerfallSiteProfile inside)) continue;
                    DaggerfallStaticNpcPlacement[] offering = [.. inside.StaticNpcs.Where(npc => npc.Services.Contains(service, StringComparer.OrdinalIgnoreCase))];
                    if (offering.Length == 0) continue;
                    w.WriteStartObject(); w.WriteString("building", BuildingLabel(record, building.Id)); w.WriteString("id", building.Id.ToString());
                    w.WriteString("enter", $"world.enter \"{record.Name}\" {building.Id}");
                    w.WriteStartArray("people");
                    foreach (DaggerfallStaticNpcPlacement npc in offering)
                    {
                        w.WriteStartObject(); w.WriteString("role", npc.Role); w.WriteNumber("faction", npc.Appearance.FactionId);
                        Vector(w, "profilePosition", npc.Position.ToVector()); w.WriteEndObject();
                    }
                    w.WriteEndArray(); w.WriteEndObject();
                }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    /// <summary>The collision movement meets now: the admitted scene plus the live doors and supports.</summary>
    private CharacterStepEnvironment LiveCollision() => sites.Exterior.CharacterEnvironment(player.Motion);

    /// <summary>The gap a standing body keeps above its floor, as the movement owner stands it.</summary>
    private float FloorClearance => spatial.CurrentController.Shape.ContactSkin + spatial.CurrentController.Shape.ClearancePadding;

    /// <summary>The player's body in its stance, which a relocation keeps: a crouched player is placed and measured crouched.</summary>
    private float BodyHeight => spatial.StanceHeight(player.Motion.Stance);

    private Vector3 PlayerCentre() => player.Position!.Value.ToVector();

    /// <summary>The floor under the player: the centre less half the body and its clearance.</summary>
    private Vector3 PlayerFloor() => PlayerCentre() - Vector3.UnitY * ((BodyHeight * .5F) + FloorClearance);

    /// <summary>What the player's body on <paramref name="feet"/> would overlap in the retained collision, if anything.</summary>
    private SpatialHit BodyOverlap(Vector3 feet)
    {
        var shape = spatial.CurrentController.Shape;
        float halfHeight = BodyHeight * .5F;
        return spatial.OverlapCapsule(feet + Vector3.UnitY * (halfHeight + FloorClearance), Math.Max(0, halfHeight - shape.Radius), shape.Radius,
            ReadOnlyMemory<SpatialEntityCollider>.Empty, LiveCollision());
    }

    /// <summary>
    /// Lands the player on scene-local feet in the active profile through the one relocation owner. The body's centre
    /// is placed over the feet, so the movement owner's standing settles it onto that floor and keeps the height asked
    /// for where there is none.
    /// </summary>
    private DebugCommandResult Land(Vector3 feet, float yaw, float pitch, string anchor)
    {
        fastTravel.CloseMap();
        Vector3 centre = feet + Vector3.UnitY * ((BodyHeight * .5F) + FloorClearance);
        Vector3 position = profilePosition(new WorldPoint(centre.X, centre.Y, centre.Z));
        if (!sites.TryRelocatePlayer(sites.ActiveProfile, new(anchor, new(position.X, position.Y, position.Z), yaw, pitch)))
            return Refused("The relocation was refused.");
        return Where();
    }

    /// <summary>Arrives at a place's exterior as guild teleport does: its keyed landing, with no time or cost.</summary>
    private bool ArriveAt(DaggerfallSiteProfile exterior)
    {
        fastTravel.CloseMap();
        return sites.TryRelocatePlayer(exterior.ProfileKey, fastTravel.Landing(exterior, null, $"developer:{exterior.ProfileKey.LogicalId}"));
    }

    /// <summary>A place by region/index or by name, ranked as travel's search ranks; null when exactly one admitted exterior answers.</summary>
    private string? ResolvePlace(string place, out DaggerfallSiteProfile? exterior)
    {
        exterior = null;
        DaggerfallSiteProfiles profiles = sites.RequireProfiles();
        string[] parts = place.Split('/');
        DaggerfallSiteRecord[] candidates;
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int region)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            candidates = [.. site.Records.Where(record => record.Id == new DaggerfallSiteId(region, index))];
        else
        {
            var ranked = site.Records.Where(record => record.Exterior is not null)
                .Select(record => (Record: record, Rank: fastTravel.PlaceRank(record.Name, record.Id.Region, place))).Where(value => value.Rank is not null).ToArray();
            int? best = ranked.Length == 0 ? null : ranked.Min(value => value.Rank);
            candidates = [.. ranked.Where(value => value.Rank == best).Select(value => value.Record)];
        }
        if (candidates.Length == 0) return $"No place is named '{place}'.";
        if (candidates.Length > 1)
            return $"'{place}' names {candidates.Length} places: {string.Join("; ", candidates.Take(12).Select(record => $"{record.Name} in {DaggerfallRegionNames.Name(definitions.BuildingNames.RegionNames, record.Id.Region)} ({record.Id})"))}. Name one by region/index.";
        DaggerfallSiteRecord chosen = candidates[0];
        try
        {
            if (!profiles.TryGet(DaggerfallWorldProfileIds.Exterior(chosen.Id), out DaggerfallSiteProfile found)) return $"{chosen.Name} ({chosen.Id}) has no exterior to land in.";
            exterior = found;
            return null;
        }
        catch (InvalidOperationException failure)
        {
            return $"{chosen.Name} ({chosen.Id}) cannot be assembled: {DaggerfallRejectionText.For(failure)}";
        }
    }

    /// <summary>A building of a place by block-x/block-y/index or by name; null when exactly one with an interior answers.</summary>
    private string? ResolveBuilding(DaggerfallSiteRecord record, string building, out DaggerfallSiteBuildingId id, out DaggerfallSiteProfile? inside)
    {
        id = default; inside = null;
        DaggerfallSiteProfiles profiles = sites.RequireProfiles();
        IReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> buildings = record.Exterior?.Buildings
            ?? new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource>();
        DaggerfallSiteBuildingId[] matches = [.. buildings.Keys.Where(key => key.ToString() == building)];
        if (matches.Length == 0)
        {
            (DaggerfallSiteBuildingId Id, string Label)[] named = [.. buildings.Keys.Select(key => (key, BuildingLabel(record, key)))];
            matches = [.. named.Where(value => value.Label.Equals(building, StringComparison.OrdinalIgnoreCase)).Select(value => value.Id)];
            if (matches.Length == 0) matches = [.. named.Where(value => value.Label.Contains(building, StringComparison.OrdinalIgnoreCase)).Select(value => value.Id)];
        }
        matches = [.. matches.Where(key => profiles.TryGet(DaggerfallWorldProfileIds.Interior(record.Id, key), out _))];
        if (matches.Length == 0) return $"{record.Name} has no building named '{building}' with an interior. world.find lists buildings by service.";
        if (matches.Length > 1)
            return $"'{building}' names {matches.Length} buildings in {record.Name}: {string.Join("; ", matches.Take(12).Select(key => $"{BuildingLabel(record, key)} ({key})"))}. Name one by its id.";
        id = matches[0];
        inside = profiles.Require(DaggerfallWorldProfileIds.Interior(record.Id, id));
        return null;
    }

    private string BuildingLabel(DaggerfallSiteRecord record, DaggerfallSiteBuildingId id) =>
        record.Exterior?.Buildings.TryGetValue(id, out DaggerfallSiteBuildingSource? source) == true
            && entrance.DisplayName(record.Id, source) is { Length: > 0 } name ? name : $"building {id}";

    private static string TargetId(DaggerfallActivationTarget target) => $"{target.Identity.Kind}:{target.Identity.Value}";

    /// <summary>
    /// Stands the player facing <paramref name="target"/> within its reach: on its outside for a door or portal (its
    /// surface normal), otherwise on the side nearest the player, trying bearings around it for a spot the body fits,
    /// with floor below and a clear line from the eye to the target.
    /// </summary>
    private bool StandWithinReach(DaggerfallActivationTarget target, out string? why)
    {
        Vector3 point = target.Position.ToVector();
        Vector3 toward = target.SurfaceNormal is { } normal && new Vector2(normal.X, normal.Z).LengthSquared() > .01F
            ? new Vector3(normal.X, 0, normal.Z)
            : new Vector3(PlayerCentre().X - point.X, 0, PlayerCentre().Z - point.Z);
        float start = toward.LengthSquared() > .0001F ? MathF.Atan2(toward.X, toward.Z) : 0F;
        float eye = BodyHeight * .9F;
        // A person's point is their feet; aim at their chest. A door or container is aimed at as it is.
        Vector3 aim = target.Kind == DaggerfallActivationTargetKind.Npc ? point + Vector3.UnitY : point;
        for (int step = 0; step < GoToBearings; step++)
        {
            // Nearest bearings first, alternating sides: 0, +1, -1, +2, -2, ...
            int turn = (step + 1) / 2 * (step % 2 == 0 ? -1 : 1);
            float bearing = start + turn * (MathF.Tau / GoToBearings);
            Vector3 feet = point + new Vector3(MathF.Sin(bearing), 0, MathF.Cos(bearing)) * GoToStandoff;
            if (target.Kind != DaggerfallActivationTargetKind.Npc && FloorBelow(feet + Vector3.UnitY) is { } floor) feet.Y = floor;
            if (BodyOverlap(feet) is { Present: true } || FloorBelow(feet + Vector3.UnitY) is null) continue;
            Vector3 from = feet + Vector3.UnitY * eye, line = aim - from;
            float length = line.Length();
            SpatialHit blocked = spatial.CastRay(from, line / length, length, LiveCollision());
            if (blocked.Present && Vector3.Distance(blocked.Point, aim) > .4F) continue;
            Vector3 facing = point - feet;
            Land(feet, ActorHeading.Yaw(facing), MathF.Atan2(aim.Y - from.Y, new Vector2(line.X, line.Z).Length()), $"developer-goto:{TargetId(target)}");
            why = null;
            return true;
        }
        why = $"No clear spot within reach of {target.Label ?? target.Kind.ToString()} ({TargetId(target)}): every bearing overlapped collision, had no floor, or had no line to it.";
        return false;
    }

    /// <summary>The floor height below a point, within three metres, or null.</summary>
    private float? FloorBelow(Vector3 from)
    {
        SpatialHit floor = spatial.CastRay(from, -Vector3.UnitY, 3F, LiveCollision());
        return floor.Present && !floor.StartSolid && floor.Normal.Y > 0 ? floor.Point.Y : null;
    }

    private static string Format(Vector3 value) => FormattableString.Invariant($"{value.X:0.##}, {value.Y:0.##}, {value.Z:0.##}");
}
