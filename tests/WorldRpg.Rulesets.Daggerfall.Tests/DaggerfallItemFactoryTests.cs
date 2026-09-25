using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallItemFactoryTests
{
    [Fact]
    public void Every_retained_group_template_creates_a_resolvable_engine_backed_item()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());
        DaggerfallItemTemplateDefinition[] retained = definitions.ItemTemplateCatalog.Templates.Values
            .Where(template => template.Groups.Count != 0).OrderBy(template => template.Index).ToArray();

        DaggerfallCreatedItem[] created = retained.Select(template => factory.Create(Request(template))).ToArray();

        Assert.Equal(retained.Length, created.Length);
        Assert.Equal(288, created.Length);
        foreach ((DaggerfallItemTemplateDefinition template, DaggerfallCreatedItem item) in retained.Zip(created))
        {
            string expectedMaterial = template.Index == 131 ? "none"
                : template.Groups.Contains("Weapons", StringComparer.Ordinal) ? "iron"
                : template.Groups.Contains("Armor", StringComparer.Ordinal) ? "leather"
                : "none";
            Assert.Equal(expectedMaterial == "none" ? $"template-{template.Index}" : $"template-{template.Index}-{expectedMaterial}", item.Item.Value);
            Assert.True(definitions.TryResolveItem(new DaggerfallItemId(item.Item.Value), out DaggerfallItemDefinition resolved));
            Assert.Equal(template.Stackable, resolved.IsFungible);
            Assert.Same(template, resolved.Template);
            Assert.InRange(item.Metadata.Variant, 0, Math.Max(0, template.Variants - 1));
            Assert.Equal(template.Index == 131 ? 0 : template.HitPoints, item.Metadata.CurrentCondition);
        }
    }

    [Fact]
    public void Materialized_definitions_and_instance_condition_follow_ItemBuilder_material_rules()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());

        DaggerfallCreatedItem steelDagger = factory.Create(new DaggerfallItemCreateRequest("Weapons", "steel-dagger", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: "steel"));
        DaggerfallCreatedItem daedricCuirass = factory.Create(new DaggerfallItemCreateRequest("Armor", "daedric-cuirass", DaggerfallItemOwner.Player,
            TemplateIndex: 102, Material: "daedric", Race: "breton", Gender: "male"));
        DaggerfallCreatedItem leatherCuirass = factory.Create(new DaggerfallItemCreateRequest("Armor", "leather-cuirass", DaggerfallItemOwner.Player,
            TemplateIndex: 102, Material: "leather", Race: "breton", Gender: "male"));
        DaggerfallCreatedItem chainCuirass = factory.Create(new DaggerfallItemCreateRequest("Armor", "chain-cuirass", DaggerfallItemOwner.Player,
            TemplateIndex: 102, Material: "chain", Race: "breton", Gender: "male"));

        Assert.Equal(("template-113-steel", 75), (steelDagger.Item.Value, steelDagger.Metadata.MaximumCondition));
        Assert.Equal((3, 6), (definitions.RequireItem(new DaggerfallItemId(steelDagger.Item.Value)).Weight, definitions.RequireItem(new DaggerfallItemId(steelDagger.Item.Value)).Value));
        Assert.Equal(("template-102-daedric", 32_768), (daedricCuirass.Item.Value, daedricCuirass.Metadata.MaximumCondition));
        Assert.Equal((63, 153_600), (definitions.RequireItem(new DaggerfallItemId(daedricCuirass.Item.Value)).Weight, definitions.RequireItem(new DaggerfallItemId(daedricCuirass.Item.Value)).Value));
        Assert.Equal((25, 100, 4_096), (
            definitions.RequireItem(new DaggerfallItemId(leatherCuirass.Item.Value)).Weight,
            definitions.RequireItem(new DaggerfallItemId(leatherCuirass.Item.Value)).Value,
            leatherCuirass.Metadata.MaximumCondition));
        Assert.Equal((50, 200, 4_096), (
            definitions.RequireItem(new DaggerfallItemId(chainCuirass.Item.Value)).Weight,
            definitions.RequireItem(new DaggerfallItemId(chainCuirass.Item.Value)).Value,
            chainCuirass.Metadata.MaximumCondition));
    }

    [Fact]
    public void Magic_templates_materialize_a_unique_magic_base_with_donor_uses_value_and_artifact_material()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());

        DaggerfallCreatedItem regular = factory.Create(new DaggerfallItemCreateRequest("Magic", "regular-magic", DaggerfallItemOwner.Player,
            Race: "breton", Gender: "male", MagicItemKey: "magic-item.0010"));
        DaggerfallCreatedItem artifact = factory.Create(new DaggerfallItemCreateRequest("Magic", "artifact-magic", DaggerfallItemOwner.Player,
            MagicItemKey: "magic-item.0001"));

        DaggerfallMagicItemDefinition regularDefinition = definitions.Magic.MagicItems["magic-item.0010"];
        Assert.False(regular.Stackable);
        Assert.Equal((regularDefinition.Key, regularDefinition.Uses), (regular.Metadata.Enchantment, regular.Metadata.MaximumCondition));
        Assert.Equal(regularDefinition.Value, definitions.RequireItem(new DaggerfallItemId(regular.Item.Value)).Value);
        Assert.Equal(("template-113-orcish-magic-magic-item-0001", "orcish"), (artifact.Item.Value, artifact.Metadata.Material));
        Assert.Equal(1UL, definitions.RequireItem(new DaggerfallItemId(artifact.Item.Value)).MaximumQuantity);
    }

    [Fact]
    public void Creation_preserves_donor_appearance_book_and_arrow_rules_through_metadata_capture()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());

        DaggerfallCreatedItem clothing = factory.Create(new DaggerfallItemCreateRequest("MensClothing", "clothing", DaggerfallItemOwner.Player,
            TemplateIndex: 141, Race: "breton"));
        DaggerfallCreatedItem armor = factory.Create(new DaggerfallItemCreateRequest("Armor", "armor", DaggerfallItemOwner.Player,
            TemplateIndex: 102, Race: "breton", Gender: "female", Material: "chain"));
        DaggerfallCreatedItem arrows = factory.Create(new DaggerfallItemCreateRequest("Weapons", "arrows", DaggerfallItemOwner.Player,
            TemplateIndex: 131));
        DaggerfallCreatedItem book = factory.Create(new DaggerfallItemCreateRequest("Books", "book", DaggerfallItemOwner.Player,
            TemplateIndex: 277));
        DaggerfallCreatedItem potion = factory.Create(new DaggerfallItemCreateRequest("UselessItems1", "potion", DaggerfallItemOwner.Player,
            TemplateIndex: 83, PotionRecipeKey: 221871));
        DaggerfallCreatedItem recipe = factory.Create(new DaggerfallItemCreateRequest("MiscItems", "recipe", DaggerfallItemOwner.Player,
            TemplateIndex: 278, PotionRecipeKey: 221871));
        DaggerfallCreatedItem letter = factory.Create(new DaggerfallItemCreateRequest("MiscItems", "letter", DaggerfallItemOwner.Player,
            TemplateIndex: 275, CreditValue: 100));

        Assert.Equal(("breton", "male", "blue"), (clothing.Metadata.Race, clothing.Metadata.Gender, clothing.Metadata.Dye));
        Assert.Equal(("breton", "female", "chain"), (armor.Metadata.Race, armor.Metadata.Gender, armor.Metadata.Dye));
        Assert.Equal(("none", 0, 1UL), (arrows.Metadata.Material, arrows.Metadata.CurrentCondition, arrows.Quantity));
        Assert.Equal(1UL, arrows.Quantity); // Random minimum produces the donor's inclusive lower arrow count.
        Assert.NotNull(book.Metadata.BookId);
        Assert.Equal(221871, potion.Metadata.PotionRecipeKey);
        Assert.Equal(221871, recipe.Metadata.PotionRecipeKey);
        Assert.Equal(100UL, letter.Metadata.CreditValue);
        Assert.Equal(book.Metadata, DaggerfallItemInstanceMetadata.Restore(book.Item.Value, book.Metadata.Capture()));
        Assert.Equal(potion.Metadata, DaggerfallItemInstanceMetadata.Restore(potion.Item.Value, potion.Metadata.Capture()));
        Assert.Equal(recipe.Metadata, DaggerfallItemInstanceMetadata.Restore(recipe.Item.Value, recipe.Metadata.Capture()));
        Assert.Equal(letter.Metadata, DaggerfallItemInstanceMetadata.Restore(letter.Item.Value, letter.Metadata.Capture()));
        Assert.True(DaggerfallEquipmentPolicy.IsCompatible(definitions, definitions.RequireItem(new DaggerfallItemId("template-102")), "chest-armor"));
    }

    [Fact]
    public void Book_identity_resolves_its_normalized_runtime_value_and_reports_missing_metadata_definition()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());
        DaggerfallItemValuation valuation = new(definitions);
        DaggerfallCreatedItem first = factory.Create(new DaggerfallItemCreateRequest("Books", "book-zero", DaggerfallItemOwner.Player,
            TemplateIndex: 277, BookId: 0));
        DaggerfallCreatedItem biography = factory.Create(new DaggerfallItemCreateRequest("Books", "book-fifty-nine", DaggerfallItemOwner.Player,
            TemplateIndex: 277, BookId: 59));
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(first.Item.Value));

        Assert.Equal(580, valuation.CurrentValue(definition, first.Metadata));
        Assert.Equal(406, valuation.CurrentValue(definition, biography.Metadata));
        Assert.NotEqual(definition.Value, valuation.CurrentValue(definition, first.Metadata));
        Assert.Equal(valuation.CurrentValue(definition, biography.Metadata),
            valuation.CurrentValue(definition, DaggerfallItemInstanceMetadata.Restore(biography.Item.Value, biography.Metadata.Capture())));
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() =>
            valuation.CurrentValue(definition, first.Metadata with { BookId = 255 }));
        Assert.Contains("unpublished book 255", missing.Message);
    }

    [Fact]
    public void Materialize_commits_to_engine_then_registers_metadata_for_stack_and_unique_instances()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());
        EntityDirectory entities = new();
        EntityId owner = entities.Create(new DurableIdentityReference(DurableIdentityKind.Container, 700), new EntityTypeId("inventory-owner"));
        InventoryStore store = new();
        store.RegisterInventory(new InventoryState(owner));
        InventoryComponent component = new(store, owner);
        MechanicsInventoryCoordinator inventory = new(component, entities, definitions.Items.Values
            .Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem));
        DaggerfallItemInstances instances = new();
        DaggerfallCreatedItem arrows = factory.Create(new DaggerfallItemCreateRequest("Weapons", "arrows", DaggerfallItemOwner.Player, TemplateIndex: 131));
        DaggerfallCreatedItem sword = factory.Create(new DaggerfallItemCreateRequest("Weapons", "sword", DaggerfallItemOwner.Player, TemplateIndex: 113));
        DaggerfallCreatedItem magic = factory.Create(new DaggerfallItemCreateRequest("Magic", "magic", DaggerfallItemOwner.Player,
            Race: "breton", Gender: "male", MagicItemKey: "magic-item.0010"));
        InventoryStackId stack = InventoryStackId.Parse("template-arrows");
        DurableIdentityReference unique = new(DurableIdentityKind.Item, 701);
        DurableIdentityReference magicUnique = new(DurableIdentityKind.Item, 702);

        factory.Materialize(arrows, inventory, instances, stack: stack);
        factory.Materialize(sword, inventory, instances, unique: unique);
        factory.Materialize(magic, inventory, instances, unique: magicUnique);

        Assert.Equal(arrows.Quantity, Assert.Single(component.View().Stacks).Quantity);
        Assert.Equal(arrows.Metadata, instances.RequireStack(DaggerfallItemOwner.Player, stack));
        Assert.Equal(sword.Metadata, instances.RequireUnique(unique.Value));
        Assert.Equal(magic.Metadata, DaggerfallItemInstanceMetadata.Restore(magic.Item.Value, instances.RequireUnique(magicUnique.Value).Capture()));
    }

    [Theory]
    [InlineData("male", 4)]
    [InlineData("female", 2)]
    public void Magic_clothing_uses_player_gender_even_when_other_clothing_group_is_drawn(string gender, int group)
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        IRandomService random = DispatchProxy.Create<IRandomService, MagicSelectionProxy>();
        ((MagicSelectionProxy)(object)random).Group = group;
        string magic = definitions.Magic.MagicItems.Values.First(item => item.Type == 0 && item.Group == 0).Key;
        DaggerfallCreatedItem item = new DaggerfallItemFactory(definitions, random).Create(new(
            "Magic", "clothing-case", DaggerfallItemOwner.Player, Race: "breton", Gender: gender, MagicItemKey: magic));
        Assert.Equal(gender, item.Metadata.Gender);
        Assert.Equal(magic, item.Metadata.Enchantment);
    }

    [Fact]
    public void Magic_weapon_maximum_template_draw_excludes_arrows()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        IRandomService random = DispatchProxy.Create<IRandomService, MagicSelectionProxy>();
        ((MagicSelectionProxy)(object)random).Group = 1;
        string magic = definitions.Magic.MagicItems.Values.First(item => item.Type == 0 && item.Group == 0).Key;
        DaggerfallCreatedItem item = new DaggerfallItemFactory(definitions, random).Create(new(
            "Magic", "weapon-case", DaggerfallItemOwner.Player, Race: "breton", Gender: "male", MagicItemKey: magic));
        Assert.NotEqual(131, item.TemplateIndex);
        Assert.False(item.Stackable);
        Assert.Equal(magic, item.Metadata.Enchantment);
    }

    private class MagicSelectionProxy : DispatchProxy
    {
        internal int Group;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            return new KeyedRngReceipt(request.Key.EndsWith(".magic-group", StringComparison.Ordinal) ? Group
                : request.Key.EndsWith(".template", StringComparison.Ordinal) || request.Key.EndsWith(".magic-weapon", StringComparison.Ordinal)
                    ? request.Maximum : request.Minimum);
        }
    }

    [Fact]
    public void Malformed_category_template_appearance_and_quantity_are_rejected_before_materialization()
    {
        DaggerfallItemFactory factory = new(LoadDefinitions(), RandomMinimum());

        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("missing", "bad", DaggerfallItemOwner.Player)));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("Armor", "bad", DaggerfallItemOwner.Player, TemplateIndex: 113, Race: "breton", Gender: "male")));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("Armor", "bad", DaggerfallItemOwner.Player, TemplateIndex: 102, Race: "not-a-race", Gender: "male")));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("Weapons", "bad", DaggerfallItemOwner.Player, Quantity: 2, TemplateIndex: 113)));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("Weapons", "bad", DaggerfallItemOwner.Player, Material: "leather", TemplateIndex: 113)));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("UselessItems1", "potion", DaggerfallItemOwner.Player, TemplateIndex: 83)));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("Weapons", "not-potion", DaggerfallItemOwner.Player, TemplateIndex: 113, PotionRecipeKey: 221871)));
        Assert.Throws<ArgumentException>(() => factory.Create(new DaggerfallItemCreateRequest("MiscItems", "letter", DaggerfallItemOwner.Player, TemplateIndex: 275)));
    }

    private static DaggerfallItemCreateRequest Request(DaggerfallItemTemplateDefinition template) => new(
        template.Groups[0],
        $"all-{template.Index}",
        DaggerfallItemOwner.Player,
        TemplateIndex: template.Index,
        Race: template.Groups.Contains("Armor", StringComparer.Ordinal) || template.Groups.Contains("MensClothing", StringComparer.Ordinal) || template.Groups.Contains("WomensClothing", StringComparer.Ordinal) ? "breton" : null,
        Gender: template.Groups.Contains("Armor", StringComparer.Ordinal) ? "male" : null,
        PotionRecipeKey: template.Index is 83 or 278 ? 221871 : null,
        CreditValue: template.Index == 275 ? 100 : null);

    private static IRandomService RandomMinimum() => DispatchProxy.Create<IRandomService, RandomMinimumProxy>();

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static DaggerfallDefinitions LoadDefinitions() =>
        TestPayload.Definitions;

}
