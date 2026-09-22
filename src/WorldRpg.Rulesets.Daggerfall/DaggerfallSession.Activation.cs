using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Contextual activation composition and named dispatch into existing Daggerfall owners.</summary>
internal sealed partial class DaggerfallSession
{
    private DaggerfallActivationModule? _activation;
    private readonly DaggerfallActivationPresentation _activationPresentation = new();

    /// <summary>
    /// Wires activation after spatial, actor, and corpse owners exist. The normal session
    /// constructor calls this once; keeping it named makes the dependency order explicit.
    /// </summary>
    private void InitializeActivation(IEngineContext engine, DaggerfallLootInteractionTuning reach)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _activation = new DaggerfallActivationModule(
            new InteractionTargetingService(engine.Perception, _spatial, State.Actors.Entities),
            reach,
            new DaggerfallActivationContributions(new DaggerfallCorpseActivationOwner(_corpseLoot, _lootUi, State.Actors, _facts)));
    }

    internal DaggerfallActivationMode ActivationMode => _activation?.Mode ?? DaggerfallActivationMode.Grab;
    internal InteractionTargetingEvidence? LastActivationTargeting => _activation?.LastEvidence;
    internal DaggerfallActivationView ActivationView => _activationPresentation.View;

    /// <summary>Lets the ordinary HUD projection callback carry activation state with its snapshot.</summary>
    internal void PublishActivationView(Action<DaggerfallActivationView> publish) => _activationPresentation.Publish(publish);

    /// <summary>Consumes one parsed mode action; it does not turn into a world activation.</summary>
    private bool ApplyActivationMode(DaggerfallPlayerUiAction action)
    {
        if (action.Action != "activation-mode" || action.Mode is null || _activation is null) return false;
        DaggerfallActivationMode mode = action.Mode switch
        {
            "grab" => DaggerfallActivationMode.Grab,
            "info" => DaggerfallActivationMode.Info,
            "talk" => DaggerfallActivationMode.Talk,
            "steal" => DaggerfallActivationMode.Steal,
            "bash" => DaggerfallActivationMode.Bash,
            _ => throw new InvalidOperationException($"Parsed activation mode '{action.Mode}' is not declared."),
        };
        _activation.ChangeMode(mode);
        _activationPresentation.SetMode(mode);
        Presentation.SetOutcome(_activationPresentation.View.Message);
        return true;
    }

    /// <summary>
    /// Applies one interaction request. Returning true means contextual activation owned the input,
    /// even when no object was eligible, so callers never fall through to an unrelated use path.
    /// </summary>
    private bool TryActivateContextual(LookReceipt look)
    {
        if (_activation is null) return false;
        DaggerfallActivationOutcome outcome = _activation.Activate(
            State.Actors.Player.Actor.Entity,
            State.PlayerControl,
            look);
        _activationPresentation.Report(outcome);
        Presentation.SetOutcome(outcome.Message);
        return true;
    }

    /// <summary>The current corpse owner composed into the activation seam.</summary>
    private sealed class DaggerfallCorpseActivationOwner(
        DaggerfallCorpseLootModule loot,
        DaggerfallLootPresentation lootUi,
        ActorsState actors,
        FactBuffer<IProductFact> facts) : IDaggerfallCorpseActivationOwner
    {
        public IEnumerable<DaggerfallActivationTarget> CorpseTargets()
        {
            foreach (long actorId in loot.Corpses.Keys.OrderBy(id => id))
            {
                if (!actors.TryGet(actorId, out ActorState actor)) continue;
                yield return new(
                DaggerfallActivationTargetKind.Corpse,
                ActorsState.Identity(actorId),
                actor.Actor.Entity,
                checked((ulong)actorId),
                actor.Position,
                Precedence: 4);
            }
        }

        public DaggerfallActivationOutcome ActivateCorpse(DaggerfallActivationSelection selection)
        {
            long actorId = checked((long)selection.Target.Identity.Value);
            if (selection.Mode == DaggerfallActivationMode.Info)
                return new(true, $"You see the remains of {loot.ContainerName(actorId)}.");
            if (selection.Mode == DaggerfallActivationMode.Bash)
                return new(false, "Bash requires a door.");

            if (lootUi.OpenResolved(actorId) is { IsEmpty: true } empty)
                loot.TryCommitLoot(empty, facts);
            return lootUi.Read() is null
                ? new(false, lootUi.Message)
                : new(true, lootUi.Message);
        }
    }

}
