using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A current save resolved against the admitted content: every relationship the restore relies on has
/// been checked, and the persisted identity ledger was rebuilt once and is handed to the session as its
/// live allocator.
/// </summary>
/// <param name="Payload">The validated payload.</param>
/// <param name="Identities">The persisted durable identity ledger, rebuilt once for validation and play.</param>
/// <param name="TombstonedActors">Actor identities the ledger had already retired when the save was made.</param>
internal sealed record DaggerfallResolvedRestore(DaggerfallSavePayload Payload, DurableIdentityAllocator Identities, IReadOnlySet<long> TombstonedActors, DaggerfallDefinitions Definitions);

/// <summary>Daggerfall's complete current state. The Host stores its encoded bytes without interpreting them.</summary>
internal sealed record DaggerfallSavePayload(
    DaggerfallPlayerSave Player,
    DaggerfallActorSave[] Actors,
    DaggerfallDynamicActorSave[] DynamicActors,
    int Experience,
    int Level,
    DaggerfallInventorySave Inventory,
    DaggerfallCorpseSave[] Corpses,
    DurableIdentityState Identities,
    DaggerfallCombatCooldownSave[] CombatCooldowns,
    DaggerfallCalendarSave Calendar,
    DaggerfallSiteSave Site,
    DaggerfallActorInventorySave[] ActorInventories,
    DaggerfallVariablesSave Variables,
    DaggerfallNpcSave Npcs,
    DaggerfallActiveEffectSave[] ActiveEffects,
    DaggerfallSkillProgressionSave SkillUses,
    DaggerfallSocialSave Social,
    DaggerfallCharacterSave? Character = null,
    DaggerfallLevelUpSave? LevelUp = null)
{
    [JsonRequired]
    public DaggerfallSpellDefinition[] CustomSpells { get; init; } = [];
    [JsonRequired]
    public long MagicRounds { get; init; }
    [JsonRequired] public DaggerfallInfectionsSave Infections { get; init; } = DaggerfallInfectionsSave.Empty;
    public long NextCastSequence { get; init; } = 1;
    [JsonRequired]
    public DaggerfallReadySpell? ReadySpell {get;init;}
    [JsonRequired]
    public DaggerfallDispelRequest? PendingDispel { get; init; }
    [JsonRequired] public string? PendingTeleport { get; init; }
    [JsonRequired] public DaggerfallTeleportAnchor? TeleportAnchor { get; init; }
    [JsonRequired] public DaggerfallIdentifyRequest? PendingIdentify { get; init; }
    [JsonRequired] public DaggerfallCreateItemRequest? PendingCreateItem { get; init; }
    [JsonRequired]
    public long[] BanishedActors { get; init; } = [];

    /// <summary>Every current quest instance; an empty collection is meaningful current state.</summary>
    [JsonRequired]
    public DaggerfallQuestInstancesSave Quests { get; init; } = new([]);
    /// <summary>Every selected RDB door's current state, including a partially completed motion.</summary>
    [JsonRequired]
    public DaggerfallDoorSave[] Doors { get; init; } = [];
    /// <summary>Active profile's motion phases and endpoint intent for admitted action models.</summary>
    [JsonRequired]
    public DaggerfallDungeonMotionSnapshot DungeonMotion { get; init; } = null!;
    /// <summary>Durable map-pixel center and local origin for the active exterior window.</summary>
    [JsonRequired]
    public DaggerfallExteriorCellResidencySave? ExteriorResidency { get; init; }
    /// <summary>Whether the active exterior location cell still owns admitted geometry and actors.</summary>
    public DaggerfallExteriorLocationResidencySave? ExteriorLocationResidency { get; init; }
    /// <summary>Detached state for admitted sites that are currently unloaded.</summary>
    [JsonRequired]
    public DaggerfallSiteDeltaSave[] SiteDeltas { get; init; } = [];
    /// <summary>The current bank balance and generated-gold stack sequence; coins and letters remain inventory entries.</summary>
    [JsonRequired]
    public DaggerfallCurrencySave Currency { get; init; } = new(0, 1);
    /// <summary>The current regional account partition; carried coins and letters stay in inventory state.</summary>
    [JsonRequired]
    public DaggerfallRegionalBankSave Bank { get; init; } = DaggerfallRegionalBankSave.Empty;
    [JsonRequired]
    public DaggerfallLoansSave Loans { get; init; } = DaggerfallLoansSave.Empty;
    [JsonRequired]
    public DaggerfallPropertySave Property { get; init; } = DaggerfallPropertySave.Empty;
    [JsonRequired]
    public DaggerfallLodgingSave Lodging { get; init; } = DaggerfallLodgingSave.Empty;
    [JsonRequired]
    public DaggerfallTravelSave Travel { get; init; } = DaggerfallTravelSave.Empty;
    [JsonRequired]
    public DaggerfallCrimeSave Crime { get; init; } = new([], [], [], 0, 0, 0, 0);
    [JsonRequired]
    public DaggerfallKnightlyOrderClaimStateSave KnightlyClaims { get; init; } = DaggerfallKnightlyOrderClaimStateSave.Empty;
    /// <summary>Movement work accumulated before its next calendar-minute fatigue charge.</summary>
    [JsonRequired]
    public DaggerfallLocomotionSave Locomotion { get; init; } = new(0d);
    /// <summary>Current wall attachment and the donor check timers, independent of transient physical keys.</summary>
    [JsonRequired]
    public DaggerfallClimbingSave Climbing { get; init; } = new(false, false, 0f, 0f);
    /// <summary>Accepted water mode and breath continuation, including an admitted submersion timer.</summary>
    [JsonRequired]
    public DaggerfallSwimmingSave Swimming { get; init; } = DaggerfallSwimmingSave.Empty;
    /// <summary>Profile-scoped dungeon exploration, independent of active geometry or door motion.</summary>
    [JsonRequired]
    public DaggerfallDungeonDiscoverySnapshot[] DungeonDiscovery { get; init; } = [];
    /// <summary>Profile-scoped normalized dungeon action state, including activation counts and cooldowns.</summary>
    [JsonRequired]
    public DaggerfallDungeonActionGraphSnapshot[] DungeonActions { get; init; } = [];
    /// <summary>One pending dungeon answer and its stale-submission revision.</summary>
    [JsonRequired]
    public DaggerfallDungeonTextSnapshot DungeonText { get; init; } = new(0, null);
    /// <summary>Resolved encounter outcomes, including outcomes selected before an actor materializes.</summary>
    [JsonRequired]
    public DaggerfallEncounterRuntimeSave Encounters { get; init; } = new([]);
    /// <summary>Persistent player-dropped containers and their current Engine-backed contents.</summary>
    [JsonRequired]
    public DaggerfallGroundContainerSave[] GroundContainers { get; init; } = [];
    /// <summary>Actual inventory owners retaining canonical taken quest items for reoffer.</summary>
    [JsonRequired]
    public DaggerfallQuestCustodySave[] QuestCustody { get; init; } = [];
    /// <summary>Accepted service work that has not reached its concrete service-specific completion.</summary>
    [JsonRequired]
    public DaggerfallServiceStateSave Services { get; init; } = new([]);
    /// <summary>Materialized merchant stock and repair custody keyed by the live provider site.</summary>
    [JsonRequired]
    public DaggerfallMerchantSave[] Merchants { get; init; } = [];
    /// <summary>The last donor quest-provided free skill training time, when one has occurred.</summary>
    [JsonRequired]
    public DaggerfallQuestTrainingSave QuestTraining { get; init; } = new(null);
    /// <summary>Resolved market factors at the last applied calendar day.</summary>
    [JsonRequired]
    public DaggerfallRegionalPriceSave RegionalPrices { get; init; } = null!;
    /// <summary>Resolved climate weather and its next admitted calendar boundary.</summary>
    [JsonRequired]
    public DaggerfallWeatherSave Weather { get; init; } = null!;
    /// <summary>Current mount or ship choice and its return pose.</summary>
    [JsonRequired]
    public DaggerfallTransportSave Transport { get; init; } = DaggerfallTransportSave.Foot;
    /// <summary>The persistent wagon owner and its Engine-backed inventory, if one exists.</summary>
    [JsonRequired]
    public DaggerfallWagonSave? Wagon { get; init; }
    /// <summary>Generated spoken-world events, including their current expiry markers.</summary>
    [JsonRequired]
    public DaggerfallDialogueWorldSave DialogueWorld { get; init; } = DaggerfallDialogueWorldSave.Empty;
    [JsonRequired]
    public DaggerfallNotebookSave Notebook { get; init; } = new([], [], null, 0, 0);
    /// <summary>The dynamic identity kinds owned by the current Daggerfall ruleset.</summary>
    internal static readonly DurableIdentityKind[] PersistedKinds = [DurableIdentityKind.Actor, DurableIdentityKind.Item, DurableIdentityKind.Container];

    internal static RulesetSavePayload Encode(DaggerfallSavePayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.NextCastSequence < 1) throw new ArgumentException("Saved next cast sequence must be positive.");
        value.Validate();
        return new RulesetSavePayload(
            DaggerfallRuleset.Identity,
            JsonSerializer.SerializeToUtf8Bytes(value, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload));
    }

    internal static DaggerfallSavePayload Read(RulesetSavePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException("The save payload does not belong to the Daggerfall ruleset.", nameof(payload));
        try
        {
            return (JsonSerializer.Deserialize(payload.Bytes.Span, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload)
                ?? throw new ArgumentException("The Daggerfall save payload is empty.", nameof(payload))).Validate();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The Daggerfall save payload is malformed.", nameof(payload), exception);
        }
    }

    /// <summary>
    /// Verifies every current-state relationship against the selected definitions before session construction.
    /// Missing or incompatible meaning is a rejected load, never a partially restored world.
    /// </summary>
    /// <summary>
    /// Checks every current-state relationship against the admitted content before any session is
    /// composed from the save, so a malformed or mismatched save fails here with its reason.
    /// </summary>
    internal DaggerfallResolvedRestore ResolveRestore(DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallSiteProfiles? profiles = null, DaggerfallTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(inputs);
        if (ExteriorResidency is { } savedExterior)
        {
            DaggerfallExteriorWorldOrigin origin = new(savedExterior.Origin.X, savedExterior.Origin.Y,
                new System.Numerics.Vector3(savedExterior.CompensationX, savedExterior.CompensationY, savedExterior.CompensationZ));
            DaggerfallExteriorCellId playerCell = DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                new(Player.X, Player.Y, Player.Z), origin, new(definitions.Terrain.Width, definitions.Terrain.Height));
            if (savedExterior.Center != playerCell)
                throw new ArgumentException("Saved exterior window is not centered on the saved player; restoring it would admit the wrong terrain cells.");
        }
        if (ExteriorLocationResidency is { } savedLocation)
        {
            savedLocation.Validate();
            DaggerfallWorldProfileKey active = Site.ActiveProfile?.Require()
                ?? throw new ArgumentException("Saved exterior location residency requires an active profile.");
            if (savedLocation.Profile.Require() != active)
                throw new ArgumentException("Saved exterior location residency names a different active profile.");
            DaggerfallExteriorWorldBounds.Daggerfall.Require(savedLocation.Cell, nameof(ExteriorLocationResidency));
        }
        ArgumentNullException.ThrowIfNull(CustomSpells);
        if (CustomSpells.Any(spell => spell is null || !spell.IsPlayerCreated))
            throw new ArgumentException("Saved custom spells must be actual player-created definitions.");
        definitions = definitions.ForSession(CustomSpells);
        TeleportAnchor?.Resolve(definitions, inputs, profiles, tuning ?? DaggerfallTuning.Defaults);
        Notebook.Validate(definitions, definitions.TextPresentation);
        Lodging.Validate(definitions.Locations);
        Travel.Validate(definitions.Locations);
        if (Transport.OnShip || Transport.Mode is DaggerfallTransportMode.Horse or DaggerfallTransportMode.Cart)
        {
            DaggerfallWorldProfileKey activeProfile = Site.ActiveProfile?.Require()
                ?? throw new ArgumentException("Saved transport state requires an active world profile.");
            if (!Transport.OnShip && activeProfile.Kind != DaggerfallWorldProfileKind.Exterior)
                throw new ArgumentException("Saved riding or ship state cannot be active inside a building or dungeon.");
            if (Transport.Mode is DaggerfallTransportMode.Horse or DaggerfallTransportMode.Cart)
            {
                string requiredItem = Transport.Mode == DaggerfallTransportMode.Horse
                    ? DaggerfallTransportPolicy.HorseItemId : DaggerfallTransportPolicy.CartItemId;
                if (!Inventory.UniqueItems.Any(item => StringComparer.Ordinal.Equals(item.ItemId, requiredItem)))
                    throw new ArgumentException($"Saved {Transport.Mode} transport requires its owned item in player inventory.");
            }
        }
        foreach (DaggerfallHouseOwnershipSave savedHouse in Property.Houses.Concat(Property.RetainedHouses))
        {
            DaggerfallHouseIdentity house = savedHouse.Identity;
            DaggerfallSiteRecord? site = definitions.Locations.Records.SingleOrDefault(record => record.Id == house.Site);
            if (site?.Exterior is not { } exterior
                || !exterior.Buildings.TryGetValue(new(house.BlockX, house.BlockY, house.Building.Index), out var building)
                || building.Source.Id != house.Building
                || !DaggerfallPropertyPolicy.IsEligibleHouse(new(house.Site, house.Building,
                    building.Source.BuildingType, site.Kind, building.ModelRadius ?? 0f,
                    false, house.BlockX, house.BlockY)))
                throw new ArgumentException($"Saved property house '{house}' does not resolve to an admitted house building.");
        }
        if (Transport.OnShip)
        {
            DaggerfallWorldProfileKey land = Transport.ShipReturnProfile!.Require();
            if (Property.Ship is null) throw new ArgumentException("Saved boarding requires owned ship state.");
            if (profiles is not null) _ = profiles.Require(land);
            // Transport owns the detached land return. A doorway inside the ship only
            // returns to its deck; the exterior carries no duplicate land entrance relation.
            DaggerfallWorldProfileKey active = Site.ActiveProfile!.Require();
            if (active.Kind == DaggerfallWorldProfileKind.Exterior)
            {
                if (Site.ReturnProfile is not null || Site.ReturnAnchor is not null || Site.ReturnPose is not null)
                    throw new ArgumentException("Saved ship exterior must not duplicate the transport land return destination.");
            }
            else if (active.Kind != DaggerfallWorldProfileKind.Interior
                || Site.ReturnProfile?.Require() is not { Kind: DaggerfallWorldProfileKind.Exterior } deck || deck.Site != active.Site)
                throw new ArgumentException("Saved ship interior must return to its owned ship exterior.");
            DaggerfallShipArrivalAnchor anchor = DaggerfallPropertyPolicy.ShipArrival(Property.Ship.Ship, (tuning ?? DaggerfallTuning.Defaults).Property);
            DaggerfallSiteRecord activeShip = definitions.Locations.Records.Single(record => record.Id == Site.ActiveProfile!.Require().Site);
            if (activeShip.Kind != DaggerfallSiteKind.HomeYourShips || activeShip.MapPixelX != anchor.MapPixelX || activeShip.MapPixelY != anchor.MapPixelY)
                throw new ArgumentException("Saved boarding must be at the owned ship's actual world site.");
        }
        HashSet<DaggerfallWorldProfileKey> admittedGroundProfiles = profiles is null
            ? [inputs.ProfileKey]
            : [.. profiles.Keys];
        foreach (DaggerfallGroundContainerSave ground in GroundContainers)
        {
            DaggerfallWorldProfileKey profile = ground.Profile.Require();
            if (!admittedGroundProfiles.Contains(profile))
                throw new ArgumentException($"Saved ground container {ground.Id} names an unadmitted world profile '{profile.LogicalId}'.");
            if (ground.PropertyPlacement is { } placement)
            {
                var source = profile == inputs.ProfileKey ? inputs : profiles!.Require(profile);
                if (!source.PropertyContainers.Any(value => value.Id == placement)
                    || GroundContainers.Count(value => value.Profile.Require() == profile && value.PropertyPlacement == placement) != 1)
                    throw new ArgumentException($"Saved property container {ground.Id} names a missing or repeated source placement '{placement}'.");
            }
        }
        HashSet<DaggerfallRdbDoorId> selectedDoors = [.. inputs.Doors.Select(door => door.Id)];
        ValidateDungeonMotion(inputs, DungeonMotion);
        HashSet<DaggerfallWorldProfileKey> discoveryProfiles = [];
        foreach (DaggerfallDungeonDiscoverySnapshot snapshot in DungeonDiscovery)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!discoveryProfiles.Add(snapshot.Profile))
                throw new ArgumentException($"Saved dungeon discovery repeats profile '{snapshot.Profile.LogicalId}'.");
            DaggerfallSiteProfile selected = snapshot.Profile == inputs.ProfileKey
                ? inputs
                : (profiles ?? throw new ArgumentException("Saved dungeon discovery requires admitted site profiles.")).Require(snapshot.Profile);
            DaggerfallDungeonMapContent map = selected.DungeonMap
                ?? throw new ArgumentException($"Saved dungeon discovery names non-dungeon profile '{snapshot.Profile.LogicalId}'.");
            _ = new DaggerfallDungeonDiscovery(snapshot.Profile, map, snapshot);
        }
        HashSet<string> actionProfiles = new(StringComparer.Ordinal);
        foreach (DaggerfallDungeonActionGraphSnapshot snapshot in DungeonActions)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            snapshot.Validate();
            if (!actionProfiles.Add(snapshot.ProfileId))
                throw new ArgumentException($"Saved dungeon action state repeats profile '{snapshot.ProfileId}'.");

            DaggerfallSiteProfile selected;
            if (StringComparer.Ordinal.Equals(inputs.ProfileKey.LogicalId, snapshot.ProfileId))
            {
                selected = inputs;
            }
            else if (profiles is null)
            {
                throw new ArgumentException($"Saved dungeon action state names an unadmitted world profile '{snapshot.ProfileId}'.");
            }
            else
            {
                DaggerfallWorldProfileKey[] matches = profiles.Keys
                    .Where(key => StringComparer.Ordinal.Equals(key.LogicalId, snapshot.ProfileId))
                    .ToArray();
                if (matches.Length != 1)
                    throw new ArgumentException($"Saved dungeon action state names an unadmitted or ambiguous world profile '{snapshot.ProfileId}'.");
                selected = profiles.Require(matches[0]);
            }
            if (!StringComparer.Ordinal.Equals(selected.ProfileKey.LogicalId, snapshot.ProfileId))
                throw new ArgumentException($"Saved dungeon action state names an unadmitted world profile '{snapshot.ProfileId}'.");

            // Constructing a graph validates every saved node against the selected normalized
            // action closure. Variables are validated separately by the payload; this temporary
            // store keeps restore validation free of runtime session state.
            _ = new DaggerfallDungeonActionGraph(
                snapshot.ProfileId,
                selected.DungeonActions,
                new DaggerfallVariableStore(definitions.QuestSources.Tables.Globals.Lookup),
                snapshot);
        }
        if (profiles is null && DungeonActions.Any(snapshot =>
                !StringComparer.Ordinal.Equals(snapshot.ProfileId, inputs.ProfileKey.LogicalId)))
            throw new ArgumentException("Saved dungeon action state names a world profile not admitted by the selected content.");
        // Action snapshots are sparse by design. A save can be captured before an admitted
        // destination has ever been visited; that profile is reconstructed from its authored
        // normalized actions on the next admission. Snapshots that are present still have to pass
        // the exact graph/node validation above, and an unadmitted profile is rejected above.
        HashSet<DaggerfallRdbDoorId> savedDoors = [];
        foreach (DaggerfallDoorSave door in Doors)
        {
            ArgumentNullException.ThrowIfNull(door);
            door.Validate();
            if (!selectedDoors.Contains(door.Id) || !savedDoors.Add(door.Id))
                throw new ArgumentException($"Saved RDB door '{door.Id}' is not a distinct door in the selected world.");
        }
        if (!savedDoors.SetEquals(selectedDoors))
            throw new ArgumentException("Current save must carry one state for every selected RDB door.");
        HashSet<long> knownAuthoredActorIds = [.. inputs.Project.Actors.Keys];
        HashSet<long> inactiveAuthoredActorIds = [];
        HashSet<long> inactiveDynamicActorIds = [];
        bool activeLocationUnloaded = ExteriorLocationResidency?.Loaded == false;
        if (profiles is null)
        {
            HashSet<DaggerfallWorldProfileKey> detachedProfiles = [];
            foreach (DaggerfallSiteDeltaSave delta in SiteDeltas)
            {
                DaggerfallWorldProfileKey key = delta.Profile.Require();
                if (!detachedProfiles.Add(key))
                    throw new ArgumentException($"Saved site state repeats inactive site '{key}'.");
            }
        }
        else
        {
            HashSet<DaggerfallWorldProfileKey> detachedProfiles = [];
            foreach (DaggerfallSiteDeltaSave delta in SiteDeltas)
            {
                DaggerfallWorldProfileKey key = delta.Profile.Require();
                DaggerfallSiteId site = key.Site;
                if ((key == inputs.ProfileKey && !activeLocationUnloaded) || !detachedProfiles.Add(key))
                    throw new ArgumentException("Saved inactive site state must name each non-active profile once.");
                DaggerfallSiteProfile profile = profiles.Require(key);
                HashSet<long> selectedActors = [.. profile.Project.Actors.Keys];
                knownAuthoredActorIds.UnionWith(selectedActors);
                ValidateBanished(delta.BanishedActors, selectedActors, delta.Actors.Select(actor => actor.EntityId));
                HashSet<long> savedActors = [.. delta.Actors.Select(actor => actor.EntityId), .. delta.BanishedActors];
                if (!savedActors.SetEquals(selectedActors))
                    throw new ArgumentException($"Saved inactive site '{site}' does not carry exactly its authored actors.");
                foreach (DaggerfallActorSave actor in delta.Actors)
                {
                    if (!inactiveAuthoredActorIds.Add(actor.EntityId))
                        throw new ArgumentException($"Saved inactive site '{site}' repeats authored actor {actor.EntityId}.");
                    ValidateStats(actor.Stats, $"inactive actor {actor.EntityId}");
                    if (actor.WabbajackDefinition is { } transformation) _ = DaggerfallWabbajack.RequireDefinition(definitions, transformation);
                }
                foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors)
                {
                    if (actor.EntityId <= 0 || selectedActors.Contains(actor.EntityId) || !inactiveDynamicActorIds.Add(actor.EntityId))
                        throw new ArgumentException($"Saved inactive site '{site}' has an invalid or repeated dynamic actor identity.");
                    if (!IsAdmittedDynamicDefinition(definitions, actor.Definition))
                        throw new ArgumentException($"Saved inactive dynamic actor {actor.EntityId} refers to missing definition '{actor.Definition}'.");
                    ValidateStats(actor.Stats, $"inactive dynamic actor {actor.EntityId}");
                    if (actor.WabbajackActive) _ = DaggerfallWabbajack.RequireDefinition(definitions, actor.Definition);
                }
                HashSet<long> inventoryOwners = [.. delta.ActorInventories.Select(inventory => inventory.EntityId)];
                HashSet<long> selectedSiteActors = [.. selectedActors.Except(delta.BanishedActors), .. delta.DynamicActors.Select(actor => actor.EntityId)];
                if (!inventoryOwners.SetEquals(selectedSiteActors))
                    throw new ArgumentException($"Saved inactive site '{site}' does not carry exactly its actor inventories.");
                if (delta.Corpses.Any(corpse => !selectedSiteActors.Contains(corpse.ActorId)))
                    throw new ArgumentException($"Saved inactive site '{site}' carries a corpse for a non-site actor.");
                if (delta.Effects.Any(effect => !selectedSiteActors.Contains(effect.TargetId)))
                    throw new ArgumentException($"Saved inactive site '{site}' carries an effect for a non-site actor.");
                HashSet<DaggerfallRdbDoorId> profileDoors = [.. profile.Doors.Select(door => door.Id)];
                HashSet<DaggerfallRdbDoorId> deltaDoors = [.. delta.Doors.Select(door => door.Id)];
                if (!deltaDoors.SetEquals(profileDoors) || deltaDoors.Count != delta.Doors.Length)
                    throw new ArgumentException($"Saved inactive site '{site}' does not carry exactly its selected doors.");
                ValidateDungeonMotion(profile, delta.Motion);
            }
        }
        _ = definitions.RequireActor(new DaggerfallActorId("player"));
        HashSet<long> savedActorIds = [];
        foreach (DaggerfallActorSave actor in Actors)
        {
            if (!inputs.Project.Actors.TryGetValue(actor.EntityId, out AuthoredActor? placement))
                throw new ArgumentException($"Saved actor {actor.EntityId} is not placed by the selected content.");
            if (!definitions.Actors.ContainsKey(placement.ActorId))
                throw new ArgumentException($"Saved actor {actor.EntityId} refers to missing definition '{placement.ActorId.Value}'.");
            if (actor.WabbajackDefinition is { } transformation) _ = DaggerfallWabbajack.RequireDefinition(definitions, transformation);
            if (!savedActorIds.Add(actor.EntityId))
                throw new ArgumentException($"Saved actor {actor.EntityId} appears more than once.");
        }
        ValidateBanished(BanishedActors, knownAuthoredActorIds, savedActorIds.Concat(inactiveAuthoredActorIds));
        foreach (AuthoredActor placement in inputs.Project.Actors.Values)
        {
            if (!activeLocationUnloaded && !savedActorIds.Contains(placement.EntityId) && !BanishedActors.Contains(placement.EntityId))
                throw new ArgumentException($"Current save is missing authored actor {placement.EntityId}.");
        }
        // Dynamic actors are spawn-time registrations, not content placements: each one names
        // the definition it was spawned from, and a definition the selected content cannot
        // explain is reported rather than silently materialized.
        HashSet<long> dynamicActorIds = [];
        foreach (DaggerfallDynamicActorSave actor in DynamicActors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.EntityId <= 0 || !dynamicActorIds.Add(actor.EntityId))
                throw new ArgumentException("Saved dynamic actor identities must be positive and unique.");
            if (actor.WabbajackActive) _ = DaggerfallWabbajack.RequireDefinition(definitions, actor.Definition);
            if (inactiveDynamicActorIds.Contains(actor.EntityId))
                throw new ArgumentException($"Saved dynamic actor {actor.EntityId} is active and inactive at once.");
            if (savedActorIds.Contains(actor.EntityId) || BanishedActors.Contains(actor.EntityId))
                throw new ArgumentException($"Saved dynamic actor {actor.EntityId} collides with an authored actor.");
            if (!IsAdmittedDynamicDefinition(definitions, actor.Definition))
                throw new ArgumentException($"Saved dynamic actor {actor.EntityId} refers to missing definition '{actor.Definition}'.");
            ValidateStats(actor.Stats, $"dynamic actor {actor.EntityId}");
        }
        // A dynamic actor the ledger does not call live is either tombstoned or forged: restoring
        // it would reissue an identity the save already retired.
        DurableIdentityAllocator savedLedger = DurableIdentityAllocator.Restore(RestoredIdentities());
        foreach (long actorId in dynamicActorIds.Concat(inactiveDynamicActorIds))
        {
            if (savedLedger.Classify(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)actorId))) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved dynamic actor {actorId} is not live in the persisted identity ledger.");
        }
        Dictionary<long, DaggerfallNpcEntry> savedNpcs = Npcs.Entries.ToDictionary(entry => entry.DurableId);
        foreach (DaggerfallDynamicActorSave actor in DynamicActors.Concat(SiteDeltas.SelectMany(delta => delta.DynamicActors)))
        {
            if (actor.Definition is not (DaggerfallActorKinds.Civilian or DaggerfallActorKinds.StaticNpc)) continue;
            if (!savedNpcs.TryGetValue(actor.EntityId, out DaggerfallNpcEntry? npc))
                throw new ArgumentException($"Saved civilian actor {actor.EntityId} has no matching NPC identity record.");
            if ((DaggerfallNpcKind)npc.Kind != (actor.Definition == DaggerfallActorKinds.StaticNpc ? DaggerfallNpcKind.Static : DaggerfallNpcKind.Civilian) || (DaggerfallNpcPresence)npc.Presence == DaggerfallNpcPresence.Removed)
                throw new ArgumentException($"Saved civilian actor {actor.EntityId} does not name a live civilian NPC identity.");
        }

        var dynamicDefinitions = DynamicActors.Concat(SiteDeltas.SelectMany(delta => delta.DynamicActors))
            .ToDictionary(actor => actor.EntityId, actor => actor.Definition);
        foreach (var npc in savedNpcs.Values)
        {
            if (npc.DurableId == DaggerfallActorIdentity.PlayerEntityId || savedActorIds.Contains(npc.DurableId)
                || inactiveAuthoredActorIds.Contains(npc.DurableId) || BanishedActors.Contains(npc.DurableId)
                || dynamicDefinitions.TryGetValue(npc.DurableId, out var definition)
                    && (npc.Kind != (int)DaggerfallNpcKind.Civilian || definition != DaggerfallActorKinds.Civilian)
                    && (npc.Kind != (int)DaggerfallNpcKind.Static || definition != DaggerfallActorKinds.StaticNpc))
                throw new ArgumentException($"Saved NPC {npc.DurableId} aliases an unrelated actor identity.");
            var classification = savedLedger.Classify(new(DurableIdentityKind.Actor, checked((ulong)npc.DurableId)));
            if (classification != DurableIdentityClassification.Live
                && !(npc.Presence == (int)DaggerfallNpcPresence.Removed && classification == DurableIdentityClassification.Removed))
                throw new ArgumentException($"Saved NPC {npc.DurableId} has no issued identity for its presence.");
        }
        foreach (var npc in savedNpcs.Values.Where(value => value.Profile is not null))
        {
            if (savedLedger.Classify(new(DurableIdentityKind.Actor, checked((ulong)npc.DurableId))) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved placed NPC {npc.DurableId} is not live in the identity ledger.");
            var profile = npc.Profile!.Require();
            DaggerfallSiteProfile owner = profiles is null ? inputs : profiles.Require(profile);
            if (owner.ProfileKey != profile || !owner.BillboardSprites.ContainsKey((npc.BillboardArchive, npc.BillboardRecord)))
                throw new ArgumentException($"Saved placed NPC {npc.DurableId} has no admitted profile or billboard.");
        }
        Dictionary<ulong, DaggerfallItemMetadataSave> uniqueItems = [];
        ValidateInventory(Inventory, definitions, uniqueItems, DaggerfallItemOwner.Player, requireEquipment: true);
        HashSet<ulong> containerIdentities = [];
        HashSet<long> corpseActors = [];
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            if (!savedActorIds.Contains(corpse.ActorId) && !dynamicActorIds.Contains(corpse.ActorId))
                throw new ArgumentException($"Saved corpse refers to missing actor {corpse.ActorId}.");
            corpse.Validate();
            if (!corpseActors.Add(corpse.ActorId))
                throw new ArgumentException($"Saved corpse actor {corpse.ActorId} appears more than once.");
            if (!containerIdentities.Add(corpse.ContainerId))
                throw new ArgumentException($"Saved corpse container identity {corpse.ContainerId} appears more than once.");
            RequireLiveCorpseContainer(savedLedger, corpse);
            ValidateInventory(new DaggerfallInventorySave(corpse.Stacks, corpse.UniqueItems, []), definitions, uniqueItems, DaggerfallItemOwner.Corpse(corpse.ActorId), requireEquipment: false);
        }
        HashSet<long> groundIds = [];
        foreach (DaggerfallGroundContainerSave ground in GroundContainers)
        {
            ground.Validate();
            if (!groundIds.Add(ground.Id)) throw new ArgumentException("Saved ground containers must have distinct identities.");
            if (!containerIdentities.Add(checked((ulong)ground.Id)))
                throw new ArgumentException($"Saved container identity {ground.Id} collides with a corpse container.");
            if (savedLedger.Classify(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)ground.Id))) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved ground container {ground.Id} is not live in the persisted identity ledger.");
            ValidateInventory(ground.Inventory, definitions, uniqueItems, DaggerfallItemOwner.Ground(ground.Id), requireEquipment: false);
        }
        foreach (var custody in QuestCustody)
        {
            if (!containerIdentities.Add(checked((ulong)custody.Id))) throw new ArgumentException("Quest custody container collides with another owner.");
            if (savedLedger.Classify(new(DurableIdentityKind.Container, checked((ulong)custody.Id))) != DurableIdentityClassification.Live)
                throw new ArgumentException("Quest custody container is not live in the identity ledger.");
            ValidateInventory(custody.Inventory, definitions, uniqueItems, DaggerfallItemOwner.Quest(custody.Id), requireEquipment: false);
        }
        HashSet<string> merchantKeys = new(StringComparer.Ordinal);
        HashSet<string> repairRequests = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallServiceQueuedWork> pendingServices = Services.Pending.ToDictionary(value => value.Id, StringComparer.Ordinal);
        foreach (DaggerfallMerchantSave merchant in Merchants)
        {
            merchant.Validate();
            if (!merchantKeys.Add(merchant.Key))
                throw new ArgumentException($"Saved merchant provider '{merchant.Key}' appears more than once.");
            foreach (long containerId in new[] { merchant.MerchantContainerId, merchant.CustodyContainerId })
            {
                if (!containerIdentities.Add(checked((ulong)containerId)))
                    throw new ArgumentException($"Saved merchant container identity {containerId} collides with another container.");
                if (savedLedger.Classify(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)containerId)))
                    != DurableIdentityClassification.Live)
                    throw new ArgumentException($"Saved merchant container {containerId} is not live in the persisted identity ledger.");
            }
            ValidateInventory(merchant.Inventory, definitions, uniqueItems,
                DaggerfallItemOwner.Merchant(merchant.MerchantContainerId), requireEquipment: false);
            ValidateInventory(merchant.Custody, definitions, uniqueItems,
                DaggerfallItemOwner.RepairCustody(merchant.CustodyContainerId), requireEquipment: false);
            if (!savedNpcs.TryGetValue(merchant.ProviderNpcId, out DaggerfallNpcEntry? providerNpc)
                || providerNpc.Presence != (int)DaggerfallNpcPresence.Active
                || providerNpc.Region != merchant.ProviderRegion
                || !StringComparer.Ordinal.Equals(providerNpc.Location, merchant.ProviderLocation)
                || !StringComparer.Ordinal.Equals(providerNpc.Building, merchant.ProviderBuilding))
                throw new ArgumentException($"Saved merchant '{merchant.Key}' has no matching active provider NPC site.");
            DaggerfallServiceProvider provider = new(merchant.ProviderNpcId,
                new(merchant.ProviderRegion, merchant.ProviderLocation, merchant.ProviderBuilding, providerNpc.ProfileId), merchant.Service);
            foreach (DaggerfallMerchantRepairSave repair in merchant.Repairs)
            {
                if (!repairRequests.Add(repair.RequestId)
                    || !pendingServices.TryGetValue(repair.RequestId, out DaggerfallServiceQueuedWork? pending)
                    || pending.Provider.NpcId != provider.NpcId || pending.Provider.Site != provider.Site
                    || pending.Provider.Service != "repair" || pending.CompletesAtMinute != repair.DueMinute)
                    throw new ArgumentException($"Saved repair '{repair.RequestId}' has no matching pending provider work.");
            }
        }
        if (Wagon is { } wagon)
        {
            wagon.Validate();
            if (groundIds.Contains(wagon.Id))
                throw new ArgumentException($"Saved wagon {wagon.Id} collides with a ground container.");
            if (!containerIdentities.Add(checked((ulong)wagon.Id)))
                throw new ArgumentException($"Saved wagon identity {wagon.Id} collides with another container.");
            if (savedLedger.Classify(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)wagon.Id))) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved wagon {wagon.Id} is not live in the persisted identity ledger.");
            ValidateInventory(wagon.Inventory, definitions, uniqueItems, DaggerfallItemOwner.Wagon(wagon.Id), requireEquipment: false);
        }
        foreach (DaggerfallPropertyStorageSave storage in Property.Storage)
        {
            ulong containerId = checked((ulong)storage.ContainerId);
            if (!containerIdentities.Add(containerId))
                throw new ArgumentException($"Saved property storage container {containerId} collides with another container.");
            if (savedLedger.Classify(new DurableIdentityReference(DurableIdentityKind.Container, containerId)) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved property storage container {containerId} is not live in the persisted identity ledger.");
            ValidateInventory(storage.Inventory, definitions, uniqueItems,
                DaggerfallItemOwner.Property(storage.ContainerId), requireEquipment: false);
        }
        HashSet<long> actorInventories = [];
        foreach (DaggerfallActorInventorySave inventory in ActorInventories)
        {
            inventory.Validate();
            if ((!savedActorIds.Contains(inventory.EntityId) && !dynamicActorIds.Contains(inventory.EntityId)) || !actorInventories.Add(inventory.EntityId))
                throw new ArgumentException($"Saved actor inventory {inventory.EntityId} has no distinct saved actor.");
            ValidateInventory(inventory.Inventory, definitions, uniqueItems, DaggerfallItemOwner.Actor(inventory.EntityId), requireEquipment: true);
        }
        foreach (DaggerfallSiteDeltaSave delta in SiteDeltas)
        {
            foreach (DaggerfallCorpseSave corpse in delta.Corpses)
            {
                corpse.Validate();
                if (!corpseActors.Add(corpse.ActorId))
                    throw new ArgumentException($"Saved corpse actor {corpse.ActorId} appears in more than one active or inactive site.");
                if (!containerIdentities.Add(corpse.ContainerId))
                    throw new ArgumentException($"Saved inactive corpse container identity {corpse.ContainerId} appears more than once.");
                RequireLiveCorpseContainer(savedLedger, corpse);
                ValidateInventory(new DaggerfallInventorySave(corpse.Stacks, corpse.UniqueItems, []), definitions, uniqueItems, DaggerfallItemOwner.Corpse(corpse.ActorId), requireEquipment: false);
            }
            foreach (DaggerfallActorInventorySave inventory in delta.ActorInventories)
                ValidateInventory(inventory.Inventory, definitions, uniqueItems, DaggerfallItemOwner.Actor(inventory.EntityId), requireEquipment: true);
        }
        HashSet<long> allActors = [.. savedActorIds, .. dynamicActorIds];
        if (allActors.Overlaps(inactiveAuthoredActorIds) || allActors.Overlaps(inactiveDynamicActorIds)
            || inactiveAuthoredActorIds.Overlaps(inactiveDynamicActorIds))
            throw new ArgumentException("Saved active and inactive site actors must not share durable identities.");
        if (!actorInventories.SetEquals(allActors))
            throw new ArgumentException("Current save must carry one actor inventory section for every saved actor.");
        foreach (var response in Crime.LegalResponses)
        {
            var profile = response.Profile.Require();
            if (!admittedGroundProfiles.Contains(profile) || profile.Site.Region != response.Region)
                throw new ArgumentException($"Saved legal response '{response.Id}' names an unadmitted or mismatched regional profile.");
            foreach (long guard in response.Guards)
                if (guard == DaggerfallActorIdentity.PlayerEntityId ||
                    !allActors.Contains(guard) && !inactiveAuthoredActorIds.Contains(guard) && !inactiveDynamicActorIds.Contains(guard)
                    && savedLedger.Classify(new(DurableIdentityKind.Actor, checked((ulong)guard))) != DurableIdentityClassification.Removed)
                    throw new ArgumentException($"Saved legal response '{response.Id}' names guard {guard} without an actor or removed identity.");
        }
        if (PendingIdentify?.SourceItem is ulong identifySource
            && (!Inventory.UniqueItems.Any(item => item.EntityId == identifySource)
                || !uniqueItems.TryGetValue(identifySource, out var identifyMetadata)
                || (identifyMetadata.MaximumCondition > 0 && identifyMetadata.CurrentCondition == 0)))
            throw new ArgumentException("Pending identify requires an available item source in the player inventory.");
        RequireLiveUniqueItems(savedLedger, uniqueItems.Keys);
        Encounters.Validate();
        HashSet<string> admittedEncounterProfiles = profiles is null
            ? [inputs.ProfileKey.LogicalId]
            : [.. profiles.Keys.Select(profile => profile.LogicalId)];
        if (Encounters.Resolved.Any(encounter => !admittedEncounterProfiles.Contains(encounter.ProfileId)))
            throw new ArgumentException("Saved encounter references a world profile not admitted by the current bundle.");
        foreach (DaggerfallEncounterResolution encounter in Encounters.Resolved)
        {
            if (encounter.ActorDefinition is { } definition && !definitions.Actors.ContainsKey(new DaggerfallActorId(definition)))
                throw new ArgumentException($"Saved encounter refers to missing actor definition '{definition}'.");
            if (encounter.SpawnedActorId is long actorId && !dynamicActorIds.Contains(actorId) && !inactiveDynamicActorIds.Contains(actorId))
                throw new ArgumentException($"Saved spawned encounter actor {actorId} is not a dynamic actor.");
        }
        Quests.Validate(definitions);
        foreach (var operation in Quests.Instances.SelectMany(instance => instance.Tasks).SelectMany(task => task.OperationState))
            foreach (var profile in new DaggerfallWorldProfileKey?[] { operation.FoeSpawn?.PendingProfile, operation.GuardSpawn?.Profile })
                if (profile is { } required && required != inputs.ProfileKey && profiles?.TryGet(required, out _) != true)
                    throw new ArgumentException($"Quest spawn request names unavailable admitted profile '{required.LogicalId}'.");
        foreach (var instance in Quests.Instances)
            foreach (var operation in instance.Placements.Where(value => value.Applied is not null))
            {
                var applied = operation.Applied!;
                var projected = applied.Profile == inputs.ProfileKey ? inputs
                    : (profiles ?? throw new ArgumentException("Saved quest placements require their admitted site catalog.")).Require(applied.Profile);
                var destination = DaggerfallQuestPlacements.Destination(instance.Resources, operation.PlaceSymbol);
                if (!DaggerfallQuestPlacements.Matches(destination, projected)
                    || !projected.QuestMarkers.Any(marker => marker.Id == applied.MarkerId))
                    throw new ArgumentException($"Saved quest placement '{operation.Id}' names an unavailable profile or marker.");
            }

        HashSet<(int Region, int Index)> locations = [.. definitions.Locations.Records.Select(value => (value.Region, value.Index))];
        RequireSite(Site.Active, locations, "active site");
        RequireSite(Site.ReturnAnchor, locations, "return anchor");
        foreach (DaggerfallSiteIdSave discovered in Site.Discovered)
            RequireSite(discovered, locations, "discovered site");
        HashSet<long> combatants = [DaggerfallActorIdentity.PlayerEntityId, .. allActors, .. inactiveAuthoredActorIds, .. inactiveDynamicActorIds];
        foreach (DaggerfallCombatCooldownSave cooldown in CombatCooldowns)
            if (!combatants.Contains(cooldown.AttackerId))
                throw new ArgumentException($"Saved attack cooldown refers to missing actor {cooldown.AttackerId}.");
        Dictionary<(string Scope, long OwnerId, string StackId), DaggerfallStackSave> questStacks = [];
        foreach (var custody in QuestCustody) AddQuestStacks(questStacks, DaggerfallItemOwner.Quest(custody.Id), custody.Inventory.Stacks);
        AddQuestStacks(questStacks, DaggerfallItemOwner.Player, Inventory.Stacks);
        foreach (DaggerfallCorpseSave corpse in Corpses)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Corpse(corpse.ActorId), corpse.Stacks);
        foreach (DaggerfallGroundContainerSave ground in GroundContainers)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Ground(ground.Id), ground.Inventory.Stacks);
        foreach (DaggerfallMerchantSave merchant in Merchants)
        {
            AddQuestStacks(questStacks, DaggerfallItemOwner.Merchant(merchant.MerchantContainerId), merchant.Inventory.Stacks);
            AddQuestStacks(questStacks, DaggerfallItemOwner.RepairCustody(merchant.CustodyContainerId), merchant.Custody.Stacks);
        }
        if (Wagon is { } questWagon)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Wagon(questWagon.Id), questWagon.Inventory.Stacks);
        foreach (DaggerfallActorInventorySave inventory in ActorInventories)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Actor(inventory.EntityId), inventory.Inventory.Stacks);
        foreach (var delta in SiteDeltas)
        {
            foreach (var inventory in delta.ActorInventories)
                AddQuestStacks(questStacks, DaggerfallItemOwner.Actor(inventory.EntityId), inventory.Inventory.Stacks);
            foreach (var corpse in delta.Corpses)
                AddQuestStacks(questStacks, DaggerfallItemOwner.Corpse(corpse.ActorId), corpse.Stacks);
        }
        foreach (var storage in Property.Storage)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Property(storage.ContainerId), storage.Inventory.Stacks);
        DaggerfallUniqueSave[] questUnique = [.. Inventory.UniqueItems, .. ActorInventories.SelectMany(value => value.Inventory.UniqueItems),
            .. Corpses.SelectMany(value => value.UniqueItems), .. GroundContainers.SelectMany(value => value.Inventory.UniqueItems),
            .. QuestCustody.SelectMany(value => value.Inventory.UniqueItems), .. (Wagon?.Inventory.UniqueItems ?? []),
            .. Merchants.SelectMany(value => value.Inventory.UniqueItems), .. Merchants.SelectMany(value => value.Custody.UniqueItems),
            .. Property.Storage.SelectMany(value => value.Inventory.UniqueItems), .. SiteDeltas.SelectMany(value => value.ActorInventories).SelectMany(value => value.Inventory.UniqueItems),
            .. SiteDeltas.SelectMany(value => value.Corpses).SelectMany(value => value.UniqueItems)];
        Quests.ValidateBindings(combatants, savedLedger, locations, questStacks, questUnique.ToDictionary(value => value.EntityId),
            QuestCustody.ToDictionary(value => value.Id, value => value.InstanceId), savedNpcs, definitions, BanishedActors.Concat(SiteDeltas.SelectMany(delta => delta.BanishedActors)).ToHashSet());
        DaggerfallActiveEffectSave[] allEffects = [.. ActiveEffects, .. SiteDeltas.SelectMany(delta => delta.Effects)];
        if (uniqueItems.Values.Any(item => item.HealthLeechLastUsedMinute > new World.DaggerfallCalendar(Calendar.Year, Calendar.Month, Calendar.Day, Calendar.Hour, Calendar.Minute, Calendar.Second).ToAbsoluteSeconds() / 60))
            throw new ArgumentException("Saved health-leech last use is later than the current calendar.");
        ValidateActiveEffects(allEffects, combatants, uniqueItems, definitions.Magic);
        var racialEffects = allEffects.Where(effect => effect.EffectKey == DaggerfallRacialOverrides.EffectKey).ToArray();
        if (racialEffects.Length > 1) throw new ArgumentException("Saved player has multiple racial overrides.");
        foreach (var racial in racialEffects)
        {
            if (racial.TargetId != DaggerfallActorIdentity.PlayerEntityId || racial.RemainingRounds is not null || racial.ItemId is not null)
                throw new ArgumentException("Saved racial override must be a permanent player effect.");
            if (Character?.SpellGrants?.Count(grant => grant.Source == racial.Instance && grant.Spell == "spell.085"
                && grant.Kind == DaggerfallSpellGrantKind.Lycanthropy) != 1)
                throw new ArgumentException("Saved racial override is missing its protected transformation spell.");
        }
        foreach (var grant in Character?.SpellGrants ?? [])
            if (!Enum.IsDefined(grant.Kind) || !racialEffects.Any(effect => effect.Instance == grant.Source))
                throw new ArgumentException($"Saved spell grant '{grant.Spell}' has no matching active racial source '{grant.Source}'.");
        Social.Validate(definitions.Factions);
        Character?.Validate(definitions);
        if (ReadySpell is { } ready && (!Enum.IsDefined(ready.Source) || ready.Cost < 0 || ready.ItemId is not null && ready.Cost != 0
            || ready.Source is DaggerfallCastSource.ItemHeld or DaggerfallCastSource.ItemStrike
            || (ready.ItemId is null) != (ready.Source is DaggerfallCastSource.Spell or DaggerfallCastSource.DungeonAction)
            || !definitions.Magic.Spells.ContainsKey(ready.SpellKey)
            || ready.Source == DaggerfallCastSource.DungeonAction
                && DaggerfallMagicCostPolicy.TargetForRangeType(definitions.Magic.Spells[ready.SpellKey].RangeType) != DaggerfallSpellTarget.CasterOnly
            || ready.Source == DaggerfallCastSource.DungeonAction && ready.Cost != 0
            || ready.Source != DaggerfallCastSource.DungeonAction && ready.ItemId is null && Character?.KnownSpells?.Contains(ready.SpellKey) != true
            || ready.ItemId is ulong item && !Inventory.UniqueItems.Any(value => value.EntityId == item
                && value.Metadata.CurrentCondition > 0)))
            throw new ArgumentException($"Saved ready spell '{ready.SpellKey}' has an invalid spell or item source.");

        ValidateEffectSourceReferences(
        [
            (DaggerfallActorIdentity.PlayerEntityId, Player.Stats),
            .. Actors.Select(actor => (actor.EntityId, actor.Stats)),
            .. DynamicActors.Select(actor => (actor.EntityId, actor.Stats)),
            .. SiteDeltas.SelectMany(delta => delta.Actors).Select(actor => (actor.EntityId, actor.Stats)),
            .. SiteDeltas.SelectMany(delta => delta.DynamicActors).Select(actor => (actor.EntityId, actor.Stats)),
        ],
        allEffects, Inventory, definitions.Magic);
        IReadOnlySet<long> tombstonedActors = savedLedger.RemovedIdentities(DurableIdentityKind.Actor)
            .Select(value => checked((long)value))
            .ToHashSet();
        return new DaggerfallResolvedRestore(this, savedLedger, tombstonedActors, definitions);
    }

    internal DurableIdentityState RestoredIdentities() => Identities.Validate().RequireKinds(PersistedKinds);

    private static void ValidateBanished(long[] removed, IEnumerable<long> placements, IEnumerable<long> live)
    {
        ArgumentNullException.ThrowIfNull(removed);
        HashSet<long> admitted = [.. placements], active = [.. live];
        if (removed.Distinct().Count() != removed.Length || removed.Any(id => !admitted.Contains(id) || active.Contains(id)))
            throw new ArgumentException("Banished actors must name unique admitted placements absent from the live actor state.");
    }

    internal DaggerfallSavePayload Validate()
    {
        if (MagicRounds < 0) throw new ArgumentException("Saved magic-round cadence cannot be negative.");
        if (NextCastSequence < 1) throw new ArgumentException("Saved next cast sequence must be positive.");
        ArgumentNullException.ThrowIfNull(Player);
        PendingDispel?.Validate();
        if (PendingTeleport is not null && string.IsNullOrWhiteSpace(PendingTeleport)) throw new ArgumentException("Saved teleport choice requires its paid cast identity.");
        TeleportAnchor?.Validate();
        PendingIdentify?.Validate();
        PendingCreateItem?.Validate();
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(DynamicActors);
        if(Actors.Any(actor=>actor.ForcedHostile && actor.MagicallyPacified) || DynamicActors.Any(actor=>actor.ForcedHostile && actor.MagicallyPacified))
            throw new ArgumentException("An actor cannot be forced hostile and magically pacified together.");
        ArgumentNullException.ThrowIfNull(Inventory);
        ArgumentNullException.ThrowIfNull(Corpses);
        ArgumentNullException.ThrowIfNull(GroundContainers);
        ArgumentNullException.ThrowIfNull(QuestCustody);
        ArgumentNullException.ThrowIfNull(Services);
        Services.Validate();
        ArgumentNullException.ThrowIfNull(Merchants);
        foreach (DaggerfallMerchantSave merchant in Merchants)
        {
            ArgumentNullException.ThrowIfNull(merchant);
            merchant.Validate();
        }
        ArgumentNullException.ThrowIfNull(QuestTraining);
        QuestTraining.Validate();
        ArgumentNullException.ThrowIfNull(RegionalPrices);
        RegionalPrices.Validate();
        ArgumentNullException.ThrowIfNull(Transport);
        Transport.Validate();
        Wagon?.Validate();
        ArgumentNullException.ThrowIfNull(DialogueWorld);
        DialogueWorld.Validate();
        ArgumentNullException.ThrowIfNull(Notebook);
        Notebook.Validate();
        ArgumentNullException.ThrowIfNull(Identities);
        ArgumentNullException.ThrowIfNull(CombatCooldowns);
        ArgumentNullException.ThrowIfNull(Calendar);
        ArgumentNullException.ThrowIfNull(Weather);
        Weather.Validate();
        if (Weather.NextDay != checked(new DaggerfallCalendar(Calendar.Year, Calendar.Month, Calendar.Day, Calendar.Hour, Calendar.Minute, Calendar.Second).DayNumber + 1))
            throw new ArgumentException("Saved weather boundary does not follow the saved calendar day.");
        ArgumentNullException.ThrowIfNull(Site);
        ArgumentNullException.ThrowIfNull(ActorInventories);
        ArgumentNullException.ThrowIfNull(Variables);
        Variables.Validate();
        ArgumentNullException.ThrowIfNull(Npcs);
        Npcs.Validate();
        ArgumentNullException.ThrowIfNull(ActiveEffects);
        ArgumentNullException.ThrowIfNull(Infections);
        Infections.Validate();
        long infectionDay = new DaggerfallCalendar(Calendar.Year, Calendar.Month, Calendar.Day, Calendar.Hour, Calendar.Minute, Calendar.Second).DayNumber;
        if (Infections.LastOutcomes.Any(value => value.Day > infectionDay))
            throw new ArgumentException("Saved infection cleanup is later than the calendar.");
        foreach (var effect in ActiveEffects) DaggerfallTransformationInfectionPolicy.ValidateSaved(effect, infectionDay);
        ArgumentNullException.ThrowIfNull(SkillUses);
        SkillUses.Validate();
        ArgumentNullException.ThrowIfNull(Social);
        Social.Validate();
        ArgumentNullException.ThrowIfNull(Quests);
        Quests.Validate();
        ArgumentNullException.ThrowIfNull(Doors);
        foreach (DaggerfallDoorSave door in Doors) { ArgumentNullException.ThrowIfNull(door); door.Validate(); }
        ArgumentNullException.ThrowIfNull(DungeonMotion);
        DungeonMotion.Validate();
        if (ExteriorResidency is { } exterior)
        {
            DaggerfallExteriorWorldBounds.Daggerfall.Require(exterior.Center, nameof(ExteriorResidency));
            DaggerfallExteriorWorldBounds.Daggerfall.Require(exterior.Origin, nameof(ExteriorResidency));
            if (!float.IsFinite(exterior.CompensationX) || !float.IsFinite(exterior.CompensationY)
                || !float.IsFinite(exterior.CompensationZ))
                throw new ArgumentException("Saved exterior origin compensation must be finite.", nameof(ExteriorResidency));
            if (Site.ActiveProfile?.Require().Kind != DaggerfallWorldProfileKind.Exterior)
                throw new ArgumentException("Saved exterior residency requires an active exterior profile.", nameof(ExteriorResidency));
        }
        if (ExteriorLocationResidency is { } location)
        {
            location.Validate();
            if (Site.ActiveProfile?.Require() != location.Profile.Require())
                throw new ArgumentException("Saved exterior location residency must name the active profile.", nameof(ExteriorLocationResidency));
            DaggerfallExteriorWorldBounds.Daggerfall.Require(location.Cell, nameof(ExteriorLocationResidency));
            if (Site.ActiveProfile?.Require().Kind != DaggerfallWorldProfileKind.Exterior)
                throw new ArgumentException("Saved exterior location residency requires an active exterior profile.", nameof(ExteriorLocationResidency));
        }
        ArgumentNullException.ThrowIfNull(SiteDeltas);
        foreach (DaggerfallSiteDeltaSave delta in SiteDeltas) delta.Validate();
        ArgumentNullException.ThrowIfNull(Currency);
        Currency.Validate();
        ArgumentNullException.ThrowIfNull(Bank);
        Bank.Validate();
        ArgumentNullException.ThrowIfNull(Loans);
        Loans.Validate();
        ArgumentNullException.ThrowIfNull(Property);
        Property.Validate();
        ArgumentNullException.ThrowIfNull(Lodging);
        Lodging.Validate();
        ArgumentNullException.ThrowIfNull(Travel);
        Travel.Validate();
        ArgumentNullException.ThrowIfNull(Crime);
        Crime.Validate();
        ArgumentNullException.ThrowIfNull(KnightlyClaims);
        KnightlyClaims.Validate();
        ulong regionalBankTotal = 0;
        foreach (DaggerfallBankAccountSave account in Bank.Accounts)
            regionalBankTotal = checked(regionalBankTotal + account.Gold);
        if (regionalBankTotal != Currency.AccountGold)
            throw new ArgumentException("Saved regional bank accounts must sum to the currency settlement account.", nameof(Bank));
        ArgumentNullException.ThrowIfNull(Locomotion);
        Locomotion.Validate();
        ArgumentNullException.ThrowIfNull(Climbing);
        Climbing.Validate();
        ArgumentNullException.ThrowIfNull(Swimming);
        Swimming.Validate();
        ArgumentNullException.ThrowIfNull(DungeonDiscovery);
        foreach (DaggerfallDungeonDiscoverySnapshot snapshot in DungeonDiscovery) ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(DungeonActions);
        foreach (DaggerfallDungeonActionGraphSnapshot snapshot in DungeonActions)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            snapshot.Validate();
        }
        ArgumentNullException.ThrowIfNull(DungeonText);
        DungeonText.Validate();
        ArgumentNullException.ThrowIfNull(Encounters);
        Encounters.Validate();
        ArgumentNullException.ThrowIfNull(Character);
        LevelUp?.Validate();
        if (LevelUp is not null && !LevelUp.MatchesProgressionLevel(Level))
            throw new ArgumentException("Saved Daggerfall attribute allocation does not match its progression level.", nameof(LevelUp));
        if (Experience < 0 || Level < 1)
            throw new ArgumentOutOfRangeException(nameof(Experience), "Saved progression must be non-negative and begin at level one.");
        if (!double.IsFinite(Calendar.RemainderSeconds) || Calendar.RemainderSeconds < 0d || Calendar.RemainderSeconds >= 1d)
            throw new ArgumentException("Saved calendar remainder must be a fraction of one second.");
        if (Travel.LastResult is { } journey && journey.EndedSeconds > new DaggerfallCalendar(Calendar.Year, Calendar.Month, Calendar.Day, Calendar.Hour, Calendar.Minute, Calendar.Second).ToAbsoluteSeconds())
            throw new ArgumentException("Saved journey ends after the saved calendar.");
        Player.Validate();
        ValidateStats(Player.Stats, "player");
        HashSet<long> actorIds = [];
        foreach (DaggerfallActorSave actor in Actors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            actor.Validate();
            if (actor.EntityId <= 0 || !actorIds.Add(actor.EntityId))
                throw new ArgumentException("Saved actor identities must be positive and unique.");
            ValidateStats(actor.Stats, $"actor {actor.EntityId}");
        }
        foreach (DaggerfallDynamicActorSave actor in DynamicActors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            actor.Validate();
            if (actor.EntityId <= 0 || !actorIds.Add(actor.EntityId))
                throw new ArgumentException("Saved dynamic actor identities must be positive and unique.");
            ValidateStats(actor.Stats, $"dynamic actor {actor.EntityId}");
        }
        Inventory.Validate();
        HashSet<long> corpseActors = [];
        HashSet<ulong> containerIdentities = [];
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            ArgumentNullException.ThrowIfNull(corpse);
            if (corpse.ActorId <= 0 || !corpseActors.Add(corpse.ActorId))
                throw new ArgumentException("Saved corpse actor identities must be positive and unique.");
            corpse.Validate();
            if (!containerIdentities.Add(corpse.ContainerId))
                throw new ArgumentException("Saved corpse container identities must be positive and unique.");
        }
        foreach (DaggerfallGroundContainerSave ground in GroundContainers)
        {
            ArgumentNullException.ThrowIfNull(ground);
            ground.Validate();
            if (!containerIdentities.Add(checked((ulong)ground.Id)))
                throw new ArgumentException("Saved container identities must be distinct across corpses and ground drops.");
        }
        if (Wagon is { } savedWagon && !containerIdentities.Add(checked((ulong)savedWagon.Id)))
            throw new ArgumentException("Saved wagon identity must be distinct from corpse and ground containers.");
        foreach (DaggerfallPropertyStorageSave storage in Property.Storage)
            if (!containerIdentities.Add(checked((ulong)storage.ContainerId)))
                throw new ArgumentException($"Saved property storage container {storage.ContainerId} collides with another container.");
        foreach (DaggerfallSiteDeltaSave delta in SiteDeltas)
        foreach (DaggerfallCorpseSave corpse in delta.Corpses)
            if (!containerIdentities.Add(corpse.ContainerId))
                throw new ArgumentException("Saved inactive corpse container identities must be distinct from active containers and each other.");
        HashSet<string> custodyQuests = new(StringComparer.Ordinal);
        foreach (var custody in QuestCustody)
        {
            if (custody.Id <= 0 || string.IsNullOrWhiteSpace(custody.InstanceId) || !custodyQuests.Add(custody.InstanceId)
                || !Quests.Instances.Any(value => value.InstanceId == custody.InstanceId && value.Lifecycle == DaggerfallQuestLifecycle.Active)) throw new ArgumentException("Quest custody requires a distinct known quest and positive owner identity.");
            ArgumentNullException.ThrowIfNull(custody.Inventory);
            custody.Inventory.Validate();
            if (custody.Inventory.Equipment.Length != 0) throw new ArgumentException("Quest custody cannot equip items.");
            if (!containerIdentities.Add(checked((ulong)custody.Id))) throw new ArgumentException("Quest custody identity collides with another container.");
        }
        HashSet<string> merchantKeys = new(StringComparer.Ordinal);
        foreach (DaggerfallMerchantSave merchant in Merchants)
        {
            if (!merchantKeys.Add(merchant.Key)) throw new ArgumentException("Saved merchant providers must have distinct keys.");
            if (!containerIdentities.Add(checked((ulong)merchant.MerchantContainerId)
                ) || !containerIdentities.Add(checked((ulong)merchant.CustodyContainerId)))
                throw new ArgumentException("Saved merchant container identities must be distinct from every other container.");
        }
        HashSet<long> inventoryActors = [];
        foreach (DaggerfallActorInventorySave inventory in ActorInventories)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            if (inventory.EntityId <= 0 || !inventoryActors.Add(inventory.EntityId))
                throw new ArgumentException("Saved actor inventories must name distinct positive actors.");
            inventory.Validate();
        }
        HashSet<long> cooldownActors = [];
        foreach (DaggerfallCombatCooldownSave cooldown in CombatCooldowns)
        {
            ArgumentNullException.ThrowIfNull(cooldown);
            if (cooldown.AttackerId <= 0 || cooldown.RemainingSteps == 0 || !cooldownActors.Add(cooldown.AttackerId))
                throw new ArgumentException("Saved attack cooldowns must name distinct actors and positive remaining steps.");
        }
        foreach (DaggerfallActiveEffectSave effect in ActiveEffects)
        {
            ArgumentNullException.ThrowIfNull(effect);
            effect.Validate();
        }
        Identities.Validate().RequireKinds(PersistedKinds);
        Site.Validate();
        return this;
    }

    private static void ValidateStats(DaggerfallStatsSave stats, string owner)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(stats.Snapshot);
        ArgumentNullException.ThrowIfNull(stats.Sources);
        _ = DaggerfallStatsSaveBoundary.Restore(stats, default);
    }

    private static void ValidateDungeonMotion(DaggerfallSiteProfile profile, DaggerfallDungeonMotionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);
        Dictionary<string, DaggerfallDungeonActionDefinition> actions = profile.DungeonActions
            .ToDictionary(action => action.Id, StringComparer.Ordinal);
        List<(string ActionId, double DurationSeconds)> expected = [];
        foreach (DaggerfallDungeonActionModelDefinition model in profile.DungeonActionModels)
        {
            if (model.DoorIdentity is not null) continue;
            if (!actions.TryGetValue(model.ActionId, out DaggerfallDungeonActionDefinition? action))
                throw new ArgumentException($"Selected action model '{model.ActionId}' has no world action definition.");
            if (DaggerfallDungeonMotionPolicy.TryInterpret(action, model.Description, out DaggerfallDungeonMotionSpecification specification))
                expected.Add((action.Id, specification.DurationSeconds));
        }
        DaggerfallDungeonMotionRuntime.ValidateSnapshot(profile.ProfileKey.LogicalId, expected, snapshot);
    }

    private static void RequireSite(DaggerfallSiteIdSave? id, HashSet<(int Region, int Index)> locations, string owner)
    {
        if (id is null) return;
        DaggerfallSiteId site = id.Require();
        if (!locations.Contains((site.Region, site.Index)))
            throw new ArgumentException($"Saved {owner} {site} is not defined by the selected content.");
    }

    /// <summary>Every materialized unique item must already be live in the persisted item ledger.</summary>
    private static void RequireLiveUniqueItems(DurableIdentityAllocator identities, IEnumerable<ulong> uniqueItems)
    {
        foreach (ulong itemId in uniqueItems)
        {
            DurableIdentityClassification classification = identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, itemId));
            if (classification != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved unique item '{itemId}' is {classification} in the persisted item identity ledger.");
        }
    }

    private static void RequireLiveCorpseContainer(DurableIdentityAllocator identities, DaggerfallCorpseSave corpse)
    {
        DurableIdentityReference identity = new(DurableIdentityKind.Container, corpse.ContainerId);
        if (identities.Classify(identity) != DurableIdentityClassification.Live)
            throw new ArgumentException($"Saved corpse {corpse.ActorId} container {corpse.ContainerId} is not live in the persisted identity ledger.");
    }

    private static bool IsAdmittedDynamicDefinition(DaggerfallDefinitions definitions, string definition) =>
        !string.IsNullOrWhiteSpace(definition)
        && (definition is DaggerfallActorKinds.Civilian or DaggerfallActorKinds.StaticNpc
            || definitions.Actors.ContainsKey(new DaggerfallActorId(definition)));

    private static void ValidateInventory(DaggerfallInventorySave inventory, DaggerfallDefinitions definitions, Dictionary<ulong, DaggerfallItemMetadataSave> allUnique, DaggerfallItemOwner owner, bool requireEquipment)
    {
        inventory.Validate();
        foreach (var item in inventory.UniqueItems.Where(value => value.Metadata.HeldCast is not null))
        {
            var held = item.Metadata.HeldCast!;
            if (held.CasterId != owner.Id || !requireEquipment || item.Metadata.CurrentCondition <= 0
                || !inventory.Equipment.Any(slot => slot.ItemEntityId == item.EntityId)
                || !definitions.Magic.TryEnchantments(item.Metadata, out var payloads)
                || !payloads.Any(effect => effect.Type == 1))
                throw new ArgumentException($"Saved held spell cadence on item {item.EntityId} has no equipped cast source.");
        }
        foreach (DaggerfallStackSave stack in inventory.Stacks) RequireFungible(definitions, stack, owner);
        Dictionary<ulong, DaggerfallItemDefinition> unique = [];
        foreach (DaggerfallUniqueSave saved in inventory.UniqueItems)
        {
            if (!definitions.TryResolveItem(new DaggerfallItemId(saved.ItemId), out DaggerfallItemDefinition definition) || definition.IsFungible || !allUnique.TryAdd(saved.EntityId, saved.Metadata))
                throw new ArgumentException($"Saved {owner.Scope} {owner.Id} unique item '{saved.EntityId}' is missing, incompatible, or duplicated.");
            RequireMetadata(definitions, saved.ItemId, saved.Metadata, owner);
            unique.Add(saved.EntityId, definition);
        }
        if (!requireEquipment && inventory.Equipment.Length != 0)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} cannot equip items.");
        foreach (IGrouping<ulong, DaggerfallEquipmentSave> group in inventory.Equipment.GroupBy(value => value.ItemEntityId))
        {
            if (!unique.TryGetValue(group.Key, out DaggerfallItemDefinition? item) || item.Equipment is null)
                throw new ArgumentException($"Saved {owner} equipment refers to unknown unique item {group.Key}.");
            DaggerfallItemMetadataSave metadata = inventory.UniqueItems.Single(value => value.EntityId == group.Key).Metadata;
            if (metadata.MaximumCondition > 0 && metadata.CurrentCondition == 0)
                throw new ArgumentException($"Saved equipment refers to broken unique item {group.Key}.");
            foreach (DaggerfallEquipmentSave equipped in group)
            {
                if (!definitions.EquipmentSlots.TryGetValue(new DaggerfallEquipmentSlotId(equipped.SlotId), out DaggerfallEquipmentSlotDefinition? slot)
                    || !item.Equipment.Classifications.Any(classification => slot.AllowedClassifications.Contains(classification)))
                    throw new ArgumentException($"Saved equipment slot '{equipped.SlotId}' is incompatible with unique item {group.Key}.");
            }
        }
    }

    private static void RequireFungible(DaggerfallDefinitions definitions, DaggerfallStackSave stack, DaggerfallItemOwner owner)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(stack.ItemId), out DaggerfallItemDefinition definition) || !definition.IsFungible)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} stack '{stack.ItemId}' is not a selected fungible item.");
        if (stack.Quantity > definition.MaximumQuantity)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} stack '{stack.ItemId}' exceeds its authored maximum quantity.");
        DaggerfallItemInstanceMetadata metadata = RequireMetadata(definitions, stack.ItemId, stack.Metadata, owner);
        if (metadata.Enchantment is not null || metadata.MadeEnchantment is not null)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} stack '{stack.ItemId}' cannot carry an enchantment.");
    }

    private static void AddQuestStacks(Dictionary<(string Scope, long OwnerId, string StackId), DaggerfallStackSave> target, DaggerfallItemOwner owner, IEnumerable<DaggerfallStackSave> stacks)
    {
        foreach (DaggerfallStackSave stack in stacks)
            if (!target.TryAdd((owner.Scope, owner.Id, stack.StackId), stack))
                throw new ArgumentException($"Saved stack '{stack.StackId}' appears more than once for {owner.Scope} {owner.Id}.");
    }

    private static DaggerfallItemInstanceMetadata RequireMetadata(DaggerfallDefinitions definitions, string itemId, DaggerfallItemMetadataSave metadata, DaggerfallItemOwner owner)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        DaggerfallItemInstanceMetadata restored = DaggerfallItemInstanceMetadata.Restore(itemId, metadata);
        if (restored.Owner != owner)
            throw new ArgumentException($"Saved item '{itemId}' has metadata ownership {restored.Owner.Scope} {restored.Owner.Id}, not {owner.Scope} {owner.Id}.");
        bool isBook = definitions.TryResolveItem(new DaggerfallItemId(itemId), out DaggerfallItemDefinition definition)
            && definition.Template?.Groups.Contains("Books", StringComparer.Ordinal) == true;
        if (restored.BookId is int bookId)
        {
            if (!isBook)
                throw new ArgumentException($"Saved item '{itemId}' carries book identity {bookId}, but is not a book item.");
            if (!definitions.Books.Books.TryGetValue(bookId, out DaggerfallBookDefinition? book))
                throw new ArgumentException($"Saved book item '{itemId}' names unpublished book {bookId}.");
            if (book.Disposition != DaggerfallBookDisposition.Read)
                throw new ArgumentException($"Saved book item '{itemId}' names unreadable book {bookId}.");
        }
        else if (isBook)
        {
            throw new ArgumentException($"Book item '{itemId}' has no selected book identity.");
        }
        bool potionItem = definitions.RequireItem(new(itemId)).Template?.Index is 83 or 278;
        if (potionItem != (restored.PotionRecipeKey is not null)
            || restored.PotionRecipeKey is int recipe && !definitions.Magic.PotionRecipes.ContainsKey(recipe))
            throw new ArgumentException($"Saved item '{itemId}' requires its published potion recipe identity.");
        if (restored.CapturedSoulMobileId is int soul && !definitions.Actors.Values.Any(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == soul))
            throw new ArgumentException($"Saved item {itemId} names unpublished creature soul {soul}.");
        if (restored.CapturedSoulMobileId is not null && !DaggerfallSoulGems.IsTrap(restored, definitions.Magic))
            throw new ArgumentException($"Saved item {itemId} cannot hold a soul.");
        if (restored.MadeEnchantment is { } made)
        {
            made.Validate(definitions.Magic);
            var plain = restored with { MadeEnchantment = null, WeightClassicUnits = null };
            var canonical = DaggerfallEnchantmentConstruction.Quote(definitions, plain, made.Name,
                made.Settings.Where(value => value.Parent is null).Select(value => value.Key));
            if (made.Value != canonical.Enchantment.Value || !made.Settings.SequenceEqual(canonical.Enchantment.Settings)
                || restored.WeightClassicUnits != DaggerfallEnchantmentConstruction.EnchantedWeight(definitions, plain, canonical.Enchantment))
                throw new ArgumentException($"Saved made item '{itemId}' differs from its canonical enchantment value, weight, or forced settings.");
        }
        definitions.Magic.TryEnchantments(restored, out var currentPayloads);
        bool hasBoundSoul = currentPayloads.Any(value => value.Type == 15);
        if ((restored.BoundSoulReleased || restored.BoundSoulReleasePending) && !hasBoundSoul
            || restored.BoundSoulReleased && restored.BoundSoulReleasePending)
            throw new ArgumentException($"Saved item '{itemId}' has invalid bound-soul release state.");
        if (currentPayloads.Where(value => value.Type == 15).Any(value => !definitions.Actors.Values.Any(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == value.Param)))
            throw new ArgumentException($"Saved item '{itemId}' binds an unavailable creature soul.");

        if (restored.Enchantment is not { } enchantment) return restored;
        // An item maker's setting is a legitimate enchantment with no published magic item, so it is
        // stored on the item as it stands rather than being required to name a template.
        if (definitions.Magic.EnchantmentSettings.TryGetValue(enchantment, out _)) return restored;
        if (!definitions.Magic.MagicItems.ContainsKey(enchantment))
            throw new ArgumentException($"Saved item '{itemId}' names unpublished magic metadata '{enchantment}'.");
        string suffix = $"-magic-{enchantment.Replace('.', '-')}";
        bool factoryMagic = itemId.EndsWith(suffix, StringComparison.Ordinal)
            && definitions.TryResolveItem(new DaggerfallItemId(itemId[..^suffix.Length]), out _);
        bool plainEnchanted = definitions.TryResolveItem(new DaggerfallItemId(DaggerfallMagicItemIds.For(itemId, enchantment)), out _);
        if (!factoryMagic && !plainEnchanted)
            throw new ArgumentException($"Saved enchantment '{enchantment}' is incompatible with item definition '{itemId}'.");
        return restored;
    }

    /// <summary>Every effect-backed stat source must have the active effect or equipped item that owns its cleanup.</summary>
    private static void ValidateEffectSourceReferences(
        IEnumerable<(long ActorId, DaggerfallStatsSave Stats)> actors,
        IEnumerable<DaggerfallActiveEffectSave> activeEffects,
        DaggerfallInventorySave playerInventory,
        DaggerfallMagicCatalogSet magic)
    {
        HashSet<(string Instance, long Target)> active = activeEffects
            .Select(effect => (effect.Instance, effect.TargetId))
            .ToHashSet();
        foreach ((long actorId, DaggerfallStatsSave stats) in actors)
        foreach (DaggerfallStatSourceSave source in stats.Sources)
        {
            if (source.Identity.Kind != DaggerfallStatSourceIdentityKind.Effect) continue;
            if (actorId == DaggerfallActorIdentity.PlayerEntityId
                && DaggerfallHeldEnchantments.OwnsSavedSource(source, playerInventory, magic)) continue;
            if (!active.Contains((source.Identity.InstanceId, actorId)))
                throw new ArgumentException($"Saved effect source '{source.Identity.InstanceId}' on actor {actorId} has no matching active effect cleanup owner.");
        }
    }

    private static void ValidateActiveEffects(IEnumerable<DaggerfallActiveEffectSave> effects, ISet<long> actors, IReadOnlyDictionary<ulong, DaggerfallItemMetadataSave> uniqueItems, DaggerfallMagicCatalogSet magic)
    {
        DaggerfallActiveEffectSave[] savedEffects = effects.ToArray();
        foreach (var item in uniqueItems.Where(value => value.Value.HeldCast is not null))
        foreach (string instance in item.Value.HeldCast!.ActiveEffectInstances)
            if (!savedEffects.Any(effect => effect.Instance == instance && effect.ItemId == item.Key
                && effect.BundleKind == DaggerfallEffectBundleKind.HeldMagicItem && effect.CasterId == item.Value.HeldCast.CasterId))
                throw new ArgumentException($"Saved held source {item.Key} names missing active effect '{instance}'.");
        HashSet<string> instances = new(StringComparer.Ordinal);
        foreach (DaggerfallActiveEffectSave effect in savedEffects)
        {
            effect.Validate();
            if (!instances.Add(effect.Instance)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' appears more than once.");
            if (!actors.Contains(effect.TargetId)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' targets missing actor {effect.TargetId}.");
            if (effect.CasterId is long caster && !actors.Contains(caster)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' names missing caster {caster}.");
            if (effect.BundleKind == DaggerfallEffectBundleKind.HeldMagicItem && effect.ItemId is null)
                throw new ArgumentException($"Saved held effect '{effect.Instance}' has no item source.");
            if (effect.ItemId is ulong item)
            {
                if (!uniqueItems.TryGetValue(item, out var metadata)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' names missing item {item}.");
                if (effect.BundleKind == DaggerfallEffectBundleKind.HeldMagicItem
                    && (effect.CasterId is null || effect.TargetId != effect.CasterId || effect.RemainingRounds is not null
                        || metadata.HeldCast is null && magic.TryEnchantments(metadata, out var payloads) && payloads.Any(value => value.Type == 1)
                        || metadata.HeldCast is { } held && (effect.CasterId != held.CasterId || !held.ActiveEffectInstances.Contains(effect.Instance))))
                    throw new ArgumentException($"Saved held effect '{effect.Instance}' has no matching held source or lifetime.");
                if (metadata.MaximumCondition > 0 && metadata.CurrentCondition == 0)
                    throw new ArgumentException($"Saved effect instance '{effect.Instance}' names broken item {item}.");
            }
        }
    }
}

/// <summary>One placed actor's full Engine inventory meaning, including unique instances and equipment.</summary>
internal sealed record DaggerfallActorInventorySave(long EntityId, DaggerfallInventorySave Inventory)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Inventory);
        Inventory.Validate();
    }
}

