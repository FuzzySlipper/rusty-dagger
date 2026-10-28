using System.Globalization;
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
        if (parsed.Contains(DaggerfallUiActionKind.Begin) && State.Character.Pending is not null)
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
            Presentation.SetOutcome("You cannot use your inventory in beast form.");
            return;
        }
        if (LegalModalOpen && action.Kind is not (DaggerfallUiActionKind.LegalChoice or DaggerfallUiActionKind.SaveGame
            or DaggerfallUiActionKind.LoadGame or DaggerfallUiActionKind.SaveSlots or DaggerfallUiActionKind.SaveSlot
            or DaggerfallUiActionKind.LoadSlot)) return;
        ApplyUiAction(action, firstStep, opensInteraction, ref elapsedSubmitted);
        // These immediate UI actions may arrive while no world step is due. Spend a step when
        // play resumes; reading panels, preparing spells and editing preferences remain free.
        if (action.Kind is DaggerfallUiActionKind.SpellCast or DaggerfallUiActionKind.InventoryUse
            or DaggerfallUiActionKind.InventoryMove or DaggerfallUiActionKind.InventoryDrop
            or DaggerfallUiActionKind.TransportSelect or DaggerfallUiActionKind.TransportToggle)
            _pendingWorldAction = true;
    }

    private void ApplyUiAction(DaggerfallPlayerUiAction action, ProductUpdateState firstStep, bool opensInteraction, ref bool elapsedSubmitted)
    {
        switch (action.Kind)
        {
            // The entry screen's own action is the product's to answer; the product has already left
            // the mode by the time an action in ordinary play could arrive. The DOM owns its panels.
            case DaggerfallUiActionKind.DaedraSummon:
            case DaggerfallUiActionKind.DaedraAnswer:
                ChangeSummoning(action); break;
            case DaggerfallUiActionKind.Begin:
            // A running game already has its content; only the unavailable session imports it.
            case DaggerfallUiActionKind.ContentImport:
            case DaggerfallUiActionKind.ContentImportCancel:
                break;
            case DaggerfallUiActionKind.LegalChoice: ChooseLegal(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.CreateItemSelect: ChooseCreateItem(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.CreateItemCancel: CancelCreateItem(action.Revision!); break;
            case DaggerfallUiActionKind.IdentifySelect: ChooseIdentify(action.Revision!,action.Key); break;
            case DaggerfallUiActionKind.IdentifyCancel: ChooseIdentify(action.Revision!,null); break;
            case DaggerfallUiActionKind.TeleportSelect: ChooseTeleport(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.DispelSelect: ChooseDispel(action.Revision!, action.Key); break;
            case DaggerfallUiActionKind.DispelCancel: ChooseDispel(action.Revision!, null); break;
            case DaggerfallUiActionKind.MapOpen: _mapOpen = action.Open; break;
            case DaggerfallUiActionKind.MapBuilding: SelectMapBuilding(action); break;
            case DaggerfallUiActionKind.MapNoteAdd:
            case DaggerfallUiActionKind.MapNoteEdit:
            case DaggerfallUiActionKind.MapNoteRemove: ChangeMapNote(action); break;
            case DaggerfallUiActionKind.Menu: _interactions.SetMenuOpen(action.Open); break;
            case DaggerfallUiActionKind.CinematicSkip:
                if (_openingCinematics.IsActive) _openingCinematics.Skip();
                else Cinematics?.Skip();
                break;
            // A reloaded DOM holds no art and asks for the revision it is missing; the projection
            // answers on its next snapshot rather than a second delivery channel existing.
            case DaggerfallUiActionKind.ArtRequest: _hud.RequestArt(action.Revision!); break;
            case DaggerfallUiActionKind.ActionDrivenTime: ChangePlayerTime(action.Enabled!.Value); break;
            case DaggerfallUiActionKind.ControlsLook:
            case DaggerfallUiActionKind.ControlsUiScale:
            case DaggerfallUiActionKind.ControlsRebind:
            case DaggerfallUiActionKind.ControlsRebindButton:
            case DaggerfallUiActionKind.ControlsVolume:
            case DaggerfallUiActionKind.ControlsReset: ChangeControls(action); break;
            case DaggerfallUiActionKind.CharacterClassQuestions:
            case DaggerfallUiActionKind.CharacterClassAnswer:
            case DaggerfallUiActionKind.CharacterClassBack:
            case DaggerfallUiActionKind.CharacterBegin:
            case DaggerfallUiActionKind.CharacterUpdate:
            case DaggerfallUiActionKind.CharacterBackgroundReroll:
            case DaggerfallUiActionKind.CharacterCommit:
            case DaggerfallUiActionKind.CharacterCancel:
            case DaggerfallUiActionKind.CharacterQuickStart: ChangeCharacter(action); break;
            case DaggerfallUiActionKind.CharacterLevelAllocate:
            case DaggerfallUiActionKind.CharacterLevelCommit: ChangeLevelUp(action); break;
            case DaggerfallUiActionKind.ActivationMode: ApplyActivationMode(action); break;
            case DaggerfallUiActionKind.SpellReady:
            case DaggerfallUiActionKind.SpellUnready:
                if (!opensInteraction) ChangeSpell(action); break;
            case DaggerfallUiActionKind.SpellBuy:
            case DaggerfallUiActionKind.SpellDelete:
            case DaggerfallUiActionKind.SpellSwap:
            case DaggerfallUiActionKind.SpellSort:
            case DaggerfallUiActionKind.SpellInfo: ChangeSpellbook(action); break;
            case DaggerfallUiActionKind.ItemMakerDraft:
            case DaggerfallUiActionKind.ItemMakerBuy: ChangeItemMaker(action); break;
            case DaggerfallUiActionKind.PotionMix: MakePotion(action); break;
            case DaggerfallUiActionKind.SpellMakerDraft:
            case DaggerfallUiActionKind.SpellMakerBuy: ChangeSpellMaker(action); break;
            case DaggerfallUiActionKind.SpellCast:
                if (!opensInteraction && !_interactions.HoldsWorldOpen) ChangeSpell(action); break;
            case DaggerfallUiActionKind.Attack: if (!opensInteraction) firstStep.Request(DaggerfallInput.Attack); break;
            case DaggerfallUiActionKind.Loot: firstStep.Request(DaggerfallInput.Interact); break;
            case DaggerfallUiActionKind.DialogueTone:
            case DaggerfallUiActionKind.DialogueTopic:
            case DaggerfallUiActionKind.DialogueClose: _ = ApplyDialogueAction(action); break;
            case DaggerfallUiActionKind.TavernBuy: BuyTavernMeal(action); break;
            case DaggerfallUiActionKind.TrainingCommit: ApplyTrainingAction(action); break;
            case DaggerfallUiActionKind.MerchantBuy:
            case DaggerfallUiActionKind.MerchantSell:
            case DaggerfallUiActionKind.MerchantRepair:
            case DaggerfallUiActionKind.MerchantCollectRepair:
            case DaggerfallUiActionKind.MerchantIdentify:
            case DaggerfallUiActionKind.MerchantShoplift: ChangeMerchant(action); break;
            case DaggerfallUiActionKind.MerchantQuote: QuoteMerchantTrade(action); break;
            case DaggerfallUiActionKind.TransportSelect:
            case DaggerfallUiActionKind.TransportToggle:
            case DaggerfallUiActionKind.TransportLeaveShip:
            case DaggerfallUiActionKind.TransportBoardShip: ChangeTransport(action); break;
            case DaggerfallUiActionKind.PropertyBuy:
            case DaggerfallUiActionKind.PropertySell:
            case DaggerfallUiActionKind.PropertyEnter:
            case DaggerfallUiActionKind.PropertyPut:
            case DaggerfallUiActionKind.PropertyTake: ChangeProperty(action); break;
            case DaggerfallUiActionKind.TravelSearch:
            case DaggerfallUiActionKind.TravelPreview: ChangeTravel(action); break;
            case DaggerfallUiActionKind.TravelMap: ChangeTravelMap(action); break;
            case DaggerfallUiActionKind.TravelAccept:
                if (!elapsedSubmitted) { elapsedSubmitted = true; AcceptTravel(action.Key!, action.Amount!.Value); }
                break;
            // One rest per input slice: explicit elapsed time is applied once.
            case DaggerfallUiActionKind.LodgingQuote:
            case DaggerfallUiActionKind.LodgingBook: ChangeLodging(action); break;
            case DaggerfallUiActionKind.Rest:
                if (!elapsedSubmitted) { ChangeRest(action); elapsedSubmitted = true; }
                break;
            case DaggerfallUiActionKind.WagonPut:
            case DaggerfallUiActionKind.WagonTake: ChangeWagon(action); break;
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
            case DaggerfallUiActionKind.DungeonTextClose: ApplyDungeonTextInput(action); break;
            case DaggerfallUiActionKind.InventoryMove: _inventoryUi.Move(action); break;
            case DaggerfallUiActionKind.InventoryInspect: _inventoryUi.Inspect(action); break;
            case DaggerfallUiActionKind.InventoryUse: _inventoryUi.Use(action); break;
            case DaggerfallUiActionKind.InventoryDrop: _inventoryUi.Drop(action); break;
            case DaggerfallUiActionKind.InventorySort: _inventoryUi.Sort(); break;
            case DaggerfallUiActionKind.NotebookPage:
            case DaggerfallUiActionKind.NotebookAdd:
            case DaggerfallUiActionKind.NotebookEdit:
            case DaggerfallUiActionKind.NotebookRemove:
            case DaggerfallUiActionKind.NotebookMove: ApplyNotebookAction(action); break;
            case DaggerfallUiActionKind.CurrencyDepositGold:
            case DaggerfallUiActionKind.CurrencyWithdrawGold:
            case DaggerfallUiActionKind.CurrencyDepositLetters:
            case DaggerfallUiActionKind.CurrencyWithdrawLetter:
            case DaggerfallUiActionKind.BankTransfer: ChangeCurrency(action); break;
            case DaggerfallUiActionKind.BankOpen: OpenCurrentBank(action.Revision); break;
            case DaggerfallUiActionKind.BankLoanIssue:
            case DaggerfallUiActionKind.BankLoanRepayAccount:
            case DaggerfallUiActionKind.BankLoanRepayCarried: ChangeLoan(action); break;
            case DaggerfallUiActionKind.LootClose: _lootUi.Close(action.Container); break;
            case DaggerfallUiActionKind.LootTake: TakeLoot(action); break;
            case DaggerfallUiActionKind.LootTakeAll: TakeAllLoot(action); break;
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
            case DaggerfallUiActionKind.DeathQuit: HandleDeathAction(action); break;
            default: throw new InvalidOperationException($"Admitted UI action '{action.Kind}' has no dispatch.");
        }
    }

    /// <summary>
    /// Takes every row of the shown container through the ordinary take, each whole and against the
    /// container's current revision, and stops at the first one refused (too heavy, out of reach), so
    /// what was taken stays taken and the refusal is reported. It reports once, not once per row.
    /// </summary>
    private void TakeAllLoot(DaggerfallPlayerUiAction action)
    {
        LootPresentation? shown = _lootUi.Read();
        if (shown is null || shown.Container != action.Container || shown.Revision != action.Revision)
        {
            Presentation.SetOutcome("Loot changed. Choose the item again.");
            return;
        }
        int taken = 0;
        foreach (string key in shown.Items.Select(item => item.Key).ToArray())
        {
            LootPresentation? current = _lootUi.Read();
            if (current?.Items.SingleOrDefault(item => item.Key == key) is not { } row) continue;
            TakeLoot(new DaggerfallPlayerUiAction("loot-take", current.Revision, key, Container: current.Container,
                Amount: ulong.Parse(row.Quantity, CultureInfo.InvariantCulture)), report: false);
            if (_lootUi.Read()?.Items.Any(item => item.Key == key) ?? false) break;
            taken++;
        }
        _lootUi.CompleteAll(taken, _lootUi.Read()?.Empty ?? true);
        Presentation.SetOutcome(_lootUi.Message);
    }

    /// <summary>
    /// A valid take applies at once: the transfer commits, its completed-change facts deliver at the
    /// update's boundary even while the modal holds the world, and the published presentation already
    /// reflects the result. A refused take (a stale revision, a closed container) states its reason too.
    /// </summary>
    private void TakeLoot(DaggerfallPlayerUiAction action, bool report = true)
    {
        if (_lootUi.PrepareGroundTake(action) is { } groundTake)
        {
            if (!State.Encumbrance.CanCarry(_definitions.RequireItem(new DaggerfallItemId(groundTake.Definition)), groundTake.Quantity,
                groundTake.Selection.UniqueEntityId is ulong entity ? State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(new(entity)).Value) : null))
            {
                _lootUi.CompleteGround(false, "You cannot carry any more.");
                if (report) Presentation.SetOutcome(_lootUi.Message);
                return;
            }
            try
            {
                _groundContainers.TryGet(groundTake.Id, out var source);
                var questItem = QuestLootMetadata(DaggerfallItemOwner.Ground(groundTake.Id), groundTake.Selection);
                var transfer = _groundContainers.Take(groundTake.Id, groundTake.Selection, groundTake.ExpectedWorldRevision);
                if (questItem is not null) State.Quests.ItemClicked(questItem);
                ObservePropertyLoot(groundTake, source, transfer);
                _lootUi.CompleteGround(true);
            }
            catch (Exception rejection) when (rejection is InvalidOperationException or ArgumentException)
            {
                _lootUi.CompleteGround(false, "That item cannot be taken.");
            }
            if (report) Presentation.SetOutcome(_lootUi.Message);
            return;
        }
        if (_lootUi.PrepareTake(action, State.PlayerControl, _input.ResolveCurrentLook(State.PlayerControl)) is { } take)
        {
            var questItem = take.Selection is { } selection ? QuestLootMetadata(DaggerfallItemOwner.Corpse(take.ActorId), selection) : null;
            var result = _corpseLoot.TryCommitLoot(take, _facts);
            if (result == CorpseLootCommitResult.Committed && questItem is not null) State.Quests.ItemClicked(questItem);
            _lootUi.Complete(result);
        }
        if (report) Presentation.SetOutcome(_lootUi.Message);
    }
}
