using System.Globalization;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>
/// Reads the donor's item enumerations (<c>ItemEnums.cs</c>), its group-to-enumeration mapping (<c>ItemHelper.cs</c>)
/// and its native template count (<c>ItemsFile.cs</c>) into our item-group table's values.
/// </summary>
internal static class DonorItemGroups
{
    /// <summary>The native template count and every mapped group, in the donor's mapping order.</summary>
    public static (int TemplateCount, IReadOnlyList<ItemGroupEnumeration> Groups) Read(string itemEnumsSource, string itemHelperSource, string itemsFileSource, string source)
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

        int declaredCount = ParseDeclaredTemplateCount(itemsFileSource, source);
        Dictionary<string, int> ids = enums.TryGetValue("ItemGroups", out var itemGroups)
            ? itemGroups.Members.ToDictionary(member => member.Name, member => member.Value, StringComparer.Ordinal)
            : [];
        List<ItemGroupEnumeration> result = [];
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

            if (!ids.TryGetValue(group, out int id) || id < 0)
                throw new Arena2FormatException(source, 0, $"group '{group}' has no identity in the donor's ItemGroups enumeration");
            // Enum.GetValues sorts by the unsigned magnitude of each underlying value,
            // including repeated aliases. Declaration order alone is not its ordinal contract.
            result.Add(new(id, group, !IsReferenceSpace(declaration.DeclarationComment),
                declaration.Members.Select(member => member.Value).OrderBy(value => unchecked((uint)value)).ToArray()));
        }

        if (result.Select(group => group.Id).Distinct().Count() != result.Count)
            throw new Arena2FormatException(source, 0, "the donor group mapping repeats an identity");
        return (declaredCount, result);
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
