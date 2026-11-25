using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What the menus show of Quit: whether a game in progress has changed since it was kept, which asks a confirm.</summary>
internal sealed record DaggerfallQuitView(bool Unsaved);

/// <summary>
/// Quit, from the game menu and the title screen: the Host says whether the product can quit and when the game was last
/// kept (begun, loaded or saved), and takes the player's request; quitting ends the product as closing its window does.
/// A game in progress that has changed since it was kept is unsaved, which the menu asks the player to confirm before it
/// quits: play time has passed, or the player has acted, as when a note is written while the menu holds the world.
/// </summary>
internal sealed class DaggerfallQuit(PresentationState presentation)
{
    /// <summary>
    /// The player actions that change nothing a save holds: reading and asking (inspecting, quotes, the expedition map's places and view, pages, art; the map's Find pins its match, which is saved),
    /// opening the menu or a map, the saves and Quit themselves, the title's choices, and the settings, which the
    /// Host keeps apart from the game. Any other action applied may have changed the game.
    /// </summary>
    private static readonly HashSet<DaggerfallUiActionKind> Unchanging =
    [
        DaggerfallUiActionKind.Begin, DaggerfallUiActionKind.BundleSelect, DaggerfallUiActionKind.ArtRequest,
        DaggerfallUiActionKind.SpellInfo, DaggerfallUiActionKind.Menu, DaggerfallUiActionKind.InputClaimed,
        DaggerfallUiActionKind.MapOpen, DaggerfallUiActionKind.ExpeditionOpen, DaggerfallUiActionKind.ExpeditionPlaces,
        DaggerfallUiActionKind.ExpeditionView, DaggerfallUiActionKind.ExpeditionFollow, DaggerfallUiActionKind.ExpeditionLook,
        DaggerfallUiActionKind.WorldMap,
        DaggerfallUiActionKind.AtlasTiles, DaggerfallUiActionKind.LodgingQuote,
        DaggerfallUiActionKind.MerchantQuote, DaggerfallUiActionKind.InventoryInspect, DaggerfallUiActionKind.NotebookPage,
        DaggerfallUiActionKind.BankOpen,
        DaggerfallUiActionKind.SaveGame, DaggerfallUiActionKind.LoadGame, DaggerfallUiActionKind.SaveSlots,
        DaggerfallUiActionKind.SaveSlot, DaggerfallUiActionKind.LoadSlot, DaggerfallUiActionKind.DeleteSlot,
        DaggerfallUiActionKind.DeathNewGame, DaggerfallUiActionKind.DeathLoadGame, DaggerfallUiActionKind.DeathQuit,
        DaggerfallUiActionKind.Quit,
        DaggerfallUiActionKind.ControlsRebind, DaggerfallUiActionKind.ControlsRebindButton, DaggerfallUiActionKind.ControlsVolume,
        DaggerfallUiActionKind.ControlsReset, DaggerfallUiActionKind.ControlsLook, DaggerfallUiActionKind.ControlsUiScale,
        DaggerfallUiActionKind.ControlsGroundCover, DaggerfallUiActionKind.ControlsClutterDistance,
        DaggerfallUiActionKind.ControlsLampShadows, DaggerfallUiActionKind.ControlsSurfaceRelief, DaggerfallUiActionKind.ControlsReliefDistance,
        DaggerfallUiActionKind.ControlsInteriorExtrude, DaggerfallUiActionKind.ControlsInteriorExtrudeDepth,
        DaggerfallUiActionKind.ControlsInteriorExtrudeCutoff, DaggerfallUiActionKind.ControlsInteriorExtrudeDistance,
    ];

    private bool _available;
    private double _keptAtPlaySeconds;
    private bool _actedSinceKept;
    private bool _requested;

    internal void ReportAvailable(bool available) => _available = available;

    /// <summary>The game as it stands at <paramref name="playSeconds"/> of play is kept.</summary>
    internal void Kept(double playSeconds)
    {
        _keptAtPlaySeconds = playSeconds;
        _actedSinceKept = false;
    }

    /// <summary>The session applied a player action; one that can change the game leaves it unsaved, held world or not.</summary>
    internal void Applied(DaggerfallUiActionKind kind)
    {
        if (!Unchanging.Contains(kind)) _actedSinceKept = true;
    }

    /// <summary>Quit as the menus show it, or null where the product cannot quit; only a game being played can be unsaved.</summary>
    internal DaggerfallQuitView? View(ProductMode mode, double playSeconds) => _available
        ? new(mode is ProductMode.Playing or ProductMode.Paused or ProductMode.Modal && (playSeconds > _keptAtPlaySeconds || _actedSinceKept))
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
