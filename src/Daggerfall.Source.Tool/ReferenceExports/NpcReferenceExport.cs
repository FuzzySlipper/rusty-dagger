using System.Text;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.ReferenceExports;

internal sealed record NpcArt(string Label, IReadOnlyList<ReferenceImage> Frames);
internal sealed record NpcLink(string Label, string Href);
internal sealed record NpcEntry(string Id, string Name, string Kind, IReadOnlyList<NpcArt> Art,
    IReadOnlyList<NpcPlacement> Placements, IReadOnlyDictionary<string, string> Facts, IReadOnlyList<NpcLink> Links,
    string[] Aliases, bool ShowInChart = true)
{
    public ReferenceImage? Thumbnail => Art.SelectMany(art => art.Frames).FirstOrDefault();
}

internal static class NpcReferenceExport
{
    private const string SpecialFaces = "FACES.CIF";
    private const string CommonFaces = "TFAC00I0.RCI";
    private const string ChildFaces = "KIDS00I0.CIF";
    private const string ChildPortraits = "CHLD00I0.RCI";
    private const int PlaceholderFace = 410;
    private const int PortraitSide = 64;
    private const int PortraitCellBytes = PortraitSide * PortraitSide;

    // These have NPC-related metadata, but the images depict a barn door, a standing light and a brazier.
    private static readonly HashSet<NpcFlat> Props = [new(171, 1), new(197, 6), new(210, 0)];

    // Distinct figure art in the noble test archive and two mixed scenery archives.
    // The other noble figures, and 195:19–22, duplicate frames in the main people archives.
    private static readonly HashSet<NpcFlat> SupplementalFlats =
    [
        new(185, 0), new(185, 1), new(185, 10), new(185, 11),
        .. Enumerable.Range(0, 19).Select(record => new NpcFlat(195, record)),
        new(195, 24), new(198, 8), new(198, 9),
    ];

    // Daggerfall Unity's MobilePersonNPC face banks, matched to MobilePersonBillboard's outfits.
    private static readonly Dictionary<int, int> FaceStarts = new()
    {
        [381] = 336, [382] = 312, [383] = 336, [384] = 312, [395] = 144, [396] = 144, [397] = 120, [398] = 96,
        [387] = 240, [388] = 264, [389] = 168, [390] = 192, [392] = 72, [393] = 0, [451] = 48, [452] = 0,
        [385] = 192, [386] = 216, [391] = 288, [394] = 240, [453] = 72, [454] = 72, [455] = 24, [456] = 72,
    };

    private sealed record FlatCaption(string Caption, string Gender, int Face);

