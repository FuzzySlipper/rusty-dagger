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
internal sealed partial class DaggerfallSession : IPlaytestGameSession, ISaveableGameSession, IModeAwareGameSession, IEntryScreenSession, IEntryScreenStartupSession, ISaveRequestingGameSession, IPlayerPreferencesSession, IPlayerDefeatOutcomeSession,
    IDaggerfallSiteTransitionHost
{
    private readonly IRandomService _random;
    private readonly IEngineContext _engine;
    private readonly DaggerfallTuning _tuning;
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
    private DaggerfallHeldEnchantments _heldEnchantments = null!;
    private readonly DaggerfallActorRoster _roster;
    private readonly DaggerfallActorGrounding _grounding;
    private readonly DaggerfallDefinitions _definitions;
    private readonly ISpatialService _spatialService;
    private readonly FactBuffer<IProductFact> _facts = new();
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly DaggerfallEquipmentMoves _equipmentMoves;
    private readonly DaggerfallItemConditionService _itemCondition;
    private readonly DaggerfallInventoryPresentation _inventoryUi;
    private readonly DaggerfallLootPresentation _lootUi;
    private readonly DaggerfallCharacterPresentation _characterUi;
    /// <summary>The one owner of the active site projection, catalog, deltas and exterior window.</summary>
    // Assigned once the roster and persistence it composes exist; readers before that are lazy.
    private readonly DaggerfallSiteLifecycle _sites = null!;
    private DaggerfallWorldProfileKey _activeProfileKey => _sites.ActiveProfile;
    private DaggerfallSiteAppearance _appearance => _sites.Projection.Appearance;
    private DaggerfallDoorRuntime _doors => _sites.Projection.Doors;

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
    /// <summary>What is open over the world, which of it holds the world, and the pad's panel request.</summary>
    private readonly DaggerfallOpenInteractions _interactions;
    private bool _disposed;

    internal DaggerfallCinematicPresentation? Cinematics { get; }
    private readonly DaggerfallOpeningCinematics _openingCinematics;

    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallTuning tuning)
        : this(engine, definitions, inputs, tuning, null, null, null) { }

    /// <summary>Explicit compiled effect composition seam for ruleset families and save reconstruction tests.</summary>
    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs,
        DaggerfallTuning tuning, DaggerfallEffectCatalog effects)
        : this(engine, definitions, inputs, tuning, null, null, effects) { }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallTuning tuning, DaggerfallMusicBundle? music = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, null, null, null, null, true, null, null, null, music) { }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallTuning tuning, DaggerfallSiteAudioBundles audioBundles, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, null, null, audioBundles, cinematicContent, videosEnabled, questAdmission, null, disabledQuestSelection, music) { }

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallTuning tuning, RulesetSavePayload saved, IRandomService random)
        => Restore(engine, compositionIdentity, definitions, inputs, tuning, saved, random, (DaggerfallEffectCatalog?)null);

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallTuning tuning, RulesetSavePayload saved,
        IRandomService random, DaggerfallSiteAudioBundles audioBundles, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallSiteProfiles? profiles = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
        => Restore(engine, compositionIdentity, definitions, inputs, tuning, saved, random, null, audioBundles, cinematicContent, videosEnabled, questAdmission, profiles, disabledQuestSelection, music);

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallTuning tuning, RulesetSavePayload saved,
        IRandomService random, DaggerfallEffectCatalog? effects, DaggerfallSiteAudioBundles? audioBundles = null, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallSiteProfiles? profiles = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
    {
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        DaggerfallSiteProfile activeInputs = profiles is null
            ? inputs
            : raw.Site.ActiveProfile is { } profile
                ? profiles.Require(profile.Require())
                : profiles.RequireUniqueSite(ToSiteId(raw.Site.Active) ?? throw new ArgumentException("A restored Daggerfall session must name an active site.", nameof(saved)));
        DaggerfallSavePayload payload = raw.ResolveRestore(definitions, activeInputs, profiles);
        return new DaggerfallSession(engine, definitions, activeInputs, tuning, compositionIdentity, payload, effects, audioBundles, cinematicContent, videosEnabled, questAdmission, profiles, disabledQuestSelection, music);
    }

    private DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs,
        DaggerfallTuning tuning, ResolvedCompositionIdentity? compositionIdentity, DaggerfallSavePayload? saved,
        DaggerfallEffectCatalog? effects, DaggerfallSiteAudioBundles? audioBundles = null, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null, DaggerfallSiteProfiles? profiles = null, DaggerfallDisabledQuestSelection? disabledQuestSelection = null, DaggerfallMusicBundle? music = null)
    {
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _engine = engine;
            _tuning = tuning;
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
            DaggerActorAssembly assembled = DaggerActorFactory.Create(_random, definitions, inputs, saved, questAdmission, disabledQuestSelection);
            State = assembled.State;
            State.Social.SetBiographyReactionModifier(State.Character.Background?.Modifiers.Reaction ?? 0);
            ActorsState actors = State.Actors;
            partiallyConstructed.Add(actors);
            Dictionary<long, DaggerfallActorDefinition> authored = assembled.Definitions;
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
            _travelPolicy = new DaggerfallTravelPolicy(_site, definitions.Grids, tuning.Transport);
            DaggerfallWorldProfileKey activeProfile = saved?.Site.ActiveProfile?.Require() ?? inputs.ProfileKey;
            _controlEngine = engine;
            _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            _locomotion = new DaggerfallLocomotionPolicy(tuning.Locomotion, _controlSettings);
            _climbing = new DaggerfallClimbingPolicy(tuning.Climbing);
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            _dungeonVisibility = new DaggerfallDungeonVisibility(_spatial, tuning.Spatial.CollisionVoxelSize);
            _grounding = new DaggerfallActorGrounding(engine.Spatial, _spatial,
                tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);
            partiallyConstructed.Add(_spatial);
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteProfile admitted) in DaggerfallSiteLifecycle.ActionProfiles(inputs, profiles))
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
                DaggerfallSiteProfile admitted = snapshot.Profile == inputs.ProfileKey
                    ? inputs
                    : (profiles ?? throw new ArgumentException("Saved dungeon discovery requires admitted site profiles.", nameof(saved))).Require(snapshot.Profile);
                DaggerfallDungeonMapContent map = admitted.DungeonMap
                    ?? throw new ArgumentException($"Saved dungeon discovery names non-dungeon profile '{snapshot.Profile.LogicalId}'.", nameof(saved));
                State.DungeonDiscoveries.Add(snapshot.Profile, new DaggerfallDungeonDiscovery(snapshot.Profile, map, snapshot));
            }
            if (inputs.DungeonMap is { } initialMap && !State.DungeonDiscoveries.ContainsKey(activeProfile))
                State.DungeonDiscoveries.Add(activeProfile, new DaggerfallDungeonDiscovery(activeProfile, initialMap));
            // The selected site's normalized RDB doors restore their Engine pose/collider projection
            // before activation can query them and before the first character step consumes them.
            DaggerfallSiteProjection projection = DaggerfallSiteProjection.Create(engine, State.Actors.Entities, _random, tuning, _time.Calendar,
                inputs, audioBundles?.Require(inputs.ProfileKey), _spatial, saved?.Doors, saved?.DungeonMotion);
            partiallyConstructed.Add(projection);
            DaggerfallDungeonActionTriggerRuntime actionTriggers = new(
                State.Actors.Entities, engine.Spatial, _spatial, DaggerfallSiteLifecycle.ActionProfiles(inputs, profiles), activeProfile);
            partiallyConstructed.Add(actionTriggers);
            // Dynamic actors restore before the site projection, while authored placements are
            // already in ActorSprites.  Admit their mobile media here without replaying any item
            // creation or combat rolls.
            foreach (ActorState actor in actors.All.Where(actor => !inputs.ActorSprites.ContainsKey(actor.DurableId)))
            {
                if (!authored.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition) || definition.MobileId is not int mobileId) continue;
                if (!inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored dynamic actor '{definition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                projection.Appearance.AddActor(actor.DurableId, sprite);
            }
            if (saved is null)
            {
                foreach (ActorState actor in actors.All.Where(actor => authored[actor.DurableId].GroundOnSpawn))
                    _grounding.Ground(actor);
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, State.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            TargetingService targeting = new(engine.Perception, _spatial, State.Actors,
                new DaggerTargetingPolicy(authored, tuning.MeleeTargeting, () => _sites.Projection.Inputs));
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
                tuning.Progression);
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
            }

            _uniqueItems = DaggerfallUniqueItemAllocator.Sharing(_actorIdentities);
            State.Npcs.Identities = _actorIdentities;
            _heldEnchantments = new DaggerfallHeldEnchantments(State.Equipment, State.ItemInstances, definitions.Magic.MagicItems,
                State.Actors.Player.Stats, State.Actors.Entities, State.Actors.Player.Actor.Entity, () => _time.Calendar,
                () => State.PlayerControl.Position, () => DaggerfallActorRoster.NearbyCreatures(State.Actors, authored), InSunlight, _itemCondition, InHolyPlace,
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
            State.Property = new DaggerfallPropertyState(tuning.Property, saved?.Property);
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
            State.Transport = new DaggerfallTransportPolicy(tuning.Transport);
            State.Wagon = new DaggerfallWagonStorage(State.Containers, State.ItemInstances, definitions,
                playerEntity, _actorIdentities, tuning.Transport);
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
            _groundContainers = new DaggerfallGroundContainers(containers, State.ItemInstances, playerEntity, _actorIdentities, activeProfile);
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
            _interactions = new DaggerfallOpenInteractions(
                () => _lootUi.Read(),
                () => _lootUi.Message,
                dungeonTextOpen: () => _dungeonTextProjection is not null,
                dialogueOpen: () => _activationPresentation.View.Dialogue is not null,
                characterCreationOpen: () => State.Character.Pending is not null,
                levelUpOpen: () => State.LevelUps.Pending is not null,
                bankOpen: () => ActiveBankRegion() is not null);
            _persistence = new(State, _corpseLoot, _groundContainers, _notebook, _uniqueItems, _camera, _time, _site, State.Effects, () => _doors, _locomotion, _climbing, _dungeonText, CapturePropertyStorage);
            _roster = new DaggerfallActorRoster(State, definitions, _random, assembled.Mechanics, _actorIdentities, _uniqueItems,
                _authoredEntityIds, authored, saved?.DynamicActors ?? [], _grounding, () => _sites.Projection, _lootUi, _corpseLoot);
            _sites = new DaggerfallSiteLifecycle(engine, State, definitions, tuning, _time, _site, _spatial, _camera, audioBundles,
                _roster, _persistence, _groundContainers, _enemyBehavior, ExecuteDungeonFamilyAction, this,
                projection, actionTriggers, profiles, activeProfile, saved?.Site.ReturnProfile?.Require());
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
            if (saved is not null)
            {
                _sites.RestoreDeltas(saved.SiteDeltas);
                HashSet<string> admittedEncounterProfiles = profiles is null
                    ? [inputs.ProfileKey.LogicalId]
                    : [.. profiles.Keys.Select(profile => profile.LogicalId)];
                if (saved.Encounters.Resolved.Any(encounter => !admittedEncounterProfiles.Contains(encounter.ProfileId)))
                    throw new ArgumentException("Saved encounter references a world profile not admitted by the current bundle.", nameof(saved));
                _encounters.Restore(saved.Encounters, DynamicActorDefinitions(), TombstonedActorIds(saved));
                _persistence.Restore(saved);
                // Inventory/equipment materialize during persistence restore, after the initial held
                // refresh. Rebind the now-worn sources before any elapsed time can advance them.
                _heldEnchantments.Refresh();
                _appearance.SyncRestoredDefeat(State.Actors);
                RestoreDungeonText(saved.DungeonText);
                actionTriggers.RebaseRestoredPlayer(State.PlayerControl, playerEntity);
            }
            _sites.AdmitInitialExterior(saved?.ExteriorResidency);
        }
        catch (Exception constructionFailure)
        {
            List<Exception> failures = [constructionFailure];
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            try { _sites?.RetireExteriorAppearance(); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    /// <summary>The selected site's one authoritative RDB door owner for activation, spells, and dungeon actions.</summary>
    internal DaggerfallDoorRuntime Doors => _doors;

    /// <summary>Admits the full selected site catalog once composition has constructed this session.</summary>
    internal void AdmitSiteProfiles(DaggerfallSiteProfiles profiles) => _sites.AdmitProfiles(profiles);

    /// <summary>The session's site lifecycle: projection, catalog, deltas and exterior window.</summary>
    internal DaggerfallSiteLifecycle Sites => _sites;

    /// <summary>Relocates the existing player (or a live actor) to an admitted named anchor.</summary>
    internal bool TryRelocate(DaggerfallRelocationDestination destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryRelocate(destination);
    }

    /// <summary>Attempts one real site transition; failed destination admission leaves the source projection live.</summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryTransitionTo(destination);
    }

    void IDaggerfallSiteTransitionHost.RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians) =>
        ApplyRelocation(position, yawRadians, pitchRadians);

    void IDaggerfallSiteTransitionHost.RebuildActivation() => InitializeActivation(_engine, _tuning.LootInteraction);

    void IDaggerfallSiteTransitionHost.EnteredSite() => ChangeMusicSite();

    /// <summary>
    /// A swing the departing projection was still timing ends with it: retire and hand its
    /// impact back to the shared state here, inside the generation that admitted it, so the
    /// player is not left charged against an animation that no longer exists.
    /// </summary>
    void IDaggerfallSiteTransitionHost.RetireDepartingProjection(DaggerfallSiteProjection source)
    {
        RetireDepartingSwing(source);
        _combat.ClearRangedFlight();
        CancelDungeonTextOnUnload();
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
            ? _sites.Profiles?.Require(_activeProfileKey).InteriorBuilding : null);

    internal static bool IsHolyPlace(DaggerfallWorldProfileKind kind, DaggerfallInteriorBuilding? building) =>
        kind == DaggerfallWorldProfileKind.Interior
        && building is { BuildingType: 14 } or { FactionId: DaggerfallConcreteGuildCatalog.FightersTrainerFactionId };

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
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> DefinitionsByActor => _roster.Definitions;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> DynamicActors => _roster.Dynamic;

    private IReadOnlyDictionary<long, string> DynamicActorDefinitions()
    {
        Dictionary<long, string> values = _roster.Dynamic.ToDictionary(entry => entry.Key, entry => entry.Value.Value);
        foreach (DaggerfallSiteRuntimeDelta delta in _sites.Deltas.Values)
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

    /// <summary>Registers one actor from a published definition beyond the authored placements.</summary>
    internal long SpawnActor(string definitionId, ActorPose pose, int? level = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _roster.Spawn(definitionId, pose, level);
    }

    /// <summary>Retires one spawned actor through the roster's lifetime policy.</summary>
    internal void RetireActor(long durableId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _roster.Retire(durableId);
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
        // A modal equipment action can be saved before another playing step. Capture the worn set
        // that actually owns the items, rather than a prior frame's held stat sources.
        _heldEnchantments.Refresh();
        return _persistence.Capture(_latestUpdateGeneration, _latestSimulationStep, _roster.Dynamic, _encounters,
            _sites.Deltas, _activeProfileKey, _sites.ReturnProfile, State.DungeonDiscoveries, State.DungeonActions,
            _sites.Projection.CaptureMotion(), _sites.CaptureExteriorResidency());
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();

    private static World.DaggerfallSiteReturnPose? ToSiteReturnPose(DaggerfallSiteReturnPoseSave? pose) => pose is null
        ? null
        : new World.DaggerfallSiteReturnPose(new WorldPoint(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians);

    public ProductUpdateResult Update(ProductUpdate update)
    {
        // An exception that escapes here faults the lifecycle; the Engine reports it with its full
        // text and stack, and a resume continues this same session, so nothing is torn down on the way
        // out. Session resources are released by Dispose.
        _appearance.BeginAdmittedUpdate();
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
        DaggerfallUiPhases phase = _mode == ProductMode.Dead ? DaggerfallUiPhases.Dead
            : playing ? DaggerfallUiPhases.Playing
            : modal ? DaggerfallUiPhases.Modal
            : DaggerfallUiPhases.Held;
        DaggerfallUiInput ui = TakeUiInput(input);
        // The slice that opens an interaction admits no attack. Whichever order the Engine delivers
        // the keys in, swinging on the frame a container opens is an unintended attack.
        bool opensInteraction = playing && OpensInteraction(ui);
        bool restSubmitted = false;
        for (int index = 0; index < input.Length; index++)
        {
            firstStep.Add(input[index]);
            if (ui.IsUiAction(index))
                AdmitUiAction(ui.ActionAt(index), phase, firstStep, opensInteraction, ref restSubmitted);
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
            // The mode's own line was set when the mode was entered; an outcome an action set during
            // this update stays on the line rather than being overwritten by it.
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

        // A standing panel request ages on the same admitted world time as everything else.
        _interactions.AgePanelRequest(deltaSeconds * facts.AdmittedStepCount);

        // Ordering within this one admitted update is clock, calendar consumers (magic rounds among
        // them), simulation, then locomotion minutes. A normal game minute is one magic round; a
        // larger admitted interval uses the same lifecycle catch-up path as rest, travel, and prison,
        // so no second effect timer can drift from the saved calendar.
        DaggerfallCalendar calendarBefore = _time.Calendar;
        _time.Advance(deltaSeconds * facts.AdmittedStepCount);
        AdvanceCalendar(calendarBefore, DaggerfallCalendarAdvanceKind.OrdinaryPlay, simulate: () =>
        {
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
        });
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
        Presentation.SetOutcome(_interactions.ModeMessage(_mode));
        PublishPresentation();
    }

    internal void Update(ProductUpdateState update)
    {
        SimulateStep(update, 0, 0);
        DeliverFacts();
        PublishPresentation();
        AdvanceMusic();
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
        _sites.Projection.AdvanceMotion(update.DeltaSeconds);
        CharacterStepEnvironment doorEnvironment = _sites.Projection.CharacterEnvironment(State.PlayerControl.Motion);
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
            // No movement owner reports water yet, so the player is never swimming here.
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
            _sites.UpdateExteriorResidency();
        if (movement is not null && actionGraph is not null)
            _ = ReportDungeonActions(_sites.ActionTriggers.Reconcile(actionGraph, State.PlayerControl,
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // DisposeAll walks backward: projection door entities must release before the actor store.
        Exception? failure = null;
        try { DisposeAll([_hud, _camera, _spatial, State.Actors, _sites.Projection, State.Effects, _sites.ActionTriggers, .. Cinematics is null ? Array.Empty<IDisposable>() : new IDisposable[] { Cinematics }]); }
        catch (Exception exception) { failure = exception; }
        try { _sites.RetireExteriorAppearance(); }
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
        ObservePlaytestCombatFact(fact);
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
                ownsShip: State.Property.OwnsShip, wagon: State.Wagon), _dungeonTextProjection, _deathPresentation.View, RestView,
            ReadTravelPresentation(), Site.ActiveSite?.Name);
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors, _groundContainers.All,
            _latestUpdateGeneration is ulong generation && _latestSimulationStep is ulong simulationStep
                ? _combat.ReadRangedFlights(generation, simulationStep) : [],
            _tuning.Camera.EyeHeight);
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

    /// <summary>The panel the player asked for through a device the DOM has no channel of its own for.</summary>
    internal DaggerfallPanelRequest? LatestPanelRequest => _interactions.LatestPanelRequest;

    /// <summary>What is open over the world and which of it holds the world.</summary>
    internal DaggerfallOpenInteractions Interactions => _interactions;

    private void RequestPanel(string panel) => _interactions.RequestPanel(panel);

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
