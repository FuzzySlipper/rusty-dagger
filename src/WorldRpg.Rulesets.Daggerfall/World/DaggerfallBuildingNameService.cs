using System.Text.RegularExpressions;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The outcome of resolving a real RMB building's classic display name.</summary>
internal sealed record DaggerfallBuildingNameResult(string Name, string? Unresolved)
{
    internal bool IsResolved => Unresolved is null;
    internal static DaggerfallBuildingNameResult Missing(string reason) => new(string.Empty, reason);
    internal static DaggerfallBuildingNameResult Resolved(string name) => new(name, null);
}

/// <summary>
/// Daggerfall's exact building-name policy. It resolves an actual admitted RMB building plus an
/// explicit site identity, so maps, dialogue and quest-place consumers cannot invent a seed, type or
/// faction outside the published block and site records.
/// </summary>
internal sealed class DaggerfallBuildingNameService(
    IRandomService random,
    DaggerfallDefinitions definitions,
    DaggerfallBlocksSnapshot blocks,
    DaggerfallSiteContext sites)
{
    private const int Alchemist = 0;
    private const int HouseForSale = 1;
    private const int Armorer = 2;
    private const int Bank = 3;
    private const int Bookseller = 5;
    private const int ClothingStore = 6;
    private const int FurnitureStore = 7;
    private const int GemStore = 8;
    private const int GeneralStore = 9;
    private const int Library = 10;
    private const int GuildHall = 11;
    private const int PawnShop = 12;
    private const int WeaponSmith = 13;
    private const int Temple = 14;
    private const int Tavern = 15;
    private const int Palace = 16;
    private const int Town23 = 23;

    private readonly IRandomService _random = random ?? throw new ArgumentNullException(nameof(random));
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly DaggerfallBlocksSnapshot _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    private readonly DaggerfallSiteContext _sites = sites ?? throw new ArgumentNullException(nameof(sites));

    /// <summary>Resolves one real block building at a site, or returns the exact missing input.</summary>
    internal DaggerfallBuildingNameResult Resolve(DaggerfallSiteId siteId, DaggerfallRmbBuildingId buildingId)
    {
        if (!_sites.TryFind(siteId, out DaggerfallSiteRecord? site))
        {
            return DaggerfallBuildingNameResult.Missing($"Building name site '{siteId}' is not published.");
        }

        if (!_blocks.RmbBuildings.TryGetValue(buildingId, out DaggerfallRmbBuildingSource? building))
        {
            return DaggerfallBuildingNameResult.Missing($"Building name source '{buildingId}' is not a published RMB building.");
        }

        // The donor returns this localized value before it draws from DFRandom. Preserve that ordering:
        // even a supplied RNG must observe no request for a house-for-sale name.
        if (building.BuildingType == HouseForSale)
        {
            return Localized("houseForSale");
        }

        uint state = unchecked((uint)building.NameSeed);
        string? a = null;
        string? b = null;
        bool singleton = false;
        switch (building.BuildingType)
        {
            case Tavern:
                if (!Pick("TavernsB", ref state, out b, out string? tavernB)) return Missing(tavernB!);
                if (!Pick("TavernsA", ref state, out a, out string? tavernA)) return Missing(tavernA!);
                break;
            case Alchemist:
                if (!Store("AlchemyStoresB", ref state, out a, out b, out string? alchemy)) return Missing(alchemy!);
                break;
            case Armorer:
                if (!Store("ArmorStoresB", ref state, out a, out b, out string? armor)) return Missing(armor!);
                break;
            case Bookseller:
                if (!Store("BookStoresB", ref state, out a, out b, out string? books)) return Missing(books!);
                break;
            case ClothingStore:
                if (!Store("ClothingStoresB", ref state, out a, out b, out string? clothing)) return Missing(clothing!);
                break;
            case FurnitureStore:
                if (!Store("FurnitureStoresB", ref state, out a, out b, out string? furniture)) return Missing(furniture!);
                break;
            case GemStore:
                if (!Store("GemStoresB", ref state, out a, out b, out string? gems)) return Missing(gems!);
                break;
            case GeneralStore:
                if (!Store("GeneralStoresB", ref state, out a, out b, out string? general)) return Missing(general!);
                break;
            case Library:
                if (!Store("LibraryStoresB", ref state, out a, out b, out string? library)) return Missing(library!);
                break;
            case PawnShop:
                if (!Store("PawnStoresB", ref state, out a, out b, out string? pawn)) return Missing(pawn!);
                break;
            case WeaponSmith:
                if (!Store("WeaponStoresB", ref state, out a, out b, out string? weapon)) return Missing(weapon!);
                break;
            case Bank:
                if (!Localized("theBankOf", out a, out string? bank)) return Missing(bank!);
                b = site.Name;
                break;
            case GuildHall:
                if (!_definitions.Factions.Factions.TryGetValue(building.FactionId, out DaggerfallFactionDefinition? guild)) return Missing($"Guild hall '{buildingId}' names faction {building.FactionId}, which the faction catalog does not carry.");
                a = guild.Name;
                singleton = true;
                break;
            case Temple:
                if (!_definitions.Factions.Factions.TryGetValue(building.FactionId, out DaggerfallFactionDefinition? temple)) return Missing($"Temple '{buildingId}' names faction {building.FactionId}, which the faction catalog does not carry.");
                if (temple.Children.Count == 0 || !_definitions.Factions.Factions.TryGetValue(temple.Children[0], out DaggerfallFactionDefinition? deity)) return Missing($"Temple faction {building.FactionId} names no published first child for its display name.");
                a = deity.Name;
                singleton = true;
                break;
            case Palace:
                if (!ResolvePalace(site.Name, out a, out string? palace)) return Missing(palace!);
                singleton = true;
                break;
            case Town23:
                if (!Localized("cityWall", out a, out string? wall)) return Missing(wall!);
                singleton = true;
                break;
            default:
                return DaggerfallBuildingNameResult.Resolved(string.Empty);
        }

        if (a is null) return Missing($"Building '{buildingId}' produced no donor name prefix.");
        if (!Expand(a, site, ref state, out a, out string? prefix)) return Missing(prefix!);
        if (!singleton)
        {
            if (b is null) return Missing($"Building '{buildingId}' produced no donor name suffix.");
            if (!Expand(b, site, ref state, out b, out string? suffix)) return Missing(suffix!);
        }

        return DaggerfallBuildingNameResult.Resolved(singleton ? a : $"{a} {b}");
    }

    private DaggerfallBuildingNameResult Localized(string key) =>
        Localized(key, out string? value, out string? reason) ? DaggerfallBuildingNameResult.Resolved(value!) : Missing(reason!);

    private static DaggerfallBuildingNameResult Missing(string reason) => DaggerfallBuildingNameResult.Missing(reason);

    private bool Store(string suffixKey, ref uint state, out string? a, out string? b, out string? reason)
    {
        if (!Pick(suffixKey, ref state, out b, out reason))
        {
            a = null;
            return false;
        }

        return Pick("StoresA", ref state, out a, out reason);
    }

    private bool ResolvePalace(string locationName, out string? name, out string? reason)
    {
        if (!Localized("daggerfall", out string? daggerfall, out reason))
        {
            name = null;
            return false;
        }
        if (!Localized("wayrest", out string? wayrest, out reason))
        {
            name = null;
            return false;
        }
        if (!Localized("sentinel", out string? sentinel, out reason))
        {
            name = null;
            return false;
        }

        string? key = string.Equals(locationName, daggerfall, StringComparison.Ordinal) ? "475"
            : string.Equals(locationName, wayrest, StringComparison.Ordinal) ? "476"
            : string.Equals(locationName, sentinel, StringComparison.Ordinal) ? "477"
            : null;
        if (key is null) return Localized("palace", out name, out reason);
        if (!Text(new DaggerfallTextKey(DaggerfallTextKind.Resource, key), out name, out reason)) return false;
        name = name!.TrimEnd('.');
        return true;
    }

    private bool Expand(string template, DaggerfallSiteRecord site, ref uint state, out string value, out string? reason)
    {
        value = template.Replace("%cn", site.Name, StringComparison.Ordinal);
        if (value.Contains("%ef", StringComparison.Ordinal))
        {
            if (!FirstName(site.Region, ref state, out string? firstName, out reason)) return false;
            value = value.Replace("%ef", firstName!, StringComparison.Ordinal);
        }
        if (value.Contains("%rt", StringComparison.Ordinal))
        {
            if (!RegentTitle(site.Region, out string? title, out reason)) return false;
            value = value.Replace("%rt", title!, StringComparison.Ordinal);
        }

        Match macro = Regex.Match(value, "%[A-Za-z0-9]*", RegexOptions.CultureInvariant);
        if (macro.Success)
        {
            reason = $"Building name template '{template}' carries unsupported macro '{macro.Value}'.";
            return false;
        }

        reason = null;
        return true;
    }

    private bool FirstName(int region, ref uint state, out string? name, out string? reason)
    {
        _ = Draw(ref state, 32768); // FormulaHelper burns one rand() before NameHelper.FirstName.
        if (!_definitions.BuildingNames.TryGetNameBank(region, out int bank))
        {
            name = null;
            reason = $"Building name region {region} has no published Breton/Redguard name-bank mapping.";
            return false;
        }

        DaggerfallNameBankDefinition? table = _definitions.Names.Banks.SingleOrDefault(candidate => candidate.Bank == bank);
        if (table is null)
        {
            name = null;
            reason = $"Building name bank {bank} is not published.";
            return false;
        }

        if (bank == 0) return ComposeName(table, [0, 1], ref state, out name, out reason);
        if (!ComposeName(table, [0, 1, 2], ref state, out string? three, out reason))
        {
            name = null;
            return false;
        }
        if (Draw(ref state, 100) < 75)
        {
            if (!ComposeName(table, [3], ref state, out string? fourth, out reason))
            {
                name = null;
                return false;
            }
            name = three + fourth;
            return true;
        }

        name = three;
        reason = null;
        return true;
    }

    private bool ComposeName(DaggerfallNameBankDefinition bank, IReadOnlyList<int> sets, ref uint state, out string? name, out string? reason)
    {
        List<string> pieces = [];
        foreach (int setIndex in sets)
        {
            DaggerfallNameSetDefinition? set = bank.Sets.SingleOrDefault(candidate => candidate.Set == setIndex);
            if (set is null || set.Keys.Count == 0)
            {
                name = null;
                reason = $"Building name bank '{bank.Name}' has no published fragment set {setIndex}.";
                return false;
            }
            if (!Text(set.Keys[checked((int)Draw(ref state, checked((uint)set.Keys.Count)))], out string? piece, out reason))
            {
                name = null;
                return false;
            }
            pieces.Add(piece!);
        }

        name = string.Concat(pieces);
        reason = null;
        return true;
    }

    private bool RegentTitle(int region, out string? title, out string? reason)
    {
        DaggerfallFactionDefinition[] provinces = [.. _definitions.Factions.Factions.Values.Where(faction => faction.Type == 7 && faction.Region == region)];
        if (provinces.Length != 1)
        {
            title = null;
            reason = $"Building name region {region} resolves {provinces.Length} province factions; %rt requires exactly one.";
            return false;
        }

        string key = provinces[0].Ruler switch
        {
            1 => "King", 2 => "Queen", 3 => "Duke", 4 => "Duchess", 5 => "Marquis", 6 => "Marquise",
            7 => "Count", 8 => "Countess", 9 => "Baron", 10 => "Baroness", 11 => "Lord", 12 => "Lady", _ => "Lord",
        };
        return Localized(key, out title, out reason);
    }

    private bool Pick(string key, ref uint state, out string? value, out string? reason)
    {
        if (!Localized(key, out string? list, out reason))
        {
            value = null;
            return false;
        }
        string[] choices = list!.TrimEnd('\r', '\n').Split(["\r\n", "\n"], StringSplitOptions.None);
        if (choices.Length == 0 || choices.Any(choice => choice.Length == 0))
        {
            value = null;
            reason = $"Localized building list '{key}' carries no usable entries.";
            return false;
        }
        value = choices[Draw(ref state, checked((uint)choices.Length))];
        return true;
    }

    private bool Localized(string key, out string? value, out string? reason) =>
        Text(new DaggerfallTextKey(DaggerfallTextKind.Internal, key), out value, out reason);

    private bool Text(DaggerfallTextKey key, out string? value, out string? reason)
    {
        switch (_definitions.Text.Resolve(key, out DaggerfallTextValue? text))
        {
            case DaggerfallTextResolution.Resolved:
                value = string.Concat(text!.TextRuns);
                reason = null;
                return true;
            case DaggerfallTextResolution.Malformed:
                value = null;
                reason = $"Building name text '{key}' is malformed: {text!.Reason}";
                return false;
            default:
                value = null;
                reason = $"Building name text '{key}' is not published.";
                return false;
        }
    }

    private uint Draw(ref uint state, uint upperExclusive)
    {
        Lcg15Receipt receipt = _random.DrawLcg15(new Lcg15Request(state, upperExclusive));
        state = receipt.State;
        return receipt.Value;
    }
}
