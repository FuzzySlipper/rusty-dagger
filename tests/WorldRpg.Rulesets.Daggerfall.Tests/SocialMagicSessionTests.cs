using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using Item=WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SocialMagicSessionTests
{
    [Theory]
    [InlineData(0,(int)DaggerfallEnemyGroup.Animals)] [InlineData(1,(int)DaggerfallEnemyGroup.Undead)]
    [InlineData(2,(int)DaggerfallEnemyGroup.Humanoid)] [InlineData(3,(int)DaggerfallEnemyGroup.Daedra)]
    public void Every_pacify_group_uses_actual_compiled_target_policy_and_saved_disposition(int variant,int group)
    {
        using Fixture f=new();var s=f.Session;
        var definition=TestPayload.Definitions.Actors.Values.First(actor=>actor.Kind==DaggerfallActorKinds.Monster && DaggerfallFormulaPolicy.EnemyGroupFor(actor)==(DaggerfallEnemyGroup)group);
        long id=s.SpawnActor(definition.Id.Value,new ActorPose(new WorldPoint(11,0,11),0));
        Assert.Equal(DaggerfallCastOutcome.Applied,Cast(s,Spell(33,variant),id));
        Assert.True(Memory(s,id).MagicallyPacified);Assert.True(Memory(s,id).Pacified);Assert.Empty(s.State.Effects.Active);
        s.State.Effects.AdvanceElapsedRounds(10000);Assert.True(Memory(s,id).Pacified);
        Memory(s,id).Clear();Assert.True(Memory(s,id).Pacified);
        using var restored=f.Restore(s.CaptureSave());Assert.True(Memory(restored,id).MagicallyPacified);
        Assert.True(Memory(restored,id).Pacified);
    }

    [Fact]
    public void Charm_accepts_only_enemy_classes_and_physical_attack_breaks_the_saved_disposition()
    {
        using Fixture f=new();var s=f.Session;
        var definition=TestPayload.Definitions.Actors.Values.First(actor=>actor.Kind==DaggerfallActorKinds.EnemyClass);
        long id=s.SpawnActor(definition.Id.Value,new(new(11,0,11),0));
        Assert.Equal(DaggerfallCastOutcome.Applied,Cast(s,Spell(34,-1),id));Assert.True(Memory(s,id).Pacified);
        using var restored=f.Restore(s.CaptureSave());Assert.True(Memory(restored,id).Pacified);
        Assert.Equal(DaggerfallCastOutcome.NoMatch,Cast(s,Spell(33,2),id));
        typeof(DaggerfallSession).GetMethod("React",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(s,[new AttackHitFact(1,id,1,1,0,false,1,1)]);
        Assert.False(Memory(s,id).Pacified);Assert.False(Memory(s,id).MagicallyPacified);Assert.True(Memory(s,id).ForcedHostile);
        using var after=f.Restore(s.CaptureSave());Assert.False(Memory(after,id).Pacified);Assert.True(Memory(after,id).ForcedHostile);
        Assert.Equal(DaggerfallCastOutcome.NoMatch,Cast(s,Spell(34,-1),s.State.Actors.All.First(actor=>actor.DurableId!=id).DurableId));
    }

    [Theory]
    [InlineData(100,true)] [InlineData(1,false)]
    public void Identify_uses_current_items_actual_chance_one_batch_cost_and_restored_pending_choice(int chance,bool succeeds)
    {
        using Fixture f=new();var s=f.Session;Fund(s);var first=Stock(s);var second=Stock(s);
        Assert.Equal(DaggerfallCastOutcome.Applied,Cast(s,Spell(40,-1,chance),1));
        var view=s.IdentifyView!;Assert.Equal(2,view.Options.Count);Assert.True(view.Cost>=5);
        var save=s.CaptureSave();using var restored=f.Restore(save);Assert.Equal(view.Revision,restored.IdentifyView!.Revision);Assert.Equal(view.Cost,restored.IdentifyView.Cost);Assert.Equal(view.Options,restored.IdentifyView.Options);
        double before=Magicka(restored).Current;
        restored.ChooseIdentify("stale","all");Assert.Equal(before,Magicka(restored).Current);
        f.Submit(restored,new{action="identify-select",revision=view.Revision,key="all"});Assert.Equal(before-view.Cost,Magicka(restored).Current);
        Assert.Equal(succeeds,restored.State.ItemInstances.RequireUnique(Id(s,first)).Identified);
        Assert.Equal(succeeds,restored.State.ItemInstances.RequireUnique(Id(s,second)).Identified);
        Assert.Equal(succeeds?0:2,restored.IdentifyView!.Options.Count);
        Assert.Equal(1,DaggerfallSavePayload.Read(restored.CaptureSave()).PendingIdentify!.Attempts);
        using var again=f.Restore(restored.CaptureSave());Assert.Equal(succeeds,again.State.ItemInstances.RequireUnique(Id(s,first)).Identified);
        again.ChooseIdentify(view.Revision,null);Assert.Null(again.IdentifyView);
    }

    [Fact]
    public void Published_identify_cast_and_semantic_choice_share_normal_HUD_inventory_and_modal_owner()
    {
        var spell=Spell(40,-1);
        using Fixture f=new(spell);var s=f.Session;Fund(s);var item=Stock(s);
        s.State.Character.LearnSpell(spell.Key);s.ReadyPlayerSpell(spell.Key);s.ReleaseReadySpell(1,Vector3.UnitZ);f.Submit();
        Assert.NotNull(s.IdentifyView);Assert.True(s.Interactions.IsOpen(DaggerfallInteractionScreen.Identify));Assert.True(s.Interactions.HoldsWorldOpen);
        Assert.Contains("identify",System.Text.Json.JsonSerializer.Serialize(f.Engine.Published()));
        var view=s.IdentifyView!;Magicka(s).SetCurrent(0);f.Submit(new{action="identify-select",revision=view.Revision,key="all"});
        Assert.False(s.State.ItemInstances.RequireUnique(Id(s,item)).Identified);Assert.Equal(0,Magicka(s).Current);
        f.Submit(new{action="identify-cancel",revision=view.Revision});Assert.Null(s.IdentifyView);
        var malformed=DaggerfallSavePayload.Read(s.CaptureSave()) with {PendingIdentify=new("request",1,-1)};
        Assert.Throws<ArgumentOutOfRangeException>(()=>f.Restore(DaggerfallSavePayload.Encode(malformed)));
    }

    [Fact]
    public void Item_identify_cancels_after_real_source_transfer_or_break_and_rejects_unavailable_saved_sources()
    {
        using Fixture f = new();
        var s = f.Session; var source = Stock(s); var target = Stock(s);
        ulong sourceId = Id(s, source), targetId = Id(s, target);
        Assert.Equal(DaggerfallCastOutcome.Applied, Cast(s, Spell(40, -1), 1, sourceId));
        f.Submit();
        var request = DaggerfallSavePayload.Read(s.CaptureSave()).PendingIdentify!;
        double before = Magicka(s).Current;
        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{source.EntityId}", amount = 1 });
        Assert.Null(s.IdentifyView);
        Assert.NotEqual(DaggerfallItemOwner.Player, s.State.ItemInstances.RequireUnique(sourceId).Owner);
        s.ChooseIdentify(request.Instance, "all");
        Assert.Equal(before, Magicka(s).Current);
        Assert.False(s.State.ItemInstances.RequireUnique(targetId).Identified);
        var movedSave = DaggerfallSavePayload.Read(s.CaptureSave()) with { PendingIdentify = request };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(movedSave)));

        Assert.Equal(DaggerfallCastOutcome.Applied, Cast(s, Spell(40, -1), 1, targetId));
        var currentSave = s.CaptureSave();
        foreach (string field in new[] { "SourceItem", "Attempts" })
        {
            var current = System.Text.Json.Nodes.JsonNode.Parse(currentSave.Bytes.Span)!;
            current["PendingIdentify"]!.AsObject().Remove(field);
            var malformed = new RulesetSavePayload(DaggerfallRuleset.Identity, System.Text.Encoding.UTF8.GetBytes(current.ToJsonString()));
            Assert.Throws<ArgumentException>(() => f.Restore(malformed));
        }
        var valid = DaggerfallSavePayload.Read(currentSave);
        var brokenSave = valid with { Inventory = valid.Inventory with { UniqueItems = valid.Inventory.UniqueItems.Select(item => item.EntityId == targetId
            ? item with { Metadata = item.Metadata with { CurrentCondition = 0 } } : item).ToArray() } };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(brokenSave)));
        var metadata = s.State.ItemInstances.RequireUnique(targetId);
        s.State.ItemInstances.ReplaceUnique(targetId, metadata with { CurrentCondition = 0 });
        Assert.Null(s.IdentifyView);
        Assert.Null(DaggerfallSavePayload.Read(s.CaptureSave()).PendingIdentify);
    }

    private static DaggerfallEnemyPerceptionMemory Memory(DaggerfallSession s,long id)=>s.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>();
    private static DaggerfallSpellDefinition Spell(int type,int subtype,int chance=100)=>new($"social.{type}.{subtype}",1,false,"Social magic",4,type==40?0:1,0,0,[new("social",type,subtype,99,0,1,chance,0,1,0,0,0,0,1)]);
    private static DaggerfallCastOutcome Cast(DaggerfallSession s,DaggerfallSpellDefinition spell,long target,ulong? sourceItem=null)
    {
        Fund(s);var casting=new DaggerfallCasting(TestPayload.Definitions.Magic with {Spells=new Dictionary<string,DaggerfallSpellDefinition>{{spell.Key,spell}}},s.State.Effects,
            id=>id==1?s.State.Actors.Player.Actor:s.State.Actors.Get(id).Actor,s.MagicProfile,_=>true,_=>{},_=>{},RandomMaximum.Create(),playerId:1);
        Assert.Equal(DaggerfallCastOutcome.Ready,casting.Ready(1,spell.Key,itemId:sourceItem).Outcome);
        var bundle=casting.Release(1,true).Bundle!;casting.Deliver(bundle,[target]);return Assert.Single(bundle.Results).Outcome;
    }
    private static Track Magicka(DaggerfallSession s)=>s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
    private static void Fund(DaggerfallSession s){var t=Magicka(s);t.Maximum.BaseValue=10000;t.SetCurrent(10000);}
    private static ulong Id(DaggerfallSession s,Item item)=>s.State.Inventory.GetDurableItemId(new(item.EntityId)).Value;
    private static Item Stock(DaggerfallSession s)
    {
        var created=new DaggerfallItemFactory(TestPayload.Definitions,RandomMinimum.Create()).Create(new("Magic","identify-source",DaggerfallItemOwner.Player,Race:"breton",Gender:"male",MagicItemKey:"magic-item.0022"));
        var id=s.UniqueItemAllocator.AllocateReference();var item=s.State.Equipment.Materialize(id,created.Item);s.State.ItemInstances.RegisterUnique(id.Value,created.Metadata);return item;
    }
    private sealed class Fixture:IDisposable
    {
        private readonly DaggerfallSessionComposition composition;
        internal DaggerfallSession Session{get;}internal EngineContextFake Engine{get;}private ulong step;
        internal Fixture(DaggerfallSpellDefinition? constructed=null){
            var definitions=TestPayload.Definitions;
            if(constructed is not null)definitions=new(
                definitions.Catalogs, definitions.Vocabulary, definitions.Actors, definitions.Items, definitions.EquipmentSlots,
                definitions.ArmorValuesByMaterial, definitions.Actions, definitions.LootTables, definitions.HudResources,
                definitions.LootCategoryPools, definitions.DonorErrata, definitions.ItemTemplates, definitions.CharacterPresentation,
                definitions.Locations, definitions.Text, definitions.Magic with {Spells=new Dictionary<string,DaggerfallSpellDefinition>(definitions.Magic.Spells){{constructed.Key,constructed}}},
                definitions.Mobiles, definitions.Names, definitions.Rumors, definitions.Biographies, definitions.Grids, definitions.Books,
                definitions.Factions, definitions.Terrain, definitions.ItemTemplateCatalog, definitions.QuestSources, definitions.Cinematics, definitions.Encounters)
                {BuildingNames=definitions.BuildingNames};
            composition=new(definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);
            Engine=CreateEngine();Session=DaggerfallSession.StartNew(Engine.Context,composition);
        }
        private EngineContextFake CreateEngine(){List<string> releases=[];ContentFake content=new(releases);PopulateContent(content,composition.StartSite);return EngineContextFake.Create(content,SpatialFake.Create(composition.StartSite.SpatialArtifact.Sha256,releases).Service,new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());}
        internal void Submit(params object[] actions)=>Submit(Session,actions);
        internal void Submit(DaggerfallSession session,params object[] actions){session.Update(new ProductUpdate(OuterUpdate(++step),actions.Select(action=>Ui(System.Text.Json.JsonSerializer.Serialize(action))).ToArray()));if(session.PendingModeRequest is {} mode)session.ApplyProductMode(mode);}
        internal DaggerfallSession Restore(RulesetSavePayload save)=>DaggerfallSession.Restore(CreateEngine().Context,composition,save);
        public void Dispose()=>Session.Dispose();
    }
}