internal sealed record DaggerfallInventorySave(DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems, DaggerfallEquipmentSave[] Equipment)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        ArgumentNullException.ThrowIfNull(Equipment);
        HashSet<string> stackIds = [];
        foreach (DaggerfallStackSave stack in Stacks)
            if (stack is null || string.IsNullOrWhiteSpace(stack.StackId) || string.IsNullOrWhiteSpace(stack.ItemId) || stack.Quantity == 0 || !stackIds.Add(stack.StackId))
                throw new ArgumentException("Inventory stacks must have distinct explicit identities, definitions, and positive quantities.");
            else _ = DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata);
        HashSet<ulong> items = [];
        foreach (DaggerfallUniqueSave unique in UniqueItems)
            if (unique is null || string.IsNullOrWhiteSpace(unique.ItemId) || unique.EntityId == 0 || !items.Add(unique.EntityId))
                throw new ArgumentException("Unique inventory entries must be valid and distinct.");
            else _ = DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata);
        HashSet<string> slots = [];
        foreach (DaggerfallEquipmentSave equipment in Equipment)
            if (equipment is null || string.IsNullOrWhiteSpace(equipment.SlotId) || !slots.Add(equipment.SlotId) || !items.Contains(equipment.ItemEntityId))
                throw new ArgumentException("Equipment must refer to a saved unique item once per slot.");
    }
}

