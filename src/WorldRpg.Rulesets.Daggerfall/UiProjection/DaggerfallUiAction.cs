using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>One admitted Daggerfall player action: its typed kind plus the fields its wire shape carries.</summary>
internal sealed record DaggerfallPlayerUiAction(string Action, string? Revision = null, string? Item = null, int? TargetGrid = null, string? TargetEquipment = null, string? Container = null, string? Key = null, string? Label = null, bool Confirm = false,
    string? Name = null, string? Race = null, string? Gender = null, int? FaceIndex = null, int? Reflexes = null, string? Career = null, string? Mode = null,
    string? PrimarySkills = null, string? MajorSkills = null, string? MinorSkills = null, string? Advantages = null, string? Disadvantages = null, int? HitPointsPerLevel = null,
    string? Attribute = null, string? BackgroundAnswers = null, string? AttributeAllocations = null, string? SkillAllocations = null, ulong? Amount = null,
    string? QuestInstance = null, int? QuestMessage = null, int? QuestChoice = null, string? QuestPrompt = null, string? QuestDelivery = null,
    string? Note = null, string? Text = null, int? Page = null, int? Destination = null,
    string? Tone = null, string? Topic = null, int? Hours = null, int? Region = null, int? Days = null,
    int? X = null, int? Y = null, int? Index = null, int? Speed = null, bool Open = false, int? Question = null, int? Answer = null, float? Sensitivity = null, bool? Invert = null, bool? Enabled = null, float? Scale = null,
    IReadOnlyList<DaggerfallAtlasTileRequest>? Tiles = null, IReadOnlyList<double>? View = null, IReadOnlyList<double>? Point = null)
{
    /// <summary>The typed action the wire name names; resolved once when the action is built.</summary>
    internal DaggerfallUiActionKind Kind { get; } = DaggerfallUiAction.KindOf(Action);
}

/// <summary>Every player action the <c>dagger.ui.action.v1</c> contract carries, by meaning.</summary>
internal enum DaggerfallUiActionKind
{
    Begin, CinematicSkip, MapNoteAdd, MapNoteEdit, MapNoteRemove, ArtRequest, SpellReady, SpellUnready, SpellCast, SpellBuy, SpellDelete, SpellInfo, SpellSwap, SpellSort, SpellMakerDraft, SpellMakerBuy, PotionMix, ItemMakerDraft, ItemMakerBuy, DaedraSummon, DaedraAnswer,
    ControlsRebind, ControlsRebindButton, ControlsVolume, ControlsReset, ControlsLook, ControlsUiScale, ControlsGroundCover, ControlsClutterDistance, ControlsLampShadows, ControlsSurfaceRelief, ActionDrivenTime,
    CharacterClassQuestions, CharacterClassAnswer, CharacterClassBack,
    CharacterBegin, CharacterUpdate, CharacterBackgroundReroll, CharacterCommit, CharacterCancel, CharacterQuickStart,
    CharacterLevelAllocate, CharacterLevelCommit,
    ActivationMode, Attack, Loot, WeaponToggle, Menu, TutorialStart, TutorialContinue, TutorialEnd, InputClaimed,
    DialogueTone, DialogueTopic, DialogueClose, TavernBuy, TrainingCommit, MerchantBuy, MerchantSell, MerchantRepair, MerchantCollectRepair, MerchantIdentify, MerchantShoplift, MerchantQuote,
    LegalChoice, CreateItemSelect, CreateItemCancel, DispelSelect, DispelCancel, TeleportSelect, IdentifySelect, IdentifyCancel,
    TransportSelect, TransportLeaveShip, TransportBoardShip,
    PropertyBuy, PropertySell, PropertyEnter, PropertyPut, PropertyTake,
    ExpeditionOpen, ExpeditionPlaces, ExpeditionSpeed, ExpeditionMode, ExpeditionGo, ExpeditionStop, ExpeditionPin, ExpeditionFind,
    ExpeditionShip, ExpeditionLeave, ExpeditionEnter, ExpeditionView, ExpeditionFollow, ExpeditionLook, ExpeditionGoAt, ExpeditionPinAt,
    WorldMap, WorldMapFind, WorldMapPin, WorldMapTravel, AtlasTiles, MapOpen, MapBuilding,
    Rest, LodgingQuote, LodgingBook,
    WagonPut, WagonTake,
    QuestChoice, QuestDismiss, QuestOfferAnswer,
    DungeonTextAnswer, DungeonTextClose,
    InventoryMove, InventoryInspect, InventoryUse, InventoryDrop, InventorySort,
    NotebookPage, NotebookAdd, NotebookEdit, NotebookRemove, NotebookMove,
    CurrencyDepositGold, CurrencyWithdrawGold, CurrencyDepositLetters, CurrencyWithdrawLetter, BankTransfer,
    BankOpen, BankLoanIssue, BankLoanRepayAccount, BankLoanRepayCarried,
    LootClose, LootTake, LootTakeAll,
    SaveGame, LoadGame, SaveSlots, SaveSlot, LoadSlot, DeleteSlot, BundleSelect,
    DeathNewGame, DeathLoadGame, DeathQuit,
    Quit,
    ContentImport, ContentImportCancel,
}

/// <summary>Which of the session's input phases an action is admitted in.</summary>
[Flags]
internal enum DaggerfallUiPhases
{
    None = 0,
    /// <summary>Ordinary play with no cinematic over it.</summary>
    Playing = 1,
    /// <summary>A modal interaction holds the world.</summary>
    Modal = 2,
    /// <summary>The entry screen, a pause, or a cinematic over play.</summary>
    Held = 4,
    /// <summary>The player is dead and the Host is choosing the replacement.</summary>
    Dead = 8,
    Interaction = Playing | Modal,
    Live = Playing | Modal | Held,
}

/// <summary>
/// One action's declared admission: the wire name, the phases it acts in, and what a live phase that
/// does not admit it tells the player (null drops it silently, as a held world drops play input).
/// </summary>
internal sealed record DaggerfallUiActionRule(DaggerfallUiActionKind Kind, string Wire, DaggerfallUiPhases Phases, string? Refusal = null)
{
    internal bool Admits(DaggerfallUiPhases phase) => (Phases & phase) != 0;
}

/// <summary>
/// One admitted update's Daggerfall UI payloads, each parsed once, aligned with the update's input
/// events. Every reader in the update (the begin check, the interaction pre-scan and the dispatch)
/// reads this one list.
/// </summary>
internal sealed class DaggerfallUiInput
{
    private readonly (ReadOnlyMemory<byte> Payload, bool IsUiAction, DaggerfallPlayerUiAction? Action)[] _events;

    private DaggerfallUiInput((ReadOnlyMemory<byte>, bool, DaggerfallPlayerUiAction?)[] events) => _events = events;

