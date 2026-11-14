using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Admission and dispatch of the <c>dagger.ui.action.v1</c> player actions. Each payload is parsed
/// once per update into a typed action; its declared phases decide whether it acts, and one switch
/// over the typed kind routes it to the owner that applies it.
/// </summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// The parse the entry-screen check made of this update's input, reused by the update itself so
    /// no payload is parsed twice. It is taken by the next update and never outlives the input it read.
    /// </summary>
    private DaggerfallUiInput? _uiInput;

    public bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input)
    {
        DaggerfallUiInput parsed = _uiInput is { } cached && cached.Matches(input) ? cached : DaggerfallUiInput.Parse(input);
        _uiInput = parsed;
        // Quick start commits its character in this update's actions, before the product begins from it.
        if (parsed.Contains(DaggerfallUiActionKind.CharacterQuickStart)) return true;
        if (parsed.Contains(DaggerfallUiActionKind.Begin) && State.Character.Pending is not null && !_definitions.NewGame.QuickEntry)
        {
            Presentation.SetOutcome("Commit or cancel character choices before beginning play.");
            return false;
        }
        return parsed.Contains(DaggerfallUiActionKind.Begin);
    }

    /// <summary>Takes this update's parsed UI actions: the entry-screen check's parse when it read these events.</summary>
    private DaggerfallUiInput TakeUiInput(ReadOnlySpan<ProductInputEvent> input)
    {
        DaggerfallUiInput parsed = _uiInput is { } cached && cached.Matches(input) ? cached : DaggerfallUiInput.Parse(input);
        _uiInput = null;
        return parsed;
    }

    /// <summary>
    /// Whether this admitted slice consumes attack/use as a contextual interaction or mode change. The
    /// slice that opens an interaction admits no attack: a key pressed in the same admitted update as
    /// the interaction key is a coincidence of timing rather than an instruction.
    /// </summary>
    private static bool OpensInteraction(DaggerfallUiInput input) => input.ContainsAny(
        DaggerfallUiActionKind.Loot, DaggerfallUiActionKind.ActivationMode, DaggerfallUiActionKind.DialogueTopic,
        DaggerfallUiActionKind.TavernBuy, DaggerfallUiActionKind.TrainingCommit, DaggerfallUiActionKind.Rest, DaggerfallUiActionKind.LodgingBook, DaggerfallUiActionKind.TravelAccept);

    /// <summary>
    /// Admits one UI payload in the current phase. An unrecognized payload is reported in a live phase;
    /// a recognized action that its phase does not admit is dropped, or reports its declared refusal.
    /// Death owns the session's input while the Host decides the resulting replacement, so every
    /// payload death does not admit is consumed silently.
    /// </summary>
    private void AdmitUiAction(DaggerfallPlayerUiAction? action, DaggerfallUiPhases phase, ProductUpdateState firstStep,
        bool opensInteraction, ref bool elapsedSubmitted)
    {
        if (action is null)
        {
            if (phase != DaggerfallUiPhases.Dead) Presentation.SetOutcome(DaggerfallUiAction.UnrecognizedRefusal);
            return;
        }
        DaggerfallUiActionRule rule = DaggerfallUiAction.RuleFor(action.Kind);
        if (!rule.Admits(phase))
        {
            if (phase != DaggerfallUiPhases.Dead && rule.Refusal is { } refusal) Presentation.SetOutcome(refusal);
            return;
        }
        if (State.RacialOverrides.Current?.SuppressInventory == true && action.Kind is
            DaggerfallUiActionKind.InventoryMove or
            DaggerfallUiActionKind.InventoryInspect or DaggerfallUiActionKind.InventoryUse or DaggerfallUiActionKind.InventoryDrop or DaggerfallUiActionKind.InventorySort
            or DaggerfallUiActionKind.BankOpen)
        {
            Presentation.SetOutcome(DaggerfallRacialOverrideView.InventoryRefusal);
            return;
        }
        if (Law.ModalOpen && action.Kind is not (DaggerfallUiActionKind.LegalChoice or DaggerfallUiActionKind.SaveGame
            or DaggerfallUiActionKind.LoadGame or DaggerfallUiActionKind.SaveSlots or DaggerfallUiActionKind.SaveSlot
            or DaggerfallUiActionKind.LoadSlot)) return;
        ApplyUiAction(action, firstStep, opensInteraction, ref elapsedSubmitted);
        // These immediate UI actions may arrive while no world step is due. Spend a step when
        // play resumes; reading panels, preparing spells and editing preferences remain free.
        if (action.Kind is DaggerfallUiActionKind.SpellCast or DaggerfallUiActionKind.InventoryUse
            or DaggerfallUiActionKind.InventoryMove or DaggerfallUiActionKind.InventoryDrop
            or DaggerfallUiActionKind.TransportSelect)
            _playerTime.RequestWorldAction();
    }

    private void ApplyUiAction(DaggerfallPlayerUiAction action, ProductUpdateState firstStep, bool opensInteraction, ref bool elapsedSubmitted)
    {
        switch (action.Kind)
        {
            // The entry screen's own action is the product's to answer; the product has already left
            // the mode by the time an action in ordinary play could arrive. The DOM owns its panels.
            case DaggerfallUiActionKind.DaedraSummon:
            case DaggerfallUiActionKind.DaedraAnswer:
                SummoningPanel.Change(action); break;
            case DaggerfallUiActionKind.Begin:
                _characterActions.Begin();
                break;
            // A running game already has its content; only the unavailable session imports it.
            case DaggerfallUiActionKind.ContentImport:
            case DaggerfallUiActionKind.ContentImportCancel:
                break;
            case DaggerfallUiActionKind.LegalChoice: ChooseLegal(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.CreateItemSelect: CreateItemChoice.Choose(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.CreateItemCancel: CreateItemChoice.Cancel(action.Revision!); break;
            case DaggerfallUiActionKind.IdentifySelect: IdentifyChoice.Choose(action.Revision!,action.Key); break;
            case DaggerfallUiActionKind.IdentifyCancel: IdentifyChoice.Choose(action.Revision!,null); break;
            case DaggerfallUiActionKind.TeleportSelect: TeleportChoice.Choose(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.DispelSelect: DispelChoice.Choose(action.Revision!, action.Key); break;
            case DaggerfallUiActionKind.DispelCancel: DispelChoice.Choose(action.Revision!, null); break;
            case DaggerfallUiActionKind.MapOpen: LocalMap.SetOpen(action.Open); break;
            case DaggerfallUiActionKind.MapBuilding: LocalMap.SelectBuilding(action); break;
            case DaggerfallUiActionKind.MapNoteAdd:
            case DaggerfallUiActionKind.MapNoteEdit:
            case DaggerfallUiActionKind.MapNoteRemove: LocalMap.ChangeNote(action); break;
            case DaggerfallUiActionKind.Menu: _interactions.SetMenuOpen(action.Open); break;
            case DaggerfallUiActionKind.CinematicSkip:
                if (_openingCinematics.IsActive) _openingCinematics.Skip();
                else Cinematics?.Skip();
                break;
            // A reloaded DOM holds no art and asks for the revision it is missing; the projection
            // answers on its next snapshot rather than a second delivery channel existing.
            case DaggerfallUiActionKind.ArtRequest: _hud.RequestArt(action.Revision!); break;
            case DaggerfallUiActionKind.ActionDrivenTime:
            case DaggerfallUiActionKind.ControlsLook:
            case DaggerfallUiActionKind.ControlsUiScale:
            case DaggerfallUiActionKind.ControlsGroundCover:
            case DaggerfallUiActionKind.ControlsClutterDistance:
            case DaggerfallUiActionKind.ControlsRebind:
            case DaggerfallUiActionKind.ControlsRebindButton:
            case DaggerfallUiActionKind.ControlsVolume:
            case DaggerfallUiActionKind.ControlsReset: _characterActions.ChangeControls(action); break;
            case DaggerfallUiActionKind.CharacterClassQuestions:
            case DaggerfallUiActionKind.CharacterClassAnswer:
            case DaggerfallUiActionKind.CharacterClassBack:
            case DaggerfallUiActionKind.CharacterBegin:
            case DaggerfallUiActionKind.CharacterUpdate:
            case DaggerfallUiActionKind.CharacterBackgroundReroll:
            case DaggerfallUiActionKind.CharacterCommit:
            case DaggerfallUiActionKind.CharacterCancel:
            case DaggerfallUiActionKind.CharacterQuickStart: _characterActions.ChangeCharacter(action); break;
            case DaggerfallUiActionKind.CharacterLevelAllocate:
            case DaggerfallUiActionKind.CharacterLevelCommit: _characterActions.ChangeLevelUp(action); break;
            case DaggerfallUiActionKind.ActivationMode: ActivationPanel.ChangeMode(action); break;
            case DaggerfallUiActionKind.SpellReady:
            case DaggerfallUiActionKind.SpellUnready:
                if (!opensInteraction) Spellbook.ChangeSpell(action); break;
            case DaggerfallUiActionKind.SpellBuy:
            case DaggerfallUiActionKind.SpellDelete:
            case DaggerfallUiActionKind.SpellSwap:
            case DaggerfallUiActionKind.SpellSort:
            case DaggerfallUiActionKind.SpellInfo: Spellbook.ChangeSpellbook(action); break;
            case DaggerfallUiActionKind.ItemMakerDraft:
            case DaggerfallUiActionKind.ItemMakerBuy: _makers.ChangeItemMaker(action); break;
            case DaggerfallUiActionKind.PotionMix: _makers.MakePotion(action); break;
            case DaggerfallUiActionKind.SpellMakerDraft:
            case DaggerfallUiActionKind.SpellMakerBuy: Spellbook.ChangeSpellMaker(action); break;
            case DaggerfallUiActionKind.SpellCast:
                if (!opensInteraction && !_interactions.HoldsWorldOpen) PlayerAttack.Clock.PressCast(); break;
            case DaggerfallUiActionKind.Attack: if (!opensInteraction) firstStep.Request(DaggerfallInput.Attack); break;
            case DaggerfallUiActionKind.Loot: firstStep.Request(DaggerfallInput.Interact); break;
            case DaggerfallUiActionKind.WeaponToggle: ToggleWeapon(); break;
            case DaggerfallUiActionKind.TutorialStart: _tutorial.Start(); break;
            case DaggerfallUiActionKind.TutorialContinue: _tutorial.Continue(action.Page!.Value); break;
            case DaggerfallUiActionKind.TutorialEnd: _tutorial.End(); break;
            case DaggerfallUiActionKind.DialogueTone:
            case DaggerfallUiActionKind.DialogueTopic:
            case DaggerfallUiActionKind.DialogueClose: _ = ActivationPanel.ApplyDialogue(action); break;
            case DaggerfallUiActionKind.TavernBuy: EconomyPanels.BuyTavernMeal(action); break;
            case DaggerfallUiActionKind.TrainingCommit: EconomyPanels.ApplyTraining(action); break;
            case DaggerfallUiActionKind.MerchantBuy:
            case DaggerfallUiActionKind.MerchantSell:
            case DaggerfallUiActionKind.MerchantRepair:
            case DaggerfallUiActionKind.MerchantCollectRepair:
            case DaggerfallUiActionKind.MerchantIdentify:
            case DaggerfallUiActionKind.MerchantShoplift: EconomyPanels.ChangeMerchant(action); break;
            case DaggerfallUiActionKind.MerchantQuote: EconomyPanels.QuoteMerchantTrade(action); break;
            case DaggerfallUiActionKind.TransportSelect:
            case DaggerfallUiActionKind.TransportLeaveShip:
            case DaggerfallUiActionKind.TransportBoardShip: _travelPanel.ChangeTransport(action); break;
            case DaggerfallUiActionKind.PropertyBuy:
            case DaggerfallUiActionKind.PropertySell:
            case DaggerfallUiActionKind.PropertyEnter:
            case DaggerfallUiActionKind.PropertyPut:
            case DaggerfallUiActionKind.PropertyTake: EconomyPanels.ChangeProperty(action); break;
            case DaggerfallUiActionKind.TravelSearch: _travelPanel.Search(action); break;
            case DaggerfallUiActionKind.TravelPreview: _travelPanel.Preview(action); break;
            case DaggerfallUiActionKind.TravelMap: _travelPanel.ChangeMap(action); break;
            case DaggerfallUiActionKind.AtlasTiles: _travelPanel.GrantAtlasTiles(action); break;
            case DaggerfallUiActionKind.TravelAccept:
                if (!elapsedSubmitted) { elapsedSubmitted = true; _travelPanel.Accept(action); }
                break;
            // One rest per input slice: explicit elapsed time is applied once.
            case DaggerfallUiActionKind.LodgingQuote:
            case DaggerfallUiActionKind.LodgingBook: EconomyPanels.ChangeLodging(action); break;
            case DaggerfallUiActionKind.Rest:
                if (!elapsedSubmitted) { RestPanel.Change(action); elapsedSubmitted = true; }
                break;
            case DaggerfallUiActionKind.WagonPut:
            case DaggerfallUiActionKind.WagonTake: _travelPanel.ChangeWagon(action); break;
            case DaggerfallUiActionKind.QuestChoice:
                if (State.Quests.ChoosePrompt(State.Variables, action.QuestInstance!, action.QuestMessage!.Value, action.QuestPrompt!, action.QuestChoice!.Value))
                    Presentation.SetOutcome("Quest choice recorded.");
                else Presentation.SetOutcome(DaggerfallUiAction.RuleFor(DaggerfallUiActionKind.QuestChoice).Refusal!);
                break;
            case DaggerfallUiActionKind.QuestOfferAnswer: AnswerQuestOffer(action.QuestInstance!, action.Confirm); break;
            case DaggerfallUiActionKind.QuestDismiss:
                _ = State.Quests.DismissRewardMessage(action.QuestInstance!, action.QuestDelivery!);
                break;
            case DaggerfallUiActionKind.DungeonTextAnswer:
            case DaggerfallUiActionKind.DungeonTextClose: _textActions.ApplyDungeonText(action); break;
            case DaggerfallUiActionKind.InventoryMove: _inventoryUi.Move(action); break;
            case DaggerfallUiActionKind.InventoryInspect: _inventoryUi.Inspect(action); break;
            case DaggerfallUiActionKind.InventoryUse: _inventoryUi.Use(action); break;
            case DaggerfallUiActionKind.InventoryDrop: _inventoryUi.Drop(action); break;
            case DaggerfallUiActionKind.InventorySort: _inventoryUi.Sort(); break;
            case DaggerfallUiActionKind.NotebookPage:
            case DaggerfallUiActionKind.NotebookAdd:
            case DaggerfallUiActionKind.NotebookEdit:
            case DaggerfallUiActionKind.NotebookRemove:
            case DaggerfallUiActionKind.NotebookMove: _textActions.ApplyNotebook(action); break;
            case DaggerfallUiActionKind.CurrencyDepositGold:
            case DaggerfallUiActionKind.CurrencyWithdrawGold:
            case DaggerfallUiActionKind.CurrencyDepositLetters:
            case DaggerfallUiActionKind.CurrencyWithdrawLetter:
            case DaggerfallUiActionKind.BankTransfer: EconomyPanels.ChangeCurrency(action); break;
            case DaggerfallUiActionKind.BankOpen: EconomyPanels.OpenCurrentBank(action.Revision); break;
            case DaggerfallUiActionKind.BankLoanIssue:
            case DaggerfallUiActionKind.BankLoanRepayAccount:
            case DaggerfallUiActionKind.BankLoanRepayCarried: EconomyPanels.ChangeLoan(action); break;
            case DaggerfallUiActionKind.LootClose: _lootUi.Close(action.Container); break;
            case DaggerfallUiActionKind.LootTake: _lootActions.Take(action); break;
            case DaggerfallUiActionKind.LootTakeAll: _lootActions.TakeAll(action); break;
            // Quick save and quick load follow the Host's slot naming; the menu's slot actions name
            // their own slots. All of them use the same catalog owner.
            case DaggerfallUiActionKind.SaveGame: _saveSlotRequest = new(SaveSlotOperation.QuickSave); break;
            case DaggerfallUiActionKind.LoadGame: _saveSlotRequest = new(SaveSlotOperation.QuickLoad); break;
            case DaggerfallUiActionKind.SaveSlots: _saveSlotRequest = new(SaveSlotOperation.List); break;
            case DaggerfallUiActionKind.SaveSlot: _saveSlotRequest = new(SaveSlotOperation.Save, action.Key, action.Label, action.Confirm); break;
            case DaggerfallUiActionKind.LoadSlot: _saveSlotRequest = new(SaveSlotOperation.Load, action.Key); break;
            case DaggerfallUiActionKind.BundleSelect: ChooseBundle(action.Key!); break;
            case DaggerfallUiActionKind.DeleteSlot: _saveSlotRequest = new(SaveSlotOperation.Delete, action.Key, Confirm: action.Confirm); break;
            case DaggerfallUiActionKind.DeathNewGame:
            case DaggerfallUiActionKind.DeathLoadGame:
            case DaggerfallUiActionKind.DeathQuit: _deathScreen.Choose(action); break;
            default: throw new InvalidOperationException($"Admitted UI action '{action.Kind}' has no dispatch.");
        }
    }

    // Opening a panel or preparing a spell must work without spending a world step. Physical
    // attack/use edges remain queued in the input owner until an Engine-admitted step consumes them.
    private void ApplyIdleInterfaceActions()
    {
        bool panel = false;
        foreach (var (action, name) in new[]
        {
            (DaggerfallInput.Inventory, DaggerfallPanel.Inventory),
            (DaggerfallInput.Character, DaggerfallPanel.Character),
            (DaggerfallInput.Map, DaggerfallPanel.Map),
            (DaggerfallInput.Menu, DaggerfallPanel.Menu),
        })
            if (_input.TakeAction(action)) { RequestPanel(name); panel = true; }
        foreach (string shortcut in DaggerfallInput.Shortcuts)
            if (_input.TakeAction(DaggerfallInput.Shortcut(shortcut))) { ApplyShortcut(shortcut); panel = true; }
        if (panel) _input.ClearPendingActions();
    }

    /// <summary>What a shortcut key asks for: a panel, a save, an activation mode or a spell.</summary>
    private void ApplyShortcut(string id)
    {
        if (id.StartsWith("activation.", StringComparison.Ordinal))
        {
            ActivationPanel.ChangeMode(new("activation-mode", Mode: id["activation.".Length..]));
            return;
        }
        switch (id)
        {
            case "quick-save": _saveSlotRequest = new(WorldRpg.Kit.SaveSlotOperation.QuickSave); break;
            // Quick load discards progress, so the key asks the screen to confirm it, as the menu's button does.
            case "quick-load": RequestPanel("quick-load"); break;
            case "spell": RequestPanel("spells"); break;
            case "rest": RequestPanel("rest"); break;
            case "transport": RequestPanel(DaggerfallPanel.Travel); break;
            case "logbook": RequestPanel(DaggerfallPanel.Journal); break;
            case "notebook": RequestPanel(DaggerfallPanel.Notebook); break;
            case "travel-map": _travelPanel.OpenMap(); RequestPanel(DaggerfallPanel.Travel); break;
            case "use-magic-item": RequestPanel("magic-items"); break;
            case "radial": RequestPanel(DaggerfallPanel.Radial); break;
            case "abort-spell": Spellbook.ChangeSpell(new("spell-unready")); break;
            case "recast":
                PlayerAttack.Clock.PressCast();
                break;
        }
    }
}
