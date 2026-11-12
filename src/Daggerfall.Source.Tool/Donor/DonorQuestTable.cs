using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>Offline reader for id/name quest tables, not donor save serialization.</summary>
internal static class DonorQuestTable
{
    public static DaggerfallQuestTable Read(byte[] bytes, string sourcePath, bool globals = false)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        PublishedSource source = new(sourcePath, ContentDigest.Compute(bytes), bytes.LongLength);
        source.Validate();
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Split('\n');
        List<DaggerfallQuestTableRow> rows = [];
        Dictionary<string, int> names = new(StringComparer.OrdinalIgnoreCase);
        bool schema = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Split("--", 2, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0 || line.StartsWith('-')) continue;
            if (line.StartsWith("schema:", StringComparison.OrdinalIgnoreCase))
            {
                if (schema || !line[(line.IndexOf(':') + 1)..].Split(',').Select(value => value.Trim()).SequenceEqual(["id", "*name"]))
                    throw new Arena2FormatException(sourcePath, index + 1, "Expected one id,*name schema.");
                schema = true;
                continue;
            }
            string[] fields = line.Split(',');
            if (!schema || fields.Length != 2 || !int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || id < 0 || (globals && id >= 64) || string.IsNullOrWhiteSpace(fields[1]))
                throw new Arena2FormatException(sourcePath, index + 1, "Expected a valid numeric id and non-empty alias.");
            string name = fields[1].Trim();
            if (names.TryGetValue(name, out int previous) && previous != id)
                throw new Arena2FormatException(sourcePath, index + 1, $"Alias '{name}' names both {previous} and {id}.");
            names[name] = id;
            rows.Add(new(id, name, index + 1));
        }
        if (!schema || rows.Count == 0)
            throw new Arena2FormatException(sourcePath, 1, "Quest table has no schema or rows.");
        if (globals && rows.Select(row => row.Id).Distinct().Count() != 64)
            throw new Arena2FormatException(sourcePath, 1, "Quest globals must retain all 64 slots.");
        return new(source, rows);
    }
}
