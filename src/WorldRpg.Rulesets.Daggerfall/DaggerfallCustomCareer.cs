using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One selected custom-class trait. Targets retain the classic choice the trait needs.</summary>
internal sealed record DaggerfallCustomCareerTrait(string Id, string? Target = null);

/// <summary>
/// The durable custom-class policy around the same career record shape that predefined careers use.
/// Equipment restrictions remain named policy because the equipment owner decides an item's class.
/// </summary>
internal sealed record DaggerfallCustomCareerDefinition(
    DaggerfallCareerDefinition Career,
    DaggerfallCustomCareerTrait[] Advantages,
    DaggerfallCustomCareerTrait[] Disadvantages,
    string[] ForbiddenEquipment);

/// <summary>Typed custom-class fields carried by a character-creation draft and current saves.</summary>
internal sealed record DaggerfallCustomCareerChoices(
    string Name,
    string[] PrimarySkills,
    string[] MajorSkills,
    string[] MinorSkills,
    int HitPointsPerLevel,
    DaggerfallCustomCareerTrait[] Advantages,
    DaggerfallCustomCareerTrait[] Disadvantages)
{
    internal static DaggerfallCustomCareerChoices Default(DaggerfallDefinitions definitions, DaggerfallCareerDefinition fallback)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(fallback);
        string[] skills = definitions.Catalogs.Skills.Select(value => value.Id).ToArray();
        return new(fallback.Name, skills.Take(3).ToArray(), skills.Skip(3).Take(3).ToArray(), skills.Skip(6).Take(6).ToArray(), 8, [], []);
    }
}

internal sealed record DaggerfallCustomCareerPresentation(
    DaggerfallCustomCareerChoices Current,
    string[] Eligibility,
    string[] Skills,
    string[] SupportedAdvantages,
    string[] SupportedDisadvantages);

