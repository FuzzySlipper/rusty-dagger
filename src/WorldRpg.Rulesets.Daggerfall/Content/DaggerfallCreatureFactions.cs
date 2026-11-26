using System.Collections.ObjectModel;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One authored creature-family faction in <c>daggerfall.base</c>, mapping 1:1 to the 22 classic
/// <c>DaggerfallFoeTeams</c> so both social standing and creature-vs-creature / faction aggro share one backing model.
/// </summary>
internal sealed record DaggerfallCreatureFactionDefinition(
    string Id,
    int TeamIndex,
    string Name,
    bool HostileToPlayer,
    IReadOnlyList<string> Allies,
    IReadOnlyList<string> Enemies);

/// <summary>Authored diplomatic/combat relation between two creature-family factions.</summary>
internal enum DaggerfallCreatureFactionRelation
{
    Ally = 0,
    Neutral = 1,
    Enemy = 2,
}

/// <summary>The authored creature-family faction catalog loaded from <c>daggerfall.base</c>.</summary>
internal sealed class DaggerfallCreatureFactionSet(IReadOnlyDictionary<string, DaggerfallCreatureFactionDefinition> factions)
{
    internal IReadOnlyDictionary<string, DaggerfallCreatureFactionDefinition> Factions { get; } =
        new ReadOnlyDictionary<string, DaggerfallCreatureFactionDefinition>(
            factions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    internal bool TryGet(string? id, out DaggerfallCreatureFactionDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            definition = null;
            return false;
        }
        return Factions.TryGetValue(id, out definition);
    }

    /// <summary>
    /// Evaluates the authored creature-faction relation between <paramref name="observerTeam"/> and
    /// <paramref name="targetTeam"/>: same team or declared allies yield <see cref="DaggerfallCreatureFactionRelation.Ally"/>,
    /// declared enemies yield <see cref="DaggerfallCreatureFactionRelation.Enemy"/>, and all other pairs are
    /// <see cref="DaggerfallCreatureFactionRelation.Neutral"/>.
    /// </summary>
    internal DaggerfallCreatureFactionRelation Relation(string? observerTeam, string? targetTeam)
    {
        if (string.IsNullOrWhiteSpace(observerTeam) || string.IsNullOrWhiteSpace(targetTeam))
            return DaggerfallCreatureFactionRelation.Neutral;
        if (string.Equals(observerTeam, targetTeam, StringComparison.Ordinal))
            return DaggerfallCreatureFactionRelation.Ally;

        bool hasObserver = TryGet(observerTeam, out DaggerfallCreatureFactionDefinition? observer);
        bool hasTarget = TryGet(targetTeam, out DaggerfallCreatureFactionDefinition? target);

        if ((hasObserver && ContainsId(observer!.Allies, targetTeam))
            || (hasTarget && ContainsId(target!.Allies, observerTeam)))
            return DaggerfallCreatureFactionRelation.Ally;

        if ((hasObserver && ContainsId(observer!.Enemies, targetTeam))
            || (hasTarget && ContainsId(target!.Enemies, observerTeam)))
            return DaggerfallCreatureFactionRelation.Enemy;

        return DaggerfallCreatureFactionRelation.Neutral;
    }

    private static bool ContainsId(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], id, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Whether two creature teams are the same faction or explicitly allied.</summary>
    internal bool AreAllies(string? observerTeam, string? targetTeam) =>
        Relation(observerTeam, targetTeam) == DaggerfallCreatureFactionRelation.Ally;

    /// <summary>Whether two creature teams are authored natural enemies that may infight when truce/clash rules permit.</summary>
    internal bool AreNaturalEnemies(string? observerTeam, string? targetTeam) =>
        Relation(observerTeam, targetTeam) == DaggerfallCreatureFactionRelation.Enemy;

    /// <summary>
    /// Whether an actor on <paramref name="observerTeam"/> treats <paramref name="targetTeam"/> as hostile under
    /// classic team/faction rules. Same-faction and explicitly allied creature families do not fight each other;
    /// distinct non-allied teams fight when quest infighting is enabled (except pacified actors leave <c>player-ally</c> alone).
    /// </summary>
    internal bool AreTeamsHostile(string? observerTeam, string? targetTeam, bool observerPacified)
    {
        if (string.Equals(observerTeam, targetTeam, StringComparison.Ordinal)) return false;
        if (string.Equals(targetTeam, "player-ally", StringComparison.Ordinal) && observerPacified) return false;
        if (AreAllies(observerTeam, targetTeam)) return false;
        return true;
    }

