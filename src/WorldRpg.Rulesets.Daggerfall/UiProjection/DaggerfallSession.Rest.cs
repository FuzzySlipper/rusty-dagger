using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The rest panel: the DOM rest action admitted, and the last rest result kept for the HUD.</summary>
internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallRestPresentation _restPresentation = new();

    /// <summary>The last accepted or rejected rest result, retained for the next HUD projection.</summary>
    internal DaggerfallRestView RestView => _restPresentation.Read();

    /// <summary>Admits the DOM rest action at the session boundary.</summary>
    private void ChangeRest(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        DaggerfallRestMode mode = action.Mode switch
        {
            "timed" => DaggerfallRestMode.Timed,
            "until-healed" => DaggerfallRestMode.UntilHealed,
            "loiter" => DaggerfallRestMode.Loiter,
            _ => throw new ArgumentException($"Unknown rest mode '{action.Mode}'.", nameof(action)),
        };
        DaggerfallRestRequest request = new(mode, mode == DaggerfallRestMode.UntilHealed ? 0 : action.Hours ?? 0);
        DaggerfallRestEligibility eligibility = _rest.Eligibility(mode, State.Actors.Player.Stats, this);
        (bool rapidHealing, bool noRegeneration) = _rest.CharacterTraits(PlayerSpecials, _activeProfileKey.Kind);
        DaggerfallRestResult result = ApplyRest(request, eligibility,
            seconds => _rest.PassInterval(seconds, mode, State.Actors.Player.Stats, this), rapidHealing, noRegeneration);
        // An encounter ends the donor's rest window: the player faces it rather than a menu.
        if (result.Interruption == DaggerfallRestInterruption.Encounter) RequestPanel(DaggerfallPanel.Resume);
        _input.Neutralize();
        _locomotion.Neutralize(); NeutralizeGuard();
    }
}