    internal static DaggerfallUiInput Parse(ReadOnlySpan<ProductInputEvent> input)
    {
        var events = new (ReadOnlyMemory<byte>, bool, DaggerfallPlayerUiAction?)[input.Length];
        for (int index = 0; index < input.Length; index++)
        {
            ProductInputEvent value = input[index];
            bool isUiAction = value.ValueKind == InputValueKind.ProductPayload
                && value.PayloadContract.Span.SequenceEqual(DaggerfallUiAction.Contract);
            events[index] = (value.PayloadData, isUiAction, isUiAction ? DaggerfallUiAction.Parse(value.PayloadData.Span) : null);
        }
        return new(events);
    }

    /// <summary>Whether this parse was made from exactly these events, so a second reader can reuse it.</summary>
    internal bool Matches(ReadOnlySpan<ProductInputEvent> input)
    {
        if (input.Length != _events.Length) return false;
        for (int index = 0; index < input.Length; index++)
        {
            ProductInputEvent value = input[index];
            bool isUiAction = value.ValueKind == InputValueKind.ProductPayload
                && value.PayloadContract.Span.SequenceEqual(DaggerfallUiAction.Contract);
            if (isUiAction != _events[index].IsUiAction) return false;
            if (isUiAction && !value.PayloadData.Span.SequenceEqual(_events[index].Payload.Span)) return false;
        }
        return true;
    }

    /// <summary>Whether the event at this index is a Daggerfall UI payload, recognized or not.</summary>
    internal bool IsUiAction(int index) => _events[index].IsUiAction;

    /// <summary>The recognized action at this index, or null for an unrecognized payload or another event.</summary>
    internal DaggerfallPlayerUiAction? ActionAt(int index) => _events[index].Action;

    /// <summary>The recognized actions, in the order they arrived.</summary>
    internal IEnumerable<DaggerfallPlayerUiAction> Actions => _events.Select(entry => entry.Action).OfType<DaggerfallPlayerUiAction>();

    internal bool Contains(DaggerfallUiActionKind kind) => _events.Any(entry => entry.Action?.Kind == kind);

    internal bool ContainsAny(params DaggerfallUiActionKind[] kinds) => _events.Any(entry => entry.Action is { } action && kinds.Contains(action.Kind));
}

/// <summary>The small Daggerfall player-action wire contract, consumed during admitted updates.</summary>
internal static class DaggerfallUiAction
{
    internal const string BeginAction = "begin";

    /// <summary>The product payload contract every Daggerfall UI action travels under.</summary>
    internal static ReadOnlySpan<byte> Contract => "dagger.ui.action.v1"u8;

    private const string Unrecognized = "Unrecognized player UI action.";

