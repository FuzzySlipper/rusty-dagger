using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ItemMakerSessionTests
{
    [Fact]
    public void Paid_item_retains_identity_and_condition_and_activates_callbacks_after_equip_and_restore()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session; var provider = Provider(s);
        Fund(f); var (item,id) = ItemEnchantmentMutationTests.Weapon(f);
        s.State.Character.BeginChoices();s.State.Character.ReplacePending(s.State.Character.ReadCreation().Current with { CareerId="class16",CustomCareer=null,Background=null });s.State.Character.CommitChoices();
        int skill=s.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt;
        s.ItemMaker.SetDraft(new("unique:"+id,"Listener",["enchantment.10.29","enchantment.13.0","enchantment.23.-1"]));
        var quote=s.ItemMaker.Quote(provider)!;Assert.True(quote.Eligible,quote.Reason);var before=s.State.Currency.Read().Gold;
        Assert.Equal("ConfirmationRequired",s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold,false).Outcome);
        Assert.Equal("PriceChanged",s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold+1,true).Outcome);
        var made=s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold,true);Assert.True(made.Accepted,made.Outcome);Assert.Equal(id,made.Item);
        Assert.Equal(before-(ulong)quote.Gold,s.State.Currency.Read().Gold);Assert.Equal(1,s.State.ItemInstances.RequireUnique(id).CurrentCondition);
        Assert.Equal("DraftChanged",s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold,true).Outcome);
        Assert.Equal(EquipmentMoveOutcome.Applied,s.EquipmentMoves.MoveToSlot(item,new("right-hand")).Outcome);
        Assert.Equal(skill+15,s.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);Assert.True(s.State.HeldEnchantments.Talents.AcuteHearing);
        using var restored=f.Restore();Assert.Equal("Listener",restored.State.ItemInstances.RequireUnique(id).MadeEnchantment!.Name);
        Assert.True(restored.State.HeldEnchantments.Talents.AcuteHearing);
        var worn=restored.State.Equipment.Read().Assignments.First(value=>restored.State.Equipment.GetDurableItemId(new(value.Item.EntityId)).Value==id).Item;
        restored.ItemCondition.Damage(worn,1);Assert.False(restored.State.HeldEnchantments.Talents.AcuteHearing);
        Assert.Equal(skill,restored.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
    }

    [Fact]
    public void Soul_bound_gem_consumes_one_unit_and_one_soul_and_preserves_remaining_stack_and_restore()
    {
        using var f=new SanguineRoseSessionTests.Fixture();var s=f.Session;var provider=Provider(s);Fund(f);
        var factory=new DaggerfallItemFactory(TestPayload.Definitions,f.Engine.Context.Random);var stack=InventoryStackId.Parse("maker.rubies");
        factory.Materialize(factory.Create(new("Gems","maker.rubies",DaggerfallItemOwner.Player,Quantity:3,TemplateIndex:0)),s.State.Inventory,s.State.ItemInstances,stack);
        ulong soul=ItemEnchantmentMutationTests.Gem(f,18);var original=s.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player,stack);
        s.ItemMaker.SetDraft(new("stack:"+stack.Value,"Ghost ruby",["enchantment.15.18"]));
        var quote=s.ItemMaker.Quote(provider)!;Assert.True(quote.Eligible,quote.Reason);Assert.Equal(-300,quote.Power);Assert.Equal(1000,quote.Gold);Assert.Equal(4,quote.Payloads.Length);
        var made=s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold,true);Assert.True(made.Accepted,made.Outcome);
        Assert.False(s.State.ItemInstances.ContainsUnique(soul));Assert.Equal(2UL,s.State.Inventory.Read().Stacks.Single(value=>value.Id==stack).Quantity);
        Assert.Equal(original,s.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player,stack));
        var enchanted=s.State.ItemInstances.RequireUnique(made.Item!.Value);Assert.Equal("template-0-enchantable",enchanted.ItemId);Assert.Equal(100UL,enchanted.WeightClassicUnits);
        Assert.Equal(4,enchanted.MadeEnchantment!.Settings.Length);
        using var restored=f.Restore();Assert.Equal(enchanted,restored.State.ItemInstances.RequireUnique(made.Item.Value) with { MadeEnchantment=enchanted.MadeEnchantment });
        Assert.Equal(2UL,restored.State.Inventory.Read().Stacks.Single(value=>value.Id==stack).Quantity);
    }

    [Fact]
    public void Invalid_power_conflicts_missing_funds_and_changed_provider_leave_items_and_souls_untouched()
    {
        using var f=new SanguineRoseSessionTests.Fixture();var s=f.Session;var provider=Provider(s);var (_,id)=ItemEnchantmentMutationTests.Weapon(f);
        s.State.Currency.TrySpendGold(s.State.Currency.Read().Gold,[]);var original=s.State.ItemInstances.RequireUnique(id);ulong soul=ItemEnchantmentMutationTests.Gem(f,18);
        s.ItemMaker.SetDraft(new("unique:"+id,"Ghost",["enchantment.15.18"]));var quote=s.ItemMaker.Quote(provider)!;
        Assert.Equal("InsufficientFunds",s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold,true).Outcome);
        Assert.Equal(original,s.State.ItemInstances.RequireUnique(id));Assert.True(s.State.ItemInstances.ContainsUnique(soul));
        s.ItemMaker.SetDraft(new("unique:"+id,"Conflict",["enchantment.11.-1","enchantment.23.-1"]));Assert.False(s.ItemMaker.Quote(provider)!.Eligible);
        s.ItemMaker.SetDraft(new("unique:"+id,"Too much",["enchantment.10.0","enchantment.10.1","enchantment.10.2","enchantment.10.3","enchantment.10.4","enchantment.10.5","enchantment.10.6","enchantment.10.7","enchantment.10.8","enchantment.10.9"]));Assert.False(s.ItemMaker.Quote(provider)!.Eligible);
        s.State.Social.ExpelGuild(40);s.ItemMaker.SetDraft(new("unique:"+id,"Ghost",["enchantment.15.18"]));Assert.False(s.ItemMaker.Quote(provider)!.Eligible);
        Assert.Equal(original,s.State.ItemInstances.RequireUnique(id));Assert.True(s.State.ItemInstances.ContainsUnique(soul));
    }

    [Fact]
    public void Enchanted_stack_gem_applies_extra_weight_once_and_held_power_through_crystal_slot()
    {
        using var f=new SanguineRoseSessionTests.Fixture();var s=f.Session;var provider=Provider(s);Fund(f);
        var factory=new DaggerfallItemFactory(TestPayload.Definitions,f.Engine.Context.Random);var stack=InventoryStackId.Parse("maker.crystal");
        factory.Materialize(factory.Create(new("Gems",stack.Value,DaggerfallItemOwner.Player,Quantity:2,TemplateIndex:0)),s.State.Inventory,s.State.ItemInstances,stack);
        ulong weight=DaggerfallEncumbrancePolicy.ClassicWeightCost(TestPayload.Definitions.RequireItem(new("template-0")));
        s.ItemMaker.SetDraft(new("stack:"+stack.Value,"Listening crystal",["enchantment.13.0","enchantment.23.-1"]));
        var quote=s.ItemMaker.Quote(provider)!;Assert.True(quote.Eligible,quote.Reason);var purchase=s.ItemMaker.Buy(provider,quote.Key,(ulong)quote.Gold,true);Assert.True(purchase.Accepted,purchase.Outcome);
        var metadata=s.State.ItemInstances.RequireUnique(purchase.Item!.Value);Assert.Equal(weight*4,metadata.WeightClassicUnits);
        var row=s.State.Inventory.Read().UniqueItems.Single(value=>s.State.Inventory.GetDurableItemId(value.Entity).Value==purchase.Item.Value);
        var item=new WorldRpg.Kit.Inventory.UniqueInventoryItem(row.Entity.Value,new(row.Definition.Value));
        Assert.Equal(EquipmentMoveOutcome.Applied,s.EquipmentMoves.MoveToSlot(item,new("crystal0")).Outcome);Assert.True(s.State.HeldEnchantments.Talents.AcuteHearing);
        using var restored=f.Restore();Assert.True(restored.State.HeldEnchantments.Talents.AcuteHearing);Assert.Equal(weight*4,restored.State.ItemInstances.RequireUnique(purchase.Item.Value).WeightClassicUnits);
    }

    [Fact]
    public void Source_allows_a_free_disadvantage_only_item_and_does_not_charge_or_reapply_it()
    {
        using var f=new SanguineRoseSessionTests.Fixture();var s=f.Session;var provider=Provider(s);var (_,id)=ItemEnchantmentMutationTests.Weapon(f);
        s.State.Currency.TrySpendGold(s.State.Currency.Read().Gold,[]);s.ItemMaker.SetDraft(new("unique:"+id,"Heavy blade",["enchantment.23.-1"]));
        var quote=s.ItemMaker.Quote(provider)!;Assert.True(quote.Eligible,quote.Reason);Assert.Equal(0,quote.Gold);Assert.True(quote.Power<0);
        var result=s.ItemMaker.Buy(provider,quote.Key,0,true);Assert.True(result.Accepted,result.Outcome);Assert.Equal(0UL,s.State.Currency.Read().Gold);
        Assert.NotNull(s.State.ItemInstances.RequireUnique(id).MadeEnchantment);Assert.Equal("DraftChanged",s.ItemMaker.Buy(provider,quote.Key,0,true).Outcome);
    }

    [Fact]
    public void Visible_zero_gold_purchase_is_admitted_and_forged_derived_save_values_are_rejected()
    {
        var action = Presentation.DaggerfallUiAction.Parse(System.Text.Encoding.UTF8.GetBytes(
            "{\"action\":\"itemmaker-buy\",\"revision\":\"dialogue.1\",\"key\":\"1\",\"amount\":0,\"confirm\":true}"));
        Assert.NotNull(action); Assert.Equal(0UL, action.Amount);
        using var f = new SanguineRoseSessionTests.Fixture(); var s=f.Session; var provider=Provider(s);
        var (_,id)=ItemEnchantmentMutationTests.Weapon(f);
        s.ItemMaker.SetDraft(new("unique:"+id,"Heavy blade",["enchantment.23.-1"])); var quote=s.ItemMaker.Quote(provider)!;
        Assert.True(s.ItemMaker.Buy(provider,quote.Key,0,true).Accepted);
        var original=s.State.ItemInstances.RequireUnique(id);
        s.State.ItemInstances.ReplaceUnique(id,original with { MadeEnchantment=original.MadeEnchantment! with { Value=original.MadeEnchantment.Value+1 } });
        Assert.Throws<ArgumentException>(()=>f.Restore());
        s.State.ItemInstances.ReplaceUnique(id,original with { WeightClassicUnits=original.WeightClassicUnits+1 });
        Assert.Throws<ArgumentException>(()=>f.Restore());
        s.State.ItemInstances.ReplaceUnique(id,original); using var restored=f.Restore();
        Assert.Equal(original.WeightClassicUnits,restored.State.ItemInstances.RequireUnique(id).WeightClassicUnits);
    }

    private static DaggerfallServiceProvider Provider(DaggerfallSession s)
    {
        s.State.Social.JoinGuild(40,0);for(int rank=0;rank<5;rank++)s.State.Social.PromoteGuild(40,0);
        var site=s.Site.ActiveSite!;var location=new DaggerfallNpcSite(site.Id.Region,site.Name,string.Empty);
        long id=s.State.Npcs.RegisterStable(DaggerfallNpcKind.Static,"test-itemmaker",location,new("Breton","Male",0,0,0,802),"Enchanter",["talk","make-magic-items"]);
        return new(id,location,"make-magic-items");
    }
    private static void Fund(SanguineRoseSessionTests.Fixture f)
    {
        var factory=new DaggerfallItemFactory(TestPayload.Definitions,f.Engine.Context.Random);
        factory.Materialize(factory.Create(new("Currency","maker.gold",DaggerfallItemOwner.Player,Quantity:1_000_000,TemplateIndex:276)),f.Session.State.Inventory,f.Session.State.ItemInstances,InventoryStackId.Parse("maker.gold"));
    }
}
