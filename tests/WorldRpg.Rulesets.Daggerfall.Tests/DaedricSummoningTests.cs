using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using WorldRpg.Rulesets.Daggerfall.World;
using Rusty.Engine.Mechanics;
using System.Reflection;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaedricSummoningTests
{
    internal static DaggerfallDisabledQuestSelection Selection() => DaggerfallDisabledQuestSelection.From(
        DaggerfallClassicQuestCorpusContent.Read(new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.quests.disabled.json")), TestPayload.Definitions));

    [Fact]
    public void All_sixteen_published_summoning_quests_are_runnable_and_missing_script_is_unavailable()
    {
        var selection = Selection();
        Assert.False(selection.TryResolveSummon("80C00Y00", out _));
        List<string> issues = [];
        foreach (var cinematic in TestPayload.Definitions.Cinematics.Cinematics.Values.Where(value => value.FactionId is not null))
        {
            Assert.True(selection.TryResolveSummon(cinematic.Quest, out var quest), cinematic.Quest);
            issues.AddRange(quest!.Diagnostics.Select(value => $"{quest.Name}:{value.Line}: {value.Text}: {value.Reason}"));
        }
        Assert.True(issues.Count == 0, string.Join("\n", issues));
    }

    [Fact]
    public void Calendar_providers_cost_weather_and_daily_witch_selection_use_source_policy()
    {
        using var f = new SanguineRoseSessionTests.Fixture(prepareComposition:Composition);
        var provider = Provider(f.Session, 66);
        var day = DaggerfallCalendar.Start;
        var random = SummonRandom.Create();
        var summoning = new DaggerfallDaedricSummoning(TestPayload.Definitions, f.Session.State, random, Selection(),
            () => day, () => (false, false), () => f.Inputs.ProfileKey,result=>Prepare(f,result));
        Assert.Equal("ProviderUnavailable", summoning.Quote(provider).Reason);
        f.Session.State.Social.JoinGuild(40, 0);
        for (int i=0;i<6;i++) f.Session.State.Social.PromoteGuild(40, 0);
        foreach (var prince in DaggerfallDaedricSummoningPolicy.Princes)
        {
            day = new(405,(prince.Day-1)/30,(prince.Day-1)%30,12,0,0);
            var quote=summoning.Quote(provider);
            Assert.True(quote.Eligible, quote.Reason); Assert.Equal(prince.Faction,quote.Prince);
            Assert.Equal(200000UL,quote.Gold);
            int dry = prince.Weather == DaggerfallSummoningWeather.Any ? 30 : 0;
            Assert.Equal(dry,DaggerfallDaedricSummoningPolicy.WeatherBonus(prince,false,false));
            Assert.Equal(30,DaggerfallDaedricSummoningPolicy.WeatherBonus(prince,true,true));
        }
        day=new(405,0,1,12,0,0);Assert.Equal("WrongDay",summoning.Quote(provider).Reason);
        var glenmoril=Provider(f.Session,419);
        var first=summoning.Quote(glenmoril);Assert.True(first.Eligible,first.Reason);Assert.NotEqual(4,first.Prince);
        var restored=new DaggerfallDaedricSummoning(TestPayload.Definitions,f.Session.State,random,Selection(),()=>day,()=> (false,false),()=>f.Inputs.ProfileKey,result=>Prepare(f,result),summoning.Capture());
        Assert.Equal(first,restored.Quote(glenmoril));
        var other=TestPayload.Definitions.Factions.Factions.Values.First(x=>x.Type==8&&x.Id!=419);
        Assert.Equal(4,summoning.Quote(Provider(f.Session,other.Id)).Prince);
        Assert.Equal(100000,DaggerfallDaedricSummoningPolicy.CalculateDaedraSummoningCost(100));
        Assert.Equal(300000,DaggerfallDaedricSummoningPolicy.CalculateDaedraSummoningCost(-100));
        Assert.Equal(-70,DaggerfallDaedricSummoningPolicy.CalculateDaedraSummoningChance(-100,0));
    }

    [Fact]
    public void Payment_failure_success_pending_restore_and_repeat_are_single_results()
    {
        var random=SummonRandom.Create();
        using var f=new SanguineRoseSessionTests.Fixture(prepareComposition:Composition,random:random);var provider=Provider(f.Session,419); var control=(SummonRandom)(object)random;
        var summoning=new DaggerfallDaedricSummoning(TestPayload.Definitions,f.Session.State,random,Selection(),()=>DaggerfallCalendar.Start,()=> (false,false),()=>f.Inputs.ProfileKey,result=>Prepare(f,result));
        var quote=summoning.Quote(provider);
        f.Session.State.Currency.TrySpendGold(f.Session.State.Currency.Read().Gold,[]);
        Assert.Equal("ConfirmationRequired",summoning.Summon(provider,quote.Key,quote.Gold,false));
        Assert.Equal("QuoteChanged",summoning.Summon(provider,quote.Key,quote.Gold+1,true));
        Assert.Equal("InsufficientFunds",summoning.Summon(provider,quote.Key,quote.Gold,true));Assert.Null(summoning.Last);
        Fund(f);ulong before=f.Session.State.Currency.Read().Gold;
        control.Success=100;Assert.Equal("Failed",summoning.Summon(provider,quote.Key,quote.Gold,true));
        Assert.Equal(before-quote.Gold,f.Session.State.Currency.Read().Gold);
        Assert.Equal("HostileArrivalPending",summoning.Summon(provider,quote.Key,quote.Gold,true));
        foreach (int i in Enumerable.Range(0,summoning.Last!.HostilesRemaining))summoning.Spawned(1000+i);
        quote=summoning.Quote(provider);control.Success=1;
        Assert.True(summoning.Summon(provider,quote.Key,quote.Gold,true)=="Offered",summoning.Last?.Diagnostic);
        Assert.Equal("AnswerPendingOffer",summoning.Summon(provider,quote.Key,quote.Gold,true));
        var restored=new DaggerfallDaedricSummoning(TestPayload.Definitions,f.Session.State,random,Selection(),()=>DaggerfallCalendar.Start,()=> (false,false),()=>f.Inputs.ProfileKey,result=>Prepare(f,result),summoning.Capture());
        int starts=0;
        Assert.Equal("Accepted",restored.Answer(restored.Last!.Sequence,true,_=>starts++));
        Assert.Equal("OfferUnavailable",restored.Answer(restored.Last.Sequence,true,_=>starts++));Assert.Equal(1,starts);
        quote=restored.Quote(provider);Assert.Equal("Dismissed",restored.Summon(provider,quote.Key,quote.Gold,true));
        Assert.Equal(before-3*quote.Gold,f.Session.State.Currency.Read().Gold);
        restored.Capture().Validate();
    }

    [Fact]
    public void Ordinary_offer_accepts_real_quest_and_refusal_creates_saved_hostile_actors()
    {
        var random=SummonRandom.Create();
        using var f=new SanguineRoseSessionTests.Fixture(prepareComposition:Composition, random:random);
        var provider=Provider(f.Session,419);Fund(f);
        var quote=f.Session.Summoning.Quote(provider);Assert.True(quote.Eligible,quote.Reason);
        Assert.True(f.Session.Summoning.Summon(provider,quote.Key,quote.Gold,true)=="Offered",f.Session.Summoning.Last?.Diagnostic);
        var offer=f.Session.ReadSummoning()!;Assert.NotNull(offer.OfferRevision);Assert.Empty(offer.Diagnostics);
        using(var pending=f.Restore())
        {
            Assert.Empty(pending.State.Quests.Capture().Instances);
            Assert.Equal(offer.Message,pending.ReadSummoning()!.Message);
            Assert.Equal(offer.OfferRevision,pending.ReadSummoning()!.OfferRevision);
        }
        f.Submit(new { action="daedra-answer", revision=offer.OfferRevision, confirm=true });
        Assert.Equal("Accepted",f.Session.Summoning.Last!.Outcome);
        var accepted=f.Session.Summoning.Capture();
        Assert.Throws<ArgumentException>(()=>(accepted with { Last=accepted.Last! with { HostileMobile=25,HostilesRemaining=1 } }).Validate());
        Assert.Throws<ArgumentException>(()=>(accepted with { Last=accepted.Last! with { HostileMobile=25,Spawned=[999] } }).Validate());
        var quest=Assert.Single(f.Session.State.Quests.Capture().Instances);
        Assert.Equal(quote.Quest+".txt",quest.SourceFile);Assert.Equal(provider.NpcId,quest.QuestorId);
        using var restored=f.Restore();Assert.Equal("Accepted",restored.Summoning.Last!.Outcome);
        Assert.Equal(quest.InstanceId,Assert.Single(restored.State.Quests.Capture().Instances).InstanceId);

        // Another coven's Hircine offer exercises the ordinary refusal action and actual roster admission.
        var other=TestPayload.Definitions.Factions.Factions.Values.First(x=>x.Type==8&&x.Id!=419);
        var coven=Provider(f.Session,other.Id);quote=f.Session.Summoning.Quote(coven);
        Assert.Equal("Offered",f.Session.Summoning.Summon(coven,quote.Key,quote.Gold,true));
        offer=f.Session.ReadSummoning()!;
        f.Submit(new { action="daedra-answer",revision=offer.OfferRevision,confirm=false });
        var refusal=f.Session.Summoning.Last!;Assert.Equal("Refused",refusal.Outcome);Assert.Equal(0,refusal.HostilesRemaining);
        Assert.InRange(refusal.Spawned.Length,3,5);
        Assert.All(refusal.Spawned,id=>Assert.Equal(refusal.HostileMobile,f.Session.DefinitionsByActor[id].MobileId));
        using var after=f.Restore();Assert.Equal(refusal.Spawned,after.Summoning.Last!.Spawned);
        Assert.All(refusal.Spawned,id=>Assert.True(after.State.Actors.TryGet(id,out _)));
    }

    [Theory]
    [InlineData(false,1,60)]
    [InlineData(true,9,60)]
    public void Storm_changes_replacement_threshold_and_prince_weather_bonus(bool storm,int prince,int chance)
    {
        using var f=new SanguineRoseSessionTests.Fixture();var provider=Provider(f.Session,419);Fund(f);
        var random=SummonRandom.Create();var draws=(SummonRandom)(object)random;draws.Replacement=10;draws.Success=100;
        f.Session.State.Social.ChangeFactionReputation(9,-f.Session.State.Social.FactionReputation(9));
        var summoning=new DaggerfallDaedricSummoning(TestPayload.Definitions,f.Session.State,random,Selection(),()=>DaggerfallCalendar.Start,
            ()=> (storm,storm),()=>f.Inputs.ProfileKey,_=>throw new InvalidOperationException("A failed roll must not prepare a quest."));
        var quote=summoning.Quote(provider);Assert.Equal("Failed",summoning.Summon(provider,quote.Key,quote.Gold,true));
        Assert.Equal(prince,summoning.Last!.Prince);Assert.Equal(chance,summoning.Last.Chance);
        Assert.Empty(summoning.Capture().Summoned);
    }

    private static DaggerfallSessionComposition Composition(DaggerfallSessionComposition composition)=>composition with
    { DisabledQuestSelection=Selection(), VideosEnabled=false,
      Blocks=DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,"content/worldrpg/payloads/daggerfall.blocks.json"))) };

    private static DaggerfallQuestInstanceSave Prepare(SanguineRoseSessionTests.Fixture f,DaggerfallSummoningResult result)
    {
        var source=TestPayload.Definitions.QuestSources.Resolve(result.Quest+".txt");
        return f.Session.State.Quests.PrepareSummoned(result.Quest,new("daedric:"+result.Sequence,source.SourceFile,source.Name,DaggerfallQuestLifecycle.Active,null,[],[])
            { FactionId=result.ProviderFaction,QuestorId=result.Provider });
    }

    private static DaggerfallServiceProvider Provider(DaggerfallSession session,int faction)
    {
        var site=session.Site.ActiveSite!;var location=new DaggerfallNpcSite(site.Id.Region,site.Name,string.Empty);
        long npc=session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static,"summoner:"+faction,location,new("Breton","Female",0,0,0,faction),"Summoner",["talk","daedra-summoning"]);
        return new(npc,location,"daedra-summoning");
    }
    private static void Fund(SanguineRoseSessionTests.Fixture f)
    {
        var factory=new DaggerfallItemFactory(TestPayload.Definitions,f.Engine.Context.Random);
        factory.Materialize(factory.Create(new("Currency","summoning.gold",DaggerfallItemOwner.Player,Quantity:1_000_000,TemplateIndex:276)),f.Session.State.Inventory,f.Session.State.ItemInstances,InventoryStackId.Parse("summoning.gold"));
    }
}
internal class SummonRandom : DispatchProxy
{
    internal int Replacement=100,Success=1;
    internal static IRandomService Create()=>Create<IRandomService,SummonRandom>();
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method?.Name==nameof(IRandomService.DrawKeyed))
        {
            var request=(KeyedRngRequest)args![0]!;
            string text=request.ToString();
            uint hash=2166136261;
            foreach(char ch in text)hash=unchecked((hash^ch)*16777619);
            long selected=request.Minimum+(long)(hash%(ulong)(request.Maximum-request.Minimum+1));
            return new KeyedRngReceipt(text.Contains(":replacement")?Replacement:text.Contains(":success")?Success:text.Contains("witch:")?request.Minimum:selected);
        }
        return method!.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
    }
}
