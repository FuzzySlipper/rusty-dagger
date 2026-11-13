using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSummoningView(string Revision, string Provider, DaggerfallSummoningQuote? Quote,
    string? OfferRevision, string? Prince, string? Message, string[] Diagnostics);
/// <summary>The summoner's pane of the spellbook frame, the prince's answer and its cinematic, worded for the player.</summary>
internal sealed partial class DaggerfallSession
{
    internal DaggerfallDaedricSummoning Summoning { get; private set; } = null!;
    private DaggerfallServiceProvider? CurrentSummoner(string? revision = null)
    {
        var npc = _dialogue?.CurrentNpc(revision);
        return npc?.Services.Contains("daedra-summoning",StringComparer.Ordinal) == true ? new(npc.DurableId,npc.Site,"daedra-summoning") : null;
    }
    internal DaggerfallSummoningView? ReadSummoning()
    {
        if (Cinematics?.ActiveSource is not null) return null;
        if (Summoning.Last is { Outcome:"Offered" } offer)
        {
            var rendered = State.Quests.RenderPreparedOffer(offer.PreparedQuest!,1000);
            return new("","",null,offer.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Summoning.PrinceName(offer.Prince),rendered.Text,[.. rendered.Diagnostics]);
        }
        if (CurrentSummoner() is not { } provider) return null;
        return new(_activationPresentation.View.Dialogue!.Revision,_activationPresentation.View.Dialogue.TargetLabel,
            Summoning.Quote(provider),null,null,null,[]);
    }
    internal void ChangeSummoning(DaggerfallPlayerUiAction action)
    {
        string outcome;
        if (action.Kind == DaggerfallUiActionKind.DaedraAnswer)
        {
            var prior = Summoning.Last;
            outcome = long.TryParse(action.Revision,out long sequence) ? Summoning.Answer(sequence,action.Confirm,offer=>DaggerfallDaedricSummoning.StartQuest(State.Quests,offer)) : "OfferUnavailable";
            if (outcome == "Refused" && prior is not null)
            {
                var text = State.Quests.RenderPreparedOffer(prior.PreparedQuest!,1001);
                Presentation.SetOutcome(text.Text); ReconcileSummoningFoes(); return;
            }
        }
        else if (CurrentSummoner(action.Revision) is not { } provider) outcome="ProviderUnavailable";
        else
        {
            outcome=Summoning.Summon(provider,action.Key!,action.Amount!.Value,action.Confirm);
            if (outcome is "Offered" or "Dismissed") PlaySummoningStory(Summoning.Last!.Prince);
        }
        ReconcileSummoningFoes();
        Presentation.SetOutcome(SummoningOutcomeText(outcome));
    }

    /// <summary>Player wording for summoning quotes, refusals and the prince's answer.</summary>
    internal static string SummoningOutcomeText(string outcome) => outcome switch {
        "WrongDay" => "Today is not a Daedric summoning day.", "Failed" => "The prince did not answer. The summoning fee has been paid.",
        "Dismissed" => "This prince remembers your earlier summons and dismisses you.", "Offered" => "The prince has answered. Will you accept the quest?",
        "Accepted" => "You accepted the prince's quest.", "InsufficientFunds" => "You do not have enough carried gold.",
        "ProviderUnavailable" => "Your standing does not permit this service.",
        "AnswerPendingOffer" => "Answer the prince's offer first.",
        "HostileArrivalPending" => "The summoned daedra have not finished arriving.",
        "OfferUnavailable" => "That offer is no longer open.",
        "QuestUnavailable" => "The prince's quest is unavailable.",
        _ => DaggerfallServiceOutcomeText.Common(outcome) };
    private void PlaySummoningStory(int prince)
    {
        if (!_composition.VideosEnabled) return;
        var cinematic=Summoning.Cinematic(prince);
        if (Cinematics is null || cinematic.Artifact is null)
        { Presentation.SetOutcome($"The {Summoning.PrinceName(prince)} cinematic is unavailable."); return; }
        Cinematics.Play(cinematic.FileName);
    }
}