/// <summary>One Engine-backed stack preserved by its product-selected owner-scoped identity.</summary>
internal sealed record DaggerfallStackSave(string StackId, string ItemId, ulong Quantity, DaggerfallItemMetadataSave Metadata);
internal sealed record DaggerfallUniqueSave(string ItemId, ulong EntityId, DaggerfallItemMetadataSave Metadata);
internal sealed record DaggerfallItemOwnerSave(string Scope, long Id);
internal sealed record DaggerfallItemMetadataSave(
    string Material,
    int Variant,
    int CurrentCondition,
    int MaximumCondition,
    bool Identified,
    bool Stolen,
    string? QuestId,
    string? QuestItemSymbol,
    string? Enchantment,
    DaggerfallItemOwnerSave Owner,
    string? Race = null,
    string? Gender = null,
    string? Dye = null,
    int? BookId = null,
    int? PotionRecipeKey = null,
    ulong? CreditValue = null,
    int? PoisonVariant = null, DaggerfallHeldCastState? HeldCast = null, long HealthLeechLastUsedMinute = 0, int? CapturedSoulMobileId = null, DaggerfallConjuredItem? Conjuration = null,
    DaggerfallMadeEnchantment? MadeEnchantment = null, ulong? WeightClassicUnits = null, bool BoundSoulReleased = false, bool BoundSoulReleasePending = false);
