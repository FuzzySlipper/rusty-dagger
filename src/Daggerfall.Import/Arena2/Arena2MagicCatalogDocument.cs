using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daggerfall.Import.Arena2;

/// <summary>The published magic catalog document and what it carried.</summary>
public sealed record Arena2MagicCatalogPublication(string Json, int Spells, int MagicItems, int Enchantments, int UnresolvedLinks, int Dispositions);

/// <summary>
/// Publishes the classic magical catalogs as one normalized document: stable keys, the identity the
/// source gives each record, and the cross-references between an item's enchantments and the spells they
/// name.
/// </summary>
/// <remarks>
/// A record's key is its ordinal in the source table, because the file's own identity byte is not unique
/// in this corpus and a key that collided would silently drop a spell. The source identity is published
/// beside the key rather than folded into it, an enchantment that names a spell resolves to that spell's
/// key, and a name that resolves to nothing or to more than one spell is reported as an unresolved or
/// ambiguous link instead of being assumed. Nothing is published as behavior here: this is the catalog a
/// later effect task consumes.
/// </remarks>
public static class Arena2MagicCatalogDocument
{
    /// <summary>The documented inventory record that owns the magical sources.</summary>
    public const string SourceRecordId = "CNT-012";

    /// <summary>Builds the document's JSON from the two source files' bytes.</summary>
    public static Arena2MagicCatalogPublication Build(byte[] spellBytes, byte[] magicBytes, string spellLabel, string magicLabel)
    {
        Arena2SpellCatalog spells = Arena2MagicReader.ReadSpells(spellBytes, spellLabel);
        Arena2MagicItemCatalog items = Arena2MagicReader.ReadMagicItems(magicBytes, magicLabel);

        Dictionary<int, List<string>> byIdentity = [];
        for (int ordinal = 0; ordinal < spells.Spells.Count; ordinal++)
        {
            Arena2Spell spell = spells.Spells[ordinal];
            if (!byIdentity.TryGetValue(spell.Index, out List<string>? keys)) byIdentity[spell.Index] = keys = [];
            keys.Add(SpellKey(ordinal));
        }

        JsonArray publishedSpells = [];
        for (int ordinal = 0; ordinal < spells.Spells.Count; ordinal++)
        {
            Arena2Spell spell = spells.Spells[ordinal];
            JsonArray effects = [];
            for (int slot = 0; slot < spell.Effects.Count; slot++)
            {
                Arena2SpellEffect effect = spell.Effects[slot];
                effects.Add(new JsonObject
                {
                    ["key"] = $"{SpellKey(ordinal)}.effect.{slot + 1}",
                    ["type"] = effect.Type,
                    ["subType"] = effect.SubType,
                    ["duration"] = Triple(effect.DurationBase, effect.DurationMod, effect.DurationPerLevel),
                    ["chance"] = Triple(effect.ChanceBase, effect.ChanceMod, effect.ChancePerLevel),
                    ["magnitude"] = new JsonObject
                    {
                        ["baseLow"] = effect.MagnitudeBaseLow,
                        ["baseHigh"] = effect.MagnitudeBaseHigh,
                        ["levelBase"] = effect.MagnitudeLevelBase,
                        ["levelHigh"] = effect.MagnitudeLevelHigh,
                        ["perLevel"] = effect.MagnitudePerLevel,
                    },
                });
            }

            publishedSpells.Add(new JsonObject
            {
                ["key"] = SpellKey(ordinal),
                ["identity"] = spell.Index,
                // The file repeats one identity, so a consumer told only "spell 58" cannot tell which of
                // the two it means; the publication says so where it happens.
                ["identityShared"] = byIdentity[spell.Index].Count > 1,
                ["name"] = spell.Name,
                ["element"] = spell.Element,
                ["rangeType"] = spell.RangeType,
                ["cost"] = spell.Cost,
                ["icon"] = spell.Icon,
                ["effects"] = effects,
            });
        }

        JsonArray publishedItems = [];
        JsonArray unresolved = [];
        int enchantments = 0;
        for (int ordinal = 0; ordinal < items.Items.Count; ordinal++)
        {
            Arena2MagicItem item = items.Items[ordinal];
            string itemKey = $"magic-item.{ordinal:D4}";
            JsonArray slots = [];
            for (int slot = 0; slot < item.Enchantments.Count; slot++)
            {
                Arena2MagicEnchantment enchantment = item.Enchantments[slot];
                string enchantmentKey = $"{itemKey}.enchantment.{slot + 1}";
                string? spellKey = null;
                bool ambiguous = false;
                if (enchantment.Param >= 0 && byIdentity.TryGetValue(enchantment.Param, out List<string>? candidates))
                {
                    spellKey = candidates[0];
                    ambiguous = candidates.Count > 1;
                }
                else if (enchantment.Param >= 0)
                {
                    unresolved.Add(new JsonObject
                    {
                        ["enchantment"] = enchantmentKey,
                        ["item"] = itemKey,
                        ["spellIdentity"] = enchantment.Param,
                        ["reason"] = "no published spell carries this identity",
                    });
                }

                slots.Add(new JsonObject
                {
                    ["key"] = enchantmentKey,
                    ["type"] = enchantment.Type,
                    ["param"] = enchantment.Param,
                    ["spell"] = spellKey,
                    ["spellIdentityShared"] = ambiguous,
                });
                enchantments++;
            }

            publishedItems.Add(new JsonObject
            {
                ["key"] = itemKey,
                ["offset"] = item.Index,
                ["name"] = item.Name,
                ["type"] = item.Type,
                ["group"] = item.Group,
                ["groupIndex"] = item.GroupIndex,
                ["uses"] = item.Uses,
                ["value"] = item.Value,
                ["material"] = item.Material,
                ["enchantments"] = slots,
            });
        }

        JsonArray dispositions = [];
        foreach (Arena2MagicDisposition disposition in spells.Dispositions)
        {
            dispositions.Add(new JsonObject { ["offset"] = disposition.Offset, ["kind"] = "spell", ["reason"] = disposition.Reason });
        }

        foreach (Arena2MagicDisposition disposition in items.Dispositions)
        {
            dispositions.Add(new JsonObject { ["offset"] = disposition.Offset, ["kind"] = "magic-item", ["reason"] = disposition.Reason });
        }

        JsonObject document = new()
        {
            ["schemaVersion"] = 1,
            ["sources"] = new JsonArray(
                new JsonObject { ["recordId"] = SourceRecordId, ["path"] = spellLabel },
                new JsonObject { ["recordId"] = SourceRecordId, ["path"] = magicLabel }),
            ["spells"] = publishedSpells,
            ["magicItems"] = publishedItems,
            ["unresolvedLinks"] = unresolved,
            ["dispositions"] = dispositions,
        };

        string json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return new Arena2MagicCatalogPublication(json, spells.Spells.Count, items.Items.Count, enchantments, unresolved.Count, dispositions.Count);
    }

    private static string SpellKey(int ordinal) => $"spell.{ordinal + 1:D3}";

    private static JsonObject Triple(int baseValue, int mod, int perLevel) => new()
    {
        ["base"] = baseValue,
        ["mod"] = mod,
        ["perLevel"] = perLevel,
    };
}
