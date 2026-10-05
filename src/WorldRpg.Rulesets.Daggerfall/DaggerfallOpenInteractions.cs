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
    Dispel,
    Teleport,
    Identify,
    CreateItem,
    Legal,
    /// <summary>
    /// The DOM's game menu and every panel it hosts: inventory, character sheet, notebook, travel,
    /// rest, transport and wagon, save and load, and settings. The DOM reports whether it is open.
    /// </summary>
    Menu,
}

/// <summary>
/// The one owner of what is open over the world: which session-known interaction screens are open,
/// which of them hold the world still, the mode request that follows from that, and the DOM panel
/// request a pad button makes.
/// </summary>
/// <remarks>
/// Every screen holds the world while it is open: the session asks the product for Modal exactly while
/// one is, so time stands still and nothing acts on the player behind a menu. A screen that should let
/// the world run is an explicit exception in <see cref="RunsOverTheWorld"/>; there are none. Character
/// creation runs under the product's entry screen, which holds the world on its own.
/// </remarks>
internal sealed class DaggerfallOpenInteractions(
    Func<LootPresentation?> loot,
    Func<string> lootMessage,
    Func<bool> dungeonTextOpen,
    Func<bool> dialogueOpen,
    Func<bool> characterCreationOpen,
    Func<bool> levelUpOpen,
    Func<bool> bankOpen, Func<bool>? dispelOpen = null, Func<bool>? identifyOpen = null, Func<bool>? teleportOpen = null, Func<bool>? createItemOpen = null, Func<bool>? legalOpen = null)
{
    /// <summary>Admitted world seconds a panel request stands before the DOM is assumed not to need it.</summary>
    private const double PanelRequestLifetimeSeconds = 1d;

    /// <summary>The screens that deliberately let the world run while they are open.</summary>
    private static readonly IReadOnlySet<DaggerfallInteractionScreen> RunsOverTheWorld = new HashSet<DaggerfallInteractionScreen>();

    private string? _panelRequest;
    private ulong _panelRequestRevision;
    private double _panelRequestRemainingSeconds;
    private bool _menuOpen;

    /// <summary>Whether a screen holds the world still while it is open: every screen, unless it is an exception.</summary>
    internal static bool HoldsWorld(DaggerfallInteractionScreen screen) => !RunsOverTheWorld.Contains(screen);

    /// <summary>
    /// Records what the DOM reported about its game menu. The menu opening is also the DOM acting on
    /// a pad's panel request, so a standing request is spent: it cannot age while the world is held.
    /// </summary>
    internal void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        if (open) _panelRequest = null;
    }

    /// <summary>Whether one screen is open now.</summary>
    internal bool IsOpen(DaggerfallInteractionScreen screen) => screen switch
    {
        DaggerfallInteractionScreen.LootContainer => loot() is not null,
        DaggerfallInteractionScreen.DungeonText => dungeonTextOpen(),
        DaggerfallInteractionScreen.Dialogue => dialogueOpen(),
        DaggerfallInteractionScreen.CharacterCreation => characterCreationOpen(),
        DaggerfallInteractionScreen.LevelUp => levelUpOpen(),
        DaggerfallInteractionScreen.Bank => bankOpen(),
        DaggerfallInteractionScreen.Legal => legalOpen?.Invoke() == true,
        DaggerfallInteractionScreen.CreateItem => createItemOpen?.Invoke() == true,
        DaggerfallInteractionScreen.Identify => identifyOpen?.Invoke() == true,
        DaggerfallInteractionScreen.Dispel => dispelOpen?.Invoke() == true,
        DaggerfallInteractionScreen.Teleport => teleportOpen?.Invoke() == true,
        DaggerfallInteractionScreen.Menu => _menuOpen,
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
            : IsOpen(DaggerfallInteractionScreen.LootContainer) ? lootMessage()
            : IsOpen(DaggerfallInteractionScreen.Menu) ? "Menu open."
            : "Interaction open.",
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
