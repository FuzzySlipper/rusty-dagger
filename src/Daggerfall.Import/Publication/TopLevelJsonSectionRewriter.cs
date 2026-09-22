using System.Text.Json;

namespace Daggerfall.Import.Publication;

/// <summary>
/// Replaces named root-object values without reserializing unrelated payload sections.
/// Content payloads have independently imported sections; preserving their original JSON keeps
/// an update to one importer from creating formatting churn in every other section.
/// </summary>
public static class TopLevelJsonSectionRewriter
{
    /// <summary>Replaces or appends root object sections using already serialized JSON values.</summary>
    public static string ReplaceOrAppend(string json, IReadOnlyDictionary<string, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return json;

        int index = SkipWhitespace(json, 0);
        if (index == json.Length || json[index++] != '{') throw new ArgumentException("The payload root is not a JSON object.", nameof(json));
        int rootStart = index - 1;
        int copied = 0;
        HashSet<string> replaced = new(StringComparer.Ordinal);
        System.Text.StringBuilder output = new(json.Length);
        while (true)
        {
            index = SkipWhitespaceAndCommas(json, index);
            if (index == json.Length) throw new ArgumentException("The root object does not close.", nameof(json));
            if (json[index] == '}')
            {
                if (replaced.Count == values.Count)
                {
                    output.Append(json, copied, json.Length - copied);
                    return output.ToString();
                }

                string[] missing = [.. values.Keys.Where(key => !replaced.Contains(key)).Order(StringComparer.Ordinal)];
                int beforeClosingWhitespace = index;
                while (beforeClosingWhitespace > copied && char.IsWhiteSpace(json[beforeClosingWhitespace - 1])) beforeClosingWhitespace--;
                output.Append(json, copied, beforeClosingWhitespace - copied);
                bool hasMembers = HasRootMembers(json, rootStart + 1, index);
                if (hasMembers) output.Append(',');
                foreach ((string sectionName, int position) in missing.Select((sectionName, position) => (sectionName, position)))
                {
                    output.Append('\n').Append("  ").Append(JsonSerializer.Serialize(sectionName)).Append(": ")
                        .Append(Indent(values[sectionName], "  "));
                    if (position != missing.Length - 1) output.Append(',');
                }

                output.Append('\n');
                output.Append(json, index, json.Length - index);
                return output.ToString();
            }

            if (json[index] != '"') throw new ArgumentException($"Expected a root property at offset {index}.", nameof(json));
            int nameEnd = EndString(json, index);
            string name = JsonSerializer.Deserialize<string>(json[index..nameEnd])
                ?? throw new ArgumentException($"Root property at offset {index} is null.", nameof(json));
            int colon = SkipWhitespace(json, nameEnd);
            if (colon == json.Length || json[colon] != ':') throw new ArgumentException($"Root property '{name}' has no colon.", nameof(json));
            int valueStart = SkipWhitespace(json, colon + 1);
            int valueEnd = EndValue(json, valueStart);
            if (values.TryGetValue(name, out string? replacement))
            {
                output.Append(json, copied, valueStart - copied);
                string lineIndent = LineIndent(json, index);
                output.Append(Indent(replacement, lineIndent));
                copied = valueEnd;
                replaced.Add(name);
            }

            index = valueEnd;
        }
    }

    private static int SkipWhitespace(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
        return index;
    }

    private static int SkipWhitespaceAndCommas(string value, int index)
    {
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || value[index] == ',')) index++;
        return index;
    }

    private static int EndValue(string value, int start)
    {
        if (start >= value.Length) throw new ArgumentException("A root property has no value.", nameof(value));
        if (value[start] == '"') return EndString(value, start);
        if (value[start] is '{' or '[')
        {
            char opening = value[start];
            char closing = opening == '{' ? '}' : ']';
            int depth = 0;
            for (int index = start; index < value.Length; index++)
            {
                if (value[index] == '"')
                {
                    index = EndString(value, index) - 1;
                    continue;
                }

                if (value[index] == opening) depth++;
                else if (value[index] == closing && --depth == 0) return index + 1;
            }

            throw new ArgumentException($"The value at offset {start} does not close.", nameof(value));
        }

        int end = start;
        while (end < value.Length && value[end] is not ',' and not '}') end++;
        if (end == start) throw new ArgumentException($"The value at offset {start} is invalid.", nameof(value));
        return end;
    }

    private static int EndString(string value, int start)
    {
        for (int index = start + 1; index < value.Length; index++)
        {
            if (value[index] == '\\')
            {
                index++;
                continue;
            }

            if (value[index] == '"') return index + 1;
        }

        throw new ArgumentException($"The string at offset {start} does not close.", nameof(value));
    }

    private static string LineIndent(string value, int position)
    {
        int line = value.LastIndexOf('\n', position);
        int start = line < 0 ? 0 : line + 1;
        int end = start;
        while (end < value.Length && value[end] is ' ' or '\t') end++;
        return value[start..end];
    }

    private static string Indent(string value, string prefix) => value.Replace("\n", "\n" + prefix, StringComparison.Ordinal);

    private static bool HasRootMembers(string value, int start, int end) => value[start..end].Any(character => !char.IsWhiteSpace(character));
}