    /// <summary>
    /// Every action's admission. A death choice outside death is not an action the live session
    /// recognizes; a quest choice outside play reports the same rejection a stale prompt does.
    /// </summary>
    internal static readonly IReadOnlyList<DaggerfallUiActionRule> Rules =
    [
        // The entry screen's own action is the product's to answer, so the session knows the shape
        // and does nothing with it.
        new(DaggerfallUiActionKind.Begin, BeginAction, DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CinematicSkip, "cinematic-skip", DaggerfallUiPhases.Live),
        // Art requests are presentation maintenance, so a reloaded death screen can still recover
        // its admitted image while gameplay and menu actions remain suppressed.
        new(DaggerfallUiActionKind.ArtRequest, "art-request", DaggerfallUiPhases.Live | DaggerfallUiPhases.Dead),
        new(DaggerfallUiActionKind.ControlsRebind, "controls-rebind", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsRebindButton, "controls-rebind-button", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsVolume, "controls-volume", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsLook, "controls-look", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsUiScale, "controls-ui-scale", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsGroundCover, "controls-ground-cover", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsClutterDistance, "controls-clutter-distance", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsLampShadows, "controls-lamp-shadows", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsSurfaceRelief, "controls-surface-relief", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ActionDrivenTime, "action-driven-time", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.ControlsReset, "controls-reset", DaggerfallUiPhases.Live),
        // Character creation checks the entry screen itself, so a refusal is reported rather than dropped.
        new(DaggerfallUiActionKind.CharacterClassQuestions, "character-class-questions", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterClassAnswer, "character-class-answer", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterClassBack, "character-class-back", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterBegin, "character-begin", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterUpdate, "character-update", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterBackgroundReroll, "character-background-reroll", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterCommit, "character-commit", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterQuickStart, "character-quick-start", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterCancel, "character-cancel", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterLevelAllocate, "character-level-allocate", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CharacterLevelCommit, "character-level-commit", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ActivationMode, "activation-mode", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellReady,"spell-ready",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellUnready,"spell-unready",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellCast,"spell-cast",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DaedraSummon,"daedra-summon",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DaedraAnswer,"daedra-answer",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ItemMakerDraft,"itemmaker-draft",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ItemMakerBuy,"itemmaker-buy",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PotionMix,"potion-mix",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellMakerDraft,"spellmaker-draft",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellMakerBuy,"spellmaker-buy",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellBuy,"spell-buy",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellDelete,"spell-delete",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellSwap,"spell-swap",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellSort,"spell-sort",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellInfo,"spell-info",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.Attack, "attack", DaggerfallUiPhases.Playing),
        new(DaggerfallUiActionKind.Loot, "loot", DaggerfallUiPhases.Playing),
        // The command radial readies or sheathes the weapon as its key does, on the first step play admits.
        new(DaggerfallUiActionKind.WeaponToggle, "weapon-toggle", DaggerfallUiPhases.Live),
        // A world act's binding pressed while a DOM surface held the cursor, so it never reached play: the developer's
        // input readout names it (item, the binding) and the surface (key).
        new(DaggerfallUiActionKind.InputClaimed, "input-claimed", DaggerfallUiPhases.Live | DaggerfallUiPhases.Dead),
        new(DaggerfallUiActionKind.TutorialStart, "tutorial-start", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TutorialContinue, "tutorial-continue", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TutorialEnd, "tutorial-end", DaggerfallUiPhases.Interaction),
        // The DOM reports whether its game menu (and so any of its panels) is open, because an open
        // menu holds the world. A menu closed over death still reaches the session.
        new(DaggerfallUiActionKind.Menu, "menu", DaggerfallUiPhases.Live | DaggerfallUiPhases.Dead),
        new(DaggerfallUiActionKind.IdentifySelect, "identify-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.IdentifyCancel, "identify-cancel", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LegalChoice, "legal-choice", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CreateItemSelect, "create-item-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CreateItemCancel, "create-item-cancel", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DispelSelect, "dispel-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DispelCancel, "dispel-cancel", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TeleportSelect, "teleport-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DialogueTone, "dialogue-tone", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DialogueTopic, "dialogue-topic", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DialogueClose, "dialogue-close", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TavernBuy, "tavern-buy", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TrainingCommit, "training-commit", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantBuy, "merchant-buy", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantSell, "merchant-sell", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantRepair, "merchant-repair", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantCollectRepair, "merchant-collect-repair", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantIdentify, "merchant-identify", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantShoplift, "merchant-shoplift", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MerchantQuote, "merchant-quote", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportSelect, "transport-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportBoardShip, "transport-board-ship", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyBuy, "property-buy", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertySell, "property-sell", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyEnter, "property-enter", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyPut, "property-put", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyTake, "property-take", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportLeaveShip, "transport-leave-ship", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MapOpen, "map-open", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.MapBuilding, "map-building", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MapNoteAdd, "map-note-add", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MapNoteEdit, "map-note-edit", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MapNoteRemove, "map-note-remove", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionOpen, "expedition-open", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionPlaces, "expedition-places", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionSpeed, "expedition-speed", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionMode, "expedition-mode", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionGo, "expedition-go", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionStop, "expedition-stop", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionPin, "expedition-pin", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionFind, "expedition-find", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionShip, "expedition-ship", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionLeave, "expedition-leave", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionEnter, "expedition-enter", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionView, "expedition-view", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionFollow, "expedition-follow", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionLook, "expedition-look", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionGoAt, "expedition-go-at", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ExpeditionPinAt, "expedition-pin-at", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WorldMap, "world-map", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.WorldMapFind, "world-map-find", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WorldMapPin, "world-map-pin", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WorldMapTravel, "world-map-travel", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.AtlasTiles, "atlas-tiles", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LodgingQuote, "lodging-quote", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LodgingBook, "lodging-book", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.Rest, "rest", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WagonPut, "wagon-put", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WagonTake, "wagon-take", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.QuestChoice, "quest-choice", DaggerfallUiPhases.Interaction,
            "Quest choice rejected: this prompt is no longer pending or the choice is invalid."),
        new(DaggerfallUiActionKind.QuestOfferAnswer, "quest-offer-answer", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.QuestDismiss, "quest-dismiss", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DungeonTextAnswer, "dungeon-text-answer", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DungeonTextClose, "dungeon-text-close", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryMove, "inventory-move", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryInspect, "inventory-inspect", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryUse, "inventory-use", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryDrop, "inventory-drop", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventorySort, "inventory-sort", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.NotebookPage, "notebook-page", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.NotebookAdd, "notebook-add", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.NotebookEdit, "notebook-edit", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.NotebookRemove, "notebook-remove", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.NotebookMove, "notebook-move", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CurrencyDepositGold, "currency-deposit-gold", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CurrencyWithdrawGold, "currency-withdraw-gold", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CurrencyDepositLetters, "currency-deposit-letters", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CurrencyWithdrawLetter, "currency-withdraw-letter", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.BankTransfer, "bank-transfer", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.BankOpen, "bank-open", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.BankLoanIssue, "bank-loan-issue", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.BankLoanRepayAccount, "bank-loan-repay-account", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.BankLoanRepayCarried, "bank-loan-repay-carried", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LootClose, "loot-close", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LootTake, "loot-take", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LootTakeAll, "loot-take-all", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SaveGame, "save-game", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LoadGame, "load-game", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SaveSlots, "save-slots", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SaveSlot, "save-slot", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LoadSlot, "load-slot", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.BundleSelect, "bundle-select", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.DeleteSlot, "delete-slot", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.DeathNewGame, DaggerfallDeathPresentation.NewGameAction, DaggerfallUiPhases.Dead, Unrecognized),
        new(DaggerfallUiActionKind.DeathLoadGame, DaggerfallDeathPresentation.LoadGameAction, DaggerfallUiPhases.Dead, Unrecognized),
        new(DaggerfallUiActionKind.DeathQuit, DaggerfallDeathPresentation.QuitAction, DaggerfallUiPhases.Dead, Unrecognized),
        // Quit ends the product from the title screen, the game menu or the death screen alike.
        new(DaggerfallUiActionKind.Quit, "quit-game", DaggerfallUiPhases.Live | DaggerfallUiPhases.Dead),
        // The entry screen's import, which only the unavailable session acts on: a game that runs has its content.
        new(DaggerfallUiActionKind.ContentImport, "content-import", DaggerfallUiPhases.Held),
        new(DaggerfallUiActionKind.ContentImportCancel, "content-import-cancel", DaggerfallUiPhases.Held),
    ];

    private static readonly Dictionary<string, DaggerfallUiActionRule> ByWire = Rules.ToDictionary(rule => rule.Wire, StringComparer.Ordinal);
    private static readonly Dictionary<DaggerfallUiActionKind, DaggerfallUiActionRule> ByKind = Rules.ToDictionary(rule => rule.Kind);

    /// <summary>What a live phase tells the player about a payload no rule recognizes.</summary>
    internal static string UnrecognizedRefusal => Unrecognized;

    internal static DaggerfallUiActionKind KindOf(string wire) =>
        ByWire.TryGetValue(wire ?? throw new ArgumentNullException(nameof(wire)), out DaggerfallUiActionRule? rule)
            ? rule.Kind
            : throw new ArgumentException($"'{wire}' is not a Daggerfall UI action.", nameof(wire));

    internal static DaggerfallUiActionRule RuleFor(DaggerfallUiActionKind kind) => ByKind[kind];