    internal static DaggerfallCreatureFactionSet ClassicDefault { get; } = CreateClassicDefault();

    internal static DaggerfallCreatureFactionSet Read(JsonElement section, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallCreatureFactionDefinition> result = new(StringComparer.Ordinal);
        if (section.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add("Base payload section 'creatureFactions' must be an array.");
            return ClassicDefault;
        }
        foreach (JsonElement item in section.EnumerateArray())
        {
            JsonElement obj = DaggerfallBaseContent.Object(item, "creatureFaction", diagnostics);
            string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
            int teamIndex = DaggerfallBaseContent.Integer(obj, "teamIndex", diagnostics);
            string name = DaggerfallBaseContent.Text(obj, "name", diagnostics);
            bool hostile = obj.TryGetProperty("hostileToPlayer", out JsonElement h) && h.ValueKind == JsonValueKind.True;
            string[] allies = ReadStringList(obj, "allies", diagnostics);
            string[] enemies = ReadStringList(obj, "enemies", diagnostics);
            if (!DaggerfallBaseContent.ValidId(id) || teamIndex < 0)
            {
                diagnostics.Add($"Creature faction '{id}' has an invalid id or negative teamIndex.");
                continue;
            }
            if (!result.TryAdd(id, new(id, teamIndex, name, hostile, allies, enemies)))
                diagnostics.Add($"Creature faction '{id}' is repeated in creatureFactions.");
        }
        foreach (DaggerfallCreatureFactionDefinition faction in result.Values)
        {
            foreach (string ally in faction.Allies)
            {
                if (!result.ContainsKey(ally)) diagnostics.Add($"Creature faction '{faction.Id}' names unknown ally '{ally}'.");
                if (string.Equals(ally, faction.Id, StringComparison.Ordinal)) diagnostics.Add($"Creature faction '{faction.Id}' cannot list itself as an ally.");
            }
            foreach (string enemy in faction.Enemies)
            {
                if (!result.ContainsKey(enemy)) diagnostics.Add($"Creature faction '{faction.Id}' names unknown enemy '{enemy}'.");
                if (string.Equals(enemy, faction.Id, StringComparison.Ordinal)) diagnostics.Add($"Creature faction '{faction.Id}' cannot list itself as an enemy.");
                if (faction.Allies.Contains(enemy, StringComparer.Ordinal)) diagnostics.Add($"Creature faction '{faction.Id}' cannot list '{enemy}' as both ally and enemy.");
            }
        }
        return new(result);
    }

