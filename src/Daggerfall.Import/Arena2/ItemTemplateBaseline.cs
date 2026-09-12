using System.Globalization;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// One native item template target: its index in the classic 288-entry space and the
/// donor groups whose enumerations name that index. An index no group names is retained
/// with an empty group list rather than dropped, because the target exists whether or not
/// the donor has a group for it.
/// </summary>
public sealed record ItemTemplateTarget(
    int Index,
    IReadOnlyList<string> DonorGroups,
    IReadOnlyList<string> DonorReferenceGroups)
{
    /// <summary>Whether any donor group names this index as a template index.</summary>
    public bool IsReferenced => DonorGroups.Count != 0;
}

/// <summary>
/// One rule the baseline was built under, with the donor evidence it rests on. The rules
/// are published with the ledger so a reader can reconstruct the reading rather than
/// inferring it from code comments.
/// </summary>
public sealed record ItemTemplateRule(string Id, string Rule, string Evidence);

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

    private ItemTemplateBaseline(
        string source,
        IReadOnlyList<ItemTemplateTarget> targets,
        IReadOnlyList<int> outOfRangeIndices,
        IReadOnlyList<ItemTemplateRule> rules)
    {
        Source = source;
        Targets = targets;
        OutOfRangeIndices = outOfRangeIndices;
        Rules = rules;
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

    /// <summary>The rules the baseline was read under, each with its donor evidence.</summary>
    public IReadOnlyList<ItemTemplateRule> Rules { get; }

    /// <summary>The targets no donor group names as a template index.</summary>
    public IEnumerable<ItemTemplateTarget> Unreferenced => Targets.Where(target => !target.IsReferenced);

    /// <summary>
    /// Reads the baseline from the donor's item enumerations and its group-to-enumeration
    /// mapping.
    /// </summary>
    /// <param name="itemEnumsSource">The donor's item enumeration declarations.</param>
    /// <param name="itemHelperSource">The donor source carrying the group mapping.</param>
    /// <param name="itemsFileSource">The donor source carrying the native template count.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static ItemTemplateBaseline FromDonorSources(string itemEnumsSource, string itemHelperSource, string itemsFileSource, string source)
    {
        ArgumentNullException.ThrowIfNull(itemEnumsSource);
        ArgumentNullException.ThrowIfNull(itemHelperSource);
        ArgumentNullException.ThrowIfNull(itemsFileSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Dictionary<string, (List<(string Name, int Value)> Members, string DeclarationComment)> enums = ParseEnums(itemEnumsSource, source);
        List<(string Group, string Enum)> mapping = ParseGroupMapping(itemHelperSource, source);
        if (mapping.Count == 0)
        {
            throw new Arena2FormatException(source, 0, "the donor's group mapping names no group, so no template index can be attributed");
        }

        // The native template count is the donor's own constant; the baseline refuses to
        // build a space of a different size than the source it cites declares.
        int declaredCount = ParseDeclaredTemplateCount(itemsFileSource, source);
        if (declaredCount != TargetCount)
        {
            throw new Arena2FormatException(source, 0, $"the donor declares {declaredCount} native item templates where this baseline covers {TargetCount}");
        }

        Dictionary<int, List<string>> groupsByIndex = [];
        Dictionary<int, List<string>> referenceGroupsByIndex = [];
        List<ItemTemplateRule> rules =
        [
            new("index-space", $"The target space is the {TargetCount} native template indices 0..{TargetCount - 1}.", $"donor:ItemsFile.cs declares totalItems = {declaredCount} and ItemHelper.cs declares LastDFTemplate = {TargetCount - 1}."),
            new("declaration-comment", "A mapped enumeration whose declaration says its values are not template indices contributes reference ids, not template indices.", "donor:ItemEnums.cs marks MagicItemSubTypes 'Not mapped to a specific item template index' and ArtifactsSubTypes 'Mapped to artifact definitions in MAGIC.DEF'."),
            new("implicit-values", "A member without an explicit value takes the previous member's value plus one, as the donor's runtime enumerations do.", "donor:ItemEnums.cs leaves Deeds, the magic sub-type and MiscItems.Unused unvalued, and the donor's GetEnumArray returns the runtime array."),
            new("sentinel-exclusion", "A negative member names no template and takes no place in the index space.", "donor:ItemEnums.cs declares None = -1 in ItemGroups and ArtifactsSubTypes."),
            new("alias-once", "A group naming one index through several members counts once.", "donor:ItemEnums.cs gives the four Books members the value 277."),
            new("substitute-source", "The native file is absent, and the donor's exported table is the substitute the decode must rest on until an authorized byte source appears.", "donor:ItemHelper.cs loads Assets/Resources/ItemTemplates.txt, which it states was exported from FALL.EXE, and MagicItemTemplates.txt beside it."),
        ];

        foreach ((string group, string enumName) in mapping)
        {
            if (!enums.TryGetValue(enumName, out (List<(string Name, int Value)> Members, string DeclarationComment) declaration))
            {
                throw new Arena2FormatException(source, 0, $"group '{group}' maps to enumeration '{enumName}', which the donor's item enumerations do not declare");
            }

            if (declaration.Members.Count == 0)
            {
                // A mapped enumeration with no members contributes no index, which would
                // quietly shrink the baseline.
                throw new Arena2FormatException(source, 0, $"group '{group}' maps to enumeration '{enumName}', which declares no member");
            }

            bool templateIndices = !IsReferenceSpace(declaration.DeclarationComment);
            foreach ((string _, int value) in declaration.Members)
            {
                // A negative member is the enumeration's "none" sentinel: it names no
                // template, so it takes no place in the index space.
                if (value < 0)
                {
                    continue;
                }

                Dictionary<int, List<string>> byIndex = templateIndices ? groupsByIndex : referenceGroupsByIndex;
                if (!byIndex.TryGetValue(value, out List<string>? groups))
                {
                    byIndex[value] = groups = [];
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
            targets.Add(new ItemTemplateTarget(
                index,
                groupsByIndex.TryGetValue(index, out List<string>? groups) ? groups : [],
                referenceGroupsByIndex.TryGetValue(index, out List<string>? references) ? references : []));
        }

        int[] outOfRange = [.. groupsByIndex.Keys.Concat(referenceGroupsByIndex.Keys).Where(index => index >= TargetCount).Order()];
        return new ItemTemplateBaseline(source, targets, outOfRange, rules);
    }

    /// <summary>Whether an enumeration's declaration says its values are not template indices.</summary>
    private static bool IsReferenceSpace(string declarationComment) =>
        declarationComment.Contains("Not mapped to a specific item template index", StringComparison.OrdinalIgnoreCase)
        || declarationComment.Contains("artifact definitions", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes block comments so a commented member still reads as a member.</summary>
    private static string StripBlockComments(string text)
    {
        int at = text.IndexOf("/*", StringComparison.Ordinal);
        while (at >= 0)
        {
            int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
            text = end < 0 ? text[..at] : text[..at] + text[(end + 2)..];
            at = text.IndexOf("/*", StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>
    /// Reads a C# integer literal: decimal, hexadecimal or binary, with an optional sign
    /// and an optional unsigned or long suffix. An expression is not a literal and is
    /// refused by the caller, because a member whose value cannot be read would silently
    /// drop out of the baseline.
    /// </summary>
    private static bool TryParseIntLiteral(string text, out int value)
    {
        value = 0;
        string literal = text.Trim().TrimEnd('u', 'U', 'l', 'L').Trim();
        if (literal.Length == 0)
        {
            return false;
        }

        bool negative = literal.StartsWith('-');
        if (negative || literal.StartsWith('+'))
        {
            literal = literal[1..].Trim();
        }

        NumberStyles style = NumberStyles.Integer;
        if (literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            literal = literal[2..];
            style = NumberStyles.AllowHexSpecifier;
        }
        else if (literal.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            literal = literal[2..];
            if (literal.Length == 0 || !literal.All(character => character is '0' or '1'))
            {
                return false;
            }

            value = Convert.ToInt32(literal, 2);
            return !negative || (value = -value) != 0;
        }

        if (!int.TryParse(literal, style, CultureInfo.InvariantCulture, out int parsed))
        {
            return false;
        }

        value = negative ? -parsed : parsed;
        return true;
    }

    /// <summary>Reads the donor's declared native template count.</summary>
    private static int ParseDeclaredTemplateCount(string itemsFileSource, string logicalSource)
    {
        int at = itemsFileSource.IndexOf("totalItems", StringComparison.Ordinal);
        if (at < 0)
        {
            throw new Arena2FormatException(logicalSource, 0, "the donor's item template source does not declare a template count");
        }

        int equals = itemsFileSource.IndexOf('=', at);
        int end = itemsFileSource.IndexOf(';', equals);
        if (equals < 0 || end < 0 || !int.TryParse(itemsFileSource[(equals + 1)..end].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            throw new Arena2FormatException(logicalSource, at, "the donor's declared template count is not a number");
        }

        return count;
    }

    /// <summary>
    /// Reads every <c>public enum</c> declaration, its members, and the comment on the
    /// declaration line. C# assigns an unvalued member the previous value plus one, and the
    /// donor's runtime enumerations include those members, so they are computed here rather
    /// than skipped: skipping them would attribute four indices differently from the donor.
    /// </summary>
    private static Dictionary<string, (List<(string Name, int Value)> Members, string DeclarationComment)> ParseEnums(string source, string logicalSource)
    {
        Dictionary<string, (List<(string Name, int Value)> Members, string DeclarationComment)> enums = new(StringComparer.Ordinal);
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
            int lineEnd = source.IndexOf('\n', nameEnd);
            string declarationLine = source[nameEnd..(lineEnd < 0 ? source.Length : lineEnd)];
            int commentAt = declarationLine.IndexOf("//", StringComparison.Ordinal);
            string declarationComment = commentAt < 0 ? string.Empty : declarationLine[(commentAt + 2)..].Trim();
            int open = source.IndexOf('{', nameEnd);
            if (open < 0)
            {
                throw new Arena2FormatException(logicalSource, declaration, $"enumeration '{name}' has no body");
            }

            int close = MatchingBrace(source, open, logicalSource);
            List<(string Name, int Value)> members = [];
            int next = 0;
            // Members are comma-separated, so a body written on one line reads the same as
            // one member per line, and both comment forms are stripped before a value is read.
            string body = StripBlockComments(source[(open + 1)..close]);
            foreach (string entry in body.Split(',', StringSplitOptions.TrimEntries))
            {
                string text = entry.Split("//")[0].Trim();
                int equals = text.IndexOf('=');
                string member = equals <= 0 ? text : text[..equals].Trim();
                if (member.Length == 0 || !member.All(character => char.IsLetterOrDigit(character) || character == '_'))
                {
                    continue;
                }

                if (equals <= 0)
                {
                    // An implicit member past the last representable value would wrap to a
                    // negative number and then be dropped as a sentinel, so it is refused
                    // rather than silently lost.
                    if (next == int.MaxValue)
                    {
                        throw new Arena2FormatException(logicalSource, open, $"enumeration '{name}' gives member '{member}' an implicit value past the last representable one");
                    }

                    members.Add((member, next));
                    next++;
                    continue;
                }

                string value = text[(equals + 1)..].Trim();
                if (!TryParseIntLiteral(value, out int parsed))
                {
                    // An unreadable value would silently drop the member and understate the
                    // published coverage, so it is refused instead.
                    throw new Arena2FormatException(logicalSource, open, $"enumeration '{name}' gives member '{member}' the value '{value}', which is not an integer");
                }

                members.Add((member, parsed));
                next = parsed == int.MaxValue
                    ? throw new Arena2FormatException(logicalSource, open, $"enumeration '{name}' gives member '{member}' the value {parsed}, so the member after it would wrap past the last representable value")
                    : parsed + 1;
            }

            if (!enums.TryAdd(name, (members, declarationComment)))
            {
                throw new Arena2FormatException(logicalSource, declaration, $"enumeration '{name}' is declared more than once");
            }

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
        if (open < 0)
        {
            throw new Arena2FormatException(logicalSource, at, $"'{signature}' declares no body");
        }

        int close = MatchingBrace(source, open, logicalSource);
        // The donor writes a case label and its return on separate lines, so the group is
        // carried from the label to the return that follows it. A label whose return never
        // arrives, or a second label before the first returns, would silently misattribute
        // or drop a group, so both are refused rather than guessed at.
        List<(string Group, string Enum)> mapping = [];
        const string casePrefix = "case ItemGroups.";
        const string returnPrefix = "return Enum.GetValues(typeof(";
        string? pending = null;
        int pendingAt = 0;
        for (int index = open + 1; index <= close; index++)
        {
            int lineEnd = source.IndexOf('\n', index);
            int end = lineEnd < 0 || lineEnd > close ? close : lineEnd;
            string text = source[index..end].Split("//")[0].Trim();
            index = end;
            if (text.StartsWith(casePrefix, StringComparison.Ordinal))
            {
                if (pending is not null)
                {
                    throw new Arena2FormatException(logicalSource, pendingAt, $"group '{pending}' is labelled twice before its enumeration is returned, so the mapping cannot attribute both");
                }

                // The label ends at its colon: anything after it on the line (a same-line
                // return, a comment) is not part of the group name.
                int colon = text.IndexOf(':');
                pending = text[casePrefix.Length..(colon < 0 ? text.Length : colon)].Trim();
                pendingAt = index;
            }

            int returnAt = text.IndexOf(returnPrefix, StringComparison.Ordinal);
            if (returnAt < 0)
            {
                continue;
            }

            if (pending is null)
            {
                throw new Arena2FormatException(logicalSource, index, "the donor's group mapping returns an enumeration without a group label");
            }

            int start = returnAt + returnPrefix.Length;
            mapping.Add((pending, text[start..text.IndexOf(')', start)]));
            pending = null;
        }

        if (pending is not null)
        {
            throw new Arena2FormatException(logicalSource, pendingAt, $"group '{pending}' is labelled without returning an enumeration, so its indices would be lost");
        }

        return mapping;
    }

    private static int MatchingBrace(string source, int open, string logicalSource)
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

        throw new Arena2FormatException(logicalSource, open, "a declaration body is not closed");
    }
}
