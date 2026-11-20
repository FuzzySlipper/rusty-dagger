using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The games the Host offers at the entry screen and the one the player chose there. Stepping the choice only
/// records it; starting a game (Begin, or Create character) under a choice other than the running game asks the Host
/// to switch to it and start there. A new game that has begun its opening belongs to the game it was created under,
/// so the choice is open only on the title screen and before then.
/// </summary>
internal sealed class DaggerfallBundleChoice(PresentationState presentation)
{
    private GameBundleChoices? _offered;
    private GameBundleId? _chosen;
    private GameBundleRequest? _request;

    /// <summary>The games the Host offers, the chosen one current, for the HUD to list; none until the Host reports them.</summary>
    internal GameBundleChoices? Offered => _offered is { } offered && _chosen is { } chosen ? offered with { Current = chosen } : _offered;

    internal void Report(GameBundleChoices choices) => _offered = choices ?? throw new ArgumentNullException(nameof(choices));

    /// <summary>Records the game the player chose, or says why it cannot be changed now.</summary>
    internal void Choose(string key, ProductMode mode, bool newGameBegun)
    {
        if (mode != ProductMode.Title || newGameBegun)
        {
            presentation.SetOutcome("The game can be changed only from the title screen.");
            return;
        }
        GameBundleId chosen = new(key);
        _chosen = chosen == _offered?.Current ? null : chosen;
    }

    /// <summary>
    /// Asks the Host for the chosen game, to start it as the player just did, when one other than the running game is
    /// chosen. False when the running game is the choice, so the start is this session's own.
    /// </summary>
    internal bool StartUnderChoice(GameBundleStart start)
    {
        if (_chosen is not { } chosen) return false;
        _request = new(chosen, start);
        return true;
    }

    /// <summary>Takes the game the player chose and started, once.</summary>
    internal GameBundleRequest? Take()
    {
        GameBundleRequest? request = _request;
        _request = null;
        return request;
    }
}