    public static void Run(string arena2, string basePayload, string characters, string output)
    {
        ReferenceExport export = new(arena2, output);
        const string factionSource = "arena2/FACTION.TXT";
        Dictionary<int, ClassicFaction> factions = FactionReader.Read(Encoding.UTF8.GetString(export.Read("FACTION.TXT")), factionSource)
            .ToDictionary(faction => faction.Id);
        Dictionary<NpcFlat, FlatCaption> captions = ReadCaptions(export);
        HashSet<int> archives = [.. PeopleReferenceExport.StaticArchives];
        HashSet<NpcFlat> listed = [.. captions.Keys, .. SupplementalFlats];
        ILookup<NpcFlat, NpcPlacement> placements = NpcReferenceWorld.ReadPlacements(export, archives, listed)
            .Where(placement => IsPerson(placement.Flat)).ToLookup(placement => placement.Flat);
        ILookup<int, NpcPlacement> factionPlacements = placements.SelectMany(group => group).ToLookup(placement => placement.Faction);
        NpcReferenceWorld world = new(export, basePayload, placements.SelectMany(group => group).Select(placement => placement.Block));

        var factionFlats = factions.Values.SelectMany(faction => faction.Flats.Where(id => id > 0)
            .Select(id => (Flat: NpcFlat.Decode(id), Faction: faction))).ToLookup(value => value.Flat, value => value.Faction);
        HashSet<NpcFlat> flatKeys = [.. archives.SelectMany(archive => Enumerable.Range(0, export.Texture(archive).RecordCount)
            .Select(record => new NpcFlat(archive, record))), .. listed, .. placements.Select(group => group.Key), .. factionFlats.Select(group => group.Key)];

        HashSet<string> files = export.FileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<NpcFlat, NpcArt> spriteArt = [];
        foreach (NpcFlat flat in flatKeys.Where(IsPerson).OrderBy(flat => flat.Number))
        {
            string file = $"TEXTURE.{flat.Archive:000}";
            if (!files.Contains(file))
            {
                export.Warn($"{flat.Id} refers to absent {file}, record {flat.Record}.");
                continue;
            }
            IReadOnlyList<ReferenceImage> images = export.TextureImages("art", "sprite", flat.Archive, flat.Record);
            if (images.Count > 0) spriteArt.Add(flat, new(images.Count == 1 ? "Billboard sprite" : "Billboard animation", images));
        }

        var portraits = new PortraitBank(export);
        List<NpcEntry> entries = [];
        // Faction ids and sprite ids are independent. For example, two named NPCs can use one flat.
        ILookup<NpcFlat, ClassicFaction> namedByFlat = factions.Values.Where(IsNamedPerson)
            .SelectMany(faction => faction.Flats.Where(id => id > 0).Select(NpcFlat.Decode)
                .Concat(factionPlacements[faction.Id].Select(placement => placement.Flat)).Distinct()
                .Where(spriteArt.ContainsKey).Select(flat => (Flat: flat, Faction: faction)))
            .ToLookup(value => value.Flat, value => value.Faction);
        var namedFlats = namedByFlat.SelectMany(group => group.Select(faction => (faction.Id, Flat: group.Key)))
            .ToLookup(value => value.Id, value => value.Flat);
        HashSet<int> namedSpecialFaces = [];
        foreach (ClassicFaction faction in factions.Values.Where(IsNamedPerson).OrderBy(faction => faction.Name, StringComparer.OrdinalIgnoreCase))
        {
            NpcFlat[] flats = [.. namedFlats[faction.Id].OrderBy(flat => flat.Number)];
            List<NpcArt> art = [.. flats.Select(flat => spriteArt[flat])];
            string portraitSource = faction.Face <= 60 ? SpecialFaces : CommonFaces;
            if (faction.Face >= 0 && portraits.Get(portraitSource, faction.Face) is { } portrait)
            {
                art.Add(new("Dialogue portrait", [portrait]));
                if (portraitSource == SpecialFaces) namedSpecialFaces.Add(faction.Face);
            }
            if (art.Count == 0) continue;

            Dictionary<string, string> facts = new()
            {
                ["Role"] = faction.Type == 0 ? "Daedric quest patron" : "Named character",
                ["Faction ID"] = faction.Id == faction.FiledId ? $"{faction.Id}" : $"{faction.Id} (filed as {faction.FiledId})",
            };
            if (faction.Region >= 0) facts["Faction region"] = world.Region(faction.Region);
            if (factions.TryGetValue(faction.Parent, out ClassicFaction? parent)) facts["Affiliation"] = parent.Name;
            if (!factionPlacements[faction.Id].Any()) facts["World placements"] = "No fixed block placements found for this identity.";

            entries.Add(new(FactionId(faction), faction.Name, "Named NPC", art, [.. factionPlacements[faction.Id]], facts,
                [.. flats.Select(flat => new NpcLink(flat.Id, $"../{flat.Id}/index.html"))], []));
        }

        foreach ((NpcFlat flat, NpcArt sprites) in spriteArt)
        {
            ClassicFaction[] named = [.. namedByFlat[flat]];
            var uses = placements[flat].ToArray();
            ClassicFaction[] genericFactions = [.. factionFlats[flat].Concat(uses.Select(use => factions.GetValueOrDefault(use.Faction)).OfType<ClassicFaction>())
                .Where(faction => !IsNamedPerson(faction) && faction.Id != 0).DistinctBy(faction => faction.Id).OrderBy(faction => faction.Name, StringComparer.Ordinal)];
            List<NpcArt> art = [sprites];
            Dictionary<string, string> facts = new();
            string kind = sprites.Frames.Count == 1 ? "Billboard" : "Animated billboard";
            string name = kind;
            if (SupplementalFlats.Contains(flat) && uses.Length == 0)
            {
                name = sprites.Frames.Count == 1 ? "Source-only figure" : "Source-only animated figure";
                kind = "Source-only figure";
                facts["World use"] = "Character art supplied in the original game files with no fixed placement found.";
            }
            if (captions.TryGetValue(flat, out FlatCaption? caption))
            {
                facts["Source caption"] = caption.Caption;
                facts["Appearance"] = caption.Gender;
                if (caption.Face >= 0 && caption.Face != PlaceholderFace && portraits.Get(CommonFaces, caption.Face) is { } face)
                    art.Add(new("Flat’s dialogue portrait", [face]));
            }
            if (genericFactions.Length > 0) facts["Factions using this sprite"] = string.Join(", ", genericFactions.Select(faction => faction.Name));
            if (uses.Length == 0 && !facts.ContainsKey("World use")) facts["World placements"] = "No fixed block placements found for this sprite.";

            // List the art separately if it represents a generic use as well as a known person.
            bool show = named.Length == 0 || genericFactions.Length > 0 || uses.Any(use => named.All(faction => faction.Id != use.Faction));
            entries.Add(new(flat.Id, caption?.Caption ?? (named.Length == 1 ? named[0].Name : name), kind, art, uses, facts,
                [.. named.Select(faction => new NpcLink(faction.Name, $"../{FactionId(faction)}/index.html"))],
                [$"{flat.Archive:000}-{flat.Record}"], show));
        }

        var characterLinks = ReadCharacterLinks(characters);
        foreach (var source in DirectionalSpriteReferenceExport.Sources()
            .Where(source => source.Family is "townspeople" or "guard" || source.Archive is 473 or >= 491 and <= 495)
            .OrderBy(source => source.Archive))
        {
            int archive = source.Archive;
            string id = $"npc-{archive:000}", alias = $"mobile-{archive:000}";
            List<NpcArt> art = ReadDirectional(export, source);
            Dictionary<string, string> facts = new() { ["Reference alias"] = alias };
            string kind;
            switch (source.Family)
            {
                case "townspeople":
                    kind = "Wandering townsfolk";
                    facts["Display appearance"] = $"{source.DisplayRace} · {source.Gender} · outfit {source.Outfit}";
                    facts["World use"] = "Procedurally populated streets. Name and dialogue portrait vary by instance.";
                    int start = FaceStarts[archive];
                    art.Add(new("Dialogue portrait variants", [.. portraits.Range(CommonFaces, start, 24)]));
                    break;
                case "guard":
                    kind = "Wandering guard";
                    facts["World use"] = "Civilian city watch patrol variant. Dialogue portraits come from the corresponding male outfit 1 face banks.";
                    art.Add(new("Dialogue portrait variants", [.. new[] { 385, 387, 381 }.SelectMany(outfit =>
                        portraits.Range(CommonFaces, FaceStarts[outfit], 24))]));
                    break;
                case "supplemental" when archive == 473:
                    kind = "Named NPC";
                    facts["World use"] = "King Lysandus’s spectre in the main quest. Lysandus’ Revenge names his tomb and Woodborne Hall.";
                    break;
                default:
                    kind = "Source-only figure";
                    facts["World use"] = "Mounted directional character art. No NPC placement is established.";
                    break;
            }
            string[] matching = [.. characterLinks.Keys.Where(key => key == $"{archive:000}" || key.StartsWith($"{archive:000}-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
            var links = matching.Select(key => new NpcLink($"Asset-pipeline {key}",
                Path.GetRelativePath(Path.Combine(export.Root, id), characterLinks[key]).Replace('\\', '/')));
            entries.Add(new(id, source.Name, kind, art, [], facts, [.. links], [alias, .. matching]));
        }

        foreach (ReferenceImage face in portraits.Range(SpecialFaces))
            if (!namedSpecialFaces.Contains(face.Record))
                entries.Add(new($"npc-face-{face.Record:000}", "Special dialogue portrait", "Portrait only", [new("Dialogue portrait", [face])],
                    [], new Dictionary<string, string> { ["World use"] = "No named character mapping in the supplied faction records." }, [], []));

        foreach (ReferenceImage face in portraits.Range(ChildFaces))
            entries.Add(new($"npc-child-{face.Record:000}", (face.Record % 2 == 0 ? "Boy" : "Girl") + " escort", "Child portrait",
                [new("Escort portrait", [face])], [],
                new Dictionary<string, string> { ["World use"] = "One of four portraits for child escorts. The quest supplies the identity." }, [], []));

        foreach (ReferenceImage face in portraits.Range(ChildPortraits))
            entries.Add(new($"npc-child-face-{face.Record:000}", "Child dialogue portrait", "Child portrait", [new("Portrait", [face])],
                [], new Dictionary<string, string> { ["World use"] = "Source portrait. No fixed identity or placement established." }, [], []));

        NpcReferencePages.Write(export, world, factions, entries);
    }

    private static bool IsNamedPerson(ClassicFaction faction) => faction.Id > 0 && faction.Type is 0 or 4;
    private static string FactionId(ClassicFaction faction) => $"npc-faction-{faction.Id:000}";
    private static bool IsPerson(NpcFlat flat) => flat.Archive > 0 && flat.Archive != RdbSourceClassification.EditorFlatArchive && !Props.Contains(flat);

    private static Dictionary<NpcFlat, FlatCaption> ReadCaptions(ReferenceExport export)
    {
        Dictionary<NpcFlat, FlatCaption> captions = [];
        string text = Encoding.UTF8.GetString(export.Read("FLATS.CFG")).Replace("\r", "", StringComparison.Ordinal);
        foreach (string block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] rows = block.Split('\n', StringSplitOptions.TrimEntries);
            string[] pair = rows[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length < 6 || pair.Length != 2 || !int.TryParse(pair[0], out int archive) ||
                !int.TryParse(pair[1], out int record) || !int.TryParse(rows[5], out int face))
            {
                export.Warn($"Skipped malformed FLATS.CFG section '{rows[0]}'.");
                continue;
            }
            captions.TryAdd(new(archive, record), new(rows[1], rows[2].Contains('2') ? "Female" : "Male", face));
        }
        return captions;
    }

    private static Dictionary<string, string> ReadCharacterLinks(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory).Where(path => File.Exists(Path.Combine(path, "index.html")))
                .ToDictionary(path => Path.GetFileName(path), path => Path.GetFullPath(Path.Combine(path, "index.html")), StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

    private static List<NpcArt> ReadDirectional(ReferenceExport export, DirectionalSpriteReferenceExport.Source source)
    {
        TextureArchive texture = export.Texture(source.Archive);
        List<NpcArt> art = [];
        for (int record = 0; record < texture.RecordCount; record++)
        {
            string label = source.Family != "supplemental" && record < 5
                ? $"Walk · {DirectionalSpriteReferenceExport.Directions[record]}"
                : $"Source record {record}";
            if ((source.Family == "townspeople" && record == 5) || (source.Family == "guard" && record == 15)) label = "Idle · front";
            if (source.Archive == 473) label = $"Spectral sprite · record {record}";
            List<ReferenceImage> frames = [];
            if (Arena2SpectralTextures.IsSpectralArchive((ushort)source.Archive))
            {
                TextureRecordInfo info = texture.GetRecordInfo(record);
                for (int frame = 0; frame < info.FrameCount; frame++)
                {
                    IndexedTextureFrame decoded = texture.DecodeFrame(record, frame);
                    frames.Add(export.Rgba("art", $"sprite/texture-{source.Archive:000}-record-{record:000}-frame-{frame:000}-source.png",
                        "sprite", texture.Source, record, frame, decoded.Width, decoded.Height, info.XOffset, info.YOffset,
                        Arena2SpectralTextures.ToRgbaBytes(decoded.Pixels.Span, export.Palette, texture.Source), ["source"]));
                }
            }
            else frames = export.TextureImages("art", "sprite", source.Archive, record);
            if (frames.Count > 0) art.Add(new(label, frames));
        }
        return art;
    }

    private sealed class PortraitBank(ReferenceExport export)
    {
        private readonly Dictionary<string, byte[]> raw = new(StringComparer.Ordinal);
        private readonly Dictionary<(string File, int Record), ReferenceImage> images = [];
        private IReadOnlyList<IndexedImg>? children;

        public IEnumerable<ReferenceImage> Range(string file, int start = 0, int? count = null)
        {
            int length = count ?? Count(file);
            for (int index = start; index < start + length; index++)
                if (Get(file, index) is { } image) yield return image;
        }

        public ReferenceImage? Get(string file, int record)
        {
            if (images.TryGetValue((file, record), out ReferenceImage? image)) return image;
            if (record < 0 || record >= Count(file))
            {
                export.Warn($"{file} portrait {record} is absent.");
                return null;
            }

            IndexedImg? child = file == ChildFaces ? Children[record] : null;
            int width = child?.Width ?? PortraitSide, height = child?.Height ?? PortraitSide;
            ReadOnlySpan<byte> pixels = child is not null ? child.Pixels.Span : Raw(file).AsSpan(record * PortraitCellBytes, PortraitCellBytes);
            image = export.Rgba("art", $"portrait/{file.ToLowerInvariant()}-{record:000}.png", "portrait", $"arena2/{file}", record, 0,
                width, height, child?.XOffset ?? 0, child?.YOffset ?? 0,
                export.Palette.ToRgbaBytes(pixels, PaletteAlphaMode.IndexZeroTransparent), ["source"]);
            images.Add((file, record), image);
            return image;
        }

        private int Count(string file) => file == ChildFaces ? Children.Count : Raw(file).Length / PortraitCellBytes;
        private IReadOnlyList<IndexedImg> Children => children ??= ImgDecoder.DecodeRecordSequence(Raw(ChildFaces), $"arena2/{ChildFaces}");
        private byte[] Raw(string file)
        {
            if (!raw.TryGetValue(file, out byte[]? bytes)) raw[file] = bytes = export.Read(file);
            return bytes;
        }
    }
}
