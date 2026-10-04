using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Training's semantic dialogue action boundary over the canonical service transaction.</summary>
internal sealed partial class DaggerfallSession
{
    private void ApplyTrainingAction(DaggerfallPlayerUiAction action)
    {
        if (action.Revision is not { Length: > 0 } revision || action.Key is not { Length: > 0 } skill
            || action.Amount is not ulong quotedPrice)
        {
            Presentation.SetOutcome("Training request is incomplete.");
            return;
        }
        if (!action.Confirm)
        {
            Presentation.SetOutcome("Confirm the quoted training price before paying.");
            return;
        }

        DaggerfallNpc? npc = _dialogue?.CurrentNpc(revision);
        if (npc is null || !npc.Services.Contains(DaggerfallSkillTrainingPolicy.ServiceName, StringComparer.Ordinal))
        {
            Presentation.SetOutcome("That trainer is no longer available.");
            return;
        }

        DaggerfallServiceProvider provider = new(npc.DurableId, npc.Site, DaggerfallSkillTrainingPolicy.ServiceName);
        DaggerfallSkillTrainingProviderView? view = State.SkillTraining.ReadProvider(provider);
        if (view is null)
        {
            Presentation.SetOutcome("That person does not offer skill training.");
            return;
        }
        if (view.Price != quotedPrice)
        {
            Presentation.SetOutcome("The training price changed. Review the current offer.");
            return;
        }

        DaggerfallServiceRequest request = new(
            $"dialogue-training:{revision}:{skill}", provider);
        DaggerfallSkillTrainingQuoteResult quoted = State.SkillTraining.Quote(request, skill);
        if (!quoted.IsQuoted)
        {
            Presentation.SetOutcome(TrainingMessage(quoted.TrainingDenial, quoted.ServiceOutcome?.Denial));
            return;
        }

        DaggerfallSkillTrainingOutcome outcome = State.SkillTraining.Commit(quoted.Quote!);
        Presentation.SetOutcome(outcome.Accepted
            ? $"Training complete: {skill} is now {outcome.PermanentValue}."
            : TrainingMessage(outcome.TrainingDenial, outcome.ServiceOutcome?.Denial));
        if (outcome.Accepted) _dialogue?.RefreshEligibility();
    }

    private static string TrainingMessage(DaggerfallSkillTrainingDenial denial, DaggerfallServiceDenial? serviceDenial) =>
        denial switch
        {
            DaggerfallSkillTrainingDenial.SkillNotOffered => "That trainer does not teach this skill.",
            DaggerfallSkillTrainingDenial.SkillAtLimit => "That skill has reached the trainer's limit.",
            DaggerfallSkillTrainingDenial.TrainingTooSoon => "You have already trained during this period.",
            DaggerfallSkillTrainingDenial.UnsupportedProvider => "That person does not offer skill training.",
            DaggerfallSkillTrainingDenial.QuoteChanged => "The training offer changed. Review the current price.",
            _ => serviceDenial switch
            {
                DaggerfallServiceDenial.NotMember => "You are not a member of the required guild.",
                DaggerfallServiceDenial.InsufficientRank => "Your guild rank is too low for this service.",
                DaggerfallServiceDenial.InsufficientFunds => "You do not have enough gold for training.",
                DaggerfallServiceDenial.ProviderUnavailable or DaggerfallServiceDenial.SiteChanged
                    or DaggerfallServiceDenial.ServiceUnavailable => "That trainer is no longer available.",
                _ => "Training was refused.",
            },
        };
}
