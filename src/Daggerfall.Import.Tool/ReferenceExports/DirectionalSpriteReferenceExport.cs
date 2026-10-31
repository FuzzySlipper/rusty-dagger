using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>Original directional character archives outside an existing mobile-reference export.</summary>
internal static class DirectionalSpriteReferenceExport
{
    private static readonly string[] Directions = ["front", "front-right", "right", "back-right", "back"];
    private static readonly int[] StaticPeopleArchives = [175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 186, 197, 334, 346, 357];

    internal sealed record Source(int Archive, string Name, string Family, string? DisplayRace = null,
        string? Gender = null, int? Outfit = null);
    internal sealed record ReferenceFrame(string File, string Direction, int Orientation, int SourceRecord,
        int SourceFrame, string SourceState, int[] Size, bool Mirrored = false);

    // MobilePersonBillboard's display families, not the player's available races. In particular,
    // archive 386 is assigned to Breton even though its archive header says "MALE NORD".
    internal static IEnumerable<Source> Sources()
    {
        (string Race, string Gender, int[] Archives)[] families =
        [
            ("Redguard", "male", [381, 382, 383, 384]),
            ("Redguard", "female", [395, 396, 397, 398]),
            ("Nord", "male", [387, 388, 389, 390]),
            ("Nord", "female", [392, 393, 451, 452]),
            ("Breton", "male", [385, 386, 391, 394]),
            ("Breton", "female", [453, 454, 455, 456]),
        ];
        foreach ((string race, string gender, int[] archives) in families)
            for (int outfit = 0; outfit < archives.Length; outfit++)
                yield return new(archives[outfit], $"{race} {gender} outfit {outfit + 1}", "townspeople", race, gender, outfit + 1);
        yield return new(399, "City Watch", "guard");
        yield return new(299, "Female vampire alternate archive", "supplemental");
        yield return new(457, "Bear alternate archive", "supplemental");
        yield return new(473, "Lysandus", "supplemental");
        yield return new(491, "Female heavy horse rider", "supplemental");
        yield return new(492, "Male heavy horse rider", "supplemental");
        yield return new(493, "Female medium horse rider", "supplemental");
        yield return new(494, "Male medium horse rider", "supplemental");
        yield return new(495, "Female light horse rider", "supplemental");
    }

