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
internal sealed partial class DaggerfallSession : ISaveableGameSession, IModeAwareGameSession, IEntryScreenSession, IEntryScreenStartupSession, ISaveRequestingGameSession, IPlayerPreferencesSession, IPlayerDefeatOutcomeSession
{

    /// <summary>Admitted world seconds a panel request stands before the DOM is assumed not to need it.</summary>
    private const double PanelRequestLifetimeSeconds = 1d;
    private readonly IRandomService _random;
    private readonly IEngineContext _engine;
    private readonly DaggerfallTuning _tuning;
    private readonly DaggerfallSiteAudioBundles? _siteAudioBundles;
    private PlayerInputSystem _input;
    private ProductMode _mode = ProductMode.Playing;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerCombatRules _combat;
    // The player's weapon swing gesture, read from the look turns each admitted update commits and
    // consumed by the weapon attack it admits.
    private readonly DaggerfallSwingTracker _playerSwings;
    private readonly DaggerfallVitalityConsequences _vitality;
    // The poisons the session's actors carry, and the ordinal its draws advance on so a replay inside a
    // session draws the same values.
    private readonly DaggerfallPoisonRuntime _poisons;
    private long _poisonDraws;
    private readonly DaggerfallStaminaRecoveryModule _staminaRecovery;
    private readonly CombatResolution _combatResolution;
    private readonly DaggerfallLocomotionPolicy _locomotion;
    private readonly DaggerfallClimbingPolicy _climbing;
    private readonly DaggerfallLevitationPolicy _levitation = new();
    private readonly DaggerfallDungeonVisibility _dungeonVisibility;
    private bool _verticalMovementDriven;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallBookNotebook _notebook;
    private readonly DaggerfallEncounterRuntime _encounters;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private DaggerSessionPersistence _persistence = null!;
    private readonly HashSet<ulong> _authoredEntityIds;
    /// <summary>
    /// Dynamic actors share one numeric base with generated unique items; durable kinds keep
    /// them distinct, the way a corpse container already shares its actor's number under a
    /// different kind.
    /// </summary>
    internal const ulong DynamicActorFirstIdentity = 1_000_000_000_000UL;
    internal const ulong GroundContainerFirstIdentity = 2_000_000_000_000UL;
    private readonly DurableIdentityAllocator _actorIdentities;
    private readonly Dictionary<long, DaggerfallActorDefinition> _definitionsByActor;
    private DaggerfallHeldEnchantments _heldEnchantments = null!;
    private readonly Dictionary<long, DaggerfallActorId> _dynamicActors = [];
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallMechanicsState _mechanics;
    private readonly ISpatialService _spatialService;
    private readonly float _spawnGroundProbeLift;
    private readonly double _spawnGroundProbeDistance;
    private readonly FactBuffer<IProductFact> _facts = new();
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly DaggerfallEquipmentMoves _equipmentMoves;
    private readonly DaggerfallItemConditionService _itemCondition;
    private readonly DaggerfallInventoryPresentation _inventoryUi;
    private readonly DaggerfallLootPresentation _lootUi;
    private readonly DaggerfallCharacterPresentation _characterUi;
    private DaggerfallSiteProjection _siteProjection = null!;
    private DaggerfallSiteProfiles? _siteProfiles;
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> _siteDeltas = [];
    private DaggerfallWorldProfileKey _activeProfileKey;
    private DaggerfallWorldProfileKey? _returnProfileKey;
    private PrivateersHoldAppearance _appearance => _siteProjection.Appearance;
    private DaggerfallDoorRuntime _doors => _siteProjection.Doors;

    private readonly World.DaggerfallWorldTime _time;
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

    private ulong? _latestUpdateGeneration;
    private ulong? _latestSimulationStep;
    private string? _panelRequest;
    private ulong _panelRequestRevision;
    private double _panelRequestRemainingSeconds;
    private int _lastHolidayId;
    private bool _disposed;

