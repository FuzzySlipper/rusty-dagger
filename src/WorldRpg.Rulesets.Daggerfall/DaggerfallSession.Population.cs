using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Admits source-backed civilian placements for the active exterior. The importer owns the
/// placement, billboard, faction and climate facts; this owner only selects the current time slice,
/// binds one durable NPC identity, and routes it through the existing actor/lifetime owner.
/// </summary>
internal sealed partial class DaggerfallSession
{
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
    internal const float PopulationNavGridSpawnRadiusMeters = 96F * 64F * (float)DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
    internal const float PopulationMaximumOutsideRangeMeters = 2500F * (float)DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
    internal const float PopulationVisiblePopulationRangeMeters = 120F;
    private WanderPolicy PopulationWanderPolicy => new(
        _tuning.CivilianWander.MovementSpeedUnitsPerSecond,
        _tuning.CivilianWander.WaypointDistance,
        _tuning.CivilianWander.IdleDurationSeconds,
        _tuning.CivilianWander.NavigationMaximumVisited,
        MaximumFailures: 2,
        NavigationMode: ActorNavigationMode.Ground);

    /// <summary>
    /// Reconciles the active exterior's source population. Re-entry finds the same source key and
    /// therefore the same actor identity; it never creates a demo or test-only person to fill a gap.
    /// </summary>
    internal void ReconcileCivilianPopulation()
    {
        DaggerfallSiteProfile profile = _sites.Projection.Inputs;
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Exterior || profile.Site is not { } siteId)
            return;
        // An unloaded location keeps its population, hidden or not, in the site delta that holds
        // its actors; presence is reconciled again when the location is admitted.
        if (!_sites.ActiveLocationLoaded) return;
        // Where the location (and the player in it) lies in the world is known only once its terrain window is
        // admitted: an exterior arrival reconciles its population after that (DaggerfallSiteLifecycle.TryTransitionTo).
        if (!_sites.ExteriorResidencyInitialized) return;

