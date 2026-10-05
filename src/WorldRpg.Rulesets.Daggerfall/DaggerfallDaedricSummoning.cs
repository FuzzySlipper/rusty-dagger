using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallSummoningWeather { Any, Rain, Thunder }
internal sealed record DaggerfallDaedricPrince(int Faction, int Day, DaggerfallSummoningWeather Weather);
internal static class DaggerfallDaedricSummoningPolicy
{
    // DaggerfallQuestPopupWindow's complete source table. Quest/media identity has one home in the cinematic catalog.
    internal static readonly DaggerfallDaedricPrince[] Princes = [
        new(4,155,DaggerfallSummoningWeather.Any),new(1,1,DaggerfallSummoningWeather.Any),new(2,320,DaggerfallSummoningWeather.Any),
        new(3,350,DaggerfallSummoningWeather.Any),new(5,46,DaggerfallSummoningWeather.Rain),new(6,99,DaggerfallSummoningWeather.Rain),
        new(7,278,DaggerfallSummoningWeather.Any),new(8,65,DaggerfallSummoningWeather.Any),new(9,32,DaggerfallSummoningWeather.Thunder),
        new(10,302,DaggerfallSummoningWeather.Rain),new(11,129,DaggerfallSummoningWeather.Any),new(12,13,DaggerfallSummoningWeather.Any),
        new(13,190,DaggerfallSummoningWeather.Any),new(14,248,DaggerfallSummoningWeather.Rain),new(15,283,DaggerfallSummoningWeather.Any),
        new(16,81,DaggerfallSummoningWeather.Any)];
    internal static int CalculateDaedraSummoningCost(int npcReputation) => checked(200000 - npcReputation * 1000);
    internal static int CalculateDaedraSummoningChance(int princeReputation, int bonus) => checked(30 + princeReputation + bonus);
    internal static int WeatherBonus(DaggerfallDaedricPrince prince, bool raining, bool storming) =>
        prince.Weather == DaggerfallSummoningWeather.Any || prince.Weather == DaggerfallSummoningWeather.Rain && raining
            || prince.Weather == DaggerfallSummoningWeather.Thunder && storming ? 30 : 0;
}
internal sealed record DaggerfallSummoningResult(long Sequence, int Prince, int ProviderFaction, long Provider,
    string Quest, string Outcome, ulong Paid, int Chance, int Roll, DaggerfallWorldProfileKeySave Profile,
    int HostileMobile, int HostilesRemaining, long[] Spawned, bool Refusal)
{
    public DaggerfallQuestInstanceSave? PreparedQuest { get; init; }
    public string? Diagnostic { get; init; }
}
internal sealed record DaggerfallSummoningSave(long Sequence, int WitchDay, int WitchPrince, int[] Summoned, DaggerfallSummoningResult? Last)
{
    internal static DaggerfallSummoningSave Empty => new(0,0,0,[],null);
    internal void Validate()
    {
        if (Sequence < 0 || WitchDay is < 0 or > 360 || WitchPrince is < 0 or > 16 || (WitchDay == 0) != (WitchPrince == 0)
            || Summoned is null || Summoned.Distinct().Count() != Summoned.Length || Summoned.Any(value => value is < 1 or > 16))
            throw new ArgumentException("Summoning selection or eligibility is malformed.");
        if (Last is { } last)
        {
            if (last.Sequence <= 0 || last.Sequence != Sequence || last.Prince is < 1 or > 16 || last.Provider <= 0 || last.ProviderFaction <= 0
                || string.IsNullOrWhiteSpace(last.Quest) || last.Outcome is not ("Offered" or "Accepted" or "Refused" or "Failed" or "Dismissed" or "QuestUnavailable")
                || last.Paid is < 100000 or > 300000 || last.Roll is < 1 or > 100 || last.Chance is < -70 or > 160
                || last.HostilesRemaining is < 0 or > 5 || last.Spawned is null || last.Spawned.Any(id => id <= 0)
                || last.Spawned.Distinct().Count() != last.Spawned.Length || last.HostilesRemaining + last.Spawned.Length > 5
                || last.HostileMobile is not (0 or 25 or 26 or 27 or 29 or 31)
                || (last.HostilesRemaining > 0 || last.Spawned.Length > 0) && last.HostileMobile == 0
                || last.Outcome is not ("Failed" or "Refused") && (last.HostileMobile != 0 || last.HostilesRemaining != 0 || last.Spawned.Length != 0)
                || last.Outcome != "Failed" && !Summoned.Contains(last.Prince))
                throw new ArgumentException("Summoning result is malformed.");
            if (last.Refusal != (last.Outcome == "Refused"))
                throw new ArgumentException("Summoning refusal state disagrees with its outcome.");
            if ((last.Outcome == "Offered") != (last.PreparedQuest is not null))
                throw new ArgumentException("Only an outstanding summoning offer holds a prepared quest.");
            last.PreparedQuest?.ValidateShape();
            if (last.PreparedQuest is { } prepared && (prepared.SourceFile != last.Quest + ".txt"
                || prepared.InstanceId != "daedric:" + last.Sequence || prepared.FactionId != last.ProviderFaction || prepared.QuestorId != last.Provider))
                throw new ArgumentException("Summoning offer and prepared quest disagree.");
            _ = last.Profile.Require();
        }
    }
}
internal sealed record DaggerfallSummoningQuote(string Key, int Prince, string Name, string Quest, ulong Gold, bool Eligible, string? Reason);

