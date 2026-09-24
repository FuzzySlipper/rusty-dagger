using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Session-owned admission boundary for the semantic rest choices.</summary>
internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallRestRecoveryModule _restRecovery = new();
    private readonly DaggerfallRestPresentation _restPresentation = new();

    /// <summary>The last accepted or rejected rest result, retained for the next HUD projection.</summary>
    internal DaggerfallRestView RestView => _restPresentation.Read();

    /// <summary>
    /// Applies one rest choice through a caller-supplied adapter to the shared elapsed-time owner.
    /// </summary>
    /// <remarks>
    /// The adapter is deliberately explicit: the session knows the calendar and encounter owners,
    /// while this method owns player stats and Medical attribution. A caller must not advance time
    /// separately before invoking it.
    /// </remarks>
    internal DaggerfallRestResult ApplyRest(
        DaggerfallRestRequest request,
        DaggerfallRestEligibility eligibility,
        Func<long, DaggerfallRestTimeAdvance> advanceTime,
        bool rapidHealing = false,
        bool noRegeneration = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(advanceTime);

        try
        {
            request.Validate();
            eligibility.Validate();
        }
        catch (ArgumentException rejection)
        {
            DaggerfallRestResult rejected = DaggerfallRestResult.Rejected(request.Mode, rejection.Message);
            _restPresentation.Publish(rejected);
            Presentation.SetOutcome(rejected.Message!);
            return rejected;
        }

        StatsComponent player = State.Actors.Player.Stats;
        DaggerfallRestResult result = _restRecovery.Apply(
            player,
            request,
            eligibility,
            player.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
            player.GetStat(StatId.Parse("medical")).ValueInt,
            rapidHealing,
            noRegeneration,
            advanceTime,
            recordMedicalRest: () => State.SkillUses.Record(new DaggerfallSkillUse(
                "medical",
                DaggerfallSkillUseReason.MedicalRest,
                DaggerfallSkillUseOutcome.Accepted)),
            advanceSkills: () =>
            {
                State.SkillUses.RaiseSkills(_time.Calendar.ToAbsoluteSeconds());
                State.LevelUps.BeginIfEligible();
            },
            currentRecoveryInputs: () =>
            {
                (bool rapid, bool noRegen) = RestCharacterTraits();
                return (player.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
                    player.GetStat(StatId.Parse("medical")).ValueInt, rapid, noRegen);
            });
        _restPresentation.Publish(result);
        Presentation.SetOutcome(result.Message ?? (result.Accepted ? "Rest complete." : "Rest refused."));
        return result;
    }

    /// <summary>Admits the DOM rest action at the session boundary.</summary>
    private void ChangeRest(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        DaggerfallRestMode mode = action.Mode switch
        {
            "timed" => DaggerfallRestMode.Timed,
            "until-healed" => DaggerfallRestMode.UntilHealed,
            "loiter" => DaggerfallRestMode.Loiter,
            _ => throw new ArgumentException($"Unknown rest mode '{action.Mode}'.", nameof(action)),
        };
        DaggerfallRestRequest request = new(mode, mode == DaggerfallRestMode.UntilHealed ? 0 : action.Hours ?? 0);
        DaggerfallRestEligibility eligibility = CurrentRestEligibility();
        (bool rapidHealing, bool noRegeneration) = RestCharacterTraits();
        _ = ApplyRest(request, eligibility, AdvanceRestInterval, rapidHealing, noRegeneration);
        _input.Neutralize();
        _locomotion.Neutralize();
    }

    /// <summary>
    /// Supplies the current location/player gate without inventing rental or ownership state that the
    /// product does not yet publish. The admitted profile remains the authority for whether a rest
    /// action has a world to advance in; a missing player pose is a concrete rejection.
    /// </summary>
    private DaggerfallRestEligibility CurrentRestEligibility()
    {
        bool alive = State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0d;
        if (!alive) return new(false, IsAlive: false, "You cannot rest while defeated.");
        if (HasNearbyRestEnemy()) return new(false, Message: "Enemies are too close to rest.");
        if (State.PlayerControl.Position is null) return new(false, Message: "You cannot rest before the player has a world position.");
        if (_activeProfileKey.LogicalId.Length == 0) return new(false, Message: "You cannot rest without an admitted world profile.");

        // An interior needs proof of the exact placed building and a current room, property, or guild
        // privilege. The normalized profile carries that source identity; unknown interiors stay closed.
        if (!_site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site))
            return new(false, Message: "You cannot rest without an admitted location record.");
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior)
        {
            DaggerfallInteriorBuilding? building = _siteProfiles?.Require(_activeProfileKey).InteriorBuilding;
            if (FightersGuildRestAllowed(building, State.GuildMembership, _activeProfileKey.Site.Region,
                checked((int)_time.Calendar.DayNumber)))
                return new(true);
            return new(false, Message: "You cannot rest in this interior without an admitted room or guild privilege.");
        }
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior
            && CurrentExteriorCell() is { } current
            && _definitions.Grids.Climate.GetCell(current.X, current.Y).Value is not (224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232))
            return new(false, Message: "You cannot rest on this terrain.");
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior
            && site.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage)
        {
            DaggerfallExteriorCellId cell = CurrentExteriorCell();
            if (site.Exterior is { } exterior && cell == new DaggerfallExteriorCellId(exterior.MapPixelX, exterior.MapPixelY))
                return new(false, Message: "Camping in a town is not permitted.");
        }
        return new(true);
    }

    internal static bool FightersGuildRestAllowed(DaggerfallInteriorBuilding? building,
        DaggerfallGuildMembershipPolicy membership, int region, int day)
    {
        if (building is not { BuildingType: 11, FactionId: DaggerfallConcreteGuildCatalog.FightersFactionId })
            return false;
        DaggerfallGuildMembershipView member = membership.Read(DaggerfallConcreteGuildCatalog.FightersFactionId, day);
        DaggerfallConcreteGuildDefinition guild = DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.FightersFactionId);
        return DaggerfallConcreteGuildPolicy.EvaluateService(guild, DaggerfallConcreteGuildService.Rest,
            new DaggerfallGuildServiceContext(member.IsMember, member.Rank, CurrentRegion: region)).Eligible;
    }

    /// <summary>
    /// Reads the admitted character owner for the two recovery branches it currently exposes.
    /// Character custom-class validation does not publish these classic trait ids yet, so all
    /// current characters resolve to the ordinary formula branches; if #7986 later admits them,
    /// rest will consume the same committed owner without another hardcoded flag.
    /// </summary>
    private (bool RapidHealing, bool NoRegeneration) RestCharacterTraits()
    {
        DaggerfallCustomCareerDefinition? custom = State.Character.CustomCareer;
        if (custom is null) return (false, false);

        bool rapidHealing = custom.Advantages.Any(trait => trait.Id == "rapid-healing" && RapidHealingApplies(trait.Target));
        bool noRegeneration = custom.Disadvantages.Any(trait => trait.Id == "inability-to-regen");
        return (rapidHealing, noRegeneration);
    }

    /// <summary>Matches the donor's light/dark rapid-healing condition at the current profile and hour.</summary>
    private bool RapidHealingApplies(string? target) => target switch
    {
        "general" => true,
        "light" => _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior && _time.Calendar.IsDay,
        "darkness" => _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior || !_time.Calendar.IsDay,
        _ => false,
    };

    /// <summary>
    /// Advances exactly one rest tick through the session calendar and reports a selected encounter as
    /// the rest interruption. Pending encounter actors materialize on the next ordinary admitted step.
    /// </summary>
    private DaggerfallRestTimeAdvance AdvanceRestInterval(long requestedSeconds)
    {
        if (HasNearbyRestEnemy()) return new(requestedSeconds, 0, DaggerfallRestInterruption.Encounter);
        long applied = 0;
        while (applied < requestedSeconds)
        {
            long secondsToMinute = DaggerfallCalendar.SecondsPerMinute
                - (_time.Calendar.ToAbsoluteSeconds() % DaggerfallCalendar.SecondsPerMinute);
            long slice = Math.Min(requestedSeconds - applied, secondsToMinute);
            DaggerfallCalendarAdvance advance = AdvanceElapsedTime(slice, deferSkillAdvancement: true);
            applied = checked(applied + advance.AppliedSeconds);
            if (State.Actors.Player.IsDefeated)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Defeated);
            if (HasNearbyRestEnemy())
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Encounter);
            if (advance.AppliedSeconds != slice)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Prevented);
            if (_time.Calendar.ToAbsoluteSeconds() % DaggerfallCalendar.SecondsPerMinute != 0) continue;

            long minute = _time.Calendar.ToAbsoluteSeconds() / DaggerfallCalendar.SecondsPerMinute;
            DaggerfallEncounterRequest? encounter = RestEncounterRequest(minute);
            if (encounter is null) continue;
            DaggerfallEncounterResolution selected = QueueEncounter(encounter);
            if (selected.Choice.MobileId is not null)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Encounter);
        }
        return new(requestedSeconds, applied);
    }

    /// <summary>
    /// Builds the source encounter context for each elapsed eligible game minute. The donor checks
    /// intermittent spawns at minutes where (minute / 12) % 12 is zero and retries after failed draws.
    /// </summary>
    private DaggerfallEncounterRequest? RestEncounterRequest(long minute)
    {
        if ((minute / 12) % 12 != 0 || !_site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site)) return null;

        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon)
            return new(DaggerfallEncounterContext.Dungeon, State.Progression.Level,
                DungeonType: site.DungeonType, EnemyAlert: true);
        if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior) return null;

        DaggerfallExteriorCellId cell = CurrentExteriorCell();
        DaggerfallClimateCell climate = _definitions.Grids.Climate.GetCell(cell.X, cell.Y);
        if (climate.Value is not (224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232))
            throw new InvalidOperationException($"Exterior rest at {cell.X}/{cell.Y} has no encounter climate.");
        long timeOfDay = minute % (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour);
        bool day = timeOfDay is >= 360 and <= 1080;
        bool atLocation = site.Exterior is { } exterior
            && cell == new DaggerfallExteriorCellId(exterior.MapPixelX, exterior.MapPixelY);
        if (atLocation && day) return null;
        DaggerfallEncounterContext context = atLocation
            ? DaggerfallEncounterContext.LocationNight
            : day ? DaggerfallEncounterContext.WildernessDay : DaggerfallEncounterContext.WildernessNight;
        return new(context, State.Progression.Level, Climate: climate.Value);
    }

    private bool HasNearbyRestEnemy()
    {
        if (State.PlayerControl.Position is not { } player) return false;
        foreach (var actor in State.Actors.All)
        {
            if (actor.IsDefeated || _enemyBehavior.IsPacified(actor.DurableId)
                || !_definitionsByActor.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)
                || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
                || definition.Team == "player-ally") continue;
            bool inSight = _enemyBehavior.LastPerception.TryGetValue(actor.DurableId, out var perception)
                && perception.InSight;
            if (inSight || Vector3.DistanceSquared(actor.Position.ToVector(), player.ToVector()) <= 12f * 12f)
                return true;
        }
        return false;
    }
}
