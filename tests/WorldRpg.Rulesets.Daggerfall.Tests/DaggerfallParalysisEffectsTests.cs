using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallParalysisEffectsTests
{
    [Fact]
    public void Independent_sources_expire_cancel_and_cure_without_releasing_another_sources_restriction()
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "short", 1, 1, 2); Start(s, "long", 1, 1, 5);
        Assert.Equal(new ActorControlRestrictions(true,true), s.State.Effects.ControlsFor(1));
        s.State.Effects.AdvanceElapsedRounds(1);
        Assert.Equal("long", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
        Assert.True(s.State.Effects.ControlsFor(1).Movement);
        Start(s, "other", 1, 1, 10);
        s.State.Effects.Cancel(EffectInstanceId.Parse("long"));
        Assert.True(s.State.Effects.ControlsFor(1).PhysicalAttacks);
        Assert.Equal(1, DaggerfallParalysisEffects.Cure(s.State.Effects, 1));
        Assert.Equal(0, DaggerfallParalysisEffects.Cure(s.State.Effects, 1));
        Assert.Equal(default, s.State.Effects.ControlsFor(1)); Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Canonical_actor_and_item_retirement_remove_only_their_bound_sources()
    {
        using Fixture f = new(); var s = f.Session;
        long caster = s.SpawnActor("rat", new ActorPose(new WorldPoint(10,0,10), 0));
        Start(s, "caster", caster, 1, 10); Start(s, "retained", 1, 1, 10);
        s.RetireActor(caster);
        Assert.Equal("retained", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
        var item = s.State.Inventory.Read().UniqueItems.First();
        ulong itemId = s.State.Inventory.GetDurableItemId(item.Entity).Value;
        Start(s, "item", 1, 1, 10, itemId);
        s.State.Effects.Cancel(EffectInstanceId.Parse("retained"));
        // A used item's effect outlives the item breaking; destroying the item ends it.
        s.State.ItemInstances.ReplaceUnique(itemId, s.State.ItemInstances.RequireUnique(itemId) with { CurrentCondition = 0 });
        Assert.True(s.State.Effects.ControlsFor(1).Movement);
        s.State.ItemInstances.RemoveUnique(itemId);
        Assert.Equal(default, s.State.Effects.ControlsFor(1));
        long target = s.SpawnActor("rat", new ActorPose(new WorldPoint(11,0,11), 0));
        Start(s, "target", 1, target, 10); s.RetireActor(target);
        Assert.Empty(s.State.Effects.Active); Assert.Equal(default, s.State.Effects.ControlsFor(target));
    }

    [Fact]
    public void Restore_retains_source_lifetimes_and_cure_saves_clear_current_condition_without_replay()
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "one", 1, 1, 10); Start(s, "two", 1, 1, 20);
        var before = s.State.Effects.Active.Select(effect => effect.Lifecycle.RemainingRounds).ToArray();
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(before, restored.State.Effects.Active.Select(effect => effect.Lifecycle.RemainingRounds));
        Assert.True(restored.State.Effects.ControlsFor(1).Movement);
        Assert.True(restored.State.Effects.Cure(EffectInstanceId.Parse("one")));
        Assert.True(restored.State.Effects.ControlsFor(1).Movement);
        Assert.Equal(1, DaggerfallParalysisEffects.Cure(restored.State.Effects, 1));
        using var cured = f.Restore(restored.CaptureSave());
        Assert.Empty(cured.State.Effects.Active); Assert.Equal(default, cured.State.Effects.ControlsFor(1));
    }

    [Fact]
    public void Real_movement_proposal_preserves_look_and_gravity_but_blocks_planar_jump_and_vertical_drive_until_cure()
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "player", 1, 1, 10);
        float yaw = s.State.PlayerControl.YawRadians;
        s.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard:KeyboardControl.KeyW),
            Input(InputEventKind.PointerDelta, x:20), Input(InputEventKind.MappedDigital, InputEdge.Pressed, x:1, phase:InputPhase.Pressed, intent:"attack")]));
        var proposal = f.Spatial.StepRequests.Last();
        Assert.Equal(Vector2.Zero, proposal.Command.PlanarIntent); Assert.False(proposal.Command.JumpPressed);
        Assert.True(proposal.Config.Vertical.Gravity > 0); Assert.NotEqual(yaw, s.State.PlayerControl.YawRadians);
        var before = s.State.Actors.All.First().Stats.GetTrack(TrackId.Parse("health")).Current;
        s.ResolveExplicitMelee(new(1, s.State.Actors.All.First().DurableId, 1, 1000, .125));
        Assert.Equal(before, s.State.Actors.All.First().Stats.GetTrack(TrackId.Parse("health")).Current);
        DaggerfallParalysisEffects.Cure(s.State.Effects, 1);
        s.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.NotEqual(Vector2.Zero, f.Spatial.StepRequests.Last().Command.PlanarIntent);
    }

    [Fact]
    public void Enemy_pursuit_and_accepted_delayed_melee_stop_without_cancelling_another_actor()
    {
        using Fixture f = new(); var s = f.Session; long enemy = s.State.Actors.All.First().DurableId;
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new CertainStrike());
        FactBuffer<IProductFact> facts = new();
        Assert.True(s.State.Kit.AttackExecution.Start(new(1,enemy,1,1,.125,true),facts));
        double health = s.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("health")).Current;
        Start(s, "player", 1, 1, 10);
        s.State.Kit.AttackExecution.ApplyImpacts([new(1,enemy,1,1,false)],1,facts);
        Assert.Equal(health, s.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("health")).Current);
        Start(s, "enemy", 1, enemy, 10);
        var position = s.State.Actors.Get(enemy).Position;
        s.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(position, s.State.Actors.Get(enemy).Position);
        Assert.Equal(WorldRpg.Rulesets.Daggerfall.Modules.Behavior.EnemyBehaviorState.Idle, s.LastEnemyBehavior[enemy].State);
        var delivered = new List<IProductFact>(); facts.Deliver(delivered.Add);
        Assert.Contains(delivered, fact => fact is AttackRejectedFact { Reason: AttackRejection.Incapacitated });
        Assert.True(s.State.Effects.ControlsFor(1).Movement);
    }

    [Theory]
    [InlineData("spell.034")] [InlineData("spell.047")] [InlineData("spell.062")]
    public void Published_paralysis_spells_run_chance_save_immunity_and_default_catalog(string key)
    {
        using Fixture f = new(); var s = f.Session; long target = s.State.Actors.All.First().DurableId;
        s.State.Progression.AdvanceTo(0, 100); s.State.Character.LearnSpell(key);
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(key).Outcome);
        var bundle = s.Casting.Release(1,true).Bundle!; s.Casting.Deliver(bundle,[target]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.True(s.State.Effects.ControlsFor(target).Movement);
        DaggerfallParalysisEffects.Cure(s.State.Effects,target);
        var immunity = s.State.Actors.Get(target).Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityParalysis.Value)); immunity.BaseValue = 1;
        s.ReadyPlayerSpell(key); bundle = s.Casting.Release(1,true).Bundle!; s.Casting.Deliver(bundle,[target]);
        Assert.Equal(DaggerfallCastOutcome.Immune, Assert.Single(bundle.Results).Outcome); Assert.Empty(s.State.Effects.Active);
        immunity.BaseValue = 0; DaggerfallAlterationEffectsTests.Resistance(s,"ward",DaggerfallMagicResistanceElement.Magic,100,10,target);
        s.ReadyPlayerSpell(key); bundle = s.Casting.Release(1,true).Bundle!; s.Casting.Deliver(bundle,[target]);
        Assert.Equal(DaggerfallCastOutcome.Resisted, Assert.Single(bundle.Results).Outcome); Assert.False(s.State.Effects.ControlsFor(target).Movement);
    }

    [Fact]
    public void Caster_only_shape_is_refused_before_payment_and_nonzero_save_retains_full_duration()
    {
        using Fixture f = new(); var s = f.Session;
        var original = TestPayload.Definitions.Magic.Spells["spell.047"];
        var setting = original.Effects[0] with { DurationBase = 8, DurationMod = 0, ChanceBase = 100, ChanceMod = 0 };
        var spell = original with { Effects = [setting] };
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
        // Only a constructed spell is held to its effects' allowed targets; classic records are not.
        var invalid = CastingFor(f, spell with { RangeType = 0, IsCustom = true }, 50);
        Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect, invalid.Ready(1, spell.Key).Outcome);
        Assert.Equal(10000, magicka.Current); Assert.Empty(s.State.Effects.Active);
        var casting = CastingFor(f, spell, 50);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1,true).Bundle!; casting.Deliver(bundle,[s.State.Actors.All.First().DurableId]);
        Assert.Equal(75, Assert.Single(bundle.Results).SavePercent);
        Assert.Equal(7u, Assert.Single(s.State.Effects.Active).Lifecycle.RemainingRounds);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Every_allowed_element_uses_the_compiled_paralysis_owner(int element)
    {
        using Fixture f = new();
        var spell = TestPayload.Definitions.Magic.Spells["spell.047"];
        spell = spell with { Element = element, Effects = [spell.Effects[0] with { ChanceBase=100, ChanceMod=0 }] };
        var magicka=f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));magicka.Maximum.BaseValue=10000;magicka.SetCurrent(10000);
        var casting=CastingFor(f,spell,100); Assert.Equal(DaggerfallCastOutcome.Ready,casting.Ready(1,spell.Key).Outcome);
        var bundle=casting.Release(1,true).Bundle!;long target=f.Session.State.Actors.All.First().DurableId;casting.Deliver(bundle,[target]);
        Assert.Equal(DaggerfallCastOutcome.Applied,Assert.Single(bundle.Results).Outcome);
        Assert.True(f.Session.State.Effects.ControlsFor(target).Movement);
    }

    [Fact]
    public void Low_chance_refuses_without_condition_and_paralysis_does_not_block_actual_spell_casting()
    {
        using Fixture f = new(); var s = f.Session;
        s.State.Character.LearnSpell("spell.047");
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue=10000; magicka.SetCurrent(10000);
        Start(s, "player", 1, 1, 10);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.047").Outcome);
        var bundle = s.Casting.Release(1,true).Bundle!; s.Casting.Deliver(bundle,[s.State.Actors.All.First().DurableId]);
        Assert.Equal(DaggerfallCastOutcome.ChanceFailed, Assert.Single(bundle.Results).Outcome);
        Assert.Equal("player", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
    }

    [Fact]
    public void Lethal_spell_ends_target_condition_through_ordinary_death_and_corpse_consumer()
    {
        using Fixture f = new(); var s = f.Session; var target = s.State.Actors.All.First();
        Start(s, "condition",1,target.DurableId,10);
        s.State.Progression.AdvanceTo(0,100); s.State.Character.LearnSpell("spell.052");
        var magicka=s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue=10000;magicka.SetCurrent(10000);
        s.ReadyPlayerSpell("spell.052"); var bundle=s.Casting.Release(1,true).Bundle!;s.Casting.Deliver(bundle,[target.DurableId]);
        s.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.True(target.IsDefeated); Assert.True(s.Corpses.ContainsKey(target.DurableId));
        Assert.Equal(default,s.State.Effects.ControlsFor(target.DurableId)); Assert.Empty(s.State.Effects.Active);
    }

    internal static void Start(DaggerfallSession s,string instance,long caster,long target,uint rounds,ulong? item=null)
    {
        var setting=new DaggerfallSpellEffectDefinition("paralyze-settings",0,-1,(int)rounds,0,1,100,0,1,0,0,0,0,1);
        var state=JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(setting,1,0,100),DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        s.State.Effects.Start(new(instance,"paralyze","spell.paralyze",caster,target,setting.Key,"Magic",item,1,rounds,state));
    }
    private static DaggerfallCasting CastingFor(Fixture f,DaggerfallSpellDefinition spell,int save)
    {
        var s=f.Session;
        var catalog=TestPayload.Definitions.Magic with { Spells=new Dictionary<string,DaggerfallSpellDefinition> { [spell.Key]=spell } };
        var profile=new DaggerfallMagicTargetProfile(50,new(DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal,
            DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal,
            DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal),null,0,0,0,new(0,0,0,0,0),[]);
        return new(catalog,s.State.Effects,id=>id==1?s.State.Actors.Player.Actor:s.State.Actors.TryGet(id,out var actor)?actor.Actor:null,
            _=>profile,_=>true,_=>{},_=>{},DaggerfallCastingTests.SaveDice.Create(save),1,playerKnowsSpell:_=>true);
    }
    private sealed class CertainStrike : ICombatContribution { public void Hit(TryHitEvent interaction)=>interaction.Hit=true; }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition=new(TestPayload.Definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);
        internal SpatialFake Spatial=null!;
        internal DaggerfallSession Session { get; }
        internal Fixture()=>Session=DaggerfallSession.StartNew(Engine().Context,_composition);
        private EngineContextFake Engine()
        {
            List<string> releases=[];ContentFake content=new(releases);PopulateContent(content,_composition.StartSite);
            Spatial=SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256,releases);Spatial.KeepPosition=true;
            return EngineContextFake.Create(content,Spatial.Service,new AppearanceFake(releases),random:RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload)=>DaggerfallSession.Restore(Engine().Context,_composition,payload);
        public void Dispose()=>Session.Dispose();
    }
}
