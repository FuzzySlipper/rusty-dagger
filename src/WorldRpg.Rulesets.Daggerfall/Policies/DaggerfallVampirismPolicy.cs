using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>Province clan and curse-granted spell meaning over the normalized Daggerfall catalogs.</summary>
internal static class DaggerfallVampirismPolicy
{
    internal static DaggerfallFactionDefinition GetVampireClan(DaggerfallFactionsSet factions, int region)
    {
        if (region is < 0 or > 61) throw new ArgumentOutOfRangeException(nameof(region));
        var provinces = factions.Factions.Values.Where(value => value.Region == region && value.Type == 7).ToArray();
        if (provinces.Length != 1) throw new NotSupportedException($"Region {region} requires exactly one province faction; found {provinces.Length}.");
        // FormulaHelper.GetVampireClan uses Lyrezi when the source province names no recognized clan.
        int id = provinces[0].Vampire is >= 150 and <= 158 ? provinces[0].Vampire : 153;
        if (!factions.Factions.TryGetValue(id, out var clan) || clan.Type != 6)
            throw new NotSupportedException($"Vampire clan faction {id} is not published.");
        return clan;
    }

    internal static string ClanName(DaggerfallDefinitions definitions, int clan)
    {
        string[] keys = ["vraseth", "haarvenu", "thrafey", "lyrezi", "montalion", "khulari", "garlythi", "anthotis", "selenu"];
        if (clan is < 150 or > 158) throw new ArgumentOutOfRangeException(nameof(clan));
        return definitions.Text.RequireInternalEntry(keys[clan - 150], 0);
    }

    internal static string[] GrantedSpells(DaggerfallMagicCatalogSet catalog, int clan)
    {
        int[] specific = clan switch
        {
            150 => [85], 151 => [20, 33], 152 => [64], 153 => [23, 6], 154 => [94],
            155 => [50], 156 => [17], 157 => [], 158 => [11, 12, 13],
            _ => throw new ArgumentOutOfRangeException(nameof(clan)),
        };
        return [.. new[] { 4, 90, 91 }.Concat(specific).Select(identity => catalog.Spells.Values
            .Where(spell => !spell.IsCustom && spell.Identity == identity).OrderBy(spell => spell.Key, StringComparer.Ordinal).FirstOrDefault()?.Key
            ?? throw new NotSupportedException($"Vampirism requires published spell identity {identity}."))];
    }
}

internal sealed record DaggerfallVampirismTuning(int AttributeBonus, int SkillBonus, int SatiationMinutes)
{
    internal static DaggerfallVampirismTuning Classic { get; } = new(20, 30, 1440);
    internal DaggerfallVampirismTuning Validate() => AttributeBonus >= 0 && SkillBonus >= 0 && SatiationMinutes > 0
        ? this : throw new ArgumentException("Vampirism tuning contains invalid bonuses or satiation duration.");
}
