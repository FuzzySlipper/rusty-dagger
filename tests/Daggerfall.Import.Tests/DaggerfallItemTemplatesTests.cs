using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The donor's exported template tables: substitute provenance, group facts, the donor's stack
/// rule, weapon media references and magic enchantment params. Native bytes stay absent.
/// </summary>
public sealed class DaggerfallItemTemplatesTests
{
    [DonorFact("Assets/Scripts/Game/Items/ItemEnums.cs", "Assets/Scripts/Game/Items/ItemHelper.cs")]
    public void Retains_runtime_group_ordinals_with_aliases_and_implicit_members()
    {
        var groups = ItemTemplateBaseline.ReadGroupEnumerations(
            File.ReadAllText(TestData.Donor("Assets/Scripts/Game/Items/ItemEnums.cs")),
            File.ReadAllText(TestData.Donor("Assets/Scripts/Game/Items/ItemHelper.cs")), "donor item groups");
        Assert.Equal(29, groups.Count);
        Assert.Equal([277, 277, 277, 277], groups.Single(group => group.Id == 7).Values);
        Assert.Equal([132, 274, 275, 276, 278, 281, 285, 286, 287], groups.Single(group => group.Id == 27).Values);
        Assert.Equal([0, 1, 2], groups.Single(group => group.Id == 24).Values);
        Assert.False(groups.Single(group => group.Id == 5).TemplateIndices);
        Assert.Equal(-1, groups.Single(group => group.Id == 5).Values[^1]);
        Assert.Equal(54, groups.Single(group => group.Id == 17).Values[13]);
    }

    [Fact]
    public void Refuses_malformed_substitute_tables()
    {
        Assert.Throws<Arena2FormatException>(() => ItemTemplateReader.ReadTemplates("[]", "substitute"));
        Assert.Throws<Arena2FormatException>(() => ItemTemplateReader.ReadTemplates("[{}]", "substitute"));
        Assert.Throws<Arena2FormatException>(() => ItemTemplateReader.ReadTemplates("nope", "substitute"));
        Assert.Empty(ItemTemplateReader.ReadMagic("[]", "substitute"));
        Assert.Throws<InvalidOperationException>(() => DaggerfallItemTemplatesBuilder.Build([], [], "donor/Assets/Resources/ItemTemplates.txt", [1], Inventory()));
    }

    [DonorFact("Assets/Resources/ItemTemplates.txt", "Assets/Resources/MagicItemTemplates.txt")]
    public void Builds_the_template_catalog_end_to_end()
    {
        string items = TestData.Donor("Assets/Resources/ItemTemplates.txt");
        string magic = TestData.Donor("Assets/Resources/MagicItemTemplates.txt");

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "data/content-source-manifest.csv")));
        DaggerfallItemTemplates catalog = DaggerfallItemTemplatesBuilder.Build(
            ItemTemplateReader.ReadTemplates(File.ReadAllText(items), "donor/Assets/Resources/ItemTemplates.txt"),
            ItemTemplateReader.ReadMagic(File.ReadAllText(magic), "donor/Assets/Resources/MagicItemTemplates.txt"),
            "donor/Assets/Resources/ItemTemplates.txt",
            File.ReadAllBytes(items),
            inventory);
        catalog.Validate();

        Assert.Equal(288, catalog.Templates.Count);
        Assert.Equal(59, catalog.Magic.Count);
        Assert.All(catalog.Templates, template => Assert.Equal(DaggerfallItemTemplateDisposition.Substitute, template.Disposition));
        // The donor's stack rule: ingredients, the bottle, books, gold, arrows and oil stack.
        Assert.True(catalog.Templates.Single(template => template.Index == 131).Stackable);
        Assert.True(catalog.Templates.Single(template => template.Index == 276).Stackable);
        Assert.True(catalog.Templates.Single(template => template.Index == 252).Stackable);
        Assert.True(catalog.Templates.Single(template => template.Index == 83).Stackable);
        Assert.False(catalog.Templates.Single(template => template.Index == 113).Stackable);
        // Weapon templates reference the 7943 weapon media; arrows name none.
        Assert.Equal("weapon.dagger.steel", catalog.Templates.Single(template => template.Index == 113).WeaponMediaId);
        Assert.Equal("weapon.longblade", catalog.Templates.Single(template => template.Index == 120).WeaponMediaId);
        Assert.Equal("weapon.bow", catalog.Templates.Single(template => template.Index == 129).WeaponMediaId);
        Assert.Empty(catalog.Templates.Single(template => template.Index == 131).WeaponMediaId);
        // Magic placements name donor groups with live enchantment params.
        Assert.All(catalog.Magic, entry => Assert.NotEmpty(entry.GroupName));
        Assert.Contains(catalog.Magic.SelectMany(entry => entry.Enchantments), enchantment => enchantment.Param >= 0);
        // Every template names a donor group.
        Assert.DoesNotContain(catalog.Templates, template => template.Groups.Count == 0);
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-011", "family", "CNT-011", "items", "donor", string.Empty, "pending-import", string.Empty),
    ];
}