    /// <summary>
    /// The holiday announcement the calendar currently names for the session's site, or null when
    /// the site is no settlement or no holiday is kept there today.
    /// </summary>
    internal World.DaggerfallHolidayAnnouncement? HolidayAnnouncement { get; private set; }
    internal DaggerfallCinematicPresentation? Cinematics { get; }
    private readonly DaggerfallOpeningCinematics _openingCinematics;

    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning)
        : this(engine, definitions, inputs, tuning, null, null, null) { }

    /// <summary>Explicit compiled effect composition seam for ruleset families and save reconstruction tests.</summary>
    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs,
        DaggerfallTuning tuning, DaggerfallEffectCatalog effects)
        : this(engine, definitions, inputs, tuning, null, null, effects) { }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, DaggerfallMusicBundle? music = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, null, null, null, null, true, null, null, null, music) { }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, DaggerfallSiteAudioBundles audioBundles, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, null, null, audioBundles, cinematicContent, videosEnabled, questAdmission, null, disabledQuestSelection, music) { }

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, RulesetSavePayload saved, IRandomService random)
        => Restore(engine, compositionIdentity, definitions, inputs, tuning, saved, random, (DaggerfallEffectCatalog?)null);

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, RulesetSavePayload saved,
        IRandomService random, DaggerfallSiteAudioBundles audioBundles, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallSiteProfiles? profiles = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
        => Restore(engine, compositionIdentity, definitions, inputs, tuning, saved, random, null, audioBundles, cinematicContent, videosEnabled, questAdmission, profiles, disabledQuestSelection, music);

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, RulesetSavePayload saved,
        IRandomService random, DaggerfallEffectCatalog? effects, DaggerfallSiteAudioBundles? audioBundles = null, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallSiteProfiles? profiles = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
    {
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        PrivateersHoldInputs activeInputs = profiles is null
            ? inputs
            : raw.Site.ActiveProfile is { } profile
                ? profiles.Require(profile.Require())
                : profiles.RequireUniqueSite(ToSiteId(raw.Site.Active) ?? throw new ArgumentException("A restored Daggerfall session must name an active site.", nameof(saved)));
        DaggerfallSavePayload payload = raw.ResolveRestore(definitions, activeInputs, profiles);
        return new DaggerfallSession(engine, definitions, activeInputs, tuning, compositionIdentity, payload, effects, audioBundles, cinematicContent, videosEnabled, questAdmission, profiles, disabledQuestSelection, music);
    }

    private DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs,
        DaggerfallTuning tuning, ResolvedCompositionIdentity? compositionIdentity, DaggerfallSavePayload? saved,
        DaggerfallEffectCatalog? effects, DaggerfallSiteAudioBundles? audioBundles = null, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallSiteProfiles? profiles = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
    {
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _engine = engine;
            _tuning = tuning;
            _siteAudioBundles = audioBundles;
            // The score's clips are named by the site's cue list and carried by the product-wide music
            // bundle, so the resolver the director asks is this session's own: it keeps the Engine
            // resource alive for as long as the session plays that track and releases it on disposal.
            _musicBundle = music;
            if (music is not null) _music = new DaggerfallMusicDirector(engine.Audio, ResolveMusicClip, ReportMusicRetired);
            _random = engine.Random;
            tuning = tuning.Validate();
            _definitions = definitions;
            _dungeonText = new DaggerfallDungeonTextActions(new DaggerfallTextResolver(definitions.Text));
            _encounters = new DaggerfallEncounterRuntime(definitions, _random);
            Cinematics = cinematicContent is null ? null : new DaggerfallCinematicPresentation(engine, cinematicContent, definitions.Cinematics);
            if (Cinematics is not null) partiallyConstructed.Add(Cinematics);
            _openingCinematics = new DaggerfallOpeningCinematics(Cinematics, videosEnabled);
            _spatialService = engine.Spatial;
            _spawnGroundProbeLift = tuning.EnemyBehavior.SpawnGroundProbeLift;
            _spawnGroundProbeDistance = tuning.EnemyBehavior.SpawnGroundProbeDistance;
            DaggerActorAssembly assembled = DaggerActorFactory.Create(_random, definitions, inputs, saved, questAdmission, disabledQuestSelection);
            State = assembled.State;
            State.Social.SetBiographyReactionModifier(State.Character.Background?.Modifiers.Reaction ?? 0);
            ActorsState actors = State.Actors;
            partiallyConstructed.Add(actors);
            _definitionsByActor = assembled.Definitions;
            _mechanics = assembled.Mechanics;
            Dictionary<long, DaggerfallActorDefinition> authored = _definitionsByActor;
            DaggerfallActorDefinition playerDefinition = assembled.PlayerDefinition;
            EntityId playerEntity = actors.Player.Actor.Entity;
            MechanicsInventoryCoordinator inventory = State.Inventory;
            MechanicsEquipmentCoordinator equipmentCoordinator = State.Equipment;
            MechanicsInventoryContainerCoordinator containers = State.Containers;
            Presentation = new PresentationState("Ready");
            _time = new DaggerfallWorldTime(
                saved?.Calendar is { } restored
                    ? new DaggerfallCalendar(restored.Year, restored.Month, restored.Day, restored.Hour, restored.Minute, restored.Second)
                    : DaggerfallCalendar.Start,
                saved?.Calendar?.RemainderSeconds ?? 0d,
                tuning.Time.GameSecondsPerRealSecond);
            // New games use the bundle site; current saves carry their explicit site state.
            _site = saved?.Site is { } restoredSite
                ? new World.DaggerfallSiteContext(
                    definitions.Locations,
                    ToSiteId(restoredSite.Active),
                    ToSiteId(restoredSite.ReturnAnchor),
                    ToSiteReturnPose(restoredSite.ReturnPose),
                    restoredSite.Discovered.Select(id => id.Require()))
                : new World.DaggerfallSiteContext(definitions.Locations, inputs.Site, null, []);
            _travelPolicy = new DaggerfallTravelPolicy(_site, definitions.Grids);
            _siteProfiles = profiles;
            _activeProfileKey = saved?.Site.ActiveProfile?.Require() ?? inputs.ProfileKey;
            _returnProfileKey = saved?.Site.ReturnProfile?.Require();
            _controlEngine = engine;
            _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            _locomotion = new DaggerfallLocomotionPolicy(tuning.Locomotion, _controlSettings);
            _climbing = new DaggerfallClimbingPolicy(tuning.Climbing);
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            _dungeonVisibility = new DaggerfallDungeonVisibility(_spatial, tuning.Spatial.CollisionVoxelSize);
            partiallyConstructed.Add(_spatial);
            foreach ((DaggerfallWorldProfileKey key, PrivateersHoldInputs admitted) in ActionProfiles(inputs, profiles))
            {
                DaggerfallDungeonActionGraphSnapshot? snapshot = saved?.DungeonActions
                    .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ProfileId, key.LogicalId));
                State.DungeonActions.Add(key, new DaggerfallDungeonActionGraph(
                    key.LogicalId,
                    admitted.DungeonActions,
                    State.Variables,
                    snapshot,
                    ExecuteDungeonFamilyAction));
            }
            foreach (DaggerfallDungeonDiscoverySnapshot snapshot in saved?.DungeonDiscovery ?? [])
            {
                PrivateersHoldInputs admitted = snapshot.Profile == inputs.ProfileKey
                    ? inputs
                    : (profiles ?? throw new ArgumentException("Saved dungeon discovery requires admitted site profiles.", nameof(saved))).Require(snapshot.Profile);
                DaggerfallDungeonMapContent map = admitted.DungeonMap
                    ?? throw new ArgumentException($"Saved dungeon discovery names non-dungeon profile '{snapshot.Profile.LogicalId}'.", nameof(saved));
                State.DungeonDiscoveries.Add(snapshot.Profile, new DaggerfallDungeonDiscovery(snapshot.Profile, map, snapshot));
            }
            if (inputs.DungeonMap is { } initialMap && !State.DungeonDiscoveries.ContainsKey(_activeProfileKey))
                State.DungeonDiscoveries.Add(_activeProfileKey, new DaggerfallDungeonDiscovery(_activeProfileKey, initialMap));
            // The selected site's normalized RDB doors restore their Engine pose/collider projection
            // before activation can query them and before the first character step consumes them.
            _siteProjection = DaggerfallSiteProjection.Create(engine, State.Actors.Entities, _random, tuning, _time.Calendar,
                inputs, AudioFor(inputs), _spatial, saved?.Doors, saved?.DungeonMotion);
            partiallyConstructed.Add(_siteProjection);
            _actionTriggers = new DaggerfallDungeonActionTriggerRuntime(
                State.Actors.Entities, engine.Spatial, _spatial, ActionProfiles(inputs, profiles), _activeProfileKey);
            partiallyConstructed.Add(_actionTriggers);
            // Dynamic actors restore before the site projection, while authored placements are
            // already in ActorSprites.  Admit their mobile media here without replaying any item
            // creation or combat rolls.
            foreach (ActorState actor in actors.All.Where(actor => !inputs.ActorSprites.ContainsKey(actor.DurableId)))
            {
                if (!authored.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition) || definition.MobileId is not int mobileId) continue;
                if (!inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored dynamic actor '{definition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                _appearance.AddActor(actor.DurableId, sprite);
            }
            if (saved is null)
            {
                foreach (ActorState actor in actors.All.Where(actor => authored[actor.DurableId].GroundOnSpawn))
                    GroundActor(actor);
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, State.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            TargetingService targeting = new(engine.Perception, _spatial, State.Actors, new DaggerTargetingPolicy(authored, tuning.MeleeTargeting));
            _staminaRecovery = new DaggerfallStaminaRecoveryModule(tuning.StaminaRecovery);
            CombatResolution combatRules = new();
            _combatResolution = combatRules;
            _vitality = new DaggerfallVitalityConsequences(combatRules);
            // One catalog answers every effect family this ruleset compiles, so a saved effect names the
            // definition that has to interpret it rather than the family that happened to start it.
            State.Effects = new DaggerfallEffectLifecycle(State.Actors, effects ?? new DaggerfallEffectCatalog(
            [
                .. DaggerfallDiseasePolicy.Definitions(
                    _random,
                    () => _time.Calendar.DayNumber,
                    () => State.Character.Career,
                    combatRules,
                    AppendEffectDamage),
                .. DaggerfallPoisonEffects.Definitions(_random, _vitality, () => State.Character.Career),
            ]));
            partiallyConstructed.Add(State.Effects);
            _rewards = new DaggerfallRewardReactions(
                State.Progression,
                State.Actors.Player.Stats,
                State.Actors.Player.Actor.Entity,
                () => State.Character.Career,
                _random,
                authored,
                tuning.Progression.EnableExperimentalKillExperience);
            State.SkillUses = new DaggerfallSkillUseReactions(State.Progression, State.Actors.Player.Stats, definitions, () => State.Character.Career);
            State.GuildMembership = new DaggerfallGuildMembershipPolicy(State.Social,
                State.SkillUses.PermanentSkillValue, DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
            State.ConcreteGuildMembership = new DaggerfallConcreteGuildMembershipRuntime(State.GuildMembership);
            State.Quests.BindRuntime(new DaggerfallQuestRuntime(State.Progression, State.Actors.Player.Stats, definitions,
                State.QuestTraining, tuning.Locomotion, _random, () => _time.Calendar, AdvanceQuestTraining));
            State.Quests.BindTravelMinutes(site => _travelPolicy.CautiousQuestLegMinutes(QuestTravelOrigin(), site));
            State.Character.BindCareerCommitted(State.SkillUses.RebaseForCareerSelection);
            State.LevelUps = new DaggerfallLevelUpState(State.Progression, State.SkillUses, State.Actors.Player.Stats,
                definitions, () => State.Character.Career, _random, _rewards);
            _equipmentMoves = new DaggerfallEquipmentMoves(inventory, equipmentCoordinator, definitions,
                () => State.Character.Career.ForbiddenEquipment, State.ItemInstances);
            _itemCondition = new DaggerfallItemConditionService(definitions, State.ItemInstances, _equipmentMoves);
            _playerSwings = new DaggerfallSwingTracker(_tuning.MeleeTargeting.MinimumSwingGestureRadians);
            _combat = new DaggerCombatRules(_random, State.Actors, State.Equipment, State.InventoryFor, State.ItemInstances, definitions, authored, targeting, use => State.SkillUses.Record(use),
                () => State.Character.Background?.Modifiers.AvoidHit ?? 0, State.EquipmentFor, _itemCondition, combatRules,
                actorId => actorId == DaggerfallActorIdentity.PlayerEntityId
                    && State.Character.CustomCareer?.Advantages.Any(trait => trait.Id == "adrenaline-rush") == true
                    ? new DaggerfallAdrenalineRush(Enabled: true, Improved: _heldEnchantments.Talents.AdrenalineRush) : default,
                () => State.PlayerControl.Position, () => State.Character, _playerSwings.TryGesture, ShotBlockedByCover,
                () => _heldEnchantments.ArmorValueModifier, DeliverWeaponPoison);
            State.Kit = new(State.Actors, _combat.Targeting, _combat.Attacks, _combat.Execution, _combat.Rules, State.Inventory, State.Equipment);
            _enemyBehavior = new DaggerfallEnemyBehaviorModule(
                engine.Perception,
                _spatial,
                new ActorNavigationCoordinator(engine.Spatial, _spatial.Session),
                State.Actors,
                State.Kit.Attacks,
                tuning.EnemyBehavior,
                contextProvider: BuildEnemyPerceptionContext,
                recordSkillUse: use => State.SkillUses.Record(use));
            _authoredEntityIds = DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, playerDefinition.Loadout);
            if (saved is null)
            {
                ulong[] actorReservations = [DaggerfallActorIdentity.PlayerEntityId, .. inputs.Project.Actors.Values.Select(placement => checked((ulong)placement.EntityId))];
                _actorIdentities = DurableIdentityAllocator.Restore(new DurableIdentityState(
                [
                    new KindAllocatorState(DurableIdentityKind.Actor, DynamicActorFirstIdentity, actorReservations, []),
                    new KindAllocatorState(DurableIdentityKind.Item, DaggerfallUniqueItemAllocator.DefaultFirstEntityId, [.. _authoredEntityIds], []),
                    new KindAllocatorState(DurableIdentityKind.Container, GroundContainerFirstIdentity, [], []),
                ]));
            }
            else
            {
                _actorIdentities = DurableIdentityAllocator.Restore(saved.RestoredIdentities());
                foreach (DaggerfallDynamicActorSave spawned in saved.DynamicActors)
                    _dynamicActors.Add(spawned.EntityId, new DaggerfallActorId(spawned.Definition));
            }

            _uniqueItems = DaggerfallUniqueItemAllocator.Sharing(_actorIdentities);
            State.Npcs.Identities = _actorIdentities;
            _heldEnchantments = new DaggerfallHeldEnchantments(State.Equipment, State.ItemInstances, definitions.Magic.MagicItems,
                State.Actors.Player.Stats, State.Actors.Entities, State.Actors.Player.Actor.Entity, () => _time.Calendar,
                () => State.PlayerControl.Position, NearbyCreatures, InSunlight, _itemCondition, InHolyPlace,
                amount => _vitality.ResolveHeldEnchantmentDamage(State.Actors.Player.Actor, amount));
            State.HeldEnchantments = _heldEnchantments;
            // Poisons are active effects, so restoring them is the effect lifecycle's own restore: this
            // owner reads, starts and cures them and keeps no state of its own to carry.
            _poisons = new DaggerfallPoisonRuntime(State.Effects, PoisonRoll, () => State.Character.Career);
            State.Poisons = _poisons;
            State.Encumbrance = new DaggerfallEncumbrancePolicy(State.Inventory, State.Actors.Player.Stats,
                () => _heldEnchantments.CarryMultiplier);
            _heldEnchantments.Refresh();
            State.Currency = new DaggerfallCurrencyService(definitions, State.Inventory, State.ItemInstances, State.Encumbrance, _uniqueItems, saved?.Currency);
            State.Bank = new DaggerfallRegionalBankState(State.Currency, State.Inventory, State.ItemInstances, saved?.Bank);
            State.Loans = new DaggerfallLoanState(saved?.Loans);
            State.Property = new DaggerfallPropertyState(saved?.Property);
            InitializePropertyStorage(saved?.Property);
            State.Crime = new DaggerfallCrimeState(saved?.Crime);
            State.Services = new DaggerfallServiceTransactions(State.Npcs, State.Social, State.Inventory, State.ItemInstances,
                State.Currency, _uniqueItems, () => _time.Calendar, () => _site.ActiveSite is { } active
                    ? new DaggerfallNpcSite(active.Id.Region, active.Name, string.Empty)
                    : null, saved?.Services);
            State.ConcreteGuildServices = new DaggerfallConcreteGuildServiceRuntime(
                State.GuildMembership, State.Npcs, State.Services);
            State.KnightlyClaims = new DaggerfallKnightlyOrderClaimState(saved?.KnightlyClaims);
            State.KnightlyClaimActions = new DaggerfallKnightlyOrderClaimRuntime(
                State.ConcreteGuildServices, State.KnightlyClaims, _random);
            State.SkillTraining = new DaggerfallSkillTrainingService(State.Services, State.Npcs, State.Social,
                State.Progression, State.SkillUses, State.QuestTraining, State.Actors.Player.Stats,
                tuning.Locomotion, () => _time.Calendar, seconds => { _ = AdvanceElapsedTime(seconds); });
            State.RegionalPrices = new DaggerfallRegionalPriceState(definitions.Factions, _random,
                _time.Calendar.DayNumber, saved?.RegionalPrices);
            State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
            State.TradeQuotes = new DaggerfallTradeQuoteService(definitions, new DaggerfallItemValuation(definitions),
                State.RegionalPrices);
            State.Transport = new DaggerfallTransportPolicy();
            State.Wagon = new DaggerfallWagonStorage(State.Containers, State.ItemInstances, definitions,
                playerEntity, _actorIdentities);
            _corpseLoot = new DaggerfallCorpseLootModule(
                engine.Perception,
                _spatial,
                containers,
                State.ItemInstances,
                playerEntity,
                State.Actors,
                authored,
                definitions,
                State.Encumbrance,
                _random,
                _uniqueItems,
                State.Progression,
                tuning.LootInteraction,
                State.Character,
                _actorIdentities);
            _groundContainers = new DaggerfallGroundContainers(containers, State.ItemInstances, playerEntity, _actorIdentities, _activeProfileKey);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored, () => State.Kit.Targeting.LastEvidence, definitions.Text);
            _inventoryUi = new DaggerfallInventoryPresentation(_equipmentMoves, definitions, inputs.ClassicPresentation.InventoryIcons,
                State.Encumbrance, State.Currency);
            _inventoryUi.UseBank(State.Bank, ActiveBankRegion);
            _inventoryUi.UseLoans(State.Loans, () => _time.Calendar, () => State.Progression.Level);
            _inventoryUi.UseItemValuation(new DaggerfallItemValuation(definitions), State.ItemInstances, DaggerfallItemOwner.Player,
                entity => State.Actors.Entities.IdentityOf(new Rusty.Engine.Entities.EntityId(entity)).Value);
            _inventoryUi.UseItemCondition(_itemCondition);
            _inventoryUi.UseGroundDrops(_groundContainers, () => State.PlayerControl.Position);
            _notebook = new DaggerfallBookNotebook(definitions, new DaggerfallTextResolver(definitions.Text));
            _inventoryUi.UseItemActions(new DaggerfallInventoryUseService(State.Inventory, definitions, State.ItemInstances, _uniqueItems, _site, _random, _itemCondition, _notebook,
                useDrug: variant => UseDrug(variant) == DaggerfallPoisonAdmission.Admitted));
            _inventoryUi.BookOpened += _ => RequestPanel(DaggerfallPanel.Journal);
            _lootUi = new DaggerfallLootPresentation(_corpseLoot, _inventoryUi, _groundContainers);
            InitializeActivation(engine, tuning.LootInteraction);
            _characterUi = new DaggerfallCharacterPresentation(definitions, State.Character, playerDefinition, equipmentCoordinator, State.LevelUps, State.Social, State.SkillUses);
            _characterUi.UseGuildMembership(State.GuildMembership, () => checked((int)_time.Calendar.DayNumber));
            _characterUi.UseItemPresentation(_inventoryUi);
            // The DOM's art comes from admitted content by media identity, so a session reads the
            // published closure once and publishes it to the UI that draws it.
            _hud = new DaggerfallHudProjection(
                engine.Ui,
                definitions.HudResources,
                compositionIdentity,
                DaggerfallUiArt.Read(engine.Content, inputs.ClassicPresentation.InventoryIcons.Values));
            partiallyConstructed.Add(_hud);
            _persistence = new(State, _corpseLoot, _groundContainers, _notebook, _uniqueItems, _camera, _time, _site, State.Effects, () => _doors, _locomotion, _climbing, _dungeonText, CapturePropertyStorage);
            if (saved is not null)
            {
                foreach (DaggerfallSiteDeltaSave delta in saved.SiteDeltas)
                {
                    DaggerfallWorldProfileKey id = delta.Profile.Require();
                    if (!_siteDeltas.TryAdd(id, new DaggerfallSiteRuntimeDelta(delta.Actors, delta.DynamicActors, delta.ActorInventories,
                        delta.Corpses, delta.Doors, delta.Effects, delta.Motion)))
                        throw new ArgumentException($"Saved site state repeats inactive site '{id}'.", nameof(saved));
                }
                HashSet<string> admittedEncounterProfiles = _siteProfiles is null
                    ? [inputs.ProfileKey.LogicalId]
                    : [.. _siteProfiles.Keys.Select(profile => profile.LogicalId)];
                if (saved.Encounters.Resolved.Any(encounter => !admittedEncounterProfiles.Contains(encounter.ProfileId)))
                    throw new ArgumentException("Saved encounter references a world profile not admitted by the current bundle.", nameof(saved));
                _encounters.Restore(saved.Encounters, DynamicActorDefinitions(), TombstonedActorIds(saved));
                _persistence.Restore(saved);
                _appearance.SyncRestoredDefeat(State.Actors);
                RestoreDungeonText(saved.DungeonText);
                _actionTriggers.RebaseRestoredPlayer(State.PlayerControl, playerEntity);
            }
            if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior)
            {
                if (saved?.ExteriorResidency is { } exterior)
                    RestoreExteriorResidency(exterior);
                else
                    UpdateExteriorResidency();
            }
        }
        catch (Exception constructionFailure)
        {
            List<Exception> failures = [constructionFailure];
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            try { DisposeExteriorAppearance(); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    /// <summary>The selected site's one authoritative RDB door owner for activation, spells, and dungeon actions.</summary>
    internal DaggerfallDoorRuntime Doors => _doors;

    private static IReadOnlyList<(DaggerfallWorldProfileKey Key, PrivateersHoldInputs Inputs)> ActionProfiles(
        PrivateersHoldInputs active,
        DaggerfallSiteProfiles? profiles)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (profiles is null) return [(active.ProfileKey, active)];

        Dictionary<DaggerfallWorldProfileKey, PrivateersHoldInputs> admitted = [];
        foreach (DaggerfallWorldProfileKey key in profiles.Keys)
            admitted.Add(key, profiles.Require(key));
        admitted.TryAdd(active.ProfileKey, active);
        if (admitted.Keys.Select(key => key.LogicalId).Distinct(StringComparer.Ordinal).Count() != admitted.Count)
            throw new ArgumentException("Admitted world profiles must use distinct logical ids for action graph save identity.", nameof(profiles));
        return admitted.OrderBy(entry => entry.Key.Site.Region)
            .ThenBy(entry => entry.Key.Site.Index)
            .ThenBy(entry => entry.Key.LogicalId, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value))
            .ToArray();
    }

    private void EnsureDungeonActionGraph(DaggerfallWorldProfileKey key, PrivateersHoldInputs inputs)
    {
        key.Validate();
        ArgumentNullException.ThrowIfNull(inputs);
        _actionTriggers.AdmitProfile(key, inputs);
        if (State.DungeonActions.ContainsKey(key)) return;
        if (inputs.ProfileKey != key)
            throw new InvalidOperationException($"World profile '{key.LogicalId}' action graph was paired with '{inputs.ProfileKey.LogicalId}'.");
        State.DungeonActions.Add(key, new DaggerfallDungeonActionGraph(key.LogicalId, inputs.DungeonActions,
            State.Variables, executeFamilyAction: ExecuteDungeonFamilyAction));
    }

    /// <summary>Admits the full selected site catalog once composition has constructed this session.</summary>
    internal void AdmitSiteProfiles(DaggerfallSiteProfiles profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (_siteProfiles is not null)
        {
            if (ReferenceEquals(_siteProfiles, profiles)) return;
            throw new InvalidOperationException("Daggerfall site profiles are already admitted for this session.");
        }
        _ = profiles.Require(_activeProfileKey);
        if (_siteAudioBundles is not null)
            foreach (DaggerfallWorldProfileKey key in profiles.Keys) _ = _siteAudioBundles.Require(key);
        _siteProfiles = profiles;
        foreach (DaggerfallWorldProfileKey key in profiles.Keys)
            EnsureDungeonActionGraph(key, profiles.Require(key));
    }

    private DaggerfallAudioBundle? AudioFor(PrivateersHoldInputs inputs) => _siteAudioBundles?.Require(inputs.ProfileKey);

    /// <summary>
    /// Relocates the existing player to an admitted named anchor. The destination is resolved
    /// before a source projection is touched, so a missing anchor cannot unload the player or
    /// source actors. Cross-profile relocation reuses the one site-transition lifecycle.
    /// </summary>
    internal bool TryRelocate(DaggerfallRelocationDestination destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        destination = (destination ?? throw new ArgumentNullException(nameof(destination))).Validate();
        PrivateersHoldInputs target = (_siteProfiles ?? throw new InvalidOperationException("Site profiles have not been admitted.")).Require(destination.Profile);
        DaggerfallSiteAnchor anchor = target.RequireAnchor(destination.AnchorId);
        if (destination.ActorId != DaggerfallActorIdentity.PlayerEntityId)
        {
            if (!State.Actors.TryGet(destination.ActorId, out _))
                throw new InvalidOperationException($"Actor {destination.ActorId} is not live in the active profile.");
            if (destination.Profile == _activeProfileKey)
            {
                ApplyActorRelocation(destination.ActorId, anchor);
                return true;
            }
            // Authored placements are owned by their profile and have no portable definition in
            // the inactive-site delta. Dynamic actors carry their definition and durable identity,
            // so they are the only non-player actors that can cross a profile boundary safely.
            if (!_dynamicActors.ContainsKey(destination.ActorId))
                throw new InvalidOperationException($"Authored actor {destination.ActorId} cannot relocate across world profiles.");
            return TryRelocateDynamicActorAcrossProfiles(destination.Profile, anchor, destination.ActorId);
        }
        if (destination.Profile == _activeProfileKey)
        {
            ApplyRelocation(anchor.Position, anchor.YawRadians, anchor.PitchRadians);
            return true;
        }
        return TryTransitionTo(destination.Profile, anchor, useReturnDestination: false);
    }

    /// <summary>
    /// Transfers a dynamic actor through the admitted destination lifecycle while retaining the
    /// player's active site.  The temporary destination admission is immediately returned through
    /// its recorded source pose, leaving the actor in the destination runtime delta.
    /// </summary>
    private bool TryRelocateDynamicActorAcrossProfiles(DaggerfallWorldProfileKey destination, DaggerfallSiteAnchor anchor, long actorId)
    {
        DaggerfallWorldProfileKey sourceProfile = _activeProfileKey;
        DaggerfallSiteContextCheckpoint sourceSite = _site.CaptureCheckpoint();
        DaggerfallWorldProfileKey? sourceReturnProfile = _returnProfileKey;
        void RestoreSourceContext()
        {
            _site.RestoreCheckpoint(sourceSite);
            _activeProfileKey = sourceProfile;
            _returnProfileKey = sourceReturnProfile;
        }
        try
        {
            if (!TryTransitionTo(destination, anchor, useReturnDestination: false, actorId, playerFacing: false)) return false;
        }
        catch (Exception failure)
        {
            // TryTransitionTo commits the destination before releasing the old projection.  A
            // release failure therefore leaves the destination active; complete the actor-only
            // round trip before surfacing that release failure.
            try
            {
                if (_activeProfileKey != sourceProfile)
                    _ = TryTransitionTo(sourceProfile, null, useReturnDestination: true, relocatedActorId: null, playerFacing: false);
                if (_activeProfileKey == sourceProfile)
                    RestoreSourceContext();
            }
            catch (Exception rollbackFailure)
            {
                // A second transition may itself commit source before reporting a projection
                // disposal failure. Preserve that committed source context before aggregating.
                if (_activeProfileKey == sourceProfile)
                {
                    try { RestoreSourceContext(); }
                    catch (Exception contextFailure) { throw new AggregateException(failure, rollbackFailure, contextFailure); }
                }
                throw new AggregateException(failure, rollbackFailure);
            }
            throw;
        }
        try
        {
            if (!TryTransitionTo(sourceProfile, null, useReturnDestination: true, relocatedActorId: null, playerFacing: false)) return false;
            // Returning through the ordinary site lifecycle establishes the durable destination
            // delta. Restore the source context checkpoint so actor-only relocation does not alter
            // the player's prior return anchor or discovery state.
            _site.RestoreCheckpoint(sourceSite);
            _activeProfileKey = sourceProfile;
            _returnProfileKey = sourceReturnProfile;
            return true;
        }
        catch (Exception failure)
        {
            // A failure while returning leaves the temporary destination active. Make one bounded
            // attempt to restore the source before surfacing the original failure.
            if (_activeProfileKey != sourceProfile)
            {
                try
                {
                    _ = TryTransitionTo(sourceProfile, null, useReturnDestination: true, relocatedActorId: null, playerFacing: false);
                    RestoreSourceContext();
                }
                catch (Exception rollbackFailure)
                {
                    if (_activeProfileKey == sourceProfile)
                    {
                        try { RestoreSourceContext(); }
                        catch (Exception contextFailure) { throw new AggregateException(failure, rollbackFailure, contextFailure); }
                    }
                    throw new AggregateException(failure, rollbackFailure);
                }
            }
            else
            {
                try { RestoreSourceContext(); }
                catch (Exception rollbackFailure) { throw new AggregateException(failure, rollbackFailure); }
            }
            throw;
        }
    }

    /// <summary>Attempts one real site transition; failed destination admission leaves the source projection live.</summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination) => TryTransitionTo(destination, null, useReturnDestination: true);

    private bool TryTransitionTo(DaggerfallWorldProfileKey destination, DaggerfallSiteAnchor? arrival, bool useReturnDestination, long? relocatedActorId = null, bool playerFacing = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PrivateersHoldInputs target = (_siteProfiles ?? throw new InvalidOperationException("Site profiles have not been admitted.")).Require(destination);
        WorldPoint sourcePosition = State.PlayerControl.Position ?? throw new InvalidOperationException("A site transition requires a player position.");
        DaggerfallSiteReturnDestination? returnDestination = useReturnDestination && _returnProfileKey == destination
            ? _site.RequireReturnDestination()
            : null;
        DaggerfallSiteProjection source = _siteProjection;
        DaggerfallWorldProfileKey sourceProfile = _activeProfileKey;
        DaggerfallWorldProfileKey? sourceReturnProfile = _returnProfileKey;
        DaggerfallServiceProvider? sourceBankProvider = _bankProvider;
        DaggerfallSiteContextCheckpoint sourceSite = _site.CaptureCheckpoint();
        float sourceYawRadians = State.PlayerControl.YawRadians;
        float sourcePitchRadians = State.PlayerControl.PitchRadians;
        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> sourceDeltas = new(_siteDeltas);
        DaggerfallSiteRuntimeDelta sourceDelta = _persistence.CaptureSiteDelta(source.Inputs, source.Doors,
            source.Motion, _dynamicActors);
        DaggerfallExteriorCellResidencySave? sourceExterior = CaptureExteriorResidency();
        DaggerfallDynamicActorSave? relocatedDynamic = relocatedActorId is long requestedActor
            ? sourceDelta.DynamicActors.SingleOrDefault(actor => actor.EntityId == requestedActor)
                ?? throw new InvalidOperationException($"Dynamic actor {requestedActor} was not captured in the active site delta.")
            : null;
        DaggerfallSiteRuntimeDelta? relocatedDelta = relocatedDynamic is null
            ? null
            : DynamicActorDelta(sourceDelta, relocatedDynamic.EntityId);
        _siteDeltas.TryGetValue(destination, out DaggerfallSiteRuntimeDelta? destinationDelta);
        DaggerfallSiteRuntimeDelta? destinationTeardownDelta = destinationDelta;
        DaggerfallSiteProjection? candidate = null;
        bool spatialReplaced = false;
        bool sourceActorsUnloaded = false;
        bool groundProfileSwitched = false;
        bool playerRelocated = false;
        bool exteriorCleared = false;
        DaggerfallSiteRuntimeDelta committedSourceDelta = sourceDelta;
        try
        {
            candidate = DaggerfallSiteProjection.Create(_engine, State.Actors.Entities, _random, _tuning, _time.Calendar,
                target, AudioFor(target), _spatial, destinationDelta?.Doors, destinationDelta?.Motion,
                deferMotionCollisionAdmission: true);
            if (sourceExterior is not null)
            {
                ClearExteriorResidency();
                exteriorCleared = true;
            }
            _spatial.ReplaceContent(target.SpatialArtifact);
            spatialReplaced = true;
            candidate.ActivateMotionCollisionResidency();
            UnloadSiteActors(source.Inputs, sourceDelta);
            sourceActorsUnloaded = true;
            _siteProjection = candidate;
            candidate = null;
            RestoreAuthoredSiteActors(target, destinationDelta);
            if (relocatedDynamic is not null)
            {
                // Include the migrating identity in rollback teardown before materialization can
                // fail on destination media or effect admission.
                destinationTeardownDelta = AppendDynamicActor(destinationDelta, relocatedDynamic);
                RestoreRelocatedDynamicActor(target, relocatedDynamic, relocatedDelta!);
                committedSourceDelta = WithoutDynamicActor(sourceDelta, relocatedDynamic.EntityId);
                ApplyActorRelocation(relocatedDynamic.EntityId, arrival
                    ?? throw new InvalidOperationException("A cross-profile actor relocation requires a destination anchor."));
            }
            _groundContainers.SwitchProfile(destination);
            groundProfileSwitched = true;
            // Activation depends only on the admitted candidate projection.  Prepare it before
            // mutating player or site state so an Engine service rejection has nothing semantic
            // to roll back.
            InitializeActivation(_engine, _tuning.LootInteraction);
            if (returnDestination is { } returned)
            {
                _site.Leave();
                playerRelocated = true;
                ApplyRelocation(returned.Pose.Position, returned.Pose.YawRadians, returned.Pose.PitchRadians);
            }
            else
            {
                _site.Enter(destination.Site, sourcePosition, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
                playerRelocated = true;
                if (arrival is { } selected)
                    ApplyRelocation(selected.Position, selected.YawRadians, selected.PitchRadians);
                else
                    ApplyRelocation(target.Project.PlayerPosition ?? sourcePosition, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
            }
            _siteDeltas[sourceProfile] = committedSourceDelta;
            _siteDeltas.Remove(destination);
            _activeProfileKey = destination;
            // Entering a place retires the previous world's loop: the donor gives a dungeon a new song
            // per location rather than carrying the last one through the door. A relocation that moves
            // one actor between profiles is not the player entering anything, so it leaves the score
            // alone rather than restarting it behind a teleport the player never sees.
            if (playerFacing) ChangeMusicSite();
            _bankProvider = null;
            if (destination.Kind == DaggerfallWorldProfileKind.Exterior)
                UpdateExteriorResidency();
            if (destination.Kind != DaggerfallWorldProfileKind.Exterior)
                State.Transport.ForceFootOnInteriorTransition();
            _returnProfileKey = returnDestination is null ? sourceProfile : null;
            _enemyBehavior.ClearPerceptionMemory();
            if (target.DungeonMap is { } destinationMap)
            {
                if (!State.DungeonDiscoveries.TryGetValue(destination, out DaggerfallDungeonDiscovery? discovery))
                    State.DungeonDiscoveries.Add(destination, discovery = new DaggerfallDungeonDiscovery(destination, destinationMap));
                discovery.BeginVisit();
            }
            EnsureDungeonActionGraph(destination, target);
            _actionTriggers.ActivateProfile(destination);
        }
        catch (Exception failure)
        {
            List<Exception> failures = [failure];
            DaggerfallSiteProjection? rejectedProjection = null;
            if (!ReferenceEquals(_siteProjection, source))
            {
                rejectedProjection = _siteProjection;
                _siteProjection = source;
            }
            if (groundProfileSwitched)
            {
                try { _groundContainers.SwitchProfile(sourceProfile); }
                catch (Exception groundFailure) { failures.Add(groundFailure); }
            }
            if (sourceActorsUnloaded)
            {
                try { UnloadSiteActors(target, destinationTeardownDelta); }
                catch (Exception teardownFailure) { failures.Add(teardownFailure); }
                try { RestoreAuthoredSiteActors(source.Inputs, sourceDelta); }
                catch (Exception restoreFailure) { failures.Add(restoreFailure); }
            }
            if (spatialReplaced)
            {
                try { _spatial.ReplaceContent(source.Inputs.SpatialArtifact); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                // Whole-content replacement removes every incremental resident collider. Drop the
                // destination coordinator's remembered set before restoring the source window;
                // otherwise overlapping cells would be skipped as already admitted.
                try { ClearExteriorResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                try { source.RebuildMotionCollisionResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { source.Lighting.ApplyBackground(); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { _site.RestoreCheckpoint(sourceSite); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            _siteDeltas.Clear();
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteRuntimeDelta delta) in sourceDeltas) _siteDeltas.Add(key, delta);
            _activeProfileKey = sourceProfile;
            _returnProfileKey = sourceReturnProfile;
            _bankProvider = sourceBankProvider;
            if (sourceExterior is { } priorExterior && exteriorCleared)
            {
                try { RestoreExteriorResidency(priorExterior); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            else if (_exteriorResidency is { IsInitialized: true })
            {
                try { ClearExteriorResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { _actionTriggers.ActivateProfile(sourceProfile); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            if (playerRelocated)
            {
                try { ApplyRelocation(sourcePosition, sourceYawRadians, sourcePitchRadians); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { InitializeActivation(_engine, _tuning.LootInteraction); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { candidate?.Dispose(); }
            catch (Exception disposeFailure) { failures.Add(disposeFailure); }
            try { rejectedProjection?.Dispose(); }
            catch (Exception disposeFailure) { failures.Add(disposeFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Site transition failed and source restoration was incomplete.", failures);
        }

        if (relocatedActorId is null
            && State.DungeonDiscoveries.TryGetValue(sourceProfile, out DaggerfallDungeonDiscovery? sourceDiscovery)
            && source.Inputs.DungeonMap is { } sourceMap)
        {
            DaggerfallDungeonMapMarker? usedPortal = sourceMap.Markers
                .Where(marker => marker.Kind == DaggerfallDungeonMapMarkerKind.Portal
                    && marker.DestinationLogicalProfile == destination.LogicalId)
                .OrderBy(marker => Vector3.DistanceSquared(marker.Position.ToVector(), sourcePosition.ToVector()))
                .FirstOrDefault();
            if (usedPortal is not null && Vector3.DistanceSquared(usedPortal.Position.ToVector(), sourcePosition.ToVector()) <= 9f)
                sourceDiscovery.RevealMarker(usedPortal.Id);
        }

        // Releasing the old projection happens only after every durable and live owner has
        // committed to the destination. A disposal failure therefore leaves the destination as
        // the honest current state instead of pretending the torn-down source can be restored.
        // A swing the departing projection was still timing ends with it: retire and hand its
        // impact back to the shared state here, inside the generation that admitted it, so the
        // player is not left charged against an animation that no longer exists.
        RetireDepartingSwing(source);
        CancelDungeonTextOnUnload();
        source.Dispose();
        return true;
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
    /// Whether the player stands in a holy place, which the donor's worn condition payloads and its
    /// career damage traits both read. No site classification answers it yet, so this is false and a
    /// payload carrying that condition never acts; when one lands it is wired here and both readers
    /// pick it up at once.
    /// </summary>
    private bool InHolyPlace() => false;

    /// <summary>
    /// The living creatures a worn enchantment's near-creature condition can see: the group the
    /// ruleset's own enemy-group policy gives them, and where they stand now. The donor flags a
    /// civilian NPC as a humanoid outright, so a civilian definition answers humanoid here too
    /// rather than falling through the enemy-group policy's monster and class arms.
    /// </summary>
    private IReadOnlyList<DaggerfallNearbyCreature> NearbyCreatures()
    {
        List<DaggerfallNearbyCreature> nearby = [];
        foreach (ActorState actor in State.Actors.All)
        {
            if (actor.IsDefeated) continue;
            if (!_definitionsByActor.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)) continue;
            DaggerfallEnemyGroup group = definition.Kind == DaggerfallActorKinds.Civilian
                ? DaggerfallEnemyGroup.Humanoid
                : DaggerfallFormulaPolicy.EnemyGroupFor(definition);
            nearby.Add(new DaggerfallNearbyCreature(group, actor.Position));
        }
        return nearby;
    }

    /// <summary>
    /// Whether admitted static geometry stands between a shot's release and its aim, asked of the
    /// Engine's own segment query at chest height. A shooter standing inside geometry would otherwise
    /// report every shot as blocked, so the segment starts clear of the muzzle.
    /// </summary>
    private bool ShotBlockedByCover(WorldPoint origin, WorldPoint aim)
    {
        Vector3 from = origin.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight;
        Vector3 to = aim.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight;
        Vector3 delta = to - from;
        float distance = delta.Length();
        if (!float.IsFinite(distance) || distance <= CoverCastStartMeters) return false;
        Vector3 direction = delta / distance;
        SpatialHit hit = _spatial.CastRay(from + direction * CoverCastStartMeters, direction, distance - CoverCastStartMeters);
        return hit.Present && hit.Kind == SpatialHitKind.StaticMesh;
    }

    /// <summary>How far along the shot the cover query starts, clear of the shooter's own position.</summary>
    private const float CoverCastStartMeters = .3f;

    /// <summary>
    /// Retires the departing projection's unfinished swing. The notice it publishes belongs to the
    /// generation that admitted the swing, so it is applied with that generation rather than the
    /// destination's, and the caller's normal fact boundary publishes any consequence.
    /// </summary>
    private void RetireDepartingSwing(DaggerfallSiteProjection source)
    {
        source.Appearance.RetirePendingSwing();
        IReadOnlyList<AttackImpactNotice> impacts = source.Appearance.TakeAttackImpacts();
        if (impacts.Count == 0) return;
        if (_latestUpdateGeneration is not ulong generation) return;
        State.Kit.AttackExecution.ApplyImpacts(impacts, generation, _facts);
    }

    private void ApplyActorRelocation(long actorId, DaggerfallSiteAnchor anchor)
    {
        if (actorId == DaggerfallActorIdentity.PlayerEntityId)
        {
            ApplyRelocation(anchor.Position, anchor.YawRadians, anchor.PitchRadians);
            return;
        }

        ActorState actor = State.Actors.TryGet(actorId, out ActorState? live)
            ? live
            : throw new InvalidOperationException($"Actor {actorId} is not live in the active profile.");
        actor.ApplyPose(new ActorPose(anchor.Position, anchor.YawRadians));
    }

    private void RestoreRelocatedDynamicActor(PrivateersHoldInputs destination, DaggerfallDynamicActorSave saved, DaggerfallSiteRuntimeDelta delta)
    {
        _ = DaggerActorFactory.CreateDynamicActor(_random, _mechanics, _definitions, State.Actors, State.InventoryStore,
            _definitionsByActor, saved);
        _dynamicActors.Add(saved.EntityId, new DaggerfallActorId(saved.Definition));
        if (_definitionsByActor[saved.EntityId].MobileId is int mobileId)
        {
            if (!destination.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                throw new InvalidOperationException($"Relocated actor '{saved.Definition}' has no admitted mobile {mobileId} presentation.");
            _appearance.AddActor(saved.EntityId, sprite);
        }
        _persistence.RestoreSiteDelta(delta);
        _appearance.SyncRestoredDefeat(State.Actors);
    }

    private static DaggerfallSiteRuntimeDelta DynamicActorDelta(DaggerfallSiteRuntimeDelta source, long actorId) =>
        new(
            [],
            source.DynamicActors.Where(actor => actor.EntityId == actorId).ToArray(),
            source.ActorInventories.Where(inventory => inventory.EntityId == actorId).ToArray(),
            source.Corpses.Where(corpse => corpse.ActorId == actorId).ToArray(),
            [],
            source.Effects.Where(effect => effect.TargetId == actorId).ToArray());

    private static DaggerfallSiteRuntimeDelta WithoutDynamicActor(DaggerfallSiteRuntimeDelta source, long actorId) =>
        source with
        {
            DynamicActors = source.DynamicActors.Where(actor => actor.EntityId != actorId).ToArray(),
            ActorInventories = source.ActorInventories.Where(inventory => inventory.EntityId != actorId).ToArray(),
            Corpses = source.Corpses.Where(corpse => corpse.ActorId != actorId).ToArray(),
            Effects = source.Effects.Where(effect => effect.TargetId != actorId).ToArray(),
        };

    private static DaggerfallSiteRuntimeDelta AppendDynamicActor(DaggerfallSiteRuntimeDelta? destination, DaggerfallDynamicActorSave actor) =>
        destination is null
            ? new([], [actor], [], [], [], [])
            : destination with { DynamicActors = [.. destination.DynamicActors, actor] };

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
        _locomotion.Neutralize();
        _climbing.Detach();
        _verticalMovementDriven = false;
        _lootUi.CloseAll();
        State.PlayerControl.YawRadians = yawRadians;
        State.PlayerControl.PitchRadians = pitchRadians;
        _spatial.Relocate(State.PlayerControl, position);
        _camera.Update(State.PlayerControl);
    }

    /// <summary>Every actor definition by durable identity: authored placements and spawned actors alike.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> DefinitionsByActor => _definitionsByActor;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> DynamicActors => _dynamicActors;

    private IReadOnlyDictionary<long, string> DynamicActorDefinitions()
    {
        Dictionary<long, string> values = _dynamicActors.ToDictionary(entry => entry.Key, entry => entry.Value.Value);
        foreach (DaggerfallSiteRuntimeDelta delta in _siteDeltas.Values)
        foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors)
            if (!values.TryAdd(actor.EntityId, actor.Definition))
                throw new InvalidOperationException($"Dynamic actor {actor.EntityId} is active in more than one site profile.");
        return values;
    }

    private static IReadOnlySet<long> TombstonedActorIds(DaggerfallSavePayload saved)
    {
        DurableIdentityAllocator identities = DurableIdentityAllocator.Restore(saved.RestoredIdentities());
        return identities.RemovedIdentities(DurableIdentityKind.Actor)
            .Select(value => checked((long)value))
            .ToHashSet();
    }

    private void UnloadSiteActors(PrivateersHoldInputs source, DaggerfallSiteRuntimeDelta? delta)
    {
        long[] ids = [.. source.Project.Actors.Keys.Concat(delta?.DynamicActors.Select(actor => actor.EntityId) ?? []).Order()];
        // The delta was captured while these actors and their target-bound contributions were live.
        // Detach every target lifecycle before destroying Engine entities; effects on a live player
        // with one of these actors as caster remain active by durable caster identity.
        _ = State.Effects.SuspendTargets(ids);
        foreach (long id in ids)
        {
            if (!State.Actors.TryGet(id, out ActorState? actor)) continue;
            _lootUi.CloseActor(actor.DurableId);
            DestroySiteOwnedUniqueItems(actor);
            _definitionsByActor.Remove(actor.DurableId);
            if (_dynamicActors.Remove(actor.DurableId)) _appearance.RetireActor(actor.DurableId);
            State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(actor.DurableId));
            // A corpse owns a second Engine inventory/container entity. Capture has already
            // detached its durable facts, so retire that owner with the site actor rather than
            // leaving an unreachable native container alive across the transition.
            State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(actor.DurableId));
            _corpseLoot.Unload(actor.DurableId);
            State.Actors.Entities.Destroy(ActorsState.Identity(actor.DurableId));
        }
    }

    /// <summary>Releases live Engine item entities while preserving their durable identities for an inactive-site restore.</summary>
    private void DestroySiteOwnedUniqueItems(ActorState actor)
    {
        List<ulong> identities = [];
        if (State.InventoryFor(actor.DurableId) is { } inventory)
            identities.AddRange(inventory.Read().UniqueItems.Select(item => State.Actors.Entities.IdentityOf(item.Entity).Value));
        if (_corpseLoot.Corpses.TryGetValue(actor.DurableId, out CorpseContainer? corpse) && corpse.IsRegistered)
            identities.AddRange(State.Containers.Read(corpse.Owner).UniqueItems.Select(item => State.Actors.Entities.IdentityOf(item.Entity).Value));
        foreach (ulong itemId in identities.Distinct())
        {
            State.ItemInstances.RemoveUnique(itemId);
            State.Actors.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Item, itemId));
        }
    }

    private void RestoreAuthoredSiteActors(PrivateersHoldInputs destination, DaggerfallSiteRuntimeDelta? delta)
    {
        Dictionary<long, DaggerfallActorSave> saved = delta?.Actors.ToDictionary(value => value.EntityId) ?? [];
        foreach (AuthoredActor placement in destination.Project.Actors.Values.OrderBy(value => value.EntityId))
        {
            saved.TryGetValue(placement.EntityId, out DaggerfallActorSave? prior);
            DaggerfallActorDefinition definition = _definitions.RequireActor(placement.ActorId);
            ActorState actor = DaggerActorFactory.CreateAuthoredActor(_random, _mechanics, _definitions, State.Actors, State.InventoryStore,
                State.ItemDefinitions, State.ItemInstances, placement, prior);
            if (prior is not null) actor.ApplyPose(new ActorPose(new WorldPoint(prior.X, prior.Y, prior.Z), prior.HeadingRadians));
            else if (definition.GroundOnSpawn) GroundActor(actor);
            _definitionsByActor.Add(actor.DurableId, definition);
        }
        if (delta is not null)
        {
            foreach (DaggerfallDynamicActorSave savedDynamic in delta.DynamicActors.OrderBy(actor => actor.EntityId))
            {
                _ = DaggerActorFactory.CreateDynamicActor(_random, _mechanics, _definitions, State.Actors, State.InventoryStore,
                    _definitionsByActor, savedDynamic);
                _dynamicActors.Add(savedDynamic.EntityId, new DaggerfallActorId(savedDynamic.Definition));
                if (_definitionsByActor[savedDynamic.EntityId].MobileId is int mobileId)
                {
                    if (!_siteProjection.Inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                        throw new InvalidOperationException($"Restored dynamic actor '{savedDynamic.Definition}' has no admitted mobile {mobileId} presentation.");
                    _appearance.AddActor(savedDynamic.EntityId, sprite);
                }
            }
        }
        if (delta is not null)
        {
            _persistence.RestoreSiteDelta(delta);
            _appearance.SyncRestoredDefeat(State.Actors);
        }
    }

    /// <summary>
    /// Registers one actor from a published definition beyond the authored placements, with the
    /// same Mechanics binding an authored actor is constructed with: catalog stats, pursuit
    /// memory, managed inventory and equipment, definition loadout, and floor grounding.
    /// Returns the allocated durable identity, which the save persists and restore reuses.
    /// </summary>
    internal long SpawnActor(string definitionId, ActorPose pose, int? level = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        DaggerfallActorDefinition definition = _definitions.RequireActor(new DaggerfallActorId(definitionId));
        if (definition.Kind == DaggerfallActorKinds.Player)
            throw new InvalidOperationException("The player actor is authored once; it cannot be spawned.");
        if (level is < 1) throw new ArgumentOutOfRangeException(nameof(level));
        // Class enemies level to the player the way the donor levels them; monsters carry their
        // own level. Pack skills stay as authored: the donor overwrites every skill from the
        // spawn level, but authored pack values own that meaning here.
        int spawnLevel = level ?? definition.Level ?? (definition.Kind == DaggerfallActorKinds.EnemyClass ? State.Progression.Level : 1);
        DurableIdentityReference identity = _actorIdentities.Allocate(DurableIdentityKind.Actor);
        long durableId = checked((long)identity.Value);
        if (definition.Kind == DaggerfallActorKinds.EnemyClass && definition.MobileId == 146)
            spawnLevel = checked(spawnLevel + (int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.EnemyScope,
                $"class-guard-level:{durableId}", 3, 6)).Value);
        DaggerfallActorDefinition spawnedDefinition = DaggerfallEncounterActors.AtLevel(definition, _definitions.Vocabulary, spawnLevel);
        bool registered = false;
        try
        {
            ActorState actor = State.Actors.CreateActor(durableId, new EntityTypeId(spawnedDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(spawnedDefinition, SpawnVitals(spawnedDefinition, spawnLevel, durableId)),
                pose, spawnedDefinition.Combat.Health.Value);
            // The behavior module attaches pursuit memory to every actor it is constructed with;
            // a spawn arrives after construction, so it carries its own.
            State.Actors.Store.Add(actor.Actor.Entity, new PursuitMemoryComponent());
            DaggerActorFactory.RegisterActorInventory(actor, State.InventoryStore);
            GrantSpawnLoadout(actor, spawnedDefinition);
            if (spawnedDefinition.Kind == DaggerfallActorKinds.EnemyClass)
                GrantClassEnemyEquipment(actor, spawnedDefinition, spawnLevel);
            if (spawnedDefinition.MobileId is int mobileId)
            {
                if (!_siteProjection.Inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Spawned actor '{spawnedDefinition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                _appearance.AddActor(durableId, sprite);
            }
            _definitionsByActor.Add(durableId, spawnedDefinition);
            _dynamicActors.Add(durableId, spawnedDefinition.Id);
            registered = true;
            if (definition.GroundOnSpawn) GroundActor(actor);
            return durableId;
        }
        catch
        {
            if (registered)
            {
                _definitionsByActor.Remove(durableId);
                _dynamicActors.Remove(durableId);
            }

            State.Actors.Entities.Destroy(identity);
            _actorIdentities.Remove(identity);
            throw;
        }
    }

    /// <summary>
    /// Retires one spawned actor: definition and appearance references drop, an open loot
    /// container for the actor closes, owned unique items
    /// are destroyed with their identities tombstoned, the corpse container goes with the actor,
    /// and the identity stays tombstoned so removal remains distinguishable from never-loaded.
    /// Authored placement actors belong to the selected content and cannot retire; the player
    /// can never retire. The shared managed store keeps unreachable per-actor state afterwards —
    /// it has no unregister-owner path — but nothing reachable observes it: every read goes
    /// through live actors.
    /// </summary>
    internal void RetireActor(long durableId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (durableId == DaggerfallActorIdentity.PlayerEntityId)
            throw new InvalidOperationException("The player actor cannot be retired.");
        if (durableId <= 0) throw new ArgumentOutOfRangeException(nameof(durableId));
        if (!_dynamicActors.Remove(durableId, out _))
        {
            if (State.Actors.TryGet(durableId, out _))
                throw new InvalidOperationException($"Authored actor {durableId} belongs to the selected content and cannot be retired; only spawned actors retire.");
            DurableIdentityClassification classification = _actorIdentities.Classify(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
            throw new InvalidOperationException($"Actor {durableId} is {classification}; only a live spawned actor can be retired.");
        }

        _definitionsByActor.Remove(durableId);
        _appearance.RetireActor(durableId);
        _lootUi.CloseActor(durableId);
        _ = State.Effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        State.Actors.Entities.Destroy(ActorsState.Identity(durableId));
        _actorIdentities.Remove(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
        if (State.Npcs.All.Any(npc => npc.DurableId == durableId && npc.Kind == DaggerfallNpcKind.Civilian))
            State.Npcs.SetPresence(durableId, DaggerfallNpcPresence.Removed);
    }

    private void GroundActor(ActorState actor)
    {
        // RDB marker heights are probe origins, not floor contacts. Unlike DFU's centered
        // capsule, our navigation pose is the sprite's base.
        SpatialHit floor = _spatialService.CastRay(new SpatialRaycastRequest(
            _spatial.Session,
            actor.Position.ToVector() + Vector3.UnitY * _spawnGroundProbeLift,
            -Vector3.UnitY,
            _spawnGroundProbeDistance,
            new SpatialQueryFilter(uint.MaxValue, uint.MaxValue), default, default, default));
        if (floor.Present && !floor.StartSolid && floor.Normal.Y > 0f)
            actor.ApplyPose(new ActorPose(WorldPoint.From(floor.Point), actor.HeadingYawRadians));
    }

    private DaggerfallVitalValues SpawnVitals(DaggerfallActorDefinition definition, int level, long durableId)
    {
        if (definition.Kind == DaggerfallActorKinds.EnemyClass && definition.HitPointsPerLevel is int hitPointsPerLevel)
        {
            // Each level roll draws under its own key: a keyed draw is deterministic per key, so
            // reusing one key would repeat the same roll for every level.
            int rollIndex = 0;
            int health = DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(level, hitPointsPerLevel, (minimum, maximum) =>
                checked((int)_random.DrawKeyed(new KeyedRngRequest(
                    CombatRandomKey.Seed,
                    CombatRandomKey.EnemyScope,
                    CombatRandomKey.ClassHealth(durableId, definition.Id.Value, rollIndex++),
                    minimum,
                    maximum)).Value));
            return new DaggerfallVitalValues(health, 0, 0);
        }

        return DaggerActorFactory.InitialVitals(_random, definition, durableId);
    }

    private void GrantClassEnemyEquipment(ActorState actor, DaggerfallActorDefinition definition, int spawnLevel)
    {
        if (definition.MobileId is not int mobileId) throw new InvalidOperationException($"Class actor '{definition.Id.Value}' has no human mobile id.");
        MechanicsInventoryCoordinator inventory = State.InventoryFor(actor.DurableId)
            ?? throw new InvalidOperationException($"Spawned actor {actor.DurableId} has no registered inventory.");
        DaggerfallClassEnemyEquipmentPolicy.Equip(_definitions, _random, State.ItemInstances, _uniqueItems, inventory, State.EquipmentFor(actor.DurableId),
            actor.DurableId, mobileId, State.Progression.Level,
            State.Character.Identity.RaceId,
            State.Character.Identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male");
    }

    private void GrantSpawnLoadout(ActorState actor, DaggerfallActorDefinition definition)
    {
        if (definition.Loadout.Count == 0) return;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            if (!_definitions.Items.TryGetValue(entry.ItemId, out DaggerfallItemDefinition? item) || !item.IsFungible)
                throw new InvalidOperationException($"Spawned actor '{definition.Id.Value}' loadout carries a unique or missing item, which spawned actors do not equip yet.");
        }

        MechanicsInventoryCoordinator actorInventory = State.InventoryFor(actor.DurableId)
            ?? throw new InvalidOperationException($"Spawned actor {actor.DurableId} has no registered inventory.");
        int ordinal = 0;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            InventoryStackId stackId = DaggerfallInventoryStackIds.ForSpawnLoadout(actor.DurableId, ordinal++);
            actorInventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value), stackId, entry.Quantity));
            State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(actor.DurableId),
                new InventoryStack(stackId, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), _definitions.Items[entry.ItemId]);
        }
    }

    /// <summary>
    /// Destroys the unique items one retiring actor owns — carried, equipped, and corpse-seeded —
    /// and tombstones their identities so the save never reissues them. Fungible stacks and their instance metadata retire with the actor.
    /// </summary>
    private void DestroyOwnedItems(long durableId)
    {
        if (!State.Actors.TryGet(durableId, out ActorState? actor)) return;
        HashSet<ulong> owned = [];
        if (State.InventoryFor(durableId) is { } inventory)
            foreach (var item in inventory.Read().UniqueItems)
                owned.Add(State.Actors.Entities.IdentityOf(item.Entity).Value);
        // Equipment assignments name the same durable numbers directly.
        foreach (var assignment in State.EquipmentFor(durableId).Read().Assignments)
            owned.Add(assignment.Item.EntityId);
        if (actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null && corpse.HasRegisteredInventory)
            foreach (var item in State.Containers.Read(corpse.Owner).UniqueItems)
                owned.Add(State.Actors.Entities.IdentityOf(item.Entity).Value);
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(durableId));
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(durableId));
        foreach (ulong itemId in owned)
        {
            State.ItemInstances.RemoveUnique(itemId);
            _ = State.Effects.CancelItemReferences(itemId);
            DurableIdentityReference reference = new(DurableIdentityKind.Item, itemId);
            State.Actors.Entities.Destroy(reference);
            // Authored reservations stay reserved: the destroyed entity is gone either way, and
            // tombstoning one would misreport content as removed. This is the same rule as
            // RemoveUniqueItemIdentity, minus the throw — retirement must not fail midway.
            if (!_authoredEntityIds.Contains(itemId)) _uniqueItems.Remove(reference);
        }
    }

    public void PublishInitial()
    {
        _hud.RequestArt();
        PublishPresentation();
    }

    /// <summary>Captures meaningful state using the current product schema.</summary>
    public RulesetSavePayload CaptureSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _persistence.Capture(_latestUpdateGeneration, _latestSimulationStep, _dynamicActors, _encounters,
            _siteDeltas, _activeProfileKey, _returnProfileKey, State.DungeonDiscoveries, State.DungeonActions,
            _siteProjection.CaptureMotion(), CaptureExteriorResidency());
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();

    private static World.DaggerfallSiteReturnPose? ToSiteReturnPose(DaggerfallSiteReturnPoseSave? pose) => pose is null
        ? null
        : new World.DaggerfallSiteReturnPose(new WorldPoint(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians);

    public ProductUpdateResult Update(ProductUpdate update)
    {
        _appearance.BeginAdmittedUpdate();
        try
        {
        Update(update.Facts, update.Input);
        // Sprite playback consumes the Engine-bound outer update identity.  It
        // must not run for each private catch-up simulation step above, and it
        // must not run while the world is holding still.
        if (_mode == ProductMode.Playing
            && update.Facts.LifecycleState == ProductLifecycleState.Running
            && update.Facts.Mode == ProductUpdateMode.Realtime
            && update.Facts.AdmittedStepCount > 0)
        {
            _appearance.Advance(update.Facts);
            // A held value is recomputed before anything this update can read it: impacts resolve, the
            // flight advances and presentation publishes against what the player is wearing now.
            _heldEnchantments.Refresh();
            ApplyAttackImpacts();
            UpdateRangedFlight(update.Facts);
            PublishPresentation();
            // The score follows the world this admitted update settled: a site change or a change of day
            // is real once the step applied it, and the director is told once per admitted update.
            AdvanceMusic();
        }
        _appearance.CompleteAdmittedUpdate();
        }
        catch (Exception failure)
        {
            // A thrown product callback is terminal for this runtime incarnation:
            // Engine discards staged output and requires a fresh process rather
            // than a same-instance retry. There is no fact/stamina replay here,
            // only native appearance cleanup.
            try { _appearance.Dispose(); }
            catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
            throw;
        }

        return ProductUpdateResult.None;
    }

    internal void Update(ProductUpdateFacts facts, ReadOnlySpan<ProductInputEvent> input)
    {
        // Daggerfall is a realtime simulation. Demand and external turns have no
        // fixed delta, so this ruleset deliberately does not interpret them as steps.
        if (facts.LifecycleState != ProductLifecycleState.Running
            || facts.Mode != ProductUpdateMode.Realtime
            || facts.AdmittedStepCount == 0
            || !double.IsFinite(facts.FixedDeltaSeconds)
            || facts.FixedDeltaSeconds <= 0d
            || facts.FixedDeltaSeconds > float.MaxValue)
        {
            return;
        }

        float deltaSeconds = (float)facts.FixedDeltaSeconds;
        ProductUpdateState firstStep = new(deltaSeconds);
        // A mode that is not ordinary play interprets no gameplay input and advances no world
        // time. The modal's own semantic actions still apply, because a modal that cannot act is
        // not a modal; everything else - including the entry screen's own action, which the product
        // answers before this runs - is dropped and the presentation still publishes so the player
        // can see the mode they are in.
        Cinematics?.Poll();
        _openingCinematics.Poll();
        bool playing = _mode == ProductMode.Playing && Cinematics?.ActiveSource is null;
        bool modal = _mode == ProductMode.Modal;
        // The slice that opens an interaction admits no attack. A key pressed in the same admitted
        // update as the interaction key is a coincidence of timing rather than an instruction, and
        // whichever order the Engine delivers them in, swinging on the frame a container opens is
        // the "unintended attack" this task exists to prevent.
        bool opensInteraction = playing && ContainsInteractionAction(input);
        bool restSubmitted = false;
        foreach (ProductInputEvent inputEvent in input)
        {
            firstStep.Add(inputEvent);
            if (inputEvent.ValueKind != InputValueKind.ProductPayload
                || !inputEvent.PayloadContract.Span.SequenceEqual("dagger.ui.action.v1"u8)) continue;
            DaggerfallPlayerUiAction? action = DaggerfallUiAction.Parse(inputEvent.PayloadData.Span);
            // Death owns the session's input while the Host decides the resulting replacement.
            // Consume every payload here so held or stale ordinary actions cannot mutate state
            // while the death choices are visible.
            if (_mode == ProductMode.Dead)
            {
                // Art requests are presentation maintenance, so a reloaded death screen can still
                // recover its admitted image while gameplay and menu actions remain suppressed.
                if (action?.Action == "art-request") _hud.RequestArt();
                else HandleDeathAction(action);
                continue;
            }
            switch (action?.Action)
            {
                // The entry screen's own action is the product's to answer, so the session knows the
                // shape and does nothing with it; the product has already left the mode by the time an
                // action in ordinary play could arrive.
                case "begin": break;
                case "cinematic-skip": _openingCinematics.Skip(); break;
                case "controls-rebind":
                case "controls-reset": ChangeControls(action!); break;
                case "character-begin":
                case "character-update":
                case "character-background-reroll":
                case "character-commit":
                case "character-cancel": ChangeCharacter(action!); break;
                case "character-level-allocate":
                case "character-level-commit": if (playing) ChangeLevelUp(action!); break;
                case "activation-mode": if (playing) ApplyActivationMode(action!); break;
                case "dialogue-tone":
                case "dialogue-topic":
                case "dialogue-close": if (playing || modal) _ = ApplyDialogueAction(action!); break;
                case "transport-select":
                case "transport-toggle":
                case "transport-leave-ship": if (playing) ChangeTransport(action!); break;
                case "travel-search":
                case "travel-preview": if (playing || modal) ChangeTravel(action!); break;
                case "rest": if (playing && !restSubmitted) { ChangeRest(action!); restSubmitted = true; } break;
                case "wagon-put":
                case "wagon-take": if (playing) ChangeWagon(action!); break;
                case "quest-choice":
                    if ((playing || modal) && State.Quests.ChoosePrompt(State.Variables, action!.QuestInstance!, action.QuestMessage!.Value, action.QuestPrompt!, action.QuestChoice!.Value))
                        Presentation.SetOutcome("Quest choice recorded.");
                    else Presentation.SetOutcome("Quest choice rejected: this prompt is no longer pending or the choice is invalid.");
                    break;
                case "dungeon-text-answer":
                case "dungeon-text-close": if (playing || modal) ApplyDungeonTextInput(action!); break;
                case "attack": if (playing && !opensInteraction) firstStep.Request(DaggerfallInput.Attack); break;
                // A reloaded DOM holds no art and asks for the revision it is missing; the projection
                // answers on its next snapshot rather than a second delivery channel existing.
                case "art-request": _hud.RequestArt(); break;
                case "inventory": break;
                case "inventory-move": if (playing || modal) _inventoryUi.Move(action!); break;
                case "inventory-inspect": if (playing || modal) _inventoryUi.Inspect(action!); break;
                case "inventory-use": if (playing || modal) _inventoryUi.Use(action!); break;
                case "notebook-page": if (playing || modal) ApplyNotebookAction(action!); break;
                case "notebook-add": if (playing || modal) ApplyNotebookAction(action!); break;
                case "notebook-edit": if (playing || modal) ApplyNotebookAction(action!); break;
                case "notebook-remove": if (playing || modal) ApplyNotebookAction(action!); break;
                case "notebook-move": if (playing || modal) ApplyNotebookAction(action!); break;
                case "inventory-drop": if (playing || modal) _inventoryUi.Drop(action!); break;
                case "currency-deposit-gold": if (playing || modal) ChangeCurrency(action!); break;
                case "currency-withdraw-gold": if (playing || modal) ChangeCurrency(action!); break;
                case "currency-deposit-letters": if (playing || modal) ChangeCurrency(action!); break;
                case "currency-withdraw-letter": if (playing || modal) ChangeCurrency(action!); break;
                case "bank-transfer": if (playing || modal) ChangeCurrency(action!); break;
                case "bank-loan-issue":
                case "bank-loan-repay-account":
                case "bank-loan-repay-carried": if (playing || modal) ChangeLoan(action!); break;
                case "character": break;
                case "loot": if (playing) firstStep.Request(DaggerfallInput.Interact); break;
                case "loot-close": if (playing || modal) _lootUi.Close(action!.Container); break;
                // Bare quick-save creates a named slot; quick-load selects slot-1. Both use
                // the same catalog owner as the selectable DOM controls.
                case "save-game": if (playing || modal) _saveSlotRequest = new(SaveSlotOperation.Save, Label: "Saved game"); break;
                case "load-game": if (playing || modal) _saveSlotRequest = new(SaveSlotOperation.Load, "slot-1"); break;
                case "save-slots": if (playing || modal) _saveSlotRequest = new(SaveSlotOperation.List); break;
                case "save-slot": if (playing || modal) _saveSlotRequest = new(SaveSlotOperation.Save, action!.Key, action.Label, action.Confirm); break;
                case "load-slot": if (playing || modal) _saveSlotRequest = new(SaveSlotOperation.Load, action!.Key); break;
                case "delete-slot": if (playing || modal) _saveSlotRequest = new(SaveSlotOperation.Delete, action!.Key, Confirm: action.Confirm); break;
                case "loot-take":
                    if (playing || modal)
                    {
                        if (_lootUi.PrepareGroundTake(action!) is { } groundTake)
                        {
                            if (!State.Encumbrance.CanCarry(_definitions.RequireItem(new DaggerfallItemId(groundTake.Definition)), groundTake.Quantity))
                            {
                                _lootUi.CompleteGround(false, "You cannot carry any more.");
                                Presentation.SetOutcome(_lootUi.Message);
                                break;
                            }
                            try
                            {
                                _groundContainers.Take(groundTake.Id, groundTake.Selection, groundTake.ExpectedWorldRevision);
                                _lootUi.CompleteGround(true);
                            }
                            catch (Exception rejection) when (rejection is InvalidOperationException or ArgumentException)
                            {
                                _lootUi.CompleteGround(false, rejection.Message);
                            }
                            Presentation.SetOutcome(_lootUi.Message);
                            break;
                        }
                        // A valid take applies at once: the transfer commits, its completed-change
                        // facts deliver at the boundary below even while the modal holds the world,
                        // and the published presentation already reflects the result.
                        if (_lootUi.PrepareTake(action!, State.PlayerControl, _input.ResolveCurrentLook(State.PlayerControl)) is { } take)
                        {
                            CorpseLootCommitResult result = _corpseLoot.TryCommitLoot(take, _facts);
                            _lootUi.Complete(result);
                            Presentation.SetOutcome(_lootUi.Message);
                        }
                    }
                    break;
                default: Presentation.SetOutcome("Unrecognized player UI action."); break;
            }
        }

        if (restSubmitted)
        {
            // Explicit elapsed time already passed through the one calendar and its consumers.
            // The same input slice must not also apply an ordinary realtime step or attack.
            DeliverFacts();
            PublishPresentation();
            return;
        }

        if (!playing)
        {
            Presentation.SetOutcome(ModalMessage());
            // Reactions to modal-own actions (a loot take above) deliver here rather than waiting
            // for a playing step: the batch is stable and reentrant appends wait for the next one.
            DeliverFacts();
            PublishPresentation();
            return;
        }

        // The message line ages with the world it reports on, so it is advanced here rather than
        // while a mode holds the world still.
        Presentation.Advance(deltaSeconds * facts.AdmittedStepCount);

        // The worn set is recomputed before the round advances, so a payload that ticks with the clock
        // reads the body the player is wearing now rather than the one the previous update saw.
        State.HeldEnchantments.Refresh();

        // Ordering within this one admitted update is clock, magic rounds, then calendar consumers
        // and simulation.  A normal game minute is one magic round; a larger admitted interval uses
        // the same lifecycle catch-up path as rest, travel, and prison, so no second effect timer can
        // drift from the saved calendar.
        DaggerfallCalendar calendarBefore = _time.Calendar;
        long minuteBefore = MinuteIndex(calendarBefore);
        _time.Advance(deltaSeconds * facts.AdmittedStepCount);
        State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
        _siteProjection.Lighting.UpdateAmbient(_time.Calendar);
        State.Quests.AdvanceClocks(State.Variables, calendarBefore, _time.Calendar);
        State.Social.AdvanceElapsedMinutes(minuteBefore, MinuteIndex(_time.Calendar));
        AdvanceLoans();
        AdvanceEffectsForCalendar(calendarBefore, ordinaryPlay: true);
        AnnounceHoliday();
        AgePanelRequest(deltaSeconds * facts.AdmittedStepCount);

        // One admitted update owns one input slice. Later catch-up steps derive
        // only committed held keyboard/mapped-direction intent; direct axes,
        // direct digital movement, pointer deltas, and semantic actions do not replay.
        // Simulation and reactions run per step; the final publication below (and the outer
        // update's, after animation impacts) happens once, not once per step.
        SimulateStep(firstStep, facts.Generation, facts.SimulationStep);
        DeliverFacts();
        for (uint step = 1; step < facts.AdmittedStepCount; step++)
        {
            SimulateStep(new ProductUpdateState(deltaSeconds), facts.Generation, checked(facts.SimulationStep + step));
            DeliverFacts();
        }
        _locomotion.AdvanceCalendarMinutes(minuteBefore, MinuteIndex(_time.Calendar), State.Actors.Player.Stats);
    }

    /// <summary>
    /// The status rows owners outside this session publish: effects, escorts and quests put what
    /// they want shown here, and the projection carries it without knowing what it means.
    /// </summary>
    internal PresentationSlots Slots { get; } = new();

    /// <summary>Whether this admitted slice consumes attack/use as a contextual interaction or mode change.</summary>
    private static bool ContainsInteractionAction(ReadOnlySpan<ProductInputEvent> input)
    {
        foreach (ProductInputEvent inputEvent in input)
        {
            if (inputEvent.ValueKind != InputValueKind.ProductPayload
                || !inputEvent.PayloadContract.Span.SequenceEqual("dagger.ui.action.v1"u8)) continue;
            if (DaggerfallUiAction.Parse(inputEvent.PayloadData.Span)?.Action is "loot" or "activation-mode" or "dialogue-topic" or "rest") return true;
        }

        return false;
    }

    /// <summary>The input system's held state, readable so the mode request and tests agree on it.</summary>
    internal ProductMode Mode => _mode;

    /// <summary>
    /// The mode this session asks the product for. Death outranks everything and only a session
    /// replacement leaves it; a modal exists exactly while a loot container is open, so the
    /// request follows that container rather than the key that opened it.
    /// </summary>
    public ProductMode? PendingModeRequest
    {
        get
        {
            // The Kit actor state already owns the question of whether the player is defeated.
            if (State.Actors.Player.IsDefeated) return ProductMode.Dead;
            if (_mode == ProductMode.Dead) return null;
            // The menu pair is what this asks about: a mode the product holds on its own, such as the
            // entry screen, has no loot container to follow, so this asks for nothing rather than
            // dragging the world into ordinary play behind the product's back.
            if (_mode is not (ProductMode.Playing or ProductMode.Modal)) return null;
            bool open = _lootUi.Read() is not null || _dungeonTextProjection is not null;
            return open == (_mode == ProductMode.Modal) ? null : open ? ProductMode.Modal : ProductMode.Playing;
        }
    }

    /// <summary>
    /// Whether the pending request closes the owned loot interaction. Ordinary play is only ever
    /// requested when an open container just closed — the getter above returns Playing exactly in
    /// that case — so a Playing request is a close by construction rather than a second opinion
    /// about the mode. Death, modal-open, and silence carry no close.
    /// </summary>
    public bool PendingModeRequestClosesModal => PendingModeRequest == ProductMode.Playing;

    private SaveSlotRequest? _saveSlotRequest;
    private IReadOnlyList<SaveSlotSummary> _saveSlots = [];
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
        _input.Neutralize();
        _locomotion.Neutralize();
        ApplyDeathPresentationMode(mode);
        Presentation.SetOutcome(ModalMessage());
        PublishPresentation();
    }

    /// <summary>What the outcome line says while a mode other than ordinary play holds the world.</summary>
    private string ModalMessage() => _mode switch
    {
        ProductMode.Modal => _dungeonTextProjection is not null ? "Dungeon text open."
            : _lootUi.Read() is not null ? _lootUi.Message : "Interaction open.",
        ProductMode.Dead => "You have died.",
        ProductMode.Paused => "Paused.",
        // The entry screen says its own thing; a status line would compete with the screen that is up.
        ProductMode.Title => string.Empty,
        _ => string.Empty,
    };

    internal void Update(ProductUpdateState update)
    {
        SimulateStep(update, 0, 0);
        DeliverFacts();
        PublishPresentation();
        AdvanceMusic();
    }

    /// <summary>
    /// Advances a rest, travel, prison, or other ruleset-owned elapsed interval through the session's
    /// single calendar.  A caller resumes <see cref="DaggerfallCalendarAdvance.RemainingSeconds"/>
    /// after handling a consequence; only the portion the calendar accepted advances effects.
    /// </summary>
    internal DaggerfallCalendarAdvance AdvanceElapsedTime(long gameSeconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)>? consequences = null,
        DaggerfallEncounterRequest? encounter = null,
        bool deferSkillAdvancement = false)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        long minuteBefore = MinuteIndex(calendarBefore);
        DaggerfallCalendarAdvance advance = _time.AdvanceInterval(gameSeconds, consequences ?? []);
        State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
        _siteProjection.Lighting.UpdateAmbient(_time.Calendar);
        State.Quests.AdvanceClocks(State.Variables, calendarBefore, _time.Calendar);
        // Daily conditions and ordinary source-order operations observe the same admitted calendar
        // after rest, travel, prison, or another interval, including an interval with no clock expiry.
        State.Quests.Advance(State.Variables, _time.Calendar);
        if (!deferSkillAdvancement)
        {
            State.SkillUses.RaiseSkills(_time.Calendar.ToAbsoluteSeconds());
            State.LevelUps.BeginIfEligible();
        }
        State.Social.AdvanceElapsedMinutes(minuteBefore, MinuteIndex(_time.Calendar));
        AdvanceLoans();
        AdvanceEffectsForCalendar(calendarBefore, ordinaryPlay: false);
        if (advance.AppliedSeconds > 0 && encounter is not null) QueueEncounter(encounter);
        AnnounceHoliday();
        return advance;
    }

    /// <summary>Applies a quest-owned training interval through the existing calendar without recursively re-running quest tasks.</summary>
    private void AdvanceQuestTraining(long gameSeconds)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        long minuteBefore = MinuteIndex(calendarBefore);
        _ = _time.AdvanceInterval(gameSeconds, []);
        State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
        _siteProjection.Lighting.UpdateAmbient(_time.Calendar);
        State.Quests.AdvanceClocks(State.Variables, calendarBefore, _time.Calendar);
        State.SkillUses.RaiseSkills(_time.Calendar.ToAbsoluteSeconds());
        State.LevelUps.BeginIfEligible();
        State.Social.AdvanceElapsedMinutes(minuteBefore, MinuteIndex(_time.Calendar));
        AdvanceLoans();
        AdvanceEffectsForCalendar(calendarBefore, ordinaryPlay: false);
        AnnounceHoliday();
    }

    /// <summary>
    /// Resolves one rest/travel/time encounter at the player pose. The result remains durable but
    /// unmaterialized until the next admitted simulation step, which makes a save between the two
    /// operations restore the selected source result rather than draw again.
    /// </summary>
    internal DaggerfallEncounterResolution QueueEncounter(DaggerfallEncounterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorldPoint pose = State.PlayerControl.Position
            ?? throw new InvalidOperationException("Daggerfall cannot select an encounter without a player position.");
        ulong generation = _latestUpdateGeneration is > 0 ? _latestUpdateGeneration.Value : 1UL;
        return _encounters.Select(request, generation, _activeProfileKey.LogicalId, new ActorPose(pose, State.PlayerControl.YawRadians));
    }

    /// <summary>
    /// Draws one poison bound through the Engine's keyed service, so a replay inside a session draws the
    /// same values rather than whatever the process clock says.
    /// </summary>
    private int PoisonRoll(int minimum, int maximum) => checked((int)_random.DrawKeyed(new KeyedRngRequest(
        DaggerfallPoisonRandomKey.Seed,
        DaggerfallPoisonRandomKey.Scope,
        DaggerfallPoisonRandomKey.For(DaggerfallActorIdentity.PlayerEntityId, "admission", ++_poisonDraws, DaggerfallPoisonRandomKey.MinuteScope),
        minimum,
        maximum)).Value);

    private void AdvanceEffectsForCalendar(DaggerfallCalendar before, bool ordinaryPlay)
    {
        long minuteBefore = MinuteIndex(before);
        long minutes = MinuteIndex(_time.Calendar) - minuteBefore;
        if (minutes <= 0) return;

        // The normal path is expressed as its normal one-round operation.  Multiple minutes (whether
        // an unusually long admitted update or an elapsed interval) retain the donor's bounded
        // catch-up policy inside the lifecycle.
        if (ordinaryPlay && minutes == 1)
        {
            State.Effects.AdvanceOrdinaryRound();
            State.HeldEnchantments.AdvanceRounds(1);
            return;
        }

        _ = State.Effects.AdvanceElapsedRounds(minutes);
        State.HeldEnchantments.AdvanceRounds(checked((int)Math.Min(minutes, int.MaxValue)));
    }

    private static long MinuteIndex(DaggerfallCalendar calendar) =>
        (calendar.DayNumber * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)
        + (calendar.Hour * DaggerfallCalendar.MinutesPerHour)
        + calendar.Minute;


    /// <summary>
    /// One simulation step: input, world time, and reactions. Publication is the caller's:
    /// the admitted update publishes once after all its steps, and direct callers publish
    /// with the step.
    /// </summary>
    private void SimulateStep(ProductUpdateState update, ulong generation, ulong simulationStep)
    {
        _latestUpdateGeneration = generation;
        _latestSimulationStep = simulationStep;
        if (State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? actionGraph))
            actionGraph.Advance(update.DeltaSeconds);
        State.Kit.AttackExecution.ObserveTimeline(generation, simulationStep);
        _input.Apply(State.PlayerControl, update);
        // The Engine still receives an ordinary character step (grounding and gravity remain its
        // responsibility), but classic over-capacity removes planar intent before that proposal.
        bool alive = State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0d;
        if (!State.Encumbrance.Read().CanMove || !alive) update.PlanarIntent = Vector2.Zero;
        bool canMove = State.Encumbrance.Read().CanMove && alive;
        State.Transport.Reconcile(State.Inventory.Read(), new DaggerfallTransportAccessContext(
            IsIndoor: _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior,
            IsDungeon: _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon));
        DaggerfallLocomotionStep locomotion = _locomotion.BeginStep([.. update.Inputs], update.DeltaSeconds,
            State.Actors.Player.Stats, canMove, State.Transport);
        CharacterMotion motionBefore = State.PlayerControl.Motion;
        WorldPoint? positionBefore = State.PlayerControl.Position;
        _doors.Advance(update.DeltaSeconds);
        _siteProjection.AdvanceMotion(update.DeltaSeconds);
        CharacterStepEnvironment doorEnvironment = _siteProjection.CharacterEnvironment(State.PlayerControl.Motion);
        bool wallAhead = _spatial.TryProbeClimbWall(State.PlayerControl, CharacterWallProbeDirection.Forward, out SpatialHit forwardHit, doorEnvironment)
            && MathF.Abs(forwardHit.Normal.Y) <= .06f;
        bool wallAtFeet = _climbing.IsAttached
            && _spatial.TryProbeClimbWallAtFeet(State.PlayerControl, CharacterWallProbeDirection.Forward, out SpatialHit footHit, doorEnvironment)
            && MathF.Abs(footHit.Normal.Y) <= .06f;
        bool wallBehind = !motionBefore.Grounded && locomotion.ForwardIntent < 0f
            && _spatial.TryProbeClimbWall(State.PlayerControl, CharacterWallProbeDirection.Backward, out SpatialHit rearHit, doorEnvironment)
            && MathF.Abs(rearHit.Normal.Y) <= .06f;
        DaggerfallClimbStep climb = _climbing.BeginStep(locomotion, motionBefore, wallAhead, wallAtFeet, wallBehind,
            canMove && State.Transport.IsOnFoot,
            State.Actors.Player.Stats, State.Character.Race.Id == "khajiit",
            State.Effects.EnhancesClimbing(DaggerfallActorIdentity.PlayerEntityId),
            update.DeltaSeconds, () => checked((int)_random.DrawKeyed(new KeyedRngRequest(
                CombatRandomKey.Seed, "daggerfall.climbing.v1", $"generation:{generation}:step:{simulationStep}", 1, 100)).Value));
        DaggerfallLevitationStep levitation = _levitation.Resolve(new DaggerfallLevitationContext(
            State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId),
            Swimming: false,
            Climbing: climb.Climbing,
            CanMove: canMove,
            UpHeld: locomotion.UpHeld,
            DownHeld: locomotion.DownHeld,
            VerticalSpeed: _tuning.Locomotion.LevitationVerticalSpeed));
        locomotion = locomotion with
        {
            Controls = locomotion.Controls with
            {
                VerticalVelocity = climb.VerticalVelocity ?? levitation.VerticalVelocity,
                CrouchRequested = !levitation.IsLevitating && locomotion.Controls.CrouchRequested,
            },
            Climbing = climb.Climbing,
            Running = !levitation.IsLevitating && locomotion.Running,
            JumpRequested = !levitation.IsLevitating && locomotion.JumpRequested,
        };
        bool releasedVerticalDrive = _verticalMovementDriven && !locomotion.Controls.VerticalVelocity.HasValue;
        CharacterStepReceipt? movement = _spatial.Step(State.PlayerControl, update, doorEnvironment, locomotion.Controls);
        if (movement is not null) _verticalMovementDriven = locomotion.Controls.VerticalVelocity.HasValue;
        if (movement is not null && _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior)
            UpdateExteriorResidency();
        if (movement is not null && actionGraph is not null)
            _ = ReportDungeonActions(_actionTriggers.Reconcile(actionGraph, State.PlayerControl,
                State.Actors.Player.Actor.Entity, simulationStep));
        if (movement is not null && State.DungeonDiscoveries.TryGetValue(_activeProfileKey, out DaggerfallDungeonDiscovery? discovery))
            _dungeonVisibility.Observe(discovery, State.PlayerControl, _doors, doorEnvironment, simulationStep, _tuning.Camera.EyeHeight);
        CharacterMotion landingBefore = releasedVerticalDrive && positionBefore is WorldPoint releasePosition
            ? motionBefore with { PeakY = releasePosition.Y, FallOriginY = releasePosition.Y }
            : motionBefore;
        DaggerfallLanding? landing = _locomotion.CompleteStep(locomotion, landingBefore, movement, update.DeltaSeconds * _tuning.Time.GameSecondsPerRealSecond, State.Actors.Player.Stats, use => State.SkillUses.Record(use));
        _climbing.CompleteStep(climb, movement, use => State.SkillUses.Record(use));
        if (_vitality.ResolveLanding(State.Actors.Player.Actor, landing, State.Effects.PreventsFallDamage(DaggerfallActorIdentity.PlayerEntityId)) is { } fall)
            AppendDamage(fall, DaggerfallDamageCause.Fall, 0);
        _camera.Update(State.PlayerControl);
        if (!alive || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d) return;
        _ = _encounters.MaterializePending(_activeProfileKey.LogicalId, (definition, pose, level) => SpawnActor(definition, pose, level));
        _enemyBehavior.Update(State.PlayerControl, generation, simulationStep, update.DeltaSeconds, _facts);
        // Enemy attack-start facts must reach presentation before the post-enemy actions below.
        // A hit marker is consumed by the outer admitted update after this simulation step; if
        // that swing is already in flight, keep input and progression behind its same boundary.
        // Immediate (no damage-frame) swings are resolved here so a lethal result also stops the
        // remainder of this step. DeliverFacts remains a stable-batch boundary; any facts emitted
        // by these reactions wait for the existing caller boundary below.
        DeliverFacts();
        ApplyAttackImpacts();
        // Enemy reactions resolve inside this admitted step. A lethal reaction owns the rest of
        // the step: do not recover stamina, toggle equipment, attack, activate a target, or advance
        // quests after the player has been defeated. DeliverFacts still runs at the caller boundary,
        // so the mode transition and death presentation are published from the committed outcome.
        if (State.Actors.Player.IsDefeated
            || State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0d)
            return;
        if (_appearance.HasPendingEnemyHitTarget(DaggerfallActorIdentity.PlayerEntityId)) return;
        LookReceipt currentLook = _input.ResolveCurrentLook(State.PlayerControl);
        // The swing gesture is measured from the look turns this admitted update committed, so the
        // attack below reads the gesture the player actually drew in the moments before it.
        _playerSwings.Observe(update.DeltaSeconds, State.PlayerControl.YawRadians, State.PlayerControl.PitchRadians);
        _staminaRecovery.Update(State.Actors.Player.Stats, update.DeltaSeconds);
        if (update.IsRequested(DaggerfallInput.ToggleWeapon)) _appearance.ToggleWeaponDrawn();
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        // Interaction owns this slice once requested. Direct semantic input can carry both intents
        // in the same Engine delivery, and it must follow the same no-attack rule as a DOM loot action.
        bool contextualInteraction = update.IsRequested(DaggerfallInput.Interact);
        if (!contextualInteraction && update.IsRequested(DaggerfallInput.Attack) && _appearance.CanStartPlayerAttack)
        {
            State.Kit.Attacks.TryPlayerMelee(State.PlayerControl, currentLook, generation, simulationStep, update.DeltaSeconds, _facts);
            // WeaponManager sends Attack to an action on the environment after the ordinary hit
            // query. The safe Engine hit reports a static surface rather than a source id, so the
            // normalized placement resolver above is the product-side identity join.
            _ = TryTriggerDungeonActionRay(currentLook.Forward, _tuning.MeleeTargeting.MaximumDistance, DaggerfallDungeonActionEvent.Attack);
        }
        if (contextualInteraction)
            _ = TryActivateContextual(currentLook);
        // A panel button asks the DOM for a panel during ordinary play, which is where the keyboard's
        // own I, C and Escape are heard. While a modal or a death holds the world the DOM already has
        // a panel in front of the player, so a request there would fight the mode rather than serve it.
        // Two panel buttons in one admitted slice ask in a fixed order and the last one stands, which
        // is what the DOM's own key handling does with two keys in one frame: one panel can open, so
        // the earlier press must not swallow the later one.
        if (update.IsRequested(DaggerfallInput.Inventory)) RequestPanel(DaggerfallPanel.Inventory);
        if (update.IsRequested(DaggerfallInput.Character)) RequestPanel(DaggerfallPanel.Character);
        if (update.IsRequested(DaggerfallInput.Menu)) RequestPanel(DaggerfallPanel.Menu);
        // Tasks consume the state committed by this admitted step. Clock actions mutate only the
        // quest clock state; elapsed duration is still consumed by the calendar owner above.
        State.Quests.Advance(State.Variables, _time.Calendar);
    }

    public bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input)
    {
        foreach (ProductInputEvent value in input)
            if (value.ValueKind == InputValueKind.ProductPayload && value.PayloadContract.Span.SequenceEqual("dagger.ui.action.v1"u8)
                && DaggerfallUiAction.Parse(value.PayloadData.Span)?.Action == DaggerfallUiAction.BeginAction) return true;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // DisposeAll walks backward: projection door entities must release before the actor store.
        Exception? failure = null;
        try { DisposeAll([_hud, _camera, _spatial, State.Actors, _siteProjection, State.Effects, _actionTriggers, .. Cinematics is null ? Array.Empty<IDisposable>() : new IDisposable[] { Cinematics }]); }
        catch (Exception exception) { failure = exception; }
        try { DisposeExteriorAppearance(); }
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

    private void AppendDamage(DamageResult result, DaggerfallDamageCause cause, int struckBody)
    {
        long source = checked((long)result.Source.Get<DurableEntityIdentity>().Identity.Value);
        long target = checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value);
        ulong generation = _latestUpdateGeneration ?? 1UL;
        ulong step = _latestSimulationStep ?? 1UL;
        _facts.Append(new DamageAppliedFact(source, target, cause,
            result.CalculatedDamage, result.ActualHealthLost, struckBody, generation, step));
        if (result.ActualHealthLost > 0)
            _facts.Append(new ActorDamagedFact(target, source, cause,
                result.CalculatedDamage, result.ActualHealthLost));
        if (result.Defeated)
            _facts.Append(new ActorDiedFact(target, source, cause,
                result.CalculatedDamage, result.ActualHealthLost, generation, step));
    }

    private void React(IProductFact fact)
    {
        _staminaRecovery.React(fact);
        if (fact is ActorDiedFact died)
        {
            _corpseLoot.Create(died);
            _rewards.React(died, _facts);
        }
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.React(fact, State.Actors);
        _outcomes.React(fact);
    }

    private void PublishPresentation()
    {
        _hud.Publish(State.Actors.Player, State.Progression, Presentation, _mode, State.PlayerControl, Slots,
            _inventoryUi.Read(), _lootUi.Read(), _characterUi.Read(State.Actors.Player, State.Progression),
            LatestPanelRequest, _saveSlots, _saveSlotDiagnostic, _controlSettings, _controlDiagnostic,
            ActivationView, State.Quests.ReadPresentation(QuestTextContext), _notebook.Read(),
            DaggerfallTransportProjection.Read(State.Transport, State.Inventory.Read(), TransportAccess(),
                ownsShip: false, wagon: State.Wagon), _dungeonTextProjection, _deathPresentation.View, RestView,
            ReadTravelPresentation());
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors, _groundContainers.All);
    }

    private DaggerfallQuestMessageContext QuestTextContext(DaggerfallQuestRuntimeInstance instance)
    {
        World.DaggerfallCalendar calendar = _time.Calendar;
        DaggerfallCharacterIdentity player = State.Character.Identity;
        Dictionary<string, DaggerfallQuestResourceTextContext> resources = [];
        foreach (DaggerfallQuestResourceState resource in instance.Resources)
        {
            DaggerfallQuestResourceDefinition? declared = _definitions.QuestSources.Resources
                .SingleOrDefault(value => value.SourceFile == instance.SourceFile
                    && value.CanonicalId == DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource"));
            if (declared is null) continue;
            string? place = resource.Binding.Places.Select(value => _site.TryFind(new DaggerfallSiteId(value.Region!.Value, value.Index!.Value), out var site) ? site.Name : null)
                .FirstOrDefault(value => value is not null);
            string? actorRole = resource.Binding.ActorIds.Select(id => State.Npcs.All.FirstOrDefault(npc => npc.DurableId == id)?.Role)
                .FirstOrDefault(value => value is not null);
            string? item = resource.Binding.UniqueItemIds.Select(id => State.ItemInstances.RequireUnique(id).ItemId)
                .FirstOrDefault(value => value is not null);
            string? named = declared.Person?.Named?.Replace('_', ' ');
            string? faction = declared.Person?.Faction?.Replace('_', ' ');
            string? group = declared.Person?.Group?.Replace('_', ' ');
            string? name = place ?? named ?? item ?? actorRole ?? group;
            resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = new(
                Name: name,
                NameTwo: place,
                NameThree: place,
                NameFour: place,
                Details: item ?? declared.SourceText,
                Binding: actorRole ?? place,
                Faction: faction);
        }
        return new(new(
            new DaggerfallTextPlayerContext(Name: player.Name, Race: player.RaceId),
            new DaggerfallTextCalendarContext(
                Date: $"{calendar.Month + 1}/{calendar.Day + 1}/{calendar.Year}",
                Time: $"{calendar.Hour:D2}:{calendar.Minute:D2}",
                DayNumber: (calendar.Day + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                MonthNumber: (calendar.Month + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Year: calendar.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Minute: calendar.Minute.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Hour: calendar.Hour.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Season: calendar.Season.ToString()),
            new DaggerfallTextLocationContext(City: _site.ActiveSite?.Name,
                Region: _site.Region?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(), new(), new()), resources);
    }

    /// <summary>
    /// The panel the player asked for through a device the DOM has no channel of its own for.
    /// </summary>
    /// <remarks>
    /// The keyboard reaches the panels because the DOM hears the keys itself; a pad reaches the
    /// product. The panels stay where they are — the DOM owns whether one is open — so a button that
    /// opens one travels as that panel's own menu action and the product invents no second notion of
    /// a panel. The revision is what lets the DOM apply each request once while the request stays
    /// published, the same way a published loot revision is recognised rather than replayed.
    /// </remarks>
    internal DaggerfallPanelRequest? LatestPanelRequest => _panelRequest is null ? null : new DaggerfallPanelRequest(_panelRequest, _panelRequestRevision);

    /// <summary>
    /// Publishes the holiday announcement when the calendar names a new one for the session's site.
    /// </summary>
    /// <remarks>
    /// The check runs on the first playing update and on every date change after it, which is how a
    /// session constructed or restored onto a holiday announces once, the way the donor announces on
    /// entering an eligible location and after loading: the tracking restarts unannounced, so the first observation
    /// of a kept holiday is itself the entry. A session standing at a dungeon, a graveyard, a coven or
    /// the player's ship never announces, and leaving a holiday clears the tracking silently rather than reporting the ordinary day.
    /// </remarks>
    private void AnnounceHoliday()
    {
        World.DaggerfallHolidayAnnouncement? announcement =
            World.DaggerfallHolidayAnnouncement.ForDate(_time.Calendar, _site.Region, _site.ActiveSite?.Kind);
        int holidayId = announcement?.HolidayId ?? 0;
        if (holidayId == _lastHolidayId)
        {
            return;
        }

        _lastHolidayId = holidayId;
        HolidayAnnouncement = announcement;
        if (announcement is not null)
        {
            Presentation.SetOutcome(_definitions.TextPresentation.Resolve(announcement.TextKey, DaggerfallTextContext.Empty).Text);
        }
    }

    private void RequestPanel(string panel)
    {
        _panelRequest = panel;
        _panelRequestRevision = checked(_panelRequestRevision + 1);
        _panelRequestRemainingSeconds = PanelRequestLifetimeSeconds;
    }

    /// <summary>
    /// Ages a standing panel request on the same admitted world time everything else ages on.
    /// </summary>
    /// <remarks>
    /// Opening a panel is an event, not state: the product does not own whether one is open, so a
    /// request that outlived the DOM that performed it would re-open a panel nobody asked for on the
    /// next page load — and a player holding only a pad has no way back out of it. The window is
    /// generous because the DOM acts on the next snapshot, and it is admitted world time because
    /// there is one clock here.
    /// </remarks>
    private void AgePanelRequest(double deltaSeconds)
    {
        if (_panelRequest is null) return;
        _panelRequestRemainingSeconds -= deltaSeconds;
        if (_panelRequestRemainingSeconds <= 0d) _panelRequest = null;
    }

    private void ApplyAttackImpacts()
    {
        IReadOnlyList<AttackImpactNotice> impacts = _appearance.TakeAttackImpacts();
        if (impacts.Count == 0) return;
        if (_latestUpdateGeneration is not ulong generation) return;
        State.Kit.AttackExecution.ApplyImpacts(impacts, generation, _facts);
        DeliverFacts();
    }

    /// <summary>Optional compiled ranged-flight owner; invoked once for every admitted realtime update.</summary>
    partial void UpdateRangedFlight(ProductUpdateFacts facts);

    internal void ResolveExplicitMelee(ExplicitMeleeRequest request)
    {
        _latestUpdateGeneration = request.Generation;
        _latestSimulationStep = request.SimulationStep;
        State.Kit.AttackExecution.ObserveTimeline(request.Generation, request.SimulationStep);
        _combat.ResolveExplicit(request, _facts);
        DeliverFacts();
        PublishPresentation();
    }

    internal TargetingEvidence? LastMeleeTargeting => State.Kit.Targeting.LastEvidence;

    private void DeliverFacts()
    {
        _facts.Deliver(React);
    }
    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEnemyBehavior => _enemyBehavior.LastEvidence;
    internal LootPresentation? OpenLoot => _lootUi.Read();

    /// <summary>Typed equipment moves over live state: the same operations the UI adapter uses.</summary>
    internal DaggerfallEquipmentMoves EquipmentMoves => _equipmentMoves;
    /// <summary>Typed mutation and disclosure of persisted item condition and magic meaning.</summary>
    internal DaggerfallItemConditionService ItemCondition => _itemCondition;
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


}

internal static class DaggerfallInput
{
    internal static readonly InputActionId ToggleWeapon = new("daggerfall.toggle-weapon");
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly InputActionId Interact = new("daggerfall.interact");
    internal static readonly InputActionId Inventory = new("daggerfall.inventory");
    internal static readonly InputActionId Character = new("daggerfall.character");
    internal static readonly InputActionId Menu = new("daggerfall.menu");
    internal static readonly PlayerControlBindings Controls = new(
        ["move"u8.ToArray(), "movement"u8.ToArray()],
        KeyboardControl.KeyW,
        KeyboardControl.KeyS,
        KeyboardControl.KeyA,
        KeyboardControl.KeyD,
        new DirectionalMovementBindings("move.forward"u8.ToArray(), "move.backward"u8.ToArray(), "move.left"u8.ToArray(), "move.right"u8.ToArray()));
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [
        new(Attack, "attack"u8.ToArray()),
        new(ToggleWeapon, "toggle-weapon"u8.ToArray()),
        new(Interact, "interact"u8.ToArray()),
        new(Inventory, "inventory"u8.ToArray()),
        new(Character, "character"u8.ToArray()),
        new(Menu, "menu"u8.ToArray()),
    ];

    /// <summary>
    /// The pad's action buttons in the layout the browser shell delivers: the bottom face attacks, the
    /// right face reaches for what the player is facing, the left face readies the weapon, the top
    /// face opens the character sheet, select opens the pack, and start opens the menu. They are
    /// product bindings rather than engine mappings because the Engine publishes positions and this is
    /// the layer that knows what an action means.
    /// </summary>
    internal static readonly IReadOnlyList<ControllerActionBinding> PadActions = [
        new(ControllerButton.Button0, Attack),
        new(ControllerButton.Button1, Interact),
        new(ControllerButton.Button2, ToggleWeapon),
        new(ControllerButton.Button3, Character),
        new(ControllerButton.Button8, Inventory),
        new(ControllerButton.Button9, Menu),
    ];
}
