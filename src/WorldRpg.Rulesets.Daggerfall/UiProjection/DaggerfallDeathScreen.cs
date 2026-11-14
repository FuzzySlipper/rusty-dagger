using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The Dead screen: its choices, the one replacement or named-slot request the Host takes, and the outcome the Host
/// reports back. What entering death does across domains is the session's list.
/// </summary>
/// <param name="reportOutcome">The Host's outcome report, which reopens the choices, says the line and publishes.</param>
internal sealed class DaggerfallDeathScreen(PresentationState presentation, Action<string> reportOutcome)
{
    private readonly DaggerfallDeathPresentation _presentation = new();
    private PlayerDefeatOutcomeRequest? _request;

    internal DaggerfallDeathView View => _presentation.View;

    /// <summary>
    /// Consumes only the three death choices while the product owns Dead. Ordinary payloads are
    /// dropped by the caller before they can reach gameplay or another panel.
    /// </summary>
    internal void Choose(DaggerfallPlayerUiAction? action)
    {
        switch (action?.Action)
        {
            case DaggerfallDeathPresentation.NewGameAction:
                if (_presentation.TrySelect(action.Action, out _))
                    _request = new(PlayerDefeatOutcome.NewGame);
                break;
            case DaggerfallDeathPresentation.LoadGameAction:
                if (action.Key is not { Length: > 0 })
                {
                    reportOutcome("Choose a saved game to load.");
                    break;
                }
                if (_presentation.TrySelect(action.Action, out _))
                    _request = new(PlayerDefeatOutcome.Load, action.Key);
                break;
            case DaggerfallDeathPresentation.QuitAction:
                if (_presentation.TrySelect(action.Action, out _))
                    _request = new(PlayerDefeatOutcome.QuitToTitle);
                break;
        }
    }

    /// <summary>Lets the Host take one admitted replacement or named-slot request.</summary>
    internal PlayerDefeatOutcomeRequest? TakeOutcomeRequest()
    {
        PlayerDefeatOutcomeRequest? request = _request;
        _request = null;
        return request;
    }

    /// <summary>Says a Host outcome and reopens the choices for a session that remains defeated.</summary>
    internal void Reopen(string message)
    {
        _presentation.ClearSelection();
        presentation.SetOutcome(message);
    }

    /// <summary>Opens the screen on entry to death; true only on the entry that opened it.</summary>
    internal bool Enter() => _presentation.Enter();

    /// <summary>Clears the screen on a return to play or the title.</summary>
    internal void Clear() => _presentation.Clear();

    internal void SetLoadAvailable(bool available) => _presentation.SetLoadAvailable(available);

    /// <summary>A save outcome reopens the choices while the player is dead.</summary>
    internal void ReopenAfterSaveOutcome(bool dead)
    {
        if (dead) _presentation.ClearSelection();
    }
}
