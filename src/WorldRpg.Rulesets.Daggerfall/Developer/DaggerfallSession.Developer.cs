using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Developer movement (<see cref="IDeveloperWorldSession"/>). Every landing goes through the site lifecycle's one
/// relocation owner, as quest teleports, recall and guild teleport do: the player is stood on the floor, the exterior's
/// residency follows, and a change of place is an ordinary site transition. A journey lands as guild teleport does,
/// with no time, cost or encounter; a building is entered as its door enters it, without the door's greeting,
/// hours or trespass. Positions in and out are scene-local, as <c>navigation.targets</c> reports them.
/// </summary>
internal sealed partial class DaggerfallSession : IDeveloperWorldSession
{
    /// <summary>How far from a target <see cref="GoTo"/> stands: inside every interaction reach, clear of a counter's far side.</summary>
    private const float GoToStandoff = 1.5F;
    /// <summary>How many standing spots around a target <see cref="GoTo"/> tries.</summary>
    private const int GoToBearings = 16;

    public DebugCommandResult Teleport(double x, double y, double z, bool force)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "A teleport target must be finite.");
        Vector3 feet = new((float)x, (float)y, (float)z);
        // An interior is a box in empty space and a dungeon has gaps: a spot with nothing to stand on is refused too.
        if (!force && FloorBelow(feet + Vector3.UnitY) is null)
            return Refused($"There is no floor within three metres below ({Format(feet)}); world.teleport-force places the player there anyway.");
        if (!force && BodyOverlap(feet) is { Present: true })
            return Refused($"The body would overlap collision at ({Format(feet)}); world.teleport-force places it anyway. "
                + SpatialClearanceSnapshot.Capture(_engine.Spatial, _spatial.Session, PlayerCentre(), feet + Vector3.UnitY * FloorClearance,
                    _spatial.CurrentController.Shape.StandingHeight, _spatial.CurrentController, SpatialMovementSystem.SpatialColliders(LiveCollision())));
        return Land(feet, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians, "developer-teleport");
    }

    public DebugCommandResult Move(double dx, double dy, double dz)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        Vector3 feet = PlayerFloor();
        return Teleport(feet.X + dx, feet.Y + dy, feet.Z + dz, force: false);
    }

    public DebugCommandResult Forward(double metres)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (!double.IsFinite(metres)) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "A distance must be finite.");
        Vector3 offset = ActorHeading.Forward(State.PlayerControl.YawRadians) * (float)metres;
        return Move(offset.X, 0, offset.Z);
    }

    public DebugCommandResult Spawn(string actor, double metres)
    {
        if (!double.IsFinite(metres) || metres <= 0) return Refused("A distance is finite and above 0.");
        if (!_definitions.Actors.TryGetValue(new DaggerfallActorId(actor), out DaggerfallActorDefinition? definition) || definition.Kind == DaggerfallActorKinds.Player)
            return Refused($"'{actor}' is no actor definition that can be spawned.");
        if (State.PlayerControl.Position is not WorldPoint position) return Refused("The player has no position.");
        float yaw = State.PlayerControl.YawRadians;
        Vector3 at = position.ToVector() + (ActorHeading.Forward(yaw) * (float)metres);
        long spawned = SpawnActor(actor, new ActorPose(WorldPoint.From(at), yaw + MathF.PI));
        return DebugCommandResult.Success($"{actor} ({spawned}) stands {metres:0.#} m ahead.");
    }

    public DebugCommandResult Travel(string place)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (ResolvePlace(place, out DaggerfallSiteProfile? exterior) is { } unresolved) return Refused(unresolved);
        if (!ArriveAt(exterior!)) return Refused($"{exterior!.ProfileKey.LogicalId} could not be admitted.");
        return Where();
    }

    public DebugCommandResult Enter(string place, string building)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (ResolvePlace(place, out DaggerfallSiteProfile? exterior) is { } unresolved) return Refused(unresolved);
        DaggerfallSiteRecord site = _site.Require(exterior!.Site!.Value);
        if (ResolveBuilding(site, building, out DaggerfallSiteBuildingId id, out DaggerfallSiteProfile? inside) is { } missing) return Refused(missing);
        if (_activeProfileKey != exterior.ProfileKey && !ArriveAt(exterior)) return Refused($"{exterior.ProfileKey.LogicalId} could not be admitted.");
        DaggerfallDoorView? door = _doors.All.Where(value => _doors.ExteriorBuildingOf(value.Id) == id)
            .Select(value => (DaggerfallDoorView?)value).FirstOrDefault();
        if (door is not { } entrance) return Refused($"{BuildingLabel(site, id)} has no door in the loaded exterior.");
        // Stand at the door first, so leaving the building returns the player to its doorstep as walking in would.
        if (_activation?.InspectTargets().Where(target => target.Entity == entrance.Entity)
            .Select(target => (DaggerfallActivationTarget?)target).FirstOrDefault() is { } doorTarget)
            _ = StandWithinReach(doorTarget, out _);
        if (!_sites.TryEnterBuilding(inside!.ProfileKey, BuildingLanding(inside, entrance.Id)))
            return Refused($"{BuildingLabel(site, id)} could not be entered.");
        return Where();
    }

    public DebugCommandResult GoTo(string target)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (_activation is null) return Refused("No interaction targets are loaded.");
        WorldPoint origin = State.PlayerControl.Position!.Value;
        DaggerfallActivationTarget[] targets = [.. _activation.InspectTargets()];
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
        if (State.PlayerControl.Position is not WorldPoint position) return Refused("The player has no position.");
        return PlaytestJson(w =>
        {
            w.WriteStartObject();
            DaggerfallSiteRecord? site = _site.ActiveSite;
            w.WriteString("place", site?.Name); w.WriteString("site", site?.Id.ToString());
            w.WriteString("profile", _activeProfileKey.LogicalId); w.WriteString("kind", _activeProfileKey.Kind.ToString());
            // The title stands a world behind it too, so whether a game is running is said outright.
            w.WriteBoolean("playing", _mode != ProductMode.Title);
            if (site is not null && _sites.Projection.Inputs.InteriorBuilding is { } interior)
                w.WriteString("building", BuildingLabel(site, new(interior.BlockX, interior.BlockY, interior.Building.Index)));
            Vector(w, "feet", PlayerFloor()); Vector(w, "centre", position.ToVector());
            w.WriteNumber("yawDegrees", State.PlayerControl.YawRadians * 180 / MathF.PI);
            w.WriteNumber("pitchDegrees", State.PlayerControl.PitchRadians * 180 / MathF.PI);
            w.WriteString("lastOutcome", Presentation.LastOutcome);
            w.WriteStartArray("nearest");
            foreach (DaggerfallActivationTarget target in (_activation?.InspectTargets() ?? []).OrderBy(value => position.HorizontalDistanceTo(value.Position)).Take(8))
            {
                w.WriteStartObject(); w.WriteString("id", TargetId(target)); w.WriteString("label", target.Label ?? target.Kind.ToString());
                w.WriteNumber("distance", Math.Round(position.HorizontalDistanceTo(target.Position), 2)); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Hour(int hour)
    {
        if (hour is < 0 or > 23) return Refused("An hour is from 0 to 23.");
        const int secondsPerDay = 86_400;
        int seconds = ((hour * 3_600) - (int)_time.Calendar.SecondOfDay + secondsPerDay) % secondsPerDay;
        _ = AdvanceElapsedTime(seconds, idleFatigue: false);
        return DebugCommandResult.Success($"It is {_time.Calendar.SecondOfDay / 3_600}:00, {_time.Calendar.Season}.");
    }

    public DebugCommandResult Season(string season)
    {
        if (!Enum.TryParse(season, ignoreCase: true, out DaggerfallSeason target) || !Enum.IsDefined(target))
            return Refused($"A season is one of {string.Join(", ", Enum.GetNames<DaggerfallSeason>())}.");
        DaggerfallCalendar now = _time.Calendar;
        if (now.Season == target) return DebugCommandResult.Success($"It is already {target}.");
        // Each season opens on its first month, by the calendar's own table.
        int first = target switch { DaggerfallSeason.Winter => 11, DaggerfallSeason.Spring => 2, DaggerfallSeason.Summer => 5, _ => 8 };
        int days = ((first - now.Month + DaggerfallCalendar.MonthsPerYear) % DaggerfallCalendar.MonthsPerYear * DaggerfallCalendar.DaysPerMonth) - now.Day;
        _ = AdvanceElapsedTime((long)days * DaggerfallCalendar.SecondsPerDay, idleFatigue: false);
        return DebugCommandResult.Success($"It is day {_time.Calendar.Day + 1} of month {_time.Calendar.Month + 1}, {_time.Calendar.Season}.");
    }

    public DebugCommandResult GroundBlend(double contrast, double repeatMetres, double triplanarSharpness)
    {
        if (contrast < 1 || repeatMetres <= 0 || triplanarSharpness < 0) return Refused("A contrast is 1 or more, a repeat above 0 and a sharpness 0 or more.");
        if (!DaggerfallGroundBlend.FillsCell((float)repeatMetres)) return Refused($"A texture repeating every {repeatMetres} metres would break at every cell's edge.");
        _horizon.Rebuild();
        _exterior.UseGroundBlend(new((float)contrast, (float)repeatMetres, (float)triplanarSharpness));
        return DebugCommandResult.Success("Ground layers again by these settings.");
    }

    public DebugCommandResult GroundWeather(double wet, double snow)
    {
        LocalWeather.State.Force((float)wet, (float)snow);
        return DebugCommandResult.Success($"The ground here is {LocalWeather.Conditions.Wet:0.##} wet and {LocalWeather.Conditions.Snow:0.##} snowed.");
    }

    public DebugCommandResult Weather(string kind)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out DaggerfallWeatherKind weather) || !Enum.IsDefined(weather))
            return Refused($"The weather is one of {string.Join(", ", Enum.GetNames<DaggerfallWeatherKind>())}.");
        LocalWeather.State.Force(weather);
        return DebugCommandResult.Success($"The weather is {weather}.");
    }

    public DebugCommandResult Stand(double east, double south)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (_exterior.ActiveExteriorOrigin is not { } origin || State.PlayerControl.Position is not { } position)
            return Refused("The player is not outdoors.");
        Vector3 feet = PlayerFloor();
        double rise = _horizon.GroundRise(origin, position.ToVector(), east, south);
        return Teleport(feet.X + east, feet.Y + rise + .05, feet.Z + south, force: true);
    }

    public DebugCommandResult Scout(string kind, double kilometres)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (!DaggerfallHorizonGround.Kinds.Contains(kind)) return Refused($"Ground is one of {string.Join(", ", DaggerfallHorizonGround.Kinds)}.");
        if (!double.IsFinite(kilometres) || kilometres is <= 0 or > 100) return Refused("Search out to more than 0 and at most 100 kilometres.");
        if (_exterior.ActiveExteriorOrigin is not { } origin || State.PlayerControl.Position is not { } position) return Refused("The player is not outdoors.");
        bool winter = _time.Calendar.Season == DaggerfallSeason.Winter;
        if (_horizon.FindGround(origin, position.ToVector(), kind, kilometres * 1000, winter) is not { } found)
            return Refused($"No open ground within {kilometres} km is mostly {kind}{(winter ? " in winter" : "")}; travel nearer first.");
        (double fromX, double fromZ) = DaggerfallHorizon.MapPoint(origin, position.ToVector());
        string by = "";
        // A stand moves the player within the Engine's local envelope; farther, the player lands at the place nearest the spot first.
        if (Math.Max(Math.Abs(found.X - fromX), Math.Abs(found.Z - fromZ)) > ScoutStandMetres)
        {
            DaggerfallSiteProfiles profiles = _sites.RequireProfiles();
            DaggerfallSiteRecord? nearest = _site.Records
                .Where(record => record.Exterior is not null && profiles.TryGet(DaggerfallWorldProfileIds.Exterior(record.Id), out _))
                .MinBy(record => Math.Pow(((record.MapPixelX + .5) * DaggerfallExteriorCellResidency.CellSize) - found.X, 2)
                    + Math.Pow(((record.MapPixelY + .5) * DaggerfallExteriorCellResidency.CellSize) - found.Z, 2));
            if (nearest is null || !profiles.TryGet(DaggerfallWorldProfileIds.Exterior(nearest.Id), out DaggerfallSiteProfile exterior) || !ArriveAt(exterior))
                return Refused($"Ground that is mostly {kind} lies {Math.Max(Math.Abs(found.X - fromX), Math.Abs(found.Z - fromZ)) / 1000:F0} km away, and no place near it could be landed at.");
            if (_exterior.ActiveExteriorOrigin is not { } landed || State.PlayerControl.Position is not { } there) return Refused($"{nearest.Name} has no ground to stand on.");
            (fromX, fromZ) = DaggerfallHorizon.MapPoint(landed, there.ToVector());
            by = $" by way of {nearest.Name}";
        }

        double east = found.X - fromX, south = found.Z - fromZ;
        DebugCommandResult stood = Stand(east, south);
        return stood.Status == DebugCommandStatus.Success
            ? DebugCommandResult.Success(string.Create(CultureInfo.InvariantCulture, $"Standing in {kind}, {east:F0} m east and {south:F0} m south of where the player was{by}."))
            : stood;
    }

    /// <summary>The farthest a scout stands the player from where they are, well inside the Engine's local envelope.</summary>
    private const double ScoutStandMetres = 4000;

    public DebugCommandResult NearestRiver() => _exterior.ActiveExteriorOrigin is { } origin && State.PlayerControl.Position is { } position
        ? DebugCommandResult.Success(_horizon.NearestRiver(origin, position.ToVector(), _definitions.Terrain))
        : Refused("The player is not outdoors.");

    public DebugCommandResult GroundSurvey() => DebugCommandResult.Success(_horizon.GroundSurvey(_definitions.Terrain));

    public DebugCommandResult Horizon() => DebugCommandResult.Success(_horizon.Readout());

    public DebugCommandResult HorizonSet(double lift, double fogDensity)
    {
        if (!double.IsFinite(lift) || lift < 1) return Refused("A lift is 1 (true height) or more.");
        if (!double.IsFinite(fogDensity) || fogDensity <= 0) return Refused("A haze is a positive density per metre.");
        _horizon.Adjust(lift, fogDensity);
        return DebugCommandResult.Success(_horizon.Readout());
    }

    public DebugCommandResult Flora(double density)
    {
        if (!double.IsFinite(density) || density < 0) return Refused("A density is finite and 0 or more.");
        _horizon.UseFloraDensity((float)density);
        return DebugCommandResult.Success(_horizon.Readout());
    }

    public DebugCommandResult FarForest(string state)
    {
        if (state is not ("on" or "off")) return Refused("The far forest is on or off.");
        _horizon.UseFarForest(state == "on");
        return DebugCommandResult.Success($"The far forest is {state}.");
    }

    public DebugCommandResult Trees(string kind)
    {
        if (kind is not ("mesh" or "sprite")) return Refused("Trees grow as mesh or sprite.");
        _horizon.UseTreeMeshes(kind == "mesh");
        return DebugCommandResult.Success($"Trees grow as {kind}s.");
    }

    public DebugCommandResult ClassicArt(string state)
    {
        if (state is not ("on" or "off")) return Refused("Classic art is on or off.");
        _classicArt = state == "on";
        return DebugCommandResult.Success(_classicArt ? "Classic art is drawn in place of the refreshed art for this run." : "The refreshed art is drawn.");
    }

    public DebugCommandResult GroundCover(string kinds)
    {
        if (_horizon.GroundCover is not { } cover) return Refused("Ground cover draws outdoors in refreshed art.");
        string[] named = kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (named.Length == 0 || named.Any(kind => kind is not ("grass" or "relief" or "tufts" or "all" or "none")))
            return Refused("Ground cover is grass, relief and tufts, separated by commas, or all or none.");
        bool all = named.Contains("all");
        cover.Show(all || named.Contains("grass"), all || named.Contains("relief"), all || named.Contains("tufts"));
        return DebugCommandResult.Success($"Ground cover: {kinds}.");
    }

    public DebugCommandResult Find(string service)
    {
        if (_site.ActiveSite is not { } site) return Refused("The player is at no place with people.");
        Dictionary<long, string> labels = (_activation?.InspectTargets() ?? [])
            .Where(target => target.Kind == DaggerfallActivationTargetKind.Npc)
            .GroupBy(target => (long)target.Identity.Value).ToDictionary(group => group.Key, group => group.First().Label ?? "person");
        return PlaytestJson(w =>
        {
            w.WriteStartObject();
            w.WriteString("place", site.Name); w.WriteString("service", service);
            // The people loaded here now, by the services the ruleset admitted for them.
            w.WriteStartArray("loaded");
            foreach (DaggerfallNpc npc in State.Npcs.All.Where(npc => npc.Presence == DaggerfallNpcPresence.Active
                && npc.Services.Contains(service, StringComparer.OrdinalIgnoreCase) && State.Actors.TryGet(npc.DurableId, out _)))
            {
                w.WriteStartObject(); w.WriteString("id", $"Actor:{npc.DurableId}");
                w.WriteString("label", labels.GetValueOrDefault(npc.DurableId, npc.Role)); w.WriteString("role", npc.Role);
                Vector(w, "position", State.Actors.Get(npc.DurableId).Position.ToVector()); w.WriteEndObject();
            }
            w.WriteEndArray();
            // Everyone placed in the place's buildings, so a service is found before its building is entered.
            w.WriteStartArray("buildings");
            if (site.Exterior is { } exterior && _sites.Profiles is { } profiles)
                foreach (DaggerfallSiteBuildingSource building in exterior.Buildings.Values
                    .Where(value => !DaggerfallBuildingEntryPolicy.IsResidence(value.Source.BuildingType)).OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
                {
                    if (!profiles.TryGet(DaggerfallWorldProfileIds.Interior(site.Id, building.Id), out DaggerfallSiteProfile inside)) continue;
                    DaggerfallStaticNpcPlacement[] offering = [.. inside.StaticNpcs.Where(npc => npc.Services.Contains(service, StringComparer.OrdinalIgnoreCase))];
                    if (offering.Length == 0) continue;
                    w.WriteStartObject(); w.WriteString("building", BuildingLabel(site, building.Id)); w.WriteString("id", building.Id.ToString());
                    w.WriteString("enter", $"world.enter \"{site.Name}\" {building.Id}");
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

    /// <summary>
    /// Why the player cannot be moved now, or null. A gameplay screen or question that holds the world (a conversation,
    /// loot, dungeon text, a pending offer or court) is answered first; the game menu, where the developer console
    /// lives, is not one of them.
    /// </summary>
    private string? DeveloperMoveRefusal()
    {
        if (_mode == ProductMode.Title) return "No game is running.";
        if (State.Actors.Player.IsDefeated) return "The player is dead.";
        if (State.PlayerControl.Position is null) return "The player has no position.";
        DaggerfallInteractionScreen[] open = [.. Enum.GetValues<DaggerfallInteractionScreen>()
            .Where(screen => screen != DaggerfallInteractionScreen.Menu && _interactions.IsOpen(screen))];
        if (open.Length > 0) return $"Close the open {string.Join(", ", open)} first.";
        if (Summoning.PendingOffer || State.Quests.PendingOffer is not null) return "Answer the pending offer first.";
        // A quest's question (the tutorial's Yes/No, a prompt) holds as it holds a rest.
        if (State.Quests.Messages.Pending is not null) return "Answer the quest's question first.";
        if (Law.ModalOpen) return "Answer the court first.";
        if (DispelChoice.Pending is not null || IdentifyChoice.Open || CreateItemChoice.Pending is not null) return "Answer the pending spell choice first.";
        return _openingCinematics.IsActive || Cinematics?.ActiveSource is not null ? "A cinematic is playing." : null;
    }

    /// <summary>The collision movement meets now: the admitted scene plus the live doors and supports.</summary>
    private CharacterStepEnvironment LiveCollision() => _exterior.CharacterEnvironment(State.PlayerControl.Motion);

    private static DebugCommandResult Refused(string reason) => DebugCommandResult.Failure(DebugCommandStatus.Failed, reason);

    /// <summary>The gap a standing body keeps above its floor, as the movement owner stands it.</summary>
    private float FloorClearance => _spatial.CurrentController.Shape.ContactSkin + _spatial.CurrentController.Shape.ClearancePadding;

    private Vector3 PlayerCentre() => State.PlayerControl.Position!.Value.ToVector();

    /// <summary>The floor under the standing player: the centre less half the body and its clearance.</summary>
    private Vector3 PlayerFloor() =>
        PlayerCentre() - Vector3.UnitY * ((_spatial.CurrentController.Shape.StandingHeight * .5F) + FloorClearance);

    /// <summary>What a standing body on <paramref name="feet"/> would overlap in the retained collision, if anything.</summary>
    private SpatialHit BodyOverlap(Vector3 feet)
    {
        var shape = _spatial.CurrentController.Shape;
        float halfHeight = shape.StandingHeight * .5F;
        return _spatial.OverlapCapsule(feet + Vector3.UnitY * (halfHeight + FloorClearance), Math.Max(0, halfHeight - shape.Radius), shape.Radius,
            ReadOnlyMemory<SpatialEntityCollider>.Empty, LiveCollision());
    }

    /// <summary>
    /// Lands the player on scene-local feet in the active profile through the one relocation owner. The body's centre
    /// is placed over the feet, so the movement owner's standing settles it onto that floor and keeps the height asked
    /// for where there is none.
    /// </summary>
    private DebugCommandResult Land(Vector3 feet, float yaw, float pitch, string anchor)
    {
        FastTravel.CloseMap();
        Vector3 centre = feet + Vector3.UnitY * ((_spatial.CurrentController.Shape.StandingHeight * .5F) + FloorClearance);
        Vector3 position = ProfilePosition(new WorldPoint(centre.X, centre.Y, centre.Z));
        if (!_sites.TryRelocatePlayer(_activeProfileKey, new(anchor, new(position.X, position.Y, position.Z), yaw, pitch)))
            return Refused("The relocation was refused.");
        return Where();
    }

    /// <summary>Arrives at a place's exterior as guild teleport does: its keyed landing, with no time or cost.</summary>
    private bool ArriveAt(DaggerfallSiteProfile exterior)
    {
        FastTravel.CloseMap();
        return _sites.TryRelocatePlayer(exterior.ProfileKey, FastTravel.Landing(exterior, null, $"developer:{exterior.ProfileKey.LogicalId}"));
    }

    /// <summary>A place by region/index or by name, ranked as travel's search ranks; null when exactly one admitted exterior answers.</summary>
    private string? ResolvePlace(string place, out DaggerfallSiteProfile? exterior)
    {
        exterior = null;
        DaggerfallSiteProfiles profiles = _sites.RequireProfiles();
        string[] parts = place.Split('/');
        DaggerfallSiteRecord[] candidates;
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int region)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            candidates = [.. _site.Records.Where(record => record.Id == new DaggerfallSiteId(region, index))];
        else
        {
            var ranked = _site.Records.Where(record => record.Exterior is not null)
                .Select(record => (Record: record, Rank: FastTravel.PlaceRank(record.Name, record.Id.Region, place))).Where(value => value.Rank is not null).ToArray();
            int? best = ranked.Length == 0 ? null : ranked.Min(value => value.Rank);
            candidates = [.. ranked.Where(value => value.Rank == best).Select(value => value.Record)];
        }
        if (candidates.Length == 0) return $"No place is named '{place}'.";
        if (candidates.Length > 1)
            return $"'{place}' names {candidates.Length} places: {string.Join("; ", candidates.Take(12).Select(record => $"{record.Name} in {DaggerfallRegionNames.Name(_definitions.BuildingNames.RegionNames, record.Id.Region)} ({record.Id})"))}. Name one by region/index.";
        DaggerfallSiteRecord site = candidates[0];
        try
        {
            if (!profiles.TryGet(DaggerfallWorldProfileIds.Exterior(site.Id), out DaggerfallSiteProfile found)) return $"{site.Name} ({site.Id}) has no exterior to land in.";
            exterior = found;
            return null;
        }
        catch (InvalidOperationException failure)
        {
            return $"{site.Name} ({site.Id}) cannot be assembled: {DaggerfallRejectionText.For(failure)}";
        }
    }

    /// <summary>A building of a place by block-x/block-y/index or by name; null when exactly one with an interior answers.</summary>
    private string? ResolveBuilding(DaggerfallSiteRecord site, string building, out DaggerfallSiteBuildingId id, out DaggerfallSiteProfile? inside)
    {
        id = default; inside = null;
        DaggerfallSiteProfiles profiles = _sites.RequireProfiles();
        IReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> buildings = site.Exterior?.Buildings
            ?? new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource>();
        DaggerfallSiteBuildingId[] matches = [.. buildings.Keys.Where(key => key.ToString() == building)];
        if (matches.Length == 0)
        {
            (DaggerfallSiteBuildingId Id, string Label)[] named = [.. buildings.Keys.Select(key => (key, BuildingLabel(site, key)))];
            matches = [.. named.Where(value => value.Label.Equals(building, StringComparison.OrdinalIgnoreCase)).Select(value => value.Id)];
            if (matches.Length == 0) matches = [.. named.Where(value => value.Label.Contains(building, StringComparison.OrdinalIgnoreCase)).Select(value => value.Id)];
        }
        matches = [.. matches.Where(key => profiles.TryGet(DaggerfallWorldProfileIds.Interior(site.Id, key), out _))];
        if (matches.Length == 0) return $"{site.Name} has no building named '{building}' with an interior. world.find lists buildings by service.";
        if (matches.Length > 1)
            return $"'{building}' names {matches.Length} buildings in {site.Name}: {string.Join("; ", matches.Take(12).Select(key => $"{BuildingLabel(site, key)} ({key})"))}. Name one by its id.";
        id = matches[0];
        inside = profiles.Require(DaggerfallWorldProfileIds.Interior(site.Id, id));
        return null;
    }

    private string BuildingLabel(DaggerfallSiteRecord site, DaggerfallSiteBuildingId id) =>
        site.Exterior?.Buildings.TryGetValue(id, out DaggerfallSiteBuildingSource? source) == true
            && BuildingDisplayName(site.Id, source) is { Length: > 0 } name ? name : $"building {id}";

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
        float eye = _spatial.CurrentController.Shape.StandingHeight * .9F;
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
            SpatialHit blocked = _spatial.CastRay(from, line / length, length, LiveCollision());
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
        SpatialHit floor = _spatial.CastRay(from, -Vector3.UnitY, 3F, LiveCollision());
        return floor.Present && !floor.StartSolid && floor.Normal.Y > 0 ? floor.Point.Y : null;
    }

    private static string Format(Vector3 value) => FormattableString.Invariant($"{value.X:0.##}, {value.Y:0.##}, {value.Z:0.##}");
}