        DaggerfallSiteRecord site = _site.Require(siteId);
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty);
        DaggerfallNpc[] sourceNpcs = [.. State.Npcs.All.Where(npc => IsPopulationNpc(npc) && npc.Site == npcSite)];
        if (!_time.Calendar.IsDay)
        {
            foreach (DaggerfallNpc npc in sourceNpcs)
                HidePopulationNpc(npc);
            return;
        }

        string? race = ResolvePopulationRace(site, profile.Population);
        if (race is null)
        {
            foreach (DaggerfallNpc npc in sourceNpcs) HidePopulationNpc(npc);
            return;
        }

        WorldPoint playerProfile = PopulationPlayerProfilePosition(profile);
        if (!IsWithinPopulationSourceRange(profile.Population, playerProfile))
        {
            foreach (DaggerfallNpc npc in sourceNpcs)
                HidePopulationNpc(npc);
            return;
        }

        Dictionary<string, DaggerfallNpc> existingByKey = sourceNpcs.ToDictionary(npc => npc.StableKey, StringComparer.Ordinal);
        DaggerfallPopulationPlacement[] candidates = [.. profile.Population
            .Where(placement => profile.BillboardSprites.ContainsKey((placement.BillboardArchive, placement.BillboardRecord))
                && (placement.FactionId == 0 || _definitions.Factions.Factions.ContainsKey(placement.FactionId)))
            .Where(placement => IsPopulationPlacementAdmitted(placement, existingByKey.GetValueOrDefault(placement.Id), playerProfile))
            // Preserve already active source identities through normal wandering. Stable source
            // order remains the tie-breaker for newly admitted people.
            .OrderByDescending(placement => existingByKey.TryGetValue(placement.Id, out DaggerfallNpc? npc)
                && npc.Presence == DaggerfallNpcPresence.Active)
            .ThenBy(placement => placement.Id, StringComparer.Ordinal)
            .Take(PopulationCapacity(site, profile.Population.Count))];
        HashSet<string> admittedKeys = candidates.Select(placement => placement.Id).ToHashSet(StringComparer.Ordinal);
        foreach (DaggerfallNpc npc in sourceNpcs.Where(npc => !admittedKeys.Contains(npc.StableKey)))
            HidePopulationNpc(npc);

        foreach (DaggerfallPopulationPlacement placement in candidates)
        {
            DaggerfallFactionDefinition? faction = placement.FactionId == 0
                ? null
                : _definitions.Factions.Factions[placement.FactionId];
            DaggerfallNpcAppearance appearance = new(race, (placement.Flags & FemalePopulationFlag) != 0 ? "Female" : "Male",
                placement.BillboardArchive, placement.BillboardRecord, placement.NameSeed, placement.FactionId);
            (string role, IReadOnlyList<string> services) = DaggerfallNpcServiceFacts.Resolve(
                _definitions, faction, placement.SourceBuildingType, placement.SourceBuildingFactionId, "civilian");

            DaggerfallNpc? existing = sourceNpcs.FirstOrDefault(npc =>
                StringComparer.Ordinal.Equals(npc.StableKey, placement.Id));
            if (State.RacialOverrides.Current?.SuppressPopulationSpawns == true
                && existing?.Presence != DaggerfallNpcPresence.Active) continue;
            long npcId = existing?.DurableId ?? State.Npcs.RegisterPopulationCivilian(
                npcSite,
                placement.Id,
                appearance,
                role,
                services);
            if (existing is not null)
                State.Npcs.RefreshPopulationFacts(npcId, appearance, role, services);
            DaggerfallNpc npc = State.Npcs.Require(npcId);
            if (npc.Presence == DaggerfallNpcPresence.Removed)
                continue;

            // A live (or site-retained, already restored) actor owns the wandering pose. The authored
            // source position is only the initial placement of a person with no actor, and is never
            // reapplied to a live one.
            if (State.Actors.TryGet(npcId, out _))
            {
                if (npc.Profile != profile.ProfileKey) State.Npcs.Bind(npcId, profile.ProfileKey);
                else State.Npcs.SetPresence(npcId, DaggerfallNpcPresence.Active);
            }
            else if (npc.Profile != profile.ProfileKey || npc.X is null || npc.Y is null || npc.Z is null)
                State.Npcs.Place(npcId, profile.ProfileKey, placement.Position);
            else
                State.Npcs.SetPresence(npcId, DaggerfallNpcPresence.Active);

            DaggerfallNpc current = State.Npcs.Require(npcId);
            WorldPoint position = current.X is float x && current.Y is float y && current.Z is float z
                ? _sites.ProfileToLocal(new WorldPoint(x, y, z))
                : _sites.ProfileToLocal(placement.Position);
            MaterializeNpcActor(npcId, new ActorPose(position, 0F));
        }

        // A source civilian may have been hidden by night before a retained site delta was saved.
        // The actor is restored by the lifecycle first; this call restores only its visual admission.
        MaterializeNpcActors(profile.Project.PlayerPosition ?? new WorldPoint(0F, 0F, 0F));
        _appearance.SyncRestoredDefeat(State.Actors);
    }

    /// <summary>
    /// Advances only admitted source civilians. Enemy pursuit never owns this actor family; the
    /// wander coordinator records explicit idle, seeking, moving, blocked, unloaded, and dead
    /// states while Engine remains the movement authority.
    /// </summary>
    internal void UpdateCivilianPopulation(ulong simulationStep, float deltaSeconds)
    {
        HashSet<long> activeSource = State.Npcs.All
            .Where(npc => IsPopulationNpc(npc)
                && State.Npcs.IsGameplayActive(npc.DurableId)
                && npc.Profile == _sites.ActiveProfile)
            .Select(npc => npc.DurableId)
            .ToHashSet();
        bool admitted = _sites.Projection.Inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior
            && _time.Calendar.IsDay;
        foreach (ActorState actor in State.Actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian))
        {
            bool recycled = false;
            DaggerfallNpc? populationNpc = State.Npcs.All.FirstOrDefault(npc =>
                IsPopulationNpc(npc) && npc.DurableId == actor.DurableId);
            if (populationNpc is not null
                && _sites.Projection.Inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior
                && _time.Calendar.IsDay
                && _sites.ActiveLocationLoaded
                && _sites.Projection.Inputs.Project.PlayerPosition is not null
                && State.PlayerControl.Position is WorldPoint player
                && actor.Position.HorizontalDistanceTo(player) > _tuning.CivilianWander.RecycleDistanceMeters)
            {
                // Retire only the admitted appearance. The canonical actor, inventory, corpse,
                // and durable pose remain owned by the existing lifetime/save path for re-entry.
                HidePopulationNpc(populationNpc);
                recycled = true;
            }

            if (!admitted || recycled || !activeSource.Contains(actor.DurableId))
            {
                actor.Wander.MarkUnloaded();
                continue;
            }

            actor.Wander.MarkLoaded();
            _ = _enemyBehavior.UpdateCivilian(actor, PopulationWanderPolicy, simulationStep, deltaSeconds);
        }
    }

    private void HidePopulationNpc(DaggerfallNpc npc)
    {
        if (npc.Presence == DaggerfallNpcPresence.Removed)
            return;
        State.Npcs.SetPresence(npc.DurableId, DaggerfallNpcPresence.Hidden);
        if (_appearance.HasActor(npc.DurableId)) _appearance.RetireActor(npc.DurableId);
    }

    private static bool IsPopulationNpc(DaggerfallNpc npc) =>
        npc.Kind == DaggerfallNpcKind.Civilian
        && npc.StableKey.StartsWith("population/", StringComparison.Ordinal);

    private WorldPoint PopulationPlayerProfilePosition(DaggerfallSiteProfile profile)
    {
        if (State.PlayerControl.Position is WorldPoint local)
            return WorldPoint.From(_sites.LocalToProfile(local.ToVector()));
        return profile.Project.PlayerPosition ?? new WorldPoint(0F, 0F, 0F);
    }

    private static bool IsWithinPopulationSourceRange(IReadOnlyList<DaggerfallPopulationPlacement> placements, WorldPoint player)
    {
        if (placements.Count == 0) return false;
        float minX = placements.Min(placement => placement.Position.X) - PopulationMaximumOutsideRangeMeters;
        float maxX = placements.Max(placement => placement.Position.X) + PopulationMaximumOutsideRangeMeters;
        float minZ = placements.Min(placement => placement.Position.Z) - PopulationMaximumOutsideRangeMeters;
        float maxZ = placements.Max(placement => placement.Position.Z) + PopulationMaximumOutsideRangeMeters;
        return player.X >= minX && player.X <= maxX && player.Z >= minZ && player.Z <= maxZ;
    }

    private bool IsPopulationPlacementAdmitted(DaggerfallPopulationPlacement placement, DaggerfallNpc? existing,
        WorldPoint playerProfile)
    {
        WorldPoint placementLocal = _sites.ProfileToLocal(placement.Position);
        WorldPoint playerLocal = _sites.ProfileToLocal(playerProfile);
        float distance = playerLocal.HorizontalDistanceTo(placementLocal);
        if (distance > PopulationNavGridSpawnRadiusMeters) return false;
        if (existing is { Presence: DaggerfallNpcPresence.Active })
        {
            if (State.Actors.TryGet(existing.DurableId, out ActorState? actor))
                distance = actor.Position.HorizontalDistanceTo(playerLocal);
            return distance <= _tuning.CivilianWander.RecycleDistanceMeters;
        }

        // Visible population is allowed to change inside the donor's 120m hysteresis range;
        // the larger grid radius only bounds which source pool entries can be considered.
        return distance <= PopulationVisiblePopulationRangeMeters;
    }

    /// <summary>
    /// Routes a blocked enemy's donor door action through the active canonical door owner. The
    /// behavior layer supplies the source CanOpenDoors fact; this method only selects a nearby
    /// normalized door and asks that owner to mutate its motion/lock state.
    /// </summary>
    private bool TryOpenDoorForEnemy(ActorState actor)
    {
        const float reach = 1.75F;
        WorldPoint position = actor.Position;
        DaggerfallDoorView[] nearby = [.. _doors.All
            .Where(door => door.Kind == DaggerfallDoorKind.Normal && door.Motion == DaggerfallDoorMotion.Closed)
            .Where(door => MathF.Abs(door.Pose.Translation.Y - position.Y) <= 2.5F)
            .Where(door =>
            {
                float dx = door.Pose.Translation.X - position.X;
                float dz = door.Pose.Translation.Z - position.Z;
                return (dx * dx) + (dz * dz) <= reach * reach;
            })
            .OrderBy(door => door.Id.SourceKey, StringComparer.Ordinal)
            .ThenBy(door => door.Id.BlockX)
            .ThenBy(door => door.Id.BlockZ)
            .ThenBy(door => door.Id.ModelIndex)];
        foreach (DaggerfallDoorView door in nearby)
        {
            DaggerfallDoorOperationResult result = door.LockValue == 0
                ? _doors.Open(door.Id, DaggerfallDoorOperationSource.Player)
                : door.LockValue < 20 ? _doors.Bash(door.Id) : DaggerfallDoorOperationResult.MagicallyHeld;
            if (result is DaggerfallDoorOperationResult.Started or DaggerfallDoorOperationResult.AlreadyOpen)
                return true;
        }
        return false;
    }

    private string? ResolvePopulationRace(DaggerfallSiteRecord site, IReadOnlyList<DaggerfallPopulationPlacement> placements)
    {
        DaggerfallClimateCell climate = _definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY);
        if (!string.IsNullOrWhiteSpace(climate.People))
        {
            DaggerfallRaceDefinition? climateRace = _definitions.Catalogs.Races.FirstOrDefault(race =>
                StringComparer.OrdinalIgnoreCase.Equals(race.Id, climate.People));
            if (climateRace is not null) return climateRace.Id;
        }

        // Older hand-authored fixtures may carry no importer People column. Their source faction
        // still supplies a donor race when one placement names a published faction; no default race
        // is invented when neither source has one.
        foreach (DaggerfallPopulationPlacement placement in placements.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            if (placement.FactionId == 0 || !_definitions.Factions.Factions.TryGetValue(placement.FactionId, out DaggerfallFactionDefinition? faction))
                continue;
            DaggerfallRaceDefinition? factionRace = _definitions.Catalogs.Races.FirstOrDefault(race => race.DonorRaceId == faction.Race);
            if (factionRace is not null) return factionRace.Id;
        }
        return null;
    }

    /// <summary>Applies the donor's bounded active population pool to real normalized placements.</summary>
    internal static int PopulationCapacity(DaggerfallSiteRecord site, int sourceCount)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (sourceCount <= 0) return 0;
        int totalBlocks = site.Exterior?.Blocks.Count ?? 0;
        if (totalBlocks <= 0) return sourceCount;
        int bands = Math.Clamp(totalBlocks / PopulationBlocksPerBand, MinimumPopulationBands, MaximumPopulationBands);
        return Math.Min(sourceCount, checked(bands * PopulationBandSize));
    }

    /// <summary>Maps the source faction's filed type to the role consumers use for crime and dialogue.</summary>
    internal static string PopulationRole(DaggerfallFactionDefinition? faction) =>
        faction?.Type == 10 || StringComparer.Ordinal.Equals(faction?.TypeName, "KnightlyGuard")
            ? "guard"
            : "civilian";
}
