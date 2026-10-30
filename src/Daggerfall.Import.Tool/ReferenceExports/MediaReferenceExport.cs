using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>
/// Source art the item and spell exports do not carry: the paper-doll bodies, faces and creature heads, and the
/// effect texture archives beyond the element missiles. Native frames only; a file the decoders refuse is reported.
/// </summary>
internal static class MediaReferenceExport
{
    /// <summary>The paper-doll families: body by race and gender, face, lycanthrope and vampire art.</summary>
    private static readonly string[] PaperDollPrefixes = ["BODY", "FACE", "WERE", "WOLF", "BOAR", "VAMP"];

    /// <summary>
    /// Effect archives with no other export. TEXTURE.380 is the shared effect set (blood splash, corpse, sparkle) and
    /// TEXTURE.434 the inventory glints. TEXTURE.370-372 (interior surfaces) and 374 (doors) are world surfaces, not effects.
    /// </summary>
    private static readonly (int Archive, string Note)[] EffectArchives =
    [
        (380, "Shared effect set: animated blood splash (record 0), corpse (record 1) and the magic sparkle records."),
        (434, "Inventory glint textures (DaggerfallInventoryWindow coinsAnimTextureName/magicAnimTextureName): magic glint animation, a skeleton corpse and sparkle frames."),
    ];

    public static void RunPaperDoll(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        List<string> sources = [];
        foreach (string file in export.FileNames.Where(name => PaperDollPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            && (name.EndsWith(".IMG", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".CIF", StringComparison.OrdinalIgnoreCase))))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            string directory = $"{Family(stem)}/{stem.ToLowerInvariant()}";
            List<ReferenceImage> images = [];
            IReadOnlyList<IndexedImg>? frames = Decode(export, file);
            if (frames is null) continue;
            sources.Add("arena2/" + file);
            for (int record = 0; record < frames.Count; record++)
            {
                IndexedImg frame = frames[record];
                images.AddRange(export.Indexed(directory, $"{stem.ToLowerInvariant()}-record-{record:000}-frame-000", "paper-doll", "arena2/" + file,
                    record, 0, frame.Width, frame.Height, frame.XOffset, frame.YOffset, frame.Pixels.ToArray()));
            }
            export.Subject(directory, stem, new { kind = "paper-doll-layer", family = Family(stem), file, directory, records = frames.Count, images }, images,
                $"{file}: {frames.Count} record(s), drawn with ART_PAL.COL and index 0 transparent. The donor's naming: a body is BODY{{race}}I0/I1 for a male and BODY{{race+10}}I0/I1 for a female; a face is FACE{{race}}I0 and FACE{{race+10}}I0.");
        }

        export.Finish("Daggerfall paper-doll references", new { files = sources.Count },
            new { naming = "BODY{race}I0/I1 male, BODY{race+10}I0/I1 female; FACE{race}I0 and FACE{race+10}I0; I0 unclothed and I1 clothed.",
                note = "Worn-item layers and dyes are in the item export (player role); these are the bodies and heads they are drawn over." },
            "Open index.html for the visual catalog; index.csv lists every file. Native-resolution frames, palette ART_PAL.COL, index 0 transparent, never resized or mirrored.\n" +
            "bodies/, faces/ and creatures/ hold the BODY, FACE and WERE/WOLF/BOAR/VAMP files; each file is its own subject with every record it carries.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-paper-doll --output authoring/df-raw/NEW_PAPER_DOLL_DIRECTORY\n" +
            "Use a fresh output directory to preserve experiments. No generation jobs have been submitted. Extracted art stays in ignored authoring/df-raw/.",
            sources);
    }

    public static void RunEffects(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        List<string> sources = [];
        foreach ((int archive, string note) in EffectArchives)
        {
            string name = $"TEXTURE.{archive:000}";
            if (!export.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)) { export.Warn($"{name} is not in the supplied corpus."); continue; }
            TextureArchive texture = export.Texture(archive);
            string directory = $"texture-{archive:000}";
            List<ReferenceImage> images = [];
            for (int record = 0; record < texture.RecordCount; record++)
                images.AddRange(export.TextureImages(directory, "effect", archive, record));
            sources.Add("arena2/" + name);
            export.Subject(directory, $"{name} ({texture.RecordCount} records)", new { kind = "effect-archive", archive, directory, records = texture.RecordCount, note, images }, images, note);
        }

        export.Finish("Daggerfall effect references", new { archives = sources.Count },
            new { note = "The element missiles and impacts (TEXTURE.375-379) and the casting hands are in the spell export; TEXTURE.380 records 1 and 3 also appear there and in the item export." },
            "Open index.html for the visual catalog. Every record and frame of each archive, native resolution, ART_PAL.COL with index 0 transparent.\n" +
            "380 is the shared effect set and 434 the inventory glints. TEXTURE.370-372 (interior surfaces) and 374 (doors) are world surfaces and are not exported here.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-effects --output authoring/df-raw/NEW_EFFECTS_DIRECTORY\n" +
            "Use a fresh output directory to preserve experiments. No generation jobs have been submitted. Extracted art stays in ignored authoring/df-raw/.",
            sources);
    }

    private static string Family(string stem) => stem.StartsWith("BODY", StringComparison.OrdinalIgnoreCase) ? "bodies"
        : stem.StartsWith("FACE", StringComparison.OrdinalIgnoreCase) ? "faces" : "creatures";

    /// <summary>Decodes a file as a headerless canvas, one IMG record or a CIF record sequence; null, after a warning, when none apply.</summary>
    private static IReadOnlyList<IndexedImg>? Decode(ReferenceExport export, string file)
    {
        byte[] bytes = export.Read(file);
        string source = "arena2/" + file;
        try
        {
            if (file.EndsWith(".CIF", StringComparison.OrdinalIgnoreCase)) return ImgDecoder.DecodeRecordSequence(bytes, source);
            if (ImgDecoder.TryDecodeHeaderless(bytes, source, out IndexedImg? headerless, out _)) return [headerless];
            return [ImgDecoder.Decode(bytes, source)];
        }
        catch (Exception ex)
        {
            export.Warn($"{source} was not exported: {ex.Message}");
            return null;
        }
    }
}
