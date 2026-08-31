using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Concrete Daggerfall composition of catalog policy, module state, and named Engine capabilities.</summary>
internal sealed class DaggerfallSession : IGameSession
{
    private const ulong PlayerMechanicsEntityId = (ulong)DaggerfallActorIdentity.PlayerEntityId;
    private readonly IRandomService _random;
    private readonly PlayerInputSystem _input;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly CombatModule _combat;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly FactBuffer<IProductFact> _facts = new();
    private FactBuffer<IProductFact>.FactTransaction? _outerFacts;
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly PrivateersHoldAppearance _appearance;
    private bool _disposed;

    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning)
        : this(engine, definitions, inputs, tuning, compositionIdentity: null)
    {
    }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning)
        : this(engine, definitions, inputs, tuning, compositionIdentity)
    {
    }

    private DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, ResolvedCompositionIdentity? compositionIdentity)
    {
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _random = engine.Random;
            tuning = tuning.Validate();
            DaggerfallMechanicsState mechanics = new();
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            ValidateInitialEntityIds(inputs, playerDefinition.Loadout);
            Dictionary<InventoryItemId, ItemDefinition> itemDefinitions = definitions.Items.Values
                .ToDictionary(item => new InventoryItemId(item.Id.Value), ToManagedItem);
            Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> equipmentSlots = definitions.EquipmentSlots.Values
                .ToDictionary(slot => new KitEquipmentSlotId(slot.Id.Value), ToManagedSlot);
            EntityId playerEntity = new(PlayerMechanicsEntityId);
            InventoryWorld inventoryWorld = new();
            inventoryWorld.RegisterInventory(new InventoryState(playerEntity));
            inventoryWorld.RegisterEquipment(new EquipmentState(playerEntity));
            MechanicsInventoryCoordinator inventory = new(inventoryWorld, playerEntity, itemDefinitions);
            MechanicsEquipmentCoordinator equipmentCoordinator = new(inventoryWorld, playerEntity, itemDefinitions, equipmentSlots);
            partiallyConstructed.Add(equipmentCoordinator);
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => definitions.Items[entry.ItemId].IsFungible))
            {
                inventory.Grant(new InventoryGrant(
                    "daggerfall.initial-loadout",
                    $"daggerfall.loadout.{entry.ItemId.Value}",
                    new InventoryItemId(entry.ItemId.Value),
                    entry.Quantity));
            }
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => !definitions.Items[entry.ItemId].IsFungible))
            {
                KitUniqueInventoryItem item = equipmentCoordinator.Materialize(new UniqueItemMaterialization(
                    $"daggerfall.loadout.{entry.UniqueEntityId!.Value}",
                    entry.UniqueEntityId.Value,
                    new InventoryItemId(entry.ItemId.Value)));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                {
                    equipmentCoordinator.Equip(
                        item,
                        [new KitEquipmentSlotId(slot.Value)],
                        new EquipmentChange(
                            "daggerfall.initial-equipment",
                            $"daggerfall.equipment.{entry.UniqueEntityId.Value}"));
                }
            }
            PlayerActorState player = new(mechanics.CreateActor(playerDefinition, playerDefinition.PlayerInitialVitals, PlayerMechanicsEntityId), playerDefinition.Combat.Health.Value);
            partiallyConstructed.Add(player);
            List<ActorState> actorStates = [];
            Dictionary<long, DaggerfallActorDefinition> authored = [];
            foreach (AuthoredActor source in inputs.Project.Actors.Values)
            {
                if (!definitions.Actors.TryGetValue(source.ActorId, out DaggerfallActorDefinition? definition))
                    throw new InvalidOperationException($"Privateer's Hold placement '{source.EntityId}' refers to missing actor '{source.ActorId.Value}'.");
                ActorState actor = new(
                    source.EntityId,
                    mechanics.CreateActor(definition, InitialVitals(definition, source.EntityId), checked((ulong)source.EntityId)),
                    source.Position,
                    definition.Combat.Health.Value);
                partiallyConstructed.Add(actor);
                actorStates.Add(actor);
                authored.Add(source.EntityId, definition);
            }
            ActorsState actors = new(player, actorStates);
            State = new DaggerfallState(new PlayerControlState(inputs.Project.PlayerPosition, inputs.InitialLook.YawRadians, inputs.InitialLook.PitchRadians), actors, new ProgressionState(), inventory, equipmentCoordinator);
            Presentation = new PresentationState("Ready");
            _input = new PlayerInputSystem(tuning.PlayerControl, engine.Look, DaggerfallInput.Controls, DaggerfallInput.Bindings);
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            partiallyConstructed.Add(_spatial);
            _camera = new FirstPersonCameraSystem(engine.CameraView, State.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            authored.Add(checked((long)PlayerMechanicsEntityId), playerDefinition);
            DaggerfallMeleeTargetingModule targeting = new(engine.Perception, _spatial, State.Actors, authored, tuning.MeleeTargeting);
            _combat = new CombatModule(_random, State.Actors, State.Equipment, definitions, authored, targeting);
            _enemyBehavior = new DaggerfallEnemyBehaviorModule(
                engine.Perception,
                _spatial,
                new ActorNavigationCoordinator(engine.Spatial, _spatial.Session),
                State.Actors,
                _combat,
                tuning.EnemyBehavior);
            _rewards = new DaggerfallRewardReactions(
                State.Inventory,
                State.Progression,
                State.Actors.Player.Mechanics,
                playerDefinition,
                _random,
                authored,
                definitions,
                new DaggerfallUniqueItemAllocator(DaggerfallUniqueItemAllocator.DefaultFirstEntityId));
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored);
            _hud = new DaggerfallHudProjection(engine.Ui, definitions.HudResources, compositionIdentity);
            partiallyConstructed.Add(_hud);
            _appearance = new PrivateersHoldAppearance(engine.Content, engine.Appearance, inputs, engine.Audio, tuning.PresentationAudio, _random);
            partiallyConstructed.Add(_appearance);
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
    public void PublishInitial() => PublishPresentation();

    public ProductUpdateResult Update(ProductUpdate update)
    {
        _appearance.BeginAdmittedUpdate();
        PrivateersHoldAppearance.PresentationCheckpoint mediaCheckpoint = _appearance.Checkpoint();
        _outerFacts = _facts.BeginTransaction();
        try
        {
        Update(update.Facts, update.Input);
        // Sprite playback consumes the Engine-bound outer update identity.  It
        // must not run for each private catch-up simulation step above.
        if (update.Facts.LifecycleState == ProductLifecycleState.Running
            && update.Facts.Mode == ProductUpdateMode.Realtime
            && update.Facts.AdmittedStepCount > 0)
        {
            _appearance.Advance(update.Facts);
            PublishPresentation();
        }
        _appearance.CompleteAdmittedUpdate();
        _outerFacts.Commit();
        _outerFacts = null;
        return ProductUpdateResult.None;
        }
        catch
        {
            _appearance.Restore(mediaCheckpoint);
            _outerFacts?.Rollback();
            _outerFacts = null;
            throw;
        }
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
        foreach (ProductInputEvent inputEvent in input) firstStep.Add(inputEvent);

        // One admitted update owns one input slice. Later catch-up steps derive
        // only committed held keyboard/mapped-direction intent; direct axes,
        // direct digital movement, pointer deltas, and semantic actions do not replay.
        Update(firstStep, facts.Generation, facts.SimulationStep);
        for (uint step = 1; step < facts.AdmittedStepCount; step++)
            Update(new ProductUpdateState(deltaSeconds), facts.Generation, checked(facts.SimulationStep + step));
    }

    internal void Update(ProductUpdateState update) => Update(update, 0, 0);

    private void Update(ProductUpdateState update, ulong generation, ulong simulationStep)
    {
        PreparedPlayerInput input = _input.Prepare(State.PlayerControl, update);
        _input.EnsureCommittable(input, State.PlayerControl);
        // Daggerfall presently has no authored dynamic support or obstacle facts.
        // The generic Kit environment remains call-local and is resubmitted per proposal.
        PreparedSpatialStep? step = _spatial.Prepare(State.PlayerControl, update, input, CharacterStepEnvironment.Empty);
        if (step is { } prepared)
        {
            CharacterStepReceipt receipt = _spatial.Propose(prepared);
            _input.Commit(input, State.PlayerControl, update);
            State.PlayerControl.Apply(receipt);
        }
        else _input.Commit(input, State.PlayerControl, update);
        _camera.Update(State.PlayerControl);
        _enemyBehavior.Update(State.PlayerControl, generation, simulationStep, update.DeltaSeconds, _facts);
        if (update.IsRequested(DaggerfallInput.Attack)) _combat.TryPlayerMelee(State.PlayerControl, _input.ResolveCurrentLook(State.PlayerControl), generation, simulationStep, update.DeltaSeconds, _facts);
        DeliverFacts();
        PublishPresentation();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAll([_hud, State.Actors, State.Equipment, _camera, _spatial, _appearance]);
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
        if (fact is ActorDiedFact died) _rewards.React(died, _facts);
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.React(fact, State.Actors);
        _outcomes.React(fact);
    }

    private void PublishPresentation()
    {
        _hud.Publish(State.Actors.Player, State.Progression, Presentation);
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors);
    }

    private static ItemDefinition ToManagedItem(DaggerfallItemDefinition item)
    {
        ItemEquipmentPolicy? equipment = item.Equipment is null
            ? null
            : new ItemEquipmentPolicy(
                item.Equipment.RequiredSlots,
                item.Equipment.ExclusiveGroup is { } group ? EquipmentExclusivityId.Parse(group) : null);
        // Weight is authored item metadata for every catalog entry.  The current
        // reference session does not register a carrying-capacity policy, so this
        // is a cost declaration rather than a claim that encumbrance is enforced.
        IEnumerable<ItemCapacityCost>? capacity = item.Weight > 0
            ? [new ItemCapacityCost(CapacityMetricId.Parse("weight"), checked((ulong)item.Weight))]
            : null;
        return new ItemDefinition(
            ItemDefinitionId.Parse(item.Id.Value),
            item.IsFungible ? ItemKind.Fungible : ItemKind.Unique,
            item.MaximumQuantity,
            item.Equipment?.Classifications.Select(ItemClassificationId.Parse),
            capacity,
            equipment);
    }

    private static EquipmentSlotDefinition ToManagedSlot(DaggerfallEquipmentSlotDefinition slot) =>
        new(Rusty.Engine.Mechanics.EquipmentSlotId.Parse(slot.Id.Value), slot.AllowedClassifications.Select(ItemClassificationId.Parse));

    internal void ResolveExplicitMelee(ExplicitMeleeRequest request)
    {
        _combat.ResolveExplicit(request, _facts);
        DeliverFacts();
        PublishPresentation();
    }

    internal DaggerfallMeleeTargetingEvidence? LastMeleeTargeting => _combat.LastMeleeTargeting;

    private void DeliverFacts()
    {
        if (_outerFacts is { } transaction) transaction.Deliver(React);
        else _facts.Deliver(React);
    }
    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEnemyBehavior => _enemyBehavior.LastEvidence;

    private DaggerfallVitalValues InitialVitals(DaggerfallActorDefinition definition, long entityId)
    {
        if (definition.Id.Value == "player") return definition.PlayerInitialVitals;
        int health = checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.EnemyScope, CombatRandomKey.InitialHealth(entityId, definition.Id.Value), definition.Health.Minimum, definition.Health.Maximum)).Value);
        return new DaggerfallVitalValues(health, 0, 0);
    }

    private static void ValidateInitialEntityIds(PrivateersHoldInputs inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout)
    {
        HashSet<ulong> ids = [PlayerMechanicsEntityId];
        foreach (AuthoredActor actor in inputs.Project.Actors.Values)
        {
            if (actor.EntityId <= 0 || !ids.Add(checked((ulong)actor.EntityId)))
                throw new InvalidOperationException($"Initial Mechanics entity id '{actor.EntityId}' collides with another player, placement, or item entity.");
        }
        foreach (DaggerfallLoadoutEntry item in loadout)
            if (item.UniqueEntityId is ulong entityId && !ids.Add(entityId))
                throw new InvalidOperationException($"Initial Mechanics entity id '{entityId}' collides with another player, placement, or item entity.");
    }
}

internal static class DaggerfallInput
{
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly PlayerControlBindings Controls = new(
        ["move"u8.ToArray(), "movement"u8.ToArray()],
        KeyboardControl.KeyW,
        KeyboardControl.KeyS,
        KeyboardControl.KeyA,
        KeyboardControl.KeyD,
        new DirectionalMovementBindings("move.forward"u8.ToArray(), "move.backward"u8.ToArray(), "move.left"u8.ToArray(), "move.right"u8.ToArray()));
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [new(Attack, "attack"u8.ToArray())];
}
