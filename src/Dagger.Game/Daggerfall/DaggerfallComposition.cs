using Rusty.Engine;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Daggerfall.Presentation;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;
using RustyDagger.Game.Modules.Encounters;
using RustyDagger.Game.Modules.Equipment;
using RustyDagger.Game.Modules.Inventory;
using RustyDagger.Game.Modules.PlayerControl;
using RustyDagger.Game.Modules.Presentation;
using RustyDagger.Game.Modules.Progression;

namespace RustyDagger.Game.Daggerfall;

/// <summary>Concrete Daggerfall composition of catalog policy, module state, and named Engine capabilities.</summary>
internal sealed class DaggerfallComposition : IDisposable
{
    private const ulong PlayerMechanicsEntityId = 1;
    private readonly IRandomService _random;
    private readonly PlayerInputSystem _input;
    private readonly SpatialMovementSystem _spatial;
    private readonly CombatModule _combat;
    private readonly EncounterSystem _encounters;
    private readonly DaggerfallMechanicsCatalog _mechanicsCatalog;
    private readonly IReadOnlyDictionary<long, CombatantState> _combatants;
    private readonly CombatantState _playerCombatant;
    private readonly ProductFactBuffer _facts = new();
    private readonly DaggerfallRewardReactions _rewards;
    private readonly EncounterReaction _encounterReactions;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly PrivateersHoldAppearance _appearance;
    private readonly WeaponDefinition _rightHand;
    private bool _disposed;

    internal DaggerfallComposition(IEngineContext engine, ProductContent content) : this(engine, PrivateersHoldContent.Read(content)) { }

    internal DaggerfallComposition(IEngineContext engine, PrivateersHoldInputs inputs)
    {
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _random = engine.Random;
            DaggerfallTuning tuning = DaggerfallTuning.Defaults.Validate();
            DaggerfallCatalog catalog = new();
            _mechanicsCatalog = new DaggerfallMechanicsCatalog(engine.Mechanics);
            partiallyConstructed.Add(_mechanicsCatalog);
            PlayerActorState player = new(_mechanicsCatalog.Bind(catalog.Player, PlayerMechanicsEntityId), catalog.Player.Combat.Health.Value, engine.Mechanics);
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
            Dictionary<long, CombatantState> combatants = [];
            foreach ((long entityId, DaggerfallActorDefinition definition) in authored)
                if (actors.TryGet(entityId, out ActorState actor)) combatants.Add(entityId, new CombatantState(actor, definition.Combat.ToCombatantProfile()));
            _combatants = combatants;
            _playerCombatant = new CombatantState(player, catalog.Player.Combat.ToCombatantProfile());
            State = new DaggerfallState(new PlayerControlState(inputs.Project.PlayerPosition), actors, new InventoryState([new ItemStack(catalog.IronLongsword.Id, 1), new ItemStack("iron-dagger", 1), new ItemStack("iron-cuirass", 1), new ItemStack("gold-piece", 25)]), new EquipmentState(catalog.IronLongsword), new CombatState(), new EncounterState(), new ProgressionState());
            Presentation = new PresentationState();
            _input = new PlayerInputSystem(tuning.PlayerControl, engine.Look);
            _spatial = new SpatialMovementSystem(engine.Spatial, inputs, tuning.Spatial);
            partiallyConstructed.Add(_spatial);
            _combat = new CombatModule(engine.Mechanics, tuning.Combat);
            _encounters = new EncounterSystem(catalog.Encounters);
            _rewards = new DaggerfallRewardReactions(State.Inventory, State.Progression, _random, authored);
            _encounterReactions = new EncounterReaction(State.Encounters);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored);
            _hud = new DaggerfallHudProjection(engine.Ui, engine.Mechanics, catalog.HudResources);
            _appearance = new PrivateersHoldAppearance(engine.Appearance, inputs);
            _rightHand = catalog.IronLongsword;
        }
        catch
        {
            for (int index = partiallyConstructed.Count - 1; index >= 0; index--) partiallyConstructed[index].Dispose();
            throw;
        }
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    internal void PublishInitial() => PublishPresentation();

    internal void Update(ProductUpdateState update)
    {
        _combat.AdvanceCooldowns(State.Actors, update.DeltaSeconds);
        _input.Apply(State.PlayerControl, update);
        _spatial.Step(State.PlayerControl, update);
        EncounterTarget? encounter = _encounters.ActiveEncounter(State.PlayerControl, State.Actors);
        CombatantState? target = encounter is not null && _combatants.TryGetValue(encounter.MemberEntityId, out CombatantState? combatant) ? combatant : null;
        if (update.AttackRequested) _combat.TryPlayerMelee(State.PlayerControl, _playerCombatant, target, _rightHand, State.UpdateSequence, _random, _facts);
        _combat.TryEnemyMelee(State.PlayerControl, _playerCombatant, target, State.UpdateSequence, _random, _facts);
        State.AdvanceSequence();
        _facts.Deliver(React);
        PublishPresentation();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _spatial.Dispose();
        State.Actors.Dispose();
        _mechanicsCatalog.Dispose();
        _disposed = true;
    }

    private void React(IProductFact fact)
    {
        if (fact is ActorDiedFact died) { _rewards.React(died, _facts); _encounterReactions.React(died); }
        _outcomes.React(fact);
    }

    private void PublishPresentation()
    {
        _hud.Publish(State.Actors.Player, State.Progression, _encounters.ActiveEncounter(State.PlayerControl, State.Actors), Presentation);
        _appearance.Publish(State.Actors);
    }
}
