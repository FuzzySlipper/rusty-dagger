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
    /// Whether an actor on <paramref name="observerTeam"/> treats <paramref name="targetTeam"/> as hostile under
    /// classic team/faction rules. Same-faction and explicitly allied creature families do not fight each other;
    /// distinct teams fight when infighting is enabled (except pacified actors leave <c>player-ally</c> alone).
    /// </summary>
    internal bool AreTeamsHostile(string? observerTeam, string? targetTeam, bool observerPacified)
    {
        if (string.Equals(observerTeam, targetTeam, StringComparison.Ordinal)) return false;
        if (string.Equals(targetTeam, "player-ally", StringComparison.Ordinal) && observerPacified) return false;
        if (TryGet(observerTeam, out DaggerfallCreatureFactionDefinition? observer)
            && !string.IsNullOrWhiteSpace(targetTeam)
            && observer!.Allies.Contains(targetTeam, StringComparer.Ordinal))
            return false;
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
                if (!result.ContainsKey(ally)) diagnostics.Add($"Creature faction '{faction.Id}' names unknown ally '{ally}'.");
            foreach (string enemy in faction.Enemies)
                if (!result.ContainsKey(enemy)) diagnostics.Add($"Creature faction '{faction.Id}' names unknown enemy '{enemy}'.");
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
        (string Id, int Index, string Name, bool Hostile)[] classic =
        [
            ("player-enemy", 0, "Player Enemies", true),
            ("player-ally", 1, "Player Allies", false),
            ("vermin", 2, "Vermin", true),
            ("spriggans", 3, "Spriggans", true),
            ("bears", 4, "Bears", true),
            ("tigers", 5, "Tigers", true),
            ("spiders", 6, "Spiders", true),
            ("orcs", 7, "Orcs", true),
            ("centaurs", 8, "Centaurs", true),
            ("werecreatures", 9, "Werecreatures", true),
            ("nymphs", 10, "Nymphs", true),
            ("aquatic", 11, "Aquatic Creatures", true),
            ("harpies", 12, "Harpies", true),
            ("undead", 13, "Undead", true),
            ("giants", 14, "Giants", true),
            ("scorpions", 15, "Scorpions", true),
            ("magic", 16, "Magical Constructs", true),
            ("daedra", 17, "Daedra", true),
            ("dragonlings", 18, "Dragonlings", true),
            ("knights-and-mages", 19, "Knights and Mages", true),
            ("criminals", 20, "Criminals", true),
            ("city-watch", 21, "City Watch", true),
        ];
        return new(classic.ToDictionary(
            item => item.Id,
            item => new DaggerfallCreatureFactionDefinition(item.Id, item.Index, item.Name, item.Hostile, [], []),
            StringComparer.Ordinal));
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
