using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Death-mode action admission and presentation wiring for the Daggerfall session.</summary>
internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallDeathPresentation _deathPresentation = new();
    private PlayerDefeatOutcomeRequest? _playerDefeatOutcomeRequest;

    /// <summary>
    /// Consumes only the three death choices while the product owns Dead. Ordinary payloads are
    /// dropped by the caller before they can reach gameplay or another panel.
    /// </summary>
    private void HandleDeathAction(DaggerfallPlayerUiAction? action)
    {
        switch (action?.Action)
        {
            case DaggerfallDeathPresentation.NewGameAction:
                if (_deathPresentation.TrySelect(action.Action, out _))
                    _playerDefeatOutcomeRequest = new(PlayerDefeatOutcome.NewGame);
                break;
            case DaggerfallDeathPresentation.LoadGameAction:
                if (action.Key is not { Length: > 0 })
                {
                    ReportPlayerDefeatOutcome("Choose a saved game to load.");
                    break;
                }
                if (_deathPresentation.TrySelect(action.Action, out _))
                    _playerDefeatOutcomeRequest = new(PlayerDefeatOutcome.Load, action.Key);
                break;
            case DaggerfallDeathPresentation.QuitAction:
                if (_deathPresentation.TrySelect(action.Action, out _))
                    _playerDefeatOutcomeRequest = new(PlayerDefeatOutcome.QuitToTitle);
                break;
        }
    }

    /// <summary>Lets the Host take one admitted replacement or named-slot request.</summary>
    public PlayerDefeatOutcomeRequest? TakePlayerDefeatOutcomeRequest()
    {
        PlayerDefeatOutcomeRequest? request = _playerDefeatOutcomeRequest;
        _playerDefeatOutcomeRequest = null;
        return request;
    }

    /// <summary>Publishes a Host outcome and reopens choices when the session remains defeated.</summary>
    public void ReportPlayerDefeatOutcome(string message)
    {
        _deathPresentation.ClearSelection();
        Presentation.SetOutcome(message);
        PublishPresentation();
    }

    /// <summary>Applies one-shot effects on entry to death and clears them on a replacement.</summary>
    private void ApplyDeathPresentationMode(ProductMode mode)
    {
        if (mode == ProductMode.Dead)
        {
            // A retained activation projection must not leave its dialogue owner open over the
            // defeat choices. Close through the existing dialogue service so the next snapshot
            // carries the canonical null dialogue value as well as the death projection.
            _dialogue?.Close();
            bool entered = _deathPresentation.Enter();
            // Keep the authoritative player pose intact while moving the existing Engine camera
            // down to the floor. This is a one-shot presentation pose, not a second gameplay clock
            // or a parallel camera; a replacement session restores its ordinary camera descriptor.
            if (entered)
            {
                if (State.PlayerControl.Position is not null)
                    _camera.Update(State.PlayerControl, -_tuning.Camera.EyeHeight);
                _appearance.PlayPlayerDeath(_latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);
            }
        }
        else if (mode is ProductMode.Playing or ProductMode.Title)
            _deathPresentation.Clear();
    }

    private void SetDeathLoadAvailability(bool available) => _deathPresentation.SetLoadAvailable(available);

    private void ReopenDeathChoicesAfterSaveOutcome()
    {
        if (_mode == ProductMode.Dead) _deathPresentation.ClearSelection();
    }
}
