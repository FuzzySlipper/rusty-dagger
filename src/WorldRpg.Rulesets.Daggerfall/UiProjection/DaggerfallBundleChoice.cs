using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The games the Host offers at the entry screen and the one the player chose there, for the Host to switch to.
/// A new game that has begun its opening belongs to the game it was created under, so the choice is open only on
/// the title screen and before then.
/// </summary>
internal sealed class DaggerfallBundleChoice(PresentationState presentation)
{
    private GameBundleId? _request;

    /// <summary>The games the Host offers, for the HUD to list; none until the Host reports them.</summary>
    internal GameBundleChoices? Offered { get; private set; }

    internal void Report(GameBundleChoices choices) => Offered = choices ?? throw new ArgumentNullException(nameof(choices));

    /// <summary>Asks for another game, or says why it cannot be changed now.</summary>
    internal void Choose(string key, ProductMode mode, bool newGameBegun)
    {
        if (mode != ProductMode.Title || newGameBegun)
        {
            presentation.SetOutcome("The game can be changed only from the title screen.");
            return;
        }
        _request = new GameBundleId(key);
    }

    /// <summary>Takes the game the player chose, once.</summary>
    internal GameBundleId? Take()
    {
        GameBundleId? request = _request;
        _request = null;
        return request;
    }
}
