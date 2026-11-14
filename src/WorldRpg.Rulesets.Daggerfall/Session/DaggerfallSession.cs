using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Concrete Daggerfall composition of catalog policy, module state, and named Engine capabilities.</summary>
internal sealed partial class DaggerfallSession : IPlaytestGameSession, IPlaytestWorldInspectionSession, ISaveableGameSession, IModeAwareGameSession, IEntryScreenSession, IEntryScreenStartupSession, ICharacterCreationSession, ISaveRequestingGameSession, IPlayerPreferencesSession, IPlayerDefeatOutcomeSession, IGameBundleChoiceSession,
    IDaggerfallSiteTransitionHost, IDaggerfallQuestWorldAdmission, IDaggerfallRestSurroundings, IDaggerfallTeleportSites, IDaggerfallTransformationConsumer,
    IDaggerfallAfflictionQuests
{
    private readonly IRandomService _random;
    private readonly IEngineContext _engine;
    private readonly DaggerfallTuning _tuning;
    private readonly DaggerfallViewLight _viewLight;
    private PlayerInputSystem _input;
    private ProductMode _mode = ProductMode.Playing;
    // The line the last mode change set for its mode, so returning to play can tell it from a result.
    private string? _modeLine = string.Empty;
    private readonly SpatialMovementSystem _spatial;
    /// <summary>Harm and spell delivery's physical contact over the admitted scene and canonical actor bodies.</summary>
    private readonly ActorContact _contact;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerCombatRules _combat;
    // The player's weapon swing gesture, read from the look turns each admitted update commits and
    // consumed by the weapon attack it admits.
    private readonly DaggerfallVitalityConsequences _vitality;
    // The player's Stamina, swimming fatigue and collapse.
    private readonly DaggerfallPlayerCondition _condition;
    // Who a poison reaches and the poisons the session's actors carry; the player's diseases.
    private readonly DaggerfallPoisoning _poisoning;
    private readonly DaggerfallDiseases _diseases;
    // The curses an infection becomes.
    private readonly DaggerfallLycanthropy _lycanthropy;
    private readonly DaggerfallVampirism _vampirism;
    private readonly CombatResolution _combatResolution;
    private readonly DaggerfallLocomotionPolicy _locomotion;
    private readonly DaggerfallClimbingPolicy _climbing;
    private readonly DaggerfallLevitationPolicy _levitation = new();
    private readonly DaggerfallDungeonVisibility _dungeonVisibility;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly DaggerfallEnemyMagicModule _enemyMagic;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallBookNotebook _notebook;
    private readonly DaggerfallEncounterRuntime _encounters;
    private readonly DaggerfallActorPlacement _placement;
    private readonly DaggerfallEnemySenses _senses;
    private readonly DaggerfallDungeonPopulation _dungeonPopulation;
    internal DaggerfallCivilians Civilians { get; }
    internal DaggerfallSourcePopulation Population { get; }
    internal DaggerfallNpcProjection NpcProjection { get; }
    internal DaggerfallWorldEncounters WorldEncounters { get; }
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly DaggerSessionPersistence _persistence;
    private readonly HashSet<ulong> _authoredEntityIds;
    /// <summary>
    /// Dynamic actors share one numeric base with generated unique items; durable kinds keep
    /// them distinct, the way a corpse container already shares its actor's number under a
    /// different kind.
    /// </summary>
    internal const ulong DynamicActorFirstIdentity = 1_000_000_000_000UL;
    internal const ulong GroundContainerFirstIdentity = 2_000_000_000_000UL;
    private readonly DurableIdentityAllocator _actorIdentities;
    private readonly DaggerfallHeldEnchantments _heldEnchantments;
    private readonly DaggerfallItemCastTriggers _itemCastTriggers = null!;
    private readonly DaggerfallActorRoster _roster;
    private readonly DaggerfallActorGrounding _grounding;
    private readonly DaggerfallDefinitions _definitions;
    private readonly FactBuffer<IProductFact> _facts = new();
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly DaggerfallEquipmentMoves _equipmentMoves;
    private readonly DaggerfallItemConditionService _itemCondition;
    private readonly DaggerfallItemEnchanting _itemEnchanting;
    private readonly DaggerfallArtifactPowers _artifacts;
    private readonly DaggerfallDoorSpells _doorSpells;
    private readonly DaggerfallItemMaker _itemMaker;
    private readonly DaggerfallPotionMaking _potionMaker;
    private readonly DaggerfallItemOwnership _itemOwnership;
    private readonly DaggerfallItemLifetime _itemLifetime;
    private readonly DaggerfallInventoryPresentation _inventoryUi;
    private readonly DaggerfallLootPresentation _lootUi;
    private readonly DaggerfallCharacterPresentation _characterUi;
    /// <summary>The one owner of the active site projection, catalog, deltas and exterior window.</summary>
    // Assigned once the roster and persistence it composes exist; readers before that are lazy.
    private readonly DaggerfallSiteCrossing _sites = null!;
    private DaggerfallExteriorWorld _exterior => _sites.Exterior;
    private DaggerfallWorldProfileKey _activeProfileKey => _sites.ActiveProfile;
    private DaggerfallSiteAppearance _appearance => _sites.Projection.Appearance;
    private DaggerfallDoorRuntime _doors => _sites.Projection.Doors;

    /// <summary>The transport access where the player stands (Travel's rule), with whether an owned ship is reachable here (Economy's).</summary>
    private DaggerfallTransportAccessContext TransportAccess() => DaggerfallTransportAccessContext.At(_activeProfileKey.Kind,
        State.PlayerControl.Position, _sites.Projection.Inputs.DungeonMap, ResolveOwnedShipProfile() is not null);

    private readonly World.DaggerfallWorldTime _time;
    private readonly World.DaggerfallHolidays _holidays = new();
    private readonly DaggerfallRest _rest;
    private readonly World.DaggerfallPlayerTime _playerTime;

    /// <summary>Whether action-driven play is holding the world still now.</summary>
    internal bool PlayerTimeHeld => _playerTime.Held;

    // What the career's special abilities do from moment to moment.
    private readonly DaggerfallCareerPassives _passives;
    internal float EnemyAudibleRange => _passives.EnemyAudibleRange(State.HeldEnchantments.Talents.AcuteHearing);

    /// <summary>
    /// Career absorption for any target, as the donor applies it to every entity: a career that absorbs spells in
    /// the light where the player is absorbs every one.
    /// </summary>
    private DaggerfallMagicDefense CareerMagicDefense(long actorId) =>
        _passives.AbsorbsSpells(actorId, InSunlight()) ? new(100, 0, []) : DaggerfallMagicDefense.None;

    // Character creation from the title screen.
    private readonly DaggerfallCharacterCreation _creation;
    // The player's control settings: bindings, look, pad, sound levels, ground cover and action-driven time.
    private readonly DaggerfallPlayerControls _controls;
    public string CapturePlayerPreferences() => _controls.Capture();
    public string? TakePlayerPreferencesSave() => _controls.TakePreferencesSave();
    public void ReportPlayerPreferencesOutcome(string message)
    {
        _controls.Report(message);
        PublishPresentation();
    }
    public void ApplyPlayerPreferences(string? serialized) => _controls.ApplyPreferences(serialized);
    internal bool ActionDrivenPlay => _controls.ActionDrivenTime;
    private readonly World.DaggerfallSiteContext _site;

    /// <summary>The holiday announcement the calendar currently names for the session's site, or null.</summary>
    internal World.DaggerfallHolidayAnnouncement? HolidayAnnouncement => _holidays.Announcement;

    /// <summary>
    /// Where the session is: the active site, the region it belongs to and the sites play has revealed.
    /// </summary>
    /// <remarks>
    /// This is the one owner of location context in the session, which is what lets a region consumer -
    /// a holiday, a price, a service - read the region the player is actually standing in rather than
    /// carrying a second copy of it.
    /// </remarks>
    internal World.DaggerfallSiteContext Site => _site;

    /// <summary>The player's active block.</summary>
    private readonly DaggerfallContactGuard _guard;

    /// <summary>Whether the player's guard is raised now; never under a ruleset with no guard.</summary>
    internal bool PlayerGuardRaised => _guard.Raised;
    private readonly DaggerfallContactTouch _touch;
    /// <summary>Who a blow is holding now.</summary>
    private readonly ActorReactions _reactions;
    internal ActorReactions Reactions => _reactions;
    /// <summary>Where a blow comes from and whom the player's harm goes through.</summary>
    internal DaggerfallStrikeAim StrikeAim { get; }
    /// <summary>The blow's weight on screen: the knock and the hit-stop.</summary>
    internal DaggerfallStrikeFeel StrikeFeel { get; }
    /// <summary>The player's attack around its clock.</summary>
    internal DaggerfallPlayerAttack PlayerAttack { get; } = null!;
    /// <summary>Fires, and where a thrown body meets the traps.</summary>
    private readonly DaggerfallHazards _hazards;
    /// <summary>Hurt traps reaching past the body that set them off.</summary>
    internal DaggerfallTraps Traps { get; }
    /// <summary>What a monster's landed hit carries.</summary>
    private readonly DaggerfallMonsterHits _monsterHits;

    /// <summary>A focus, mode, rest or travel change drops a held guard with the rest of the held input.</summary>
    private void NeutralizeGuard() => _guard.Neutralize(_latestUpdateGeneration ?? 0UL, _latestSimulationStep ?? 0UL, _facts);
    private ulong? _latestUpdateGeneration;
    private ulong? _latestSimulationStep;
    /// <summary>What is open over the world, which of it holds the world, and the pad's panel request.</summary>
    private readonly DaggerfallOpenInteractions _interactions;
    private bool _disposed;

    internal DaggerfallCinematicPresentation? Cinematics { get; }
    private readonly DaggerfallOpeningCinematics _openingCinematics;
    internal DaggerfallTransformationInfections Infections { get; }
    internal DaggerfallPoisoning Poisoning => _poisoning;
    internal DaggerfallDiseases Diseases => _diseases;
    internal DaggerfallLycanthropy Lycanthropy => _lycanthropy;
    internal DaggerfallVampirism Vampirism => _vampirism;

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    /// <summary>The selected site's one authoritative RDB door owner for activation, spells, and dungeon actions.</summary>
    internal DaggerfallDoorRuntime Doors => _doors;

    /// <summary>Admits the full selected site catalog once composition has constructed this session.</summary>
    internal void AdmitSiteProfiles(DaggerfallSiteProfiles profiles) => _sites.AdmitProfiles(profiles);

    /// <summary>The session's site lifecycle: projection, catalog, deltas and transitions.</summary>
    internal DaggerfallSiteCrossing Sites => _sites;

    /// <summary>The cells streamed around the player and the towns placed on them.</summary>
    internal DaggerfallExteriorWorld Exterior => _sites.Exterior;

    /// <summary>The admitted spatial scene, for Engine queries made outside an update such as a test looking along a wall.</summary>
    internal SpatialMovementSystem Spatial => _spatial;

    /// <summary>Relocates the existing player (or a live actor) to an admitted named anchor.</summary>
    internal bool TryRelocate(DaggerfallRelocationDestination destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryRelocate(destination);
    }

    /// <summary>
    /// Attempts one real site transition, landing at the named destination anchor when there is no entrance to
    /// return through; failed destination admission leaves the source projection live.
    /// </summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination, string? arrivalAnchor = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryTransitionTo(destination, arrivalAnchor);
    }

    void IDaggerfallExteriorHost.RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians) =>
        ApplyRelocation(position, yawRadians, pitchRadians);

    void IDaggerfallExteriorHost.ConfigureSiteAppearance(DaggerfallSiteAppearance appearance) =>
        ConfigureSiteAppearance(appearance);

    void IDaggerfallExteriorHost.RebuildActivation()
    {
        ReconcileNpcProjection();
        ReconcilePropertyContainers();
        InitializeActivation(_engine, _tuning.LootInteraction);
        SyncWeatherContext();
    }

    void IDaggerfallExteriorHost.RebaseTransientWorld(Vector3 delta)
    {
        _combat.RebaseRangedFlight(delta);
        Casting.Rebase(delta);
        _encounters.RebasePending(_activeProfileKey.LogicalId, delta);
        _dialogue?.Rebase(delta);
        // Projected quest people keep their placement in the Engine Transform, which the origin
        // commit's EntityOriginRebaser already moved.
        // Ship return pose is detached in the land profile; active-world origin moves do not own it.
    }

    void IDaggerfallSiteTransitionHost.ReconcileArrivalPopulation() => Population.Reconcile();

    void IDaggerfallExteriorHost.GroundArrivedActors() => _grounding.GroundAwaiting(actor =>
        State.Actors.TryGet(actor.DurableId, out ActorState live) && live.Actor.Entity == actor.Actor.Entity);

    void IDaggerfallSiteTransitionHost.EnteredSite()
    {
        Casting.ClearTransient();
        _enemyMagic.Clear();
        _enemyBehavior.ClearEnemyMagic();
        ChangeMusicSite();
        if (_site.ActiveSite is { } offerSite) State.Quests.EnterOfferSite(offerSite.Id, _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon);
        Law.ReconcileSite();
    }

    void IDaggerfallExteriorHost.AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection) =>
        Civilians.AdmitResidentAppearances(projection.Inputs, projection.Appearance);

    // What rest asks of the world, each read from the domain that knows it when the rest asks.
    DaggerfallWorldProfileKey IDaggerfallTeleportSites.ActiveProfile => _activeProfileKey;
    DaggerfallWorldProfileKey? IDaggerfallTeleportSites.ReturnProfile => _sites.ReturnProfile;
    DaggerfallSiteReturnPose? IDaggerfallTeleportSites.ReturnPose => _site.ReturnPose;
    Vector3 IDaggerfallTeleportSites.ProfilePosition(WorldPoint local) => ProfilePosition(local);
    bool IDaggerfallTeleportSites.TryRelocatePlayer(DaggerfallWorldProfileKey profile, DaggerfallSiteAnchor pose) =>
        _sites.TryRelocatePlayer(profile, pose);
    void IDaggerfallTeleportSites.RestoreReturnDestination(DaggerfallWorldProfileKey? profile, DaggerfallSiteReturnPose? pose) =>
        _sites.RestoreReturnDestination(profile, pose);

    /// <summary>
    /// A scene-local point in the active profile's own frame, as <see cref="DaggerfallSiteCrossing.TryRelocatePlayer"/>
    /// takes it. Exterior profiles author poses in the site's local frame; LocalToProfile is the map-global projection
    /// used by live population and map reads, so feeding it back to a relocation resolves an impossible exterior cell.
    /// </summary>
    private Vector3 ProfilePosition(WorldPoint local) => _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior
        ? _exterior.ExteriorSitePosition(local).ToVector()
        : _exterior.LocalToProfile(local.ToVector());

    bool IDaggerfallRestSurroundings.MustFeed => _vampirism.MustFeed;
    bool IDaggerfallRestSurroundings.EncounterPending => _encounters.HasPending(_activeProfileKey.LogicalId);
    bool IDaggerfallRestSurroundings.EnemyNearby => _senses.EnemyNearby();
    bool IDaggerfallRestSurroundings.QuestPromptPending => State.Quests.Messages.Pending is not null;
    bool IDaggerfallRestSurroundings.PlayerDefeated => State.Actors.Player.IsDefeated;
    bool IDaggerfallRestSurroundings.HasPosition => State.PlayerControl.Position is not null;
    DaggerfallWorldProfileKey IDaggerfallRestSurroundings.Profile => _activeProfileKey;
    DaggerfallSiteRecord? IDaggerfallRestSurroundings.Site =>
        _site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site) ? site : null;

    bool IDaggerfallRestSurroundings.InteriorPrivilege
    {
        get
        {
            DaggerfallInteriorBuilding? building = CurrentInteriorBuilding();
            if (building is not null && State.Lodging.RemainingSeconds(_activeProfileKey.Site,
                new(building.BlockX, building.BlockY, building.Building.Index), _time.Calendar.ToAbsoluteSeconds()) > 0)
                return true;
            if (building is not null && State.Property.HouseAccess(new(_activeProfileKey.Site, building.Building, building.BlockX, building.BlockY)).Allowed)
                return true;
            return DaggerfallConcreteGuildPolicy.FightersHallRestAllowed(building, State.GuildMembership, _activeProfileKey.Site.Region,
                checked((int)_time.Calendar.DayNumber));
        }
    }

    long? IDaggerfallRestSurroundings.TavernRoomSeconds =>
        CurrentInteriorBuilding() is { BuildingType: DaggerfallLodgingState.TavernBuildingType } tavern
            ? State.Lodging.RemainingSeconds(_activeProfileKey.Site,
                new(tavern.BlockX, tavern.BlockY, tavern.Building.Index), _time.Calendar.ToAbsoluteSeconds())
            : null;

    int IDaggerfallRestSurroundings.ExteriorClimate
    {
        get
        {
            DaggerfallExteriorCellId cell = _exterior.CurrentExteriorCell();
            return _definitions.Grids.ClimateAtWorldPixel(cell.X, cell.Y).Value;
        }
    }

    bool IDaggerfallRestSurroundings.InSiteCell =>
        _site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site) && site.Exterior is { } exterior
        && _exterior.CurrentExteriorCell() == new DaggerfallExteriorCellId(exterior.MapPixelX, exterior.MapPixelY);

    bool IDaggerfallRestSurroundings.EncounterArrives(long minute) => WorldEncounters.ArrivesWhileResting(minute);

    // What a ready infection asks before its permanent change; the change itself is the transformation's list.
    bool IDaggerfallTransformationConsumer.HasRacialOverride => State.RacialOverrides.Current is not null;
    DaggerfallInfectionConsumption IDaggerfallTransformationConsumer.Consume(DaggerfallInfectionTransition transition) =>
        transition.Kind == DaggerfallInfectionKind.Vampire ? BecomeVampire(transition) : _lycanthropy.Become(transition);

    // What lycanthropy and vampirism ask of the quests, read from the quest state when they ask.
    bool IDaggerfallAfflictionQuests.Active(string sourceFile) =>
        State.Quests.All.Any(value => value.SourceFile == sourceFile && value.Lifecycle == DaggerfallQuestLifecycle.Active);
    bool IDaggerfallAfflictionQuests.Started(string instanceId) => State.Quests.All.Any(value => value.InstanceId == instanceId);
    IReadOnlyList<string> IDaggerfallAfflictionQuests.ClanWork(int clan) =>
        [.. State.Quests.OrdinaryWorkPool(clan, true, State.Progression.Level, State.Social.FactionReputation(clan),
            State.Progression.Level, State.Character.Identity.Gender).Select(row => row.Name)];

    bool IDaggerfallAfflictionQuests.Offer(string instanceId, string sourceFile, int factionId)
    {
        try
        {
            var source = _definitions.QuestSources.Resolve(sourceFile);
            State.Quests.Start(new(instanceId, source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = factionId });
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    void IDaggerfallAfflictionQuests.FailActive(string definitionPrefix, string reason)
    {
        foreach (var quest in State.Quests.Capture().Instances.Where(quest => quest.Lifecycle == DaggerfallQuestLifecycle.Active
            && quest.DefinitionName.StartsWith(definitionPrefix, StringComparison.OrdinalIgnoreCase)))
            State.Quests.Fail(quest.InstanceId, reason);
    }

    /// <summary>
    /// A swing the departing projection was still timing ends with it: retire and hand its
    /// impact back to the shared state here, inside the generation that admitted it, so the
    /// player is not left charged against an animation that no longer exists.
    /// </summary>
    void IDaggerfallSiteTransitionHost.RetireDepartingProjection(DaggerfallSiteProjection source)
    {
        source.DeactivateWaterTriggers(_latestSimulationStep ?? 0UL);
        RetireDepartingSwing(source);
        _combat.ClearRangedFlight();
        CancelDungeonTextOnUnload();
        // The destination is committed; restored item lifetimes settle before any presentation.
        ExpireConjuredItems();
    }

    /// <summary>
    /// Whether the player stands in sunlight. The donor reads daytime while not inside any structure,
    /// so a shop, home or guild counts as darkness by day exactly as a dungeon does. The donor also
    /// exempts prison; no product state reaches prison yet, so that input is false until one does.
    /// </summary>
    private bool InSunlight() => DaggerfallHeldEnchantments.InSunlight(
        _time.Calendar.IsDay,
        insideStructure: _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior,
        inPrison: false);

    /// <summary>
    /// Whether the player stands in a holy place. The donor requires an interior whose admitted
    /// building is a temple or belongs to the Fighters Guild trainers faction.
    /// </summary>
    private bool InHolyPlace() => IsHolyPlace(_activeProfileKey.Kind,
        _activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior
            ? CurrentInteriorBuilding() : null);

    private DaggerfallInteriorBuilding? CurrentInteriorBuilding()
    {
        DaggerfallInteriorBuilding? placement = _sites.Projection.Inputs.InteriorBuilding;
        if (placement is null) return null;
        DaggerfallSiteBuildingSource building = _site.RequireBuildingSource(_activeProfileKey.Site,
            new(placement.BlockX, placement.BlockY, placement.Building.Index));
        return placement with { BuildingType = building.Source.BuildingType, FactionId = building.Source.FactionId };
    }

    internal static bool IsHolyPlace(DaggerfallWorldProfileKind kind, DaggerfallInteriorBuilding? building) =>
        kind == DaggerfallWorldProfileKind.Interior
        && building is { BuildingType: 14 } or { FactionId: DaggerfallConcreteGuildCatalog.FightersTrainerFactionId };

    /// <summary>
    /// Retires the departing projection's unfinished swing. The notice it publishes belongs to the
    /// generation that admitted the swing, so it is applied with that generation rather than the
    /// destination's, and the caller's normal fact boundary publishes any consequence.
    /// </summary>
    private void RetireDepartingSwing(DaggerfallSiteProjection source)
    {
        if (_latestUpdateGeneration is ulong swinging) PlayerAttack.Retire(swinging);
        IReadOnlyList<AttackImpactNotice> impacts = source.Appearance.TakeAttackImpacts();
        if (impacts.Count == 0) return;
        if (_latestUpdateGeneration is not ulong generation) return;
        State.Kit.AttackExecution.ApplyImpacts(impacts, generation, _facts);
    }

    /// <summary>
    /// Clears retained input and open interaction state before installing a landing pose. The Kit
    /// starts the next Engine proposal with detached motion, so stale floor support, jump state,
    /// and velocity cannot carry across a teleport or admitted content replacement.
    /// </summary>
    private void ApplyRelocation(WorldPoint position, float yawRadians, float pitchRadians)
    {
        if (!float.IsFinite(yawRadians)) throw new ArgumentOutOfRangeException(nameof(yawRadians));
        if (!float.IsFinite(pitchRadians)) throw new ArgumentOutOfRangeException(nameof(pitchRadians));
        _input.Neutralize();
        _locomotion.Neutralize(); NeutralizeGuard();
        _appearance.ResetFootsteps();
        _climbing.Detach();
        _lootUi.CloseAll();
        State.PlayerControl.YawRadians = yawRadians;
        State.PlayerControl.PitchRadians = pitchRadians;
        State.Transport.Ride.Reset();
        _spatial.Relocate(State.PlayerControl, position);
        _camera.Update(State.PlayerControl);
    }

    /// <summary>Every actor definition by durable identity: authored placements and spawned actors alike.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> DefinitionsByActor => _roster.Definitions;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> DynamicActors => _roster.Dynamic;

    /// <summary>Registers one actor from a published definition beyond the authored placements.</summary>
    internal long SpawnActor(string definitionId, ActorPose pose, int? level = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _roster.Spawn(definitionId, pose, level);
    }

    /// <summary>Retires one spawned actor through the roster's lifetime policy.</summary>
    internal void BanishActor(long durableId) => _roster.Banish(durableId);

    internal void RetireActor(long durableId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _roster.Retire(durableId);
    }

    /// <summary>
    /// Removes a dynamic actor retained by an inactive site delta. The
    /// detached representation has no Engine entities left to destroy, so retirement
    /// removes every durable relationship directly and keeps the allocator tombstones:
    /// the items it and its corpse hold (Items, with Magic's references to them), its
    /// effects (Magic), its corpse container and identity (Items, Actors), the deltas
    /// that held it (the site crossing's), and the quests that named it (Quests).
    /// </summary>
    private bool RetireDetachedActor(long npcId, string? requiredDefinition = null)
    {
        DaggerfallWorldProfileKey? owner = null;
        DaggerfallSiteRuntimeDelta? retained = null;
        foreach ((DaggerfallWorldProfileKey profile, DaggerfallSiteRuntimeDelta delta) in _sites.Deltas)
        {
            if (!delta.DynamicActors.Any(actor => actor.EntityId == npcId)) continue;
            if (owner is not null)
                throw new InvalidOperationException($"Actor {npcId} is retained by more than one site delta.");
            owner = profile;
            retained = delta;
        }

        if (retained is null || owner is not DaggerfallWorldProfileKey profileKey) return false;
        DaggerfallDynamicActorSave actor = retained.DynamicActors.Single(value => value.EntityId == npcId);
        if (requiredDefinition is not null && !StringComparer.Ordinal.Equals(actor.Definition, requiredDefinition))
            throw new InvalidOperationException($"Detached NPC actor {npcId} does not carry the required runtime definition.");

        foreach (ulong itemId in retained.ActorInventories
            .Where(inventory => inventory.EntityId == npcId)
            .SelectMany(inventory => inventory.Inventory.UniqueItems.Select(item => item.EntityId))
            .Concat(retained.Corpses.Where(corpse => corpse.ActorId == npcId)
                .SelectMany(corpse => corpse.UniqueItems.Select(item => item.EntityId)))
            .Distinct())
        {
            State.ItemInstances.RemoveUnique(itemId);
            State.Effects.CancelItemReferences(itemId);
            Casting.CancelItemReferences(itemId);
            DurableIdentityReference identity = new(DurableIdentityKind.Item, itemId);
            if (_actorIdentities.Classify(identity) == DurableIdentityClassification.Live)
                _uniqueItems.Remove(identity);
        }

        foreach (var inventory in retained.ActorInventories.Where(value => value.EntityId == npcId))
            foreach (var stack in inventory.Inventory.Stacks)
                State.ItemInstances.RetireRetainedStack(DaggerfallItemOwner.Actor(npcId), Rusty.Engine.Mechanics.InventoryStackId.Parse(stack.StackId));
        foreach (var corpse in retained.Corpses.Where(value => value.ActorId == npcId))
            foreach (var stack in corpse.Stacks)
                State.ItemInstances.RetireRetainedStack(DaggerfallItemOwner.Corpse(npcId), Rusty.Engine.Mechanics.InventoryStackId.Parse(stack.StackId));
        State.Effects.CancelActorReferences(npcId);

        DaggerfallCorpseSave? corpseSave = retained.Corpses.SingleOrDefault(value => value.ActorId == npcId);
        if (corpseSave is not null)
        {
            DurableIdentityReference persisted = new(DurableIdentityKind.Container, corpseSave.ContainerId);
            if (_corpseLoot.TryGetContainerIdentity(npcId, out DurableIdentityReference mapped)
                && mapped != persisted)
                throw new InvalidOperationException($"Detached actor {npcId} has changed its corpse container identity.");
            if (!_corpseLoot.Retire(npcId)
                && _actorIdentities.Classify(persisted) == DurableIdentityClassification.Live)
                _actorIdentities.Remove(persisted);
        }

        DurableIdentityReference actorIdentity = ActorsState.Identity(npcId);
        if (_actorIdentities.Classify(actorIdentity) == DurableIdentityClassification.Live)
            _actorIdentities.Remove(actorIdentity);

        var retiredEffectIds = retained.Effects.Where(value => value.TargetId == npcId || value.CasterId == npcId)
            .Select(value => value.Instance).ToHashSet(StringComparer.Ordinal);
        _sites.ReplaceDelta(profileKey, retained with
        {
            Actors = retained.Actors.Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, retiredEffectIds) }).ToArray(),
            DynamicActors = retained.DynamicActors.Where(value => value.EntityId != npcId)
                .Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, retiredEffectIds) }).ToArray(),
            ActorInventories = retained.ActorInventories.Where(value => value.EntityId != npcId).ToArray(),
            Corpses = retained.Corpses.Where(value => value.ActorId != npcId).ToArray(),
            Effects = retained.Effects.Where(value => value.TargetId != npcId && value.CasterId != npcId).ToArray(),
        });
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(npcId));
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(npcId));
        foreach (var entry in _sites.Deltas.Where(value => value.Key != profileKey).ToArray())
        {
            var ended = entry.Value.Effects.Where(value => value.TargetId == npcId || value.CasterId == npcId)
                .Select(value => value.Instance).ToHashSet(StringComparer.Ordinal);
            if (ended.Count == 0) continue;
            _sites.ReplaceDelta(entry.Key, entry.Value with
            {
                Effects = entry.Value.Effects.Where(value => !ended.Contains(value.Instance)).ToArray(),
                Actors = entry.Value.Actors.Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, ended) }).ToArray(),
                DynamicActors = entry.Value.DynamicActors.Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, ended) }).ToArray(),
            });
        }
        State.Quests.ObserveFoeRemoval(npcId);
        return true;
    }

    public void PublishInitial()
    {
        // A client that attaches starts with its game menu closed. A menu left open by a page that
        // went away must not hold the world for a client that cannot see it.
        _interactions.SetMenuOpen(false);
        _hud.RequestArt();
        // A fresh page holds none of the travel map's places either.
        _hud.RequestTravelPlaces();
        SyncWeatherContext();
        PublishPresentation();
    }

    /// <summary>Captures meaningful state using the current product schema.</summary>
    public RulesetSavePayload CaptureSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A modal equipment action can be saved before another playing step. Capture the worn set
        // that actually owns the items, rather than a prior frame's held stat sources.
        Casting.ClearPending();
        DaggerfallMolagBalEffects.Reconcile(State.Effects, _artifacts.MolagBalEquipped);
        State.ItemInstances.ReconcileResidentOwners();
        _heldEnchantments.Refresh();
        using IDisposable detachedResidents = _exterior.SuspendResidentExteriorLocationsForSave();
        return _persistence.Capture(_latestUpdateGeneration, _latestSimulationStep, _roster.Dynamic, _encounters,
            _sites.CaptureDeltas(), _activeProfileKey, _sites.ReturnProfile, State.DungeonDiscoveries, State.DungeonActions,
            _sites.Projection.CaptureMotion(), _exterior.CaptureExteriorResidency(), _exterior.CaptureExteriorLocationResidency());
    }

    /// <summary>
    /// The status rows owners outside this session publish: effects, escorts and quests put what
    /// they want shown here, and the projection carries it without knowing what it means.
    /// </summary>
    internal PresentationSlots Slots { get; } = new();

    /// <summary>The input system's held state, readable so the mode request and tests agree on it.</summary>
    internal ProductMode Mode => _mode;

    /// <summary>
    /// The mode this session asks the product for, derived from the one open-interaction owner. The
    /// Kit actor state owns whether the player is defeated.
    /// </summary>
    public ProductMode? PendingModeRequest => _interactions.ModeRequest(_mode, State.Actors.Player.IsDefeated);

    /// <summary>
    /// Whether the pending request closes the owned loot interaction. Ordinary play is only ever
    /// requested when an open container just closed — the getter above returns Playing exactly in
    /// that case — so a Playing request is a close by construction rather than a second opinion
    /// about the mode. Death, modal-open, and silence carry no close.
    /// </summary>
    public bool PendingModeRequestClosesModal => PendingModeRequest == ProductMode.Playing;

    private SaveSlotRequest? _saveSlotRequest;
    private IReadOnlyList<SaveSlotSummary> _saveSlots = [];
    private double _playSeconds;
    private string? _saveSlotDiagnostic;

    /// <summary>Takes a structured named-slot action for the Host to perform.</summary>
    public SaveSlotRequest? TakeSaveSlotRequest()
    {
        SaveSlotRequest? request = _saveSlotRequest;
        _saveSlotRequest = null;
        return request;
    }

    /// <summary>Presents the product's save/load outcome after it honored a request.</summary>
    public void ReportSaveOutcome(string message)
    {
        Presentation.SetOutcome(message);
        ReopenDeathChoicesAfterSaveOutcome();
        PublishPresentation();
    }

    private GameBundleChoices? _bundles;
    private GameBundleId? _bundleRequest;

    /// <summary>Stores the games the Host offers at the entry screen for the next HUD projection.</summary>
    public void ReportBundles(GameBundleChoices choices)
    {
        _bundles = choices ?? throw new ArgumentNullException(nameof(choices));
        PublishPresentation();
    }

    /// <summary>Takes the game the player chose at the entry screen, for the Host to switch to.</summary>
    public GameBundleId? TakeBundleRequest()
    {
        GameBundleId? request = _bundleRequest;
        _bundleRequest = null;
        return request;
    }

    /// <summary>Presents why the Host did not switch to the chosen game.</summary>
    public void ReportBundleOutcome(string message)
    {
        Presentation.SetOutcome(message);
        PublishPresentation();
    }

    /// <summary>
    /// Asks for another game at the entry screen. A new game that has begun its opening belongs to the game it
    /// was created under, so the choice is open only before then.
    /// </summary>
    private void ChooseBundle(string key)
    {
        if (_mode != ProductMode.Title || _newGameInitialized)
        {
            Presentation.SetOutcome("The game can be changed only from the title screen.");
            return;
        }
        _bundleRequest = new GameBundleId(key);
    }

    /// <summary>Stores Host-owned save metadata for the next ordinary HUD projection.</summary>
    public void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic)
    {
        _saveSlots = slots?.ToArray() ?? throw new ArgumentNullException(nameof(slots));
        _saveSlotDiagnostic = diagnostic;
        SetDeathLoadAvailability(_saveSlots.Count > 0);
        PublishPresentation();
    }

    /// <summary>
    /// Applies the mode the product decided. A mode change is a focus change, so held movement is
    /// dropped: a key held when play paused, a modal opened or the player died must not keep moving
    /// the character, and a release this interpreter never sees would leave it held forever.
    /// </summary>
    public void ApplyProductMode(ProductMode mode)
    {
        if (mode == _mode) return;
        _mode = mode;
        // Travel is offered in play and modal interaction; the entry screen and death leave it. A host
        // pause keeps the map, since resuming returns to the same workflow.
        if (mode is ProductMode.Title or ProductMode.Dead) FastTravel.CloseMap();
        _input.Neutralize();
        _locomotion.Neutralize(); NeutralizeGuard();
        _appearance.ResetFootsteps();
        ApplyDeathPresentationMode(mode);
        // A death from exhaustion keeps the donor's collapse text rather than the generic death line.
        // Returning to play clears the line a mode set for itself, but not a result reported while that
        // mode was up: a journey or rest that ends by closing the menu keeps its line for the player.
        // A screen that says what it is itself adds no line, and leaves the recent messages where they are.
        string line = mode == ProductMode.Dead && _condition.ExhaustionDeathLine is { } exhausted ? exhausted : _interactions.ModeMessage(_mode);
        if (line.Length == 0 && mode == ProductMode.Modal) _modeLine = null;
        else
        {
            if (mode != ProductMode.Playing || Presentation.LastOutcome == _modeLine) Presentation.SetOutcome(line);
            _modeLine = Presentation.LastOutcome;
        }
        if (mode is ProductMode.Playing or ProductMode.Title) _condition.ClearExhaustionDeathLine();
        PublishPresentation();
    }

    /// <summary>An escort's portrait in the art set drawn: a generic escort wears a race's head, which has refreshed art.</summary>
    private DaggerfallQuestPresentation DrawnEscorts(DaggerfallQuestPresentation quests) => DrawnArt is { } art
        ? quests with { EscortFaces = [.. quests.EscortFaces.Select(face => face with { MediaId = art.Drawn(face.MediaId) })] }
        : quests;

    public void Dispose()
    {
        if (_disposed) return;
        Casting.ClearTransient();
        _disposed = true;
        // DisposeAll walks backward: projection door entities must release before the actor store.
        Exception? failure = null;
        try { _exterior.ReleaseExteriorWaterTriggers(); }
        catch (Exception exception) { failure = exception; }
        try { _exterior.DropGroundCollider(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { _exterior.RetireResidentExteriorLocations(capture: false); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { DisposeAll([.. Cinematics is null ? Array.Empty<IDisposable>() : new IDisposable[] { Cinematics }, .. _atlasTiles is null ? Array.Empty<IDisposable>() : new IDisposable[] { _atlasTiles }, _hud, _camera, _spatial, State.Actors, _heldEnchantments, _sites.Projection, State.Effects, _sites.Interiors.ActionTriggers, Infections, _horizon, _weatherPresentation]); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { _exterior.RetireExteriorAppearance(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        // The test figure's appearances and rigs are released once no site appearance draws them.
        try { _peopleRigs?.Dispose(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        // The score's loop and the clips it opened belong to this session, so they are retired before
        // the Engine context that produced them is asked for anything else.
        DisposeMusic(ref failure);
        // Site lighting may have retained an interior background after its resources were
        // released; clear that product-owned camera state only after the final projection dispose.
        try { _engine.CameraView.ClearSkyBackground(default); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        if (failure is not null) throw failure;
    }

    private static void DisposeAll(IReadOnlyList<IDisposable> values)
    {
        List<Exception>? failures = null;
        for (int index = values.Count - 1; index >= 0; index--)
        {
            try { values[index].Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void AppendEffectDamage(DaggerfallEffectDamage effect)
    {
        AppendDamage(effect.Result, DaggerfallDamageCause.Effect, 0);
    }

    private void ReactToSpellAttack(long caster, long target, string bundle)
    {
        bool killed = State.Actors.TryGet(target, out var actor) && actor.IsDefeated;
        Crimes.ObserveAttack(caster, target, $"spell-attack:{bundle}:{caster}:{target}:{(killed ? "death" : "attack")}");
        ReactToAttack(caster, target);
    }

    private void ReactToAttack(long caster, long target)
    {
        DaggerfallConcealmentEffects.BreakNormal(State.Effects, caster);
        if (caster != DaggerfallActorIdentity.PlayerEntityId || target == caster) return;
        if (_enemyBehavior.IsPacified(target)) _enemyBehavior.MakeActiveEnemiesHostile();
        State.Quests.ReleaseFoeRestraint(target);
        _enemyBehavior.MakeHostile(target);
    }

    private void AppendSpellTransfer(DaggerfallSpellTransferResult result)
    {
        if (result.HealthDamage is { } health) AppendEffectDamage(new(health));
        if (result.TrackDamage is { } fatigue) AppendSpellTrackLoss(fatigue);
        _facts.Append(new VitalTransferredFact(checked((long)result.Caster.Get<DurableEntityIdentity>().Identity.Value),
            checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value), result.Track.Value, result.Amount,
            result.ActualLoss, result.Restored, result.TargetDefeated, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL));
    }

    private void AppendSpellTrackLoss(DaggerfallSpellTrackResult result)
    {
        long source = checked((long)result.Source.Get<DurableEntityIdentity>().Identity.Value);
        long target = checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value);
        ulong generation = _latestUpdateGeneration ?? 1UL, step = _latestSimulationStep ?? 1UL;
        _facts.Append(result.Track == TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value)
            ? new FatigueAppliedFact(source, target, result.CalculatedLoss, result.ActualLoss, generation, step)
            : new SpellPointsAppliedFact(source, target, result.CalculatedLoss, result.ActualLoss, generation, step));
    }

    private void AppendDamage(DamageResult result, DaggerfallDamageCause cause, int struckBody) =>
        DaggerfallDamageFacts.Append(_facts, result, cause, struckBody, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);

    private void React(IProductFact fact)
    {
        State.Quests.ObserveFoeFact(fact);
        ObservePlaytestCombatFact(fact);
        if (fact is ActorDamagedFact { ActorId: DaggerfallActorIdentity.PlayerEntityId, ActualHealthLost: > 0 } hurt)
        {
            _appearance.PlayPlayerPain(State.Character.Race.Id, State.Character.Identity.Gender == DaggerfallCharacterGender.Female,
                hurt.ActualHealthLost, State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Maximum.Value);
            if (hurt.Cause == DaggerfallDamageCause.Fall) _appearance.PlayGameplaySound(91, "fall-damage");
        }
        if (fact is ActorStruckFact struck) StrikeFeel.Feel(struck);
        if (fact is AttackHitFact hit)
        {
            Crimes.ObserveHit(hit);
            DaggerfallConcealmentEffects.AfterPhysicalHit(State.Effects, hit);
            if(hit.AttackerId==DaggerfallActorIdentity.PlayerEntityId && hit.TargetId!=hit.AttackerId)
                ReactToAttack(hit.AttackerId,hit.TargetId);
        }
        if (fact is ActorDiedFact died)
        {
            ItemSouls.CaptureHeldSoul(died);
            if (died.ActorId==DaggerfallActorIdentity.PlayerEntityId) { IdentifyChoice.Clear(); CreateItemChoice.Clear(); }
            DaggerfallMolagBalEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallItemSoulEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallParalysisEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallContinuousDestructionEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallConcealmentEffects.End(State.Effects, died.ActorId);
            foreach (var light in State.Effects.Active.Where(effect => effect.Definition.Key == DaggerfallIllusionEffects.LightKey
                && effect.Context.Target.Value == (ulong)died.ActorId).ToArray()) State.Effects.Cancel(light.Context.Instance);
            // A corpse rests on the floor: a flyer killed in the air, or a walker on a marker above the floor, falls to it.
            if (died.ActorId != DaggerfallActorIdentity.PlayerEntityId && State.Actors.TryGet(died.ActorId, out ActorState fallen))
                fallen.ApplyPose(new ActorPose(_grounding.RestingPosition(fallen.Position), fallen.HeadingYawRadians));
            _corpseLoot.Create(died);
            _rewards.React(died, _facts);
        }
        if (fact is MagicEffectFact magic)
            _appearance.ReactEffectOutcome(magic.Outcome, State.Actors, State.PlayerControl.Position,
                _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);
        if (fact is SpellCastFact cast)
            _appearance.ReactSpellCast(cast, State.Actors, State.PlayerControl.Position);
        _appearance.UpdateRightHandEquipment(State.Equipment.Read(), State.WeaponDrawn);
        if (fact is EnemyAttackStartedFact enemy)
            (_sites.Appearances.FirstOrDefault(appearance => appearance.HasActor(enemy.AttackerId)) ?? _appearance).React(fact, State.Actors);
        else
        {
            _appearance.React(fact, State.Actors);
            foreach (var appearance in _sites.Appearances)
                if (appearance != _appearance) appearance.ReactResidentActor(fact, State.Actors);
        }
        _outcomes.React(fact);
    }

    /// <summary>The device the player last pressed something on, so the HUD names its bindings for it.</summary>
    private string _lastInputDevice = "keyboard";

    /// <summary>
    /// The device this update's input came from, or null when it carries none: a pad button or a stick
    /// moved past the controller deadzone (so idle drift never switches it), or a key, a mouse button or
    /// mouse look. The latest such event wins.
    /// </summary>
    private static string? InputDevice(ReadOnlySpan<ProductInputEvent> input, float deadzone)
    {
        string? device = null;
        foreach (ProductInputEvent value in input)
        {
            if (value.Kind == InputEventKind.ControllerButton && value.Edge == InputEdge.Pressed) device = "controller";
            else if (value.Kind == InputEventKind.ControllerAxis && MathF.Abs(value.X) > deadzone) device = "controller";
            else if (value.Kind is InputEventKind.Key or InputEventKind.PointerButton && value.Edge == InputEdge.Pressed) device = "keyboard";
            else if (value.Kind == InputEventKind.PointerDelta && (value.X != 0 || value.Y != 0)) device = "keyboard";
        }
        return device;
    }

    private static bool PressesAnyKey(ReadOnlySpan<ProductInputEvent> input)
    {
        foreach (ProductInputEvent value in input)
            if (value.Edge == InputEdge.Pressed && value.Kind is InputEventKind.Key or InputEventKind.PointerButton
                    or InputEventKind.ControllerButton or InputEventKind.MappedDigital or InputEventKind.DirectDigital)
                return true;
        return false;
    }

    private void PublishPresentation()
    {
        _heldEnchantments.Refresh();
        RefreshPassiveMagery();
        _lycanthropy.Refresh();
        _appearance.RefreshEnemyVoices(State.Actors);
        DaggerfallConcealmentEffects.Publish(State.Effects, DaggerfallActorIdentity.PlayerEntityId, Slots);
        DaggerfallDoorMagicEffects.Publish(State.Effects, DaggerfallActorIdentity.PlayerEntityId, Slots);
        _appearance.RetireUnavailableMagic(State.Actors, State.ItemInstances.ContainsUnique);
        InventoryPresentation inventory = _inventoryUi.Read();
        _hud.Publish(new DaggerfallHudFrame(State.Actors.Player, Presentation, _mode, State.PlayerControl, Slots,
            Inventory: inventory,
            Property: ReadPropertyPresentation(),
            Loot: _lootUi.Read(),
            Character: DaggerfallCharacterSheetText.Describe(_characterUi.Read(State.Actors.Player, State.Progression), _definitions, CurrentTextContext()),
            CharacterCreationAvailable: !_newGameInitialized,
            PanelRequest: LatestPanelRequest,
            SaveSlots: _saveSlots,
            Bundles: _mode == ProductMode.Title && !_newGameInitialized ? _bundles : null,
            SaveSlotDiagnostic: _saveSlotDiagnostic,
            ControlSettings: _controls.Settings,
            ControlDiagnostic: _controls.Diagnostic,
            Activation: ActivationView,
            Breath: State.Swimming.ReadBreath(State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
                State.Effects.GrantsWaterBreathing(DaggerfallActorIdentity.PlayerEntityId)),
            Quests: DrawnEscorts(State.Quests.ReadPresentation(QuestTextContext, _time.Calendar)),
            Notebook: _notebook.Read(),
            Transport: DaggerfallTransportProjection.Read(State.Transport, State.Inventory.Read(), TransportAccess(),
                ownsShip: State.Property.OwnsShip, wagon: State.Wagon, itemLabel: (key, definition, quantity) => _inventoryUi.DescribeItem(key, definition, quantity,
                    owner: DaggerfallItemOwner.Wagon(State.Wagon.Id!.Value)).Label),
            DungeonText: _dungeonTextProjection,
            Death: _deathPresentation.View,
            Rest: RestView,
            Lodging: LodgingView,
            Travel: FastTravel.Read(),
            SiteName: Site.ActiveSite?.Name,
            Calendar: _time.Calendar,
            Effects: ReadActiveEffects(),
            CinematicSource: Cinematics?.ActiveSource,
            Map: _mapOpen ? ReadMapPresentation() : null, Legal: Law.View, CreateItem: CreateItemChoice.View, Teleport: TeleportChoice.View, Dispel: DispelChoice.View, Identify: IdentifyChoice.View, Spells: ReadSpells(), Detectors: Detectors.Read())
        {
            ActionDrivenTime = ActionDrivenPlay, TimeHeld = PlayerTimeHeld,
            Weapon = new DaggerfallWeaponView(State.WeaponDrawn,
                inventory.Items.FirstOrDefault(item => item.EquippedSlots.Contains("right-hand"))?.Label ?? "Bare hands"),
            Prompt = ReadInteractionPrompt(),
            InputDevice = _lastInputDevice,
            Loading = _loading,
            Screens = DaggerfallUiArt.Screens.ToDictionary(screen => screen, screen => DrawnArt?.Drawn(screen) ?? screen, StringComparer.Ordinal),
            TutorialOffer = _tutorial.Offer,
            Radial = RadialCommands(ActivationView.Mode),
            TutorialRunning = _tutorial.Running,
            Tutorial = _tutorial.View,
        });
        _appearance.UpdateRightHandEquipment(State.Equipment.Read(), State.WeaponDrawn);
        Vector3 facing = ActorHeading.Forward(State.PlayerControl.YawRadians);
        Vector3? candlePosition = !State.Actors.Player.IsDefeated && State.PlayerControl.Position is { } playerPosition
            && State.Effects.Active.Any(effect => effect.Definition.Key == DaggerfallIllusionEffects.LightKey)
            ? playerPosition.ToVector() + new Vector3(facing.X * _tuning.NormalLight.Distance,
                _spatial.CurrentController.Shape.StandingHeight * _tuning.NormalLight.HeightFraction,
                facing.Z * _tuning.NormalLight.Distance) : null;
        _sites.Projection.Lighting.UpdateMobileLights(State.Actors.All, _roster.Definitions, _definitions.Mobiles);
        IReadOnlyList<DaggerfallDungeonSpellFlightView> spellFlights = SpellFlights.Views();
        _sites.Projection.Lighting.UpdateSpellLights(spellFlights);
        _sites.Projection.Lighting.UpdateMagicCandle(candlePosition, _tuning.NormalLight);
        _appearance.UpdateMagicCandle(candlePosition);
        if (State.PlayerControl.Position is { } eyeFeet)
            _viewLight.Update(() => _exterior.ViewLight(eyeFeet.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight));
        foreach (var appearance in _sites.Appearances) appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors, _groundContainers.All.Where(pair => pair.Value.PropertyPlacement is null).ToDictionary(),
            _latestUpdateGeneration is ulong generation && _latestSimulationStep is ulong simulationStep
                ? _combat.ReadRangedFlights(generation, simulationStep) : [],
            State.Effects.PerceptionFor, NpcProjection.Views(), spellFlights, State.Npcs.IsGameplayActive,
            id => _groundContainers.Read(id) is { } contents ? _inventoryUi.GroundLook(id, contents) : null);
        PublishDebugOverlays();
    }

    /// <summary>The carried inventory as the HUD publishes it, eligibility included.</summary>
    internal InventoryPresentation ReadInventoryPresentation() => _inventoryUi.Read();

    /// <summary>The player's active effects as the HUD lists them.</summary>
    internal IReadOnlyList<DaggerfallActiveEffectView> ReadActiveEffects() =>
        DaggerfallMagicPresentation.Read(State.Effects, State.Actors.Player, EffectItemName);

    /// <summary>The panel the player asked for through a device the DOM has no channel of its own for.</summary>
    internal DaggerfallPanelRequest? LatestPanelRequest => _interactions.LatestPanelRequest;

    /// <summary>What is open over the world and which of it holds the world.</summary>
    internal DaggerfallOpenInteractions Interactions => _interactions;

    private void RequestPanel(string panel)
    {
        if (panel == DaggerfallPanel.Inventory && State.RacialOverrides.Current?.SuppressInventory == true)
        {
            Presentation.SetOutcome(DaggerfallRacialOverrideView.InventoryRefusal);
            return;
        }
        _interactions.RequestPanel(panel);
    }

    /// <summary>Optional compiled ranged-flight owner; invoked once for every admitted realtime update.</summary>

    internal TargetingEvidence? LastMeleeTargeting => State.Kit.Targeting.LastEvidence;

    private void DeliverFacts()
    {
        _facts.Deliver(React);
    }
    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEnemyBehavior => _enemyBehavior.LastEvidence;
    internal DaggerfallEnemyBehaviorModule EnemyBehavior => _enemyBehavior;
    internal IReadOnlyDictionary<long, DaggerfallEnemySpellEvidence> LastEnemySpell => _enemyMagic.LastEvidence;
    internal LootPresentation? OpenLoot => _lootUi.Read();

    /// <summary>Typed equipment moves over live state: the same operations the UI adapter uses.</summary>
    internal DaggerfallEquipmentMoves EquipmentMoves => _equipmentMoves;
    /// <summary>Typed mutation and disclosure of persisted item condition and magic meaning.</summary>
    internal DaggerfallItemConditionService ItemCondition => _itemCondition;
    /// <summary>Enchanting a live item the player holds (Magic).</summary>
    internal DaggerfallItemEnchanting ItemEnchanting => _itemEnchanting;
    internal DaggerfallDispel DispelChoice { get; } = null!;
    internal DaggerfallIdentify IdentifyChoice { get; } = null!;
    internal DaggerfallCreateItem CreateItemChoice { get; } = null!;
    internal DaggerfallItemSouls ItemSouls { get; } = null!;
    internal DaggerfallTeleport TeleportChoice { get; } = null!;

    /// <summary>Fast travel: the travel panel, its map and the journeys it starts.</summary>
    internal DaggerfallFastTravel FastTravel { get; } = null!;

    /// <summary>The player's crimes: pickpocketing, witnesses, and what an attack on a civilian or a guard charges.</summary>
    internal DaggerfallCrimes Crimes { get; } = null!;

    /// <summary>The law: the legal response, the court, the sentence and the standing law.</summary>
    internal DaggerfallLaw Law { get; } = null!;

    /// <summary>The guilds' providers: what a guild's NPC answers to its topics, its welcome and its free passage.</summary>
    internal DaggerfallGuildProviders GuildProviders { get; } = null!;

    /// <summary>The atlas the import drew and the tiles granted to the page, or null when the data container carries none and the travel map draws classic art.</summary>
    private readonly DaggerfallAtlasTiles? _atlasTiles;
    internal DaggerfallDetectors Detectors { get; } = null!;
    internal DaggerfallCasting Casting { get; private set; } = null!;
    internal DaggerfallSpellTargets SpellTargets { get; } = null!;
    internal DaggerfallSpellRelease SpellRelease { get; } = null!;
    internal DaggerfallSpellFlights SpellFlights { get; } = null!;
    /// <summary>The end of an item's life: unique retirement, stack consumption, a quest's items taken back.</summary>
    internal DaggerfallItemLifetime ItemLifetime => _itemLifetime;
    /// <summary>The Mages Guild's enchanting service (Economy).</summary>
    internal DaggerfallItemMaker ItemMaker => _itemMaker;
    /// <summary>The Mages Guild's potion maker (Economy).</summary>
    internal DaggerfallPotionMaking PotionMaker => _potionMaker;
    internal CorpseLootEvidence? LastCorpseLoot => _corpseLoot.LastEvidence;
    internal CorpseLootCommitEvidence? LastCorpseLootCommit => _corpseLoot.LastCommit;
    internal IReadOnlyDictionary<long, CorpseContainer> Corpses => _corpseLoot.Corpses;

    /// <summary>The session's durable identity ledger for generated unique loot.</summary>
    internal DaggerfallUniqueItemAllocator UniqueItemAllocator => _uniqueItems;

    /// <summary>
    /// Records that one generated unique item no longer exists, so its identity is
    /// tombstoned. Authored content identities are refused — they are reserved rather
    /// than issued, and tombstoning one would misreport content as removed.
    /// </summary>
    internal void RemoveUniqueItemIdentity(ulong entityId)
    {
        if (_authoredEntityIds.Contains(entityId))
            throw new InvalidOperationException($"Unique item identity {entityId} is authored content and cannot be removed.");
        _uniqueItems.Remove(new DurableIdentityReference(DurableIdentityKind.Item, entityId));
    }

    /// <summary>
    /// What ordinary use would do to the target the player aims at, named for the HUD's prompt; null
    /// outside play or when nothing usable is aimed at.
    /// </summary>
    private DaggerfallInteractionPromptView? ReadInteractionPrompt()
    {
        if (_mode != ProductMode.Playing || _interactions.HoldsWorldOpen || _activation is null) return null;
        if (_activation.FocusedTarget(State.Actors.Player.Actor.Entity, State.PlayerControl, _input.ResolveCurrentLook(State.PlayerControl))
            is not { } target) return null;
        // The owner of what is aimed at words what use would do from its own state; where use would do
        // nothing (a door in Talk mode, a person in Bash), there is no prompt to promise it.
        if (_activation.PromptAction(target) is not { } action) return null;
        return new(action, string.IsNullOrWhiteSpace(target.Label) ? DaggerfallHudText.TargetName(target.Kind) : target.Label);
    }

    /// <summary>The name of the item a player effect comes from, for the active-effect status row.</summary>
    internal string EffectItemName(ulong item) => State.ItemInstances.ContainsUnique(item)
        && _definitions.TryResolveItem(new(State.ItemInstances.RequireUnique(item).ItemId), out DaggerfallItemDefinition? definition)
        ? DaggerfallItemNames.Label(_definitions, definition, State.ItemInstances.RequireUnique(item))
        : "an enchanted item";
}
