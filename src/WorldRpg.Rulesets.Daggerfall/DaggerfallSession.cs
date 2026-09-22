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
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
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
internal sealed partial class DaggerfallSession : ISaveableGameSession, IModeAwareGameSession, IEntryScreenSession, IEntryScreenStartupSession, ISaveRequestingGameSession, IPlayerPreferencesSession
{

    /// <summary>Admitted world seconds a panel request stands before the DOM is assumed not to need it.</summary>
    private const double PanelRequestLifetimeSeconds = 1d;
    private readonly IRandomService _random;
    private PlayerInputSystem _input;
    private ProductMode _mode = ProductMode.Playing;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerCombatRules _combat;
    private readonly DaggerfallStaminaRecoveryModule _staminaRecovery;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly DaggerSessionPersistence _persistence;
    private readonly HashSet<ulong> _authoredEntityIds;
    /// <summary>
    /// Dynamic actors share one numeric base with generated unique items; durable kinds keep
    /// them distinct, the way a corpse container already shares its actor's number under a
    /// different kind.
    /// </summary>
    internal const ulong DynamicActorFirstIdentity = 1_000_000_000_000UL;
    private readonly DurableIdentityAllocator _actorIdentities;
    private readonly Dictionary<long, DaggerfallActorDefinition> _definitionsByActor;
    private readonly Dictionary<long, DaggerfallActorId> _dynamicActors = [];
    private readonly DaggerfallDefinitions _definitions;
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
    private readonly PrivateersHoldAppearance _appearance;
    private readonly DaggerfallDoorRuntime _doors;

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

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning)
        : this(engine, definitions, inputs, tuning, compositionIdentity, null, null) { }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, DaggerfallAudioBundle audioBundle, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, null, null, audioBundle, cinematicContent, videosEnabled, questAdmission) { }

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, RulesetSavePayload saved, IRandomService random)
        => Restore(engine, compositionIdentity, definitions, inputs, tuning, saved, random, (DaggerfallEffectCatalog?)null);

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, RulesetSavePayload saved,
        IRandomService random, DaggerfallAudioBundle audioBundle, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null)
        => Restore(engine, compositionIdentity, definitions, inputs, tuning, saved, random, null, audioBundle, cinematicContent, videosEnabled, questAdmission);

    internal static DaggerfallSession Restore(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, RulesetSavePayload saved,
        IRandomService random, DaggerfallEffectCatalog? effects, DaggerfallAudioBundle? audioBundle = null, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null)
    {
        DaggerfallSavePayload payload = DaggerfallSavePayload.Read(saved).ResolveRestore(definitions, inputs);
        return new DaggerfallSession(engine, definitions, inputs, tuning, compositionIdentity, payload, effects, audioBundle, cinematicContent, videosEnabled, questAdmission);
    }

    private DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs,
        DaggerfallTuning tuning, ResolvedCompositionIdentity? compositionIdentity, DaggerfallSavePayload? saved,
        DaggerfallEffectCatalog? effects, DaggerfallAudioBundle? audioBundle = null, ProductContent? cinematicContent = null, bool videosEnabled = true, DaggerfallQuestRuntimeAdmission? questAdmission = null)
    {
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _random = engine.Random;
            tuning = tuning.Validate();
            _definitions = definitions;
            Cinematics = cinematicContent is null ? null : new DaggerfallCinematicPresentation(engine, cinematicContent, definitions.Cinematics);
            if (Cinematics is not null) partiallyConstructed.Add(Cinematics);
            _openingCinematics = new DaggerfallOpeningCinematics(Cinematics, videosEnabled);
            _spatialService = engine.Spatial;
            _spawnGroundProbeLift = tuning.EnemyBehavior.SpawnGroundProbeLift;
            _spawnGroundProbeDistance = tuning.EnemyBehavior.SpawnGroundProbeDistance;
            DaggerActorAssembly assembled = DaggerActorFactory.Create(_random, definitions, inputs, saved, questAdmission);
            State = assembled.State;
            ActorsState actors = State.Actors;
            partiallyConstructed.Add(actors);
            _definitionsByActor = assembled.Definitions;
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
                    restoredSite.Discovered.Select(id => id.Require()))
                : new World.DaggerfallSiteContext(definitions.Locations, inputs.Site, null, []);
            _controlEngine = engine;
            _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            partiallyConstructed.Add(_spatial);
            // The selected site's normalized RDB doors restore their Engine pose/collider projection
            // before activation can query them and before the first character step consumes them.
            _doors = new DaggerfallDoorRuntime(State.Actors.Store, _random, inputs.Doors, saved?.Doors);
            partiallyConstructed.Add(_doors);
            if (saved is null)
            {
                foreach (ActorState actor in actors.All.Where(actor => authored[actor.DurableId].GroundOnSpawn))
                    GroundActor(actor);
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, State.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            TargetingService targeting = new(engine.Perception, _spatial, State.Actors, new DaggerTargetingPolicy(authored, tuning.MeleeTargeting));
            _staminaRecovery = new DaggerfallStaminaRecoveryModule(tuning.StaminaRecovery);
            State.Effects = new DaggerfallEffectLifecycle(State.Actors, effects ?? DaggerfallDiseasePolicy.CreateCatalog(
                _random,
                () => _time.Calendar.DayNumber,
                () => State.Character.Career));
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
            State.Character.BindCareerCommitted(State.SkillUses.RebaseForCareerSelection);
            State.LevelUps = new DaggerfallLevelUpState(State.Progression, State.SkillUses, State.Actors.Player.Stats,
                definitions, () => State.Character.Career, _random, _rewards);
            _combat = new DaggerCombatRules(_random, State.Actors, State.Equipment, State.InventoryFor, State.ItemInstances, definitions, authored, targeting, use => State.SkillUses.Record(use),
                () => State.Character.Background?.Modifiers.AvoidHit ?? 0);
            State.Kit = new(State.Actors, _combat.Targeting, _combat.Attacks, _combat.Execution, _combat.Rules, State.Inventory, State.Equipment);
            _enemyBehavior = new DaggerfallEnemyBehaviorModule(
                engine.Perception,
                _spatial,
                new ActorNavigationCoordinator(engine.Spatial, _spatial.Session),
                State.Actors,
                State.Kit.Attacks,
                tuning.EnemyBehavior);
            _authoredEntityIds = DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, playerDefinition.Loadout);
            if (saved is null)
            {
                ulong[] actorReservations = [DaggerfallActorIdentity.PlayerEntityId, .. inputs.Project.Actors.Values.Select(placement => checked((ulong)placement.EntityId))];
                _actorIdentities = DurableIdentityAllocator.Restore(new DurableIdentityState(
                [
                    new KindAllocatorState(DurableIdentityKind.Actor, DynamicActorFirstIdentity, actorReservations, []),
                    new KindAllocatorState(DurableIdentityKind.Item, DaggerfallUniqueItemAllocator.DefaultFirstEntityId, [.. _authoredEntityIds], []),
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
            State.Encumbrance = new DaggerfallEncumbrancePolicy(State.Inventory, State.Actors.Player.Stats);
            State.Currency = new DaggerfallCurrencyService(definitions, State.Inventory, State.ItemInstances, State.Encumbrance, _uniqueItems, saved?.Currency);
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
                State.Character);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored, () => State.Kit.Targeting.LastEvidence);
            _equipmentMoves = new DaggerfallEquipmentMoves(inventory, equipmentCoordinator, definitions,
                () => State.Character.Career.ForbiddenEquipment, State.ItemInstances);
            _itemCondition = new DaggerfallItemConditionService(definitions, State.ItemInstances, _equipmentMoves);
            _inventoryUi = new DaggerfallInventoryPresentation(_equipmentMoves, definitions, inputs.ClassicPresentation.InventoryIcons,
                State.Encumbrance, State.Currency);
            _inventoryUi.UseItemValuation(new DaggerfallItemValuation(definitions), State.ItemInstances, DaggerfallItemOwner.Player,
                entity => State.Actors.Entities.IdentityOf(new Rusty.Engine.Entities.EntityId(entity)).Value);
            _inventoryUi.UseItemCondition(_itemCondition);
            _lootUi = new DaggerfallLootPresentation(_corpseLoot, _inventoryUi);
            InitializeActivation(engine, tuning.LootInteraction);
            _characterUi = new DaggerfallCharacterPresentation(definitions, State.Character, playerDefinition, equipmentCoordinator, State.LevelUps);
            // The DOM's art comes from admitted content by media identity, so a session reads the
            // published closure once and publishes it to the UI that draws it.
            _hud = new DaggerfallHudProjection(
                engine.Ui,
                definitions.HudResources,
                compositionIdentity,
                DaggerfallUiArt.Read(engine.Content, inputs.ClassicPresentation.InventoryIcons.Values));
            partiallyConstructed.Add(_hud);
            _appearance = new PrivateersHoldAppearance(engine.Content, engine.Graphics, inputs, engine.Audio, tuning.PresentationAudio, _random, audioBundle, _doors);
            partiallyConstructed.Add(_appearance);
            _persistence = new(State, _corpseLoot, _uniqueItems, _camera, _time, _site, State.Effects, _doors);
            if (saved is not null) _persistence.Restore(saved);
        }
        catch (Exception constructionFailure)
        {
            List<Exception> failures = [constructionFailure];
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    /// <summary>The selected site's one authoritative RDB door owner for activation, spells, and dungeon actions.</summary>
    internal DaggerfallDoorRuntime Doors => _doors;

    /// <summary>Every actor definition by durable identity: authored placements and spawned actors alike.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> DefinitionsByActor => _definitionsByActor;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> DynamicActors => _dynamicActors;

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
        bool registered = false;
        try
        {
            ActorState actor = State.Actors.CreateActor(durableId, new EntityTypeId(definition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(definition, SpawnVitals(definition, spawnLevel, durableId)),
                pose, definition.Combat.Health.Value);
            // The behavior module attaches pursuit memory to every actor it is constructed with;
            // a spawn arrives after construction, so it carries its own.
            State.Actors.Store.Add(actor.Actor.Entity, new PursuitMemoryComponent());
            DaggerActorFactory.RegisterActorInventory(actor, State.InventoryStore);
            GrantSpawnLoadout(actor, definition);
            _definitionsByActor.Add(durableId, definition);
            _dynamicActors.Add(durableId, definition.Id);
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
        State.Actors.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Container, checked((ulong)durableId)));
        State.Actors.Entities.Destroy(ActorsState.Identity(durableId));
        _actorIdentities.Remove(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
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
        return _persistence.Capture(_latestUpdateGeneration, _latestSimulationStep, _dynamicActors);
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();

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
            ApplyAttackImpacts();
            UpdateRangedFlight(update.Facts);
            PublishPresentation();
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
        foreach (ProductInputEvent inputEvent in input)
        {
            firstStep.Add(inputEvent);
            if (inputEvent.ValueKind != InputValueKind.ProductPayload
                || !inputEvent.PayloadContract.Span.SequenceEqual("dagger.ui.action.v1"u8)) continue;
            DaggerfallPlayerUiAction? action = DaggerfallUiAction.Parse(inputEvent.PayloadData.Span);
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
                case "character-commit":
                case "character-cancel": ChangeCharacter(action!); break;
                case "character-level-allocate":
                case "character-level-commit": if (playing) ChangeLevelUp(action!); break;
                case "activation-mode": if (playing) ApplyActivationMode(action!); break;
                case "quest-choice":
                    if ((playing || modal) && State.Quests.ChoosePrompt(State.Variables, action!.QuestInstance!, action.QuestMessage!.Value, action.QuestChoice!.Value))
                        Presentation.SetOutcome("Quest choice recorded.");
                    break;
                case "attack": if (playing && !opensInteraction) firstStep.Request(DaggerfallInput.Attack); break;
                // A reloaded DOM holds no art and asks for the revision it is missing; the projection
                // answers on its next snapshot rather than a second delivery channel existing.
                case "art-request": _hud.RequestArt(); break;
                case "inventory": break;
                case "inventory-move": if (playing || modal) _inventoryUi.Move(action!); break;
                case "currency-deposit-gold": if (playing || modal) ChangeCurrency(action!); break;
                case "currency-withdraw-gold": if (playing || modal) ChangeCurrency(action!); break;
                case "currency-deposit-letters": if (playing || modal) ChangeCurrency(action!); break;
                case "currency-withdraw-letter": if (playing || modal) ChangeCurrency(action!); break;
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

        // Ordering within this one admitted update is clock, magic rounds, then calendar consumers
        // and simulation.  A normal game minute is one magic round; a larger admitted interval uses
        // the same lifecycle catch-up path as rest, travel, and prison, so no second effect timer can
        // drift from the saved calendar.
        DaggerfallCalendar calendarBefore = _time.Calendar;
        long minuteBefore = MinuteIndex(calendarBefore);
        _time.Advance(deltaSeconds * facts.AdmittedStepCount);
        State.Quests.AdvanceClocks(State.Variables, calendarBefore, _time.Calendar);
        State.Social.AdvanceElapsedMinutes(minuteBefore, MinuteIndex(_time.Calendar));
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
            if (DaggerfallUiAction.Parse(inputEvent.PayloadData.Span)?.Action is "loot" or "activation-mode") return true;
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
            bool open = _lootUi.Read() is not null;
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
        PublishPresentation();
    }

    /// <summary>Stores Host-owned save metadata for the next ordinary HUD projection.</summary>
    public void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic)
    {
        _saveSlots = slots?.ToArray() ?? throw new ArgumentNullException(nameof(slots));
        _saveSlotDiagnostic = diagnostic;
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
        Presentation.SetOutcome(ModalMessage());
        PublishPresentation();
    }

    /// <summary>What the outcome line says while a mode other than ordinary play holds the world.</summary>
    private string ModalMessage() => _mode switch
    {
        ProductMode.Modal => _lootUi.Read() is not null ? _lootUi.Message : "Interaction open.",
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
    }

    /// <summary>
    /// Advances a rest, travel, prison, or other ruleset-owned elapsed interval through the session's
    /// single calendar.  A caller resumes <see cref="DaggerfallCalendarAdvance.RemainingSeconds"/>
    /// after handling a consequence; only the portion the calendar accepted advances effects.
    /// </summary>
    internal DaggerfallCalendarAdvance AdvanceElapsedTime(long gameSeconds,
        IReadOnlyList<(int Identity, long SecondsFromNow)>? consequences = null)
    {
        DaggerfallCalendar calendarBefore = _time.Calendar;
        long minuteBefore = MinuteIndex(calendarBefore);
        DaggerfallCalendarAdvance advance = _time.AdvanceInterval(gameSeconds, consequences ?? []);
        State.Quests.AdvanceClocks(State.Variables, calendarBefore, _time.Calendar);
        // Daily conditions and ordinary source-order operations observe the same admitted calendar
        // after rest, travel, prison, or another interval, including an interval with no clock expiry.
        State.Quests.Advance(State.Variables, _time.Calendar);
        State.SkillUses.RaiseSkills(_time.Calendar.ToAbsoluteSeconds());
        State.LevelUps.BeginIfEligible();
        State.Social.AdvanceElapsedMinutes(minuteBefore, MinuteIndex(_time.Calendar));
        AdvanceEffectsForCalendar(calendarBefore, ordinaryPlay: false);
        AnnounceHoliday();
        return advance;
    }

    private void AdvanceEffectsForCalendar(DaggerfallCalendar before, bool ordinaryPlay)
    {
        long minutes = MinuteIndex(_time.Calendar) - MinuteIndex(before);
        if (minutes <= 0) return;

        // The normal path is expressed as its normal one-round operation.  Multiple minutes (whether
        // an unusually long admitted update or an elapsed interval) retain the donor's bounded
        // catch-up policy inside the lifecycle.
        if (ordinaryPlay && minutes == 1)
        {
            State.Effects.AdvanceOrdinaryRound();
            return;
        }

        _ = State.Effects.AdvanceElapsedRounds(minutes);
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
        State.Kit.AttackExecution.ObserveTimeline(generation, simulationStep);
        _input.Apply(State.PlayerControl, update);
        // The Engine still receives an ordinary character step (grounding and gravity remain its
        // responsibility), but classic over-capacity removes planar intent before that proposal.
        if (!State.Encumbrance.Read().CanMove) update.PlanarIntent = Vector2.Zero;
        _doors.Advance(update.DeltaSeconds);
        _spatial.Step(State.PlayerControl, update, _doors.CharacterEnvironment());
        _camera.Update(State.PlayerControl);
        _enemyBehavior.Update(State.PlayerControl, generation, simulationStep, update.DeltaSeconds, _facts);
        LookReceipt currentLook = _input.ResolveCurrentLook(State.PlayerControl);
        _staminaRecovery.Update(State.Actors.Player.Stats, update.DeltaSeconds);
        if (update.IsRequested(DaggerfallInput.ToggleWeapon)) _appearance.ToggleWeaponDrawn();
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        // Interaction owns this slice once requested. Direct semantic input can carry both intents
        // in the same Engine delivery, and it must follow the same no-attack rule as a DOM loot action.
        bool contextualInteraction = update.IsRequested(DaggerfallInput.Interact);
        if (!contextualInteraction && update.IsRequested(DaggerfallInput.Attack) && _appearance.CanStartPlayerAttack)
            State.Kit.Attacks.TryPlayerMelee(State.PlayerControl, currentLook, generation, simulationStep, update.DeltaSeconds, _facts);
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
        DisposeAll([_hud, _camera, _spatial, _appearance, State.Actors, _doors, State.Effects, .. Cinematics is null ? Array.Empty<IDisposable>() : new IDisposable[] { Cinematics }]);
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
        _hud.Publish(State.Actors.Player, State.Progression, Presentation, _mode, State.PlayerControl, Slots, _inventoryUi.Read(), _lootUi.Read(), _characterUi.Read(State.Actors.Player, State.Progression), LatestPanelRequest, _saveSlots, _saveSlotDiagnostic, _controlSettings, _controlDiagnostic, ActivationView, State.Quests.ReadPresentation(QuestTextContext));
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors);
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