/// <summary>Paid summoning policy over canonical NPC, guild, currency, calendar, weather and quest admission.</summary>
internal sealed class DaggerfallDaedricSummoning(DaggerfallDefinitions definitions, DaggerfallState state, IRandomService random,
    DaggerfallDisabledQuestSelection selection, Func<DaggerfallCalendar> calendar, Func<(bool Rain, bool Storm)> weather,
    Func<DaggerfallWorldProfileKey> profile, Func<DaggerfallSummoningResult, DaggerfallQuestInstanceSave> prepareQuest, DaggerfallSummoningSave? restored = null)
{
    private DaggerfallSummoningSave _state = Admit(restored ?? DaggerfallSummoningSave.Empty);
    internal DaggerfallSummoningResult? Last => _state.Last;
    internal bool PendingOffer => Last?.Outcome == "Offered";
    internal DaggerfallSummoningSave Capture() => _state;
    private static DaggerfallSummoningSave Admit(DaggerfallSummoningSave value) { value.Validate(); return value; }
    internal DaggerfallCinematicDefinition Cinematic(int prince) => definitions.Cinematics.Cinematics.Values.Single(value => value.FactionId == prince);
    internal string PrinceName(int prince) => definitions.Factions.Factions[prince].Name;
    internal bool QuestAvailable(string quest) => selection.TryResolveSummon(quest, out var resolution) && resolution!.Runnable;

    private (DaggerfallFactionDefinition Faction, DaggerfallServiceEligibility Eligibility)? Provider(DaggerfallServiceProvider provider)
    {
        if (provider.Service != "daedra-summoning" || state.Services.ProviderAvailable(provider) != DaggerfallServiceDenial.None) return null;
        var npc = state.Npcs.Require(provider.NpcId);
        if (!definitions.Factions.Factions.TryGetValue(npc.Appearance.FactionId, out var faction)) return null;
        if (faction.Type == 8) return (faction, new(false));
        var guild = DaggerfallConcreteGuildCatalog.All.FirstOrDefault(guild => guild.TryGetService(DaggerfallConcreteGuildService.DaedraSummoning, out var service)
            && service.ProviderFactionId == faction.Id);
        if (guild is null || !state.ConcreteGuildServices.Evaluate(guild.FactionId,DaggerfallConcreteGuildService.DaedraSummoning,
                checked((int)calendar().DayNumber),new(provider)).CanUse) return null;
        guild.TryGetService(DaggerfallConcreteGuildService.DaedraSummoning, out var policy);
        return (faction, new(policy.RequiresMembership, policy.MinimumRank ?? 0, guild.FactionId));
    }

    internal DaggerfallSummoningQuote Quote(DaggerfallServiceProvider provider)
    {
        var current = calendar();
        if (Provider(provider) is not { } source) return new("",0,"","",0,false,"ProviderUnavailable");
        int prince;
        if (source.Faction.Type == 8)
        {
            // Classic's Glenmoril coven selects a daily non-Hircine prince; other covens summon Hircine.
            // DFU explicitly reverses those two branches; DEC-01 retains the documented classic distinction.
            if (source.Faction.Id == 419)
            {
                if (_state.WitchDay != current.DayOfYear || _state.WitchPrince == 0)
                    _state = _state with { WitchDay = current.DayOfYear, WitchPrince = DaggerfallDaedricSummoningPolicy.Princes[Draw($"witch:{current.DayOfYear}",1,15)].Faction };
                prince = _state.WitchPrince;
            }
            else prince = 4;
        }
        else prince = DaggerfallDaedricSummoningPolicy.Princes.FirstOrDefault(value => value.Day == current.DayOfYear)?.Faction ?? 0;
        if (prince == 0) return new("",0,"","",0,false,"WrongDay");
        string quest = Cinematic(prince).Quest;
        string? reason = PendingOffer ? "AnswerPendingOffer" : Last?.HostilesRemaining > 0 ? "HostileArrivalPending" : !QuestAvailable(quest) ? "QuestUnavailable" : null;
        return new($"{_state.Sequence}:{current.DayNumber}:{provider.NpcId}:{prince}",prince,PrinceName(prince),quest,
            checked((ulong)DaggerfallDaedricSummoningPolicy.CalculateDaedraSummoningCost(state.Social.FactionReputation(source.Faction.Id))),reason is null,reason);
    }

    internal string Summon(DaggerfallServiceProvider provider, string key, ulong price, bool confirm)
    {
        if (!confirm) return "ConfirmationRequired";
        var quote = Quote(provider);
        if (!quote.Eligible) return quote.Reason!;
        if (key != quote.Key || price != quote.Gold) return "QuoteChanged";
        var source = Provider(provider)!.Value;
        long sequence = checked(_state.Sequence + 1);
        var payment = state.Services.Quote(new($"daedric-summoning:{sequence}",provider),source.Eligibility,new(quote.Gold));
        if (payment.Quote is null) return payment.Outcome.Denial.ToString();
        var paid = state.Services.Commit(payment.Quote);
        if (!paid.Accepted) return paid.Denial.ToString();
        var sky = weather();
        int prince = Draw($"{sequence}:replacement",1,100) <= (sky.Storm ? 15 : 5) ? 9 : quote.Prince;
        var policy = DaggerfallDaedricSummoningPolicy.Princes.Single(value => value.Faction == prince);
        int chance = DaggerfallDaedricSummoningPolicy.CalculateDaedraSummoningChance(state.Social.FactionReputation(prince),
            DaggerfallDaedricSummoningPolicy.WeatherBonus(policy,sky.Rain,sky.Storm));
        int roll = Draw($"{sequence}:success",1,100);
        string outcome = roll > chance ? "Failed" : _state.Summoned.Contains(prince) ? "Dismissed" : QuestAvailable(Cinematic(prince).Quest) ? "Offered" : "QuestUnavailable";
        var result = new DaggerfallSummoningResult(sequence,prince,source.Faction.Id,provider.NpcId,Cinematic(prince).Quest,outcome,paid.PaidGold,chance,roll,
            DaggerfallWorldProfileKeySave.Capture(profile()),0,0,[],false);
        _state = _state with { Sequence=sequence, Last=result,
            Summoned = outcome == "Failed" || _state.Summoned.Contains(prince) ? _state.Summoned : [.. _state.Summoned,prince] };
        if (outcome == "Offered")
        {
            try { _state = _state with { Last=result with { PreparedQuest=prepareQuest(result) } }; }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                outcome="QuestUnavailable";
                _state = _state with { Last=result with { Outcome=outcome, Diagnostic=error.Message } };
            }
        }
        if (outcome == "Failed" && source.Faction.GuildGroup == 22) QueueHostiles(false);
        return outcome;
    }

    internal string Answer(long sequence, bool accept, Action<DaggerfallSummoningResult> startQuest)
    {
        if (Last is not { Outcome: "Offered" } offered || offered.Sequence != sequence) return "OfferUnavailable";
        if (accept)
        {
            if (!QuestAvailable(offered.Quest)) return "QuestUnavailable";
            startQuest(offered);
            _state = _state with { Last=offered with { Outcome="Accepted",PreparedQuest=null } };
            return "Accepted";
        }
        _state = _state with { Last=offered with { Outcome="Refused",Refusal=true,PreparedQuest=null } };
        QueueHostiles(true);
        return "Refused";
    }

    private void QueueHostiles(bool refusal)
    {
        int[] foes = [31,29,27,26,25]; var last=Last!;
        _state = _state with { Last=last with { HostileMobile=foes[Draw($"{last.Sequence}:foe",0,4)],
            HostilesRemaining=Draw($"{last.Sequence}:count",refusal ? 3 : 1,refusal ? 5 : 3) } };
    }
    internal void Spawned(long actor)
    {
        if (Last is not { HostilesRemaining: > 0 } last) throw new InvalidOperationException("No summoning foe awaits admission.");
        _state = _state with { Last=last with { HostilesRemaining=last.HostilesRemaining-1, Spawned=[.. last.Spawned,actor] } };
    }
    private int Draw(string key,int minimum,int maximum) => checked((int)random.DrawKeyed(new(0,"daggerfall.daedric-summoning",key,minimum,maximum)).Value);
}
