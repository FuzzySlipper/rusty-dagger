using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Concrete Daggerfall composition of catalog policy, module state, and named Engine capabilities.</summary>
internal sealed class DaggerfallSession : IGameSession
{
    private const ulong PlayerMechanicsEntityId = 1;
    private readonly IRandomService _random;
    private readonly PlayerInputSystem _input;
    private readonly SpatialMovementSystem _spatial;
    private readonly CombatModule _combat;
    private readonly DaggerfallMechanicsCatalog _mechanicsCatalog;
    private readonly FactBuffer<IProductFact> _facts = new();
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly PrivateersHoldAppearance _appearance;
    private bool _disposed;

    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning)
    {
        List<IDisposable> partiallyConstructed = [];
        DaggerfallMechanicsCatalog? catalog = null;
        try
        {
            _random = engine.Random;
            tuning = tuning.Validate();
            ValidateInitialEntityIds(inputs);
            _mechanicsCatalog = catalog = new DaggerfallMechanicsCatalog(engine.Mechanics, definitions);
            partiallyConstructed.Add(_mechanicsCatalog);
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            IReadOnlyList<ScenarioLoadoutEntry> uniqueLoadout = inputs.Loadout.Where(entry => !definitions.Items[entry.ItemId].IsFungible).ToArray();
            List<MechanicsEntity> uniqueItems = [];
            List<EquipmentItemLease> equipmentItemLeases = [];
            foreach (ScenarioLoadoutEntry entry in uniqueLoadout)
            {
                MechanicsEntity item = _mechanicsCatalog.BindUniqueItem(definitions.Items[entry.ItemId], entry.UniqueEntityId!.Value);
                uniqueItems.Add(item);
                equipmentItemLeases.Add(new EquipmentItemLease(new UniqueInventoryItem(entry.UniqueEntityId.Value, new InventoryItemId(entry.ItemId.Value)), item));
            }
            IReadOnlyList<MechanicsInitialEquipmentAssignment> equipment = uniqueLoadout
                .Where(entry => entry.EquipSlot is not null)
                .Select(entry => new MechanicsInitialEquipmentAssignment(entry.EquipSlot!.Value.Value, entry.UniqueEntityId!.Value))
                .ToArray();
            // An empty-but-present component is required for later Engine-owned Equip operations.
            PlayerActorState player = new(_mechanicsCatalog.Bind(playerDefinition, playerDefinition.PlayerInitialVitals, PlayerMechanicsEntityId, InitialFungibleLoadout(inputs.Loadout, definitions), equipment, uniqueItems), playerDefinition.Combat.Health.Value, engine.Mechanics);
            partiallyConstructed.Add(player);
            List<ActorState> actorStates = [];
            Dictionary<long, DaggerfallActorDefinition> authored = [];
            foreach (AuthoredActor source in inputs.Project.Actors.Values)
            {
                if (!definitions.Actors.TryGetValue(source.ActorId, out DaggerfallActorDefinition? definition))
                    throw new InvalidOperationException($"Privateer's Hold placement '{source.EntityId}' refers to missing actor '{source.ActorId.Value}'.");
                MechanicsEntity mechanics = _mechanicsCatalog.Bind(definition, InitialVitals(definition, source.EntityId), checked((ulong)source.EntityId));
                ActorState actor = new(source.EntityId, mechanics, source.Position, definition.Combat.Health.Value, engine.Mechanics);
                partiallyConstructed.Add(actor);
                actorStates.Add(actor);
                authored.Add(source.EntityId, definition);
            }
            ActorsState actors = new(player, actorStates);
            MechanicsInventoryCoordinator inventory = new(engine.Mechanics, player.Mechanics);
            MechanicsEquipmentCoordinator equipmentCoordinator = new(engine.Mechanics, _mechanicsCatalog.Catalog, player.Mechanics, equipmentItemLeases);
            State = new DaggerfallState(new PlayerControlState(inputs.Project.PlayerPosition, inputs.InitialLook.YawRadians, inputs.InitialLook.PitchRadians), actors, new ProgressionState(), inventory, equipmentCoordinator);
            Presentation = new PresentationState("Ready");
            _input = new PlayerInputSystem(tuning.PlayerControl, engine.Look, DaggerfallInput.Controls, DaggerfallInput.Bindings);
            _spatial = new SpatialMovementSystem(engine.Spatial, inputs.ToSpatialScene(), tuning.Spatial);
            partiallyConstructed.Add(_spatial);
            authored.Add(checked((long)PlayerMechanicsEntityId), playerDefinition);
            _combat = new CombatModule(engine.Mechanics, _random, State.Actors, State.Equipment, definitions, authored, tuning.Combat);
            _rewards = new DaggerfallRewardReactions(State.Inventory, State.Progression, _random, authored);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored);
            _hud = new DaggerfallHudProjection(engine.Ui, engine.Mechanics, definitions.HudResources);
            partiallyConstructed.Add(_hud);
            _appearance = new PrivateersHoldAppearance(engine.Appearance, inputs);
            partiallyConstructed.Add(_appearance);
        }
        catch (Exception constructionFailure)
        {
            // Catalog-owned mechanics entities must receive their terminal lifecycle while their
            // safe handles remain live. Preserve and surface both failures rather than hiding cleanup.
            List<Exception> failures = [constructionFailure];
            if (catalog is not null)
            {
                partiallyConstructed.Remove(catalog);
                try { catalog.Dispose(); }
                catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            }
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    public void PublishInitial() => PublishPresentation();

    public ProductTurnRequest Update(ProductUpdate update)
    {
        Update(update.Facts, update.Input);
        return ProductTurnRequest.None;
    }

    internal void Update(ProductUpdateFacts facts, ReadOnlySpan<ProductInputEvent> input)
    {
        // Daggerfall is a realtime simulation. Demand and external turns have no
        // fixed delta, so this ruleset deliberately does not interpret them as steps.
        if (facts.LifecycleState != ProductLifecycleState.Running
            || facts.Mode != ProductTurnKind.Realtime
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

        // One admitted update owns one input slice. Apply it to the first step,
        // then preserve held intent without replaying one-shot actions.
        Update(firstStep);
        for (uint step = 1; step < facts.AdmittedStepCount; step++)
            Update(new ProductUpdateState(deltaSeconds) { PlanarIntent = firstStep.PlanarIntent });
    }

    internal void Update(ProductUpdateState update)
    {
        _input.Apply(State.PlayerControl, update);
        _spatial.Step(State.PlayerControl, update);
        if (update.IsRequested(DaggerfallInput.Attack)) _combat.TryPlayerMelee(State.PlayerControl, _facts);
        _facts.Deliver(React);
        PublishPresentation();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAll([_hud, State.Actors, _mechanicsCatalog, State.Equipment, _spatial, _appearance]);
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
        _outcomes.React(fact);
    }

    private void PublishPresentation()
    {
        _hud.Publish(State.Actors.Player, State.Progression, Presentation);
        _appearance.Publish(State.Actors);
    }

    private static IReadOnlyList<MechanicsInitialInventoryStack> InitialFungibleLoadout(IEnumerable<ScenarioLoadoutEntry> values, DaggerfallDefinitions definitions) => values
        .Where(value => definitions.Items[value.ItemId].IsFungible)
        .Select(value => new MechanicsInitialInventoryStack(value.ItemId.Value, value.Quantity))
        .ToArray();

    internal void ResolveExplicitMelee(ExplicitMeleeRequest request)
    {
        _combat.ResolveExplicit(request, _facts);
        _facts.Deliver(React);
        PublishPresentation();
    }

    private DaggerfallVitalValues InitialVitals(DaggerfallActorDefinition definition, long entityId)
    {
        if (definition.Id.Value == "player") return definition.PlayerInitialVitals;
        int health = checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.EnemyScope, CombatRandomKey.InitialHealth(entityId, definition.Id.Value), definition.Health.Minimum, definition.Health.Maximum)).Value);
        return new DaggerfallVitalValues(health, 0, 0);
    }

    private static void ValidateInitialEntityIds(PrivateersHoldInputs inputs)
    {
        HashSet<ulong> ids = [PlayerMechanicsEntityId];
        foreach (AuthoredActor actor in inputs.Project.Actors.Values)
        {
            if (actor.EntityId <= 0 || !ids.Add(checked((ulong)actor.EntityId)))
                throw new InvalidOperationException($"Initial Mechanics entity id '{actor.EntityId}' collides with another player, placement, or item entity.");
        }
        foreach (ScenarioLoadoutEntry item in inputs.Loadout)
            if (item.UniqueEntityId is ulong entityId && !ids.Add(entityId))
                throw new InvalidOperationException($"Initial Mechanics entity id '{entityId}' collides with another player, placement, or item entity.");
    }
}

internal static class DaggerfallInput
{
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly PlayerControlBindings Controls = new(["move"u8.ToArray(), "movement"u8.ToArray()], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD);
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [new(Attack, "attack"u8.ToArray())];
}
