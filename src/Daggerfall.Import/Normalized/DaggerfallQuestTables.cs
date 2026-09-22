using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>A source row, retaining aliases and its original one-based line.</summary>
public sealed record DaggerfallQuestTableRow(int Id, string Name, int SourceLine);

/// <summary>Named numeric identities with source provenance; aliases share numeric identity.</summary>
public sealed record DaggerfallQuestTable(ImportPublicationSource Source, IReadOnlyList<DaggerfallQuestTableRow> Rows)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, int> Lookup => Rows.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
}

public sealed record DaggerfallQuestTables(DaggerfallQuestTable Globals, DaggerfallQuestTable StaticMessages,
    DaggerfallQuestPlaces Places, DaggerfallQuestTable Sounds,
    DaggerfallQuestTable Diseases, DaggerfallQuestTable Spells, DaggerfallQuestActorItemTables ActorItemTables);

/// <summary>Offline reader for id/name quest tables, not donor save serialization.</summary>
public static class DaggerfallQuestTableReader
{
    public static DaggerfallQuestTable Read(byte[] bytes, string sourcePath, bool globals = false)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ImportPublicationSource source = new(sourcePath, ContentDigest.Compute(bytes), bytes.LongLength);
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
