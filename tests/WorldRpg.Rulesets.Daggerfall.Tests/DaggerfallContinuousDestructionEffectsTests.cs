using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallContinuousDestructionEffectsTests
{
    [Theory]
    [InlineData(0,"health",2)] [InlineData(1,"stamina",128)] [InlineData(2,"magicka",2)]
    public void All_tracks_share_normal_catchup_restore_and_cure_without_double_ticks(int subtype,string trackName,int perRound)
    {
        using Fixture f = new(); var s = f.Session; var track = Track(s,1,trackName); track.Maximum.BaseValue = 1000; track.SetCurrent(1000);
        Start(s,"continuous",subtype,10);
        Assert.Equal(1000-perRound,track.Current);
        Assert.Equal(1,DaggerfallPeriodicCast.Read(Assert.Single(s.State.Effects.Active).State,1,subtype).NextRound);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(track.Current,Track(restored,1,trackName).Current);
        for (int i=0;i<3;i++) s.State.Effects.AdvanceOrdinaryRound();
        restored.State.Effects.AdvanceElapsedRounds(3);
        Assert.Equal(1000-4*perRound,Track(restored,1,trackName).Current);
        Assert.Equal(track.Current,Track(restored,1,trackName).Current);
        Assert.Equal(6u,Assert.Single(restored.State.Effects.Active).Lifecycle.RemainingRounds);
        Assert.Equal(4,DaggerfallPeriodicCast.Read(Assert.Single(restored.State.Effects.Active).State,1,subtype).NextRound);
        Start(restored,"duration-topup",subtype,5,magnitude:99);
        Assert.Equal(1000-4*perRound,Track(restored,1,trackName).Current);
        var incumbent = Assert.Single(restored.State.Effects.Active);
        Assert.Equal(11u,incumbent.Lifecycle.RemainingRounds);
        Assert.Equal(2,DaggerfallPeriodicCast.Read(incumbent.State,1,subtype).Cast.Settings.MagnitudeBaseLow);
        restored.State.Effects.Cure(incumbent.Context.Instance);
        restored.State.Effects.AdvanceElapsedRounds(100);
        Assert.Equal(1000-4*perRound,Track(restored,1,trackName).Current); Assert.Empty(restored.State.Effects.Active);
    }

    [Theory]
    [InlineData("spell.032",0,"health")] [InlineData("spell.048",1,"stamina")]
    [InlineData("spell.050",1,"stamina")] [InlineData("spell.051",2,"magicka")]
    public void Published_spells_apply_real_periodic_tracks_and_save_does_not_repeat_the_initial_tick(string key,int subtype,string name)
    {
        using Fixture f = new(); var s = f.Session; const long target = 2000;
        foreach (string track in new[] {"health","stamina","magicka"}) { var t=Track(s,target,track); t.Maximum.BaseValue=10000;t.SetCurrent(10000); }
        Fund(s); s.State.Character.LearnSpell(key);
        Assert.Equal(DaggerfallCastOutcome.Ready,s.ReadyPlayerSpell(key).Outcome);
        var bundle=s.Casting.Release(1,true).Bundle!; s.Casting.Deliver(bundle,[target]);
        Assert.Equal(DaggerfallCastOutcome.Applied,bundle.Results[0].Outcome);
        double after=Track(s,target,name).Current; Assert.True(after<10000);
        var periodic=s.State.Effects.Active.Single(e=>e.Definition.Spell is {Type:1});
        Assert.Equal(1,DaggerfallPeriodicCast.Read(periodic.State,1,subtype).NextRound);
        using var restored=f.Restore(s.CaptureSave());Assert.Equal(after,Track(restored,target,name).Current);
        restored.AdvanceElapsedTime(60); Assert.True(Track(restored,target,name).Current<after);
    }

    [Theory]
    [InlineData("spell.064",0,"health")] [InlineData("spell.065",0,"health")]
    [InlineData("spell.066",0,"health")] [InlineData("spell.068",1,"stamina")]
    [InlineData("spell.069",0,"health")] [InlineData("spell.070",2,"magicka")]
    [InlineData("spell.072",1,"stamina")]
    public void Hidden_poison_spell_parameters_tick_through_the_compiled_owner_without_becoming_player_spells(string key,int subtype,string name)
    {
        using Fixture f = new(); var s=f.Session; Fund(s);
        s.State.Character.LearnSpell(key);
        Assert.Equal(DaggerfallCastOutcome.UnknownSpell,s.ReadyPlayerSpell(key).Outcome);
        var setting=TestPayload.Definitions.Magic.Spells[key].Effects.Single(e=>e.Type==1);
        const long target=2000;
        var track=Track(s,target,name);track.Maximum.BaseValue=10000;track.SetCurrent(10000);
        uint duration=checked((uint)DaggerfallMagicAdmissionPolicy.CalculateEffectDuration(setting,1));
        s.State.Effects.Start(new("poison",new[]{"continuous-damage-health","continuous-damage-fatigue","continuous-damage-spell-points"}[subtype],
            key,1,target,setting.Key,"Poison",null,1,duration,DaggerfallPeriodicCast.Encode(new(new(setting,1,0,100),0))));
        double after=track.Current; Assert.True(after<10000);
        using var restored=f.Restore(s.CaptureSave());Assert.Equal(after,Track(restored,target,name).Current);
        restored.State.Effects.AdvanceOrdinaryRound();Assert.True(Track(restored,target,name).Current<after);
    }

    [Fact]
    public void Peaceful_nonplayer_fatigue_preserves_the_track_and_pacification_until_it_becomes_hostile()
    {
        using Fixture f=new();var s=f.Session;const long target=2000;
        var memory=s.State.Actors.Get(target).Actor.Get<WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallEnemyPerceptionMemory>();
        memory.Pacified=true;
        var stamina=Track(s,target,"stamina");stamina.Maximum.BaseValue=1000;stamina.SetCurrent(1000);
        double before=stamina.Current;
        Start(s,"sleep",1,10,target:target,caster:1);
        Assert.Equal(before,Track(s,target,"stamina").Current);Assert.True(memory.Pacified);Assert.False(memory.ForcedHostile);
        s.State.Effects.AdvanceOrdinaryRound();Assert.Equal(before,Track(s,target,"stamina").Current);
        Assert.Equal(2,DaggerfallPeriodicCast.Read(Assert.Single(s.State.Effects.Active).State,1,1).NextRound);
        memory.Pacified=false;s.State.Effects.AdvanceOrdinaryRound();Assert.True(Track(s,target,"stamina").Current<before);
        Assert.True(memory.ForcedHostile);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Every_element_is_bound_and_caster_only_shape_is_rejected_before_payment(int subtype)
    {
        using Fixture f = new(); var s=f.Session; Fund(s);
        var original=TestPayload.Definitions.Magic.Spells["spell.032"];
        for (int element=0;element<5;element++)
        {
            var spell=original with {Element=element,RangeType=0,Effects=[Setting(subtype)]};
            var casting=CastingFor(s,spell);
            double before=Track(s,1,"magicka").Current;
            Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect,casting.Ready(1,spell.Key).Outcome);
            Assert.Equal(before,Track(s,1,"magicka").Current);
            casting=CastingFor(s,spell with {RangeType=1});
            Assert.Equal(DaggerfallCastOutcome.Ready,casting.Ready(1,spell.Key).Outcome);
            var bundle=casting.Release(1,true).Bundle!;casting.Deliver(bundle,[2000]);
            Assert.Equal(DaggerfallCastOutcome.Applied,Assert.Single(bundle.Results).Outcome);
            var instance=Assert.Single(s.State.Effects.Active);s.State.Effects.Cancel(instance.Context.Instance);
        }
    }

    [Fact]
    public void Shield_absorbs_periodic_health_damage_and_terminal_target_ends_all_periodic_payloads_once()
    {
        using Fixture f=new();var s=f.Session;Fund(s);Track(s,1,"health").SetCurrent(10);
        s.State.Character.LearnSpell("spell.017");s.ReadyPlayerSpell("spell.017");s.ReleaseReadySpell(1,Vector3.UnitZ);
        var shield=s.State.Effects.Active.Single(e=>e.Definition.Key=="shield");
        int pool=DaggerfallAlterationEffects.ReadShield(shield.State).Remaining;
        Start(s,"health",0,100);Assert.Equal(10,Track(s,1,"health").Current);
        Assert.Equal(pool-2,DaggerfallAlterationEffects.ReadShield(shield.State).Remaining);
        s.State.Effects.Cure(shield.Context.Instance);
        Start(s,"fatigue",1,100);Start(s,"magicka",2,100);
        Track(s,1,"health").SetCurrent(1);
        s.State.Effects.AdvanceOrdinaryRound();s.Update(new WorldRpg.Kit.Controls.ProductUpdateState(.125f));
        Assert.Equal(0,Track(s,1,"health").Current);
        Assert.DoesNotContain(s.State.Effects.Active,e=>e.Definition.Spell is {Type:1});
        double health=Track(s,1,"health").Current; s.State.Effects.AdvanceElapsedRounds(100);Assert.Equal(health,Track(s,1,"health").Current);
    }

    [Fact]
    public void Real_periodic_death_reaches_existing_corpse_and_reward_consumers()
    {
        using Fixture f=new();var s=f.Session;const long target=2000;
        Track(s,target,"health").SetCurrent(3);
        Start(s,"death",0,100,target:target,caster:1);
        Assert.False(s.Corpses.ContainsKey(target));
        s.State.Effects.AdvanceOrdinaryRound();s.Update(new WorldRpg.Kit.Controls.ProductUpdateState(.125f));
        Assert.True(s.Corpses.ContainsKey(target));Assert.True(s.State.Actors.Get(target).IsDefeated);
        Assert.Empty(s.State.Effects.Active);
        long experience=s.State.Progression.Experience;
        s.State.Effects.AdvanceElapsedRounds(100);s.Update(new WorldRpg.Kit.Controls.ProductUpdateState(.125f));
        Assert.Equal(experience,s.State.Progression.Experience);
    }

    [Fact]
    public void Actor_and_item_retirement_remove_only_their_sources()
    {
        using Fixture f=new();var s=f.Session; long target=s.SpawnActor("rat",new(new(11,0,11),0));
        var item=s.State.Inventory.Read().UniqueItems.First();ulong itemId=s.State.Inventory.GetDurableItemId(item.Entity).Value;
        Start(s,"item",0,100,item:itemId);Start(s,"other",0,100,caster:target);
        Assert.Equal(2,s.State.Effects.Active.Count);
        s.State.ItemInstances.ReplaceUnique(itemId,s.State.ItemInstances.RequireUnique(itemId) with {CurrentCondition=0});
        Assert.Equal("other",Assert.Single(s.State.Effects.Active).Context.Instance.Value);
        s.RetireActor(target);Assert.Empty(s.State.Effects.Active);
    }

    [Theory]
    [InlineData(0,"health",1)] [InlineData(1,"stamina",64)] [InlineData(2,"magicka",1)]
    public void Saved_partial_resistance_applies_each_round_then_duration_expiry_stops_the_payload(int subtype,string name,int perRound)
    {
        using Fixture f=new();var s=f.Session;var track=Track(s,1,name);track.Maximum.BaseValue=1000;track.SetCurrent(1000);
        var setting=Setting(subtype);
        s.State.Effects.Start(new("partial",new[]{"continuous-damage-health","continuous-damage-fatigue","continuous-damage-spell-points"}[subtype],
            "spell.periodic",2000,1,setting.Key,"Magic",null,1,2,DaggerfallPeriodicCast.Encode(new(new(setting,1,0,50),0))));
        Assert.Equal(1000-perRound,track.Current);
        using var restored=f.Restore(s.CaptureSave());restored.State.Effects.AdvanceElapsedRounds(20);
        Assert.Equal(1000-2*perRound,Track(restored,1,name).Current);Assert.Empty(restored.State.Effects.Active);
        restored.State.Effects.AdvanceOrdinaryRound();Assert.Equal(1000-2*perRound,Track(restored,1,name).Current);
    }

    [Theory]
    [InlineData(0,"health",2)] [InlineData(1,"stamina",128)] [InlineData(2,"magicka",2)]
    public void A_player_payload_survives_unloading_its_casters_site_and_resumes_its_saved_round(int subtype,string name,int perRound)
    {
        var source=ReadInputs(TestData.RepositoryRoot);
        var destination=DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot),
            File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,"content/worldrpg/payloads/daggerfall.castle-necromoghan.json")),TestPayload.Definitions);
        var composition=new DaggerfallSessionComposition(TestPayload.Definitions,source,DaggerfallTuning.Defaults)
            {Profiles=new([source,destination])};
        List<string> releases=[];ContentFake content=new(releases);PopulateContent(content,source);PopulateContent(content,destination);
        var spatial=SpatialFake.Create(source.SpatialArtifact.Sha256,releases);
        var engine=EngineContextFake.Create(content,spatial.Service,new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());
        using var s=DaggerfallSession.StartNew(engine.Context,composition);
        var track=Track(s,1,name);track.Maximum.BaseValue=1000;track.SetCurrent(1000);
        Start(s,"unloaded-caster",subtype,100);
        Assert.Equal(1000-perRound,track.Current);
        Assert.True(s.TryTransitionTo(destination.ProfileKey));Assert.DoesNotContain(2000L,s.DefinitionsByActor.Keys);
        s.AdvanceElapsedTime(60);Assert.Equal(1000-2*perRound,track.Current);
        using var restored=DaggerfallSession.Restore(engine.Context,composition,s.CaptureSave());
        Assert.Equal(1000-2*perRound,Track(restored,1,name).Current);
        restored.AdvanceElapsedTime(60);Assert.Equal(1000-3*perRound,Track(restored,1,name).Current);
        Assert.Equal(3,DaggerfallPeriodicCast.Read(Assert.Single(restored.State.Effects.Active).State,1,subtype).NextRound);
    }

    [Fact]
    public void A_malformed_periodic_save_is_refused()
    {
        using Fixture f=new();var s=f.Session;Start(s,"invalid",0,100);
        var saved=DaggerfallSavePayload.Read(s.CaptureSave());var effect=Assert.Single(saved.ActiveEffects);
        var state=DaggerfallPeriodicCast.Read(effect.State,1,0);
        var bad=saved with {ActiveEffects=[effect with {State=DaggerfallPeriodicCast.Encode(state with {NextRound=-1})}]};
        Assert.Throws<ArgumentException>(()=>f.Restore(DaggerfallSavePayload.Encode(bad)));
    }

    private static DaggerfallSpellEffectDefinition Setting(int subtype,int magnitude=2)=>new("periodic",1,subtype,10,0,1,100,0,1,magnitude,magnitude,0,0,1);
    private static void Start(DaggerfallSession s,string instance,int subtype,uint rounds,int magnitude=2,long target=1,long caster=2000,ulong? item=null)
    {
        var setting=Setting(subtype,magnitude);
        var state=DaggerfallPeriodicCast.Encode(new(new(setting,1,0,100),0));
        s.State.Effects.Start(new(instance,new[]{"continuous-damage-health","continuous-damage-fatigue","continuous-damage-spell-points"}[subtype],
            "spell.periodic",caster,target,setting.Key,"Magic",item,1,rounds,state));
    }
    private static Track Track(DaggerfallSession s,long id,string name)=>(id==1?s.State.Actors.Player.Stats:s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse(name));
    private static void Fund(DaggerfallSession s){var t=Track(s,1,"magicka");t.Maximum.BaseValue=10000;t.SetCurrent(10000);}
    private static DaggerfallCasting CastingFor(DaggerfallSession s,DaggerfallSpellDefinition spell)
    {
        var catalog=TestPayload.Definitions.Magic with {Spells=new Dictionary<string,DaggerfallSpellDefinition>{[spell.Key]=spell}};
        return new(catalog,s.State.Effects,id=>id==1?s.State.Actors.Player.Actor:s.State.Actors.TryGet(id,out var actor)?actor.Actor:null,
            s.MagicProfile,_=>true,_=>{},_=>{},DaggerfallCastingTests.SaveDice.Create(100),1,playerKnowsSpell:_=>true,casterLevel:_=>1);
    }
    private sealed class Fixture:IDisposable
    {
        private readonly DaggerfallSessionComposition _composition=new(TestPayload.Definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);
        internal DaggerfallSession Session{get;}
        internal Fixture()=>Session=DaggerfallSession.StartNew(Engine().Context,_composition);
        private EngineContextFake Engine()
        {
            List<string> releases=[];ContentFake content=new(releases);PopulateContent(content,_composition.StartSite);
            var spatial=SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256,releases);
            return EngineContextFake.Create(content,spatial.Service,new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload)=>DaggerfallSession.Restore(Engine().Context,_composition,payload);
        public void Dispose()=>Session.Dispose();
    }
}
