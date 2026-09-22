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
        QuestParameterTable table = QuestParameterTableReader.Read(bytes, sourcePath, ["*name", "p1", "p2", "p3"]);
        List<DaggerfallQuestPlace> rows = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuestParameterTableRow row in table.Rows.Where(row => row.Active))
        {
            if (!names.Add(row.Name)
                || !QuestParameterTableReader.TryNumber(row.Parameters[0], out int p1)
                || !QuestParameterTableReader.TryNumber(row.Parameters[1], out int p2)
                || !QuestParameterTableReader.TryNumber(row.Parameters[2], out int p3))
                throw new Arena2FormatException(sourcePath, row.SourceLine, "Expected a unique place alias and three numeric parameters.");
            bool permanent = p1 > 0x300;
            if (permanent && (p1 > ushort.MaxValue || p2 is < 0 or > ushort.MaxValue))
                throw new Arena2FormatException(sourcePath, row.SourceLine, "Permanent place code must fit two 16-bit words.");
            // Table comments number physical columns: columns 2/3 are p1/p2. The
            // donor Place.SelectFixedSite uses p1 as location and p2's low byte as marker.
            rows.Add(new(row.Name, row.Name == "Mantellan_Crux" ? "MantellanCrux" : row.Name, p1, p2, p3,
                permanent ? ((uint)p1 << 16) | (uint)p2 : null, permanent ? (byte)(p2 & 0xff) : null, row.SourceLine));
        }
        if (rows.Count == 0) throw new Arena2FormatException(sourcePath, 1, "Place table has no rows.");
        foreach (DaggerfallQuestPlace row in rows)
            if (!names.Contains(row.CanonicalName)) throw new Arena2FormatException(sourcePath, row.SourceLine, $"Canonical place '{row.CanonicalName}' is missing.");
        return new(table.Source, rows);
    }
}
