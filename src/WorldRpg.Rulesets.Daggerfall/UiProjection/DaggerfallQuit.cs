using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What the menus show of Quit: whether a game in progress has been played since it was kept, which asks a confirm.</summary>
internal sealed record DaggerfallQuitView(bool Unsaved);

/// <summary>
/// Quit, from the game menu and the title screen: the Host says whether the product can quit and when the game was last
/// kept (begun, loaded or saved), and takes the player's request; quitting ends the product as closing its window does.
/// A game in progress played since it was kept is unsaved, which the menu asks the player to confirm before it quits.
/// </summary>
internal sealed class DaggerfallQuit(PresentationState presentation)
{
    private bool _available;
    private double _keptAtPlaySeconds;
    private bool _requested;

    internal void ReportAvailable(bool available) => _available = available;

    /// <summary>The game as it stands at <paramref name="playSeconds"/> of play is kept.</summary>
    internal void Kept(double playSeconds) => _keptAtPlaySeconds = playSeconds;

    /// <summary>Quit as the menus show it, or null where the product cannot quit; only a game being played can be unsaved.</summary>
    internal DaggerfallQuitView? View(ProductMode mode, double playSeconds) => _available
        ? new(mode is ProductMode.Playing or ProductMode.Paused or ProductMode.Modal && playSeconds > _keptAtPlaySeconds)
        : null;

    /// <summary>The player asked to quit; the menus asked them to confirm an unsaved game first.</summary>
    internal void Request()
    {
        if (_available) _requested = true;
        else presentation.SetOutcome("Close the window to quit.");
    }

    /// <summary>Takes the player's quit, once.</summary>
    internal bool Take()
    {
        bool requested = _requested;
        _requested = false;
        return requested;
    }
}