internal sealed record DaggerfallEquipmentSave(string SlotId, ulong ItemEntityId);
internal sealed record DaggerfallCombatCooldownSave(long AttackerId, ulong RemainingSteps);

/// <summary>Stable current-state data for one Daggerfall active effect; compiled policy rebuilds its behavior.</summary>
internal sealed record DaggerfallActiveEffectSave(
    string Instance,
    string EffectKey,
    string Source,
    long? CasterId,
    long TargetId,
    string Settings,
    string? Element,
    ulong? ItemId,
    uint? RemainingRounds,
    ushort Stacks,
    JsonElement State)
{
    public string? BundleId { get; init; }
    public string? BundleName { get; init; }
    public DaggerfallEffectBundleKind BundleKind { get; init; }
    public long BundleSequence { get; init; }
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(EffectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(Settings);
        if (BundleSequence < 0) throw new ArgumentOutOfRangeException(nameof(BundleSequence));
        if (!Enum.IsDefined(BundleKind)) throw new ArgumentException("Saved effect bundle kind is not recognized.");
        if (TargetId <= 0 || CasterId is <= 0 || ItemId == 0 || Stacks == 0)
            throw new ArgumentOutOfRangeException(nameof(TargetId), "Saved effect identities and stacks must be positive.");
        if (State.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Saved effect state must be explicit.", nameof(State));
    }

    internal DaggerfallEffectRequest ToRequest() => new(
        Instance, EffectKey, Source, CasterId, TargetId, Settings, Element, ItemId, Stacks, RemainingRounds, State.Clone()) { BundleId = BundleId, BundleName = BundleName, BundleKind = BundleKind, BundleSequence = BundleSequence };
}

internal sealed record DaggerfallCorpseSave(long ActorId, ulong ContainerId, ulong OriginatingSequence, bool IsRegistered, bool IsInteractable, DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        if (ActorId <= 0 || ContainerId == 0)
            throw new ArgumentOutOfRangeException(nameof(ContainerId), "A saved corpse must name positive actor and container identities.");
        if (!IsRegistered && (Stacks.Length != 0 || UniqueItems.Length != 0))
            throw new ArgumentException("An unregistered corpse cannot contain inventory.");
        new DaggerfallInventorySave(Stacks, UniqueItems, []).Validate();
    }
}

