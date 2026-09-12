using System.Globalization;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// One native item template target: its index in the classic 288-entry space and the
/// donor groups whose enumerations name that index. An index no group names is retained
/// with an empty group list rather than dropped, because the target exists whether or not
/// the donor has a group for it.
/// </summary>
public sealed record ItemTemplateTarget(int Index, IReadOnlyList<string> DonorGroups)
{
    /// <summary>Whether any donor group names this index.</summary>
    public bool IsReferenced => DonorGroups.Count != 0;
}

/// <summary>
/// The donor's item-template baseline: which of the classic 288 template indices the
/// donor's group enumerations reference, read from the donor's own sources rather than
/// transcribed by hand.
/// </summary>
/// <remarks>
/// This is a baseline about *indices*, not about item data. The native template records
/// live in FALL.EXE, which is not supplied, so nothing here carries a template's name,
/// weight, price or damage. The donor establishes two things and only two: the index
/// space is 0..287, and its group enumerations assign some of those indices to groups.
/// Both are facts about donor code, which is why the ledger built from this marks them as
/// a provisional baseline under DEC-11 rather than as native decoding.
/// </remarks>
public sealed class ItemTemplateBaseline
{
    /// <summary>The classic native template count.</summary>
    public const int TargetCount = 288;

    private ItemTemplateBaseline(string source, IReadOnlyList<ItemTemplateTarget> targets, IReadOnlyList<int> outOfRangeIndices)
    {
        Source = source;
        Targets = targets;
        OutOfRangeIndices = outOfRangeIndices;
    }

    /// <summary>Logical source identity supplied to <see cref="FromDonorSources"/>.</summary>
    public string Source { get; }

    /// <summary>Every target index in ascending order.</summary>
    public IReadOnlyList<ItemTemplateTarget> Targets { get; }

    /// <summary>
    /// Indices the donor's enumerations name beyond the classic template space. Retained
    /// rather than refused: an index past 287 means donor drift, which the ledger reports.
    /// A negative member is the donor's own "none" sentinel rather than an index and is
    /// not reported here.
    /// </summary>
    public IReadOnlyList<int> OutOfRangeIndices { get; }

    /// <summary>The targets no donor group names.</summary>
    public IEnumerable<ItemTemplateTarget> Unreferenced => Targets.Where(target => !target.IsReferenced);

