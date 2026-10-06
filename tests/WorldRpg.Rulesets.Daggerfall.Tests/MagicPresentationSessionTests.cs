using System.Reflection;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class MagicPresentationSessionTests
{
    [Fact]
    public void Real_cast_delivers_one_release_and_impact_and_live_status_without_replaying_on_restore()
    {
        using Fixture f = new(); var s = f.Session; Fund(s);
        s.State.Character.LearnSpell("spell.023"); s.ReadyPlayerSpell("spell.023");
        var health=s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));health.SetCurrent(health.Maximum.Value-1);
        var bundle = s.ReleaseReadySpell(1,Vector3.UnitZ).Bundle!;
        Assert.Equal(1,Assert.Single(Pending(s).OfType<SpellTrackRestoredFact>()).Restored);
        Assert.Equal(2, Pending(s).OfType<SpellCastFact>().Count());
        Assert.Single(Pending(s).OfType<MagicEffectFact>(), fact => fact.Outcome.Kind == DaggerfallEffectOutcomeKind.Started);
        f.Update();
        Assert.Single(s.Slots.Read(), slot => slot.Owner == DaggerfallMagicPresentation.Owner);
        Assert.Equal(1, EffectCount(Visual(s)));
        Assert.Single(f.Engine.EmittedAudio, request => request.SignalId.Contains("spell-release"));
        f.Update(); Assert.Single(f.Engine.EmittedAudio, request => request.SignalId.Contains("spell-release"));
        var save = s.CaptureSave(); using var restored = f.Restore(save, out var engine);
        restored.PublishInitial();
        Assert.Single(restored.Slots.Read(), slot => slot.Owner == DaggerfallMagicPresentation.Owner);
        Assert.Equal(0, EffectCount(Visual(restored))); Assert.DoesNotContain(engine.EmittedAudio, request => request.SignalId.Contains("spell-release"));
        s.State.Effects.CancelActorReferences(1); f.Update();
        Assert.DoesNotContain(s.Slots.Read(), slot => slot.Owner == DaggerfallMagicPresentation.Owner); Assert.Equal(0, EffectCount(Visual(s)));
    }

    [Fact]
    public void Item_teardown_and_expiry_remove_authoritative_status_and_bound_appearance()
    {
        using Fixture f = new(); var s = f.Session; Fund(s);
        var item = s.State.Inventory.Read().UniqueItems.First(); var id = s.State.Inventory.GetDurableItemId(item.Entity).Value;
        s.Casting.Ready(1,"spell.023",id); s.ReleaseReadySpell(1,Vector3.UnitZ); f.Update();
        // The status row names the source item and the remaining game time, not ids or magic rounds.
        var row = Assert.Single(s.Slots.Read(), slot => slot.Owner == DaggerfallMagicPresentation.Owner);
        Assert.Contains($"From {s.ItemDefinitionName(item.Definition.Value)}.", row.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(id.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("magic rounds", row.Detail, StringComparison.Ordinal);
        Assert.Equal(1, EffectCount(Visual(s)));
        s.State.Effects.CancelItemReferences(id); f.Update(); Assert.Equal(0, EffectCount(Visual(s)));
        Assert.DoesNotContain(s.Slots.Read(), slot => slot.Owner == DaggerfallMagicPresentation.Owner);
        s.State.Character.LearnSpell("spell.023"); s.ReadyPlayerSpell("spell.023"); s.ReleaseReadySpell(1,Vector3.UnitZ); f.Update();
        s.State.Effects.AdvanceElapsedRounds(100000); f.Update();
        Assert.DoesNotContain(s.Slots.Read(), slot => slot.Owner == DaggerfallMagicPresentation.Owner); Assert.Equal(0, EffectCount(Visual(s)));
    }

    [Fact]
    public void Healing_reports_actual_bounded_recovery_and_instant_feedback_is_not_removed_by_immediate_expiry()
    {
        using Fixture f = new(); var s = f.Session; Fund(s);
        var spell = TestPayload.Definitions.Magic.Spells.Values.First(spell => !spell.Name.StartsWith('!') && spell.RangeType == 0
            && spell.Effects.Any(effect => effect.Type == 10 && effect.SubType == 8) && s.Casting.AvailableSpellCost(1,spell.Key) is not null);
        var health = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")); health.SetCurrent(health.Maximum.Value-1);
        s.State.Character.LearnSpell(spell.Key); s.ReadyPlayerSpell(spell.Key); s.ReleaseReadySpell(1,Vector3.UnitZ);
        var fact = Assert.Single(Pending(s).OfType<SpellTrackRestoredFact>());
        Assert.Equal("health",fact.Track); Assert.Equal(1,fact.Restored); Assert.True(fact.Requested>=1);
        f.Update(); Assert.True(EffectCount(Visual(s))>0);
        Assert.DoesNotContain(s.Slots.Read(), slot => slot.Owner==DaggerfallMagicPresentation.Owner);
    }

    [Fact]
    public void Cure_before_delivery_and_death_clear_feedback_and_status_without_stale_rows()
    {
        using Fixture f = new(); var s = f.Session; Fund(s);
        s.State.Character.LearnSpell("spell.023"); s.ReadyPlayerSpell("spell.023"); s.ReleaseReadySpell(1,Vector3.UnitZ);
        var effect = Assert.Single(s.State.Effects.Active);
        s.State.Effects.Cure(effect.Context.Instance); f.Update();
        Assert.Equal(0,EffectCount(Visual(s))); Assert.DoesNotContain(s.Slots.Read(),slot=>slot.Owner==DaggerfallMagicPresentation.Owner);
        s.ReadyPlayerSpell("spell.023");s.ReleaseReadySpell(1,Vector3.UnitZ);f.Update();Assert.Equal(1,EffectCount(Visual(s)));
        s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);s.PublishInitial();
        Assert.Equal(0,EffectCount(Visual(s)));Assert.DoesNotContain(s.Slots.Read(),slot=>slot.Owner==DaggerfallMagicPresentation.Owner);
    }

    [Fact]
    public void Poison_death_and_drug_recovery_flow_through_actual_consequence_facts()
    {
        using(Fixture f=new())
        {
            var s=f.Session;s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
            Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor,130));s.State.Effects.AdvanceElapsedRounds(1);
            Assert.Single(Pending(s).OfType<DamageAppliedFact>());Assert.Single(Pending(s).OfType<ActorDiedFact>());
            f.Update();Assert.True(s.State.Actors.Player.IsDefeated);
        }
        using(Fixture f=new())
        {
            var s=f.Session;var stamina=s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));stamina.SetCurrent(1);
            Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor,138));s.State.Effects.AdvanceElapsedRounds(20);
            Assert.Contains(Pending(s).OfType<SpellTrackRestoredFact>(),fact=>fact.Track=="stamina" && fact.Restored>0);
            Assert.True(stamina.Current>1);
        }
    }

    [Theory]
    [InlineData(0,"fireCast")] [InlineData(1,"coldCast")] [InlineData(2,"poisonCast")] [InlineData(3,"shockCast")] [InlineData(4,"magicCast")]
    public void Each_element_opens_published_content_audio_once(int element,string clip)
    {
        using Fixture f=new(); f.Update(); var s=f.Session;var visual=Visual(s);
        var fact=new SpellCastFact(DaggerfallCastOutcome.Released,100,1,"spell",1,[],Element:element);
        visual.ReactSpellCast(fact,s.State.Actors,s.State.PlayerControl.Position);
        visual.ReactSpellCast(fact,s.State.Actors,s.State.PlayerControl.Position);
        Assert.Single(f.Engine.EmittedAudio,request=>request.SignalId.EndsWith(clip)); Assert.True(f.Engine.OpenedContentClips>0);
    }

    private static IProductFact[] Pending(DaggerfallSession s)
    {
        var buffer=typeof(DaggerfallSession).GetField("_facts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;
        return ((IEnumerable<IProductFact>)buffer.GetType().GetField("_pending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(buffer)!).ToArray();
    }
    private static DaggerfallSiteAppearance Visual(DaggerfallSession s)=>(DaggerfallSiteAppearance)typeof(DaggerfallSession).GetProperty("_appearance",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;
    private static void Fund(DaggerfallSession s){var t=s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));t.Maximum.BaseValue=10000;t.SetCurrent(10000);}
    private sealed class Fixture:IDisposable
    {
        private readonly DaggerfallSessionComposition composition=new(TestPayload.Definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);
        internal EngineContextFake Engine{get;} internal DaggerfallSession Session{get;} private ulong step;
        internal Fixture()
        {
            var site=composition.StartSite; BundleContentFake bundles=new();
            string prefix=site.ProfileKey.LogicalId+"/media/audio/clips/";
            foreach(var clip in site.Audio)
                bundles.Add(site.AudioBundle!,clip.Path[prefix.Length..],File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,"content",clip.Path)));
            ProductContent content=new(Array.Empty<ProductContentFile>(),bundles);
            composition=composition with { Audio=new DaggerfallSiteAudioBundles(content,new DaggerfallSiteProfiles([site])) };
            Engine=CreateEngine();Session=DaggerfallSession.StartNew(Engine.Context,composition);
        }
        private EngineContextFake CreateEngine(){List<string> releases=[];ContentFake c=new(releases);PopulateContent(c,composition.StartSite);return EngineContextFake.Create(c,SpatialFake.Create(composition.StartSite.SpatialArtifact.Sha256,releases).Service,new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());}
        internal void Update()=>Session.Update(new ProductUpdate(OuterUpdate(++step),[]));
        internal DaggerfallSession Restore(RulesetSavePayload save,out EngineContextFake engine){engine=CreateEngine();return DaggerfallSession.Restore(engine.Context,composition,save);}
        public void Dispose()=>Session.Dispose();
    }
}