/// <summary>One persistent dropped-item container at a world position.</summary>
internal sealed record DaggerfallGroundContainerSave(DaggerfallWorldProfileKeySave Profile, long Id, float X, float Y, float Z, DaggerfallInventorySave Inventory, string? PropertyPlacement = null, long StockedDay = 0)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Profile);
        Profile.Validate();
        if (Id <= 0 || !float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new ArgumentException("Ground containers require a positive identity and finite position.");
        if (StockedDay < -1 || PropertyPlacement is not null && string.IsNullOrWhiteSpace(PropertyPlacement))
            throw new ArgumentException("Property containers require a valid placement and stock date.");
        ArgumentNullException.ThrowIfNull(Inventory);
        Inventory.Validate();
    }
}

internal sealed record DaggerfallCalendarSave(int Year, int Month, int Day, int Hour, int Minute, int Second, double RemainderSeconds);

internal sealed record DaggerfallSiteIdSave(int? Region, int? Index)
{
    internal void Validate(string owner)
    {
        if (Region is not int region || Index is not int index || region < 0 || index < 0)
            throw new ArgumentException($"A saved {owner} must name a non-negative region and index.");
    }

    internal DaggerfallSiteId Require()
    {
        Validate("site");
        return new DaggerfallSiteId(Region!.Value, Index!.Value);
    }
}

