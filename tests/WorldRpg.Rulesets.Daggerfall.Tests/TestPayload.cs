using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The one parsed copy of the base definitions that facts share. They are read from two payloads: the
/// authored daggerfall.base payload and the imported payload scripts/regenerate-content.sh generates.
/// Reading them parses a 75 MiB document, so parsing it inside every fact — as the suite used to,
/// hundreds of times per run — cost far more than the assertions did. The parsed value is immutable once
/// read, which is what makes sharing it across parallel test classes safe; a fact that deliberately reads
/// tampered bytes still reads its own.
/// </summary>
internal static class TestPayload
{
    private static readonly SharedFixture<DaggerfallDefinitions> Shared = new(() => DaggerfallBaseContent.Read(CombinedBytes));
    private static readonly SharedFixture<byte[]> Combined = new(() => DaggerfallBaseContent.Combine(
        File.ReadAllBytes(PayloadPath("daggerfall.base.json")), File.ReadAllBytes(PayloadPath("daggerfall.imported.json"))));

    /// <summary>The parsed base definitions.</summary>
    internal static DaggerfallDefinitions Definitions => Shared.Value;

    /// <summary>
    /// The authored and imported sections joined as the ruleset joins them, each section's bytes as its
    /// file carries them, for the facts that tamper with a section or hand the bytes to a reader.
    /// </summary>
    internal static byte[] CombinedBytes => (byte[])Combined.Value.Clone();

    /// <summary>The joined sections as text.</summary>
    internal static string CombinedText => System.Text.Encoding.UTF8.GetString(Combined.Value);

    /// <summary>Whether the joined payload carries this text.</summary>
    internal static bool Contains(string text) =>
        Combined.Value.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(text)) >= 0;

    /// <summary>
    /// The joined payload with every occurrence of each edit's text replaced, edit by edit, exactly as an
    /// ordinal string replace would leave it. The edit runs on the UTF-8 bytes: decoding the whole payload
    /// to a string and encoding it back costs gigabytes per fact, which parallel facts cannot afford.
    /// </summary>
    internal static byte[] Replaced(params (string Before, string After)[] edits)
    {
        byte[] current = Combined.Value;
        foreach ((string before, string after) in edits)
            current = ReplaceAll(current, System.Text.Encoding.UTF8.GetBytes(before), System.Text.Encoding.UTF8.GetBytes(after));
        return ReferenceEquals(current, Combined.Value) ? (byte[])current.Clone() : current;
    }

    private static byte[] ReplaceAll(byte[] source, byte[] before, byte[] after)
    {
        if (before.Length == 0) throw new ArgumentException("An edit must name the text it replaces.", nameof(before));
        List<int> found = [];
        for (int from = 0, index; (index = source.AsSpan(from).IndexOf(before)) >= 0; from += index + before.Length)
            found.Add(from + index);
        if (found.Count == 0) return source;
        byte[] result = new byte[source.Length + found.Count * (after.Length - before.Length)];
        int read = 0, write = 0;
        foreach (int index in found)
        {
            source.AsSpan(read, index - read).CopyTo(result.AsSpan(write));
            write += index - read;
            after.CopyTo(result.AsSpan(write));
            write += after.Length;
            read = index + before.Length;
        }
        source.AsSpan(read).CopyTo(result.AsSpan(write));
        return result;
    }

    /// <summary>
    /// The named top-level sections of the joined payload, parsed alone, for a fact that edits them.
    /// Parsing the whole joined payload into a node tree costs gigabytes; a fact that changes one
    /// section reads only that section and hands the result to <see cref="Splice"/>.
    /// </summary>
    internal static System.Text.Json.Nodes.JsonObject Sections(params string[] names)
    {
        System.Text.Json.Nodes.JsonObject root = new();
        foreach ((string name, int start, int end) in SectionRanges(Combined.Value))
            if (names.Contains(name, StringComparer.Ordinal))
                root[name] = System.Text.Json.Nodes.JsonNode.Parse(Combined.Value.AsSpan(start, end - start));
        foreach (string name in names)
            if (!root.ContainsKey(name)) throw new ArgumentException($"The joined payload has no section '{name}'.", nameof(names));
        return root;
    }

    /// <summary>The joined payload with the given sections replaced by the edited ones from <see cref="Sections"/>.</summary>
    internal static byte[] Splice(System.Text.Json.Nodes.JsonObject sections)
    {
        byte[] combined = Combined.Value;
        // The result is allocated once at its exact size: a growable stream and its copy would hold two
        // payload-sized arrays at once.
        List<(int Start, int End, byte[] Replacement)> edits = [];
        foreach ((string name, int start, int end) in SectionRanges(combined))
            if (sections.TryGetPropertyValue(name, out System.Text.Json.Nodes.JsonNode? value))
                edits.Add((start, end, System.Text.Encoding.UTF8.GetBytes(value?.ToJsonString() ?? "null")));
        HashSet<string> present = [.. SectionRanges(combined).Select(range => range.Name)];
        if (sections.Select(property => property.Key).FirstOrDefault(name => !present.Contains(name)) is { } added)
            throw new ArgumentException($"Section '{added}' is not in the joined payload; splicing replaces sections, it does not add them.", nameof(sections));
        byte[] result = new byte[combined.Length + edits.Sum(edit => edit.Replacement.Length - (edit.End - edit.Start))];
        int read = 0, write = 0;
        foreach ((int start, int end, byte[] replacement) in edits)
        {
            combined.AsSpan(read, start - read).CopyTo(result.AsSpan(write));
            write += start - read;
            replacement.CopyTo(result.AsSpan(write));
            write += replacement.Length;
            read = end;
        }
        combined.AsSpan(read).CopyTo(result.AsSpan(write));
        return result;
    }

    private static IEnumerable<(string Name, int Start, int End)> SectionRanges(byte[] combined)
    {
        List<(string, int, int)> ranges = [];
        System.Text.Json.Utf8JsonReader reader = new(combined);
        if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            throw new InvalidOperationException("The joined payload is not a JSON object.");
        while (reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            int start = checked((int)reader.TokenStartIndex);
            reader.Skip();
            ranges.Add((name, start, checked((int)reader.BytesConsumed)));
        }
        return ranges;
    }

    private static string PayloadPath(string file) => System.IO.Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "payloads", file);
}
