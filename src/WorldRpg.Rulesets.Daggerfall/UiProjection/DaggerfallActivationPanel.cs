using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Activation and the conversation as the player sees them: the mode action, the conversation's actions, and the view
/// the HUD draws, which is the activation view with the conversation's page and the services this person offers.
/// </summary>
/// <param name="view">The activation view's state: mode, outcome and the conversation's page.</param>
/// <param name="dialogue">The conversation the player is in, which activation rebuilds with each site.</param>
/// <param name="building">The interior building the player stands in.</param>
internal sealed class DaggerfallActivationPanel(DaggerfallActivationPresentation view, DaggerfallActivation activation,
    Func<DaggerfallDialogueService?> dialogue, DaggerfallBanking banking, DaggerfallEffectLifecycle effects,
    DaggerfallSkillTrainingService training, DaggerfallTavernService tavern, Func<DaggerfallInteriorBuilding?> building,
    DaggerfallEconomyPanels economy, DaggerfallSpellbookPanel spellbook, DaggerfallServiceCounter counter,
    PresentationState presentation)
{
    /// <summary>Consumes one parsed mode action; it does not turn into a world activation.</summary>
    internal bool ChangeMode(DaggerfallPlayerUiAction action)
    {
        if (action.Kind != DaggerfallUiActionKind.ActivationMode || action.Mode is null || activation.Module is null) return false;
        DaggerfallActivationMode mode = action.Mode switch
        {
            "grab" => DaggerfallActivationMode.Grab,
            "info" => DaggerfallActivationMode.Info,
            "talk" => DaggerfallActivationMode.Talk,
            "steal" => DaggerfallActivationMode.Steal,
            "lockpick" => DaggerfallActivationMode.Steal,
            "bash" => DaggerfallActivationMode.Bash,
            _ => throw new InvalidOperationException($"Parsed activation mode '{action.Mode}' is not declared."),
        };
        if (mode != DaggerfallActivationMode.Talk) dialogue()?.Close();                          // Dialogue
        activation.ChangeMode(mode);                                                              // Interiors
        view.SetMode(mode);
        presentation.SetOutcome(view.View.Message);
        return true;
    }

    /// <summary>The conversation's actions: each reads its revision and asks Dialogue's owner.</summary>
    internal bool ApplyDialogue(DaggerfallPlayerUiAction action)
    {
        if (action.Kind is not (DaggerfallUiActionKind.DialogueTone or DaggerfallUiActionKind.DialogueTopic or DaggerfallUiActionKind.DialogueClose)
            || dialogue() is not { } conversation)
            return false;
        _ = action.Kind switch
        {
            DaggerfallUiActionKind.DialogueClose => conversation.End(action.Revision),
            DaggerfallUiActionKind.DialogueTone => conversation.ChooseTone(action.Revision, action.Tone),
            _ => conversation.Ask(action.Revision, action.Topic, action.Amount, action.Key),
        };
        return true;
    }

    /// <summary>The activation view the HUD draws, its conversation page carrying this person's services.</summary>
    internal DaggerfallActivationView View => view.View with
    {
        Dialogue = view.View.Dialogue is { } page ? WithServices(page with
        {
            BankAvailable = banking.Offers(dialogue()?.CurrentNpc()),
            ComprehendLanguagesBonus = effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId).ComprehendLanguagesBonus,
            Training = training.ReadProvider(dialogue()?.CurrentNpc(page.Revision)),
            Tavern = tavern.ReadAt(dialogue()?.CurrentNpc(page.Revision), building),
            Merchant = economy.ReadCurrentMerchant(),
            Retainer = economy.ReadCurrentRetainer(),
        }) : null,
    };

    // The chooser lists a service exactly when the view its page draws is published: the dialogue's own
    // trade, retainer gear, training and tavern, and the spellbook's sale, makers and summoning for this person.
    private DaggerfallDialogueView WithServices(DaggerfallDialogueView page) => page with
    {
        Services = DaggerfallPersonServices.For(page.Merchant, page.Training is not null, page.Tavern is not null,
            spellbook.ReadSpellSale() is not null,
            counter.Provider(service: "make-magic-items") is not null,
            counter.Provider(service: "make-potions") is not null,
            counter.Provider(service: "make-spells") is not null,
            counter.Summoner() is not null,
            retainer: page.Retainer is not null),
    };
}
