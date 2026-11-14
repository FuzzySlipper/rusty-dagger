using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The donor's internal strings and number forms the text macros' contexts are built from. Each domain builds the
/// context of its own values: Character's player, Items' item, Time's calendar.
/// </summary>
internal static class DaggerfallTextContexts
{
    internal static string Internal(DaggerfallDefinitions definitions, string key) =>
        string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, key)).TextRuns);

    internal static string[] List(DaggerfallDefinitions definitions, string key) =>
        Internal(definitions, key).Split('\n');

    internal static string Race(DaggerfallDefinitions definitions, string id)
    {
        _ = definitions.Catalogs.RequireRace(id);
        string[] words = id.Split('-');
        string key = words[0] + string.Concat(words.Skip(1).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        return Internal(definitions, key);
    }

    internal static DaggerfallTextContext Book(DaggerfallBookDefinition book) => DaggerfallTextContext.Empty with
        { Item = new(ItemName: book.Title, BookAuthor: book.Author) };

    internal static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string Signed(int value) => value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
}
