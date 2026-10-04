using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

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
    private const int KnightlyGuardFactionType = 10;
    private static readonly IReadOnlyList<string> PopulationServices = ["talk"];
    private static readonly WanderPolicy PopulationWanderPolicy = new(
        MovementSpeedUnitsPerSecond: 1.3f,
        WaypointDistance: 2.5f,
        IdleDurationSeconds: 2.5f,
        NavigationMaximumVisited: 64,
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

        DaggerfallPopulationPlacement[] candidates = [.. profile.Population
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .Where(placement => profile.BillboardSprites.ContainsKey((placement.BillboardArchive, placement.BillboardRecord))
                && (placement.FactionId == 0 || _definitions.Factions.Factions.ContainsKey(placement.FactionId)))
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
            string role = PopulationRole(faction);

            DaggerfallNpc? existing = sourceNpcs.FirstOrDefault(npc =>
                StringComparer.Ordinal.Equals(npc.StableKey, placement.Id));
            long npcId = existing?.DurableId ?? State.Npcs.RegisterPopulationCivilian(
                npcSite,
                placement.Id,
                appearance,
                role,
                PopulationServices);
            if (existing is not null)
                State.Npcs.RefreshPopulationFacts(npcId, appearance, role, PopulationServices);
            DaggerfallNpc npc = State.Npcs.Require(npcId);
            if (npc.Presence == DaggerfallNpcPresence.Removed)
                continue;

            // A saved wandering pose is authoritative until the person first enters this profile.
            // The authored source position is only the initial placement and is never reapplied on
            // every admitted update.
            if (npc.Profile != profile.ProfileKey || npc.X is null || npc.Y is null || npc.Z is null)
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
                && npc.Presence == DaggerfallNpcPresence.Active
                && npc.Profile == _sites.ActiveProfile)
            .Select(npc => npc.DurableId)
            .ToHashSet();
        bool admitted = _sites.Projection.Inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior
            && _time.Calendar.IsDay;
        foreach (ActorState actor in State.Actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian))
        {
            if (!admitted || !activeSource.Contains(actor.DurableId))
            {
                actor.Wander.MarkUnloaded();
                continue;
            }

            actor.Wander.MarkLoaded();
            _ = _enemyBehavior.UpdateCivilian(actor, PopulationWanderPolicy, simulationStep, deltaSeconds);
            SyncCivilianPosition(actor);
        }
    }

    /// <summary>Copies accepted Engine actor poses into the durable profile-coordinate registry.</summary>
    internal void SyncCivilianPositions()
    {
        foreach (ActorState actor in State.Actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian))
            SyncCivilianPosition(actor);
    }

    private void SyncCivilianPosition(ActorState actor)
    {
        if (!State.Npcs.All.Any(npc => IsPopulationNpc(npc) && npc.DurableId == actor.DurableId)) return;
        WorldPoint profilePosition = WorldPoint.From(_sites.LocalToProfile(actor.Position.ToVector()));
        State.Npcs.Relocate(actor.DurableId, profilePosition.X, profilePosition.Y, profilePosition.Z);
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

    private string? ResolvePopulationRace(DaggerfallSiteRecord site, IReadOnlyList<DaggerfallPopulationPlacement> placements)
    {
        DaggerfallClimateCell climate = _definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY);
        if (!string.IsNullOrWhiteSpace(climate.People))
        {
            DaggerfallRaceDefinition? climateRace = _definitions.Catalogs.Races.FirstOrDefault(race =>
                StringComparer.OrdinalIgnoreCase.Equals(race.Id, climate.People));
            return climateRace?.Id;
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
        faction?.Type == KnightlyGuardFactionType || StringComparer.Ordinal.Equals(faction?.TypeName, "KnightlyGuard")
            ? "guard"
            : "civilian";
}
