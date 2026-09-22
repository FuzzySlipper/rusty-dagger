using System.Text.RegularExpressions;

namespace Daggerfall.Import.Normalized;

/// <summary>The resource category declared by a quest source.</summary>
public enum DaggerfallQuestResourceKind { Foe, Item, Person, Place }
/// <summary>The location selection stated by a place declaration.</summary>
public enum DaggerfallQuestPlaceKind { Local, Remote, Permanent, RandomPermanent }
/// <summary>A source symbol and its case-insensitive lookup identity.</summary>
public sealed record DaggerfallQuestSymbol(string SourceSpelling, string CanonicalId)
{
    public static DaggerfallQuestSymbol Parse(string value) => new(value, Canonical(value));
    public static string Canonical(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string inner = value.Trim();
        if (inner.Length >= 2 && inner[0] == '_' && inner[^1] == '_') inner = inner[1..^1];
        if (inner.Length == 0) throw new ArgumentException("A quest symbol has no inner name.", nameof(value));
        return inner.ToLowerInvariant();
    }
}
/// <summary>One typed declaration retained from a resource line, with source spelling and provenance.</summary>
public sealed record DaggerfallQuestResourceDeclaration(string Quest, string SourceFile, int SourceLine,
    DaggerfallQuestResourceKind Kind, DaggerfallQuestSymbol Symbol, string SourceText,
    string? TargetSourceSpelling, string? TargetCanonicalId, DaggerfallQuestPlaceKind? PlaceKind,
    IReadOnlyList<string> Parameters,
    DaggerfallQuestFoeOptions? Foe = null, DaggerfallQuestItemOptions? Item = null,
    DaggerfallQuestPersonOptions? Person = null, DaggerfallQuestPlaceOptions? Place = null);
