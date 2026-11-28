using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
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
internal sealed partial class DaggerfallSession : ISaveableGameSession, IModeAwareGameSession, IEntryScreenSession, IEntryScreenStartupSession, ICharacterCreationSession, ISaveRequestingGameSession, IPlayerPreferencesSession, IPlayerDefeatOutcomeSession, IGameBundleChoiceSession, IQuitRequestingGameSession,
    IDaggerfallSiteTransitionHost, IDaggerfallRestSurroundings, IDaggerfallExpeditionSurroundings, IDaggerfallTeleportSites, IDaggerfallTransformationConsumer,
    IDaggerfallConvoyFight,
    IDaggerfallMusicPlace, IPresentationYieldingSession
{
    private readonly DaggerfallGameRandom _random;
    private readonly ulong _worldSocialSeed;
    internal ulong WorldSocialSeed => _worldSocialSeed;
    private readonly IEngineContext _engine;
    private readonly DaggerfallTuning _tuning;
    private readonly DaggerfallViewLight _viewLight;
    private readonly DaggerfallInteriorLook _interiorLook;
    private PlayerInputSystem _input;
    private ProductMode _mode = ProductMode.Playing;
    // The line a mode change says.
    private readonly DaggerfallModeLine _modeLine;
    private readonly SpatialMovementSystem _spatial;
    /// <summary>Whether static geometry covers a sound from the listener.</summary>
    private readonly DaggerfallSoundCover _soundCover;
    /// <summary>Harm and spell delivery's physical contact over the admitted scene and canonical actor bodies.</summary>
    private readonly ActorContact _contact;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerfallCombatRules _combat;
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
    /// <summary>The player's movement in each simulation step.</summary>
    private readonly DaggerfallPlayerMovement _movement;
    /// <summary>When the player's acts run: now, or behind an enemy hit on the player in flight.</summary>
    private readonly DaggerfallPlayerActTiming _actTiming;

    /// <summary>Readies or sheathes the weapon when the hands are free (Travel's rule): its key on a step, or the radial's command, which a held world never steps.</summary>
    private void ToggleWeapon()
    {
        if (State.Transport.WeaponHandsFree) State.WeaponDrawn = !State.WeaponDrawn;
        else InputRefusals.Refused("Weapon toggle", "the reins take the hands");
    }
    private readonly DaggerfallDungeonVisibility _dungeonVisibility;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly DaggerfallEnemyMagicModule _enemyMagic;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallGroundContainers _groundContainers;
    /// <summary>The loose piles' bodies near the player, and the one the player carries.</summary>
    internal DaggerfallLooseItems LooseItems { get; }
    private readonly DaggerfallBookNotebook _notebook;
    private readonly DaggerfallEncounterRuntime _encounters;
    private readonly DaggerfallActorPlacement _placement;
    private readonly DaggerfallNearbySpawn _nearbySpawn;
    private readonly DaggerfallEnemySenses _senses;
    internal DaggerfallEnemySenses Senses => _senses;
    private readonly DaggerfallDungeonPopulation _dungeonPopulation;
    internal DaggerfallCivilians Civilians { get; }
    internal DaggerfallSourcePopulation Population { get; }
    internal DaggerfallHomeNightRoutine HomeNightRoutine { get; }
    internal DaggerfallWorldEncounters WorldEncounters { get; }

    /// <summary>Unified proximity query over the single active-scene <c>EntityStore</c>; excludes off-stage and <c>WithParty</c> escorts.</summary>
    internal IReadOnlyList<NearbyActorHit> QueryNearby(WorldPoint origin, double radius,
        Func<ActorState, bool>? predicate = null, bool includeDefeated = false) =>
        DaggerfallActorQueries.QueryNearby(State.Actors, origin, radius, predicate, State.Npcs.IsGameplayActive, includeDefeated);
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly DaggerfallSessionPersistence _persistence;
    private readonly DurableIdentityAllocator _actorIdentities;
    private readonly DaggerfallHeldEnchantments _heldEnchantments;
    private readonly DaggerfallItemCastTriggers _itemCastTriggers = null!;
    private readonly DaggerfallActorRoster _roster;
    internal DaggerfallActorRoster Roster => _roster;
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
    internal DaggerfallSiteAppearance ActiveAppearance => _appearance;
    private DaggerfallDoorRuntime _doors => _sites.Projection.Doors;
    /// <summary>Exterior doors' buildings: their names, entry rules, greetings and what entering comes to.</summary>
    internal DaggerfallBuildingEntrance Entrance { get; }
    /// <summary>Picking and bashing a door's lock.</summary>
    internal DaggerfallLocks Locks { get; }
    /// <summary>What the active site's dungeon actions do and how the player sets them off.</summary>
    internal DaggerfallDungeonActions DungeonActions { get; } = null!;
    /// <summary>The activation in force over the active site.</summary>
    internal DaggerfallActivation Activation { get; } = null!;
    private DaggerfallActivationModule? _activation => Activation.Module;
    // Where a body or a person is sighted for use.
    private readonly DaggerfallActivationSight _sight = null!;
    /// <summary>The wild grid walkers path on outdoors, past a location's own navigation.</summary>
    internal DaggerfallWildNavigation WildNavigation { get; } = null!;
    /// <summary>Every walker's navigation step: the location's grid, the wild grid, then a straight walk.</summary>
    internal ActorNavigationCoordinator Navigation { get; } = null!;

    /// <summary>Where entering through an exterior door lands inside its building.</summary>
    private DaggerfallSiteAnchor BuildingLanding(DaggerfallSiteProfile inside, DaggerfallRdbDoorId door) =>
        DaggerfallBuildingArrival.Landing(inside, _exterior.LocalToProfile(_doors.Read(door).Pose.Translation), _spatial.CurrentController.Shape);

    /// <summary>The weather where the player is; rules read its conditions.</summary>
    internal DaggerfallWeather LocalWeather { get; }
    /// <summary>The ordinary music: what plays where the player stands.</summary>
    internal DaggerfallMusic Music { get; }
    private readonly DaggerfallWeatherPresentation _weatherPresentation;
    private readonly DaggerfallHorizon _horizon;
    /// <summary>The expedition's travel map as a world the Engine draws.</summary>
    private readonly DaggerfallOverlandMap _overland;
    /// <summary>The travel panel, whose world map the page reports open or closed.</summary>
    private DaggerfallExpeditionPanel? _travelPanel;
    internal DaggerfallExpeditionPanel TravelPanel => _travelPanel!;

    /// <summary>Whether river water about the player is still being built, a few map pixels an update.</summary>
    internal bool RiverWaterBuilding => _horizon.RiversBuilding;

    /// <summary>The transport access where the player stands (Travel's rule), with whether an owned ship is reachable here (Economy's).</summary>
    private DaggerfallTransportAccessContext TransportAccess() => DaggerfallTransportAccessContext.At(_activeProfileKey.Kind,
        State.PlayerControl.Position, _sites.Projection.Inputs.DungeonMap, Holdings.OwnedShipProfile() is not null);

    private readonly World.DaggerfallWorldTime _time;
    private readonly World.DaggerfallHolidays _holidays = new();
    private readonly DaggerfallRest _rest;
    /// <summary>Where the player may rest inside a building, and a tavern's let room.</summary>
    private readonly DaggerfallRestPlaces _restPlaces;
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
        DaggerfallMagicDefense.CareerAbsorption(_passives.AbsorbsSpells(actorId, InSunlight()));

    // Character creation from the title screen.
    private readonly DaggerfallCharacterCreation _creation;
    private readonly DaggerfallSessionComposition _composition;

    /// <summary>
    /// Whether this run draws classic art in place of the refreshed art set: a developer's comparison, switched by
    /// <c>world.classic-art</c>, never offered to a player nor kept past the run.
    /// </summary>
    private bool _classicArt;
    /// <summary>The active dungeon's or interior's hanging pieces, while one is active; none outdoors.</summary>
    private DaggerfallHanging? _hanging;

    /// <summary>The refreshed art set the session draws, or null when a developer switched to classic art or the bundle carries none.</summary>
    private DaggerfallArtReplacements? DrawnArt => _classicArt ? null : _composition.ArtReplacements;

    /// <summary>The UI art the session publishes, which reads a classic image the art set draws anew only for classic art.</summary>
    private DaggerfallUiArt _uiArt = DaggerfallUiArt.None;

    /// <summary>
    /// Switches the run to classic art or back. Refreshed art opens no classic file of an entry the art set draws anew,
    /// so they are built at the switch: every live site builds the classic visuals it left unbuilt and the UI art reads the
    /// classic images it left unread. A file that cannot be opened refuses the switch, named, and the run stays in
    /// refreshed art; what was built is kept, and drawn only by classic art.
    /// </summary>
    /// <returns>Why classic art cannot be drawn, or null once the switch is made.</returns>
    private string? UseClassicArt(bool classic)
    {
        if (classic && !_classicArt && _composition.ArtReplacements is not null)
        {
            // A player's data container leaves the classic media the pack draws anew out; the developer's container holds it.
            if (_composition.Data?.ClassicArtMissing is { } missing)
                return $"{missing} The data left out the classic art the refreshed art draws anew; import with --classic-art to compare them.";
            try
            {
                foreach (DaggerfallSiteAppearance appearance in _sites.Appearances) appearance.BuildClassic();
                _exterior.BuildClassicNature();
                _uiArt.DemandClassic();
            }
            catch (InvalidOperationException refused) { return refused.Message; }
        }
        _classicArt = classic;
        return null;
    }

    /// <summary>The live-debug modules the developer and playtest owners answer, which the host registers for this session.</summary>
    public IReadOnlyList<IDebugCommandModule> DebugCommands { get; } = null!;

    /// <summary>The playtest state writer: the gameplay facts, the last combat event and the last use.</summary>
    private readonly DaggerfallPlaytestState _playtest = null!;

    /// <summary>The debug overlay sets, built each presentation while on.</summary>
    private readonly DaggerfallDebugOverlays _overlays = null!;

    /// <summary>The switch that draws walking people as the parts-rig test figure.</summary>
    private readonly DaggerfallPeopleSwitch _people = null!;
    private bool _newGameInitialized;
    public bool RequiresCharacterInitialization => !_newGameInitialized;

    public bool HasCommittedCharacter => !_newGameInitialized && _creation.Committed;

    public void OpenCharacterCreation()
    {
        if (_newGameInitialized || !_creation.OpenFresh()) return;
        Presentation.SetOutcome("Character choices opened.");
    }
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

    /// <summary>
    /// The walkers' figures bind animation instances to the walkers' objects, which a replacement publishes too, and hold
    /// the characters' bodies it binds again: a snapshot of their sprites lets the instances go, then the figures go.
    /// </summary>
    public void YieldPresentation()
    {
        _people.Yield();                                                                          // Developer
        PublishPresentation();                                                                    // UiProjection
        _people.Dispose();                                                                        // Developer
    }
    internal bool ActionDrivenPlay => _controls.ActionDrivenTime;
    private readonly World.DaggerfallSiteContext _site;

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
    /// <summary>The one seam a player input that did nothing reports through.</summary>
    internal DaggerfallInputRefusals InputRefusals { get; } = new();
    /// <summary>The developer's input readout, which the HUD carries while it is on.</summary>
    internal DaggerfallInputReadout InputReadout { get; } = null!;
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
    /// <summary>This update's play waits for arrival ground the Engine has not made live, which holds the world.</summary>
    private bool _groundless;
    /// <summary>
    /// An arrival stood and drawn whose loading screen has not yet cleared over it: the update that clears the screen
    /// shows the scene uncovered and takes no step, so nothing acts before the player has seen where they are.
    /// </summary>
    private bool _arrivalUnseen;
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

    /// <summary>Admits the full selected site catalog once composition has constructed this session.</summary>
    internal void AdmitSiteProfiles(DaggerfallSiteProfiles profiles)
    {
        _sites.AdmitProfiles(profiles);
        if (_sites.Projection.Inputs.ProfileKind == DaggerfallWorldProfileKind.Interior)
            ReconcileInteriorScheduledVisitors(_sites.Projection.Inputs, _appearance);
    }

    /// <summary>The session's current in-game calendar.</summary>
    internal DaggerfallCalendar Calendar => _time.Calendar;

    /// <summary>The session's site lifecycle: projection, catalog, deltas and transitions.</summary>
    internal DaggerfallSiteCrossing Sites => _sites;

    /// <summary>The cells streamed around the player and the towns placed on them.</summary>
    internal DaggerfallExteriorWorld Exterior => _sites.Exterior;

    /// <summary>The admitted spatial scene, for Engine queries made outside an update such as a test looking along a wall.</summary>
    internal SpatialMovementSystem Spatial => _spatial;

    /// <summary>
    /// Attempts one real site transition, landing at the named destination anchor when there is no entrance to
    /// return through; failed destination admission leaves the source projection live.
    /// </summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination, string? arrivalAnchor = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryTransitionTo(destination, arrivalAnchor);
    }

    void IDaggerfallExteriorHost.RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians, CharacterStance? stance) =>
        ApplyRelocation(position, yawRadians, pitchRadians, stance);

    void IDaggerfallExteriorHost.ConfigureSiteAppearance(DaggerfallSiteAppearance appearance) =>
        ConfigureSiteAppearance(appearance);

    void IDaggerfallSiteTransitionHost.ReconcileArrivalPopulation()
    {
        ResetScheduleWeatherBaseline();
        Population.ReconcileImmediate();
        SyncActiveGuardLivery();
    }

    private (DaggerfallSiteId? Site, DaggerfallWorldProfileKey Profile, int? SovereignFactionId, bool Occupied, int ResidentDigest, int LiveGuardDigest)? _lastGuardLiverySignature;

    /// <summary>
    /// Synchronizes live guard faction identity, <see cref="ActorRenderComponent"/> livery, and
    /// <see cref="DaggerfallSiteAppearance"/> tint with each guard's owning site's live political allegiance and
    /// conquest-occupation state (Den #11167).
    /// </summary>
    internal void SyncActiveGuardLivery()
    {
        if (GuardPosts is null || _sites?.Projection is not { } projection)
            return;
        DaggerfallSiteRecord? activeSite = _site.ActiveSite;
        DaggerfallSiteProfile activeProfile = projection.Inputs;
        IReadOnlyList<DaggerfallPopulationPlacement> placements = activeSite is not null
            ? ExteriorPlacementsFor(activeSite.Id)
            : [];
        GuardPosts.ApplyLiveGuardAppearances(
            activeSite,
            activeProfile,
            placements,
            State.Actors,
            State.Npcs,
            _appearance,
            resolveProfileContext: ResolveGuardProfileContext);

        _lastGuardLiverySignature = ComputeGuardLiverySignature(activeSite, activeProfile);
    }

    private (DaggerfallSiteRecord? Site, DaggerfallSiteProfile? Profile, IReadOnlyList<DaggerfallPopulationPlacement> Placements, DaggerfallSiteAppearance? Appearance)?
        ResolveGuardProfileContext(DaggerfallWorldProfileKey profileKey)
    {
        if (_exterior.TryGetResidentLocationContext(profileKey, out DaggerfallSiteProfile? residentProfile, out DaggerfallSiteAppearance? residentAppearance))
        {
            DaggerfallSiteRecord? residentSite = residentProfile.Site is { } sid ? _site.Require(sid) : null;
            return (residentSite, residentProfile, residentProfile.Population, residentAppearance);
        }
        return null;
    }

    private (DaggerfallSiteId? Site, DaggerfallWorldProfileKey Profile, int? SovereignFactionId, bool Occupied, int ResidentDigest, int LiveGuardDigest)
        ComputeGuardLiverySignature(DaggerfallSiteRecord? activeSite, DaggerfallSiteProfile activeProfile)
    {
        int region = activeSite?.Id.Region ?? activeProfile.Site?.Region ?? 0;
        HashCode residentHash = new();
        foreach (DaggerfallWorldProfileKey residentKey in _exterior.ResidentLocationKeys.OrderBy(k => k.LogicalId, StringComparer.Ordinal))
        {
            if (_exterior.TryGetResidentLocationContext(residentKey, out DaggerfallSiteProfile? resProfile, out _))
            {
                int resRegion = resProfile.Site?.Region ?? 0;
                residentHash.Add(residentKey.LogicalId, StringComparer.Ordinal);
                residentHash.Add(State.Social.Liege.SovereignOf(resRegion)?.Id ?? -1);
                residentHash.Add(GuardPosts.IsRegionOccupied(resRegion));
            }
        }
        HashCode guardHash = new();
        foreach (ActorState actor in State.Actors.All)
        {
            if (actor.DurableId == DaggerfallActorIdentity.PlayerEntityId)
                continue;
            bool isWatch = (actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc)
                    && string.Equals(fc.CreatureFactionId, "city-watch", StringComparison.OrdinalIgnoreCase))
                || (State.Npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc)
                    && string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase));
            if (!isWatch)
                continue;
            guardHash.Add(actor.DurableId);
            if (actor.Actor.TryGet<ActorRenderComponent>(out ActorRenderComponent? render))
            {
                guardHash.Add((int)render.Spec.Kind);
                guardHash.Add(render.Spec.LiveryKey ?? string.Empty, StringComparer.Ordinal);
            }
        }
        return (
            activeSite?.Id,
            activeProfile.ProfileKey,
            State.Social.Liege.SovereignOf(region)?.Id,
            GuardPosts.IsRegionOccupied(region),
            residentHash.ToHashCode(),
            guardHash.ToHashCode());
    }

    /// <summary>
    /// Detects live board allegiance or conquest-occupation changes for the active site (or any resident
    /// neighboring exterior) and refreshes exterior garrison admission plus live special-post guard appearances.
    /// </summary>
    internal void ReconcileGuardLiveryIfAllegianceChanged()
    {
        if (GuardPosts is null || _sites?.Projection is not { } projection)
            return;
        DaggerfallSiteRecord? activeSite = _site.ActiveSite;
        DaggerfallSiteProfile activeProfile = projection.Inputs;
        var current = ComputeGuardLiverySignature(activeSite, activeProfile);
        if (_lastGuardLiverySignature == current)
            return;
        if (activeProfile.ProfileKind == DaggerfallWorldProfileKind.Exterior && _exterior.ActiveLocationLoaded)
            Population.Reconcile(immediate: false);
        SyncActiveGuardLivery();
    }

    void IDaggerfallSiteTransitionHost.ReconcileInteriorScheduledVisitors(
        DaggerfallSiteProfile destination,
        DaggerfallSiteAppearance appearance) =>
        ReconcileInteriorScheduledVisitors(destination, appearance);

    internal void ReconcileInteriorScheduledVisitors(
        DaggerfallSiteProfile destination,
        DaggerfallSiteAppearance appearance)
    {
        AdmitInteriorScheduledVisitors(destination, appearance, assignment: null);
        ReconcileHomeNightRoutine(destination, appearance, immediate: true);
        SyncActiveGuardLivery();
    }

    /// <summary>
    /// Reconciles an active interior's scheduled visitors and schedule-suppressed home static slots across an
    /// elapsed time jump (rest, wait, loiter, or debug time skip) with zero orphaned actors, zero duplicate bodies,
    /// and zero save bytes for untouched NPCs.
    /// </summary>
    internal void ReconcileInteriorScheduleCatchUp(
        DaggerfallSiteProfile destination,
        DaggerfallSiteAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(appearance);
        if (destination.ProfileKind != DaggerfallWorldProfileKind.Interior
            || destination.Site is not { } siteId
            || destination.InteriorBuilding is not { } interiorBuilding)
            return;

        DaggerfallTownShelterAssignment assignment = ResolveTownShelterAssignment(siteId, destination.BillboardSprites);
        DaggerfallSiteBuildingId buildingId = new(interiorBuilding.BlockX, interiorBuilding.BlockY, interiorBuilding.Building.Index);
        HashSet<long> desiredVisitors = [.. assignment.VisitorsFor(buildingId).Select(v => v.DurableId)];
        IReadOnlySet<long> suppressedStatic = assignment.SuppressedSlotsFor(buildingId);

        // 1. Unload stale scheduled visitors no longer scheduled into this interior (flushing sparse diffs first).
        foreach (ActorState live in State.Actors.All.ToArray())
        {
            if (live.DurableId == DaggerfallActorIdentity.PlayerEntityId || live.IsDefeated)
                continue;
            if (!live.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp)
                || !originComp.IsScheduledVisitor)
                continue;
            if (desiredVisitors.Contains(live.DurableId))
                continue;
            _persistence.FlushLiveDeterministicActorToLedger(live, destination.ProfileKey, p => p);
            _roster.UnloadActor(live.DurableId, appearance);
        }

        // 2. Reconcile home static slots that became scheduled away at a tavern or returned when hours reopened.
        bool needsStaticRematerialize = false;
        foreach (DaggerfallStaticNpcPlacement placement in destination.StaticNpcs)
        {
            if ((!string.IsNullOrWhiteSpace(placement.CastId) && _definitions.TryGetCastOrNoble(placement.CastId, out _))
                || (placement.Appearance.FactionId > 0 && _definitions.TryGetCastOrNobleByFaction(placement.Appearance.FactionId, out _)))
                continue;
            long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{destination.ProfileKey.LogicalId}/{placement.Id}");
            if (suppressedStatic.Contains(id))
            {
                if (State.Actors.TryGet(id, out ActorState? liveStatic)
                    && !liveStatic.IsDefeated
                    && liveStatic.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? staticOrigin)
                    && !staticOrigin.IsScheduledVisitor
                    && !staticOrigin.IsRelocated
                    && staticOrigin.Origin.Kind == ActorOriginKind.SeededSlot)
                {
                    _persistence.FlushLiveDeterministicActorToLedger(liveStatic, destination.ProfileKey, p => p);
                    _roster.UnloadActor(id, appearance);
                }
            }
            else if (!State.Actors.TryGet(id, out _)
                && !State.ActorLedger.IsHomeSlotSuppressed(id, destination.ProfileKey)
                && !_roster.BanishedActors.Contains(id))
            {
                needsStaticRematerialize = true;
            }
        }
        if (needsStaticRematerialize)
            _roster.MaterializeStaticNpcs(destination, appearance);

        // 3. Admit any newly scheduled visitors without overlapping already-standing live actors.
        AdmitInteriorScheduledVisitors(destination, appearance, assignment);

        // 4. Sync residential night lights-out and bed sleep state after interior population settles.
        ReconcileHomeNightRoutine(destination, appearance, immediate: true);
    }

    private void ReconcileHomeNightRoutine(
        DaggerfallSiteProfile destination,
        DaggerfallSiteAppearance appearance,
        bool immediate)
    {
        if (HomeNightRoutine is null || _sites?.Projection is not { } projection)
            return;
        HomeNightRoutine.Reconcile(destination, projection.Lighting, appearance, _time.Calendar.Sky, immediate);
    }

    private void AdmitInteriorScheduledVisitors(
        DaggerfallSiteProfile destination,
        DaggerfallSiteAppearance appearance,
        DaggerfallTownShelterAssignment? assignment)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(appearance);
        if (destination.ProfileKind != DaggerfallWorldProfileKind.Interior
            || destination.Site is not { } siteId
            || destination.InteriorBuilding is not { } interiorBuilding)
            return;

        assignment ??= ResolveTownShelterAssignment(siteId, destination.BillboardSprites);
        DaggerfallSiteBuildingId buildingId = new(interiorBuilding.BlockX, interiorBuilding.BlockY, interiorBuilding.Building.Index);
        IReadOnlyList<DaggerfallScheduledVisitor> visitors = assignment.VisitorsFor(buildingId);
        if (visitors.Count == 0)
            return;

        DaggerfallScheduledVisitor[] pending = [.. visitors.Where(v => !State.Actors.TryGet(v.DurableId, out _))];
        if (pending.Length == 0)
            return;

        WorldPoint[] liveOccupied = [.. State.Actors.All
            .Where(a => a.DurableId != DaggerfallActorIdentity.PlayerEntityId)
            .Select(a => a.Position)];
        IReadOnlyList<(WorldPoint Position, float HeadingRadians)> standPoints =
            DaggerfallScheduleShelterResolver.ResolveInteriorStandPoints(destination, pending.Length, liveOccupied);
        int admitCount = Math.Min(pending.Length, standPoints.Count);
        for (int i = 0; i < admitCount; i++)
        {
            DaggerfallScheduledVisitor visitor = pending[i];
            (WorldPoint pos, float heading) = standPoints[i];
            ActorOrigin origin = ActorOrigin.Seeded(
                visitor.HomeProfile,
                visitor.HomeSlotId,
                visitor.SpawnerId,
                visitor.Harm,
                visitor.Interaction);
            _ = _roster.MaterializeScheduledInteriorVisitor(
                destination,
                appearance,
                visitor.DurableId,
                origin,
                visitor.Kind,
                visitor.HomeSlotId,
                visitor.Appearance,
                visitor.Role,
                visitor.Services,
                pos,
                heading,
                visitor.FamilyId,
                visitor.DisplayName,
                visitor.Harm,
                visitor.Interaction);
        }
    }

    internal IReadOnlySet<long> SuppressedStaticSlotsFor(DaggerfallSiteProfile profile)
    {
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Interior
            || profile.Site is not { } siteId
            || profile.InteriorBuilding is not { } interiorBuilding
            || interiorBuilding.BuildingType == 15
            || DaggerfallScheduleShelterResolver.IsResidenceBuildingType(interiorBuilding.BuildingType))
            return DaggerfallTownShelterAssignment.Empty.SuppressedSlotsFor(default);
        DaggerfallTownShelterAssignment assignment = ResolveTownShelterAssignment(siteId, profile.BillboardSprites);
        DaggerfallSiteBuildingId buildingId = new(interiorBuilding.BlockX, interiorBuilding.BlockY, interiorBuilding.Building.Index);
        return assignment.SuppressedSlotsFor(buildingId);
    }

    internal DaggerfallTownShelterAssignment ResolveTownShelterAssignment(
        DaggerfallSiteId siteId,
        IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite>? availableBillboards = null)
    {
        if (!_site.TryFind(siteId, out DaggerfallSiteRecord siteRecord)
            || siteRecord.Exterior is not { Buildings.Count: > 0 } exterior)
            return DaggerfallTownShelterAssignment.Empty;
        DaggerfallSiteBuildingSource[] buildings = [.. exterior.Buildings.Values];

        IReadOnlyList<DaggerfallPopulationPlacement> exteriorPlacements = [];
        DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(siteId);
        if (_sites?.Profiles is { } profiles && profiles.TryGet(exteriorKey, out DaggerfallSiteProfile exteriorProfile))
            exteriorPlacements = exteriorProfile.Population;

        int minuteOfDay = checked(_time.Calendar.Hour * DaggerfallCalendar.MinutesPerHour + _time.Calendar.Minute);
        DaggerfallWeatherKind weather = LocalWeather.Current;
        DaggerfallWeatherShelterElapsed weatherElapsed = ObserveScheduleWeatherElapsed();
        int holidayId = _time.Calendar.GetHolidayId(siteId.Region);
        HashSet<long> officeHolderIds = OfficeLedger.CollectOfficeHolderIds(siteRecord);

        return DaggerfallScheduleShelterResolver.ResolveTown(
            _worldSocialSeed,
            _definitions,
            siteRecord,
            buildings,
            exteriorPlacements,
            minuteOfDay,
            weather,
            weatherElapsed,
            holidayId: holidayId,
            isActiveQuestor: id => State.Quests.QuestContacts(id).Count > 0 || State.Quests.IsNpcUnavailable(id),
            isOfficeHolder: officeHolderIds.Contains,
            isHomeSlotSuppressed: (id, homeProfile) =>
                State.ActorLedger.IsHomeSlotSuppressed(id, homeProfile)
                || _roster.BanishedActors.Contains(id)
                || State.Quests.IsEscortWithParty(id)
                || (State.ActorLedger.TryGet(id, out ActorLedgerEntry? entry)
                    && entry?.Location?.Kind is ActorLocationKind.WithParty or ActorLocationKind.OnOverlandBand
                        or ActorLocationKind.Dead or ActorLocationKind.Removed or ActorLocationKind.OffStage)
                || (State.Npcs.TryGet(id, out DaggerfallNpc? existingNpc) && existingNpc.Presence == DaggerfallNpcPresence.Removed),
            availableBillboards: availableBillboards);
    }

    IReadOnlyList<long> IDaggerfallSiteTransitionHost.BeforeSiteTransition()
    {
        HomeNightRoutine?.Reset();
        IReadOnlyList<long> boarded = OverlandParties.StagedPartyMemberIds.Count > 0 ? OverlandParties.BoardStagedPartyMembers() : [];
        _ = OverlandParties.ReleaseDepartingTrafficVisitors(_activeProfileKey, dispatchReturnBand: false);
        return boarded;
    }

    void IDaggerfallSiteTransitionHost.AfterSiteTransition(IReadOnlyList<long> boardedPartyIds)
    {
        if (State.PlayerControl.Position is not WorldPoint pos) return;
        DaggerfallOverlandPartyCoordinator.DaggerfallPartyTransitionStagingResult result =
            OverlandParties.StagePartyForSiteTransition(
                boardedPartyIds,
                _sites.Projection.Inputs,
                pos,
                State.PlayerControl.YawRadians,
                TryResolveRetainerCatchUpPose);
        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            Presentation.SetOutcome(result.StatusMessage);
    }

    void IDaggerfallSiteTransitionHost.AfterSameProfileRelocation()
    {
        if (State.PlayerControl.Position is not WorldPoint pos || OverlandParties.StagedPartyMemberIds.Count == 0)
            return;
        _ = OverlandParties.RelocateFollowingCompanions(
            pos,
            State.PlayerControl.YawRadians,
            PartyHire.ShouldFollowPlayer,
            TryResolveRetainerCatchUpPose);
    }

    private ActorPose? TryResolveRetainerCatchUpPose(long actorId, WorldPoint playerPos)
    {
        string scope = $"retainer-catchup:{_activeProfileKey.LogicalId}:{actorId}:{(int)MathF.Round(playerPos.X)}:{(int)MathF.Round(playerPos.Y)}:{(int)MathF.Round(playerPos.Z)}";
        if (_placement.TrySpawnPose("retainer-behind", scope, playerPos, 1.25f, 3.5f, out ActorPose behind, SpawnDirection.Behind))
            return behind;
        if (_placement.TrySpawnPose("retainer-any", scope, playerPos, 1.25f, 4.0f, out ActorPose any, SpawnDirection.Any))
            return any;
        HashSet<long> moving = [actorId];
        Vector3 forward = ActorHeading.Forward(State.PlayerControl.YawRadians);
        foreach (float offset in new[] { -1.75f, 1.75f })
        {
            if (_placement.StandingAt(playerPos.ToVector() + forward * offset, moving) is WorldPoint standing)
                return new ActorPose(standing, State.PlayerControl.YawRadians);
        }
        return null;
    }

    void IDaggerfallExteriorHost.GroundArrivedActors() => _grounding.GroundAwaiting(State.Actors);

    void IDaggerfallExteriorHost.AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection) =>
        Civilians.AdmitResidentAppearances(projection.Inputs, projection.Appearance);

    private readonly DaggerfallWeatherShelterClock _scheduleWeatherClock = new();
    private (DaggerfallSiteId SiteId, int MinuteOfDay, DaggerfallWeatherKind Weather, DaggerfallWeatherShelterElapsed Elapsed, int HolidayId, DaggerfallTownShelterAssignment Assignment)? _cachedShelterAssignment;

    internal void ResetScheduleWeatherBaseline()
    {
        _scheduleWeatherClock.Reset();
        _cachedShelterAssignment = null;
    }

    private DaggerfallSiteBuildingId? ResolveScheduledShelterBuildingForWalker(DaggerfallSiteId siteId, long actorId)
    {
        int minuteOfDay = checked(_time.Calendar.Hour * DaggerfallCalendar.MinutesPerHour + _time.Calendar.Minute);
        DaggerfallWeatherKind weather = LocalWeather.Current;
        DaggerfallWeatherShelterElapsed elapsed = ObserveScheduleWeatherElapsed();
        int holidayId = _time.Calendar.GetHolidayId(siteId.Region);

        DaggerfallTownShelterAssignment assignment;
        if (_cachedShelterAssignment is { } cached
            && cached.SiteId == siteId
            && cached.MinuteOfDay == minuteOfDay
            && cached.Weather == weather
            && cached.Elapsed == elapsed
            && cached.HolidayId == holidayId)
        {
            assignment = cached.Assignment;
        }
        else
        {
            assignment = ResolveTownShelterAssignment(siteId);
            _cachedShelterAssignment = (siteId, minuteOfDay, weather, elapsed, holidayId, assignment);
        }

        return assignment.AssignedBuildingByActor.TryGetValue(actorId, out DaggerfallSiteBuildingId building)
            ? building
            : null;
    }

    private DaggerfallSiteBuildingId? ResolveHomeResidenceForWalker(DaggerfallSiteId siteId, string? familyId, string slotId)
    {
        if (!_site.TryFind(siteId, out DaggerfallSiteRecord siteRecord)
            || siteRecord.Exterior is not { Buildings.Count: > 0 } exterior)
            return null;
        DaggerfallSiteBuildingSource[] residences =
        [
            .. exterior.Buildings.Values
                .Where(b => DaggerfallScheduleShelterResolver.IsResidenceBuildingType(b.Source.BuildingType))
                .OrderBy(b => b.Id.BlockY)
                .ThenBy(b => b.Id.BlockX)
                .ThenBy(b => b.Id.Index),
        ];
        if (residences.Length == 0)
            return null;
        return DaggerfallScheduleShelterResolver.SelectFamilyResidence(_worldSocialSeed, siteId, familyId, slotId, residences).Id;
    }

    internal DaggerfallWeatherShelterElapsed ObserveScheduleWeatherElapsed() =>
        _scheduleWeatherClock.Observe(LocalWeather.Current, _time.Calendar.MinuteNumber);

    internal IReadOnlyList<DaggerfallPopulationPlacement> ExteriorPlacementsFor(DaggerfallSiteId siteId)
    {
        DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(siteId);
        return _sites?.Profiles is { } profiles && profiles.TryGet(exteriorKey, out DaggerfallSiteProfile exteriorProfile)
            ? exteriorProfile.Population
            : [];
    }

    internal DaggerfallScheduleDecision EvaluateExteriorPlacementSchedule(
        DaggerfallSiteId siteId,
        DaggerfallPopulationPlacement placement,
        long actorId,
        int totalPopulationCount)
    {
        DaggerfallSiteRecord? siteRecord = _site.TryFind(siteId, out DaggerfallSiteRecord found) ? found : null;
        // A reserve guard no crackdown or conquest occupation calls out stays off the street.
        bool calledByOccupation = placement.CrackdownGuard > 0
            && siteRecord is not null
            && GuardPosts is not null
            && placement.CrackdownGuard <= GuardPosts.OccupationGarrisonCount(siteRecord);
        if (CrackdownGuards.HeldBack(siteId.Region, placement, siteRecord) && !calledByOccupation)
            return new DaggerfallScheduleDecision(DaggerfallSchedulePlacement.ShelteredInHome, DaggerfallScheduleBuildingTarget.None,
                DaggerfallNpcSchedulePolicy.CityGuardScheduleId, "held in reserve until a crackdown calls the watch out", 0, 0, false);
        int minuteOfDay = checked(_time.Calendar.Hour * DaggerfallCalendar.MinutesPerHour + _time.Calendar.Minute);
        DaggerfallWeatherKind weather = LocalWeather.Current;
        DaggerfallWeatherShelterElapsed weatherElapsed = ObserveScheduleWeatherElapsed();
        bool isWithParty = State.Quests.IsEscortWithParty(actorId)
            || (State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.Location?.Kind == ActorLocationKind.WithParty);
        bool isOnOverlandBand = State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? bandEntry)
            && bandEntry?.Location?.Kind == ActorLocationKind.OnOverlandBand;
        bool isActiveQuestor = State.Quests.QuestContacts(actorId).Count > 0 || State.Quests.IsNpcUnavailable(actorId);
        bool isOfficeHolder = OfficeLedger.IsExteriorOfficeHolder(actorId, siteRecord);

        return DaggerfallNpcSchedulePolicy.EvaluateExteriorPlacement(
            _worldSocialSeed,
            siteId,
            placement.Id,
            placement.IsGuard,
            totalPopulationCount,
            _definitions.ActorCatalog,
            minuteOfDay,
            weather,
            weatherElapsedMinutes: weatherElapsed.PrecipElapsedMinutes,
            isActiveQuestor: isActiveQuestor,
            isOfficeHolder: isOfficeHolder,
            isWithParty: isWithParty,
            isOnOverlandBand: isOnOverlandBand,
            severeWeatherElapsedMinutes: weatherElapsed.SevereElapsedMinutes);
    }

    bool IDaggerfallExteriorHost.IsPlacementScheduledOutdoors(
        DaggerfallSiteId siteId,
        DaggerfallPopulationPlacement placement,
        long actorId,
        int totalPopulationCount) =>
        EvaluateExteriorPlacementSchedule(siteId, placement, actorId, totalPopulationCount).Placement
            == DaggerfallSchedulePlacement.ExteriorStreet;

    // What rest asks of the world, each read from the domain that knows it when the rest asks.
    DaggerfallWorldProfileKey IDaggerfallTeleportSites.ActiveProfile => _activeProfileKey;
    DaggerfallWorldProfileKey? IDaggerfallTeleportSites.ReturnProfile => _sites.ReturnProfile;
    DaggerfallSiteReturnPose? IDaggerfallTeleportSites.ReturnPose => _site.ReturnPose;
    Vector3 IDaggerfallTeleportSites.ProfilePosition(WorldPoint local) => _exterior.ProfilePosition(local);
    bool IDaggerfallTeleportSites.TryRelocatePlayer(DaggerfallWorldProfileKey profile, DaggerfallSiteAnchor pose) =>
        _sites.TryRelocatePlayer(profile, pose);
    void IDaggerfallTeleportSites.RestoreReturnDestination(DaggerfallWorldProfileKey? profile, DaggerfallSiteReturnPose? pose) =>
        _sites.RestoreReturnDestination(profile, pose);

    bool IDaggerfallRestSurroundings.MustFeed => _vampirism.MustFeed;
    bool IDaggerfallRestSurroundings.EncounterPending => _encounters.HasPending(_activeProfileKey.LogicalId);
    bool IDaggerfallRestSurroundings.EnemyNearby => _senses.EnemyNearby();
    bool IDaggerfallRestSurroundings.QuestPromptPending => State.Quests.Messages.Pending is not null;
    bool IDaggerfallRestSurroundings.PlayerDefeated => State.Actors.Player.IsDefeated;
    bool IDaggerfallRestSurroundings.HasPosition => State.PlayerControl.Position is not null;
    DaggerfallWorldProfileKey IDaggerfallRestSurroundings.Profile => _activeProfileKey;
    DaggerfallSiteRecord? IDaggerfallRestSurroundings.Site =>
        _site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site) ? site : null;

    bool IDaggerfallRestSurroundings.InteriorPrivilege => _restPlaces.InteriorPrivilege(_activeProfileKey.Site,
        CurrentInteriorBuilding(), _time.Calendar.ToAbsoluteSeconds(), checked((int)_time.Calendar.DayNumber));
    long? IDaggerfallRestSurroundings.TavernRoomSeconds =>
        _restPlaces.TavernRoomSeconds(_activeProfileKey.Site, CurrentInteriorBuilding(), _time.Calendar.ToAbsoluteSeconds());
    int IDaggerfallRestSurroundings.ExteriorClimate => _exterior.CurrentClimate();
    bool IDaggerfallRestSurroundings.InSiteCell => _exterior.InSiteCell(((IDaggerfallRestSurroundings)this).Site);

    bool IDaggerfallRestSurroundings.EncounterArrives(long minute) => WorldEncounters.ArrivesWhileResting(minute);

    // What the expedition asks of the world it crosses, each read from the domain that knows it when it asks.
    DaggerfallCalendar IDaggerfallExpeditionSurroundings.Calendar => _time.Calendar;
    DaggerfallWeatherKind IDaggerfallExpeditionSurroundings.Weather(int climate) => LocalWeather.State.ForClimate(climate);
    DaggerfallTransportMode IDaggerfallExpeditionSurroundings.Transport =>
        State.Transport.HasHorse(State.Inventory.Read()) ? DaggerfallTransportMode.Horse
        : State.Transport.HasCart(State.Inventory.Read()) ? DaggerfallTransportMode.Cart : DaggerfallTransportMode.Foot;
    bool IDaggerfallExpeditionSurroundings.OwnsShip => State.Property?.OwnsShip ?? false;
    bool IDaggerfallExpeditionSurroundings.FreeShipPassage => GuildProviders.FreeShipPassage();
    bool IDaggerfallExpeditionSurroundings.TrySpendGold(int amount) =>
        amount == 0 || State.Currency.TrySpendCarried((ulong)amount, (ulong)amount);
    double IDaggerfallExpeditionSurroundings.FatigueShare => _condition.FatigueShare(State.Actors.Player.Stats);
    bool IDaggerfallExpeditionSurroundings.Exhausted => _condition.Exhausted(State.Actors.Player.Stats);
    bool IDaggerfallExpeditionSurroundings.Defeated => State.Actors.Player.IsDefeated;
    bool IDaggerfallExpeditionSurroundings.HeldElsewhere =>
        _mode is not (ProductMode.Playing or ProductMode.Modal)
        || Enum.GetValues<DaggerfallInteractionScreen>().Any(screen => screen != DaggerfallInteractionScreen.Expedition && _interactions.IsOpen(screen))
        || Summoning.PendingOffer || State.Quests.PendingOffer is not null || State.Quests.Messages.Pending is not null || State.Quests.Messages.AwaitsReading || Law.ModalOpen
        || DispelChoice.Pending is not null || IdentifyChoice.Open || CreateItemChoice.Pending is not null || Cinematics?.ActiveSource is not null;

    long IDaggerfallExpeditionSurroundings.Pass(DaggerfallExpeditionStretch stretch)
    {
        // Marching and swimming pay the per-minute fatigue any minute does (a march its factor of it, a swim the swimming
        // rate where its roll fails); a halt waits off its feet and a ship carries the party, so neither tires it; a camp
        // is a rest.
        _locomotion.RecordStretch(stretch.Activity == DaggerfallExpeditionActivity.Swimming ? stretch.Seconds : 0, stretch.FatigueFactor);   // Character
        return AdvanceElapsedTime(stretch.Seconds, deferSkillAdvancement: true,
            resting: stretch.Activity == DaggerfallExpeditionActivity.Camping,
            idleFatigue: stretch.Activity is DaggerfallExpeditionActivity.Marching or DaggerfallExpeditionActivity.Swimming,
            completeTimeIncrease: false).AppliedSeconds;                                          // Session: the elapsed interval's list
    }

    void IDaggerfallExpeditionSurroundings.RecoverCampHour(bool sheltered)
    {
        _rest.RecoverCampHour(State.Actors.Player.Stats, _passives.PlayerSpecials, sheltered);
        _ = OverlandParties.HealPartyMembersForRest(sheltered ? 0.15d : 0.10d);
    }

    void IDaggerfallExpeditionSurroundings.Collapse(bool inWater) => Collapse(inWater, enemiesNearby: false);

    int IDaggerfallExpeditionSurroundings.Roll(string key, int minimum, int maximum) => checked((int)_random.DrawKeyed(
        new KeyedRngRequest(0, "daggerfall.expedition.bands", key, minimum, maximum)).Value);
    int IDaggerfallExpeditionSurroundings.PlayerLevel => State.Actors.Player.Progression.Level;
    // No scouting skill exists yet: the party's sight takes none of it.
    double IDaggerfallExpeditionSurroundings.Scouting => 0;
    double IDaggerfallExpeditionSurroundings.Concealment =>
        Math.Clamp(State.Actors.Player.Stats.GetStat(StatId.Parse("stealth")).ValueInt / 100d, 0, 1);

    void IDaggerfallExpeditionSurroundings.MeetBand(DaggerfallExpeditionBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging)
    {
        // The band faces as it went on the map: north is -Z and east +X.
        double radians = band.Heading * Math.PI / 180;
        float yaw = ActorHeading.Yaw(new Vector3((float)Math.Sin(radians), 0F, (float)-Math.Cos(radians)));
        string profileId = DaggerfallWorldProfileIds.Exterior(Expedition.HostAt(pixel).Id).LogicalId;
        WorldEncounters.QueueBand(band.Request, band.Choice, new(band.Id, staging, yaw), band.Size, profileId);   // Actors
        if (band.IsFactionClash && band.RivalChoice is { } rivalChoice)
        {
            float rivalYaw = ActorHeading.Yaw(new Vector3((float)-Math.Sin(radians), 0F, (float)Math.Cos(radians)));
            WorldEncounters.QueueBand(band.Request, rivalChoice, new(checked(band.Id + 1_000_000), DaggerfallEncounterStaging.Travelling, rivalYaw),
                band.RivalSize, profileId);
        }
    }

    void IDaggerfallExpeditionSurroundings.MeetPersistentBand(DaggerfallPersistentOverlandBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging) =>
        OverlandParties.OnMeetPersistentBand(band, pixel, staging);

    void IDaggerfallExpeditionSurroundings.PersistentBandArrived(DaggerfallPersistentOverlandBand band, long second)
    {
        if (RationConvoys.OnBandArrived(band)) return;                                     // Society: a ration delivered
        if (CourierDispatches.OnOverlandBandArrived(band.BandId, second / DaggerfallCalendar.SecondsPerMinute,
                checked((int)(second / DaggerfallCalendar.SecondsPerDay))) is not null)
            return;
        _ = OverlandParties.OnPersistentBandArrived(band);
    }

    void IDaggerfallExpeditionSurroundings.PersistentBandRetiredOffScreen(string bandId, long second, DaggerfallWorldConditionToken? raidedUnder)
    {
        if (RationConvoys.OnBandLost(bandId, raidedUnder, second)) return;                       // Society: a ration missed, its news spreading
        _ = CourierDispatches.OnOverlandBandLost(bandId, second / DaggerfallCalendar.SecondsPerMinute,
            checked((int)(second / DaggerfallCalendar.SecondsPerDay)));
        OverlandParties.RetireBandOffScreen(bandId);
    }

    /// <summary>
    /// A ration convoy lost on the road is news from the town it never reached (#11180), naming the soldiers that took it
    /// when they were a seat's.
    /// </summary>
    private void RecordConvoyLost(DaggerfallRationConvoySave convoy, string? raider, long second)
    {
        if (PoliticalBoard is not { } board || !_site.TryFind(convoy.Destination.Require(), out DaggerfallSiteRecord town)) return;
        string giver = board.Board.Seat(convoy.Giver).Name, receiver = board.Board.Seat(convoy.Receiver).Name;
        string body = raider is null
            ? $"The wagons of grain {giver} sent to {receiver} never reached {town.Name}. They were taken on the road."
            : $"The wagons of grain {giver} sent to {receiver} never reached {town.Name}. Soldiers of {board.Board.Seat(raider).Name} took them on the road.";
        WordSpread.RecordNews($"convoy:{convoy.BandId}:lost", $"Grain Convoy Lost on the Road to {town.Name}", body, town,
            second / DaggerfallCalendar.SecondsPerMinute, _definitions.BoardSeats.TryGetBySeatId(convoy.Giver, out DaggerfallBoardSeatBinding? seat) ? seat?.OfficeFactionId : null);
    }

    /// <summary>A convoy fighter takes the player's side (#11181): a combatant on the player's team, no longer forced hostile.</summary>
    /// <summary>
    /// A convoy's guard takes up a road guard's arms and craft (#11181), as a hired road guard does (#11105), keeping the
    /// side it stands on: staged as townsfolk, it would otherwise watch the fight it was sent to guard.
    /// </summary>
    void IDaggerfallConvoyFight.Arm(long guardId)
    {
        if (!State.Actors.TryGet(guardId, out ActorState? guard) || guard.IsDefeated) return;
        FactionComponent faction = guard.Actor.Get<FactionComponent>();
        string? team = faction.TeamOverride;
        _roster.ApplyPartyHireSpec(guardId, DaggerfallPartyHireService.DefaultRoadGuardSpecId);
        faction.SetTeamOverride(team);
    }

    void IDaggerfallConvoyFight.Ally(long actorId)
    {
        if (!State.Actors.TryGet(actorId, out ActorState? actor) || actor.IsDefeated) return;
        actor.Actor.Get<ActorHarmPolicy>().Set(ActorHarmPolicyKind.Combatant);
        actor.Actor.Get<FactionComponent>().SetTeamOverride("player-ally");
        if (actor.Actor.TryGet<DaggerfallEnemyPerceptionMemory>(out DaggerfallEnemyPerceptionMemory? memory))
        {
            memory.SetForcedHostile(false);
            memory.LairTruce = false;
        }
    }

    /// <summary>A convoy fighter turns on the player (#11181): a combatant off the player's team, hostile and roused.</summary>
    void IDaggerfallConvoyFight.Foe(long actorId)
    {
        if (!State.Actors.TryGet(actorId, out ActorState? actor) || actor.IsDefeated) return;
        actor.Actor.Get<ActorHarmPolicy>().Set(ActorHarmPolicyKind.Combatant);
        if (actor.Actor.Get<FactionComponent>().TeamOverride == "player-ally") actor.Actor.Get<FactionComponent>().SetTeamOverride(null);
        _enemyBehavior.MakeHostile(actorId, includePlayerAllies: true);
        _enemyBehavior.Provoke(actorId, _time.Calendar.MinuteNumber);
    }

    /// <summary>
    /// The player raided a convoy (#11181): the theft of its grain is a crime in the region they are in, reported by its
    /// people, and costs standing with its giver and receiver, a loss the seats hostile to them welcome. A raid chosen on
    /// the travel map falls in the region of the party's pixel, not that of the scene the map was opened from.
    /// </summary>
    void IDaggerfallConvoyFight.Raided(DaggerfallRationConvoySave convoy)
    {
        int region = Expedition.Open && Expedition.Policy.RegionAt(Expedition.Party) is >= 0 and int mapRegion
            ? mapRegion
            : _site.ActiveSite?.Region ?? convoy.Origin.Require().Region;
        Crimes.ReportConvoyRaid($"convoy-raid:{convoy.BandId}", region, convoy.Riders);
        foreach (string seat in new[] { convoy.Giver, convoy.Receiver })
            if (_definitions.BoardSeats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) && binding is not null)
            {
                State.Social.ChangeFactionReputation(binding.OfficeFactionId, -_tuning.Politics.ConvoyRaidStanding, DaggerfallFactionReputationChange.Propagate);
                StandingRipple?.ObserveLoss(binding.OfficeFactionId, _tuning.Politics.ConvoyRaidStanding);
            }
    }

    /// <summary>The player saw a convoy through its raiders (#11181): standing with its giver and receiver, which their enemies resent.</summary>
    void IDaggerfallConvoyFight.Defended(DaggerfallRationConvoySave convoy)
    {
        foreach (string seat in new[] { convoy.Giver, convoy.Receiver })
            if (_definitions.BoardSeats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) && binding is not null)
                State.Social.ChangeFactionReputation(binding.OfficeFactionId, _tuning.Politics.ConvoyDefenceStanding, DaggerfallFactionReputationChange.Propagate);
    }

    /// <summary>The convoy-guard job paid the player (#11182): the gold, and word of it.</summary>
    private void PayConvoyGuard(int gold)
    {
        _ = State.Currency.ReceiveGold((ulong)gold);
        Presentation.SetOutcome($"The convoy is in. You are paid {gold} gold for guarding it.");
    }

    /// <summary>Changes the player's standing with board seat <paramref name="seat"/>'s office faction by <paramref name="delta"/>.</summary>
    private void ChangeSeatStanding(string seat, int delta)
    {
        if (_definitions.BoardSeats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) && binding is not null)
            State.Social.ChangeFactionReputation(binding.OfficeFactionId, delta, DaggerfallFactionReputationChange.Propagate);
    }

    /// <summary>
    /// The guard jobs <paramref name="npc"/> offers at <paramref name="site"/> (#11182): one for each convoy mustering in its
    /// town, not yet set out, while the player holds no other hire, when <paramref name="npc"/> is of its giver's court:
    /// the court factor of that town, or a questgiver of the giver's seat (as a political quest's giver is) anywhere in its
    /// region, since a crown's court keeps its castle apart from its town. A court that refuses the player work still
    /// lists the job, and says no when asked.
    /// </summary>
    internal IReadOnlyList<DaggerfallConvoyGuardOffer> ConvoyGuardOffers(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (site is null || ConvoyGuard.Hire is not null) return [];
        bool factor = OfficeLedger.ResolveLocalOffices(site).Any(office => office.Kind == DaggerfallOfficeKind.CourtFactor && office.HolderActorId == npc.DurableId);
        string? seat = npc.Services.Contains("quest") ? PoliticalBoard?.SeatOfFaction(npc.Appearance.FactionId) : null;
        return [.. RationConvoys.All
            .Where(convoy => convoy.Leg == 0
                && ((factor && convoy.Origin.Require() == site.Id) || (convoy.Giver == seat && convoy.Origin.Require().Region == site.Id.Region))
                && Expedition.PersistentBands.Any(band => StringComparer.Ordinal.Equals(band.BandId, convoy.BandId) && band is { RouteStep: 0, RoadSegment: 0 }))
            .Select(convoy => new DaggerfallConvoyGuardOffer(convoy.BandId,
                $"Guard the grain convoy to {PlaceName(convoy.Destination)} ({_tuning.Politics.ConvoyGuardPay} gold on arrival)"))];
    }

    /// <summary>The player asks <paramref name="npc"/> for the guard job on the convoy on band <paramref name="bandId"/> (#11182).</summary>
    internal DaggerfallConvoyGuardReply TakeConvoyGuard(DaggerfallNpc npc, string bandId, DaggerfallSiteRecord? site)
    {
        const string Question = "Does the grain convoy need another guard?";
        if (!ConvoyGuardOffers(npc, site).Any(offer => offer.BandId == bandId) || !RationConvoys.TryGet(bandId, out DaggerfallRationConvoySave? convoy) || convoy is null)
            return new(false, Question, "That convoy has already gone.");
        if (ConvoyGuard.Refuses(convoy.Giver))
            return new(false, Question, "The court remembers the last convoy you were paid to guard. There is no work here for you.");
        if (!ConvoyGuard.Take(convoy))
            return new(false, Question, "You are sworn to another convoy already.");
        return new(true, Question, $"Then ride with it to {PlaceName(convoy.Destination)}. It musters at {PlaceName(convoy.Origin)} and sets out soon; find it there on your travel map. "
            + $"You will have {_tuning.Politics.ConvoyGuardPay} gold when the grain is in, and {_tuning.Politics.ConvoyGuardBonus} more if you see off any raiders.");
    }

    private string PlaceName(DaggerfallSiteIdSave place) =>
        _definitions.Locations.Records.FirstOrDefault(site => site.Id == place.Require())?.Name ?? "its town";

    /// <summary>
    /// The convoy the player guards, met in an ambush and left, rides on from where the party takes the map again, its
    /// people all living (#11182); otherwise the guard has lost it.
    /// </summary>
    private void ResumeGuardedConvoy()
    {
        if (ConvoyGuard.Hire is not { } hire
            || Expedition.PersistentBands.Any(band => StringComparer.Ordinal.Equals(band.BandId, hire.BandId)))
            return;
        bool rode = RationConvoys.ResumeGuarded(hire.BandId, Expedition.Party,
            rider => State.ActorLedger.TryGet(rider, out ActorLedgerEntry? entry) && entry?.Location?.Kind == ActorLocationKind.Dead);
        Presentation.SetOutcome(rode ? "The convoy forms up again beside you." : "The convoy you were guarding is lost, and its pay with it.");
    }

    void IDaggerfallExpeditionSurroundings.PersistentBandDelayed(DaggerfallPersistentOverlandBand band, long seconds) =>
        CourierDispatches.OnOverlandBandDelayed(band.BandId, seconds / DaggerfallCalendar.SecondsPerMinute);

    private (string? Faction, string? Detail) DescribePersistentExpeditionBand(string bandId)
    {
        if (!bandId.StartsWith(DaggerfallCourierDispatches.DispatchBandPrefix, StringComparison.Ordinal))
            return default;
        DaggerfallCourierDispatchSave? dispatch = CourierDispatches.All.FirstOrDefault(d =>
            StringComparer.Ordinal.Equals(d.OverlandBandId, bandId));
        if (dispatch is null)
            return default;
        string? seatName = _definitions.BoardSeats.TryGetBySeatId(dispatch.SeatId, out DaggerfallBoardSeatBinding? seat) && seat is not null
            ? seat.SeatName
            : null;
        string? detail = dispatch.Revealed
            ? $"Revealed dispatch: {dispatch.Summary}"
            : null;
        return (seatName, detail);
    }

    IReadOnlyList<DaggerfallWorldConditionToken> IDaggerfallExpeditionSurroundings.WorldConditions(int region) =>
        WorldConditions.ActiveForRegion(region, checked((int)_time.Calendar.DayNumber));

    IReadOnlyList<DaggerfallWorldConditionToken> IDaggerfallExpeditionSurroundings.WorldConditionsOn(int region, int day) =>
        WorldConditions.ActiveForRegion(region, day);

    bool IDaggerfallExpeditionSurroundings.EncounterArrives(DaggerfallTravelMapPixel pixel, bool atLocation, long minute) =>
        WorldEncounters.Overland(minute, _definitions.Grids.ClimateAtWorldPixel(pixel.X, pixel.Y).Value, atLocation) is { } request
        && WorldEncounters.Queue(request, DaggerfallWorldProfileIds.Exterior(Expedition.HostAt(pixel).Id).LogicalId)
            .Choice.MobileId is not null;

    // What a ready infection asks before its permanent change; the change itself is the transformation's list.
    bool IDaggerfallTransformationConsumer.HasRacialOverride => State.RacialOverrides.Current is not null;
    DaggerfallInfectionConsumption IDaggerfallTransformationConsumer.Consume(DaggerfallInfectionTransition transition) =>
        transition.Kind == DaggerfallInfectionKind.Vampire ? BecomeVampire(transition) : _lycanthropy.Become(transition);

    /// <summary>Whether the player stands in sunlight (Magic's rule), from the calendar and the active profile.</summary>
    private bool InSunlight() => DaggerfallHeldEnchantments.InSunlight(_time.Calendar.IsDay, _activeProfileKey.Kind);

    /// <summary>
    /// Whether the player stands in a holy place. The donor requires an interior whose admitted
    /// building is a temple or belongs to the Fighters Guild trainers faction.
    /// </summary>
    private bool InHolyPlace() => DaggerfallConcreteGuildPolicy.HolyPlace(_activeProfileKey.Kind,
        _activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior
            ? CurrentInteriorBuilding() : null);

    /// <summary>The donor music context the admitted world answers, from each domain's reading of where the player stands.</summary>
    internal DaggerfallMusicContext MusicContext() => Music.Context(this);

    // What the music asks of where the player stands, each read from the domain that knows it when the music asks.
    DaggerfallWorldProfileKind IDaggerfallMusicPlace.Kind => _activeProfileKey.Kind;
    bool IDaggerfallMusicPlace.InsideCastle => InsideCastleZone();
    DaggerfallInteriorBuilding? IDaggerfallMusicPlace.Building => CurrentInteriorBuilding();
    DaggerfallSiteKind? IDaggerfallMusicPlace.Site => _site.ActiveSite?.Kind;
    bool IDaggerfallMusicPlace.IsDay => _time.Calendar.IsDay;
    DaggerfallWeatherKind IDaggerfallMusicPlace.Weather => LocalWeather.Current;

    /// <summary>Whether the player stands in a dungeon's castle block (Content's rule over the projection's zones).</summary>
    private bool InsideCastleZone() => DaggerfallAmbientZones.InsideCastle(_sites.Projection.Inputs.ProfileKind,
        _sites.Projection.Inputs.AmbientZones, State.PlayerControl.Position);

    /// <summary>The building the player is inside, as the site records it.</summary>
    private DaggerfallInteriorBuilding? CurrentInteriorBuilding() =>
        _site.InteriorBuilding(_activeProfileKey.Site, _sites.Projection.Inputs.InteriorBuilding);

    /// <summary>The building type ordinal for interior mood selection, or null when the active site has no building record.</summary>
    private int? TryCurrentInteriorBuildingType() =>
        _site.TryInteriorBuilding(_activeProfileKey.Site, _sites.Projection.Inputs.InteriorBuilding)?.BuildingType;

    /// <summary>Every actor definition by durable identity: authored placements and spawned actors alike.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> DefinitionsByActor => _roster.Definitions;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> DynamicActors => _roster.Dynamic;

    /// <summary>Registers one actor from a published definition beyond the authored placements.</summary>
    internal long SpawnActor(string definitionId, ActorPose pose, int? level = null, bool lairTruce = false, bool factionInfighting = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _roster.Spawn(definitionId, pose, level, lairTruce: lairTruce, factionInfighting: factionInfighting);
    }

    /// <summary>Retires one spawned actor through the roster's lifetime policy.</summary>
    internal void BanishActor(long durableId) => _roster.Banish(durableId);

    internal void RetireActor(long durableId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _roster.Retire(durableId);
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
            _sites.CaptureDeltas(), _sites.Deltas, _activeProfileKey, _sites.ReturnProfile, State.DungeonDiscoveries, State.DungeonActions,
            _sites.Projection.CaptureMotion(), _exterior.CaptureExteriorResidency(), _exterior.CaptureExteriorLocationResidency(),
            point => WorldPoint.From(_exterior.LocalToProfile(point.ToVector())));
    }

    /// <summary>
    /// The status rows owners outside this session publish: effects, escorts and quests put what
    /// they want shown here, and the projection carries it without knowing what it means.
    /// </summary>
    internal PresentationSlots Slots { get; } = new();

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

    /// <summary>The command radial's commands as the rules stand now.</summary>
    internal DaggerfallRadialCommands Radial { get; } = null!;

    /// <summary>The rest panel: the rest action and the last result.</summary>
    internal DaggerfallRestPanel RestPanel { get; } = null!;

    /// <summary>The local map panel: the map it shows, its building selection and notes.</summary>
    internal DaggerfallLocalMap LocalMap { get; } = null!;

    /// <summary>The service panels: bank and loans, property, merchant, tavern, rooms and training.</summary>
    internal DaggerfallEconomyPanels EconomyPanels { get; } = null!;

    /// <summary>The guild's spell sale (Economy).</summary>
    internal DaggerfallSpellSales SpellSales { get; private set; } = null!;
    /// <summary>The guild's spellmaker (Economy).</summary>
    internal DaggerfallSpellMaker SpellMaker { get; private set; } = null!;
    /// <summary>Daedric summoning: the summoner's quote, the prince's answer and the quest it offers (Quests).</summary>
    internal DaggerfallDaedricSummoning Summoning { get; private set; } = null!;

    /// <summary>The activation view's state: mode, outcome and the conversation's page.</summary>
    private readonly DaggerfallActivationPresentation _activationPresentation = new();

    /// <summary>The summoner's pane, the prince's answer and its cinematic.</summary>
    internal DaggerfallSummoningPanel SummoningPanel { get; } = null!;

    /// <summary>The spellbook frame and its actions.</summary>
    internal DaggerfallSpellbookPanel Spellbook { get; } = null!;

    /// <summary>Activation and the conversation as the HUD draws them, the mode action and the conversation's actions.</summary>
    internal DaggerfallActivationPanel ActivationPanel { get; } = null!;

    /// <summary>
    /// The loot window's actions; the quests' rewards and the corpse and ground activation owners, bound before it is built
    /// and again with each site, open their windows through it.
    /// </summary>
    private readonly DaggerfallLootActions _lootActions = null!;

    /// <summary>The HUD frame each publication sends, the active effects it lists, and the device the player last pressed on.</summary>
    internal DaggerfallHudFrames HudFrames { get; } = null!;

    /// <summary>The UI actions' admission and dispatch, the shortcut keys and the idle panel keys.</summary>
    private readonly DaggerfallUiDispatch _dispatch = null!;

    /// <summary>Whether this update's UI input asks to begin play from the entry screen.</summary>
    public bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input) => _dispatch.RequestsBegin(input);

    /// <summary>The Dead screen: its choices and the Host's defeat requests.</summary>
    private readonly DaggerfallDeathScreen _deathScreen;

    /// <summary>Lets the Host take one admitted replacement or named-slot request.</summary>
    public PlayerDefeatOutcomeRequest? TakePlayerDefeatOutcomeRequest() => _deathScreen.TakeOutcomeRequest();

    /// <summary>Publishes a Host outcome and reopens choices when the session remains defeated.</summary>
    public void ReportPlayerDefeatOutcome(string message)
    {
        _deathScreen.Reopen(message);
        PublishPresentation();
    }

    /// <summary>Presents the product's save/load outcome after it honored a request.</summary>
    public void ReportSaveOutcome(string message)
    {
        Presentation.SetOutcome(message);
        _deathScreen.ReopenAfterSaveOutcome(_mode == ProductMode.Dead);
        PublishPresentation();
    }

    /// <summary>The games the Host offers at the entry screen and the player's choice of one.</summary>
    private readonly DaggerfallBundleChoice _bundleChoice;

    /// <summary>Stores the games the Host offers at the entry screen for the next HUD projection.</summary>
    public void ReportBundles(GameBundleChoices choices)
    {
        _bundleChoice.Report(choices);
        PublishPresentation();
    }

    /// <summary>Takes the game the player chose and started at the entry screen, for the Host to switch to.</summary>
    public GameBundleRequest? TakeBundleRequest() => _bundleChoice.Take();

    /// <summary>Presents why the Host did not switch to the chosen game.</summary>
    public void ReportBundleOutcome(string message)
    {
        Presentation.SetOutcome(message);
        PublishPresentation();
    }

    /// <summary>Quit: whether the product can quit, when the game was last kept, and the player's request.</summary>
    private readonly DaggerfallQuit _quit;

    /// <summary>Whether the product can quit, for the next HUD projection.</summary>
    public void ReportQuitAvailable(bool available)
    {
        _quit.ReportAvailable(available);
        PublishPresentation();
    }

    /// <summary>The game as it stands is kept: a quit now loses no play.</summary>
    public void ReportKept()
    {
        _quit.Kept(_playSeconds);
        PublishPresentation();
    }

    /// <summary>Takes the player's quit for the Host to end the product.</summary>
    public bool TakeQuitRequest() => _quit.Take();

    /// <summary>The character choices' actions, which make the entry screen's starts.</summary>
    private readonly DaggerfallCharacterActions _characterActions = null!;

    /// <summary>
    /// Makes the start pressed under the game chosen at the entry screen, now that this session runs it: Begin as this
    /// session's own Begin makes it (a quick-entry game commits its quick-start character first), Create character by
    /// opening creation.
    /// </summary>
    public bool StartChosen(GameBundleStart start)
    {
        if (start == GameBundleStart.CreateCharacter)
        {
            OpenCharacterCreation();
            PublishPresentation();
            return false;
        }
        _characterActions.Begin();
        return true;
    }

    /// <summary>Stores Host-owned save metadata for the next ordinary HUD projection.</summary>
    public void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic)
    {
        _saveSlots = slots?.ToArray() ?? throw new ArgumentNullException(nameof(slots));
        _saveSlotDiagnostic = diagnostic;
        _deathScreen.SetLoadAvailable(_saveSlots.Count > 0);
        PublishPresentation();
    }

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
        // The hanging pieces' ragdolls hold the site appearance's instances, so they go before the projection.
        try { _hanging?.Dispose(); }                                                              // Scene
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { LooseItems.Dispose(); }                                                              // Items
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { DisposeAll([.. Cinematics is null ? Array.Empty<IDisposable>() : new IDisposable[] { Cinematics }, .. _atlasTiles is null ? Array.Empty<IDisposable>() : new IDisposable[] { _atlasTiles }, _hud, _overland, _camera, _spatial, State.Actors, _heldEnchantments, _sites.Projection, State.Effects, _sites.Interiors.ActionTriggers, Infections, _horizon, _weatherPresentation, _interiorLook]); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { _exterior.RetireExteriorAppearance(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        // The walkers' figures (rigs, gsplat characters and their clips) are released once no site appearance draws them.
        try { _people?.Dispose(); }                                                          // Developer
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { HostilityAudioBarks?.Dispose(); }                                               // Scene
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        // The score's loop and the clips it opened belong to this session, so they are retired before
        // the Engine context that produced them is asked for anything else.
        Music.Dispose(ref failure);
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

    private void AppendSpellTransfer(DaggerfallSpellTransferResult result) =>
        DaggerfallDamageFacts.AppendTransfer(_facts, result, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);

    private void AppendSpellTrackLoss(DaggerfallSpellTrackResult result) =>
        DaggerfallDamageFacts.AppendTrackLoss(_facts, result, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);

    private void AppendDamage(DamageResult result, DaggerfallDamageCause cause, int struckBody) =>
        DaggerfallDamageFacts.Append(_facts, result, cause, struckBody, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);

    /// <summary>The carried inventory as the HUD publishes it, eligibility included.</summary>
    internal InventoryPresentation ReadInventoryPresentation() => _inventoryUi.Read();

    /// <summary>The panel the player asked for through a device the DOM has no channel of its own for.</summary>
    internal DaggerfallPanelRequest? LatestPanelRequest => _interactions.LatestPanelRequest;

    private void RequestPanel(string panel)
    {
        if (_interactions.RequestPanel(panel, State.RacialOverrides.Current?.SuppressInventory == true) is { } refusal)
            Presentation.SetOutcome(refusal);
    }

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

    /// <summary>Where the player stands on the world map, a quest leg from there, a map's reveal and a landing at a location.</summary>
    internal DaggerfallMapPosition MapPosition { get; } = null!;

    /// <summary>The expedition map, the one way to travel overland: the party on the atlas, in running time.</summary>
    internal DaggerfallExpedition Expedition { get; }
    internal DaggerfallOverlandPartyCoordinator OverlandParties { get; } = null!;
    internal DaggerfallOverlandTraffic OverlandTraffic { get; } = null!;
    internal DaggerfallHostileOverlandBands HostileOverlandBands { get; } = null!;
    internal DaggerfallPartyHireService PartyHire { get; } = null!;

    /// <summary>The player's crimes: pickpocketing, witnesses, and what an attack on a civilian or a guard charges.</summary>
    internal DaggerfallCrimes Crimes { get; } = null!;

    /// <summary>The law: the legal response, the court, the sentence and the standing law.</summary>
    internal DaggerfallLaw Law { get; } = null!;

    /// <summary>Service denial policy for wanted and banished characters, with outlaw and ally exemptions.</summary>
    internal DaggerfallLegalServicePolicy LegalServices { get; } = null!;

    /// <summary>Faction reputation and witness memory consequences when the player kills a faction member.</summary>
    internal DaggerfallSocialConsequences SocialConsequences { get; } = null!;

    /// <summary>Household grudge and blood-feud settlement (exponential wergild, truce mediation, reconciliation service and pardons).</summary>
    internal DaggerfallFeudSettlement FeudSettlement { get; } = null!;
    /// <summary>Active blood-feud vendetta retaliation (kin confrontations, hired toughs/bounty hunters/assassins, lawful self-defense, and non-blocking hostility signals).</summary>
    internal DaggerfallFeudVendetta FeudVendetta { get; } = null!;
    /// <summary>Non-blocking near-actor comic speech bubbles projected into HUD camera space (Den #11164).</summary>
    internal DaggerfallSpeechBubbles SpeechBubbles { get; } = new();
    /// <summary>Non-blocking spatialized TTS voice-bark director for vendetta hostility signaling (Den #11164).</summary>
    internal DaggerfallHostilityAudioBarks HostilityAudioBarks { get; } = null!;
    /// <summary>Active non-blocking hostility signaling mode (Both, SpeechBubbles, AudioBarks, or Off).</summary>
    internal DaggerfallHostilitySignalingMode HostilitySignalingMode { get; set; } = DaggerfallHostilitySignalingMode.Both;
    private double _vendettaCheckAccumulator;
    private DaggerfallWorldProfileKey? _lastVendettaProfile;

    internal static string HostileSignalKindSlug(DaggerfallHostileSignalKind kind) => kind switch
    {
        DaggerfallHostileSignalKind.HiredTough => "hired-tough",
        DaggerfallHostileSignalKind.BountyHunter => "bounty-hunter",
        DaggerfallHostileSignalKind.DarkBrotherhoodAssassin => "assassin",
        _ => "kin-vendetta",
    };

    /// <summary>Consumes a non-blocking hostility signal and dispatches TTS voice bark and/or near-actor speech bubble without pausing simulation.</summary>
    private bool SyncVendettaSceneProfile(DaggerfallWorldProfileKey currentProfile)
    {
        if (_lastVendettaProfile == currentProfile)
            return false;
        _lastVendettaProfile = currentProfile;
        _vendettaCheckAccumulator = 0d;
        FeudVendetta.ResetSceneSignals();
        SpeechBubbles.Clear();
        HostilityAudioBarks?.ClearActiveVoices();
        return true;
    }

    internal void HandleHostilitySignal(DaggerfallHostileReasonSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _lastVendettaProfile ??= _activeProfileKey;
        string kindSlug = HostileSignalKindSlug(signal.Kind);
        WorldPoint? position = signal.Position
            ?? (State.Actors.TryGet(signal.ActorId, out ActorState? actor) ? actor.Position : null);

        if (HostilitySignalingMode is DaggerfallHostilitySignalingMode.Both or DaggerfallHostilitySignalingMode.SpeechBubbles)
            SpeechBubbles.Show(signal.ActorId, signal.SpeakerName, kindSlug, signal.ReasonLine, position);

        if (HostilitySignalingMode is DaggerfallHostilitySignalingMode.Both or DaggerfallHostilitySignalingMode.AudioBarks)
            _ = HostilityAudioBarks?.TryPlay(signal.ActorId, kindSlug, signal.Gender, position);
    }

    private DebugCommandResult HandleHostilitySignalCommand(string action)
    {
        string normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "":
            case "status":
                break;
            case "both":
            case "hybrid":
                HostilitySignalingMode = DaggerfallHostilitySignalingMode.Both;
                break;
            case "bubbles":
            case "bubble":
                HostilitySignalingMode = DaggerfallHostilitySignalingMode.SpeechBubbles;
                HostilityAudioBarks?.ClearActiveVoices();
                break;
            case "audio":
            case "tts":
            case "barks":
                HostilitySignalingMode = DaggerfallHostilitySignalingMode.AudioBarks;
                SpeechBubbles.Clear();
                if (!_inAdmittedUpdate && !_disposed)
                    PublishPresentation();
                break;
            case "off":
            case "none":
                HostilitySignalingMode = DaggerfallHostilitySignalingMode.Off;
                SpeechBubbles.Clear();
                HostilityAudioBarks?.ClearActiveVoices();
                if (!_inAdmittedUpdate && !_disposed)
                    PublishPresentation();
                break;
            case "demo":
            case "demo-kin":
            case "demo-tough":
            case "demo-hunter":
            case "demo-assassin":
            {
                DaggerfallHostileSignalKind kind = normalized switch
                {
                    "demo-tough" => DaggerfallHostileSignalKind.HiredTough,
                    "demo-hunter" => DaggerfallHostileSignalKind.BountyHunter,
                    "demo-assassin" => DaggerfallHostileSignalKind.DarkBrotherhoodAssassin,
                    _ => DaggerfallHostileSignalKind.KinVendettaConfrontation,
                };
                WorldPoint playerPos = State.PlayerControl.Position ?? new WorldPoint(0f, 0f, 0f);
                Vector3 forward = ActorHeading.Forward(State.PlayerControl.YawRadians);
                WorldPoint ahead = WorldPoint.From(playerPos.ToVector() + (forward * 4.5f));
                ActorState? nearest = State.Actors.All
                    .Where(a => a.DurableId != DaggerfallActorIdentity.PlayerEntityId
                        && !a.IsDefeated
                        && Vector3.Distance(a.Position.ToVector(), playerPos.ToVector()) <= 18f)
                    .OrderBy(a => Vector3.Distance(a.Position.ToVector(), playerPos.ToVector()))
                    .FirstOrDefault();
                long actorId = nearest?.DurableId ?? 990_001L;
                DaggerfallNpc? npc = nearest is not null && State.Npcs.TryGet(nearest.DurableId, out DaggerfallNpc? reg) ? reg : null;
                string speakerName = kind switch
                {
                    DaggerfallHostileSignalKind.HiredTough => "Hired Tough",
                    DaggerfallHostileSignalKind.BountyHunter => "Bounty Hunter",
                    DaggerfallHostileSignalKind.DarkBrotherhoodAssassin => "Dark Brotherhood Assassin",
                    _ => npc?.DisplayName ?? "Vengeful Kinsman",
                };
                string gender = npc?.Appearance.Gender ?? "Male";
                string line = kind switch
                {
                    DaggerfallHostileSignalKind.HiredTough => "Nothing personal — House Hawkwing paid good coin to break your legs!",
                    DaggerfallHostileSignalKind.BountyHunter => "There's a blood-feud writ from House Hawkwing on your head — I'm collecting!",
                    DaggerfallHostileSignalKind.DarkBrotherhoodAssassin => "A blood feud bought your death. The Brotherhood collects.",
                    _ => "You spilled our blood! House Hawkwing will have its due!",
                };
                WorldPoint signalPos = nearest is not null ? nearest.Position : ahead;
                DaggerfallHostileReasonSignal demoSignal = new(
                    actorId, speakerName, gender, "fam:demo:hawkwing", "House Hawkwing", kind, line, signalPos);
                HandleHostilitySignal(demoSignal);
                Presentation?.SetOutcome($"{speakerName}: \"{line}\"");
                if (!_inAdmittedUpdate && !_disposed)
                    PublishPresentation();
                break;
            }
            default:
                return DaggerfallDeveloperReplies.Refused(
                    $"Unknown hostility-signal action '{action}'. Use: status, both, bubbles, audio, off, demo, demo-kin, demo-tough, demo-hunter, or demo-assassin.");
        }

        IReadOnlyList<DaggerfallSpeechBubbleView> projected = SpeechBubbles.Read(State.PlayerControl, _tuning.Camera, State.Actors);
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("mode", HostilitySignalingMode.ToString());
            w.WriteNumber("catalogClips", _definitions.People.HostilityBarks.Clips.Count);
            w.WriteNumber("activeBubbles", SpeechBubbles.ActiveCount);
            w.WriteNumber("projectedBubbles", projected.Count);
            w.WriteNumber("audioPlayedCount", HostilityAudioBarks?.PlayedCount ?? 0);
            w.WriteNumber("audioThrottledCount", HostilityAudioBarks?.ThrottledCount ?? 0);
            w.WriteNumber("audioFailedOpenCount", HostilityAudioBarks?.FailedOpenCount ?? 0);
            w.WriteNumber("activeVoices", HostilityAudioBarks?.ActiveVoiceCount ?? 0);
            if (HostilityAudioBarks?.LastPlayedClipId is { } clipId) w.WriteString("lastClipId", clipId);
            else w.WriteNull("lastClipId");
            w.WriteStartArray("bubbles");
            foreach (DaggerfallSpeechBubbleView b in projected)
            {
                w.WriteStartObject();
                w.WriteNumber("actorId", b.ActorId);
                w.WriteString("speakerName", b.SpeakerName);
                w.WriteString("kind", b.Kind);
                w.WriteString("text", b.Text);
                w.WriteNumber("screenX", b.ScreenX);
                w.WriteNumber("screenY", b.ScreenY);
                w.WriteNumber("opacity", b.Opacity);
                w.WriteString("offscreen", b.Offscreen);
                w.WriteNumber("distanceMeters", b.DistanceMeters);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }
    internal DaggerfallShopContinuity ShopContinuity { get; } = null!;
    internal DaggerfallSocialJournalProjection SocialJournal { get; } = null!;

    /// <summary>The guilds' providers: what a guild's NPC answers to its topics, its welcome and its free passage.</summary>
    internal DaggerfallGuildProviders GuildProviders { get; } = null!;

    /// <summary>Who offers quest work, the offers and their answers, and the criminal guilds' invitations.</summary>
    internal DaggerfallQuestWork QuestWork { get; } = null!;

    /// <summary>What a quest does to the world: variants, revealed places and the quest teleport.</summary>
    private readonly DaggerfallQuestWorldActions _questWorldActions = null!;

    /// <summary>The city guards a quest calls.</summary>
    private readonly DaggerfallQuestGuards _questGuards = null!;

    /// <summary>Where a quest's items physically go: placed again, given, or set down as the reward.</summary>
    private readonly DaggerfallQuestItemPlacement _questItemPlacement = null!;

    /// <summary>Where a quest's resources stand once their site is loaded.</summary>
    internal DaggerfallQuestPlacement QuestPlacement { get; } = null!;
    internal DaggerfallOfficeHolders OfficeHolders { get; } = null!;

    /// <summary>Sparse office ledger connecting local seeded offices and board-game seats to live holders and overrides.</summary>
    internal DaggerfallOfficeLedger OfficeLedger { get; } = null!;

    /// <summary>Sparse world-condition seam (authored/stubbed tokens now; B2 weekly board game later).</summary>
    internal DaggerfallWorldConditions WorldConditions { get; } = null!;

    /// <summary>The extra guards a crime crackdown calls out in a region's towns.</summary>
    internal DaggerfallCrackdownGuards CrackdownGuards { get; } = null!;
    /// <summary>Post-differentiated watch officers and live liege-livery resolution (palaces, keeps, offices, walled-city gates, and occupation garrisons).</summary>
    internal DaggerfallGuardPosts GuardPosts { get; } = null!;

    /// <summary>Elapsed-time road-distance news and standing spread (instant within bubble, courier-gated outside).</summary>
    internal DaggerfallWordSpread WordSpread { get; } = null!;

    /// <summary>Sealed board-game courier dispatches that the player can deliver, buy, or intercept.</summary>
    internal DaggerfallCourierDispatches CourierDispatches { get; } = null!;

    /// <summary>The board's weekly rations on the road as convoys (#11180).</summary>
    internal DaggerfallRationConvoys RationConvoys { get; } = null!;
    internal DaggerfallConvoyGuardJob ConvoyGuard { get; } = null!;
    internal DaggerfallConditionCounterplay ConditionCounterplay { get; } = null!;

    /// <summary>Whom each board region's chief-town offices answer to, and how that takes the player; null without a board.</summary>
    internal DaggerfallOfficeAllegiance? OfficeAllegiance { get; }

    /// <summary>How the board's quarrels follow the player's standing; null without a board.</summary>
    /// <summary>The arms each region shows, its liege's or its own (#11144).</summary>
    internal DaggerfallRealmArms RealmArms { get; } = null!;

    internal DaggerfallBoardStandingRipple? StandingRipple { get; }
    /// <summary>The political quests a seat's questgivers offer from the live board (#11121); null without a board.</summary>
    internal DaggerfallPoliticalQuests? PoliticalQuests { get; }

    /// <summary>The notice-board bounties the player holds, and who pays them.</summary>
    internal DaggerfallNoticeBounties NoticeBounties { get; } = null!;

    /// <summary>The political board, playing a turn every Sunday; null when the base pack carries no board.</summary>
    internal DaggerfallPoliticalBoard? PoliticalBoard { get; }

    /// <summary>Gate, watch, service, and scarcity scrutiny policy driven by standing and active world conditions.</summary>
    internal DaggerfallScrutinyPolicy Scrutiny { get; } = null!;

    /// <summary>Town public notice board assembling classic sign rumours, bounties, office notices, and condition decrees.</summary>
    internal DaggerfallNoticeBoard NoticeBoard { get; } = null!;
    /// <summary>The street dressing each region's towns put out for its conditions (#11169).</summary>
    internal DaggerfallTownDressing TownDressing { get; } = null!;

    /// <summary>Transient UI panel for physical in-world town/tavern notice board reading and lead pickup.</summary>
    internal DaggerfallNoticeBoardPanel NoticeBoardPanel { get; } = null!;

    /// <summary>Reads the active town's notice board (null when no site is active).</summary>
    internal DaggerfallTownNoticeBoardView? ReadTownNoticeBoard() =>
        _site.ActiveSite is { } active ? NoticeBoard.BuildForSite(active, _time.Calendar.MinuteNumber) : null;

    /// <summary>Staged two-sided creature-faction clashes (downstream of board-game moves or explicit faction pairs).</summary>
    internal DaggerfallCreatureFactionClashes CreatureClashes { get; } = null!;

    /// <summary>Social introductions and vouching: prominence gating, social-skill skips, structured vouches, and better terms.</summary>
    internal DaggerfallIntroductions Introductions { get; } = null!;

    /// <summary>A quest's foes as actors: their team and target, the commands over them, and spawning.</summary>
    internal DaggerfallQuestFoes QuestFoes { get; } = null!;

    /// <summary>The greeting a building gives as the player walks in, holding the entry until it is read.</summary>
    private readonly DaggerfallBuildingGreeting _buildingGreeting;

    /// <summary>The dungeon text or greeting that is up, the texts it covers and a riddle's waiting answer.</summary>
    internal DaggerfallDungeonText DungeonText { get; }

    /// <summary>What the text macros name now, read from every domain that answers one.</summary>
    internal DaggerfallTextContext CurrentTextContext() => DaggerfallTextContext.Empty with
    {
        Player = DaggerfallPlayerTextContext.For(_definitions, State.Character.Identity, State.Actors.Player.Stats,
            State.Currency.Read().Gold, State.Character.Career),
        Calendar = DaggerfallCalendarTextContext.For(_definitions, _time.Calendar),
        Location = new(City: _site.ActiveSite?.Name,
            Region: _site.Region is int region ? _definitions.BuildingNames.RegionName(region) : null),
        Faction = new(VampireClan: State.RacialOverrides.Current?.State.Vampire is { } vampire
            ? DaggerfallVampirismPolicy.ClanName(_definitions, vampire.Clan) : null,
            Liege: _site.Region is int here ? State.Social.Liege.SovereignOf(here)?.Name : null),
    };

    /// <summary>The conversation with the person the player talks to; activation rebuilds it with each site.</summary>
    private DaggerfallDialogueService? _dialogue;
    internal DaggerfallDialogueService Dialogue => _dialogue ?? throw new InvalidOperationException("The session has no dialogue owner.");

    private DaggerfallTutorial _tutorial = null!;

    /// <summary>What each quest's messages name.</summary>
    private DaggerfallQuestTextContext _questTexts = null!;

    /// <summary>The tutorial Help offers, and the page of it that is up.</summary>
    internal DaggerfallTutorial Tutorial => _tutorial;

    /// <summary>The "Where is" directory and what a direction's answer discloses.</summary>
    internal DaggerfallDialogueDirections DialogueDirections { get; } = null!;

    /// <summary>The bank counter: the open bank, its region, loans and their daily reminders and defaults.</summary>
    internal DaggerfallBanking Banking { get; } = null!;

    /// <summary>Trading with the merchant the player is talking to.</summary>
    internal DaggerfallMerchantTrade MerchantTrade { get; } = null!;
    internal DaggerfallRetainerExchange RetainerExchange { get; } = null!;

    /// <summary>Renting a room at the tavern the player stands in.</summary>
    internal DaggerfallRoomRental RoomRental { get; } = null!;

    /// <summary>The player's houses and ship: ownership, the offers, trading, entering, storage, boarding and the knightly house.</summary>
    internal DaggerfallPropertyHoldings Holdings { get; } = null!;

    /// <summary>The goods of an interior's property containers: their stock and the theft of them.</summary>
    internal DaggerfallPropertyLoot PropertyLoot { get; } = null!;

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
    internal IReadOnlyDictionary<long, CorpseContainer> Corpses => _corpseLoot.Corpses;

    /// <summary>The session's durable identity ledger for generated unique loot.</summary>
    internal DaggerfallUniqueItemAllocator UniqueItemAllocator => _uniqueItems;

    /// <summary>The name of the item a player effect comes from, for the active-effect status row.</summary>
    internal string EffectItemName(ulong item) => DaggerfallItemNames.EffectItemName(_definitions, State.ItemInstances, item);
}
