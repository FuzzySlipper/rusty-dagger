using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

internal static class StaticNpcNormalization
{
    internal static IReadOnlyDictionary<(int, int), string> ReadGenders(DungeonLogicalSourceSet sources)
    {
        Dictionary<(int, int), string> result = [];
        if (!sources.TryGet("FLATS.CFG", out var source) || source is null) return result;
        string text = Encoding.UTF8.GetString(source.Bytes.Span).Replace("\r", "", StringComparison.Ordinal);
        foreach (string block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] lines = block.Trim().Split('\n');
            string[] pair = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || pair.Length != 2 || !int.TryParse(pair[0], out int archive) || !int.TryParse(pair[1], out int record))
                throw new InvalidOperationException("FLATS.CFG contains a malformed flat header or gender row.");
            // FlatsFile keeps the first entry when the source repeats a flat.
            result.TryAdd((archive, record), lines[2].Contains('2') ? "Female" : "Male");
        }
        return result;
    }

    // RDBLayout.NPCFlatArchives: layout NPCs, independently of quest activation.
    internal static bool IsDungeonNpc(int archive) => archive is 334 or 346 or 357 or >= 175 and <= 184;

    internal static DaggerfallWorldBlockStaticNpc Exterior(RmbBlockFlat flat, IReadOnlyDictionary<int, ClassicFaction> factions, IReadOnlyDictionary<(int, int), string> genders) =>
        new($"person/flat-{flat.Source.SourceOffset}", MeshGeometry.ToRightHanded(flat.Point), flat.TextureArchive, flat.TextureRecord,
            flat.Source.FactionId, RmbBlockContent.StaticNpcRace(flat.Source.FactionId, factions),
            genders.GetValueOrDefault((flat.TextureArchive, flat.TextureRecord)) ?? ((flat.Source.Flags & 32) != 0 ? "Female" : "Male"), flat.Source.SourceOffset);

    internal static DaggerfallWorldBlockStaticNpc Dungeon(RdbBlockFlat flat, IReadOnlyDictionary<int, ClassicFaction> factions) =>
        new($"person/flat-{flat.Index}", MeshGeometry.ToRightHanded(flat.Point), flat.Source.TextureArchive, flat.Source.TextureRecord,
            flat.Source.FactionOrMobileId, RmbBlockContent.StaticNpcRace(flat.Source.FactionOrMobileId, factions),
            (flat.Source.Flags & 32) != 0 ? "Female" : "Male", flat.Source.ObjectOffset);
}
