using System.Collections.ObjectModel;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Authored classic identities, resolved once against the normalized spell catalog.</summary>
internal sealed record DaggerfallEnemySpells(
    IReadOnlyDictionary<int, string[]> MobileLists, IReadOnlySet<int> ClassCasters, string[][] ClassTiers)
{
    internal static DaggerfallEnemySpells Empty { get; } = new(new Dictionary<int, string[]>(), new HashSet<int>(), []);
    internal bool IsCaster(int? mobile) => mobile is int id && (MobileLists.ContainsKey(id) || ClassCasters.Contains(id));
    internal IReadOnlyList<string> For(DaggerfallActorDefinition actor) => actor.MobileId is not int mobile ? []
        : ClassCasters.Contains(mobile) ? ClassTiers[Math.Min((actor.Level ?? 1) / 3, ClassTiers.Length - 1)]
        : MobileLists.GetValueOrDefault(mobile) ?? [];
    internal static readonly string[] Schools = ["destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism"];
    internal static int MagickaMaximum(int level) => checked(10 * level + 100);
}

internal static partial class DaggerfallBaseContent
{
    private static DaggerfallEnemySpells ReadEnemySpells(JsonElement root, DaggerfallMobileCatalogSet mobiles,
        DaggerfallMagicCatalogSet magic, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("enemySpells", out JsonElement section))
        { diagnostics.Add("Authored enemySpells lists are missing."); return DaggerfallEnemySpells.Empty; }
        Dictionary<int, string[]> lists = [];
        string[] Resolve(JsonElement ids)
        {
            List<string> keys = [];
            foreach (JsonElement value in ids.EnumerateArray())
            {
                int identity = value.GetInt32();
                var matches = magic.Spells.Values.Where(spell => !spell.IsCustom && spell.Identity == identity).ToArray();
                if (matches.Length != 1) diagnostics.Add($"Enemy spell source identity {identity} resolves to {matches.Length} spell records.");
                else keys.Add(matches[0].Key);
            }
            if (keys.Count == 0 || keys.Distinct().Count() != keys.Count) diagnostics.Add("An enemy spell list is empty or repeats a spell identity.");
            return keys.ToArray();
        }
        foreach (JsonProperty entry in section.GetProperty("mobiles").EnumerateObject())
        {
            int mobile = int.Parse(entry.Name, System.Globalization.CultureInfo.InvariantCulture);
            // EnemyEntity assigns monster lists independently of the class-only CastsMagic flag.
            if (!mobiles.Mobiles.ContainsKey(mobile) || mobile >= DaggerfallEncounterActors.FirstClassMobile)
                diagnostics.Add($"Enemy spell list names unknown monster mobile {mobile}.");
            lists.Add(mobile, Resolve(entry.Value));
        }
        string[][] tiers = section.GetProperty("classTiers").EnumerateArray().Select(Resolve).ToArray();
        if (tiers.Length != 7) diagnostics.Add("Classic enemy spell policy requires seven class level/3 tiers.");
        foreach (var mobile in mobiles.Mobiles.Values.Where(value => value.CastsMagic && value.DonorId < DaggerfallEncounterActors.FirstClassMobile))
            if (!lists.ContainsKey(mobile.DonorId)) diagnostics.Add($"Caster mobile {mobile.DonorId} has no authored enemy spell list.");
        return new(new ReadOnlyDictionary<int, string[]>(lists),
            mobiles.Mobiles.Values.Where(value => value.CastsMagic && value.DonorId >= DaggerfallEncounterActors.FirstClassMobile)
                .Select(value => value.DonorId).ToHashSet(), tiers);
    }
}
