using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSummoningView(string Revision, string Provider, DaggerfallSummoningQuote? Quote,
    string? OfferRevision, string? Prince, string? Message, string[] Diagnostics);
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
            outcome = long.TryParse(action.Revision,out long sequence) ? Summoning.Answer(sequence,action.Confirm,StartSummoningQuest) : "OfferUnavailable";
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
    private DaggerfallQuestInstanceSave PrepareSummoningQuest(DaggerfallSummoningResult result)
    {
        string source=result.Quest+".txt";
        var definition=_definitions.QuestSources.Resolve(source);
        string identity="daedric:"+result.Sequence;
        return State.Quests.PrepareSummoned(result.Quest,new(identity,source,definition.Name,DaggerfallQuestLifecycle.Active,null,[],[])
            { FactionId=result.ProviderFaction,QuestorId=result.Provider });
    }
    private void StartSummoningQuest(DaggerfallSummoningResult result)
    {
        var quest=State.Quests.AdmitPreparedSummoned(result.Quest,result.PreparedQuest!);
        State.Quests.ShowMessage(quest.InstanceId,1002);
    }
    private void PlaySummoningStory(int prince)
    {
        if (!_composition.VideosEnabled) return;
        var cinematic=Summoning.Cinematic(prince);
        if (Cinematics is null || cinematic.Artifact is null)
        { Presentation.SetOutcome($"The {Summoning.PrinceName(prince)} cinematic is unavailable."); return; }
        Cinematics.Play(cinematic.FileName);
    }
    private void ReconcileSummoningFoes()
    {
        if (Summoning?.Last is not { HostilesRemaining: > 0 } result || result.Profile.Require()!=_activeProfileKey
            || State.PlayerControl.Position is not { } player) return;
        var actor=_definitions.Actors.Values.Single(value=>value.Kind==DaggerfallActorKinds.Monster && value.MobileId==result.HostileMobile);
        if (!_sites.Projection.Inputs.MobileSprites.ContainsKey(result.HostileMobile)) return;
        while (Summoning.Last is { HostilesRemaining: > 0 } pending)
        {
            if (!TrySpawnPose($"summoning:{pending.Sequence}:foe:{pending.Spawned.Length}","daggerfall.daedric-summoning",player,
                pending.Refusal ? 8 : 4,64,out ActorPose pose)) return;
            long id=_roster.Spawn(actor.Id.Value,pose); _enemyBehavior.MakeHostile(id); Summoning.Spawned(id);
        }
    }
}
