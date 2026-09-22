using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>Source parameters remain intact even when an alias resolves to a corrected place.</summary>
public sealed record DaggerfallQuestPlace(string Name, string CanonicalName, int P1, int P2, int P3,
    uint? LocationKey, byte? TeleportTransfer, int SourceLine);

public sealed record DaggerfallQuestPlaces(ImportPublicationSource Source, IReadOnlyList<DaggerfallQuestPlace> Rows);

public static class DaggerfallQuestPlaceReader
{
    public static DaggerfallQuestPlaces Read(byte[] bytes, string sourcePath)
    {
        ImportPublicationSource source = new(sourcePath, ContentDigest.Compute(bytes), bytes.LongLength);
        source.Validate();
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Split('\n');
        bool schema = false;
        List<DaggerfallQuestPlace> rows = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Split("--", 2, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0 || line.StartsWith('-')) continue;
            if (line.StartsWith("schema:", StringComparison.OrdinalIgnoreCase))
            {
                if (schema || !line[(line.IndexOf(':') + 1)..].Split(',').Select(value => value.Trim()).SequenceEqual(["*name", "p1", "p2", "p3"]))
                    throw new Arena2FormatException(sourcePath, index + 1, "Expected one *name,p1,p2,p3 schema.");
                schema = true;
                continue;
            }
            string[] fields = line.Split(',').Select(value => value.Trim()).ToArray();
            if (!schema || fields.Length != 4 || fields[0].Length == 0 || !names.Add(fields[0])
                || !Number(fields[1], out int p1) || !Number(fields[2], out int p2) || !Number(fields[3], out int p3))
                throw new Arena2FormatException(sourcePath, index + 1, "Expected a unique place alias and three numeric parameters.");
            bool permanent = p1 > 0x300;
            if (permanent && (p1 > ushort.MaxValue || p2 is < 0 or > ushort.MaxValue))
                throw new Arena2FormatException(sourcePath, index + 1, "Permanent place code must fit two 16-bit words.");
            // Table comments number physical columns: columns 2/3 are p1/p2. The
            // donor Place.SelectFixedSite uses p1 as location and p2's low byte as marker.
            rows.Add(new(fields[0], fields[0] == "Mantellan_Crux" ? "MantellanCrux" : fields[0], p1, p2, p3,
                permanent ? ((uint)p1 << 16) | (uint)p2 : null, permanent ? (byte)(p2 & 0xff) : null, index + 1));
        }
        if (!schema || rows.Count == 0) throw new Arena2FormatException(sourcePath, 1, "Place table has no rows.");
        foreach (DaggerfallQuestPlace row in rows)
            if (!names.Contains(row.CanonicalName)) throw new Arena2FormatException(sourcePath, row.SourceLine, $"Canonical place '{row.CanonicalName}' is missing.");
        return new(source, rows);
    }

    private static bool Number(string text, out int value) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
        : int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
