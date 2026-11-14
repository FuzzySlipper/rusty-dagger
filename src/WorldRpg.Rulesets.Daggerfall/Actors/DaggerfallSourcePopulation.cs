using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Presentation;

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
internal sealed class DaggerfallSourcePopulation(DaggerfallNpcRegistry npcs, ActorsState actors, PlayerControlState player,
    DaggerfallDefinitions definitions, DaggerfallCivilianWanderTuning tuning, DaggerfallCivilians civilians,
    Func<DaggerfallSiteProfile> profile, Func<DaggerfallWorldProfileKey> activeProfile, Func<DaggerfallSiteAppearance> appearance,
    Func<bool> locationLoaded, Func<bool> residencyInitialized, Func<WorldPoint, WorldPoint> profileToLocal, Func<Vector3, Vector3> localToProfile,
    Func<DaggerfallSiteId, DaggerfallSiteRecord> requireSite, Func<bool> isDay, Func<bool> suppressSpawns, Func<long, bool> held,
    Action<ActorState, WanderPolicy, ulong, float> wander)
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
    internal const float NavGridSpawnRadiusMeters = 96F * 64F * (float)DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
    internal const float MaximumOutsideRangeMeters = 2500F * (float)DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
    internal const float VisiblePopulationRangeMeters = 120F;
    private WanderPolicy WanderPolicy => new(
        tuning.MovementSpeedUnitsPerSecond,
        tuning.WaypointDistance,
        tuning.IdleDurationSeconds,
        tuning.NavigationMaximumVisited,
        MaximumFailures: 2,
        NavigationMode: ActorNavigationMode.Ground);

    /// <summary>
    /// Reconciles the active exterior's source population. Re-entry finds the same source key and
    /// therefore the same actor identity; it never creates a demo or test-only person to fill a gap.
    /// </summary>
    internal void Reconcile()
    {
        DaggerfallSiteProfile active = profile();
        if (active.ProfileKind != DaggerfallWorldProfileKind.Exterior || active.Site is not { } siteId)
            return;
        // An unloaded location keeps its population, hidden or not, in the site delta that holds
        // its actors; presence is reconciled again when the location is admitted.
        if (!locationLoaded()) return;
        // Where the location (and the player in it) lies in the world is known only once its terrain window is
        // admitted: an exterior arrival reconciles its population after that (DaggerfallSiteCrossing.TryTransitionTo).
        if (!residencyInitialized()) return;

        DaggerfallSiteRecord site = requireSite(siteId);
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty);
        DaggerfallNpc[] sourceNpcs = [.. npcs.All.Where(npc => DaggerfallCivilians.IsSourcePopulation(npc) && npc.Site == npcSite)];
        if (!isDay())
        {
            foreach (DaggerfallNpc npc in sourceNpcs)
                Hide(npc);
            return;
        }

        string? race = ResolveRace(site, active.Population);
        if (race is null)
        {
            foreach (DaggerfallNpc npc in sourceNpcs) Hide(npc);
            return;
        }

        WorldPoint playerProfile = PlayerProfilePosition(active);
        if (!IsWithinSourceRange(active.Population, playerProfile))
        {
            foreach (DaggerfallNpc npc in sourceNpcs)
                Hide(npc);
            return;
        }

        Dictionary<string, DaggerfallNpc> existingByKey = sourceNpcs.ToDictionary(npc => npc.StableKey, StringComparer.Ordinal);
        DaggerfallPopulationPlacement[] candidates = [.. active.Population
            .Where(placement => active.BillboardSprites.ContainsKey((placement.BillboardArchive, placement.BillboardRecord))
                && (placement.FactionId == 0 || definitions.Factions.Factions.ContainsKey(placement.FactionId)))
            .Where(placement => IsPlacementAdmitted(placement, existingByKey.GetValueOrDefault(placement.Id), playerProfile))
            // Preserve already active source identities through normal wandering. Stable source
            // order remains the tie-breaker for newly admitted people.
            .OrderByDescending(placement => existingByKey.TryGetValue(placement.Id, out DaggerfallNpc? npc)
                && npc.Presence == DaggerfallNpcPresence.Active)
            .ThenBy(placement => placement.Id, StringComparer.Ordinal)
            .Take(Capacity(site, active.Population.Count))];
        HashSet<string> admittedKeys = candidates.Select(placement => placement.Id).ToHashSet(StringComparer.Ordinal);
        foreach (DaggerfallNpc npc in sourceNpcs.Where(npc => !admittedKeys.Contains(npc.StableKey)))
            Hide(npc);

        foreach (DaggerfallPopulationPlacement placement in candidates)
        {
            DaggerfallFactionDefinition? faction = placement.FactionId == 0
                ? null
                : definitions.Factions.Factions[placement.FactionId];
            DaggerfallNpcAppearance look = new(race, (placement.Flags & FemalePopulationFlag) != 0 ? "Female" : "Male",
                placement.BillboardArchive, placement.BillboardRecord, placement.NameSeed, placement.FactionId);
            (string role, IReadOnlyList<string> services) = DaggerfallNpcServiceFacts.Resolve(
                definitions, faction, placement.SourceBuildingType, placement.SourceBuildingFactionId, "civilian");

            DaggerfallNpc? existing = sourceNpcs.FirstOrDefault(npc =>
                StringComparer.Ordinal.Equals(npc.StableKey, placement.Id));
            if (suppressSpawns() && existing?.Presence != DaggerfallNpcPresence.Active) continue;
            long npcId = existing?.DurableId ?? npcs.RegisterPopulationCivilian(
                npcSite,
                placement.Id,
                look,
                role,
                services);
            if (existing is not null)
                npcs.RefreshPopulationFacts(npcId, look, role, services);
            DaggerfallNpc npc = npcs.Require(npcId);
            if (npc.Presence == DaggerfallNpcPresence.Removed)
                continue;

            // A live (or site-retained, already restored) actor owns the wandering pose. The authored
            // source position is only the initial placement of a person with no actor, and is never
            // reapplied to a live one.
            if (actors.TryGet(npcId, out _))
            {
                if (npc.Profile != active.ProfileKey) npcs.Bind(npcId, active.ProfileKey);
                else npcs.SetPresence(npcId, DaggerfallNpcPresence.Active);
            }
            else if (npc.Profile != active.ProfileKey || npc.X is null || npc.Y is null || npc.Z is null)
                npcs.Place(npcId, active.ProfileKey, placement.Position);
            else
                npcs.SetPresence(npcId, DaggerfallNpcPresence.Active);

            DaggerfallNpc current = npcs.Require(npcId);
            WorldPoint position = current.X is float x && current.Y is float y && current.Z is float z
                ? profileToLocal(new WorldPoint(x, y, z))
                : profileToLocal(placement.Position);
            civilians.Materialize(npcId, new ActorPose(position, 0F));
        }

        // A source civilian may have been hidden by night before a retained site delta was saved.
        // The actor is restored by the lifecycle first; this call restores only its visual admission.
        civilians.MaterializeAll(active.Project.PlayerPosition ?? new WorldPoint(0F, 0F, 0F));
        appearance().SyncRestoredDefeat(actors);
    }

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
        bool admitted = profile().ProfileKind == DaggerfallWorldProfileKind.Exterior
            && isDay();
        foreach (ActorState actor in actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian))
        {
            bool recycled = false;
            DaggerfallNpc? populationNpc = npcs.All.FirstOrDefault(npc =>
                DaggerfallCivilians.IsSourcePopulation(npc) && npc.DurableId == actor.DurableId);
            if (populationNpc is not null
                && profile().ProfileKind == DaggerfallWorldProfileKind.Exterior
                && isDay()
                && locationLoaded()
                && profile().Project.PlayerPosition is not null
                && player.Position is WorldPoint standing
                && actor.Position.HorizontalDistanceTo(standing) > tuning.RecycleDistanceMeters)
            {
                // Retire only the admitted appearance. The canonical actor, inventory, corpse,
                // and durable pose remain owned by the existing lifetime/save path for re-entry.
                Hide(populationNpc);
                recycled = true;
            }

            if (!admitted || recycled || !activeSource.Contains(actor.DurableId))
            {
                actor.Wander.MarkUnloaded();
                continue;
            }

            actor.Wander.MarkLoaded();
            if (held(actor.DurableId)) continue;
            wander(actor, WanderPolicy, simulationStep, deltaSeconds);
        }
    }

    private void Hide(DaggerfallNpc npc)
    {
        if (npc.Presence == DaggerfallNpcPresence.Removed)
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

    private string? ResolveRace(DaggerfallSiteRecord site, IReadOnlyList<DaggerfallPopulationPlacement> placements)
    {
        DaggerfallClimateCell climate = definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY);
        if (!string.IsNullOrWhiteSpace(climate.People))
        {
            DaggerfallRaceDefinition? climateRace = definitions.Catalogs.Races.FirstOrDefault(race =>
                StringComparer.OrdinalIgnoreCase.Equals(race.Id, climate.People));
            if (climateRace is not null) return climateRace.Id;
        }

        // Older hand-authored fixtures may carry no importer People column. Their source faction
        // still supplies a donor race when one placement names a published faction; no default race
        // is invented when neither source has one.
        foreach (DaggerfallPopulationPlacement placement in placements.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            if (placement.FactionId == 0 || !definitions.Factions.Factions.TryGetValue(placement.FactionId, out DaggerfallFactionDefinition? faction))
                continue;
            DaggerfallRaceDefinition? factionRace = definitions.Catalogs.Races.FirstOrDefault(race => race.DonorRaceId == faction.Race);
            if (factionRace is not null) return factionRace.Id;
        }
        return null;
    }

    /// <summary>Applies the donor's bounded active population pool to real normalized placements.</summary>
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
