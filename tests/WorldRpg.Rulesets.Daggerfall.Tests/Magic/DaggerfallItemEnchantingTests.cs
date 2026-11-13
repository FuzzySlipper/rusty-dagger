using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Persistence;
using System.Reflection;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using Fixture = WorldRpg.Rulesets.Daggerfall.Tests.DaggerfallItemConditionServiceTests.Fixture;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Enchanting a live item, by the enchanting owner over the condition tests' inventories, without the session.</summary>
public sealed class DaggerfallItemEnchantingTests
{
    [Fact]
    public void Enchanting_a_plain_factory_item_unequips_then_persists_magic_value_condition_and_projection()
    {
        using Fixture f = new();
        const string magicKey = "magic-item.0010";
        DaggerfallMagicItemDefinition magic = f.Definitions.Magic.MagicItems[magicKey];
        UniqueItem sword = f.CreatePlainWeapon(404, 115, "daedric");
        string plainItemId = f.Instances.RequireUnique(404).ItemId;
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(sword, new SlotId("right-hand")).Outcome);
        List<DaggerfallEquipmentChange> changes = [];
        f.Moves.Changed += changes.Add;

        DaggerfallItemConditionResult result = f.Enchanting.Enchant(sword, magicKey);
        InventoryItemPresentation row = Assert.Single(f.Presentation.Read().Items);
        DaggerfallItemInstanceMetadata restored = DaggerfallItemInstanceMetadata.Restore(result.Metadata.ItemId, result.Metadata.Capture());

        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, result.Outcome);
        Assert.Equal((plainItemId, magicKey, true, magic.Uses),
            (result.Metadata.ItemId, result.Metadata.Enchantment, result.Metadata.Identified, result.Metadata.MaximumCondition));
        Assert.Equal(result.EquipmentChange, Assert.Single(changes));
        Assert.DoesNotContain(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
        Assert.Equal((magic.Value, magic.Uses, magic.Uses), (row.Value, row.Condition!.Current, row.Condition.Maximum));
        Assert.Equal(result.Metadata, restored);
        Assert.Equal(DaggerfallItemConditionOutcome.AlreadyEnchanted, f.Enchanting.Enchant(sword, magicKey).Outcome);
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(sword, new SlotId("right-hand")).Outcome);
        Assert.Contains(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
    }

    [Fact]
    public void Enchanting_with_an_item_makers_setting_keeps_the_items_own_condition()
    {
        // A setting has no template and no uses, so the item keeps its condition and its own identity
        // while gaining a made enchantment and identification; a magic item's uses are not copied onto it.
        using Fixture f = new();
        DaggerfallEnchantmentSetting setting = TestPayload.Definitions.Magic.EnchantmentSettings.Values.Single(candidate => candidate.Type == 7 && candidate.Param == 0);
        // Daedric carries the largest enchantment capacity, as the published-item fact above uses.
        UniqueItem sword = f.CreatePlainWeapon(406, 115, "daedric");
        DaggerfallItemInstanceMetadata before = f.Instances.RequireUnique(406);
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(sword, new SlotId("right-hand")).Outcome);

        DaggerfallItemConditionResult result = f.Enchanting.Enchant(sword, setting.Key);

        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, result.Outcome);
        Assert.Equal((before.ItemId, (string?)null, true, before.CurrentCondition, before.MaximumCondition),
            (result.Metadata.ItemId, result.Metadata.Enchantment, result.Metadata.Identified,
                result.Metadata.CurrentCondition, result.Metadata.MaximumCondition));
        Assert.Equal([setting.Key], MadeKeys(result.Metadata));
        Assert.Throws<InvalidOperationException>(() => f.Enchanting.Enchant(sword, setting.Key));
        Assert.Equal([setting.Key], MadeKeys(f.Instances.RequireUnique(406)));
        // A setting-enchanted item is a normal item everywhere else: its metadata round-trips and it
        // presents as the ordinary item named by what the setting does, not as an unpublished template.
        DaggerfallItemInstanceMetadata stored = f.Instances.RequireUnique(406);
        Assert.Equal(stored, DaggerfallItemInstanceMetadata.Restore(stored.ItemId, stored.Capture()));
        InventoryItemPresentation row = Assert.Single(f.Presentation.Read().Items);
        // The claim is that the row names what the setting does; the condition prefix belongs to the
        // condition owner and is asserted where that behavior is.
        Assert.True(row.Identified);
        Assert.Contains("Increased Weight Allowance: 25% additional", row.Details, StringComparison.Ordinal);
        // An identified setting leaves the item's value alone rather than standing in for a magic template.
        Assert.Equal(f.Definitions.RequireItem(new DaggerfallItemId(stored.ItemId)).Value, row.Value);
        // The quotation entry point answers for a setting too, at the donor's own cost.
        DaggerfallItemEnchantmentQuote quote = f.Enchanting.QuoteEnchantment(sword, setting.Key);
        Assert.Equal((true, setting.Cost), (quote.Eligible, quote.RequiredPoints));
    }

    [Fact]
    public void The_enchanting_action_quotes_the_loaded_setting_cost()
    {
        System.Text.Json.Nodes.JsonObject payload = TestPayload.Sections("magic");
        System.Text.Json.Nodes.JsonObject setting = payload["magic"]!["enchantmentSettings"]!.AsArray()
            .Single(row => row!["key"]!.GetValue<string>() == "enchantment.7.0")!.AsObject();
        setting["cost"] = 417;
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload.AsObject()));
        using Fixture fixture = new(definitions);
        UniqueItem item = fixture.CreatePlainWeapon(501, 115, "daedric");
        Assert.Equal(417, fixture.Enchanting.QuoteEnchantment(item, "enchantment.7.0").RequiredPoints);
        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, fixture.Enchanting.Enchant(item, "enchantment.7.0").Outcome);
        Assert.Equal(["enchantment.7.0"], MadeKeys(fixture.Instances.RequireUnique(501)));
        Assert.True(DaggerfallMagicCostPolicy.TryGetNonSpellEnchantmentCost(definitions.Magic,
            DaggerfallEnchantmentSettings.ToEffect(definitions.Magic.EnchantmentSettings["enchantment.7.0"]), out int cost));
        Assert.Equal(417, cost);
    }

    [Fact]
    public void Every_item_maker_payload_family_can_be_applied_to_an_eligible_item()
    {
        // One representative per payload family the item maker offers within a weapon's capacity, plus a
        // detriment, which the donor prices negatively and which therefore cannot be a lone payment.
        (int Type, int Param)[] families = [(3, 0), (3, 7), (7, 0), (7, 1), (10, 29), (10, 33), (12, -1), (13, 0), (13, 2)];
        using Fixture f = new();
        ulong durable = 500;
        foreach ((int type, int param) in families)
        {
            DaggerfallEnchantmentSetting setting = TestPayload.Definitions.Magic.EnchantmentSettings.Values.Single(candidate => candidate.Type == type && candidate.Param == param);
            UniqueItem item = f.CreatePlainWeapon(durable, 115, "daedric");
            int condition = f.Instances.RequireUnique(durable).CurrentCondition;

            DaggerfallItemConditionResult result = f.Enchanting.Enchant(item, setting.Key);

            Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, result.Outcome);
            Assert.Equal([setting.Key], MadeKeys(f.Instances.RequireUnique(durable)));
            Assert.Equal(condition, f.Instances.RequireUnique(durable).CurrentCondition);
            durable++;
        }

        // A detriment carries a negative donor cost, and the donor sums raw costs: a drawback-only build
        // is legal and pays nothing, so it lands like any other setting and keeps the item's condition.
        DaggerfallEnchantmentSetting detriment = TestPayload.Definitions.Magic.EnchantmentSettings.Values.Single(candidate => candidate.Type == 24 && candidate.Param == -1);
        UniqueItem cursed = f.CreatePlainWeapon(durable, 115, "daedric");
        int cursedCondition = f.Instances.RequireUnique(durable).CurrentCondition;
        DaggerfallItemConditionResult cursedResult = f.Enchanting.Enchant(cursed, detriment.Key);
        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, cursedResult.Outcome);
        Assert.Equal([detriment.Key], MadeKeys(f.Instances.RequireUnique(durable)));
        Assert.Equal(cursedCondition, f.Instances.RequireUnique(durable).CurrentCondition);
    }

    [Fact]
    public void Ineligible_artifact_quote_cannot_unequip_or_mutate_a_plain_item()
    {
        using Fixture f = new();
        UniqueItem sword = f.CreatePlainSword(405);
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(sword, new SlotId("right-hand")).Outcome);
        DaggerfallItemInstanceMetadata before = f.Instances.RequireUnique(405);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => f.Enchanting.Enchant(sword, "magic-item.0000"));

        Assert.Contains("artifact template", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, f.Instances.RequireUnique(405));
        Assert.Contains(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
    }

    [Fact]
    public void Social_settings_preserve_capacity_and_existing_item_enchantment_exclusivity()
    {
        using Fixture f = new();
        UniqueItem item = f.CreatePlainWeapon(507, 115, "daedric");
        DaggerfallItemInstanceMetadata before = f.Instances.RequireUnique(507);
        Assert.False(f.Enchanting.QuoteEnchantment(item, "enchantment.14.5").Eligible);
        Assert.Throws<InvalidOperationException>(() => f.Enchanting.Enchant(item, "enchantment.14.5"));
        Assert.Equal(before, f.Instances.RequireUnique(507));
        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, f.Enchanting.Enchant(item, "enchantment.14.0").Outcome);
        Assert.Throws<InvalidOperationException>(() => f.Enchanting.Enchant(item, "enchantment.14.0"));
        Assert.Throws<InvalidOperationException>(() => f.Enchanting.Enchant(item, "enchantment.25.0"));
        Assert.Equal(["enchantment.14.0"], MadeKeys(f.Instances.RequireUnique(507)));
    }

    /// <summary>The settings a made enchantment was built from, without the children a setting forces.</summary>
    private static string[] MadeKeys(DaggerfallItemInstanceMetadata metadata) =>
        metadata.MadeEnchantment?.Settings.Where(value => value.Parent is null).Select(value => value.Key).ToArray() ?? [];
}