/// <summary>Classic custom-class validation and derivation, kept with Daggerfall policy.</summary>
internal static class DaggerfallCustomCareerPolicy
{
    internal const string CareerId = "custom";
    private const int MinimumHp = 4, MaximumHp = 30, DefaultHp = 8, MinimumDifficulty = -12, MaximumDifficulty = 40;
    private static readonly HashSet<string> Advantages = new(StringComparer.Ordinal)
    {
        "immunity", "increased-magery", "resistance",
    };
    private static readonly HashSet<string> Disadvantages = new(StringComparer.Ordinal)
    {
        "critical-weakness", "forbidden-armor", "forbidden-material", "forbidden-shield", "forbidden-weapon", "low-tolerance",
    };
    private static readonly Dictionary<string, int> TraitDifficulty = new(StringComparer.Ordinal)
    {
        ["acute-hearing"] = 1, ["adrenaline-rush"] = 4, ["athleticism"] = 4, ["bonus-to-hit"] = 6, ["expertise"] = 2, ["immunity"] = 10,
        ["increased-magery:1"] = 2, ["increased-magery:1.5"] = 4, ["increased-magery:1.75"] = 6, ["increased-magery:2"] = 8, ["increased-magery:3"] = 10,
        ["rapid-healing:general"] = 4, ["rapid-healing:darkness"] = 3, ["rapid-healing:light"] = 2,
        ["regenerate-health:general"] = 14, ["regenerate-health:darkness"] = 10, ["regenerate-health:light"] = 6, ["regenerate-health:immersed"] = 2,
        ["resistance"] = 5, ["spell-absorption:general"] = 14, ["spell-absorption:darkness"] = 12, ["spell-absorption:light"] = 8,
        ["critical-weakness"] = -14, ["damage:holy-places"] = -6, ["damage:sunlight"] = -10,
        ["darkness-powered-magery:reduced"] = -7, ["darkness-powered-magery:unable"] = -10,
        ["forbidden-armor:chain"] = -2, ["forbidden-armor:leather"] = -1, ["forbidden-armor:plate"] = -5,
        ["forbidden-material:adamantium"] = -5, ["forbidden-material:daedric"] = -2, ["forbidden-material:dwarven"] = -7, ["forbidden-material:ebony"] = -5, ["forbidden-material:elven"] = -9, ["forbidden-material:iron"] = -1, ["forbidden-material:mithril"] = -6, ["forbidden-material:orcish"] = -3, ["forbidden-material:silver"] = -6, ["forbidden-material:steel"] = -10,
        ["forbidden-shield"] = -1, ["forbidden-weapon"] = -2, ["inability-to-regen"] = -14,
        ["light-powered-magery:reduced"] = -10, ["light-powered-magery:unable"] = -14, ["low-tolerance"] = -5, ["phobia"] = -4,
    };
    private static readonly IReadOnlyDictionary<string, string[]> TraitTargets = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["bonus-to-hit"] = ["animals", "daedra", "humanoid", "undead"], ["phobia"] = ["animals", "daedra", "humanoid", "undead"],
        ["expertise"] = ["axe", "blunt-weapon", "hand-to-hand", "long-blade", "archery", "short-blade"], ["forbidden-weapon"] = ["axe", "blunt-weapon", "hand-to-hand", "long-blade", "archery", "short-blade"],
        ["immunity"] = ["disease"], ["resistance"] = ["disease"], ["critical-weakness"] = ["disease"], ["low-tolerance"] = ["disease"],
        ["increased-magery"] = ["1", "1.5", "1.75", "2", "3"], ["rapid-healing"] = ["general", "darkness", "light"], ["spell-absorption"] = ["general", "darkness", "light"], ["regenerate-health"] = ["general", "darkness", "light", "immersed"], ["damage"] = ["holy-places", "sunlight"], ["darkness-powered-magery"] = ["reduced", "unable"], ["light-powered-magery"] = ["reduced", "unable"], ["forbidden-armor"] = ["chain", "leather", "plate"], ["forbidden-material"] = ["adamantium", "daedric", "dwarven", "ebony", "elven", "iron", "mithril", "orcish", "silver", "steel"], ["forbidden-shield"] = ["buckler", "kite-shield", "round-shield", "tower-shield"],
    };

    internal static IReadOnlyList<string> SupportedAdvantages => Advantages.Order(StringComparer.Ordinal).ToArray();
    internal static IReadOnlyList<string> SupportedDisadvantages => Disadvantages.Order(StringComparer.Ordinal).ToArray();

    internal static DaggerfallCustomCareerDefinition Compile(DaggerfallDefinitions definitions, DaggerfallCustomCareerChoices choices, DaggerfallCareerDefinition attributeBase)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(attributeBase);
        List<string> errors = Validate(definitions, choices);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors));
        int resistance = Flags(choices.Advantages, "resistance");
        int immunity = Flags(choices.Advantages, "immunity");
        int lowTolerance = Flags(choices.Disadvantages, "low-tolerance");
        int criticalWeakness = Flags(choices.Disadvantages, "critical-weakness");
        int difficulty = Difficulty(choices);
        int multiplier = MageryMultiplier(choices.Advantages);
        DaggerfallCareerDefinition career = new(CareerId, choices.Name.Trim(), choices.PrimarySkills, choices.MajorSkills, choices.MinorSkills,
            attributeBase.Attributes, attributeBase.AttributeValues, choices.HitPointsPerLevel, multiplier,
            0.3f + (2.7f * (difficulty + 12) / 52f), Elements(resistance), Elements(immunity), resistance, immunity, lowTolerance, criticalWeakness,
            new DaggerfallCatalogCitation("F006", "custom:character-creation"));
        string[] forbidden = choices.Disadvantages.Where(trait => trait.Id is "forbidden-armor" or "forbidden-material" or "forbidden-shield" or "forbidden-weapon")
            .Select(trait => $"{trait.Id}:{trait.Target}").Order(StringComparer.Ordinal).ToArray();
        return new(career, choices.Advantages, choices.Disadvantages, forbidden);
    }

    internal static List<string> Validate(DaggerfallDefinitions definitions, DaggerfallCustomCareerChoices choices)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(choices.Name)) errors.Add("A custom class needs a name.");
        if (choices.PrimarySkills.Length != 3 || choices.MajorSkills.Length != 3 || choices.MinorSkills.Length != 6) errors.Add("Choose exactly 3 primary, 3 major, and 6 minor skills.");
        string[] skills = [.. choices.PrimarySkills, .. choices.MajorSkills, .. choices.MinorSkills];
        if (skills.Distinct(StringComparer.Ordinal).Count() != skills.Length) errors.Add("Each trained skill may appear only once.");
        HashSet<string> knownSkills = definitions.Catalogs.Skills.Select(skill => skill.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string skill in skills.Where(skill => !knownSkills.Contains(skill))) errors.Add($"'{skill}' is not a published Daggerfall skill.");
        if (choices.HitPointsPerLevel is < MinimumHp or > MaximumHp) errors.Add($"Hit points per level must be from {MinimumHp} to {MaximumHp}.");
        ValidateTraits(choices.Advantages, Advantages, "advantage", errors);
        ValidateTraits(choices.Disadvantages, Disadvantages, "disadvantage", errors);
        ValidatePairs(choices.Advantages, choices.Disadvantages, errors);
        int difficulty = Difficulty(choices);
        if (difficulty is < MinimumDifficulty or > MaximumDifficulty) errors.Add($"Classic difficulty must remain from {MinimumDifficulty} to {MaximumDifficulty}; this class is {difficulty}.");
        return errors;
    }

    internal static int Difficulty(DaggerfallCustomCareerChoices choices)
    {
        int hp = choices.HitPointsPerLevel >= DefaultHp ? choices.HitPointsPerLevel - DefaultHp : -2 * (DefaultHp - choices.HitPointsPerLevel);
        return checked(hp + choices.Advantages.Sum(TraitPoints) + choices.Disadvantages.Sum(TraitPoints));
    }

    private static void ValidateTraits(IEnumerable<DaggerfallCustomCareerTrait> traits, HashSet<string> supported, string role, List<string> errors)
    {
        DaggerfallCustomCareerTrait[] all = traits.ToArray();
        if (all.Length > 7) errors.Add($"A custom class supports at most 7 {role}s.");
        foreach (DaggerfallCustomCareerTrait trait in all)
        {
            if (!supported.Contains(trait.Id)) errors.Add($"'{trait.Id}' is not a supported {role}.");
            if (TraitTargets.TryGetValue(trait.Id, out string[]? targets) && (trait.Target is null || !targets.Contains(trait.Target, StringComparer.Ordinal)))
                errors.Add($"'{trait.Id}' has an unsupported target '{trait.Target}'.");
            else if (!TraitTargets.ContainsKey(trait.Id) && trait.Target is not null)
                errors.Add($"'{trait.Id}' does not take a target.");
        }
        if (all.Select(Key).Distinct(StringComparer.Ordinal).Count() != all.Length) errors.Add($"Duplicate {role}s are not allowed.");
        foreach (string id in new[] { "increased-magery", "darkness-powered-magery", "light-powered-magery" })
            if (all.Count(trait => trait.Id == id) > 1) errors.Add($"'{id}' may be chosen only once.");
    }

    private static void ValidatePairs(IEnumerable<DaggerfallCustomCareerTrait> advantages, IEnumerable<DaggerfallCustomCareerTrait> disadvantages, List<string> errors)
    {
        foreach (DaggerfallCustomCareerTrait left in advantages.Concat(disadvantages))
        foreach (DaggerfallCustomCareerTrait right in advantages.Concat(disadvantages))
        {
            if (ReferenceEquals(left, right) || !StringComparer.Ordinal.Equals(left.Target, right.Target)) continue;
            bool forbidden = (left.Id, right.Id) is ("bonus-to-hit", "phobia") or ("phobia", "bonus-to-hit") or ("expertise", "forbidden-weapon") or ("forbidden-weapon", "expertise")
                || new[] { "immunity", "resistance", "low-tolerance", "critical-weakness" }.Contains(left.Id, StringComparer.Ordinal)
                && new[] { "immunity", "resistance", "low-tolerance", "critical-weakness" }.Contains(right.Id, StringComparer.Ordinal) && left.Id != right.Id;
            if (forbidden) errors.Add($"'{left.Id}' and '{right.Id}' cannot both target '{left.Target}'.");
        }
    }

    private static int TraitPoints(DaggerfallCustomCareerTrait trait) => TraitDifficulty.GetValueOrDefault(Key(trait), TraitDifficulty.GetValueOrDefault(trait.Id));
    private static string Key(DaggerfallCustomCareerTrait trait) => trait.Target is { Length: > 0 } target ? $"{trait.Id}:{target}" : trait.Id;
    private static int MageryMultiplier(IEnumerable<DaggerfallCustomCareerTrait> advantages) => advantages.SingleOrDefault(trait => trait.Id == "increased-magery")?.Target switch
    {
        "1" => 1000, "1.5" => 1500, "1.75" => 1750, "2" => 2000, "3" => 3000, _ => 500,
    };
    private static int Flags(IEnumerable<DaggerfallCustomCareerTrait> traits, string id) => traits.Where(trait => trait.Id == id).Aggregate(0, (flags, trait) => flags | Flag(trait.Target));
    private static int Flag(string? target) => target switch { "fire" => 8, "frost" => 16, "disease" or "poison" => 64, "shock" => 32, "magic" => 2, "paralysis" => 1, _ => 0 };
    private static IReadOnlyList<string> Elements(int flags) => new[] { (8, "fire"), (16, "frost"), (64, "disease-or-poison"), (32, "shock"), (2, "magic") }.Where(value => (flags & value.Item1) != 0).Select(value => value.Item2).ToArray();

    /// <summary>Interprets the retained classic restriction choices against the admitted item record.</summary>
    internal static bool Forbids(DaggerfallItemDefinition item, IEnumerable<string> restrictions, out string reason)
    {
        foreach (string restriction in restrictions)
        {
            string[] pair = restriction.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2) continue;
            string kind = pair[0], target = pair[1];
            bool forbidden = kind switch
            {
                "forbidden-material" => item.Weapon?.Material == target || item.Armor?.Material == target,
                "forbidden-weapon" => item.Weapon?.Skill == target,
                "forbidden-shield" => item.Shield is not null && ShieldKind(item) == target,
                "forbidden-armor" => ArmorClass(item) == target,
                _ => false,
            };
            if (forbidden)
            {
                reason = $"Your custom class forbids {target.Replace('-', ' ')} {kind.Replace("forbidden-", string.Empty)}.";
                return true;
            }
        }
        reason = string.Empty;
        return false;
    }

    private static string? ArmorClass(DaggerfallItemDefinition item) => item.Armor?.Material switch
    {
        null => null,
        "leather" => "leather",
        "chain" => "chain",
        _ => "plate",
    };

    /// <summary>Shield type is its stable classic template identity, not a materialized item id.</summary>
    private static string? ShieldKind(DaggerfallItemDefinition item) => item.Template?.Index switch
    {
        109 => "buckler", 110 => "round-shield", 111 => "kite-shield", 112 => "tower-shield",
        _ => item.Id.Value is "buckler" or "round-shield" or "kite-shield" or "tower-shield" ? item.Id.Value : null,
    };
}
