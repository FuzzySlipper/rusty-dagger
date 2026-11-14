using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The rest panel: the DOM rest action admitted, and the last rest result kept for the HUD.</summary>
/// <param name="place">The kind of place the player rests in, which some careers' healing answers to.</param>
/// <param name="applyRest">The rest's list, which passes the time and publishes its result here.</param>
/// <param name="requestPanel">Asks the DOM for a panel.</param>
/// <param name="neutralizeHeldInput">Drops held movement, run and guard once the rest is over.</param>
internal sealed class DaggerfallRestPanel(DaggerfallRest rest, StatsComponent player, IDaggerfallRestSurroundings surroundings,
    DaggerfallCareerPassives passives, Func<DaggerfallWorldProfileKind> place, DaggerfallRestPanel.RestList applyRest,
    Action<string> requestPanel, Action neutralizeHeldInput)
{
    internal delegate DaggerfallRestResult RestList(DaggerfallRestRequest request, DaggerfallRestEligibility eligibility,
        Func<long, DaggerfallRestTimeAdvance> advanceTime, bool rapidHealing, bool noRegeneration);

    private readonly DaggerfallRestPresentation _presentation = new();

    /// <summary>The last accepted or rejected rest result, retained for the next HUD projection.</summary>
    internal DaggerfallRestView View => _presentation.Read();

    /// <summary>Keeps a rest's result for the HUD.</summary>
    internal void Publish(DaggerfallRestResult result) => _presentation.Publish(result);

    /// <summary>Admits the DOM rest action at the session boundary.</summary>
    internal void Change(DaggerfallPlayerUiAction action)
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
        DaggerfallRestEligibility eligibility = rest.Eligibility(mode, player, surroundings);
        (bool rapidHealing, bool noRegeneration) = rest.CharacterTraits(passives.PlayerSpecials, place());
        DaggerfallRestResult result = applyRest(request, eligibility,
            seconds => rest.PassInterval(seconds, mode, player, surroundings), rapidHealing, noRegeneration);
        // An encounter ends the donor's rest window: the player faces it rather than a menu.
        if (result.Interruption == DaggerfallRestInterruption.Encounter) requestPanel(DaggerfallPanel.Resume);
        neutralizeHeldInput();
    }
}
