using Rusty.Engine;
using RustyDagger.Game.Content;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;
using RustyDagger.Game.Modules.Encounters;
using RustyDagger.Game.Modules.Equipment;
using RustyDagger.Game.Modules.Inventory;
using RustyDagger.Game.Modules.Loot;
using RustyDagger.Game.Modules.PlayerControl;
using RustyDagger.Game.Modules.Presentation;
using RustyDagger.Game.Modules.Progression;

namespace RustyDagger.Game.Daggerfall;

/// <summary>The first concrete construction-kit composition: Daggerfall definitions, tuning, module owners, and Engine capabilities.</summary>
internal sealed class DaggerfallComposition : IDisposable
{
    private readonly IRandomService _random;
    private readonly PlayerInputSystem _input;
    private readonly SpatialMovementSystem _spatial;
    private readonly CombatModule _combat;
    private readonly ProductFactBuffer _facts = new();
    private readonly LootReaction _loot;
    private readonly ProgressionReaction _progression;
    private readonly EncounterReaction _encounters;
    private readonly PresentationReaction _presentationReaction;
    private readonly DaggerPresentation _presentation;
    private bool _disposed;

    internal DaggerfallComposition(IEngineContext engine, ProductContent content)
        : this(engine, PrivateersHoldContent.Read(content))
    {
    }

    internal DaggerfallComposition(IEngineContext engine, PrivateersHoldInputs inputs)
    {
        _random = engine.Random;
        DaggerfallTuning tuning = DaggerfallTuning.Defaults.Validate();
        ActorsState actors = new(DaggerfallDefinitions.Player, inputs.Project.Actors.Values);
        InventoryState inventory = new([new ItemStack(DaggerfallDefinitions.IronLongsword.Id, 1), new ItemStack("iron-dagger", 1), new ItemStack("iron-cuirass", 1), new ItemStack("gold-piece", 25)]);
        State = new DaggerfallState(new PlayerControlState(inputs.Project.PlayerPosition), actors, inventory, new EquipmentState(DaggerfallDefinitions.IronLongsword), new CombatState(), new EncounterState(), new ProgressionState());
        Presentation = new PresentationState();
        _input = new PlayerInputSystem(tuning.PlayerControl, engine.Look);
        _spatial = new SpatialMovementSystem(engine.Spatial, inputs, tuning.Spatial);
        _combat = new CombatModule(tuning.Combat);
        _loot = new LootReaction(inventory, _random);
        _progression = new ProgressionReaction(State.Progression);
        _encounters = new EncounterReaction(State.Encounters);
        _presentationReaction = new PresentationReaction(Presentation);
        _presentation = new DaggerPresentation(engine.Ui, engine.Appearance, inputs);
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    internal void PublishInitial() => _presentation.Publish(State, Presentation);

    internal void Update(ProductUpdateState update)
    {
        _combat.AdvanceCooldowns(State.Actors, update.DeltaSeconds);
        _input.Apply(State.PlayerControl, update);
        _spatial.Step(State.PlayerControl, update);
        if (update.AttackRequested) _combat.TryPlayerMelee(State, _random, _facts);
        _combat.TryEnemyMelee(State, _random, _facts);
        State.AdvanceSequence();
        _facts.Deliver(React);
        _presentation.Publish(State, Presentation);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _spatial.Dispose();
        _disposed = true;
    }

    private void React(IProductFact fact)
    {
        if (fact is ActorDiedFact died)
        {
            _loot.React(died, State, _facts);
            _progression.React(died, State, _facts);
            _encounters.React(died);
        }
        _presentationReaction.React(fact, State);
    }
}
