using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published item template catalog through the pack: representative records resolve, weapon
/// media names published media, and spell-carrying enchantments name catalog spells.
/// </summary>
public sealed class DaggerfallItemTemplatesContentTests
{
    private static readonly HashSet<string> WeaponMedia = new(
        ["weapon.staff", "weapon.dagger.steel", "weapon.longblade", "weapon.mace", "weapon.flail", "weapon.warhammer", "weapon.axe", "weapon.bow"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> SpellEnchantments = new(
        ["CastWhenUsed", "CastWhenHeld", "CastWhenStrikes"],
        StringComparer.Ordinal);

    [Fact]
    public void Resolves_templates_media_and_spell_references()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(288, definitions.ItemTemplateCatalog.Templates.Count);
        Assert.Equal(59, definitions.ItemTemplateCatalog.Magic.Count);

        // A weapon resolves with its media; armor with its condition; gold stacks.
        DaggerfallItemTemplateDefinition dagger = definitions.ItemTemplateCatalog.Resolve(113);
        Assert.Equal("Dagger", dagger.Name);
        Assert.Equal(["Weapons"], dagger.Groups);
        Assert.False(dagger.Stackable);
        Assert.Equal("weapon.dagger.steel", dagger.WeaponMediaId);
        DaggerfallItemTemplateDefinition cuirass = definitions.ItemTemplateCatalog.Resolve(105);
        Assert.True(cuirass.HitPoints > 0);
        Assert.True(definitions.ItemTemplateCatalog.Resolve(276).Stackable);

        // Every weapon media reference names published weapon media.
        foreach (DaggerfallItemTemplateDefinition template in definitions.ItemTemplateCatalog.Templates.Values)
        {
            if (template.WeaponMediaId.Length > 0)
            {
                Assert.Contains(template.WeaponMediaId, WeaponMedia);
            }
        }

        // Spell-carrying enchantments name catalog spells.
        HashSet<int> spells = [.. definitions.Magic.Spells.Select(spell => spell.Value.Identity)];
        foreach (DaggerfallMagicTemplateDefinition magic in definitions.ItemTemplateCatalog.Magic.Values)
        {
            foreach (DaggerfallTemplateEnchantmentDefinition enchantment in magic.Enchantments)
            {
                if (SpellEnchantments.Contains(enchantment.Type))
                {
                    Assert.Contains(enchantment.Param, spells);
                }
            }
        }
    }

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return TestPayload.Definitions;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