/// <summary>The exact player pose to restore when a site transition returns to its source.</summary>
internal sealed record DaggerfallSiteReturnPoseSave(float X, float Y, float Z, float YawRadians, float PitchRadians)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new ArgumentException("A saved site return position must be finite.");
        if (!float.IsFinite(YawRadians) || !float.IsFinite(PitchRadians))
            throw new ArgumentException("A saved site return look must be finite.");
    }
}

internal sealed record DaggerfallSiteSave(
    DaggerfallSiteIdSave? Active,
    DaggerfallSiteIdSave? ReturnAnchor,
    DaggerfallSiteIdSave[] Discovered,
    DaggerfallSiteReturnPoseSave? ReturnPose = null,
    DaggerfallWorldProfileKeySave? ActiveProfile = null,
    DaggerfallWorldProfileKeySave? ReturnProfile = null)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Discovered);
        Active?.Validate("active site");
        ReturnAnchor?.Validate("return anchor");
        if (ReturnAnchor is not null && Active is null)
            throw new ArgumentException("A saved return anchor requires an active site.");
        if (ReturnAnchor is null && ReturnPose is not null)
            throw new ArgumentException("A saved return pose requires a return anchor.");
        if (ReturnAnchor is not null && ReturnPose is null)
            throw new ArgumentException("A saved return anchor requires its exact return pose.");
        if (ReturnProfile is not null && ReturnAnchor is null)
            throw new ArgumentException("A saved return projection requires its geographic return anchor.");
        ReturnPose?.Validate();
        ActiveProfile?.Validate();
        ReturnProfile?.Validate();
        if (ActiveProfile is not null && Active is not null && ActiveProfile.Site.Require() != Active.Require())
            throw new ArgumentException("Saved active projection must belong to the saved active site.");
        if (ReturnProfile is not null && ReturnAnchor is not null && ReturnProfile.Site.Require() != ReturnAnchor.Require())
            throw new ArgumentException("Saved return projection must belong to the saved return site.");
        HashSet<(int Region, int Index)> seen = [];
        foreach (DaggerfallSiteIdSave discovered in Discovered)
        {
            ArgumentNullException.ThrowIfNull(discovered);
            discovered.Validate("discovered site");
            if (!seen.Add((discovered.Region!.Value, discovered.Index!.Value)))
                throw new ArgumentException("A save must record each discovered site once.");
        }
    }
}

