using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Activation's presentation: the mode and outcome the HUD shows, the mode action, and the loot windows a body or a
/// pile opens when used.
/// </summary>
internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallActivationPresentation _activationPresentation = new();

    /// <summary>Lets the ordinary HUD projection callback carry activation state with its snapshot.</summary>
    internal void PublishActivationView(Action<DaggerfallActivationView> publish) => publish(ActivationView);

    /// <summary>Consumes one parsed mode action; it does not turn into a world activation.</summary>
    private bool ApplyActivationMode(DaggerfallPlayerUiAction action)
    {
        if (action.Kind != DaggerfallUiActionKind.ActivationMode || action.Mode is null || Activation.Module is null) return false;
        DaggerfallActivationMode mode = action.Mode switch
        {
            "grab" => DaggerfallActivationMode.Grab,
            "info" => DaggerfallActivationMode.Info,
            "talk" => DaggerfallActivationMode.Talk,
            "steal" => DaggerfallActivationMode.Steal,
            "lockpick" => DaggerfallActivationMode.Steal,
            "bash" => DaggerfallActivationMode.Bash,
            _ => throw new InvalidOperationException($"Parsed activation mode '{action.Mode}' is not declared."),
        };
        if (mode != DaggerfallActivationMode.Talk) _dialogue?.Close();                          // Dialogue
        Activation.ChangeMode(mode);                                                              // Interiors
        _activationPresentation.SetMode(mode);
        Presentation.SetOutcome(_activationPresentation.View.Message);
        return true;
    }

    /// <summary>What using a body does: opens its loot window, an empty one committed at once.</summary>
    private DaggerfallActivationOutcome OpenCorpseLoot(long actorId)
    {
        if (_lootUi.OpenResolved(actorId) is { IsEmpty: true } empty)
            _corpseLoot.TryCommitLoot(empty, _facts);
        return _lootUi.Read() is null
            ? new(false, _lootUi.Message)
            : new(true, _lootUi.Message);
    }

    /// <summary>What using a pile or a property container does: stocks it as it opens, then opens its loot window.</summary>
    private DaggerfallActivationOutcome OpenGroundLoot(long id) =>
        PropertyLoot.Open(id) is not bool privateProperty ? new(false, "That container is no longer available.")
        : _lootUi.OpenGround(id, privateProperty) ? new(true, _lootUi.Message) : new(false, _lootUi.Message);
}