    private static string[] ReadStringList(JsonElement obj, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!obj.TryGetProperty(property, out JsonElement element)) return [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add($"Creature faction property '{property}' must be an array.");
            return [];
        }
        List<string> list = [];
        foreach (JsonElement item in element.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value) list.Add(value);
        return [.. list];
    }

    private static DaggerfallCreatureFactionSet CreateClassicDefault()
    {
        (string Id, int Index, string Name, bool Hostile, string[] Allies, string[] Enemies)[] classic =
        [
            ("player-enemy", 0, "Player Enemies", true, [], []),
            ("player-ally", 1, "Player Allies", false, [], []),
            ("vermin", 2, "Vermin", true, [], ["bears", "tigers", "spiders", "scorpions", "city-watch"]),
            ("spriggans", 3, "Spriggans", true, ["nymphs", "centaurs", "bears"], ["orcs", "undead", "daedra", "criminals", "spiders", "werecreatures"]),
            ("bears", 4, "Bears", true, ["spriggans"], ["tigers", "spiders", "scorpions", "werecreatures", "criminals", "city-watch", "vermin", "aquatic"]),
            ("tigers", 5, "Tigers", true, [], ["bears", "spiders", "scorpions", "werecreatures", "criminals", "city-watch", "vermin", "aquatic"]),
            ("spiders", 6, "Spiders", true, [], ["scorpions", "vermin", "bears", "tigers", "spriggans", "city-watch", "knights-and-mages", "criminals"]),
            ("orcs", 7, "Orcs", true, [], ["undead", "daedra", "city-watch", "knights-and-mages", "criminals", "centaurs", "spriggans", "nymphs", "werecreatures", "giants", "magic", "aquatic"]),
            ("centaurs", 8, "Centaurs", true, ["spriggans", "nymphs"], ["orcs", "undead", "daedra", "giants", "harpies"]),
            ("werecreatures", 9, "Werecreatures", true, [], ["undead", "orcs", "daedra", "city-watch", "knights-and-mages", "criminals", "bears", "tigers", "spriggans"]),
            ("nymphs", 10, "Nymphs", true, ["spriggans", "centaurs"], ["undead", "daedra", "orcs", "harpies"]),
            ("aquatic", 11, "Aquatic Creatures", true, [], ["bears", "tigers", "orcs"]),
            ("harpies", 12, "Harpies", true, [], ["nymphs", "centaurs", "city-watch", "knights-and-mages", "dragonlings"]),
            ("undead", 13, "Undead", true, [], ["orcs", "daedra", "city-watch", "knights-and-mages", "criminals", "spriggans", "centaurs", "nymphs", "werecreatures", "magic"]),
            ("giants", 14, "Giants", true, [], ["orcs", "centaurs", "dragonlings", "city-watch"]),
            ("scorpions", 15, "Scorpions", true, [], ["spiders", "vermin", "bears", "tigers", "city-watch", "knights-and-mages", "criminals"]),
            ("magic", 16, "Magical Constructs", true, ["knights-and-mages"], ["daedra", "undead", "orcs", "criminals"]),
            ("daedra", 17, "Daedra", true, [], ["undead", "orcs", "city-watch", "knights-and-mages", "criminals", "spriggans", "centaurs", "nymphs", "dragonlings", "magic", "werecreatures"]),
            ("dragonlings", 18, "Dragonlings", true, [], ["daedra", "giants", "harpies", "city-watch", "knights-and-mages"]),
            ("knights-and-mages", 19, "Knights and Mages", true, ["city-watch", "magic"], ["criminals", "orcs", "undead", "daedra", "werecreatures", "harpies", "dragonlings", "spiders", "scorpions"]),
            ("criminals", 20, "Criminals", true, [], ["city-watch", "knights-and-mages", "undead", "daedra", "orcs", "werecreatures", "bears", "tigers", "spiders", "scorpions", "spriggans", "magic"]),
            ("city-watch", 21, "City Watch", true, ["knights-and-mages"], ["criminals", "orcs", "undead", "daedra", "werecreatures", "giants", "harpies", "spiders", "scorpions", "dragonlings", "bears", "tigers", "vermin"]),
        ];
        return new(classic.ToDictionary(
            item => item.Id,
            item => new DaggerfallCreatureFactionDefinition(item.Id, item.Index, item.Name, item.Hostile, item.Allies, item.Enemies),
            StringComparer.Ordinal));
    }
}

/// <summary>
/// Maps downstream political board-game <see cref="DaggerfallWorldConditionToken"/>s (such as <c>MonsterSurge</c>,
/// <c>BorderTension</c>, <c>CrimeCrackdown</c>, and <c>GrainShortage</c>) onto the authored 22 creature-family
/// factions so creature clashes remain downstream of board-game pressure rather than a separate background sim.
/// </summary>
internal static class DaggerfallCreatureFactionPolicy
{
    private static readonly string[] BeastFactions = ["bears", "tigers", "spiders", "scorpions", "werecreatures", "vermin"];
    private static readonly string[] WoodlandFactions = ["spriggans", "nymphs", "centaurs"];
    private static readonly string[] GenericSurgeFactions = ["orcs", "undead", "daedra"];
    private static readonly string[] BorderPatrolFactions = ["knights-and-mages", "city-watch"];
    private static readonly string[] WatchFactions = ["city-watch", "knights-and-mages"];

    /// <summary>Returns the creature-faction ids pressured or mobilized by <paramref name="token"/>.</summary>
    internal static IReadOnlyList<string> FactionsForCondition(DaggerfallWorldConditionToken token, DaggerfallCreatureFactionSet? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        string subject = token.SubjectTag?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (token.Kind)
        {
            case DaggerfallWorldConditionKind.MonsterSurge:
                return subject switch
                {
                    "orcs" or "orc" => ["orcs"],
                    "undead" => ["undead"],
                    "beasts" or "animals" => BeastFactions,
                    "daedra" => ["daedra"],
                    "bandits" or "criminals" => ["criminals"],
                    "spriggans" or "woodland" => WoodlandFactions,
                    "" => GenericSurgeFactions,
                    _ when (catalog ?? DaggerfallCreatureFactionSet.ClassicDefault).TryGet(subject, out _) => [subject],
                    _ => GenericSurgeFactions,
                };
            case DaggerfallWorldConditionKind.BorderTension:
                return BorderPatrolFactions;
            case DaggerfallWorldConditionKind.CrimeCrackdown:
                return WatchFactions;
            case DaggerfallWorldConditionKind.GrainShortage:
                return ["criminals"];
            default:
                return [];
        }
    }

