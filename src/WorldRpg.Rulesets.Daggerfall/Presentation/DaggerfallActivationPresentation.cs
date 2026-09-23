using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>The small DOM projection for contextual activation; the DOM sends semantic actions back.</summary>
internal sealed record DaggerfallActivationView(string Mode, string Message, bool Applied, DaggerfallDialogueView? Dialogue = null);

/// <summary>
/// Ruleset-owned activation projection state. The HUD adapter supplies the callback when it publishes
/// its normal snapshot; no browser-side mode state or secondary input path is introduced here.
/// </summary>
internal sealed class DaggerfallActivationPresentation
{
    private DaggerfallActivationView _view = new("grab", "Interaction mode: grab.", false);
    internal DaggerfallActivationView View => _view;

    internal void SetMode(DaggerfallActivationMode mode)
    {
        string name = mode.ToString().ToLowerInvariant();
        _view = new(name, $"Interaction mode: {name}.", false);
    }

    internal void Report(DaggerfallActivationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _view = _view with { Message = outcome.Message, Applied = outcome.Applied };
    }

    internal void SetDialogue(DaggerfallDialogueView? dialogue) => _view = _view with { Dialogue = dialogue };

    internal void Publish(Action<DaggerfallActivationView> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        publish(_view);
    }
}
