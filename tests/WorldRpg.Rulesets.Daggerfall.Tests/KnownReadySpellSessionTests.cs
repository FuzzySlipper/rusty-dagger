using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class KnownReadySpellSessionTests
{
    [Fact]
    public void Learn_select_unready_and_actual_cast_use_semantic_projection_and_shared_casting()
    {
        using Fixture f=new();var s=f.Session;Fund(s);
        Assert.Empty(s.ReadSpells().Available);
        Assert.True(s.State.Character.LearnSpell("spell.023"));
        Assert.False(s.State.Character.LearnSpell("spell.023"));
        f.Submit(new{action="spell-ready",key="spell.023"});
        Assert.Equal("spell.023",s.ReadSpells().Ready);Assert.Equal("Spell ready.",s.ReadSpells().Result);
        Assert.Equal("spell.023",s.Casting.ReadyFor(1)!.SpellKey);
        var row=Assert.Single(s.ReadSpells().Available); Assert.True(row.Cost>0);
        s.PublishInitial();Assert.Contains("spell.023",JsonSerializer.Serialize(f.Engine.Published()));
        f.Submit(new{action="spell-unready"});Assert.Null(s.Casting.ReadyFor(1));Assert.Null(s.ReadSpells().Ready);
        Assert.Equal("No spell ready.",s.ReadSpells().Result);
        f.Submit(new{action="spell-ready",key="spell.023"});
        double before=Magicka(s).Current;
        f.Submit(new{action="spell-cast"});
        Assert.Null(s.ReadSpells().Ready);Assert.Equal("Spell cast.",s.ReadSpells().Result);
        Assert.Equal(before-row.Cost,Magicka(s).Current);
        Assert.Equal("regenerate",Assert.Single(s.State.Effects.Active).Definition.Key);
        Assert.Equal(2,s.Casting.NextSequence);
    }

    [Fact]
    public void Unknown_unavailable_and_forgotten_records_cannot_leave_an_old_spell_armed()
    {
        using Fixture f=new();var s=f.Session;Fund(s);
        s.State.Character.LearnSpell("spell.023"); f.Submit(new{action="spell-ready",key="spell.023"});
        f.Submit(new{action="spell-ready",key="missing-record"});
        Assert.Null(s.ReadSpells().Ready);Assert.Equal("That spell is not known or available.",s.ReadSpells().Result);
        Assert.Throws<ArgumentException>(()=>s.State.Character.LearnSpell("missing-record"));
        var unavailable=TestPayload.Definitions.Magic.Spells.Values.First(spell=>!spell.Name.StartsWith('!')
            && s.Casting.AvailableSpellCost(1,spell.Key) is null);
        s.State.Character.LearnSpell(unavailable.Key);
        Assert.False(Assert.Single(s.ReadSpells().Available,row=>row.Key==unavailable.Key).CanCast);
        f.Submit(new{action="spell-ready",key=unavailable.Key});
        Assert.Equal("That spell has unavailable effects.",s.ReadSpells().Result);Assert.Null(s.Casting.ReadyFor(1));
        f.Submit(new{action="spell-ready",key="spell.023"});
        Assert.True(s.State.Character.ForgetSpell("spell.023"));Assert.Null(s.Casting.ReadyFor(1));
        Assert.Null(DaggerfallSavePayload.Read(s.CaptureSave()).ReadySpell);
    }

    [Fact]
    public void Save_retains_selected_identity_restore_requotes_restored_stats_and_keeps_donor_ready_admission()
    {
        using Fixture f=new();var s=f.Session;Fund(s);
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("restoration")).BaseValue=80;
        s.State.Character.LearnSpell("spell.023");f.Submit(new{action="spell-ready",key="spell.023"});
        int quoted=s.Casting.ReadyFor(1)!.Cost;Magicka(s).SetCurrent(0);
        var save=s.CaptureSave();Assert.Equal("spell.023",DaggerfallSavePayload.Read(save).ReadySpell!.SpellKey);
        Assert.Equal("spell.023",s.Casting.ReadyFor(1)!.SpellKey);
        using var restored=f.Restore(save);
        Assert.Equal("spell.023",restored.Casting.ReadyFor(1)!.SpellKey);
        Assert.Equal(quoted,restored.Casting.ReadyFor(1)!.Cost);
        // Classic admits payment on selection and consumes the latched quote on release,
        // even if magicka was drained while the spell was held ready.
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted,restored.ReleaseReadySpell(1,System.Numerics.Vector3.UnitZ).Outcome);
        Assert.Equal(0,Magicka(restored).Current);Assert.Single(restored.State.Effects.Active);
        Assert.Null(restored.Casting.ReadyFor(1));
        var raw=DaggerfallSavePayload.Read(save);
        Assert.Throws<ArgumentException>(()=>f.Restore(DaggerfallSavePayload.Encode(raw with{ReadySpell=new("missing",null,0,DaggerfallCastSource.Spell)})));
    }

    [Fact]
    public void Save_persists_item_readiness_and_held_UI_cast_is_refused()
    {
        using Fixture f=new();var s=f.Session;Fund(s);
        s.State.Character.LearnSpell("spell.023");f.Submit(new{action="spell-ready",key="spell.023"});
        f.Submit(new{action="menu",open=true});
        f.Submit(new{action="spell-cast"});Assert.Equal("spell.023",s.Casting.ReadyFor(1)!.SpellKey);
        Assert.Empty(s.State.Effects.Active);
        f.Submit(new{action="menu",open=false},new{action="spell-cast"});
        Assert.Null(s.Casting.ReadyFor(1));Assert.Single(s.State.Effects.Active);
        Assert.Equal("Spell cast.",s.ReadSpells().Result);
        f.Submit(new{action="spell-unready"});Assert.Null(s.Casting.ReadyFor(1));
        var item=s.State.Inventory.Read().UniqueItems.First();
        ulong id=s.State.Inventory.GetDurableItemId(item.Entity).Value;
        s.Casting.Ready(1,"spell.023",id);Assert.NotNull(s.Casting.ReadyFor(1));
        Assert.Equal(id,DaggerfallSavePayload.Read(s.CaptureSave()).ReadySpell!.ItemId);
        using var restored=f.Restore(s.CaptureSave());Assert.Equal(s.Casting.ReadyFor(1),restored.Casting.ReadyFor(1));
    }

    private static Track Magicka(DaggerfallSession s)=>s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
    private static void Fund(DaggerfallSession s){var track=Magicka(s);track.Maximum.BaseValue=10000;track.SetCurrent(10000);}
    internal sealed class Fixture:IDisposable
    {
        private readonly DaggerfallSessionComposition composition;
        internal DaggerfallSession Session{get;}internal EngineContextFake Engine{get;}private ulong step;
        internal Fixture(Content.DaggerfallDefinitions? definitions=null){composition=new(definitions ?? TestPayload.Definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);Engine=CreateEngine();Session=DaggerfallSession.StartNew(Engine.Context,composition);}
        private EngineContextFake CreateEngine()
        {
            List<string> releases=[];ContentFake content=new(releases);PopulateContent(content,composition.StartSite);
            return EngineContextFake.Create(content,SpatialFake.Create(composition.StartSite.SpatialArtifact.Sha256,releases).Service,
                new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());
        }
        internal void Submit(params object[] actions)
        {
            Session.Update(new ProductUpdate(OuterUpdate(++step),actions.Select(action=>Ui(JsonSerializer.Serialize(action))).ToArray()));
            if(Session.PendingModeRequest is { } mode) Session.ApplyProductMode(mode);
        }
        internal DaggerfallSession Restore(RulesetSavePayload save)=>DaggerfallSession.Restore(CreateEngine().Context,composition,save);
        public void Dispose()=>Session.Dispose();
    }
}
