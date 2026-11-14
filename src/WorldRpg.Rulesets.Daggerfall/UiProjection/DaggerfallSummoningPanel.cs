using System.Globalization;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSummoningView(string Revision, string Provider, DaggerfallSummoningQuote? Quote,
    string? OfferRevision, string? Prince, string? Message, string[] Diagnostics);

/// <summary>The summoner's pane of the spellbook frame, the prince's answer and its cinematic, worded for the player.</summary>
/// <param name="videosEnabled">Whether this product plays cinematics.</param>
/// <param name="reconcileFoes">Session: how a summoning's hostiles arrive.</param>
internal sealed class DaggerfallSummoningPanel(DaggerfallDaedricSummoning summoning, DaggerfallQuestInstances quests,
    DaggerfallCinematicPresentation? cinematics, bool videosEnabled, DaggerfallServiceCounter counter, Action reconcileFoes,
    PresentationState presentation)
{
    internal DaggerfallSummoningView? Read()
    {
        if (cinematics?.ActiveSource is not null) return null;
        if (summoning.Last is { Outcome: "Offered" } offer)
        {
            var rendered = quests.RenderPreparedOffer(offer.PreparedQuest!, 1000);
            return new("", "", null, offer.Sequence.ToString(CultureInfo.InvariantCulture),
                summoning.PrinceName(offer.Prince), rendered.Text, [.. rendered.Diagnostics]);
        }
        if (counter.Summoner() is not { } provider) return null;
        return new(counter.Page.Revision, counter.Page.TargetLabel, summoning.Quote(provider), null, null, null, []);
    }

    internal void Change(DaggerfallPlayerUiAction action)
    {
        string outcome;
        if (action.Kind == DaggerfallUiActionKind.DaedraAnswer)
        {
            var prior = summoning.Last;
            outcome = long.TryParse(action.Revision, out long sequence) ? summoning.Answer(sequence, action.Confirm, offer => DaggerfallDaedricSummoning.StartQuest(quests, offer)) : "OfferUnavailable";
            if (outcome == "Refused" && prior is not null)
            {
                var text = quests.RenderPreparedOffer(prior.PreparedQuest!, 1001);
                presentation.SetOutcome(text.Text); reconcileFoes(); return;
            }
        }
        else if (counter.Summoner(action.Revision) is not { } provider) outcome = "ProviderUnavailable";
        else
        {
            outcome = summoning.Summon(provider, action.Key!, action.Amount!.Value, action.Confirm);
            if (outcome is "Offered" or "Dismissed") PlayStory(summoning.Last!.Prince);
        }
        reconcileFoes();
        presentation.SetOutcome(OutcomeText(outcome));
    }

    /// <summary>Player wording for summoning quotes, refusals and the prince's answer.</summary>
    internal static string OutcomeText(string outcome) => outcome switch {
        "WrongDay" => "Today is not a Daedric summoning day.", "Failed" => "The prince did not answer. The summoning fee has been paid.",
        "Dismissed" => "This prince remembers your earlier summons and dismisses you.", "Offered" => "The prince has answered. Will you accept the quest?",
        "Accepted" => "You accepted the prince's quest.", "InsufficientFunds" => "You do not have enough carried gold.",
        "ProviderUnavailable" => "Your standing does not permit this service.",
        "AnswerPendingOffer" => "Answer the prince's offer first.",
        "HostileArrivalPending" => "The summoned daedra have not finished arriving.",
        "OfferUnavailable" => "That offer is no longer open.",
        "QuestUnavailable" => "The prince's quest is unavailable.",
        _ => DaggerfallServiceOutcomeText.Common(outcome) };

    private void PlayStory(int prince)
    {
        if (!videosEnabled) return;
        var cinematic = summoning.Cinematic(prince);
        if (cinematics is null || cinematic.Artifact is null)
        { presentation.SetOutcome($"The {summoning.PrinceName(prince)} cinematic is unavailable."); return; }
        cinematics.Play(cinematic.FileName);
    }
}