/// <summary>Persisted projection identity; its geographic site remains separately owned by DaggerfallSiteContext.</summary>
internal sealed record DaggerfallWorldProfileKeySave(DaggerfallSiteIdSave Site, int Kind, string LogicalId)
{
    internal DaggerfallWorldProfileKey Require()
    {
        Validate();
        return new DaggerfallWorldProfileKey(Site.Require(), (DaggerfallWorldProfileKind)Kind, LogicalId).Validate();
    }
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Site); Site.Validate("world profile site");
        if (!Enum.IsDefined((DaggerfallWorldProfileKind)Kind) || string.IsNullOrWhiteSpace(LogicalId))
            throw new ArgumentException("Saved world profile identity is malformed.");
    }
    internal static DaggerfallWorldProfileKeySave Capture(DaggerfallWorldProfileKey key) =>
        new(new DaggerfallSiteIdSave(key.Site.Region, key.Site.Index), (int)key.Kind, key.LogicalId);
}

/// <summary>One persisted variable: its scope, owner, key and value.</summary>
internal sealed record DaggerfallVariableSave(int Scope, int Owner, int Key, bool Value)
{
    internal DaggerfallVariableAddress Require()
    {
        DaggerfallVariableScope scope = Scope switch
        {
            0 => DaggerfallVariableScope.Global,
            1 => DaggerfallVariableScope.Region,
            2 => DaggerfallVariableScope.Faction,
            _ => throw new InvalidOperationException($"Saved variable names scope {Scope}, which the contract does not declare."),
        };
        return new DaggerfallVariableAddress(scope, Owner, Key);
    }
}


