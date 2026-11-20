using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Admission and dispatch of the <c>dagger.ui.action.v1</c> player actions. Each payload is parsed
/// once per update into a typed action; its declared phases decide whether it acts, and one switch
/// over the typed kind routes it to the panel that decodes it or, where the decode is one call, to the
/// domain owner itself. What an action does across domains is a Session list it is handed in <see cref="SessionCalls"/>.
/// The shortcut keys and the idle panel keys are decoded here too.
/// </summary>
/// <param name="quickEntry">Whether the bundle begins with a quick-start character.</param>
/// <param name="playerInput">The player's input, whose panel and shortcut keys are taken here between steps.</param>
/// <param name="refusals">Where a press this dispatch drops says why.</param>
internal sealed class DaggerfallUiDispatch(DaggerfallCharacterState character, bool quickEntry, DaggerfallRacialOverrides racialOverrides,
    DaggerfallLaw law, DaggerfallPlayerTime playerTime, DaggerfallCreateItem createItem, DaggerfallIdentify identify,
    DaggerfallTeleport teleport, DaggerfallDispel dispel, DaggerfallOpeningCinematics openingCinematics,
    DaggerfallCinematicPresentation? cinematics, DaggerfallTutorial tutorial, DaggerfallPlayerAttack attack,
    DaggerfallQuestInstances quests, DaggerfallVariableStore variables, PlayerInputSystem playerInput, DaggerfallHudProjection hud,
    DaggerfallInventoryPresentation inventory, DaggerfallLootPresentation loot, DaggerfallCharacterActions characterActions,
    DaggerfallLocalMap map, DaggerfallActivationPanel activation, DaggerfallSpellbookPanel spellbook, DaggerfallMakerPanels makers,
    DaggerfallSummoningPanel summoning, DaggerfallEconomyPanels economy, DaggerfallExpeditionPanel travel, DaggerfallRestPanel rest,
    DaggerfallTextActions text, DaggerfallLootActions lootActions, DaggerfallDeathScreen death, DaggerfallUiDispatch.SessionCalls session,
    PresentationState presentation, DaggerfallInputRefusals refusals)
{
    /// <summary>What the dispatch asks of the session: its cross-domain lists, its open interactions and the Host's requests.</summary>
    /// <param name="ChooseLegal">The legal choice's list.</param>
    /// <param name="AnswerQuestOffer">The quest offer answer's list.</param>
    /// <param name="ToggleWeapon">Readies or sheathes the weapon.</param>
    /// <param name="RequestPanel">Asks the DOM for a panel, refused for the inventory in a form that suppresses it.</param>
    /// <param name="RequestSaveSlot">Leaves a save-slot request for the Host to take.</param>
    /// <param name="ChooseBundle">Records the game chosen at the entry screen.</param>
    /// <param name="StartUnderChosenBundle">Asks the Host for the chosen game, to start it there, when it is not the running one.</param>
    /// <param name="SetMenuOpen">Opens or closes the game menu over the world.</param>
    /// <param name="RequestQuit">Leaves the player's quit for the Host to take.</param>
    /// <param name="Applied">Hears each action applied, which may leave the game changed since it was kept.</param>
    /// <param name="HoldsWorldOpen">Whether something open holds the world still.</param>
    internal sealed record SessionCalls(Action<string, string> ChooseLegal, Action<string, bool> AnswerQuestOffer, Action ToggleWeapon,
        Action<string> RequestPanel, Action<SaveSlotRequest> RequestSaveSlot, Action<string> ChooseBundle,
        Func<GameBundleStart, bool> StartUnderChosenBundle, Action<bool> SetMenuOpen, Action RequestQuit,
        Action<DaggerfallUiActionKind> Applied, Func<bool> HoldsWorldOpen);

    /// <summary>
    /// The parse the entry-screen check made of this update's input, reused by the update itself so
    /// no payload is parsed twice. It is taken by the next update and never outlives the input it read.
    /// </summary>
    private DaggerfallUiInput? _parsed;

    internal bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input)
    {
        DaggerfallUiInput parsed = _parsed is { } cached && cached.Matches(input) ? cached : DaggerfallUiInput.Parse(input);
        _parsed = parsed;
        // Quick start commits its character in this update's actions, before the product begins from it.
        if (parsed.Contains(DaggerfallUiActionKind.CharacterQuickStart)) return true;
        if (parsed.Contains(DaggerfallUiActionKind.Begin) && character.Pending is not null && !quickEntry)
        {
            presentation.SetOutcome("Commit or cancel character choices before beginning play.");
            return false;
        }
        // Begin under a game other than the running one is the Host's to start, once it has switched to that game.
        return parsed.Contains(DaggerfallUiActionKind.Begin) && !session.StartUnderChosenBundle(GameBundleStart.Begin);
    }

    /// <summary>Takes this update's parsed UI actions: the entry-screen check's parse when it read these events.</summary>
    internal DaggerfallUiInput Take(ReadOnlySpan<ProductInputEvent> input)
    {
        DaggerfallUiInput parsed = _parsed is { } cached && cached.Matches(input) ? cached : DaggerfallUiInput.Parse(input);
        _parsed = null;
        return parsed;
    }

    /// <summary>
    /// Whether this admitted slice consumes attack/use as a contextual interaction or mode change. The
    /// slice that opens an interaction admits no attack: a key pressed in the same admitted update as
    /// the interaction key is a coincidence of timing rather than an instruction.
    /// </summary>
    internal static bool OpensInteraction(DaggerfallUiInput input) => input.ContainsAny(
        DaggerfallUiActionKind.Loot, DaggerfallUiActionKind.ActivationMode, DaggerfallUiActionKind.DialogueTopic,
        DaggerfallUiActionKind.TavernBuy, DaggerfallUiActionKind.TrainingCommit, DaggerfallUiActionKind.Rest, DaggerfallUiActionKind.LodgingBook, DaggerfallUiActionKind.ExpeditionOpen);

    /// <summary>
    /// Admits one UI payload in the current phase. An unrecognized payload is reported in a live phase;
    /// a recognized action that its phase does not admit is dropped, or reports its declared refusal.
    /// Death owns the session's input while the Host decides the resulting replacement, so every
    /// payload death does not admit is consumed silently.
    /// </summary>
    internal void Admit(DaggerfallPlayerUiAction? action, DaggerfallUiPhases phase, ProductUpdateState firstStep,
        bool opensInteraction, ref bool elapsedSubmitted)
    {
        if (action is null)
        {
            if (phase != DaggerfallUiPhases.Dead) presentation.SetOutcome(DaggerfallUiAction.UnrecognizedRefusal);
            return;
        }
        DaggerfallUiActionRule rule = DaggerfallUiAction.RuleFor(action.Kind);
        string? act = action.Kind switch
        {
            DaggerfallUiActionKind.Attack => "Attack", DaggerfallUiActionKind.SpellCast => "Cast",
            DaggerfallUiActionKind.Loot => "Use", DaggerfallUiActionKind.WeaponToggle => "Weapon toggle", _ => null,
        };
        if (!rule.Admits(phase))
        {
            if (act is not null) refusals.Dropped(act, phase switch
            {
                DaggerfallUiPhases.Dead => "dead", DaggerfallUiPhases.Modal => "a modal holds the world", _ => "play is held",
            });
            if (phase != DaggerfallUiPhases.Dead && rule.Refusal is { } refusal) presentation.SetOutcome(refusal);
            return;
        }
        if (racialOverrides.Current?.SuppressInventory == true && action.Kind is
            DaggerfallUiActionKind.InventoryMove or
            DaggerfallUiActionKind.InventoryInspect or DaggerfallUiActionKind.InventoryUse or DaggerfallUiActionKind.InventoryDrop or DaggerfallUiActionKind.InventorySort
            or DaggerfallUiActionKind.BankOpen)
        {
            presentation.SetOutcome(DaggerfallRacialOverrideView.InventoryRefusal);
            return;
        }
        // The court admits its answer, the saves, and the UI's report of a press it kept from play, which changes nothing.
        if (law.ModalOpen && action.Kind is not (DaggerfallUiActionKind.LegalChoice or DaggerfallUiActionKind.SaveGame
            or DaggerfallUiActionKind.LoadGame or DaggerfallUiActionKind.SaveSlots or DaggerfallUiActionKind.SaveSlot
            or DaggerfallUiActionKind.LoadSlot or DaggerfallUiActionKind.InputClaimed))
        {
            if (act is not null) refusals.Dropped(act, "the court waits for an answer");
            return;
        }
        Apply(action, firstStep, opensInteraction, ref elapsedSubmitted);
        session.Applied(action.Kind);
        // These immediate UI actions may arrive while no world step is due. Spend a step when
        // play resumes; reading panels, preparing spells and editing preferences remain free.
        if (action.Kind is DaggerfallUiActionKind.SpellCast or DaggerfallUiActionKind.InventoryUse
            or DaggerfallUiActionKind.InventoryMove or DaggerfallUiActionKind.InventoryDrop
            or DaggerfallUiActionKind.TransportSelect)
            playerTime.RequestWorldAction();
    }

    private void Apply(DaggerfallPlayerUiAction action, ProductUpdateState firstStep, bool opensInteraction, ref bool elapsedSubmitted)
    {
        switch (action.Kind)
        {
            // The entry screen's own action is the product's to answer; the product has already left
            // the mode by the time an action in ordinary play could arrive. The DOM owns its panels.
            case DaggerfallUiActionKind.DaedraSummon:
            case DaggerfallUiActionKind.DaedraAnswer:
                summoning.Change(action); break;
            case DaggerfallUiActionKind.Begin:
                characterActions.Begin();
                break;
            // A running game already has its content; only the unavailable session imports it.
            case DaggerfallUiActionKind.ContentImport:
            case DaggerfallUiActionKind.ContentImportCancel:
                break;
            case DaggerfallUiActionKind.LegalChoice: session.ChooseLegal(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.CreateItemSelect: createItem.Choose(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.CreateItemCancel: createItem.Cancel(action.Revision!); break;
            case DaggerfallUiActionKind.IdentifySelect: identify.Choose(action.Revision!,action.Key); break;
            case DaggerfallUiActionKind.IdentifyCancel: identify.Choose(action.Revision!,null); break;
            case DaggerfallUiActionKind.TeleportSelect: teleport.Choose(action.Revision!, action.Key!); break;
            case DaggerfallUiActionKind.DispelSelect: dispel.Choose(action.Revision!, action.Key); break;
            case DaggerfallUiActionKind.DispelCancel: dispel.Choose(action.Revision!, null); break;
            case DaggerfallUiActionKind.MapOpen: map.SetOpen(action.Open); break;
            case DaggerfallUiActionKind.MapBuilding: map.SelectBuilding(action); break;
            case DaggerfallUiActionKind.MapNoteAdd:
            case DaggerfallUiActionKind.MapNoteEdit:
            case DaggerfallUiActionKind.MapNoteRemove: map.ChangeNote(action); break;
            case DaggerfallUiActionKind.Menu: session.SetMenuOpen(action.Open); break;
            case DaggerfallUiActionKind.CinematicSkip:
                if (openingCinematics.IsActive) openingCinematics.Skip();
                else cinematics?.Skip();
                break;
            // A reloaded DOM holds no art and asks for the revision it is missing; the projection
            // answers on its next snapshot rather than a second delivery channel existing.
            case DaggerfallUiActionKind.ArtRequest: hud.RequestArt(action.Revision!); break;
            case DaggerfallUiActionKind.ActionDrivenTime:
            case DaggerfallUiActionKind.ControlsLook:
            case DaggerfallUiActionKind.ControlsUiScale:
            case DaggerfallUiActionKind.ControlsGroundCover:
            case DaggerfallUiActionKind.ControlsClutterDistance:
            case DaggerfallUiActionKind.ControlsLampShadows:
            case DaggerfallUiActionKind.ControlsSurfaceRelief:
            case DaggerfallUiActionKind.ControlsRebind:
            case DaggerfallUiActionKind.ControlsRebindButton:
            case DaggerfallUiActionKind.ControlsVolume:
            case DaggerfallUiActionKind.ControlsReset: characterActions.ChangeControls(action); break;
            // Creating a character under another game opens creation there, once the Host has switched to it.
            case DaggerfallUiActionKind.CharacterBegin when session.StartUnderChosenBundle(GameBundleStart.CreateCharacter): break;
            case DaggerfallUiActionKind.CharacterClassQuestions:
            case DaggerfallUiActionKind.CharacterClassAnswer:
            case DaggerfallUiActionKind.CharacterClassBack:
            case DaggerfallUiActionKind.CharacterBegin:
            case DaggerfallUiActionKind.CharacterUpdate:
            case DaggerfallUiActionKind.CharacterBackgroundReroll:
            case DaggerfallUiActionKind.CharacterCommit:
            case DaggerfallUiActionKind.CharacterCancel:
            case DaggerfallUiActionKind.CharacterQuickStart: characterActions.ChangeCharacter(action); break;
            case DaggerfallUiActionKind.CharacterLevelAllocate:
            case DaggerfallUiActionKind.CharacterLevelCommit: characterActions.ChangeLevelUp(action); break;
            case DaggerfallUiActionKind.ActivationMode: activation.ChangeMode(action); break;
            case DaggerfallUiActionKind.SpellReady:
            case DaggerfallUiActionKind.SpellUnready:
                if (!opensInteraction) spellbook.ChangeSpell(action); break;
            case DaggerfallUiActionKind.SpellBuy:
            case DaggerfallUiActionKind.SpellDelete:
            case DaggerfallUiActionKind.SpellSwap:
            case DaggerfallUiActionKind.SpellSort:
            case DaggerfallUiActionKind.SpellInfo: spellbook.ChangeSpellbook(action); break;
            case DaggerfallUiActionKind.ItemMakerDraft:
            case DaggerfallUiActionKind.ItemMakerBuy: makers.ChangeItemMaker(action); break;
            case DaggerfallUiActionKind.PotionMix: makers.MakePotion(action); break;
            case DaggerfallUiActionKind.SpellMakerDraft:
            case DaggerfallUiActionKind.SpellMakerBuy: spellbook.ChangeSpellMaker(action); break;
            case DaggerfallUiActionKind.SpellCast:
                if (opensInteraction) refusals.Dropped("Cast", "an interaction opened in the same update");
                else if (session.HoldsWorldOpen()) refusals.Dropped("Cast", "an open screen holds the world");
                else attack.Clock.PressCast();
                break;
            case DaggerfallUiActionKind.Attack:
                if (opensInteraction) refusals.Dropped("Attack", "an interaction opened in the same update");
                else firstStep.Request(DaggerfallInput.Attack);
                break;
            case DaggerfallUiActionKind.Loot: firstStep.Request(DaggerfallInput.Interact); break;
            case DaggerfallUiActionKind.WeaponToggle: session.ToggleWeapon(); break;
            case DaggerfallUiActionKind.InputClaimed: refusals.Dropped(DaggerfallInput.WorldAct(action.Item!)!, $"the {action.Key} has the cursor"); break;
            case DaggerfallUiActionKind.TutorialStart: tutorial.Start(); break;
            case DaggerfallUiActionKind.TutorialContinue: tutorial.Continue(action.Page!.Value); break;
            case DaggerfallUiActionKind.TutorialEnd: tutorial.End(); break;
            case DaggerfallUiActionKind.DialogueTone:
            case DaggerfallUiActionKind.DialogueTopic:
            case DaggerfallUiActionKind.DialogueClose: _ = activation.ApplyDialogue(action); break;
            case DaggerfallUiActionKind.TavernBuy: economy.BuyTavernMeal(action); break;
            case DaggerfallUiActionKind.TrainingCommit: economy.ApplyTraining(action); break;
            case DaggerfallUiActionKind.MerchantBuy:
            case DaggerfallUiActionKind.MerchantSell:
            case DaggerfallUiActionKind.MerchantRepair:
            case DaggerfallUiActionKind.MerchantCollectRepair:
            case DaggerfallUiActionKind.MerchantIdentify:
            case DaggerfallUiActionKind.MerchantShoplift: economy.ChangeMerchant(action); break;
            case DaggerfallUiActionKind.MerchantQuote: economy.QuoteMerchantTrade(action); break;
            case DaggerfallUiActionKind.TransportSelect:
            case DaggerfallUiActionKind.TransportLeaveShip:
            case DaggerfallUiActionKind.TransportBoardShip: travel.ChangeTransport(action); break;
            case DaggerfallUiActionKind.PropertyBuy:
            case DaggerfallUiActionKind.PropertySell:
            case DaggerfallUiActionKind.PropertyEnter:
            case DaggerfallUiActionKind.PropertyPut:
            case DaggerfallUiActionKind.PropertyTake: economy.ChangeProperty(action); break;
            case DaggerfallUiActionKind.ExpeditionOpen: travel.Open(); break;
            case DaggerfallUiActionKind.ExpeditionPlaces: travel.RequestPlaces(); break;
            case DaggerfallUiActionKind.AtlasTiles: travel.GrantAtlasTiles(action); break;
            // Entering changes the world, so it takes the update's one explicit interval, as a rest does.
            case DaggerfallUiActionKind.ExpeditionEnter:
                if (!elapsedSubmitted) { elapsedSubmitted = true; travel.Change(action); }
                break;
            case DaggerfallUiActionKind.ExpeditionSpeed:
            case DaggerfallUiActionKind.ExpeditionMode:
            case DaggerfallUiActionKind.ExpeditionGo:
            case DaggerfallUiActionKind.ExpeditionStop:
            case DaggerfallUiActionKind.ExpeditionPin:
            case DaggerfallUiActionKind.ExpeditionFind:
            case DaggerfallUiActionKind.ExpeditionShip:
            case DaggerfallUiActionKind.ExpeditionLeave: travel.Change(action); break;
            // One rest per input slice: explicit elapsed time is applied once.
            case DaggerfallUiActionKind.LodgingQuote:
            case DaggerfallUiActionKind.LodgingBook: economy.ChangeLodging(action); break;
            case DaggerfallUiActionKind.Rest:
                if (!elapsedSubmitted) { rest.Change(action); elapsedSubmitted = true; }
                break;
            case DaggerfallUiActionKind.WagonPut:
            case DaggerfallUiActionKind.WagonTake: travel.ChangeWagon(action); break;
            case DaggerfallUiActionKind.QuestChoice:
                if (quests.ChoosePrompt(variables, action.QuestInstance!, action.QuestMessage!.Value, action.QuestPrompt!, action.QuestChoice!.Value))
                    presentation.SetOutcome("Quest choice recorded.");
                else presentation.SetOutcome(DaggerfallUiAction.RuleFor(DaggerfallUiActionKind.QuestChoice).Refusal!);
                break;
            case DaggerfallUiActionKind.QuestOfferAnswer: session.AnswerQuestOffer(action.QuestInstance!, action.Confirm); break;
            case DaggerfallUiActionKind.QuestDismiss:
                _ = quests.DismissRewardMessage(action.QuestInstance!, action.QuestDelivery!);
                break;
            case DaggerfallUiActionKind.DungeonTextAnswer:
            case DaggerfallUiActionKind.DungeonTextClose: text.ApplyDungeonText(action); break;
            case DaggerfallUiActionKind.InventoryMove: inventory.Move(action); break;
            case DaggerfallUiActionKind.InventoryInspect: inventory.Inspect(action); break;
            case DaggerfallUiActionKind.InventoryUse: inventory.Use(action); break;
            case DaggerfallUiActionKind.InventoryDrop: inventory.Drop(action); break;
            case DaggerfallUiActionKind.InventorySort: inventory.Sort(); break;
            case DaggerfallUiActionKind.NotebookPage:
            case DaggerfallUiActionKind.NotebookAdd:
            case DaggerfallUiActionKind.NotebookEdit:
            case DaggerfallUiActionKind.NotebookRemove:
            case DaggerfallUiActionKind.NotebookMove: text.ApplyNotebook(action); break;
            case DaggerfallUiActionKind.CurrencyDepositGold:
            case DaggerfallUiActionKind.CurrencyWithdrawGold:
            case DaggerfallUiActionKind.CurrencyDepositLetters:
            case DaggerfallUiActionKind.CurrencyWithdrawLetter:
            case DaggerfallUiActionKind.BankTransfer: economy.ChangeCurrency(action); break;
            case DaggerfallUiActionKind.BankOpen: economy.OpenCurrentBank(action.Revision); break;
            case DaggerfallUiActionKind.BankLoanIssue:
            case DaggerfallUiActionKind.BankLoanRepayAccount:
            case DaggerfallUiActionKind.BankLoanRepayCarried: economy.ChangeLoan(action); break;
            case DaggerfallUiActionKind.LootClose: loot.Close(action.Container); break;
            case DaggerfallUiActionKind.LootTake: lootActions.Take(action); break;
            case DaggerfallUiActionKind.LootTakeAll: lootActions.TakeAll(action); break;
            // Quick save and quick load follow the Host's slot naming; the menu's slot actions name
            // their own slots. All of them use the same catalog owner.
            case DaggerfallUiActionKind.SaveGame: session.RequestSaveSlot(new(SaveSlotOperation.QuickSave)); break;
            case DaggerfallUiActionKind.LoadGame: session.RequestSaveSlot(new(SaveSlotOperation.QuickLoad)); break;
            case DaggerfallUiActionKind.SaveSlots: session.RequestSaveSlot(new(SaveSlotOperation.List)); break;
            case DaggerfallUiActionKind.SaveSlot: session.RequestSaveSlot(new(SaveSlotOperation.Save, action.Key, action.Label, action.Confirm)); break;
            case DaggerfallUiActionKind.LoadSlot: session.RequestSaveSlot(new(SaveSlotOperation.Load, action.Key)); break;
            case DaggerfallUiActionKind.BundleSelect: session.ChooseBundle(action.Key!); break;
            case DaggerfallUiActionKind.DeleteSlot: session.RequestSaveSlot(new(SaveSlotOperation.Delete, action.Key, Confirm: action.Confirm)); break;
            case DaggerfallUiActionKind.DeathNewGame:
            case DaggerfallUiActionKind.DeathLoadGame:
            case DaggerfallUiActionKind.DeathQuit: death.Choose(action); break;
            case DaggerfallUiActionKind.Quit: session.RequestQuit(); break;
            default: throw new InvalidOperationException($"Admitted UI action '{action.Kind}' has no dispatch.");
        }
    }

    // Opening a panel or preparing a spell must work without spending a world step. Physical
    // attack/use edges remain queued in the input owner until an Engine-admitted step consumes them.
    internal void ApplyIdleInterfaceActions()
    {
        bool panel = false;
        foreach (var (action, name) in new[]
        {
            (DaggerfallInput.Inventory, DaggerfallPanel.Inventory),
            (DaggerfallInput.Character, DaggerfallPanel.Character),
            (DaggerfallInput.Map, DaggerfallPanel.Map),
            (DaggerfallInput.Menu, DaggerfallPanel.Menu),
        })
            if (playerInput.TakeAction(action)) { session.RequestPanel(name); panel = true; }
        foreach (string shortcut in DaggerfallInput.Shortcuts)
            if (playerInput.TakeAction(DaggerfallInput.Shortcut(shortcut))) { ApplyShortcut(shortcut); panel = true; }
        if (!panel) return;
        foreach (var (action, name) in DaggerfallInput.WorldActs)
            if (playerInput.TakeAction(action)) refusals.Dropped(name, "a panel key took the same update");
        playerInput.ClearPendingActions();
    }

    /// <summary>What a shortcut key asks for: a panel, a save, an activation mode or a spell.</summary>
    internal void ApplyShortcut(string id)
    {
        if (id.StartsWith("activation.", StringComparison.Ordinal))
        {
            activation.ChangeMode(new(DaggerfallUiAction.RuleFor(DaggerfallUiActionKind.ActivationMode).Wire, Mode: id["activation.".Length..]));
            return;
        }
        switch (id)
        {
            case "quick-save": session.RequestSaveSlot(new(SaveSlotOperation.QuickSave)); break;
            // Quick load discards progress, so the key asks the screen to confirm it, as the menu's button does.
            case "quick-load": session.RequestPanel("quick-load"); break;
            case "spell": session.RequestPanel("spells"); break;
            case "rest": session.RequestPanel("rest"); break;
            case "transport": session.RequestPanel(DaggerfallPanel.Travel); break;
            case "logbook": session.RequestPanel(DaggerfallPanel.Journal); break;
            case "notebook": session.RequestPanel(DaggerfallPanel.Notebook); break;
            case "travel-map": travel.Open(); break;
            case "use-magic-item": session.RequestPanel("magic-items"); break;
            case "radial": session.RequestPanel(DaggerfallPanel.Radial); break;
            case "abort-spell": spellbook.ChangeSpell(new(DaggerfallUiAction.RuleFor(DaggerfallUiActionKind.SpellUnready).Wire)); break;
            case "recast":
                attack.Clock.PressCast();
                break;
        }
    }
}