public sealed record DaggerfallQuestFoeOptions(int Count);
public sealed record DaggerfallQuestItemOptions(bool Artifact, int? Class, int? Subclass, int? Template, int? Key, int? RangeLow, int? RangeHigh, string? UsedMessage, string? AnyInfoMessage);
public sealed record DaggerfallQuestPersonOptions(string? Named, string? Faction, string? FactionType, string? Group, int? Face, string? Gender, string? Scope, bool AtHome);
public sealed record DaggerfallQuestPlaceOptions(IReadOnlyList<DaggerfallQuestSymbol> Sites);
/// <summary>A symbol reference which was not declared by that quest; it remains explicit for later binding.</summary>
public sealed record DaggerfallQuestUnresolvedReference(string Quest, string SourceFile, int SourceLine, string SourceSpelling, string CanonicalId);
/// <summary>Normalized resource and symbol declarations from every compiled quest source.</summary>
public sealed record DaggerfallQuestResources(IReadOnlyList<DaggerfallQuestResourceDeclaration> Declarations,
    IReadOnlyList<DaggerfallQuestUnresolvedReference> Unresolved)
{
    public void Validate()
    {
        foreach (IGrouping<string, DaggerfallQuestResourceDeclaration> group in Declarations.GroupBy(value => $"{value.SourceFile}\0{value.Symbol.CanonicalId}", StringComparer.Ordinal))
            if (group.Count() != 1) throw new InvalidOperationException($"Quest source '{group.First().SourceFile}' declares symbol '{group.First().Symbol.CanonicalId}' {group.Count()} times.");
    }
    /// <summary>Fails a caller that requires every symbolic use to bind before it can create quest state.</summary>
    public void RequireAllReferencesResolved()
    {
        if (Unresolved.Count == 0) return;
        DaggerfallQuestUnresolvedReference first = Unresolved[0];
        throw new InvalidOperationException($"Quest '{first.SourceFile}' line {first.SourceLine} references symbol '{first.SourceSpelling}', whose canonical id '{first.CanonicalId}' has no resource declaration.");
    }
}
/// <summary>Normalizes declaration lines without creating world, actor, or item state.</summary>
public static class DaggerfallQuestResourceBuilder
{
    private static readonly Regex Declaration = new(@"^(?<kind>foe|item|person|place)\s+(?<symbol>[^\s]+)(?:\s+(?<tail>.*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Placement = new(@"^place\s+(?:item|foe|person)\s+(?<resource>_[A-Za-z0-9.\-]+_)\s+at\s+(?<place>_[A-Za-z0-9.\-]+_)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static DaggerfallQuestResources Build(DaggerfallQuestPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        List<DaggerfallQuestResourceDeclaration> declarations = [];
        foreach (DaggerfallQuestRecord quest in pack.Quests.Where(quest => quest.Disposition == DaggerfallQuestDisposition.Compiled))
        foreach (DaggerfallQuestBlock block in quest.Blocks)
        foreach ((string line, int offset) in block.Lines.Select((line, offset) => (line, offset)))
        {
            Match match = Declaration.Match(line.Trim());
            if (!match.Success) continue;
            string kindText = match.Groups["kind"].Value;
            if (!Enum.TryParse(kindText, true, out DaggerfallQuestResourceKind kind)) continue;
            string tail = match.Groups["tail"].Value;
            if (kind == DaggerfallQuestResourceKind.Place
                && !tail.StartsWith("local ", StringComparison.OrdinalIgnoreCase)
                && !tail.StartsWith("remote ", StringComparison.OrdinalIgnoreCase)
                && !tail.StartsWith("permanent ", StringComparison.OrdinalIgnoreCase)
                && !tail.StartsWith("randompermanent ", StringComparison.OrdinalIgnoreCase)) continue;
            DaggerfallQuestSymbol symbol = DaggerfallQuestSymbol.Parse(match.Groups["symbol"].Value);
            (string? target, DaggerfallQuestPlaceKind? placeKind, IReadOnlyList<string> parameters) = Target(kind, tail);
            string[] words = tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string? After(string key)
            {
                int position = Array.FindIndex(words, word => word.Equals(key, StringComparison.OrdinalIgnoreCase));
                return position < 0 || position + 1 == words.Length ? null : words[position + 1];
            }
            int? Number(string key) => After(key) is string value
                ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : null;
            bool Has(string value) => words.Contains(value, StringComparer.OrdinalIgnoreCase);
            DaggerfallQuestFoeOptions? foe = kind == DaggerfallQuestResourceKind.Foe
                ? new(After("is") is string count && int.TryParse(count, out int amount) ? amount : 1) : null;
            DaggerfallQuestItemOptions? item = kind == DaggerfallQuestResourceKind.Item
                ? new(Has("artifact"), Number("class"), Number("subclass"), Number("template"), Number("key"),
                    Number("range"), Number("to"), After("used"), After("anyInfo")) : null;
            DaggerfallQuestPersonOptions? person = kind == DaggerfallQuestResourceKind.Person
                ? new(After("named"), After("faction"), After("factiontype"), After("group"), Number("face"),
                    Has("female") ? "female" : Has("male") ? "male" : null,
                    Has("local") ? "local" : Has("remote") ? "remote" : null, Has("athome")) : null;
            DaggerfallQuestPlaceOptions? place = kind == DaggerfallQuestResourceKind.Place
                ? new((target ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(DaggerfallQuestSymbol.Parse).ToArray()) : null;
            declarations.Add(new(quest.Name, quest.SourceFile, block.FirstLine + offset, kind, symbol, line,
                target, target is null ? null : DaggerfallQuestSymbol.Canonical(target), placeKind, parameters, foe, item, person, place));
        }
        DaggerfallQuestResources result = new([.. declarations.OrderBy(value => value.SourceFile, StringComparer.Ordinal).ThenBy(value => value.SourceLine)], []);
        result.Validate();
        HashSet<string> declared = [.. result.Declarations.Select(value => $"{value.SourceFile}\0{value.Symbol.CanonicalId}")];
        List<DaggerfallQuestUnresolvedReference> unresolved = [];
        foreach (DaggerfallQuestRecord quest in pack.Quests.Where(quest => quest.Disposition == DaggerfallQuestDisposition.Compiled))
        foreach (DaggerfallQuestBlock block in quest.Blocks)
        foreach ((string line, int offset) in block.Lines.Select((line, offset) => (line, offset)))
        foreach (Group use in Placement.Match(line.Trim()).Groups.Cast<Group>().Where(group => group.Name is "resource" or "place" && group.Success))
        {
            string spelling = use.Value;
            string canonical = DaggerfallQuestSymbol.Canonical(spelling);
            if (!declared.Contains($"{quest.SourceFile}\0{canonical}")) unresolved.Add(new(quest.Name, quest.SourceFile, block.FirstLine + offset, spelling, canonical));
        }
        return result with { Unresolved = [.. unresolved.Distinct().OrderBy(value => value.SourceFile, StringComparer.Ordinal).ThenBy(value => value.SourceLine).ThenBy(value => value.CanonicalId, StringComparer.Ordinal)] };
    }
    private static (string? Target, DaggerfallQuestPlaceKind? PlaceKind, IReadOnlyList<string> Parameters) Target(DaggerfallQuestResourceKind kind, string tail)
    {
        string[] words = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (kind == DaggerfallQuestResourceKind.Place)
        {
            if (words.Length < 2) return (null, null, words);
            if (words[0].Equals("randompermanent", StringComparison.OrdinalIgnoreCase)) return (words[1], DaggerfallQuestPlaceKind.RandomPermanent, words[2..]);
            if (Enum.TryParse(words[0], true, out DaggerfallQuestPlaceKind place)) return (words[1], place, words[2..]);
            return (words[0], null, words[1..]);
        }
        if (kind == DaggerfallQuestResourceKind.Foe)
        {
            int isAt = Array.FindIndex(words, word => word.Equals("is", StringComparison.OrdinalIgnoreCase));
            if (isAt < 0 || isAt + 1 >= words.Length) return (null, null, words);
            int target = isAt + 1;
            if (int.TryParse(words[target], out _) && target + 1 < words.Length) target++;
            return (words[target], null, words.Where((_, index) => index != target).ToArray());
        }
        if (kind == DaggerfallQuestResourceKind.Person)
        {
            int target = Array.FindIndex(words, word => word.Equals("named", StringComparison.OrdinalIgnoreCase) || word.Equals("faction", StringComparison.OrdinalIgnoreCase) || word.Equals("factiontype", StringComparison.OrdinalIgnoreCase) || word.Equals("group", StringComparison.OrdinalIgnoreCase));
            return target >= 0 && target + 1 < words.Length ? (words[target + 1], null, words.Where((_, index) => index != target + 1).ToArray()) : (null, null, words);
        }
        if (words.Length == 0) return (null, null, words);
        int item = words[0].Equals("artifact", StringComparison.OrdinalIgnoreCase) && words.Length > 1 ? 1 : 0;
        return (words[item], null, words.Where((_, index) => index != item).ToArray());
    }
}
