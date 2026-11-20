using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What only the session holds of a HUD frame, handed in each time it publishes one.</summary>
/// <param name="Mode">The product mode the session is in.</param>
/// <param name="NewGameInitialized">Whether a new game has begun its opening, after which the entry screen offers no creation or game choice.</param>
/// <param name="PanelRequest">The panel the player asked for through a device the DOM has no channel of its own for.</param>
/// <param name="SaveSlots">The Host's save slots.</param>
/// <param name="SaveSlotDiagnostic">The Host's word on its save slots.</param>
/// <param name="Bundles">The games the Host offers at the entry screen.</param>
internal readonly record struct DaggerfallHudSession(ProductMode Mode, bool NewGameInitialized, DaggerfallPanelRequest? PanelRequest,
    IReadOnlyList<SaveSlotSummary> SaveSlots, string? SaveSlotDiagnostic, GameBundleChoices? Bundles);

/// <summary>
/// The HUD frame: every panel's projection read in one place and published to the DOM, once per publication. It also
/// keeps the device the player last pressed on, whose bindings the HUD names.
/// </summary>
/// <param name="pack">The player's carried inventory, which the transport panel weighs.</param>
/// <param name="currentText">What the text macros name now.</param>
/// <param name="transportAccess">Where the player's transport can be reached from here.</param>
/// <param name="drawnArt">The refreshed art set drawn, or null for the classic one.</param>
/// <param name="effectItemName">The name of the item a player effect comes from.</param>
/// <param name="weaponDrawn">Whether the player's weapon is drawn.</param>
/// <param name="holdsWorldOpen">Whether something open holds the world still, which puts the aim's prompt away.</param>
/// <param name="deadzone">How far a stick moves before the controller counts as the device pressed on.</param>
/// <param name="readout">The developer's input readout.</param>
internal sealed class DaggerfallHudFrames(DaggerfallHudProjection hud, ActorsState actors, PresentationState presentation,
    PlayerControlState player, PresentationSlots slots, DaggerfallInventoryPresentation inventoryUi, DaggerfallEconomyPanels economy,
    DaggerfallLootPresentation loot, DaggerfallCharacterPresentation characterUi, DaggerfallDefinitions definitions,
    Func<DaggerfallTextContext> currentText, DaggerfallPlayerControls controls, DaggerfallActivationPanel activation,
    DaggerfallSwimmingPolicy swimming, DaggerfallEffectLifecycle effects, DaggerfallQuestInstances quests,
    DaggerfallQuestTextContext questTexts, DaggerfallWorldTime time, DaggerfallBookNotebook notebook, DaggerfallTransportPolicy transport,
    MechanicsInventoryCoordinator pack, Func<DaggerfallTransportAccessContext> transportAccess, DaggerfallPropertyState property,
    DaggerfallWagonStorage wagon, DaggerfallDungeonText dungeonText, DaggerfallDeathScreen death, DaggerfallRestPanel rest,
    DaggerfallRoomRental roomRental, DaggerfallFastTravel fastTravel, DaggerfallSiteContext site, Func<ulong, string> effectItemName,
    DaggerfallCinematicPresentation? cinematics, DaggerfallLocalMap map, DaggerfallLaw law, DaggerfallCreateItem createItem,
    DaggerfallTeleport teleport, DaggerfallDispel dispel, DaggerfallIdentify identify, DaggerfallSpellbookPanel spellbook,
    DaggerfallDetectors detectors, DaggerfallPlayerTime playerTime, LoadingScreen? loading, DaggerfallTutorial tutorial,
    DaggerfallRadialCommands radial, DaggerfallActivation aim, PlayerInputSystem playerInput, Func<DaggerfallArtReplacements?> drawnArt,
    Func<bool> weaponDrawn, Func<bool> holdsWorldOpen, float deadzone, DaggerfallInputReadout readout)
{
    /// <summary>The device the player last pressed something on ("keyboard" or "controller"), so the HUD names its bindings for it.</summary>
    internal string InputDevice { get; private set; } = "keyboard";

    /// <summary>Publishes this frame of every panel to the DOM.</summary>
    internal void Publish(in DaggerfallHudSession session)
    {
        InventoryPresentation inventory = inventoryUi.Read();
        hud.Publish(new DaggerfallHudFrame(actors.Player, presentation, session.Mode, player, slots,
            Inventory: inventory,
            Property: economy.ReadPropertyPresentation(),
            Loot: loot.Read(),
            Character: DaggerfallCharacterSheetText.Describe(characterUi.Read(actors.Player, actors.Player.Progression), definitions, currentText()),
            CharacterCreationAvailable: !session.NewGameInitialized,
            PanelRequest: session.PanelRequest,
            SaveSlots: session.SaveSlots,
            Bundles: session.Mode == ProductMode.Title && !session.NewGameInitialized ? session.Bundles : null,
            SaveSlotDiagnostic: session.SaveSlotDiagnostic,
            ControlSettings: controls.Settings,
            ControlDiagnostic: controls.Diagnostic,
            Activation: activation.View,
            Breath: swimming.ReadBreath(actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
                effects.GrantsWaterBreathing(DaggerfallActorIdentity.PlayerEntityId)),
            Quests: DrawnEscorts(quests.ReadPresentation(questTexts.For, time.Calendar)),
            Notebook: notebook.Read(),
            Transport: DaggerfallTransportProjection.Read(transport, pack.Read(), transportAccess(),
                ownsShip: property.OwnsShip, wagon: wagon, itemLabel: (key, definition, quantity) => inventoryUi.DescribeItem(key, definition, quantity,
                    owner: DaggerfallItemOwner.Wagon(wagon.Id!.Value)).Label),
            DungeonText: dungeonText.Projection,
            Death: death.View,
            Rest: rest.View,
            Lodging: roomRental.View,
            Travel: fastTravel.Read(),
            SiteName: site.ActiveSite?.Name,
            Calendar: time.Calendar,
            Effects: ReadActiveEffects(),
            CinematicSource: cinematics?.ActiveSource,
            Map: map.ReadOpen(), Legal: law.View, CreateItem: createItem.View, Teleport: teleport.View, Dispel: dispel.View, Identify: identify.View, Spells: spellbook.Read(), Detectors: detectors.Read())
        {
            ActionDrivenTime = controls.ActionDrivenTime, TimeHeld = playerTime.Held,
            Weapon = new DaggerfallWeaponView(weaponDrawn(),
                inventory.Items.FirstOrDefault(item => item.EquippedSlots.Contains("right-hand"))?.Label ?? "Bare hands"),
            Prompt = Prompt(session),
            InputDevice = InputDevice,
            Loading = loading,
            Screens = DaggerfallUiArt.Screens.ToDictionary(screen => screen, screen => drawnArt()?.Drawn(screen) ?? screen, StringComparer.Ordinal),
            TutorialOffer = tutorial.Offer,
            Radial = radial.Read(activation.View.Mode),
            TutorialRunning = tutorial.Running,
            Tutorial = tutorial.View,
            Readout = readout.Read(),
        });
    }

    /// <summary>The player's active effects as the HUD lists them.</summary>
    internal IReadOnlyList<DaggerfallActiveEffectView> ReadActiveEffects() =>
        DaggerfallMagicPresentation.Read(effects, actors.Player, effectItemName);

    /// <summary>
    /// Notes the device this update's input came from, when it carries one: a pad button or a stick moved past the
    /// controller deadzone (so idle drift never switches it), or a key, a mouse button or mouse look. The latest such
    /// event wins.
    /// </summary>
    internal void ObserveInputDevice(ReadOnlySpan<ProductInputEvent> input)
    {
        string? device = null;
        foreach (ProductInputEvent value in input)
        {
            if (value.Kind == InputEventKind.ControllerButton && value.Edge == InputEdge.Pressed) device = "controller";
            else if (value.Kind == InputEventKind.ControllerAxis && MathF.Abs(value.X) > deadzone) device = "controller";
            else if (value.Kind is InputEventKind.Key or InputEventKind.PointerButton && value.Edge == InputEdge.Pressed) device = "keyboard";
            else if (value.Kind == InputEventKind.PointerDelta && (value.X != 0 || value.Y != 0)) device = "keyboard";
        }
        InputDevice = device ?? InputDevice;
    }

    /// <summary>
    /// What ordinary use would do to the target the player aims at, named for the HUD's prompt; null
    /// outside play or when nothing usable is aimed at.
    /// </summary>
    private DaggerfallInteractionPromptView? Prompt(in DaggerfallHudSession session)
    {
        if (session.Mode != ProductMode.Playing || holdsWorldOpen() || aim.Module is not { } module) return null;
        if (module.FocusedTarget(actors.Player.Actor.Entity, player, playerInput.ResolveCurrentLook(player))
            is not { } target) return null;
        // The owner of what is aimed at words what use would do from its own state; where use would do
        // nothing (a door in Talk mode, a person in Bash), there is no prompt to promise it.
        if (module.PromptAction(target) is not { } action) return null;
        return new(action, string.IsNullOrWhiteSpace(target.Label) ? DaggerfallHudText.TargetName(target.Kind) : target.Label);
    }

    /// <summary>An escort's portrait in the art set drawn: a generic escort wears a race's head, which has refreshed art.</summary>
    private DaggerfallQuestPresentation DrawnEscorts(DaggerfallQuestPresentation escorts) => drawnArt() is { } art
        ? escorts with { EscortFaces = [.. escorts.EscortFaces.Select(face => face with { MediaId = art.Drawn(face.MediaId) })] }
        : escorts;
}
