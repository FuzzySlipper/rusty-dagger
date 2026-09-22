using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

public sealed record DaggerfallQuestCatalogRow(string Name, string Group, string? Membership,
    int MinimumRequirement, string RequirementKind, bool Adult, bool OneTime, bool Active,
    string SourceDisposition, string Notes, int SourceLine);

public sealed record DaggerfallQuestCatalog(ImportPublicationSource Source, IReadOnlyList<DaggerfallQuestCatalogRow> Rows);

/// <summary>Reads only the explicitly selected classic list; no runtime pack discovery or selection policy.</summary>
public static class DaggerfallQuestCatalogReader
{
    public static DaggerfallQuestCatalog Read(byte[] bytes, string sourcePath, IEnumerable<string> sourceFiles)
    {
        ImportPublicationSource source = new(sourcePath, ContentDigest.Compute(bytes), bytes.LongLength);
        source.Validate();
        HashSet<string> supplied = sourceFiles.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<DaggerfallQuestCatalogRow> rows = [];
        bool schema = false;
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal)) continue;
            if (line.StartsWith("schema:", StringComparison.OrdinalIgnoreCase))
            {
                if (schema || !line[(line.IndexOf(':') + 1)..].Split(',').Select(field => field.Trim())
                    .SequenceEqual(["*name", "group", "membership", "minReq", "flag", "notes"]))
                    throw new Arena2FormatException(sourcePath, index + 1, "Expected the classic quest-list schema.");
                schema = true;
                continue;
            }
            if (!schema) continue; // explanatory comments can use one dash before the schema
            bool active = !line.StartsWith('-');
            if (!active) line = line[1..].TrimStart();
            string[] fields = line.Split(',', 6).Select(field => field.Trim()).ToArray();
            // The disabled Oblivion rows omit membership entirely. Preserve that absence;
            // do not manufacture eligibility for rows the donor never offers.
            bool omittedMembership = !active && fields.Length == 5 && fields[1] == "Oblivion";
            if ((fields.Length != 6 && !omittedMembership) || fields[0].Length == 0 || fields[1].Length == 0
                || !names.Add(fields[0]))
                throw new Arena2FormatException(sourcePath, index + 1, "Malformed or duplicate classic quest row.");
            string? membership = omittedMembership ? null : fields[2];
            int requirementColumn = omittedMembership ? 2 : 3;
            if (!int.TryParse(fields[requirementColumn], NumberStyles.None, CultureInfo.InvariantCulture, out int requirement)
                || (membership is not null && membership.Length != 1))
                throw new Arena2FormatException(sourcePath, index + 1, "Invalid quest requirement or membership.");
            string flag = fields[requirementColumn + 1];
            if (flag is not ("0" or "1" or "X"))
                throw new Arena2FormatException(sourcePath, index + 1, $"Unknown classic quest flag '{flag}'.");
            bool social = fields[1] is "Commoners" or "Merchants" or "Nobility";
            string requirementKind = requirement >= 10 ? "reputation" : social ? "level" : omittedMembership ? "unspecified" : "rank";
            rows.Add(new(fields[0], fields[1], membership, requirement, requirementKind, flag == "X", flag == "1", active,
                supplied.Contains(fields[0]) ? "present" : "missing", fields[requirementColumn + 2], index + 1));
        }
        if (!schema || rows.Count == 0) throw new Arena2FormatException(sourcePath, 1, "Classic quest list contains no rows.");
        return new(source, rows);
    }
}