    public static void Run(string arena2, string baseline, string output)
    {
        string baselineManifest = Path.Combine(Path.GetFullPath(baseline), "manifest.json");
        using JsonDocument previous = JsonDocument.Parse(File.ReadAllBytes(baselineManifest));
        Dictionary<int, string[]> covered = previous.RootElement.GetProperty("mobiles").EnumerateArray()
            .GroupBy(mobile => mobile.GetProperty("sourceArchive").GetInt32())
            .ToDictionary(group => group.Key, group => group.Select(mobile => mobile.GetProperty("directory").GetString()!).ToArray());
        ReferenceExport export = new(arena2, output);
        Dictionary<int, string> dispositions = covered.ToDictionary(pair => pair.Key, _ => "covered-by-baseline");
        List<object> overlaps = [], aliases = [];
        List<Source> exported = [];
        foreach (Source source in Sources().OrderBy(source => source.Archive))
        {
            string file = $"TEXTURE.{source.Archive:000}";
            if (!export.FileNames.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                export.Warn($"{file} ({source.Name}) is not in the supplied corpus.");
                continue;
            }
            if (covered.TryGetValue(source.Archive, out string[]? directories))
            {
                overlaps.Add(new { source.Archive, source.Name, directories });
                continue;
            }
            // Archive 299 is a second supplied copy of Vampire 283. Compare the actual files so
            // a changed alternate archive is exported instead of silently losing distinct art.
            if (source.Archive == 299 && covered.TryGetValue(283, out string[]? vampireDirectories)
                && export.Read(file).AsSpan().SequenceEqual(export.Read("TEXTURE.283")))
            {
                aliases.Add(new { archive = 299, identicalToArchive = 283, directories = vampireDirectories,
                    comparison = "Entire supplied archive files are byte-identical; no additional character art." });
                dispositions[299] = "identical-to-covered-archive-283";
                continue;
            }
            Export(export, source);
            exported.Add(source);
            dispositions[source.Archive] = "exported-" + source.Family;
        }

        List<object> audit = [];
        List<int> unclassified = [];
        List<int> unreadable = [];
        int suppliedArchives = 0;
        foreach (string file in export.FileNames.Where(file => file.StartsWith("TEXTURE.", StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse(file.AsSpan(8), out int archive)) continue;
            suppliedArchives++;
            try
            {
                TextureArchive texture = export.Texture(archive);
                int[] frameCounts = Enumerable.Range(0, texture.RecordCount).Select(record => (int)texture.GetRecordInfo(record).FrameCount).ToArray();
                // Repeated animation in five records only screens for candidates: guild people
                // and lights also match, but their records depict independent billboards.
                bool candidate = frameCounts.Length >= 5 && frameCounts.Take(5).All(count => count >= 3);
                string disposition = dispositions.GetValueOrDefault(archive) ??
                    (StaticPeopleArchives.Contains(archive) || archive == 210 ? "independent-billboard-records" : "outside-character-scope");
                if (candidate && disposition == "outside-character-scope")
                {
                    unclassified.Add(archive);
                    export.Warn($"{file} has five animated records and has not been classified; inspect archive-audit.json before treating the directional corpus as complete.");
                }
                if (candidate || dispositions.ContainsKey(archive) || StaticPeopleArchives.Contains(archive))
                    audit.Add(new { archive, sourceName = ArchiveName(export, archive), records = texture.RecordCount,
                        frameCounts, fiveAnimatedRecords = candidate, disposition });
            }
            catch (IOException exception)
            {
                unreadable.Add(archive);
                export.Warn($"{file} could not be audited: {exception.Message}");
                audit.Add(new { archive, disposition = "unreadable", reason = exception.Message });
            }
        }
        export.WriteJson("archive-audit.json", new { suppliedArchives, unclassifiedCandidates = unclassified, unreadableArchives = unreadable, archives = audit,
            note = "Animation counts screen for candidates, not camera directions. Static NPCs use independent records, sometimes animated. Supplemental source archives do not establish a gameplay consumer." });
        export.Finish("Daggerfall remaining directional sprite references",
            new { exportedArchives = exported.Count, townspeople = exported.Count(source => source.Family == "townspeople"),
                supplemental = exported.Count(source => source.Family == "supplemental"), referenceDirections = exported.Count * 5,
                originalAnimationFrames = exported.Sum(source => Enumerable.Range(0, export.Texture(source.Archive).RecordCount)
                    .Sum(record => export.Texture(source.Archive).GetRecordInfo(record).FrameCount)),
                baselineVariants = previous.RootElement.GetProperty("mobiles").GetArrayLength(), baselineArchives = covered.Count,
                suppliedArchives, unclassifiedCandidates = unclassified, unreadableArchives = unreadable },
            new { baselineManifest, overlaps, aliases, audit = "archive-audit.json",
                directionConvention = "Importer sectors 0-4: front, front-right, right, back-right, back. Labels are source-sector names, not anatomical sides. No mirrored sectors are exported.",
                selection = "Record 0-4 frame 0 provides the five reference PNGs. These are starting references, not visually curated rest phases. Every original record and animation frame is retained under frames/.",
                townspeople = "24 display outfits: Breton, Nord, Redguard; two genders; four outfits each. Walk records 0-4 at 4 fps; record 5 is front-only idle at 1 fps, not a sixth direction.",
                supplemental = "Bear 457, Lysandus 473 and mounted figures 491-495 are source-only supplemental sets. Five-record groups retain record numbers without inventing action names or asserting runtime use.",
                spectral = "473 uses the existing Arena2SpectralTextures grey ramp and partial alpha, not raw palette-index colours.",
                donorSources = new[] { "Assets/Scripts/Internal/MobilePersonBillboard.cs: texture families, MoveAnims, IdleAnims, IdleAnimsGuard",
                    "Assets/Scripts/Game/MobilePersonNPC.cs: display races and outfit variants", "Assets/Scripts/Utility/RDBLayout.cs: NPCFlatArchives",
                    "Assets/Scripts/API/TextureFile.cs: IsSpectralArchive", "Arena2 TEXTURE archive headers and record tables" } },
            "Open index.html for the visual catalog and index.csv for the directory list. Each subject has five original unmirrored direction PNGs, manifest.json and every original record/frame under frames/.\n" +
            "Townspeople are grouped by display race, gender and outfit. The two idle frames are front-only; walk references use frame 0 and have not undergone the mobile corpus's pose curation. The full animation is retained for later selection.\n" +
            "supplemental/ contains other directional character source archives outside the baseline; their gameplay use is not established. Other animation groups retain neutral source-record identities. Lysandus uses the existing spectral conversion.\n" +
            "The baseline is read only. Covered archives and exact duplicate aliases are recorded in manifest.json rather than copied. Static/special NPC billboards are catalogued in archive-audit.json, not exported as invented directional sets.\n" +
            "Native dimensions, palette and transparency; no mirroring, resizing, pose edits or generated views. See manifest.json for the baseline path and decoder provenance.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-directional-sprites --baseline local/exports/daggerfall-mobile-idle-unmirrored-r01 --output local/exports/NEW_DIRECTIONAL_DIRECTORY\n" +
            "Use a fresh output directory to preserve references and experiments. Extracted art stays ignored; no inference is submitted.",
            exported.Select(source => $"arena2/TEXTURE.{source.Archive:000}"));
        StringBuilder csv = new("archive,name,family,displayRace,gender,outfit,directory,sourceRecords,sourceFrames,referenceDirections,selection\n");
        foreach (Source source in exported)
        {
            TextureArchive texture = export.Texture(source.Archive);
            string[] fields = [$"{source.Archive}", source.Name, source.Family, source.DisplayRace ?? "", source.Gender ?? "",
                $"{source.Outfit}", DirectoryFor(source), $"{texture.RecordCount}",
                $"{Enumerable.Range(0, texture.RecordCount).Sum(record => texture.GetRecordInfo(record).FrameCount)}", "5", "first-original-frame"];
            csv.AppendLine(string.Join(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")));
        }
        File.WriteAllText(Path.Combine(export.Root, "index.csv"), csv.ToString());
    }

    private static void Export(ReferenceExport export, Source source)
    {
        TextureArchive texture = export.Texture(source.Archive);
        string directory = DirectoryFor(source);
        List<ReferenceImage> images = [];
        List<ReferenceFrame> references = [];
        for (int record = 0; record < texture.RecordCount; record++)
        {
            TextureRecordInfo info = texture.GetRecordInfo(record);
            string role = source.Family == "townspeople"
                ? record < 5 ? "walk/" + Directions[record] : record == 5 ? "idle/front" : $"source-record-{record:000}"
                : $"source-record-{record:000}";
            if (info.FrameCount == 0) export.Warn($"TEXTURE.{source.Archive:000} record {record} has no frames.");
            for (int frame = 0; frame < info.FrameCount; frame++)
            {
                IndexedTextureFrame decoded = texture.DecodeFrame(record, frame);
                byte[] rgba = Arena2SpectralTextures.IsSpectralArchive((ushort)source.Archive)
                    ? Arena2SpectralTextures.ToRgbaBytes(decoded.Pixels.Span, export.Palette, texture.Source)
                    : decoded.ToRgba(export.Palette, PaletteAlphaMode.IndexZeroTransparent);
                images.Add(export.Rgba(directory, $"frames/record-{record:000}/frame-{frame:000}.png", role,
                    texture.Source, record, frame, decoded.Width, decoded.Height, info.XOffset, info.YOffset, rgba, ["source"]));
                if (record < 5 && frame == 0)
                {
                    string direction = Directions[record];
                    images.Add(export.Rgba(directory, direction + ".png", "reference/" + direction, texture.Source,
                        record, frame, decoded.Width, decoded.Height, info.XOffset, info.YOffset, rgba, ["source"]));
                    references.Add(new($"{directory}/{direction}.png", direction, record, record, frame,
                        source.Family == "townspeople" ? "move" : "source-record-group-0", [decoded.Width, decoded.Height]));
                }
            }
        }
        string note = source.Family == "townspeople"
            ? "Walk records 0-4, 4 fps; front-only idle record 5, 1 fps. Five frame-0 source references; all walk/idle phases retained."
            : "Supplemental original archive. All source records retained; action names and runtime use are not established. Five first-group frame-0 references.";
        export.Subject(directory, source.Name, new { kind = "directional-sprite", source.Name, directory,
            sourceArchive = source.Archive, sourceName = ArchiveName(export, source.Archive), source.Family,
            source.DisplayRace, source.Gender, source.Outfit, selectionMode = "first-original-frame", sourceFrame = 0,
            records = texture.RecordCount, frames = references, spectral = Arena2SpectralTextures.IsSpectralArchive((ushort)source.Archive),
            note, images }, images.OrderBy(image => image.Role.StartsWith("reference/", StringComparison.Ordinal) ? 0 : 1).ToArray(), note);
    }

    private static string ArchiveName(ReferenceExport export, int archive) =>
        Encoding.ASCII.GetString(export.Read($"TEXTURE.{archive:000}"), 2, 24).TrimEnd('\0').Trim();

    private static string DirectoryFor(Source source) =>
        $"{source.Family}/texture-{source.Archive:000}-{ReferenceExport.Slug(source.Name)}";
}