    internal static DaggerfallPlayerUiAction? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > 4096) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            HashSet<string> fields = new(StringComparer.Ordinal);
            string? action = null, revision = null, item = null, targetEquipment = null, container = null, key = null, label = null, name = null, race = null, gender = null, career = null, mode = null, primarySkills = null, majorSkills = null, minorSkills = null, advantages = null, disadvantages = null, attribute = null, backgroundAnswers = null, attributeAllocations = null, skillAllocations = null, questInstance = null, questPrompt = null, questDelivery = null, note = null, text = null, tone = null, topic = null;
            int? targetGrid = null, faceIndex = null, reflexes = null, hitPointsPerLevel = null, questMessage = null, page = null, destination = null, hours = null, region = null, days = null,
                x = null, y = null, index = null, speed = null;
            ulong? amount = null;
            float? sensitivity = null, scale = null; bool? invert = null, enabled = null;
            bool confirm = false, open = false;
            int? questChoice = null, classQuestion = null, classAnswer = null;
            List<DaggerfallAtlasTileRequest>? tiles = null;
            double[]? view = null, point = null;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name)) return null;
                if (property.Name is "view" or "point")
                {
                    // The travel map's view moves [turn, tilt, right, forward, zoom], or a point of it [across, down].
                    int count = property.Name == "view" ? 5 : 2;
                    if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() != count) return null;
                    double[] numbers = new double[count];
                    for (int at = 0; at < count; at++)
                        if (!property.Value[at].TryGetDouble(out numbers[at]) || !double.IsFinite(numbers[at])) return null;
                    if (property.Name == "view") view = numbers; else point = numbers;
                    continue;
                }
                if (property.Name is "x" or "y" or "index" or "speed")
                {
                    if (!property.Value.TryGetInt32(out int number) || number < 0) return null;
                    if (property.Name == "x") x = number;
                    else if (property.Name == "y") y = number;
                    else if (property.Name == "index") index = number;
                    else speed = number;
                    continue;
                }
                if (property.Name is "targetGrid" or "faceIndex" or "reflexes" or "hitPointsPerLevel" or "questMessage" or "page" or "destination" or "hours" or "region" or "days")
                {
                    if (!property.Value.TryGetInt32(out int grid)) return null;
                    if (property.Name == "targetGrid") targetGrid = grid;
                    else if (property.Name == "faceIndex") faceIndex = grid;
                    else if (property.Name == "reflexes") reflexes = grid;
                    else if (property.Name == "hitPointsPerLevel") hitPointsPerLevel = grid;
                    else if (property.Name == "questMessage") questMessage = grid;
                    else if (property.Name == "page") page = grid;
                    else if (property.Name == "destination") destination = grid;
                    else if (property.Name == "hours") hours = grid;
                    else if (property.Name == "days") days = grid;
                    else region = grid;
                    continue;
                }
                if (property.Name == "tiles")
                {
                    // [[level, column, row], ...]: a bounded read of what the page shows.
                    if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() is 0 or > Content.DaggerfallAtlas.MaximumTilesPerRequest) return null;
                    tiles = [];
                    foreach (JsonElement tile in property.Value.EnumerateArray())
                    {
                        if (tile.ValueKind != JsonValueKind.Array || tile.GetArrayLength() != 3
                            || !tile[0].TryGetInt32(out int level) || !tile[1].TryGetInt32(out int column) || !tile[2].TryGetInt32(out int row)) return null;
                        tiles.Add(new(level, column, row));
                    }
                    continue;
                }
                if (property.Name == "sensitivity")
                {
                    if (!property.Value.TryGetSingle(out float number) || !float.IsFinite(number) || number <= 0) return null;
                    sensitivity = number; continue;
                }
                if (property.Name == "scale")
                {
                    if (!property.Value.TryGetSingle(out float number) || !float.IsFinite(number) || number <= 0) return null;
                    scale = number; continue;
                }
                if (property.Name == "enabled")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return null;
                    enabled = property.Value.GetBoolean(); continue;
                }
                if (property.Name == "invert")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return null;
                    invert = property.Value.GetBoolean(); continue;
                }
                if (property.Name == "amount")
                {
                    if (!property.Value.TryGetUInt64(out ulong parsed)) return null;
                    amount = parsed;
                    continue;
                }
                if (property.Name is "confirm" or "open")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return null;
                    if (property.Name == "confirm") confirm = property.Value.GetBoolean();
                    else open = property.Value.GetBoolean();
                    continue;
                }
                if (property.Name is "question" or "answer")
                {
                    if (!property.Value.TryGetInt32(out int selected)) return null;
                    if (property.Name == "question") classQuestion = selected; else classAnswer = selected;
                    continue;
                }
                if (property.Name == "questChoice")
                {
                    if (!property.Value.TryGetInt32(out int selected) || selected < 0) return null;
                    questChoice = selected;
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String) return null;
                string? value = property.Value.GetString();
                switch (property.Name)
                {
                    case "action": action = value; break;
                    case "revision": revision = value; break;
                    case "item": item = value; break;
                    case "targetEquipment": targetEquipment = value; break;
                    case "container": container = value; break;
                    case "key": key = value; break;
                    case "label": label = value; break;
                    case "name": name = value; break;
                    case "race": race = value; break;
                    case "gender": gender = value; break;
                    case "career": career = value; break;
                    case "mode": mode = value; break;
                    case "primarySkills": primarySkills = value; break;
                    case "majorSkills": majorSkills = value; break;
                    case "minorSkills": minorSkills = value; break;
                    case "advantages": advantages = value; break;
                    case "disadvantages": disadvantages = value; break;
                    case "attribute": attribute = value; break;
                    case "backgroundAnswers": backgroundAnswers = value; break;
                    case "attributeAllocations": attributeAllocations = value; break;
                    case "skillAllocations": skillAllocations = value; break;
                    case "questInstance": questInstance = value; break;
                    case "questPrompt": questPrompt = value; break;
                    case "questDelivery": questDelivery = value; break;
                    case "note": note = value; break;
                    case "text": text = value; break;
                    case "tone": tone = value; break;
                    case "topic": topic = value; break;
                    default: return null;
                }
            }
            // The wire is named once, by its rule: a payload no rule names is no action, and the dispatch
            // reports it as unrecognized.
            if (action is null || !ByWire.TryGetValue(action, out DaggerfallUiActionRule? rule)) return null;
            if (amount == 0 && rule.Kind is not (DaggerfallUiActionKind.LodgingBook
                or DaggerfallUiActionKind.SpellBuy or DaggerfallUiActionKind.ItemMakerBuy)) return null;
            switch (rule.Kind)
            {
                case DaggerfallUiActionKind.DaedraSummon:
                    return fields.SetEquals(["action","revision","key","amount","confirm"]) && !string.IsNullOrWhiteSpace(revision)
                        && !string.IsNullOrWhiteSpace(key) && amount is not null ? new(action, Revision:revision, Key:key, Amount:amount, Confirm:confirm) : null;
                case DaggerfallUiActionKind.DaedraAnswer:
                    return fields.SetEquals(["action","revision","confirm"]) && !string.IsNullOrWhiteSpace(revision)
                        ? new(action,Revision:revision,Confirm:confirm) : null;
                case DaggerfallUiActionKind.LodgingQuote:
                    return fields.SetEquals(["action", "key", "days"]) && !string.IsNullOrWhiteSpace(key) && days is >= 1 and <= 350
                        ? new(action, Key: key, Days: days) : null;
                case DaggerfallUiActionKind.LodgingBook:
                    return fields.SetEquals(["action", "key", "days", "amount"]) && !string.IsNullOrWhiteSpace(key) && days is >= 1 and <= 350 && amount is not null
                        ? new(action, Key: key, Days: days, Amount: amount) : null;
                case DaggerfallUiActionKind.InventoryMove:
                {
                    if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(item)
                        || (targetGrid is null) == (targetEquipment is null) || fields.Count != 4) return null;
                    return new(action, revision, item, targetGrid, targetEquipment);
                }
                case DaggerfallUiActionKind.InventoryInspect:
                    return fields.SetEquals(["action", "revision", "item"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        ? new(action, revision, item) : null;
                case DaggerfallUiActionKind.InventoryUse:
                    return fields.SetEquals(["action", "revision", "item"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        ? new(action, revision, item) : null;
                case DaggerfallUiActionKind.NotebookPage:
                    return fields.SetEquals(["action", "revision", "page"]) && !string.IsNullOrWhiteSpace(revision) && page is >= 0
                        ? new(action, Revision: revision, Page: page) : null;
                case DaggerfallUiActionKind.NotebookAdd:
                    return fields.SetEquals(["action", "revision", "text"]) && !string.IsNullOrWhiteSpace(revision) && ValidNotebookText(text)
                        ? new(action, Revision: revision, Text: text) : null;
                case DaggerfallUiActionKind.NotebookEdit:
                    return fields.SetEquals(["action", "revision", "note", "text"]) && !string.IsNullOrWhiteSpace(revision)
                        && !string.IsNullOrWhiteSpace(note) && ValidNotebookText(text)
                        ? new(action, Revision: revision, Note: note, Text: text) : null;
                case DaggerfallUiActionKind.NotebookRemove:
                    return fields.SetEquals(["action", "revision", "note"]) && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(note)
                        ? new(action, Revision: revision, Note: note) : null;
                case DaggerfallUiActionKind.NotebookMove:
                    return fields.SetEquals(["action", "revision", "note", "destination"]) && !string.IsNullOrWhiteSpace(revision)
                        && !string.IsNullOrWhiteSpace(note) && destination is >= 0
                        ? new(action, Revision: revision, Note: note, Destination: destination) : null;
                case DaggerfallUiActionKind.SpellMakerDraft or DaggerfallUiActionKind.PotionMix or DaggerfallUiActionKind.ItemMakerDraft:
                    return fields.SetEquals(["action", "revision", "text"]) && !string.IsNullOrWhiteSpace(revision)
                        && text is { Length: > 0 and <= 3000 }
                        ? new(action, Revision: revision, Text: text) : null;
                case DaggerfallUiActionKind.SpellBuy or DaggerfallUiActionKind.SpellMakerBuy or DaggerfallUiActionKind.ItemMakerBuy:
                    return fields.SetEquals(["action", "key", "revision", "amount", "confirm"]) && !string.IsNullOrWhiteSpace(key)
                        && !string.IsNullOrWhiteSpace(revision) && amount is not null
                        ? new(action, Key: key, Revision: revision, Amount: amount, Confirm: confirm) : null;
                case DaggerfallUiActionKind.SpellDelete:
                    return fields.SetEquals(["action", "key", "confirm"]) && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Key: key, Confirm: confirm) : null;
                case DaggerfallUiActionKind.InventorySort: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.SpellSort: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.SpellSwap:
                    return fields.SetEquals(["action", "key", "item"]) && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(item)
                        ? new(action, Key: key, Item: item) : null;
                case DaggerfallUiActionKind.SpellInfo:
                    return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key) ? new(action, Key: key) : null;
                case DaggerfallUiActionKind.SpellReady:
                    return fields.SetEquals(["action","key"]) && !string.IsNullOrWhiteSpace(key) ? new(action,Key:key) : null;
                case DaggerfallUiActionKind.SpellUnready or DaggerfallUiActionKind.SpellCast:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.InventoryDrop:
                    return fields.SetEquals(["action", "revision", "item", "amount"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && amount is not null
                        ? new(action, revision, item, Amount: amount) : null;
                case DaggerfallUiActionKind.LootTakeAll:
                    return fields.SetEquals(["action", "revision", "container"]) && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(container)
                        ? new(action, revision, Container: container) : null;
                case DaggerfallUiActionKind.LootTake:
                {
                    bool shape = fields.SetEquals(["action", "revision", "item", "container"])
                        || fields.SetEquals(["action", "revision", "item", "container", "amount"]);
                    return shape && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(container)
                        ? new(action, revision, item, Container: container, Amount: amount) : null;
                }
                case DaggerfallUiActionKind.CurrencyDepositGold or DaggerfallUiActionKind.CurrencyWithdrawGold or DaggerfallUiActionKind.CurrencyWithdrawLetter:
                    return fields.SetEquals(["action", "amount"]) && amount is not null ? new(action, Amount: amount) : null;
                case DaggerfallUiActionKind.CurrencyDepositLetters:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.BankTransfer:
                    return fields.SetEquals(["action", "amount", "destination"]) && amount is not null && destination is >= 0
                        ? new(action, Amount: amount, Destination: destination) : null;
                case DaggerfallUiActionKind.BankLoanIssue or DaggerfallUiActionKind.BankLoanRepayAccount or DaggerfallUiActionKind.BankLoanRepayCarried:
                    return fields.SetEquals(["action", "amount"]) && amount is not null
                        ? new(action, Amount: amount) : null;
                case DaggerfallUiActionKind.ExpeditionFind:
                    return fields.SetEquals(["action", "text"]) && text is { Length: <= 80 }
                        ? new(action, Text: text) : null;
                case DaggerfallUiActionKind.ExpeditionSpeed:
                    return fields.SetEquals(["action", "speed"]) && speed is not null ? new(action, Speed: speed) : null;
                case DaggerfallUiActionKind.ExpeditionMode:
                    return fields.SetEquals(["action", "mode"]) && mode is "cautious" or "reckless" or "halt" or "camp" ? new(action, Mode: mode) : null;
                // A point on the map, or a place there: its pixel, with the place's region and index.
                case DaggerfallUiActionKind.ExpeditionGo or DaggerfallUiActionKind.ExpeditionPin:
                    if (rule.Kind == DaggerfallUiActionKind.ExpeditionPin && fields.SetEquals(["action"])) return new(action);
                    return (fields.SetEquals(["action", "x", "y"]) || fields.SetEquals(["action", "x", "y", "region", "index"]) && region is >= 0)
                        && x is < Travel.DaggerfallTravelMapPixel.Width && y is < Travel.DaggerfallTravelMapPixel.Height
                        ? new(action, X: x, Y: y, Region: region, Index: index) : null;
                // A view's move is bounded: a turn or tilt within a half turn, a pan within a few eye distances, a few zoom steps.
                case DaggerfallUiActionKind.ExpeditionView:
                    return fields.SetEquals(["action", "view"]) && view is [var turn, var tilt, var right, var forward, var zoom]
                        && Math.Abs(turn) <= 180 && Math.Abs(tilt) <= 90 && Math.Abs(right) <= 4 && Math.Abs(forward) <= 4 && Math.Abs(zoom) <= 8
                        ? new(action, View: view) : null;
                case DaggerfallUiActionKind.ExpeditionLook:
                    return fields.SetEquals(["action", "x", "y"]) && x is < Travel.DaggerfallTravelMapPixel.Width && y is < Travel.DaggerfallTravelMapPixel.Height
                        ? new(action, X: x, Y: y) : null;
                case DaggerfallUiActionKind.ExpeditionGoAt or DaggerfallUiActionKind.ExpeditionPinAt:
                    return fields.SetEquals(["action", "point"]) && point is [>= 0 and <= 1, >= 0 and <= 1] ? new(action, Point: point) : null;
                case DaggerfallUiActionKind.WorldMap: return fields.SetEquals(["action", "open"]) ? new(action, Open: open) : null;
                case DaggerfallUiActionKind.WorldMapFind:
                    return fields.SetEquals(["action", "text"]) && text is { Length: <= 80 } ? new(action, Text: text) : null;
                case DaggerfallUiActionKind.WorldMapPin or DaggerfallUiActionKind.WorldMapTravel:
                    if (rule.Kind == DaggerfallUiActionKind.WorldMapPin && fields.SetEquals(["action"])) return new(action);
                    return (fields.SetEquals(["action", "x", "y"]) || fields.SetEquals(["action", "x", "y", "region", "index"]) && region is >= 0)
                        && x is < Travel.DaggerfallTravelMapPixel.Width && y is < Travel.DaggerfallTravelMapPixel.Height
                        ? new(action, X: x, Y: y, Region: region, Index: index) : null;
                case DaggerfallUiActionKind.ExpeditionOpen or DaggerfallUiActionKind.ExpeditionPlaces or DaggerfallUiActionKind.ExpeditionStop
                    or DaggerfallUiActionKind.ExpeditionFollow
                    or DaggerfallUiActionKind.ExpeditionShip or DaggerfallUiActionKind.ExpeditionLeave or DaggerfallUiActionKind.ExpeditionEnter:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.AtlasTiles: return fields.SetEquals(["action", "tiles"]) && tiles is not null ? new(action, Tiles: tiles) : null;
                case DaggerfallUiActionKind.DungeonTextAnswer:
                    return fields.SetEquals(["action", "revision", "item", "text"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        && text is { Length: <= 256 }
                        ? new(action, Revision: revision, Item: item, Text: text) : null;
                case DaggerfallUiActionKind.DungeonTextClose:
                    return fields.SetEquals(["action", "revision", "item"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        ? new(action, Revision: revision, Item: item) : null;
                case DaggerfallUiActionKind.ArtRequest:
                    return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                        ? new(action, revision) : null;
                case DaggerfallUiActionKind.LootClose:
                    return fields.SetEquals(["action", "container"]) && !string.IsNullOrWhiteSpace(container)
                        ? new(action, Container: container) : null;
                case DaggerfallUiActionKind.ControlsClutterDistance or DaggerfallUiActionKind.ControlsSurfaceRelief:
                    return fields.SetEquals(["action", "scale"]) && scale.HasValue ? new(action, Scale: scale) : null;
                // Which lamps cast (item), in sectors of what share of the tuned side (scale), and whether outdoors too (enabled).
                case DaggerfallUiActionKind.ControlsLampShadows: return fields.SetEquals(["action", "item", "scale", "enabled"])
                    && !string.IsNullOrWhiteSpace(item) && scale.HasValue && enabled.HasValue ? new(action, Item: item, Scale: scale, Enabled: enabled) : null;
                case DaggerfallUiActionKind.ActionDrivenTime or DaggerfallUiActionKind.ControlsGroundCover: return fields.SetEquals(["action", "enabled"])
                    && enabled.HasValue ? new(action, Enabled: enabled) : null;
                case DaggerfallUiActionKind.ControlsLook: return fields.SetEquals(["action", "sensitivity", "invert"])
                    && sensitivity.HasValue && invert.HasValue ? new(action, Sensitivity: sensitivity, Invert: invert) : null;
                // No scale is the player choosing automatic: the DOM follows the window.
                case DaggerfallUiActionKind.ControlsUiScale: return fields.IsSubsetOf(["action", "scale"]) ? new(action, Scale: scale) : null;
                case DaggerfallUiActionKind.ControlsRebind:
                    return fields.IsSubsetOf(["action", "item", "key", "confirm"])
                        && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Item: item, Key: key, Confirm: confirm) : null;
                case DaggerfallUiActionKind.ControlsVolume:
                    return fields.SetEquals(["action", "item", "scale"]) && !string.IsNullOrWhiteSpace(item) && scale.HasValue
                        ? new(action, Item: item, Scale: scale) : null;
                case DaggerfallUiActionKind.ControlsRebindButton:
                    return fields.IsSubsetOf(["action", "item", "amount", "confirm"]) && fields.IsSupersetOf(["action", "item", "amount"])
                        && !string.IsNullOrWhiteSpace(item) && amount is >= 0
                        ? new(action, Item: item, Amount: amount, Confirm: confirm) : null;
                case DaggerfallUiActionKind.ControlsReset: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.MapOpen: return fields.SetEquals(["action", "open"]) ? new(action, Open: open) : null;
                case DaggerfallUiActionKind.MapBuilding: return fields.SetEquals(["action", "region", "destination", "item"])
                    && region is >= 0 && destination is >= 0 && !string.IsNullOrWhiteSpace(item)
                    ? new(action, Region: region, Destination: destination, Item: item) : null;
                case DaggerfallUiActionKind.MapNoteAdd:
                    return fields.SetEquals(["action", "text"]) && ValidMapNoteText(text) ? new(action, Text: text) : null;
                case DaggerfallUiActionKind.MapNoteEdit:
                    return fields.SetEquals(["action", "note", "text"]) && !string.IsNullOrWhiteSpace(note) && ValidMapNoteText(text)
                        ? new(action, Note: note, Text: text) : null;
                case DaggerfallUiActionKind.MapNoteRemove:
                    return fields.SetEquals(["action", "note"]) && !string.IsNullOrWhiteSpace(note) ? new(action, Note: note) : null;
                case DaggerfallUiActionKind.Menu: return fields.SetEquals(["action", "open"]) ? new(action, Open: open) : null;
                case DaggerfallUiActionKind.WeaponToggle: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.InputClaimed:
                    return fields.SetEquals(["action", "item", "key"]) && item is not null && DaggerfallInput.WorldAct(item) is not null
                        && key is { Length: > 0 and <= 40 } && key.All(letter => char.IsAsciiLetterLower(letter) || letter == ' ')
                        ? new(action, Item: item, Key: key) : null;
                case DaggerfallUiActionKind.TutorialStart or DaggerfallUiActionKind.TutorialEnd: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.TutorialContinue: return fields.SetEquals(["action", "page"]) && page is >= 0 ? new(action, Page: page) : null;
                case DaggerfallUiActionKind.SaveSlots: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.ContentImport or DaggerfallUiActionKind.ContentImportCancel: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.SaveSlot:
                    return fields.IsSubsetOf(["action", "key", "label", "confirm"])
                        && !string.IsNullOrWhiteSpace(label)
                        && (key is null || !string.IsNullOrWhiteSpace(key))
                        ? new(action, Key: key, Label: label, Confirm: confirm) : null;
                case DaggerfallUiActionKind.BundleSelect:
                    return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Key: key) : null;
                case DaggerfallUiActionKind.LoadSlot:
                    return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Key: key) : null;
                case DaggerfallUiActionKind.DeleteSlot:
                    return fields.IsSubsetOf(["action", "key", "confirm"])
                        && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Key: key, Confirm: confirm) : null;
                case DaggerfallUiActionKind.DeathNewGame or DaggerfallUiActionKind.DeathQuit or DaggerfallUiActionKind.Quit:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.DeathLoadGame:
                    return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Key: key) : null;
                case DaggerfallUiActionKind.CharacterBegin or DaggerfallUiActionKind.CharacterCancel:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.CharacterLevelAllocate:
                    return fields.SetEquals(["action", "attribute"]) && !string.IsNullOrWhiteSpace(attribute)
                        ? new(action, Attribute: attribute) : null;
                case DaggerfallUiActionKind.CharacterLevelCommit:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.CharacterUpdate or DaggerfallUiActionKind.CharacterCommit or DaggerfallUiActionKind.CharacterBackgroundReroll or DaggerfallUiActionKind.CharacterClassQuestions:
                {
                    HashSet<string> required = career == DaggerfallCustomCareerPolicy.CareerId
                        ? ["action", "name", "race", "gender", "faceIndex", "reflexes", "career", "primarySkills", "majorSkills", "minorSkills", "hitPointsPerLevel", "advantages", "disadvantages"]
                        : ["action", "name", "race", "gender", "faceIndex", "reflexes", "career"];
                    bool background = fields.Contains("backgroundAnswers") || fields.Contains("attributeAllocations") || fields.Contains("skillAllocations");
                    if (background) required.UnionWith(["backgroundAnswers", "attributeAllocations", "skillAllocations"]);
                    // A draft may hold an empty name while the player is typing one; only the commit needs it.
                    bool named = rule.Kind == DaggerfallUiActionKind.CharacterCommit ? !string.IsNullOrWhiteSpace(name) : name is not null;
                    return fields.SetEquals(required)
                        && named && !string.IsNullOrWhiteSpace(race)
                        && !string.IsNullOrWhiteSpace(gender) && !string.IsNullOrWhiteSpace(career)
                        && faceIndex is not null && reflexes is not null
                        ? new(action, Name: name, Race: race, Gender: gender, FaceIndex: faceIndex, Reflexes: reflexes, Career: career,
                            PrimarySkills: primarySkills, MajorSkills: majorSkills, MinorSkills: minorSkills, Advantages: advantages, Disadvantages: disadvantages, HitPointsPerLevel: hitPointsPerLevel,
                            BackgroundAnswers: backgroundAnswers, AttributeAllocations: attributeAllocations, SkillAllocations: skillAllocations) : null;
                }
                case DaggerfallUiActionKind.CharacterQuickStart:
                {
                    // The draft's identity and career, with no background: quick start completes it. A blank name is allowed.
                    HashSet<string> required = career == DaggerfallCustomCareerPolicy.CareerId
                        ? ["action", "name", "race", "gender", "faceIndex", "reflexes", "career", "primarySkills", "majorSkills", "minorSkills", "hitPointsPerLevel", "advantages", "disadvantages"]
                        : ["action", "name", "race", "gender", "faceIndex", "reflexes", "career"];
                    return fields.SetEquals(required) && name is not null
                        && !string.IsNullOrWhiteSpace(race) && !string.IsNullOrWhiteSpace(gender) && !string.IsNullOrWhiteSpace(career)
                        && faceIndex is not null && reflexes is not null
                        ? new(action, Name: name, Race: race, Gender: gender, FaceIndex: faceIndex, Reflexes: reflexes, Career: career,
                            PrimarySkills: primarySkills, MajorSkills: majorSkills, MinorSkills: minorSkills, Advantages: advantages, Disadvantages: disadvantages, HitPointsPerLevel: hitPointsPerLevel) : null;
                }
                case DaggerfallUiActionKind.CharacterClassBack: return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.CharacterClassAnswer:
                    return fields.SetEquals(["action", "question", "answer"]) && classQuestion is >= 1 and <= 40 && classAnswer is >= 0 and <= 2
                        ? new(action, Question: classQuestion, Answer: classAnswer) : null;
                case DaggerfallUiActionKind.ActivationMode:
                    return fields.SetEquals(["action", "mode"])
                        && mode is "grab" or "info" or "talk" or "steal" or "lockpick" or "bash"
                        ? new(action, Mode: mode) : null;
                case DaggerfallUiActionKind.TransportSelect:
                    return fields.SetEquals(["action", "mode"])
                        && mode is "foot" or "horse" or "cart"
                        ? new(action, Mode: mode) : null;
                case DaggerfallUiActionKind.TransportLeaveShip or DaggerfallUiActionKind.TransportBoardShip:
                    return fields.SetEquals(["action"]) ? new(action) : null;
                case DaggerfallUiActionKind.Rest:
                {
                    bool timedOrLoiter = mode is "timed" or "loiter";
                    bool untilHealed = mode == "until-healed";
                    bool shape = timedOrLoiter
                        ? fields.SetEquals(["action", "mode", "hours"]) && hours is >= 0
                        : untilHealed && fields.SetEquals(["action", "mode"]);
                    return shape ? new(action, Mode: mode, Hours: hours) : null;
                }
                case DaggerfallUiActionKind.PropertyBuy or DaggerfallUiActionKind.PropertySell or DaggerfallUiActionKind.PropertyEnter:
                    return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key) ? new(action, Key: key) : null;
                case DaggerfallUiActionKind.PropertyPut or DaggerfallUiActionKind.PropertyTake:
                    return fields.IsSubsetOf(["action", "key", "item", "amount", "revision"])
                        && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(revision)
                        && amount is null or > 0 ? new(action, Key: key, Item: item, Amount: amount, Revision: revision) : null;
                case DaggerfallUiActionKind.WagonPut or DaggerfallUiActionKind.WagonTake:
                    return (fields.SetEquals(["action", "revision", "item"])
                        || fields.SetEquals(["action", "revision", "item", "amount"]))
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        ? new(action, Revision: revision, Item: item, Amount: amount) : null;
                case DaggerfallUiActionKind.TeleportSelect:
                    return fields.SetEquals(["action", "revision", "key"]) && !string.IsNullOrWhiteSpace(revision) && key is "anchor" or "recall" or "cancel" ? new(action, Revision: revision, Key: key) : null;
                case DaggerfallUiActionKind.LegalChoice:
                    return fields.SetEquals(["action", "revision", "key"]) && !string.IsNullOrWhiteSpace(revision) && key is "yield" or "resist" or "escape" or "guilty" or "etiquette" or "streetwise" or "accept" or "continue" ? new(action, Revision: revision, Key: key) : null;
                case DaggerfallUiActionKind.DispelSelect or DaggerfallUiActionKind.IdentifySelect or DaggerfallUiActionKind.CreateItemSelect:
                    return fields.SetEquals(["action", "revision", "key"]) && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(key) ? new(action, Revision: revision, Key: key) : null;
                case DaggerfallUiActionKind.DispelCancel or DaggerfallUiActionKind.IdentifyCancel or DaggerfallUiActionKind.CreateItemCancel:
                    return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision) ? new(action, Revision: revision) : null;
                case DaggerfallUiActionKind.BankOpen:
                    return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision) ? new(action, Revision: revision) : null;
                case DaggerfallUiActionKind.DialogueTone:
                    return fields.SetEquals(["action", "revision", "tone"])
                        && !string.IsNullOrWhiteSpace(revision)
                        && tone is "polite" or "normal" or "blunt"
                        ? new(action, Revision: revision, Tone: tone) : null;
                case DaggerfallUiActionKind.DialogueTopic:
                {
                    if (topic == "donate")
                        return fields.SetEquals(["action", "revision", "topic", "amount"])
                            && !string.IsNullOrWhiteSpace(revision) && amount is > 0
                            ? new(action, Revision: revision, Topic: topic, Amount: amount) : null;
                    if (topic is "armor" or "join" or "restore-attributes" && fields.SetEquals(["action", "revision", "topic", "key"]))
                        return !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(key) && (topic == "armor" || key is "yes" or "no")
                            ? new(action, Revision: revision, Topic: topic, Key: key) : null;
                    return fields.SetEquals(["action", "revision", "topic"])
                        && !string.IsNullOrWhiteSpace(revision)
                        && (ValidDialogueTopic(topic) || topic is "cure" or "rank-review" or "armor" or "house" or "join" or "teleport" or "spymaster")
                        ? new(action, Revision: revision, Topic: topic) : null;
                }
                case DaggerfallUiActionKind.DialogueClose:
                    return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                        ? new(action, Revision: revision) : null;
                case DaggerfallUiActionKind.TavernBuy:
                    return fields.SetEquals(["action", "revision", "key", "amount"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(key) && amount is not null
                        ? new(action, Revision: revision, Key: key, Amount: amount) : null;
                case DaggerfallUiActionKind.TrainingCommit:
                    return fields.SetEquals(["action", "revision", "key", "amount", "confirm"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(key) && amount is > 0
                        ? new(action, Revision: revision, Key: key, Amount: amount, Confirm: confirm) : null;
                case DaggerfallUiActionKind.MerchantBuy or DaggerfallUiActionKind.MerchantSell or DaggerfallUiActionKind.MerchantShoplift:
                {
                    // Buying and selling may carry the player's confirmation of a large trade.
                    bool confirmable = rule.Kind != DaggerfallUiActionKind.MerchantShoplift;
                    bool shape = fields.SetEquals(["action", "revision", "item"])
                        || fields.SetEquals(["action", "revision", "item", "amount"])
                        || confirmable && (fields.SetEquals(["action", "revision", "item", "confirm"])
                            || fields.SetEquals(["action", "revision", "item", "amount", "confirm"]));
                    return shape && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        && (amount is null or > 0)
                        && (!item.StartsWith("unique:", StringComparison.Ordinal) || (amount is null or 1))
                        ? new(action, Revision: revision, Item: item, Amount: amount, Confirm: confirm) : null;
                }
                case DaggerfallUiActionKind.MerchantQuote:
                    return fields.SetEquals(["action", "revision", "item", "key", "amount"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && key is "buy" or "sell" && amount is > 0
                        ? new(action, Revision: revision, Item: item, Key: key, Amount: amount) : null;
                case DaggerfallUiActionKind.MerchantRepair or DaggerfallUiActionKind.MerchantIdentify:
                    return fields.SetEquals(["action", "revision", "item"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                        ? new(action, Revision: revision, Item: item) : null;
                case DaggerfallUiActionKind.MerchantCollectRepair:
                    return fields.SetEquals(["action", "revision", "key"])
                        && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(key)
                        ? new(action, Revision: revision, Key: key) : null;
                case DaggerfallUiActionKind.QuestChoice:
                    return fields.SetEquals(["action", "questInstance", "questMessage", "questPrompt", "questChoice"])
                        && !string.IsNullOrWhiteSpace(questInstance) && !string.IsNullOrWhiteSpace(questPrompt) && questMessage is > 0 && questChoice is not null
                        ? new(action, QuestInstance: questInstance, QuestMessage: questMessage, QuestChoice: questChoice, QuestPrompt: questPrompt) : null;
                case DaggerfallUiActionKind.QuestOfferAnswer:
                    return fields.SetEquals(["action", "questInstance", "confirm"]) && !string.IsNullOrWhiteSpace(questInstance)
                        ? new(action, QuestInstance: questInstance, Confirm: confirm) : null;
                case DaggerfallUiActionKind.QuestDismiss:
                    return fields.SetEquals(["action", "questInstance", "questDelivery"])
                        && !string.IsNullOrWhiteSpace(questInstance) && !string.IsNullOrWhiteSpace(questDelivery)
                        ? new(action, QuestInstance: questInstance, QuestDelivery: questDelivery) : null;
                // "begin" is the entry screen's own action, which the product answers: the session accepts the
                // shape so a slice carrying it is a known action it does not act on, rather than an
                // unrecognized one it reports over the screen that asked.
                case DaggerfallUiActionKind.Attack or DaggerfallUiActionKind.Loot or DaggerfallUiActionKind.Begin
                    or DaggerfallUiActionKind.CinematicSkip or DaggerfallUiActionKind.SaveGame or DaggerfallUiActionKind.LoadGame:
                    return fields.Count == 1 ? new(action) : null;
            }
            // A rule whose kind has no case above admits no payload.
            return null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private static bool ValidDialogueTopic(string? value) => value is { Length: > 0 and <= 512 }
        && (value is "directions" or "news" or "work"
            || HasTopicTarget(value, "direction:")
            || HasTopicTarget(value, "quest-info:")
            || HasTopicTarget(value, "quest-rumor:"));

    private static bool HasTopicTarget(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length;

    private static bool ValidMapNoteText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    private static bool ValidNotebookText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
}