/// <summary>One mutable reputation entry for an admitted faction.</summary>
internal sealed record DaggerfallFactionReputationSave(int FactionId, int Value);

/// <summary>One mutable regional legal-reputation entry.</summary>
internal sealed record DaggerfallRegionalReputationSave(int Region, int Value);

/// <summary>One player social-group reputation entry.</summary>
internal sealed record DaggerfallPersonalReputationSave(int SocialGroup, int Value);

/// <summary>One guild-group membership and the compact counters the donor persists with it.</summary>
internal sealed record DaggerfallGuildMembershipSave(int GuildGroup, int FactionId, int Rank, int LastRankChangeDay, int NotedByGuild);

/// <summary>Meaningful social state, independently persisted from loaded NPC entities.</summary>
internal sealed record DaggerfallSocialSave(
    DaggerfallFactionReputationSave[] Factions,
    DaggerfallRegionalReputationSave[] Regions,
    DaggerfallPersonalReputationSave[] Personal,
    DaggerfallGuildMembershipSave[] Memberships)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Factions);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Personal);
        ArgumentNullException.ThrowIfNull(Memberships);
        HashSet<int> factions = [];
        foreach (DaggerfallFactionReputationSave entry in Factions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.FactionId < 0 || !factions.Add(entry.FactionId) || entry.Value is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
                throw new ArgumentException("Saved faction reputation entries must name distinct non-negative factions within the social bounds.");
        }
        HashSet<int> regions = [];
        foreach (DaggerfallRegionalReputationSave entry in Regions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Region is < 0 or > 61 || !regions.Add(entry.Region) || entry.Value is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
                throw new ArgumentException("Saved regional reputation entries must name distinct classic regions within the social bounds.");
        }
        HashSet<int> groups = [];
        foreach (DaggerfallPersonalReputationSave entry in Personal)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.SocialGroup is < 0 or >= DaggerfallSocialState.SocialGroupCount || !groups.Add(entry.SocialGroup) || entry.Value is < DaggerfallSocialState.MinimumPersonalReputation or > DaggerfallSocialState.MaximumPersonalReputation)
                throw new ArgumentException("Saved personal reputation entries must name distinct donor social groups within the signed-short source bounds.");
        }
        HashSet<int> guilds = [];
        foreach (DaggerfallGuildMembershipSave entry in Memberships)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.GuildGroup <= 0 || entry.FactionId < 0 || !guilds.Add(entry.GuildGroup)
                || entry.Rank is < 0 or > DaggerfallSocialState.MaximumGuildRank
                || entry.NotedByGuild is < 0 or > byte.MaxValue)
                throw new ArgumentException("Saved guild memberships must name distinct guild groups with valid faction, rank, day, and recognition values.");
        }
    }

    internal void Validate(DaggerfallFactionsSet catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Validate();
        HashSet<int> savedFactionIds = [.. Factions.Select(entry => entry.FactionId)];
        if (!savedFactionIds.SetEquals(catalog.Factions.Keys))
            throw new ArgumentException("Current social state must carry one faction reputation entry for every admitted faction.");
        foreach (DaggerfallRegionalReputationSave entry in Regions)
            if (!catalog.Regions.ContainsKey(entry.Region))
                throw new ArgumentException($"Saved regional reputation names absent region {entry.Region}.");
        foreach (DaggerfallGuildMembershipSave entry in Memberships)
        {
            if (!catalog.Factions.TryGetValue(entry.FactionId, out DaggerfallFactionDefinition? faction) || faction.GuildGroup != entry.GuildGroup)
                throw new ArgumentException($"Saved guild membership group {entry.GuildGroup} does not match admitted faction {entry.FactionId}.");
        }
    }
}

/// <summary>One persisted NPC: its identity, kind, site, appearance, role and presence.</summary>
internal sealed record DaggerfallNpcEntry(
    long DurableId,
    int Kind,
    string StableKey,
    int Region,
    string Location,
    string Building,
    string Race,
    string Gender,
    int BillboardArchive,
    int BillboardRecord,
    int NameSeed,
    int FactionId,
    string Role,
    string[] Services,
    int Presence,
    float? X,
    float? Y,
    float? Z)
{
    [JsonRequired] public DaggerfallWorldProfileKeySave? Profile { get; init; }
    [JsonRequired] public string? DisplayName { get; init; }
    [JsonRequired] public string? ProfileId { get; init; }
}

/// <summary>The session's NPCs in durable order.</summary>
internal sealed record DaggerfallNpcSave(DaggerfallNpcEntry[] Entries)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Entries);
        HashSet<long> identities = [];
        foreach (DaggerfallNpcEntry entry in Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.DurableId <= 0 || !identities.Add(entry.DurableId))
                throw new ArgumentException("Saved NPC identities must be positive and unique.", nameof(Entries));
            if (!Enum.IsDefined((DaggerfallNpcKind)entry.Kind) || !Enum.IsDefined((DaggerfallNpcPresence)entry.Presence))
            {
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "A saved NPC names a kind or presence the contract does not declare.");
            }

            ArgumentNullException.ThrowIfNull(entry.Services);
            if (entry.Profile is { } profile)
            {
                _ = profile.Require();
                if (entry.X is not float x || entry.Y is not float y || entry.Z is not float z || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    throw new ArgumentException($"Saved NPC {entry.DurableId} has no finite profile position.");
            }
            if (entry.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(entry.DisplayName);
        }
    }
}

/// <summary>The session's written variables in a stable order.</summary>
internal sealed record DaggerfallVariablesSave(DaggerfallVariableSave[] Entries)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Entries);
        foreach (DaggerfallVariableSave entry in Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Require();
        }
    }
}

internal sealed record DaggerfallPlayerSave(float X, float Y, float Z, float YawRadians, float PitchRadians, DaggerfallStatsSave Stats)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(YawRadians) || !float.IsFinite(PitchRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved player pose must be finite.");
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

internal sealed record DaggerfallActorSave(long EntityId, float X, float Y, float Z, float HeadingRadians, DaggerfallStatsSave Stats)
{
    public string? WabbajackDefinition { get; init; }
    public bool ForcedHostile { get; init; }
    [JsonRequired] public bool MagicallyPacified { get; init; }
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved actor pose must be finite.");
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

/// <summary>One inactive site's durable gameplay state. Runtime Engine identities are recreated when it becomes active.</summary>
internal sealed record DaggerfallSiteDeltaSave(
    DaggerfallWorldProfileKeySave Profile,
    DaggerfallActorSave[] Actors,
    DaggerfallDynamicActorSave[] DynamicActors,
    DaggerfallActorInventorySave[] ActorInventories,
    DaggerfallCorpseSave[] Corpses,
    DaggerfallDoorSave[] Doors,
    DaggerfallActiveEffectSave[] Effects)
{
    /// <summary>Detached current motion phases for this unloaded profile.</summary>
    [JsonRequired]
    public DaggerfallDungeonMotionSnapshot Motion { get; init; } = null!;
    [JsonRequired]
    public long[] BanishedActors { get; init; } = [];

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Profile); Profile.Validate();
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(DynamicActors);
        if(Actors.Any(actor=>actor.ForcedHostile && actor.MagicallyPacified) || DynamicActors.Any(actor=>actor.ForcedHostile && actor.MagicallyPacified))
            throw new ArgumentException("An actor cannot be forced hostile and magically pacified together.");
        ArgumentNullException.ThrowIfNull(ActorInventories);
        ArgumentNullException.ThrowIfNull(Corpses);
        ArgumentNullException.ThrowIfNull(Doors);
        ArgumentNullException.ThrowIfNull(Effects);
        ArgumentNullException.ThrowIfNull(Motion);
        Motion.Validate();
        foreach (DaggerfallActorSave actor in Actors) { ArgumentNullException.ThrowIfNull(actor); actor.Validate(); }
        foreach (DaggerfallDynamicActorSave actor in DynamicActors) { ArgumentNullException.ThrowIfNull(actor); actor.Validate(); }
        foreach (DaggerfallActorInventorySave inventory in ActorInventories) { ArgumentNullException.ThrowIfNull(inventory); inventory.Validate(); }
        foreach (DaggerfallCorpseSave corpse in Corpses) { ArgumentNullException.ThrowIfNull(corpse); corpse.Validate(); }
        foreach (DaggerfallDoorSave door in Doors) { ArgumentNullException.ThrowIfNull(door); door.Validate(); }
        foreach (DaggerfallActiveEffectSave effect in Effects) { ArgumentNullException.ThrowIfNull(effect); effect.Validate(); }
    }
}

/// <summary>
/// One dynamically spawned actor: the definition it was registered from plus its pose and full
/// Engine stat meaning. Authored placement actors need no definition reference because the
/// selected content already names theirs.
/// </summary>
internal sealed record DaggerfallDynamicActorSave(long EntityId, string Definition, float X, float Y, float Z, float HeadingRadians, DaggerfallStatsSave Stats)
{
    [JsonRequired] public int Level { get; init; }
    public bool WabbajackActive { get; init; }
    public bool ForcedHostile { get; init; }
    [JsonRequired] public bool MagicallyPacified { get; init; }
    public bool PlayerAllied { get; init; }
    public DaggerfallCorruptionOrigin? CorruptionOrigin { get; init; }
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved dynamic actor pose must be finite.");
        if (string.IsNullOrWhiteSpace(Definition))
            throw new ArgumentException("A saved dynamic actor must name its definition.", nameof(Definition));
        ArgumentNullException.ThrowIfNull(Stats);
        if (Level < 1) throw new ArgumentOutOfRangeException(nameof(Level), "Saved actor level must be positive.");
        CorruptionOrigin?.Validate();
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DaggerfallDispelRequest))]
[JsonSerializable(typeof(DaggerfallTeleportAnchor))]
[JsonSerializable(typeof(DaggerfallIdentifyRequest))]
[JsonSerializable(typeof(DaggerfallSoulTrapState))]
[JsonSerializable(typeof(DaggerfallCastEffectState))]
[JsonSerializable(typeof(DaggerfallInfectionState))]
[JsonSerializable(typeof(DaggerfallInfectionsSave))]
[JsonSerializable(typeof(DaggerfallShieldState))]
[JsonSerializable(typeof(DaggerfallPeriodicCastState))]
[JsonSerializable(typeof(DaggerfallAttributeDrainState))]
[JsonSerializable(typeof(DaggerfallMolagBalState))]
[JsonSerializable(typeof(DaggerfallSpellPointHealingState))]
[JsonSerializable(typeof(DaggerfallTempleBlessingState))]
[JsonSerializable(typeof(DaggerfallSavePayload))]
[JsonSerializable(typeof(DaggerfallStatsSave))]
[JsonSerializable(typeof(DaggerfallNotebookSave))]
[JsonSerializable(typeof(DaggerfallCharacterSave))]
[JsonSerializable(typeof(DaggerfallRacialOverrideState))]
[JsonSerializable(typeof(DurableIdentityState))]
[JsonSerializable(typeof(KindAllocatorState))]
internal partial class DaggerfallSaveJsonContext : JsonSerializerContext;
