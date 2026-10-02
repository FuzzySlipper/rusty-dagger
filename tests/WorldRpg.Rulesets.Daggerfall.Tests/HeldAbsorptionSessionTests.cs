using System.Numerics;
using System.Text;
using System.Text.Json;
using WorldRpg.Kit.Controls;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class HeldAbsorptionSessionTests
{
    [Fact]
    public void Stock_absorption_item_reads_live_equipment_break_and_retirement_without_a_saved_flag()
    {
        using Fixture f = new(); var s = f.Session;
        var item = Stock(s); ulong id = Id(s,item);
        Assert.Equal(0, Defense(s).AbsorptionChance);
        Equip(s,item);
        Assert.Equal(100, Defense(s).AbsorptionChance);
        Assert.Equal(new[]{id}, Defense(s).AbsorptionItems);
        Assert.Empty(s.State.Effects.Active);
        s.State.Equipment.Unequip(item);
        Assert.Equal(0, Defense(s).AbsorptionChance);
        Equip(s,item);
        Assert.Equal(DaggerfallItemConditionOutcome.Broken,
            s.ItemCondition.Damage(item,s.State.ItemInstances.RequireUnique(id).CurrentCondition).Outcome);
        Assert.Equal(0, Defense(s).AbsorptionChance);
        Assert.DoesNotContain(s.State.Equipment.Read().Assignments,a=>a.Item.EntityId==item.EntityId);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(0,Defense(restored).AbsorptionChance);
    }

    [Fact]
    public void Duplicate_sources_do_not_stack_and_restore_rebuilds_only_current_equipment_alongside_restoration()
    {
        using Fixture f = new(); var s = f.Session;
        var stock = Stock(s); Equip(s,stock);
        var identity = s.UniqueItemAllocator.AllocateReference();
        var definition = TestPayload.Definitions.RequireItem(new DaggerfallItemId("template-121-daedric"));
        var weapon = s.State.Equipment.Materialize(identity,new InventoryItemId(definition.Id.Value));
        s.State.ItemInstances.RegisterDefaultUnique(identity.Value,definition,DaggerfallItemOwner.Player);
        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted,s.ItemCondition.Enchant(weapon,"enchantment.9.-1").Outcome);
        Assert.Equal(EquipmentMoveOutcome.Applied,s.EquipmentMoves.MoveToSlot(weapon,new EquipmentSlotId("right-hand")).Outcome);
        Fund(s,1); s.State.Character.LearnSpell("spell.045");
        s.ReadyPlayerSpell("spell.045"); s.ReleaseReadySpell(1,Vector3.UnitZ);
        Assert.Equal(100,Defense(s).AbsorptionChance);
        Assert.Equal(new[]{Id(s,stock),identity.Value}.Order(),Defense(s).AbsorptionItems);
        var save = s.CaptureSave();
        Assert.Single(DaggerfallSavePayload.Read(save).ActiveEffects);
        using var restored = f.Restore(save);
        Assert.Equal(100,Defense(restored).AbsorptionChance);
        Assert.Equal(Defense(s).AbsorptionItems,Defense(restored).AbsorptionItems);
        foreach(var assignment in restored.State.Equipment.Read().Assignments.ToArray())
            restored.State.Equipment.Unequip(assignment.Item);
        Assert.Equal(15,Defense(restored).AbsorptionChance);
        Assert.Empty(Defense(restored).AbsorptionItems ?? []);
        restored.State.Effects.Cure(Assert.Single(restored.State.Effects.Active).Context.Instance);
        Assert.Equal(0,Defense(restored).AbsorptionChance);
    }

    [Fact]
    public void Normal_incoming_cast_is_consumed_once_with_source_identity_actual_refund_and_HUD_outcome()
    {
        using Fixture f = new(); var s = f.Session; var item = Stock(s); Equip(s,item);
        Fund(s,2000); var magicka = Fund(s,1); magicka.SetCurrent(0);
        double strength = s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value;
        var bundle = Incoming(s);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted,s.Casting.Deliver(bundle,[1]).Outcome);
        Assert.Equal(DaggerfallCastOutcome.Absorbed,Assert.Single(bundle.Results).Outcome);
        var result = Assert.Single(bundle.Absorptions);
        Assert.Equal(1,result.TargetId); Assert.Equal(new[]{Id(s,item)},result.SourceItems);
        Assert.True(result.AdmittedSpellPoints>0);
        Assert.Equal(result.AdmittedSpellPoints,result.RestoredSpellPoints);
        Assert.Equal(result.RestoredSpellPoints,magicka.Current);
        Assert.Equal(strength,s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value);
        Assert.DoesNotContain(s.State.Effects.Active,e=>e.Definition.Key=="drain-strength");
        s.Update(new ProductUpdate(OuterUpdate(1),[]));
        Assert.Contains($"Spell absorbed; restored {result.RestoredSpellPoints:0} magicka",s.Presentation.LastOutcome);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered,s.Casting.Deliver(bundle,[1]).Outcome);
        Assert.Single(bundle.Absorptions); Assert.Equal(result.RestoredSpellPoints,magicka.Current);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(magicka.Current,FundRead(restored,1).Current);
        Assert.Equal(new[]{Id(s,item)},Defense(restored).AbsorptionItems);
        Assert.Empty(restored.State.Effects.Active);
    }

    [Fact]
    public void Whole_effect_capacity_school_and_held_source_rules_are_preserved()
    {
        using Fixture f = new(); var s = f.Session; var item = Stock(s); Equip(s,item);
        Fund(s,2000); var magicka = Fund(s,1); magicka.SetCurrent(0);
        var baseline = Incoming(s); s.Casting.Deliver(baseline,[1]);
        int cost = Assert.Single(baseline.Absorptions).AdmittedSpellPoints;
        magicka.Maximum.BaseValue=cost-1; magicka.SetCurrent(0);
        var tooLarge=Incoming(s); s.Casting.Deliver(tooLarge,[1]);
        Assert.NotEqual(DaggerfallCastOutcome.Absorbed,Assert.Single(tooLarge.Results).Outcome);
        Assert.Empty(tooLarge.Absorptions); Assert.Equal(0,magicka.Current);
        magicka.Maximum.BaseValue=cost; magicka.SetCurrent(0);
        var exact=Incoming(s); s.Casting.Deliver(exact,[1]);
        Assert.Equal(DaggerfallCastOutcome.Absorbed,Assert.Single(exact.Results).Outcome);
        Assert.Equal(cost,magicka.Current);
        Fund(s,1); s.State.Character.LearnSpell("spell.023");
        s.ReadyPlayerSpell("spell.023"); var healing=s.ReleaseReadySpell(1,Vector3.UnitZ).Bundle!;
        Assert.NotEqual(DaggerfallCastOutcome.Absorbed,Assert.Single(healing.Results).Outcome);
        Assert.Empty(healing.Absorptions);
        magicka.SetCurrent(0);
        Assert.Equal(DaggerfallCastOutcome.Ready,s.Casting.Ready(1,"spell.009",Id(s,item),DaggerfallCastSource.ItemHeld).Outcome);
        var held = s.Casting.Release(1,true).Bundle!; s.Casting.Deliver(held,[1]);
        Assert.NotEqual(DaggerfallCastOutcome.Absorbed,Assert.Single(held.Results).Outcome);
        Assert.Empty(held.Absorptions); Assert.Equal(0,magicka.Current);
    }

    [Fact]
    public void Removing_canonical_item_metadata_and_player_death_remove_held_defense_immediately()
    {
        using Fixture f=new(); var s=f.Session; var item=Stock(s); Equip(s,item);
        var health=s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        double before=health.Current; health.SetCurrent(0);
        Assert.Equal(0,Defense(s).AbsorptionChance); health.SetCurrent(before);
        Assert.Equal(100,Defense(s).AbsorptionChance);
        s.State.ItemInstances.RemoveUnique(Id(s,item));
        Assert.Equal(0,Defense(s).AbsorptionChance);
    }

    [Fact]
    public void Ordinary_drop_transfers_the_same_item_and_destroying_the_source_clears_defense()
    {
        using Fixture f=new(); var s=f.Session; var item=Stock(s); Equip(s,item); ulong id=Id(s,item);
        s.State.PlayerControl.MoveTo(new Vector3(10,0,10));
        s.PublishInitial();
        f.Submit(new {action="inventory-drop",revision=f.Engine.PublishedNested("inventory","revision"),item=$"unique:{item.EntityId}",amount=1});
        Assert.Equal(100,Defense(s).AbsorptionChance); // ordinary caller refuses dropping a worn item
        Assert.Equal(EquipmentMoveOutcome.Applied,s.EquipmentMoves.MoveToGrid(item,49).Outcome); s.PublishInitial();
        f.Submit(new {action="inventory-drop",revision=f.Engine.PublishedNested("inventory","revision"),item=$"unique:{item.EntityId}",amount=1});
        Assert.Equal(0,Defense(s).AbsorptionChance);
        Assert.True(f.Engine.PublishedNested("inventory","message").Contains("Dropped"),f.Engine.PublishedNested("inventory","message"));
        Assert.Equal(DaggerfallItemOwner.Ground(Assert.Single(DaggerfallSavePayload.Read(s.CaptureSave()).GroundContainers).Id),
            s.State.ItemInstances.RequireUnique(id).Owner);
        using var restored=f.Restore(s.CaptureSave()); Assert.Equal(0,Defense(restored).AbsorptionChance);
        var second=Stock(s); Equip(s,second); ulong secondId=Id(s,second);
        Assert.Throws<MechanicsException>(()=>s.State.Inventory.Destroy(second));
        s.State.Equipment.Unequip(second);
        s.State.Inventory.Destroy(second);
        s.State.ItemInstances.RemoveUnique(secondId);
        Assert.Equal(0,Defense(s).AbsorptionChance);
    }

    private static DaggerfallMagicDefense Defense(DaggerfallSession s)=>s.State.Effects.MagicDefenseFor(1);
    private static ulong Id(DaggerfallSession s,UniqueInventoryItem item)=>s.State.Equipment.GetDurableItemId(new EntityId(item.EntityId)).Value;
    private static UniqueInventoryItem Stock(DaggerfallSession s)
    {
        var created=new DaggerfallItemFactory(TestPayload.Definitions,RandomMinimum.Create())
            .Create(new("Magic","stock-absorption",DaggerfallItemOwner.Player,Race:"breton",Gender:"male",MagicItemKey:"magic-item.0022"));
        var identity=s.UniqueItemAllocator.AllocateReference();
        var item=s.State.Equipment.Materialize(identity,created.Item);
        s.State.ItemInstances.RegisterUnique(identity.Value,created.Metadata); return item;
    }
    private static void Equip(DaggerfallSession s,UniqueInventoryItem item)
    {
        var definition=TestPayload.Definitions.RequireItem(new DaggerfallItemId(item.Definition.Value));
        var slot=TestPayload.Definitions.EquipmentSlots.Values.First(v=>v.AllowedClassifications.Intersect(definition.Equipment!.Classifications).Any());
        var moved=s.EquipmentMoves.MoveToSlot(item,new EquipmentSlotId(slot.Id.Value));
        Assert.True(moved.Outcome==EquipmentMoveOutcome.Applied,moved.Detail);
    }
    private static DaggerfallLiveSpell Incoming(DaggerfallSession s)
    {
        Assert.Equal(DaggerfallCastOutcome.Ready,s.Casting.Ready(2000,"spell.009").Outcome);
        return s.Casting.Release(2000,true).Bundle!;
    }
    private static Track FundRead(DaggerfallSession s,long id)=>(id==1?s.State.Actors.Player.Stats:s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse("magicka"));
    private static Track Fund(DaggerfallSession s,long id)
    { var track=FundRead(s,id);track.Maximum.BaseValue=10000;track.SetCurrent(10000);return track; }
    private sealed class Fixture:IDisposable
    {
        private readonly DaggerfallSessionComposition composition=new(TestPayload.Definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);
        internal DaggerfallSession Session{get;}
        internal EngineContextFake Engine{get;}
        private ulong step;
        internal Fixture()
        {
            Engine=CreateEngine();
            Session=DaggerfallSession.StartNew(Engine.Context,composition);
            Session.State.Character.BeginChoices();
            Session.State.Character.ReplacePending(Session.State.Character.ReadCreation().Current with
                {CareerId="class16",CustomCareer=null,Background=null});
            Session.State.Character.CommitChoices();
        }
        private EngineContextFake CreateEngine()
        {
            List<string> releases=[]; ContentFake content=new(releases);PopulateContent(content,composition.StartSite);
            return EngineContextFake.Create(content,SpatialFake.Create(composition.StartSite.SpatialArtifact.Sha256,releases).Service,
                new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload save)=>DaggerfallSession.Restore(CreateEngine().Context,composition,save);
        internal void Submit(object value)=>Session.Update(new ProductUpdate(OuterUpdate(++step),[Ui(JsonSerializer.Serialize(value))]));
        public void Dispose()=>Session.Dispose();
    }
}
