using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestResourceSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_retained_item_and_foe_selects_actual_published_meaning_before_spawn(bool maximum)
    {
        var definitions = TestPayload.Definitions;
        var allocator = Allocator(definitions, maximum);
        var resources = definitions.QuestSources.Resources.Where(value => value.Kind is "item" or "foe").ToArray();
        Assert.Equal(995, resources.Length);
        foreach (var source in resources)
        {
            var selected = allocator.Allocate("corpus/" + source.SourceFile, 0, source);
            Assert.Equal(DaggerfallQuestResourceBindingKind.Pending, selected.Binding.Kind);
            Assert.Empty(selected.Binding.ActorIds);
            Assert.Empty(selected.Binding.UniqueItemIds);
            Assert.Empty(selected.Binding.Stacks);
            Assert.NotNull(selected.Text);
            if (source.Kind == "item")
            {
                var item = Assert.IsType<DaggerfallCreatedItem>(selected.SelectedItem);
                Assert.True(definitions.TryResolveItem(new(item.Item.Value), out _));
                Assert.Equal(source.CanonicalId, item.Metadata.QuestItemSymbol);
                Assert.True(item.Quantity > 0);
            }
            else
            {
                var foe = Assert.IsType<DaggerfallQuestFoeSelection>(selected.SelectedFoe);
                var mobile = definitions.QuestSources.Tables.ActorItemTables.Foes.Resolve(source.TargetSourceSpelling!).Id;
                Assert.Equal(mobile, definitions.RequireActor(new(foe.Definition)).MobileId);
                Assert.InRange(foe.Count, 1, 8);
            }
        }
    }

    [Theory]
    [InlineData("B0B81Y02.txt", "i.06", 54, null)]
    [InlineData("C0B00Y14.txt", "i.00", 262, null)]
    [InlineData("N0B00Y16.txt", "crn", 90, null)]
    [InlineData("R0C11Y28.txt", "i.09", 23, null)]
    [InlineData("S0000501.txt", "reward1", -1, "magic-item.0036")]
    [InlineData("S0000502.txt", "reward", -1, "magic-item.0013")]
    public void Source_subclasses_resolve_to_native_template_or_regular_magic_ordinal(string file, string symbol, int template, string? magic)
    {
        var definitions = TestPayload.Definitions;
        var source = definitions.QuestSources.Resources.Single(value => value.SourceFile == file && value.CanonicalId == symbol);
        var item = Allocator(definitions).Allocate("exact", 0, source).SelectedItem!;
        if (magic is null) Assert.Equal(template, item.TemplateIndex);
        else Assert.Equal(magic, item.Metadata.Enchantment);
    }

    [Fact]
    public void Ordinary_start_retains_selected_items_foes_and_names_in_encoded_save_without_minting_entity_ids()
    {
        JsonObject root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var declarationRows = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var item = declarationRows.Single(value => value!["sourceFile"]!.GetValue<string>() == "S0000502.txt"
            && value["symbol"]!["canonicalId"]!.GetValue<string>() == "reward")!.DeepClone();
        var foe = declarationRows.First(value => value!["kind"]!.GetValue<string>() == "foe")!.DeepClone();
        foreach (var row in new[] { item, foe })
        {
            row["quest"] = "selected"; row["sourceFile"] = "selected.txt";
            declarationRows.Add(row);
        }
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"selected","displayName":"","sourceFile":"selected.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        var definitions = DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
        DaggerfallQuestInstances quests = new(definitions, RandomMinimum.Create());
        quests.BindResourceAllocator(Allocator(definitions));
        var first = quests.Start(new("first", "selected.txt", "selected", DaggerfallQuestLifecycle.Active, null, [], []));
        var second = quests.Start(new("second", "selected.txt", "selected", DaggerfallQuestLifecycle.Active, null, [], []));
        Assert.Equal(2, first.Resources.Length);
        var firstItem = first.Resources.Single(value => value.SelectedItem is not null);
        var secondItem = second.Resources.Single(value => value.SelectedItem is not null);
        Assert.Equal("first", firstItem.SelectedItem!.Metadata.QuestId);
        Assert.Equal("second", secondItem.SelectedItem!.Metadata.QuestId);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(quests.Capture(), DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave);
        DaggerfallQuestInstances restored = new(definitions, RandomMaximum.Create());
        restored.Restore(JsonSerializer.Deserialize(encoded, DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave)!);
        Assert.True(restored.TryGet("first", out var saved));
        foreach (var resource in first.Resources)
        {
            var retained = saved!.Resources.Single(value => value.Symbol == resource.Symbol);
            Assert.Equal(resource.SelectedItem, retained.SelectedItem);
            Assert.Equal(resource.SelectedFoe, retained.SelectedFoe);
            Assert.Equal(resource.Text, retained.Text);
        }
    }

    private static DaggerfallQuestResourceAllocator Allocator(DaggerfallDefinitions definitions, bool maximum = false) =>
        new(definitions, maximum ? RandomMaximum.Create() : RandomMinimum.Create(),
            new(definitions, maximum ? RandomMaximum.Create() : RandomMinimum.Create()),
            new(definitions, maximum ? RandomMaximum.Create() : RandomMinimum.Create()),
            () => new(4, "breton", "male", 17), _ => default, _ => 0,
            item => definitions.ItemTemplateCatalog.Resolve(item.TemplateIndex).Name);
}
