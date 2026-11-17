using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The oaths a people swear, which %oth names: text record 201 plus the faction race, as Daggerfall Unity reads them
/// (QuestMCP.Oath and the talk window's macro source).
/// </summary>
internal static class DaggerfallOaths
{
    /// <summary>
    /// The oaths of a person of this faction: its race's, or where the faction has none, the race of the region's
    /// province. Empty when neither is known.
    /// </summary>
    internal static IReadOnlyList<string> Of(DaggerfallDefinitions definitions, int faction, int? region)
    {
        int race = definitions.Factions.Factions.GetValueOrDefault(faction)?.Race ?? -1;
        if (race < 0 && region is int index)
            race = definitions.Factions.Factions.Values.Where(value => value.Type == 7 && value.Region == index)
                .OrderBy(value => value.Id).FirstOrDefault()?.Race ?? -1;
        DaggerfallTextKey key = new(DaggerfallTextKind.Resource, (201 + race).ToString(CultureInfo.InvariantCulture));
        return race >= 0 && definitions.Text.Resolve(key, out DaggerfallTextValue? value) == DaggerfallTextResolution.Resolved
            ? [.. value!.TextRuns] : [];
    }
}
