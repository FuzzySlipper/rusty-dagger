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

    internal DaggerfallSession(IEngineContext engine, PrivateersHoldInputs inputs) : this(engine, inputs, DaggerfallTuning.Defaults) { }

    internal DaggerfallSession(IEngineContext engine, PrivateersHoldInputs inputs, DaggerfallTuning tuning)
    {
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _random = engine.Random;
            tuning = tuning.Validate();
            DaggerfallCatalog catalog = new();
            _mechanicsCatalog = new DaggerfallMechanicsCatalog(engine.Mechanics, catalog.Items);
            partiallyConstructed.Add(_mechanicsCatalog);
            PlayerActorState player = new(_mechanicsCatalog.Bind(catalog.Player, PlayerMechanicsEntityId, InitialLoadout()), catalog.Player.Combat.Health.Value, engine.Mechanics);
            partiallyConstructed.Add(player);
            List<ActorState> actorStates = [];
            Dictionary<long, DaggerfallActorDefinition> authored = [];
            foreach (AuthoredActor source in inputs.Project.Actors.Values)
            {
                if (catalog.ForAuthoredName(source.Name) is not DaggerfallActorDefinition definition) continue;
                MechanicsEntity mechanics = _mechanicsCatalog.Bind(definition, checked((ulong)source.EntityId));
                ActorState actor = new(source.EntityId, mechanics, source.Position, definition.Combat.Health.Value, engine.Mechanics);
                partiallyConstructed.Add(actor);
                actorStates.Add(actor);
                authored.Add(source.EntityId, definition);
            }
            ActorsState actors = new(player, actorStates);
            State = new DaggerfallState(new PlayerControlState(inputs.Project.PlayerPosition, tuning.InitialPlayerLook.YawRadians, tuning.InitialPlayerLook.PitchRadians), actors, new ProgressionState());
            Presentation = new PresentationState("Ready");
            _input = new PlayerInputSystem(tuning.PlayerControl, engine.Look, DaggerfallInput.Controls, DaggerfallInput.Bindings);
            _spatial = new SpatialMovementSystem(engine.Spatial, inputs.ToSpatialScene(), tuning.Spatial);
            partiallyConstructed.Add(_spatial);
            _combat = new CombatModule();
            _rewards = new DaggerfallRewardReactions(new MechanicsInventoryCoordinator(engine.Mechanics, player.Mechanics), State.Progression, _random, authored);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored);
            _hud = new DaggerfallHudProjection(engine.Ui, engine.Mechanics, catalog.HudResources);
            partiallyConstructed.Add(_hud);
            _appearance = new PrivateersHoldAppearance(engine.Appearance, inputs);
            partiallyConstructed.Add(_appearance);
        }
        catch
        {
            DisposeAll(partiallyConstructed);
            throw;
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
        DisposeAll([_hud, _mechanicsCatalog, State.Actors, _spatial, _appearance]);
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

    private static IReadOnlyList<MechanicsInitialInventoryStack> InitialLoadout() =>
    [
        new("iron-longsword", 1),
        new("iron-dagger", 1),
        new("iron-cuirass", 1),
        new("gold-piece", 25),
    ];
}

internal static class DaggerfallInput
{
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly PlayerControlBindings Controls = new(["move"u8.ToArray(), "movement"u8.ToArray()], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD);
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [new(Attack, "attack"u8.ToArray())];
}