    /// <summary>
    /// Whether any active condition in <paramref name="activeConditions"/> drives creature-faction clash pressure
    /// involving either <paramref name="teamA"/> or <paramref name="teamB"/>, and the two factions are authored natural enemies.
    /// </summary>
    internal static bool IsConditionDrivingClash(
        DaggerfallCreatureFactionSet factions,
        IReadOnlyList<DaggerfallWorldConditionToken> activeConditions,
        string? teamA,
        string? teamB)
    {
        if (activeConditions.Count == 0 || !factions.AreNaturalEnemies(teamA, teamB))
            return false;

        foreach (DaggerfallWorldConditionToken token in activeConditions)
        {
            IReadOnlyList<string> driven = FactionsForCondition(token, factions);
            for (int i = 0; i < driven.Count; i++)
            {
                if (string.Equals(driven[i], teamA, StringComparison.Ordinal)
                    || string.Equals(driven[i], teamB, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Returns whether <paramref name="activeConditions"/> contains any condition capable of driving creature-faction clashes.</summary>
    internal static bool HasClashDrivingCondition(IReadOnlyList<DaggerfallWorldConditionToken> activeConditions)
    {
        for (int i = 0; i < activeConditions.Count; i++)
        {
            if (activeConditions[i].Kind is DaggerfallWorldConditionKind.MonsterSurge
                or DaggerfallWorldConditionKind.BorderTension
                or DaggerfallWorldConditionKind.CrimeCrackdown
                or DaggerfallWorldConditionKind.GrainShortage)
                return true;
        }
        return false;
    }

    /// <summary>Returns the ordered distinct authored rival creature factions opposing <paramref name="token"/>.</summary>
    internal static IReadOnlyList<string> RivalFactionsForCondition(DaggerfallCreatureFactionSet factions, DaggerfallWorldConditionToken token)
    {
        IReadOnlyList<string> primary = FactionsForCondition(token, factions);
        if (primary.Count == 0)
            return [];

        HashSet<string> primarySet = new(primary, StringComparer.Ordinal);
        List<string> rivals = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string factionId in primary)
        {
            if (!factions.TryGet(factionId, out DaggerfallCreatureFactionDefinition? def) || def is null)
                continue;
            foreach (string enemy in def.Enemies)
            {
                if (!primarySet.Contains(enemy) && seen.Add(enemy))
                    rivals.Add(enemy);
            }
        }
        return rivals;
    }
}

/// <summary>
/// Unified faction backing attached to every live actor in <c>EntityStore</c> (people and creatures alike).
/// People carry their classic service/guild/individual <see cref="PrimaryFactionId"/> plus their site's
/// <see cref="RegionalPeopleFactionId"/> (even inside buildings); creatures carry one of the 22 authored
/// <see cref="CreatureFactionId"/> families.
/// </summary>
internal sealed class FactionComponent
{
    internal FactionComponent(int? primaryFactionId = null, int? regionalPeopleFactionId = null, string? creatureFactionId = null, string? teamOverride = null)
    {
        PrimaryFactionId = primaryFactionId;
        RegionalPeopleFactionId = regionalPeopleFactionId;
        CreatureFactionId = creatureFactionId;
        TeamOverride = teamOverride;
    }

    internal int? PrimaryFactionId { get; private set; }
    internal int? RegionalPeopleFactionId { get; private set; }
    internal string? CreatureFactionId { get; private set; }
    internal string? TeamOverride { get; private set; }

    /// <summary>Effective combat/aggro faction team before quest-instance overrides.</summary>
    internal string? EffectiveTeam => TeamOverride ?? CreatureFactionId;

    internal void SetPeopleFactions(int? primaryFactionId, int? regionalPeopleFactionId)
    {
        PrimaryFactionId = primaryFactionId;
        RegionalPeopleFactionId = regionalPeopleFactionId;
    }

    internal void SetCreatureFaction(string? creatureFactionId) => CreatureFactionId = creatureFactionId;

    internal void SetTeamOverride(string? teamOverride) => TeamOverride = teamOverride;
}
