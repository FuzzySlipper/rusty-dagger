using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The conversation's actions and its view: each action reads its revision and asks Dialogue's owner, and the view
/// the HUD draws is the conversation's page with the services this person offers.
/// </summary>
internal sealed partial class DaggerfallSession
{
    private bool ApplyDialogueAction(DaggerfallPlayerUiAction action)
    {
        if (action.Kind is not (DaggerfallUiActionKind.DialogueTone or DaggerfallUiActionKind.DialogueTopic or DaggerfallUiActionKind.DialogueClose) || _dialogue is null)
            return false;
        _ = action.Kind switch
        {
            DaggerfallUiActionKind.DialogueClose => _dialogue.End(action.Revision),
            DaggerfallUiActionKind.DialogueTone => _dialogue.ChooseTone(action.Revision, action.Tone),
            _ => _dialogue.Ask(action.Revision, action.Topic, action.Amount, action.Key),
        };
        return true;
    }

    internal DaggerfallActivationView ActivationView => _activationPresentation.View with
    {
        Dialogue = _activationPresentation.View.Dialogue is { } dialogue ? WithServices(dialogue with
        {
            BankAvailable = Banking.Offers(_dialogue?.CurrentNpc()),
            ComprehendLanguagesBonus = State.Effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId).ComprehendLanguagesBonus,
            Training = State.SkillTraining.ReadProvider(_dialogue?.CurrentNpc(dialogue.Revision)),
            Tavern = State.Tavern.ReadAt(_dialogue?.CurrentNpc(dialogue.Revision), CurrentInteriorBuilding),
            Merchant = ReadCurrentMerchant(),
        }) : null,
    };

    // The chooser lists a service exactly when the view its page draws is published: the dialogue's own
    // trade, training and tavern, and the spellbook's sale, makers and summoning for this person.
    private DaggerfallDialogueView WithServices(DaggerfallDialogueView dialogue) => dialogue with
    {
        Services = DaggerfallPersonServices.For(dialogue.Merchant, dialogue.Training is not null, dialogue.Tavern is not null,
            ReadSpellSale() is not null,
            CurrentSpellProvider(service: "make-magic-items") is not null,
            CurrentSpellProvider(service: "make-potions") is not null,
            CurrentSpellProvider(service: "make-spells") is not null,
            CurrentSummoner() is not null),
    };
}