    /// <summary>
    /// Reads the baseline from the donor's item enumerations and its group-to-enumeration
    /// mapping.
    /// </summary>
    /// <param name="itemEnumsSource">The donor's item enumeration declarations.</param>
    /// <param name="itemHelperSource">The donor source carrying the group mapping.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static ItemTemplateBaseline FromDonorSources(string itemEnumsSource, string itemHelperSource, string source)
    {
        ArgumentNullException.ThrowIfNull(itemEnumsSource);
        ArgumentNullException.ThrowIfNull(itemHelperSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Dictionary<string, List<(string Name, int Value)>> enums = ParseEnums(itemEnumsSource, source);
        List<(string Group, string Enum)> mapping = ParseGroupMapping(itemHelperSource, source);
        if (mapping.Count == 0)
        {
            throw new Arena2FormatException(source, 0, "the donor's group mapping names no group, so no template index can be attributed");
        }

        Dictionary<int, List<string>> groupsByIndex = [];
        foreach ((string group, string enumName) in mapping)
        {
            if (!enums.TryGetValue(enumName, out List<(string Name, int Value)>? members))
            {
                throw new Arena2FormatException(source, 0, $"group '{group}' maps to enumeration '{enumName}', which the donor's item enumerations do not declare");
            }

            foreach ((string _, int value) in members)
            {
                // A negative member is the enumeration's "none" sentinel: it names no
                // template, so it takes no place in the index space.
                if (value < 0)
                {
                    continue;
                }

                if (!groupsByIndex.TryGetValue(value, out List<string>? groups))
                {
                    groupsByIndex[value] = groups = [];
                }

                // A group may name one index through several members: the donor's four book
                // variants all name index 277. The group is recorded once, since the aliasing
                // is a fact about the donor's naming rather than about the index space.
                if (!groups.Contains(group, StringComparer.Ordinal))
                {
                    groups.Add(group);
                }
            }
        }

        List<ItemTemplateTarget> targets = new(TargetCount);
        for (int index = 0; index < TargetCount; index++)
        {
            targets.Add(new ItemTemplateTarget(index, groupsByIndex.TryGetValue(index, out List<string>? groups) ? groups : []));
        }

        int[] outOfRange = [.. groupsByIndex.Keys.Where(index => index >= TargetCount).Order()];
        return new ItemTemplateBaseline(source, targets, outOfRange);
    }

    /// <summary>Reads every <c>public enum</c> declaration and its explicitly valued members.</summary>
    private static Dictionary<string, List<(string Name, int Value)>> ParseEnums(string source, string logicalSource)
    {
        Dictionary<string, List<(string Name, int Value)>> enums = new(StringComparer.Ordinal);
        int at = 0;
        while (true)
        {
            int declaration = source.IndexOf("public enum ", at, StringComparison.Ordinal);
            if (declaration < 0)
            {
                return enums;
            }

            int nameStart = declaration + "public enum ".Length;
            int nameEnd = nameStart;
            while (nameEnd < source.Length && (char.IsLetterOrDigit(source[nameEnd]) || source[nameEnd] == '_'))
            {
                nameEnd++;
            }

            string name = source[nameStart..nameEnd];
            int open = source.IndexOf('{', nameEnd);
            if (open < 0)
            {
                throw new Arena2FormatException(logicalSource, declaration, $"enumeration '{name}' has no body");
            }

            int close = MatchingBrace(source, open);
            List<(string Name, int Value)> members = [];
            foreach (string line in source[(open + 1)..close].Split('\n'))
            {
                // Only explicitly valued members are read: an implicit value would depend on
                // the member's position, which the native index space does not use.
                string text = line.Split("//")[0].Trim().TrimEnd(',');
                int equals = text.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string member = text[..equals].Trim();
                string value = text[(equals + 1)..].Trim();
                if (member.Length == 0 || !member.All(character => char.IsLetterOrDigit(character) || character == '_'))
                {
                    continue;
                }

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    members.Add((member, parsed));
                }
            }

            enums[name] = members;
            at = close;
        }
    }

    /// <summary>Reads the donor's group-to-enumeration mapping from its accessor.</summary>
    private static List<(string Group, string Enum)> ParseGroupMapping(string source, string logicalSource)
    {
        const string signature = "GetEnumArray(ItemGroups group)";
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0)
        {
            throw new Arena2FormatException(logicalSource, 0, $"the donor source does not declare '{signature}'");
        }

        int open = source.IndexOf('{', at);
        int close = MatchingBrace(source, open);
        // The donor writes a case label and its return on separate lines, so the group is
        // carried from the label to the return that follows it.
        List<(string Group, string Enum)> mapping = [];
        const string casePrefix = "case ItemGroups.";
        const string returnPrefix = "return Enum.GetValues(typeof(";
        string? pending = null;
        foreach (string line in source[(open + 1)..close].Split('\n'))
        {
            string text = line.Trim();
            if (text.StartsWith(casePrefix, StringComparison.Ordinal))
            {
                pending = text[casePrefix.Length..].TrimEnd(':', ' ').Trim();
            }

            int returnAt = text.IndexOf(returnPrefix, StringComparison.Ordinal);
            if (returnAt < 0)
            {
                continue;
            }

            if (pending is null)
            {
                throw new Arena2FormatException(logicalSource, 0, "the donor's group mapping returns an enumeration without a group label");
            }

            int start = returnAt + returnPrefix.Length;
            mapping.Add((pending, text[start..text.IndexOf(')', start)]));
            pending = null;
        }

        return mapping;
    }

    private static int MatchingBrace(string source, int open)
    {
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        throw new Arena2FormatException("donor source", open, "a declaration body is not closed");
    }
}
