using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Admits source-backed civilian placements for the active exterior and walks them. The importer owns the placement,
/// billboard, faction and climate facts; this owner only selects the current time slice, binds one durable NPC identity,
/// and routes it through the civilians' materialization.
/// </summary>
/// <param name="profile">The active site's profile.</param>
/// <param name="activeProfile">The active site profile's key.</param>
/// <param name="appearance">The active site's appearance.</param>
/// <param name="locationLoaded">Whether the exterior's active location (its town) is loaded.</param>
/// <param name="residencyInitialized">Whether the exterior's cell window has been admitted, so the town lies somewhere.</param>
/// <param name="profileToLocal">A point in the active profile's frame in the scene's local frame.</param>
/// <param name="localToProfile">A scene-local point in the active profile's frame.</param>
/// <param name="requireSite">A site's record by identity.</param>
/// <param name="isDay">Whether it is day now.</param>
/// <param name="suppressSpawns">Whether the player's form keeps new people from coming out (a beast form).</param>
/// <param name="held">Whether a reaction holds an actor in place.</param>
/// <param name="wander">Steps one admitted civilian's wander (the enemy behaviour module's).</param>
/// <param name="report">Says, once a site, that its townsfolk stay away because its climate gives them no race.</param>
internal sealed class DaggerfallSourcePopulation(DaggerfallNpcRegistry npcs, ActorsState actors, PlayerControlState player,
    DaggerfallDefinitions definitions, DaggerfallCivilianWanderTuning tuning, DaggerfallCivilians civilians,
    Func<DaggerfallSiteProfile> profile, Func<DaggerfallWorldProfileKey> activeProfile, Func<DaggerfallSiteAppearance> appearance,
    Func<bool> locationLoaded, Func<bool> residencyInitialized, Func<WorldPoint, WorldPoint> profileToLocal, Func<Vector3, Vector3> localToProfile,
    Func<DaggerfallSiteId, DaggerfallSiteRecord> requireSite, Func<bool> isDay, Func<bool> suppressSpawns, Func<long, bool> held,
    Action<ActorState, WanderPolicy, ulong, float> wander, Action<string> report,
    DaggerfallActorLedger? actorLedger = null, Func<long, bool>? isWithParty = null,
    Func<DaggerfallSiteId, DaggerfallPopulationPlacement, long, int, bool>? isPlacementScheduledOutdoors = null,
    Func<DaggerfallSiteId, DaggerfallPopulationPlacement, int, bool>? isUnderworldNightSlot = null,
    Func<DaggerfallSiteId, long, DaggerfallSiteBuildingId?>? resolveShelterBuilding = null,
    Func<DaggerfallSiteId, string?, string, DaggerfallSiteBuildingId?>? resolveHomeBuilding = null,
    Func<WorldPoint, DaggerfallSiteBuildingId?, bool>? openResidentDoor = null)
{
    private readonly DaggerfallActorLedger _actorLedger = actorLedger ?? new DaggerfallActorLedger();
    private readonly Func<long, bool> _isWithParty = isWithParty ?? (_ => false);
    private readonly Func<DaggerfallSiteId, DaggerfallPopulationPlacement, long, int, bool>? _isPlacementScheduledOutdoors = isPlacementScheduledOutdoors;
    private readonly Func<DaggerfallSiteId, DaggerfallPopulationPlacement, int, bool>? _isUnderworldNightSlot = isUnderworldNightSlot;
    private readonly Func<DaggerfallSiteId, long, DaggerfallSiteBuildingId?>? _resolveShelterBuilding = resolveShelterBuilding;
    private readonly Func<DaggerfallSiteId, string?, string, DaggerfallSiteBuildingId?>? _resolveHomeBuilding = resolveHomeBuilding;
    private readonly Func<WorldPoint, DaggerfallSiteBuildingId?, bool>? _openResidentDoor = openResidentDoor;
    private const byte FemalePopulationFlag = 0x20;
    // PopulationManager's source pool scales by the number of exterior blocks, then clamps
    // the admitted pool to the donor's 24..96 people window.  The source list remains the
    // authority: this only bounds how many of its real placements can be admitted at once.
    private const int PopulationBlocksPerBand = 16;
    private const int PopulationBandSize = 24;
    private const int MinimumPopulationBands = 1;
    private const int MaximumPopulationBands = 4;
    // PopulationManager's source admission facts. The grid radius is converted through the
    // donor MeshReader scale once; it is a placement filter, not a second navigation system.
    internal const float NavGridSpawnRadiusMeters = 96F * 64F * (float)DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
    internal const float MaximumOutsideRangeMeters = 2500F * (float)DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
    internal const float VisiblePopulationRangeMeters = 120F;
    internal const float VisibleFrustumGraceRangeMeters = 25F;
    internal const float VisibleTransitionRangeMeters = 45F;
    internal const float VisibleFrustumCosThreshold = 0.25F;
    internal const float RetireDoorSearchRangeMeters = 60F;
    internal const float RetireDoorApproachMeters = 0.55F;
    internal const float RetireDoorArrivalMeters = 2.5F;
    internal const float DoorCrossingInsideDepthMeters = 0.85F;
    internal const float DoorCrossingThresholdMeters = 0.15F;
    internal const float DoorCrossingLateralMaxMeters = 1.1F;
    internal const float DoorCrossingArrivalMeters = 0.5F;
    internal const float DoorCrossingRetrySeconds = 2.0F;
    internal const int MaxDoorCrossingRetries = 2;
    internal const float EmergeStepOutMeters = 2.4F;
    internal const float EmergeArrivalMeters = 1.2F;
    internal const float EmergeTimeoutSeconds = 6F;
    internal const float RetireFallbackAwayMeters = 30F;
    internal const float RetireTimeoutSeconds = 45F;

    private readonly record struct RetireDoorPlan(
        WorldPoint ApproachLocal,
        WorldPoint DoorPlaneLocal,
        WorldPoint InsideDoorLocal,
        Vector3 OutwardNormal,
        DaggerfallSiteBuildingId? TargetBuilding,
        bool HasDoorTarget);

    private sealed class RetiringWalkerState(RetireDoorPlan plan)
    {
        internal WorldPoint GoalLocal { get; set; } = plan.ApproachLocal;
        internal WorldPoint DoorPlaneLocal { get; set; } = plan.DoorPlaneLocal;
        internal WorldPoint InsideDoorLocal { get; set; } = plan.InsideDoorLocal;
        internal Vector3 OutwardNormal { get; set; } = plan.OutwardNormal;
        internal DaggerfallSiteBuildingId? TargetBuilding { get; set; } = plan.TargetBuilding;
        internal bool HasDoorTarget { get; set; } = plan.HasDoorTarget;
        internal bool CrossingDoor { get; set; }
        internal float DoorCrossingSeconds { get; set; }
        internal int CrossingRetryCount { get; set; }
        internal float ElapsedSeconds { get; set; }

        internal void ApplyPlan(RetireDoorPlan plan)
        {
            GoalLocal = plan.ApproachLocal;
            DoorPlaneLocal = plan.DoorPlaneLocal;
            InsideDoorLocal = plan.InsideDoorLocal;
            OutwardNormal = plan.OutwardNormal;
            TargetBuilding = plan.TargetBuilding;
            HasDoorTarget = plan.HasDoorTarget;
            CrossingDoor = false;
            DoorCrossingSeconds = 0f;
            CrossingRetryCount = 0;
            ElapsedSeconds = 0f;
        }
    }

    private sealed class EmergingWalkerState(WorldPoint stepOutLocal, DaggerfallSiteBuildingId? sourceBuilding)
    {
        internal WorldPoint StepOutLocal { get; set; } = stepOutLocal;
        internal DaggerfallSiteBuildingId? SourceBuilding { get; } = sourceBuilding;
        internal float ElapsedSeconds { get; set; }
    }

    private readonly Dictionary<long, RetiringWalkerState> _retiringWalkers = [];
    private readonly Dictionary<long, EmergingWalkerState> _emergingWalkers = [];
    private readonly Dictionary<long, DaggerfallSiteBuildingId> _lastShelterBuildingByActor = [];

    /// <summary>Whether an active outdoor walker is currently walking toward or crossing a doorway to retire indoors.</summary>
    internal bool IsRetiring(long actorId) => _retiringWalkers.ContainsKey(actorId);

    /// <summary>Whether a retiring outdoor walker has opened the doorway and is actively stepping through the door plane.</summary>
    internal bool IsCrossingDoor(long actorId) =>
        _retiringWalkers.TryGetValue(actorId, out RetiringWalkerState? state) && state.CrossingDoor;

    /// <summary>Reads the active doorway plane and outward normal targeted by a retiring walker.</summary>
    internal bool TryGetRetiringDoorGeometry(long actorId, out WorldPoint doorPlaneLocal, out Vector3 outwardNormal)
    {
        if (_retiringWalkers.TryGetValue(actorId, out RetiringWalkerState? state) && state.HasDoorTarget)
        {
            doorPlaneLocal = state.DoorPlaneLocal;
            outwardNormal = state.OutwardNormal;
            return true;
        }
        doorPlaneLocal = default;
        outwardNormal = Vector3.Zero;
        return false;
    }

    /// <summary>Whether an active outdoor walker is currently stepping out of a building doorway onto the street.</summary>
    internal bool IsEmerging(long actorId) => _emergingWalkers.ContainsKey(actorId);

    /// <summary>Shifts transient staged retirement and emergence goals when the exterior local origin rebases.</summary>
    internal void RebaseTransient(Vector3 delta)
    {
        if (delta == Vector3.Zero)
            return;
        foreach ((long actorId, RetiringWalkerState state) in _retiringWalkers)
        {
            state.GoalLocal = WorldPoint.From(state.GoalLocal.ToVector() + delta);
            state.DoorPlaneLocal = WorldPoint.From(state.DoorPlaneLocal.ToVector() + delta);
            state.InsideDoorLocal = WorldPoint.From(state.InsideDoorLocal.ToVector() + delta);
            if (actors.TryGet(actorId, out ActorState? actor) && actor.Actor.Has<WanderMemoryComponent>())
                actor.Wander.Stage([state.GoalLocal]);
        }
        foreach ((long actorId, EmergingWalkerState state) in _emergingWalkers)
        {
            state.StepOutLocal = WorldPoint.From(state.StepOutLocal.ToVector() + delta);
            if (actors.TryGet(actorId, out ActorState? actor) && actor.Actor.Has<WanderMemoryComponent>())
                actor.Wander.Stage([state.StepOutLocal]);
        }
    }

    private WanderPolicy WanderPolicy => new(
        tuning.MovementSpeedUnitsPerSecond,
        tuning.WaypointDistance,
        tuning.IdleDurationSeconds,
        tuning.NavigationMaximumVisited,
        MaximumFailures: 2,
        NavigationMode: ActorNavigationMode.Ground,
        TurnRadiansPerSecond: float.DegreesToRadians(tuning.TurnDegreesPerSecond),
        EaseSeconds: tuning.EaseSeconds);
    /// <summary>The sites already said to have no race, so each is said once.</summary>
    private readonly HashSet<DaggerfallSiteId> raceless = [];

    /// <summary>Reconciles immediately without off-frustum live grace (scene load, arrival, rest, or fast-forward).</summary>
    internal void ReconcileImmediate() => Reconcile(immediate: true);

    /// <summary>
    /// Reconciles the active exterior's source population. Re-entry finds the same source key and
    /// therefore the same actor identity; it never creates a demo or test-only person to fill a gap.
    /// On live outdoor ticks (<paramref name="immediate"/> = <see langword="false"/>), already-visible
    /// walkers whose schedule flips indoors receive off-frustum / door-proximity live grace instead of popping out.
    /// </summary>
    internal void Reconcile(bool immediate = false)
    {
        DaggerfallSiteProfile active = profile();
        if (active.ProfileKind != DaggerfallWorldProfileKind.Exterior || active.Site is not { } siteId)
        {
            ClearTransientTransitions();
            return;
        }
        // An unloaded location keeps its population, hidden or not, in the site delta that holds
        // its actors; presence is reconciled again when the location is admitted.
        if (!locationLoaded())
        {
            ClearTransientTransitions();
            return;
        }
        // Where the location (and the player in it) lies in the world is known only once its terrain window is
        // admitted: an exterior arrival reconciles its population after that (DaggerfallSiteCrossing.TryTransitionTo).
        if (!residencyInitialized()) return;

        DaggerfallSiteRecord site = requireSite(siteId);
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty);
        DaggerfallNpc[] sourceNpcs = [.. npcs.All.Where(npc => DaggerfallCivilians.IsSourcePopulation(npc) && npc.Site == npcSite)];
        if (_isPlacementScheduledOutdoors is null && !isDay())
        {
            if (immediate)
            {
                ClearTransientTransitions();
                foreach (DaggerfallNpc npc in sourceNpcs)
                    Hide(npc);
            }
            else
            {
                foreach (DaggerfallNpc npc in sourceNpcs)
                    RetireOrHide(active, siteId, npc);
            }
            return;
        }

        string? race = Race(site);
        if (race is null)
        {
            ClearTransientTransitions();
            foreach (DaggerfallNpc npc in sourceNpcs) Hide(npc);
            return;
        }

        WorldPoint playerProfile = PlayerProfilePosition(active);
        if (!IsWithinSourceRange(active.Population, playerProfile))
        {
            ClearTransientTransitions();
            foreach (DaggerfallNpc npc in sourceNpcs)
                Hide(npc);
            return;
        }

        Dictionary<string, DaggerfallNpc> existingByKey = sourceNpcs.ToDictionary(npc => npc.StableKey, StringComparer.Ordinal);
        // The town's own walkers count towards its size and capacity; the guards a crackdown calls out walk on top.
        int totalPopulationCount = OrdinaryCount(active.Population);
        IReadOnlyList<DaggerfallPopulationPlacement> candidates = Admitted(site, active.Population,
            placement => IsEligiblePlacement(active, definitions, placement)
                && IsScheduledOutdoors(siteId, active.ProfileKey.LogicalId, placement, totalPopulationCount)
                && IsPlacementAdmitted(placement, existingByKey.GetValueOrDefault(placement.Id), playerProfile),
            // Preserve already active source identities through normal wandering. Stable source
            // order remains the tie-breaker for newly admitted people.
            walkers => walkers
                .OrderByDescending(placement => existingByKey.TryGetValue(placement.Id, out DaggerfallNpc? npc)
                    && npc.Presence == DaggerfallNpcPresence.Active)
                .ThenBy(placement => placement.Id, StringComparer.Ordinal));
        HashSet<string> admittedKeys = candidates.Select(placement => placement.Id).ToHashSet(StringComparer.Ordinal);
        if (immediate)
        {
            ClearTransientTransitions();
            foreach (DaggerfallNpc npc in sourceNpcs.Where(npc => !admittedKeys.Contains(npc.StableKey)))
                Hide(npc);
        }
        else
        {
            foreach (DaggerfallNpc npc in sourceNpcs)
            {
                if (admittedKeys.Contains(npc.StableKey))
                    CancelRetirement(npc.DurableId);
                else
                    RetireOrHide(active, siteId, npc);
            }
        }

        foreach (DaggerfallPopulationPlacement placement in candidates)
        {
            string slotKey = $"{active.ProfileKey.LogicalId}/{placement.Id}";
            long deterministicId = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
            if (_actorLedger.IsHomeSlotSuppressed(deterministicId, active.ProfileKey) || _isWithParty(deterministicId))
                continue;

            bool underworldNight = !placement.IsGuard
                && _isUnderworldNightSlot?.Invoke(siteId, placement, totalPopulationCount) == true;
            (DaggerfallNpcAppearance look, string role, IReadOnlyList<string> services) =
                ResolvePlacementIdentity(definitions, placement, race, underworldNight);

            DaggerfallNpc? existing = sourceNpcs.FirstOrDefault(npc =>
                npc.DurableId == deterministicId || StringComparer.Ordinal.Equals(npc.StableKey, placement.Id));
            if (existing is not null && (_actorLedger.IsHomeSlotSuppressed(existing.DurableId, active.ProfileKey) || _isWithParty(existing.DurableId)))
                continue;
            bool wasActiveOutdoors = existing?.Presence == DaggerfallNpcPresence.Active;
            if (suppressSpawns() && !wasActiveOutdoors) continue;
            long npcId = existing?.DurableId ?? deterministicId;
            if (existing is null)
            {
                npcs.RegisterDeterministic(deterministicId, DaggerfallNpcKind.Civilian, placement.Id, npcSite, look, role, services,
                    homeProfile: active.ProfileKey, familyId: placement.FamilyId);
            }
            else
            {
                npcs.RefreshPopulationFacts(npcId, look, role, services);
            }
            DaggerfallNpc npc = npcs.Require(npcId);
            if (npc.Presence == DaggerfallNpcPresence.Removed)
                continue;

            bool emergedFromDoor = false;
            WorldPoint emergeDoorLocal = default;
            WorldPoint emergeStepOutLocal = default;
            DaggerfallSiteBuildingId? emergeBuilding = null;
            if (!immediate
                && !wasActiveOutdoors
                && !placement.IsGuard
                && TryResolveEmergeDoorLocal(active, siteId, npcId, placement, out emergeDoorLocal, out emergeStepOutLocal, out emergeBuilding)
                && IsWithinVisibleTransitionRange(emergeDoorLocal))
            {
                emergedFromDoor = true;
            }

            // A live (or site-retained, already restored) actor owns the wandering pose. The authored
            // source position is only the initial placement of a person with no actor, and is never
            // reapplied to a live one (except when emerging through a doorway on a live morning/clear transition).
            if (actors.TryGet(npcId, out ActorState? existingActor))
            {
                if (npc.Profile != active.ProfileKey) npcs.Bind(npcId, active.ProfileKey);
                else npcs.SetPresence(npcId, DaggerfallNpcPresence.Active);
                if (emergedFromDoor)
                    existingActor.ApplyPose(new ActorPose(emergeDoorLocal, YawToward(emergeDoorLocal, emergeStepOutLocal)));
            }
            else if (emergedFromDoor)
            {
                npcs.Place(npcId, active.ProfileKey, WorldPoint.From(localToProfile(emergeDoorLocal.ToVector())));
            }
            else if (npc.Profile != active.ProfileKey || npc.X is null || npc.Y is null || npc.Z is null)
                npcs.Place(npcId, active.ProfileKey, placement.Position);
            else
                npcs.SetPresence(npcId, DaggerfallNpcPresence.Active);

            DaggerfallNpc current = npcs.Require(npcId);
            WorldPoint position = emergedFromDoor
                ? emergeDoorLocal
                : current.X is float x && current.Y is float y && current.Z is float z
                    ? profileToLocal(new WorldPoint(x, y, z))
                    : profileToLocal(placement.Position);
            float initialHeading = emergedFromDoor ? YawToward(emergeDoorLocal, emergeStepOutLocal) : 0F;
            if (DaggerfallPeopleSlots.IsDeterministicId(npcId))
                civilians.MaterializeDeterministic(npcId, ActorOrigin.Seeded(active.ProfileKey, placement.Id), placement.Position,
                    new ActorPose(position, initialHeading), profileToLocal(default).ToVector(), placement.FamilyId);
            else
                civilians.Materialize(npcId, new ActorPose(position, initialHeading));

            if (emergedFromDoor && actors.TryGet(npcId, out ActorState? emergingActor) && emergingActor.Actor.Has<WanderMemoryComponent>())
            {
                _ = _openResidentDoor?.Invoke(emergeDoorLocal, emergeBuilding);
                _emergingWalkers[npcId] = new EmergingWalkerState(emergeStepOutLocal, emergeBuilding);
                emergingActor.Wander.Stage([emergeStepOutLocal]);
            }
        }

        // A source civilian may have been hidden by night before a retained site delta was saved.
        // The actor is restored by the lifecycle first; this call restores only its visual admission.
        civilians.MaterializeAll(active.Project.PlayerPosition ?? new WorldPoint(0F, 0F, 0F));
        appearance().SyncRestoredDefeat(actors);
    }

    /// <summary>Whether <paramref name="actorId"/> is one of the active site's source civilians, which this population walks.</summary>
    internal bool Walks(long actorId) => npcs.All.Any(npc => npc.DurableId == actorId && DaggerfallCivilians.IsSourcePopulation(npc)
        && npcs.IsGameplayActive(actorId) && npc.Profile == activeProfile())
        && !IsPromotedCombatant(actorId);

    private bool IsPromotedCombatant(long actorId) =>
        actors.TryGet(actorId, out ActorState? actor)
        && actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm)
        && harm.IsCombatant;

    /// <summary>
    /// The classic townsperson art walker <paramref name="actorId"/> wears, as classic Daggerfall dresses a townsperson:
    /// one of its race's archives for its gender, chosen by its stable name seed; null for one its race gives none.
    /// </summary>
    internal int? Townsperson(long actorId) => npcs.TryGet(actorId, out DaggerfallNpc? npc)
        && definitions.Catalogs.TryGetRace(npc.Appearance.Race, out DaggerfallRaceDefinition race)
        ? race.Townsperson(npc.Appearance.Gender, npc.Appearance.NameSeed) : null;

    /// <summary>Walker <paramref name="actorId"/>'s stable name seed, saved with it; 0 for an actor that is no townsperson.</summary>
    internal int Seed(long actorId) => npcs.TryGet(actorId, out DaggerfallNpc? npc) ? npc.Appearance.NameSeed : 0;

    /// <summary>
    /// Advances only admitted source civilians. Enemy pursuit never owns this actor family; the
    /// wander coordinator records explicit idle, seeking, moving, blocked, unloaded, and dead
    /// states while Engine remains the movement authority.
    /// </summary>
    internal void Step(ulong simulationStep, float deltaSeconds)
    {
        HashSet<long> activeSource = npcs.All
            .Where(npc => DaggerfallCivilians.IsSourcePopulation(npc)
                && npcs.IsGameplayActive(npc.DurableId)
                && npc.Profile == activeProfile())
            .Select(npc => npc.DurableId)
            .ToHashSet();
        bool exteriorActive = profile().ProfileKind == DaggerfallWorldProfileKind.Exterior && locationLoaded();
        if (!exteriorActive)
            ClearTransientTransitions();
        bool admitted = profile().ProfileKind == DaggerfallWorldProfileKind.Exterior
            && (_isPlacementScheduledOutdoors is not null || isDay() || _retiringWalkers.Count > 0 || _emergingWalkers.Count > 0);
        foreach (ActorState actor in actors.All.Where(actor => actor.Actor.Has<WanderMemoryComponent>()))
        {
            if (actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm) && harm.IsCombatant)
            {
                CancelRetirement(actor.DurableId);
                CancelEmergence(actor.DurableId);
                actor.Wander.MarkUnloaded();
                continue;
            }
            bool recycled = false;
            DaggerfallNpc? populationNpc = npcs.All.FirstOrDefault(npc =>
                DaggerfallCivilians.IsSourcePopulation(npc) && npc.DurableId == actor.DurableId);
            if (populationNpc is not null
                && admitted
                && locationLoaded()
                && profile().Project.PlayerPosition is not null
                && player.Position is WorldPoint standing
                && actor.Position.HorizontalDistanceTo(standing) > tuning.RecycleDistanceMeters)
            {
                // Retire only the admitted appearance. The canonical actor, inventory, corpse,
                // and durable pose remain owned by the existing lifetime/save path for re-entry.
                CancelRetirement(actor.DurableId);
                CancelEmergence(actor.DurableId);
                Hide(populationNpc);
                recycled = true;
            }

            if (!admitted || recycled || !activeSource.Contains(actor.DurableId))
            {
                CancelRetirement(actor.DurableId);
                CancelEmergence(actor.DurableId);
                actor.Wander.MarkUnloaded();
                continue;
            }

            actor.Wander.MarkLoaded();
            if (held(actor.DurableId)) continue;
            wander(actor, WanderPolicy, simulationStep, deltaSeconds);
            if (actor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp)
                && !originComp.IsRelocated)
            {
                originComp.RecordIdleWanderPose(WorldPoint.From(localToProfile(actor.Position.ToVector())), actor.HeadingYawRadians);
            }

            if (populationNpc is not null && _retiringWalkers.TryGetValue(actor.DurableId, out RetiringWalkerState? retireState))
            {
                retireState.ElapsedSeconds += Math.Max(0f, deltaSeconds);
                bool outOfRange = !IsWithinVisibleTransitionRange(actor.Position);
                if (retireState.CrossingDoor)
                {
                    retireState.DoorCrossingSeconds += Math.Max(0f, deltaSeconds);
                    Vector3 offsetFromDoor = new(actor.Position.X - retireState.DoorPlaneLocal.X, 0f, actor.Position.Z - retireState.DoorPlaneLocal.Z);
                    float signedOutward = Vector3.Dot(offsetFromDoor, retireState.OutwardNormal);
                    Vector3 lateral = offsetFromDoor - (retireState.OutwardNormal * signedOutward);
                    bool crossedPlane = retireState.OutwardNormal.LengthSquared() > 1e-4f
                        && signedOutward <= -DoorCrossingThresholdMeters
                        && lateral.Length() <= DoorCrossingLateralMaxMeters;
                    bool reachedInside = actor.Position.HorizontalDistanceTo(retireState.InsideDoorLocal) <= DoorCrossingArrivalMeters;
                    if (crossedPlane || reachedInside || outOfRange)
                    {
                        _retiringWalkers.Remove(actor.DurableId);
                        actor.Wander.Unstage();
                        Hide(populationNpc);
                        actor.Wander.MarkUnloaded();
                    }
                    else if (retireState.DoorCrossingSeconds >= DoorCrossingRetrySeconds)
                    {
                        // Visible walker is blocked from crossing this doorway: keep them active instead of popping out,
                        // re-open/re-stage the inside point, or replan to an alternate reachable doorway after retries.
                        retireState.DoorCrossingSeconds = 0f;
                        retireState.CrossingRetryCount++;
                        if (retireState.CrossingRetryCount >= MaxDoorCrossingRetries
                            && TrySelectAlternateRetireDoorPlan(profile(), actor.Position, retireState.DoorPlaneLocal, retireState.TargetBuilding) is { } alternate)
                        {
                            retireState.ApplyPlan(alternate);
                            if (alternate.TargetBuilding is { } alternateBuilding)
                                _lastShelterBuildingByActor[actor.DurableId] = alternateBuilding;
                            actor.Wander.Stage([alternate.ApproachLocal]);
                        }
                        else
                        {
                            _ = _openResidentDoor?.Invoke(retireState.DoorPlaneLocal, retireState.TargetBuilding);
                            actor.Wander.Stage([retireState.InsideDoorLocal]);
                        }
                    }
                    continue;
                }

                bool reachedApproach = actor.Position.HorizontalDistanceTo(retireState.GoalLocal) <= RetireDoorApproachMeters;
                if (retireState.HasDoorTarget && reachedApproach)
                {
                    _ = _openResidentDoor?.Invoke(retireState.DoorPlaneLocal, retireState.TargetBuilding);
                    retireState.CrossingDoor = true;
                    retireState.DoorCrossingSeconds = 0f;
                    retireState.GoalLocal = retireState.InsideDoorLocal;
                    actor.Wander.Stage([retireState.InsideDoorLocal]);
                    continue;
                }
                if (outOfRange || (!retireState.HasDoorTarget && reachedApproach))
                {
                    _retiringWalkers.Remove(actor.DurableId);
                    actor.Wander.Unstage();
                    Hide(populationNpc);
                    actor.Wander.MarkUnloaded();
                }
                else if (retireState.ElapsedSeconds >= RetireTimeoutSeconds)
                {
                    // Visible walker has not reached a doorway yet: replan instead of disappearing in plain sight.
                    retireState.ElapsedSeconds = 0f;
                    if (TrySelectAlternateRetireDoorPlan(profile(), actor.Position, retireState.DoorPlaneLocal, retireState.TargetBuilding) is { } alternate)
                    {
                        retireState.ApplyPlan(alternate);
                        if (alternate.TargetBuilding is { } alternateBuilding)
                            _lastShelterBuildingByActor[actor.DurableId] = alternateBuilding;
                        actor.Wander.Stage([alternate.ApproachLocal]);
                    }
                    else
                    {
                        actor.Wander.Stage([retireState.GoalLocal]);
                    }
                }
            }
            else if (_emergingWalkers.TryGetValue(actor.DurableId, out EmergingWalkerState? emergeState))
            {
                emergeState.ElapsedSeconds += Math.Max(0f, deltaSeconds);
                bool reachedStreet = actor.Position.HorizontalDistanceTo(emergeState.StepOutLocal) <= EmergeArrivalMeters;
                bool timedOut = emergeState.ElapsedSeconds >= EmergeTimeoutSeconds;
                if (reachedStreet || timedOut)
                {
                    _emergingWalkers.Remove(actor.DurableId);
                    actor.Wander.Unstage();
                }
            }
        }
    }

    private void RetireOrHide(DaggerfallSiteProfile active, DaggerfallSiteId siteId, DaggerfallNpc npc)
    {
        if (npc.Presence != DaggerfallNpcPresence.Active
            || _isWithParty(npc.DurableId)
            || IsPromotedCombatant(npc.DurableId)
            || !actors.TryGet(npc.DurableId, out ActorState? actor)
            || !actor.Actor.Has<WanderMemoryComponent>())
        {
            _retiringWalkers.Remove(npc.DurableId);
            CancelEmergence(npc.DurableId);
            Hide(npc);
            return;
        }

        CancelEmergence(npc.DurableId);
        if (_retiringWalkers.ContainsKey(npc.DurableId))
            return;

        if (!IsWithinVisibleTransitionRange(actor.Position))
        {
            if (_resolveShelterBuilding?.Invoke(siteId, npc.DurableId) is { } assignedOffscreen)
                _lastShelterBuildingByActor[npc.DurableId] = assignedOffscreen;
            Hide(npc);
            return;
        }

        RetireDoorPlan plan = SelectRetireDoorPlan(active, siteId, npc.DurableId, actor.Position);
        if (plan.TargetBuilding is { } chosenBuilding)
            _lastShelterBuildingByActor[npc.DurableId] = chosenBuilding;
        _retiringWalkers[npc.DurableId] = new RetiringWalkerState(plan);
        actor.Wander.Stage([plan.ApproachLocal]);
    }

    private void CancelRetirement(long actorId)
    {
        if (_retiringWalkers.Remove(actorId)
            && actors.TryGet(actorId, out ActorState? actor)
            && actor.Actor.Has<WanderMemoryComponent>())
        {
            actor.Wander.Unstage();
        }
    }

    private void CancelEmergence(long actorId)
    {
        if (_emergingWalkers.Remove(actorId)
            && actors.TryGet(actorId, out ActorState? actor)
            && actor.Actor.Has<WanderMemoryComponent>())
        {
            actor.Wander.Unstage();
        }
    }

    private void ClearTransientTransitions()
    {
        if (_retiringWalkers.Count > 0)
        {
            foreach (long actorId in _retiringWalkers.Keys.ToArray())
                CancelRetirement(actorId);
        }
        if (_emergingWalkers.Count > 0)
        {
            foreach (long actorId in _emergingWalkers.Keys.ToArray())
                CancelEmergence(actorId);
        }
    }

    /// <summary>Whether an actor is close enough to the player that schedule transitions should play out visibly through doors.</summary>
    internal bool IsWithinVisibleTransitionRange(WorldPoint actorPosition)
    {
        if (player.Position is not WorldPoint playerPos)
            return false;
        float dist = actorPosition.HorizontalDistanceTo(playerPos);
        return dist <= VisibleTransitionRangeMeters || IsWithinPlayerCameraFrustum(actorPosition);
    }

    internal bool IsWithinPlayerCameraFrustum(WorldPoint actorPosition)
    {
        if (player.Position is not WorldPoint playerPos)
            return false;
        Vector3 toActor = new(actorPosition.X - playerPos.X, 0f, actorPosition.Z - playerPos.Z);
        float distSq = toActor.LengthSquared();
        if (distSq > VisibleFrustumGraceRangeMeters * VisibleFrustumGraceRangeMeters)
            return false;
        if (distSq < 1e-4f)
            return true;
        Vector3 forward = ActorHeading.Forward(player.YawRadians);
        float dot = Vector3.Dot(toActor / MathF.Sqrt(distSq), forward);
        return dot >= VisibleFrustumCosThreshold;
    }

    private RetireDoorPlan SelectRetireDoorPlan(
        DaggerfallSiteProfile active,
        DaggerfallSiteId siteId,
        long actorId,
        WorldPoint actorLocal)
    {
        WorldPoint? playerPos = player.Position;

        // 1. Prefer the exact exterior building doorway assigned by #11104 DaggerfallScheduleShelterResolver.
        DaggerfallSiteBuildingId? assigned = _resolveShelterBuilding?.Invoke(siteId, actorId);
        if (assigned is { } assignedBuilding
            && TryFindDoorForBuilding(active, siteId, assignedBuilding, actorLocal, out WorldPoint assignedApproachLocal, out WorldPoint assignedPlaneLocal, out Vector3 assignedOutward)
            && actorLocal.HorizontalDistanceTo(assignedApproachLocal) <= RetireDoorSearchRangeMeters)
        {
            return BuildDoorPlan(assignedApproachLocal, assignedPlaneLocal, assignedOutward, actorLocal, assignedBuilding);
        }

        // 2. Next prefer the nearest exterior building entrance door in active.Doors.
        RetireDoorPlan? bestPlan = null;
        float bestDoorDist = float.MaxValue;
        foreach (DaggerfallRdbDoorDefinition door in active.Doors)
        {
            if (door.ExteriorBuilding is not { } buildingId || door.Kind == DaggerfallDoorKind.Special)
                continue;
            (WorldPoint approachLocal, WorldPoint planeLocal, Vector3 outward) = ExteriorDoorGeometryLocal(door);
            float actorDist = actorLocal.HorizontalDistanceTo(approachLocal);
            if (actorDist > RetireDoorSearchRangeMeters)
                continue;
            if (playerPos is WorldPoint p)
            {
                float playerToActor = actorLocal.HorizontalDistanceTo(p);
                float playerToDoor = approachLocal.HorizontalDistanceTo(p);
                if (playerToDoor + 1f < playerToActor && playerToDoor < 3.5f)
                    continue;
            }
            if (actorDist < bestDoorDist)
            {
                bestDoorDist = actorDist;
                bestPlan = BuildDoorPlan(approachLocal, planeLocal, outward, actorLocal, buildingId);
            }
        }

        // 3. Also consider explicit doorway portals (used by synthetic profiles and transition portals).
        foreach (DaggerfallSitePortal portal in active.Portals)
        {
            WorldPoint planeLocal = profileToLocal(portal.Position);
            float actorDist = actorLocal.HorizontalDistanceTo(planeLocal);
            if (actorDist > RetireDoorSearchRangeMeters)
                continue;
            if (playerPos is WorldPoint p)
            {
                float playerToActor = actorLocal.HorizontalDistanceTo(p);
                float playerToDoor = planeLocal.HorizontalDistanceTo(p);
                if (playerToDoor + 1f < playerToActor && playerToDoor < 3.5f)
                    continue;
            }
            if (actorDist < bestDoorDist)
            {
                bestDoorDist = actorDist;
                Vector3 outward = portal.Normal is Vector3 n ? HorizontalNormal(n) : Vector3.Zero;
                bestPlan = BuildDoorPlan(planeLocal, planeLocal, outward, actorLocal, TryParsePortalBuilding(portal));
            }
        }

        if (bestPlan is { } chosenPlan)
            return chosenPlan;

        // 4. Fallback: move away from the player until clear of visible transition range.
        Vector3 away = playerPos is WorldPoint standing
            ? new Vector3(actorLocal.X - standing.X, 0f, actorLocal.Z - standing.Z)
            : ActorHeading.Forward(player.YawRadians);
        if (away.LengthSquared() < 1e-4f)
        {
            Vector3 forward = ActorHeading.Forward(player.YawRadians);
            away = new Vector3(-forward.Z, 0f, forward.X);
        }
        away = Vector3.Normalize(away);
        WorldPoint fallback = new(
            actorLocal.X + away.X * RetireFallbackAwayMeters,
            actorLocal.Y,
            actorLocal.Z + away.Z * RetireFallbackAwayMeters);
        return new RetireDoorPlan(fallback, fallback, fallback, Vector3.Zero, TargetBuilding: null, HasDoorTarget: false);
    }

    private RetireDoorPlan? TrySelectAlternateRetireDoorPlan(
        DaggerfallSiteProfile active,
        WorldPoint actorLocal,
        WorldPoint blockedDoorPlaneLocal,
        DaggerfallSiteBuildingId? blockedBuilding)
    {
        RetireDoorPlan? bestPlan = null;
        float bestDoorDist = float.MaxValue;
        foreach (DaggerfallRdbDoorDefinition door in active.Doors)
        {
            if (door.ExteriorBuilding is not { } buildingId || door.Kind == DaggerfallDoorKind.Special)
                continue;
            if (blockedBuilding is { } blocked && buildingId == blocked)
                continue;
            (WorldPoint approachLocal, WorldPoint planeLocal, Vector3 outward) = ExteriorDoorGeometryLocal(door);
            if (planeLocal.HorizontalDistanceTo(blockedDoorPlaneLocal) < 1.5f)
                continue;
            float actorDist = actorLocal.HorizontalDistanceTo(approachLocal);
            if (actorDist > RetireDoorSearchRangeMeters)
                continue;
            if (actorDist < bestDoorDist)
            {
                bestDoorDist = actorDist;
                bestPlan = BuildDoorPlan(approachLocal, planeLocal, outward, actorLocal, buildingId);
            }
        }

        foreach (DaggerfallSitePortal portal in active.Portals)
        {
            WorldPoint planeLocal = profileToLocal(portal.Position);
            if (planeLocal.HorizontalDistanceTo(blockedDoorPlaneLocal) < 1.5f)
                continue;
            DaggerfallSiteBuildingId? buildingId = TryParsePortalBuilding(portal);
            if (blockedBuilding is { } blocked && buildingId is { } candidate && candidate == blocked)
                continue;
            float actorDist = actorLocal.HorizontalDistanceTo(planeLocal);
            if (actorDist > RetireDoorSearchRangeMeters)
                continue;
            if (actorDist < bestDoorDist)
            {
                bestDoorDist = actorDist;
                Vector3 outward = portal.Normal is Vector3 n ? HorizontalNormal(n) : Vector3.Zero;
                bestPlan = BuildDoorPlan(planeLocal, planeLocal, outward, actorLocal, buildingId);
            }
        }

        return bestPlan;
    }

    private static RetireDoorPlan BuildDoorPlan(
        WorldPoint approachLocal,
        WorldPoint doorPlaneLocal,
        Vector3 outwardNormal,
        WorldPoint actorLocal,
        DaggerfallSiteBuildingId? building)
    {
        Vector3 outward = outwardNormal.LengthSquared() > 1e-4f
            ? Vector3.Normalize(outwardNormal)
            : HorizontalNormal(new Vector3(actorLocal.X - doorPlaneLocal.X, 0f, actorLocal.Z - doorPlaneLocal.Z));
        WorldPoint insideLocal = outward.LengthSquared() > 1e-4f
            ? new WorldPoint(
                doorPlaneLocal.X - (outward.X * DoorCrossingInsideDepthMeters),
                doorPlaneLocal.Y,
                doorPlaneLocal.Z - (outward.Z * DoorCrossingInsideDepthMeters))
            : doorPlaneLocal;
        return new RetireDoorPlan(approachLocal, doorPlaneLocal, insideLocal, outward, building, HasDoorTarget: true);
    }

    private bool TryResolveEmergeDoorLocal(
        DaggerfallSiteProfile active,
        DaggerfallSiteId siteId,
        long actorId,
        DaggerfallPopulationPlacement placement,
        out WorldPoint doorLocal,
        out WorldPoint stepOutLocal,
        out DaggerfallSiteBuildingId? buildingId)
    {
        doorLocal = default;
        stepOutLocal = default;
        buildingId = null;

        WorldPoint streetAnchorLocal = profileToLocal(placement.Position);

        // 1. Building this walker last sheltered in, or their deterministic #11104 family residence.
        DaggerfallSiteBuildingId? preferredBuilding = _lastShelterBuildingByActor.TryGetValue(actorId, out DaggerfallSiteBuildingId lastBuilding)
            ? lastBuilding
            : _resolveHomeBuilding?.Invoke(siteId, placement.FamilyId, placement.Id);

        if (preferredBuilding is { } target
            && TryFindDoorForBuilding(active, siteId, target, streetAnchorLocal, out WorldPoint foundDoor, out _, out Vector3 foundNormal))
        {
            doorLocal = foundDoor;
            stepOutLocal = ComputeStepOut(foundDoor, foundNormal, streetAnchorLocal);
            buildingId = target;
            return true;
        }

        // 2. Fallback: nearest exterior building door or doorway portal to the walker's street placement.
        WorldPoint? bestDoor = null;
        Vector3 bestNormal = Vector3.Zero;
        DaggerfallSiteBuildingId? bestBuilding = null;
        float bestDist = float.MaxValue;

        foreach (DaggerfallRdbDoorDefinition door in active.Doors)
        {
            if (door.ExteriorBuilding is not { } bId || door.Kind == DaggerfallDoorKind.Special)
                continue;
            WorldPoint threshold = ExteriorDoorThresholdLocal(door);
            float dist = streetAnchorLocal.HorizontalDistanceTo(threshold);
            if (dist <= RetireDoorSearchRangeMeters && dist < bestDist)
            {
                bestDist = dist;
                bestDoor = threshold;
                bestNormal = HorizontalNormal(Vector3.Transform(Vector3.UnitZ, door.Rotation));
                bestBuilding = bId;
            }
        }

        foreach (DaggerfallSitePortal portal in active.Portals)
        {
            WorldPoint threshold = profileToLocal(portal.Position);
            float dist = streetAnchorLocal.HorizontalDistanceTo(threshold);
            if (dist <= RetireDoorSearchRangeMeters && dist < bestDist)
            {
                bestDist = dist;
                bestDoor = threshold;
                bestNormal = portal.Normal is Vector3 n ? HorizontalNormal(n) : Vector3.Zero;
                bestBuilding = TryParsePortalBuilding(portal);
            }
        }

        if (bestDoor is not WorldPoint chosen)
            return false;

        doorLocal = chosen;
        stepOutLocal = ComputeStepOut(chosen, bestNormal, streetAnchorLocal);
        buildingId = bestBuilding;
        return true;
    }

    private bool TryFindDoorForBuilding(
        DaggerfallSiteProfile active,
        DaggerfallSiteId siteId,
        DaggerfallSiteBuildingId building,
        WorldPoint referenceLocal,
        out WorldPoint approachLocal,
        out WorldPoint doorPlaneLocal,
        out Vector3 outwardNormal)
    {
        approachLocal = default;
        doorPlaneLocal = default;
        outwardNormal = Vector3.Zero;
        float bestDist = float.MaxValue;
        bool found = false;

        foreach (DaggerfallRdbDoorDefinition door in active.Doors)
        {
            if (door.ExteriorBuilding != building || door.Kind == DaggerfallDoorKind.Special)
                continue;
            (WorldPoint threshold, WorldPoint plane, Vector3 normal) = ExteriorDoorGeometryLocal(door);
            float dist = referenceLocal.HorizontalDistanceTo(threshold);
            if (!found || dist < bestDist)
            {
                found = true;
                bestDist = dist;
                approachLocal = threshold;
                doorPlaneLocal = plane;
                outwardNormal = normal;
            }
        }

        if (found)
            return true;

        string targetProfileId = DaggerfallWorldProfileIds.Interior(siteId, building).LogicalId;
        foreach (DaggerfallSitePortal portal in active.Portals)
        {
            if (!StringComparer.Ordinal.Equals(portal.DestinationLogicalProfile, targetProfileId))
                continue;
            WorldPoint plane = profileToLocal(portal.Position);
            float dist = referenceLocal.HorizontalDistanceTo(plane);
            if (!found || dist < bestDist)
            {
                found = true;
                bestDist = dist;
                approachLocal = plane;
                doorPlaneLocal = plane;
                outwardNormal = portal.Normal is Vector3 n ? HorizontalNormal(n) : Vector3.Zero;
            }
        }

        return found;
    }

    private (WorldPoint ApproachLocal, WorldPoint DoorPlaneLocal, Vector3 OutwardNormal) ExteriorDoorGeometryLocal(DaggerfallRdbDoorDefinition door)
    {
        Vector3 normal = HorizontalNormal(Vector3.Transform(Vector3.UnitZ, door.Rotation));
        Vector3 planeProfile = door.Position + new Vector3(0f, door.BoundsMin.Y, 0f);
        Vector3 thresholdProfile = planeProfile + (normal * 0.75f);
        return (profileToLocal(WorldPoint.From(thresholdProfile)), profileToLocal(WorldPoint.From(planeProfile)), normal);
    }

    private WorldPoint ExteriorDoorThresholdLocal(DaggerfallRdbDoorDefinition door) =>
        ExteriorDoorGeometryLocal(door).ApproachLocal;

    private static WorldPoint ComputeStepOut(WorldPoint doorLocal, Vector3 outwardNormal, WorldPoint streetAnchorLocal)
    {
        Vector3 dir = outwardNormal.LengthSquared() > 1e-4f
            ? Vector3.Normalize(outwardNormal)
            : HorizontalNormal(new Vector3(streetAnchorLocal.X - doorLocal.X, 0f, streetAnchorLocal.Z - doorLocal.Z));
        if (dir.LengthSquared() < 1e-4f)
            dir = Vector3.UnitZ;
        return new WorldPoint(
            doorLocal.X + (dir.X * EmergeStepOutMeters),
            doorLocal.Y,
            doorLocal.Z + (dir.Z * EmergeStepOutMeters));
    }

    private static Vector3 HorizontalNormal(Vector3 value)
    {
        Vector3 flat = new(value.X, 0f, value.Z);
        return flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.Zero;
    }

    private static float YawToward(WorldPoint from, WorldPoint toward)
    {
        Vector3 delta = new(toward.X - from.X, 0f, toward.Z - from.Z);
        return delta.LengthSquared() > 1e-4f ? ActorHeading.Yaw(delta) : 0f;
    }

    private static DaggerfallSiteBuildingId? TryParsePortalBuilding(DaggerfallSitePortal portal) =>
        DaggerfallWorldProfileIds.TryParse(portal.DestinationLogicalProfile, out _, out DaggerfallSiteBuildingId? building)
            ? building
            : null;

    private bool IsScheduledOutdoors(DaggerfallSiteId siteId, string logicalProfileId, DaggerfallPopulationPlacement placement, int totalPopulationCount)
    {
        if (_isPlacementScheduledOutdoors is null)
            return isDay();
        long deterministicId = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{logicalProfileId}/{placement.Id}");
        return _isPlacementScheduledOutdoors(siteId, placement, deterministicId, totalPopulationCount);
    }

    private void Hide(DaggerfallNpc npc)
    {
        if (npc.Presence == DaggerfallNpcPresence.Removed || _isWithParty(npc.DurableId) || IsPromotedCombatant(npc.DurableId)
            || (_actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? ledgerEntry) && ledgerEntry?.Location?.Kind == ActorLocationKind.WithParty))
            return;
        npcs.SetPresence(npc.DurableId, DaggerfallNpcPresence.Hidden);
        DaggerfallSiteAppearance shown = appearance();
        if (shown.HasActor(npc.DurableId)) shown.RetireActor(npc.DurableId);
    }

    private WorldPoint PlayerProfilePosition(DaggerfallSiteProfile active)
    {
        if (player.Position is WorldPoint local)
            return WorldPoint.From(localToProfile(local.ToVector()));
        return active.Project.PlayerPosition ?? new WorldPoint(0F, 0F, 0F);
    }

    private static bool IsWithinSourceRange(IReadOnlyList<DaggerfallPopulationPlacement> placements, WorldPoint player)
    {
        if (placements.Count == 0) return false;
        float minX = placements.Min(placement => placement.Position.X) - MaximumOutsideRangeMeters;
        float maxX = placements.Max(placement => placement.Position.X) + MaximumOutsideRangeMeters;
        float minZ = placements.Min(placement => placement.Position.Z) - MaximumOutsideRangeMeters;
        float maxZ = placements.Max(placement => placement.Position.Z) + MaximumOutsideRangeMeters;
        return player.X >= minX && player.X <= maxX && player.Z >= minZ && player.Z <= maxZ;
    }

    private bool IsPlacementAdmitted(DaggerfallPopulationPlacement placement, DaggerfallNpc? existing,
        WorldPoint playerProfile)
    {
        WorldPoint placementLocal = profileToLocal(placement.Position);
        WorldPoint playerLocal = profileToLocal(playerProfile);
        float distance = playerLocal.HorizontalDistanceTo(placementLocal);
        if (distance > NavGridSpawnRadiusMeters) return false;
        if (existing is { Presence: DaggerfallNpcPresence.Active })
        {
            if (actors.TryGet(existing.DurableId, out ActorState? actor))
                distance = actor.Position.HorizontalDistanceTo(playerLocal);
            return distance <= tuning.RecycleDistanceMeters;
        }

        // Visible population is allowed to change inside the donor's 120m hysteresis range;
        // the larger grid radius only bounds which source pool entries can be considered.
        return distance <= VisiblePopulationRangeMeters;
    }

    /// <summary>
    /// Resolves the race <paramref name="site"/>'s townsfolk are from the climate cell the site lies in.
    /// Returns <see langword="null"/> when the climate names no race or a race the catalog lacks.
    /// </summary>
    internal static string? TryResolveClimateRace(DaggerfallDefinitions definitions, DaggerfallSiteRecord site, out DaggerfallClimateCell climate)
    {
        climate = definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY);
        string people = climate.People;
        return definitions.Catalogs.Races.FirstOrDefault(race => StringComparer.OrdinalIgnoreCase.Equals(race.Id, people))?.Id;
    }

    /// <summary>Whether <paramref name="placement"/> carries a resolvable billboard and faction for population admission.</summary>
    internal static bool IsEligiblePlacement(DaggerfallSiteProfile profile, DaggerfallDefinitions definitions, DaggerfallPopulationPlacement placement) =>
        profile.BillboardSprites.ContainsKey((placement.BillboardArchive, placement.BillboardRecord))
        && (placement.FactionId == 0 || definitions.Factions.Factions.ContainsKey(placement.FactionId));

    /// <summary>Builds the canonical appearance, role, and service facts for an eligible source population placement.</summary>
    internal static (DaggerfallNpcAppearance Appearance, string Role, IReadOnlyList<string> Services) ResolvePlacementIdentity(
        DaggerfallDefinitions definitions, DaggerfallPopulationPlacement placement, string race, bool underworldNight = false)
    {
        DaggerfallFactionDefinition? faction = placement.FactionId == 0
            ? null
            : definitions.Factions.Factions.GetValueOrDefault(placement.FactionId);
        DaggerfallNpcAppearance look = new(race, (placement.Flags & FemalePopulationFlag) != 0 ? "Female" : "Male",
            placement.BillboardArchive, placement.BillboardRecord, placement.NameSeed, placement.FactionId);
        (string role, IReadOnlyList<string> services) = DaggerfallNpcServiceFacts.Resolve(
            definitions, faction, placement.SourceBuildingType, placement.SourceBuildingFactionId,
            placement.IsGuard ? "guard" : underworldNight ? "underworld" : "civilian");
        return (look, role, services);
    }

    /// <summary>
    /// The race <paramref name="site"/>'s townsfolk are, as the donor's PopulationManager takes it: the People of the
    /// climate cell the site lies in. A site whose climate names none, or a race the catalog lacks, has no townsfolk,
    /// said once; no race is made up for it.
    /// </summary>
    private string? Race(DaggerfallSiteRecord site)
    {
        if (TryResolveClimateRace(definitions, site, out DaggerfallClimateCell climate) is { } race)
            return race;
        if (raceless.Add(site.Id))
            report($"{site.Name} ({site.Id}) has no townsfolk: its climate {climate.Value} ({climate.Name}) names " +
                (string.IsNullOrWhiteSpace(climate.People) ? "no race for them." : $"the race '{climate.People}', which the race catalog lacks."));
        return null;
    }

    /// <summary>Applies the donor's bounded active population pool to real normalized placements.</summary>
    /// <summary>
    /// A town's own walkers, which count towards its size, schedules and capacity; the reserve watch a crackdown calls
    /// out (<see cref="DaggerfallPopulationPlacement.CrackdownGuard"/>) walks on top of them.
    /// </summary>
    internal static int OrdinaryCount(IReadOnlyList<DaggerfallPopulationPlacement> population) =>
        population.Count(placement => placement.CrackdownGuard == 0);

    /// <summary>
    /// Who of <paramref name="population"/> walks <paramref name="site"/>'s streets now, in the active town or a resident
    /// one: of those <paramref name="walks"/> lets out, the town's own walkers in <paramref name="order"/> up to its
    /// capacity, and on top of them every reserve guard a crackdown calls out.
    /// </summary>
    internal static IReadOnlyList<DaggerfallPopulationPlacement> Admitted(DaggerfallSiteRecord site,
        IReadOnlyList<DaggerfallPopulationPlacement> population, Func<DaggerfallPopulationPlacement, bool> walks,
        Func<IEnumerable<DaggerfallPopulationPlacement>, IEnumerable<DaggerfallPopulationPlacement>> order) =>
        [.. order(population.Where(placement => placement.CrackdownGuard == 0 && walks(placement))).Take(Capacity(site, OrdinaryCount(population))),
            .. population.Where(placement => placement.CrackdownGuard > 0 && walks(placement))];

    internal static int Capacity(DaggerfallSiteRecord site, int sourceCount)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (sourceCount <= 0) return 0;
        int totalBlocks = site.Exterior?.Blocks.Count ?? 0;
        if (totalBlocks <= 0) return sourceCount;
        int bands = Math.Clamp(totalBlocks / PopulationBlocksPerBand, MinimumPopulationBands, MaximumPopulationBands);
        return Math.Min(sourceCount, checked(bands * PopulationBandSize));
    }

    /// <summary>Maps the source faction's filed type to the role consumers use for crime and dialogue.</summary>
    internal static string Role(DaggerfallFactionDefinition? faction) =>
        faction?.Type == 10 || StringComparer.Ordinal.Equals(faction?.TypeName, "KnightlyGuard")
            ? "guard"
            : "civilian";
}
