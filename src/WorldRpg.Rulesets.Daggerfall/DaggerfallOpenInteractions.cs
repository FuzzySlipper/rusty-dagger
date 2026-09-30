using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The session-known interaction screens that can be open over the world.</summary>
internal enum DaggerfallInteractionScreen
{
    /// <summary>A corpse or ground container's loot window.</summary>
    LootContainer,
    /// <summary>A dungeon action's text or riddle prompt.</summary>
    DungeonText,
    /// <summary>A conversation opened by Talk activation.</summary>
    Dialogue,
    /// <summary>The character-creation draft opened from the entry screen.</summary>
    CharacterCreation,
    /// <summary>A pending level-up allocation on the character sheet.</summary>
    LevelUp,
    /// <summary>A banking service opened at a live provider.</summary>
    Bank,
}

/// <summary>
/// The one owner of what is open over the world: which session-known interaction screens are open,
/// which of them hold the world still, the mode request that follows from that, and the DOM panel
/// request a pad button makes.
/// </summary>
/// <remarks>
/// Only <see cref="DaggerfallInteractionScreen.LootContainer"/> and
/// <see cref="DaggerfallInteractionScreen.DungeonText"/> hold the world today: the session asks the
/// product for Modal exactly while one is open. Dialogue, level-up, bank and the DOM's own panels
/// (inventory, character sheet, notebook, travel, menu) run over ordinary play, so time advances while
/// they are open; character creation runs under the product's entry screen. Changing which screens
/// hold the world is a change to <see cref="HoldsWorld"/> alone.
/// </remarks>
internal sealed class DaggerfallOpenInteractions(
    Func<LootPresentation?> loot,
    Func<string> lootMessage,
    Func<bool> dungeonTextOpen,
    Func<bool> dialogueOpen,
    Func<bool> characterCreationOpen,
    Func<bool> levelUpOpen,
    Func<bool> bankOpen)
{
    /// <summary>Admitted world seconds a panel request stands before the DOM is assumed not to need it.</summary>
    private const double PanelRequestLifetimeSeconds = 1d;

    private string? _panelRequest;
    private ulong _panelRequestRevision;
    private double _panelRequestRemainingSeconds;

    /// <summary>Whether a screen holds the world still while it is open.</summary>
    internal static bool HoldsWorld(DaggerfallInteractionScreen screen) =>
        screen is DaggerfallInteractionScreen.LootContainer or DaggerfallInteractionScreen.DungeonText;

    /// <summary>Whether one screen is open now.</summary>
    internal bool IsOpen(DaggerfallInteractionScreen screen) => screen switch
    {
        DaggerfallInteractionScreen.LootContainer => loot() is not null,
        DaggerfallInteractionScreen.DungeonText => dungeonTextOpen(),
        DaggerfallInteractionScreen.Dialogue => dialogueOpen(),
        DaggerfallInteractionScreen.CharacterCreation => characterCreationOpen(),
        DaggerfallInteractionScreen.LevelUp => levelUpOpen(),
        DaggerfallInteractionScreen.Bank => bankOpen(),
        _ => throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unknown interaction screen."),
    };

    /// <summary>Whether any open screen holds the world.</summary>
    internal bool HoldsWorldOpen => Enum.GetValues<DaggerfallInteractionScreen>().Any(screen => HoldsWorld(screen) && IsOpen(screen));

    /// <summary>
    /// The mode this session asks the product for. Death outranks everything and only a session
    /// replacement leaves it; a modal exists exactly while a world-holding screen is open, so the
    /// request follows that screen rather than the key that opened it. A mode the product holds on its
    /// own, such as the entry screen or a pause, has no screen to follow, so nothing is asked.
    /// </summary>
    internal ProductMode? ModeRequest(ProductMode current, bool playerDefeated)
    {
        if (playerDefeated) return ProductMode.Dead;
        if (current is not (ProductMode.Playing or ProductMode.Modal)) return null;
        bool holds = HoldsWorldOpen;
        return holds == (current == ProductMode.Modal) ? null : holds ? ProductMode.Modal : ProductMode.Playing;
    }

    /// <summary>What the outcome line says while a mode other than ordinary play holds the world.</summary>
    internal string ModeMessage(ProductMode mode) => mode switch
    {
        ProductMode.Modal => IsOpen(DaggerfallInteractionScreen.DungeonText) ? "Dungeon text open."
            : IsOpen(DaggerfallInteractionScreen.LootContainer) ? lootMessage() : "Interaction open.",
        ProductMode.Dead => "You have died.",
        ProductMode.Paused => "Paused.",
        // The entry screen says its own thing; a status line would compete with the screen that is up.
        ProductMode.Title => string.Empty,
        _ => string.Empty,
    };

    /// <summary>
    /// The panel the player asked for through a device the DOM has no channel of its own for.
    /// </summary>
    /// <remarks>
    /// The keyboard reaches the panels because the DOM hears the keys itself; a pad reaches the
    /// product. The panels stay where they are — the DOM owns whether one is open — so a button that
    /// opens one travels as that panel's own menu action and the product invents no second notion of
    /// a panel. The revision is what lets the DOM apply each request once while the request stays
    /// published, the same way a published loot revision is recognised rather than replayed.
    /// </remarks>
    internal DaggerfallPanelRequest? LatestPanelRequest => _panelRequest is null ? null : new DaggerfallPanelRequest(_panelRequest, _panelRequestRevision);

    internal void RequestPanel(string panel)
    {
        _panelRequest = panel;
        _panelRequestRevision = checked(_panelRequestRevision + 1);
        _panelRequestRemainingSeconds = PanelRequestLifetimeSeconds;
    }

    /// <summary>
    /// Ages a standing panel request on the same admitted world time everything else ages on.
    /// </summary>
    /// <remarks>
    /// Opening a panel is an event, not state: the product does not own whether one is open, so a
    /// request that outlived the DOM that performed it would re-open a panel nobody asked for on the
    /// next page load — and a player holding only a pad has no way back out of it. The window is
    /// generous because the DOM acts on the next snapshot, and it is admitted world time because
    /// there is one clock here.
    /// </remarks>
    internal void AgePanelRequest(double deltaSeconds)
    {
        if (_panelRequest is null) return;
        _panelRequestRemainingSeconds -= deltaSeconds;
        if (_panelRequestRemainingSeconds <= 0d) _panelRequest = null;
    }
}
