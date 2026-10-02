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
    bool Cautious = false, bool Inn = false, bool Ship = false, bool Open = false, int? Question = null, int? Answer = null)
{
    /// <summary>The typed action the wire name names; resolved once when the action is built.</summary>
    internal DaggerfallUiActionKind Kind { get; } = DaggerfallUiAction.KindOf(Action);
}

/// <summary>Every player action the <c>dagger.ui.action.v1</c> contract carries, by meaning.</summary>
internal enum DaggerfallUiActionKind
{
    Begin, CinematicSkip, ArtRequest, SpellReady, SpellUnready, SpellCast, SpellBuy, SpellDelete, SpellInfo,
    ControlsRebind, ControlsReset,
    CharacterClassQuestions, CharacterClassAnswer, CharacterClassBack,
    CharacterBegin, CharacterUpdate, CharacterBackgroundReroll, CharacterCommit, CharacterCancel,
    CharacterLevelAllocate, CharacterLevelCommit,
    ActivationMode, Attack, Loot, Inventory, Character, Menu,
    DialogueTone, DialogueTopic, DialogueClose, CreateItemSelect, DispelSelect, DispelCancel, TeleportSelect, IdentifySelect, IdentifyCancel,
    TransportSelect, TransportToggle, TransportLeaveShip, TransportBoardShip,
    PropertyBuy, PropertySell, PropertyEnter, PropertyPut, PropertyTake,
    TravelSearch, TravelPreview, TravelAccept, MapOpen, MapBuilding,
    Rest, LodgingQuote, LodgingBook,
    WagonPut, WagonTake,
    QuestChoice, QuestDismiss,
    DungeonTextAnswer, DungeonTextClose,
    InventoryMove, InventoryInspect, InventoryUse, InventoryDrop,
    NotebookPage, NotebookAdd, NotebookEdit, NotebookRemove, NotebookMove,
    CurrencyDepositGold, CurrencyWithdrawGold, CurrencyDepositLetters, CurrencyWithdrawLetter, BankTransfer,
    BankOpen, BankLoanIssue, BankLoanRepayAccount, BankLoanRepayCarried,
    LootClose, LootTake,
    SaveGame, LoadGame, SaveSlots, SaveSlot, LoadSlot, DeleteSlot,
    DeathNewGame, DeathLoadGame, DeathQuit,
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
        new(DaggerfallUiActionKind.ControlsReset, "controls-reset", DaggerfallUiPhases.Live),
        // Character creation checks the entry screen itself, so a refusal is reported rather than dropped.
        new(DaggerfallUiActionKind.CharacterClassQuestions, "character-class-questions", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterClassAnswer, "character-class-answer", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterClassBack, "character-class-back", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterBegin, "character-begin", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterUpdate, "character-update", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterBackgroundReroll, "character-background-reroll", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterCommit, "character-commit", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterCancel, "character-cancel", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.CharacterLevelAllocate, "character-level-allocate", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CharacterLevelCommit, "character-level-commit", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.ActivationMode, "activation-mode", DaggerfallUiPhases.Playing),
        new(DaggerfallUiActionKind.SpellReady,"spell-ready",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellUnready,"spell-unready",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellCast,"spell-cast",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellBuy,"spell-buy",DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SpellDelete,"spell-delete",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.SpellInfo,"spell-info",DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.Attack, "attack", DaggerfallUiPhases.Playing),
        new(DaggerfallUiActionKind.Loot, "loot", DaggerfallUiPhases.Playing),
        // The DOM owns its panels; these name a panel the DOM opened and the session does nothing with.
        new(DaggerfallUiActionKind.Inventory, "inventory", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.Character, "character", DaggerfallUiPhases.Live),
        // The DOM reports whether its game menu (and so any of its panels) is open, because an open
        // menu holds the world. A menu closed over death still reaches the session.
        new(DaggerfallUiActionKind.Menu, "menu", DaggerfallUiPhases.Live | DaggerfallUiPhases.Dead),
        new(DaggerfallUiActionKind.IdentifySelect, "identify-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.IdentifyCancel, "identify-cancel", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.CreateItemSelect, "create-item-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DispelSelect, "dispel-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DispelCancel, "dispel-cancel", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TeleportSelect, "teleport-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DialogueTone, "dialogue-tone", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DialogueTopic, "dialogue-topic", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DialogueClose, "dialogue-close", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportSelect, "transport-select", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportToggle, "transport-toggle", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportBoardShip, "transport-board-ship", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyBuy, "property-buy", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertySell, "property-sell", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyEnter, "property-enter", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyPut, "property-put", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.PropertyTake, "property-take", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TransportLeaveShip, "transport-leave-ship", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.MapOpen, "map-open", DaggerfallUiPhases.Live),
        new(DaggerfallUiActionKind.MapBuilding, "map-building", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TravelSearch, "travel-search", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TravelPreview, "travel-preview", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.TravelAccept, "travel-accept", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LodgingQuote, "lodging-quote", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LodgingBook, "lodging-book", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.Rest, "rest", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WagonPut, "wagon-put", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.WagonTake, "wagon-take", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.QuestChoice, "quest-choice", DaggerfallUiPhases.Interaction,
            "Quest choice rejected: this prompt is no longer pending or the choice is invalid."),
        new(DaggerfallUiActionKind.QuestDismiss, "quest-dismiss", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DungeonTextAnswer, "dungeon-text-answer", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DungeonTextClose, "dungeon-text-close", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryMove, "inventory-move", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryInspect, "inventory-inspect", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryUse, "inventory-use", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.InventoryDrop, "inventory-drop", DaggerfallUiPhases.Interaction),
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
        new(DaggerfallUiActionKind.SaveGame, "save-game", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LoadGame, "load-game", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SaveSlots, "save-slots", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.SaveSlot, "save-slot", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.LoadSlot, "load-slot", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DeleteSlot, "delete-slot", DaggerfallUiPhases.Interaction),
        new(DaggerfallUiActionKind.DeathNewGame, DaggerfallDeathPresentation.NewGameAction, DaggerfallUiPhases.Dead, Unrecognized),
        new(DaggerfallUiActionKind.DeathLoadGame, DaggerfallDeathPresentation.LoadGameAction, DaggerfallUiPhases.Dead, Unrecognized),
        new(DaggerfallUiActionKind.DeathQuit, DaggerfallDeathPresentation.QuitAction, DaggerfallUiPhases.Dead, Unrecognized),
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
            int? targetGrid = null, faceIndex = null, reflexes = null, hitPointsPerLevel = null, questMessage = null, page = null, destination = null, hours = null, region = null, days = null;
            ulong? amount = null;
            bool confirm = false, cautious = false, inn = false, ship = false, open = false;
            int? questChoice = null, classQuestion = null, classAnswer = null;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name)) return null;
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
                if (property.Name == "amount")
                {
                    if (!property.Value.TryGetUInt64(out ulong parsed)) return null;
                    amount = parsed;
                    continue;
                }
                if (property.Name is "confirm" or "cautious" or "inn" or "ship" or "open")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return null;
                    if (property.Name == "confirm") confirm = property.Value.GetBoolean();
                    else if (property.Name == "cautious") cautious = property.Value.GetBoolean();
                    else if (property.Name == "inn") inn = property.Value.GetBoolean();
                    else if (property.Name == "open") open = property.Value.GetBoolean();
                    else ship = property.Value.GetBoolean();
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
            if (amount == 0 && action is not ("lodging-book" or "travel-accept" or "spell-buy")) return null;
            if (action == "travel-accept")
                return fields.SetEquals(["action", "key", "amount"]) && !string.IsNullOrWhiteSpace(key) && amount is not null
                    ? new(action, Key: key, Amount: amount) : null;
            if (action == "lodging-quote")
                return fields.SetEquals(["action", "key", "days"]) && !string.IsNullOrWhiteSpace(key) && days is >= 1 and <= 350
                    ? new(action, Key: key, Days: days) : null;
            if (action == "lodging-book")
                return fields.SetEquals(["action", "key", "days", "amount"]) && !string.IsNullOrWhiteSpace(key) && days is >= 1 and <= 350 && amount is not null
                    ? new(action, Key: key, Days: days, Amount: amount) : null;
            if (action == "inventory-move")
            {
                if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(item)
                    || (targetGrid is null) == (targetEquipment is null) || fields.Count != 4) return null;
                return new(action, revision, item, targetGrid, targetEquipment);
            }
            if (action == "inventory-inspect")
                return fields.SetEquals(["action", "revision", "item"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, revision, item) : null;
            if (action == "inventory-use")
                return fields.SetEquals(["action", "revision", "item"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, revision, item) : null;
            if (action == "notebook-page")
                return fields.SetEquals(["action", "revision", "page"]) && !string.IsNullOrWhiteSpace(revision) && page is >= 0
                    ? new(action, Revision: revision, Page: page) : null;
            if (action == "notebook-add")
                return fields.SetEquals(["action", "revision", "text"]) && !string.IsNullOrWhiteSpace(revision) && ValidNotebookText(text)
                    ? new(action, Revision: revision, Text: text) : null;
            if (action == "notebook-edit")
                return fields.SetEquals(["action", "revision", "note", "text"]) && !string.IsNullOrWhiteSpace(revision)
                    && !string.IsNullOrWhiteSpace(note) && ValidNotebookText(text)
                    ? new(action, Revision: revision, Note: note, Text: text) : null;
            if (action == "notebook-remove")
                return fields.SetEquals(["action", "revision", "note"]) && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(note)
                    ? new(action, Revision: revision, Note: note) : null;
            if (action == "notebook-move")
                return fields.SetEquals(["action", "revision", "note", "destination"]) && !string.IsNullOrWhiteSpace(revision)
                    && !string.IsNullOrWhiteSpace(note) && destination is >= 0
                    ? new(action, Revision: revision, Note: note, Destination: destination) : null;
            if (action == "spell-buy")
                return fields.SetEquals(["action", "key", "revision", "amount", "confirm"]) && !string.IsNullOrWhiteSpace(key)
                    && !string.IsNullOrWhiteSpace(revision) && amount is not null
                    ? new(action, Key: key, Revision: revision, Amount: amount, Confirm: confirm) : null;
            if (action == "spell-delete")
                return fields.SetEquals(["action", "key", "confirm"]) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key, Confirm: confirm) : null;
            if (action == "spell-info")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key) ? new(action, Key: key) : null;
            if (action=="spell-ready")
                return fields.SetEquals(["action","key"]) && !string.IsNullOrWhiteSpace(key) ? new(action,Key:key) : null;
            if (action is "spell-unready" or "spell-cast")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "inventory-drop")
                return fields.SetEquals(["action", "revision", "item", "amount"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && amount is not null
                    ? new(action, revision, item, Amount: amount) : null;
            if (action == "loot-take")
            {
                bool shape = fields.SetEquals(["action", "revision", "item", "container"])
                    || fields.SetEquals(["action", "revision", "item", "container", "amount"]);
                return shape && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, revision, item, Container: container, Amount: amount) : null;
            }
            if (action is "currency-deposit-gold" or "currency-withdraw-gold" or "currency-withdraw-letter")
                return fields.SetEquals(["action", "amount"]) && amount is not null ? new(action, Amount: amount) : null;
            if (action == "currency-deposit-letters")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "bank-transfer")
                return fields.SetEquals(["action", "amount", "destination"]) && amount is not null && destination is >= 0
                    ? new(action, Amount: amount, Destination: destination) : null;
            if (action is "bank-loan-issue" or "bank-loan-repay-account" or "bank-loan-repay-carried")
                return fields.SetEquals(["action", "amount"]) && amount is not null
                    ? new(action, Amount: amount) : null;
            if (action == "travel-search")
                return fields.SetEquals(["action", "text"]) && text is { Length: <= 80 }
                    ? new(action, Text: text) : null;
            if (action == "travel-preview")
                return fields.SetEquals(["action", "region", "destination", "cautious", "inn", "ship"])
                    && region is >= 0 && destination is >= 0
                    ? new(action, Region: region, Destination: destination, Cautious: cautious, Inn: inn, Ship: ship) : null;
            if (action == "dungeon-text-answer")
                return fields.SetEquals(["action", "revision", "item", "text"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    && text is { Length: <= 256 }
                    ? new(action, Revision: revision, Item: item, Text: text) : null;
            if (action == "dungeon-text-close")
                return fields.SetEquals(["action", "revision", "item"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, Revision: revision, Item: item) : null;
            if (action == "art-request")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                    ? new(action, revision) : null;
            if (action == "loot-close")
                return fields.SetEquals(["action", "container"]) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, Container: container) : null;
            if (action == "controls-rebind")
                return fields.IsSubsetOf(["action", "item", "key", "confirm"])
                    && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Item: item, Key: key, Confirm: confirm) : null;
            if (action == "controls-reset") return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "map-open") return fields.SetEquals(["action", "open"]) ? new(action, Open: open) : null;
            if (action == "map-building") return fields.SetEquals(["action", "region", "destination", "item"])
                && region is >= 0 && destination is >= 0 && !string.IsNullOrWhiteSpace(item)
                ? new(action, Region: region, Destination: destination, Item: item) : null;
            if (action == "menu") return fields.SetEquals(["action", "open"]) ? new(action, Open: open) : null;
            if (action == "save-slots") return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "save-slot")
                return fields.IsSubsetOf(["action", "key", "label", "confirm"])
                    && !string.IsNullOrWhiteSpace(label)
                    && (key is null || !string.IsNullOrWhiteSpace(key))
                    ? new(action, Key: key, Label: label, Confirm: confirm) : null;
            if (action == "load-slot")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key) : null;
            if (action == "delete-slot")
                return fields.IsSubsetOf(["action", "key", "confirm"])
                    && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key, Confirm: confirm) : null;
            if (action is "death-new-game" or "death-quit")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "death-load-game")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key) : null;
            if (action is "character-begin" or "character-cancel")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "character-level-allocate")
                return fields.SetEquals(["action", "attribute"]) && !string.IsNullOrWhiteSpace(attribute)
                    ? new(action, Attribute: attribute) : null;
            if (action == "character-level-commit")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action is "character-update" or "character-commit" or "character-background-reroll" or "character-class-questions")
            {
                HashSet<string> required = career == DaggerfallCustomCareerPolicy.CareerId
                    ? ["action", "name", "race", "gender", "faceIndex", "reflexes", "career", "primarySkills", "majorSkills", "minorSkills", "hitPointsPerLevel", "advantages", "disadvantages"]
                    : ["action", "name", "race", "gender", "faceIndex", "reflexes", "career"];
                bool background = fields.Contains("backgroundAnswers") || fields.Contains("attributeAllocations") || fields.Contains("skillAllocations");
                if (background) required.UnionWith(["backgroundAnswers", "attributeAllocations", "skillAllocations"]);
                return fields.SetEquals(required)
                    && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(race)
                    && !string.IsNullOrWhiteSpace(gender) && !string.IsNullOrWhiteSpace(career)
                    && faceIndex is not null && reflexes is not null
                    ? new(action, Name: name, Race: race, Gender: gender, FaceIndex: faceIndex, Reflexes: reflexes, Career: career,
                        PrimarySkills: primarySkills, MajorSkills: majorSkills, MinorSkills: minorSkills, Advantages: advantages, Disadvantages: disadvantages, HitPointsPerLevel: hitPointsPerLevel,
                        BackgroundAnswers: backgroundAnswers, AttributeAllocations: attributeAllocations, SkillAllocations: skillAllocations) : null;
            }
            if (action == "character-class-back") return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "character-class-answer")
                return fields.SetEquals(["action", "question", "answer"]) && classQuestion is >= 1 and <= 40 && classAnswer is >= 0 and <= 2
                    ? new(action, Question: classQuestion, Answer: classAnswer) : null;
            if (action == "activation-mode")
                return fields.SetEquals(["action", "mode"])
                    && mode is "grab" or "info" or "talk" or "steal" or "lockpick" or "bash"
                    ? new(action, Mode: mode) : null;
            if (action == "transport-select")
                return fields.SetEquals(["action", "mode"])
                    && mode is "foot" or "horse" or "cart"
                    ? new(action, Mode: mode) : null;
            if (action is "transport-toggle" or "transport-leave-ship" or "transport-board-ship")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "rest")
            {
                bool timedOrLoiter = mode is "timed" or "loiter";
                bool untilHealed = mode == "until-healed";
                bool shape = timedOrLoiter
                    ? fields.SetEquals(["action", "mode", "hours"]) && hours is >= 0
                    : untilHealed && fields.SetEquals(["action", "mode"]);
                return shape ? new(action, Mode: mode, Hours: hours) : null;
            }
            if (action is "property-buy" or "property-sell" or "property-enter")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key) ? new(action, Key: key) : null;
            if (action is "property-put" or "property-take")
                return fields.IsSubsetOf(["action", "key", "item", "amount", "revision"])
                    && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(revision)
                    && amount is null or > 0 ? new(action, Key: key, Item: item, Amount: amount, Revision: revision) : null;
            if (action is "wagon-put" or "wagon-take")
                return (fields.SetEquals(["action", "revision", "item"])
                    || fields.SetEquals(["action", "revision", "item", "amount"]))
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, Revision: revision, Item: item, Amount: amount) : null;
            if (action == "teleport-select")
                return fields.SetEquals(["action", "revision", "key"]) && !string.IsNullOrWhiteSpace(revision) && key is "anchor" or "recall" or "cancel" ? new(action, Revision: revision, Key: key) : null;
            if (action is "dispel-select" or "identify-select" or "create-item-select")
                return fields.SetEquals(["action", "revision", "key"]) && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(key) ? new(action, Revision: revision, Key: key) : null;
            if (action is "dispel-cancel" or "identify-cancel")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision) ? new(action, Revision: revision) : null;
            if (action == "bank-open")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision) ? new(action, Revision: revision) : null;
            if (action == "dialogue-tone")
                return fields.SetEquals(["action", "revision", "tone"])
                    && !string.IsNullOrWhiteSpace(revision)
                    && tone is "polite" or "normal" or "blunt"
                    ? new(action, Revision: revision, Tone: tone) : null;
            if (action == "dialogue-topic")
                return fields.SetEquals(["action", "revision", "topic"])
                    && !string.IsNullOrWhiteSpace(revision)
                    && topic is "directions" or "news"
                    ? new(action, Revision: revision, Topic: topic) : null;
            if (action == "dialogue-close")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                    ? new(action, Revision: revision) : null;
            if (action == "quest-choice")
                return fields.SetEquals(["action", "questInstance", "questMessage", "questPrompt", "questChoice"])
                    && !string.IsNullOrWhiteSpace(questInstance) && !string.IsNullOrWhiteSpace(questPrompt) && questMessage is > 0 && questChoice is not null
                    ? new(action, QuestInstance: questInstance, QuestMessage: questMessage, QuestChoice: questChoice, QuestPrompt: questPrompt) : null;
            if (action == "quest-dismiss")
                return fields.SetEquals(["action", "questInstance", "questDelivery"])
                    && !string.IsNullOrWhiteSpace(questInstance) && !string.IsNullOrWhiteSpace(questDelivery)
                    ? new(action, QuestInstance: questInstance, QuestDelivery: questDelivery) : null;
            // "begin" is the entry screen's own action, which the product answers: the session accepts the
            // shape so a slice carrying it is a known action it does not act on, rather than an
            // unrecognized one it reports over the screen that asked.
            return fields.Count == 1 && action is "attack" or "inventory" or "character" or "loot" or "begin" or "cinematic-skip" or "save-game" or "load-game"
                ? new(action) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private static bool ValidNotebookText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
}
