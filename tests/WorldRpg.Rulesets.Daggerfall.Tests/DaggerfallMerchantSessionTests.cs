using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallMerchantSessionTests
{
    [Fact]
    public void Stack_projection_publishes_one_unit_price_used_by_a_single_item_purchase()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMaximum.Create());
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(6 * 60 * 60);
        DaggerfallServiceProvider provider = OpenProvider(session, faction: 0, service: "shop", "shop");
        DaggerfallMerchantProviderContext context = Context(provider, buildingType: 13);
        AddGold(session, 100_000);
        AddArrows(session, 10);

        DaggerfallMerchantView beforeSale = session.State.Merchants.Read(context);
        DaggerfallMerchantResult sold = session.State.Merchants.Sell(context, beforeSale.Revision, "stack:merchant.test.arrows", 4);
        Assert.True(sold.Accepted, sold.Outcome);
        DaggerfallMerchantView afterSale = session.State.Merchants.Read(context);
        DaggerfallMerchantItemView row = Assert.Single(afterSale.Stock,
            item => item.Key.Contains(".acquired.", StringComparison.Ordinal) && item.Definition == "template-131");

        DaggerfallMerchantResult bought = session.State.Merchants.Buy(context, afterSale.Revision, row.Key, 1);
        Assert.True(bought.Accepted, bought.Outcome);
        Assert.Equal(row.UnitPrice, bought.PaidGold);
        Assert.Equal(3UL, session.State.Merchants.Read(context).Stock.Single(item => item.Key == row.Key).Quantity);
    }

    [Fact]
    public void Actual_trade_caller_supports_partial_stolen_and_payment_failure_without_losing_stock_on_restock_or_save()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMaximum.Create());
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(6 * 60 * 60);
        DaggerfallServiceProvider provider = OpenProvider(session, faction: 0, service: "shop", "shop");
        DaggerfallMerchantProviderContext context = Context(provider, buildingType: 13);
        AddGold(session, 100_000);
        AddArrows(session, 10);

        DaggerfallMerchantView initial = session.State.Merchants.Read(context);
        ulong initialGold = session.State.Currency.Read().Gold;
        DaggerfallMerchantResult sold = session.State.Merchants.Sell(context, initial.Revision, "stack:merchant.test.arrows", 4);
        Assert.True(sold.Accepted, sold.Outcome);
        Assert.Equal("Sold", sold.Outcome);
        Assert.Equal(6UL, StackQuantity(session, "merchant.test.arrows"));
        DaggerfallMerchantView afterSale = session.State.Merchants.Read(context);
        DaggerfallMerchantItemView acquired = Assert.Single(afterSale.Stock,
            item => item.Key.Contains(".acquired.", StringComparison.Ordinal) && item.Definition == "template-131");
        Assert.Equal(4UL, acquired.Quantity);
        Assert.True(session.State.Currency.Read().Gold > initialGold);

        ulong goldBeforePurchase = session.State.Currency.Read().Gold;
        DaggerfallMerchantResult bought = session.State.Merchants.Buy(context, afterSale.Revision, acquired.Key, 2);
        Assert.True(bought.Accepted, bought.Outcome);
        Assert.Equal("Purchased", bought.Outcome);
        Assert.Equal(goldBeforePurchase - bought.PaidGold, session.State.Currency.Read().Gold);
        Assert.Equal(2UL, session.State.Merchants.Read(context).Stock.Single(item => item.Key == acquired.Key).Quantity);
        Assert.Equal(8UL, PlayerArrowQuantity(session));

        session.State.Actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).BaseValue = 100;
        DaggerfallMerchantView beforeTheft = session.State.Merchants.Read(context);
        DaggerfallMerchantItemView stealable = beforeTheft.Stock.Single(item => item.Key == acquired.Key);
        DaggerfallMerchantResult stolen = session.State.Merchants.Shoplift(context, beforeTheft.Revision, stealable.Key, 1);
        Assert.True(stolen.Accepted, stolen.Outcome);
        Assert.Equal("Stolen", stolen.Outcome);
        Assert.Equal(1, session.State.Crime.Attempts.Count);
        Assert.Equal(DaggerfallCrimeAttemptOutcome.PropertyTransferred, session.State.Crime.Attempts[0].Outcome);
        InventoryStack stolenStack = Assert.Single(session.State.Inventory.Read().Stacks,
            stack => stack.Id.Value.StartsWith("daggerfall.player.stolen.", StringComparison.Ordinal));
        Assert.True(session.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stolenStack.Id).Stolen);

        ulong gold = session.State.Currency.Read().Gold;
        Assert.True(session.State.Currency.TrySpendGold(gold, []));
        DaggerfallMerchantView beforeFailedBuy = session.State.Merchants.Read(context);
        ulong stockBeforeFailedBuy = beforeFailedBuy.Stock.Single(item => item.Key == acquired.Key).Quantity;
        DaggerfallMerchantResult failedBuy = session.State.Merchants.Buy(context, beforeFailedBuy.Revision, acquired.Key, 1);
        Assert.False(failedBuy.Accepted);
        Assert.Equal("InsufficientFunds", failedBuy.Outcome);
        Assert.Equal(0UL, session.State.Currency.Read().Gold);
        Assert.Equal(stockBeforeFailedBuy, session.State.Merchants.Read(context).Stock.Single(item => item.Key == acquired.Key).Quantity);

        DaggerfallSavePayload beforeRestock = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallMerchantSave savedBinding = Assert.Single(beforeRestock.Merchants);
        Assert.Contains(savedBinding.Inventory.Stacks, item => item.StackId.Contains(".acquired.", StringComparison.Ordinal));
        session.AdvanceElapsedTime(24 * 60 * 60);
        DaggerfallMerchantView afterRestock = session.State.Merchants.Read(context);
        DaggerfallMerchantSave restockedBinding = Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).Merchants);
        Assert.True(restockedBinding.StockedDay > savedBinding.StockedDay);
        Assert.Contains(afterRestock.Stock, item => item.Key == acquired.Key && item.Quantity == 1);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        OpenExistingProvider(restored, provider);
        DaggerfallMerchantView restoredView = restored.State.Merchants.Read(context);
        Assert.Equal(0UL, restored.State.Currency.Read().Gold);
        Assert.Contains(restoredView.Stock, item => item.Key == acquired.Key && item.Quantity == 1);
        Assert.Equal(8UL, StackQuantity(restored, "merchant.test.arrows"));
        Assert.Contains(restored.State.Inventory.Read().Stacks,
            stack => stack.Id.Value.StartsWith("daggerfall.player.stolen.", StringComparison.Ordinal));
    }

    [Fact]
    public void Fighters_repair_uses_canonical_membership_provider_rank_reduction_and_custody_before_and_after_due()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMaximum.Create());
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(6 * 60 * 60);
        int repairProviderFaction = DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.FightersFactionId)
            .Services.Single(service => service.Service == DaggerfallConcreteGuildService.Repair).ProviderFactionId!.Value;
        DaggerfallServiceProvider provider = OpenProvider(session, repairProviderFaction, "repair", "repair");
        // The repair provider is faction 850, while the Fighters Guild membership is faction 41.
        DaggerfallMerchantProviderContext context = Context(provider, buildingType: 2);
        DurableIdentityReference itemId = AddDamagedWeapon(session);
        string itemKey = "unique:" + itemId.Value;
        AddGold(session, 100_000);

        DaggerfallMerchantView initial = session.State.Merchants.Read(context);
        Assert.True(initial.CanRepair);
        Assert.False(initial.CanSell);
        Assert.Contains(initial.PlayerItems, item => item.Key == itemKey);
        DaggerfallMerchantResult refused = session.State.Merchants.RequestRepair(context, initial.Revision, itemKey);
        Assert.False(refused.Accepted);
        Assert.Equal("NotMember", refused.Outcome);
        Assert.Contains(session.State.Inventory.Read().UniqueItems, item => item.Entity.Value == ResolveEntity(session, itemId));

        _ = session.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.FightersFactionId, 0);
        DaggerfallMerchantView admitted = session.State.Merchants.Read(context);
        ulong beforeGold = session.State.Currency.Read().Gold;
        DaggerfallMerchantResult accepted = session.State.Merchants.RequestRepair(context, admitted.Revision, itemKey);
        Assert.True(accepted.Accepted, accepted.Outcome);
        Assert.Equal("RepairAccepted", accepted.Outcome);
        Assert.True(beforeGold > accepted.PaidGold);
        Assert.DoesNotContain(session.State.Inventory.Read().UniqueItems, item => item.Entity.Value == ResolveEntity(session, itemId));
        DaggerfallMerchantView pending = session.State.Merchants.Read(context);
        DaggerfallRepairView repair = Assert.Single(pending.Repairs);
        DaggerfallMerchantSave custodyBeforeDue = Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).Merchants);
        Assert.Contains(custodyBeforeDue.Custody.UniqueItems, item => item.EntityId == itemId.Value);

        DaggerfallMerchantResult tooSoon = session.State.Merchants.CollectRepair(context, pending.Revision, repair.RequestId);
        Assert.False(tooSoon.Accepted);
        Assert.Equal("RepairNotReady", tooSoon.Outcome);
        Assert.Contains(session.State.Merchants.Read(context).Repairs, item => item.RequestId == repair.RequestId);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        OpenExistingProvider(restored, provider);
        restored.AdvanceElapsedTime(2 * DaggerfallCalendar.SecondsPerDay);
        DaggerfallMerchantView ready = restored.State.Merchants.Read(context);
        DaggerfallRepairView restoredRepair = Assert.Single(ready.Repairs);
        Assert.True(restoredRepair.Ready);
        DaggerfallMerchantResult collected = restored.State.Merchants.CollectRepair(context, ready.Revision, restoredRepair.RequestId);
        Assert.True(collected.Accepted, collected.Outcome);
        Assert.Equal("RepairCollected", collected.Outcome);
        Assert.Empty(restored.State.Merchants.Read(context).Repairs);
        Assert.Contains(restored.State.Inventory.Read().UniqueItems, item => item.Entity.Value == ResolveEntity(restored, itemId));
        Assert.Equal(restored.State.ItemInstances.RequireUnique(itemId.Value).MaximumCondition,
            restored.State.ItemInstances.RequireUnique(itemId.Value).CurrentCondition);
        Assert.Equal("RepairUnavailable", restored.State.Merchants.CollectRepair(context,
            restored.State.Merchants.Read(context).Revision, restoredRepair.RequestId).Outcome);

        DurableIdentityReference rankedItem = AddDamagedWeapon(restored);
        _ = restored.State.Social.PromoteGuild(DaggerfallConcreteGuildCatalog.FightersFactionId, 0);
        DaggerfallMerchantView rankedView = restored.State.Merchants.Read(context);
        DaggerfallMerchantResult ranked = restored.State.Merchants.RequestRepair(context, rankedView.Revision,
            "unique:" + rankedItem.Value);
        Assert.True(ranked.Accepted, ranked.Outcome);
        Assert.True(ranked.PaidGold < accepted.PaidGold);
    }

    [Fact]
    public void Mages_identify_requires_the_named_provider_and_mutates_one_unique_once_then_survives_save()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMaximum.Create());
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(6 * 60 * 60);
        AddGold(session, 100_000);
        DurableIdentityReference itemId = AddUnidentifiedMagic(session);
        string itemKey = "unique:" + itemId.Value;

        DaggerfallServiceProvider wrongProvider = OpenProvider(session, 0, "identify", "identify");
        DaggerfallMerchantProviderContext wrongContext = Context(wrongProvider, buildingType: 12);
        DaggerfallMerchantView wrongView = session.State.Merchants.Read(wrongContext);
        Assert.True(wrongView.CanIdentify);
        Assert.False(wrongView.CanSell);
        Assert.Contains(wrongView.PlayerItems, item => item.Key == itemKey);
        ulong beforeWrongProvider = session.State.Currency.Read().Gold;
        DaggerfallMerchantResult wrong = session.State.Merchants.Identify(wrongContext, wrongView.Revision, itemKey);
        Assert.False(wrong.Accepted);
        Assert.Equal("ProviderUnavailable", wrong.Outcome);
        Assert.Equal(beforeWrongProvider, session.State.Currency.Read().Gold);
        Assert.False(session.State.ItemInstances.RequireUnique(itemId.Value).Identified);

        DaggerfallServiceProvider provider = OpenProvider(session, 801, "identify", "identify");
        DaggerfallMerchantProviderContext context = Context(provider, buildingType: 12);

        DaggerfallMerchantView initial = session.State.Merchants.Read(context);
        ulong beforeGold = session.State.Currency.Read().Gold;
        DaggerfallMerchantResult identified = session.State.Merchants.Identify(context, initial.Revision, itemKey);
        Assert.True(identified.Accepted, identified.Outcome);
        Assert.Equal("Identified", identified.Outcome);
        Assert.Equal(beforeGold - identified.PaidGold, session.State.Currency.Read().Gold);
        Assert.True(session.State.ItemInstances.RequireUnique(itemId.Value).Identified);
        DaggerfallMerchantResult repeated = session.State.Merchants.Identify(context,
            session.State.Merchants.Read(context).Revision, itemKey);
        Assert.False(repeated.Accepted);
        Assert.Equal("AlreadyIdentified", repeated.Outcome);
        Assert.Equal(beforeGold - identified.PaidGold, session.State.Currency.Read().Gold);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        OpenExistingProvider(restored, provider);
        Assert.True(restored.State.ItemInstances.RequireUnique(itemId.Value).Identified);
        Assert.Equal("AlreadyIdentified", restored.State.Merchants.Identify(context,
            restored.State.Merchants.Read(context).Revision, itemKey).Outcome);
    }

    [Fact]
    public void Identify_returns_item_unavailable_after_the_real_repair_custody_transfer()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMaximum.Create());
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(6 * 60 * 60);
        AddGold(session, 100_000);
        DurableIdentityReference itemId = AddUnidentifiedMagic(session);
        DaggerfallItemInstanceMetadata metadata = session.State.ItemInstances.RequireUnique(itemId.Value);
        session.State.ItemInstances.ReplaceUnique(itemId.Value,
            metadata with { CurrentCondition = Math.Max(1, metadata.MaximumCondition / 2) });
        string itemKey = "unique:" + itemId.Value;

        DaggerfallServiceProvider magesProvider = OpenProvider(session, 801, "identify", "identify");
        DaggerfallMerchantProviderContext identifyContext = Context(magesProvider, buildingType: 12);
        DaggerfallMerchantView identifyView = session.State.Merchants.Read(identifyContext);
        Assert.True(identifyView.CanIdentify);
        Assert.False(identifyView.CanSell);
        Assert.Contains(identifyView.PlayerItems, item => item.Key == itemKey && !item.Identified);
        ulong beforeMoveGold = session.State.Currency.Read().Gold;

        int repairProviderFaction = DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.FightersFactionId)
            .Services.Single(service => service.Service == DaggerfallConcreteGuildService.Repair).ProviderFactionId!.Value;
        DaggerfallServiceProvider fightersProvider = OpenProvider(session, repairProviderFaction, "repair", "repair");
        _ = session.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.FightersFactionId, 0);
        DaggerfallMerchantProviderContext repairContext = Context(fightersProvider, buildingType: 2);
        DaggerfallMerchantView repairView = session.State.Merchants.Read(repairContext);
        DaggerfallMerchantResult repair = session.State.Merchants.RequestRepair(repairContext, repairView.Revision, itemKey);
        Assert.True(repair.Accepted, repair.Outcome);
        Assert.DoesNotContain(session.State.Inventory.Read().UniqueItems, item => item.Entity.Value == ResolveEntity(session, itemId));

        DaggerfallMerchantResult moved = session.State.Merchants.Identify(identifyContext, identifyView.Revision, itemKey);
        Assert.False(moved.Accepted);
        Assert.Equal("ItemUnavailable", moved.Outcome);
        Assert.Equal(beforeMoveGold - repair.PaidGold, session.State.Currency.Read().Gold);
        Assert.False(session.State.ItemInstances.RequireUnique(itemId.Value).Identified);
        DaggerfallMerchantSave save = Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).Merchants,
            merchant => merchant.Key == repairContext.Key);
        Assert.Contains(save.Custody.UniqueItems, item => item.EntityId == itemId.Value);
    }

    private static DaggerfallMerchantProviderContext Context(DaggerfallServiceProvider provider, int buildingType) =>
        new(provider, Quality: 10, BuildingType: buildingType, BlockX: 1, BlockY: 1, BuildingIndex: 1);

    private static DaggerfallServiceProvider OpenProvider(DaggerfallSession session, int faction, string service,
        params string[] services)
    {
        DaggerfallSiteRecord site = session.Site.ActiveSite ?? throw new InvalidOperationException("No active fixture site.");
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty);
        string[] offered = ["talk", .. services.Distinct(StringComparer.Ordinal)];
        long id = session.State.Npcs.RegisterCivilian(npcSite,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, faction), "merchant test provider", offered);
        session.MaterializeNpcActor(id, new ActorPose(session.State.PlayerControl.Position!.Value, 0));
        DaggerfallActivationTarget target = session.Dialogue.NpcTargets().Single(value => value.Identity.Value == (ulong)id);
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        return new(id, npcSite, service);
    }

    private static void OpenExistingProvider(DaggerfallSession session, DaggerfallServiceProvider provider)
    {
        DaggerfallActivationTarget target = session.Dialogue.NpcTargets().Single(value => value.Identity.Value == (ulong)provider.NpcId);
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
    }

    private static void AddGold(DaggerfallSession session, ulong amount)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "merchant.test.gold", DaggerfallItemOwner.Player,
            Quantity: amount, TemplateIndex: 276)), session.State.Inventory, session.State.ItemInstances,
            InventoryStackId.Parse("merchant.test.gold"));
    }

    private static void AddArrows(DaggerfallSession session, ulong quantity)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Weapons", "merchant.test.arrows", DaggerfallItemOwner.Player,
            Quantity: quantity, TemplateIndex: 131)), session.State.Inventory, session.State.ItemInstances,
            InventoryStackId.Parse("merchant.test.arrows"));
    }

    private static ulong StackQuantity(DaggerfallSession session, string stackId) =>
        session.State.Inventory.Read().Stacks.Single(stack => stack.Id == InventoryStackId.Parse(stackId)).Quantity;

    private static ulong PlayerArrowQuantity(DaggerfallSession session) =>
        session.State.Inventory.Read().Stacks.Where(stack => stack.Definition.Value == "template-131")
            .Aggregate(0UL, (total, stack) => checked(total + stack.Quantity));

    private static DurableIdentityReference AddDamagedWeapon(DaggerfallSession session)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        DaggerfallCreatedItem created = factory.Create(new("Weapons", "merchant.test.repair", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: "iron"));
        DurableIdentityReference id = session.UniqueItemAllocator.AllocateReference();
        factory.Materialize(created, session.State.Inventory, session.State.ItemInstances, unique: id);
        DaggerfallItemInstanceMetadata metadata = session.State.ItemInstances.RequireUnique(id.Value);
        session.State.ItemInstances.ReplaceUnique(id.Value,
            metadata with { CurrentCondition = Math.Max(1, metadata.MaximumCondition / 2) });
        return id;
    }

    private static DurableIdentityReference AddUnidentifiedMagic(DaggerfallSession session)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        DaggerfallCreatedItem created = factory.Create(new("Magic", "merchant.test.identify", DaggerfallItemOwner.Player,
            Race: "breton", Gender: "male", MagicItemKey: "magic-item.0022"));
        DurableIdentityReference id = session.UniqueItemAllocator.AllocateReference();
        factory.Materialize(created, session.State.Inventory, session.State.ItemInstances, unique: id);
        session.State.ItemInstances.ReplaceUnique(id.Value,
            session.State.ItemInstances.RequireUnique(id.Value) with { Identified = false });
        return id;
    }

    private static ulong ResolveEntity(DaggerfallSession session, DurableIdentityReference identity) =>
        checked((ulong)session.State.Containers.Entities.Resolve(identity).Value);
}
