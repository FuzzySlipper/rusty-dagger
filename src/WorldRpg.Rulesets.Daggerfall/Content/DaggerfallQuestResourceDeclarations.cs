using System.Globalization;
using System.Text.RegularExpressions;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Reads the resources each compiled quest record declares (its <c>Foe</c>, <c>Item</c>, <c>Person</c> and
/// <c>Place</c> lines) and the placements that name a symbol their record does not declare. Both are read from the
/// records at load, so an edited record declares what its lines say.
/// </summary>
internal static partial class DaggerfallQuestResourceDeclarations
{
    [GeneratedRegex(@"^(?<kind>foe|item|person|place)\s+(?<symbol>[^\s]+)(?:\s+(?<tail>.*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^place\s+(?:item|foe|person)\s+(?<resource>_[A-Za-z0-9.\-]+_)\s+at\s+(?<place>_[A-Za-z0-9.\-]+_)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Placement();

    internal static (IReadOnlyList<DaggerfallQuestResourceDefinition> Declarations, IReadOnlyList<DaggerfallQuestUnresolvedReferenceDefinition> Unresolved) Read(
        IEnumerable<DaggerfallQuestSourceDefinition> sources)
    {
        DaggerfallQuestSourceDefinition[] compiled = [.. sources.Where(source => source.Disposition == DaggerfallQuestDisposition.Compiled)
            .OrderBy(source => source.SourceFile, StringComparer.Ordinal)];
        List<DaggerfallQuestResourceDefinition> declarations = [];
        foreach (DaggerfallQuestSourceDefinition quest in compiled)
        foreach (DaggerfallQuestBlockDefinition block in quest.Blocks)
        foreach ((string line, int offset) in block.Lines.Select((line, offset) => (line, offset)))
            if (Declare(quest, line, block.FirstLine + offset) is { } declaration) declarations.Add(declaration);
        declarations.Sort((left, right) => string.CompareOrdinal(left.SourceFile, right.SourceFile) is var file and not 0 ? file : left.SourceLine.CompareTo(right.SourceLine));
        // A symbol must name one resource of its quest, or a binding would pick one of two silently.
        foreach (IGrouping<(string SourceFile, string CanonicalId), DaggerfallQuestResourceDefinition> group in declarations.GroupBy(value => (value.SourceFile, value.CanonicalId)))
            if (group.Count() != 1)
                throw new DaggerfallContentException([$"Quest '{group.Key.SourceFile}' declares symbol '{group.Key.CanonicalId}' {group.Count()} times, at lines {string.Join(", ", group.Select(value => value.SourceLine))}."]);

        HashSet<(string, string)> declared = [.. declarations.Select(value => (value.SourceFile, value.CanonicalId))];
        List<DaggerfallQuestUnresolvedReferenceDefinition> unresolved = [];
        foreach (DaggerfallQuestSourceDefinition quest in compiled)
        foreach (DaggerfallQuestBlockDefinition block in quest.Blocks)
        foreach ((string line, int offset) in block.Lines.Select((line, offset) => (line, offset)))
        foreach (Group use in Placement().Match(line.Trim()).Groups.Cast<Group>().Where(group => group.Name is "resource" or "place" && group.Success))
        {
            string canonical = Canonical(use.Value);
            if (!declared.Contains((quest.SourceFile, canonical))) unresolved.Add(new(quest.Name, quest.SourceFile, block.FirstLine + offset, use.Value, canonical));
        }
        return (declarations.AsReadOnly(), [.. unresolved.Distinct().OrderBy(value => value.SourceFile, StringComparer.Ordinal).ThenBy(value => value.SourceLine).ThenBy(value => value.CanonicalId, StringComparer.Ordinal)]);
    }

    /// <summary>A symbol's lookup identity: its inner name, lower-cased.</summary>
    private static string Canonical(string value)
    {
        string inner = value.Trim();
        if (inner.Length >= 2 && inner[0] == '_' && inner[^1] == '_') inner = inner[1..^1];
        return inner.Length == 0 ? throw new DaggerfallContentException([$"Quest symbol '{value}' has no inner name."]) : inner.ToLowerInvariant();
    }

    private static DaggerfallQuestResourceDefinition? Declare(DaggerfallQuestSourceDefinition quest, string line, int sourceLine)
    {
        Match match = Declaration().Match(line.Trim());
        if (!match.Success) return null;
        string kind = match.Groups["kind"].Value.ToLowerInvariant();
        string tail = match.Groups["tail"].Value;
        // A place line that states no scope is an action ("place foe _x_ at _y_"), not a declaration.
        if (kind == "place" && !(tail.StartsWith("local ", StringComparison.OrdinalIgnoreCase) || tail.StartsWith("remote ", StringComparison.OrdinalIgnoreCase)
            || tail.StartsWith("permanent ", StringComparison.OrdinalIgnoreCase) || tail.StartsWith("randompermanent ", StringComparison.OrdinalIgnoreCase)))
            return null;
        string symbol = match.Groups["symbol"].Value;
        (string? target, string? placeKind, IReadOnlyList<string> parameters) = Target(kind, tail);
        string[] words = tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string? After(string key)
        {
            int position = Array.FindIndex(words, word => word.Equals(key, StringComparison.OrdinalIgnoreCase));
            return position < 0 || position + 1 == words.Length ? null : words[position + 1];
        }
        int? Number(string key) => After(key) is { } value
            ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number
                : throw new DaggerfallContentException([$"Quest '{quest.SourceFile}' line {sourceLine} gives '{key}' the value '{value}', which is not a number."])
            : null;
        bool Has(string value) => words.Contains(value, StringComparer.OrdinalIgnoreCase);
        return new(quest.Name, quest.SourceFile, sourceLine, kind, symbol, Canonical(symbol), line,
            target, target is null ? null : Canonical(target), placeKind, parameters,
            kind == "foe" ? new(After("is") is { } count && int.TryParse(count, CultureInfo.InvariantCulture, out int amount) ? amount : 1) : null,
            kind == "item" ? new(Has("artifact"), Number("class"), Number("subclass"), Number("template"), Number("key"), Number("range"), Number("to"), After("used"), After("anyInfo")) : null,
            kind == "person" ? new(After("named"), After("faction"), After("factiontype"), After("group"), Number("face"),
                Has("female") ? "female" : Has("male") ? "male" : null, Has("local") ? "local" : Has("remote") ? "remote" : null, Has("athome"), After("office")) : null,
            kind == "place" ? [.. (target ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Canonical)] : null);
    }

    private static (string? Target, string? PlaceKind, IReadOnlyList<string> Parameters) Target(string kind, string tail)
    {
        string[] words = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (kind)
        {
            case "place":
                if (words.Length < 2) return (null, null, words);
                string scope = words[0].ToLowerInvariant() switch
                {
                    "randompermanent" => "randomPermanent",
                    var other => other,
                };
                return (words[1], scope, words[2..]);
            case "foe":
            {
                int isAt = Array.FindIndex(words, word => word.Equals("is", StringComparison.OrdinalIgnoreCase));
                if (isAt < 0 || isAt + 1 >= words.Length) return (null, null, words);
                int target = isAt + 1;
                if (int.TryParse(words[target], CultureInfo.InvariantCulture, out _) && target + 1 < words.Length) target++;
                return (words[target], null, words.Where((_, index) => index != target).ToArray());
            }
            case "person":
            {
                int target = Array.FindIndex(words, word => word.Equals("named", StringComparison.OrdinalIgnoreCase) || word.Equals("faction", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("factiontype", StringComparison.OrdinalIgnoreCase) || word.Equals("group", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("office", StringComparison.OrdinalIgnoreCase));
                return target >= 0 && target + 1 < words.Length ? (words[target + 1], null, words.Where((_, index) => index != target + 1).ToArray()) : (null, null, words);
            }
            default:
                if (words.Length == 0) return (null, null, words);
                int item = words[0].Equals("artifact", StringComparison.OrdinalIgnoreCase) && words.Length > 1 ? 1 : 0;
                return (words[item], null, words.Where((_, index) => index != item).ToArray());
        }
    }
}
