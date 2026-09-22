using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>A non-row comment preserved from a quest table with its original one-based line.</summary>
public sealed record DaggerfallQuestTableComment(string Text, int SourceLine);

/// <summary>One parameter-table row, retaining its source key, ordered parameters and source disposition.</summary>
internal sealed record QuestParameterTableRow(string Name, IReadOnlyList<string> Parameters, bool Active, int SourceLine);

/// <summary>A parameter table's source evidence, ordered rows and retained comments.</summary>
internal sealed record QuestParameterTable(
    ImportPublicationSource Source,
    IReadOnlyList<QuestParameterTableRow> Rows,
    IReadOnlyList<DaggerfallQuestTableComment> Comments);

/// <summary>
/// Shared scanner for the donor's named quest parameter tables. It retains the donor's source
/// names, line order, disabled rows and comments; each owning reader interprets its own fields.
/// </summary>
internal static class QuestParameterTableReader
{
    internal static QuestParameterTable Read(byte[] bytes, string sourcePath, IReadOnlyList<string> expectedSchema)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ImportPublicationSource source = new(sourcePath, ContentDigest.Compute(bytes), bytes.LongLength);
        source.Validate();
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Split('\n');
        List<QuestParameterTableRow> rows = [];
        List<DaggerfallQuestTableComment> comments = [];
        int primaryColumn = Enumerable.Range(0, expectedSchema.Count).Single(index => expectedSchema[index] == "*name");
        bool schema = false;
        for (int index = 0; index < lines.Length; index++)
        {
            int sourceLine = index + 1;
            string line = lines[index].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                AddComment(line[2..], sourceLine);
                continue;
            }

            bool active = true;
            if (line[0] == '-')
            {
                string disabled = line[1..].Trim();
                if (!TryFields(disabled, expectedSchema.Count, primaryColumn, out string name, out string[] parameters, out string? inlineComment))
                {
                    AddComment(disabled, sourceLine);
                    continue;
                }

                active = false;
                AddRow(name, parameters, active, sourceLine);
                if (inlineComment is not null) AddComment(inlineComment, sourceLine);
                continue;
            }

            string content = line.Split("--", 2, StringSplitOptions.None)[0].Trim();
            string? comment = line.Contains("--", StringComparison.Ordinal) ? line[(line.IndexOf("--", StringComparison.Ordinal) + 2)..].Trim() : null;
            if (content.StartsWith("schema:", StringComparison.OrdinalIgnoreCase))
            {
                if (schema || !content[(content.IndexOf(':') + 1)..].Split(',').Select(value => value.Trim()).SequenceEqual(expectedSchema))
                {
                    throw new Arena2FormatException(sourcePath, sourceLine, $"Expected one {string.Join(',', expectedSchema)} schema.");
                }

                schema = true;
                if (comment is not null) AddComment(comment, sourceLine);
                continue;
            }

            if (!TryFields(content, expectedSchema.Count, primaryColumn, out string rowName, out string[] rowParameters, out _))
            {
                throw new Arena2FormatException(sourcePath, sourceLine, $"Expected a source name and {expectedSchema.Count - 1} parameters.");
            }

            if (!schema)
            {
                throw new Arena2FormatException(sourcePath, sourceLine, "Quest table row appears before its schema.");
            }

            AddRow(rowName, rowParameters, active, sourceLine);
            if (comment is not null) AddComment(comment, sourceLine);
        }

        if (!schema || rows.Count == 0)
        {
            throw new Arena2FormatException(sourcePath, 1, "Quest table has no schema or rows.");
        }

        return new(source, rows, comments);

        void AddRow(string name, string[] parameters, bool active, int sourceLine)
        {
            rows.Add(new(name, parameters, active, sourceLine));
        }

        void AddComment(string text, int sourceLine)
        {
            string trimmed = text.Trim();
            if (trimmed.Length != 0) comments.Add(new(trimmed, sourceLine));
        }
    }

    internal static int Number(string text, string sourcePath, int sourceLine) => TryNumber(text, out int value)
        ? value : throw new Arena2FormatException(sourcePath, sourceLine, $"Expected numeric parameter '{text}'.");

    internal static bool TryNumber(string text, out int value) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
        : int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static bool TryFields(string text, int expectedCount, int primaryColumn, out string name, out string[] parameters, out string? inlineComment)
    {
        string[] parts = text.Split("--", 2, StringSplitOptions.None);
        inlineComment = parts.Length == 2 ? parts[1].Trim() : null;
        string[] fields = parts[0].Split(',').Select(value => value.Trim()).ToArray();
        name = fields.Length > primaryColumn ? fields[primaryColumn] : string.Empty;
        parameters = fields.Where((_, index) => index != primaryColumn).ToArray();
        return fields.Length == expectedCount && name.Length != 0 && parameters.All(value => value.Length != 0);
    }
}
